module WidgetBench

open System
open System.Diagnostics
open System.IO
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

// Preserved implementation provides an in-process paired callback baseline.
let baseline visibleRows (items: Signal<string array>) (selected: Signal<int>) =
    let rows = Array.init visibleRows (fun _ -> Node.create [ Css.height (Css.cells 1) ] [||])
    let node = Node.create [ Css.height (Css.cells visibleRows) ] rows
    let mutable first = 0
    createEffect (fun () ->
        let values = items.Value
        let current = max 0 (min (values.Length - 1) selected.Value)
        first <- min (max 0 (values.Length - visibleRows)) first
        if current < first then first <- current
        elif current >= first + visibleRows then first <- current - visibleRows + 1
        for slot = 0 to rows.Length - 1 do
            let index = first + slot
            let visible = index < values.Length
            Node.setStyle [ Css.height (Css.cells 1); Css.visible visible
                            Css.attributes (if index = current then Attributes.Reverse else Attributes.None) ] rows[slot]
            if visible then Node.setText values[index] rows[slot])
    node

let measure name iterations step =
    let warm = Stopwatch.StartNew()
    let mutable i = 0
    while warm.ElapsedMilliseconds < 500L do step i; i <- i + 1
    let times = Array.zeroCreate<float> 7
    let mutable allocated = 0L
    for batch = 0 to times.Length - 1 do
        let startBytes = GC.GetAllocatedBytesForCurrentThread()
        let start = Stopwatch.GetTimestamp()
        for index = 0 to iterations - 1 do step index
        times[batch] <- Stopwatch.GetElapsedTime(start).TotalMicroseconds / float iterations
        allocated <- allocated + GC.GetAllocatedBytesForCurrentThread() - startBytes
    Array.sortInPlace times
    sprintf "%s,%.3f,%.3f,%.3f" name times[3] times[6] (float allocated / float (iterations * times.Length))

let collectionIdentity () =
    for keyed in [ false; true ] do
        use graph = new Graph()
        let short = Array.init 100 id
        let long = Array.append short [| 100 |]
        let items = graph.Run(fun () -> createSignal short)
        let build read = Dsl.text (fun () -> string (read ()))
        let mounted = View.mount graph (fun () ->
            if keyed then Dsl.eachKeyed id (fun () -> items.Value) build
            else Dsl.each (fun () -> items.Value) (fun item -> build (fun () -> item)))
        try
            let name = if keyed then "keyed" else "rebuild"
            printfn "%s" (measure ("toggle-last-of-101-bound-rows-" + name) 100 (fun i ->
                items.Value <- if i % 2 = 0 then long else short))
        finally View.dispose mounted

let components () =
    use graph = new Graph()
    let tick, state, caption = graph.Run(fun () ->
        createSignal 0, createSignal TaskStatus.Spinner, createSignal "BUILD")
    let mounted = View.mount graph (fun () ->
        Dsl.column [] {
            TaskStatus.create [] (fun _ -> []) 12 (fun () -> tick.Value) (fun () -> state.Value)
            Figlet.banner [] Figlet.compact (fun () -> 64) (fun () -> caption.Value)
        })
    try
        let renderer = Renderer.create 64 12 65536 { Colors=TrueColor; SynchronizedOutput=false }
        let sink = { Write = (fun _ _ _ -> ()); Flush = ignore }
        let present () = Layout.presentReactive sink mounted renderer |> ignore
        let tickAndPresent i = tick.Value <- i; present ()
        present ()
        printfn "workload,median_us,max_batch_us,allocated_bytes_per_iteration"
        printfn "%s" (measure "spinner-tick-through-null-output" 2000 tickAndPresent)
        state.Value <- TaskStatus.Success
        present ()
        printfn "%s" (measure "finished-task-host-tick-through-idle-present" 2000 tickAndPresent)
        printfn "%s" (measure "figlet-changing-heading-through-null-output" 500 (fun i ->
            caption.Value <- if i % 2 = 0 then "BUILD" else "READY"
            present ()))
        let items = Array.init 1000 (fun i -> (i * 37) % 1000)
        let sort = Table.sortable id [| { Id="number"; Compare=compare; Descending=false } |] "number"
        printfn "%s" (measure "typed-sort-1000-integers-reverse-and-order" 200 (fun _ ->
            Table.reverseSort sort
            Table.order items sort |> ignore))
        printfn "%s" (measure "typed-sort-1000-integers-cached-order" 10000 (fun _ -> Table.order items sort |> ignore))
        collectionIdentity ()
        0
    finally View.dispose mounted

let lists (args: string array) =
    VirtualizationChecks.run ()
    printfn "measured here: widget regression checks passed"
    let results = ResizeArray<string>()
    let memory = ResizeArray<string>()
    memory.Add "workload,retained_heap_bytes,setup_allocated_bytes"
    results.Add "workload,median_us,max_batch_us,allocated_bytes_per_iteration"
    for mode in [ "baseline"; "candidate" ] do
        for scrolling in [ false; true ] do
            use graph = new Graph()
            let items, selected = graph.Run(fun () -> createSignal (Array.init 10000 string), createSignal 0)
            let mounted = View.mount graph (fun () ->
                if mode = "baseline" then baseline 20 items selected else Widgets.virtualList [] 20 items selected)
            let step index = selected.Value <- index % (if scrolling then 200 else 20)
            results.Add(measure (sprintf "fixed-%s-%s" mode (if scrolling then "scroll" else "selection")) 5000 step)
            View.dispose mounted
    for mode in [ "materialized"; "virtual-cached"; "virtual-extent" ] do
        use graph = new Graph()
        let items, selected, viewport, scroll = graph.Run(fun () ->
            createSignal (Array.init 10000 (fun i -> sprintf "Row %d: hello world 界\nsecond line" i)),
            createSignal 0, createSignal { Width = 40; Height = 20 }, createSignal 0)
        let heapBefore = GC.GetTotalMemory(true)
        let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()
        let mounted = View.mount graph (fun () ->
            if mode = "virtual-cached" then Widgets.virtualTextList [] viewport items selected scroll
            elif mode = "virtual-extent" then Widgets.virtualTextListWithCache [] 0 viewport items selected scroll
            else
                let rows = items.Peek |> Array.mapi (fun index value ->
                    let row = Node.create [ Css.wrap true; Css.attributes (if index = 0 then Attributes.Reverse else Attributes.None) ] [||]
                    Node.setText value row
                    row)
                let root = Node.create [] rows
                createEffect (fun () -> Node.setScroll 0 scroll.Value root)
                root)
        let allocatedSetup = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
        let retained = GC.GetTotalMemory(true) - heapBefore
        memory.Add(sprintf "%s,%d,%d" mode retained allocatedSetup)
        let renderer = Renderer.create 40 20 100000 { Colors = TrueColor; SynchronizedOutput = false }
        let output = { Write = (fun _ _ _ -> ()); Flush = (fun () -> ()) }
        let step index =
            scroll.Value <- index % 200
            Layout.presentReactive output mounted renderer |> ignore
        results.Add(measure ("variable-" + mode + "-scroll-pipeline") 200 step)
        let resize index =
            let width = if index % 2 = 0 then 19 else 40
            viewport.Value <- { Width = width; Height = 20 }
            Renderer.resize width 20 renderer
            Layout.presentReactive output mounted renderer |> ignore
        results.Add(measure ("variable-" + mode + "-resize-pipeline") 10 resize)
        GC.KeepAlive mounted
        View.dispose mounted
    let report = String.Join(Environment.NewLine, results)
    printfn "%s" report
    let memoryReport = String.Join(Environment.NewLine, memory)
    printfn "measured here: live managed heap delta after forced collection, excluding input strings; not process working set\n%s" memoryReport
    if args.Length > 0 then
        File.WriteAllText(args[0], report)
        File.WriteAllText(Path.ChangeExtension(args[0], "memory.csv"), memoryReport)
    0

[<EntryPoint>]
let main args =
    if args = [| "--components" |] then components () else lists args
