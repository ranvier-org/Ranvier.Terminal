namespace Utilities

open System
open System.Collections.Generic
open System.Text
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

type Pane =
    { Buffer: OutputBuffer
      Snapshot: Signal<OutputSnapshot>
      Offset: Signal<int>
      mutable Node: Node option
      mutable Width: int
      mutable Height: int
      mutable Dirty: bool }

type Model =
    { Stream: Pane
      Process: Pane
      Status: Signal<string>
      Revision: Signal<int>
      Mode: SubprocessMode
      Stdout: Decoder
      Stderr: Decoder
      ActiveSession: Signal<SubprocessSession option>
      mutable PendingWrite: Task
      WakeOnCompletion: (Task -> unit) option
      mutable PumpOutput: unit -> unit
      Writes: Queue<byte[]>
      mutable QueuedBytes: int
      mutable PendingBytes: int
      mutable Closing: Task
      mutable Elapsed: float
      mutable NextLine: float
      mutable Received: string
      mutable Completed: bool }
    member this.Session = this.ActiveSession.Peek

module Model =
    let private pane () =
        let buffer = OutputBuffer.create 40 8 400
        let snapshot = createSignal (OutputBuffer.snapshot 0 buffer)
        let offset = createSignal 0
        createEffect (fun () -> snapshot.Value <- OutputBuffer.snapshot offset.Value buffer)
        { Buffer = buffer; Snapshot = snapshot
          Offset = offset; Node = None; Width = 40; Height = 8; Dirty = true }

    let close model =
        model.Writes.Clear()
        model.QueuedBytes <- 0
        match model.Session with
        | Some session ->
            model.Closing <- session.Close()
            model.Closing
        | None -> model.Closing

    let send (bytes: byte[]) (model: Model) =
        match model.Session with
        | Some session when not session.Exit.IsCompleted && bytes.Length <= 65536 - model.QueuedBytes - model.PendingBytes ->
            model.Writes.Enqueue bytes
            model.QueuedBytes <- model.QueuedBytes + bytes.Length
            if model.Writes.Count = 1 then model.WakeOnCompletion |> Option.iter (fun wake -> wake Task.CompletedTask)
            true
        | Some _ ->
            model.Status.Value <- "Input queue full or child exited — input rejected"
            true
        | None -> false

    let sendLine text model =
        let ending = match model.Mode with WindowsPseudoConsole _ | PosixPseudoTerminal _ -> "\r" | Redirected -> "\n"
        send (Encoding.UTF8.GetBytes(text + ending)) model |> ignore

    let start (model: Model) =
        if model.Session.IsNone && model.Closing.IsCompleted then
            try
                model.Stdout.Reset()
                model.Stderr.Reset()
                model.Completed <- false
                model.Received <- ""
                model.Writes.Clear()
                model.QueuedBytes <- 0
                let mode =
                    match model.Mode with
                    | WindowsPseudoConsole _ -> WindowsPseudoConsole(model.Process.Width, model.Process.Height)
                    | PosixPseudoTerminal _ -> PosixPseudoTerminal(model.Process.Width, model.Process.Height)
                    | Redirected -> Redirected
                let options =
                    { Subprocess.command "dotnet" [ typeof<Model>.Assembly.Location; "--child" ] with
                        Mode = mode; OutputCapacity = 65536 }
                model.ActiveSession.Value <- Some(Subprocess.start options CancellationToken.None)
                model.Status.Value <- "Running — type in the process pane or send hello"
            with error -> model.Status.Value <- "Start failed: " + error.Message

    let clear model =
        for pane in [ model.Stream; model.Process ] do
            OutputBuffer.clear pane.Buffer
            pane.Offset.Value <- 0
            pane.Dirty <- true

    let private publish pane =
        if pane.Dirty || pane.Offset.Peek <> pane.Snapshot.Peek.ScrollOffset then
            pane.Dirty <- false
            pane.Snapshot.Value <- OutputBuffer.snapshot pane.Offset.Peek pane.Buffer

    let private resize model pane isProcess =
        let size = match pane.Node with Some node -> OutputView.tryViewportSize node | None -> ValueNone
        match size with
        | ValueSome(struct (width, height)) ->
            if width <> pane.Width || height <> pane.Height then
                pane.Width <- width
                pane.Height <- height
                OutputBuffer.resize width height pane.Buffer
                pane.Dirty <- true
                if isProcess then
                    match model.Mode with
                    | WindowsPseudoConsole _ | PosixPseudoTerminal _ ->
                        model.Session |> Option.iter (fun session ->
                            if not session.Exit.IsCompleted then
                                try session.Resize width height
                                with :? ObjectDisposedException -> ())
                    | Redirected -> ()
        | _ -> ()

    let private literal model decoder (bytes: byte[]) flush =
        let chars = Array.zeroCreate<char> (Encoding.UTF8.GetMaxCharCount bytes.Length)
        let count = (decoder: Decoder).GetChars(bytes, 0, bytes.Length, chars, 0, flush)
        let text = String(chars, 0, count)
        OutputBuffer.feedText text model.Process.Buffer
        model.Received <- model.Received + text
        if model.Received.Length > 16384 then model.Received <- model.Received.Substring(model.Received.Length - 16384)

    let createWithWake wake mode =
        let model =
            { Stream = pane (); Process = pane (); Status = createSignal "Ready — start the child process"
              Revision = createSignal 0; Mode = mode; Stdout = Encoding.UTF8.GetDecoder()
              Stderr = Encoding.UTF8.GetDecoder(); ActiveSession = createSignal None; PendingWrite = Task.CompletedTask
              WakeOnCompletion = wake; PumpOutput = ignore
              Writes = System.Collections.Generic.Queue<byte[]>(); QueuedBytes = 0; PendingBytes = 0
              Closing = Task.CompletedTask; Elapsed = 0.; NextLine = 0.; Received = ""; Completed = false }
        let receive (chunks: SubprocessChunk array) =
            for chunk in chunks do
                match chunk.Kind with
                | TerminalOutput ->
                    OutputBuffer.feedUtf8 chunk.Bytes 0 chunk.Bytes.Length model.Process.Buffer
                    // This bounded transcript is only a smoke-test marker, not the terminal decoder.
                    model.Received <- model.Received + Encoding.UTF8.GetString chunk.Bytes
                    if model.Received.Length > 16384 then model.Received <- model.Received.Substring(model.Received.Length - 16384)
                | StandardOutput -> literal model model.Stdout chunk.Bytes false
                | StandardError -> literal model model.Stderr chunk.Bytes false
            model.Process.Dirty <- true
        let ended (exit: Task<int>) =
            literal model model.Stdout [||] true
            literal model model.Stderr [||] true
            OutputBuffer.finish model.Process.Buffer
            model.Process.Dirty <- true
            model.Completed <- true
            model.Status.Value <-
                if exit.IsFaulted then "Process failed: " + exit.Exception.GetBaseException().Message
                else sprintf "Process exited: %d" exit.Result
            close model |> ignore
            model.ActiveSession.Value <- None
        model.PumpOutput <- SubprocessOutput.bind 65536 (fun () -> model.ActiveSession.Value) receive ended
        model

    let create mode = createWithWake None mode

    let tick seconds model =
        if model.PendingWrite.IsCompleted then model.PendingBytes <- 0
        resize model model.Stream false
        resize model model.Process true
        model.Elapsed <- model.Elapsed + seconds
        if model.Elapsed >= model.NextLine then
            model.NextLine <- model.Elapsed + 0.25
            let text = sprintf "\r\u001b[36mLive %s  progress %3d%%\u001b[0m" (DateTime.Now.ToString "HH:mm:ss") (int (model.Elapsed * 10.) % 101)
            let bytes = Encoding.UTF8.GetBytes text
            OutputBuffer.feedUtf8 bytes 0 bytes.Length model.Stream.Buffer
            if int (model.Elapsed * 4.) % 4 = 0 then OutputBuffer.feedText "\nStreaming continues • 界 • é\n" model.Stream.Buffer
            model.Stream.Dirty <- true
        model.PumpOutput ()
        if model.Session.IsSome && model.PendingWrite.IsCompleted && model.QueuedBytes < 32768 then
            let responses = OutputBuffer.drainResponses model.Process.Buffer
            if responses.Length > 0 then send responses model |> ignore
        if model.PendingWrite.IsFaulted then
            model.Status.Value <- "Input failed: " + model.PendingWrite.Exception.GetBaseException().Message
            model.PendingWrite <- Task.CompletedTask
        if model.PendingWrite.IsCompleted then
            model.PendingBytes <- 0
            match model.Session with
            | Some session when not session.Exit.IsCompleted && model.Writes.Count > 0 ->
                let bytes = model.Writes.Dequeue()
                model.QueuedBytes <- model.QueuedBytes - bytes.Length
                model.PendingBytes <- bytes.Length
                model.PendingWrite <- session.Write bytes CancellationToken.None
                model.WakeOnCompletion |> Option.iter (fun wake -> wake model.PendingWrite)
            | _ -> ()
        if model.Closing.IsFaulted then
            model.Status.Value <- "Close failed: " + model.Closing.Exception.GetBaseException().Message
        publish model.Stream
        publish model.Process
