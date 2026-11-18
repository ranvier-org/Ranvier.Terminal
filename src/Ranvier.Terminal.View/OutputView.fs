namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

/// Retained views of detached output snapshots. Publish snapshots and mutate historyOffset on the graph thread.
module OutputView =
    /// Size the backing buffer or PTY from an arranged output view. Reserving its scrollback
    /// gutter from the first frame keeps terminal columns stable when history starts accumulating.
    let tryViewportSize (node: Node) =
        let width = node.Content.Width + (if node.VerticalScrollbar then 1 else 0)
        let height = node.Content.Height + (if node.HorizontalScrollbar then 1 else 0)
        if width <= 0 || height <= 0 then ValueNone
        else
            let reserve = width > 1 && node.OnScrollYRequested.IsSome && node.Computed.ScrollbarVisibility = ScrollbarVisibility.Auto
            ValueSome(struct ((if reserve then width - 1 else width), height))

    let private prepared (glyphs: Glyph array) width =
        let columns = Array.zeroCreate (glyphs.Length + 1)
        for index = 0 to glyphs.Length - 1 do
            columns[index + 1] <- columns[index] + glyphs[index].Columns
        { Source = String.Concat(glyphs |> Array.map (fun glyph -> glyph.Text))
          Glyphs = glyphs
          ColumnOffsets = columns
          Width = width
          Height = 1
          Lines = { Starts = Array.zeroCreate (glyphs.Length + 1)
                    Ends = Array.zeroCreate (glyphs.Length + 1)
                    Count = 0; Width = -1; Wrap = false; WordWrap = true } }

    let private build interactive browseHistory declarations (snapshots: Signal<OutputSnapshot>)
                      (historyOffset: Signal<int>) (onInput: InputEvent -> bool) =
        let viewport = Node.create [] [||]
        viewport.InheritColors <- false
        let root = Node.create declarations [| viewport |]
        root.Focusable <- true
        if browseHistory then
            createEffect (fun () ->
                let snapshot = snapshots.Value
                let top = snapshot.HistoryLines - Math.Clamp(historyOffset.Value, 0, snapshot.HistoryLines)
                Node.setVirtualScrollY snapshot.Frame.Width top snapshot.HistoryLines root)
            root.OnScrollYRequested <- Some (fun top ->
                let maximum = snapshots.Peek.HistoryLines
                historyOffset.Value <- maximum - Math.Clamp(top, 0, maximum))
        let mutable previousFrame: Frame option = None
        let mutable previousScrolled = snapshots.Peek.TotalScrolledLines
        let mutable previousCursor = struct (-1, -1, 0)
        createEffect (fun () ->
            let snapshot = snapshots.Value
            let frame = snapshot.Frame
            let cursor =
                if interactive && snapshot.CursorVisible && snapshot.ScrollOffset = 0 &&
                   snapshot.CursorX >= 0 && snapshot.CursorX < frame.Width &&
                   snapshot.CursorY >= 0 && snapshot.CursorY < frame.Height then
                    let x =
                        if frame.Cells[snapshot.CursorY * frame.Width + snapshot.CursorX].Width = 0uy then
                            max 0 (snapshot.CursorX - 1)
                        else snapshot.CursorX
                    struct (x, snapshot.CursorY, int frame.Cells[snapshot.CursorY * frame.Width + x].Width)
                else struct (-1, -1, 0)
            let struct (cursorX, cursorY, cursorWidth) = cursor
            let struct (_, oldCursorY, _) = previousCursor
            let paintAt x y =
                let paint = frame.Cells[y * frame.Width + x].Paint
                if y = cursorY && x >= cursorX && x < cursorX + cursorWidth then
                    { paint with Attributes = paint.Attributes ^^^ Attributes.Reverse }
                else paint
            let delta = snapshot.TotalScrolledLines - previousScrolled
            previousScrolled <- snapshot.TotalScrolledLines
            let offset = historyOffset.Peek
            if delta < 0L || offset < 0 then historyOffset.Value <- 0
            elif offset > 0 then
                let limit = int64 (max 0 snapshot.HistoryLines)
                let anchored = int (min limit (int64 offset + min limit delta))
                if anchored <> offset then historyOffset.Value <- anchored
            let resized =
                match previousFrame with
                | Some previous -> previous.Width <> frame.Width || previous.Height <> frame.Height
                | None -> true
            if resized then
                Node.setStyle [ Css.width (Css.cells frame.Width); Css.height (Css.cells frame.Height) ] viewport
            if viewport.Children.Length <> frame.Height then
                let old = viewport.Children
                Node.setChildren
                    (Array.init frame.Height (fun index ->
                        if index < old.Length then old[index]
                        else Node.create [ Css.direction Row; Css.height (Css.cells 1) ] [||])) viewport
            for y = 0 to frame.Height - 1 do
                let changed =
                    if resized || (cursor <> previousCursor && (y = cursorY || y = oldCursorY)) then true
                    else
                        let previous = previousFrame.Value
                        let mutable index = y * frame.Width
                        let finish = index + frame.Width
                        while index < finish && Cell.equal previous.Cells[index] frame.Cells[index] do
                            index <- index + 1
                        index <> finish
                if changed then
                    let row = viewport.Children[y]
                    let runs = ResizeArray<Node>()
                    let mutable x = 0
                    while x < frame.Width do
                        let start = x
                        let paint = paintAt x y
                        let glyphs = ResizeArray<Glyph>()
                        while x < frame.Width && Paint.equal paint (paintAt x y) do
                            let cell = frame.Cells[y * frame.Width + x]
                            let width = int cell.Width
                            if width < 1 || width > 2 || x + width > frame.Width then
                                invalidArg "snapshots" "Output snapshots must contain complete wide-cell pairs."
                            if width = 2 then
                                let continuation = frame.Cells[y * frame.Width + x + 1]
                                if continuation.Width <> 0uy || not (Paint.equal cell.Paint continuation.Paint) then
                                    invalidArg "snapshots" "Output snapshots must contain matching wide-cell continuations."
                            glyphs.Add { Text = cell.Glyph; Columns = width }
                            x <- x + width
                        let index = runs.Count
                        let node = if index < row.Children.Length then row.Children[index] else Node.create [] [||]
                        Node.setStyle [ Css.width (Css.cells (x - start)); Css.height (Css.cells 1)
                                        Css.color paint.Foreground; Css.backgroundColor paint.Background
                                        Css.attributes paint.Attributes ] node
                        Node.setPreparedText (prepared (glyphs.ToArray()) (x - start)) node
                        runs.Add node
                    if runs.Count <> row.Children.Length then Node.setChildren (runs.ToArray()) row
            previousFrame <- Some frame
            previousCursor <- cursor)
        let moveHistory delta =
            let snapshot = snapshots.Peek
            let next = int (max 0L (min (int64 snapshot.HistoryLines) (int64 historyOffset.Peek + delta)))
            historyOffset.Value <- next
            true
        let navigate event =
            match event with
            | _ when not browseHistory -> false
            | Mouse mouse when mouse.Action = MouseAction.Scroll && mouse.Delta <> 0
                               && mouse.Modifiers &&& Modifiers.Shift = Modifiers.None -> moveHistory (-int64 mouse.Delta)
            | Key key when not key.Released && not key.Paste &&
                           key.Modifiers = (if interactive then Modifiers.Shift else Modifiers.None) ->
                match key.Key with
                | Key.PageUp -> moveHistory (int64 snapshots.Peek.Frame.Height)
                | Key.PageDown -> moveHistory (-int64 snapshots.Peek.Frame.Height)
                | Key.Home -> historyOffset.Value <- snapshots.Peek.HistoryLines; true
                | Key.End -> historyOffset.Value <- 0; true
                | _ -> false
            | _ -> false
        viewport.OnEvent <- navigate
        root.OnEvent <- fun event -> if navigate event then true else onInput event
        root

    /// A read-only output frame. Zero historyOffset follows the tail; nonzero offsets anchor history
    /// as output arrives. Republish the buffer snapshot when historyOffset changes. Page keys, Home,
    /// End and the wheel browse history. Styling applies to the outer frame, not snapshot colors.
    let create declarations snapshots historyOffset =
        build false true declarations snapshots historyOffset (fun _ -> false)

    /// Display snapshots without consuming input or browsing output history. Parent views own navigation.
    let display declarations snapshots =
        let node = build false false declarations snapshots (createSignal 0) (fun _ -> false)
        node.Focusable <- false
        node

    /// An interactive output frame. Shift+PageUp/PageDown/Home/End and the wheel browse history;
    /// other input is forwarded to onInput. Ctrl+Tab remains the host focus-navigation escape.
    /// The snapshot's visible cursor reverses its complete cell independently of host focus.
    let interactive declarations snapshots historyOffset onInput =
        build true true declarations snapshots historyOffset onInput
