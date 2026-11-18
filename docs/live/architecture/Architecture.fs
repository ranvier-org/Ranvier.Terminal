module TerminalDocs.Architecture

open Fable.Core
open Ranvier
open Ranvier.D5
open Ranvier.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dsl

type Change = Idle | Paint | Geometry

let private modes = [ Paint, "Paint only"; Geometry, "Geometry"; Idle, "Idle" ]

let private explanation = function
    | Paint -> "A color binding marks its node for paint. The renderer keeps its bounds and repaints the affected rows in the back framebuffer."
    | Geometry -> "A geometry change marks the layout dirty. Layout remeasures affected nodes, arranges them and repaints the rows touched by the change."
    | Idle -> "With a valid front snapshot and a clean tree, presentation returns immediately. It skips layout, painting, cell comparisons and output."

type Stage = Present | Clean | Color | Size | Sleep | Rows | Measure | PaintRows | Encode | Commit

let private caption = function
    | Present -> "Present frame"
    | Clean -> "Clean tree"
    | Color -> "Paint dirty"
    | Size -> "Layout dirty"
    | Sleep -> "Return"
    | Rows -> "Find rows"
    | Measure -> "Lay out"
    | PaintRows -> "Repaint rows"
    | Encode -> "Diff cells"
    | Commit -> "Write + commit"

let private active mode = function
    | Present -> true
    | Clean | Sleep -> mode = Idle
    | Color | Rows -> mode = Paint
    | Size | Measure -> mode = Geometry
    | PaintRows | Encode | Commit -> mode <> Idle

// The first half branches by damage type. Both dirty branches join the same presentation path.
let private branches =
    let leaf stage = Hierarchy.node stage 1. []
    Hierarchy.node Present 1.
        [ Hierarchy.node Clean 1. [leaf Sleep]
          Hierarchy.node Color 1. [leaf Rows]
          Hierarchy.node Size 1. [leaf Measure] ]

let private points, links =
    let layout =
        match Tree.layout (TreeConfig.create (Bounds.create 0. 27. 360. 116.)) branches with
        | Ok items -> items
        | Error errors -> failwithf "Invalid frame diagram: %A" errors
    let positions = layout |> List.map (fun node -> node.Id, node.Point) |> Map.ofList
    let center = (positions[Rows].X + positions[Measure].X) / 2.
    let common = [ PaintRows, Point.create center 201.; Encode, Point.create center 249.; Commit, Point.create center 297. ]
    let edges =
        layout |> List.choose (fun node -> node.Parent |> Option.map (fun parent -> parent, node.Id))
    Map.ofList (Map.toList positions @ common),
    edges @ [ Rows, PaintRows; Measure, PaintRows; PaintRows, Encode; Encode, Commit ]

type Diagram =
    { Mode: Signal<Change> }

    static member Create(_: Setup, _: unit) = { Mode = createSignal Paint }

    static member View =
        div (div.className "frame-explorer") {
            div (div.className "frame-controls", div.attribute ("role", "group"), div.aria ("label", "Frame change")) {
                for mode, label in modes do
                    button (
                        button.attribute ("type", "button"),
                        button.aria ("pressed", fun (state: Diagram) -> Some (if state.Mode.Value = mode then "true" else "false")),
                        button.onClick (fun state _ -> state.Mode.Value <- mode)
                    ) { label }
            }
            p (p.className "frame-explanation", p.aria ("live", "polite")) {
                fun (state: Diagram) -> explanation state.Mode.Value
            }
            svg (svg.attribute ("viewBox", "0 0 360 325"), svg.attribute ("role", "img"), svg.aria ("label", fun (state: Diagram) ->
                    Some (match state.Mode.Value with
                          | Idle -> "Clean tree: return from presentation."
                          | Paint -> "Paint dirty: find and repaint rows, diff cells, write changes and commit."
                          | Geometry -> "Layout dirty: lay out, repaint rows, diff cells, write changes and commit."))) {
                for source, target in links do
                    let a, b = points[source], points[target]
                    let middle = (a.Y + b.Y) / 2.
                    path (path.attribute ("d", $"M {a.X} {a.Y + 14.} C {a.X} {middle} {b.X} {middle} {b.X} {b.Y - 14.}"),
                          path.attribute ("class", fun (state: Diagram) ->
                              Some (if active state.Mode.Value source && active state.Mode.Value target then "flow-edge selected" else "flow-edge"))) { () }
                for KeyValue(stage, point) in points do
                    g (g.attribute ("transform", $"translate({point.X} {point.Y})"),
                       g.attribute ("class", fun (state: Diagram) -> Some (if active state.Mode.Value stage then "flow-stage selected" else "flow-stage"))) {
                        rect (rect.attribute ("x", "-52"), rect.attribute ("y", "-14"), rect.attribute ("width", "104"),
                              rect.attribute ("height", "28"), rect.attribute ("rx", "5")) { () }
                        text' (text'.attribute ("text-anchor", "middle"), text'.attribute ("y", "4")) { caption stage }
                    }
            }
        }

[<Global("document")>]
let private document: Ranvier.Dom.Document = jsNative

let private host = document.getElementById "architecture-flow"

if not (isNull host) then
    let diagram = Component.ofMembers<Diagram, unit>()
    let graph = new Graph()
    Mounting.mount graph None (fun errors -> errors |> List.iter (fun error -> JS.console.error error)) host None
        (fun context -> Program.run (Component.render diagram ()) context.Backend)
    |> ignore
