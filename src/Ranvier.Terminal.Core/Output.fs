namespace Ranvier.Terminal

open System
open System.Text

type ColorMode =
    | TrueColor
    | Palette256
    | Monochrome

type Capabilities =
    { Colors: ColorMode
      SynchronizedOutput: bool }

type Output =
    { Write: byte array -> int -> int -> unit
      Flush: unit -> unit }

type Renderer =
    { mutable Front: Frame
      mutable Back: Frame
      mutable Bytes: byte array
      mutable Count: int
      mutable Valid: bool
      mutable Pending: bool
      mutable EncodedCells: int
      mutable ComparedCells: int
      mutable CopiedCells: int
      mutable PendingRegion: Rect
      mutable PendingRows: bool array
      Capabilities: Capabilities }

module Renderer =
    let create width height capacity capabilities =
        if capacity < 64 then
            invalidArg "capacity" "Output capacity is too small."

        { Front = Frame.create width height
          Back = Frame.create width height
          Bytes = Array.zeroCreate capacity
          Count = 0
          Valid = false
          Pending = false
          EncodedCells = 0
          ComparedCells = 0
          CopiedCells = 0
          PendingRegion = { X = 0; Y = 0; Width = width; Height = height }
          PendingRows = null
          Capabilities = capabilities }

    let reserve capacity renderer =
        if capacity > renderer.Bytes.Length then
            renderer.Bytes <- Array.zeroCreate capacity

    let resize width height renderer =
        let front = Frame.create width height
        let back = Frame.create width height
        renderer.Front <- front
        renderer.Back <- back
        renderer.Valid <- false
        renderer.Pending <- false

    let invalidate renderer = renderer.Valid <- false

    let private ascii (value: string) r =
        if r.Count + value.Length > r.Bytes.Length then
            invalidOp "Output capacity exceeded; call Renderer.reserve outside the render loop."

        for i = 0 to value.Length - 1 do
            r.Bytes[r.Count + i] <- byte value[i]

        r.Count <- r.Count + value.Length

    let private number (value: int) r =
        let mutable divisor = 1

        while value / divisor >= 10 && divisor <= 100000000 do
            divisor <- divisor * 10

        while divisor > 0 do
            if r.Count = r.Bytes.Length then
                invalidOp "Output capacity exceeded."

            r.Bytes[r.Count] <- byte (48 + value / divisor % 10)
            r.Count <- r.Count + 1
            divisor <- divisor / 10

    let private color separated foreground mode value r =
        if mode <> Monochrome && separated then ascii ";" r
        match mode, value with
        | Monochrome, _ -> ()
        | _, Default -> ascii (if foreground then "39" else "49") r
        | _, Indexed index ->
            ascii (if foreground then "38;5;" else "48;5;") r
            number (int index) r
        | TrueColor, Rgb rgb ->
            ascii (if foreground then "38;2;" else "48;2;") r
            number ((rgb >>> 16) &&& 255) r
            ascii ";" r
            number ((rgb >>> 8) &&& 255) r
            ascii ";" r
            number (rgb &&& 255) r
        | Palette256, Rgb rgb ->
            let red = ((rgb >>> 16) &&& 255) * 5 / 255
            let green = ((rgb >>> 8) &&& 255) * 5 / 255
            let blue = (rgb &&& 255) * 5 / 255
            ascii (if foreground then "38;5;" else "48;5;") r
            number (16 + 36 * red + 6 * green + blue) r

    let private paint value r =
        ascii "\u001b[0" r
        let flags = value.Attributes

        if flags &&& Attributes.Bold <> Attributes.None then
            ascii ";1" r

        if flags &&& Attributes.Dim <> Attributes.None then
            ascii ";2" r

        if flags &&& Attributes.Italic <> Attributes.None then
            ascii ";3" r

        if flags &&& Attributes.Underline <> Attributes.None then
            ascii ";4" r

        if flags &&& Attributes.Reverse <> Attributes.None then
            ascii ";7" r

        if flags &&& Attributes.Strike <> Attributes.None then
            ascii ";9" r

        color true true r.Capabilities.Colors value.Foreground r
        color true false r.Capabilities.Colors value.Background r
        ascii "m" r

    let private paintDelta (previous: Paint) (next: Paint) r =
        if previous.Attributes <> next.Attributes then paint next r
        elif r.Capabilities.Colors <> Monochrome then
            let foreground = previous.Foreground <> next.Foreground
            let background = previous.Background <> next.Background
            if foreground || background then
                ascii "\u001b[" r
                if foreground then color false true r.Capabilities.Colors next.Foreground r
                if background then color foreground false r.Capabilities.Colors next.Background r
                ascii "m" r

    let private digits value =
        let mutable count, remaining = 1, value
        while remaining >= 10 do
            count <- count + 1
            remaining <- remaining / 10
        count

    let private move index x y cursor r =
        if cursor >= 0 && cursor < index && digits (index - cursor) + 3 < digits (x + 1) + digits (y + 1) + 4 then
            ascii "\u001b[" r
            number (index - cursor) r
            ascii "C" r
        else
            ascii "\u001b[" r
            number (y + 1) r
            ascii ";" r
            number (x + 1) r
            ascii "H" r

    let private encodeRegion region (rows: bool array) r =
        let region = if r.Valid then Rect.intersect region (Frame.bounds r.Back) else Frame.bounds r.Back
        r.PendingRegion <- region
        r.PendingRows <- if r.Valid then rows else null
        r.ComparedCells <- 0
        r.CopiedCells <- 0
        r.Count <- 0
        r.EncodedCells <- 0
        r.Pending <- false
        let mutable started = false
        let mutable previousPaint = Paint.empty
        let mutable hasPaint = false
        let mutable cursor = -1

        for y = region.Y to region.Y + region.Height - 1 do
            let mutable x = if isNull r.PendingRows || r.PendingRows[y] then 0 else r.Back.Width

            while x < r.Back.Width do
                let index = y * r.Back.Width + x
                let cell = r.Back.Cells[index]
                r.ComparedCells <- r.ComparedCells + 1
                let changed = not r.Valid || not (Cell.equal cell r.Front.Cells[index])

                let tailChanged =
                    cell.Width = 2uy
                    && not (Cell.equal r.Back.Cells[index + 1] r.Front.Cells[index + 1])

                if cell.Width <> 0uy && (changed || tailChanged) then
                    if not started then
                        if r.Capabilities.SynchronizedOutput then
                            ascii "\u001b[?2026h" r

                        started <- true

                    if cursor <> index then
                        move index x y cursor r

                    if not hasPaint || not (Paint.equal previousPaint cell.Paint) then
                        if hasPaint then paintDelta previousPaint cell.Paint r else paint cell.Paint r
                        previousPaint <- cell.Paint
                        hasPaint <- true

                    if cell.Glyph.Length = 1 && cell.Glyph[0] < '\u0080' then
                        if r.Count = r.Bytes.Length then invalidOp "Output capacity exceeded."
                        r.Bytes[r.Count] <- byte cell.Glyph[0]
                        r.Count <- r.Count + 1
                    else
                        let count = Encoding.UTF8.GetByteCount(cell.Glyph)
                        if count > r.Bytes.Length - r.Count then
                            invalidOp "Output capacity exceeded; reserve more space."
                        r.Count <- r.Count + Encoding.UTF8.GetBytes(cell.Glyph.AsSpan(), r.Bytes.AsSpan(r.Count))
                    r.EncodedCells <- r.EncodedCells + 1
                    cursor <- index + int cell.Width

                x <- x + max 1 (int cell.Width)

            cursor <- -1

        if started then
            ascii "\u001b[0m" r

            if r.Capabilities.SynchronizedOutput then
                ascii "\u001b[?2026l" r

        r.Pending <- true
        r.Count

    let encode r = encodeRegion (Frame.bounds r.Back) null r

    let commit r =
        if not r.Pending then
            invalidOp "There is no encoded frame to commit."

        if r.EncodedCells > 0 || not r.Valid then
            if isNull r.PendingRows then
                let offset = r.PendingRegion.Y * r.Back.Width
                let count = r.PendingRegion.Height * r.Back.Width
                Array.Copy(r.Back.Cells, offset, r.Front.Cells, offset, count)
                r.CopiedCells <- count
            else
                let mutable y = r.PendingRegion.Y
                let finish = r.PendingRegion.Y + r.PendingRegion.Height
                while y < finish do
                    if r.PendingRows[y] then
                        let start = y
                        while y < finish && r.PendingRows[y] do y <- y + 1
                        let count = (y - start) * r.Back.Width
                        let offset = start * r.Back.Width
                        Array.Copy(r.Back.Cells, offset, r.Front.Cells, offset, count)
                        r.CopiedCells <- r.CopiedCells + count
                    else y <- y + 1
        r.Valid <- true
        r.Pending <- false

    let private presentSelected output region rows r =
        let count = encodeRegion region rows r

        try
            if count > 0 then
                output.Write r.Bytes 0 count
                output.Flush()

            commit r
        with _ ->
            r.Valid <- false
            r.Pending <- false
            r.Count <- 0

            if r.Capabilities.SynchronizedOutput then
                try
                    ascii "\u001b[?2026l" r
                    output.Write r.Bytes 0 r.Count
                    output.Flush()
                with _ ->
                    ()

            reraise ()

    let presentRegion output region r = presentSelected output region null r

    let presentRows output (rows: bool array) r =
        if isNull rows || rows.Length <> r.Back.Height then
            invalidArg "rows" "A damage mask must have one entry per framebuffer row."
        presentSelected output (Frame.bounds r.Back) rows r

    let present output r = presentRegion output (Frame.bounds r.Back) r
