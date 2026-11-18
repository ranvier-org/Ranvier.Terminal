module Utilities.Program

open System
open System.Diagnostics
open System.Text
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let child () =
    Console.OutputEncoding <- UTF8Encoding(false)
    Console.WriteLine "child ready — type hello or exit"
    Console.Out.Flush()
    let mutable running = true
    while running do
        match Console.ReadLine() with
        | null | "exit" -> running <- false
        | "size" when not Console.IsOutputRedirected ->
            Console.WriteLine(sprintf "terminal size: %d x %d" Console.WindowWidth Console.WindowHeight)
            Console.Out.Flush()
        | line ->
            Console.WriteLine("echo: " + line)
            Console.Out.Flush()
    Console.WriteLine "child goodbye"

let finish model =
    try Model.close model |> fun work -> work.GetAwaiter().GetResult()
    finally
        try model.PendingWrite.GetAwaiter().GetResult() with _ -> ()

let verify mode =
    use graph = new Graph()
    let model = graph.Run(fun () -> Model.create mode)
    let mounted = View.mount graph (fun () -> Kit.build model)
    let renderer = Renderer.create 100 30 500000 Terminal.defaults.Capabilities
    let output = { Write = (fun _ _ _ -> ()); Flush = ignore }
    let step () =
        graph.Run(fun () -> Model.tick 0.05 model)
        Layout.presentReactive output mounted renderer |> ignore
    let until description condition =
        let timeout = Stopwatch.StartNew()
        while not (condition ()) && timeout.Elapsed < TimeSpan.FromSeconds 15. do
            step ()
            Thread.Sleep 10
        if not (condition ()) then failwith ("Timed out: " + description + "; " + model.Status.Peek)
    let verifyChildSize () =
        if mode <> Redirected then
            graph.Run(fun () -> Model.sendLine "size" model)
            let expected = sprintf "terminal size: %d x %d" model.Process.Width model.Process.Height
            until expected (fun () -> model.Received.Contains expected)
    try
        step ()
        step ()
        let first = model.Stream.Snapshot.Peek.Revision
        graph.Run(fun () -> Model.start model)
        until "child startup" (fun () -> model.Received.Contains "child ready")
        verifyChildSize ()
        graph.Run(fun () -> Model.sendLine "hello" model)
        until "child input" (fun () -> model.Received.Contains "echo: hello")
        graph.Run(fun () ->
            Model.sendLine "queued alpha" model
            Model.sendLine "queued beta" model
            Model.sendLine "queued gamma" model)
        until "queued type-ahead" (fun () -> model.Received.Contains "echo: queued gamma")
        if not (model.Received.Contains "echo: queued alpha" && model.Received.Contains "echo: queued beta") then
            failwith "Queued input was lost."
        Renderer.resize 120 38 renderer
        step ()
        step ()
        if model.Process.Width <> model.Process.Node.Value.Bounds.Width - 3 then failwith "Process viewport did not reserve its gutter after resize."
        verifyChildSize ()
        for _ = 0 to 30 do
            graph.Run(fun () ->
                OutputBuffer.feedText "history entry\n" model.Stream.Buffer
                model.Stream.Dirty <- true)
        step ()
        let pane = model.Stream.Node.Value
        if not pane.VerticalScrollbar || pane.Content.Width <> model.Stream.Width || pane.HorizontalScrollbar then
            failwith "The bordered output pane must show its history gutter without losing or scrolling a terminal column."
        let drag action y =
            View.dispatch (Mouse { Action = action; Button = 0; X = pane.Content.X + pane.Content.Width
                                   Y = y; Delta = 0; Modifiers = Modifiers.None }) mounted |> ignore
        drag MouseAction.Press pane.Content.Y
        if model.Stream.Offset.Peek <> model.Stream.Snapshot.Peek.HistoryLines then failwith "Output history track did not reach the oldest line."
        drag MouseAction.Move (pane.Content.Y + pane.Content.Height - 1)
        drag MouseAction.Release (pane.Content.Y + pane.Content.Height - 1)
        if model.Stream.Offset.Peek <> 0 then failwith "Dragging output history to its tail did not resume live output."
        graph.Run(fun () -> model.Stream.Offset.Value <- 3)
        if model.Stream.Snapshot.Peek.ScrollOffset <> 3 then failwith "History selection was not published immediately."
        let beforeScroll = model.Stream.Snapshot.Peek.TotalScrolledLines
        graph.Run(fun () ->
            OutputBuffer.feedText "anchored incoming entry\n" model.Stream.Buffer
            model.Stream.Dirty <- true
            Model.tick 0. model)
        let anchored = model.Stream.Snapshot.Peek
        let expectedOffset = min anchored.HistoryLines (3 + int (anchored.TotalScrolledLines - beforeScroll))
        if anchored.ScrollOffset <> expectedOffset || model.Stream.Offset.Peek <> expectedOffset then
            failwith "Incoming output did not immediately publish the anchored history viewport."
        if model.Stream.Snapshot.Peek.Revision <= first then failwith "Live stream did not advance."
        let revision = model.Revision.Peek
        Kit.changed ()
        graph.Run(fun () -> model.Revision.Value <- Kit.currentRevision ())
        if model.Revision.Peek = revision then failwith "Hot reload revision did not change."
        step ()
        graph.Run(fun () -> Model.sendLine "exit" model)
        until "child exit" (fun () -> model.Completed)
        if not (model.Received.Contains "child goodbye") then failwith "Final output was not drained."
        graph.Run(fun () -> Model.start model)
        until "restarted child" (fun () -> model.Received.Contains "child ready")
        View.dispose mounted
        finish model
        if not model.Closing.IsCompletedSuccessfully then failwith "Owned process cleanup failed."
        printfn "measured here: utilities headless verification passed (live output, subprocess input/exit, history, resize, hot reload and disposal)."
    finally
        View.dispose mounted
        finish model

let run smoke redirected =
    use stop = new CancellationTokenSource()
    use notifications = new CancellationTokenSource()
    let mutable sent = false
    let mutable passed = false
    if smoke then stop.CancelAfter(TimeSpan.FromSeconds 15.)
    let mutable awaitShutdown = ignore
    let setup _ =
        let graph = Graph.Current
        let wake (work: Task) =
            work.ContinueWith((fun (_: Task) -> graph.Dispatch(Action ignore)), notifications.Token,
                              TaskContinuationOptions.None, TaskScheduler.Default) |> ignore
        let mode =
            if redirected then Redirected
            else Subprocess.tryPseudoTerminal 40 8 |> Option.defaultValue Redirected
        let value = Model.createWithWake (Some wake) mode
        onCleanup (fun () ->
            notifications.Cancel()
            Model.close value |> ignore
            awaitShutdown <- fun () -> finish value)
        let tick (delta: TimeSpan) =
            value.Revision.Value <- Kit.currentRevision ()
            if smoke && value.Session.IsNone && not value.Completed then Model.start value
            Model.tick delta.TotalSeconds value
            if smoke then
                if value.Received.Contains "child ready" && not sent then
                    Model.sendLine "hello" value
                    sent <- true
                if value.Received.Contains "echo: hello" then
                    passed <- true
                    stop.Cancel()
                None
            else Some(TimeSpan.FromSeconds(max 0.001 (value.NextLine - value.Elapsed)))
        Kit.build value, tick
    try Terminal.runApp Terminal.defaults stop.Token setup
    finally awaitShutdown ()
    if smoke && not passed then failwith "Hidden utilities smoke did not receive child echo."

[<EntryPoint>]
let main args =
    if Array.contains "--child" args then child ()
    elif Array.contains "--verify" args then
        let mode = if Array.contains "--pty" args then Subprocess.tryPseudoTerminal 40 8 else Some Redirected
        mode |> Option.iter verify
    else run (Array.contains "--smoke" args) (Array.contains "--redirected" args)
    0
