namespace Utilities

open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

module Pages =
    let utilities model =
        let declarations = [ Css.height (Css.flex 1); Css.width (Css.flex 1); Css.border Rounded ]
        let stream = OutputView.create declarations model.Stream.Snapshot model.Stream.Offset
        let processPane =
            OutputView.interactive declarations model.Process.Snapshot model.Process.Offset (fun event ->
                let bytes = TerminalKeys.encode model.Process.Snapshot.Peek.BracketedPaste event
                bytes.Length > 0 && Model.send bytes model)
        model.Stream.Node <- Some stream
        model.Process.Node <- Some processPane
        column [ Css.height (Css.flex 1); Css.gap 1 ] {
            row [ Css.height (Css.cells 1); Css.gap 2 ] {
                Widgets.button [] "[ Start ]" (fun () -> Model.start model) |> id "start"
                Widgets.button [] "[ Send hello ]" (fun () -> Model.sendLine "hello" model) |> id "hello"
                Widgets.button [] "[ Stop ]" (fun () -> Model.close model |> ignore) |> id "stop"
                Widgets.button [] "[ Clear ]" (fun () -> Model.clear model) |> id "clear"
            }
            signalText model.Status
            "Live stream                                     Child process"
            row [ Css.height (Css.flex 1); Css.gap 1 ] {
                stream
                processPane
            }
        }
