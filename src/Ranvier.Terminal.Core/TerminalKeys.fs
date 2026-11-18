namespace Ranvier.Terminal

open System
open System.Text

module TerminalKeys =
    let encode bracketedPaste event =
        let text =
            match event with
            | PasteBoundary active when bracketedPaste -> if active then "\u001b[200~" else "\u001b[201~"
            | Key key when not key.Released ->
                let has flag = key.Modifiers &&& flag <> Modifiers.None
                let modifier = 1 + (if has Modifiers.Shift then 1 else 0)
                                 + (if has Modifiers.Alt then 2 else 0)
                                 + (if has Modifiers.Control then 4 else 0)
                let letter final =
                    if modifier = 1 then "\u001b[" + final
                    else "\u001b[1;" + string modifier + final
                let tilde number =
                    "\u001b[" + string number + (if modifier = 1 then "" else ";" + string modifier) + "~"
                let alt text = if has Modifiers.Alt && not key.Paste then "\u001b" + text else text
                match key.Key with
                | Key.Character when Rune.IsValid key.Scalar ->
                    let mutable scalar = key.Scalar
                    if has Modifiers.Control && not key.Paste then
                        if scalar >= int 'a' && scalar <= int 'z' then scalar <- scalar - int 'a' + 1
                        elif scalar >= int '@' && scalar <= int '_' then scalar <- scalar - int '@'
                        elif scalar = int ' ' then scalar <- 0
                        elif scalar = int '?' then scalar <- 127
                    alt (Char.ConvertFromUtf32 scalar)
                | Key.Enter -> alt "\r"
                | Key.Tab when has Modifiers.Shift && not key.Paste -> "\u001b[Z"
                | Key.Tab -> alt "\t"
                | Key.Backspace -> alt "\u007f"
                | Key.Escape -> "\u001b"
                | Key.Up -> letter "A"
                | Key.Down -> letter "B"
                | Key.Right -> letter "C"
                | Key.Left -> letter "D"
                | Key.Home -> letter "H"
                | Key.End -> letter "F"
                | Key.Insert -> tilde 2
                | Key.Delete -> tilde 3
                | Key.PageUp -> tilde 5
                | Key.PageDown -> tilde 6
                | Key.F1 | Key.F2 | Key.F3 | Key.F4 ->
                    let final = string (char (int 'P' + int key.Key - int Key.F1))
                    if modifier = 1 then "\u001bO" + final else letter final
                | Key.F5 -> tilde 15
                | Key.F6 -> tilde 17
                | Key.F7 -> tilde 18
                | Key.F8 -> tilde 19
                | Key.F9 -> tilde 20
                | Key.F10 -> tilde 21
                | Key.F11 -> tilde 23
                | Key.F12 -> tilde 24
                | _ -> ""
            | _ -> ""
        if text.Length = 0 then Array.empty else Encoding.UTF8.GetBytes text
