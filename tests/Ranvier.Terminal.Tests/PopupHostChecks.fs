module PopupHostChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    use graph = new Graph()
    let equal expected actual name =
        if actual <> expected then failwithf "%s: expected %A, got %A" name expected actual
    let rect x y width height = { X = x; Y = y; Width = width; Height = height }
    let anchorStyle x y =
        [ Width (Cells 4); Height (Cells 1); Margin { Left = x; Right = 0; Top = y; Bottom = 0 } ]
    let anchor = Node.create (anchorStyle 7 4) [||] |> Dsl.focusable
    Node.setText "abcdef" anchor
    let page = Node.create [ Direction Overlay ] [|anchor|]
    let mutable pagePresses = 0
    page.OnEvent <- fun _ -> pagePresses <- pagePresses + 1; true
    let mutable dismissed = 0
    let tip = Node.create [ Width (Cells 5); Height (Cells 2) ] [|Dsl.label "tip"|]
              |> Popup.atNode anchor |> Popup.passThrough
    let menu = Node.create [ Width (Cells 3); Height (Cells 1); Visible false ] [||]
               |> Popup.atCaret anchor |> Popup.onOutsidePress (fun () -> dismissed <- dismissed + 1)
    let mounted = View.mount graph (fun () -> Popup.host page [|tip; menu|])
    try
        let mutable frame = Frame.create 12 6
        let draw () = Layout.renderReactive mounted frame |> ignore
        let press x y =
            View.dispatch (Mouse { X=x; Y=y; Action=MouseAction.Press; Button=0; Delta=0; Modifiers=Modifiers.None }) mounted
        draw ()
        equal (rect 7 2 5 2) tip.Bounds "bottom edge flips above anchor"
        equal true (press 8 2) "nested tooltip content lets press through"
        equal 1 pagePresses "underlying page received press"
        equal false tip.Hovered "decorative popup does not hover"
        Node.setStyle (anchorStyle 9 1) anchor
        draw ()
        equal (rect 7 2 5 2) tip.Bounds "right edge clamps popup"
        Node.setStyle (anchorStyle 2 0) anchor
        draw ()
        equal (rect 2 1 5 2) tip.Bounds "reactive anchor relocation"
        equal " " frame.Cells[2 * frame.Width + 7].Glyph "old popup cleared"
        Node.setStyle [ Width (Cells 3); Height (Cells 1) ] menu
        View.focus (Some anchor) mounted
        Node.setCaret 1 anchor
        draw ()
        equal (rect 3 1 3 1) menu.Bounds "caret anchor"
        Node.setCaret 5 anchor
        draw ()
        equal (rect 5 1 3 1) menu.Bounds "scrolled caret anchor"
        menu.OnEvent <- fun _ -> true
        equal true (press 5 1) "topmost popup handles press"
        equal 0 dismissed "inside press keeps popup open"
        press 2 0 |> ignore
        equal 0 dismissed "anchor press keeps popup open"
        press 0 3 |> ignore
        equal 1 dismissed "outside press"
        frame <- Frame.create 4 2
        draw ()
        equal (rect 0 0 4 2) tip.Bounds "oversized popup fits resized viewport"
        Node.setChildren [||] page
        draw ()
        equal 0 tip.Clip.Height "detached anchor hides popup"
    finally
        View.dispose mounted
