module VirtualizationChecks

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private runWithCache budget =
    use graph = new Graph()
    let source = [| ""; "first\nsecond\n"; "hello world 界 e\u0301"; String.replicate 20 "wide 界 "; "last" |]
    let items, selected, viewport, scroll = graph.Run(fun () ->
        createSignal source, createSignal 0, createSignal { Width = 9; Height = 5 }, createSignal 0)
    let mounted = View.mount graph (fun () -> Widgets.virtualTextListWithCache [] budget viewport items selected scroll)
    let sink = { Write = (fun _ _ _ -> ()); Flush = (fun () -> ()) }
    let capabilities = { Colors = TrueColor; SynchronizedOutput = false }
    let renderer = Renderer.create viewport.Peek.Width viewport.Peek.Height 65536 capabilities
    let verify () =
        let size = viewport.Peek
        if renderer.Back.Width <> size.Width || renderer.Back.Height <> size.Height then
            Renderer.resize size.Width size.Height renderer
        Layout.presentReactive sink mounted renderer |> ignore
        let actual = renderer.Front
        let expected = Frame.create size.Width size.Height
        let mutable y = -scroll.Peek
        let current = max 0 (min (items.Peek.Length - 1) selected.Peek)
        for i = 0 to items.Peek.Length - 1 do
            let run = TextRun.prepare items.Peek[i]
            let height = max 1 (TextRun.wrappedHeight size.Width true run)
            let paint = { Paint.empty with Attributes = if i = current then Attributes.Reverse else Attributes.None }
            Frame.fill { X = 0; Y = y; Width = size.Width; Height = height } paint expected
            Frame.textWithWrapping (Frame.bounds expected) 0 y size.Width true true run paint expected
            y <- y + height
        for i = 0 to actual.Cells.Length - 1 do
            if not (Cell.equal expected.Cells[i] actual.Cells[i]) then
                failwithf "virtual mismatch at %d, viewport %A scroll %d: expected %A actual %A" i size scroll.Peek expected.Cells[i] actual.Cells[i]
        if mounted.Root.Children[0].Children.Length > size.Height + 1 then failwith "unbounded retained rows"
    verify ()
    let mouse action x y =
        Mouse { X = x; Y = y; Button = 0; Action = action; Delta = 0; Modifiers = Modifiers.None }
    let gutterX = viewport.Peek.Width
    Layout.render mounted (Frame.create (gutterX + 1) viewport.Peek.Height)
    if not (View.dispatch (mouse MouseAction.Press gutterX 0) mounted) then failwith "wrapped list thumb ignored"
    View.dispatch (mouse MouseAction.Move (gutterX + 7) 100) mounted |> ignore
    let struct (_, maxOffset) = mounted.Root.VirtualScrollY.Value
    if maxOffset <= 0 || scroll.Peek <> maxOffset || selected.Peek <> 0 then
        failwith "dragging wrapped text must reach its last logical line and preserve item selection"
    View.dispatch (mouse MouseAction.Release gutterX 100) mounted |> ignore
    scroll.Value <- 0
    verify ()
    let wheel delta =
        let size = viewport.Peek
        let total = items.Peek |> Array.sumBy (fun value -> max 1 (TextRun.wrappedHeight size.Width true (TextRun.prepare value)))
        let expected = int (max 0L (min (int64 (max 0 (total - size.Height))) (int64 scroll.Peek + int64 delta)))
        let selection = selected.Peek
        View.dispatch (Mouse { X = 0; Y = 0; Button = 0; Action = MouseAction.Scroll; Delta = delta; Modifiers = Modifiers.None }) mounted |> ignore
        if scroll.Peek <> expected then failwithf "wheel did not update logical extent scroll: expected %d actual %d" expected scroll.Peek
        if selected.Peek <> selection then failwith "wheel changed selected item"
        verify ()
    for _ = 0 to 70 do wheel 1
    for _ = 0 to 70 do wheel -1
    wheel Int32.MaxValue
    wheel Int32.MinValue
    for offset = -2 to 70 do scroll.Value <- offset; verify ()
    for index = -1 to source.Length + 1 do selected.Value <- index; verify ()
    for width in [ 1; 2; 5; 17; 9 ] do
        for height in [ 1; 3; 12 ] do
            viewport.Value <- { Width = width; Height = height }
            verify ()
            for offset = 0 to 40 do scroll.Value <- offset; verify ()
    items.Value <- [| "only\nrow" |]
    verify ()
    items.Value <- [||]
    verify ()
    items.Value <- source
    verify ()
    let press code released paste = Key { Key = code; Scalar = 0; Modifiers = Modifiers.None; Released = released; Paste = paste }
    for code in [ Key.Up; Key.Down; Key.PageUp; Key.PageDown ] do
        if not (mounted.Root.OnEvent(press code false false)) then failwith "navigation ignored"
        verify ()
        if mounted.Root.OnEvent(press code true false) then failwith "key release accepted"
        if mounted.Root.OnEvent(press code false true) then failwith "paste navigation accepted"
    let content = mounted.Root.Children[0]
    let children = content.Children
    View.dispose mounted
    items.Value <- [| "disposed" |]
    viewport.Value <- { Width = 3; Height = 2 }
    selected.Value <- 2
    scroll.Value <- 2
    if not (obj.ReferenceEquals(children, content.Children)) then failwith "disposed viewport updated"

    let fixedItems, fixedSelected = graph.Run(fun () -> createSignal [| "a"; "b"; "c"; "d" |], createSignal 0)
    let fixedList = View.mount graph (fun () -> Widgets.virtualList [] 3 fixedItems fixedSelected)
    let fixedRows = Array.copy fixedList.Root.Children
    let fixedRenderer = Renderer.create 4 3 4096 capabilities
    let mutable expectedFirst = 0
    let checkFixed () =
        Layout.presentReactive sink fixedList fixedRenderer |> ignore
        let values = fixedItems.Peek
        let current = max 0 (min (values.Length - 1) fixedSelected.Peek)
        expectedFirst <- min (max 0 (values.Length - 3)) expectedFirst
        if current < expectedFirst then expectedFirst <- current
        elif current >= expectedFirst + 3 then expectedFirst <- current - 2
        let expected = Frame.create 4 3
        for y = 0 to 2 do
            let index = expectedFirst + y
            if index < values.Length then
                let paint = { Paint.empty with Attributes = if index = current then Attributes.Reverse else Attributes.None }
                Frame.fill { X = 0; Y = y; Width = 4; Height = 1 } paint expected
                Frame.textWithWrapping (Frame.bounds expected) 0 y 4 false true (TextRun.prepare values[index]) paint expected
            if values.Length > 3 then
                let thumbSize = max 1 (9 / values.Length)
                let thumbStart = expectedFirst * (3 - thumbSize) / (values.Length - 3)
                let glyph = if y >= thumbStart && y < thumbStart + thumbSize then "█" else "░"
                Frame.put (Frame.bounds expected) 3 y glyph 1 Paint.empty expected
        for i = 0 to expected.Cells.Length - 1 do
            if not (Cell.equal expected.Cells[i] fixedRenderer.Front.Cells[i]) then
                failwithf "fixed cached presentation mismatch at %d" i
        let mutable highlights = 0
        for row in fixedRows do
            if row.Style.Visible && row.Style.Paint.Attributes = Attributes.Reverse then
                highlights <- highlights + 1
                if row.Text.Source <> values[current] then failwith "wrong fixed selection"
            if not row.Style.Visible && row.Text.Source <> "" then failwith "hidden row retained text"
        if highlights <> (if values.Length = 0 then 0 else 1) then failwith "fixed highlight count"
        for i = 0 to fixedRows.Length - 1 do
            if not (obj.ReferenceEquals(fixedRows[i], fixedList.Root.Children[i])) then failwith "fixed row replaced"
    checkFixed ()
    View.dispatch (mouse MouseAction.Press 3 1) fixedList |> ignore
    View.dispatch (mouse MouseAction.Move 3 20) fixedList |> ignore
    View.dispatch (mouse MouseAction.Release 3 20) fixedList |> ignore
    let struct (fixedFirst, _) = fixedList.Root.VirtualScrollY.Value
    if fixedFirst <> 1 || fixedSelected.Peek <> 1 || fixedRows[0].Text.Source <> "b" then
        failwith "dragging fixed list must keep selection on the nearest visible row"
    expectedFirst <- fixedFirst
    for selection in [ 0; 1; 3; 2; -1; Int32.MaxValue ] do fixedSelected.Value <- selection; checkFixed ()
    for values in [ [| "only" |]; [||]; [| "back"; "again" |] ] do fixedItems.Value <- values; checkFixed ()
    View.dispose fixedList
    fixedItems.Value <- [| "disposed" |]
    if fixedRows[0].Text.Source = "disposed" then failwith "fixed effect survived disposal"

let run () =
    for budget in [ 0; 128; 1048576 ] do runWithCache budget

