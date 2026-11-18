module TaskStatusChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

let run () =
    let equal expected actual name =
        if expected <> actual then failwithf "%s: expected %A, got %A" name expected actual
    use graph = new Graph()
    let state, clock = graph.Run(fun () -> createSignal TaskStatus.Queued, createSignal 0)
    let mutable reads = 0
    let tick () = reads <- reads + 1; clock.Value
    let ink = Rgb 0x33AA11
    let style = function TaskStatus.Success -> [ Foreground ink ] | _ -> []
    let mounted = View.mount graph (fun () -> TaskStatus.create [] style 3 tick (fun () -> state.Value))
    let frame = Frame.create 20 1
    let draw () = Layout.renderReactive mounted frame
    let set value = graph.Run(fun () -> state.Value <- value)
    let step n = graph.Run(fun () -> clock.Value <- n)
    try
        draw () |> ignore
        equal 0 reads "queued clock reads"
        set TaskStatus.Spinner
        draw () |> ignore
        let initial = mounted.Root.Text.Source
        step 1
        equal true (draw ()) "host tick invalidates spinner"
        equal true (initial <> mounted.Root.Text.Source) "spinner advances"
        set TaskStatus.Pulse
        step 0
        equal "█░░ Running" mounted.Root.Text.Source "pulse start"
        step 3
        equal "░█░ Running" mounted.Root.Text.Source "pulse returns"
        for status, expected in
            [ TaskStatus.Determinate 0.5, "█░░ 50%"
              TaskStatus.Determinate nan, "░░░ 0%"
              TaskStatus.Determinate infinity, "███ 100%"
              TaskStatus.Success, "✓ Success"
              TaskStatus.Failure, "× Failure"
              TaskStatus.Cancelled, "– Cancelled"
              TaskStatus.Queued, "· Queued" ] do
            set status
            draw () |> ignore
            equal expected mounted.Root.Text.Source "status text"
            if status = TaskStatus.Success then equal ink frame.Cells[0].Paint.Foreground "status styling"
            let before = reads
            step (clock.Peek + 1)
            equal before reads "static state unsubscribes clock"
            equal false (draw ()) "idle frame stays clean"
        set TaskStatus.Spinner
        let before = reads
        step -1
        equal (before + 1) reads "resumed status tracks clock again"
    finally
        View.dispose mounted

    let presentation =
        { TaskStatus.defaults with
            SpinnerFrames = [| "/"; "-" |]
            FilledGlyph = '#'
            TrackGlyph = '.'
            RunningLabel = ""
            QueuedText = "Waiting"
            ProgressLabel = fun _ -> "loaded" }
    let custom = View.mount graph (fun () -> TaskStatus.createWith presentation [] style 3 tick (fun () -> state.Value))
    try
        step 0
        equal "/" custom.Root.Text.Source "custom spinner without label"
        step 1
        equal "-" custom.Root.Text.Source "second custom spinner frame"
        set TaskStatus.Pulse
        equal ".#." custom.Root.Text.Source "custom pulse"
        set (TaskStatus.Determinate 0.5)
        equal "#.. loaded" custom.Root.Text.Source "custom meter caption"
        set TaskStatus.Queued
        equal "Waiting" custom.Root.Text.Source "custom static label"
    finally
        View.dispose custom
