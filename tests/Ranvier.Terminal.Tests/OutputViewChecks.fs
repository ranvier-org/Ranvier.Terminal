module OutputViewChecks

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private key code modifiers =
    Key { Key = code; Scalar = 0; Modifiers = modifiers; Released = false; Paste = false }

let private source wait drain =
    { WaitToRead = wait; Drain = drain; DroppedBytes = fun () -> 0L
      Write = fun _ _ -> Task.CompletedTask
      Resize = fun _ _ -> ()
      Exit = Task.FromResult 7; Close = fun () -> failwith "Output binding must not close the session" }

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private releaseBinding session =
    use graph = new Graph()
    let mounted = View.mount graph (fun () ->
        SubprocessOutput.bind 1 (fun () -> Some session) ignore ignore |> ignore
        Node.create [] [||])
    View.dispose mounted
    WeakReference(graph)

let private bindingChecks () =
    use graph = new Graph()
    let delayed = TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable waits = 0
    let mutable token = CancellationToken.None
    let idle =
        source (fun lifetime -> token <- lifetime; waits <- waits + 1; delayed.Task)
               (fun _ -> failwith "Detached session was drained")
    let mutable bytes = 5
    let flooded =
        source (fun _ -> Task.FromResult(bytes > 0)) (fun limit ->
            let count = min limit bytes
            bytes <- bytes - count
            [| { Kind = TerminalOutput; Bytes = Array.zeroCreate count } |])
    let current = graph.Run(fun () -> createSignal (Some idle))
    let deliveries = ResizeArray<int>()
    let mutable completions = 0
    let mutable pump = ignore
    let mounted = View.mount graph (fun () ->
        pump <- SubprocessOutput.bind 2 (fun () -> current.Value)
                    (fun chunks -> deliveries.Add(chunks |> Array.sumBy (fun chunk -> chunk.Bytes.Length)))
                    (fun exit -> if exit.Result = 7 then completions <- completions + 1)
        Node.create [] [||])
    try
        for _ = 1 to 5 do pump ()
        if waits <> 1 || deliveries.Count <> 0 then failwith "Idle output rearmed or delivered before readiness"
        // Complete the old waiter, then change sources before the next host tick.
        delayed.SetResult true
        graph.Run(fun () -> current.Value <- Some flooded)
        if not token.IsCancellationRequested then failwith "Replacing the session kept its old reader alive"
        graph.Pump() |> ignore
        if deliveries.Count <> 0 then failwith "Graph wake drained output ahead of the host tick"
        for expected in [ 2; 2; 1 ] do
            let before = deliveries.Count
            graph.Run pump
            if deliveries.Count <> before + 1 || deliveries[before] <> expected then
                failwith "Flooding output exceeded one bounded batch per host tick"
        graph.Run pump
        graph.Run pump
        if completions <> 1 then failwith "EOF must be delivered exactly once"
        graph.Run(fun () -> current.Value <- Some idle)
    finally View.dispose mounted
    graph.Pump() |> ignore
    pump ()
    if not token.IsCancellationRequested then failwith "Disposed owner left the output reader active"
    let stalled = TaskCompletionSource<bool>()
    let stuck = source (fun _ -> stalled.Task) (fun _ -> [||])
    let weak = releaseBinding stuck
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    if weak.IsAlive then failwith "An uncompleted output waiter retained its disposed graph"
    GC.KeepAlive stalled

let run () =
    bindingChecks ()
    use graph = new Graph()
    let buffer = OutputBuffer.create 12 4 30
    let utf8 text =
        let bytes = Encoding.UTF8.GetBytes(text: string)
        OutputBuffer.feedUtf8 bytes 0 bytes.Length buffer
    utf8 "\u001b[31mred\u001b[0m 界e\u0301\r\n\u001b[48;2;4;5;6m bg \u001b[0m\r\n👩‍💻 done"
    let snapshots, history, revision = graph.Run(fun () ->
        createSignal (OutputBuffer.snapshot 0 buffer), createSignal 0, createSignal 0)
    let received = ResizeArray<InputEvent>()
    let mutable outputNode = Unchecked.defaultof<Node>
    let mounted = View.mount graph (fun () ->
        createEffect (fun () ->
            revision.Value |> ignore
            snapshots.Value <- OutputBuffer.snapshot history.Value buffer)
        outputNode <- OutputView.interactive [] snapshots history (fun event -> received.Add event; true)
        Node.create [ Css.color (Rgb 0xABCDEF); Css.backgroundColor (Rgb 0x123456) ] [| outputNode |])
    let renderer = Renderer.create 12 4 65536 { Colors = TrueColor; SynchronizedOutput = false }
    let sink = { Write = (fun _ _ _ -> ()); Flush = (fun () -> ()) }
    let verify () =
        let snapshot = snapshots.Peek
        let expected = { snapshot.Frame with Cells = Array.copy snapshot.Frame.Cells }
        if snapshot.CursorVisible && snapshot.ScrollOffset = 0 &&
           snapshot.CursorX >= 0 && snapshot.CursorX < expected.Width &&
           snapshot.CursorY >= 0 && snapshot.CursorY < expected.Height then
            let mutable index = snapshot.CursorY * expected.Width + snapshot.CursorX
            if expected.Cells[index].Width = 0uy then index <- index - 1
            let width = int expected.Cells[index].Width
            for at = index to index + width - 1 do
                let cell = expected.Cells[at]
                expected.Cells[at] <- { cell with Paint = { cell.Paint with Attributes = cell.Paint.Attributes ^^^ Attributes.Reverse } }
        if renderer.Back.Width <> expected.Width || renderer.Back.Height <> expected.Height then
            Renderer.resize expected.Width expected.Height renderer
        Layout.presentReactive sink mounted renderer |> ignore
        for index = 0 to expected.Cells.Length - 1 do
            if not (Cell.equal expected.Cells[index] renderer.Front.Cells[index]) then
                failwithf "output snapshot mismatch at %d: expected %A actual %A" index expected.Cells[index] renderer.Front.Cells[index]
        let viewport = outputNode.Children[0]
        if viewport.Children.Length <> expected.Height then failwith "output row count escaped viewport"
        for row in viewport.Children do
            if row.Children.Length > expected.Width then failwith "output paint runs escaped viewport width"
    let publish () = revision.Value <- revision.Peek + 1; verify ()
    verify ()
    let initialRows = Array.copy outputNode.Children[0].Children
    let cursorSnapshot = snapshots.Peek
    let untouched = Array.copy cursorSnapshot.Frame.Cells
    snapshots.Value <- { cursorSnapshot with CursorX = 5; CursorY = 0; CursorVisible = true }
    verify ()
    snapshots.Value <- { snapshots.Peek with CursorX = 0; CursorY = 1 }
    verify ()
    snapshots.Value <- { snapshots.Peek with CursorVisible = false }
    verify ()
    for index = 0 to untouched.Length - 1 do
        if not (Cell.equal untouched[index] cursorSnapshot.Frame.Cells[index]) then failwith "cursor overlay mutated detached snapshot"
    snapshots.Value <- cursorSnapshot
    verify ()
    View.dispatch (Mouse { X = 1; Y = 0; Button = 0; Action = MouseAction.Press; Delta = 0; Modifiers = Modifiers.None }) mounted |> ignore
    if not outputNode.Focused then failwith "clicking an output run did not focus its terminal ancestor"
    let beforeTab = received.Count
    View.dispatch (key Key.Tab Modifiers.None) mounted |> ignore
    if received.Count <> beforeTab + 1 then failwith "terminal Tab was consumed by host focus traversal"
    View.dispatch (key Key.Tab Modifiers.Control) mounted |> ignore
    if received.Count <> beforeTab + 1 then failwith "Ctrl+Tab did not escape terminal input"
    View.focus (Some outputNode) mounted
    for code in [ Key.PageUp; Key.PageDown; Key.Home; Key.End ] do
        let count = received.Count
        View.dispatch (key code Modifiers.None) mounted |> ignore
        if received.Count <> count + 1 then failwith "plain terminal navigation was consumed by history"
    for line = 0 to 14 do
        OutputBuffer.feedText (sprintf "\r\nline %d 界" line) buffer
        publish ()
    for index = 0 to initialRows.Length - 1 do
        if not (obj.ReferenceEquals(initialRows[index], outputNode.Children[0].Children[index])) then
            failwith "streaming output rebuilt retained row nodes"
    let tail = snapshots.Peek.TotalScrolledLines
    View.dispatch (key Key.PageUp Modifiers.Shift) mounted |> ignore
    verify ()
    if history.Peek <= 0 then failwith "shifted page key did not enter history"
    let oldOffset = history.Peek
    let detached = snapshots.Peek.Frame.Cells |> Array.copy
    OutputBuffer.feedText "\r\nnew tail" buffer
    publish ()
    if snapshots.Peek.TotalScrolledLines <= tail || history.Peek <= oldOffset then failwith "history did not anchor while output arrived"
    for index = 0 to detached.Length - 1 do
        if not (Cell.equal detached[index] snapshots.Peek.Frame.Cells[index]) then failwith "anchored history moved during append"
    for delta in [ -1; -1; 1; Int32.MinValue; Int32.MaxValue ] do
        View.dispatch (Mouse { X = 1; Y = 1; Button = 0; Action = MouseAction.Scroll; Delta = delta; Modifiers = Modifiers.None }) mounted |> ignore
        verify ()
        if history.Peek < 0 || history.Peek > snapshots.Peek.HistoryLines then failwith "wheel history escaped range"
    View.dispatch (key Key.End Modifiers.Shift) mounted |> ignore
    verify ()
    if history.Peek <> 0 then failwith "shifted End did not resume follow-tail"
    for width, height in [ 5, 3; 17, 6; 12, 4 ] do
        OutputBuffer.resize width height buffer
        publish ()
    View.dispatch (key Key.Home Modifiers.Shift) mounted |> ignore
    verify ()
    OutputBuffer.clear buffer
    publish ()
    if history.Peek <> 0 then failwith "clear did not reset anchored history"
    let children = outputNode.Children[0].Children
    let texts () = children |> Array.map (fun row -> row.Children |> Array.map (fun node -> node.Text.Source))
    let beforeDispose = texts ()
    View.dispose mounted
    OutputBuffer.feedText "disposed" buffer
    snapshots.Value <- OutputBuffer.snapshot 0 buffer
    if not (obj.ReferenceEquals(children, outputNode.Children[0].Children)) then failwith "disposed output effect updated rows"
    if texts () <> beforeDispose then failwith "disposed output effect changed retained text"

    OutputBuffer.feedText "\r\none\r\ntwo\r\nthree\r\nfour\r\nfive" buffer
    snapshots.Value <- OutputBuffer.snapshot 0 buffer
    let readOnly = View.mount graph (fun () ->
        createEffect (fun () -> snapshots.Value <- OutputBuffer.snapshot history.Value buffer)
        OutputView.create [] snapshots history)
    View.dispatch (key Key.PageUp Modifiers.None) readOnly |> ignore
    if history.Peek <= 0 then failwith "read-only PageUp did not browse history"
    View.dispatch (key Key.End Modifiers.None) readOnly |> ignore
    if history.Peek <> 0 then failwith "read-only End did not follow tail"
    Layout.presentReactive sink readOnly renderer |> ignore
    for index = 0 to snapshots.Peek.Frame.Cells.Length - 1 do
        if not (Cell.equal snapshots.Peek.Frame.Cells[index] renderer.Front.Cells[index]) then
            failwith "read-only view rendered a cursor overlay"
    View.dispose readOnly

    let delayedSnapshots, delayedHistory = graph.Run(fun () ->
        createSignal { snapshots.Peek with HistoryLines = 20; ScrollOffset = 0 }, createSignal 0)
    let delayed = View.mount graph (fun () -> OutputView.create [] delayedSnapshots delayedHistory)
    let wheelUp = Mouse { X = 0; Y = 0; Button = 0; Action = MouseAction.Scroll; Delta = -1; Modifiers = Modifiers.None }
    delayed.Root.OnEvent wheelUp |> ignore
    delayed.Root.OnEvent wheelUp |> ignore
    if delayedHistory.Peek <> 2 then failwith "wheel events were lost before host snapshot publication"
    let snapshot = delayedSnapshots.Peek
    let width, height = snapshot.Frame.Width, snapshot.Frame.Height
    Layout.render delayed (Frame.create (width + 1) height)
    let mouse action y = Mouse { X = width; Y = y; Button = 0; Action = action; Delta = 0; Modifiers = Modifiers.None }
    View.dispatch (mouse MouseAction.Press (height - 1)) delayed |> ignore
    View.dispatch (mouse MouseAction.Move -1) delayed |> ignore
    if delayedHistory.Peek <> snapshot.HistoryLines then failwith "dragging upward must request oldest output history"
    View.dispatch (mouse MouseAction.Move (height + 10)) delayed |> ignore
    if delayedHistory.Peek <> 0 then failwith "dragging to the bottom must request live output"
    View.dispatch (mouse MouseAction.Release height) delayed |> ignore
    View.dispose delayed

    let mutable terminal = Unchecked.defaultof<Node>
    let next = Node.create [ Css.height (Css.cells 1) ] [||]
    next.Focusable <- true
    let focusView = View.mount graph (fun () ->
        terminal <- OutputView.interactive [] snapshots history (fun _ -> true)
        Node.create [] [| terminal; next |])
    Layout.render focusView (Frame.create snapshots.Peek.Frame.Width (snapshots.Peek.Frame.Height + 1))
    View.focus (Some terminal) focusView
    View.dispatch (key Key.Tab Modifiers.Control) focusView |> ignore
    if not next.Focused then failwith "Ctrl+Tab did not move focus beyond interactive output"
    View.dispose focusView
