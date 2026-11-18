namespace Ranvier.Terminal.View

open Ranvier
open System.Runtime.CompilerServices
open Ranvier.Terminal
open Ranvier.Terminal.Css

module Dsl =
    let label value =
        let node = Node.create [] [||]
        Node.setText value node
        node

    let text read =
        let node = Node.create [] [||]
        createEffect (fun () -> Node.setText (read ()) node)
        node

    let signalText (source: Signal<string>) =
        let node = Node.create [] [||]
        bindSignal source node (fun target value -> Node.setText value target)
        node

    /// Overlay declarations on the component style. Later bindings take precedence.
    let bindStyle read node =
        Node.bindStyleLayer read node
        node

    let styled declarations node =
        Node.mergeStyle declarations node
        node

    let id value (node: Node) =
        node.Id <- value
        Node.invalidate node
        node

    let classes values node =
        node.Classes <- values
        Node.invalidate node
        node

    type private CallbackSlot<'input, 'result> =
        { Baseline: 'input -> 'result
          Invoke: 'input -> 'result
          Current: ('input -> 'result) ref
          Layers: ResizeArray<bool ref * ('input -> 'result)> }

    // Install one dispatcher per decorated slot, leaving the widget's own handler as the fallback.
    let private handlers get set compose =
        let slots = ConditionalWeakTable<Node, CallbackSlot<_, _>>()
        fun handler node ->
            let slot =
                slots.GetValue(node, fun node ->
                    let baseline = get node
                    let current = ref baseline
                    let invoke value = current.Value value
                    set node invoke
                    { Layers = ResizeArray(); Baseline = baseline; Current = current; Invoke = invoke })
            let active = ref true
            let refresh () =
                let mutable next = slot.Baseline
                for alive, added in slot.Layers do
                    next <- compose alive added next
                slot.Current.Value <- next
            slot.Layers.Add(active, handler)
            refresh ()
            match Graph.TryCurrent with
            | ValueNone -> ()
            | ValueSome _ ->
                onCleanup (fun () ->
                    active.Value <- false
                    slot.Layers.RemoveAt(slot.Layers.FindIndex(fun (key, _) -> obj.ReferenceEquals(key, active)))
                    refresh ()
                    if slot.Layers.Count = 0 then
                        if obj.ReferenceEquals(get node, slot.Invoke) then set node slot.Baseline
                        slots.Remove node |> ignore)

    let private input = handlers (fun node -> node.OnEvent) (fun node callback -> node.OnEvent <- callback)
                            (fun alive added next event -> (alive.Value && added event) || next event)
    let private notify (alive: bool ref) added next value =
        next value
        if alive.Value then added value
    let private hover = handlers (fun node -> node.OnHoverChanged) (fun node callback -> node.OnHoverChanged <- callback) notify
    let private focus = handlers (fun node -> node.OnFocusChanged) (fun node callback -> node.OnFocusChanged <- callback) notify
    let private alt = handlers (fun node -> node.OnAltHeld) (fun node callback -> node.OnAltHeld <- callback) notify

    /// Return true to consume input. Unhandled events continue to earlier handlers and the widget.
    let onEvent handler node =
        input handler node
        node

    let onHoverChanged handler node = hover handler node; node
    let onFocusChanged handler node = focus handler node; node
    let onAltHeld handler node = alt handler node; node

    let focusable node =
        node.Focusable <- true
        node

    type ElementBuilder(declarations: Declaration list) =
        member _.Yield(value: Node) = [ value ]
        member _.Yield(value: string) = [ label value ]
        member _.YieldFrom(values: Node seq) = Seq.toList values
        member _.Zero() = []
        member _.Combine(first, second) = first @ second
        member _.Delay(body: unit -> Node list) = body ()

        member _.For(values: seq<'a>, body: 'a -> Node list) =
            values |> Seq.collect body |> Seq.toList

        member _.Run(children: Node list) =
            Node.create declarations (List.toArray children)

    let panel declarations = ElementBuilder declarations

    let column declarations =
        ElementBuilder(Direction Column :: declarations)

    let row declarations =
        ElementBuilder(Direction Row :: declarations)

    /// Stack children in paint order. Margins inset each child independently.
    let overlay declarations =
        ElementBuilder(Direction Overlay :: declarations)

    let fragment nodes = Node.create [] nodes

    let show condition build =
        let node = Node.create [] [||]
        createEffectOn condition (fun visible -> Node.setChildren (if visible then [| build () |] else [||]) node)
        node

    let each read build =
        let node = Node.create [] [||]
        createEffect (fun () ->
            let items = read ()
            untrack (fun () -> Node.setChildren (Array.map build items) node))
        node

    /// The builder runs once per key. Bind to its accessor to observe replacements with the same key.
    let eachKeyed key read build =
        let node = Node.create [] [||]
        let rows = createProjectionOnce ProjectionOptions.CreateFirst key build (fun () -> read () :> seq<_>)
        createEffect (fun () -> Node.setChildren (Array.map rows.Get rows.Keys) node)
        node
