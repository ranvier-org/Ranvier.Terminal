namespace Ranvier.Terminal.View

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

module Select =
    type private Selection<'key when 'key: comparison> =
        | Single of Signal<'key option>
        | Multiple of Signal<Set<'key>>

    let private create anchorStyle popupStyle placeholder visibleRows (options: Signal<('key * string) array>) selection =
        let anchor = Node.create anchorStyle [||]
        anchor.Focusable <- true
        let cursor = createSignal 0
        let labels = createSignal [||]
        let list = Widgets.virtualList [] visibleRows labels cursor
        list.Focusable <- false
        let hidden = popupStyle @ [ Visible false ]
        let popup = Node.create hidden [| list |] |> Popup.atNode anchor
        let mutable opened = false
        let mutable cursorKey = None

        let hide () =
            opened <- false
            Node.setStyle hidden popup

        let setCursor index =
            let values = options.Peek
            if values.Length > 0 then
                let index = max 0 (min (values.Length - 1) index)
                cursor.Value <- index
                cursorKey <- Some (fst values[index])
            else
                cursor.Value <- 0
                cursorKey <- None

        let restore key =
            let index =
                key |> Option.bind (fun k -> options.Peek |> Array.tryFindIndex (fun (id, _) -> id = k))
            setCursor (defaultArg index cursor.Peek)

        let show () =
            if options.Peek.Length > 0 then
                let initial = match selection with Single model -> model.Peek | Multiple _ -> cursorKey
                restore initial
                opened <- true
                Node.setStyle popupStyle popup

        let choose index =
            let values = options.Peek
            if opened && index >= 0 && index < values.Length then
                setCursor index
                let key = fst values[index]
                match selection with
                | Single model ->
                    hide ()
                    model.Value <- Some key
                | Multiple model ->
                    let keys = model.Peek
                    model.Value <- if Set.contains key keys then Set.remove key keys else Set.add key keys
                true
            else false

        let navigate = list.OnEvent
        list.OnEvent <- function
            | Mouse m when opened && m.Action = MouseAction.Scroll && options.Peek.Length > 0 ->
                let target = int64 cursor.Peek + int64 m.Delta
                setCursor (int (System.Math.Clamp(target, 0L, int64 options.Peek.Length - 1L)))
                true
            | event -> navigate event

        for slot = 0 to list.Children.Length - 1 do
            list.Children[slot].OnEvent <- function
                | Mouse m when m.Action = MouseAction.Press && m.Button = 0 ->
                    let first = match list.VirtualScrollY with ValueSome(struct (offset, _)) -> offset | _ -> 0
                    choose (first + slot)
                | _ -> false

        createEffect (fun () ->
            let values = options.Value
            if values.Length = 0 then hide ()
            restore cursorKey
            Node.setStyle [ Height(Cells(min visibleRows values.Length)) ] list)
        createEffect (fun () ->
            let values = options.Value
            let caption, items =
                match selection with
                | Single model ->
                    let key = model.Value
                    let selected = values |> Array.tryPick (fun (id, caption) -> if Some id = key then Some caption else None)
                    defaultArg selected placeholder, Array.map snd values
                | Multiple model ->
                    let keys = model.Value
                    let selected = ResizeArray<string>()
                    let items =
                        values |> Array.map (fun (id, caption) ->
                            if Set.contains id keys then
                                selected.Add caption
                                "[x] " + caption
                            else "[ ] " + caption)
                    (if selected.Count > 0 then System.String.Join(", ", selected) else placeholder), items
            labels.Value <- items
            Node.setText caption anchor)

        anchor.OnFocusChanged <- fun focused -> if not focused then hide ()
        anchor.OnEvent <- function
            | Mouse m when m.Action = MouseAction.Press && m.Button = 0 ->
                if opened then hide () else show ()
                true
            | Key k as event when not k.Released && not k.Paste && k.Modifiers = Modifiers.None ->
                match k.Key with
                | Key.Escape when opened -> hide (); true
                | Key.Enter | Key.Character when k.Key = Key.Enter || k.Scalar = 32 ->
                    if opened then choose cursor.Peek
                    else
                        show ()
                        opened
                | Key.Down | Key.Up when not opened -> show (); opened
                | Key.Home when opened -> setCursor 0; true
                | Key.End when opened -> setCursor (options.Peek.Length - 1); true
                | _ when opened && list.OnEvent event -> setCursor cursor.Peek; true
                | _ -> false
            | Focus false -> hide (); false
            | _ -> false
        anchor, Popup.onOutsidePress hide popup

    /// Return the anchor and popup. Option keys must be unique and stable across replacements.
    let single anchorStyle popupStyle placeholder visibleRows options selected =
        create anchorStyle popupStyle placeholder visibleRows options (Single selected)

    /// Space, Enter and row clicks toggle checks. Escape closes the popup with the checks retained.
    let multiple anchorStyle popupStyle placeholder visibleRows options checkedKeys =
        create anchorStyle popupStyle placeholder visibleRows options (Multiple checkedKeys)
