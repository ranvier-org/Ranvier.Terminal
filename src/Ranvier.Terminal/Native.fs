namespace Ranvier.Terminal

open System
open System.ComponentModel
open System.Runtime.InteropServices

type NativeInput =
    { Poll: (InputEvent -> unit) -> unit
      Close: unit -> unit }

module internal Native =
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern nativeint GetStdHandle(int handle)

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool GetConsoleMode(nativeint handle, uint32& mode)

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool SetConsoleMode(nativeint handle, uint32 mode)

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool GetNumberOfConsoleInputEvents(nativeint handle, uint32& count)

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool ReadConsoleInputW(nativeint handle, nativeint buffer, uint32 length, uint32& count)

    [<DllImport("libc", SetLastError = true)>]
    extern int tcgetattr(int fd, nativeint attributes)

    [<DllImport("libc", SetLastError = true)>]
    extern int tcsetattr(int fd, int action, nativeint attributes)

    [<DllImport("libc")>]
    extern void cfmakeraw(nativeint attributes)

    [<DllImport("libc", SetLastError = true)>]
    extern int poll(nativeint fds, unativeint count, int timeout)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint read(int fd, byte[] buffer, unativeint count)

    let private check result =
        if not result then
            raise (Win32Exception(Marshal.GetLastPInvokeError()))

    let private unixCheck result =
        if result < 0 then
            raise (Win32Exception(Marshal.GetLastPInvokeError()))

    let private modifiers bits =
        (if bits &&& 16 <> 0 then Modifiers.Shift else Modifiers.None)
        ||| (if bits &&& 3 <> 0 then Modifiers.Alt else Modifiers.None)
        ||| (if bits &&& 12 <> 0 then
                 Modifiers.Control
             else
                 Modifiers.None)

    let private windowsKey code =
        match code with
        | 27 -> Key.Escape
        | 13 -> Key.Enter
        | 9 -> Key.Tab
        | 8 -> Key.Backspace
        | 37 -> Key.Left
        | 38 -> Key.Up
        | 39 -> Key.Right
        | 40 -> Key.Down
        | 36 -> Key.Home
        | 35 -> Key.End
        | 33 -> Key.PageUp
        | 34 -> Key.PageDown
        | 45 -> Key.Insert
        | 46 -> Key.Delete
        | n when n >= 112 && n <= 123 -> enum<Key> (int Key.F1 + n - 112)
        | _ -> Key.Character

    let windows () =
        let input = GetStdHandle -10
        let output = GetStdHandle -11
        let mutable inputMode, outputMode = 0u, 0u
        check (GetConsoleMode(input, &inputMode))
        check (GetConsoleMode(output, &outputMode))
        let buffer = Marshal.AllocHGlobal 32

        try
            check (SetConsoleMode(output, outputMode ||| 4u))
            check (SetConsoleMode(input, (inputMode ||| 0x98u) &&& ~~~0x247u))
        with _ ->
            SetConsoleMode(input, inputMode) |> ignore
            SetConsoleMode(output, outputMode) |> ignore
            Marshal.FreeHGlobal buffer
            reraise ()

        let mutable closed = false
        let mutable highSurrogate = '\000'
        let mutable previousButtons = 0

        { Close =
            fun () ->
                if not closed then
                    closed <- true
                    SetConsoleMode(input, inputMode) |> ignore
                    SetConsoleMode(output, outputMode) |> ignore
                    Marshal.FreeHGlobal buffer
          Poll =
            fun emit ->
                if closed then
                    invalidOp "Input session is closed."

                let mutable available = 0u
                check (GetNumberOfConsoleInputEvents(input, &available))
                let mutable processed = 0u

                while processed < min 256u available do
                    let mutable count = 0u
                    check (ReadConsoleInputW(input, buffer, 1u, &count))
                    processed <- processed + 1u

                    if count > 0u then
                        match int (Marshal.ReadInt16(buffer, 0)) with
                        | 1 ->
                            let down = Marshal.ReadInt32(buffer, 4) <> 0
                            let repeats = max 1 (int (Marshal.ReadInt16(buffer, 8)))
                            let virtualKey = int (Marshal.ReadInt16(buffer, 10))
                            let character = char (uint16 (Marshal.ReadInt16(buffer, 14)))
                            let mods = modifiers (Marshal.ReadInt32(buffer, 16))
                            let code = windowsKey virtualKey

                            if Char.IsHighSurrogate character then
                                highSurrogate <- character
                            else
                                let mutable scalar = int character

                                if Char.IsLowSurrogate character then
                                    scalar <-
                                        if highSurrogate <> '\000' then
                                            Char.ConvertToUtf32(highSurrogate, character)
                                        else
                                            0xFFFD

                                highSurrogate <- '\000'

                                if scalar > 0 && scalar < 32 && code = Key.Character then
                                    scalar <- scalar + 96

                                if scalar <> 0 || code <> Key.Character then
                                    for _ = 1 to (if down then repeats else 1) do
                                        emit (
                                            Key
                                                { Key = code
                                                  Scalar = scalar
                                                  Modifiers = mods
                                                  Released = not down
                                                  Paste = false }
                                        )
                        | 2 ->
                            let buttons = Marshal.ReadInt32(buffer, 8)
                            let flags = Marshal.ReadInt32(buffer, 16)
                            let changed = (buttons ^^^ previousButtons) &&& 31
                            let active = if changed <> 0 then changed else buttons &&& 31

                            let button =
                                if active &&& 1 <> 0 then 0
                                elif active &&& 2 <> 0 then 2
                                elif active &&& 4 <> 0 then 1
                                else 0

                            let action =
                                if flags &&& 12 <> 0 then MouseAction.Scroll
                                elif flags &&& 1 <> 0 then MouseAction.Move
                                elif buttons &&& active = 0 then MouseAction.Release
                                else MouseAction.Press

                            emit (
                                Mouse
                                    { X = int (Marshal.ReadInt16(buffer, 4))
                                      Y = int (Marshal.ReadInt16(buffer, 6))
                                      Button = button
                                      Action = action
                                      Delta =
                                        if flags &&& 12 = 0 then 0
                                        elif buttons >>> 16 > 0 then -1
                                        else 1
                                      Modifiers = modifiers (Marshal.ReadInt32(buffer, 12)) }
                            )

                            previousButtons <- buttons &&& 31
                        | 4 ->
                            emit (
                                Resize(struct (int (Marshal.ReadInt16(buffer, 4)), int (Marshal.ReadInt16(buffer, 6))))
                            )
                        | 16 -> emit (Focus(Marshal.ReadInt32(buffer, 4) <> 0))
                        | _ -> () }

    let unix () =
        if not (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) then
            raise (PlatformNotSupportedException "The native input host supports Windows, Linux, and macOS.")

        let saved = Marshal.AllocHGlobal 256
        let raw = Marshal.AllocHGlobal 256
        let descriptor = Marshal.AllocHGlobal 8
        let buffer = Array.zeroCreate<byte> 256
        let mutable captured = false

        try
            unixCheck (tcgetattr (0, saved))
            captured <- true
            unixCheck (tcgetattr (0, raw))
            cfmakeraw raw
            unixCheck (tcsetattr (0, 0, raw))
            Marshal.WriteInt32(descriptor, 0, 0)
            Marshal.WriteInt16(descriptor, 4, 1s)
            Marshal.WriteInt16(descriptor, 6, 0s)
        with _ ->
            if captured then
                tcsetattr (0, 0, saved) |> ignore

            Marshal.FreeHGlobal saved
            Marshal.FreeHGlobal raw
            Marshal.FreeHGlobal descriptor
            reraise ()

        let mutable closed = false
        let mutable destination = ignore
        let parser = InputParser.create (fun event -> destination event)
        let mutable escapeAt = 0L

        { Close =
            fun () ->
                if not closed then
                    closed <- true
                    tcsetattr (0, 0, saved) |> ignore
                    Marshal.FreeHGlobal saved
                    Marshal.FreeHGlobal raw
                    Marshal.FreeHGlobal descriptor
          Poll =
            fun emit ->
                if closed then
                    invalidOp "Input session is closed."

                destination <- emit
                let ready = poll (descriptor, 1un, 0)

                if ready < 0 && Marshal.GetLastPInvokeError() <> 4 then
                    unixCheck ready

                if ready > 0 then
                    let flags = int (Marshal.ReadInt16(descriptor, 6))

                    if flags &&& 1 <> 0 then
                        let count = int (read (0, buffer, unativeint buffer.Length))

                        if count < 0 && Marshal.GetLastPInvokeError() <> 4 then
                            unixCheck count

                        if count = 0 then
                            raise (System.IO.EndOfStreamException())

                        if count > 0 then
                            InputParser.feedBytes buffer 0 count parser

                        if parser.State = 1 then
                            escapeAt <- Environment.TickCount64
                    elif flags &&& 24 <> 0 then
                        raise (System.IO.EndOfStreamException())

                if parser.State = 1 && Environment.TickCount64 - escapeAt >= 30L then
                    InputParser.flushEscape parser }

    let openInput () =
        if OperatingSystem.IsWindows() then windows () else unix ()
