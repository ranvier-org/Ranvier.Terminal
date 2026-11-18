module HostProbe

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

[<DllImport("kernel32.dll")>]
extern nativeint GetStdHandle(int handle)
[<DllImport("kernel32.dll")>]
extern bool GetConsoleMode(nativeint handle, uint32& mode)
[<DllImport("kernel32.dll")>]
extern uint32 GetConsoleOutputCP()
[<DllImport("kernel32.dll")>]
extern bool FlushConsoleInputBuffer(nativeint handle)
[<DllImport("kernel32.dll")>]
extern bool WriteConsoleInputW(nativeint handle, nativeint buffer, uint32 length, uint32& written)
[<DllImport("kernel32.dll", CharSet = CharSet.Unicode)>]
extern bool ReadConsoleOutputCharacterW(nativeint handle, nativeint text, uint32 length, uint32 coordinate, uint32& read)

let options = { Terminal.defaults with AlternateScreen = false }
let require condition message = if not condition then failwith message
let modes () =
    let mutable input, output = 0u, 0u
    require (GetConsoleMode(GetStdHandle -10, &input)) "input mode"
    require (GetConsoleMode(GetStdHandle -11, &output)) "output mode"
    input, output, GetConsoleOutputCP()

// Frozen old host policy: poll, tick, pump, reactive present, then sleep the remaining frame.
let baseline (cancellation: CancellationToken) build tick =
    use graph = new Graph()
    let mounted = View.mount graph build
    try
        let session = Terminal.openSession options
        try
            let width, height = max 1 Console.WindowWidth, max 1 Console.WindowHeight
            let renderer = Renderer.create width height (width * height * options.OutputCapacityPerCell + 64) options.Capabilities
            let timer = Stopwatch.StartNew()
            let mutable previous = timer.Elapsed
            while not cancellation.IsCancellationRequested do
                let start = timer.Elapsed
                session.Input.Poll (fun event -> View.dispatch event mounted |> ignore)
                graph.Run(fun () ->
                    let now = timer.Elapsed
                    tick (now - previous) |> ignore
                    previous <- now)
                graph.Pump() |> ignore
                Layout.presentReactive session.Output mounted renderer |> ignore
                let remaining = options.FrameInterval - (timer.Elapsed - start)
                if remaining > TimeSpan.Zero then Thread.Sleep remaining
        finally session.Close()
    finally View.dispose mounted

let eventHost token build tick = Terminal.runEventDriven options token build tick
let liveHost token build tick = Terminal.runReactive options token build (fun delta -> tick delta |> ignore)
let conservativeHost token build tick = Terminal.run options token build (fun delta -> tick delta |> ignore)

let withWorker host build tick work =
    let original = modes ()
    FlushConsoleInputBuffer(GetStdHandle -10) |> ignore
    use stop = new CancellationTokenSource(TimeSpan.FromSeconds 15.)
    use ready = new ManualResetEventSlim(false)
    let mutable workerError = None
    let worker = new Thread(ThreadStart(fun () ->
        try
            try
                require (ready.Wait(TimeSpan.FromSeconds 5.)) "first tick timeout"
                work stop
            with error -> workerError <- Some error
        finally stop.Cancel()))
    worker.IsBackground <- true
    worker.Start()
    try host stop.Token build (fun delta -> let result = tick delta in ready.Set(); result)
    finally
        stop.Cancel()
        require (worker.Join 5000) "worker did not exit"
        require (modes () = original) "terminal modes/code page not restored"
    match workerError with Some error -> raise error | None -> ()

let inject (kind: int16) (character: char) (repeats: int16) =
    let record = Marshal.AllocHGlobal 20
    try
        for i = 0 to 19 do Marshal.WriteByte(record, i, 0uy)
        Marshal.WriteInt16(record, 0, kind)
        if kind = 1s then
            Marshal.WriteInt32(record, 4, 1)
            Marshal.WriteInt16(record, 8, repeats)
            Marshal.WriteInt16(record, 10, int16 character)
            Marshal.WriteInt16(record, 14, int16 character)
            if character = '\003' then Marshal.WriteInt32(record, 16, 8)
        else
            Marshal.WriteInt16(record, 4, 80s)
            Marshal.WriteInt16(record, 6, 24s)
        let mutable written = 0u
        require (WriteConsoleInputW(GetStdHandle -10, record, 1u, &written) && written = 1u) "inject input"
    finally Marshal.FreeHGlobal record

let waitUntil predicate =
    let timer = Stopwatch.StartNew()
    while not (predicate ()) && timer.Elapsed.TotalSeconds < 3. do Thread.Yield() |> ignore
    require (predicate ()) "event/output deadline"

let idle log name host =
    let mutable ticks = 0
    withWorker host (fun () -> label "idle") (fun _ -> Interlocked.Increment(&ticks) |> ignore; None) (fun _ ->
        Thread.Sleep 250
        let beforeTicks = Volatile.Read(&ticks)
        use currentProcess = Process.GetCurrentProcess()
        let cpu = currentProcess.TotalProcessorTime
        let timer = Stopwatch.StartNew()
        Thread.Sleep 2000
        let elapsed = timer.Elapsed.TotalMilliseconds
        let cpuMs = (currentProcess.TotalProcessorTime - cpu).TotalMilliseconds
        log (sprintf "idle,%s,elapsed_ms=%.3f,cpu_ms=%.3f,ticks=%d" name elapsed cpuMs (Volatile.Read(&ticks) - beforeTicks)))

let latency log name host =
    let mutable source = Unchecked.defaultof<Signal<string>>
    let build () =
        source <- createSignal "!"
        signalText source |> onEvent (function
            | Key key when not key.Released -> source.Value <- string (char key.Scalar); true
            | _ -> false)
    withWorker host build (fun _ -> None) (fun _ ->
        Thread.Sleep 200
        let buffer = Marshal.AllocHGlobal 2
        try
            for sample = 0 to 39 do
                Thread.Sleep (13 + sample % 11)
                let expected = char (int 'A' + sample % 26)
                let timer = Stopwatch.StartNew()
                inject 1s expected 1s
                waitUntil (fun () ->
                    let mutable count = 0u
                    ReadConsoleOutputCharacterW(GetStdHandle -11, buffer, 1u, 0u, &count)
                    && count = 1u && char (Marshal.ReadInt16 buffer) = expected)
                log (sprintf "input_readback,%s,sample=%d,ms=%.3f" name sample timer.Elapsed.TotalMilliseconds)
        finally Marshal.FreeHGlobal buffer)

let edges log =
    let initialWidth, initialHeight = Console.WindowWidth, Console.WindowHeight
    let targetWidth = if initialWidth > 2 then initialWidth - 1 else initialWidth + 1
    let mutable observed = 0
    let mutable cleaned = false
    let viewportHost token ready update =
        Terminal.runApp options token (fun viewport ->
            require (viewport () = struct (initialWidth, initialHeight)) "initial viewport"
            onCleanup (fun () -> cleaned <- true)
            let view = ready ()
            createEffect (fun () ->
                let struct (width, _) = viewport ()
                Volatile.Write(&observed, width))
            view, update)
    try
        withWorker viewportHost (fun () -> label "resize") (fun _ -> None) (fun _ ->
            Console.SetWindowSize(targetWidth, initialHeight)
            inject 4s '\000' 1s
            waitUntil (fun () -> Volatile.Read(&observed) = targetWidth))
    finally Console.SetWindowSize(initialWidth, initialHeight)
    require cleaned "owned setup cleanup"
    log "PASS owned setup observes the real Windows window at mount and resize; binding and cleanup run"
    let mutable graph = Unchecked.defaultof<Graph>
    let mutable source = Unchecked.defaultof<Signal<string>>
    let mutable keys, resized, escaped, posts = 0, 0, 0, 0
    let build () =
        graph <- match Graph.TryCurrent with ValueSome value -> value | _ -> failwith "graph ownership"
        source <- createSignal "start"
        signalText source |> onEvent (function
            | Key key when key.Key = Key.Escape -> escaped <- escaped + 1; true
            | Key _ -> keys <- keys + 1; source.Value <- string keys; true
            | Resize _ -> resized <- resized + 1; true
            | _ -> false)
    withWorker eventHost build (fun _ -> None) (fun _ ->
        Thread.Sleep 100
        for _ = 1 to 1000 do
            graph.Dispatch(fun () -> require graph.IsOnGraphThread "post thread"; posts <- posts + 1; source.Value <- string posts)
        waitUntil (fun () -> Volatile.Read(&posts) = 1000)
        Thread.Sleep 100
        graph.Dispatch(fun () -> require graph.IsOnGraphThread "second post thread"; posts <- posts + 1; source.Value <- "awake")
        waitUntil (fun () -> Volatile.Read(&posts) = 1001)
        inject 1s 'x' 300s
        waitUntil (fun () -> Volatile.Read(&keys) = 300)
        // More records than Native.Poll's bounded batch; readiness must stay signalled.
        for _ = 1 to 300 do inject 1s 'y' 1s
        waitUntil (fun () -> Volatile.Read(&keys) = 600)
        inject 4s '\000' 1s
        waitUntil (fun () -> Volatile.Read(&resized) > 0)
        inject 1s '\027' 1s
        waitUntil (fun () -> Volatile.Read(&escaped) = 1))
    log "PASS graph burst ownership and second post after sleep, repeated keys, input beyond batch, resize, Escape, cancellation, cleanup"
    let mutable ticks = 0
    withWorker eventHost (fun () -> label "animation") (fun _ ->
        ticks <- ticks + 1
        if ticks < 5 then Some(TimeSpan.FromMilliseconds 20.) else None) (fun _ ->
        waitUntil (fun () -> Volatile.Read(&ticks) >= 5)
        Thread.Sleep 100
        require (Volatile.Read(&ticks) = 5) "animation did not suspend")
    log "PASS animation deadlines then suspend"
    for host in [ liveHost; conservativeHost ] do
        withWorker host (fun () -> label "live") (fun _ -> Interlocked.Increment(&ticks) |> ignore; None) (fun _ ->
            let before = Volatile.Read(&ticks)
            Thread.Sleep 120
            require (Volatile.Read(&ticks) > before) "legacy live tick stopped")
    log "PASS legacy run and runReactive stay live"
    let mutable returned = false
    let controlHost token build tick =
        eventHost token build tick
        Volatile.Write(&returned, true)
    withWorker controlHost (fun () -> label "control-c") (fun _ -> None) (fun _ ->
        Thread.Sleep 30
        inject 1s '\003' 1s
        waitUntil (fun () -> Volatile.Read(&returned)))
    log "PASS native Ctrl+C exits sleeping host without token cancellation"
    let mutable consumed = false
    let mutable exited = false
    let consumedHost token build tick =
        eventHost token build tick
        Volatile.Write(&exited, true)
    let consumeBuild () =
        label "consume control-c" |> focusable |> onEvent (function
            | Key key when key.Scalar = int 'c' && key.Modifiers &&& Modifiers.Control <> Modifiers.None ->
                Volatile.Write(&consumed, true)
                true
            | _ -> false)
    withWorker consumedHost consumeBuild (fun _ -> None) (fun _ ->
        Thread.Sleep 30
        inject 1s '\009' 1s
        inject 1s '\003' 1s
        waitUntil (fun () -> Volatile.Read(&consumed))
        Thread.Sleep 30
        require (not (Volatile.Read(&exited))) "consumed Ctrl+C exited host")
    log "PASS focused widget consumes native Ctrl+C and keeps host alive"
    for failBuild in [ false; true ] do
        let original = modes ()
        let mutable caught = false
        try
            eventHost CancellationToken.None (fun () -> if failBuild then failwith "expected" else label "exception") (fun _ -> failwith "expected")
        with error when error.Message = "expected" -> caught <- true
        require caught "exception propagation"
        require (modes () = original) "exception cleanup"
    use cancelled = new CancellationTokenSource()
    cancelled.Cancel()
    eventHost cancelled.Token (fun () -> label "cancelled") (fun _ -> failwith "cancelled host ticked")
    log "PASS build/tick exceptions and pre-cancelled cleanup"
    for delay in [ TimeSpan.Zero; TimeSpan.FromMilliseconds -1. ] do
        let original = modes ()
        let mutable caught = false
        try eventHost CancellationToken.None (fun () -> label "invalid") (fun _ -> Some delay)
        with :? ArgumentException -> caught <- true
        require caught "invalid animation deadline accepted"
        require (modes () = original) "invalid deadline cleanup"
    withWorker eventHost (fun () -> label "long deadline") (fun _ -> Some TimeSpan.MaxValue) (fun _ -> Thread.Sleep 20)
    let timer = Stopwatch.StartNew()
    let mutable cancelAt = TimeSpan.Zero
    withWorker eventHost (fun () -> label "cancel race") (fun _ -> None) (fun stop ->
        Thread.Sleep 50
        cancelAt <- timer.Elapsed
        stop.Cancel())
    let latency = (timer.Elapsed - cancelAt).TotalMilliseconds
    require (latency < 1000.) "sleeping cancellation stalled"
    log (sprintf "PASS deadline validation/saturation; sleeping cancellation cleanup_ms=%.3f" latency)

// The PTY driver observes each frame before delivering the next native input.
let posixViewportProbe mouse =
    use stop = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
    let mutable cleaned = false
    let mutable hovered = false
    Terminal.runApp { options with Mouse = mouse } stop.Token (fun viewport ->
        onCleanup (fun () -> cleaned <- true)
        require (viewport () = struct (72, 16)) "setup received the wrong initial PTY viewport"
        let node = text (fun () ->
            let struct (width, height) = viewport ()
            sprintf "viewport=%dx%d" width height)
        node.OnHoverChanged <- fun inside -> if inside then hovered <- true
        let update _ =
            if hovered then stop.Cancel()
            None
        node, update)
    require cleaned "owned setup cleanup did not run"
    require (hovered = mouse) "unexpected hover delivery"
    0

let windowsProbe (args: string array) =
    use report = new StreamWriter(args[0], false)
    report.AutoFlush <- true
    let log (text: string) = report.WriteLine text
    try
        require (OperatingSystem.IsWindows()) "This native probe requires Windows."
        edges log
        if not (Array.contains "--edges" args) then
            for _ = 1 to 3 do
                idle log "poll" baseline
                idle log "event" eventHost
            latency log "poll" baseline
            latency log "event" eventHost
        log "PASS hidden native host probe"
        0
    with error -> log (string error); 1

[<EntryPoint>]
let main args =
    match args with
    | [| "--posix-hover" |] -> posixViewportProbe true
    | [| "--posix-mouse-off" |] -> posixViewportProbe false
    | _ -> windowsProbe args
