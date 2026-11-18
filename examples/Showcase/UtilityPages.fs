namespace Showcase

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl
open Theme

module UtilityPages =
    let private owned (model: Model) transport =
        let utility = Utilities.Model.create transport
        model.Utility <- Some utility
        onCleanup (fun () ->
            model.Closings.Add(Utilities.Model.close utility)
            utility.PendingWrite.ContinueWith(fun (work: System.Threading.Tasks.Task) ->
                if work.IsFaulted then work.Exception |> ignore) |> ignore
            if model.Utility |> Option.exists (fun current -> obj.ReferenceEquals(current, utility)) then
                model.Utility <- None)
        utility

    let private declarations =
        [ Css.height (Css.flex 1); Css.width (Css.flex 1); Css.border Rounded ]

    let streaming (model: Model) =
        let utility = owned model Redirected
        let output = OutputView.create declarations utility.Stream.Snapshot utility.Stream.Offset |> id "stream-output"
        utility.Stream.Node <- Some output
        column [ Css.height (Css.flex 1); Css.gap 1 ] {
            title "Streaming output"
            note "Live clock, colored progress and Unicode arrive continuously. Scroll back while new output arrives; history stays anchored."
            row [ Css.height (Css.cells 1); Css.gap 2 ] {
                button "stream-clear" " Clear " (fun () -> Utilities.Model.clear utility)
                button "stream-history" " Add history " (fun () ->
                    for index = 1 to 60 do
                        OutputBuffer.feedText (sprintf "History entry %d • 界 • é\n" index) utility.Stream.Buffer
                    utility.Stream.Dirty <- true)
                button "stream-tail" " Follow live " (fun () -> utility.Stream.Offset.Value <- 0)
            }
            output
            line "Wheel / PageUp / PageDown: history   End: follow live"
        }

    let terminal (model: Model) =
        let transport = model.ProcessMode
        let utility = owned model transport
        let output =
            OutputView.interactive declarations utility.Process.Snapshot utility.Process.Offset (fun event ->
                let bytes = TerminalKeys.encode utility.Process.Snapshot.Peek.BracketedPaste event
                bytes.Length > 0 && Utilities.Model.send bytes utility)
            |> id "process-output"
        utility.Process.Node <- Some output
        let name = match transport with WindowsPseudoConsole _ -> "Windows ConPTY" | PosixPseudoTerminal _ -> "POSIX PTY" | Redirected -> "Redirected streams"
        column [ Css.height (Css.flex 1); Css.gap 1 ] {
            title ("Subprocess terminal — " + name)
            note "Start the echo child, then click its frame and type. Try size or exit. Resizing the showcase resizes the child terminal. Leaving this page closes the child."
            row [ Css.height (Css.cells 1); Css.gap 1 ] {
                button "process-start" " Start " (fun () -> Utilities.Model.start utility)
                button "process-hello" " Send hello " (fun () -> Utilities.Model.sendLine "hello" utility)
                button "process-size" " Report size " (fun () -> Utilities.Model.sendLine "size" utility)
                button "process-stop" " Stop " (fun () -> Utilities.Model.close utility |> ignore)
                button "process-clear" " Clear " (fun () -> Utilities.Model.clear utility)
            }
            signalText utility.Status |> styled [ Css.height (Css.cells 1) ]
            output
            note "Ctrl+Tab: leave frame   Shift+PageUp: history   Ctrl+C in frame: interrupt child; elsewhere: exit showcase"
        }
