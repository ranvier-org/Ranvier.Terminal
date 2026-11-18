module PreparedBench

open System
open System.Diagnostics
open Ranvier.Terminal
open Ranvier
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let caps = {Colors=TrueColor;SynchronizedOutput=false}
let budget = {Glyphs=512;Paints=64;Utf8Bytes=65536;OutputBytes=262144}
let sink = {Write=(fun _ _ _ -> ());Flush=(fun () -> ())}

let measure name iterations action =
    let clock = Stopwatch.StartNew()
    let mutable index = 0
    while clock.ElapsedMilliseconds < 500L do
        action index
        index <- index+1
    let times = Array.zeroCreate<float> 7
    let mutable allocated = 0L
    for batch = 0 to times.Length-1 do
        let before = GC.GetAllocatedBytesForCurrentThread()
        clock.Restart()
        for i = 0 to iterations-1 do action i
        clock.Stop()
        allocated <- allocated+GC.GetAllocatedBytesForCurrentThread()-before
        times[batch] <- clock.Elapsed.TotalMicroseconds/float iterations
    Array.sortInPlace times
    printfn "%s,%.3f,%.3f,%.3f" name times[3] times[6] (float allocated / float (iterations*times.Length))

let cells prepared unicode dense =
    let strings = if unicode then [|"é";"α"|] else [|"a";"b"|]
    let label = sprintf "%s-%s-%s" (if prepared then "prepared" else "ordinary") (if unicode then "unicode" else "ascii") (if dense then "dense" else "sparse")
    if prepared then
        let r = PreparedRenderer.create 160 50 budget caps
        let glyphs = strings |> Array.map (fun s -> PreparedRenderer.prepareGlyph s 1 r)
        let paint = PreparedRenderer.preparePaint Paint.empty r
        let clip = PreparedRenderer.bounds r
        PreparedRenderer.present sink r
        measure label 1000 (fun i ->
            if dense then
                for y = 0 to 49 do
                    for x = 0 to 159 do PreparedRenderer.put clip x y glyphs[i&&&1] paint r
                PreparedRenderer.present sink r
            else
                PreparedRenderer.put clip 3 20 glyphs[i&&&1] paint r
                PreparedRenderer.presentRegion sink {X=0;Y=20;Width=160;Height=1} r)
    else
        let r = Renderer.create 160 50 budget.OutputBytes caps
        let clip = Frame.bounds r.Back
        Renderer.present sink r
        measure label 1000 (fun i ->
            if dense then
                for y = 0 to 49 do
                    for x = 0 to 159 do Frame.put clip x y strings[i&&&1] 1 Paint.empty r.Back
                Renderer.present sink r
            else
                Frame.put clip 3 20 strings[i&&&1] 1 Paint.empty r.Back
                Renderer.presentRegion sink {X=0;Y=20;Width=160;Height=1} r)

let text prepared =
    let paragraphs = [|"Prepared English words é 界 👩‍💻 with multiple lines and wrapping. "+String.replicate 5 "sample text ";"Changing English words α 界 👩‍💻 with multiple lines and wrapping. "+String.replicate 5 "other words "|]
    let paint = {Paint.empty with Foreground=Rgb 0x68ACE0;Background=Rgb 0x102030}
    if prepared then
        let r = PreparedRenderer.create 160 50 budget caps
        let runs = paragraphs |> Array.map (fun s -> PreparedRenderer.prepareText s r)
        let style = PreparedRenderer.preparePaint paint r
        let clip = PreparedRenderer.bounds r
        measure "prepared-wrapped-paint" 1000 (fun i ->
            PreparedRenderer.clear style r
            for band = 0 to 5 do PreparedRenderer.text clip 3 (band*8) 70 true true runs[i&&&1] style r
            PreparedRenderer.present sink r)
        measure "prepared-register-warm" 1000 (fun i -> GC.KeepAlive(PreparedRenderer.prepareText paragraphs[i&&&1] r))
    else
        let r = Renderer.create 160 50 budget.OutputBytes caps
        let runs = paragraphs |> Array.map TextRun.prepare
        let clip = Frame.bounds r.Back
        measure "ordinary-wrapped-paint" 1000 (fun i ->
            Frame.clear paint r.Back
            for band = 0 to 5 do Frame.textWithWrapping clip 3 (band*8) 70 true true runs[i&&&1] paint r.Back
            Renderer.present sink r)
        measure "ordinary-register-warm" 1000 (fun i -> GC.KeepAlive(TextRun.prepare paragraphs[i&&&1]))

let retained prepared dense =
    use graph = new Graph()
    let runs = [|TextRun.prepare "Alpha é 界 content";TextRun.prepare "Bravo α 界 content"|]
    let children = Array.init 40 (fun _ ->
        let node=Node.create [Css.height (Css.cells 1);Css.wrap false] [||]
        Node.setPreparedText runs[0] node
        node)
    let root = Node.create [Css.direction Column] children
    let mounted = View.mount graph (fun () -> root)
    let label = sprintf "%s-retained-%s" (if prepared then "prepared" else "ordinary") (if dense then "dense-prepared-text" else "sparse-new-text")
    if prepared then
        let r=PreparedRenderer.create 160 50 budget caps
        let cache=PreparedLayout.create mounted r
        measure label 1000 (fun i ->
            if dense then
                for child in children do Node.setPreparedText runs[i&&&1] child
            else Node.setText ("Count "+string i+" é 界") children[0]
            PreparedLayout.refresh false cache
            PreparedLayout.present sink cache)
    else
        let r=Renderer.create 160 50 budget.OutputBytes caps
        measure label 1000 (fun i ->
            if dense then
                for child in children do Node.setPreparedText runs[i&&&1] child
            else Node.setText ("Count "+string i+" é 界") children[0]
            Layout.presentReactive sink mounted r |> ignore)
    View.dispose mounted

let run () =
    printfn "workload,median_us,max_batch_us,allocated_bytes"
    for unicode in [false;true] do
        for dense in [false;true] do
            cells false unicode dense
            cells true unicode dense
    text false
    text true
    retained false false
    retained true false
    retained false true
    retained true true
    measure "ordinary-setup-register" 500 (fun i ->
        let r=Renderer.create 160 50 budget.OutputBytes caps
        let text=TextRun.prepare ("sample é 界 👩‍💻 "+string i)
        GC.KeepAlive(r,text))
    measure "prepared-setup-register" 500 (fun i ->
        let r=PreparedRenderer.create 160 50 budget caps
        let text=PreparedRenderer.prepareText ("sample é 界 👩‍💻 "+string i) r
        GC.KeepAlive(r,text))
    0

[<EntryPoint>]
let main args =
    if args.Length=2 && args[0]="--native-scroll" then PreparedBenchNative.run args[1]
    else run ()
