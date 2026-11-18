namespace SystemMonitor

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Keyboard
open Ranvier.Terminal
open Ranvier.Terminal.View

type ProcessViewport =
    | Showing of Table<ProcessRow>
    | HiddenAt of firstVisible: int

type Model =
    { Revision: Signal<int>; Size: Signal<struct(int * int)>
      SampleRevision: Signal<int>; DisplayRevision: Signal<int>
      mutable PublishedSample: Sample
      mutable ProcessViewport: ProcessViewport
      mutable RowCache: (ProcessRow array * string * ProcessRow array) option
      Order: TableOrder<ProcessRow, int>
      mutable Width: int; mutable Height: int
      mutable Sample: Sample; mutable Live: bool; mutable Paused: bool
      mutable Filter: string; mutable Filtering: bool
      SelectedPid: Signal<int option>; mutable Help: bool; mutable Detail: bool
      mutable Step: int; mutable Elapsed: float; mutable NextSample: float
      mutable PollIntervalMs: int; mutable SamplesStarted: int; mutable SamplesCompleted: int
      mutable LastSampleMs: float
      mutable Dirty: bool; mutable Error: string; mutable Quit: bool
      mutable Pending: Task<struct(Sample * float)> option; Cancellation: CancellationTokenSource
      mutable ReadSample: Sample option -> CancellationToken -> Sample
      CpuHistory: ResizeArray<float>; NetHistory: ResizeArray<float> }
    member model.Scroll =
        match model.ProcessViewport with Showing table -> table.FirstVisible | HiddenAt first -> first

module Model =
    let create live (struct (width, height) as size) =
        let initial=Telemetry.demo 0
        { Revision=createSignal 0;Size=createSignal size;SampleRevision=createSignal 0;DisplayRevision=createSignal 0
          PublishedSample=initial;ProcessViewport=HiddenAt 0;RowCache=None;Width=width;Height=height
          Order=Table.sortable _.Pid
              [| { Id="cpu"; Compare=(fun a b -> compare a.Cpu b.Cpu); Descending=true }
                 { Id="mem"; Compare=(fun a b -> compare a.Memory b.Memory); Descending=true }
                 { Id="pid"; Compare=(fun a b -> compare a.Pid b.Pid); Descending=false } |] "cpu"
                |> Table.withSortIndicators "↓" "↑"
          Sample=initial;Live=live;Paused=false;Filter="";Filtering=false
          SelectedPid=createSignal None;Help=false;Detail=false;Step=0;Elapsed=0.;NextSample=0.;Dirty=true;Error="";Quit=false
          PollIntervalMs=500;SamplesStarted=0;SamplesCompleted=0;LastSampleMs=0.;ReadSample=Telemetry.read
          Pending=None;Cancellation=new CancellationTokenSource()
          CpuHistory=ResizeArray(Array.init 100 (fun i -> if i<72 then 0. else 6.+4.*sin(float i*0.6)))
          NetHistory=ResizeArray(Array.init 100 (fun i -> if i<66 then 0. else 15.+8.*sin(float i*0.4))) }

    let setPollInterval milliseconds model =
        if milliseconds<100 || milliseconds>10000 then invalidArg "milliseconds" "Polling interval must be between 100 and 10000 ms."
        model.PollIntervalMs <- milliseconds
        model.NextSample <- model.Elapsed+float milliseconds/1000.
        model.Dirty <- true

    let filteredRows model =
        match model.RowCache with
        | Some(source,filter,result) when obj.ReferenceEquals(source,model.Sample.Processes) && filter=model.Filter -> result
        | _ ->
            let result =
                if model.Filter = "" then model.Sample.Processes
                else model.Sample.Processes |> Array.filter(fun row ->
                    row.Name.Contains(model.Filter,StringComparison.OrdinalIgnoreCase) || row.Command.Contains(model.Filter,StringComparison.OrdinalIgnoreCase) || (string row.Pid).Contains model.Filter)
            model.RowCache <- Some(model.Sample.Processes,model.Filter,result)
            result

    let rows model = Table.order (filteredRows model) model.Order

    let geometry width height =
        let top=max 7 (min (height-10) (height/3))
        let left=max 20 (width*45/100)
        let bottom=height-top-3
        let memory=max 5 (bottom*55/100)
        {X=1;Y=1;Width=max 1 (width-2);Height=top},
        {X=1;Y=top+2;Width=max 1 (left-1);Height=memory},
        {X=1;Y=top+3+memory;Width=max 1 (left-1);Height=max 1 (bottom-memory-1)},
        {X=left+1;Y=top+2;Width=max 1 (width-left-2);Height=max 1 bottom}

    let handle event model =
        let navigate event =
            match model.ProcessViewport with Showing table -> table.Root.OnEvent event | HiddenAt _ -> false
        let cpu,_,_,table=geometry model.Width model.Height
        let mutable handled=true
        match event with
        | Key key when not key.Released && (key.Modifiers=Modifiers.None || (key.Key=Key.Character && key.Modifiers=Modifiers.Shift)) ->
            if model.Filtering then
                match key.LogicalKey with
                | LogicalKey.Ui Escape -> model.Filtering <- false; model.Filter <- ""
                | LogicalKey.Whitespace Enter -> model.Filtering <- false
                | LogicalKey.Editing EditingKey.Backspace ->
                    if model.Filter.Length>0 then
                        let indices=Globalization.StringInfo.ParseCombiningCharacters model.Filter
                        model.Filter <- model.Filter.Substring(0,indices[indices.Length-1])
                | LogicalKey.Character _ | LogicalKey.Whitespace Space when Text.Rune.IsValid key.Scalar && key.Scalar>=32 && model.Filter.Length<128 ->
                    model.Filter <- model.Filter+Text.Rune(key.Scalar).ToString()
                | _ -> ()
                model.SelectedPid.Value <- rows model |> Array.tryHead |> Option.map _.Pid
            else
                match key.LogicalKey with
                | LogicalKey.Whitespace Enter -> model.Detail <- not model.Detail
                | LogicalKey.Ui Escape -> model.Help <- false; model.Detail <- false; model.Filter <- ""
                | LogicalKey.Character _ | LogicalKey.Whitespace Space when not key.Paste && key.Scalar <= 127 ->
                    match Char.ToLowerInvariant(char key.Scalar) with
                    | 'q' -> model.Quit <- true
                    | '/' | 'f' -> model.Filtering <- true
                    | 's' | 'r' -> handled <- navigate event
                    | '+' | '=' -> setPollInterval (min 10000 (model.PollIntervalMs+100)) model
                    | '-' | '_' -> setPollInterval (max 100 (model.PollIntervalMs-100)) model
                    | 'p' | ' ' -> model.Paused <- not model.Paused
                    | 'l' ->
                        model.Live <- not model.Live
                        model.NextSample <- 0.
                        model.Error <- ""
                        if not model.Live then model.Sample <- Telemetry.demo model.Step
                    | '?' | 'm' -> model.Help <- not model.Help
                    | _ -> handled <- false
                | _ -> handled <- navigate event
        | Mouse mouse when mouse.Action=MouseAction.Press && mouse.Y=cpu.Y && mouse.X>=cpu.X+cpu.Width-12 && mouse.X<cpu.X+cpu.Width-9 ->
            setPollInterval (max 100 (model.PollIntervalMs-100)) model
        | Mouse mouse when mouse.Action=MouseAction.Press && mouse.Y=cpu.Y && mouse.X>=cpu.X+cpu.Width-4 && mouse.X<cpu.X+cpu.Width-1 ->
            setPollInterval (min 10000 (model.PollIntervalMs+100)) model
        | Mouse mouse when Rect.contains mouse.X mouse.Y table ->
            if mouse.Action=MouseAction.Scroll || (mouse.Action=MouseAction.Press && mouse.X=table.X+table.Width-1) then
                let x=Math.Clamp(mouse.X,table.X+1,table.X+table.Width-1)
                let y=Math.Clamp(mouse.Y,table.Y+2,table.Y+max 2 (table.Height-2))
                handled <- navigate (Mouse {mouse with X=x;Y=y})
            else handled <- navigate event
        | Resize(struct(width,height)) -> model.Width <- max 1 width;model.Height <- max 1 height
        | _ -> handled <- false
        if handled then model.Dirty <- true
        handled

    let private history value (target: ResizeArray<float>) =
        target.Add value
        if target.Count>160 then target.RemoveAt 0

    let tick seconds model =
        model.Elapsed <- model.Elapsed+seconds
        match model.Pending with
        | Some pending when pending.IsCompleted && not model.Paused ->
            model.Pending <- None
            if pending.IsCompletedSuccessfully && model.Live then
                let struct(sample,duration)=pending.Result
                model.LastSampleMs <- duration
                model.SamplesCompleted <- model.SamplesCompleted+1
                model.Sample <- sample
                model.Error <- ""
                history model.Sample.Cpu model.CpuHistory
                history (min 100. (model.Sample.Download/102.4)) model.NetHistory
                model.Dirty <- true
            elif pending.IsFaulted then model.Error <- pending.Exception.GetBaseException().Message; model.Dirty <- true
        | _ -> ()
        if not model.Paused && model.Elapsed>=model.NextSample && (not model.Live || model.Pending.IsNone) then
            model.NextSample <- model.Elapsed+float model.PollIntervalMs/1000.
            if model.Live then
                if model.Pending.IsNone then
                    let previous=if model.Sample.Demo then None else Some model.Sample
                    let readSample=model.ReadSample
                    model.SamplesStarted <- model.SamplesStarted+1
                    model.Pending <- Some(Task.Run<struct(Sample * float)>(fun () ->
                        let timer=Stopwatch.StartNew()
                        let sample=readSample previous model.Cancellation.Token
                        struct(sample,timer.Elapsed.TotalMilliseconds)))
            else
                model.Step <- model.Step+1
                model.Sample <- Telemetry.demo model.Step
                history model.Sample.Cpu model.CpuHistory
                history (model.Sample.Download/102.4) model.NetHistory
            if not model.Live then model.Dirty <- true

    let close model =
        model.Cancellation.Cancel()
        model.Pending |> Option.iter(fun work -> try work.GetAwaiter().GetResult() |> ignore with :? OperationCanceledException -> ())
        model.Cancellation.Dispose()
