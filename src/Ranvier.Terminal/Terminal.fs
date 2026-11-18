namespace Ranvier.Terminal

open System
open System.Diagnostics
open System.Text
open System.Threading
open Ranvier
open Ranvier.Terminal.View

type TerminalOptions =
    { Capabilities: Capabilities
      FrameInterval: TimeSpan
      Mouse: bool
      AlternateScreen: bool
      OutputCapacityPerCell: int }

type TerminalSession =
    { Output: Output
      Input: NativeInput
      Close: unit -> unit }

module Terminal =
    let defaults =
        { Capabilities =
            { Colors = TrueColor
              SynchronizedOutput = false }
          FrameInterval = TimeSpan.FromMilliseconds 16.
          Mouse = true
          AlternateScreen = true
          OutputCapacityPerCell = 128 }

    let private openSessionWithDeadline options =
        if Console.IsInputRedirected || Console.IsOutputRedirected then
            invalidOp
                "An interactive terminal is required. Use Renderer with an Output record for redirected or embedded rendering."

        let originalEncoding = Console.OutputEncoding
        let input, nextParserWait = Native.openInputWithDeadline ()

        let stream =
            try
                Console.OutputEncoding <- Encoding.UTF8
                Console.OpenStandardOutput()
            with _ ->
                try
                    input.Close()
                finally
                    Console.OutputEncoding <- originalEncoding

                reraise ()

        let output =
            { Write = (fun bytes offset count -> stream.Write(bytes, offset, count))
              Flush = (fun () -> stream.Flush()) }

        let posixMouse = options.Mouse && not (OperatingSystem.IsWindows())
        let startup =
            (if options.AlternateScreen then "\u001b[?1049h" else "")
            + "\u001b[?25l\u001b[?7l\u001b[?2004h\u001b[?1004h"
            + (if posixMouse then
                   "\u001b[?1003h\u001b[?1006h"
               else
                   "")

        let shutdown =
            "\u001b[?2026l\u001b[0m"
            + (if posixMouse then "\u001b[?1003l\u001b[?1006l" else "")
            + "\u001b[?1004l\u001b[?2004l\u001b[?7h\u001b[?25h"
            + (if options.AlternateScreen then "\u001b[?1049l" else "")

        let startBytes = Encoding.UTF8.GetBytes startup
        let stopBytes = Encoding.UTF8.GetBytes shutdown
        let mutable closed = false

        let close () =
            if not closed then
                closed <- true

                try
                    output.Write stopBytes 0 stopBytes.Length
                    output.Flush()
                finally
                    try
                        input.Close()
                    finally
                        try
                            stream.Dispose()
                        finally
                            Console.OutputEncoding <- originalEncoding

        try
            output.Write startBytes 0 startBytes.Length
            output.Flush()

            { Output = output
              Input = input
              Close = close }, nextParserWait
        with _ ->
            try
                close ()
            with _ ->
                ()

            reraise ()

    let openSession options = openSessionWithDeadline options |> fst

    let private runWith reactive scheduled options (cancellation: CancellationToken) setup =
        if options.FrameInterval <= TimeSpan.Zero then
            invalidArg "options" "FrameInterval must be positive."

        if options.OutputCapacityPerCell < 64 then
            invalidArg "options" "OutputCapacityPerCell must be at least 64."

        let mutable wake = Action(ignore)
        let context =
            { new SynchronizationContext() with
                override _.Post(_, _) = Volatile.Read(&wake).Invoke() }
        use graph = new Graph(GraphOptions.Default.WithDispatcher(SynchronizationContextDispatcher context))
        let session, nextParserWait = openSessionWithDeadline options
        try
            let mutable width = max 1 Console.WindowWidth
            let mutable height = max 1 Console.WindowHeight
            let mounted, (viewport, tick) =
                View.mountWithState graph (fun () ->
                    let size = createSignal (struct (width, height))
                    let root, update = setup (fun () -> size.Value)
                    root, (size, update))
            try
                let waiter = Native.createWaiter nextParserWait
                Volatile.Write(&wake, Action waiter.Wake)
                use cancelWake = cancellation.Register(Action waiter.Wake)
                try
                    let capacity () =
                        int (
                            min
                                (int64 Int32.MaxValue)
                                (int64 width * int64 height * int64 options.OutputCapacityPerCell + 64L)
                        )

                    let renderer = Renderer.create width height (capacity ()) options.Capabilities
                    let mutable running = true

                    let refreshViewport () =
                        let nextWidth, nextHeight = max 1 Console.WindowWidth, max 1 Console.WindowHeight
                        if nextWidth <> width || nextHeight <> height then
                            width <- nextWidth
                            height <- nextHeight
                            Renderer.resize width height renderer
                            Renderer.reserve (capacity ()) renderer
                            graph.Run(fun () -> viewport.Value <- struct (width, height))
                            true
                        else false

                    let receive event =
                        match event with
                        | Resize _ ->
                            // Windows input records describe the buffer, which can exceed the window.
                            refreshViewport () |> ignore
                            View.dispatch (Resize(struct (width, height))) mounted |> ignore
                        | Key key when
                            not key.Released
                            && not key.Paste
                            && key.LogicalKey = Ranvier.Keyboard.Character (System.Text.Rune 'c')
                            && key.Modifiers &&& Modifiers.Control <> Modifiers.None
                            ->
                            running <- View.dispatch event mounted
                        | Mouse _ when not options.Mouse -> ()
                        | _ -> View.dispatch event mounted |> ignore

                    let timer = Stopwatch.StartNew()
                    let mutable previous = timer.Elapsed
                    let mutable deadline = TimeSpan.Zero

                    let invokeTick () =
                        let now = timer.Elapsed
                        let next = tick (now - previous)
                        previous <- now
                        deadline <-
                            match next with
                            | Some delay when delay <= TimeSpan.Zero ->
                                invalidArg "tick" "The next tick delay must be positive, or None to sleep."
                            | Some delay ->
                                if delay > TimeSpan.MaxValue - now then TimeSpan.MaxValue else now + delay
                            | None -> TimeSpan.MaxValue

                    while running && not cancellation.IsCancellationRequested do
                        session.Input.Poll receive
                        if refreshViewport () then
                            View.dispatch (Resize(struct (width, height))) mounted |> ignore
                        graph.Pump() |> ignore
                        if running && not cancellation.IsCancellationRequested && (scheduled || timer.Elapsed >= deadline) then
                            graph.Run invokeTick
                        graph.Pump() |> ignore
                        if reactive then
                            Layout.presentReactive session.Output mounted renderer |> ignore
                        else
                            Layout.render mounted renderer.Back
                            Renderer.present session.Output renderer
                        if running && not cancellation.IsCancellationRequested then
                            let remaining = deadline - timer.Elapsed
                            let timeout =
                                if deadline = TimeSpan.MaxValue then -1
                                elif remaining <= TimeSpan.Zero then 0
                                else int (min (float Int32.MaxValue) (Math.Ceiling remaining.TotalMilliseconds))
                            waiter.Wait timeout
                finally
                    waiter.Close()
            finally
                View.dispose mounted
        finally
            session.Close()

    /// Set up the view and update callback together in a mounted owner.
    /// Reading the viewport inside a binding tracks the current console size.
    let runApp options cancellation setup = runWith true true options cancellation setup

    let run options cancellation build tick =
        runWith false false options cancellation (fun _ -> build (), fun delta -> tick delta; Some options.FrameInterval)

    /// Keep pumping Ranvier and ticking animations, but render only invalidated visual state.
    let runReactive options cancellation build tick =
        runWith true false options cancellation (fun _ -> build (), fun delta -> tick delta; Some options.FrameInterval)

    /// Render on input, posted Ranvier work, or the deadline returned by tick.
    /// Tick runs initially and on each wake; return None when no animation needs a deadline.
    /// Native input, cancellation and graph posts still wake a sleeping application.
    let runEventDriven options cancellation build tick = runApp options cancellation (fun _ -> build (), tick)
