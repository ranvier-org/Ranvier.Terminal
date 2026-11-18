module SystemMonitor.Program

open System
open System.IO
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let capture path (frame: Frame) =
    let color = function Rgb rgb -> sprintf "#%06x" rgb | _ -> "#262626"
    let cells=frame.Cells |> Array.map(fun cell -> {|Glyph=cell.Glyph;Foreground=color cell.Paint.Foreground;Background=color cell.Paint.Background;Width=int cell.Width|})
    File.WriteAllText(path,Text.Json.JsonSerializer.Serialize {|Width=frame.Width;Height=frame.Height;Cells=cells|})

let verify directory =
    use graph=new Graph()
    let model=graph.Run(fun () -> Model.create false (struct (120, 64)))
    let view=View.mount graph (fun () -> Kit.build model)
    let require condition message=if not condition then failwith message
    let key kind scalar =
        View.dispatch (Key {Key=kind;Scalar=scalar;Modifiers=Modifiers.None;Released=false;Paste=false}) view |> ignore
        graph.Run(fun () -> Kit.update model)
    let render width height =
        View.dispatch (Resize(struct(width,height))) view |> ignore
        graph.Run(fun () -> Kit.update model)
        let frame = Frame.create width height
        Layout.render view frame
        frame
    try
        for width,height in [120,64;100,30;80,24;64,20;40,12;1,1] do
            let frame=render width height
            require (frame.Cells.Length=width*height) "Frame dimensions changed"
            require (frame.Cells |> Array.forall(fun cell -> cell.Width=1uy || cell.Width=0uy || cell.Width=2uy)) "Invalid glyph width"
            directory |> Option.iter(fun path -> capture (Path.Combine(path,sprintf "monitor-%dx%d.json" width height)) frame)
        let dashboard = render 120 64
        key Key.Character (int '?')
        let help = render 120 64
        require (not (Array.forall2 Cell.equal dashboard.Cells help.Cells)) "Help overlay did not appear"
        key Key.Escape 0
        let restored = render 120 64
        require (Array.forall2 Cell.equal dashboard.Cells restored.Cells) "Closing help did not restore the dashboard"
        let before=model.Sample.Cpu
        graph.Run(fun () -> Model.tick 1. model)
        require (model.Sample.Cpu<>before) "Demo did not advance"
        key Key.Character (int 'p')
        let step=model.Step
        graph.Run(fun () -> Model.tick 2. model)
        require (model.Step=step) "Pause did not stop sample changes"
        let _,_,_,table = Model.geometry model.Width model.Height
        View.dispatch (Mouse {X=table.X+2;Y=table.Y+2;Button=0;Action=MouseAction.Press;Delta=0;Modifiers=Modifiers.None}) view |> ignore
        key Key.Character (int '/')
        for character in "dotnet" do key Key.Character (int character)
        require ((Model.rows model) |> Array.forall(fun row -> row.Name="dotnet")) "Filter failed"
        key Key.End 0
        require (model.SelectedPid.Peek=Some (Array.head (Model.rows model)).Pid && model.Filtering) "Focused table consumed End during filter editing"
        key Key.Enter 0
        key Key.End 0
        require (model.SelectedPid.Peek=Some (Array.last (Model.rows model)).Pid) "End selection failed"
        key Key.Escape 0
        key Key.Character (int '/')
        key Key.Character 0x1F680
        key Key.Backspace 0
        require (model.Filter="") "Backspace split a Unicode scalar"
        key Key.Character (int 'e')
        key Key.Character 0x301
        key Key.Backspace 0
        require (model.Filter="") "Backspace split a combining cluster"
        key Key.Escape 0
        let selectedPid () = model.SelectedPid.Peek.Value
        let beforeSort = selectedPid ()
        key Key.Character (int 's')
        let sorted=Model.rows model
        require (sorted[0].Memory >= sorted[sorted.Length-1].Memory) "Memory sort failed"
        require (selectedPid () = beforeSort) "Changing the sort lost the selected PID"
        key Key.Character (int 'r')
        require (selectedPid () = beforeSort && (Model.rows model).[0].Memory <= (Model.rows model).[sorted.Length-1].Memory) "Reversing sort lost the selected PID"
        View.dispatch (Mouse {X=table.X+2;Y=table.Y+1;Button=0;Action=MouseAction.Press;Delta=0;Modifiers=Modifiers.None}) view |> ignore
        graph.Run(fun () -> Kit.update model)
        require (model.Order.SortId="pid" && selectedPid () = beforeSort) "Clicking the PID header lost selection"
        let kept = sorted |> Array.find (fun row -> row.Pid = beforeSort)
        model.Sample <- { model.Sample with Processes = [| { kept with Cpu = 72. }; { kept with Pid = beforeSort - 1 } |] }
        model.Dirty <- true
        graph.Run(fun () -> Kit.update model)
        require (selectedPid () = beforeSort) "A smaller sample lost the selected PID"
        key Key.Character (int '/')
        for character in "definitely missing" do key Key.Character (int character)
        require ((Model.rows model).Length=0) "Empty filter fixture failed"
        let empty = render 120 64
        let right = table.X + table.Width - 1
        for y = table.Y + 1 to table.Y + table.Height - 2 do
            require (empty.Cells[y * empty.Width + right].Glyph = "│") "Filtering removed the process panel border"
        key Key.Escape 0
        let old=view.Root.Children.Length
        graph.Run(fun () -> Kit.changed();model.Revision.Value <- Kit.currentRevision())
        require (view.Root.Children.Length=old) "Registry reload lost view"
        use cancellation=new CancellationTokenSource()
        let sample=Telemetry.read None cancellation.Token
        require (not sample.Demo && sample.Processes.Length>0) "Live host collection failed"
        require (sample.Total>=0L && sample.Available>=0L) "Invalid memory readings"
        cancellation.Cancel()
        let mutable cancelled=false
        try Telemetry.read None cancellation.Token |> ignore with :? OperationCanceledException -> cancelled <- true
        require cancelled "Telemetry ignored cancellation"
        printfn "measured here: monitor responsive rendering, demo animation, pause, filter, sorting, selection, empty results, registry reload and live sampling passed."
    finally
        View.dispose view
        Model.close model

let run live smoke interval =
    use stop=new CancellationTokenSource()
    Terminal.runApp Terminal.defaults stop.Token (fun viewport ->
        let model = Model.create live (viewport ())
        onCleanup (fun () -> Model.close model)
        Model.setPollInterval interval model
        model.NextSample <- 0.
        Kit.update model
        let update (delta: TimeSpan) =
            model.Revision.Value <- Kit.currentRevision()
            Model.tick delta.TotalSeconds model
            Kit.update model
            if model.Quit || (smoke && model.Elapsed > 3.) then stop.Cancel()
            Some(TimeSpan.FromMilliseconds 50.)
        Kit.build model, update)

[<EntryPoint>]
let main args =
    if Array.contains "--verify" args then
        let directory=args |> Array.tryPick(fun arg -> if arg.StartsWith "--capture=" then Some(arg.Substring 10) else None)
        directory |> Option.iter(fun path -> Directory.CreateDirectory path |> ignore)
        verify directory
    else
        let interval=args |> Array.tryPick(fun arg -> if arg.StartsWith "--interval-ms=" then Some(Int32.Parse(arg.Substring 14)) else None) |> Option.defaultValue 500
        if interval<100 || interval>10000 then invalidArg "--interval-ms" "Polling interval must be between 100 and 10000 ms."
        run (Array.contains "--live" args) (Array.contains "--smoke" args) interval
    0
