module Showcase.Program

open System
open System.Diagnostics
open System.IO
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let capabilities model =
    { Colors =
        match model.Palette.Peek with
        | 1 -> Palette256
        | 2 -> Monochrome
        | _ -> TrueColor
      SynchronizedOutput = model.Sync.Peek }

let rec find id (node: Node) =
    if node.Id = id then
        Some node
    else
        node.Children |> Array.tryPick (find id)

let press id mounted =
    match find id mounted.Root with
    | None -> failwith ("Missing control: " + id)
    | Some node ->
        View.focus (Some node) mounted

        View.dispatch
            (Key
                { Key = Key.Enter
                  Scalar = 0
                  Modifiers = Modifiers.None
                  Released = false
                  Paste = false })
            mounted
        |> ignore

let describe event =
    match event with
    | Key key ->
        sprintf
            "Key: %A  scalar: %X  modifiers: %A  %s"
            key.Key
            key.Scalar
            key.Modifiers
            (if key.Released then "released" else "pressed")
    | Mouse mouse -> sprintf "Mouse: %A at (%d,%d)  button: %d" mouse.Action mouse.X mouse.Y mouse.Button
    | Resize(struct (width, height)) -> sprintf "Resize: %d × %d cells" width height
    | Focus active ->
        if active then
            "Terminal gained focus"
        else
            "Terminal lost focus"
    | PasteBoundary active -> if active then "Paste started" else "Paste ended"
    | SynchronizedOutputSupport supported -> sprintf "Synchronization support response: %b" supported

let snapshot path (frame: Frame) =
    let lines =
        Array.init frame.Height (fun y ->
            let line = Text.StringBuilder()

            for x = 0 to frame.Width - 1 do
                let cell = frame.Cells[y * frame.Width + x]

                if cell.Width <> 0uy then
                    line.Append cell.Glyph |> ignore

            line.ToString().TrimEnd())

    File.WriteAllLines(path, lines)

let verify captureDirectory =
    use graph = new Graph()
    let model = graph.Run Model.create
    let mounted = View.mount graph (fun () -> Kit.build model)
    mounted.Rules <- Theme.rules
    let mutable frames = 0

    let output =
        { Write = (fun _ _ _ -> ())
          Flush = (fun () -> ()) }

    try
        for struct (width, height) in [| struct (80, 24); struct (120, 40); struct (160, 50) |] do
            let renderer =
                Renderer.create width height (width * height * 128 + 64) (capabilities model)

            for page = 0 to (Kit.pages ()).Length - 1 do
                model.Page.Value <- page

                for _ = 0 to 5 do
                    graph.Run(fun () -> Model.tick (1. / 30.) model)
                    Layout.render mounted renderer.Back
                    Renderer.present output renderer
                    frames <- frames + 1

                if width = 120 then
                    match captureDirectory with
                    | Some directory -> snapshot (Path.Combine(directory, sprintf "page-%d.txt" page)) renderer.Back
                    | None -> ()

        let render () =
            Layout.render mounted (Frame.create 120 40)

        model.Page.Value <- 1
        render ()
        press "increment" mounted

        if model.Count.Peek <> 1 then
            failwith "Increment action did not update its signal."

        press "decrement" mounted

        if model.Count.Peek <> 0 then
            failwith "Decrement action did not update its signal."

        let input = (find "name-input" mounted.Root).Value
        View.focus (Some input) mounted

        View.dispatch
            (Key
                { Key = Key.Character
                  Scalar = int '!'
                  Modifiers = Modifiers.None
                  Released = false
                  Paste = false })
            mounted
        |> ignore

        if model.Name.Peek <> "Ranvier!" then
            failwith "Text input did not update its bound signal."

        model.Page.Value <- 5
        render ()
        let cleaned = model.Cleaned
        press "reveal" mounted

        if model.Reveal.Peek || model.Cleaned <= cleaned then
            failwith "Branch cleanup did not run."

        let count = model.Items.Peek.Length
        press "add-item" mounted

        if model.Items.Peek.Length <> count + 1 then
            failwith "Collection did not grow."

        press "remove-item" mounted

        if model.Items.Peek.Length <> count then
            failwith "Collection did not shrink."

        model.Page.Value <- 6
        render ()
        press "mono" mounted

        if (capabilities model).Colors <> Monochrome then
            failwith "Output-mode control failed."

        press "invalidate" mounted

        if not model.Redraw then
            failwith "Full-redraw control failed."

        model.Page.Value <- 7
        render ()
        let scroll = (find "scroll-region" mounted.Root).Value
        View.focus (Some scroll) mounted

        View.dispatch
            (Key
                { Key = Key.PageDown
                  Scalar = 0
                  Modifiers = Modifiers.None
                  Released = false
                  Paste = false })
            mounted
        |> ignore

        if scroll.ScrollY <= 0 then
            failwith "Scrolling did not advance."

        View.dispatch
            (Key
                { Key = Key.F3
                  Scalar = 0
                  Modifiers = Modifiers.None
                  Released = false
                  Paste = false })
            mounted
        |> ignore

        if model.Page.Peek <> 2 then
            failwith "Page shortcut did not bubble to the root."

        printfn
            "measured here: showcase verification passed; %d rendered frames across all registered pages and viewport sizes; controls, editing, disposal, collection changes, modes, redraw, scrolling and shortcuts checked."
            frames
    finally
        View.dispose mounted

let run smoke report =
    Console.Title <- "Ranvier.Terminal — Feature Showcase"
    use graph = new Graph()
    let model = graph.Run Model.create

    let mount () =
        let view = View.mount graph (fun () -> Kit.build model)
        view.Rules <- Theme.rules
        view

    let mutable mounted = mount ()
    let session = Terminal.openSession Terminal.defaults

    try
        let mutable width, height = max 1 Console.WindowWidth, max 1 Console.WindowHeight

        let createRenderer () =
            Renderer.create width height (width * height * 128 + 64) (capabilities model)

        let mutable renderer = createRenderer ()
        let mutable revision = Kit.currentRevision ()
        let mutable lastCapabilities = capabilities model
        let timer = Stopwatch.StartNew()
        let mutable previous = timer.Elapsed.TotalSeconds
        let mutable reportAt = 0.
        let mutable announced = false

        let receive event =
            model.LastEvent.Value <- describe event

            match event with
            | Key key when
                not key.Released
                && not key.Paste
                && key.Scalar = int 'c'
                && key.Modifiers &&& Modifiers.Control <> Modifiers.None
                ->
                model.Exit <- true
            | _ -> View.dispatch event mounted |> ignore

        while not model.Exit && (not smoke || timer.Elapsed < TimeSpan.FromSeconds 3.) do
            let frameStart = Stopwatch.GetTimestamp()
            session.Input.Poll receive
            let now = timer.Elapsed.TotalSeconds

            if smoke then
                model.Page.Value <- min 7 (int (now / 0.35))

            graph.Run(fun () -> Model.tick (min 0.1 (now - previous)) model)
            previous <- now
            graph.Pump() |> ignore

            if Kit.currentRevision () <> revision then
                revision <- Kit.currentRevision ()
                View.dispose mounted
                mounted <- mount ()
                Renderer.invalidate renderer

            let nextWidth, nextHeight = max 1 Console.WindowWidth, max 1 Console.WindowHeight

            if width <> nextWidth || height <> nextHeight then
                width <- nextWidth
                height <- nextHeight
                Renderer.resize width height renderer
                Renderer.reserve (width * height * 128 + 64) renderer

            let currentCapabilities = capabilities model

            if currentCapabilities <> lastCapabilities then
                lastCapabilities <- currentCapabilities
                renderer <- createRenderer ()

            if model.Redraw then
                model.Redraw <- false
                Renderer.invalidate renderer

            model.Viewport.Value <- sprintf "%d × %d cells" width height
            let allocationStart = GC.GetAllocatedBytesForCurrentThread()
            let renderStart = Stopwatch.GetTimestamp()
            Layout.render mounted renderer.Back
            Renderer.present session.Output renderer
            let duration = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds
            let allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart

            if not announced then
                announced <- true

                match report with
                | Some path ->
                    File.WriteAllText(
                        path,
                        sprintf
                            "Running: PID %d; viewport %d x %d; first frame presented."
                            Environment.ProcessId
                            width
                            height
                    )
                | None -> ()

            if now >= reportAt then
                reportAt <- now + 0.25

                model.Metrics.Value <-
                    sprintf
                        "Measured here: %d changed cells · %d output bytes\nRender + output: %.3f ms · %d managed bytes\nSample excludes model updates; warmup may allocate."
                        renderer.EncodedCells
                        renderer.Count
                        duration
                        allocated

            let remaining = 33. - Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds

            if remaining > 0. then
                Thread.Sleep(int remaining)
    finally
        try
            session.Close()
        finally
            View.dispose mounted

[<EntryPoint>]
let main args =
    if Array.contains "--verify" args then
        let directory =
            args
            |> Array.tryPick (fun value ->
                if value.StartsWith "--capture=" then
                    Some(value.Substring 10)
                else
                    None)

        match directory with
        | Some path -> Directory.CreateDirectory path |> ignore
        | None -> ()

        verify directory
    else
        let report =
            args
            |> Array.tryPick (fun value ->
                if value.StartsWith "--report=" then
                    Some(value.Substring 9)
                else
                    None)

        try
            run (Array.contains "--smoke" args) report
        with ex ->
            match report with
            | Some path -> File.WriteAllText(path, ex.ToString())
            | None -> ()

            reraise ()

    0
