module Ranvier.Terminal.NativeTests

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices

[<DllImport("kernel32.dll", SetLastError = true)>]
extern nativeint GetStdHandle(int handle)

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool GetConsoleMode(nativeint handle, uint32& mode)

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool WriteConsoleInputW(nativeint handle, nativeint buffer, uint32 length, uint32& written)

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool FlushConsoleInputBuffer(nativeint handle)

[<DllImport("kernel32.dll", SetLastError = true)>]
extern uint32 GetConsoleOutputCP()

[<DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)>]
extern bool ReadConsoleOutputCharacterW(nativeint handle, nativeint text, uint32 length, uint32 coordinate, uint32& read)

let run report =
    try
        let originalCodePage = GetConsoleOutputCP()
        let input = GetStdHandle -10
        let output = GetStdHandle -11
        let mutable beforeInput, beforeOutput = 0u, 0u

        if not (GetConsoleMode(input, &beforeInput) && GetConsoleMode(output, &beforeOutput)) then
            failwith "Console mode unavailable."

        let session =
            Terminal.openSession
                { Terminal.defaults with
                    Capabilities =
                        { Terminal.defaults.Capabilities with
                            SynchronizedOutput = true } }

        let mutable allocated = -1L

        try
            if GetConsoleOutputCP() <> 65001u then
                failwith "UTF8 output code page was not selected."

            let mutable duringInput, duringOutput = 0u, 0u

            if not (GetConsoleMode(input, &duringInput) && GetConsoleMode(output, &duringOutput)) then
                failwith "Console mode unavailable after entry."

            if duringOutput &&& 4u = 0u then
                failwith "VT output was not enabled."

            if duringInput &&& 7u <> 0u then
                failwith "Processed/line/echo input was not disabled."

            FlushConsoleInputBuffer input |> ignore
            let record = Marshal.AllocHGlobal 20

            try
                for offset = 0 to 19 do
                    Marshal.WriteByte(record, offset, 0uy)

                Marshal.WriteInt16(record, 0, 1s)
                Marshal.WriteInt32(record, 4, 1)
                Marshal.WriteInt16(record, 8, 2s)
                Marshal.WriteInt16(record, 10, int16 'A')
                Marshal.WriteInt16(record, 14, int16 'a')
                let mutable written = 0u

                if not (WriteConsoleInputW(input, record, 1u, &written)) then
                    failwith "Cannot inject native key event."

                let events = ResizeArray<InputEvent>()
                session.Input.Poll events.Add

                if events.Count <> 2 then
                    failwith "Native key repeat count was not preserved."

                for event in events do
                    match event with
                    | Key key when key.Scalar = int 'a' && not key.Released -> ()
                    | _ -> failwith "Native key decoding mismatch."

                Marshal.WriteInt16(record, 0, 2s)
                Marshal.WriteInt16(record, 4, 3s)
                Marshal.WriteInt16(record, 6, 2s)
                Marshal.WriteInt32(record, 8, 1)
                Marshal.WriteInt32(record, 12, 0)
                Marshal.WriteInt32(record, 16, 0)

                if not (WriteConsoleInputW(input, record, 1u, &written)) then
                    failwith "Cannot inject native mouse event."

                events.Clear()
                session.Input.Poll events.Add

                match events[0] with
                | Mouse mouse when mouse.X = 3 && mouse.Y = 2 && mouse.Action = MouseAction.Press -> ()
                | _ -> failwith "Native mouse decoding mismatch."
            finally
                Marshal.FreeHGlobal record

            let r =
                Renderer.create
                    10
                    2
                    4096
                    { Colors = TrueColor
                      SynchronizedOutput = true }

            Frame.put (Frame.bounds r.Back) 0 0 "é" 1 Paint.empty r.Back
            Renderer.present session.Output r
            let observed = Marshal.AllocHGlobal 8

            try
                for i = 0 to 7 do
                    Marshal.WriteByte(observed, i, 0uy)

                let mutable readCount = 0u
                let success = ReadConsoleOutputCharacterW(output, observed, 1u, 0u, &readCount)
                let codepoint = uint16 (Marshal.ReadInt16 observed)

                if not success || readCount <> 1u || codepoint <> uint16 'é' then
                    failwithf
                        "UTF8 readback mismatch: codepoint=%d count=%d win32=%d"
                        codepoint
                        readCount
                        (Marshal.GetLastPInvokeError())
            finally
                Marshal.FreeHGlobal observed

            let iteration i =
                Frame.put (Frame.bounds r.Back) 0 0 (if i &&& 1 = 0 then "A" else "B") 1 Paint.empty r.Back
                Renderer.present session.Output r

            let warmup = Stopwatch.StartNew()
            let mutable index = 0

            while warmup.Elapsed < TimeSpan.FromSeconds 1. do
                iteration index
                index <- index + 1

            let before = GC.GetAllocatedBytesForCurrentThread()

            for i = 0 to 999 do
                iteration i

            allocated <- GC.GetAllocatedBytesForCurrentThread() - before
        finally
            session.Close()

        if GetConsoleOutputCP() <> originalCodePage then
            failwith "Original code page was not restored."

        let mutable afterInput, afterOutput = 0u, 0u

        if not (GetConsoleMode(input, &afterInput) && GetConsoleMode(output, &afterOutput)) then
            failwith "Console mode unavailable after exit."

        if beforeInput <> afterInput || beforeOutput <> afterOutput then
            failwith "Original console modes were not restored."

        if allocated <> 0L then
            failwithf "Actual console output allocated %d bytes." allocated

        File.WriteAllText(
            report,
            sprintf
                "measured here: native Windows mode entry/restoration, injected key repeats and mouse input passed. Actual console diff/encode/write/flush/commit: 1000 changing frames, %d managed bytes.\n"
                allocated
        )

        0
    with ex ->
        File.WriteAllText(report, ex.ToString())
        1
