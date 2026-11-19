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
      Planet: Signal<string>
      Destination: Signal<string option>
      Payloads: Signal<Set<string>>
      Animate: Signal<bool>
      Reveal: Signal<bool>
      Items: Signal<string array>
      Selected: Signal<int>
      InstrumentPosition: Signal<struct (int * int)>
      Accent: Signal<bool>
      Gap: Signal<int>
      Palette: Signal<int>
      Sync: Signal<bool>
      LastEvent: Signal<string>
      LogRows: Signal<int>
      LogHeight: Signal<int>
      Viewport: Signal<string>
      Metrics: Signal<string>
      Lifecycle: Signal<string>
      mutable Created: int
      mutable Cleaned: int
      Elapsed: Signal<float>
      TaskStart: Signal<float option>
      TaskStop: Signal<(float * TaskStatus.State) option>
      Columns: Signal<int>
      mutable ReportAt: float
      mutable NextItem: int
      mutable Redraw: bool
      mutable Utility: Utilities.Model option
      mutable ProcessMode: SubprocessMode
      Closings: ResizeArray<System.Threading.Tasks.Task>
      mutable Exit: bool }

module Model =
    let create () =
        { Page = createSignal 0
          Clock = createSignal ""
          Phase = createSignal 0.
          Count = createSignal 0
          Name = createSignal "Ranvier"
          Planet = createSignal ""
          Destination = createSignal (Some "orbit")
          Payloads = createSignal (Set.singleton "camera")
          Animate = createSignal true
          Reveal = createSignal true
          Items = createSignal [| "Signal"; "Memo"; "Effect" |]
          Selected = createSignal 0
          InstrumentPosition = createSignal (struct (0, 0))
          Accent = createSignal false
          Gap = createSignal 1
          Palette = createSignal 0
          Sync = createSignal true
          LastEvent = createSignal "Click a control, type, scroll, or resize the window."
          LogRows = createSignal 24
          LogHeight = createSignal 9
          Viewport = createSignal ""
          Metrics = createSignal "Collecting frame measurements..."
          Lifecycle = createSignal ""
          Created = 0
          Cleaned = 0
          Elapsed = createSignal 0.
          TaskStart = createSignal None
          TaskStop = createSignal None
          Columns = createSignal 80
          ReportAt = 0.
          NextItem = 1
          Redraw = false
          Utility = None
          ProcessMode = Subprocess.tryPseudoTerminal 40 8 |> Option.defaultValue Redirected
          Closings = ResizeArray()
          Exit = false }

    let tick seconds model =
        model.Utility |> Option.iter (Utilities.Model.tick seconds)
        for index = model.Closings.Count - 1 downto 0 do
            let closing = model.Closings[index]
            if closing.IsCompleted then
                if closing.IsFaulted then model.LastEvent.Value <- "Process cleanup failed: " + closing.Exception.GetBaseException().Message
                model.Closings.RemoveAt index
        if model.Animate.Peek then
            let elapsed = model.Elapsed.Peek + seconds
            model.Elapsed.Value <- elapsed
            model.Phase.Value <- (sin elapsed + 1.) / 2.

        model.Clock.Value <- DateTime.Now.ToString "HH:mm:ss"
        model.Lifecycle.Value <- sprintf "Observed owners: mounted %d  |  cleaned up %d" model.Created model.Cleaned

    let finish model =
        model.Utility |> Option.iter (fun utility -> model.Closings.Add(Utilities.Model.close utility))
        System.Threading.Tasks.Task.WhenAll(model.Closings).GetAwaiter().GetResult()

module Theme =
    open Ranvier.Keyboard

    let altLetter (letter: char) =
        chord { alt; key (Character (System.Text.Rune letter)) }

    let sidebarWidth = 23
    let pageGap = 2
    let sidePadding = 1
    let background = Css.rgb 12 18 29
    let surface = Css.rgb 20 29 44
    let accent = Css.rgb 101 224 200
    let muted = Css.rgb 147 166 190
    let bright = Css.rgb 230 239 250

    let styled = Dsl.styled

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
