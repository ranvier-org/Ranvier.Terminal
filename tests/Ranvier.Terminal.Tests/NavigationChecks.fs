module NavigationChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let private check condition message = if not condition then failwith message
let private key code = Key { Key=code; Scalar=0; Modifiers=Modifiers.None; Paste=false; Released=false }
let private press x y = Mouse { X=x; Y=y; Action=MouseAction.Press; Button=0; Delta=0; Modifiers=Modifiers.None }

let run () =
    use graph = new Graph()
    graph.Run(fun () ->
        let leaf key = { Key=key; Label=key; Children=[||] }
        let branch = { Key="branch"; Label="Branch"; Children=[|leaf "leaf"|] }
        let original = { Key="root"; Label="Root"; Children=[|branch; leaf "sibling"|] }
        let source = createSignal [|original|]
        let openKeys = createSignal Set.empty
        let selected = createSignal None
        let sectionOpen, active = createSignal true, createSignal (Some "browse")
        let mutable explorer, section, tabset = Unchecked.defaultof<Node>, Unchecked.defaultof<Node>, Unchecked.defaultof<Node>
        let mutable tabData = Unchecked.defaultof<Signal<NavigationTab array>>
        let mounted = View.mount graph (fun () ->
            explorer <- Navigation.tree [ Height(Cells 3) ] source openKeys selected
            section <- Navigation.collapsible [] "Contents" sectionOpen explorer
            tabData <- createSignal [|{ Key="browse"; Label="Browse"; Content=section }
                                      { Key="info"; Label="Info"; Content=Dsl.label "details" }|]
            tabset <- Navigation.tabs [] tabData active
            tabset)
        try
            let frame = Frame.create 24 6
            let draw () = Layout.renderReactive mounted frame |> ignore
            let dispatch event = View.dispatch event mounted |> ignore; draw ()
            let strip = tabset.Children[0]
            draw ()
            check (selected.Peek = Some "root") "Initial tree selection"
            dispatch (press 0 2)
            check (openKeys.Peek.Contains "root" && mounted.Focused |> Option.exists (fun n -> obj.ReferenceEquals(n, explorer))) "Tree disclosure click and focus"
            dispatch (key Key.Down)
            dispatch (key Key.Right)
            dispatch (key Key.Right)
            check (selected.Peek = Some "leaf") "Right expands then visits first child"
            let retained = explorer.Children[2]
            openKeys.Value <- openKeys.Peek.Remove "root"
            draw ()
            check (selected.Peek = Some "root" && openKeys.Peek.Contains "branch") "Collapsed subtree restores nearest visible selection"
            openKeys.Value <- openKeys.Peek.Add "root"
            source.Value <- [|leaf "before"; original|]
            draw ()
            check (obj.ReferenceEquals(retained, explorer.Children[3])) "Keyed row survives collapse and reorder"
            dispatch (key Key.End)
            check (selected.Peek = Some "sibling" && explorer.ScrollY > 0) "Keyboard selection follows scroll"
            let info = strip.Children[1]
            dispatch (press info.Bounds.X info.Bounds.Y)
            check (active.Peek = Some "info" && obj.ReferenceEquals(tabset.Children[1], tabData.Peek[1].Content)) "Mouse tab activation"
            tabData.Value <- Array.rev tabData.Peek
            draw ()
            check (obj.ReferenceEquals(info, strip.Children[0]) && active.Peek = Some "info") "Reordered tabs preserve active key"
            dispatch (key Key.Left)
            check (active.Peek = Some "browse" && obj.ReferenceEquals(tabset.Children[1], section)) "Keyboard tab activation and retained content"
            dispatch (press 1 1)
            check (not sectionOpen.Peek && section.Children.Length = 1) "Mouse collapse"
            dispatch (key Key.Right)
            check (sectionOpen.Peek && obj.ReferenceEquals(explorer, section.Children[1])) "Keyboard expansion reuses content"
            source.Value <- [||]
            tabData.Value <- [||]
            draw ()
            check (active.Peek.IsNone && selected.Peek.IsNone && strip.Children.Length = 0) "Empty collections"
        finally View.dispose mounted)
