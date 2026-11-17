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

    let rec private measure (rules: Rule array) node =
        let mutable style = node.Style

        for i = 0 to rules.Length - 1 do
            let rule = rules[i]

            if matches rule.Selector node then
                style <- Style.merge rule.Declarations style

        node.Computed <- style
        let mutable main, cross, count = 0, 0, 0

        for i = 0 to node.Children.Length - 1 do
            let child = node.Children[i]
            measure rules child

            if child.Computed.Visible then
                let margin = child.Computed.Margin
                let width = child.DesiredWidth + margin.Left + margin.Right
                let height = child.DesiredHeight + margin.Top + margin.Bottom
                main <- main + (if style.Direction = Row then width else height)
                cross <- max cross (if style.Direction = Row then height else width)
                count <- count + 1

        main <- main + max 0 (count - 1) * style.Gap
        let border = if style.Border = NoBorder then 0 else 2

        let width =
            max node.Text.Width (if style.Direction = Row then main else cross)
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
        let mutable columns, rows = 0, (if node.Text.Glyphs.Length = 0 then 0 else 1)

        for i = 0 to node.Text.Glyphs.Length - 1 do
            let glyph = node.Text.Glyphs[i]

            if glyph.Columns < 0 then
                columns <- 0
                rows <- rows + 1
            else
                if columns > 0 && columns + glyph.Columns > contentWidth then
                    columns <- 0
                    rows <- rows + 1

                columns <- columns + glyph.Columns

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

    let rec private arrange bounds clip node =
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
        let row = style.Direction = Row
        let available = if row then content.Width else content.Height
        let mutable used, weight, count = 0, 0, 0

        for i = 0 to node.Children.Length - 1 do
            let child = node.Children[i]
            let cs = child.Computed

            if cs.Visible then
                let size = if row then cs.Width else cs.Height
                let desired = mainDesired row content.Width child

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

        let contentWidth =
            if row then
                max node.Text.Width used
            else
                max
                    node.Text.Width
                    (if node.Children.Length = 0 then
                         0
                     else
                         node.DesiredWidth - style.Padding.Left - style.Padding.Right - edge * 2)

        let contentHeight =
            if row then
                max node.Text.Height (node.DesiredHeight - style.Padding.Top - style.Padding.Bottom - edge * 2)
            else
                max (if node.Text.Glyphs.Length = 0 then 0 else node.Text.Height) used

        node.MaxScrollX <- max 0 (contentWidth - content.Width)
        node.MaxScrollY <- max 0 (contentHeight - content.Height)
        node.ScrollX <- min node.MaxScrollX (max 0 node.ScrollX)
        node.ScrollY <- min node.MaxScrollY (max 0 node.ScrollY)
        let free = max 0 (available - used)

        let mutable position =
            if weight = 0 then
                offset available used style.Justify
            else
                0

        let mutable remaining, remainingWeight = free, weight

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

                let crossLength = if row then cs.Height else cs.Width
                let crossDesired = if row then child.DesiredHeight else child.DesiredWidth

                let cross =
                    match crossLength with
                    | Flex _ -> crossAvailable
                    | Auto when style.Align = Stretch -> crossAvailable
                    | _ -> length crossAvailable crossDesired crossLength

                let cross =
                    if row then
                        min cs.MaxHeight (max cs.MinHeight cross)
                    else
                        min cs.MaxWidth (max cs.MinWidth cross)

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

                arrange childBounds childClip child
                position <- position + main + after + style.Gap
            else
                child.Bounds <- { X = 0; Y = 0; Width = 0; Height = 0 }
                child.Clip <- child.Bounds

    let private borders border =
        match border with
        | Double -> struct ("╔", "╗", "╚", "╝", "═", "║")
        | Rounded -> struct ("╭", "╮", "╰", "╯", "─", "│")
        | Ascii -> struct ("+", "+", "+", "+", "-", "|")
        | _ -> struct ("┌", "┐", "└", "┘", "─", "│")

    let rec private draw inherited frame node =
        let style = node.Computed

        if style.Visible && node.Clip.Width > 0 && node.Clip.Height > 0 then
            let paint =
                { style.Paint with
                    Foreground =
                        if style.Paint.Foreground = Default then
                            inherited.Foreground
                        else
                            style.Paint.Foreground
                    Background =
                        if style.Paint.Background = Default then
                            inherited.Background
                        else
                            style.Paint.Background }

            let bounds = node.Bounds
            Frame.fill node.Clip paint frame
            let edge = if style.Border = NoBorder then 0 else 1

            if edge > 0 && bounds.Width >= 2 && bounds.Height >= 2 then
                let struct (tl, tr, bl, br, horizontal, vertical) = borders style.Border

                for x = bounds.X + 1 to bounds.X + bounds.Width - 2 do
                    Frame.put node.Clip x bounds.Y horizontal 1 paint frame
                    Frame.put node.Clip x (bounds.Y + bounds.Height - 1) horizontal 1 paint frame

                for y = bounds.Y + 1 to bounds.Y + bounds.Height - 2 do
                    Frame.put node.Clip bounds.X y vertical 1 paint frame
                    Frame.put node.Clip (bounds.X + bounds.Width - 1) y vertical 1 paint frame

                Frame.put node.Clip bounds.X bounds.Y tl 1 paint frame
                Frame.put node.Clip (bounds.X + bounds.Width - 1) bounds.Y tr 1 paint frame
                Frame.put node.Clip bounds.X (bounds.Y + bounds.Height - 1) bl 1 paint frame
                Frame.put node.Clip (bounds.X + bounds.Width - 1) (bounds.Y + bounds.Height - 1) br 1 paint frame

            let content =
                { X = bounds.X + edge + style.Padding.Left
                  Y = bounds.Y + edge + style.Padding.Top
                  Width = max 0 (bounds.Width - edge * 2 - style.Padding.Left - style.Padding.Right)
                  Height = max 0 (bounds.Height - edge * 2 - style.Padding.Top - style.Padding.Bottom) }

            if node.Focused && node.CaretColumn >= 0 && content.Width > 0 then
                if node.CaretColumn < node.ScrollX then
                    node.ScrollX <- node.CaretColumn
                elif node.CaretColumn >= node.ScrollX + content.Width then
                    node.ScrollX <- node.CaretColumn - content.Width + 1

            Frame.text
                (Rect.intersect node.Clip content)
                (content.X - node.ScrollX)
                (content.Y - node.ScrollY)
                style.Wrap
                node.Text
                paint
                frame

            for i = 0 to node.Children.Length - 1 do
                draw paint frame node.Children[i]

            if node.Focused && node.CaretColumn >= 0 then
                let x, y = content.X + node.CaretColumn - node.ScrollX, content.Y
                let clip = Rect.intersect node.Clip content

                if Rect.contains x y clip then
                    let cell = frame.Cells[y * frame.Width + x]

                    let caretPaint =
                        { cell.Paint with
                            Attributes = cell.Paint.Attributes ^^^ Attributes.Reverse }

                    Frame.put clip x y cell.Glyph (int cell.Width) caretPaint frame

    let render mounted frame =
        if mounted.Disposed then
            invalidOp "The view has been disposed."

        measure mounted.Rules mounted.Root
        arrange (Frame.bounds frame) (Frame.bounds frame) mounted.Root
        Frame.clear Paint.empty frame
        draw Paint.empty frame mounted.Root
