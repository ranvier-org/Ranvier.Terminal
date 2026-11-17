namespace Ranvier.Terminal.View

open Ranvier
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

    let bindStyle read node =
        createEffect (fun () -> Node.setStyle (read ()) node)
        node

    let id value (node: Node) =
        node.Id <- value
        node

    let classes values node =
        node.Classes <- values
        node

    let onEvent handler node =
        node.OnEvent <- handler
        node

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

    let fragment nodes = Node.create [] nodes

    let show condition build =
        let node = Node.create [] [||]
        createEffect (fun () -> node.Children <- if condition () then [| build () |] else [||])
        node

    let each read build =
        let node = Node.create [] [||]
        createEffect (fun () -> node.Children <- read () |> Array.map build)
        node
