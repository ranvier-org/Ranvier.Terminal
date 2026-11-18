module HostParserProbe

open Ranvier.Terminal

let require condition message = if not condition then failwith message

[<EntryPoint>]
let main _ =
    let mutable now = 1000L
    let events = ResizeArray<InputEvent>()
    let parser = Native.createParserInput (fun () -> now) events.Add
    let wait requested = Native.parserTimeout requested parser.NextWait
    require (wait -1 = -1) "idle parser should permit indefinite wait"

    // A graph/resize/deadline wake has already returned. ESC arrives before owner Poll.
    // Feeding it must arm a deadline even though the prior wait did not report stdin.
    parser.Feed [| 27uy |] 1
    require (wait -1 = 30) "ESC after a non-input wake must interrupt indefinite sleep"
    require (wait 8 = 8) "earlier animation deadline must win"
    now <- now + 29L
    parser.PollEscape()
    require (events.Count = 0 && wait -1 = 1) "ESC must not flush early"
    now <- now + 1L
    require (wait -1 = 0) "due ESC must never block"
    parser.PollEscape()
    match events.ToArray() with
    | [| Key key |] when key.Key = Key.Escape -> ()
    | _ -> failwith "bare ESC must flush once"
    require (wait -1 = -1) "flushed parser should sleep again"
    parser.PollEscape()
    require (events.Count = 1) "repeated polls must not emit ESC again"

    events.Clear()
    parser.Feed [| 27uy |] 1
    now <- now + 5L
    parser.Feed [| byte '['; byte 'A' |] 2
    require (wait -1 = -1) "completed split arrow must clear escape deadline"
    match events.ToArray() with
    | [| Key key |] when key.Key = Key.Up -> ()
    | _ -> failwith "split arrow decoding"
    now <- now + 100L
    parser.PollEscape()
    require (events.Count = 1) "completed arrow must not emit a later Escape"

    parser.Feed [| 27uy |] 1
    now <- now + 20L
    parser.Feed [||] 0
    require (wait -1 = 10) "empty reads must not postpone Escape"
    now <- now + 100L
    require (wait -1 = 0) "late deadline must clamp to immediate poll"
    printfn "PASS actual parser deadlines after non-input wakes, split input, early/late deadlines, empty reads and one-shot Escape"
    0
