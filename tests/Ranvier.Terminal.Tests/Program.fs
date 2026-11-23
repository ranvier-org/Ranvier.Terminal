module Ranvier.Terminal.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

let check condition message =
    if not condition then
        failwith message

let equal expected actual message =
    if expected <> actual then
        failwithf "%s: expected %A, got %A" message expected actual

let capabilities =
    { Colors = TrueColor
      SynchronizedOutput = true }

let sink =
    { Write = (fun (_: byte array) _ _ -> ())
      Flush = (fun () -> ()) }

let capture (renderer: Renderer) =
    Encoding.UTF8.GetString(renderer.Bytes, 0, renderer.Count)

let key code scalar mods paste =
    Key
        { Key = code
          Scalar = scalar
          Modifiers = mods
          Released = false
          Paste = paste }

let tests = ResizeArray<string * (unit -> unit)>()
let test name body = tests.Add(name, body)

test "prepared retained view parity" PreparedChecks.runLayout
test "subprocess key encoding" TerminalKeysChecks.run
test "streaming output buffer" OutputBufferChecks.run
test "reactive output frame" OutputViewChecks.run
test "styled panel, meter and chart" StyledChecks.run
test "styled virtual table" TableChecks.run

test "terminal decoder supplies typed shortcut chords" (fun () ->
    let events = ResizeArray<InputEvent>()
    let parser = InputParser.create events.Add
    for b in Encoding.UTF8.GetBytes "\u001b[1;3C🙂" do InputParser.feed b parser
    let altRight =
        Ranvier.Keyboard.KeyChord.ofLogical (Ranvier.Keyboard.Navigation Ranvier.Keyboard.ArrowRight)
            { Ranvier.Keyboard.KeyModifiers.None with Alt = true }
    let mutable invoked = 0
    let bindings: Commands.Binding array =
        [| { Chord = altRight; Shortcut = "Move"; Caption = "Right"; Enabled = fun () -> true
             Invoke = fun () -> invoked <- invoked + 1 } |]
    let dispatch = Commands.dispatchBindings bindings
    match Seq.toList events with
    | [Key right; Key emoji] ->
        equal altRight right.Chord "modified arrow"
        equal (Ranvier.Keyboard.KeyChord.ofRune (Rune 0x1F642)) emoji.Chord "supplementary scalar"
        check (dispatch (Key right)) "typed chord dispatched independently of label"
        let flagged paste released (event: KeyEvent) = Key { event with Paste = paste; Released = released }
        check (not (dispatch (flagged true false right))) "paste ignored"
        check (not (dispatch (flagged false true right))) "release ignored"
        equal 1 invoked "invocation count"
    | other -> failwithf "unexpected decoded input %A" other)

test "shortcut, help and palette share command availability" (fun () ->
    use graph = new Graph()
    let enabled = graph.Run(fun () -> createSignal true)
    let mutable count = 0
    let commands: Commands.Command array =
        [| { Key=Key.Character; Scalar=int 'r'; Modifiers=Modifiers.Control; Shortcut="Ctrl+R"; Caption="Retry"
             Enabled=(fun () -> enabled.Value); Invoke=(fun () -> count <- count + 1) } |]
    let mounted = View.mount graph (fun () ->
        let editor, popup = Commands.palette [] [] 3 commands
        Popup.host (column [] { editor; Commands.help [] commands (fun () -> false) }) [| popup |])
    try
        let frame = Frame.create 30 6
        let render () = Layout.renderReactive mounted frame |> ignore
        let editor = mounted.Root.Children[0].Children[0]
        let hint = mounted.Root.Children[0].Children[1]
        render ()
        equal "Ctrl+R Retry" hint.Text.Source "shortcut help"
        View.focus (Some editor) mounted
        View.dispatch (key Key.Character (int 't') Modifiers.None false) mounted |> ignore
        render ()
        View.dispatch (key Key.Enter 0 Modifiers.None false) mounted |> ignore
        equal 1 count "palette invocation"
        check (Commands.dispatch commands (key Key.Character (int 'r') Modifiers.Control false)) "shortcut invocation"
        enabled.Value <- false
        render ()
        equal "" hint.Text.Source "disabled command removed from help"
        check (not (Commands.dispatch commands (key Key.Character (int 'r') Modifiers.Control false))) "disabled shortcut"
        equal 2 count "consistent invocation count"
    finally View.dispose mounted)
test "FIGfont parsing, layout and retained text" FigletChecks.run
test "anchored popup geometry and pointer routing" PopupHostChecks.run
test "tooltip and completion interactions" PopupWidgetChecks.run
test "task status animations and idle subscriptions" TaskStatusChecks.run
test "keyed select and multi-select navigation" SelectChecks.run
test "overflow scrollbar styles and rendering" ScrollbarChecks.run
test "owned event, style and keyed child composition" CompositionChecks.run
test "retained tree, collapsible section and tabs" NavigationChecks.run

test "fixed and variable-height virtualization" VirtualizationChecks.run
test "owned prepared renderer" PreparedChecks.run

test "overlays paint and hit-test the top child and restore uncovered cells" (fun () ->
    use graph = new Graph()
    let mutable clicked = false
    let popup = Widgets.button [ Width(Cells 3); Height(Cells 1); Margin { Style.zero with Left=2; Top=1 } ] "top" (fun () -> clicked <- true)
    let mounted = View.mount graph (fun () ->
        overlay [] {
            label "base\nlower"
            popup
        })
    try
        let frame = Frame.create 8 3
        Layout.renderReactive mounted frame |> ignore
        equal "t" frame.Cells[10].Glyph "popup draws over underlying text"
        View.dispatch (Mouse {X=2;Y=1;Button=0;Action=MouseAction.Press;Delta=0;Modifiers=Modifiers.None}) mounted |> ignore
        check clicked "click reached upper layer"
        Node.setStyle [ Visible false ] popup
        Layout.renderReactive mounted frame |> ignore
        equal "w" frame.Cells[10].Glyph "hiding popup reveals underlying text"
    finally View.dispose mounted)

test "initial, unchanged, sparse and erased frames" (fun () ->
    let r = Renderer.create 8 2 4096 capabilities
    Renderer.present sink r
    check ((capture r).StartsWith "\u001b[?2026h") "synchronized start"
    check ((capture r).EndsWith "\u001b[?2026l") "synchronized end"
    equal 0 (Renderer.encode r) "unchanged frame writes nothing"
    Renderer.commit r
    Frame.put (Frame.bounds r.Back) 3 1 "x" 1 Paint.empty r.Back
    Renderer.present sink r
    equal 1 r.EncodedCells "only changed cell encoded"
    check ((capture r).Contains "\u001b[2;4H") "absolute cursor"
    check (not ((capture r).Contains "2J")) "no clear-screen flicker"
    Frame.clear Paint.empty r.Back
    Renderer.present sink r
    equal 1 r.EncodedCells "erased cell overwritten"
    check ((capture r).Contains "m ") "space overwrites old glyph")

test "wide glyph overwrite repairs both cells" (fun () ->
    let frame = Frame.create 5 1
    let bounds = Frame.bounds frame
    Frame.put bounds 1 0 "界" 2 Paint.empty frame
    equal 0uy frame.Cells[2].Width "continuation"
    Frame.put bounds 2 0 "x" 1 Paint.empty frame
    equal " " frame.Cells[1].Glyph "old head erased"
    equal "x" frame.Cells[2].Glyph "new glyph"
    Frame.put bounds 4 0 "界" 2 Paint.empty frame
    equal " " frame.Cells[4].Glyph "wide glyph cannot spill past edge")

test "grapheme preparation and escape sanitization" (fun () ->
    let run = TextRun.prepare "e\u0301界👩‍💻\n\u001b[31m"
    equal "e\u0301" run.Glyphs[0].Text "combining cluster retained"
    equal 2 run.Glyphs[1].Columns "CJK width"
    equal "👩‍💻" run.Glyphs[2].Text "ZWJ cluster retained"
    check (run.Glyphs |> Array.forall (fun g -> not (g.Text.Contains '\u001b'))) "input cannot inject escape sequence"
    let custom = TextRun.prepareWith (fun _ -> 2) "α"
    equal 2 custom.Width "custom ambiguous-width policy")

test "single-cell text preparation matches grapheme enumeration" (fun () ->
    let drawing = String([| '\u2500' .. '\u259f' |])
    let ascii = String([| ' ' .. '~' |])
    for text in [ ""; "\n\n"; ascii + "\n" + drawing + "\n"
                  "first\r\nlast\r"; "█\u0301x"; "1\uFE0F\u20E3"; "x\t\u001b"; "abc👩‍💻" ] do
        let reference = TextRun.prepareWith TextRun.defaultWidth text
        let actual = TextRun.prepare text
        equal reference actual "prepared glyphs, offsets, dimensions and empty line cache"
        equal (TextRun.lines 7 true true reference) (TextRun.lines 7 true true actual) "wrapped line ranges"
    let custom = TextRun.prepareWith (fun _ -> 2) "█A"
    equal 4 custom.Width "custom width applies to single-cell art")

test "failed write invalidates snapshot and releases synchronization" (fun () ->
    let r = Renderer.create 4 1 1024 capabilities
    Renderer.present sink r
    Frame.put (Frame.bounds r.Back) 0 0 "x" 1 Paint.empty r.Back
    let writes = ResizeArray<string>()
    let mutable fail = true

    let output =
        { Write =
            fun bytes offset count ->
                writes.Add(Encoding.UTF8.GetString(bytes, offset, count))

                if fail then
                    fail <- false
                    raise (IOException "simulated partial write")
          Flush = fun () -> () }

    try
        Renderer.present output r
        failwith "expected IOException"
    with :? IOException ->
        ()

    check (not r.Valid) "invalid snapshot after partial output"
    equal " " r.Front.Cells[0].Glyph "snapshot not committed"
    equal "\u001b[?2026l" writes[1] "synchronization released"
    Renderer.present sink r
    equal 4 r.EncodedCells "retry fully redraws")

test "capacity failure happens before output" (fun () ->
    let r = Renderer.create 100 2 64 capabilities
    let mutable writes = 0

    let output =
        { Write = (fun _ _ _ -> writes <- writes + 1)
          Flush = (fun () -> ()) }

    try
        Renderer.present output r
        failwith "expected overflow"
    with :? InvalidOperationException ->
        ()

    equal 0 writes "nothing emitted"
    Renderer.reserve 30000 r
    Renderer.present output r
    equal 1 writes "complete frame emitted after reservation")

test "resize, palette fallback, monochrome" (fun () ->
    let r =
        Renderer.create
            2
            1
            4096
            { Colors = Palette256
              SynchronizedOutput = false }

    let paint =
        { Paint.empty with
            Foreground = Rgb 0xFF0000 }

    Frame.put (Frame.bounds r.Back) 0 0 "a" 1 paint r.Back
    Renderer.present sink r
    check ((capture r).Contains ";38;5;196") "palette fallback"
    Renderer.resize 3 2 r
    Renderer.present sink r
    equal 6 r.EncodedCells "resize invalidates full frame"

    let mono =
        Renderer.create
            2
            1
            4096
            { Colors = Monochrome
              SynchronizedOutput = false }

    Frame.put (Frame.bounds mono.Back) 0 0 "a" 1 paint mono.Back
    Renderer.present sink mono
    check (not ((capture mono).Contains ";38")) "no color in monochrome")

test "fragmented input, mouse, paste, escape and malformed UTF8" (fun () ->
    let events = ResizeArray<InputEvent>()
    let parser = InputParser.create events.Add

    let send (text: string) =
        let bytes = Encoding.UTF8.GetBytes text

        for b in bytes do
            InputParser.feed b parser

    send "\u001b[1;5A界\u001b[<0;4;2M\u001b[<32;4;8M\u001b[<0;4;8m\u001b[200~x\u001b[A\u001b[201~"
    equal (key Key.Up 0 Modifiers.Control false) events[0] "modified arrow"
    equal (key Key.Character (int '界') Modifiers.None false) events[1] "fragmented UTF8"

    for index, action, y in [ 2, MouseAction.Press, 1; 3, MouseAction.Move, 7; 4, MouseAction.Release, 7 ] do
        match events[index] with
        | Mouse m -> equal (3, y, 0, action) (m.X, m.Y, m.Button, m.Action) "fragmented SGR drag"
        | _ -> failwith "mouse expected"

    check (events |> Seq.contains (key Key.Character (int 'A') Modifiers.None true)) "paste cannot inject navigation"
    check (events |> Seq.contains (PasteBoundary false)) "paste terminated"
    InputParser.feed 27uy parser
    InputParser.flushEscape parser
    equal (key Key.Escape 0 Modifiers.None false) events[events.Count - 1] "standalone Escape"
    InputParser.feed 0xE2uy parser
    InputParser.feed (byte 'x') parser
    equal (key Key.Character (int 'x') Modifiers.None false) events[events.Count - 1] "invalid UTF8 resynchronizes"
    send ("\u001b[" + String('1', 200) + "~q")
    equal (key Key.Character (int 'q') Modifiers.None false) events[events.Count - 1] "oversized CSI resynchronizes")

test "reactive bindings update retained nodes and dispose" (fun () ->
    use graph = new Graph()
    let mutable source = Unchecked.defaultof<Signal<string>>

    let mounted =
        View.mount graph (fun () ->
            source <- createSignal "before"
            signalText source)

    let node = mounted.Root
    source.Value <- "after"
    equal "a" node.Text.Glyphs[0].Text "bound text updated"
    View.dispose mounted
    source.Value <- "ignored"
    equal "a" node.Text.Glyphs[0].Text "disposed binding stopped")

test "flex layout, padding, clipping and style states" (fun () ->
    use graph = new Graph()

    let mounted =
        View.mount graph (fun () ->
            row (
                styles {
                    Css.padding 1
                    Css.gap 1
                }
            ) {
                panel (
                    styles {
                        Css.width (Css.cells 4)
                        Css.border Single
                    }
                ) {
                    "left"
                }

                panel (styles { Css.width (Css.flex 1) }) { "right" }
            })

    mounted.Rules <- [| Rule.focus [ Css.backgroundColor (Rgb 0x223344) ] |]
    let frame = Frame.create 20 6
    Layout.render mounted frame
    equal 4 mounted.Root.Children[0].Bounds.Width "fixed width"
    equal 13 mounted.Root.Children[1].Bounds.Width "remaining width"
    equal 1 mounted.Root.Children[0].Bounds.X "padding"
    equal "┌" frame.Cells[21].Glyph "border painted"
    mounted.Root.Children[0].Focused <- true
    Layout.render mounted frame
    equal (Rgb 0x223344) frame.Cells[21].Paint.Background "focus rule"
    View.dispose mounted)

test "focus, button activation, Unicode text editing" (fun () ->
    use graph = new Graph()
    let mutable value = Unchecked.defaultof<Signal<string>>
    let mutable clicks = 0

    let mounted =
        View.mount graph (fun () ->
            value <- createSignal "a👩‍💻"

            column [] {
                Widgets.button [] "ok" (fun () -> clicks <- clicks + 1)
                Widgets.input [] value
            })

    let frame = Frame.create 20 4
    Layout.render mounted frame
    View.dispatch (key Key.Tab 0 Modifiers.None false) mounted |> ignore
    View.dispatch (key Key.Enter 0 Modifiers.None false) mounted |> ignore
    equal 1 clicks "button activated"
    View.dispatch (key Key.Tab 0 Modifiers.None false) mounted |> ignore
    View.dispatch (key Key.Backspace 0 Modifiers.None false) mounted |> ignore
    equal "a" (value.Peek) "whole grapheme removed"
    View.dispatch (key Key.Character 0x1F600 Modifiers.None true) mounted |> ignore
    equal "a😀" (value.Peek) "pasted scalar inserted"
    View.dispose mounted)

test "dynamic branch owner cleanup" (fun () ->
    use graph = new Graph()
    let mutable visible = Unchecked.defaultof<Signal<bool>>
    let mutable cleanups = 0

    let mounted =
        View.mount graph (fun () ->
            visible <- createSignal true

            show (fun () -> visible.Value) (fun () ->
                onCleanup (fun () -> cleanups <- cleanups + 1)
                label "child"))

    visible.Value <- false
    equal 1 cleanups "removed branch cleaned up"
    visible.Value <- true
    View.dispose mounted
    equal 2 cleanups "remaining branch cleaned up")

test "randomized diff roundtrip against terminal model" (fun () ->
    let r = Renderer.create 19 7 65536 capabilities
    let screen = Frame.create 19 7
    let random = Random 8675309

    let apply () =
        let output = capture r
        let mutable i, x, y = 0, 0, 0

        while i < output.Length do
            if output[i] = '\u001b' then
                let start = i + 2
                i <- start

                while i < output.Length && not (output[i] >= '@' && output[i] <= '~') do
                    i <- i + 1

                if output[i] = 'H' then
                    let numbers = output.Substring(start, i - start).Split ';'
                    y <- int numbers[0] - 1
                    x <- int numbers[1] - 1
                elif output[i] = 'C' then
                    x <- x + int (output.Substring(start, i - start))

                i <- i + 1
            else
                let glyph = Globalization.StringInfo.GetNextTextElement(output, i)
                let width = TextRun.defaultWidth glyph
                Frame.put (Frame.bounds screen) x y glyph width Paint.empty screen
                x <- x + width
                i <- i + glyph.Length

    for step = 0 to 499 do
        for _ = 0 to 9 do
            let x, y = random.Next 19, random.Next 7

            let glyph =
                match random.Next 4 with
                | 0 -> "界"
                | 1 -> "e\u0301"
                | 2 -> " "
                | _ -> "x"

            Frame.put (Frame.bounds r.Back) x y glyph (TextRun.defaultWidth glyph) Paint.empty r.Back

        Renderer.encode r |> ignore
        apply ()

        for index = 0 to screen.Cells.Length - 1 do
            check (Cell.equal screen.Cells[index] r.Back.Cells[index]) (sprintf "roundtrip step %d cell %d" step index)

        Renderer.commit r)

test "wrapping, bounded scroll bubbling and visible caret" (fun () ->
    use graph = new Graph()
    let mutable value = Unchecked.defaultof<Signal<string>>

    let mounted =
        View.mount graph (fun () ->
            value <- createSignal "abcdef"

            column [] {
                panel [ Css.wrap true ] { }
                Widgets.input [ Css.height (Css.cells 1) ] value

                for i = 1 to 8 do
                    label (string i)
            })

    let wrapped = mounted.Root.Children[0]
    Node.setText "abcdefghij" wrapped
    let frame = Frame.create 5 5
    Layout.render mounted frame
    equal 4 wrapped.Bounds.Width "right gutter leaves four text columns"
    equal 3 wrapped.Bounds.Height "wrapped intrinsic height"
    View.focus (Some mounted.Root.Children[1]) mounted
    Layout.render mounted frame
    let input = mounted.Root.Children[1]
    equal 3 input.ScrollX "caret scrolled into view"
    equal Attributes.Reverse frame.Cells[3 * frame.Width + 3].Paint.Attributes "visible caret"

    View.dispatch
        (Mouse
            { X = 0
              Y = 0
              Button = 0
              Action = MouseAction.Scroll
              Delta = 1
              Modifiers = Modifiers.None })
        mounted
    |> ignore

    equal 1 mounted.Root.ScrollY "wheel bubbles through text to scrollable parent"

    for _ = 0 to 100 do
        View.dispatch
            (Mouse
                { X = 0
                  Y = 0
                  Button = 0
                  Action = MouseAction.Scroll
                  Delta = 1
                  Modifiers = Modifiers.None })
            mounted
        |> ignore

    equal mounted.Root.MaxScrollY mounted.Root.ScrollY "scroll stops at extent"
    View.dispose mounted)

test "word wrapping defaults, opt-out, Unicode and clipped layout" (fun () ->
    let lines width wordWrap text =
        let run = TextRun.prepare text
        let result = ResizeArray<string>()
        let mutable start = 0
        while start < run.Glyphs.Length do
            let struct (finish, next) = TextRun.lineEnd width true wordWrap run start
            result.Add(String.Concat(run.Glyphs[start .. finish - 1] |> Array.map (fun g -> g.Text)))
            check (next > start) "line breaking must advance"
            start <- next
        result.ToArray()
    equal [| "hello"; "world" |] (lines 8 true "hello world") "keep words together"
    equal [| "hello wo"; "rld" |] (lines 8 false "hello world") "character opt-out"
    equal [| "hello"; "world" |] (lines 5 true "hello   world") "discard soft-break separators"
    equal [| "abcd"; "efgh"; "ij" |] (lines 4 true "abcdefghij") "oversized word fallback"
    equal [| "界界"; "e\u0301👩‍💻" |] (lines 5 true "界界 e\u0301👩‍💻") "grapheme and cell widths"
    equal [| "a"; ""; "b" |] (lines 8 true "a\n\nb") "explicit blank lines"
    equal [| "hello"; "world" |] (lines 5 true "hello \nworld") "soft break must not duplicate explicit newline"
    equal [| "hello"; ""; "world" |] (lines 5 true "hello  \n\nworld") "preserve additional explicit blank line"
    equal 2 (TextRun.wrappedHeight 8 true (TextRun.prepare "a\n")) "trailing newline"
    equal 2 (TextRun.wrappedHeight 5 true (TextRun.prepare "hello \n")) "soft break followed by trailing newline"
    equal 0 (TextRun.wrappedHeight 0 true TextRun.empty) "empty input"
    equal [| "界"; "a" |] (lines 1 true "界a") "oversized glyph makes progress"

    use graph = new Graph()
    let mutable paragraph = Unchecked.defaultof<Node>
    let mounted =
        View.mount graph (fun () ->
            paragraph <- Node.create [ Css.wrap true ] [||]
            Node.setText "hello world again" paragraph
            column [] { paragraph; "after" })
    let frame = Frame.create 8 8
    Layout.render mounted frame
    equal 3 paragraph.Bounds.Height "word-aware intrinsic height"
    equal "w" frame.Cells[8].Glyph "paint uses same word break"
    equal 3 mounted.Root.Children[1].Bounds.Y "following child placed after wrapped text"
    paragraph.Style <- Style.merge [ Css.wordWrap false ] paragraph.Style
    Layout.render mounted frame
    equal "o" frame.Cells[7].Glyph "style opt-out paints mid-word"
    paragraph.Style <- Style.merge [ Css.wrap false ] paragraph.Style
    Layout.render mounted frame
    equal 1 paragraph.Bounds.Height "wrapping can be disabled"
    // Clipping must not change the width used to choose line breaks.
    Frame.clear Paint.empty frame
    Frame.textWithWrapping { X = 0; Y = 0; Width = 3; Height = 8 }
        0 0 8 true true (TextRun.prepare "hello world") Paint.empty frame
    equal "w" frame.Cells[8].Glyph "clip does not reflow text"
    View.dispose mounted)

test "reactive rendering matches full rendering under mutations" (fun () ->
    use graph = new Graph()
    let mutable value = Unchecked.defaultof<Signal<string>>
    let mutable visible = Unchecked.defaultof<Signal<bool>>
    let mounted = View.mount graph (fun () ->
        value <- createSignal "hello 界 e\u0301"
        visible <- createSignal true
        column [ Css.padding 1; Css.border Rounded ] {
            signalText value |> bindStyle (fun () -> [ Css.wrap true; Css.width (Css.cells 12) ])
            show (fun () -> visible.Value) (fun () -> column [] { "branch"; "second" })
            Widgets.input [] value
            for i = 0 to 15 do label (sprintf "row %d 界" i)
        })
    let frame = Frame.create 30 12
    let reference = Frame.create 30 12
    let r = Renderer.create 30 12 100000 capabilities
    let random = Random 61823
    let checkFrame () =
        if Layout.renderReactive mounted frame then
            Array.Copy(frame.Cells, r.Back.Cells, frame.Cells.Length)
            Renderer.presentRegion sink mounted.Damage r
        let rec clone node =
            { node with Children = Array.map clone node.Children; Parent = None }
        let referenceView = { mounted with Root = clone mounted.Root; LastFrame = None }
        Layout.render referenceView reference
        for i = 0 to frame.Cells.Length - 1 do
            check (Cell.equal reference.Cells[i] frame.Cells[i]) (sprintf "incremental mismatch at %d" i)
            check (Cell.equal reference.Cells[i] r.Front.Cells[i]) (sprintf "commit mismatch at %d" i)
    checkFrame ()
    for iteration = 0 to 399 do
        match random.Next 8 with
        | 0 -> value.Value <- if iteration &&& 1 = 0 then "A👩‍💻 long word test" else "界 short"
        | 1 -> visible.Value <- not visible.Peek
        | 2 -> Node.setScroll 0 (random.Next 20) mounted.Root
        | 3 -> Node.setStyle [ Css.color (Rgb(random.Next 0xFFFFFF)) ] mounted.Root.Children[4]
        | 4 -> View.focus (Some mounted.Root.Children[2]) mounted
               View.dispatch (key Key.Left 0 Modifiers.None false) mounted |> ignore
        | 5 -> Node.setStyle [ Css.visible (iteration % 3 <> 0); Css.height (Css.cells (1 + iteration % 3)) ] mounted.Root.Children[5]
        | 6 -> mounted.Rules <- if iteration &&& 1 = 0 then [||] else [| Rule.focus [ Css.color (Rgb 0xABCDEF) ] |]
        | _ -> Node.setChildren [| label (sprintf "replacement %d" iteration) |] mounted.Root.Children[6]
        checkFrame ()
    check (not (Layout.renderReactive mounted frame)) "idle frame should do no work"
    let resized = Frame.create 17 8
    check (Layout.renderReactive mounted resized) "new framebuffer forces full render"
    View.dispose mounted)

test "bulk fills repair wide pairs crossing their edges" (fun () ->
    let random = Random 892
    for _ = 0 to 199 do
        let fast, reference = Frame.create 20 3, Frame.create 20 3
        for x = 0 to 18 do
            if random.Next 3 = 0 then
                Frame.put (Frame.bounds fast) x 1 "界" 2 Paint.empty fast
                Frame.put (Frame.bounds reference) x 1 "界" 2 Paint.empty reference
        let rect = { X = random.Next 20; Y = 1; Width = random.Next 21; Height = 1 }
        let paint = { Paint.empty with Background = Rgb 0x123456 }
        Frame.fill rect paint fast
        let clipped = Rect.intersect rect (Frame.bounds reference)
        for x = clipped.X to clipped.X + clipped.Width - 1 do Frame.put clipped x 1 " " 1 paint reference
        for i = 0 to fast.Cells.Length - 1 do check (Cell.equal fast.Cells[i] reference.Cells[i]) "bulk fill edge mismatch")

test "style deltas and cursor choices roundtrip independently" (fun () ->
    let r = Renderer.create 37 9 200000 capabilities
    let screen = Frame.create 37 9
    let random = Random 8192
    let colors = [| Default; Indexed 5uy; Rgb 0xABCDEF; Rgb 0x102030 |]
    for iteration = 0 to 349 do
        for _ = 0 to 24 do
            let x, y = random.Next 37, random.Next 9
            let paint = { Foreground = colors[random.Next colors.Length]
                          Background = colors[random.Next colors.Length]
                          Attributes = enum<Attributes> (random.Next 64) }
            let glyph = [| "A"; " "; "界"; "e\u0301"; "👩‍💻" |][random.Next 5]
            Frame.put (Frame.bounds r.Back) x y glyph (TextRun.defaultWidth glyph) paint r.Back
        Renderer.encode r |> ignore
        AnsiModel.apply (capture r) screen
        for i = 0 to screen.Cells.Length - 1 do
            check (Cell.equal screen.Cells[i] r.Back.Cells[i]) (sprintf "style roundtrip %d/%d" iteration i)
        Renderer.commit r)

test "virtual list retains bounded nodes and follows Ranvier state" (fun () ->
    use graph = new Graph()
    let items, selected = graph.Run(fun () -> createSignal (Array.init 10000 string), createSignal 0)
    let mounted = View.mount graph (fun () -> Widgets.virtualList [] 8 items selected)
    let rows = Array.copy mounted.Root.Children
    let frame = Frame.create 20 8
    Layout.renderReactive mounted frame |> ignore
    selected.Value <- 9999
    check (Layout.renderReactive mounted frame) "selection invalidates viewport"
    equal "9999" rows[7].Text.Source "selection kept in viewport"
    for i = 0 to rows.Length - 1 do check (obj.ReferenceEquals(rows[i], mounted.Root.Children[i])) "row node reused"
    items.Value <- [| "only" |]
    Layout.renderReactive mounted frame |> ignore
    equal "only" rows[0].Text.Source "shrinking source clamps viewport"
    check (not rows[1].Computed.Visible) "unused rows hidden"
    items.Value <- [||]
    Layout.renderReactive mounted frame |> ignore
    check (not rows[0].Computed.Visible) "empty source hides rows"
    items.Value <- [| "back"; "again" |]
    selected.Value <- 0
    Layout.renderReactive mounted frame |> ignore
    equal "back" rows[0].Text.Source "repopulated source"
    View.dispose mounted
    items.Value <- [| "disposed" |]
    equal "back" rows[0].Text.Source "disposed effect is detached")

test "horizontal clipping matches a full prepared-text reference" (fun () ->
    let run = TextRun.prepare (String.replicate 100 "ab界e\u0301 👩‍💻 ")
    for offset = 0 to 899 do
        let actual, expected = Frame.create 19 2, Frame.create 19 2
        Frame.textWithWrapping (Frame.bounds actual) -offset 0 19 false true run Paint.empty actual
        let mutable column = -offset
        for glyph in run.Glyphs do
            Frame.put (Frame.bounds expected) column 0 glyph.Text glyph.Columns Paint.empty expected
            column <- column + glyph.Columns
        for i = 0 to actual.Cells.Length - 1 do
            check (Cell.equal actual.Cells[i] expected.Cells[i]) "horizontal clipping changed a wide/combining cell")

test "list selection preserves row identity and changes only selection paint" (fun () ->
    use graph = new Graph()
    let selected = graph.Run(fun () -> createSignal 0)
    let mounted = View.mount graph (fun () -> Widgets.list [] [| "one"; "two"; "three" |] selected)
    let first, second = mounted.Root.Children[0], mounted.Root.Children[1]
    let renderer = Renderer.create 20 5 10000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    selected.Value <- 1
    check (obj.ReferenceEquals(first, mounted.Root.Children[0])) "list must retain existing rows"
    check (obj.ReferenceEquals(second, mounted.Root.Children[1])) "selected row identity must survive"
    Layout.presentReactive sink mounted renderer |> ignore
    equal Attributes.None renderer.Front.Cells[0].Paint.Attributes "old selection cleared"
    equal Attributes.Reverse renderer.Front.Cells[20].Paint.Attributes "new selection highlighted"
    selected.Value <- -1
    Layout.presentReactive sink mounted renderer |> ignore
    equal Attributes.None renderer.Front.Cells[20].Paint.Attributes "invalid selection clears highlight"
    View.dispose mounted)

test "conservative rendering cannot leave stale reactive geometry" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [ Css.border Single ] { "hello world" })
    let original = Frame.create 30 8
    let other = Frame.create 12 4
    Layout.renderReactive mounted original |> ignore
    Layout.render mounted other
    check (Layout.renderReactive mounted original) "returning from a full render must restore cached geometry"
    equal 30 mounted.Root.Bounds.Width "original viewport restored"
    View.dispose mounted)

test "reactive presentation retries capacity and flush failures without another mutation" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> label "first")
    let renderer = Renderer.create 100 5 64 capabilities
    try
        Layout.presentReactive sink mounted renderer |> ignore
        failwith "expected capacity failure"
    with :? InvalidOperationException -> ()
    check (not renderer.Valid) "capacity failure invalidates presentation"
    Renderer.reserve 100000 renderer
    check (Layout.presentReactive sink mounted renderer) "capacity retry presents unchanged tree"
    Node.setText "other" mounted.Root
    let failing = { sink with Flush = fun () -> raise (IOException "flush failed") }
    try
        Layout.presentReactive failing mounted renderer |> ignore
        failwith "expected flush failure"
    with :? IOException -> ()
    equal "f" renderer.Front.Cells[0].Glyph "failed output cannot commit"
    check (Layout.presentReactive sink mounted renderer) "flush retry presents unchanged tree"
    equal "o" renderer.Front.Cells[0].Glyph "retry commits new screen"
    check (not (Layout.presentReactive sink mounted renderer)) "idle skips output"
    equal 0 renderer.ComparedCells "idle skips comparison"
    equal 0 renderer.CopiedCells "idle skips copying"
    Renderer.invalidate renderer
    check (Layout.presentReactive sink mounted renderer) "external output forces presentation"
    equal renderer.Back.Cells.Length renderer.EncodedCells "external damage redraws all cells"
    View.dispose mounted)

test "paint-only inherited colors respect active style rules" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [] { label "child" |> id "child" })
    mounted.Rules <- [| { Selector = { Id = Some "child"; Class = None; Focused = None; Hovered = None };
                         Declarations = [ Css.backgroundColor (Rgb 0x123456) ] } |]
    let renderer = Renderer.create 20 5 10000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setStyle [ Css.color (Rgb 0xABCDEF) ] mounted.Root
    Layout.presentReactive sink mounted renderer |> ignore
    equal (Rgb 0xABCDEF) renderer.Front.Cells[0].Paint.Foreground "inherited paint refreshed"
    equal (Rgb 0x123456) renderer.Front.Cells[0].Paint.Background "child rule preserved"
    Node.setStyle [ Css.color (Rgb 0x102030) ] mounted.Root.Children[0]
    Layout.presentReactive sink mounted renderer |> ignore
    equal (Rgb 0x102030) renderer.Front.Cells[0].Paint.Foreground "base paint changes"
    equal (Rgb 0x123456) renderer.Front.Cells[0].Paint.Background "rule survives base paint change"
    equal 20 renderer.ComparedCells "paint-only child compares its row"
    Node.setChildren [| mounted.Root.Children[0]; label "other" |] mounted.Root
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setStyle [ Css.color (Rgb 0x556677) ] mounted.Root.Children[0]
    Node.setText "changed geometry" mounted.Root.Children[1]
    Layout.presentReactive sink mounted renderer |> ignore
    equal (Rgb 0x123456) renderer.Front.Cells[0].Paint.Background "paint rules survive an unrelated geometry update"
    View.dispose mounted)

test "prepared horizontal scrolling allocates no managed memory" (fun () ->
    let run = TextRun.prepare (String.replicate 2000 "abc界e\u0301 ")
    let frame = Frame.create 80 2
    let draw index =
        Frame.clear Paint.empty frame
        Frame.textWithWrapping (Frame.bounds frame) (-10000 - (index &&& 1)) 0 80 false true run Paint.empty frame
    let warmup = Stopwatch.StartNew()
    while warmup.Elapsed < TimeSpan.FromSeconds 1. do draw 0
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i = 0 to 9999 do draw i
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    printfn "measured here: prepared horizontal scroll painting: 10000 frames; %d managed bytes" allocated
    equal 0L allocated "horizontal clipping allocation gate")

test "distant paint damage compares only affected rows" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [] { for i = 0 to 39 do label "same" })
    let renderer = Renderer.create 80 50 500000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setText "next" mounted.Root.Children[0]
    Node.setText "next" mounted.Root.Children[39]
    Layout.presentReactive sink mounted renderer |> ignore
    equal 160 renderer.ComparedCells "distant dirty rows must not scan the intervening rows"
    equal 160 renderer.CopiedCells "distant dirty rows must not copy the intervening rows"
    equal "n" renderer.Front.Cells[39 * 80].Glyph "last dirty row reaches front snapshot"
    View.dispose mounted)

test "hiding and restoring a root clears its previous pixels" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> label "界 root")
    let renderer = Renderer.create 12 3 50000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setStyle [ Css.visible false ] mounted.Root
    check (Layout.presentReactive sink mounted renderer) "hiding root needs a presentation"
    for cell in renderer.Front.Cells do
        check (Cell.equal cell (Cell.blank Paint.empty)) "hidden root leaves a blank frame"
    Node.setStyle [ Css.visible true ] mounted.Root
    Layout.presentReactive sink mounted renderer |> ignore
    equal "界" renderer.Front.Cells[0].Glyph "restoring root redraws wide text"
    View.dispose mounted)

test "sparse rows recover after failed output and resize" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [] { for i = 0 to 7 do label "界x" })
    let renderer = Renderer.create 12 10 50000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setText "文y" mounted.Root.Children[0]
    Node.setText "文y" mounted.Root.Children[7]
    let bad = { sink with Write = fun _ _ _ -> raise (IOException "dirty rows fail") }
    try
        Layout.presentReactive bad mounted renderer |> ignore
        failwith "expected write failure"
    with :? IOException -> ()
    equal "界" renderer.Front.Cells[0].Glyph "failed row snapshot is not committed"
    Layout.presentReactive sink mounted renderer |> ignore
    equal "文" renderer.Front.Cells[7 * 12].Glyph "retry includes last dirty row"
    Renderer.resize 6 4 renderer
    Layout.presentReactive sink mounted renderer |> ignore
    equal 4 mounted.DamageRows.Length "row mask follows resize"
    let screen = Frame.create 6 4
    AnsiModel.apply (capture renderer) screen
    for i = 0 to screen.Cells.Length - 1 do
        check (Cell.equal screen.Cells[i] renderer.Front.Cells[i]) "resize output roundtrip"
    View.dispose mounted)

test "local geometry reuses clean measurements and explicit invalidation refreshes descendants" (fun () ->
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [] { for i = 0 to 39 do label "same" })
    let renderer = Renderer.create 80 50 500000 capabilities
    Layout.presentReactive sink mounted renderer |> ignore
    Node.setText "longer" mounted.Root.Children[0]
    Layout.presentReactive sink mounted renderer |> ignore
    equal 2 mounted.MeasuredNodes "only root and changed child need measurement"
    equal 80 renderer.ComparedCells "unchanged geometry only repaints changed text row"
    mounted.Root.Children[5].Text <- TextRun.prepare "direct field write"
    Node.invalidate mounted.Root
    Layout.presentReactive sink mounted renderer |> ignore
    equal 41 mounted.MeasuredNodes "explicit subtree invalidation must refresh descendants"
    equal 18 mounted.Root.Children[5].DesiredWidth "direct write measured"
    View.dispose mounted)

test "traced and untraced cohort metadata align" (fun () ->
#if RANVIER_TRACE
    let expectedTraced = true
#else
    let expectedTraced = false
#endif
    let hasMeta key (asm: System.Reflection.Assembly) =
        asm.GetCustomAttributes(typeof<System.Reflection.AssemblyMetadataAttribute>, false)
        |> Array.exists (fun attr ->
            let meta = attr :?> System.Reflection.AssemblyMetadataAttribute
            meta.Key = key && meta.Value = "true")
    let terminalAssemblies =
        [ typeof<Frame>.Assembly
          typeof<Ranvier.Terminal.Css.Style>.Assembly
          typeof<Ranvier.Terminal.View.Node>.Assembly
          typeof<TerminalOptions>.Assembly ]
    for asm in terminalAssemblies do
        check (hasMeta "RanvierLibrary" asm) ($"{asm.GetName().Name} carries RanvierLibrary metadata")
        equal expectedTraced (hasMeta "RanvierTrace" asm) ($"{asm.GetName().Name} RanvierTrace metadata matches RANVIER_TRACE")
    for asm in [ typeof<Graph>.Assembly; typeof<Ranvier.Keyboard.KeyChord>.Assembly ] do
        equal expectedTraced (hasMeta "RanvierTrace" asm) ($"{asm.GetName().Name} RanvierTrace metadata matches RANVIER_TRACE"))

let allocationTest () =
    use graph = new Graph()

    let mounted =
        View.mount graph (fun () ->
            row [ Css.padding 1; Css.gap 1 ] {
                panel [ Css.width (Css.flex 1); Css.border Rounded ] {
                    "Reactive terminal"
                    "界 e\u0301 👩‍💻"
                }

                panel [ Css.width (Css.flex 2); Css.color (Rgb 0xAABBCC) ] { "allocation probe" }
            })

    mounted.Rules <- [| Rule.focus [ Css.attributes Attributes.Bold ] |]
    let r = Renderer.create 80 24 300000 capabilities
    let first = mounted.Root.Children[0].Children[0]
    first.Style <- Style.merge [ Css.wrap true ] first.Style
    let a, b = TextRun.prepare "Frame A with several words wrapping across lines", TextRun.prepare "Frame B with other words wrapping across lines"

    let iteration index =
        first.Text <- if index &&& 1 = 0 then a else b
        Layout.render mounted r.Back
        Renderer.present sink r

    let warmup = Stopwatch.StartNew()
    let mutable warmIndex = 0

    while warmup.Elapsed < TimeSpan.FromSeconds 1. do
        iteration warmIndex
        warmIndex <- warmIndex + 1

    let before = GC.GetAllocatedBytesForCurrentThread()
    let start = Stopwatch.GetTimestamp()

    for i = 0 to 9999 do
        iteration i

    let elapsed = Stopwatch.GetElapsedTime start
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before

    printfn
        "measured here: layout + framebuffer paint + diff + UTF8 encoding + null output + commit: 10000 changed frames; %d managed bytes; %.3f ms"
        allocated
        elapsed.TotalMilliseconds

    View.dispose mounted
    equal 0L allocated "steady-state rendering must allocate no managed memory"

let reactiveAllocationTest () =
    use graph = new Graph()
    let mounted = View.mount graph (fun () -> column [] { label "hello"; label "other" })
    let renderer = Renderer.create 80 24 300000 capabilities
    let a, b = TextRun.prepare "hello", TextRun.prepare "world"
    let first = mounted.Root.Children[0]
    let iteration index =
        Node.setPreparedText (if index &&& 1 = 0 then a else b) first
        if index % 3 = 0 then Node.invalidate first
        Layout.presentReactive sink mounted renderer |> ignore
    let warmup = Stopwatch.StartNew()
    let mutable index = 0
    while warmup.Elapsed < TimeSpan.FromSeconds 1. do
        iteration index
        index <- index + 1
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i = 0 to 9999 do iteration i
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    printfn "measured here: reactive prepared-text updates + mixed full/partial rendering + null output: 10000 changing frames; %d managed bytes" allocated
    View.dispose mounted
    equal 0L allocated "tracked steady-state rendering must allocate no managed memory"

[<EntryPoint>]
let main args =
    if args.Length = 2 && args[0] = "--native" then
        NativeTests.run args[1]
    else
        let mutable failures = 0

        for name, body in tests do
            try
                body ()
                printfn "PASS %s" name
            with ex ->
                failures <- failures + 1
                eprintfn "FAIL %s\n%O" name ex

        try
            allocationTest ()
            reactiveAllocationTest ()
            printfn "PASS steady-state allocation gate"
        with ex ->
            failures <- failures + 1
            eprintfn "FAIL allocation gate\n%O" ex

        printfn "measured here: executable regression suite: %d tests, %d failed" (tests.Count + 1) failures
        if failures = 0 then 0 else 1
