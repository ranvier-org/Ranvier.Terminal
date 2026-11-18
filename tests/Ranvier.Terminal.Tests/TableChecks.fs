module TableChecks

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    let equal expected actual description =
        if expected <> actual then failwithf "%s: expected %A, got %A" description expected actual

    use graph = new Graph()
    graph.Run(fun () ->
        let mutable lastNavigation = struct (-1, -1)
        let navigate selected first = lastNavigation <- struct (selected, first)
        let table = Table.createWithThumb [] [ Foreground(Rgb 1) ] [ Background(Rgb 2) ] [ Foreground(Rgb 3) ] [ Foreground(Rgb 4) ] navigate
        let mutable formatted = 0
        let columns =
            [| { Header = "ID"; Width = Cells 4; Style = fun _ _ -> [ Foreground(Rgb 5) ]
                 Text = fun index -> formatted <- formatted + 1; string index }
               { Header = "Text"; Width = Flex 1; Style = fun _ _ -> [ Foreground(Rgb 6) ]
                 Text = fun _ -> "界" } |]
        let items = [| 0 .. 999 |]
        let update capacity values selected first = Table.update columns capacity values selected first table
        equal (struct (0, 0)) (update 3 items 0 0) "initial viewport"
        equal 3 formatted "formatter calls bounded by viewport"
        let slots = table.Root.Children
        let mounted = View.mount graph (fun () -> table.Root)
        try
            let frame = Frame.create 12 4
            let draw () = Layout.renderReactive mounted frame |> ignore
            let cell x y = frame.Cells[y * frame.Width + x]
            draw ()
            equal "I" (cell 0 0).Glyph "header"
            equal (Rgb 1) (cell 0 0).Paint.Foreground "header paint"
            equal (Rgb 5) (cell 0 1).Paint.Foreground "first column paint"
            equal (Rgb 6) (cell 4 1).Paint.Foreground "second column paint"
            equal (Rgb 2) (cell 9 1).Paint.Background "selected row fill"
            equal 2uy (cell 4 1).Width "wide cell"
            equal (Rgb 4) (cell 11 1).Paint.Foreground "thumb paint"
            equal (Rgb 3) (cell 11 2).Paint.Foreground "track paint"
            equal "│" (cell 11 2).Glyph "table preset retains its track"
            let key code released paste =
                Key { Key = code; Scalar = 0; Modifiers = Modifiers.None; Released = released; Paste = paste }
            let mouse x y action delta =
                Mouse { X = x; Y = y; Action = action; Delta = delta; Button = 0; Modifiers = Modifiers.None }
            let dispatch event = View.dispatch event mounted
            equal true (dispatch (mouse 1 2 MouseAction.Press 0)) "row click handled"
            equal (struct (1, 0)) lastNavigation "row click selection"
            equal 5 formatted "only selection endpoints refreshed"
            update 3 items 1 0 |> ignore
            equal 5 formatted "owner synchronization reuses prepared rows"
            equal false (dispatch (key Key.End true false)) "key release"
            equal false (dispatch (key Key.End false true)) "pasted navigation"
            equal true (dispatch (key Key.PageDown false false)) "page down"
            equal (struct (4, 2)) lastNavigation "page down result"
            equal true (dispatch (mouse 11 3 MouseAction.Press 0)) "thumb track click"
            equal (struct (997, 997)) lastNavigation "track jump"
            draw ()
            equal "█" (cell 11 3).Glyph "last thumb position"
            dispatch (mouse 11 3 MouseAction.Release 0) |> ignore
            equal true (dispatch (key Key.End false false)) "end"
            equal (struct (999, 997)) lastNavigation "end result"
            equal true (dispatch (mouse 4 2 MouseAction.Scroll Int32.MinValue)) "wheel"
            equal (struct (2, 0)) lastNavigation "wheel keeps selection visible"
            equal true (obj.ReferenceEquals(slots, table.Root.Children)) "stable retained slots"
            equal (struct (0, 0)) (update 2 [||] 999 997) "empty resize"
            draw ()
            equal " " (cell 0 1).Glyph "empty text cleared"
            equal false (dispatch (key Key.Down false false)) "empty navigation"
            equal 3 table.Root.Children.Length "only resized viewport retained"
            let small = [|0; 1; 2|]
            update 3 small 0 0 |> ignore
            draw ()
            equal false table.Root.VerticalScrollbar "no scrollbar when Tasks-style rows fit"
            equal frame.Width table.Root.Content.Width "fitting table gives gutter column back"
            equal false (dispatch (mouse 1 2 MouseAction.Scroll 1)) "fitting table lets an ancestor scroll"
            Layout.render mounted (Frame.create frame.Width 2)
            equal true table.Root.VerticalScrollbar "physical overflow shown even if all rows are materialized"
            equal true (dispatch (mouse 1 1 MouseAction.Scroll 1)) "constrained table scrolls materialized rows"
            equal 1 table.Root.ScrollY "materialized content offset advanced"
            draw ()
            let track = { Paint.empty with Foreground = Rgb 9 }
            let thumb = { Paint.empty with Foreground = Rgb 10 }
            Node.setStyle [ Css.scrollbarTrack "." track; Css.scrollbarThumb "#" thumb ] table.Root
            update 3 [|0..3|] 0 0 |> ignore
            draw ()
            equal true table.Root.VerticalScrollbar "overflow exposes a table scrollbar"
            equal " " (cell 11 0).Glyph "track starts below fixed header"
            equal [|"#"; "#"; "."|] [| for y in 1..3 -> (cell 11 y).Glyph |] "CSS glyphs and proportional thumb"
            equal (Rgb 10) (cell 11 1).Paint.Foreground "CSS thumb paint"
            equal (Rgb 9) (cell 11 3).Paint.Foreground "CSS track paint"
            Node.setStyle [ Css.scrollbarVisibility ScrollbarVisibility.Hidden ] table.Root
            draw ()
            equal false table.Root.VerticalScrollbar "hidden scrollbar releases its gutter on restyle"
            equal true (dispatch (mouse 1 2 MouseAction.Scroll 1)) "hidden overflowing table still navigates"
            Node.setStyle (Css.tableScrollbar track thumb) table.Root
            update 3 items 0 0 |> ignore
            draw ()
            equal true (dispatch (mouse 11 1 MouseAction.Press 0)) "grab fixed-header table thumb"
            let refreshed = [| 1000..1019 |]
            update 2 refreshed 0 0 |> ignore
            Layout.render mounted (Frame.create 9 3)
            equal true (dispatch (mouse 40 15 MouseAction.Move 0)) "drag continues outside the resized table"
            equal (struct (18,18)) lastNavigation "drag uses refreshed source length and resized capacity"
            equal "1018" table.Root.Children[1].Children[0].Text.Source "visible rows follow dragged offset"
            equal true (dispatch (mouse 40 15 MouseAction.Release 0)) "release outside the table"
            dispatch (mouse 8 1 MouseAction.Move 0) |> ignore
            equal (struct (18,18)) lastNavigation "movement after release leaves table selection alone"
        finally
            View.dispose mounted)

    graph.Run(fun () ->
        let values = [| for i in 0..999 -> (i, 1000 - i) |]
        let mutable selected, first = 2, 0
        let navigate index offset = selected <- index; first <- offset
        let table = Table.create [] [] [] [] navigate
        let state =
            Table.sortable fst
                [| { Id = "id"; Descending = false; Compare = fun a b -> compare (fst a) (fst b) }
                   { Id = "amount"; Descending = true; Compare = fun a b -> compare (snd a) (snd b) } |] "id"
        let mutable formatted = 0
        let columns =
            [| { Header = "ID"; Width = Cells 5; Style = fun _ _ -> []
                 Text = fun row -> formatted <- formatted + 1; string (fst row) }
               { Header = "Amount"; Width = Cells 6; Text = snd >> string; Style = fun _ _ -> [] } |]
        let headers = [| Some "id"; Some "amount" |]
        let update items =
            let struct (index, offset) = Table.updateSortable columns headers [Foreground(Rgb 42)] 3 items selected first state table
            navigate index offset
        update values
        let slots = table.Root.Children
        let mounted = View.mount graph (fun () -> table.Root)
        try
            let frame = Frame.create 12 4
            let draw () = Layout.renderReactive mounted frame |> ignore
            let key c released paste =
                View.dispatch (Key { Key = Key.Character; Scalar = int c; Modifiers = Modifiers.None; Released = released; Paste = paste }) mounted
            draw ()
            equal "↑" frame.Cells[4].Glyph "active ascending header"
            equal (Rgb 42) frame.Cells[4].Paint.Foreground "indicator paint"
            equal true (View.dispatch (Mouse { X = 7; Y = 0; Action = MouseAction.Press; Button = 0; Delta = 0; Modifiers = Modifiers.None }) mounted) "click amount header"
            equal "amount" state.SortId "clicked numeric column"
            draw ()
            equal "↓" frame.Cells[10].Glyph "descending indicator survives narrow header"
            equal (2,998) (Table.order values state).[selected] "selected key after mouse sort"
            equal false (key 'r' true false) "released sort key"
            equal false (key 's' false true) "pasted sort key"
            equal true (key 'r' false false) "keyboard reversal"
            let reversed = Table.order values state
            equal 1 (snd reversed[0]) "sort by numeric amount"
            equal (2,998) reversed[selected] "selected key after reversal"
            let refreshed = [| (2,3); (1000,1); (10,5); (0,2) |]
            update refreshed
            equal 2 (fst (Table.order refreshed state).[selected]) "selected key after value and row-count changes"
            let removed = [| (10,5); (0,2) |]
            update removed
            equal 1 selected "removed key falls back to nearest remaining index"
            update [||]
            equal (0,0) (selected,first) "empty keyed table"
            equal true (key 's' false false) "sort controls remain active on an empty view"
            equal true (obj.ReferenceEquals(slots, table.Root.Children)) "sort retained viewport nodes"
            equal true (formatted <= 18) "only visible rows formatted on each update"
        finally
            View.dispose mounted)

    graph.Run(fun () ->
        let rows = createSignal [| 1 .. 8 |]
        let selected = createSignal (Some 5)
        let sort = Table.sortable id [| { Id = "id"; Compare = compare; Descending = false } |] "id"
        let columns = [| { Header = "ID"; Width = Flex 1; Text = string; Style = fun _ _ -> [] } |]
        let mounted, table = View.mountWithState graph (fun () ->
            let table =
                Table.create [] [] [] []
                |> Table.bindSortable columns [| Some "id" |] [] 3 (fun () -> rows.Value) selected sort
            table.Root, table)
        let draw () = Layout.renderReactive mounted (Frame.create 8 4) |> ignore
        try
            draw ()
            equal 2 table.FirstVisible "bound table reveals initial selection"
            rows.Value <- [| 0 .. 8 |]
            draw ()
            equal (Some 5, 3) (selected.Peek, table.FirstVisible) "new snapshot preserves the selected row's visible slot"
            selected.Value <- Some 2
            draw ()
            equal 2 table.FirstVisible "external selected key revealed"
            let key code scalar = Key { Key = code; Scalar = scalar; Released = false; Paste = false; Modifiers = Modifiers.None }
            Table.handle (key Key.Down 0) table |> ignore
            equal (Some 3) selected.Peek "navigation publishes the selected key"
            Table.handle (key Key.Character (int 'r')) table |> ignore
            equal (Some 3, 4) (selected.Peek, table.FirstVisible) "reversing preserves key and visible slot"
            rows.Value <- [| 4; 5; 6; 7 |]
            draw ()
            equal (Some 4, 1) (selected.Peek, table.FirstVisible) "removed key selects nearest surviving index"
            rows.Value <- [||]
            draw ()
            equal (None, 0) (selected.Peek, table.FirstVisible) "empty source clears key and scroll"
            Table.handle (key Key.Character (int 'r')) table |> ignore
            rows.Value <- [| 4; 5 |]
            draw ()
            equal (Some 4) selected.Peek "repopulation selects the first ordered row"
        finally View.dispose mounted
        rows.Value <- [||]
        equal (Some 4) selected.Peek "disposing the view releases its table binding")
