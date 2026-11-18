namespace SystemMonitor

open System
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

module Kit =
    let mutable private revision=0
    let currentRevision () = Volatile.Read(&revision)
    let changed () = Interlocked.Increment(&revision) |> ignore
    let pages () = [| "System monitor", Screen.build |]
    let update (model: Model) =
        let size=struct(model.Width,model.Height)
        if model.Dirty || model.Size.Peek<>size then
            model.Dirty <- false
            model.Size.Value <- size
            if not (obj.ReferenceEquals(model.Sample,model.PublishedSample)) then
                model.PublishedSample <- model.Sample
                model.SampleRevision.Value <- model.SampleRevision.Peek+1
            model.DisplayRevision.Value <- model.DisplayRevision.Peek+1

    let build (model: Model) =
        let content=each (fun () -> [|struct(model.Revision.Value, model.Size.Value)|]) (fun _ -> (snd (pages()).[0]) model)
        Node.setStyle [Css.width(Css.flex 1);Css.height(Css.flex 1)] content
        let root=column [Css.width(Css.flex 1);Css.height(Css.flex 1);Css.backgroundColor Screen.background] { content }
        root.OnEvent <- fun event ->
            let handled=Model.handle event model
            if handled then update model
            handled
        root

type ReloadHandler =
    static member ClearCache(_: Type array) = ()
    static member UpdateApplication(_: Type array) = Kit.changed()
[<assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof<ReloadHandler>)>]
do ()
