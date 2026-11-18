namespace Showcase

open System
open Ranvier
open Ranvier.Keyboard
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl
open Theme

module Pages =
    let overview (model: Model) =
        column [ Css.gap 1 ] {
            title "A terminal UI, powered by Ranvier"

            note
                "Explore the pages on the left. Every control is live. Resize the terminal to see layout recalculate; use the mouse wheel when a page overflows."

            card
                "REACTIVE MODEL"
                [ text (fun () -> "Local clock   " + model.Clock.Value)
                  text (fun () ->
                      sprintf "Signal value  %d     Derived value  %d" model.Count.Value (model.Count.Value * 2))
                  button "overview-increment" " + Update signal " (fun () -> model.Count.Value <- model.Count.Value + 1)
                  Widgets.progress [] 32 (fun () -> model.Phase.Value)
                  Widgets.checkbox [] "Animate" model.Animate ]

            card
                "WHAT TO TRY"
                [ line "Controls       Edit text, toggle checkboxes, select a row."
                  line "Layout         Fixed cells, percentages, flex and alignment."
                  line "Style gallery  Borders, colors, attributes, focus and hover."
                  line "Unicode        Wide glyphs, accents, emoji and safe text."
                  line "Lifetimes      Mount/unmount branches and change collections."
                  line "Rendering      Measured diff output, allocations and modes."
                  line "Input / scroll Key and mouse events, clipping and scrolling."
                  line "Streaming      Live output, Unicode, progress and history."
                  line "Subprocess     Interactive child terminal, input and resize."
                  line "Instruments    Select a simulated channel and watch its trace."
                  line "Tasks          Follow and sort a simulated build pipeline."
                  line "Components     Browse live widgets and compare built-in styles." ]

            note
                "GPU rendering belongs to your terminal emulator. This app uses the same native F# framebuffer and terminal output path on every page."
        }

    let controls (model: Model) =
        let doubled = createMemo (fun _ -> model.Count.Value * 2)
        let reset = button "reset" " Reset " (fun () -> model.Count.Value <- 0)
        let popupStyle = [ Css.backgroundColor bright; Css.color background ]
        let tip = PopupWidgets.tooltip popupStyle "Set the counter back to zero" reset |> id "reset-tip"
        let planets = [| "Mercury"; "Venus"; "Earth"; "Mars"; "Jupiter"; "Saturn"; "Uranus"; "Neptune" |]
        let matchingPlanets (query: string) =
            planets |> Array.filter (fun name -> name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        let planetInput, suggestions =
            PopupWidgets.completion
                [ Css.height (Css.cells 1); Css.backgroundColor background ]
                (Css.width (Css.cells 12) :: popupStyle)
                model.Planet matchingPlanets 4
        let pickerStyle = [ Css.height (Css.cells 1); Css.backgroundColor background ]
        let optionsStyle = Css.width (Css.cells 22) :: popupStyle
        let destination, destinations =
            Select.single pickerStyle optionsStyle "Choose destination" 3
                (createSignal [| "orbit", "Orbit"; "moon", "Moon"; "mars", "Mars" |]) model.Destination
        let payload, payloads =
            Select.multiple pickerStyle optionsStyle "Choose payloads" 3
                (createSignal [| "camera", "Camera"; "radar", "Radar"; "radio", "Radio" |]) model.Payloads

        column [ Css.gap 1 ] {
            title "Controls + reactive bindings"

            card
                "BUTTONS / SIGNAL / MEMO"
                [ row [ Css.gap 2 ] {
                      button "decrement" " − Decrement " (fun () -> model.Count.Value <- model.Count.Value - 1)
                      button "increment" " + Increment " (fun () -> model.Count.Value <- model.Count.Value + 1)
                      reset
                  }
                  text (fun () -> sprintf "Count: %d    Memo (count × two): %d" model.Count.Value doubled.Value)
                  line "Hover over Reset for help, or focus it and hold Alt." ]

            card
                "PLANET COMPLETION"
                [ note "Focus the field and type a planet prefix. Use Up or Down to browse, Enter to choose, or Esc to close."
                  planetInput |> id "planet-input" ]

            card "MISSION LOADOUT"
                [ line "Destination"; destination |> id "mission-destination"
                  line "Payloads"; payload |> id "mission-payloads"
                  note "Choose a destination and check the payloads to carry. Esc closes the menu." ]

            card
                "TEXT INPUT"
                [ note "Tab or click to focus. Edit with arrows, Home, End, Backspace and Delete."
                  Widgets.input [ Css.height (Css.cells 1); Css.backgroundColor background ] model.Name
                  |> id "name-input"
                  text (fun () -> "Hello, " + model.Name.Value + "!") ]

            card
                "CHECKBOX / PROGRESS"
                [ Widgets.checkbox [] "Keep the simulation moving" model.Animate |> id "animate"
                  Widgets.progress [] 32 (fun () -> model.Phase.Value) ]

            card
                "SELECTION LIST"
                [ note "Focus the list with Tab, then use Up and Down."
                  Widgets.list [ Css.height (Css.cells 4) ] [| "Signals"; "Memos"; "Effects"; "Owners" |] model.Selected
                  |> id "selection"
                  text (fun () ->
                      "Selected: "
                      + [| "Signals"; "Memos"; "Effects"; "Owners" |][model.Selected.Value]) ]
        }
        |> fun content -> Popup.host content [| tip; suggestions |> id "planet-matches"; destinations; payloads |]
        |> styled [ Css.height (Css.flex 1) ]

    let layout (model: Model) =
        let box (caption: string) declarations =
            panel (Css.border Single :: Css.paddingXY 1 0 :: declarations) { caption }

        let spacing =
            row [ Css.height (Css.cells 3) ] {
                box "fixed" [ Css.width (Css.cells 12) ]
                box "flex" [ Css.width (Css.flex 1) ]
                box "double flex" [ Css.width (Css.flex 2) ]
            }
            |> bindStyle (fun () -> [ Css.gap model.Gap.Value ])

        column [ Css.gap 1 ] {
            title "Layout laboratory"

            note
                "Resize the window: fixed-width content stays fixed while weighted flex content shares the remaining columns."

            row [ Css.gap 2 ] {
                button "gap-less" " Less gap " (fun () -> model.Gap.Value <- max 0 (model.Gap.Value - 1))
                button "gap-more" " More gap " (fun () -> model.Gap.Value <- min 4 (model.Gap.Value + 1))
                text (fun () -> sprintf "Gap: %d cells" model.Gap.Value)
            }

            spacing

            row [ Css.height (Css.cells 3) ] {
                box "quarter" [ Css.width (Css.percent 25) ]
                box "half" [ Css.width (Css.percent 50) ]
                box "rest" [ Css.width (Css.flex 1) ]
            }

            card
                "ALIGNMENT / JUSTIFICATION"
                [ row [ Css.justify Center; Css.gap 1 ] {
                      "centered"
                      "row"
                  }
                  row [ Css.justify End; Css.gap 1 ] {
                      "end-aligned"
                      "row"
                  }
                  column [ Css.align Center ] { "centered child" }
                  column [ Css.align End ] { "right-aligned child" } ]

            card "WORD WRAPPING" [
                note "Word-aware wrapping is the default. Long words fall back to grapheme breaks."
                Dsl.label "Default: keep these words together when the line fills."
                |> bindStyle (fun () -> [ Css.width (Css.cells 24); Css.wrap true ])
                Dsl.label "Opt-out: keep these words together when the line fills."
                |> bindStyle (fun () -> [ Css.width (Css.cells 24); Css.wrap true; Css.wordWrap false ])
            ]

            panel [ Css.border Double; Css.padding 1; Css.margin 1 ] {
                "Padding inside; margin outside."

                note
                    "This paragraph wraps on the cell grid, contributes its wrapped height, and is clipped to its parent's content box when space runs out."
            }

            panel
                [ Css.width (Css.percent 70)
                  Css.minWidth 20
                  Css.maxWidth 50
                  Css.border Ascii ] {
                "Percentage width with minimum / maximum bounds"
            }
        }

    let stylesPage (model: Model) =
        let dynamic =
            line "Reactive styling: toggle the accent below."
            |> bindStyle (fun () ->
                [ Css.color (if model.Accent.Value then Css.rgb 255 183 112 else accent)
                  Css.attributes Attributes.Bold ])

        column [ Css.gap 1 ] {
            title "Style gallery"

            row [ Css.height (Css.cells 3); Css.gap 1 ] {
                for border, name in [ Single, "Single"; Double, "Double"; Rounded, "Rounded"; Ascii, "ASCII" ] do
                    panel [ Css.width (Css.flex 1); Css.border border; Css.paddingXY 1 0 ] { name }
            }

            card
                "TEXT ATTRIBUTES"
                [ row [ Css.gap 2 ] {
                      for flag, name in
                          [ Attributes.Bold, "Bold"
                            Attributes.Dim, "Dim"
                            Attributes.Italic, "Italic"
                            Attributes.Underline, "Underline"
                            Attributes.Reverse, "Reverse"
                            Attributes.Strike, "Strike" ] do
                          line name |> styled [ Css.attributes flag ]
                  }
                  note "The terminal decides how fonts, italic and strike-through look." ]

            card
                "INDEXED COLORS / TRUECOLOR"
                [ row [] {
                      for i = 0 to 15 do
                          line "   " |> styled [ Css.backgroundColor (Indexed(byte i)) ]
                  }
                  row [] {
                      for i = 0 to 39 do
                          line " "
                          |> styled [ Css.backgroundColor (Css.rgb (40 + i * 5) (220 - i * 3) (180 + i)) ]
                  }
                  note "Switch truecolor / palette fallback / monochrome on Rendering." ]

            card
                "STATE RULES / CLASS / ID"
                [ line "Class selector: accent" |> classes [| "accent" |]
                  line "ID selector: underline" |> id "id-example"
                  button "style-hover" " Hover or focus me " (fun () -> model.Accent.Value <- not model.Accent.Value)
                  dynamic
                  Widgets.checkbox [] "Warm accent" model.Accent ]

            note
                "Nested children inherit foreground and background. Rules apply in authored order. This is typed terminal styling, not a browser CSS engine."
        }

    let unicode (model: Model) =
        column [ Css.gap 1 ] {
            title "Unicode + safe terminal text"

            card
                "PREPARED GRAPHEMES"
                [ line "Latin       café · naïve · Ångström"
                  line "Combining   é  â  ö  — each accent stays with its base"
                  line "Wide text   日本語  中文  한글"
                  line "Emoji       👩‍💻  🚀  🌍  😀"
                  line "Box drawing ┌─┬─┐  ╔═╦═╗  ╭───╮"
                  line "Symbols     → ← ↑ ↓  ✓  ●  ◇  ∑" ]

            card
                "GRAPHEME-AWARE EDITING"
                [ button "load-unicode" " Load mixed Unicode text " (fun () -> model.Name.Value <- "café 界 é 👩‍💻")
                  Widgets.input [ Css.height (Css.cells 1); Css.backgroundColor background ] model.Name
                  |> id "unicode-input"
                  note "Backspace removes a whole grapheme. The caret scrolls into view." ]

            card
                "CONTROL-CHARACTER SAFETY"
                [ line "The following is text containing an escape, not a color command:"
                  line "safe \u001b[31m sample" ]

            note
                "Wide cells are paired and overwritten safely. The default width policy is practical; fonts and emulator Unicode versions can disagree, especially for emoji. A custom preparation policy is supported."
        }

    let lifetimes (model: Model) =
        let branch =
            show (fun () -> model.Reveal.Value) (fun () ->
                model.Created <- model.Created + 1
                onCleanup (fun () -> model.Cleaned <- model.Cleaned + 1)

                card
                    "OWNED LIVE BRANCH"
                    [ text (fun () -> "I am mounted and following the clock: " + model.Clock.Value)
                      Widgets.progress [] 30 (fun () -> model.Phase.Value) ])

        column [ Css.gap 1 ] {
            title "Dynamic content + ownership"
            Widgets.checkbox [] "Mount the live branch" model.Reveal |> id "reveal"
            branch
            signalText model.Lifecycle

            note
                "Unmounting releases the branch's effects and invokes its cleanup. Changing pages also disposes the active page scope."

            row [ Css.gap 2 ] {
                button "add-item" " + Add row " (fun () ->
                    let item = sprintf "Item %d" model.NextItem
                    model.NextItem <- model.NextItem + 1
                    model.Items.Value <- Array.append model.Items.Peek [| item |])

                button "remove-item" " − Remove row " (fun () ->
                    let items = model.Items.Peek

                    if items.Length > 0 then
                        model.Items.Value <- items[.. items.Length - 2])
            }

            eachKeyed (fun name -> name) (fun () -> model.Items.Value) (fun read -> line ("  • " + read ()))
            |> id "lifetime-rows"

            note
                "Each row owns its scope. Adding or removing a row preserves the other rows and their bindings."
        }

    let rendering (model: Model) =
        column [ Css.gap 1 ] {
            title "Framebuffer + output laboratory"

            card
                "MEASURED HERE / PREVIOUS RENDER"
                [ signalText model.Metrics
                  note
                      "Scope: layout, painting, diff, UTF-8 encode, output and commit. Excludes model updates, input and text preparation." ]

            card
                "SPARSE OVERWRITE"
                [ text (fun () ->
                      let position = int (model.Phase.Value * 35.)
                      "[" + String(' ', position) + "◆" + String(' ', 35 - position) + "]")
                  Widgets.checkbox [] "Animate the marker" model.Animate
                  button "invalidate" " Force full redraw " (fun () -> model.Redraw <- true)
                  note
                      "Only changed cells are emitted. A forced redraw invalidates the front snapshot; the following frame repaints the viewport." ]

            card
                "OUTPUT CAPABILITIES"
                [ row [ Css.gap 2 ] {
                      button "truecolor" " Truecolor " (fun () -> model.Palette.Value <- 0)
                      button "palette" " Palette " (fun () -> model.Palette.Value <- 1)
                      button "mono" " Monochrome " (fun () -> model.Palette.Value <- 2)
                  }
                  text (fun () ->
                      "Color output: "
                      + [| "truecolor"; "indexed fallback"; "monochrome" |][model.Palette.Value])
                  Widgets.checkbox [] "Request synchronized output" model.Sync
                  note
                      "Synchronization requires terminal support. Unsupported terminals cannot be forced to present atomically." ]

            note
                "Buffers are reused during rendering. Resizing and changing output modes may allocate. GPU rasterization is handled by the terminal host; this app does not measure GPU time."
        }

    let input (model: Model) =
        let scroller =
            each (fun () -> [| 1 .. model.LogRows.Value |])
                (fun i -> line (sprintf "Log row %02d  |  event received" i))
            |> bindStyle (fun () ->
                [ Css.height (Css.cells model.LogHeight.Value); Css.border Single; Css.paddingXY 1 0
                  Css.scrollbarTrack "░" { Paint.empty with Foreground = muted; Background = surface }
                  Css.scrollbarThumb "█" { Paint.empty with Foreground = accent; Background = surface } ])
            |> id "scroll-region"
            |> focusable

        scroller.OnEvent <-
            function
            | Key key when not key.Released && not key.Paste ->
                match key.LogicalKey with
                | LogicalKey.Navigation ArrowDown ->
                    scroller.ScrollY <- min scroller.MaxScrollY (scroller.ScrollY + 1)
                    Node.invalidate scroller
                    true
                | LogicalKey.Navigation ArrowUp ->
                    scroller.ScrollY <- max 0 (scroller.ScrollY - 1)
                    Node.invalidate scroller
                    true
                | LogicalKey.Navigation PageDown ->
                    scroller.ScrollY <- min scroller.MaxScrollY (scroller.ScrollY + scroller.Content.Height)
                    Node.invalidate scroller
                    true
                | LogicalKey.Navigation PageUp ->
                    scroller.ScrollY <- max 0 (scroller.ScrollY - scroller.Content.Height)
                    Node.invalidate scroller
                    true
                | _ -> false
            | _ -> false

        column [ Css.gap 1 ] {
            title "Input, focus, mouse + scrolling"

            row [ Css.gap 1 ] {
                button "log-remove" " − Rows " (fun () -> model.LogRows.Value <- max 4 (model.LogRows.Peek - 4))
                button "log-add" " + Rows " (fun () -> model.LogRows.Value <- min 120 (model.LogRows.Peek + 4))
                text (fun () -> sprintf "%d log rows" model.LogRows.Value)
            }
            row [ Css.gap 1 ] {
                button "log-shorter" " Shorter " (fun () -> model.LogHeight.Value <- max 5 (model.LogHeight.Peek - 2))
                button "log-taller" " Taller " (fun () -> model.LogHeight.Value <- min 19 (model.LogHeight.Peek + 2))
                text (fun () -> sprintf "Viewport %d rows high" model.LogHeight.Value)
            }
            note "Drag the bright thumb or click the track to jump. Use the wheel, or focus the log and press an arrow or Page Up / Down."
            scroller

            card
                "LAST EVENT"
                [ signalText model.LastEvent
                  note
                      "Events are observed before dispatch. Tab navigation and focused widgets still handle them normally." ]

            Widgets.input [ Css.height (Css.cells 1); Css.backgroundColor surface ] model.Name
            |> id "event-input"

            note
                "Windows uses native console input records. Unix uses the bounded UTF-8 / escape-sequence parser, including bracketed paste. Windows console input does not preserve clipboard-paste boundaries."

            signalText model.Viewport
            NavigationGallery.build ()
        }

    let instruments (model: Model) =
        let channels = [| 1 .. 128 |]
        let frequency channel = 0.25 * float channel
        let columns: TableColumn<int> array =
            [| { Header = "Channel"; Width = Css.flex 1; Text = sprintf "CH-%03d"
                 Style = fun _ _ -> [ Css.color bright ] }
               { Header = "Frequency"; Width = Css.cells 14; Text = fun channel -> sprintf "%.2f Hz" (frequency channel)
                 Style = fun _ _ -> [ Css.color accent ] } |]
        let table =
            Table.create [ Css.width (Css.cells 42) ] [ Css.color muted ] [ Css.attributes Attributes.Reverse ]
                [ Css.color accent ] (fun selected first -> model.InstrumentPosition.Value <- struct (selected, first))
        let struct (selected, first) = model.InstrumentPosition.Peek
        Table.update columns 5 channels selected first table |> ignore
        let selectedChannel () =
            let struct (selected, _) = model.InstrumentPosition.Value
            channels[selected]
        let samples =
            createMemo (fun _ ->
                let elapsed = model.Elapsed.Value
                let rate = frequency (selectedChannel ())
                Array.init 76 (fun index ->
                    let time = elapsed - float (75 - index) / 100.
                    50. + 45. * sin (2. * Math.PI * rate * time)))
        let level () = Array.last samples.Value / 100.
        let trace =
            column [] {
                Styled.brailleChart [] 38 3 accent false (fun () -> samples.Value)
                Styled.meter [] 38 accent muted level
            }
        column [ Css.gap 1 ] {
            title "Signal instruments"
            Styled.panel [] 42 6 muted
                [ (2, accent, fun () -> sprintf "┤ CH-%03d ├" (selectedChannel ())) ]
                [ (2, bright, fun () -> sprintf "┤ %.0f%% ├" (level () * 100.)) ] trace
            Widgets.checkbox [] "Run simulation" model.Animate |> id "instrument-run"
            table.Root |> id "instrument-table"
            note "Select a simulated channel with the arrow keys or mouse. The trace and level meter follow the selected frequency."
        }

    type private TaskRow = { Step: int; Name: string; State: TaskStatus.State }

    let tasks (model: Model) =
        let stages = [| "Discover sources"; "Compile modules"; "Run tests"; "Package output" |]
        let duration = 5.
        let runtime = duration * float stages.Length
        let restart () =
            model.TaskStop.Value <- None
            model.TaskStart.Value <- Some model.Elapsed.Peek
        if model.TaskStart.Peek.IsNone then restart ()

        let elapsed () =
            match model.TaskStop.Value with
            | Some (elapsed, _) -> elapsed
            | None -> min runtime (model.Elapsed.Value - model.TaskStart.Value.Value)
        let stop state =
            let time = elapsed ()
            if model.TaskStop.Peek.IsNone && time < runtime then
                model.TaskStop.Value <- Some (time, state)
        let stepTime = createMemo (fun _ -> int (elapsed () * 4.))
        let rows =
            createMemo (fun _ ->
                let stopped = model.TaskStop.Value
                let time = float stepTime.Value / 4.
                stages |> Array.mapi (fun step name ->
                    let age = time - float step * duration
                    let state =
                        if age >= duration then TaskStatus.Success
                        elif age < 0. then
                            if stopped.IsSome then TaskStatus.Cancelled else TaskStatus.Queued
                        else
                            match stopped with
                            | Some (_, state) -> state
                            | None when step = 0 -> TaskStatus.Spinner
                            | None when step = 2 -> TaskStatus.Pulse
                            | None -> TaskStatus.Determinate (age / duration)
                    { Step = step + 1; Name = name; State = state }))

        let statusText = function
            | TaskStatus.Queued -> "Queued"
            | TaskStatus.Spinner | TaskStatus.Pulse -> "Running"
            | TaskStatus.Determinate part -> sprintf "%d%%" (int (100. * part))
            | TaskStatus.Success -> "Done"
            | TaskStatus.Failure -> "Failed"
            | TaskStatus.Cancelled -> "Cancelled"
        let statusStyle state =
            [ Css.color (match state with
                         | TaskStatus.Queued | TaskStatus.Cancelled -> muted
                         | TaskStatus.Failure -> Css.rgb 255 140 125
                         | TaskStatus.Success -> accent
                         | _ -> bright) ]
        let columns: TableColumn<TaskRow> array =
            [| { Header = "Step"; Width = Css.cells 8; Text = fun row -> string row.Step
                 Style = fun _ _ -> [ Css.color muted ] }
               { Header = "Task"; Width = Css.flex 1; Text = _.Name; Style = fun _ _ -> [] }
               { Header = "Status"; Width = Css.cells 12; Text = fun row -> statusText row.State
                 Style = fun _ row -> statusStyle row.State } |]
        let sort =
            Table.sortable (fun (row: TaskRow) -> row.Step)
                [| { Id = "step"; Compare = (fun a b -> compare a.Step b.Step); Descending = false }
                   { Id = "task"; Compare = (fun a b -> String.CompareOrdinal(a.Name, b.Name)); Descending = false } |] "step"
        let selectedStep = createSignal (Some 1)
        let headings = [| Some "step"; Some "task"; None |]
        let table =
            Table.create [] [ Css.color muted ] [ Css.attributes Attributes.Reverse ] [ Css.color accent ]
            |> Table.bindSortable columns headings [ Css.color accent ] stages.Length (fun () -> rows.Value) selectedStep sort
        let selectedRow () = rows.Value[(defaultArg selectedStep.Value 1) - 1]
        let running () = model.TaskStop.Value.IsNone && stepTime.Value < int (runtime * 4.)
        let commands: Commands.Binding array =
            [| { Chord = altLetter 'b'; Shortcut = "Alt+B"
                 Caption = "Restart build"; Enabled = (fun () -> true); Invoke = restart }
               { Chord = altLetter 'f'; Shortcut = "Alt+F"
                 Caption = "Fail current step"; Enabled = running; Invoke = fun () -> stop TaskStatus.Failure }
               { Chord = altLetter 'x'; Shortcut = "Alt+X"
                 Caption = "Cancel build"; Enabled = running; Invoke = fun () -> stop TaskStatus.Cancelled } |]
        let query, choices =
            Commands.bindingPalette [ Css.height (Css.cells 1); Css.backgroundColor surface ]
                [ Css.width (Css.cells 28); Css.backgroundColor bright; Css.color background ] 3 commands
        let expanded = createSignal false
        column [ Css.gap 1 ] {
            Figlet.banner [ Css.color accent ] Figlet.compact
                (fun () -> max 0 (model.Columns.Value - sidebarWidth - pageGap - 2 * sidePadding))
                (fun () -> "Tasks")
            |> id "task-banner"
            table.Root |> id "task-table"
            card "SELECTED TASK"
                [ text (fun () -> (selectedRow ()).Name) |> id "task-name"
                  TaskStatus.create [] statusStyle 12 (fun () -> int (model.Elapsed.Value * 8.))
                      (fun () -> (selectedRow ()).State) |> id "task-status" ]
            note "The build is simulated. Select a task to inspect its progress. Click Step or Task to sort, or focus the table and use S to switch columns and R to reverse."
            card "BUILD COMMANDS"
                [ line "Type to find a command, then press Enter."
                  query |> id "task-command"
                  Widgets.checkbox [] "Expand shortcut help" expanded
                  Commands.bindingHelp [] commands (fun () -> expanded.Value) ]
        }
        |> fun content ->
            let root = Popup.host content [| choices |] |> styled [ Css.height (Css.flex 1) ]
            root.OnEvent <- Commands.dispatchBindings commands
            root
