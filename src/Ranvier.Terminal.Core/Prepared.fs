namespace Ranvier.Terminal

open System
open System.Collections.Generic
open System.Text

/// Fixed registration and output budgets. Registration and resizing are outside rendering.
type PreparedCapacity =
    { Glyphs: int
      Paints: int
      Utf8Bytes: int
      OutputBytes: int }

/// Enable only when the host supports full-width VT scrolling regions and owns terminal output.
type PreparedScrollPolicy = { FullWidthRegions: bool }

[<Struct>]
type PreparedGlyph = private { GlyphOwner: obj; GlyphId: int; Columns: int }

[<Struct>]
type PreparedPaint = private { PaintOwner: obj; PaintId: int }

type PreparedText = private { TextOwner: obj; Run: TextRun; Keys: uint64 array }

/// An explicitly owned packed framebuffer; handles cannot be shared between renderers.
type PreparedRenderer =
    private
        { Identity: obj
          mutable Width: int
          mutable Height: int
          mutable Front: uint64 array
          mutable Back: uint64 array
          GlyphIds: Dictionary<struct (string * int), int>
          PaintIds: Dictionary<Paint, int>
          GlyphValues: string array
          Payloads: byte array array
          PaintValues: Paint array
          mutable GlyphCount: int
          mutable PaintCount: int
          mutable Utf8Count: int
          Utf8Capacity: int
          mutable Bytes: byte array
          mutable Count: int
          mutable Valid: bool
          mutable Pending: bool
          mutable PendingScroll: bool
          mutable ResetMargins: bool
          mutable PendingRegion: Rect
          mutable EncodedCells: int
          mutable ComparedCells: int
          mutable CopiedCells: int
          Capabilities: Capabilities }

module PreparedRenderer =
    let private key glyph columns paint = uint64 glyph ||| (uint64 paint <<< 16) ||| (uint64 columns <<< 32)
    let private glyphId value = int (value &&& 65535UL)
    let private paintId value = int ((value >>> 16) &&& 65535UL)
    let private columns value = int (value >>> 32)
    let private blank paint = key 0 1 paint
    let private dimensions width height =
        if width < 1 || height < 1 || int64 width * int64 height > int64 Int32.MaxValue then
            invalidArg "width" "Frame dimensions must be positive and fit in an array."

    let create width height capacity capabilities =
        dimensions width height
        if capacity.Glyphs < 1 || capacity.Glyphs > 65536 || capacity.Paints < 1 || capacity.Paints > 65536 then
            invalidArg "capacity" "Glyph and paint capacities must fit the packed identity fields."
        if capacity.OutputBytes < 64 || capacity.Utf8Bytes < 0 then invalidArg "capacity" "Invalid byte capacity."
        let glyphIds = Dictionary<struct (string * int), int>(capacity.Glyphs)
        let paintIds = Dictionary<Paint, int>(capacity.Paints)
        glyphIds.Add(struct (" ", 1), 0)
        paintIds.Add(Paint.empty, 0)
        let glyphs = Array.zeroCreate capacity.Glyphs
        glyphs[0] <- " "
        let paints = Array.zeroCreate capacity.Paints
        paints[0] <- Paint.empty
        { Identity = obj(); Width = width; Height = height
          Front = Array.create (width * height) (blank 0); Back = Array.create (width * height) (blank 0)
          GlyphIds = glyphIds; PaintIds = paintIds; GlyphValues = glyphs
          Payloads = Array.zeroCreate capacity.Glyphs; PaintValues = paints
          GlyphCount = 1; PaintCount = 1; Utf8Count = 0; Utf8Capacity = capacity.Utf8Bytes
          Bytes = Array.zeroCreate capacity.OutputBytes; Count = 0; Valid = false; Pending = false;PendingScroll=false;ResetMargins=false
          PendingRegion = { X=0;Y=0;Width=width;Height=height }
          EncodedCells=0;ComparedCells=0;CopiedCells=0;Capabilities=capabilities }

    let width (r: PreparedRenderer) = r.Width
    let height (r: PreparedRenderer) = r.Height
    let bounds (r: PreparedRenderer) = { X=0;Y=0;Width=r.Width;Height=r.Height }
    let byteCount (r: PreparedRenderer) = r.Count
    let comparedCells (r: PreparedRenderer) = r.ComparedCells
    let copiedCells (r: PreparedRenderer) = r.CopiedCells
    let isValid (r: PreparedRenderer) = r.Valid
    let noScroll = { FullWidthRegions=false }
    let invalidate (r: PreparedRenderer) = r.Valid <- false; r.Pending <- false;r.PendingScroll <- false
    let private editable r = if r.Pending then invalidOp "Commit or invalidate the pending frame before mutating it."

    let reserve capacity r =
        editable r
        if capacity > r.Bytes.Length then r.Bytes <- Array.zeroCreate capacity

    let resize width height r =
        editable r
        dimensions width height
        r.Front <- Array.create (width * height) (blank 0)
        r.Back <- Array.create (width * height) (blank 0)
        r.Width <- width
        r.Height <- height
        invalidate r

    let private register text width r =
        let mutable id = 0
        let lookup = struct (text, width)
        if not (r.GlyphIds.TryGetValue(lookup, &id)) then
            if r.GlyphCount = r.GlyphValues.Length then invalidOp "Prepared glyph capacity exceeded."
            let ascii = text.Length = 1 && text[0] < '\u0080'
            let count = if ascii then 0 else Encoding.UTF8.GetByteCount text
            if count > r.Utf8Capacity - r.Utf8Count then invalidOp "Prepared UTF8 capacity exceeded."
            let bytes = if ascii then null else Encoding.UTF8.GetBytes text
            id <- r.GlyphCount
            r.GlyphIds.Add(lookup, id)
            r.GlyphValues[id] <- text
            r.Payloads[id] <- bytes
            r.GlyphCount <- id + 1
            r.Utf8Count <- r.Utf8Count + count
        { GlyphOwner=r.Identity;GlyphId=id;Columns=width }

    /// Register one sanitized grapheme; preparation is allowed to allocate.
    let prepareGlyph text width r =
        editable r
        let run = TextRun.prepareWith (fun _ -> width) text
        if run.Glyphs.Length <> 1 || run.Glyphs[0].Columns < 1 then invalidArg "text" "Expected one non-newline grapheme."
        register run.Glyphs[0].Text run.Glyphs[0].Columns r

    let preparePaint value r =
        editable r
        let mutable id = 0
        if not (r.PaintIds.TryGetValue(value, &id)) then
            if r.PaintCount = r.PaintValues.Length then invalidOp "Prepared paint capacity exceeded."
            id <- r.PaintCount
            r.PaintIds.Add(value,id)
            r.PaintValues[id] <- value
            r.PaintCount <- id + 1
        { PaintOwner=r.Identity;PaintId=id }

    /// Register sanitized text using a host-specific grapheme width policy.
    let prepareTextWith widthOf text r =
        editable r
        let safe = TextRun.prepareWith widthOf text
        let keys = Array.zeroCreate safe.Glyphs.Length
        for i = 0 to safe.Glyphs.Length - 1 do
            let glyph = safe.Glyphs[i]
            if glyph.Columns > 0 then
                let handle = register glyph.Text glyph.Columns r
                keys[i] <- key handle.GlyphId handle.Columns 0
        { TextOwner=r.Identity;Run=safe;Keys=keys }

    let prepareText text r = prepareTextWith TextRun.defaultWidth text r

    /// Preserve a prepared run's host width policy while copying and validating its source.
    let prepareRun (run: TextRun) r =
        let widths = Dictionary<string,int>(StringComparer.Ordinal)
        for glyph in run.Glyphs do
            if glyph.Columns > 0 then
                let mutable existing = 0
                if widths.TryGetValue(glyph.Text,&existing) && existing <> glyph.Columns then
                    invalidArg "run" "A glyph has inconsistent prepared widths."
                widths[glyph.Text] <- glyph.Columns
        prepareTextWith (fun glyph ->
            let mutable width = 0
            if not (widths.TryGetValue(glyph,&width)) then invalidArg "run" "Prepared source and glyphs disagree."
            width) run.Source r

    let private owner identity r =
        if not (Object.ReferenceEquals(identity,r.Identity)) then invalidArg "handle" "Prepared handles belong to another renderer."

    let private erase index x r =
        let old = r.Back[index]
        if columns old = 0 && x > 0 then r.Back[index-1] <- blank (paintId r.Back[index-1])
        elif columns old = 2 && x + 1 < r.Width then r.Back[index+1] <- blank (paintId old)
        r.Back[index] <- blank (paintId old)

    let private putKey clip x y value r =
        let width = columns value
        if Rect.contains x y clip && x >= 0 && y >= 0 && x < r.Width && y < r.Height
           && (width = 1 || (x+1 < r.Width && Rect.contains (x+1) y clip)) then
            let index = y*r.Width+x
            erase index x r
            if width = 2 then erase (index+1) (x+1) r
            r.Back[index] <- value
            if width = 2 then r.Back[index+1] <- key 0 0 (paintId value)

    let put clip x y glyph paint r =
        editable r
        owner glyph.GlyphOwner r
        owner paint.PaintOwner r
        putKey clip x y (key glyph.GlyphId glyph.Columns paint.PaintId) r

    /// Change a leading cell's paint while retaining its glyph and repairing its wide pair.
    let repaint clip x y paint r =
        editable r
        owner paint.PaintOwner r
        if x >= 0 && y >= 0 && x < r.Width && y < r.Height then
            let value = r.Back[y*r.Width+x]
            if columns value > 0 then putKey clip x y (key (glyphId value) (columns value) paint.PaintId) r

    let clear paint r =
        editable r
        owner paint.PaintOwner r
        Array.Fill(r.Back, blank paint.PaintId)

    /// Move complete back-buffer rows and fill the exposed rows; this emits no terminal commands.
    let scrollRows region distance paint r =
        editable r
        owner paint.PaintOwner r
        if region.X <> 0 || region.Width <> r.Width || region.Y < 0 || region.Height < 1
           || region.Y > r.Height-region.Height || distance = Int32.MinValue || abs distance > region.Height then
            invalidArg "region" "Scrolling needs an in-bounds full-width region and a bounded distance."
        if distance <> 0 then
            let rows = abs distance
            let start = region.Y*r.Width
            let count = (region.Height-rows)*r.Width
            if distance > 0 then
                Array.Copy(r.Back,start+rows*r.Width,r.Back,start,count)
                Array.Fill(r.Back,blank paint.PaintId,start+count,rows*r.Width)
            else
                Array.Copy(r.Back,start,r.Back,start+rows*r.Width,count)
                Array.Fill(r.Back,blank paint.PaintId,start,rows*r.Width)

    let fill rect paint r =
        editable r
        owner paint.PaintOwner r
        let rect = Rect.intersect rect (bounds r)
        if rect.Width > 0 then
            for y = rect.Y to rect.Y + rect.Height - 1 do
                let start = y*r.Width+rect.X
                erase start rect.X r
                if rect.Width > 1 then erase (start+rect.Width-1) (rect.X+rect.Width-1) r
                Array.Fill(r.Back, blank paint.PaintId, start, rect.Width)

    let text clip x y width wrap wordWrap prepared paint r =
        editable r
        owner prepared.TextOwner r
        owner paint.PaintOwner r
        let run = prepared.Run
        let lines = TextRun.lines width wrap wordWrap run
        let first = max 0 (clip.Y-y)
        let last = min (lines.Count-1) (clip.Y+clip.Height-y-1)
        for line = first to last do
            let mutable column = x
            let mutable index = lines.Starts[line]
            let finish = lines.Ends[line]
            if x < clip.X then
                let origin = run.ColumnOffsets[index]
                let target = origin+clip.X-x
                let mutable low = index
                let mutable high = finish
                while low < high do
                    let middle = low+(high-low)/2
                    if run.ColumnOffsets[middle+1] <= target then low <- middle+1 else high <- middle
                index <- low
                column <- x+run.ColumnOffsets[index]-origin
            while index < finish && column < clip.X+clip.Width do
                let value = prepared.Keys[index] ||| (uint64 paint.PaintId <<< 16)
                if column+columns value > clip.X then putKey clip column (y+line) value r
                column <- column+columns value
                index <- index+1

    /// Read a value for diagnostics; packed storage itself is never exposed for mutation.
    let cell x y accepted r =
        if x < 0 || y < 0 || x >= r.Width || y >= r.Height then invalidArg "x" "Cell lies outside framebuffer."
        let value = if accepted then r.Front[y*r.Width+x] else r.Back[y*r.Width+x]
        { Glyph=(if columns value = 0 then "" else r.GlyphValues[glyphId value])
          Width=byte (columns value);Paint=r.PaintValues[paintId value] }

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

    let private encodeRegion region scrollTop scrollHeight scrollDelta r =
        let region = if r.Valid then Rect.intersect region (bounds r) else bounds r
        r.PendingRegion <- region
        r.Count <- 0
        r.ComparedCells <- 0
        r.CopiedCells <- 0
        r.EncodedCells <- 0
        r.Pending <- false
        r.PendingScroll <- r.Valid && scrollDelta <> 0
        let mutable started = false
        let mutable previousPaint = -1
        let mutable cursor = -1
        if r.ResetMargins then
            if r.Capabilities.SynchronizedOutput then ascii "\u001b[?2026h" r
            ascii "\u001b[r\u001b[0m" r
            started <- true
        if r.PendingScroll then
            if not started && r.Capabilities.SynchronizedOutput then ascii "\u001b[?2026h" r
            ascii "\u001b[0m\u001b[" r
            number (scrollTop+1) r
            ascii ";" r
            number (scrollTop+scrollHeight) r
            ascii "r\u001b[" r
            number (abs scrollDelta) r
            ascii (if scrollDelta > 0 then "S" else "T") r
            ascii "\u001b[r" r
            started <- true
        for y = region.Y to region.Y+region.Height-1 do
            let mutable x = 0
            while x < r.Width do
                let index = y*r.Width+x
                let value = r.Back[index]
                let width = columns value
                r.ComparedCells <- r.ComparedCells+1
                let mutable frontIndex = index
                if r.PendingScroll && y >= scrollTop && y < scrollTop+scrollHeight then
                    let source = y+scrollDelta
                    frontIndex <- if source >= scrollTop && source < scrollTop+scrollHeight then source*r.Width+x else -1
                let front = if frontIndex < 0 then blank 0 else r.Front[frontIndex]
                let changed = not r.Valid || value <> front
                let tailChanged = width = 2 && r.Back[index+1] <> (if frontIndex < 0 then blank 0 else r.Front[frontIndex+1])
                if width <> 0 && (changed || tailChanged) then
                    if not started then
                        if r.Capabilities.SynchronizedOutput then ascii "\u001b[?2026h" r
                        started <- true
                    if cursor <> index then move index x y cursor r
                    let style = paintId value
                    if style <> previousPaint then
                        if previousPaint < 0 then paint r.PaintValues[style] r
                        else paintDelta r.PaintValues[previousPaint] r.PaintValues[style] r
                        previousPaint <- style
                    let id = glyphId value
                    let payload = r.Payloads[id]
                    if isNull payload then
                        if r.Count = r.Bytes.Length then invalidOp "Output capacity exceeded."
                        r.Bytes[r.Count] <- byte (r.GlyphValues[id][0])
                        r.Count <- r.Count+1
                    else
                        if payload.Length > r.Bytes.Length-r.Count then invalidOp "Output capacity exceeded."
                        payload.AsSpan().CopyTo(r.Bytes.AsSpan(r.Count))
                        r.Count <- r.Count+payload.Length
                    r.EncodedCells <- r.EncodedCells+1
                    cursor <- index+width
                x <- x+max 1 width
            cursor <- -1
        if started then
            ascii "\u001b[0m" r
            if r.Capabilities.SynchronizedOutput then ascii "\u001b[?2026l" r
        r.Pending <- true
        r.Count

    /// The returned range is borrowed until the next encoder call; do not retain or mutate it.
    let encode r =
        let count = encodeRegion (bounds r) 0 0 0 r
        ReadOnlyMemory<byte>(r.Bytes,0,count)

    let commit r =
        if not r.Pending then invalidOp "There is no encoded frame to commit."
        if r.EncodedCells > 0 || not r.Valid || r.PendingScroll then
            let offset = r.PendingRegion.Y*r.Width
            let count = r.PendingRegion.Height*r.Width
            Array.Copy(r.Back,offset,r.Front,offset,count)
            r.CopiedCells <- count
        r.Valid <- true
        r.Pending <- false
        r.PendingScroll <- false
        r.ResetMargins <- false

    let presentRegion output region r =
        let count = encodeRegion region 0 0 0 r
        try
            if count > 0 then
                output.Write r.Bytes 0 count
                output.Flush()
            commit r
        with _ ->
            invalidate r
            r.Count <- 0
            if r.Capabilities.SynchronizedOutput then
                try
                    ascii "\u001b[?2026l" r
                    output.Write r.Bytes 0 r.Count
                    output.Flush()
                with _ -> ()
            reraise ()

    let present output r = presentRegion output (bounds r) r

    /// A hint, not a promise: every back cell is compared against the logically scrolled snapshot.
    /// Positive distance scrolls upward. Unknown support uses the ordinary full-frame path.
    let presentScrolled policy output region distance r =
        if not policy.FullWidthRegions || distance = 0 || not r.Valid then present output r
        else
            if region.X <> 0 || region.Width <> r.Width || region.Y < 0 || region.Height < 2
               || region.Y > r.Height-region.Height || distance = Int32.MinValue || abs distance > region.Height then
                invalidArg "region" "Scrolling needs an in-bounds full-width region and a bounded distance."
            let count = encodeRegion (bounds r) region.Y region.Height distance r
            try
                output.Write r.Bytes 0 count
                output.Flush()
                commit r
            with _ ->
                r.ResetMargins <- true
                invalidate r
                r.Count <- 0
                try
                    ascii "\u001b[r\u001b[0m" r
                    if r.Capabilities.SynchronizedOutput then ascii "\u001b[?2026l" r
                    output.Write r.Bytes 0 r.Count
                    output.Flush()
                with _ -> ()
                reraise ()
