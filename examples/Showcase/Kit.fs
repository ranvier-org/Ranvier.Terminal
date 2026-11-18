namespace Showcase

open System
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl
open Theme

type Page = { Name: string; Build: Model -> Node }

module Kit =
    let mutable private revision = 0
    let currentRevision () = Volatile.Read(&revision)

    let changed () =
        Interlocked.Increment(&revision) |> ignore

    let pages () =
        [| { Name = "Overview"
             Build = Pages.overview }
           { Name = "Controls"
             Build = Pages.controls }
           { Name = "Layout"
             Build = Pages.layout }
           { Name = "Style gallery"
             Build = Pages.stylesPage }
           { Name = "Unicode"
             Build = Pages.unicode }
           { Name = "Lifetimes"
             Build = Pages.lifetimes }
           { Name = "Rendering"
             Build = Pages.rendering }
           { Name = "Input / scroll"
             Build = Pages.input }
           { Name = "Live output"
             Build = UtilityPages.streaming }
           { Name = "Subprocess"
             Build = UtilityPages.terminal }
           { Name = "Instruments"
             Build = Pages.instruments }
           { Name = "Tasks"
             Build = Pages.tasks }
           { Name = "Components"
             Build = Components.build }
           { Name = "Elmish"
             Build = ElmishPage.build } |]

    let build model =
        let registry = pages ()

        let page =
            each (fun () -> [| model.Page.Value |]) (fun index -> registry[index].Build model)
            |> styled
                (Css.height (Css.flex 1) ::
                 Css.tableScrollbar
                     { Paint.empty with Foreground = muted; Background = surface }
                     { Paint.empty with Foreground = accent; Background = surface })

        let root =
            column [ Css.backgroundColor background; Css.color bright; Css.paddingXY sidePadding 0 ] {
                row [ Css.height (Css.cells 1); Css.gap 3 ] {
                    title "RANVIER / TERMINAL"
                    line "interactive feature showcase"
                    signalText model.Clock
                }

                row [ Css.height (Css.flex 1); Css.gap pageGap ] {
                    panel
                        [ Css.width (Css.cells sidebarWidth)
                          Css.border Rounded
                          Css.paddingXY 1 0
                          Css.backgroundColor surface ] {
                        title "EXPLORE"

                        for index = 0 to registry.Length - 1 do
                            button
                                ("page-" + string index)
                                (if index < 12 then sprintf " F%d  %s " (index + 1) registry[index].Name
                                 else sprintf "      %s " registry[index].Name)
                                (fun () -> model.Page.Value <- index)
                            |> bindStyle (fun () ->
                                [ Css.height (Css.cells 1)
                                  Css.color (if model.Page.Value = index then accent else muted)
                                  Css.attributes (
                                      if model.Page.Value = index then
                                          Attributes.Bold
                                      else
                                          Attributes.None
                                  )
                                  Css.backgroundColor (
                                      if model.Page.Value = index then
                                          Css.rgb 31 47 63
                                      else
                                          surface
                                  ) ])

                        line "Tab       Next item" |> styled [ Css.color muted ]
                        line "Shift+Tab Previous" |> styled [ Css.color muted ]
                        line "Enter     Activate" |> styled [ Css.color muted ]
                        line "Wheel     Scroll" |> styled [ Css.color muted ]
                        button "exit" " Exit showcase " (fun () -> model.Exit <- true)
                    }

                    page |> styled [ Css.width (Css.flex 1) ]
                }

                row [ Css.height (Css.cells 1); Css.gap 3 ] {
                    line "F-keys: pages   Tab: focus   Ctrl+C: exit" |> styled [ Css.color muted ]
                    signalText model.Viewport |> styled [ Css.color muted ]
                }
            }

        root.OnEvent <-
            function
            | Key key when not key.Released && not key.Paste ->
                match key.LogicalKey with
                | Ranvier.Keyboard.FunctionKey number when number <= registry.Length ->
                    model.Page.Value <- number - 1
                    true
                | _ -> false
            | _ -> false

        root

type ReloadHandler =
    static member ClearCache(_: Type array) = ()
    static member UpdateApplication(_: Type array) = Kit.changed ()

[<assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof<ReloadHandler>)>]
do ()
