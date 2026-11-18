namespace Utilities

open System
open System.Threading
open Ranvier
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

type Page = { Name: string; Build: Model -> Node }

module Kit =
    let mutable private revision = 0
    let currentRevision () = Volatile.Read(&revision)
    let changed () = Interlocked.Increment(&revision) |> ignore
    let pages () = [| { Name = "Terminal utilities"; Build = Pages.utilities } |]

    let build model =
        onCleanup (fun () -> Model.close model |> ignore)
        let content =
            each (fun () -> [| model.Revision.Value |]) (fun _ -> (pages ()).[0].Build model)
        Node.setStyle [ Css.height (Css.flex 1) ] content
        column [ Css.padding 1; Css.color (Css.rgb 215 230 245); Css.backgroundColor (Css.rgb 14 22 32) ] {
            "RANVIER / LIVE TERMINAL UTILITIES"
            content
            "Ctrl+Tab: leave process focus • Shift+PageUp/Down: history • Ctrl+C outside process: exit"
        }

type ReloadHandler =
    static member ClearCache(_: Type array) = ()
    static member UpdateApplication(_: Type array) = Kit.changed ()

[<assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof<ReloadHandler>)>]
do ()
