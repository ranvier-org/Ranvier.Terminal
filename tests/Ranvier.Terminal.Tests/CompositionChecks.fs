module CompositionChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private equal expected actual message =
    if expected <> actual then failwithf "%s: expected %A, got %A" message expected actual

let private key = Key { Key = Key.Enter; Scalar = 0; Modifiers = Modifiers.None; Released = false; Paste = false }

let private decorations (graph: Graph) =
    let mutable pressed = 0
    let button = Widgets.button [ Direction Row; Width(Cells 8) ] "Press" (fun () -> pressed <- pressed + 1)
    let calls = ResizeArray<string>()
    let owner build = graph.Run(fun () -> createRoot (fun owner -> owner, build ()))
    let changing = graph.Run(fun () -> createSignal true)
    let first, firstTip = owner (fun () ->
        button
        |> Dsl.bindStyle (fun () -> [ Foreground (Rgb 1); Gap 2 ])
        |> Dsl.onEvent (fun _ -> calls.Add "first"; false)
        |> PopupWidgets.tooltip [] "first")
    let second, secondTip = owner (fun () ->
        button
        |> Dsl.bindStyle (fun () -> if changing.Value then [ Foreground (Rgb 2) ] else [])
        |> Dsl.onEvent (fun _ -> calls.Add "second"; false)
        |> PopupWidgets.tooltip [] "second")
    try
        equal Row button.Style.Direction "bound color preserves row layout"
        equal (Rgb 2) button.Style.Paint.Foreground "later style wins"
        button.OnEvent key |> ignore
        equal ["second"; "first"] (Seq.toList calls) "unhandled listeners fall through in order"
        equal 1 pressed "widget handles Enter after listeners"
        let overrideOwner, _ = owner (fun () -> Dsl.onEvent (fun _ -> true) button)
        button.OnEvent key |> ignore
        equal 1 pressed "consuming listener stops widget activation"
        overrideOwner.Dispose()
        changing.Value <- false
        equal (Rgb 1) button.Style.Paint.Foreground "omitted property reveals older layer"
        changing.Value <- true
        Node.setStyle [ Width(Cells 12); Foreground (Rgb 3) ] button
        button |> Dsl.styled [ Border Double ] |> ignore
        equal (Rgb 2) button.Style.Paint.Foreground "new base respects active bindings"
        first.Dispose()
        equal 0 button.Style.Gap "removing first binding restores base gap"
        equal (Rgb 2) button.Style.Paint.Foreground "later binding survives out-of-order cleanup"
        button.Hovered <- true
        button.OnHoverChanged true
        equal false firstTip.Style.Visible "retired tooltip ignores hover"
        equal true secondTip.Style.Visible "live tooltip receives hover"
        calls.Clear()
        button.OnEvent key |> ignore
        equal ["second"] (Seq.toList calls) "retired listener was removed"
        second.Dispose()
        equal (Rgb 3) button.Style.Paint.Foreground "base paint restored"
        equal Double button.Style.Border "merged base property survives disposal"
        calls.Clear()
        button.OnEvent key |> ignore
        equal 0 calls.Count "all registrations released"
        equal 3 pressed "original widget handler restored"
    finally
        second.Dispose()
        first.Dispose()

let private structure (graph: Graph) =
    let items, unrelated, show = graph.Run(fun () ->
        createSignal [| 1, "one"; 2, "two" |], createSignal 0, createSignal true)
    let mutable branches, rebuilt = 0, 0
    let removed = ResizeArray<int>()
    let mounted = View.mount graph (fun () ->
        let conditional =
            Dsl.show (fun () -> show.Value) (fun () ->
                ignore unrelated.Value
                branches <- branches + 1
                Dsl.label "branch")
        let plain =
            Dsl.each (fun () -> items.Value) (fun (_, value) ->
                ignore unrelated.Value
                rebuilt <- rebuilt + 1
                Dsl.label value)
        let keyed =
            Dsl.eachKeyed fst (fun () -> items.Value) (fun current ->
                let k, _ = current ()
                ignore unrelated.Value
                onCleanup (fun () -> removed.Add k)
                Dsl.text (fun () -> snd (current ())) |> Dsl.focusable)
        Node.create [] [| conditional; plain; keyed |])
    try
        let keyed = mounted.Root.Children[2]
        let survivor = keyed.Children[1]
        View.focus (Some survivor) mounted
        unrelated.Value <- 1
        equal (1, 2) (branches, rebuilt) "construction dependencies are untracked"
        items.Value <- [| 2, "updated"; 3, "three"; 1, "one" |]
        equal true (obj.ReferenceEquals(survivor, keyed.Children[0])) "key survives insertion and reorder"
        equal "updated" survivor.Text.Source "surviving row reads new value"
        equal true survivor.Focused "keyed row retains focus"
        equal 0 removed.Count "survivors remain owned"
        items.Value <- [| 1, "one"; 3, "three" |]
        equal [2] (Seq.toList removed) "only missing key is cleaned up"
    finally View.dispose mounted
    equal [1;2;3] (removed |> Seq.sort |> Seq.toList) "remaining keyed rows disposed once"

module private TemplateCases =
    open Templates

    type Model = { Name: Signal<string>; Accent: Signal<Color> }

    let form =
        column (column.className "form", column.style (fun (s: Model) -> Css.color s.Accent.Value)) {
            "Name"
            row () { from (fun model -> model.Name) }
            input (input.props.value (from (fun model -> model.Name)),
                   input.onInput (fun model edit -> model.Name.Value <- edit.Value))
            button (button.onClick (fun model _ -> model.Name.Value <- "Reset")) { "Reset" }
        }

    let check graph =
        let model = { Name = createSignal "Ada"; Accent = createSignal (Rgb 2) }
        let view = View.mount graph (fun () -> render form model)
        try
            let node = view.Root
            let editor = node.Children[2]
            let label = node.Children[1].Children[0]
            equal "Ada" editor.Text.Source "controlled input initial value"
            editor.OnEvent (Key { Key = Key.Character; Scalar = int '!'; Modifiers = Modifiers.None; Released = false; Paste = false }) |> ignore
            equal "Ada!" label.Text.Source "user edit updates a source child"
            node.Children[3].OnEvent key |> ignore
            equal "Reset" editor.Text.Source "button updates mounted input"
            model.Accent.Value <- Rgb 3
            equal (Rgb 3) node.Style.Paint.Foreground "reactive modifier"
        finally View.dispose view

let run () =
    use graph = new Graph()
    decorations graph
    structure graph
    graph.Run(fun () -> TemplateCases.check graph)
