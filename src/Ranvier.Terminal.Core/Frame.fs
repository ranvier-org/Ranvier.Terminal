namespace Ranvier.Terminal

open System

type Frame =
    { Width: int
      Height: int
      Cells: Cell array }

module Frame =
    let create width height =
        if width < 1 || height < 1 || int64 width * int64 height > int64 Int32.MaxValue then
            invalidArg "width" "Frame dimensions must be positive and fit in an array."

        { Width = width
          Height = height
          Cells = Array.create (width * height) (Cell.blank Paint.empty) }

    let bounds frame =
        { X = 0
          Y = 0
          Width = frame.Width
          Height = frame.Height }

    let clear paint frame =
        Array.Fill(frame.Cells, Cell.blank paint)

    let private erase x y frame =
        let index = y * frame.Width + x
        let old = frame.Cells[index]

        if old.Width = 0uy && x > 0 then
            frame.Cells[index - 1] <- Cell.blank frame.Cells[index - 1].Paint
        elif old.Width = 2uy && x + 1 < frame.Width then
            frame.Cells[index + 1] <- Cell.blank old.Paint

        frame.Cells[index] <- Cell.blank old.Paint

    let put clip x y glyph columns paint frame =
        if
            (columns = 1 || columns = 2)
            && Rect.contains x y clip
            && Rect.contains x y (bounds frame)
        then
            if columns = 1 || (Rect.contains (x + 1) y clip && x + 1 < frame.Width) then
                erase x y frame

                if columns = 2 then
                    erase (x + 1) y frame

                let index = y * frame.Width + x

                frame.Cells[index] <-
                    { Glyph = glyph
                      Width = byte columns
                      Paint = paint }

                if columns = 2 then
                    frame.Cells[index + 1] <-
                        { Glyph = ""
                          Width = 0uy
                          Paint = paint }

    let fill rect paint frame =
        let rect = Rect.intersect rect (bounds frame)

        for y = rect.Y to rect.Y + rect.Height - 1 do
            for x = rect.X to rect.X + rect.Width - 1 do
                put rect x y " " 1 paint frame

    let text clip x y wrap (run: TextRun) paint frame =
        let mutable column, row = x, y

        for index = 0 to run.Glyphs.Length - 1 do
            let glyph = run.Glyphs[index]

            if glyph.Columns < 0 then
                column <- x
                row <- row + 1
            else
                if wrap && column > x && column + glyph.Columns > clip.X + clip.Width then
                    column <- x
                    row <- row + 1

                put clip column row glyph.Text glyph.Columns paint frame
                column <- column + glyph.Columns
