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

    let private color foreground mode value r =
        match mode, value with
        | Monochrome, _ -> ()
        | _, Default -> ascii (if foreground then ";39" else ";49") r
        | _, Indexed index ->
            ascii (if foreground then ";38;5;" else ";48;5;") r
            number (int index) r
        | TrueColor, Rgb rgb ->
            ascii (if foreground then ";38;2;" else ";48;2;") r
            number ((rgb >>> 16) &&& 255) r
            ascii ";" r
            number ((rgb >>> 8) &&& 255) r
            ascii ";" r
            number (rgb &&& 255) r
        | Palette256, Rgb rgb ->
            let red = ((rgb >>> 16) &&& 255) * 5 / 255
            let green = ((rgb >>> 8) &&& 255) * 5 / 255
            let blue = (rgb &&& 255) * 5 / 255
            ascii (if foreground then ";38;5;" else ";48;5;") r
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

        color true r.Capabilities.Colors value.Foreground r
        color false r.Capabilities.Colors value.Background r
        ascii "m" r

    let encode r =
        r.Count <- 0
        r.EncodedCells <- 0
        r.Pending <- false
        let mutable started = false
        let mutable previousPaint = Paint.empty
        let mutable hasPaint = false
        let mutable cursor = -1

        for y = 0 to r.Back.Height - 1 do
            let mutable x = 0

            while x < r.Back.Width do
                let index = y * r.Back.Width + x
                let cell = r.Back.Cells[index]
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
                        ascii "\u001b[" r
                        number (y + 1) r
                        ascii ";" r
                        number (x + 1) r
                        ascii "H" r

                    if not hasPaint || not (Paint.equal previousPaint cell.Paint) then
                        paint cell.Paint r
                        previousPaint <- cell.Paint
                        hasPaint <- true

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

    let commit r =
        if not r.Pending then
            invalidOp "There is no encoded frame to commit."

        Array.Copy(r.Back.Cells, r.Front.Cells, r.Back.Cells.Length)
        r.Valid <- true
        r.Pending <- false

    let present output r =
        let count = encode r

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
