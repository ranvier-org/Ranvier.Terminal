module PreparedBenchNative

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open Ranvier.Terminal

[<Struct;StructLayout(LayoutKind.Sequential)>]
type Coord =
    val X: int16
    val Y: int16
    new(x,y) = {X=x;Y=y}

[<DllImport("kernel32.dll")>]
extern nativeint GetStdHandle(int value)
[<DllImport("kernel32.dll")>]
extern bool GetConsoleMode(nativeint handle, uint32& mode)
[<DllImport("kernel32.dll")>]
extern bool SetConsoleMode(nativeint handle, uint32 mode)
[<DllImport("kernel32.dll",CharSet=CharSet.Unicode)>]
extern bool ReadConsoleOutputCharacter(nativeint handle, StringBuilder text, uint32 count, Coord position, uint32& read)

let run path =
    let rows = ResizeArray<string>()
    rows.Add "workload,median_us,max_batch_us,bytes,allocated_bytes"
    let handle = GetStdHandle -11
    let mutable original = 0u
    if not (GetConsoleMode(handle,&original)) then failwith "Hidden interactive console required"
    let encoding = Console.OutputEncoding
    try
        if not (SetConsoleMode(handle,(original ||| 4u) &&& ~~~2u)) then failwith "Cannot enable VT output"
        Console.OutputEncoding <- UTF8Encoding(false)
        Console.Write "\u001b[?1049h\u001b[?7l\u001b[2J"
        Console.Out.Flush()
        use stream = Console.OpenStandardOutput()
        for native in [false;true] do
            for scroll in [false;true] do
                let caps = {Colors=TrueColor;SynchronizedOutput=false}
                let budget = {Glyphs=128;Paints=8;Utf8Bytes=4096;OutputBytes=65536}
                let r = PreparedRenderer.create 80 20 budget caps
                let glyphs = Array.init 26 (fun i -> PreparedRenderer.prepareGlyph (string (char (65+i))) 1 r)
                let paint = PreparedRenderer.preparePaint Paint.empty r
                let clip = PreparedRenderer.bounds r
                let output = {Write=(fun bytes offset count -> if native then stream.Write(bytes,offset,count));Flush=(fun () -> if native then stream.Flush())}
                let policy = {FullWidthRegions=true}
                for y = 0 to 19 do
                    for x = 0 to 79 do PreparedRenderer.put clip x y glyphs[y] paint r
                PreparedRenderer.present output r
                let mutable serial = 0
                let frame () =
                    PreparedRenderer.scrollRows clip 1 paint r
                    for x = 0 to 79 do PreparedRenderer.put clip x 19 glyphs[serial%26] paint r
                    serial <- serial+1
                    if scroll then PreparedRenderer.presentScrolled policy output clip 1 r
                    else PreparedRenderer.present output r
                let timer = Stopwatch.StartNew()
                while timer.ElapsedMilliseconds < 500L do frame()
                let iterations = if native then 250 else 10000
                let batches = Array.zeroCreate<float> 7
                let mutable allocated = 0L
                for batch = 0 to batches.Length-1 do
                    let before = GC.GetAllocatedBytesForCurrentThread()
                    timer.Restart()
                    for _ = 1 to iterations do frame()
                    timer.Stop()
                    batches[batch] <- timer.Elapsed.TotalMicroseconds/float iterations
                    allocated <- allocated+GC.GetAllocatedBytesForCurrentThread()-before
                if native then
                    for y = 0 to 19 do
                        let actual = StringBuilder(80)
                        let mutable read = 0u
                        if not (ReadConsoleOutputCharacter(handle,actual,80u,Coord(0s,int16 y),&read)) || read <> 80u then failwith "Native readback failed"
                        let expected = String.Concat(Array.init 80 (fun x -> (PreparedRenderer.cell x y true r).Glyph))
                        if actual.ToString(0,int read) <> expected then failwith "Native scrolling did not match accepted framebuffer"
                Array.sortInPlace batches
                rows.Add(sprintf "%s-%s,%.3f,%.3f,%d,%.3f" (if scroll then "scroll" else "diff") (if native then "native" else "null") batches[3] batches[6] (PreparedRenderer.byteCount r) (float allocated/float (iterations*7)))
    finally
        Console.Write "\u001b[r\u001b[0m\u001b[?7h\u001b[?25h\u001b[?1049l"
        Console.Out.Flush()
        Console.OutputEncoding <- encoding
        SetConsoleMode(handle,original) |> ignore
        File.WriteAllLines(path,rows)
    0
