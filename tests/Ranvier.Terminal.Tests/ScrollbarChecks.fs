module ScrollbarChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private checkDragging () =
    use graph = new Graph()
    let equal expected actual message =
        if expected <> actual then failwithf "%s: expected %A, got %A" message expected actual
    let size width height = [ Width(Cells width); Height(Cells height) ]
    let viewportStyle width height =
        size width height @
        [ Margin { Style.zero with Left = 2; Top = 1 }
          Css.scrollbarTrack "." Paint.empty; Css.scrollbarThumb "#" Paint.empty ]
    let block = Node.create (size 22 16) [||]
    let viewport = Node.create (viewportStyle 12 9) [| block |]
    let sibling = Node.create (size 10 16) [||]
    let mutable moves, releases = 0, 0
    sibling.OnEvent <- function
        | Mouse m when m.Action = MouseAction.Move -> moves <- moves + 1; true
        | Mouse m when m.Action = MouseAction.Release -> releases <- releases + 1; true
        | _ -> false
    let root = Node.create [ Direction Row ] [| viewport; sibling |]
    let mounted = View.mount graph (fun () -> root)
    let frame = Frame.create 40 20
    let draw () = Layout.renderReactive mounted frame |> ignore
    let send action x y =
        let handled = View.dispatch (Mouse { X=x; Y=y; Button=0; Delta=0; Action=action; Modifiers=Modifiers.None }) mounted
        draw ()
        handled
    let press x y = equal true (send MouseAction.Press x y) "gutter press handled"
    let move x y = send MouseAction.Move x y |> ignore
    let release x y = send MouseAction.Release x y |> ignore
    let verticalX () = viewport.Content.X + viewport.Content.Width
    let horizontalY () = viewport.Content.Y + viewport.Content.Height
    let glyph x y = frame.Cells[y * frame.Width + x].Glyph
    let pressVertical cell = press (verticalX ()) (viewport.Content.Y + cell)
    let pressHorizontal cell = press (viewport.Content.X + cell) (horizontalY ())
    let reset () = Node.setScroll 0 0 viewport; draw ()
    try
        draw ()
        equal (true, true) (viewport.HorizontalScrollbar, viewport.VerticalScrollbar) "two painted gutters"
        equal "#" (glyph (verticalX ()) (viewport.Content.Y + 1)) "grab inside proportional thumb"
        pressVertical 1
        move (verticalX ()) (viewport.Content.Y + 1)
        equal 0 viewport.ScrollY "stationary grab retains offset"
        move sibling.Bounds.X (viewport.Content.Y + 3)
        equal 4 viewport.ScrollY "vertical drag keeps the grabbed cell under the pointer"
        equal 0 moves "captured move bypasses sibling"
        move sibling.Bounds.X System.Int32.MaxValue
        equal viewport.MaxScrollY viewport.ScrollY "drag below viewport clamps at bottom"
        move sibling.Bounds.X System.Int32.MinValue
        equal 0 viewport.ScrollY "drag above viewport clamps at top"
        release sibling.Bounds.X sibling.Bounds.Y
        equal 0 releases "captured release bypasses sibling"
        move sibling.Bounds.X (sibling.Bounds.Y + 8)
        equal 1 moves "ordinary routing resumes after release"
        equal 0 viewport.ScrollY "released drag cannot move the viewport"

        Node.setScroll 0 1 viewport
        draw ()
        pressVertical 1
        move (verticalX ()) (viewport.Content.Y + 1)
        equal 1 viewport.ScrollY "stationary thumb preserves a scroll offset between painted cells"
        release (verticalX ()) (viewport.Content.Y + 1)
        let yCell = viewport.Content.Height - 2
        equal "." (glyph (verticalX ()) (viewport.Content.Y + yCell)) "vertical track cell"
        pressVertical yCell
        equal (yCell * viewport.MaxScrollY / (viewport.Content.Height - 1)) viewport.ScrollY "vertical track jump"
        release (verticalX ()) (viewport.Content.Y + yCell)
        let xCell = viewport.Content.Width - 2
        equal "." (glyph (viewport.Content.X + xCell) (horizontalY ())) "horizontal track cell"
        pressHorizontal xCell
        equal (xCell * viewport.MaxScrollX / (viewport.Content.Width - 1)) viewport.ScrollX "horizontal track jump"
        release (viewport.Content.X + xCell) (horizontalY ())
        reset ()
        pressHorizontal 1
        move System.Int32.MaxValue sibling.Bounds.Y
        equal (viewport.MaxScrollX, 0) (viewport.ScrollX, viewport.ScrollY) "horizontal drag clamps right without vertical movement"
        move System.Int32.MinValue sibling.Bounds.Y
        equal 0 viewport.ScrollX "horizontal drag clamps left"
        release sibling.Bounds.X sibling.Bounds.Y
        equal viewport.MaxScrollX viewport.ScrollX "release applies its final coordinate outside the gutter"

        pressVertical 3
        Node.setStyle (viewportStyle 10 7) viewport
        Node.setStyle (size 32 60) block
        draw ()
        equal "#" (glyph (verticalX ()) viewport.Content.Y) "resized thumb starts at top"
        equal "." (glyph (verticalX ()) (viewport.Content.Y + 1)) "resized thumb occupies one cell"
        move sibling.Bounds.X (viewport.Content.Y + 2)
        equal "#" (glyph (verticalX ()) (viewport.Content.Y + 2)) "grab offset clamps to resized thumb"
        move sibling.Bounds.X System.Int32.MaxValue
        equal 54 viewport.ScrollY "drag clamps against updated content and viewport"
        release sibling.Bounds.X sibling.Bounds.Y

        let expectCaptureCleared change =
            reset ()
            pressVertical 0
            change ()
            draw ()
            let before = moves
            move sibling.Bounds.X (sibling.Bounds.Y + 4)
            equal (before + 1) moves "cancelled capture allows sibling input"
            equal 0 viewport.ScrollY "cancelled capture leaves original viewport alone"
        expectCaptureCleared (fun () -> View.dispatch (Focus false) mounted |> ignore)
        View.dispatch (Focus true) mounted |> ignore
        expectCaptureCleared (fun () -> Node.setStyle (viewportStyle 10 7 @ [ Visible false ]) viewport)
        Node.setStyle (viewportStyle 10 7) viewport
        draw ()
        expectCaptureCleared (fun () -> Node.setStyle (viewportStyle 10 7 @ [ Css.scrollbarVisibility ScrollbarVisibility.Hidden ]) viewport)
        Node.setStyle (viewportStyle 10 7) viewport
        draw ()
        expectCaptureCleared (fun () -> Node.setStyle (size 2 2) block)
        Node.setStyle (size 32 60) block
        draw ()
        expectCaptureCleared (fun () -> Node.setChildren [| sibling |] root)
    finally
        View.dispose mounted

let private checkFocusReveal () =
    use graph = new Graph()
    let sized width height = [ Width(Cells width); Height(Cells height) ]
    let control glyph =
        let node = Node.create (sized 1 1) [||]
        Node.setText glyph node
        node.Focusable <- true
        node
    let first, last = control "A", control "Z"
    let inner = Node.create (Direction Row :: sized 5 3)
                    [| Node.create (sized 12 1) [||]; last |]
    let outer = Node.create (sized 6 4)
                    [| first; Node.create (sized 1 9) [||]; inner |]
    let unrelated = Node.create (sized 4 4) [| Node.create (sized 1 12) [||] |]
    let root = Node.create [ Direction Row ] [| outer; unrelated |]
    let mounted = View.mount graph (fun () -> root)
    let frame = Frame.create 12 5
    let draw () = Layout.renderReactive mounted frame |> ignore
    let tab modifiers =
        View.dispatch (Key { Key = Key.Tab; Scalar = 0; Modifiers = modifiers; Released = false; Paste = false }) mounted
        |> ignore
        draw ()
    let expectVisible (node: Node) =
        if not node.Focused || node.Clip <> node.Bounds || node.Clip.Width <> 1 ||
           frame.Cells[node.Bounds.Y * frame.Width + node.Bounds.X].Glyph <> node.Text.Source then
            failwithf "Focused control %s was not painted at its complete bounds" node.Text.Source
    try
        Node.setScroll 0 3 unrelated
        tab Modifiers.Shift
        expectVisible last
        if inner.ScrollX <> 8 || outer.ScrollY <> 7 || unrelated.ScrollY <> 3 then
            failwithf "Nested reveal offsets: inner %d, outer %d, unrelated %d" inner.ScrollX outer.ScrollY unrelated.ScrollY
        tab Modifiers.Shift
        expectVisible first
        if outer.ScrollY <> 0 || inner.ScrollX <> 8 then failwith "Reverse navigation moved the wrong viewport"
        tab Modifiers.None
        expectVisible last
        Node.setScroll 0 0 outer
        draw ()
        if outer.ScrollY <> 0 then failwith "Manual scroll was overridden by unchanged focus"
        Node.setStyle (sized 5 3) inner
        Node.setChildren [| Node.create (sized 1 12) [||]; last |] inner
        Node.setChildren [| first; Node.create (sized 1 9) [||]; inner; Node.create (sized 1 20) [||] |] outer
        View.focus (Some first) mounted
        draw ()
        tab Modifiers.None
        expectVisible last
        if inner.ScrollY <> 10 || outer.ScrollY <> 9 then
            failwithf "Nested vertical reveal overscrolled: inner %d, outer %d" inner.ScrollY outer.ScrollY
    finally
        View.dispose mounted

let run () =
    checkFocusReveal ()
    checkDragging ()
    use graph = new Graph()
    let rows = Array.init 9 (fun i ->
        let row = Node.create [Height(Cells 1)] [||]
        Node.setText (string i) row
        row)
    let root = Node.create [] rows
    let mounted = View.mount graph (fun () -> root)
    try
        let track = { Paint.empty with Foreground = Rgb 0x123456 }
        let thumb = { Paint.empty with Background = Rgb 0xabcdef }
        mounted.Rules <- [|Rule.all [ Css.scrollbarTrack "." track; Css.scrollbarThumb "#" thumb ]|]
        let caps = { Colors=TrueColor; SynchronizedOutput=false }
        let output = Renderer.create 7 4 8192 caps
        let renderer = PreparedRenderer.create 7 4 { Glyphs=128; Paints=32; Utf8Bytes=4096; OutputBytes=8192 } caps
        let cache = PreparedLayout.create mounted renderer
        let render () =
            Layout.render mounted output.Back
            PreparedLayout.refresh false cache
            PreparedLayout.draw cache
            for y = 0 to output.Back.Height - 1 do
                for x = 0 to output.Back.Width - 1 do
                    if not (Cell.equal output.Back.Cells[y * output.Back.Width + x] (PreparedRenderer.cell x y false renderer)) then
                        failwith "Scrollbar differs between renderers"
        let cell x y = output.Back.Cells[y * output.Back.Width + x]
        render ()
        if (cell 6 0).Glyph <> "#" || (cell 6 0).Paint <> thumb then failwith "Initial thumb"
        if (cell 6 3).Glyph <> "." || (cell 6 3).Paint <> track then failwith "Track styling"
        Node.setScroll 0 999 root
        render ()
        if (cell 6 3).Glyph <> "#" then failwith "Thumb did not reach last track cell"
        Renderer.resize 7 10 output
        PreparedRenderer.resize 7 10 renderer
        render ()
        if (cell 6 0).Glyph <> " " then failwith "Unnecessary scrollbar after resize"
        Renderer.resize 7 4 output
        PreparedRenderer.resize 7 4 renderer
        Node.setStyle [ Css.scrollbarVisibility ScrollbarVisibility.Hidden ] root
        render ()
        if (cell 6 0).Glyph <> " " then failwith "Hidden scrollbar was painted"
        Node.setStyle [] root
        let block = Node.create [ Width(Cells 7); Height(Cells 6) ] [||]
        Node.setText (System.String.Join("\n", Array.create 6 "abcdefg")) block
        Node.setChildren [|block|] root
        render ()
        if root.Content.Width <> 6 || root.Content.Height <> 3 then failwith "Coupled gutters"
        let wheel modifiers =
            Mouse { X=5; Y=1; Action=MouseAction.Scroll; Button=0; Delta=System.Int32.MaxValue; Modifiers=modifiers }
        View.dispatch (wheel Modifiers.Shift) mounted |> ignore
        View.dispatch (wheel Modifiers.None) mounted |> ignore
        render ()
        if (cell 5 2).Glyph <> "g" || (cell 5 3).Glyph <> "#" || (cell 6 2).Glyph <> "#" then
            failwith "Both scroll endpoints and final content cell must be visible"
        Node.setChildren [||] root
        Node.setText "a long single-line label" root
        render ()
        if root.HorizontalScrollbar || root.VerticalScrollbar then failwith "Clipped label gained a gutter"
        Node.setText "" root
        Node.setChildren rows root
        Renderer.resize 7 7 output
        PreparedRenderer.resize 7 7 renderer
        mounted.Rules <- [|Rule.all (Css.tableScrollbar track thumb)|]
        Node.setScroll 0 1 root
        render ()
        for y = 0 to 6 do
            let glyph, paint = if y = 3 then "█", thumb else "│", track
            if (cell 6 y).Glyph <> glyph || (cell 6 y).Paint <> paint then failwith "Single-cell table scrollbar"
        mounted.Rules <- [|Rule.all (Css.tableScrollbar track thumb @ [Css.scrollbarThumbSize ScrollbarThumbSize.Proportional])|]
        render ()
        for y = 0 to 6 do
            if (cell 6 y).Glyph <> (if y >= 1 && y <= 5 then "█" else "│") then failwith "Proportional thumb size override"
        Node.setScroll 0 2 root
        mounted.Rules <- [|Rule.all (Css.tableScrollbar track thumb @ [Css.scrollbarThumb "◆" thumb])|]
        render ()
        if (cell 6 6).Glyph <> "◆" || (cell 6 5).Glyph <> "│" then failwith "Table preset thumb override or endpoint"
    finally View.dispose mounted
