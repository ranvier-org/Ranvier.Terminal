namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

/// Style receives the visible row slot and its current item.
type TableColumn<'row> =
    { Header: string
      Width: Length
      Text: 'row -> string
      Style: int -> 'row -> Declaration list }

type TableSort<'row> =
    { Id: string
      Compare: 'row -> 'row -> int
      Descending: bool }

type TableOrder<'row, 'key when 'key: comparison> =
    private
        { Key: 'row -> 'key
          Sorts: TableSort<'row> array
          mutable Active: int
          mutable Reversed: bool
          mutable AscendingMark: string
          mutable DescendingMark: string
          mutable Cached: ('row array * 'row array) option }
    member this.SortId = this.Sorts[this.Active].Id
    member this.Descending = this.Sorts[this.Active].Descending <> this.Reversed
    member this.IsReversed = this.Reversed

type private TableLine =
    { Row: Node
      Cells: Node array }

type Table<'row> =
    private
        { Container: Node
          HeaderStyle: Declaration list
          SelectionStyle: Declaration list
          Navigate: int -> int -> unit
          mutable SortEvent: (InputEvent -> bool) option
          mutable SortedHeader: (string * string * string option array * Declaration list) option
          mutable Header: TableLine
          mutable Slots: TableLine array
          mutable Columns: TableColumn<'row> array
          mutable Items: 'row array
          mutable Selected: int
          mutable First: int }
    member this.Root = this.Container
    member this.FirstVisible = this.First

module Table =
    /// Each row must have a stable unique key. Comparers operate on row values before formatting.
    let sortable key (sorts: TableSort<_> array) initialSortId =
        if Array.isEmpty sorts || (sorts |> Array.distinctBy _.Id).Length <> sorts.Length then
            invalidArg "sorts" "Supply at least one sort and a unique ID for each."
        match sorts |> Array.tryFindIndex (fun sort -> sort.Id = initialSortId) with
        | None -> invalidArg "initialSortId" "Unknown sort ID."
        | Some index ->
            { Key = key; Sorts = Array.copy sorts; Active = index; Reversed = false
              AscendingMark = "↑"; DescendingMark = "↓"; Cached = None }

    let withSortIndicators ascending descending state =
        let check name glyph =
            if String.IsNullOrEmpty glyph then invalidArg name "A sort indicator must occupy one printable cell."
            let run = TextRun.prepare glyph
            if run.Glyphs.Length <> 1 || run.Glyphs[0].Columns <> 1 || run.Glyphs[0].Text <> glyph then
                invalidArg name "A sort indicator must occupy one printable cell."
        check "ascending" ascending
        check "descending" descending
        state.AscendingMark <- ascending
        state.DescendingMark <- descending
        state

    let reverseSort state =
        state.Reversed <- not state.Reversed
        state.Cached <- None

    let sortBy id state =
        match state.Sorts |> Array.tryFindIndex (fun sort -> sort.Id = id) with
        | None -> invalidArg "id" "Unknown sort ID."
        | Some index when index = state.Active -> reverseSort state
        | Some index ->
            state.Active <- index
            state.Reversed <- false
            state.Cached <- None

    let cycleSort state =
        state.Active <- (state.Active + 1) % state.Sorts.Length
        state.Cached <- None

    /// Row keys identify the same item across snapshots and break equal-value ties.
    let order (items: _ array) state =
        match state.Cached with
        | Some (source, sorted) when obj.ReferenceEquals(source, items) -> sorted
        | _ ->
            let sort = state.Sorts[state.Active]
            let compareRows a b =
                let result = if state.Descending then sort.Compare b a else sort.Compare a b
                if result <> 0 then result
                elif state.Reversed then compare (state.Key b) (state.Key a)
                else compare (state.Key a) (state.Key b)
            let sorted = Array.copy items
            Array.sortInPlaceWith compareRows sorted
            state.Cached <- Some (items, sorted)
            sorted

    let private line count =
        let cells = Array.init count (fun _ -> Node.create [] [||])
        { Row = Node.create [ Direction Row; Height(Cells 1) ] cells
          Cells = cells }

    let private reveal count capacity selected first =
        let selected = Math.Clamp(selected, 0, max 0 (count - 1))
        let first = Math.Clamp(first, 0, max 0 (count - capacity))
        struct (selected, Math.Clamp(first, max 0 (selected - capacity + 1), selected))

    let private paintSlot slot table =
        let line = table.Slots[slot]
        let index = table.First + slot
        let selected = index = table.Selected && index < table.Items.Length
        let highlight = if selected then table.SelectionStyle else []
        Node.setStyle (highlight @ [ Direction Row; Height(Cells 1) ]) line.Row
        for i = 0 to table.Columns.Length - 1 do
            let column = table.Columns[i]
            let cell = line.Cells[i]
            let style, text =
                if index < table.Items.Length then
                    let item = table.Items[index]
                    column.Style slot item, column.Text item
                else [], ""
            Node.setStyle (style @ highlight @ [ Width column.Width; Height(Cells 1) ]) cell
            Node.setText text cell

    let private refreshRows all previousSelected table =
        if table.Items.Length > table.Slots.Length then
            Node.setVirtualScrollWithHeader 0 1 table.First (table.Items.Length - table.Slots.Length) table.Root
        else
            Node.clearVirtualScrollY table.Root
        if all then
            for slot = 0 to table.Slots.Length - 1 do paintSlot slot table
        elif previousSelected <> table.Selected then
            paintSlot (previousSelected - table.First) table
            paintSlot (table.Selected - table.First) table

    let private updateRows columns visibleRows items selected scroll table =
        if visibleRows < 1 then invalidArg "visibleRows" "A table needs at least one visible row."
        let columns: TableColumn<_> array = columns
        let items: _ array = items
        let resized = table.Header.Cells.Length <> columns.Length || table.Slots.Length <> visibleRows
        let columnsChanged = resized || not (obj.ReferenceEquals(table.Columns, columns))
        let itemsChanged = not (obj.ReferenceEquals(table.Items, items))
        let previousSelected, previousFirst = table.Selected, table.First
        if resized then
            table.Header <- line columns.Length
            table.Slots <- Array.init visibleRows (fun _ -> line columns.Length)
            Node.setChildren (Array.append [| table.Header.Row |] (Array.map _.Row table.Slots)) table.Root
        table.Columns <- columns
        table.Items <- items
        let struct (selected, first) = reveal items.Length visibleRows selected scroll
        table.Selected <- selected
        table.First <- first
        if columnsChanged then
            for i = 0 to columns.Length - 1 do
                if table.Header.Cells[i].Children.Length > 0 then Node.setChildren [||] table.Header.Cells[i]
                Node.setStyle (table.HeaderStyle @ [ Width columns[i].Width; Height(Cells 1) ]) table.Header.Cells[i]
                Node.setText columns[i].Header table.Header.Cells[i]
        refreshRows (columnsChanged || itemsChanged || first <> previousFirst) previousSelected table
        struct (selected, first)

    /// Refreshes visible cells. Replace column and item arrays when their content changes.
    let update columns visibleRows items selected scroll table =
        if table.SortEvent.IsSome then
            table.Columns <- [||]
            table.SortEvent <- None
            table.SortedHeader <- None
        updateRows columns visibleRows items selected scroll table

    let rec private refreshSortable columns (headerIds: string option array) indicatorStyle visibleRows items selected scroll followSelection state table =
        if Array.length columns <> headerIds.Length then invalidArg "headerIds" "Supply one sort ID option per column."
        let ordered = order items state
        let mutable next = selected
        let mutable first = scroll
        if followSelection && not (obj.ReferenceEquals(table.Items, ordered)) && selected = table.Selected && selected >= 0 && selected < table.Items.Length then
            let key = state.Key table.Items[selected]
            match Array.tryFindIndex (fun item -> state.Key item = key) ordered with
            | Some index ->
                next <- index
                if scroll > 0 then first <- max 0 (index - (selected - scroll))
            | None -> ()
        let mark = if state.Descending then state.DescendingMark else state.AscendingMark
        let redrawHeader =
            visibleRows <> table.Slots.Length || not (obj.ReferenceEquals(table.Columns, columns))
            || (match table.SortedHeader with
                | Some (id, previousMark, ids, style) ->
                    id <> state.SortId || previousMark <> mark || ids <> headerIds || style <> indicatorStyle
                | None -> true)
        if redrawHeader then
            for id in headerIds do
                match id with
                | Some value when not (state.Sorts |> Array.exists (fun sort -> sort.Id = value)) ->
                    invalidArg "headerIds" ("Unknown sort ID: " + value)
                | _ -> ()
        let result = updateRows columns visibleRows ordered next first table
        if redrawHeader then
            table.SortedHeader <- Some (state.SortId, mark, headerIds, indicatorStyle)
            for i = 0 to columns.Length - 1 do
                let active = headerIds[i] = Some state.SortId
                let cell = table.Header.Cells[i]
                if active then
                    let label = Node.create (table.HeaderStyle @ [ Width(Flex 1); Height(Cells 1) ]) [||]
                    let arrow = Node.create (table.HeaderStyle @ indicatorStyle @ [ Width(Cells 1); Height(Cells 1) ]) [||]
                    Node.setText columns[i].Header label
                    Node.setText mark arrow
                    Node.setText "" cell
                    Node.setStyle (table.HeaderStyle @ [ Direction Row; Width columns[i].Width; Height(Cells 1) ]) cell
                    Node.setChildren [| label; arrow |] cell
                else
                    if cell.Children.Length > 0 then Node.setChildren [||] cell
                    Node.setText columns[i].Header cell
        table.SortEvent <- Some (fun event ->
            let handled =
                match event with
                | Key k when k.Key = Key.Character && not k.Paste && not k.Released
                             && (k.Modifiers = Modifiers.None || k.Modifiers = Modifiers.Shift) ->
                    match k.Scalar with
                    | value when value = int 's' || value = int 'S' -> cycleSort state; true
                    | value when value = int 'r' || value = int 'R' -> reverseSort state; true
                    | _ -> false
                | Mouse m when m.Action = MouseAction.Press && m.Button = 0 && Rect.contains m.X m.Y table.Header.Row.Clip ->
                    match table.Header.Cells |> Array.tryFindIndex (fun cell -> Rect.contains m.X m.Y cell.Clip) with
                    | Some index when headerIds[index].IsSome -> sortBy headerIds[index].Value state; true
                    | _ -> false
                | _ -> false
            if handled then
                let struct (index, start) = refreshSortable columns headerIds indicatorStyle visibleRows items table.Selected table.First true state table
                table.Navigate index start
            handled)
        result

    /// Supply a sort ID for each clickable heading and None for other columns. On a new snapshot,
    /// the selected key follows its row. Removing it selects the nearest remaining index.
    /// Store the returned selection and scroll offset for the next update.
    let updateSortable columns headerIds indicatorStyle visibleRows items selected scroll state table =
        refreshSortable columns headerIds indicatorStyle visibleRows items selected scroll true state table

    let private move table selected first =
        let struct (selected, first) = reveal table.Items.Length table.Slots.Length selected first
        if selected <> table.Selected || first <> table.First then
            let previous = table.Selected
            let scrolled = first <> table.First
            table.Selected <- selected
            table.First <- first
            refreshRows scrolled previous table
            table.Navigate selected first
        true

    let private scroll table target =
        let capacity = table.Slots.Length
        let count = table.Items.Length
        if count = 0 || capacity = 0 then false
        else
            let first = int (Math.Clamp(target, 0L, int64 (max 0 (count - capacity))))
            let lastVisible = first + min (count - 1 - first) (capacity - 1)
            move table (Math.Clamp(table.Selected, first, lastVisible)) first

    let handle event table =
        let count = table.Items.Length
        let capacity = table.Slots.Length
        let last = max 0 (count - 1)
        if (match table.SortEvent with Some sort -> sort event | None -> false) then true
        elif count = 0 || capacity = 0 then false
        else
            match event with
            | Key key when not key.Paste && not key.Released && key.Modifiers = Modifiers.None ->
                match key.Key with
                | Key.Home -> move table 0 table.First
                | Key.End -> move table last table.First
                | Key.Up -> move table (table.Selected - 1) table.First
                | Key.Down -> move table (table.Selected + 1) table.First
                | Key.PageUp -> move table (table.Selected - capacity) table.First
                | Key.PageDown -> move table (table.Selected + min capacity (last - table.Selected)) table.First
                | _ -> false
            | Mouse mouse when Rect.contains mouse.X mouse.Y table.Root.Clip ->
                let bounds = table.Slots[0].Row.Bounds
                let slot = mouse.Y - bounds.Y
                match mouse.Action with
                | MouseAction.Scroll when mouse.Delta <> 0 && count > capacity && mouse.Modifiers &&& Modifiers.Shift = Modifiers.None ->
                    scroll table (int64 table.First + int64 mouse.Delta)
                | MouseAction.Press when mouse.Button = 0 && slot >= 0 && slot < capacity ->
                    if count > capacity && table.Root.VerticalScrollbar && mouse.X = table.Root.Content.X + table.Root.Content.Width then
                        scroll table (int64 slot * int64 (max 0 (count - capacity)) / int64 (max 1 (capacity - 1)))
                    else move table (min last (table.First + slot)) table.First
                | _ -> false
            | _ -> false

    /// Navigation reports both the selected index and the first visible index to the owner.
    let createWithThumb declarations headerStyle selectionStyle scrollbarStyle thumbStyle onNavigate =
        let parts = Css.tableScrollbar (Style.create scrollbarStyle).Paint (Style.create thumbStyle).Paint
        let root = Node.create (parts @ declarations @ [ Direction Column ]) [||]
        let table =
            { Container = root
              HeaderStyle = headerStyle
              SelectionStyle = selectionStyle
              Navigate = onNavigate
              SortEvent = None
              SortedHeader = None
              Header = line 0
              Slots = [||]
              Columns = [||]
              Items = [||]
              Selected = 0
              First = 0 }
        root.Focusable <- true
        root.OnScrollYRequested <- Some (fun offset -> scroll table (int64 offset) |> ignore)
        root.OnEvent <- fun event -> handle event table
        table

    let create declarations headerStyle selectionStyle scrollbarStyle onNavigate =
        createWithThumb declarations headerStyle selectionStyle scrollbarStyle scrollbarStyle onNavigate

    /// Restore a bound viewport's first visible index when rebuilding its columns or dimensions.
    let bindSortableFrom firstVisible columns headerIds indicatorStyle visibleRows readRows (selectedKey: Signal<_ option>) state createTable =
        let mutable source = [||]
        let publish (ordered: _ array) index =
            let key = if ordered.Length = 0 then None else Some (state.Key ordered[index])
            if key <> selectedKey.Peek then selectedKey.Value <- key
        let table = createTable (fun index _ -> publish (order source state) index)
        table.First <- max 0 firstVisible
        createEffect (fun () ->
            source <- readRows ()
            let ordered = order source state
            let requested = selectedKey.Value
            let current = table.Selected
            let index =
                match requested with
                | None -> 0
                | Some key when current < ordered.Length && state.Key ordered[current] = key -> current
                | Some key -> Array.tryFindIndex (fun row -> state.Key row = key) ordered |> Option.defaultValue current
            let movedWithSnapshot =
                table.First > 0 && not (obj.ReferenceEquals(table.Items, ordered))
                && current < table.Items.Length && Some (state.Key table.Items[current]) = requested
            let first = if movedWithSnapshot then max 0 (index - (current - table.First)) else table.First
            let struct (actual, _) = refreshSortable columns headerIds indicatorStyle visibleRows source index first false state table
            publish ordered actual)
        table

    /// Bind a table constructor to reactive snapshots and a writable selected key in the current owner.
    /// An empty snapshot clears selection. A missing key selects the nearest remaining index.
    let bindSortable columns headerIds indicatorStyle visibleRows readRows selectedKey state createTable =
        bindSortableFrom 0 columns headerIds indicatorStyle visibleRows readRows selectedKey state createTable
