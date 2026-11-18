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

        if rect.Width > 0 then
            let blank = Cell.blank paint
            for y = rect.Y to rect.Y + rect.Height - 1 do
                // Repair pairs crossing either edge before overwriting the interior in bulk.
                erase rect.X y frame
                if rect.Width > 1 then erase (rect.X + rect.Width - 1) y frame
                Array.Fill(frame.Cells, blank, y * frame.Width + rect.X, rect.Width)

    let textWithWrapping clip x y width wrap wordWrap (run: TextRun) paint frame =
        let lines = TextRun.lines width wrap wordWrap run
        let first = max 0 (clip.Y - y)
        let last = min (lines.Count - 1) (clip.Y + clip.Height - y - 1)
        for line = first to last do
            let mutable column = x
            let mutable index = lines.Starts[line]
            let finish = lines.Ends[line]
            if x < clip.X then
                let origin = run.ColumnOffsets[index]
                let target = origin + clip.X - x
                let mutable low = index
                let mutable high = finish
                while low < high do
                    let middle = low + (high - low) / 2
                    if run.ColumnOffsets[middle + 1] <= target then low <- middle + 1
                    else high <- middle
                index <- low
                column <- x + run.ColumnOffsets[index] - origin
            while index < finish && column < clip.X + clip.Width do
                let glyph = run.Glyphs[index]
                if column + glyph.Columns > clip.X then
                    put clip column (y + line) glyph.Text glyph.Columns paint frame
                column <- column + glyph.Columns
                index <- index + 1

    let text clip x y wrap run paint frame =
        textWithWrapping clip x y (clip.X + clip.Width - x) wrap true run paint frame
