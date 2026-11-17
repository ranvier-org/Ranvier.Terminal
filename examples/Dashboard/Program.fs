module Dashboard

open System
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

[<EntryPoint>]
let main args =
    let mutable clock = Unchecked.defaultof<Signal<string>>
    let mutable progress = Unchecked.defaultof<Signal<float>>
    let mutable elapsed = 0.

    let build () =
        clock <- createSignal (DateTime.Now.ToString "HH:mm:ss")
        progress <- createSignal 0.
        let count = createSignal 0
        let name = createSignal "Ranvier"
        let enabled = createSignal true

        column (
            styles {
                Css.padding 1
                Css.gap 1
                Css.color (Css.rgb 210 225 245)
            }
        ) {
            row [ Css.height (Css.cells 1); Css.gap 3 ] {
                "RANVIER / TERMINAL"
                signalText clock
            }

            row [ Css.height (Css.flex 1); Css.gap 2 ] {
                panel [ Css.width (Css.flex 2); Css.border Rounded; Css.padding 1; Css.gap 1 ] {
                    "Native F# • retained UI • framebuffer diffing"
                    text (fun () -> "Hello, " + name.Value)
                    Widgets.input [ Css.height (Css.cells 1); Css.backgroundColor (Css.rgb 30 42 60) ] name

                    Widgets.button [ Css.height (Css.cells 1) ] "[ Increment ]" (fun () ->
                        count.Value <- count.Value + 1)

                    text (fun () -> sprintf "Count: %d" count.Value)
                    Widgets.checkbox [ Css.height (Css.cells 1) ] "Animate" enabled
                    Widgets.progress [] 24 (fun () -> if enabled.Value then progress.Value else 0.)
                }

                panel [ Css.width (Css.flex 1); Css.border Single; Css.padding 1 ] {
                    "Tab / Shift+Tab: focus"
                    "Enter / Space: activate"
                    "Mouse: click / scroll"
                    "Ctrl+C: exit"
                    "Unicode: 界 é 👩‍💻"
                }
            }

            "Only changed terminal cells are overwritten."
        }

    let tick (delta: TimeSpan) =
        elapsed <- elapsed + delta.TotalSeconds
        clock.Value <- DateTime.Now.ToString "HH:mm:ss"
        progress.Value <- (sin elapsed + 1.) / 2.

    if Array.contains "--headless" args then
        use graph = new Graph()
        let mounted = View.mount graph build
        let renderer = Renderer.create 100 24 400000 Terminal.defaults.Capabilities
        let mutable bytes = 0L

        let output =
            { Write = (fun _ _ count -> bytes <- bytes + int64 count)
              Flush = (fun () -> ()) }

        for _ = 1 to 120 do
            graph.Run(fun () -> tick (TimeSpan.FromSeconds(1. / 60.)))
            Layout.render mounted renderer.Back
            Renderer.present output renderer

        View.dispose mounted
        printfn "measured here: headless live dashboard: 120 frames, %d encoded bytes" bytes
    else
        use stop = new CancellationTokenSource()

        if Array.contains "--smoke" args then
            stop.CancelAfter(TimeSpan.FromSeconds 2.)

        let options =
            { Terminal.defaults with
                Capabilities =
                    { Terminal.defaults.Capabilities with
                        SynchronizedOutput = Array.contains "--sync" args } }

        Terminal.run options stop.Token build tick

    0
