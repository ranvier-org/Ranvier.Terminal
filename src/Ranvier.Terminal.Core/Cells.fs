namespace Ranvier.Terminal

open System
open System.Globalization
open System.Text

[<Struct>]
type Color =
    | Default
    | Indexed of index: byte
    | Rgb of rgb: int

[<Flags>]
type Attributes =
    | None = 0
    | Bold = 1
    | Dim = 2
    | Italic = 4
    | Underline = 8
    | Reverse = 16
    | Strike = 32

[<Struct>]
type Paint =
    { Foreground: Color
      Background: Color
      Attributes: Attributes }

[<Struct>]
type Cell =
    { Glyph: string
      Width: byte
      Paint: Paint }

[<Struct>]
type Rect =
    { X: int
      Y: int
      Width: int
      Height: int }

module Rect =
    let contains x y r =
        x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height

    let intersect a b =
        let x = max a.X b.X
        let y = max a.Y b.Y

        { X = x
          Y = y
          Width = max 0 (min (a.X + a.Width) (b.X + b.Width) - x)
          Height = max 0 (min (a.Y + a.Height) (b.Y + b.Height) - y) }

module Paint =
    let empty =
        { Foreground = Default
          Background = Default
          Attributes = Attributes.None }

    let equal a b =
        a.Foreground = b.Foreground
        && a.Background = b.Background
        && a.Attributes = b.Attributes

module Cell =
    let blank paint =
        { Glyph = " "
          Width = 1uy
          Paint = paint }

    let equal (a: Cell) (b: Cell) =
        a.Width = b.Width
        && Paint.equal a.Paint b.Paint
        && String.Equals(a.Glyph, b.Glyph, StringComparison.Ordinal)

[<Struct>]
type Glyph = { Text: string; Columns: int }

type TextRun =
    { Glyphs: Glyph array
      Width: int
      Height: int }

module TextRun =
    // Ambiguous-width characters are narrow. A host-specific policy can replace this at preparation time.
    let defaultWidth (text: string) =
        let mutable width = 0
        let mutable emoji = false

        for rune in text.EnumerateRunes() do
            let n = rune.Value
            let category = Rune.GetUnicodeCategory rune

            if n = 0xFE0F || n = 0x20E3 then
                emoji <- true

            if
                category <> UnicodeCategory.NonSpacingMark
                && category <> UnicodeCategory.EnclosingMark
                && category <> UnicodeCategory.Format
            then
                let wide =
                    n >= 0x1100
                    && (n <= 0x115F
                        || n = 0x2329
                        || n = 0x232A
                        || (n >= 0x2E80 && n <= 0xA4CF && n <> 0x303F)
                        || (n >= 0xAC00 && n <= 0xD7A3)
                        || (n >= 0xF900 && n <= 0xFAFF)
                        || (n >= 0xFE10 && n <= 0xFE19)
                        || (n >= 0xFE30 && n <= 0xFE6F)
                        || (n >= 0xFF00 && n <= 0xFF60)
                        || (n >= 0xFFE0 && n <= 0xFFE6)
                        || (n >= 0x1F000 && n <= 0x1FAFF)
                        || (n >= 0x20000 && n <= 0x3FFFD))

                width <- max width (if wide then 2 else 1)

        if emoji then 2 else max 1 width

    let prepareWith widthOf (text: string) =
        if isNull text then
            nullArg "text"

        let values = ResizeArray<Glyph>()
        let iterator = StringInfo.GetTextElementEnumerator text
        let mutable width, line, height = 0, 0, 1

        while iterator.MoveNext() do
            let value = iterator.GetTextElement()

            if value = "\n" || value = "\r\n" then
                values.Add { Text = "\n"; Columns = -1 }
                width <- max width line
                line <- 0
                height <- height + 1
            else
                let mutable safe = true

                for c in value do
                    if Char.IsControl c || c = '\u001b' then
                        safe <- false

                let glyph = if safe then value else "�"
                let columns = widthOf glyph

                if columns < 1 || columns > 2 then
                    invalidArg "widthOf" "A terminal grapheme must occupy one or two columns."

                values.Add { Text = glyph; Columns = columns }
                line <- line + columns

        { Glyphs = values.ToArray()
          Width = max width line
          Height = height }

    let prepare text = prepareWith defaultWidth text
    let empty = { Glyphs = [||]; Width = 0; Height = 1 }
