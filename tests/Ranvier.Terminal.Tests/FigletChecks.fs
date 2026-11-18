module FigletChecks

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    let equal expected actual name =
        if actual <> expected then failwithf "%s: expected %A, got %A" name expected actual

    let fontText options left right =
        let rows =
            Array.init 95 (fun i ->
                let art =
                    match char (i + 32) with
                    | 'A' -> left
                    | 'B' -> right
                    | _ -> "?"
                art + "@@")
        String.concat "\r\n" (Array.concat [ [| "flf2a$ 1 1 20 " + options; "fixture" |]; rows ])
    let render mode left right =
        let font = Figlet.parse (fontText ("-1 1 0 " + string mode) left right)
        Figlet.render font 30 "AB"
    equal "a  b" (render 0 "a " " b") "full width"
    equal "ab" (render 64 "a " " b") "fitting"
    equal "aXb" (render 129 "aX" "Xb") "equal smushing"
    equal "a]b" (render 130 "a_" "]b") "underscore smushing"
    equal "a{b" (render 132 "a|" "{b") "hierarchy smushing"
    equal "a|b" (render 136 "a(" ")b") "opposite pair"
    equal "aYb" (render 144 "a\\" "/b") "big X"
    equal "a b" (render 160 "a$" "$b") "hardblank smushing"
    equal "a  b" (render 129 "a$" "$b") "protected hardblank"
    equal "ayb" (render 128 "ax" "yb") "universal smushing"
    equal "aXb" (Figlet.render (Figlet.parse (fontText "1 1" "aX" "Xb")) 30 "AB") "legacy layout"
    let rtl = Figlet.parse (fontText "-1 1 1 128" "Xa" "bY")
    equal "bYa" (Figlet.render rtl 30 "AB") "right-to-left universal smushing"
    let fullRtl = Figlet.parse (fontText "-1 1 1 0" "a$" "b!")
    equal "b!a " (Figlet.render fullRtl 4 "AB") "full-width RTL hardblank at exact width"
    equal "AB\n\nb!" (Figlet.render fullRtl 2 "AB\r\n\u2028B") "independent multiline fallback"
    let tagged = fontText "-1 1" "A" "B" + String.replicate 7 "\n@@" + "\n0x2605 BLACK STAR\n*@@"
    equal "*" (Figlet.render (Figlet.parse tagged) 30 "★") "hex code tag"
    let malformed =
        try Figlet.parse "bad header" |> ignore; false
        with :? FormatException -> true
    equal true malformed "bad font rejected"
    equal "AB" (Figlet.render Figlet.compact 2 "AB") "plain fallback"
    equal "" (Figlet.render Figlet.compact 1 "界") "fallback respects a wide glyph"
    equal "A\nB" (Figlet.render Figlet.compact 1 "A\nB") "explicit newlines"
    use graph = new Graph()
    let width, title = graph.Run(fun () -> createSignal 12, createSignal "AB")
    let view = View.mount graph (fun () ->
        Figlet.banner [ Css.color (Rgb 7) ] Figlet.compact (fun () -> width.Value) (fun () -> title.Value))
    try
        let frame = Frame.create 12 5
        Layout.renderReactive view frame |> ignore
        equal "█" frame.Cells[1].Glyph "bundled banner painted"
        equal (Rgb 7) frame.Cells[1].Paint.Foreground "banner style"
        graph.Run(fun () -> width.Value <- 1; title.Value <- "BA")
        Layout.renderReactive view frame |> ignore
        equal "B" frame.Cells[0].Glyph "retained fallback"
        equal " " frame.Cells[1].Glyph "previous banner cleared"
    finally View.dispose view
