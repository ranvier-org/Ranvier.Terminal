namespace Ranvier.Terminal.View

open System
open System.Globalization
open Ranvier
open Ranvier.Keyboard
open Ranvier.Terminal
open Ranvier.Terminal.Css

/// Content dimensions for a virtual text viewport, in terminal cells.
type VirtualListViewport = { Width: int; Height: int }

module Widgets =
    /// Wrapped, variable-height text rows. Publish viewport changes and replace item arrays through
    /// their signals. Scroll is a line offset; selection changes reveal the selected item's start.
    /// Declarations decorate the outer node; the inner viewport owns its dimensions and scrolling.
    /// The prepared cache charges UTF-16 length with a minimum of 64 units per item; zero disables it.
    let virtualTextListWithCache declarations maxCachedCharacters (viewport: Signal<VirtualListViewport>)
                        (items: Signal<string array>) (selected: Signal<int>) (scroll: Signal<int>) =
        if maxCachedCharacters < 0 then invalidArg "maxCachedCharacters" "The cache budget cannot be negative."
        let content = Node.create [] [||]
        let node = Node.create declarations [| content |]
        node.Focusable <- true
        node.OnScrollYRequested <- Some (fun offset -> scroll.Value <- offset)
        let mutable previousItems = Unchecked.defaultof<string array>
        let mutable previousWidth = -1
        let mutable previousHeight = -1
        let mutable previousSelection = Int32.MinValue
        let mutable offsets = [| 0 |]
        let mutable prepared: TextRun option array = [||]
        let mutable rows: Node array = [||]
        let mutable rowIndices: int array = [||]
        let locate position =
            let mutable lo = 0
            let mutable hi = offsets.Length - 1
            while lo < hi do
                let mid = lo + (hi - lo + 1) / 2
                if offsets[mid] <= position then lo <- mid else hi <- mid - 1
            min (offsets.Length - 2) lo
        createEffect (fun () ->
            let size = viewport.Value
            if size.Width < 1 || size.Height < 1 then
                invalidArg "viewport" "Virtual text viewport dimensions must be positive."
            let values = items.Value
            let current = max 0 (min (values.Length - 1) selected.Value)
            let requested = scroll.Value
            let changedItems = not (obj.ReferenceEquals(values, previousItems))
            let changedWidth = previousWidth <> size.Width
            if changedItems || changedWidth then
                if offsets.Length <> values.Length + 1 then offsets <- Array.zeroCreate (values.Length + 1)
                if changedItems then prepared <- Array.create values.Length None
                let mutable cachedCharacters = 0L
                for index = 0 to values.Length - 1 do
                    let run =
                        match prepared[index] with
                        | Some run -> run
                        | None -> TextRun.prepare values[index]
                    let cost = int64 (max 64 values[index].Length)
                    if cachedCharacters + cost <= int64 maxCachedCharacters then
                        cachedCharacters <- cachedCharacters + cost
                        if prepared[index].IsNone then prepared[index] <- Some run
                    let height = max 1 (TextRun.wrappedHeight size.Width true run)
                    if offsets[index] > Int32.MaxValue - height then
                        invalidArg "items" "Total text height exceeds the supported line-offset range."
                    offsets[index + 1] <- offsets[index] + height
                previousWidth <- size.Width
            let maximum = max 0 (offsets[values.Length] - size.Height)
            let mutable top = max 0 (min maximum requested)
            if current <> previousSelection && values.Length > 0 then
                if offsets[current] < top then top <- offsets[current]
                elif int64 offsets[current + 1] > int64 top + int64 size.Height then
                    top <- offsets[current]
            top <- min maximum top
            let first = if values.Length = 0 then 0 else locate top
            let mutable count = 0
            while first + count < values.Length && int64 offsets[first + count] < int64 top + int64 size.Height do
                count <- count + 1
            if rows.Length <> count || (count > 0 && rowIndices[0] <> first) then
                let old = rows
                let oldIndices = rowIndices
                let oldFirst = if oldIndices.Length = 0 then -1 else oldIndices[0]
                let mutable spare = 0
                rows <- Array.init count (fun slot ->
                    let index = first + slot
                    let oldSlot = index - oldFirst
                    if oldSlot >= 0 && oldSlot < old.Length then old[oldSlot]
                    else
                        while spare < old.Length && oldIndices[spare] >= first && oldIndices[spare] < first + count do
                            spare <- spare + 1
                        if spare < old.Length then
                            let row = old[spare]
                            spare <- spare + 1
                            row
                        else Node.create [] [||])
                rowIndices <- Array.init count (fun slot ->
                    let oldSlot = first + slot - oldFirst
                    if oldSlot >= 0 && oldSlot < old.Length then first + slot else -1)
                Node.setChildren rows content
            for slot = 0 to count - 1 do
                let index = first + slot
                if changedItems || rowIndices[slot] <> index then
                    match prepared[index] with
                    | Some run -> Node.setPreparedText run rows[slot]
                    | None -> Node.setText values[index] rows[slot]
                let height = offsets[index + 1] - offsets[index]
                let attributes = if index = current then Attributes.Reverse else Attributes.None
                match rows[slot].Style.Height with
                | Cells existing when existing = height && rows[slot].Style.Wrap && rows[slot].Style.Paint.Attributes = attributes -> ()
                | _ ->
                    Node.setStyle [ Css.height (Css.cells height); Css.wrap true; Css.attributes attributes ] rows[slot]
                rowIndices[slot] <- index
            if changedWidth || previousHeight <> size.Height then
                Node.setStyle [ Css.width (Css.cells size.Width); Css.height (Css.cells size.Height)
                                Css.scrollbarVisibility ScrollbarVisibility.Hidden ] content
                previousHeight <- size.Height
            Node.setScroll 0 (if count = 0 then 0 else top - offsets[first]) content
            Node.setVirtualScrollY size.Width top maximum node
            previousItems <- values
            previousSelection <- current
            if scroll.Peek <> top then scroll.Value <- top)
        let scrollBy delta =
            scroll.Value <- int (max 0L (min (int64 Int32.MaxValue) (int64 scroll.Peek + int64 delta)))
        let wheel = function
            | Mouse mouse when mouse.Action = MouseAction.Scroll && mouse.Delta <> 0 && items.Peek.Length > 0
                               && mouse.Modifiers &&& Modifiers.Shift = Modifiers.None ->
                scrollBy mouse.Delta
                true
            | _ -> false
        content.OnEvent <- wheel
        node.OnEvent <- function
            | Mouse _ as event -> wheel event
            | Key key when not key.Released && not key.Paste ->
                let count = items.Peek.Length
                match key.LogicalKey with
                | LogicalKey.Navigation ArrowUp | LogicalKey.Navigation ArrowDown when count > 0 ->
                    let delta = if key.LogicalKey = LogicalKey.Navigation ArrowUp then -1 else 1
                    selected.Value <- max 0 (min (count - 1) (max 0 (min (count - 1) selected.Peek) + delta))
                    true
                | LogicalKey.Navigation PageUp | LogicalKey.Navigation PageDown when count > 0 ->
                    let delta = if key.LogicalKey = LogicalKey.Navigation PageUp then -viewport.Peek.Height else viewport.Peek.Height
                    scrollBy delta
                    true
                | _ -> false
            | _ -> false
        node

    /// Variable text virtualization with a bounded prepared-text cache. Use virtualTextListWithCache
    /// with a zero budget to retain only visible prepared text, trading memory for resize work.
    let virtualTextList declarations viewport items selected scroll =
        virtualTextListWithCache declarations 1048576 viewport items selected scroll

    /// A fixed-height viewport backed by Ranvier signals. Only visible row nodes are retained.
    let virtualList declarations visibleRows (items: Signal<string array>) (selected: Signal<int>) =
        if visibleRows < 1 then invalidArg "visibleRows" "A viewport must contain at least one row."
        let rows = Array.init visibleRows (fun _ -> Node.create [ Css.height (Css.cells 1) ] [||])
        let node = Node.create (Css.height (Css.cells visibleRows) :: declarations) rows
        node.Focusable <- true
        let scroll = createSignal 0
        let mutable previousFirst = -1
        let normal = [ Css.height (Css.cells 1); Css.visible true; Css.attributes Attributes.None ]
        let highlight = [ Css.height (Css.cells 1); Css.visible true; Css.attributes Attributes.Reverse ]
        let hidden = [ Css.height (Css.cells 1); Css.visible false; Css.attributes Attributes.None ]
        let mutable previousItems = Unchecked.defaultof<string array>
        let mutable previous = -1
        createEffect (fun () ->
            let values = items.Value
            let current = max 0 (min (values.Length - 1) selected.Value)
            let requested = Math.Clamp(scroll.Value, 0, max 0 (values.Length - visibleRows))
            let first = Math.Clamp(requested, max 0 (current - visibleRows + 1), current)
            Node.setVirtualScrollY 0 first (values.Length - visibleRows) node
            if previousFirst <> first || not (obj.ReferenceEquals(previousItems, values)) then
                for slot = 0 to rows.Length - 1 do
                    let index = first + slot
                    let visible = index < values.Length
                    Node.setStyle (if not visible then hidden elif index = current then highlight else normal) rows[slot]
                    if visible then Node.setText values[index] rows[slot]
                    else Node.setPreparedText TextRun.empty rows[slot]
            elif previous <> current then
                if previous >= first && previous < min values.Length (first + visibleRows) then
                    Node.setStyle normal rows[previous - first]
                if current >= first && current < min values.Length (first + visibleRows) then
                    Node.setStyle highlight rows[current - first]
            previousItems <- values
            previousFirst <- first
            previous <- current
            if scroll.Peek <> first then scroll.Value <- first)
        node.OnScrollYRequested <- Some (fun requested ->
            let count = items.Peek.Length
            if count > 0 then
                let first = Math.Clamp(requested, 0, max 0 (count - visibleRows))
                let last = first + min (count - first - 1) (visibleRows - 1)
                batch (fun () ->
                    selected.Value <- Math.Clamp(selected.Peek, first, last)
                    scroll.Value <- first))
        node.OnEvent <- function
            | Key key when not key.Released && not key.Paste ->
                let count = items.Peek.Length
                let delta =
                    match key.LogicalKey with
                    | LogicalKey.Navigation ArrowUp -> -1 | LogicalKey.Navigation ArrowDown -> 1
                    | LogicalKey.Navigation PageUp -> -visibleRows | LogicalKey.Navigation PageDown -> visibleRows
                    | _ -> 0
                if delta = 0 || count = 0 then false
                else
                    selected.Value <- max 0 (min (count - 1) (max 0 (min (count - 1) selected.Peek) + delta))
                    true
            | _ -> false
        node

    let internal buttonWithEvent declarations caption action =
        let node = Node.create declarations [||]
        Node.setText caption node
        node.Focusable <- true

        node.OnEvent <-
            function
            | Key key as event when
                not key.Released
                && not key.Paste
                && (key.LogicalKey = LogicalKey.Whitespace Enter || key.LogicalKey = LogicalKey.Whitespace Space)
                ->
                action event
                true
            | Mouse mouse as event when mouse.Action = MouseAction.Press && mouse.Button = 0 ->
                action event
                true
            | _ -> false

        node

    let button declarations caption action = buttonWithEvent declarations caption (fun _ -> action ())

    let checkbox declarations caption (value: Signal<bool>) =
        let node = button declarations "" (fun () -> value.Value <- not value.Value)
        createEffect (fun () -> Node.setText ((if value.Value then "[x] " else "[ ] ") + caption) node)
        node

    let internal progressTextWith filledGlyph trackGlyph width fraction =
        let width = max 0 width
        let fraction = if Double.IsNaN fraction then 0. else Math.Clamp(fraction, 0., 1.)
        let filled = int (fraction * float width)
        String(filledGlyph, filled) + String(trackGlyph, width - filled)

    let progress declarations width (value: unit -> float) =
        let node = Node.create (Width(Cells width) :: declarations) [||]
        createEffect (fun () -> Node.setText (progressTextWith '█' '░' width (value ())) node)

        node

    let internal inputWithReplace declarations (value: Signal<string>) =
        let node = Node.create declarations [||]
        node.Focusable <- true
        let mutable cursor = value.Peek.Length
        let mutable boundaries = [||]

        let updateCaret () =
            let mutable offset, columns = 0, 0

            for i = 0 to node.Text.Glyphs.Length - 1 do
                let glyph = node.Text.Glyphs[i]

                if offset < cursor then
                    columns <- columns + max 0 glyph.Columns

                offset <- offset + glyph.Text.Length

            Node.setCaret columns node

        let move position =
            cursor <- position
            updateCaret ()

        createEffect (fun () ->
            let current = value.Value
            boundaries <- StringInfo.ParseCombiningCharacters current
            cursor <- min cursor current.Length
            Node.setText current node
            updateCaret ())

        let previous () =
            let mutable result = 0

            for i = 0 to boundaries.Length - 1 do
                if boundaries[i] < cursor then
                    result <- boundaries[i]

            result

        let next () =
            let mutable result = value.Peek.Length
            let mutable i = boundaries.Length - 1

            while i >= 0 do
                if boundaries[i] > cursor then
                    result <- boundaries[i]

                i <- i - 1

            result

        node.OnEvent <-
            function
            | Key key when not key.Released ->
                let current = value.Peek

                match key.LogicalKey with
                | LogicalKey.Navigation ArrowLeft when not key.Paste ->
                    move (previous ())
                    true
                | LogicalKey.Navigation ArrowRight when not key.Paste ->
                    move (next ())
                    true
                | LogicalKey.Navigation Home when not key.Paste ->
                    move 0
                    true
                | LogicalKey.Navigation NavigationKey.End when not key.Paste ->
                    move current.Length
                    true
                | LogicalKey.Editing EditingKey.Backspace when not key.Paste && cursor > 0 ->
                    let start = previous ()
                    let updated = current.Remove(start, cursor - start)
                    cursor <- start
                    value.Value <- updated
                    true
                | LogicalKey.Editing EditingKey.Delete when not key.Paste && cursor < current.Length ->
                    value.Value <- current.Remove(cursor, next () - cursor)
                    true
                | LogicalKey.Character _ | LogicalKey.Whitespace Space when
                    key.Scalar >= 32
                    && key.Scalar <= 0x10FFFF
                    && (key.Paste
                        || key.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt) = Modifiers.None)
                    ->
                    let inserted = Char.ConvertFromUtf32 key.Scalar
                    let updated = current.Insert(cursor, inserted)
                    cursor <- cursor + inserted.Length
                    value.Value <- updated
                    true
                | _ -> false
            | _ -> false

        let replaceText (text: string) =
            cursor <- text.Length
            value.Value <- text
            updateCaret ()

        node, replaceText

    let input declarations value = inputWithReplace declarations value |> fst

    let list declarations (items: string array) (selected: Signal<int>) =
        let rows = Array.map Dsl.label items
        let node = Node.create declarations rows
        node.Focusable <- true
        let mutable previous = -1
        createEffect (fun () ->
            let current = selected.Value
            if previous <> current then
                if previous >= 0 && previous < rows.Length then
                    Node.setStyle [] rows[previous]
                if current >= 0 && current < rows.Length then
                    Node.setStyle [ Css.attributes Attributes.Reverse ] rows[current]
                previous <- current)

        node.OnEvent <-
            function
            | Key key when not key.Released && not key.Paste && items.Length > 0 ->
                match key.LogicalKey with
                | LogicalKey.Navigation ArrowUp ->
                    selected.Value <- max 0 (selected.Value - 1)
                    true
                | LogicalKey.Navigation ArrowDown ->
                    selected.Value <- min (items.Length - 1) (selected.Value + 1)
                    true
                | _ -> false
            | _ -> false

        node
