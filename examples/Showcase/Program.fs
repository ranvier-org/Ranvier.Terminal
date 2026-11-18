module Showcase.Program

open System
open System.Diagnostics
open System.IO
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let child () =
    Console.OutputEncoding <- Text.UTF8Encoding(false)
    Console.WriteLine "child ready — type hello, size or exit"
    Console.Out.Flush()
    let mutable running = true
    while running do
        match Console.ReadLine() with
        | null | "exit" -> running <- false
        | "size" -> Console.WriteLine(sprintf "terminal size: %d x %d" Console.WindowWidth Console.WindowHeight)
        | line -> Console.WriteLine("echo: " + line)
        Console.Out.Flush()
    Console.WriteLine "child goodbye"

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
    model.ProcessMode <- Redirected
    let mounted = View.mount graph (fun () -> Kit.build model)
    mounted.Rules <- Theme.rules
    let mutable frames = 0

    let output =
        { Write = (fun _ _ _ -> ())
          Flush = (fun () -> ()) }

    try
        for struct (width, height) in [| struct (80, 24); struct (120, 40); struct (160, 50) |] do
            model.Columns.Value <- width
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

        let mutable verifyWidth, verifyHeight = 120, 40
        let render () =
            model.Columns.Value <- verifyWidth
            Layout.render mounted (Frame.create verifyWidth verifyHeight)

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

        let send key released =
            View.dispatch
                (Key { Key = key; Scalar = 0; Modifiers = Modifiers.None
                       Released = released; Paste = false })
                mounted |> ignore

        for _ = 1 to 64 do send Key.Left false
        if input.CaretColumn <> 0 then failwith "Left repeat did not clamp at input start."
        for _ = 1 to 64 do send Key.Right false
        if input.CaretColumn <> model.Name.Peek.Length then
            failwith "Right repeat did not clamp at input end."
        if model.Name.Peek <> "Ranvier!" then failwith "Arrows modified input text."

        View.focus (find "reset" mounted.Root) mounted
        render ()
        let tip = (find "reset-tip" mounted.Root).Value
        if tip.Computed.Visible then failwith "Focusing Reset showed a tooltip without Alt."

        View.focus (find "planet-input" mounted.Root) mounted
        View.dispatch
            (Key { Key = Key.Character; Scalar = int 'm'; Modifiers = Modifiers.None
                   Released = false; Paste = false }) mounted |> ignore
        render ()
        let matches = (find "planet-matches" mounted.Root).Value
        if tip.Computed.Visible || matches.Clip.Height = 0 || matches.Children.Length <> 2 then
            failwith "The planet field did not replace the tooltip with prefix matches."
        let mars = matches.Children[1].Clip
        View.dispatch
            (Mouse { X = mars.X; Y = mars.Y; Button = 0; Action = MouseAction.Press
                     Delta = 0; Modifiers = Modifiers.None }) mounted |> ignore
        render ()
        if model.Planet.Peek <> "Mars" || matches.Computed.Visible then
            failwith "Clicking a planet suggestion did not commit and close it."

        press "mission-destination" mounted
        send Key.Down false
        send Key.Enter false
        if model.Destination.Peek <> Some "moon" then failwith "Destination selection did not commit."
        press "mission-payloads" mounted
        send Key.Down false
        send Key.Enter false
        send Key.Escape false
        if model.Payloads.Peek <> set [ "camera"; "radar" ] then failwith "Payload selection lost a checked item."

        View.focus (find "selection" mounted.Root) mounted
        for _ = 1 to 64 do send Key.Down false
        if model.Selected.Peek <> 3 then failwith "Down repeat escaped list bounds."
        send Key.Up true
        if model.Selected.Peek <> 3 then failwith "Key release changed selection."
        for _ = 1 to 64 do send Key.Up false
        if model.Selected.Peek <> 0 then failwith "Up repeat escaped list bounds."

        send Key.F3 false
        verifyWidth <- 80
        verifyHeight <- 24
        render ()
        let menuButton = (find "page-2" mounted.Root).Value
        let menu = menuButton.Parent.Value
        let body = menu.Parent.Value
        let pageViewport = body.Children[1]
        let menuOrigin = menuButton.Bounds
        let wheel x y delta =
            View.dispatch (Mouse { X = x; Y = y; Button = 0; Action = MouseAction.Scroll
                                   Delta = delta; Modifiers = Modifiers.None }) mounted |> ignore
            render ()
        for _ = 1 to 5 do
            wheel pageViewport.Bounds.X (pageViewport.Bounds.Y + 1) pageViewport.Bounds.Height
        if pageViewport.ScrollY = 0 || pageViewport.ScrollY <> pageViewport.MaxScrollY then
            failwith "Wheel input did not reach the end of the Layout page."
        if menuButton.Bounds <> menuOrigin || body.MaxScrollY <> 0 then
            failwithf "Scrolling past the Layout page moved its menu. Body scroll range: %d." body.MaxScrollY
        wheel menuOrigin.X menuOrigin.Y 1
        if menu.ScrollY <> 0 then failwith "The menu scrolled although its controls fit."
        verifyHeight <- 15
        render ()
        let pageOffset = pageViewport.ScrollY
        wheel menuButton.Bounds.X menuButton.Bounds.Y menu.MaxScrollY
        let exitButton = (find "exit" mounted.Root).Value
        if exitButton.Clip.Height = 0 || menu.ScrollY = 0 || pageViewport.ScrollY <> pageOffset then
            failwith "A short terminal must scroll the menu independently to reach Exit."
        verifyWidth <- 120
        verifyHeight <- 40

        for page = 0 to (Kit.pages ()).Length - 1 do
            model.Page.Value <- page
            render ()
            // Tab traverses the actual focus order; render after events to refresh parent links.
            for _ = 1 to 32 do
                send Key.Tab false
                for key in [| Key.Up; Key.Down; Key.Left; Key.Right |] do
                    for _ = 1 to 4 do
                        send key false
                        send key true
                    render ()
            if model.Exit then failwith "Directional navigation requested application exit."

        model.Page.Value <- 5
        render ()
        let cleaned = model.Cleaned
        press "reveal" mounted

        if model.Reveal.Peek || model.Cleaned <= cleaned then
            failwith "Branch cleanup did not run."

        let count = model.Items.Peek.Length
        let rows = (find "lifetime-rows" mounted.Root).Value
        let survivingRow = rows.Children[0]
        press "add-item" mounted

        if model.Items.Peek.Length <> count + 1 then
            failwith "Collection did not grow."

        press "remove-item" mounted

        if model.Items.Peek.Length <> count then
            failwith "Collection did not shrink."
        if not (obj.ReferenceEquals(survivingRow, rows.Children[0])) then
            failwith "Changing a sibling replaced the first keyed row."

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

        send Key.PageUp false
        render ()
        let mouse action x y =
            View.dispatch
                (Mouse { X = x; Y = y; Button = 0; Action = action; Delta = 0; Modifiers = Modifiers.None }) mounted
            |> ignore
            render ()
        let trackX, topY = scroll.Content.X + scroll.Content.Width, scroll.Content.Y
        if not scroll.VerticalScrollbar || scroll.ScrollY <> 0 then failwith "Log scrollbar was not at its start."
        mouse MouseAction.Press trackX topY
        mouse MouseAction.Move (trackX - 3) (topY + scroll.Content.Height + 2)
        mouse MouseAction.Release (trackX - 3) (topY + scroll.Content.Height + 2)
        if scroll.ScrollY <> scroll.MaxScrollY then failwith "Dragging outside the log did not reach its last row."
        mouse MouseAction.Press trackX topY
        mouse MouseAction.Release trackX topY
        if scroll.ScrollY <> 0 then failwith "Clicking the start of the log track did not jump to its first row."
        let initialRange = scroll.MaxScrollY
        press "log-add" mounted
        render ()
        if scroll.MaxScrollY <= initialRange then failwith "Adding log rows did not extend the scroll range."
        let longerRange = scroll.MaxScrollY
        press "log-taller" mounted
        render ()
        if scroll.MaxScrollY >= longerRange then failwith "Growing the log viewport did not reduce the scroll range."
        for _ = 1 to (model.LogRows.Peek + 3) / 4 do press "log-remove" mounted
        render ()
        if scroll.VerticalScrollbar || scroll.ScrollY <> 0 then failwith "A log that fits still has a scrollbar."

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

        send Key.F11 false
        render ()
        View.focus (find "instrument-table" mounted.Root) mounted
        send Key.Home false
        for _ = 1 to 8 do send Key.Down false
        render ()
        let struct (channel, first) = model.InstrumentPosition.Peek
        if channel <> 8 || first <= 0 then failwith "Instrument channel selection did not scroll its table."
        let running = model.Animate.Peek
        press "instrument-run" mounted
        if model.Animate.Peek = running then failwith "Simulation control did not toggle."
        press "instrument-run" mounted

        send Key.F12 false
        render ()
        let banner = (find "task-banner" mounted.Root).Value
        if banner.Text.Height <= 1 then failwith "The Tasks page did not display its FIGlet heading."
        verifyWidth <- Theme.sidebarWidth + Theme.pageGap + 2 * Theme.sidePadding + "Tasks".Length
        render ()
        if banner.Text.Source <> "Tasks" then failwith "The narrow Tasks page did not use its plain heading."
        verifyWidth <- 120
        render ()
        let character modifiers char =
            View.dispatch (Key { Key = Key.Character; Scalar = int char; Modifiers = modifiers
                                 Released = false; Paste = false }) mounted |> ignore
        let currentTask () = (find "task-name" mounted.Root).Value.Text.Source
        View.focus (find "task-table" mounted.Root) mounted
        send Key.Down false
        if currentTask () <> "Compile modules" then failwith "Task details did not follow the table selection."
        character Modifiers.None 's'
        character Modifiers.None 'r'
        graph.Run(fun () -> Model.tick 0.5 model)
        render ()
        if currentTask () <> "Compile modules" then failwith "Sorting or refreshing changed the selected task."
        View.focus (find "task-command" mounted.Root) mounted
        character Modifiers.None 'a'
        send Key.Down false
        send Key.Down false
        graph.Run(fun () -> Model.tick 0.5 model)
        render ()
        send Key.Enter false
        if model.TaskStop.Peek |> Option.map snd <> Some TaskStatus.Cancelled then
            failwith "A progress update changed the highlighted palette command before Enter."
        View.focus (find "task-table" mounted.Root) mounted
        character Modifiers.Alt 'b'
        character Modifiers.Alt 'x'
        if model.TaskStop.Peek |> Option.map snd <> Some TaskStatus.Cancelled then
            failwith "The task shortcut did not cancel the build."
        View.focus (find "task-command" mounted.Root) mounted
        for ch in "restart" do character Modifiers.None ch
        send Key.Enter false
        if model.TaskStop.Peek.IsSome then failwith "The command palette did not restart the build."

        model.Page.Value <- 8
        render ()
        let stream = model.Utility.Value
        press "stream-history" mounted
        graph.Run(fun () -> Model.tick 0.3 model)
        render ()
        if stream.Stream.Snapshot.Peek.HistoryLines = 0 then failwith "Streaming page did not retain history."
        View.focus (find "stream-output" mounted.Root) mounted
        send Key.PageUp false
        if stream.Stream.Offset.Peek = 0 then failwith "Streaming history navigation failed."
        press "stream-tail" mounted
        if stream.Stream.Offset.Peek <> 0 then failwith "Follow-live did not reset history."
        press "stream-clear" mounted
        graph.Run(fun () -> Model.tick 0.01 model)
        if stream.Stream.Snapshot.Peek.HistoryLines <> 0 then failwith "Clear did not clear history."

        model.Page.Value <- 9
        render ()
        let childModel = model.Utility.Value
        let until description condition =
            let timer = Stopwatch.StartNew()
            while not (condition()) && timer.Elapsed.TotalSeconds < 10. do
                graph.Run(fun () -> Model.tick 0.03 model)
                render ()
                Thread.Sleep 10
            if not (condition()) then failwith (description + ": " + childModel.Status.Peek)
        press "process-start" mounted
        until "Showcase child startup" (fun () -> childModel.Received.Contains "child ready")
        press "process-hello" mounted
        until "Showcase child input" (fun () -> childModel.Received.Contains "echo: hello")
        verifyWidth <- 100
        verifyHeight <- 32
        render ()
        graph.Run(fun () -> Model.tick 0.03 model)
        let expectedWidth, expectedHeight = childModel.Process.Width, childModel.Process.Height
        press "process-size" mounted
        match childModel.Mode with
        | Redirected -> ()
        | _ -> until "Showcase child resize" (fun () -> childModel.Received.Contains(sprintf "terminal size: %d x %d" expectedWidth expectedHeight))
        View.focus (find "process-output" mounted.Root) mounted
        View.dispatch (Key { Key=Key.Tab; Scalar=0; Modifiers=Modifiers.Control; Released=false; Paste=false }) mounted |> ignore
        send Key.F9 false
        if model.Page.Peek <> 8 then failwith "Ctrl+Tab did not release process focus for page shortcut."
        render ()
        let closed = childModel.Closing
        if not (closed.Wait 10000) then failwith "Leaving process page stranded its child."
        if not closed.IsCompletedSuccessfully then failwith "Leaving process page failed cleanup."
        model.Page.Value <- 9
        render ()
        let restarted = model.Utility.Value
        press "process-start" mounted
        let timer = Stopwatch.StartNew()
        while not (restarted.Received.Contains "child ready") && timer.Elapsed.TotalSeconds < 10. do
            graph.Run(fun () -> Model.tick 0.03 model)
            render ()
            Thread.Sleep 10
        if not (restarted.Received.Contains "child ready") then failwith "Process page could not restart after remount."
        press "process-stop" mounted
        if not (restarted.Closing.Wait 10000) then failwith "Stop control did not close child."

        let galleryIndex = Kit.pages () |> Array.findIndex (fun page -> page.Name = "Components")
        for width, height in [ 80, 24; 120, 40 ] do
            verifyWidth <- width
            verifyHeight <- height
            model.Page.Value <- 0
            render ()
            press ("page-" + string galleryIndex) mounted
            render ()
            let example id =
                find id mounted.Root |> Option.defaultWith (fun () -> failwith ("Missing gallery component: " + id))
            let indicators =
                [ "spinner-Braille"; "spinner-Line"; "spinner-Quadrants"; "spinner-BouncingBar"
                  "spinner-Points"; "spinner-Shading"; "spinner-Moon"; "pulse-Bounce"; "pulse-Sweep"; "pulse-BouncingSegment" ]
                |> List.map (fun name -> let node = example name in node, node.Text.Source)
            model.Animate.Value <- true
            graph.Run(fun () -> Model.tick 0.125 model)
            render ()
            for node, before in indicators do
                if node.Text.Source = before then failwith ("Gallery animation stayed still: " + node.Id)
            for sample in [ "meter-ThinLine"; "gallery-button"; "gallery-completion"; "gallery-select"; "gallery-list"; "gallery-navigation"
                            "gallery-charts"; "gallery-table"; "gallery-command"; "gallery-figlet-input"; "gallery-panel"
                            "scroll-Proportional"; "stream-output"; "process-output" ] do
                press "component-group" mounted
                send Key.Down false
                send Key.Enter false
                render ()
                let widget = example sample
                if sample = "meter-ThinLine" then
                    model.Phase.Value <- 0.375
                    render ()
                    let frame = Frame.create width height
                    Layout.render mounted frame
                    let cell x = frame.Cells[widget.Bounds.Y * width + widget.Bounds.X + x]
                    if (cell 5).Glyph <> "─" || (cell 6).Glyph <> "─" ||
                       (cell 5).Paint.Foreground <> Theme.accent || (cell 6).Paint.Foreground <> Theme.muted then
                        failwith "Thin meter must distinguish fill and track by color."
                    let blocks = (example "meter-fractional").Children
                    if blocks[6].Text.Source <> "▊" then failwith "Lost fractional fill precision."
                    for fraction, glyph in [ infinity, "█"; nan, " "; -1., " " ] do
                        model.Phase.Value <- fraction
                        if blocks |> Array.exists (fun cell -> cell.Text.Source <> glyph) then
                            failwith "Fractional meter did not clamp its value."
                    let gradient = (example "meter-gradient").Children
                    if gradient[0].Computed.Paint.Foreground = gradient[gradient.Length - 1].Computed.Paint.Foreground then
                        failwith "Gradient endpoints have the same color."
                elif sample = "gallery-table" then
                    let sortMark () = widget.Children[0].Children[0].Children[1].Text.Source
                    let initial = sortMark ()
                    View.focus (Some widget) mounted
                    View.dispatch (Key { Key = Key.Character; Scalar = int 'r'; Modifiers = Modifiers.None; Released = false; Paste = false }) mounted |> ignore
                    if sortMark () = initial then failwith "Gallery table failed to reverse its sort."
            model.Page.Value <- 0
            if model.Utility.IsSome then failwith "Leaving the gallery retained its subprocess model."

        printfn
            "measured here: showcase verification passed; %d rendered frames across all registered pages and viewport sizes; controls, editing, disposal, collection changes, modes, redraw, scrolling, shortcuts and repeated directional navigation checked."
            frames
    finally
        View.dispose mounted
        Model.finish model

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
        let mutable smokeSent = false
        let mutable smokeSizeSent = false
        let mutable smokePassed = false
        // Keep the child terminal mounted until the smoke exchange finishes.
        let smokePages = [| for index in 0 .. (Kit.pages()).Length - 1 do if index <> 9 then yield index
                            yield 9 |]

        let receive event =
            model.LastEvent.Value <- describe event

            let handled = View.dispatch event mounted
            match event with
            | Key key when
                not handled
                &&
                not key.Released
                && not key.Paste
                && key.Scalar = int 'c'
                && key.Modifiers &&& Modifiers.Control <> Modifiers.None
                ->
                model.Exit <- true
            | _ -> ()

        while not model.Exit && (not smoke || timer.Elapsed < TimeSpan.FromSeconds 13.) do
            let frameStart = Stopwatch.GetTimestamp()
            session.Input.Poll receive
            let now = timer.Elapsed.TotalSeconds

            if smoke then
                model.Page.Value <- smokePages[min (smokePages.Length - 1) (int (now / 0.35))]

            graph.Run(fun () -> Model.tick (min 0.1 (now - previous)) model)
            previous <- now
            graph.Pump() |> ignore

            if smoke && model.Page.Peek = 9 then
                model.Utility |> Option.iter (fun utility ->
                    if utility.Session.IsNone && not utility.Completed then Utilities.Model.start utility
                    if utility.Received.Contains "child ready" && not smokeSent then
                        Utilities.Model.sendLine "hello" utility
                        smokeSent <- true
                    if utility.Received.Contains "echo: hello" then
                        if not smokeSizeSent then
                            Utilities.Model.sendLine "size" utility
                            smokeSizeSent <- true
                        if utility.Received.Contains(sprintf "terminal size: %d x %d" utility.Process.Width utility.Process.Height) then
                            smokePassed <- true
                            model.Exit <- true)

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

            model.Columns.Value <- width
            model.Viewport.Value <- sprintf "%d × %d cells" width height
            let allocationStart = GC.GetAllocatedBytesForCurrentThread()
            let renderStart = Stopwatch.GetTimestamp()
            Layout.presentReactive session.Output mounted renderer |> ignore
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
        if smoke && not smokePassed then failwith "Showcase native subprocess exchange did not complete."
    finally
        try
            session.Close()
        finally
            View.dispose mounted
            Model.finish model

[<EntryPoint>]
let main args =
    if Array.contains "--child" args then child ()
    elif Array.contains "--verify" args then
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
