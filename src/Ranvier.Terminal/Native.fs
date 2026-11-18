namespace Ranvier.Terminal

open System
open System.ComponentModel
open System.Runtime.InteropServices
open System.Threading

type NativeInput =
    { Poll: (InputEvent -> unit) -> unit
      Close: unit -> unit }

module internal Native =
    type Waiter =
        { Wake: unit -> unit
          Wait: int -> unit
          Close: unit -> unit }

    type ParserInput =
        { Feed: byte[] -> int -> unit
          PollEscape: unit -> unit
          NextWait: unit -> int }

    let createParserInput clock emit =
        let parser = InputParser.create emit
        let mutable escapeAt = 0L
        // Bytes can arrive between any wake and Poll, so only the parser owns this deadline.
        let nextWait () =
            if parser.State = 1 then int (max 0L (30L - (clock () - escapeAt)))
            else -1
        { Feed = fun bytes count ->
              if count > 0 then
                  InputParser.feedBytes bytes 0 count parser
                  if parser.State = 1 then escapeAt <- clock ()
          PollEscape = fun () -> if nextWait () = 0 then InputParser.flushEscape parser
          NextWait = nextWait }

    let parserTimeout requested nextParserWait =
        let parserDelay = nextParserWait ()
        if requested < 0 then parserDelay
        elif parserDelay < 0 then requested
        else min requested parserDelay

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern uint32 WaitForMultipleObjects(uint32 count, nativeint[] handles, bool all, uint32 timeout)

    [<DllImport("libc", SetLastError = true)>]
    extern int pipe(int[] descriptors)

    [<DllImport("libc", SetLastError = true)>]
    extern int fcntl(int descriptor, int command, int flags)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint write(int descriptor, byte[] buffer, unativeint count)

    [<DllImport("libc")>]
    extern int close(int descriptor)

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

    [<DllImport("libc", EntryPoint = "poll", SetLastError = true)>]
    extern int pollLinux(nativeint fds, unativeint count, int timeout)

    [<DllImport("libc", EntryPoint = "poll", SetLastError = true)>]
    extern int pollMac(nativeint fds, uint32 count, int timeout)

    let private poll (fds, count, timeout) =
        if OperatingSystem.IsMacOS() then pollMac(fds, uint32 count, timeout)
        else pollLinux(fds, count, timeout)

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
                            let code =
                                if (virtualKey = 0x12 || virtualKey = 0xA4 || virtualKey = 0xA5) && character = '\000' then Key.Alt
                                else windowsKey virtualKey

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

    let private unixWithDeadline () =
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
        let parser = createParserInput (fun () -> Environment.TickCount64) (fun event -> destination event)

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
                            parser.Feed buffer count
                    elif flags &&& 24 <> 0 then
                        raise (System.IO.EndOfStreamException())

                parser.PollEscape() }, parser.NextWait

    let unix () = unixWithDeadline () |> fst

    let openInputWithDeadline () =
        if OperatingSystem.IsWindows() then windows (), (fun () -> -1)
        else unixWithDeadline ()

    let openInput () =
        if OperatingSystem.IsWindows() then windows () else unix ()

    // Only the owning host waits; wakes may arrive concurrently from graph posts or cancellation.
    let createWaiter nextParserWait =
        let gate = obj ()
        let mutable closed = false
        if OperatingSystem.IsWindows() then
            let wake = new AutoResetEvent(false)
            let handles = [| GetStdHandle -10; wake.SafeWaitHandle.DangerousGetHandle() |]
            { Wake = fun () -> lock gate (fun () -> if not closed then wake.Set() |> ignore)
              Wait = fun timeout ->
                  let result = WaitForMultipleObjects(2u, handles, false, uint32 timeout)
                  if result = UInt32.MaxValue then check false
              Close = fun () -> lock gate (fun () -> if not closed then closed <- true; wake.Dispose()) }
        else
            let descriptors = Array.zeroCreate<int> 2
            unixCheck (pipe descriptors)
            let mutable pollDescriptors = 0n
            let mutable resize: PosixSignalRegistration = null
            try
                let nonblocking = if OperatingSystem.IsMacOS() then 4 else 2048
                for descriptor in descriptors do
                    let flags = fcntl(descriptor, 3, 0)
                    unixCheck flags
                    unixCheck (fcntl(descriptor, 4, flags ||| nonblocking))
                pollDescriptors <- Marshal.AllocHGlobal 16
                for offset, descriptor in [ 0, 0; 8, descriptors[0] ] do
                    Marshal.WriteInt32(pollDescriptors, offset, descriptor)
                    Marshal.WriteInt16(pollDescriptors, offset + 4, 1s)
                    Marshal.WriteInt16(pollDescriptors, offset + 6, 0s)
                let notification = [| 1uy |]
                let drain = Array.zeroCreate<byte> 256
                let wake () =
                    lock gate (fun () ->
                        if not closed then
                            // A full nonblocking pipe already contains a wake notification.
                            let mutable result = write(descriptors[1], notification, 1un)
                            while result < 0n && Marshal.GetLastPInvokeError() = 4 do
                                result <- write(descriptors[1], notification, 1un))
                resize <- PosixSignalRegistration.Create(PosixSignal.SIGWINCH, fun context -> context.Cancel <- true; wake ())
                { Wake = wake
                  Wait = fun timeout ->
                      // Poll may consume input after any wake reason; ask its actual parser state.
                      let delay = parserTimeout timeout nextParserWait
                      let ready = poll(pollDescriptors, 2un, delay)
                      if ready < 0 && Marshal.GetLastPInvokeError() <> 4 then unixCheck ready
                      if ready > 0 && int (Marshal.ReadInt16(pollDescriptors, 14)) &&& 1 <> 0 then
                          while int (read(descriptors[0], drain, unativeint drain.Length)) = drain.Length do ()
                  Close = fun () ->
                      // Dispose outside gate: an in-flight signal callback may be waiting for it.
                      resize.Dispose()
                      lock gate (fun () ->
                          if not closed then
                              closed <- true
                              close descriptors[0] |> ignore
                              close descriptors[1] |> ignore
                              Marshal.FreeHGlobal pollDescriptors) }
            with _ ->
                if not (isNull resize) then resize.Dispose()
                if pollDescriptors <> 0n then Marshal.FreeHGlobal pollDescriptors
                close descriptors[0] |> ignore
                close descriptors[1] |> ignore
                reraise ()
