namespace Ranvier.Terminal

open System
open System.Threading
open System.Threading.Tasks
open Ranvier

module SubprocessOutput =
    /// Bind a session to the current owner. Call the returned pump once per host update;
    /// each call delivers at most maxBytes. Readiness wakes the graph, including at EOF.
    /// Replacing the session or disposing the owner detaches its reader and leaves the child running.
    let bind maxBytes (readSession: unit -> SubprocessSession option) receive ended =
        if maxBytes < 1 then invalidArg "maxBytes" "The batch byte limit must be positive."
        let graph = Graph.Current
        let mutable pump = ignore
        createEffect (fun () ->
            match readSession () with
            | None -> ()
            | Some session ->
                let lifetime = new CancellationTokenSource()
                let token = lifetime.Token
                onCleanup (fun () ->
                    pump <- ignore
                    lifetime.Cancel()
                    lifetime.Dispose())
                let arm () =
                    let ready = session.WaitToRead token
                    ready.ContinueWith((fun (_: Task<bool>) -> graph.Dispatch(Action ignore)),
                        token, TaskContinuationOptions.None, TaskScheduler.Default) |> ignore
                    Some ready
                let mutable pending = arm ()
                pump <- fun () ->
                    match pending with
                    | Some ready when ready.IsCompleted ->
                        pending <- None
                        if ready.GetAwaiter().GetResult() then
                            let chunks = session.Drain maxBytes
                            if chunks.Length > 0 then receive chunks
                            if not token.IsCancellationRequested then pending <- arm ()
                        else ended session.Exit
                    | _ -> ())
        fun () -> pump ()
