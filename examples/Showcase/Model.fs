namespace Showcase

open System
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

type Model =
    { Page: Signal<int>
      Clock: Signal<string>
      Phase: Signal<float>
      Count: Signal<int>
      Name: Signal<string>
      Animate: Signal<bool>
      Reveal: Signal<bool>
      Items: Signal<string array>
      Selected: Signal<int>
      Accent: Signal<bool>
      Gap: Signal<int>
      Palette: Signal<int>
      Sync: Signal<bool>
      LastEvent: Signal<string>
      Viewport: Signal<string>
      Metrics: Signal<string>
      Lifecycle: Signal<string>
      mutable Created: int
      mutable Cleaned: int
      mutable Elapsed: float
      mutable ReportAt: float
      mutable NextItem: int
      mutable Redraw: bool
      mutable Exit: bool }

module Model =
    let create () =
        { Page = createSignal 0
          Clock = createSignal ""
          Phase = createSignal 0.
          Count = createSignal 0
          Name = createSignal "Ranvier"
          Animate = createSignal true
          Reveal = createSignal true
          Items = createSignal [| "Signal"; "Memo"; "Effect" |]
          Selected = createSignal 0
          Accent = createSignal false
          Gap = createSignal 1
          Palette = createSignal 0
          Sync = createSignal true
          LastEvent = createSignal "Click a control, type, scroll, or resize the window."
          Viewport = createSignal ""
          Metrics = createSignal "Collecting frame measurements..."
          Lifecycle = createSignal ""
          Created = 0
          Cleaned = 0
          Elapsed = 0.
          ReportAt = 0.
          NextItem = 1
          Redraw = false
          Exit = false }

    let tick seconds model =
        if model.Animate.Peek then
            model.Elapsed <- model.Elapsed + seconds
            model.Phase.Value <- (sin model.Elapsed + 1.) / 2.

        model.Clock.Value <- DateTime.Now.ToString "HH:mm:ss"
        model.Lifecycle.Value <- sprintf "Observed owners: mounted %d  |  cleaned up %d" model.Created model.Cleaned

module Theme =
    let background = Css.rgb 12 18 29
    let surface = Css.rgb 20 29 44
    let accent = Css.rgb 101 224 200
    let muted = Css.rgb 147 166 190
    let bright = Css.rgb 230 239 250

    let styled declarations node =
        node.Style <- Style.merge declarations node.Style
        node

    let line value =
        label value |> styled [ Css.height (Css.cells 1) ]

    let note value =
        label value
        |> styled [ Css.wrap true; Css.color muted; Css.height (Css.cells 3) ]

    let title value =
        line value |> styled [ Css.color accent; Css.attributes Attributes.Bold ]

    let button name caption action =
        Widgets.button [ Css.height (Css.cells 1); Css.backgroundColor surface ] caption action
        |> id name
        |> classes [| "control" |]

    let card heading children =
        panel [ Css.border Rounded; Css.paddingXY 1 0; Css.backgroundColor surface ] {
            title heading
            yield! children
        }

    let rules =
        [| Rule.focus [ Css.backgroundColor (Css.rgb 42 72 92); Css.attributes Attributes.Bold ]
           { Rule.all [ Css.color accent ] with
               Selector =
                   { (Rule.all []).Selector with
                       Class = Some "control"
                       Hovered = Some true } }
           Rule.className "accent" [ Css.color accent ]
           { Rule.all [ Css.attributes Attributes.Underline ] with
               Selector =
                   { (Rule.all []).Selector with
                       Id = Some "id-example" } } |]
