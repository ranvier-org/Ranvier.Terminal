// Pass -I:<Release WidgetBench output directory> to profile a specific build.
#r "Ranvier.dll"
#r "Ranvier.Terminal.Core.dll"
#r "Ranvier.Terminal.Css.dll"
#r "Ranvier.Terminal.View.dll"
open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let headings = [| "BUILD"; "READY" |]
let art = headings |> Array.map (Figlet.render Figlet.compact 64)
let prepared = art |> Array.map TextRun.prepare
let iterations = 4000
let measure name step =
    for i = 0 to 499 do step (i % 2)
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i = 0 to iterations - 1 do step (i % 2)
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    printfn "%s,%.1f" name (float allocated / float iterations)

printfn "stage,allocated_bytes_per_update"
measure "compose-art" (fun i -> GC.KeepAlive(Figlet.render Figlet.compact 64 headings[i]))
measure "prepare-existing-art" (fun i -> GC.KeepAlive(TextRun.prepare art[i]))
let graph = new Graph()
let node = Node.create [] [||]
let view = View.mount graph (fun () -> node)
let renderer = Renderer.create 64 12 65536 { Colors=TrueColor; SynchronizedOutput=false }
let sink = { Write=(fun _ _ _ -> ()); Flush=ignore }
measure "install-prepared-run-and-present" (fun i ->
    Node.setPreparedText prepared[i] node
    Layout.presentReactive sink view renderer |> ignore)
View.dispose view
(graph :> IDisposable).Dispose()
