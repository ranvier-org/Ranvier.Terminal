module PerfBench

open System
open System.Diagnostics
open System.IO
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

let caps = { Colors = TrueColor; SynchronizedOutput = true }
let mutable bytes = 0L
let mutable compared = 0L
let mutable copied = 0L
let output = { Write = (fun _ _ n -> bytes <- bytes + int64 n); Flush = (fun () -> ()) }

let measure name iterations (step: int -> unit) =
    let warm = Stopwatch.StartNew()
    let mutable i = 0
    while warm.ElapsedMilliseconds < 400L do
        step i
        i <- i + 1
    let times = Array.zeroCreate<float> 7
    let mutable allocated, totalBytes, totalCompared, totalCopied = 0L, 0L, 0L, 0L
    for sample = 0 to times.Length - 1 do
        bytes <- 0L
        compared <- 0L
        copied <- 0L
        let before = GC.GetAllocatedBytesForCurrentThread()
        let start = Stopwatch.GetTimestamp()
        for index = 0 to iterations - 1 do step index
        times[sample] <- Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000. / float iterations
        allocated <- allocated + GC.GetAllocatedBytesForCurrentThread() - before
        totalBytes <- totalBytes + bytes
        totalCompared <- totalCompared + compared
        totalCopied <- totalCopied + copied
    Array.sortInPlace times
    sprintf "%s,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f" name times[3] times[6]
        (float allocated / float (iterations * times.Length))
        (float totalBytes / float (iterations * times.Length))
        (float totalCompared / float (iterations * times.Length))
        (float totalCopied / float (iterations * times.Length))

[<EntryPoint>]
let main args =
    let results = ResizeArray<string>()
    let selectedWorkload = args |> Array.tryPick (fun arg -> if arg.StartsWith "--only=" then Some(arg.Substring 7) else None)
    let record name iterations step =
        if selectedWorkload.IsNone || selectedWorkload.Value = name then results.Add(measure name iterations step)
    let reactive = args |> Array.contains "--reactive"
    let present mounted renderer =
        if reactive then Layout.presentReactive output mounted renderer |> ignore
        else
            Layout.render mounted renderer.Back
            Renderer.present output renderer
        compared <- compared + int64 renderer.ComparedCells
        copied <- copied + int64 renderer.CopiedCells
    results.Add "workload,median_us,max_sample_us,allocated_bytes_per_iteration,output_bytes_per_iteration,compared_cells_per_iteration,copied_cells_per_iteration"
    for scenario in [| "idle"; "sparse"; "dense"; "wrapped"; "distant"; "geometry"; "fragmented" |] do
        use graph = new Graph()
        let signals = graph.Run(fun () -> Array.init 40 (fun _ -> createSignal "Status ready"))
        let longA = String.replicate 600 "hello world 界 e\u0301 "
        let mounted = View.mount graph (fun () ->
            column [] {
                for index = 0 to signals.Length - 1 do
                    signalText signals[index] |> bindStyle (fun () -> [ Css.height (Css.cells 1) ])
            })
        let r = Renderer.create 160 50 1100000 caps
        if scenario = "wrapped" then
            Node.setStyle [ Css.wrap true; Css.height (Css.cells 10) ] mounted.Root.Children[0]
            Node.setText longA mounted.Root.Children[0]
        let step index =
            if scenario = "sparse" then signals[0].Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
            elif scenario = "dense" then
                for signal in signals do signal.Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
            elif scenario = "distant" then
                signals[0].Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
                signals[39].Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
            elif scenario = "geometry" then
                signals[0].Value <- if index &&& 1 = 0 then "Status ready" else "Short"
            elif scenario = "fragmented" then
                for n in 0 .. 2 .. 39 do
                    signals[n].Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
            elif scenario = "wrapped" then
                // Prepared long text is static while another row changes.
                signals[1].Value <- if index &&& 1 = 0 then "Status ready" else "Status busy!"
            present mounted r
        record ("pipeline-" + scenario) 500 step
        View.dispose mounted
    for scenario in [| "idle"; "sparse"; "dense"; "styles"; "gaps" |] do
        let r = Renderer.create 160 50 1100000 caps
        let step index =
            if scenario = "sparse" then Frame.put (Frame.bounds r.Back) 7 7 (if index &&& 1 = 0 then "A" else "B") 1 Paint.empty r.Back
            elif scenario = "gaps" then
                for y = 0 to 49 do
                    for x in 0 .. 3 .. 159 do
                        Frame.put (Frame.bounds r.Back) x y (if index &&& 1 = 0 then "A" else "B") 1 Paint.empty r.Back
            elif scenario <> "idle" then
                for y = 0 to 49 do
                    for x = 0 to 159 do
                        let paint = if scenario = "styles" then { Paint.empty with Foreground = Rgb (if (x + index) &&& 1 = 0 then 0xABCDEF else 0x123456) } else Paint.empty
                        Frame.put (Frame.bounds r.Back) x y (if index &&& 1 = 0 then "A" else "B") 1 paint r.Back
            Renderer.present output r
            compared <- compared + int64 r.ComparedCells
            copied <- copied + int64 r.CopiedCells
        record ("output-" + scenario) 500 step
    let run = TextRun.prepare (String.replicate 2000 "abc界e\u0301 ")
    let textRenderer = Renderer.create 80 2 30000 caps
    let textStep index =
        Frame.clear Paint.empty textRenderer.Back
        Frame.textWithWrapping (Frame.bounds textRenderer.Back) (-10000 - (index &&& 1)) 0 80 false true run Paint.empty textRenderer.Back
        Renderer.present output textRenderer
    record "text-horizontal" 500 textStep
    for virtualized in [| false; true |] do
        use graph = new Graph()
        let items, selected = graph.Run(fun () -> createSignal (Array.init 10000 (fun n -> sprintf "Item %d" n)), createSignal 0)
        let mounted = View.mount graph (fun () ->
            if virtualized then Widgets.virtualList [] 20 items selected
            else Widgets.list [] items.Peek selected)
        let r = Renderer.create 80 20 220000 caps
        let step index =
            selected.Value <- index % 20
            present mounted r
        record (if virtualized then "list-virtual" else "list-materialized") 60 step
        View.dispose mounted
    let report = String.Join(Environment.NewLine, results)
    printfn "measured here: Release benchmark; null output; per iteration includes application updates for pipeline workloads, painting for dense output workloads. Seven timed batches, sorted median and maximum batch average.\n%s" report
    if args.Length > 0 then File.WriteAllText(args[0], report)
    0
