namespace Ranvier.Terminal.View

open Ranvier.Terminal
open Ranvier.Terminal.Css

module Layout =
    let private matches (selector: Selector) (node: Node) =
        let mutable matched = true

        match selector.Id with
        | Some id -> matched <- node.Id = id
        | None -> ()

        match selector.Class with
        | Some name ->
            let mutable found = false

            for i = 0 to node.Classes.Length - 1 do
                if node.Classes[i] = name then
                    found <- true

            matched <- matched && found
        | None -> ()

        match selector.Focused with
        | Some value -> matched <- matched && node.Focused = value
        | None -> ()

        match selector.Hovered with
        | Some value -> matched <- matched && node.Hovered = value
        | None -> ()

        matched

    let rec private measure force (mounted: Mounted) node =
        if force || node.LayoutDirty then measureDirty (force || node.ForceLayout) mounted node

    and private measureDirty force mounted node =
        mounted.MeasuredNodes <- mounted.MeasuredNodes + 1
        let rules = mounted.Rules
        let mutable style = node.Style

        for i = 0 to rules.Length - 1 do
            let rule = rules[i]

            if matches rule.Selector node then
                style <- Style.merge rule.Declarations style

        node.Computed <- style
        let mutable main, cross, count = 0, 0, 0

        for i = 0 to node.Children.Length - 1 do
            let child = node.Children[i]
            measure force mounted child

            if child.Computed.Visible && (not node.PopupHost || child.PopupAnchor.IsNone) then
                let margin = child.Computed.Margin
                let width = child.DesiredWidth + margin.Left + margin.Right
                let height = child.DesiredHeight + margin.Top + margin.Bottom
                main <-
                    match style.Direction with
                    | Overlay -> max main height
                    | Row -> main + width
                    | Column -> main + height
                cross <- max cross (if style.Direction = Row then height else width)
                count <- count + 1

        if style.Direction <> Overlay then main <- main + max 0 (count - 1) * style.Gap
        let border = if style.Border = NoBorder then 0 else 2

        let width =
            max node.Text.Width (if style.Direction = Row then main else cross)
            + (match node.VirtualScrollY with
               | ValueSome(struct (_, maximum)) when maximum > 0 && style.ScrollbarVisibility = ScrollbarVisibility.Auto -> 1
               | _ -> 0)
            + style.Padding.Left
            + style.Padding.Right
            + border

        let height =
            max
                (if node.Text.Glyphs.Length = 0 then 0 else node.Text.Height)
                (if style.Direction = Row then cross else main)
            + style.Padding.Top
            + style.Padding.Bottom
            + border

        node.DesiredWidth <-
            min
                style.MaxWidth
                (max
                    style.MinWidth
                    (match style.Width with
                     | Cells n -> n
                     | _ -> width))

        node.DesiredHeight <-
            min
                style.MaxHeight
                (max
                    style.MinHeight
                    (match style.Height with
                     | Cells n -> n
                     | _ -> height))

    let private length available desired value =
        match value with
        | Cells n -> n
        | Percent p -> int (int64 available * int64 p / 100L)
        | Auto -> desired
        | Flex _ -> 0

    let private wrappedHeight width (node: Node) =
        let style = node.Computed
        let border = if style.Border = NoBorder then 0 else 2
        let contentWidth = max 1 (width - style.Padding.Left - style.Padding.Right - border)
        let rows = TextRun.wrappedHeight contentWidth style.WordWrap node.Text

        max node.DesiredHeight (rows + style.Padding.Top + style.Padding.Bottom + border)

    let private mainDesired row crossAvailable (child: Node) =
        if row then
            child.DesiredWidth
        elif child.Computed.Wrap then
            let cs = child.Computed
            let available = max 0 (crossAvailable - cs.Margin.Left - cs.Margin.Right)

            let width =
                match cs.Width with
                | Auto
                | Flex _ -> available
                | value -> length available child.DesiredWidth value

            wrappedHeight width child
        else
            child.DesiredHeight

    let private offset available size alignment =
        match alignment with
        | Center -> max 0 ((available - size) / 2)
        | End -> max 0 (available - size)
        | _ -> 0

    let private crossSize row available alignment (child: Node) =
        let cs = child.Computed
        let struct (desired, size, minimum, maximum) =
            if row then struct (child.DesiredHeight, cs.Height, cs.MinHeight, cs.MaxHeight)
            else struct (child.DesiredWidth, cs.Width, cs.MinWidth, cs.MaxWidth)
        let cross =
            match size with
            | Flex _ -> available
            | Auto when alignment = Stretch -> available
            | _ -> length available desired size
        min maximum (max minimum cross)

    let private markRows (rows: bool array) (clip: Rect) =
        if not (isNull rows) then
            for y = max 0 clip.Y to min rows.Length (clip.Y + clip.Height) - 1 do rows[y] <- true

    let rec private visibleInHost host node =
        node.Computed.Visible && node.Clip.Width > 0 && node.Clip.Height > 0
        && (obj.ReferenceEquals(host, node)
            || (match node.Parent with Some parent -> visibleInHost host parent | None -> false))

    let private popupBounds host (viewport: Rect) anchor (popup: Node) =
        let target = match anchor with NodeAnchor node | CaretAnchor node -> node
        let empty = { viewport with Width = 0; Height = 0 }
        if not (visibleInHost host target) then empty
        else
            let origin =
                match anchor with
                | NodeAnchor _ -> target.Clip
                | CaretAnchor _ ->
                    let style = target.Computed
                    let edge = if style.Border = NoBorder then 0 else 1
                    let width = max 0 (target.Bounds.Width - 2 * edge - style.Padding.Left - style.Padding.Right)
                    let x = target.Bounds.X + edge + style.Padding.Left + target.CaretColumn - target.ScrollX
                    let y = target.Bounds.Y + edge + style.Padding.Top
                    if target.CaretColumn < 0 || width = 0 || not (Rect.contains x y target.Clip) then empty
                    else { X = x; Y = y; Width = 1; Height = 1 }
            if origin.Width = 0 || origin.Height = 0 then empty
            else
                let dimension available desired minimum maximum size =
                    let value = match size with Flex _ -> available | _ -> length available desired size
                    min available (min maximum (max minimum value))
                let style = popup.Computed
                let width = dimension viewport.Width popup.DesiredWidth style.MinWidth style.MaxWidth style.Width
                let desired = if style.Wrap then wrappedHeight width popup else popup.DesiredHeight
                let height = dimension viewport.Height desired style.MinHeight style.MaxHeight style.Height
                let below = origin.Y + origin.Height
                let roomBelow = viewport.Y + viewport.Height - below
                let roomAbove = origin.Y - viewport.Y
                let above = height > roomBelow && roomAbove > roomBelow
                { X = max viewport.X (min origin.X (viewport.X + viewport.Width - width))
                  Y = max viewport.Y (min (if above then origin.Y - height else below) (viewport.Y + viewport.Height - height))
                  Width = width
                  Height = height }

    let private linearMetrics (node: Node) (style: Style) (content: Rect) =
        let row = style.Direction = Row
        let available = if row then content.Width else content.Height
        let crossLimit = if row then content.Height else content.Width
        let mutable used, weight, count = 0, 0, 0
        let mutable crossExtent = 0

        for i = 0 to node.Children.Length - 1 do
            let child = node.Children[i]
            let cs = child.Computed

            if cs.Visible then
                let size = if row then cs.Width else cs.Height
                let desired = mainDesired row content.Width child
                let crossMargin =
                    if row then cs.Margin.Top + cs.Margin.Bottom else cs.Margin.Left + cs.Margin.Right
                let cross = crossSize row (max 0 (crossLimit - crossMargin)) style.Align child
                crossExtent <- max crossExtent (cross + crossMargin)

                let margin =
                    if row then
                        cs.Margin.Left + cs.Margin.Right
                    else
                        cs.Margin.Top + cs.Margin.Bottom

                match size with
                | Flex n -> weight <- weight + n
                | _ -> ()

                used <- used + length available desired size + margin
                count <- count + 1

        used <- used + max 0 (count - 1) * style.Gap

        let textWidth = if style.Wrap then min content.Width node.Text.Width else node.Text.Width
        let textHeight =
            if node.Text.Glyphs.Length = 0 then 0
            elif style.Wrap && content.Width > 0 then TextRun.wrappedHeight content.Width style.WordWrap node.Text
            else node.Text.Height
        let width = max textWidth (if row then used else crossExtent)
        let height = max textHeight (if row then crossExtent else used)
        struct (used, weight, max 0 (width - content.Width), max 0 (height - content.Height))

    let rec private arrange force rows bounds clip node =
        let nextClip = Rect.intersect clip bounds
        if force || node.LayoutDirty || node.Bounds <> bounds || node.Clip <> nextClip then
            if node.Bounds <> bounds || node.Clip <> nextClip then
                markRows rows node.Clip
                markRows rows nextClip
                Node.invalidatePaint node
            arrangeDirty (force || node.ForceLayout) rows bounds clip node

    and private arrangeDirty force rows bounds clip node =
        node.Bounds <- bounds
        node.Clip <- Rect.intersect clip bounds
        let style = node.Computed
        let edge = if style.Border = NoBorder then 0 else 1

        let content =
            { X = bounds.X + edge + style.Padding.Left
              Y = bounds.Y + edge + style.Padding.Top
              Width = max 0 (bounds.Width - edge * 2 - style.Padding.Left - style.Padding.Right)
              Height = max 0 (bounds.Height - edge * 2 - style.Padding.Top - style.Padding.Bottom) }

        let childClip = Rect.intersect node.Clip content
        if style.Direction = Overlay then
            node.Content <- content
            node.VerticalScrollbar <- false
            node.HorizontalScrollbar <- false
            node.MaxScrollX <- 0
            node.MaxScrollY <- 0
            node.ScrollX <- 0
            node.ScrollY <- 0
            node.VisibleStart <- node.Children.Length
            node.VisibleEnd <- -1
            for i = 0 to node.Children.Length - 1 do
                let child = node.Children[i]
                let cs = child.Computed
                if cs.Visible then
                    let areaWidth = max 0 (content.Width - cs.Margin.Left - cs.Margin.Right)
                    let areaHeight = max 0 (content.Height - cs.Margin.Top - cs.Margin.Bottom)
                    let dimension available desired alignment = function
                        | Flex _ -> available
                        | Auto when alignment = Stretch -> available
                        | size -> length available desired size
                    let width = dimension areaWidth child.DesiredWidth style.Align cs.Width |> max cs.MinWidth |> min cs.MaxWidth
                    let desiredHeight = if cs.Wrap then wrappedHeight width child else child.DesiredHeight
                    let height = dimension areaHeight desiredHeight style.Justify cs.Height |> max cs.MinHeight |> min cs.MaxHeight
                    let overlayRect =
                        { X = content.X + cs.Margin.Left + offset areaWidth width style.Align
                          Y = content.Y + cs.Margin.Top + offset areaHeight height style.Justify
                          Width = width
                          Height = height }
                    let rect =
                        if not node.PopupHost then overlayRect
                        else
                            match child.PopupAnchor with
                            | Some anchor -> popupBounds node childClip anchor child
                            | None -> content
                    if child.Parent.IsNone || not (obj.ReferenceEquals(child.Parent.Value,node)) then child.Parent <- Some node
                    arrange force rows rect childClip child
                    if child.Clip.Width > 0 && child.Clip.Height > 0 then
                        node.VisibleStart <- min node.VisibleStart i
                        node.VisibleEnd <- i
                else
                    markRows rows child.Clip
                    child.Bounds <- { X=0; Y=0; Width=0; Height=0 }
                    child.Clip <- child.Bounds
        else
            arrangeLinear force rows node style content childClip
        if node.Focused && node.CaretColumn >= 0 && content.Width > 0 then
            node.ScrollX <- max (node.CaretColumn - content.Width + 1) (min node.ScrollX node.CaretColumn)

    and private arrangeLinear force rows node style area childClip =
        let row = style.Direction = Row
        let eligible = style.ScrollbarVisibility = ScrollbarVisibility.Auto
        let mutable vertical, horizontal = false, false
        let mutable content = area
        let mutable metrics = linearMetrics node style content
        let mutable settled = false
        while not settled do
            let struct (_, _, maxX, maxY) = metrics
            let rangeY =
                match node.VirtualScrollY with
                | ValueSome(struct (_, maximum)) -> maximum
                | ValueNone -> maxY
            let needsVertical =
                eligible && rangeY > 0 && area.Width > max 1 node.VirtualContentWidth && area.Height > 0 &&
                (node.Children.Length > 0 || (area.Height > 1 && node.CaretColumn < 0))
            let needsHorizontal =
                eligible && maxX > 0 && node.Children.Length > 0 && area.Height > 1 && area.Width > 0
            if (needsVertical && not vertical) || (needsHorizontal && not horizontal) then
                vertical <- vertical || needsVertical
                horizontal <- horizontal || needsHorizontal
                content <- { area with Width = area.Width - (if vertical then 1 else 0)
                                       Height = area.Height - (if horizontal then 1 else 0) }
                metrics <- linearMetrics node style content
            else settled <- true
        node.Content <- content
        node.VerticalScrollbar <- vertical
        node.HorizontalScrollbar <- horizontal
        let childClip = Rect.intersect childClip content
        let struct (used, weight, maxX, maxY) = metrics
        let available = if row then content.Width else content.Height
        if node.MaxScrollX <> maxX || node.MaxScrollY <> maxY then Node.invalidatePaint node
        node.MaxScrollX <- maxX
        node.MaxScrollY <- maxY
        node.ScrollX <- min node.MaxScrollX (max 0 node.ScrollX)
        node.ScrollY <- min node.MaxScrollY (max 0 node.ScrollY)
        let free = max 0 (available - used)

        let mutable position =
            if weight = 0 then
                offset available used style.Justify
            else
                0

        let mutable remaining, remainingWeight = free, weight
        node.VisibleStart <- node.Children.Length
        node.VisibleEnd <- -1

        for i = 0 to node.Children.Length - 1 do
            let child = node.Children[i]
            let cs = child.Computed

            if cs.Visible then
                let size = if row then cs.Width else cs.Height
                let desired = mainDesired row content.Width child

                let main =
                    match size with
                    | Flex n ->
                        let share =
                            if remainingWeight = 0 then
                                0
                            else
                                int (int64 remaining * int64 n / int64 remainingWeight)

                        remaining <- remaining - share
                        remainingWeight <- remainingWeight - n
                        share
                    | _ -> length available desired size

                let main =
                    if row then
                        min cs.MaxWidth (max cs.MinWidth main)
                    else
                        min cs.MaxHeight (max cs.MinHeight main)

                let crossAvailable =
                    max
                        0
                        (if row then
                             content.Height - cs.Margin.Top - cs.Margin.Bottom
                         else
                             content.Width - cs.Margin.Left - cs.Margin.Right)

                let cross = crossSize row crossAvailable style.Align child

                let before = if row then cs.Margin.Left else cs.Margin.Top
                let after = if row then cs.Margin.Right else cs.Margin.Bottom
                position <- position + before

                let childBounds =
                    if row then
                        { X = content.X + position - node.ScrollX
                          Y =
                            content.Y + cs.Margin.Top + offset crossAvailable cross style.Align
                            - node.ScrollY
                          Width = main
                          Height = cross }
                    else
                        { X =
                            content.X + cs.Margin.Left + offset crossAvailable cross style.Align
                            - node.ScrollX
                          Y = content.Y + position - node.ScrollY
                          Width = cross
                          Height = main }

                if child.Parent.IsNone || not (obj.ReferenceEquals(child.Parent.Value, node)) then
                    child.Parent <- Some node

                arrange force rows childBounds childClip child
                if child.Clip.Width > 0 && child.Clip.Height > 0 then
                    node.VisibleStart <- min node.VisibleStart i
                    node.VisibleEnd <- i
                position <- position + main + after + style.Gap
            else
                markRows rows child.Clip
                child.Bounds <- { X = 0; Y = 0; Width = 0; Height = 0 }
                child.Clip <- child.Bounds

    let internal scrollbarPaint (parent: Paint) (part: ScrollbarPart) =
        let ink = part.Paint
        { ink with
            Foreground = if ink.Foreground = Default then parent.Foreground else ink.Foreground
            Background = if ink.Background = Default then parent.Background else ink.Background }

    let inline internal paintScrollbars (node: Node) put =
        if node.VerticalScrollbar || node.HorizontalScrollbar then
            let inline draw vertical =
                match Node.scrollbarGeometry vertical node with
                | ValueNone -> ()
                | ValueSome(struct (bounds, length, _, _, start, count)) ->
                    for i = 0 to length - 1 do
                        let x = if vertical then bounds.X else bounds.X + i
                        let y = if vertical then bounds.Y + i else bounds.Y
                        put x y (i >= start && i - start < count)
            draw true
            draw false

    let private borders border =
        match border with
        | Double -> struct ("╔", "╗", "╚", "╝", "═", "║")
        | Rounded -> struct ("╭", "╮", "╰", "╯", "─", "│")
        | Ascii -> struct ("+", "+", "+", "+", "-", "|")
        | _ -> struct ("┌", "┐", "└", "┘", "─", "│")

    let rec private draw inherited damage frame node =

        let nodeClip = Rect.intersect node.Clip damage

        let style = node.Computed



        if style.Visible && nodeClip.Width > 0 && nodeClip.Height > 0 then

            let paint =

                { style.Paint with

                    Foreground =

                        if node.InheritColors && style.Paint.Foreground = Default then

                            inherited.Foreground

                        else

                            style.Paint.Foreground

                    Background =

                        if node.InheritColors && style.Paint.Background = Default then

                            inherited.Background

                        else

                            style.Paint.Background }



            let bounds = node.Bounds

            Frame.fill nodeClip paint frame

            let edge = if style.Border = NoBorder then 0 else 1



            if edge > 0 && bounds.Width >= 2 && bounds.Height >= 2 then

                let struct (tl, tr, bl, br, horizontal, vertical) = borders style.Border



                for x = bounds.X + 1 to bounds.X + bounds.Width - 2 do

                    Frame.put nodeClip x bounds.Y horizontal 1 paint frame

                    Frame.put nodeClip x (bounds.Y + bounds.Height - 1) horizontal 1 paint frame



                for y = bounds.Y + 1 to bounds.Y + bounds.Height - 2 do

                    Frame.put nodeClip bounds.X y vertical 1 paint frame

                    Frame.put nodeClip (bounds.X + bounds.Width - 1) y vertical 1 paint frame



                Frame.put nodeClip bounds.X bounds.Y tl 1 paint frame

                Frame.put nodeClip (bounds.X + bounds.Width - 1) bounds.Y tr 1 paint frame

                Frame.put nodeClip bounds.X (bounds.Y + bounds.Height - 1) bl 1 paint frame

                Frame.put nodeClip (bounds.X + bounds.Width - 1) (bounds.Y + bounds.Height - 1) br 1 paint frame



            let content = node.Content



            if node.Focused && node.CaretColumn >= 0 && content.Width > 0 then

                if node.CaretColumn < node.ScrollX then

                    node.ScrollX <- node.CaretColumn

                elif node.CaretColumn >= node.ScrollX + content.Width then

                    node.ScrollX <- node.CaretColumn - content.Width + 1



            Frame.textWithWrapping

                (Rect.intersect nodeClip content)

                (content.X - node.ScrollX)

                (content.Y - node.ScrollY)

                content.Width

                style.Wrap

                style.WordWrap

                node.Text

                paint

                frame



            for i = node.VisibleStart to node.VisibleEnd do

                draw paint damage frame node.Children[i]

            paintScrollbars node (fun x y thumb ->
                let part = if thumb then style.ScrollbarThumb else style.ScrollbarTrack
                Frame.put nodeClip x y part.Glyph 1 (scrollbarPaint paint part) frame)



            if node.Focused && node.CaretColumn >= 0 then

                let x, y = content.X + node.CaretColumn - node.ScrollX, content.Y

                let clip = Rect.intersect nodeClip content



                if Rect.contains x y clip then

                    let cell = frame.Cells[y * frame.Width + x]



                    let caretPaint =

                        { cell.Paint with

                            Attributes = cell.Paint.Attributes ^^^ Attributes.Reverse }



                    Frame.put clip x y cell.Glyph (int cell.Width) caretPaint frame



    let private arrangeView force rows viewport mounted =
        arrange force rows viewport viewport mounted.Root
        if View.revealFocus mounted then
            arrange false rows viewport viewport mounted.Root

    let private renderCore force mounted frame =
        if mounted.Disposed then
            invalidOp "The view has been disposed."

        mounted.MeasuredNodes <- 0
        measure force mounted mounted.Root
        arrangeView true null (Frame.bounds frame) mounted
        View.validateScrollbarDrag mounted
        Frame.clear Paint.empty frame
        draw Paint.empty (Frame.bounds frame) frame mounted.Root

    let render mounted frame =
        mounted.LastFrame <- None
        renderCore true mounted frame

    let rec private clean full node =
        if full || node.SubtreeDirty then
            let descendants = full || node.ForceLayout
            node.PaintDirty <- false
            node.LayoutDirty <- false
            node.ForceLayout <- false
            node.SubtreeDirty <- false
            for child in node.Children do
                if child.Parent.IsNone || not (obj.ReferenceEquals(child.Parent.Value, node)) then child.Parent <- Some node
                clean descendants child

    let rec private damageRows (rows: bool array) (top: byref<int>) (bottom: byref<int>) node =
        if node.SubtreeDirty then
            if node.PaintDirty && node.Clip.Height > 0 then
                top <- min top node.Clip.Y
                bottom <- max bottom (node.Clip.Y + node.Clip.Height)
                for y = max 0 node.Clip.Y to min rows.Length (node.Clip.Y + node.Clip.Height) - 1 do rows[y] <- true
            for child in node.Children do damageRows rows &top &bottom child

    let rec private refreshPaintRules rules node =
        if node.SubtreeDirty then
            if node.PaintDirty then
                let mutable style = node.Style
                for rule in rules do
                    if matches rule.Selector node then style <- Style.merge rule.Declarations style
                node.Computed <- style
            for child in node.Children do refreshPaintRules rules child

    let plan force mounted (viewport: Rect) =
        if mounted.Disposed then invalidOp "The view has been disposed."
        let force = force || not (obj.ReferenceEquals(mounted.LastRules, mounted.Rules))
        mounted.MeasuredNodes <- 0
        measure force mounted mounted.Root
        arrangeView force null viewport mounted
        if mounted.Rules.Length > 0 then refreshPaintRules mounted.Rules mounted.Root
        View.validateScrollbarDrag mounted
        clean force mounted.Root
        mounted.LastRules <- mounted.Rules
        mounted.LastFrame <- None

    /// Render tracked Ranvier updates. Direct field/array writes require Node.invalidate.
    /// Returns false without touching the framebuffer when there is no visual work.
    let renderReactive mounted (frame: Frame) =
        if mounted.Disposed then invalidOp "The view has been disposed."
        mounted.MeasuredNodes <- 0
        let previousFrame = mounted.LastFrame
        let sameFrame =
            match mounted.LastFrame with
            | Some previous -> obj.ReferenceEquals(previous, frame)
            | None -> false
        let full = not sameFrame || not (obj.ReferenceEquals(mounted.LastRules, mounted.Rules))
        let mutable changed = full
        if mounted.DamageRows.Length <> frame.Height then mounted.DamageRows <- Array.zeroCreate frame.Height
        if full then
            renderCore (not sameFrame || not (obj.ReferenceEquals(mounted.LastRules, mounted.Rules))) mounted frame
            mounted.Damage <- Frame.bounds frame
            System.Array.Fill(mounted.DamageRows, true)
        elif mounted.Root.SubtreeDirty then
            System.Array.Clear(mounted.DamageRows)
            if mounted.Root.LayoutDirty then
                measure false mounted mounted.Root
                arrangeView false mounted.DamageRows (Frame.bounds frame) mounted
            if mounted.Rules.Length > 0 then refreshPaintRules mounted.Rules mounted.Root
            View.validateScrollbarDrag mounted
            let mutable top, bottom = frame.Height, 0
            damageRows mounted.DamageRows &top &bottom mounted.Root
            for y = 0 to mounted.DamageRows.Length - 1 do
                if mounted.DamageRows[y] then
                    top <- min top y
                    bottom <- max bottom (y + 1)
            if bottom > top then
                // Whole rows preserve wide graphemes across overlapping horizontal clips.
                let damage = { X = 0; Y = max 0 top; Width = frame.Width; Height = min frame.Height bottom - max 0 top }
                let mutable y = damage.Y
                while y < damage.Y + damage.Height do
                    if mounted.DamageRows[y] then
                        let start = y
                        while y < damage.Y + damage.Height && mounted.DamageRows[y] do y <- y + 1
                        let span = { damage with Y = start; Height = y - start }
                        Frame.fill span Paint.empty frame
                        draw Paint.empty span frame mounted.Root
                    else y <- y + 1
                mounted.Damage <- damage
                changed <- true
        if full || mounted.Root.SubtreeDirty then clean full mounted.Root
        if full then mounted.LastFrame <- if sameFrame then previousFrame else Some frame
        mounted.LastRules <- mounted.Rules
        if not changed then mounted.Damage <- { X = 0; Y = 0; Width = 0; Height = 0 }
        changed

    /// Render and present tracked changes, retaining a retry after encode or output failure.
    let presentReactive output mounted renderer =
        try
            let changed = renderReactive mounted renderer.Back
            if changed || not renderer.Valid then
                Renderer.presentRows output mounted.DamageRows renderer
                true
            else
                renderer.Count <- 0
                renderer.EncodedCells <- 0
                renderer.ComparedCells <- 0
                renderer.CopiedCells <- 0
                false
        with _ ->
            Renderer.invalidate renderer
            reraise ()
