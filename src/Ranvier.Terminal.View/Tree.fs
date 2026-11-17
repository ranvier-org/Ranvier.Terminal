namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

[<NoEquality; NoComparison>]
type Node =
    { mutable Id: string
      mutable Classes: string array
      mutable Style: Style
      mutable Computed: Style
      mutable Text: TextRun
      mutable Children: Node array
      mutable Bounds: Rect
      mutable Clip: Rect
      mutable DesiredWidth: int
      mutable DesiredHeight: int
      mutable ScrollX: int
      mutable ScrollY: int
      mutable MaxScrollX: int
      mutable MaxScrollY: int
      mutable CaretColumn: int
      mutable Parent: Node option
      mutable Focusable: bool
      mutable Focused: bool
      mutable Hovered: bool
      mutable OnEvent: InputEvent -> bool }

module Node =
    let private ignoreEvent (_: InputEvent) = false

    let create declarations children =
        let style = Style.create declarations

        { Id = ""
          Classes = [||]
          Style = style
          Computed = style
          Text = TextRun.empty
          Children = children
          Bounds = { X = 0; Y = 0; Width = 0; Height = 0 }
          Clip = { X = 0; Y = 0; Width = 0; Height = 0 }
          DesiredWidth = 0
          DesiredHeight = 0
          ScrollX = 0
          ScrollY = 0
          MaxScrollX = 0
          MaxScrollY = 0
          CaretColumn = -1
          Parent = None
          Focusable = false
          Focused = false
          Hovered = false
          OnEvent = ignoreEvent }

    let setText text node = node.Text <- TextRun.prepare text
    let setStyle declarations node = node.Style <- Style.create declarations

type Mounted =
    { Graph: Graph
      Owner: Owner
      Root: Node
      mutable Rules: Rule array
      mutable Focused: Node option
      mutable Disposed: bool }

module View =
    let mount (graph: Graph) build =
        graph.Run(fun () ->
            createRoot (fun owner ->
                try
                    { Graph = graph
                      Owner = owner
                      Root = build ()
                      Rules = [||]
                      Focused = None
                      Disposed = false }
                with _ ->
                    owner.Dispose()
                    reraise ()))

    let dispose mounted =
        if not mounted.Disposed then
            mounted.Disposed <- true
            mounted.Owner.Dispose()

    let focus node mounted =
        match mounted.Focused with
        | Some previous -> previous.Focused <- false
        | None -> ()

        mounted.Focused <- node

        match node with
        | Some current -> current.Focused <- true
        | None -> ()

    let rec private countFocusable node =
        if not node.Computed.Visible then
            0
        else
            let mutable count = if node.Focusable then 1 else 0

            for i = 0 to node.Children.Length - 1 do
                count <- count + countFocusable node.Children[i]

            count

    let rec private findAt target (index: byref<int>) node =
        let mutable found = None

        if node.Computed.Visible then
            if node.Focusable then
                if index = target then
                    found <- Some node

                index <- index + 1

            let mutable i = 0

            while i < node.Children.Length && found.IsNone do
                found <- findAt target &index node.Children[i]
                i <- i + 1

        found

    let rec private focusIndex (index: byref<int>) node =
        let mutable found = -1

        if node.Computed.Visible then
            if node.Focusable then
                if node.Focused then
                    found <- index

                index <- index + 1

            let mutable i = 0

            while i < node.Children.Length && found < 0 do
                found <- focusIndex &index node.Children[i]
                i <- i + 1

        found

    let focusNext backwards mounted =
        let count = countFocusable mounted.Root

        if count > 0 then
            let mutable index = 0
            let current = focusIndex &index mounted.Root

            let next =
                if backwards then
                    (if current < 0 then
                         count - 1
                     else
                         (current + count - 1) % count)
                else
                    (current + 1) % count

            index <- 0
            focus (findAt next &index mounted.Root) mounted

    let rec private hit x y node =
        let mutable found = None

        if node.Computed.Visible && Rect.contains x y node.Clip then
            let mutable i = node.Children.Length - 1

            while i >= 0 && found.IsNone do
                found <- hit x y node.Children[i]
                i <- i - 1

            if found.IsNone && Rect.contains x y node.Bounds then
                found <- Some node

        found

    let rec private hover x y node =
        node.Hovered <-
            node.Computed.Visible
            && Rect.contains x y node.Clip
            && Rect.contains x y node.Bounds

        for i = 0 to node.Children.Length - 1 do
            hover x y node.Children[i]

    let rec private attached target node =
        if not node.Computed.Visible then
            false
        elif obj.ReferenceEquals(target, node) then
            true
        else
            let mutable result = false
            let mutable i = 0

            while not result && i < node.Children.Length do
                result <- attached target node.Children[i]
                i <- i + 1

            result

    let rec private bubble event node =
        if node.OnEvent event then
            true
        else
            match event with
            | Mouse mouse when mouse.Action = MouseAction.Scroll && node.MaxScrollY > 0 ->
                let previous = node.ScrollY
                node.ScrollY <- max 0 (min node.MaxScrollY (node.ScrollY + mouse.Delta))

                if previous <> node.ScrollY then
                    true
                else
                    match node.Parent with
                    | Some parent -> bubble event parent
                    | None -> false
            | _ ->
                match node.Parent with
                | Some parent -> bubble event parent
                | None -> false

    let dispatch event mounted =
        if mounted.Disposed then
            invalidOp "The view has been disposed."

        mounted.Graph.Run(fun () ->
            match mounted.Focused with
            | Some node when not (attached node mounted.Root) -> focus None mounted
            | _ -> ()

            match event with
            | Key key when key.Key = Key.Tab && not key.Paste && not key.Released ->
                focusNext (key.Modifiers &&& Modifiers.Shift <> Modifiers.None) mounted
                true
            | Mouse mouse ->
                hover mouse.X mouse.Y mounted.Root

                match hit mouse.X mouse.Y mounted.Root with
                | Some node ->
                    if node.Focusable && mouse.Action = MouseAction.Press then
                        focus (Some node) mounted

                    bubble event node
                | None -> false
            | _ ->
                match mounted.Focused with
                | Some node -> bubble event node
                | None -> mounted.Root.OnEvent event)
