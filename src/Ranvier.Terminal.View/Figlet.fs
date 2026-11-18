namespace Ranvier.Terminal.View

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open Ranvier
open Ranvier.Terminal

module Figlet =
    type Font =
        private
            { Hardblank: char
              Height: int
              Layout: int
              RightToLeft: bool
              Glyphs: Dictionary<int, string array> }

    let private malformed message = raise (FormatException("Invalid FIGfont: " + message))

    let private codeTag (tag: string) =
        let unsigned = tag.TrimStart('-', '+')
        let radix =
            if unsigned.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then 16
            elif unsigned.StartsWith('0') then 8
            else 10
        let magnitude = Convert.ToInt64(unsigned, radix)
        Convert.ToInt32(if tag.StartsWith('-') then -magnitude else magnitude)

    let private singleCell ch =
        match Char.GetUnicodeCategory ch with
        | UnicodeCategory.Control | UnicodeCategory.Format | UnicodeCategory.Surrogate
        | UnicodeCategory.NonSpacingMark | UnicodeCategory.SpacingCombiningMark | UnicodeCategory.EnclosingMark
        | UnicodeCategory.LineSeparator | UnicodeCategory.ParagraphSeparator -> false
        | _ -> TextRun.defaultWidth (string ch) = 1

    /// Parse single-cell FIGfont artwork, including horizontal layout and optional code-tagged glyphs.
    let parse (flfText: string) =
        use reader = new StringReader(flfText)
        let read () =
            let line = reader.ReadLine()
            if isNull line then malformed "unexpected end of file"
            line
        let fields (line: string) = line.Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries)
        let header = (read ()).TrimStart('\uFEFF') |> fields
        if header.Length < 6 || header[0].Length <> 6 || not (header[0].StartsWith("flf2a", StringComparison.Ordinal)) then
            malformed "expected a flf2a header"
        let number index = Int32.Parse(header[index], CultureInfo.InvariantCulture)
        let height, legacy, comments = number 1, number 4, number 5
        if height < 1 || height > flfText.Length / 95 || comments < 0 then malformed "invalid height or comment count"
        let layout =
            if header.Length > 7 then number 7
            elif legacy < 0 then 0
            elif legacy = 0 then 64
            else 128 ||| legacy
        let direction = if header.Length > 6 then number 6 else 0
        if direction <> 0 && direction <> 1 then malformed "invalid print direction"
        for _ = 1 to comments do read () |> ignore
        let glyphs = Dictionary<int, string array>()
        let readGlyph code =
            let rows =
                Array.init height (fun _ ->
                    let line = read ()
                    if line.Length = 0 then malformed "missing glyph endmark"
                    line.TrimEnd(line[line.Length - 1]))
            if rows |> Array.exists (fun row -> row.Length <> rows[0].Length) then
                malformed $"inconsistent row width in glyph {code}"
            for row in rows do
                for ch in row do
                    if not (singleCell ch) then
                        malformed $"glyph {code} contains a non-cell character"
            glyphs[code] <- rows
        for code = 32 to 126 do readGlyph code
        for code in [|196; 214; 220; 228; 246; 252; 223|] do
            if reader.Peek() >= 0 then readGlyph code
        while reader.Peek() >= 0 do
            let tag = fields (read ())
            if tag.Length > 0 then readGlyph (codeTag tag[0])
        { Hardblank = header[0][5]; Height = height; Layout = layout
          RightToLeft = direction = 1; Glyphs = glyphs }

    /// An original compact face embedded with the library.
    let compact =
        use stream = typeof<Font>.Assembly.GetManifestResourceStream("Ranvier.Terminal.Compact.flf")
        use reader = new StreamReader(stream, Encoding.UTF8)
        parse (reader.ReadToEnd())

    let private hierarchy = function
        | '|' -> 1
        | '/' | '\\' -> 2
        | '[' | ']' -> 3
        | '{' | '}' -> 4
        | '(' | ')' -> 5
        | '<' | '>' -> 6
        | _ -> 0

    let private merge font left right =
        let rules = font.Layout &&& 63
        let enabled mask = rules &&& mask <> 0
        let a, b = hierarchy left, hierarchy right
        if left = ' ' then right
        elif right = ' ' then left
        elif font.Layout &&& 128 = 0 then '\000'
        elif rules = 0 then
            if right = font.Hardblank then left
            elif left = font.Hardblank || not font.RightToLeft then right
            else left
        elif left = font.Hardblank || right = font.Hardblank then
            if left = right && enabled 32 then left else '\000'
        elif left = right && enabled 1 then left
        elif enabled 2 && left = '_' && b > 0 then right
        elif enabled 2 && right = '_' && a > 0 then left
        elif enabled 4 && a > 0 && b > 0 && a <> b then (if a > b then left else right)
        elif enabled 8 && a = b && a >= 3 && a <= 5 && left <> right then '|'
        elif enabled 16 then
            match left, right with
            | '/', '\\' -> '|'
            | '\\', '/' -> 'Y'
            | '>', '<' -> 'X'
            | _ -> '\000'
        else '\000'

    let private append font canSmush (left: string array) (right: string array) =
        let lw, rw = left[0].Length, right[0].Length
        let mutable overlap = if font.Layout &&& 192 = 0 then 0 else min lw rw
        for y = 0 to font.Height - 1 do
            if overlap > 0 then
                let l, r = left[y], right[y]
                let mutable last, first = lw - 1, 0
                while last >= 0 && l[last] = ' ' do last <- last - 1
                while first < rw && r[first] = ' ' do first <- first + 1
                let collide = canSmush && last >= 0 && first < rw && merge font l[last] r[first] <> '\000'
                overlap <- min overlap (lw - last - 1 + first + (if collide then 1 else 0))
        Array.init font.Height (fun y ->
            let l, r = left[y], right[y]
            let line = StringBuilder(lw + rw - overlap)
            line.Append(l, 0, lw - overlap) |> ignore
            for x = 0 to overlap - 1 do line.Append(merge font l[lw - overlap + x] r[x]) |> ignore
            line.Append(r, overlap, rw - overlap).ToString())

    let private plain width line =
        let output = StringBuilder()
        let mutable used = 0
        for glyph in (TextRun.prepare line).Glyphs do
            if used <= width && glyph.Columns <= width - used then output.Append(glyph.Text) |> ignore
            used <- used + glyph.Columns
        output.ToString()

    let private writeFullWidth =
        Buffers.SpanAction<char, struct (Font * string * int)>(fun output struct (font, line, columns) ->
            let mutable x = if font.RightToLeft then columns else 0
            let mutable runes = line.EnumerateRunes()
            while runes.MoveNext() do
                let glyph = font.Glyphs[runes.Current.Value]
                let width = glyph[0].Length
                if font.RightToLeft then x <- x - width
                for y = 0 to font.Height - 1 do
                    glyph[y].AsSpan().CopyTo(output.Slice(y * (columns + 1) + x, width))
                if not font.RightToLeft then x <- x + width
            output.Replace(font.Hardblank, ' ')
            for y = 1 to font.Height - 1 do output[y * (columns + 1) - 1] <- '\n')

    let private fullWidthLine font maxColumns (line: string) =
        let mutable columns = 0
        let mutable input = line.EnumerateRunes()
        let mutable fits = true
        let mutable glyph = Unchecked.defaultof<string array>
        while fits && input.MoveNext() do
            fits <- font.Glyphs.TryGetValue(input.Current.Value, &glyph)
            if fits then
                let width = glyph[0].Length
                fits <- width > 0 && width <= maxColumns - columns
                if fits then columns <- columns + width
        if not fits then plain maxColumns line
        else
            let length = Checked.(+) (Checked.(*) columns font.Height) (font.Height - 1)
            String.Create(length, struct (font, line, columns), writeFullWidth)

    let private fittedLine font width (line: string) =
        let mutable rows = Array.create font.Height ""
        let mutable previousWidth = 0
        let mutable fits = true
        for rune in line.EnumerateRunes() do
            if fits then
                match font.Glyphs.TryGetValue rune.Value with
                | true, glyph when glyph[0].Length > 0 ->
                    let canSmush = previousWidth > 1 && glyph[0].Length > 1
                    rows <-
                        if font.RightToLeft then append font canSmush glyph rows
                        else append font canSmush rows glyph
                    previousWidth <- glyph[0].Length
                    fits <- rows[0].Length <= width
                | _ -> fits <- false
        if fits then
            rows |> Array.map (fun row -> row.Replace(font.Hardblank, ' ')) |> String.concat "\n"
        else plain width line

    let private renderLine font width (line: string) =
        if line.Length = 0 then ""
        elif font.Layout &&& 192 = 0 then fullWidthLine font width line
        else fittedLine font width line

    /// Render each explicit line independently. Missing glyphs and narrow widths use clipped plain text.
    let render font maxColumns (text: string) =
        if maxColumns < 0 then invalidArg (nameof maxColumns) "Width must be nonnegative."
        let normalized = text.ReplaceLineEndings("\n")
        if normalized.IndexOf('\n') < 0 then renderLine font maxColumns normalized
        else normalized.Split('\n') |> Array.map (renderLine font maxColumns) |> String.concat "\n"

    let banner declarations font width caption =
        let node = Node.create declarations [||]
        createEffect (fun () -> Node.setText (render font (width ()) (caption ())) node)
        node
