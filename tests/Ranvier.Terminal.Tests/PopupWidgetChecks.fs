module PopupWidgetChecks

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    let equal expected actual message =
        if expected <> actual then failwithf "%s: expected %A, got %A" message expected actual
    use graph = new Graph()
    let value = graph.Run(fun () -> createSignal "")
    let mutable pressed = false
    let line = [ Height(Cells 1) ]
    let candidates = [| "alpha"; "alpine"; "alto" |]
    let suggest (query: string) =
        if query = "" then [||]
        else candidates |> Array.filter (fun item -> item.StartsWith(query, StringComparison.Ordinal))
    let mounted = View.mount graph (fun () ->
        let hintButton = Widgets.button line "Help" ignore
        let beneath = Widgets.button line "Action" (fun () -> pressed <- true)
        let editor, menu = PopupWidgets.completion line [] value suggest 2
        let hint = PopupWidgets.tooltip [] "Hint" hintButton
        Popup.host (Node.create [] [| hintButton; beneath; editor |]) [| hint; menu |])
    let content, hint, menu = mounted.Root.Children[0], mounted.Root.Children[1], mounted.Root.Children[2]
    let hintButton, beneath, editor = content.Children[0], content.Children[1], content.Children[2]
    let frame = Frame.create 32 12
    let draw () = Layout.renderReactive mounted frame |> ignore
    let send event =
        let handled = View.dispatch event mounted
        draw ()
        handled
    let point action x y =
        send (Mouse { X = x; Y = y; Button = 0; Delta = 0; Action = action; Modifiers = Modifiers.None })
    let key code scalar =
        send (Key { Key = code; Scalar = scalar; Modifiers = Modifiers.None; Released = false; Paste = false })
    let typeChar c = equal true (key Key.Character (int c)) "typing handled"
    let editorFocused () = mounted.Focused |> Option.exists (fun node -> obj.ReferenceEquals(node, editor))
    try
        draw ()
        point MouseAction.Move hintButton.Bounds.X hintButton.Bounds.Y |> ignore
        equal true hint.Computed.Visible "hover opens tooltip"
        equal "H" frame.Cells[hint.Bounds.Y * frame.Width + hint.Bounds.X].Glyph "tooltip painted above action"
        point MouseAction.Move 30 10 |> ignore
        equal false hint.Computed.Visible "pointer leave dismisses tooltip"
        key Key.Tab 0 |> ignore
        equal false hint.Computed.Visible "keyboard focus alone leaves tooltip hidden"
        let alt released =
            send (Key {Key=Key.Alt; Scalar=0; Modifiers=Modifiers.None; Released=released; Paste=false}) |> ignore
        alt false
        equal true hint.Computed.Visible "holding Alt reveals focused tooltip"
        alt true
        equal false hint.Computed.Visible "releasing Alt hides focused tooltip"
        alt false
        send (Focus false) |> ignore
        equal false hint.Computed.Visible "leaving the terminal clears held Alt"
        send (Focus true) |> ignore
        alt false
        equal beneath.Bounds.Y hint.Bounds.Y "test action under tooltip"
        point MouseAction.Press hint.Bounds.X hint.Bounds.Y |> ignore
        equal true pressed "tooltip passes click to the underlying action"
        equal false hint.Computed.Visible "focus transfer dismisses tooltip"
        key Key.Tab 0 |> ignore
        equal true (editorFocused ()) "Tab enters editor"
        typeChar 'a'
        typeChar 'l'
        equal "al" value.Peek "editor receives ordinary typing with menu open"
        equal true menu.Computed.Visible "prefix opens suggestions"
        equal 2 menu.Children.Length "bounded suggestion rows"
        key Key.Down 0 |> ignore
        key Key.Down 0 |> ignore
        equal "alpine" menu.Children[0].Text.Source "selection scrolls beyond visible rows"
        key Key.Up 0 |> ignore
        key Key.Enter 0 |> ignore
        equal "alpine" value.Peek "Enter accepts highlighted suggestion"
        equal false menu.Computed.Visible "accept dismisses menu"
        equal true (editorFocused ()) "accept keeps editor focus"
        typeChar '!'
        equal "alpine!" value.Peek "accepted text leaves caret at end"
        graph.Run(fun () -> value.Value <- "a")
        draw ()
        key Key.Escape 0 |> ignore
        equal false menu.Computed.Visible "Escape dismisses"
        equal "a" value.Peek "Escape preserves text"
        key Key.Down 0 |> ignore
        let second = menu.Children[1].Bounds
        equal true (point MouseAction.Press second.X second.Y) "suggestion click handled"
        equal "alpine" value.Peek "click accepts the pointed suggestion"
        equal true (editorFocused ()) "row click keeps editor focused"
        graph.Run(fun () -> value.Value <- "a")
        draw ()
        point MouseAction.Press 30 10 |> ignore
        equal false menu.Computed.Visible "outside press dismisses"
        key Key.Down 0 |> ignore
        equal true menu.Computed.Visible "Down reopens suggestions"
        key Key.Tab 0 |> ignore
        equal false menu.Computed.Visible "focus leaving editor dismisses"
    finally
        View.dispose mounted

    let progress = graph.Run(fun () -> createSignal 0)
    let mutable invoked = ""
    let command caption enabled : Commands.Command =
        { Key = Key.Enter; Scalar = 0; Modifiers = Modifiers.None; Shortcut = ""
          Caption = caption; Enabled = enabled; Invoke = fun () -> invoked <- caption }
    let palette = View.mount graph (fun () ->
        let commands =
            [| command "Build again" (fun () -> progress.Value < 2)
               command "Build report" (fun () -> true)
               command "Build cancellation" (fun () -> progress.Value < 3) |]
        let input, results = Commands.palette line [] 2 commands
        Popup.host input [| results |])
    let sendKey code scalar =
        View.dispatch (Key { Key = code; Scalar = scalar; Modifiers = Modifiers.None; Released = false; Paste = false }) palette |> ignore
        Layout.renderReactive palette frame |> ignore
    let tick n =
        graph.Run(fun () -> progress.Value <- n)
        Layout.renderReactive palette frame |> ignore
    try
        Layout.renderReactive palette frame |> ignore
        sendKey Key.Tab 0
        sendKey Key.Character (int 'b')
        sendKey Key.Down 0
        sendKey Key.Down 0
        let results = palette.Root.Children[1]
        equal " Build report" results.Children[0].Text.Source "palette scrolled to third command"
        tick 1
        equal " Build report" results.Children[0].Text.Source "task progress preserves palette viewport"
        tick 2
        sendKey Key.Enter 0
        equal "Build cancellation" invoked "availability changes preserve highlighted command identity"
        sendKey Key.Down 0
        sendKey Key.Down 0
        tick 3
        sendKey Key.Enter 0
        equal "Build report" invoked "removed command falls back to an enabled row"
    finally
        View.dispose palette
