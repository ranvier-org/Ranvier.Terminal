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

type TextLines =
    { Starts: int array
      Ends: int array
      mutable Count: int
      mutable Width: int
      mutable Wrap: bool
      mutable WordWrap: bool }

type TextRun =
    { Source: string
      Glyphs: Glyph array
      ColumnOffsets: int array
      Width: int
      Height: int
      Lines: TextLines }

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

    let private prepared text (glyphs: Glyph array) offsets width height =
        { Source = text
          Glyphs = glyphs
          ColumnOffsets = offsets
          Width = width
          Height = height
          Lines = { Starts = Array.zeroCreate (glyphs.Length + 1); Ends = Array.zeroCreate (glyphs.Length + 1)
                    Count = 0; Width = -1; Wrap = false; WordWrap = false } }

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

        let glyphs = values.ToArray()
        let offsets = Array.zeroCreate (glyphs.Length + 1)
        for i = 0 to glyphs.Length - 1 do
            offsets[i + 1] <- offsets[i] + max 0 glyphs[i].Columns
        prepared text glyphs offsets (max width line) height

    // Shared by measurement and painting. Return a glyph range and the next line's start.
    let lineEnd width wrap wordWrap (run: TextRun) start =
        let width = max 1 width
        let mutable index, columns = start, 0
        let mutable separatorStart, separatorEnd = -1, -1
        let mutable inSeparator = false
        let mutable finished = false
        let mutable finish, next = run.Glyphs.Length, run.Glyphs.Length
        while index < run.Glyphs.Length && not finished do
            let glyph = run.Glyphs[index]
            let space =
                glyph.Columns >= 0 && glyph.Text.Length > 0
                && Char.IsWhiteSpace(glyph.Text, 0)
                && glyph.Text[0] <> '\u00a0' && glyph.Text[0] <> '\u202f'
            if glyph.Columns < 0 then
                finish <- index
                next <- index + 1
                finished <- true
            else
                if space then
                    if not inSeparator then separatorStart <- index
                    separatorEnd <- index + 1
                inSeparator <- space
                if wrap && columns > 0 && columns + glyph.Columns > width then
                    if wordWrap && separatorStart > start then
                        finish <- separatorStart
                        next <- separatorEnd
                        while next < run.Glyphs.Length
                              && run.Glyphs[next].Columns >= 0
                              && Char.IsWhiteSpace(run.Glyphs[next].Text, 0)
                              && run.Glyphs[next].Text[0] <> '\u00a0'
                              && run.Glyphs[next].Text[0] <> '\u202f' do
                            next <- next + 1
                        if next < run.Glyphs.Length && run.Glyphs[next].Columns < 0 then
                            next <- next + 1
                    else
                        finish <- index
                        next <- index
                    finished <- true
                else
                    columns <- columns + glyph.Columns
                    index <- index + 1
        struct (finish, next)

    let lines width wrap wordWrap (run: TextRun) =
        let width = if wrap then max 1 width else 0
        let cache = run.Lines
        if cache.Width <> width || cache.Wrap <> wrap || cache.WordWrap <> wordWrap then
            let mutable start, rows = 0, 0
            while start < run.Glyphs.Length do
                let struct (finish, next) = lineEnd width wrap wordWrap run start
                cache.Starts[rows] <- start
                cache.Ends[rows] <- finish
                rows <- rows + 1
                start <- next
            if run.Glyphs.Length > 0 && run.Glyphs[run.Glyphs.Length - 1].Columns < 0 then
                cache.Starts[rows] <- start
                cache.Ends[rows] <- start
                rows <- rows + 1
            cache.Count <- rows
            cache.Width <- width
            cache.Wrap <- wrap
            cache.WordWrap <- wordWrap
        cache

    let wrappedHeight width wordWrap run = (lines width true wordWrap run).Count

    let private narrowGlyphs (first: char) (last: char) =
        Array.init (int last - int first + 1) (fun i -> { Text = string (char (int first + i)); Columns = 1 })

    let private ascii = narrowGlyphs ' ' '~'
    let private drawing = narrowGlyphs '\u2500' '\u259f'
    let private newline = { Text = "\n"; Columns = -1 }

    let prepare (text: string) =
        if isNull text then nullArg "text"
        // Check the whole string first: a following combining mark can extend even an ASCII glyph.
        let mutable i = 0
        let mutable narrow = true
        while narrow && i < text.Length do
            let c = text[i]
            narrow <- (c >= ' ' && c <= '~') || (c >= '\u2500' && c <= '\u259f') || c = '\n'
            i <- i + 1
        if not narrow then prepareWith defaultWidth text
        else
            let glyphs = Array.zeroCreate text.Length
            let offsets = Array.zeroCreate (text.Length + 1)
            let mutable width, line, height = 0, 0, 1
            for i = 0 to text.Length - 1 do
                let c = text[i]
                if c = '\n' then
                    glyphs[i] <- newline
                    offsets[i + 1] <- offsets[i]
                    width <- max width line
                    line <- 0
                    height <- height + 1
                else
                    glyphs[i] <- if c <= '~' then ascii[int c - int ' '] else drawing[int c - 0x2500]
                    offsets[i + 1] <- offsets[i] + 1
                    line <- line + 1
            prepared text glyphs offsets (max width line) height
    let empty = prepare ""
