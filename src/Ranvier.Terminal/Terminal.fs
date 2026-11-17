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

    let openSession options =
        if Console.IsInputRedirected || Console.IsOutputRedirected then
            invalidOp
                "An interactive terminal is required. Use Renderer with an Output record for redirected or embedded rendering."

        let originalEncoding = Console.OutputEncoding
        let input = Native.openInput ()

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

        let startup =
            (if options.AlternateScreen then "\u001b[?1049h" else "")
            + "\u001b[?25l\u001b[?7l\u001b[?2004h\u001b[?1004h"
            + (if options.Mouse && not (OperatingSystem.IsWindows()) then
                   "\u001b[?1002h\u001b[?1006h"
               else
                   "")

        let shutdown =
            "\u001b[?2026l\u001b[0m\u001b[?1002l\u001b[?1006l\u001b[?1004l\u001b[?2004l\u001b[?7h\u001b[?25h"
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
              Close = close }
        with _ ->
            try
                close ()
            with _ ->
                ()

            reraise ()

    let run options (cancellation: CancellationToken) build tick =
        if options.FrameInterval <= TimeSpan.Zero then
            invalidArg "options" "FrameInterval must be positive."

        if options.OutputCapacityPerCell < 64 then
            invalidArg "options" "OutputCapacityPerCell must be at least 64."

        use graph = new Graph()
        let mounted = View.mount graph build

        try
            let session = openSession options

            try
                let mutable width = max 1 Console.WindowWidth
                let mutable height = max 1 Console.WindowHeight

                let capacity () =
                    int (
                        min
                            (int64 Int32.MaxValue)
                            (int64 width * int64 height * int64 options.OutputCapacityPerCell + 64L)
                    )

                let renderer = Renderer.create width height (capacity ()) options.Capabilities
                let mutable running = true

                let receive event =
                    match event with
                    | Key key when
                        not key.Released
                        && not key.Paste
                        && key.Key = Key.Character
                        && key.Scalar = int 'c'
                        && key.Modifiers &&& Modifiers.Control <> Modifiers.None
                        ->
                        running <- false
                    | Mouse _ when not options.Mouse -> ()
                    | _ -> View.dispatch event mounted |> ignore

                let timer = Stopwatch.StartNew()
                let mutable previous = timer.Elapsed

                let invokeTick () =
                    let now = timer.Elapsed
                    tick (now - previous)
                    previous <- now

                while running && not cancellation.IsCancellationRequested do
                    let start = timer.Elapsed
                    session.Input.Poll receive
                    graph.Run invokeTick
                    graph.Pump() |> ignore
                    let nextWidth, nextHeight = max 1 Console.WindowWidth, max 1 Console.WindowHeight

                    if nextWidth <> width || nextHeight <> height then
                        width <- nextWidth
                        height <- nextHeight
                        Renderer.resize width height renderer
                        Renderer.reserve (capacity ()) renderer
                        receive (Resize(struct (width, height)))

                    Layout.render mounted renderer.Back
                    Renderer.present session.Output renderer
                    let remaining = options.FrameInterval - (timer.Elapsed - start)

                    if remaining > TimeSpan.Zero then
                        Thread.Sleep remaining
            finally
                session.Close()
        finally
            View.dispose mounted
