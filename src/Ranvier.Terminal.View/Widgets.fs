namespace Ranvier.Terminal.View

open System
open System.Globalization
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

module Widgets =
    let button declarations caption action =
        let node = Node.create declarations [||]
        Node.setText caption node
        node.Focusable <- true

        node.OnEvent <-
            function
            | Key key when
                not key.Released
                && not key.Paste
                && (key.Key = Key.Enter || (key.Key = Key.Character && key.Scalar = 32))
                ->
                action ()
                true
            | Mouse mouse when mouse.Action = MouseAction.Press && mouse.Button = 0 ->
                action ()
                true
            | _ -> false

        node

    let checkbox declarations caption (value: Signal<bool>) =
        let node = button declarations "" (fun () -> value.Value <- not value.Value)
        createEffect (fun () -> Node.setText ((if value.Value then "[x] " else "[ ] ") + caption) node)
        node

    let progress declarations width (value: unit -> float) =
        let node = Node.create (Width(Cells width) :: declarations) [||]

        createEffect (fun () ->
            let fraction = value ()

            let fraction =
                if Double.IsNaN fraction then
                    0.
                else
                    max 0. (min 1. fraction)

            let filled = int (fraction * float (max 0 width))
            Node.setText (String('█', filled) + String('░', max 0 width - filled)) node)

        node

    let input declarations (value: Signal<string>) =
        let node = Node.create declarations [||]
        node.Focusable <- true
        let mutable cursor = value.Peek.Length
        let mutable boundaries = [||]

        let updateCaret () =
            let mutable offset, columns = 0, 0

            for i = 0 to node.Text.Glyphs.Length - 1 do
                let glyph = node.Text.Glyphs[i]

                if offset < cursor then
                    columns <- columns + max 0 glyph.Columns

                offset <- offset + glyph.Text.Length

            node.CaretColumn <- columns

        let move position =
            cursor <- position
            updateCaret ()

        createEffect (fun () ->
            let current = value.Value
            boundaries <- StringInfo.ParseCombiningCharacters current
            cursor <- min cursor current.Length
            Node.setText current node
            updateCaret ())

        let previous () =
            let mutable result = 0

            for i = 0 to boundaries.Length - 1 do
                if boundaries[i] < cursor then
                    result <- boundaries[i]

            result

        let next () =
            let mutable result = value.Peek.Length
            let mutable i = boundaries.Length - 1

            while i >= 0 do
                if boundaries[i] > cursor then
                    result <- boundaries[i]

                i <- i - 1

            result

        node.OnEvent <-
            function
            | Key key when not key.Released ->
                let current = value.Peek

                match key.Key with
                | Key.Left when not key.Paste ->
                    move (previous ())
                    true
                | Key.Right when not key.Paste ->
                    move (next ())
                    true
                | Key.Home when not key.Paste ->
                    move 0
                    true
                | Key.End when not key.Paste ->
                    move current.Length
                    true
                | Key.Backspace when not key.Paste && cursor > 0 ->
                    let start = previous ()
                    let updated = current.Remove(start, cursor - start)
                    cursor <- start
                    value.Value <- updated
                    true
                | Key.Delete when not key.Paste && cursor < current.Length ->
                    value.Value <- current.Remove(cursor, next () - cursor)
                    true
                | Key.Character when
                    key.Scalar >= 32
                    && key.Scalar <= 0x10FFFF
                    && (key.Paste
                        || key.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt) = Modifiers.None)
                    ->
                    let inserted = Char.ConvertFromUtf32 key.Scalar
                    let updated = current.Insert(cursor, inserted)
                    cursor <- cursor + inserted.Length
                    value.Value <- updated
                    true
                | _ -> false
            | _ -> false

        node

    let list declarations (items: string array) (selected: Signal<int>) =
        let node = Node.create declarations [||]
        node.Focusable <- true

        createEffect (fun () ->
            let current = selected.Value

            node.Children <-
                items
                |> Array.mapi (fun index item ->
                    let child = Dsl.label item

                    if index = current then
                        child.Style <-
                            { child.Style with
                                Paint =
                                    { child.Style.Paint with
                                        Attributes = Attributes.Reverse } }

                    child))

        node.OnEvent <-
            function
            | Key key when not key.Released && not key.Paste && items.Length > 0 ->
                match key.Key with
                | Key.Up ->
                    selected.Value <- max 0 (selected.Value - 1)
                    true
                | Key.Down ->
                    selected.Value <- min (items.Length - 1) (selected.Value + 1)
                    true
                | _ -> false
            | _ -> false

        node
