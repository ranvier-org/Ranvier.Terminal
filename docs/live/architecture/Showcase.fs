module TerminalDocs.Showcase

open Fable.Core
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dsl

type Scene =
    { width: int; height: int; checkbox: int; x: int; w: int
      frames: int array array; rows: string array array; palette: string array; states: int array }

[<Global("ranvierShowcase")>]
let private scene: Scene = jsNative

[<Emit("window.matchMedia('(prefers-reduced-motion: reduce)').matches")>]
let private reduceMotion () : bool = jsNative

[<Emit("window.setInterval($0, 98)")>]
let private startClock (tick: unit -> unit) : int = jsNative

[<Emit("window.clearInterval($0)")>]
let private stopClock (token: int) : unit = jsNative

type Preview =
    { Running: Signal<bool>; Tick: Signal<int>; Focused: Signal<bool>; Hover: Signal<bool> }
    static member Create(_: Setup, _: unit) =
        let state = { Running = createSignal (not (reduceMotion ())); Tick = createSignal 0; Focused = createSignal false; Hover = createSignal false }
        createEffect (fun () ->
            if state.Running.Value then
                let timer = startClock (fun () -> state.Tick.Value <- (state.Tick.Peek + 1) % scene.frames.Length)
                onCleanup (fun () -> stopClock timer))
        state
    static member View =
        let checkboxState (state: Preview) =
            (if state.Running.Value then 0 else 4) + (if state.Focused.Value then 2 else 0) + (if state.Hover.Value then 1 else 0)
        let rowAt y (state: Preview) =
            let id = if y = scene.checkbox then scene.states[checkboxState state] else scene.frames[state.Tick.Value][y]
            scene.rows[id]
        let variants y =
            (if y = scene.checkbox then scene.states else scene.frames |> Array.map (fun f -> f[y]))
            |> Array.distinct |> Array.map (fun i -> scene.rows[i])
        div (div.className "native-screen") {
            for y in 0 .. scene.height-1 do
                div (div.className "native-row") {
                    let snapshots = variants y
                    for x in 0 .. scene.width-1 do
                        let paint (row: string array) = scene.palette[int (row[1].[x]) - 65]
                        let glyph (row: string array) = string row[0].[x]
                        let first = snapshots[0]
                        let fixedPaint = snapshots |> Array.forall (fun r -> r[1].[x] = first[1].[x])
                        let fixedGlyph = snapshots |> Array.forall (fun r -> r[0].[x] = first[0].[x])
                        span (if fixedPaint then span.attribute ("style", paint first)
                              else span.attribute ("style", fun state -> Some (paint (rowAt y state)))) {
                            if fixedGlyph then glyph first
                            else fun state -> glyph (rowAt y state)
                        }
                }
            button (button.className "native-checkbox", button.attribute ("type","button"),
                    button.attribute ("style", sprintf "left:%dch;top:%dem;width:%dch" scene.x scene.checkbox scene.w),
                    button.attribute ("role","checkbox"), button.aria ("label","Animate examples"),
                    button.aria ("checked", fun state -> Some (if state.Running.Value then "true" else "false")),
                    button.onFocus (fun state _ -> state.Focused.Value <- true),
                    button.onBlur (fun state _ -> state.Focused.Value <- false),
                    button.onPointerenter (fun state _ -> state.Hover.Value <- true),
                    button.onPointerleave (fun state _ -> state.Hover.Value <- false),
                    button.onClick (fun state _ -> state.Running.Value <- not state.Running.Value)) { () }
        }

[<Global("document")>]
let private document: Ranvier.Dom.Document = jsNative

let private host = document.getElementById "showcase-live"
if not (isNull host) then
    let graph = new Graph()
    Mounting.mount graph None (fun errors -> errors |> List.iter (fun error -> JS.console.error error)) host None
        (fun context -> Program.run (Component.render (Component.ofMembers<Preview, unit>()) ()) context.Backend)
    |> ignore
