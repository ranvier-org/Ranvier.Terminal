namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

module Templates =
    type TemplateView<'state> = private TemplateView of ('state -> Node)
    type Part<'state,'target> =
        private
        | Part of ('state -> 'target -> Node -> unit)
        | Style of Declaration
        | ReadStyle of ('state -> Declaration)
    type Source<'state,'value> = Source of ('state -> 'value)

    let inline from (access: 'state -> ^source) =
        Source (fun state -> (^source : (member Value : 'value) (access state)))

    let render (TemplateView build) state = build state

    let private decorate parts state target node =
        let mutable dynamic = false
        let mutable hasStyles = false
        for part in parts do
            match part with
            | Part install -> install state target node
            | Style _ -> hasStyles <- true
            | ReadStyle _ -> hasStyles <- true; dynamic <- true
        if hasStyles then
            let declarations () =
                [ for part in parts do
                    match part with
                    | Style value -> yield value
                    | ReadStyle read -> yield read state
                    | Part _ -> () ]
            if dynamic then Node.bindStyleLayer declarations node
            else Node.mergeStyle (declarations ()) node
        node

    let private fixedPart setter value = Part (fun _ _ node -> setter value node)
    let private readPart setter read = Part (fun state _ node -> createEffect (fun () -> setter (read state) node))
    let private setId value node = Dsl.id value node |> ignore
    let private setClass (value: string) node =
        Dsl.classes (value.Split((null: char array), StringSplitOptions.RemoveEmptyEntries)) node |> ignore

    type Modifiers<'s,'target>() =
        static member id(value: string) : Part<'s,'target> = fixedPart setId value
        static member id(read: 's -> string) = readPart setId read
        static member id(Source read: Source<'s,string>) = readPart setId read
        static member className(value: string) : Part<'s,'target> = fixedPart setClass value
        static member className(read: 's -> string) = readPart setClass read
        static member className(Source read: Source<'s,string>) = readPart setClass read
        static member style(value: Declaration) : Part<'s,'target> = Style value
        static member style(read: 's -> Declaration) : Part<'s,'target> = ReadStyle read
        static member style(Source read: Source<'s,Declaration>) = Modifiers<'s,'target>.style read
        static member onEvent(handler: 's -> InputEvent -> bool) =
            Part (fun state _ node -> Dsl.onEvent (handler state) node |> ignore)
        static member onFocusChanged(handler: 's -> bool -> unit) =
            Part (fun state _ node -> Dsl.onFocusChanged (handler state) node |> ignore)
        static member onHoverChanged(handler: 's -> bool -> unit) =
            Part (fun state _ node -> Dsl.onHoverChanged (handler state) node |> ignore)

    type Element<'s,'target>(build: Node array -> Node * 'target, parts: Part<'s,'target> array) =
        inherit Modifiers<'s,'target>()
        member _.Yield(view: TemplateView<'s>) = [|view|]
        member _.Yield(value: string) : TemplateView<'s> array = [|TemplateView (fun _ -> Dsl.label value)|]
        member _.Yield(read: 's -> string) = [|TemplateView (fun state -> Dsl.text (fun () -> read state))|]
        member this.Yield(Source read: Source<'s,string>) = this.Yield read
        member _.YieldFrom(views: TemplateView<'s> seq) = Seq.toArray views
        member _.Zero() : TemplateView<'s> array = [||]
        member _.Combine(left, right) = Array.append left right
        member _.Delay(build) = build ()
        member _.For(items, build) = Seq.collect (fun item -> build item :> seq<_>) items |> Seq.toArray
        member _.Run(children: TemplateView<'s> array) =
            TemplateView (fun state ->
                let node, target = build (Array.map (fun child -> render child state) children)
                decorate parts state target node)

    let private container direction children = Node.create [Direction direction] children, ()

    type column<'s>([<ParamArray>] parts: Part<'s,unit> array) =
        inherit Element<'s,unit>(container Column, parts)
    type row<'s>([<ParamArray>] parts: Part<'s,unit> array) =
        inherit Element<'s,unit>(container Row, parts)
    type overlay<'s>([<ParamArray>] parts: Part<'s,unit> array) =
        inherit Element<'s,unit>(container Overlay, parts)

    // Use the same modifier syntax for a custom component or an existing retained widget.
    type widget<'s>(build: 's -> Node, [<ParamArray>] parts: Part<'s,unit> array) =
        inherit Modifiers<'s,unit>()
        member _.View = TemplateView (fun state -> build state |> decorate parts state ())

    type Element<'s,'target> with
        member this.Yield(control: widget<'s>) = this.Yield control.View

    type ClickTarget = private ClickTarget of (InputEvent -> unit) ref

    type button<'s>([<ParamArray>] parts: Part<'s,ClickTarget> array) =
        inherit Element<'s,ClickTarget>((fun children ->
            let action = ref ignore
            let node = Widgets.buttonWithEvent [] "" (fun event -> action.Value event)
            Node.setChildren children node
            node, ClickTarget action), parts)
        static member onClick(handler: 's -> InputEvent -> unit) =
            Part (fun state (ClickTarget action) _ ->
                let previous = action.Value
                action.Value <- fun event -> previous event; handler state event)

    type Edit = { Value: string; Event: InputEvent }
    type InputTarget = private { Value: Signal<string>; Replace: string -> unit }
    type InputProps<'s>() =
        member _.value(value: string) : Part<'s,InputTarget> =
            Part (fun _ target _ -> target.Replace value)
        member _.value(read: 's -> string) : Part<'s,InputTarget> =
            Part (fun state target _ ->
                createEffect (fun () ->
                    let value = read state
                    if target.Value.Peek <> value then target.Replace value))
        member this.value(Source read: Source<'s,string>) = this.value read

    type input<'s>([<ParamArray>] parts: Part<'s,InputTarget> array) =
        inherit Modifiers<'s,InputTarget>()
        static let properties = InputProps<'s>()
        static member props = properties
        static member onInput(handler: 's -> Edit -> unit) : Part<'s,InputTarget> =
            Part (fun state target node ->
                let previous = node.OnEvent
                node.OnEvent <- fun event ->
                    let before = target.Value.Peek
                    let handled = previous event
                    if target.Value.Peek <> before then handler state { Value = target.Value.Peek; Event = event }
                    handled)
        member _.View =
            TemplateView (fun state ->
                let value = createSignal ""
                let node, replace = Widgets.inputWithReplace [] value
                decorate parts state { Value = value; Replace = replace } node)

    type Element<'s,'target> with
        member this.Yield(control: input<'s>) = this.Yield control.View
