namespace Ranvier.Terminal.View

open System
open System.Collections.Generic
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

type TreeItem =
    { Key: string
      Label: string
      Children: TreeItem array }

type NavigationTab =
    { Key: string
      Label: string
      Content: Node }

module Navigation =
    let private keyPress key =
        not key.Paste && not key.Released && key.Modifiers = Modifiers.None

    let private activation (key: KeyEvent) =
        key.Key = Key.Enter || (key.Key = Key.Character && key.Scalar = 32)

    let private rowStyle = [ Height(Cells 1) ]
    let private selectedStyle = [ Height(Cells 1); Css.attributes Attributes.Reverse ]

    let private children (nodes: Node array) (parent: Node) =
        if parent.Children.Length <> nodes.Length || not (Array.forall2 (fun a b -> obj.ReferenceEquals(a,b)) parent.Children nodes) then
            Node.setChildren nodes parent

    let private mark kind selected node =
        let name = if selected then kind + "-selected" else kind
        if node.Classes.Length = 0 || node.Classes[0] <> name then
            node.Classes <- if selected then [|name; kind|] else [|kind|]
            Node.setStyle (if selected then selectedStyle else rowStyle) node
            Node.invalidate node

    /// Retains the supplied content while collapsed. Focus the heading to toggle with Enter or Space.
    let collapsible declarations caption (expanded: Signal<bool>) content =
        let heading = Node.create rowStyle [||]
        heading.Classes <- [|"section-heading"|]
        heading.Focusable <- true
        let root = Node.create (Direction Column :: declarations) [||]
        let toggle () = expanded.Value <- not expanded.Peek
        heading.OnEvent <- function
            | Key key when keyPress key && activation key -> toggle (); true
            | Key key when keyPress key && (key.Key = Key.Left || key.Key = Key.Right) ->
                expanded.Value <- key.Key = Key.Right
                true
            | Mouse mouse when mouse.Action = MouseAction.Press && mouse.Button = 0 -> toggle (); true
            | _ -> false
        createEffect (fun () ->
            let shown = expanded.Value
            Node.setText ((if shown then "▾ " else "▸ ") + caption) heading
            children (if shown then [|heading; content|] else [|heading|]) root)
        root

    /// One focus stop for the tab strip. Left/Right wrap, and Home/End select the endpoints.
    let tabs declarations (items: Signal<NavigationTab array>) (selected: Signal<string option>) =
        let strip = Node.create [Direction Row; Height(Cells 1)] [||]
        strip.Classes <- [|"tab-strip"|]
        strip.Focusable <- true
        let root = Node.create (Direction Column :: declarations) [||]
        let headings = Dictionary<string, Node>(StringComparer.Ordinal)
        createEffect (fun () ->
            let tabs = items.Value
            let requested = selected.Value
            let keys = HashSet<string>(StringComparer.Ordinal)
            for tab in tabs do
                if isNull tab.Key || not (keys.Add tab.Key) then invalidArg "items" "Tab keys must be non-null and unique."
            for key in headings.Keys |> Seq.toArray do
                if not (keys.Contains key) then headings.Remove key |> ignore
            let active = tabs |> Array.tryFind (fun tab -> Some tab.Key = requested) |> Option.orElseWith (fun () -> Array.tryHead tabs)
            let actual = active |> Option.map _.Key
            let buttons = tabs |> Array.map (fun tab ->
                let node =
                    match headings.TryGetValue tab.Key with
                    | true, existing -> existing
                    | _ ->
                        let heading = Node.create rowStyle [||]
                        let key = tab.Key
                        heading.OnEvent <- function
                            | Mouse mouse when mouse.Action = MouseAction.Press && mouse.Button = 0 ->
                                selected.Value <- Some key
                                true
                            | _ -> false
                        headings.Add(key, heading)
                        heading
                Node.setText (" " + tab.Label + " ") node
                mark "tab" (actual = Some tab.Key) node
                node)
            children buttons strip
            let mutable left = 0
            for i = 0 to tabs.Length - 1 do
                let width = buttons[i].Text.Width
                if actual = Some tabs[i].Key then
                    let viewport = max 1 strip.Content.Width
                    Node.setScroll (Math.Clamp(strip.ScrollX, min left (max 0 (left + width - viewport)), left)) 0 strip
                left <- left + width
            children (match active with Some tab -> [|strip; tab.Content|] | None -> [|strip|]) root
            if requested <> actual then selected.Value <- actual)
        strip.OnEvent <- function
            | Key key when keyPress key && items.Peek.Length > 0 ->
                let tabs = items.Peek
                let i = tabs |> Array.tryFindIndex (fun tab -> selected.Peek = Some tab.Key) |> Option.defaultValue 0
                let next =
                    match key.Key with
                    | Key.Left -> (i + tabs.Length - 1) % tabs.Length
                    | Key.Right -> (i + 1) % tabs.Length
                    | Key.Home -> 0
                    | Key.End -> tabs.Length - 1
                    | _ -> -1
                if next < 0 then false
                else
                    selected.Value <- Some tabs[next].Key
                    true
            | _ -> false
        root

    /// Uses stable item keys for selection and expansion. Hidden descendants retain their expansion.
    let tree declarations (items: Signal<TreeItem array>) (expanded: Signal<Set<string>>) (selected: Signal<string option>) =
        let root = Node.create (Direction Column :: declarations) [||]
        root.Focusable <- true
        let rows = Dictionary<string, Node>(StringComparer.Ordinal)
        let parents = Dictionary<string, string option>(StringComparer.Ordinal)
        let visible = ResizeArray<TreeItem>()
        let indices = Dictionary<string, int>(StringComparer.Ordinal)
        let mutable highlighted = None
        let toggle key =
            let previous = expanded.Peek
            expanded.Value <- if previous.Contains key then previous.Remove key else previous.Add key
        let select (requested: string option) =
            let mutable actual = requested
            while actual.IsSome && not (indices.ContainsKey actual.Value) do
                match parents.TryGetValue actual.Value with
                | true, parent -> actual <- parent
                | _ -> actual <- None
            if actual.IsNone && visible.Count > 0 then actual <- Some visible[0].Key
            if highlighted <> actual then
                match highlighted with
                | Some key when rows.ContainsKey key -> mark "tree-row" false rows[key]
                | _ -> ()
            match actual with
            | Some key ->
                mark "tree-row" true rows[key]
                let i = indices[key]
                let viewport = max 1 root.Content.Height
                Node.setScroll root.ScrollX (Math.Clamp(root.ScrollY, max 0 (i - viewport + 1), i)) root
            | None -> ()
            highlighted <- actual
            if requested <> actual then selected.Value <- actual
        createEffect (fun () ->
            let items, openKeys = items.Value, expanded.Value
            parents.Clear()
            visible.Clear()
            indices.Clear()
            let rec visit parent depth shown (item: TreeItem) =
                if isNull item.Key || not (parents.TryAdd(item.Key, parent)) then invalidArg "items" "Tree keys must be non-null and unique."
                if shown then
                    let node =
                        match rows.TryGetValue item.Key with
                        | true, existing -> existing
                        | _ ->
                            let node = Node.create rowStyle [||]
                            mark "tree-row" false node
                            rows.Add(item.Key, node)
                            node
                    let branch = item.Children.Length > 0
                    let marker = if not branch then "  " elif openKeys.Contains item.Key then "▾ " else "▸ "
                    Node.setText (String(' ', depth * 2) + marker + item.Label) node
                    node.OnEvent <- function
                        | Mouse mouse when mouse.Action = MouseAction.Press && mouse.Button = 0 ->
                            batch (fun () ->
                                selected.Value <- Some item.Key
                                if branch && mouse.X = node.Content.X + depth * 2 - node.ScrollX then toggle item.Key)
                            true
                        | _ -> false
                    indices.Add(item.Key, visible.Count)
                    visible.Add item
                for child in item.Children do visit (Some item.Key) (depth + 1) (shown && openKeys.Contains item.Key) child
            for item in items do visit None 0 true item
            for key in rows.Keys |> Seq.toArray do
                if not (parents.ContainsKey key) then rows.Remove key |> ignore
            children (visible |> Seq.map (fun item -> rows[item.Key]) |> Seq.toArray) root
            select selected.Peek)
        createEffect (fun () -> select selected.Value)
        root.OnEvent <- function
            | Key key when keyPress key && visible.Count > 0 && selected.Peek.IsSome ->
                let index = indices[selected.Peek.Value]
                let item = visible[index]
                let move i =
                    selected.Value <- Some visible[Math.Clamp(i, 0, visible.Count - 1)].Key
                    true
                match key.Key with
                | Key.Up -> move (index - 1)
                | Key.Down -> move (index + 1)
                | Key.Home -> move 0
                | Key.End -> move (visible.Count - 1)
                | Key.Right when item.Children.Length > 0 ->
                    if expanded.Peek.Contains item.Key then move (index + 1)
                    else toggle item.Key; true
                | Key.Left ->
                    if item.Children.Length > 0 && expanded.Peek.Contains item.Key then toggle item.Key; true
                    else
                        match parents[item.Key] with Some parent -> selected.Value <- Some parent; true | None -> false
                | _ when activation key && item.Children.Length > 0 -> toggle item.Key; true
                | _ -> false
            | _ -> false
        root
