namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

module TaskStatus =
    type State =
        | Queued
        | Spinner
        | Pulse
        | Determinate of fraction: float
        | Success
        | Failure
        | Cancelled

    type Presentation =
        { SpinnerFrames: string array
          RunningLabel: string
          FilledGlyph: char
          TrackGlyph: char
          ProgressLabel: float -> string
          QueuedText: string
          SuccessText: string
          FailureText: string
          CancelledText: string }

    let defaults =
        { SpinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]
          RunningLabel = "Running"
          FilledGlyph = '█'
          TrackGlyph = '░'
          ProgressLabel = fun fraction -> sprintf "%d%%" (int (fraction * 100.))
          QueuedText = "· Queued"
          SuccessText = "✓ Success"
          FailureText = "× Failure"
          CancelledText = "– Cancelled" }

    let private withLabel indicator label =
        if String.IsNullOrEmpty indicator then label
        elif String.IsNullOrEmpty label then indicator
        else indicator + " " + label

    let private phase tick period =
        let remainder = int64 tick % period
        if remainder < 0L then remainder + period else remainder

    /// Width is the number of meter cells. Only the spinner and moving pulse read the host clock.
    let createWith presentation declarations (style: State -> Declaration list) width (tick: unit -> int) state =
        if isNull presentation.SpinnerFrames || presentation.SpinnerFrames.Length = 0 then
            invalidArg "presentation" "A spinner needs at least one frame."
        Styled.checkGlyph "FilledGlyph" presentation.FilledGlyph
        Styled.checkGlyph "TrackGlyph" presentation.TrackGlyph
        let frames =
            presentation.SpinnerFrames |> Array.map (fun glyph -> TextRun.prepare (withLabel glyph presentation.RunningLabel))
        let width = max 0 width
        let node = Node.create declarations [||]
        createEffect (fun () -> Node.setStyle (declarations @ style (state ())) node)
        createEffect (fun () ->
            match state () with
            | Queued -> Node.setText presentation.QueuedText node
            | Success -> Node.setText presentation.SuccessText node
            | Failure -> Node.setText presentation.FailureText node
            | Cancelled -> Node.setText presentation.CancelledText node
            | Spinner when frames.Length = 1 -> Node.setPreparedText frames[0] node
            | Spinner ->
                let index = int (phase (tick ()) (int64 frames.Length))
                Node.setPreparedText frames[index] node
            | Pulse when width <= 1 -> Node.setText (withLabel (String(presentation.FilledGlyph, width)) presentation.RunningLabel) node
            | Pulse ->
                let endCell = int64 width - 1L
                let position = int (endCell - abs (phase (tick ()) (2L * endCell) - endCell))
                let bar = String(presentation.TrackGlyph, position) + string presentation.FilledGlyph + String(presentation.TrackGlyph, width - position - 1)
                Node.setText (withLabel bar presentation.RunningLabel) node
            | Determinate fraction ->
                let fraction = if Double.IsNaN fraction then 0. else Math.Clamp(fraction, 0., 1.)
                let bar = Widgets.progressTextWith presentation.FilledGlyph presentation.TrackGlyph width fraction
                Node.setText (withLabel bar (presentation.ProgressLabel fraction)) node)
        node

    let create declarations style width tick state = createWith defaults declarations style width tick state
