namespace Ranvier.Terminal.View

open Ranvier
open Ranvier.Keyboard
open Ranvier.Terminal
open Ranvier.Terminal.Css

module PopupWidgets =
    /// Attach the returned popup to the host that contains the anchor.
    let tooltip declarations caption (anchor: Node) =
        let hidden = declarations @ [ Visible false ]
        let popup = Node.create hidden [||] |> Popup.atNode anchor |> Popup.passThrough
        Node.setText caption popup
        let mutable altHeld = false
        let refresh () =
            Node.setStyle (if anchor.Hovered || (anchor.Focused && altHeld) then declarations else hidden) popup
        anchor
        |> Dsl.onAltHeld (fun held -> altHeld <- held; refresh ())
        |> Dsl.onHoverChanged (fun _ -> refresh ())
        |> Dsl.onFocusChanged (fun _ -> refresh ())
        |> ignore
        refresh ()
        popup

    /// Return the editor for the content tree and its suggestion list for the popup host.
    /// A committed suggestion replaces the full editor value and places the caret at its end.
    let internal choices editorStyle popupStyle (value: Signal<string>) (suggest: string -> 'a array) sameChoice caption accept maxRows =
        if maxRows <= 0 then invalidArg (nameof maxRows) "The suggestion list needs at least one visible row."
        let editor, replaceText = Widgets.inputWithReplace editorStyle value
        let hidden = popupStyle @ [ Visible false ]
        let popup = Node.create hidden [||] |> Popup.atCaret editor
        let mutable matches: 'a array = [||]
        let mutable selected = 0
        let mutable first = 0
        let mutable opened = false
        let focused = createSignal false
        let dismissed = createSignal None
        let mutable lastQuery = None

        let hide () =
            opened <- false
            Node.setStyle hidden popup

        let dismiss () =
            dismissed.Value <- Some value.Peek
            hide ()

        let commit index =
            let choice = matches[index]
            hide ()
            batch (fun () ->
                dismissed.Value <- Some value.Peek
                let replacement = accept choice
                dismissed.Value <- Some replacement
                replaceText replacement)

        let rowStyle = [ Height(Cells 1) ]
        let highlight = Css.attributes Attributes.Reverse :: rowStyle
        let renderRows () =
            let count = min maxRows matches.Length
            first <- min (matches.Length - count) (max (selected - count + 1) (min first selected))
            if popup.Children.Length <> count then
                let rows =
                    Array.init count (fun slot ->
                        let row = Node.create rowStyle [||]
                        row.OnEvent <- function
                            | Mouse m when opened && m.Action = MouseAction.Press && m.Button = 0 ->
                                commit (first + slot)
                                true
                            | _ -> false
                        row)
                Node.setChildren rows popup
            for slot = 0 to count - 1 do
                let row = popup.Children[slot]
                let index = first + slot
                Node.setText (caption matches[index]) row
                Node.setStyle (if index = selected then highlight else rowStyle) row

        let refresh query =
            let next = suggest query
            if opened && lastQuery = Some query && next.Length > 0 then
                let previousChoice = matches[selected]
                if selected >= next.Length || not (sameChoice previousChoice next[selected]) then
                    selected <- next |> Array.tryFindIndex (sameChoice previousChoice)
                                     |> Option.defaultValue (min selected (next.Length - 1))
            else
                selected <- 0
                first <- 0
            matches <- next
            lastQuery <- Some query
            if matches.Length = 0 then hide ()
            else
                renderRows ()
                opened <- true
                Node.setStyle popupStyle popup

        editor.OnFocusChanged <- fun active ->
            focused.Value <- active
            dismissed.Value <- None

        createEffect (fun () ->
            if focused.Value then
                let query = value.Value
                if dismissed.Value = Some query then hide ()
                else
                    dismissed.Value <- None
                    refresh query
            else hide ())

        let edit = editor.OnEvent
        editor.OnEvent <- function
            | Key key when not key.Released && not key.Paste && key.Modifiers = Modifiers.None ->
                match key.LogicalKey with
                | LogicalKey.Navigation ArrowUp | LogicalKey.Navigation ArrowDown when opened ->
                    selected <- max 0 (min (matches.Length - 1) (selected + (if key.LogicalKey = LogicalKey.Navigation ArrowUp then -1 else 1)))
                    renderRows ()
                    true
                | LogicalKey.Navigation ArrowDown ->
                    if dismissed.Peek.IsSome then dismissed.Value <- None
                    else refresh value.Peek
                    opened
                | LogicalKey.Whitespace Enter when opened -> commit selected; true
                | LogicalKey.Ui Escape when opened -> dismiss (); true
                | _ -> edit (Key key)
            | Focus false as event -> dismiss (); edit event
            | event -> edit event

        editor, Popup.onOutsidePress dismiss popup

    let completion editorStyle popupStyle value suggest maxRows =
        choices editorStyle popupStyle value suggest (=) id id maxRows
