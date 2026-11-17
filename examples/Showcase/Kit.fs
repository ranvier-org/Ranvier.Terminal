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
             Build = Pages.input } |]

    let build model =
        let registry = pages ()

        let page =
            each (fun () -> [| model.Page.Value |]) (fun index -> registry[index].Build model)
            |> styled [ Css.height (Css.flex 1) ]

        let root =
            column [ Css.backgroundColor background; Css.color bright; Css.paddingXY 1 0 ] {
                row [ Css.height (Css.cells 1); Css.gap 3 ] {
                    title "RANVIER / TERMINAL"
                    line "interactive feature showcase"
                    signalText model.Clock
                }

                row [ Css.height (Css.flex 1); Css.gap 2 ] {
                    panel
                        [ Css.width (Css.cells 23)
                          Css.border Rounded
                          Css.paddingXY 1 0
                          Css.backgroundColor surface ] {
                        title "EXPLORE"

                        for index = 0 to registry.Length - 1 do
                            button
                                ("page-" + string index)
                                (sprintf " F%d  %s " (index + 1) registry[index].Name)
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

                        line ""
                        note "Tab / Shift+Tab\nMove focus"
                        note "Enter / Space\nActivate"
                        note "Mouse wheel\nScroll content"
                        line ""
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
            | Key key when not key.Released && not key.Paste && key.Key >= Key.F1 && key.Key <= Key.F8 ->
                model.Page.Value <- int key.Key - int Key.F1
                true
            | _ -> false

        root

type ReloadHandler =
    static member ClearCache(_: Type array) = ()
    static member UpdateApplication(_: Type array) = Kit.changed ()

[<assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof<ReloadHandler>)>]
do ()
