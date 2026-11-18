module Ranvier.Terminal.PreparedChecks

open System
open System.Diagnostics
open System.Text
open Ranvier.Terminal
open Ranvier
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private check condition message = if not condition then failwith message
let private capacity = { Glyphs=128;Paints=32;Utf8Bytes=4096;OutputBytes=65536 }
let private caps = { Colors=TrueColor;SynchronizedOutput=true }
let private sink = { Write=(fun _ _ _ -> ());Flush=(fun () -> ()) }
let private expectFailure action =
    let mutable failed = false
    try action() with :? ArgumentException | :? InvalidOperationException -> failed <- true
    check failed "Expected rejected prepared operation"

module private ScrollModel =
    let apply (output: string) (screen: Frame) =
        let mutable index, x, y = 0, 0, 0
        let mutable paint = Paint.empty
        let mutable top = 0
        let mutable bottom = screen.Height-1
        while index < output.Length do
            if output[index] = '\u001b' then
                let start = index + 2
                index <- start
                while index < output.Length && not (output[index] >= '@' && output[index] <= '~') do index <- index + 1
                if index >= output.Length then failwith "Incomplete escape sequence"
                let parameters = output.Substring(start, index - start)
                match output[index] with
                | 'H' ->
                    let p = parameters.Split ';' |> Array.map int
                    y <- p[0] - 1
                    x <- p[1] - 1
                | 'C' -> x <- x + int parameters
                | 'r' ->
                    if parameters = "" then top <- 0;bottom <- screen.Height-1
                    else
                        let p = parameters.Split ';' |> Array.map int
                        top <- p[0]-1
                        bottom <- p[1]-1
                    x <- 0
                    y <- 0
                | ('S' | 'T') as op ->
                    let count = if parameters = "" then 1 else int parameters
                    for _ = 1 to count do
                        if op = 'S' then
                            for row = top to bottom-1 do
                                Array.Copy(screen.Cells,(row+1)*screen.Width,screen.Cells,row*screen.Width,screen.Width)
                            Array.Fill(screen.Cells,Cell.blank paint,bottom*screen.Width,screen.Width)
                        else
                            for row = bottom downto top+1 do
                                Array.Copy(screen.Cells,(row-1)*screen.Width,screen.Cells,row*screen.Width,screen.Width)
                            Array.Fill(screen.Cells,Cell.blank paint,top*screen.Width,screen.Width)
                | 'm' ->
                    let p = parameters.Split ';' |> Array.map (fun s -> if s = "" then 0 else int s)
                    let mutable i = 0
                    while i < p.Length do
                        match p[i] with
                        | 0 -> paint <- Paint.empty
                        | 1 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Bold }
                        | 2 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Dim }
                        | 3 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Italic }
                        | 4 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Underline }
                        | 7 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Reverse }
                        | 9 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Strike }
                        | 39 -> paint <- { paint with Foreground = Default }
                        | 49 -> paint <- { paint with Background = Default }
                        | (38 | 48) as channel ->
                            let color =
                                if p[i+1] = 5 then
                                    i <- i + 2
                                    Indexed(byte p[i])
                                elif p[i+1] = 2 then
                                    let rgb = (p[i+2] <<< 16) ||| (p[i+3] <<< 8) ||| p[i+4]
                                    i <- i + 4
                                    Rgb rgb
                                else failwith "Unsupported color sequence"
                            paint <- if channel = 38 then { paint with Foreground = color } else { paint with Background = color }
                        | other -> failwithf "Unsupported SGR %d" other
                        i <- i + 1
                | 'h' | 'l' when parameters = "?2026" -> ()
                | other -> failwithf "Unsupported escape %c" other
                index <- index + 1
            else
                let glyph = Globalization.StringInfo.GetNextTextElement(output, index)
                let width = TextRun.defaultWidth glyph
                Frame.put (Frame.bounds screen) x y glyph width paint screen
                x <- x + width
                index <- index + glyph.Length

let run () =
    let r = PreparedRenderer.create 31 9 capacity caps
    let reference = Frame.create 31 9
    let screen = Frame.create 31 9
    let glyphs = [|"a";"b";"界";"e\u0301";"👩‍💻";" "|]
    let handles = glyphs |> Array.map (fun text -> PreparedRenderer.prepareGlyph text (TextRun.defaultWidth text) r)
    let paints = [|Paint.empty;{Paint.empty with Foreground=Rgb 0x2468AC;Background=Indexed 4uy};{Paint.empty with Attributes=Attributes.Reverse ||| Attributes.Underline}|]
    let styles = paints |> Array.map (fun value -> PreparedRenderer.preparePaint value r)
    let output = { Write=(fun bytes offset count -> AnsiModel.apply (Encoding.UTF8.GetString(bytes,offset,count)) screen);Flush=(fun () -> ()) }
    let random = Random 74613
    for iteration = 0 to 399 do
        let style = random.Next paints.Length
        let x = random.Next 31
        let y = random.Next 9
        let glyph = random.Next glyphs.Length
        if iteration % 19 = 0 then
            PreparedRenderer.clear styles[style] r
            Frame.clear paints[style] reference
        elif iteration % 7 = 0 then
            let rect = {X=x;Y=y;Width=random.Next(1,8);Height=random.Next(1,4)}
            PreparedRenderer.fill rect styles[style] r
            Frame.fill rect paints[style] reference
        else
            let clip = {X=1;Y=1;Width=29;Height=7}
            PreparedRenderer.put clip x y handles[glyph] styles[style] r
            Frame.put clip x y glyphs[glyph] (TextRun.defaultWidth glyphs[glyph]) paints[style] reference
        PreparedRenderer.present output r
        for row = 0 to 8 do
            for col = 0 to 30 do
                check (Cell.equal reference.Cells[row*31+col] (PreparedRenderer.cell col row true r)) "Prepared packed snapshot differs from reference"
                check (Cell.equal reference.Cells[row*31+col] screen.Cells[row*31+col]) "Prepared output differs from independent screen model"

    let other = PreparedRenderer.create 31 9 capacity caps
    let foreignGlyph = PreparedRenderer.prepareGlyph "q" 1 other
    let foreignPaint = PreparedRenderer.preparePaint Paint.empty other
    let foreignText = PreparedRenderer.prepareText "text" other
    expectFailure (fun () -> PreparedRenderer.put (PreparedRenderer.bounds r) 0 0 foreignGlyph styles[0] r)
    expectFailure (fun () -> PreparedRenderer.clear foreignPaint r)
    expectFailure (fun () -> PreparedRenderer.fill (PreparedRenderer.bounds r) foreignPaint r)
    expectFailure (fun () -> PreparedRenderer.text (PreparedRenderer.bounds r) 0 0 10 true true foreignText styles[0] r)
    let pending = PreparedRenderer.encode r
    GC.KeepAlive pending
    expectFailure (fun () -> PreparedRenderer.put (PreparedRenderer.bounds r) 0 0 handles[0] styles[0] r)
    PreparedRenderer.commit r

    let tiny = PreparedRenderer.create 100 4 {capacity with OutputBytes=64} caps
    let mutable writes = 0
    let counter = { Write=(fun _ _ _ -> writes <- writes+1);Flush=(fun () -> ()) }
    expectFailure (fun () -> PreparedRenderer.present counter tiny)
    check (writes=0) "Prepared capacity failure wrote partial frame"
    PreparedRenderer.reserve 65536 tiny
    PreparedRenderer.present sink tiny
    let letter = PreparedRenderer.prepareGlyph "X" 1 tiny
    let plain = PreparedRenderer.preparePaint Paint.empty tiny
    PreparedRenderer.put (PreparedRenderer.bounds tiny) 0 0 letter plain tiny
    let failing = {Write=(fun _ _ _ -> ());Flush=(fun () -> raise (IO.IOException "flush"))}
    try PreparedRenderer.present failing tiny with :? IO.IOException -> ()
    check (not (PreparedRenderer.isValid tiny)) "Prepared flush failure did not invalidate"
    check ((PreparedRenderer.cell 0 0 true tiny).Glyph=" ") "Prepared flush failure committed snapshot"
    PreparedRenderer.present sink tiny
    check ((PreparedRenderer.cell 0 0 true tiny).Glyph="X") "Prepared retry failed"
    PreparedRenderer.put (PreparedRenderer.bounds tiny) 1 0 letter plain tiny
    try PreparedRenderer.present {failing with Write=(fun _ _ _ -> raise (IO.IOException "write"))} tiny with :? IO.IOException -> ()
    check ((PreparedRenderer.cell 1 0 true tiny).Glyph=" ") "Prepared write failure committed snapshot"
    PreparedRenderer.present sink tiny
    PreparedRenderer.resize 7 3 tiny
    PreparedRenderer.put (PreparedRenderer.bounds tiny) 2 1 letter plain tiny
    PreparedRenderer.present sink tiny
    check ((PreparedRenderer.cell 2 1 true tiny).Glyph="X") "Resize invalidated valid handle"

    let limited = PreparedRenderer.create 2 2 {Glyphs=2;Paints=1;Utf8Bytes=2;OutputBytes=1024} caps
    expectFailure (fun () -> PreparedRenderer.prepareGlyph "界" 2 limited |> ignore)
    let _ = PreparedRenderer.prepareGlyph "é" 1 limited
    expectFailure (fun () -> PreparedRenderer.prepareGlyph "x" 1 limited |> ignore)
    expectFailure (fun () -> PreparedRenderer.preparePaint paints[1] limited |> ignore)

    let run = PreparedRenderer.prepareText "hello \n界é\n👩‍💻 end" r
    PreparedRenderer.clear styles[0] r
    Frame.clear paints[0] reference
    PreparedRenderer.text (PreparedRenderer.bounds r) -1 0 5 true true run styles[1] r
    Frame.textWithWrapping (Frame.bounds reference) -1 0 5 true true (TextRun.prepare "hello \n界é\n👩‍💻 end") paints[1] reference
    PreparedRenderer.present output r
    for row = 0 to 8 do
        for col = 0 to 30 do check (Cell.equal reference.Cells[row*31+col] screen.Cells[row*31+col]) "Prepared wrapped text differs"

    let allocation = PreparedRenderer.create 80 20 capacity caps
    let a = PreparedRenderer.prepareText "ASCII é 界 👩‍💻" allocation
    let b = PreparedRenderer.prepareText "other α 界 👩‍💻" allocation
    let paint = PreparedRenderer.preparePaint paints[1] allocation
    let clip = PreparedRenderer.bounds allocation
    let iteration i =
        PreparedRenderer.clear paint allocation
        PreparedRenderer.text clip 2 3 60 true true (if i%2=0 then a else b) paint allocation
        PreparedRenderer.present sink allocation
    let warm = Stopwatch.StartNew()
    let mutable index = 0
    while warm.ElapsedMilliseconds < 1000L do iteration index; index <- index+1
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i = 0 to 9999 do iteration i
    let allocated = GC.GetAllocatedBytesForCurrentThread()-before
    printfn "measured here: owned prepared text/paint/encode/commit: 10000 changing frames; %d allocated bytes" allocated
    check (allocated=0L) "Prepared render allocated managed memory"

    for mode in [TrueColor;Palette256;Monochrome] do
        let config = {caps with Colors=mode}
        let baseline = Renderer.create 16 4 65536 config
        let packed = PreparedRenderer.create 16 4 capacity config
        let handles = glyphs |> Array.map (fun s -> PreparedRenderer.prepareGlyph s (TextRun.defaultWidth s) packed)
        let styles = paints |> Array.map (fun p -> PreparedRenderer.preparePaint p packed)
        for i = 0 to 29 do
            let style = i%styles.Length
            Frame.clear paints[style] baseline.Back
            PreparedRenderer.clear styles[style] packed
            for x = 0 to 6 do
                let glyph = (x+i)%glyphs.Length
                Frame.put (Frame.bounds baseline.Back) (x*2) 1 glyphs[glyph] (TextRun.defaultWidth glyphs[glyph]) paints[(style+1)%styles.Length] baseline.Back
                PreparedRenderer.put (PreparedRenderer.bounds packed) (x*2) 1 handles[glyph] styles[(style+1)%styles.Length] packed
            let count = Renderer.encode baseline
            let bytes = PreparedRenderer.encode packed
            check (bytes.Span.SequenceEqual(ReadOnlySpan<byte>(baseline.Bytes,0,count))) "Prepared color-mode output differs from ordinary encoder"
            Renderer.commit baseline
            PreparedRenderer.commit packed

    let longText = "e"+String.replicate 512 "\u0301"
    let bounded = PreparedRenderer.create 5 3 {capacity with Utf8Bytes=2048} caps
    let longGlyph = PreparedRenderer.prepareGlyph longText 1 bounded
    let plain = PreparedRenderer.preparePaint Paint.empty bounded
    PreparedRenderer.put (PreparedRenderer.bounds bounded) 0 0 longGlyph plain bounded
    PreparedRenderer.present sink bounded
    check ((PreparedRenderer.cell 0 0 true bounded).Glyph=longText) "Long grapheme was truncated"
    expectFailure (fun () -> PreparedRenderer.prepareText ("a"+String.replicate 2048 "\u0301") bounded |> ignore)
    PreparedRenderer.put (PreparedRenderer.bounds bounded) 1 0 longGlyph plain bounded
    PreparedRenderer.present sink bounded
    check ((PreparedRenderer.cell 1 0 true bounded).Glyph=longText) "Preparation exhaustion broke existing handles"

    let scrolling = PreparedRenderer.create 19 8 capacity caps
    let scrollScreen = Frame.create 19 8
    let scrollGlyphs = glyphs |> Array.map (fun s -> PreparedRenderer.prepareGlyph s (TextRun.defaultWidth s) scrolling)
    let scrollPaints = paints |> Array.map (fun p -> PreparedRenderer.preparePaint p scrolling)
    let scrollOutput = { Write=(fun bytes offset count -> ScrollModel.apply (Encoding.UTF8.GetString(bytes,offset,count)) scrollScreen);Flush=(fun () -> ()) }
    let policy = {FullWidthRegions=true}
    let scrollRegion = {X=0;Y=1;Width=19;Height=6}
    for iteration = 0 to 199 do
        for _ = 0 to 7 do
            let glyph = random.Next scrollGlyphs.Length
            PreparedRenderer.put (PreparedRenderer.bounds scrolling) (random.Next 19) (random.Next 8) scrollGlyphs[glyph] scrollPaints[random.Next scrollPaints.Length] scrolling
        if iteration%11=0 then PreparedRenderer.invalidate scrolling
        PreparedRenderer.presentScrolled policy scrollOutput scrollRegion (if iteration%2=0 then 1 else -2) scrolling
        for row = 0 to 7 do
            for col = 0 to 18 do
                check (Cell.equal scrollScreen.Cells[row*19+col] (PreparedRenderer.cell col row true scrolling)) "Scroll hint corrupted arbitrary back-frame contents"
    expectFailure (fun () -> PreparedRenderer.presentScrolled policy sink {scrollRegion with X=1;Width=18} 1 scrolling)
    let old = PreparedRenderer.cell 0 0 true scrolling
    PreparedRenderer.put (PreparedRenderer.bounds scrolling) 0 0 scrollGlyphs[1] scrollPaints[1] scrolling
    let mutable fail = true
    let partial = {Write=(fun bytes offset count ->
                        if fail then
                            fail <- false
                            ScrollModel.apply "\u001b[2;7r\u001b[1S" scrollScreen
                            raise (IO.IOException "partial scroll")
                        else ScrollModel.apply (Encoding.UTF8.GetString(bytes,offset,count)) scrollScreen)
                   Flush=(fun () -> ())}
    try PreparedRenderer.presentScrolled policy partial scrollRegion 1 scrolling with :? IO.IOException -> ()
    check (Cell.equal old (PreparedRenderer.cell 0 0 true scrolling)) "Partial scroll committed snapshot"
    check (not (PreparedRenderer.isValid scrolling)) "Partial scroll did not invalidate"
    PreparedRenderer.presentScrolled policy scrollOutput scrollRegion 1 scrolling
    for row = 0 to 7 do
        for col = 0 to 18 do check (Cell.equal scrollScreen.Cells[row*19+col] (PreparedRenderer.cell col row true scrolling)) "Scroll retry without mutation failed"
    let mutable captured = ""
    PreparedRenderer.presentScrolled PreparedRenderer.noScroll {Write=(fun b o c -> captured <- Encoding.UTF8.GetString(b,o,c));Flush=(fun () -> ())} scrollRegion 1 scrolling
    check (not (captured.Contains "S") && not (captured.Contains "r")) "Unknown capability emitted scroll"
    let scrollIteration i =
        PreparedRenderer.scrollRows scrollRegion 1 scrollPaints[0] scrolling
        PreparedRenderer.put (PreparedRenderer.bounds scrolling) 2 6 scrollGlyphs[i%scrollGlyphs.Length] scrollPaints[i%scrollPaints.Length] scrolling
        PreparedRenderer.presentScrolled policy sink scrollRegion 1 scrolling
    let warm = Stopwatch.StartNew()
    let mutable index = 0
    while warm.ElapsedMilliseconds < 1000L do scrollIteration index;index <- index+1
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i = 0 to 9999 do scrollIteration i
    let allocated = GC.GetAllocatedBytesForCurrentThread()-before
    printfn "measured here: prepared scoped scroll/mutation/encode/commit: 10000 changing frames; %d allocated bytes" allocated
    check (allocated=0L) "Prepared scroll allocated managed memory"

let runLayout () : unit =
    let build () =
        let child = Node.create [Css.height (Css.cells 5);Css.border Rounded;Css.padding 1;Css.wrap true] [||]
        Node.setText "hello é 界 👩‍💻 words wrap here" child
        let custom = Node.create [Css.height (Css.cells 3);Css.wrap false] [||]
        Node.setPreparedText (TextRun.prepareWith (fun _ -> 2) "αβγ") custom
        let root = Node.create [Css.direction Column;Css.color (Rgb 0xABCDEF);Css.backgroundColor (Rgb 0x182838)] [|child;custom|]
        root,child,custom
    use firstGraph = new Graph()
    use secondGraph = new Graph()
    let firstRoot,firstChild,firstCustom = build()
    let secondRoot,secondChild,secondCustom = build()
    let first = View.mount firstGraph (fun () -> firstRoot)
    let second = View.mount secondGraph (fun () -> secondRoot)
    let normal = Renderer.create 40 15 65536 caps
    let packed = PreparedRenderer.create 40 15 {capacity with Paints=128;Glyphs=512} caps
    let cache = PreparedLayout.create second packed
    let compare () =
        Layout.render first normal.Back
        Renderer.present sink normal
        PreparedLayout.refresh false cache
        PreparedLayout.present sink cache
        for y = 0 to normal.Back.Height-1 do
            for x = 0 to normal.Back.Width-1 do
                check (Cell.equal normal.Back.Cells[y*normal.Back.Width+x] (PreparedRenderer.cell x y true packed)) "Retained prepared painter differs from ordinary painter"
    compare()
    for i = 0 to 19 do
        let color = if i%2=0 then Rgb 0x55AA33 else Rgb 0xAA5533
        let declarations = [Css.height (Css.cells 5);Css.border Rounded;Css.padding 1;Css.wrap true;Css.color color]
        Node.setStyle declarations firstChild
        Node.setStyle declarations secondChild
        Node.setText (if i%2=0 then "longer text é 界 with wrapped words" else "short text") firstChild
        Node.setText (if i%2=0 then "longer text é 界 with wrapped words" else "short text") secondChild
        Node.setScroll (i%4) 0 firstCustom
        Node.setScroll (i%4) 0 secondCustom
        firstCustom.Focused <- true
        secondCustom.Focused <- true
        Node.setCaret (i%5) firstCustom
        Node.setCaret (i%5) secondCustom
        compare()
    Node.setInheritColors false firstCustom
    Node.setInheritColors false secondCustom
    compare()
    Node.setChildren [|firstCustom|] firstRoot
    Node.setChildren [|secondCustom|] secondRoot
    compare()
    Renderer.resize 31 12 normal
    PreparedRenderer.resize 31 12 packed
    compare()
    let warm = Stopwatch.StartNew()
    let mutable i = 0
    let frame index =
        Node.setCaret (index%5) secondCustom
        PreparedLayout.refresh false cache
        PreparedLayout.present sink cache
    while warm.ElapsedMilliseconds < 1000L do frame i;i <- i+1
    let before = GC.GetAllocatedBytesForCurrentThread()
    for index = 0 to 9999 do frame index
    let allocated = GC.GetAllocatedBytesForCurrentThread()-before
    printfn "measured here: retained prepared caret/refresh/layout/paint/encode/commit: 10000 changing frames; %d allocated bytes" allocated
    check (allocated=0L) "Retained prepared stable handles allocated"
    View.dispose first
    View.dispose second
    use limitedGraph = new Graph()
    let limitedNode = Node.create [] [||]
    Node.setText "x" limitedNode
    let limitedView = View.mount limitedGraph (fun () -> limitedNode)
    let limitedRenderer = PreparedRenderer.create 8 3 {capacity with Paints=3} caps
    let limitedCache = PreparedLayout.create limitedView limitedRenderer
    PreparedLayout.present sink limitedCache
    Node.setStyle [Css.color (Rgb 0x123456)] limitedNode
    expectFailure (fun () -> PreparedLayout.refresh false limitedCache)
    expectFailure (fun () -> PreparedLayout.refresh false limitedCache)
    expectFailure (fun () -> PreparedLayout.draw limitedCache)
    check ((PreparedRenderer.cell 0 0 true limitedRenderer).Paint=Paint.empty) "Failed adapter registration changed accepted snapshot"
    View.dispose limitedView
