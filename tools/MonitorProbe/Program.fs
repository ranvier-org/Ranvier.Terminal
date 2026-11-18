module MonitorProbe

open System
open System.Diagnostics
open System.IO
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View
open SystemMonitor

let checkScroll () =
    use graph=new Graph()
    let model=graph.Run(fun () -> Model.create false (struct (120, 40)))
    let view=View.mount graph (fun () -> Kit.build model)
    try
        graph.Run(fun () -> Kit.update model)
        Layout.render view (Frame.create 120 40)
        let _,_,_,table=Model.geometry 120 40
        let wheel=Mouse {X=table.X+5;Y=table.Y+5;Button=0;Action=MouseAction.Scroll;Delta=3;Modifiers=Modifiers.None}
        View.dispatch wheel view |> ignore
        graph.Run(fun () -> Kit.update model)
        if model.Scroll<=0 then failwith "Mouse wheel was swallowed before the process viewport scrolled."
        let scroll=model.Scroll
        let pid=model.SelectedPid.Peek
        let resize width height =
            View.dispatch (Resize(struct (width,height))) view |> ignore
            graph.Run(fun () -> Kit.update model)
            Layout.render view (Frame.create width height)
        resize 119 40
        if model.Scroll<>scroll || model.SelectedPid.Peek<>pid then failwith "Width-only resize moved the process viewport"
        resize 40 12
        resize 120 40
        if model.Scroll<>scroll || model.SelectedPid.Peek<>pid then failwith "Restoring the process panel moved its viewport"
        View.dispatch (Mouse {X=2;Y=3;Button=0;Action=MouseAction.Scroll;Delta=3;Modifiers=Modifiers.None}) view |> ignore
        if model.Scroll<>scroll then failwith "Wheel outside process panel scrolled it"
        let press x y=View.dispatch (Mouse {X=x;Y=y;Button=0;Action=MouseAction.Press;Delta=0;Modifiers=Modifiers.None}) view |> ignore
        press (table.X+table.Width-1) (table.Y+table.Height-2)
        graph.Run(fun () -> Kit.update model)
        if model.Scroll <> (Model.rows model).Length-(table.Height-3) then failwith "Scrollbar bottom did not reach final rows"
        View.dispatch (Key {Key=Key.Home;Scalar=0;Modifiers=Modifiers.None;Released=false;Paste=false}) view |> ignore
        graph.Run(fun () -> Kit.update model)
        if model.Scroll<>0 then failwith "Home did not return to top"
        if model.PollIntervalMs<>500 then failwith "Default polling interval changed"
        View.dispatch (Key {Key=Key.Character;Scalar=int '+';Modifiers=Modifiers.Shift;Released=false;Paste=false}) view |> ignore
        if model.PollIntervalMs<>600 then failwith "Shift-plus did not adjust interval"
        let cpu,_,_,_=Model.geometry 120 40
        press (cpu.X+cpu.Width-12) cpu.Y
        if model.PollIntervalMs<>500 then failwith "Interval mouse button failed"
        for invalid in [0;99;10001] do
            let mutable rejected=false
            try Model.setPollInterval invalid model with :? ArgumentException -> rejected <- true
            if not rejected then failwith "Invalid polling interval accepted"
        printfn "PASS real dispatch: process wheel, bounds, scrollbar jump, Home, keyboard/mouse interval controls and interval validation"
    finally
        View.dispose view
        Model.close model

let checkSlowSampler () =
    use graph=new Graph()
    let model=graph.Run(fun () -> Model.create true (struct (120, 40)))
    let view=View.mount graph (fun () -> Kit.build model)
    use entered=new ManualResetEventSlim(false)
    use release=new ManualResetEventSlim(false)
    try
        model.ReadSample <- fun _ cancellation ->
            entered.Set()
            release.Wait cancellation
            {Telemetry.demo 1 with Demo=false}
        graph.Run(fun () -> Model.tick 0. model; Kit.update model)
        if not (entered.Wait 5000) then failwith "Slow sampler fixture did not start"
        Layout.render view (Frame.create 120 40)
        let pending=model.Pending.Value
        for _=1 to 10 do graph.Run(fun () -> Model.tick 1. model)
        if model.SamplesStarted<>1 || not (obj.ReferenceEquals(pending,model.Pending.Value)) then failwith "Slow polls overlapped"
        let _,_,_,table=Model.geometry 120 40
        let before = Frame.create model.Width model.Height
        Layout.render view before
        View.dispatch (Mouse {X=table.X+4;Y=table.Y+4;Button=0;Action=MouseAction.Scroll;Delta=3;Modifiers=Modifiers.None}) view |> ignore
        graph.Run(fun () -> Kit.update model)
        let after = Frame.create model.Width model.Height
        Layout.render view after
        if model.Scroll<>3 || Array.forall2 Cell.equal before.Cells after.Cells then failwith "Input could not redraw while sampler was blocked"
        Model.setPollInterval 100 model
        graph.Run(fun () -> Model.tick 1. model)
        if model.SamplesStarted<>1 then failwith "Changing interval overlapped a pending read"
        model.Paused <- true
        release.Set()
        pending.GetAwaiter().GetResult() |> ignore
        graph.Run(fun () -> Model.tick 1. model)
        if model.SamplesCompleted<>0 then failwith "Paused sampler published a result"
        model.Paused <- false
        model.NextSample <- model.Elapsed+100.
        graph.Run(fun () -> Model.tick 0. model)
        if model.SamplesCompleted<>1 then failwith "Resume lost completed sample"
        printfn "PASS blocked sampler: no overlapping reads, interval changes, responsive scrolling/redraw, paused publication and resume"
    finally
        release.Set()
        View.dispose view
        Model.close model

let summary (values: ResizeArray<float>) =
    let sorted=values.ToArray() |> Array.sort
    let at percentile=if sorted.Length=0 then 0. else sorted[min (sorted.Length-1) (int(ceil(float sorted.Length*percentile))-1)]
    (if sorted.Length=0 then 0. else Array.average sorted),at 0.95,at 0.99,at 1.

let measure native tall path =
    use report=new StreamWriter(path,false)
    report.AutoFlush <- true
    if tall && OperatingSystem.IsWindows() then
        Console.SetBufferSize(max 120 Console.BufferWidth,max 64 Console.BufferHeight)
        Console.SetWindowSize(min 120 Console.LargestWindowWidth,min 64 Console.LargestWindowHeight)
    let session=if native then Some(Terminal.openSession Terminal.defaults) else None
    let width,height=if native then max 1 Console.WindowWidth,max 1 Console.WindowHeight else 120,64
    let output=session |> Option.map(fun value -> value.Output) |> Option.defaultValue {Write=(fun _ _ _ -> ());Flush=ignore}
    let emit message=report.WriteLine(message:string)
    try
        emit (sprintf "measured here: %s; %dx%d; injected process navigation -> model tick -> view update -> retained layout/diff/encode/output. Excludes physical input transport and terminal compositor." (if native then "native terminal output" else "null output") width height)
        let warm=Telemetry.read None CancellationToken.None
        for interval in [2000;500;100;500;2000] do
            use graph=new Graph()
            let model=graph.Run(fun () -> Model.create true (struct (width, height)))
            model.Sample <- warm
            Model.setPollInterval interval model
            model.Paused <- true
            let view=View.mount graph (fun () -> Kit.build model)
            let renderer=Renderer.create width height (width*height*128+64) Terminal.defaults.Capabilities
            let step delta =
                let lastPid = Model.rows model |> Array.tryLast |> Option.map _.Pid
                let kind=if model.SelectedPid.Peek = lastPid then Key.Home else Key.Down
                View.dispatch (Key {Key=kind;Scalar=0;Modifiers=Modifiers.None;Released=false;Paste=false}) view |> ignore
                graph.Run(fun () -> Model.tick delta model; Kit.update model)
                Layout.presentReactive output view renderer |> ignore
            try
                for _=1 to 30 do step 0.
                model.Paused <- false
                model.NextSample <- 0.
                let ui=ResizeArray<float>()
                let gaps=ResizeArray<float>()
                let sampling=ResizeArray<float>()
                let timer=Stopwatch.StartNew()
                let mutable previous=0.
                let mutable next=0.
                let mutable completed=0
                let allocation=GC.GetTotalAllocatedBytes false
                use current=Process.GetCurrentProcess()
                let cpu=current.TotalProcessorTime
                while timer.Elapsed.TotalSeconds<8. do
                    let now=timer.Elapsed.TotalSeconds
                    if now>=next then
                        if previous>0. then gaps.Add((now-previous)*1000.)
                        session |> Option.iter(fun value -> value.Input.Poll(fun event -> View.dispatch event view |> ignore))
                        let start=Stopwatch.GetTimestamp()
                        step (now-previous)
                        ui.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds)
                        if completed<>model.SamplesCompleted then
                            completed <- model.SamplesCompleted
                            sampling.Add model.LastSampleMs
                        previous <- now
                        next <- now+1./60.
                    else Thread.Sleep 1
                let allocated=GC.GetTotalAllocatedBytes(false)-allocation
                let cpuMs=(current.TotalProcessorTime-cpu).TotalMilliseconds
                let mean,p95,p99,maximum=summary ui
                let _,_,gap99,gapMax=summary gaps
                let samplerMean,_,_,samplerMax=summary sampling
                emit(sprintf "interval_ms=%d frames=%d samples=%d ui_mean_ms=%.3f ui_p95_ms=%.3f ui_p99_ms=%.3f ui_max_ms=%.3f ui_over_50ms=%d gap_p99_ms=%.3f gap_max_ms=%.3f gaps_over_100ms=%d sampler_mean_ms=%.3f sampler_max_ms=%.3f allocated_bytes=%d process_cpu_ms=%.1f" interval ui.Count sampling.Count mean p95 p99 maximum (ui |> Seq.filter(fun value -> value>50.) |> Seq.length) gap99 gapMax (gaps |> Seq.filter(fun value -> value>100.) |> Seq.length) samplerMean samplerMax allocated cpuMs)
            finally
                View.dispose view
                Model.close model
    finally session |> Option.iter(fun value -> value.Close())

[<EntryPoint>]
let main args =
    if args.Length=0 then checkScroll(); checkSlowSampler()
    else measure (args[0].StartsWith "--native") (args[0]="--native-tall") args[1]
    0
