namespace Showcase

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Components
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl
open Theme

module Components =
    let private indicators (model: Model) =
        let tick () = int (model.Elapsed.Value * 8.)
        let status name state =
            TaskStatus.create [] (fun _ -> []) 12 tick state |> id name
        column [ Css.gap 1 ] {
            card "SPINNER" [
                for name, style in [ "Braille", Spinner.Braille; "ASCII line", Spinner.Line
                                     "Quadrants", Spinner.Quadrants; "Bouncing bar", Spinner.BouncingBar
                                     "Points", Spinner.Points; "Shading", Spinner.Shading; "Moon", Spinner.Moon ] do
                    Spinner.create style [] name tick
                    |> id ("spinner-" + string style) ]
            card "PULSE" [
                for name, style in [ "Bounce", Pulse.Bounce; "Sweep", Pulse.Sweep; "Bouncing segment", Pulse.BouncingSegment ] do
                    Pulse.create style [] 12 name tick
                    |> id ("pulse-" + string style) ]
            card "DETERMINATE" [
                line "Task progress"
                status "gallery-determinate" (fun () -> TaskStatus.Determinate model.Phase.Value)
                line "Progress bar"
                Widgets.progress [] 12 (fun () -> model.Phase.Value) ]
            card "TASK STATES" [
                for state in [ TaskStatus.Queued; TaskStatus.Success; TaskStatus.Failure; TaskStatus.Cancelled ] do
                    TaskStatus.create [] (fun _ -> []) 12 tick (fun () -> state) ]
            Widgets.checkbox [] "Animate examples" model.Animate
        }

    let private lists (_: Model) =
        let items = createSignal (Array.init 48 (fun index -> sprintf "Sample %02d" (index + 1)))
        let wrapped = createSignal (items.Peek |> Array.map (fun label -> label + " with a second line of text"))
        let tableStyle = Css.tableScrollbar { Paint.empty with Foreground = muted } { Paint.empty with Foreground = accent }
        column [ Css.gap 1 ] {
            card "FIXED ROWS / SINGLE-CELL THUMB" [
                Widgets.virtualList tableStyle 4 items (createSignal 0) |> id "gallery-list" ]
            card "WRAPPED ROWS / PROPORTIONAL THUMB" [
                Widgets.virtualTextList [] (createSignal { Width = 22; Height = 5 }) wrapped (createSignal 0) (createSignal 0) ]
        }

    let private charts model =
        let samples () = Array.init 36 (fun i -> 50. + 45. * sin (model.Elapsed.Value + float i / 5.))
        column [ Css.gap 1 ] {
            card "BRAILLE CHART" [
                line "Standard"
                Styled.brailleChart [] 18 2 accent false samples
                line "Inverted"
                Styled.brailleChart [] 18 2 accent true samples ]
        }

    let private field = [ Css.height (Cells 1); Css.backgroundColor surface ]
    let private floating = [ Css.width (Cells 24); Css.backgroundColor bright; Css.color background ]

    module private ControlExamples =
        open Ranvier.Terminal.View.Templates

        type State = { Clicks: Signal<int>; Value: Signal<string> }

        let count =
            column () {
                button (button.id "gallery-button", button.className "control",
                        button.style (Css.height (Css.cells 1)), button.style (Css.backgroundColor surface),
                        button.onClick (fun state _ -> state.Clicks.Value <- state.Clicks.Peek + 1)) { "Count clicks" }
                fun state -> sprintf "%d clicks" state.Clicks.Value
            }

        let editor =
            input (input.style (Css.height (Css.cells 1)), input.style (Css.backgroundColor surface),
                   input.props.value (from (fun state -> state.Value)),
                   input.onInput (fun state edit -> state.Value.Value <- edit.Value))

    let private controls _ =
        let state: ControlExamples.State = { Clicks = createSignal 0; Value = createSignal "Edit me" }
        column [ Css.gap 1 ] {
            card "BUTTON" [ Templates.render ControlExamples.count state ]
            card "TEXT INPUT" [ Templates.render ControlExamples.editor.View state ]
            card "CHECKBOX" [ Widgets.checkbox [] "Enabled" (createSignal true) ]
            card "SELECTION LIST" [ Widgets.list [] [| "Red"; "Green"; "Blue" |] (createSignal 0) ]
        }

    let private popups _ =
        let anchor = line "Hover here, or focus and hold Alt" |> focusable
        let tip = PopupWidgets.tooltip floating "A node-anchored tooltip" anchor
        let values = [| "Amber"; "Azure"; "Blue"; "Coral" |]
        let input, matches =
            PopupWidgets.completion field floating (createSignal "")
                (fun prefix -> values |> Array.filter (fun s -> s.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))) 4
        let content =
            column [ Css.gap 1 ] {
                card "TOOLTIP" [ anchor ]
                card "CARET COMPLETION" [ line "Type a color, then choose a match."; input |> id "gallery-completion" ]
            }
        Popup.host content [| tip; matches |]

    let private selects _ =
        let options = createSignal [| "amber", "Amber"; "blue", "Blue"; "coral", "Coral" |]
        let single, menu = Select.single field floating "Choose a color" 3 options (createSignal None)
        let multi, checks = Select.multiple field floating "Choose colors" 3 options (createSignal Set.empty)
        let content = column [ Css.gap 1 ] {
            card "SINGLE SELECT" [ single |> id "gallery-select" ]
            card "MULTI-SELECT" [ multi ]
        }
        Popup.host content [| menu; checks |]

    let private tables _ =
        let values = [| "Elm"; "Cedar"; "Birch"; "Oak"; "Aspen"; "Willow"; "Pine"; "Maple" |]
        let columns: TableColumn<string> array =
            [| { Header = "Tree"; Width = Css.flex 1; Text = (fun value -> value); Style = fun _ _ -> [] } |]
        let make () = Table.create [] [ Css.color muted ] [ Css.attributes Attributes.Reverse ] [ Css.color accent ] (fun _ _ -> ())
        let plain, sortable = make (), make ()
        Table.update columns 4 values 0 0 plain |> ignore
        let order = Table.sortable (fun value -> value) [| { Id = "tree"; Compare = compare; Descending = false } |] "tree"
        Table.updateSortable columns [| Some "tree" |] [ Css.color accent ] 4 values 0 0 order sortable |> ignore
        column [ Css.gap 1 ] {
            card "TABLE" [ plain.Root ]
            card "KEYED SORTABLE TABLE" [ sortable.Root |> id "gallery-table"; line "Click Tree or press R to reverse the sort." ]
        }

    let private commands _ =
        let last = createSignal "Choose a command"
        let actions: Commands.Binding array =
            [| for key, caption in [ 'a', "Add bookmark"; 'r', "Remove bookmark" ] do
                   { Commands.Binding.Chord = altLetter key
                     Shortcut = "Alt+" + string (System.Char.ToUpperInvariant key); Caption = caption
                     Enabled = (fun () -> true); Invoke = fun () -> last.Value <- caption } |]
        let query, results = Commands.bindingPalette field floating 2 actions
        let expanded = createSignal false
        let content = column [ Css.gap 1 ] {
            card "COMMAND PALETTE" [ query |> id "gallery-command"; signalText last ]
            card "SHORTCUT HELP" [ Widgets.checkbox [] "Expanded" expanded; Commands.bindingHelp [] actions (fun () -> expanded.Value) ]
        }
        let host = Popup.host content [| results |]
        host.OnEvent <- Commands.dispatchBindings actions
        host

    let private meters (model: Model) =
        let value () = model.Phase.Value
        let sample caption (meter: Node) =
            row [ Css.gap 2 ] {
                line caption |> styled [ Width(Cells 14) ]
                meter
            }
        card "TWO-COLOR METERS" [
            for caption, style in [ "Thin line", Meter.ThinLine; "Heavy line", Meter.HeavyLine; "Double line", Meter.DoubleLine
                                    "Half block", Meter.HalfBlock; "Solid", Meter.Solid; "Segmented", Meter.Segmented
                                    "Dots", Meter.Dots; "ASCII", Meter.Ascii ] do
                sample caption (Meter.create style [] 18 accent muted value |> id ("meter-" + string style))
            sample "Fractional" (Meter.fractional [] 18 (fun _ -> accent) surface value |> id "meter-fractional")
            sample "Gradient" (Meter.fractional [] 18 (fun t -> Css.rgb (int (90. + 160. * t)) (int (220. - 82. * t)) 154) surface value |> id "meter-gradient")
            text (fun () -> sprintf "%.1f%%" (100. * value ()))
            Widgets.checkbox [] "Animate examples" model.Animate ]

    let private typography (model: Model) =
        let caption = createSignal "Type"
        card "FIGlet / Compact" [
            Widgets.input field caption |> id "gallery-figlet-input"
            Figlet.banner [ Css.color accent ] Figlet.compact
                (fun () -> max 1 (model.Columns.Value - sidebarWidth - pageGap - 2 * sidePadding - 6)) (fun () -> caption.Value) ]

    let private panels _ =
        column [ Css.gap 1 ] {
            for border in [ Single; Double; Rounded; Ascii ] do
                panel [ Css.border border; Css.height (Cells 3) ] { string border }
            Styled.panel [] 28 4 accent [ 2, bright, fun () -> " Caption " ] [] (line "Titled panel")
            |> id "gallery-panel"
        }

    let private scrollbars _ =
        let track = { Paint.empty with Foreground = muted }
        let thumb = { Paint.empty with Foreground = accent }
        column [ Css.gap 1 ] {
            for name, style in [ "Proportional", []; "Single cell", Css.tableScrollbar track thumb
                                 "Hidden", [ Css.scrollbarVisibility ScrollbarVisibility.Hidden ] ] do
                card name [
                    column (Css.height (Cells 4) :: style) {
                        for i = 1 to 16 do line (sprintf "Row %02d" i)
                    } |> id ("scroll-" + name) ]
        }

    let build model =
        let sections =
            [| "Task status and progress", indicators
               "Meters", meters
               "Basic controls", controls
               "Popups", popups
               "Select menus", selects
               "Virtual lists", lists
               "Navigation", (fun _ -> NavigationGallery.build () |> id "gallery-navigation")
               "Braille charts", (fun model -> charts model |> id "gallery-charts")
               "Tables", tables
               "Commands", commands
               "FIGlet text", typography
               "Panels and borders", panels
               "Scrollbar styles", scrollbars
               "Streaming output", UtilityPages.streaming
               "Subprocess terminal", UtilityPages.terminal |]
        let selected = createSignal (Some 0)
        let options = createSignal (sections |> Array.mapi (fun index (name, _) -> index, name))
        let picker, menu =
            Select.single [ Css.height (Cells 1); Css.backgroundColor surface; Css.color accent ]
                [ Css.width (Cells 30); Css.backgroundColor bright; Css.color background ] "Choose a component group" 6 options selected
        let content =
            column [ Css.height (Css.flex 1) ] {
                title "Native components"
                line "Choose a group ▾"
                picker |> id "component-group"
                each (fun () -> [| defaultArg selected.Value 0 |]) (fun index -> (snd sections[index]) model)
                |> styled [ Css.height (Css.flex 1) ]
            }
        Popup.host content [| menu |] |> styled [ Css.height (Css.flex 1) ]
