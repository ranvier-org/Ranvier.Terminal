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
      mutable InheritColors: bool
      mutable Text: TextRun
      mutable Children: Node array
      mutable Bounds: Rect
      mutable Clip: Rect
      mutable Content: Rect
      mutable VerticalScrollbar: bool
      mutable HorizontalScrollbar: bool
      mutable VirtualContentWidth: int
      mutable DesiredWidth: int
      mutable DesiredHeight: int
      mutable VisibleStart: int
      mutable VisibleEnd: int
      mutable ScrollX: int
      mutable ScrollY: int
      mutable MaxScrollX: int
      mutable MaxScrollY: int
      mutable VirtualScrollY: struct (int * int) voption
      mutable VirtualHeaderRows: int
      mutable OnScrollYRequested: (int -> unit) option
      mutable CaretColumn: int
      mutable Parent: Node option
      mutable Focusable: bool
      mutable Focused: bool
      mutable Hovered: bool
      mutable OnFocusChanged: bool -> unit
      mutable OnAltHeld: bool -> unit
      mutable OnHoverChanged: bool -> unit
      mutable PointerTransparent: bool
      mutable PopupHost: bool
      mutable PopupAnchor: PopupAnchor option
      mutable OnOutsidePress: (unit -> unit) option
      mutable OnEvent: InputEvent -> bool
      mutable PaintDirty: bool
      mutable SubtreeDirty: bool
      mutable LayoutDirty: bool
      mutable ForceLayout: bool }

and [<NoEquality; NoComparison>] PopupAnchor =
    | NodeAnchor of Node
    | CaretAnchor of Node

module Node =
    let private ignoreEvent (_: InputEvent) = false

    let create declarations children =
        let style = Style.create declarations

        { Id = ""
          Classes = [||]
          Style = style
          Computed = style
          InheritColors = true
          Text = TextRun.empty
          Children = children
          Bounds = { X = 0; Y = 0; Width = 0; Height = 0 }
          Clip = { X = 0; Y = 0; Width = 0; Height = 0 }
          Content = { X = 0; Y = 0; Width = 0; Height = 0 }
          VerticalScrollbar = false
          HorizontalScrollbar = false
          VirtualContentWidth = 0
          DesiredWidth = 0
          DesiredHeight = 0
          VisibleStart = 0
          VisibleEnd = children.Length - 1
          ScrollX = 0
          ScrollY = 0
          MaxScrollX = 0
          MaxScrollY = 0
          VirtualScrollY = ValueNone
          VirtualHeaderRows = 0
          OnScrollYRequested = None
          CaretColumn = -1
          Parent = None
          Focusable = false
          Focused = false
          Hovered = false
          OnFocusChanged = ignore
          OnAltHeld = ignore
          OnHoverChanged = ignore
          PointerTransparent = false
          PopupHost = false
          PopupAnchor = None
          OnOutsidePress = None
          OnEvent = ignoreEvent
          PaintDirty = true
          SubtreeDirty = true
          LayoutDirty = true
          ForceLayout = true }

    let rec private mark layout node =
        node.SubtreeDirty <- true
        if layout then node.LayoutDirty <- true
        match node.Parent with
        | Some parent -> mark layout parent
        | None -> ()

    let invalidatePaint node =
        node.PaintDirty <- true
        mark false node

    let setInheritColors value node =
        if node.InheritColors <> value then
            node.InheritColors <- value
            invalidatePaint node

    let invalidate node =
        node.ForceLayout <- true
        node.PaintDirty <- true
        mark true node

    let private invalidateLocal node =
        node.PaintDirty <- true
        mark true node

    let setPreparedText (text: TextRun) node =
        if not (obj.ReferenceEquals(text, node.Text)) then
            let layout = text.Width <> node.Text.Width || text.Height <> node.Text.Height || node.Computed.Wrap
            node.Text <- text
            if layout then invalidateLocal node else invalidatePaint node

    let setText text node =
        if not (String.Equals(text, node.Text.Source, StringComparison.Ordinal)) then
            setPreparedText (TextRun.prepare text) node

    let private applyStyle next node =
        if next <> node.Style then
            let layout = { next with Paint = Paint.empty } <> { node.Style with Paint = Paint.empty }
            node.Style <- next
            if layout then invalidateLocal node
            else
                node.Computed <- { node.Computed with Paint = next.Paint }
                invalidatePaint node

    type private StyleBindings =
        { mutable Basis: Style
          Layers: ResizeArray<Declaration list ref> }

    let private styleBindings = System.Runtime.CompilerServices.ConditionalWeakTable<Node, StyleBindings>()

    let private applyLayers bindings node =
        let mutable next = bindings.Basis
        for i = 0 to bindings.Layers.Count - 1 do
            next <- Style.merge bindings.Layers[i].Value next
        applyStyle next node

    let private setBaseStyle basis node =
        let mutable bindings = Unchecked.defaultof<StyleBindings>
        if styleBindings.TryGetValue(node, &bindings) then
            bindings.Basis <- basis
            applyLayers bindings node
        else applyStyle basis node

    let setStyle declarations node = setBaseStyle (Style.create declarations) node

    let mergeStyle declarations node =
        let mutable bindings = Unchecked.defaultof<StyleBindings>
        let basis = if styleBindings.TryGetValue(node, &bindings) then bindings.Basis else node.Style
        setBaseStyle (Style.merge declarations basis) node

    let internal bindStyleLayer read node =
        let bindings = styleBindings.GetValue(node, fun node -> { Basis = node.Style; Layers = ResizeArray() })
        let layers = bindings.Layers
        let layer = ref []
        layers.Add layer
        onCleanup (fun () ->
            layers.RemoveAt(layers.FindIndex(fun item -> obj.ReferenceEquals(item, layer)))
            if layers.Count = 0 then styleBindings.Remove node |> ignore
            applyLayers bindings node)
        createEffect (fun () ->
            layer.Value <- read ()
            applyLayers bindings node)

    let setChildren children node =
        for child in node.Children do
            if child.Parent.IsSome && obj.ReferenceEquals(child.Parent.Value, node) then child.Parent <- None
        node.Children <- children
        for child in children do child.Parent <- Some node
        invalidateLocal node

    let setScroll x y node =
        if node.ScrollX <> x || node.ScrollY <> y then
            node.ScrollX <- x
            node.ScrollY <- y
            invalidateLocal node

    /// Describe a vertically virtualized viewport for scrollbar painting.
    let setVirtualScrollWithHeader contentWidth headerRows offset maximum node =
        let maximum = max 0 maximum
        let headerRows = max 0 headerRows
        let range = ValueSome(struct (Math.Clamp(offset, 0, maximum), maximum))
        if node.VirtualScrollY <> range || node.VirtualContentWidth <> contentWidth || node.VirtualHeaderRows <> headerRows then
            node.VirtualContentWidth <- contentWidth
            node.VirtualHeaderRows <- headerRows
            node.VirtualScrollY <- range
            invalidateLocal node

    let setVirtualScrollY contentWidth offset maximum node =
        setVirtualScrollWithHeader contentWidth 0 offset maximum node

    let clearVirtualScrollY node =
        if node.VirtualScrollY.IsSome then
            node.VirtualScrollY <- ValueNone
            node.VirtualContentWidth <- 0
            node.VirtualHeaderRows <- 0
            invalidateLocal node

    let setCaret column node =
        if node.CaretColumn <> column then
            node.CaretColumn <- column
            invalidateLocal node

    let internal scrollbarGeometry vertical node =
        let content = node.Content
        let struct (offset, maximum) =
            if not vertical then struct (node.ScrollX, node.MaxScrollX)
            else
                match node.VirtualScrollY with
                | ValueSome range -> range
                | ValueNone -> struct (node.ScrollY, node.MaxScrollY)
        let shown = if vertical then node.VerticalScrollbar else node.HorizontalScrollbar
        if not shown || maximum <= 0 || node.Computed.ScrollbarVisibility = ScrollbarVisibility.Hidden then ValueNone
        else
            let header = min content.Height node.VirtualHeaderRows
            let bounds =
                if vertical then
                    { X=content.X+content.Width; Y=content.Y+header; Width=1; Height=content.Height-header }
                else
                    { X=content.X; Y=content.Y+content.Height; Width=content.Width; Height=1 }
            let length = if vertical then bounds.Height else bounds.Width
            let struct (start, count) = Scrollbar.thumbRange node.Computed.ScrollbarThumbSize length length maximum offset
            if count = 0 then ValueNone else ValueSome(struct (bounds, length, offset, maximum, start, count))

type Mounted =
    { Graph: Graph
      Owner: Owner
      Root: Node
      mutable Rules: Rule array
      mutable Focused: Node option
      mutable RevealFocus: bool
      mutable AltHeld: bool
      mutable ScrollbarDrag: struct (Node * bool * int * int) voption
      mutable Disposed: bool
      mutable LastFrame: Frame option
      mutable LastRules: Rule array
      mutable DamageRows: bool array
      mutable MeasuredNodes: int
      mutable Damage: Rect }

module View =
    /// Mount the node and return the companion value created in the same owner.
    let mountWithState (graph: Graph) setup =
        graph.Run(fun () ->
            createRoot (fun owner ->
                try
                    let root, state = setup ()
                    { Graph = graph
                      Owner = owner
                      Root = root
                      Rules = [||]
                      Focused = None
                      RevealFocus = false
                      AltHeld = false
                      ScrollbarDrag = ValueNone
                      Disposed = false
                      LastFrame = None
                      LastRules = [||]
                      DamageRows = [||]
                      MeasuredNodes = 0
                      Damage = { X = 0; Y = 0; Width = 0; Height = 0 } }, state
                with _ ->
                    owner.Dispose()
                    reraise ()))

    let mount graph build = mountWithState graph (fun () -> build (), ()) |> fst

    let dispose mounted =
        if not mounted.Disposed then
            mounted.Disposed <- true
            mounted.ScrollbarDrag <- ValueNone
            mounted.Owner.Dispose()

    let focus node mounted =
        let same =
            match mounted.Focused, node with
            | Some previous, Some current -> obj.ReferenceEquals(previous, current)
            | None, None -> true
            | _ -> false
        if not same then
            mounted.ScrollbarDrag <- ValueNone
            let previous = mounted.Focused
            mounted.Focused <- node
            mounted.RevealFocus <- node.IsSome
            match previous with
            | Some old ->
                old.Focused <- false
                old.OnAltHeld false
                Node.invalidate old
                old.OnFocusChanged false
            | None -> ()
            match node with
            | Some current ->
                current.Focused <- true
                current.OnAltHeld mounted.AltHeld
                Node.invalidate current
                current.OnFocusChanged true
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

        if not node.PointerTransparent && node.Computed.Visible && Rect.contains x y node.Clip then
            let mutable i = node.Children.Length - 1

            while i >= 0 && found.IsNone do
                found <- hit x y node.Children[i]
                i <- i - 1

            if found.IsNone && Rect.contains x y node.Bounds then
                found <- Some node

        found

    let rec private hover enabled x y node =
        let previous = node.Hovered
        let enabled = enabled && not node.PointerTransparent && node.Computed.Visible && Rect.contains x y node.Clip
        node.Hovered <-
            enabled && Rect.contains x y node.Bounds

        if previous <> node.Hovered then
            Node.invalidate node
            node.OnHoverChanged node.Hovered
        for i = 0 to node.Children.Length - 1 do
            hover enabled x y node.Children[i]

    let rec private within ancestor target =
        match target with
        | Some node -> obj.ReferenceEquals(ancestor, node) || within ancestor node.Parent
        | None -> false

    let rec private dismissOutside target node =
        if node.Computed.Visible then
            match node.PopupAnchor, node.OnOutsidePress with
            | Some (NodeAnchor anchor | CaretAnchor anchor), Some dismiss ->
                if not (within node target || within anchor target) then dismiss ()
            | _ -> ()
            for child in node.Children do dismissOutside target child

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

    let private revealOffset start size origin extent scroll maximum =
        let before = int64 start - int64 origin
        let after = before + int64 size - int64 extent
        let delta =
            if before < 0L && after < 0L then max before after
            elif before > 0L && after > 0L then min before after
            else 0L
        int (Math.Clamp(int64 scroll + delta, 0L, int64 maximum))

    let internal revealFocus mounted =
        if not mounted.RevealFocus then false
        else
            mounted.RevealFocus <- false
            match mounted.Focused with
            | Some target when attached target mounted.Root ->
                let mutable bounds = target.Bounds
                let mutable ancestor = target.Parent
                let mutable changed = false
                while ancestor.IsSome && bounds.Width > 0 && bounds.Height > 0 do
                    let node = ancestor.Value
                    let content = node.Content
                    let x = revealOffset bounds.X bounds.Width content.X content.Width node.ScrollX node.MaxScrollX
                    let y = revealOffset bounds.Y bounds.Height content.Y content.Height node.ScrollY node.MaxScrollY
                    // Pass only the portion exposed by this viewport to its parent.
                    let shifted =
                        { bounds with
                            X = bounds.X + (node.ScrollX - x)
                            Y = bounds.Y + (node.ScrollY - y) }
                    bounds <- Rect.intersect content shifted
                    if x <> node.ScrollX || y <> node.ScrollY then
                        Node.setScroll x y node
                        changed <- true
                    ancestor <- node.Parent
                changed
            | _ -> false

    let rec private bubble event node =
        if node.OnEvent event then
            true
        else
            match event with
            | Mouse mouse when mouse.Action = MouseAction.Scroll ->
                let horizontal = (mouse.Modifiers &&& Modifiers.Shift) <> Modifiers.None
                let previous = if horizontal then node.ScrollX else node.ScrollY
                let maximum = if horizontal then node.MaxScrollX else node.MaxScrollY
                let next = int (Math.Clamp(int64 previous + int64 mouse.Delta, 0L, int64 maximum))
                if previous <> next then
                    if horizontal then node.ScrollX <- next else node.ScrollY <- next
                    Node.invalidate node
                    true
                else
                    match node.Parent with
                    | Some parent -> bubble event parent
                    | None -> false
            | _ ->
                match node.Parent with
                | Some parent -> bubble event parent
                | None -> false

    let private requestScroll vertical value node =
        if vertical then
            match node.VirtualScrollY, node.OnScrollYRequested with
            | ValueSome _, Some update -> update value
            | ValueSome _, None -> ()
            | ValueNone, _ -> Node.setScroll node.ScrollX value node
        else Node.setScroll value node.ScrollY node

    let private pressScrollbar (mouse: MouseEvent) node mounted =
        let press vertical =
            match Node.scrollbarGeometry vertical node with
            | ValueSome(struct (bounds, length, offset, maximum, start, count))
                when Rect.contains mouse.X mouse.Y bounds &&
                     (not vertical || node.VirtualScrollY.IsNone || node.OnScrollYRequested.IsSome) ->
                let coordinate = if vertical then mouse.Y else mouse.X
                let index = coordinate - (if vertical then bounds.Y else bounds.X)
                let mutable thumbStart = start
                if index < start || index >= start + count then
                    let next = int (int64 index * int64 maximum / int64 (max 1 (length - 1)))
                    if next <> offset then requestScroll vertical next node
                    let struct (position, _) = Scrollbar.thumbRange node.Computed.ScrollbarThumbSize length length maximum next
                    thumbStart <- position
                mounted.ScrollbarDrag <- ValueSome(struct (node, vertical, Math.Clamp(index - thumbStart, 0, count - 1), coordinate))
                true
            | _ -> false
        press true || press false

    let private activeScrollbar vertical node mounted =
        match Node.scrollbarGeometry vertical node with
        | ValueSome(struct (bounds, _, _, _, _, _)) as geometry
            when attached node mounted.Root && not node.PointerTransparent &&
                 (not vertical || node.VirtualScrollY.IsNone || node.OnScrollYRequested.IsSome) ->
            let clip = Rect.intersect bounds node.Clip
            if clip.Width > 0 && clip.Height > 0 then geometry else ValueNone
        | _ -> ValueNone

    let internal validateScrollbarDrag mounted =
        match mounted.ScrollbarDrag with
        | ValueSome(struct (node, vertical, _, _)) when (activeScrollbar vertical node mounted).IsNone ->
            mounted.ScrollbarDrag <- ValueNone
        | _ -> ()

    let private dragScrollbar (mouse: MouseEvent) mounted =
        match mounted.ScrollbarDrag with
        | ValueSome(struct (node, vertical, grip, last)) ->
            match activeScrollbar vertical node mounted with
            | ValueSome(struct (bounds, length, offset, maximum, start, count)) ->
                let coordinate = if vertical then mouse.Y else mouse.X
                let origin = if vertical then bounds.Y else bounds.X
                let travel = length - count
                let grip = min grip (count - 1)
                let target = Math.Clamp(int64 coordinate - int64 origin - int64 grip, 0L, int64 travel)
                if coordinate <> last && travel > 0 && target <> int64 start then
                    let next = int ((target * int64 maximum + int64 travel - 1L) / int64 travel)
                    if next <> offset then requestScroll vertical next node
                mounted.ScrollbarDrag <-
                    if mouse.Action = MouseAction.Release then ValueNone
                    else ValueSome(struct (node, vertical, grip, coordinate))
                true
            | _ ->
                mounted.ScrollbarDrag <- ValueNone
                false
        | ValueNone -> false

    let dispatch event mounted =
        if mounted.Disposed then
            invalidOp "The view has been disposed."

        mounted.Graph.Run(fun () ->
            match mounted.Focused with
            | Some node when not (attached node mounted.Root) -> focus None mounted
            | _ -> ()

            match event with
            | Key key when key.LogicalKey = Ranvier.Keyboard.Modifier Ranvier.Keyboard.ModifierKey.Alt && not key.Paste ->
                mounted.AltHeld <- not key.Released
                mounted.Focused |> Option.iter (fun node -> node.OnAltHeld mounted.AltHeld)
                false
            | Focus false ->
                mounted.ScrollbarDrag <- ValueNone
                mounted.AltHeld <- false
                mounted.Focused |> Option.iter (fun node -> node.OnAltHeld false)
                hover false 0 0 mounted.Root
                match mounted.Focused with
                | Some node -> bubble event node
                | None -> mounted.Root.OnEvent event
            | Key key when key.LogicalKey = Ranvier.Keyboard.Whitespace Ranvier.Keyboard.Tab && not key.Paste && not key.Released ->
                let handled =
                    key.Modifiers &&& Modifiers.Control = Modifiers.None
                    && (match mounted.Focused with Some node -> bubble event node | None -> false)
                if not handled then focusNext (key.Modifiers &&& Modifiers.Shift <> Modifiers.None) mounted
                true
            | Mouse mouse when (mouse.Action = MouseAction.Move || (mouse.Action = MouseAction.Release && mouse.Button = 0))
                               && dragScrollbar mouse mounted -> true
            | Mouse mouse ->
                hover true mouse.X mouse.Y mounted.Root

                let target = hit mouse.X mouse.Y mounted.Root
                if mouse.Action = MouseAction.Press then
                    if mouse.Button = 0 then mounted.ScrollbarDrag <- ValueNone
                    dismissOutside target mounted.Root
                match target with
                | Some node ->
                    if mouse.Action = MouseAction.Press then
                        let mutable target = Some node
                        while target.IsSome && not target.Value.Focusable do target <- target.Value.Parent
                        if target.IsSome then focus target mounted

                    (mouse.Action = MouseAction.Press && mouse.Button = 0 && pressScrollbar mouse node mounted) || bubble event node
                | None -> false
            | _ ->
                match mounted.Focused with
                | Some node -> bubble event node
                | None -> mounted.Root.OnEvent event)
