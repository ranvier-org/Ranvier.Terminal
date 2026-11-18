namespace Ranvier.Terminal.Components

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View

module Spinner =
    type Style = Braille | Line | Quadrants | BouncingBar | Points | Shading | Moon

    let frames = function
        | Braille -> Array.copy TaskStatus.defaults.SpinnerFrames
        | Line -> [| "|"; "/"; "-"; "\\" |]
        | Quadrants -> [| "▘"; "▝"; "▗"; "▖" |]
        | BouncingBar -> [| "▁"; "▃"; "▅"; "▇"; "▅"; "▃" |]
        | Points -> [| "●··"; "·●·"; "··●"; "·●·" |]
        | Shading -> [| "░"; "▒"; "▓"; "█"; "▓"; "▒" |]
        | Moon -> [| "🌑"; "🌒"; "🌓"; "🌔"; "🌕"; "🌖"; "🌗"; "🌘" |]

    let create style declarations label tick =
        let presentation = { TaskStatus.defaults with SpinnerFrames = frames style; RunningLabel = label }
        TaskStatus.createWith presentation declarations (fun _ -> []) 0 tick (fun () -> TaskStatus.Spinner)

module Pulse =
    type Style = Bounce | Sweep | BouncingSegment

    let create style declarations width label (tick: unit -> int) =
        let presentation = { TaskStatus.defaults with RunningLabel = label }
        if style = Bounce || width <= 1 then
            TaskStatus.createWith presentation declarations (fun _ -> []) width tick (fun () -> TaskStatus.Pulse)
        else
            let node = Node.create declarations [||]
            let span = if style = Sweep then 1 else min 3 (width - 1)
            let travel = int64 (width - span)
            let period = if style = Sweep then int64 width else travel * 2L
            let mutable previous = -1
            createEffect (fun () ->
                let remainder = int64 (tick ()) % period
                let phase = if remainder < 0L then remainder + period else remainder
                let position = int (if style = Sweep then phase else travel - abs (phase - travel))
                if position <> previous then
                    let bar = String(presentation.TrackGlyph, position) + String(presentation.FilledGlyph, span) + String(presentation.TrackGlyph, width - position - span)
                    Node.setText (if String.IsNullOrEmpty label then bar else bar + " " + label) node
                    previous <- position)
            node

module Meter =
    type Style = HalfBlock | Solid | ThinLine | HeavyLine | DoubleLine | Segmented | Dots | Ascii

    let create style declarations width fill track value =
        let filled, empty =
            match style with
            | HalfBlock -> '▄', '▄'
            | Solid -> '█', '░'
            | ThinLine -> '─', '─'
            | HeavyLine -> '━', '━'
            | DoubleLine -> '═', '═'
            | Segmented -> '▮', '▯'
            | Dots -> '●', '·'
            | Ascii -> '#', '-'
        Styled.meterWith filled empty declarations width fill track value

    /// A block meter with eighth-cell precision. The caller supplies the fill color at each position.
    let fractional declarations width (colorAt: float -> Color) track value =
        let width = max 0 width
        let levels = [| for glyph in " ▏▎▍▌▋▊▉█" -> TextRun.prepare (string glyph) |]
        let cells =
            Array.init width (fun i ->
                let position = float i / float (max 1 (width - 1))
                let cell = Node.create [ Width(Cells 1); Foreground(colorAt position); Background track ] [||]
                Node.setPreparedText levels[0] cell
                cell)
        let mutable previous = 0L
        createEffect (fun () ->
            let fraction = value ()
            let units = if Double.IsNaN fraction then 0L else int64 (Math.Clamp(fraction, 0., 1.) * float width * 8.)
            if units <> previous then
                let first = int (min units previous / 8L)
                let last = int ((max units previous - 1L) / 8L)
                for i = first to last do
                    let level = int (Math.Clamp(units - int64 i * 8L, 0L, 8L))
                    Node.setPreparedText levels[level] cells[i]
                previous <- units)
        Node.create (declarations @ [ Direction Row; Width(Cells width); Height(Cells 1) ]) cells
