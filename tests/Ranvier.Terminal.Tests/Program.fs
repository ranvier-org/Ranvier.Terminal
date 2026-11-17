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

let capture renderer =
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

    send "\u001b[1;5A界\u001b[<0;4;2M\u001b[200~x\u001b[A\u001b[201~"
    equal (key Key.Up 0 Modifiers.Control false) events[0] "modified arrow"
    equal (key Key.Character (int '界') Modifiers.None false) events[1] "fragmented UTF8"

    match events[2] with
    | Mouse m ->
        equal 3 m.X "mouse x"
        equal 1 m.Y "mouse y"
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
    let frame = Frame.create 4 5
    Layout.render mounted frame
    equal 3 wrapped.Bounds.Height "wrapped intrinsic height"
    View.focus (Some mounted.Root.Children[1]) mounted
    Layout.render mounted frame
    let input = mounted.Root.Children[1]
    equal 3 input.ScrollX "caret scrolled into view"
    equal Attributes.Reverse frame.Cells[15].Paint.Attributes "visible caret"

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
    let a, b = TextRun.prepare "Frame A", TextRun.prepare "Frame B"

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
            printfn "PASS steady-state allocation gate"
        with ex ->
            failures <- failures + 1
            eprintfn "FAIL allocation gate\n%O" ex

        printfn "measured here: executable regression suite: %d tests, %d failed" (tests.Count + 1) failures
        if failures = 0 then 0 else 1
