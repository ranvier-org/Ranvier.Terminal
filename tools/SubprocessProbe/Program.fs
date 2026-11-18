module SubprocessProbe

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text
open System.Threading
open System.Threading.Tasks
open Ranvier.Terminal

let require condition message = if not condition then failwith message
let text bytes = Encoding.UTF8.GetString(bytes: byte[])
let private ordinaryChild (args: string[]) =
    match args[0] with
    | "arguments" ->
        for value in args[1..] do Console.WriteLine("ARG:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value: string)))
        Console.WriteLine("ENV:" + Environment.GetEnvironmentVariable "RANVIER_SUBPROCESS_PROBE")
        Console.WriteLine("CWD:" + Environment.CurrentDirectory)
        Console.Error.WriteLine "ERR:separate"
        23
    | "interactive" ->
        Console.WriteLine("TTY:" + string (not Console.IsOutputRedirected))
        Console.WriteLine "READY"
        let line = Console.ReadLine()
        Console.WriteLine("GOT:" + line)
        if not Console.IsOutputRedirected then Console.WriteLine(sprintf "SIZE:%d,%d" Console.WindowWidth Console.WindowHeight)
        0
    | "tiny" ->
        use output = Console.OpenStandardOutput()
        let bytes = Encoding.UTF8.GetBytes "α界"
        output.Write(bytes, 0, bytes.Length)
        0
    | "wait-input" ->
        Console.WriteLine(Console.ReadLine())
        0
    | "spawn" ->
        let start = ProcessStartInfo(Environment.ProcessPath, UseShellExecute = false, CreateNoWindow = true)
        if Path.GetFileNameWithoutExtension(Environment.ProcessPath) = "dotnet" then start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location)
        for argument in [ "--child"; "linger"; args[1] ] do start.ArgumentList.Add argument
        use descendant = Process.Start start
        0
    | "linger" ->
        PosixChecks.writePidFile args[1]
        Thread.Sleep 30000
        0
    | "flood" ->
        let bytes = Array.create 4096 (byte 'x')
        use output = Console.OpenStandardOutput()
        for _ = 1 to 64 do output.Write(bytes, 0, bytes.Length)
        output.Flush()
        Console.WriteLine "END-FLOOD"
        0
    | "blocked" ->
        let bytes = Array.create 4096 (byte 'x')
        use output = Console.OpenStandardOutput()
        while true do output.Write(bytes, 0, bytes.Length)
        0
    | _ -> failwith "unknown child mode"

let child args =
    match PosixChecks.tryChild args with
    | Some code -> code
    | None -> ordinaryChild args

let options mode arguments =
    let executable = Environment.ProcessPath
    let prefix = if Path.GetFileNameWithoutExtension executable = "dotnet" then [ Assembly.GetExecutingAssembly().Location ] else []
    { Subprocess.command executable (prefix @ [ "--child" ] @ arguments) with
        Mode = mode
        OutputCapacity = 1024
        WorkingDirectory = Some(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
        Environment = [ "RANVIER_SUBPROCESS_PROBE", Some "configured value" ] }

let bounded (work: Task) = task {
    let! winner = Task.WhenAny(work, Task.Delay 10000)
    require (obj.ReferenceEquals(winner, work)) "subprocess operation timed out"
    do! work
}

let collect (session: SubprocessSession) = task {
    let chunks = ResizeArray<SubprocessChunk>()
    use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 15.)
    let mutable reading = true
    while reading do
        let! available = session.WaitToRead deadline.Token
        if available then chunks.AddRange(session.Drain 4096)
        else reading <- false
    let! code = session.Exit
    return code, chunks.ToArray()
}

let joined chunks = chunks |> Array.collect (fun chunk -> chunk.Bytes) |> text

let runMode log mode = task {
    match mode with
    | WindowsPseudoConsole(w, h) | PosixPseudoTerminal(w, h) ->
        require (Subprocess.tryPseudoTerminal w h = Some mode) "capability probe disagreed with native backend"
    | Redirected -> ()
    let values = [ ""; "two words"; "ends\\"; "quote\"inside"; "\\\\\""; "α界"; "tab\there" ]
    let config = options mode ("arguments" :: values)
    let session = Subprocess.start config CancellationToken.None
    try
        let! code, chunks = collect session
        let output = joined chunks
        require (code = 23) "exit code was not retained"
        for value in values do
            require (output.Contains("ARG:" + Convert.ToBase64String(Encoding.UTF8.GetBytes value))) (sprintf "argument quoting failed for %A in %A; captured %d bytes (base64): %s" value mode (chunks |> Array.sumBy (fun c -> c.Bytes.Length)) (chunks |> Array.collect (fun c -> c.Bytes) |> Convert.ToBase64String))
        require (output.Contains "ENV:configured value") "environment update"
        require (output.Contains("CWD:" + config.WorkingDirectory.Value)) "working directory"
        if mode = Redirected then
            require (chunks |> Array.exists (fun chunk -> chunk.Kind = StandardError)) "stderr channel missing"
        require (session.DroppedBytes() = 0L) "normal capture dropped bytes"
        do! bounded (session.Close())
        do! bounded (session.Close())
    finally session.Close() |> ignore
    log (sprintf "PASS %A structured arguments, cwd/env, tagged output, exit code and repeated close" mode)

    let interactive = Subprocess.start (options mode [ "interactive" ]) CancellationToken.None
    try
        let output = StringBuilder()
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
        while not (output.ToString().Contains "READY") do
            let! ready = interactive.WaitToRead deadline.Token
            require ready "EOF before READY"
            output.Append(joined (interactive.Drain 4096)) |> ignore
        require (output.ToString().Contains "READY") "interactive readiness"
        match mode with
        | WindowsPseudoConsole _ | PosixPseudoTerminal _ ->
            require (output.ToString().Contains "TTY:True") "child did not receive a real console"
            interactive.Resize 123 37
        | Redirected -> require (output.ToString().Contains "TTY:False") "redirected process incorrectly claims a terminal"
        do! bounded (interactive.Write (Encoding.UTF8.GetBytes(if mode = Redirected then "hello input\n" else "hello input\r")) CancellationToken.None)
        let! code, chunks = collect interactive
        output.Append(joined chunks) |> ignore
        require (code = 0 && output.ToString().Contains "GOT:hello input") "interactive input echo"
        match mode with WindowsPseudoConsole _ | PosixPseudoTerminal _ -> require (output.ToString().Contains "SIZE:123,37") "terminal resize" | _ -> ()
        do! bounded (interactive.Close())
    finally interactive.Close() |> ignore
    log (sprintf "PASS %A input and native terminal/resize semantics" mode)

    let flood = Subprocess.start (options mode [ "flood" ]) CancellationToken.None
    try
        let ready = flood.WaitToRead CancellationToken.None
        do! bounded ready
        require ready.Result "flood child exited without output"
        require (not flood.Exit.IsCompleted) "bounded full output failed to backpressure"
        let first = flood.Drain Int32.MaxValue
        require ((first |> Array.sumBy (fun chunk -> chunk.Bytes.Length)) <= 1024) "queue capacity exceeded"
        let! code, chunks = collect flood
        let output = joined (Array.append first chunks)
        require (code = 0 && output.Contains "END-FLOOD") "sustained output did not finish"
        require (flood.DroppedBytes() = 0L) "normal sustained output lost bytes"
        if mode = Redirected then require ((output |> Seq.filter ((=) 'x') |> Seq.length) = 262144) "redirected sustained byte loss"
        do! bounded (flood.Close())
    finally flood.Close() |> ignore
    log (sprintf "PASS %A bounded sustained output with draining" mode)

    let blocked = Subprocess.start (options mode [ "blocked" ]) CancellationToken.None
    let queued = blocked.WaitToRead CancellationToken.None
    do! bounded queued
    require queued.Result "blocked child exited without output"
    let write = blocked.Write (Array.create 1048576 (byte 'a')) CancellationToken.None
    do! Task.Delay 30
    do! bounded (blocked.Close())
    let! completed = Task.WhenAny(write, Task.Delay 10000)
    require (obj.ReferenceEquals(completed, write)) "input writer stranded during close"
    try do! write
    with
    | :? IOException | :? ObjectDisposedException | :? OperationCanceledException -> ()
    require ((blocked.Drain 1).Length = 1) "close lost previously queued output"
    do! bounded (blocked.Close())
    log (sprintf "PASS %A concurrent blocked input/output close without caller drain" mode)

    use cancellation = new CancellationTokenSource()
    let cancelled = Subprocess.start (options mode [ "blocked" ]) cancellation.Token
    let producing = cancelled.WaitToRead CancellationToken.None
    do! bounded producing
    require producing.Result "child exited before cancellation"
    cancellation.Cancel()
    do! bounded (cancelled.Close())
    log (sprintf "PASS %A cancellation cleanup without caller drain" mode)

    let mutable failed = false
    try
        Subprocess.start { options mode [] with FileName = "ranvier-no-such-executable-probe.exe" } CancellationToken.None |> ignore
    with :? System.ComponentModel.Win32Exception -> failed <- true
    require failed "nonexistent executable did not fail"
    log (sprintf "PASS %A nonexistent executable cleanup" mode)

    if mode = Redirected then
        let quiet = Subprocess.start (options mode [ "wait-input" ]) CancellationToken.None
        try
            use cancelledWait = new CancellationTokenSource()
            let first = quiet.WaitToRead cancelledWait.Token
            let second = quiet.WaitToRead CancellationToken.None
            require (not first.IsCompleted && not second.IsCompleted) "empty open pipe must suspend readers"
            cancelledWait.Cancel()
            try
                let! _ = first
                failwith "cancelled output wait completed successfully"
            with :? OperationCanceledException -> ()
            require (not second.IsCompleted && not quiet.Exit.IsCompleted) "wait cancellation stopped another reader or child"
            do! bounded (quiet.Write (Encoding.UTF8.GetBytes "wake\n") CancellationToken.None)
            do! bounded second
            require second.Result "new output reported EOF"
            let! code, chunks = collect quiet
            require (code = 0 && (joined chunks).Contains "wake") "wait consumed output"
            let! more = quiet.WaitToRead CancellationToken.None
            require (not more) "empty closed pipe did not persist EOF"
        finally quiet.Close() |> ignore
        let noOutput = Subprocess.start (options mode [ "wait-input" ]) CancellationToken.None
        let waiting = noOutput.WaitToRead CancellationToken.None
        do! bounded (noOutput.Close())
        do! bounded waiting
        require (not waiting.Result) "close stranded a pending output waiter"
        log "PASS concurrent output waiters, cancellation, readiness, close and EOF"
        let tiny = Subprocess.start { options mode [ "tiny" ] with OutputCapacity = 1 } CancellationToken.None
        let! code, chunks = collect tiny
        require (code = 0 && joined chunks = "α界") "single-byte queue broke UTF8 byte preservation"
        require (chunks |> Array.forall (fun chunk -> chunk.Bytes.Length = 1)) "single-byte capacity"
        do! bounded (tiny.Close())
        log "PASS Redirected single-byte queue preserves split UTF8"
        let pidFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "grandchild-" + Guid.NewGuid().ToString("N") + ".pid")
        let inherited = Subprocess.start (options mode [ "spawn"; pidFile ]) CancellationToken.None
        let mutable descendant: Process = null
        try
            let timer = Stopwatch.StartNew()
            while not (File.Exists pidFile) && timer.Elapsed.TotalSeconds < 5. do do! Task.Delay 10
            require (File.Exists pidFile) "descendant PID handshake"
            descendant <- Process.GetProcessById(Int32.Parse(File.ReadAllText pidFile))
            descendant.SafeHandle |> ignore
            do! Task.Delay 200
            require (not inherited.Exit.IsCompleted) "fixture descendant did not retain output pipe"
            do! bounded (inherited.Close())
            require (inherited.Exit.Result = 0) "parent exit code changed on post-exit close"
            do! bounded (inherited.Close())
            log "PASS Redirected close after parent exit unblocks descendant-held pipes"
        finally
            inherited.Close() |> ignore
            if not (isNull descendant) then
                if not descendant.HasExited then descendant.Kill(true)
                descendant.Dispose()
            if File.Exists pidFile then File.Delete pidFile
}

[<EntryPoint>]
let main args =
    if args.Length > 0 && args[0] = "--child" then child args[1..]
    elif args.Length > 0 && args[0] = "--verify-ignored-sigchld" then
        PosixChecks.verifyIgnoredSignal()
        0
    elif args.Length > 0 && args[0] = "--verify-denied-signal" then
        PosixChecks.verifyDeniedSignal().GetAwaiter().GetResult()
        0
    elif args.Length > 0 && args[0] = "--verify-pid-one" then
        require (Environment.ProcessId = 1) "PID-one rejection fixture must be the container entry point"
        require (Subprocess.tryPseudoTerminal 80 24 = None) "capability probe selected a PTY for PID one"
        try
            let session = Subprocess.start { Subprocess.command "/bin/true" [] with Mode = PosixPseudoTerminal(80,24) } CancellationToken.None
            session.Close().GetAwaiter().GetResult()
            failwith "PID-one host was accepted"
        with :? PlatformNotSupportedException ->
            Console.WriteLine "PASS POSIX PID-one ownership rejection"
            0
    else
        use report = new StreamWriter(args[0], false)
        report.AutoFlush <- true
        try
            runMode report.WriteLine Redirected |> fun work -> work.GetAwaiter().GetResult()
            if OperatingSystem.IsWindows() then
                runMode report.WriteLine (WindowsPseudoConsole(240, 40)) |> fun work -> work.GetAwaiter().GetResult()
            elif OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
                if OperatingSystem.IsMacOS() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.Arm64 then
                    let mutable rejected = false
                    try Subprocess.start { Subprocess.command "/bin/true" [] with Mode = PosixPseudoTerminal(80,24) } CancellationToken.None |> ignore
                    with :? PlatformNotSupportedException -> rejected <- true
                    require rejected "Unverified Darwin ARM64 ABI was accepted"
                    report.WriteLine "PASS macOS ARM64 explicitly rejects unsupported POSIX PTY ABI; native PTY lifecycle not run"
                else
                    runMode report.WriteLine (PosixPseudoTerminal(240, 40)) |> fun work -> work.GetAwaiter().GetResult()
                    PosixChecks.run report.WriteLine |> fun work -> work.GetAwaiter().GetResult()
            report.WriteLine "PASS subprocess transport probe"
            0
        with error -> report.WriteLine(string error); 1
