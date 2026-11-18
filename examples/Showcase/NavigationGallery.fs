namespace Showcase

open Ranvier
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl
open Theme

module NavigationGallery =
    let build () =
        let leaf key label = { Key = key; Label = label; Children = [||] }
        let files =
            [| { Key = "source"; Label = "src"
                 Children = [| leaf "layout" "Layout.fs"; leaf "input" "Input.fs" |] }
               { Key = "tests"; Label = "tests"
                 Children = [| leaf "navigation" "NavigationChecks.fs" |] } |]
        let selected = createSignal (Some "source")
        let tree = Navigation.tree [Css.height (Cells 4)] (createSignal files) (createSignal (set ["source"])) selected
        let summary = text (fun () -> "Selected item: " + (selected.Value |> Option.defaultValue "none"))
        let tabs =
            [| { Key = "files"; Label = "Files"; Content = tree }
               { Key = "details"; Label = "Details"; Content = summary } |]
        card "Workspace browser" [
            Navigation.collapsible [] "Project" (createSignal true)
                (Navigation.tabs [] (createSignal tabs) (createSignal (Some "files")))
            line "Use arrows in the tree or tab strip. Enter toggles a branch." ]
