module SelectChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    let equal expected actual name =
        if expected <> actual then failwithf "%s: expected %A, got %A" name expected actual
    use graph = new Graph()
    let data = [| 10, "Alpha"; 20, "Beta"; 30, "Gamma"; 40, "Delta" |]
    let options, selected, checks =
        graph.Run(fun () -> createSignal data, createSignal (Some 20), createSignal (set [10]))
    let mounted = View.mount graph (fun () ->
        let line = [ Height(Cells 1) ]
        let single, first = Select.single line [] "Choose" 2 options selected
        let multi, second = Select.multiple line [] "Check" 2 options checks
        Popup.host (Node.create [] [| single; multi |]) [| first; second |])
    let frame = Frame.create 36 12
    let draw () = Layout.renderReactive mounted frame |> ignore
    let send event =
        let handled = View.dispatch event mounted
        draw ()
        handled
    let key code scalar = send (Key { Key = code; Scalar = scalar; Modifiers = Modifiers.None; Released = false; Paste = false })
    let press x y = send (Mouse { X = x; Y = y; Button = 0; Delta = 0; Action = MouseAction.Press; Modifiers = Modifiers.None })
    let content, first, second = mounted.Root.Children[0], mounted.Root.Children[1], mounted.Root.Children[2]
    let single, multi = content.Children[0], content.Children[1]
    try
        draw ()
        equal "Beta" single.Text.Source "single caption follows key"
        key Key.Tab 0 |> ignore
        key Key.Enter 0 |> ignore
        equal true first.Computed.Visible "keyboard opens single"
        key Key.Down 0 |> ignore
        equal (Some 20) selected.Peek "navigation does not commit"
        graph.Run(fun () -> options.Value <- Array.rev data)
        draw ()
        equal true (key Key.Enter 0) "single commit handled"
        equal (Some 30) selected.Peek "reordered highlighted key committed"
        equal "Gamma" single.Text.Source "committed caption"
        equal false first.Computed.Visible "commit closes single"
        equal true single.Focused "single commit keeps anchor focus"
        press multi.Bounds.X multi.Bounds.Y |> ignore
        equal true second.Computed.Visible "pointer opens multiple"
        let list = second.Children[0]
        equal 2 list.Children.Length "only visible rows are retained"
        key Key.Home 0 |> ignore
        key Key.Character 32 |> ignore
        equal (set [10;40]) checks.Peek "Space toggles current key"
        key Key.End 0 |> ignore
        equal (set [10;40]) checks.Peek "moving cursor preserves checks"
        let beforeLast = list.Children[0].Bounds
        equal true (press beforeLast.X beforeLast.Y) "scrolled row press handled"
        equal (set [10;20;40]) checks.Peek "scrolled click toggles its key"
        equal true second.Computed.Visible "multiple stays open"
        equal true multi.Focused "row click retains anchor focus"
        key Key.Escape 0 |> ignore
        equal false second.Computed.Visible "Escape closes multiple"
        equal "Delta, Beta, Alpha" multi.Text.Source "checked labels in current option order"
        key Key.Enter 0 |> ignore
        press 30 10 |> ignore
        equal false second.Computed.Visible "outside press dismisses multiple"
        graph.Run(fun () -> options.Value <- [||])
        draw ()
        key Key.Down 0 |> ignore
        equal false second.Computed.Visible "empty options stay closed"
        equal (set [10;20;40]) checks.Peek "data removal leaves caller-owned checks intact"
    finally
        View.dispose mounted
