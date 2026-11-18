namespace Ranvier.Terminal

open System
open System.Text
open Ranvier.Keyboard

[<Flags>]
type Modifiers =
    | None = 0
    | Shift = 1
    | Alt = 2
    | Control = 4
    | Super = 8

type Key =
    | Character = 0
    | Escape = 1
    | Enter = 2
    | Tab = 3
    | Backspace = 4
    | Up = 5
    | Down = 6
    | Left = 7
    | Right = 8
    | Home = 9
    | End = 10
    | PageUp = 11
    | PageDown = 12
    | Insert = 13
    | Delete = 14
    | F1 = 15
    | F2 = 16
    | F3 = 17
    | F4 = 18
    | F5 = 19
    | F6 = 20
    | F7 = 21
    | F8 = 22
    | F9 = 23
    | F10 = 24
    | F11 = 25
    | F12 = 26
    | Alt = 27

[<Struct>]
type KeyEvent =
    { Key: Key
      Scalar: int
      Modifiers: Modifiers
      Released: bool
      Paste: bool }

    member this.LogicalKey =
        match this.Key with
        | Key.Character when this.Scalar = 32 -> Whitespace Space
        | Key.Character when Rune.IsValid this.Scalar -> Character (Rune this.Scalar)
        | Key.Enter -> Whitespace Enter
        | Key.Tab -> Whitespace Tab
        | Key.Escape -> Ui Escape
        | Key.Backspace -> Editing EditingKey.Backspace
        | Key.Delete -> Editing EditingKey.Delete
        | Key.Insert -> Editing EditingKey.Insert
        | Key.Alt -> Modifier Alt
        | Key.Left -> Navigation ArrowLeft
        | Key.Right -> Navigation ArrowRight
        | Key.Up -> Navigation ArrowUp
        | Key.Down -> Navigation ArrowDown
        | Key.Home -> Navigation Home
        | Key.End -> Navigation End
        | Key.PageUp -> Navigation PageUp
        | Key.PageDown -> Navigation PageDown
        | k when k >= Key.F1 && k <= Key.F12 -> FunctionKey (int k - int Key.F1 + 1)
        | _ -> Unidentified ""

    member this.Chord =
        let flags = this.Modifiers
        let modifiers =
            { KeyModifiers.None with
                Ctrl = flags &&& Modifiers.Control <> Modifiers.None
                Alt = flags &&& Modifiers.Alt <> Modifiers.None
                Shift = flags &&& Modifiers.Shift <> Modifiers.None
                Meta = flags &&& Modifiers.Super <> Modifiers.None }
        match this.LogicalKey with
        | Character rune when not modifiers.Ctrl && not modifiers.Alt && not modifiers.Meta ->
            // The console has already applied the active layout, including Shift.
            KeyChord.ofRune rune
        | logical -> KeyChord.ofLogical logical modifiers

type MouseAction =
    | Press = 0
    | Release = 1
    | Move = 2
    | Scroll = 3

[<Struct>]
type MouseEvent =
    { X: int
      Y: int
      Button: int
      Action: MouseAction
      Delta: int
      Modifiers: Modifiers }

[<Struct>]
type InputEvent =
    | Key of key: KeyEvent
    | Mouse of mouse: MouseEvent
    | Focus of focused: bool
    | PasteBoundary of pasting: bool
    | Resize of dimensions: struct (int * int)
    | SynchronizedOutputSupport of supported: bool

type InputParser =
    { Buffer: byte array
      Parameters: int array
      mutable Count: int
      mutable State: int
      mutable Expected: int
      mutable Pasting: bool
      Emit: InputEvent -> unit }

module InputParser =
    let create emit =
        { Buffer = Array.zeroCreate 128
          Parameters = Array.zeroCreate 16
          Count = 0
          State = 0
          Expected = 0
          Pasting = false
          Emit = emit }

    let private key code scalar modifiers released parser =
        parser.Emit(
            Key
                { Key = code
                  Scalar = scalar
                  Modifiers = modifiers
                  Released = released
                  Paste = parser.Pasting }
        )

    let private scalar n modifiers parser =
        match n with
        | 27 -> key Key.Escape 0 modifiers false parser
        | 10
        | 13 -> key Key.Enter 0 modifiers false parser
        | 9 -> key Key.Tab 0 modifiers false parser
        | 8
        | 127 -> key Key.Backspace 0 modifiers false parser
        | n when n < 32 -> key Key.Character (n + 96) (modifiers ||| Modifiers.Control) false parser
        | n -> key Key.Character n modifiers false parser

    let private csi final parser =
        Array.Clear parser.Parameters
        let mutable slot = 0
        let mutable privateMode = false
        let mutable mouse = false

        for i = 0 to parser.Count - 1 do
            let c = parser.Buffer[i]

            if c = byte '?' then
                privateMode <- true
            elif c = byte '<' then
                mouse <- true
            elif c = byte ';' || c = byte ':' then
                slot <- min (parser.Parameters.Length - 1) (slot + 1)
            elif c >= byte '0' && c <= byte '9' then
                parser.Parameters[slot] <- min 1000000 (parser.Parameters[slot] * 10 + int c - 48)

        let p = parser.Parameters
        let modifiers = enum<Modifiers> (max 0 (p[1] - 1) &&& 15)

        if mouse && (final = byte 'M' || final = byte 'm') then
            let code = p[0]

            let mods =
                (if code &&& 4 <> 0 then Modifiers.Shift else Modifiers.None)
                ||| (if code &&& 8 <> 0 then Modifiers.Alt else Modifiers.None)
                ||| (if code &&& 16 <> 0 then
                         Modifiers.Control
                     else
                         Modifiers.None)

            let action =
                if code &&& 64 <> 0 then MouseAction.Scroll
                elif final = byte 'm' then MouseAction.Release
                elif code &&& 32 <> 0 then MouseAction.Move
                else MouseAction.Press

            parser.Emit(
                Mouse
                    { X = max 0 (p[1] - 1)
                      Y = max 0 (p[2] - 1)
                      Button = code &&& 3
                      Action = action
                      Delta =
                        if code &&& 64 = 0 then 0
                        elif code &&& 1 = 0 then -1
                        else 1
                      Modifiers = mods }
            )
        elif privateMode && final = byte 'y' && p[0] = 2026 then
            parser.Emit(SynchronizedOutputSupport(p[1] = 1 || p[1] = 2))
        else
            match char final with
            | 'A' -> key Key.Up 0 modifiers false parser
            | 'B' -> key Key.Down 0 modifiers false parser
            | 'C' -> key Key.Right 0 modifiers false parser
            | 'D' -> key Key.Left 0 modifiers false parser
            | 'H' -> key Key.Home 0 modifiers false parser
            | 'F' -> key Key.End 0 modifiers false parser
            | 'Z' -> key Key.Tab 0 Modifiers.Shift false parser
            | 'I' -> parser.Emit(Focus true)
            | 'O' -> parser.Emit(Focus false)
            | 'u' when not privateMode ->
                let code =
                    match p[0] with
                    | 27 -> Key.Escape
                    | 13 -> Key.Enter
                    | 9 -> Key.Tab
                    | 127 -> Key.Backspace
                    | 57350 -> Key.Left
                    | 57351 -> Key.Right
                    | 57352 -> Key.Up
                    | 57353 -> Key.Down
                    | 57354 -> Key.PageUp
                    | 57355 -> Key.PageDown
                    | 57356 -> Key.Home
                    | 57357 -> Key.End
                    | 57443 | 57449 -> Key.Alt
                    | _ -> Key.Character

                key code p[0] modifiers (p[2] = 3) parser
            | '~' ->
                if p[0] = 200 then
                    parser.Pasting <- true
                    parser.Emit(PasteBoundary true)
                elif p[0] = 201 then
                    parser.Pasting <- false
                    parser.Emit(PasteBoundary false)
                else
                    let code =
                        match p[0] with
                        | 1
                        | 7 -> Key.Home
                        | 2 -> Key.Insert
                        | 3 -> Key.Delete
                        | 4
                        | 8 -> Key.End
                        | 5 -> Key.PageUp
                        | 6 -> Key.PageDown
                        | 11 -> Key.F1
                        | 12 -> Key.F2
                        | 13 -> Key.F3
                        | 14 -> Key.F4
                        | 15 -> Key.F5
                        | 17 -> Key.F6
                        | 18 -> Key.F7
                        | 19 -> Key.F8
                        | 20 -> Key.F9
                        | 21 -> Key.F10
                        | 23 -> Key.F11
                        | 24 -> Key.F12
                        | _ -> Key.Character

                    if code <> Key.Character then
                        key code 0 modifiers false parser
            | _ -> ()

    let rec feed value parser =
        match parser.State with
        | 0 ->
            if value = 27uy then
                parser.State <- 1
            elif value < 128uy then
                scalar (int value) Modifiers.None parser
            elif value >= 194uy && value <= 244uy then
                parser.State <- 4
                parser.Count <- 1
                parser.Buffer[0] <- value

                parser.Expected <-
                    if value < 224uy then 2
                    elif value < 240uy then 3
                    else 4
            else
                scalar 0xFFFD Modifiers.None parser
        | 1 ->
            parser.Count <- 0

            if value = byte '[' then
                parser.State <- 2
            elif value = byte 'O' && not parser.Pasting then
                parser.State <- 3
            else
                parser.State <- 0

                if parser.Pasting then
                    scalar 27 Modifiers.None parser
                    feed value parser
                elif value < 128uy then
                    scalar (int value) Modifiers.Alt parser
                else
                    scalar 27 Modifiers.None parser
                    feed value parser
        | 2 ->
            if value >= 64uy && value <= 126uy then
                parser.State <- 0

                if parser.Pasting then
                    if
                        value = byte '~'
                        && parser.Count = 3
                        && parser.Buffer[0] = byte '2'
                        && parser.Buffer[1] = byte '0'
                        && parser.Buffer[2] = byte '1'
                    then
                        parser.Pasting <- false
                        parser.Emit(PasteBoundary false)
                    else
                        scalar 27 Modifiers.None parser
                        scalar (int '[') Modifiers.None parser

                        for i = 0 to parser.Count - 1 do
                            scalar (int parser.Buffer[i]) Modifiers.None parser

                        scalar (int value) Modifiers.None parser
                else
                    csi value parser

                parser.Count <- 0
            elif parser.Count = parser.Buffer.Length then
                parser.State <- 5
                parser.Count <- 0
            else
                parser.Buffer[parser.Count] <- value
                parser.Count <- parser.Count + 1
        | 3 ->
            parser.State <- 0

            let code =
                match char value with
                | 'P' -> Key.F1
                | 'Q' -> Key.F2
                | 'R' -> Key.F3
                | 'S' -> Key.F4
                | 'A' -> Key.Up
                | 'B' -> Key.Down
                | 'C' -> Key.Right
                | 'D' -> Key.Left
                | 'H' -> Key.Home
                | 'F' -> Key.End
                | _ -> Key.Character

            if code <> Key.Character then
                key code 0 Modifiers.None false parser
        | 4 ->
            if value &&& 192uy <> 128uy then
                parser.State <- 0
                scalar 0xFFFD Modifiers.None parser
                feed value parser
            else
                parser.Buffer[parser.Count] <- value
                parser.Count <- parser.Count + 1

                if parser.Count = parser.Expected then
                    let mutable rune = Rune.ReplacementChar
                    let mutable consumed = 0

                    Rune.DecodeFromUtf8(ReadOnlySpan(parser.Buffer, 0, parser.Count), &rune, &consumed)
                    |> ignore

                    parser.State <- 0
                    scalar rune.Value Modifiers.None parser
        | _ ->
            if value >= 64uy && value <= 126uy then
                parser.State <- 0

    let feedBytes (bytes: byte array) offset count parser =
        if offset < 0 || count < 0 || offset > bytes.Length - count then
            invalidArg "count" "Invalid byte range."

        for i = offset to offset + count - 1 do
            feed bytes[i] parser

    let flushEscape parser =
        if parser.State = 1 then
            parser.State <- 0
            scalar 27 Modifiers.None parser
