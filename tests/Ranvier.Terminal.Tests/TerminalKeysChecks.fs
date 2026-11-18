module TerminalKeysChecks

open System.Text
open Ranvier.Terminal

let run () =
    let key code scalar modifiers released paste =
        Key { Key=code; Scalar=scalar; Modifiers=modifiers; Released=released; Paste=paste }
    let encode event = TerminalKeys.encode true event |> Encoding.UTF8.GetString
    let expect expected event =
        let actual = encode event
        if actual <> expected then failwithf "Input encoding: expected %A, got %A" expected actual
    expect "\t" (key Key.Tab 0 Modifiers.None false false)
    expect "\u001b[Z" (key Key.Tab 0 Modifiers.Shift false false)
    expect "\u0003" (key Key.Character (int 'c') Modifiers.Control false false)
    expect "\u001b\u0003" (key Key.Character (int 'C') (Modifiers.Control ||| Modifiers.Alt) false false)
    expect "\u001b[1;6A" (key Key.Up 0 (Modifiers.Control ||| Modifiers.Shift) false false)
    expect "👩" (key Key.Character 0x1F469 Modifiers.None false false)
    expect "" (key Key.Character 0xD800 Modifiers.None false false)
    expect "" (key Key.Enter 0 Modifiers.None true false)
    expect "\r" (key Key.Enter 0 Modifiers.None false false)
    expect "\u001b[15~" (key Key.F5 0 Modifiers.None false false)
    expect "\u001b[24;3~" (key Key.F12 0 Modifiers.Alt false false)
    expect "c" (key Key.Character (int 'c') Modifiers.Control false true)
    expect "\u001b[200~" (PasteBoundary true)
    expect "\u001b[201~" (PasteBoundary false)
    if TerminalKeys.encode false (PasteBoundary true) <> [||] then failwith "Disabled paste framing"
