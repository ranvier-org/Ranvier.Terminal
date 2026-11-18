module Ranvier.Terminal.OutputBufferChecks

open System
open System.Text

let private check value message = if not value then failwith message
let private row y (snapshot: OutputSnapshot) =
    let text = StringBuilder()
    for x = 0 to snapshot.Frame.Width-1 do
        let cell = snapshot.Frame.Cells[y*snapshot.Frame.Width+x]
        if cell.Width > 0uy then text.Append cell.Glyph |> ignore
    text.ToString()

let private same (a: OutputSnapshot) (b: OutputSnapshot) =
    check (a.Frame.Width=b.Frame.Width && a.Frame.Height=b.Frame.Height) "Snapshot size differs"
    check (Array.forall2 Cell.equal a.Frame.Cells b.Frame.Cells) "Chunking changed output cells"
    check (a.CursorX=b.CursorX && a.CursorY=b.CursorY) "Chunking changed cursor"
    check (a.Title=b.Title && a.HistoryLines=b.HistoryLines && a.TotalScrolledLines=b.TotalScrolledLines) "Chunking changed metadata"

let run () =
    let rejected action =
        let mutable caught=false
        try action() with :? ArgumentException -> caught <- true
        check caught "Invalid buffer argument was accepted"
    rejected (fun () -> OutputBuffer.create 0 2 2 |> ignore)
    rejected (fun () -> OutputBuffer.create 2 0 2 |> ignore)
    rejected (fun () -> OutputBuffer.create 2 2 -1 |> ignore)
    rejected (fun () -> OutputBuffer.create Int32.MaxValue 2 0 |> ignore)
    let literal = OutputBuffer.create 12 3 4
    rejected (fun () -> OutputBuffer.resize 0 3 literal)
    rejected (fun () -> OutputBuffer.feedUtf8 [|1uy|] 1 1 literal)
    OutputBuffer.feedText "10%\r20%\nwide 界e" literal
    OutputBuffer.feedText "\u0301 👩" literal
    OutputBuffer.feedText "‍💻\n\u001b[31m" literal
    let literalShot = OutputBuffer.snapshot 0 literal
    check ((row 0 literalShot).StartsWith "20%") "Carriage-return progress replacement"
    check (literalShot.Frame.Cells |> Array.forall (fun c -> not (c.Glyph.Contains '\u001b'))) "Literal text injected escape"

    let stream = "start\r\n\u001b[31mred\u001b[0m é 界 👩‍💻\r\n\u001b[2;3Hxy\u001b[K\u001b]2;A title\u0007\u001b[6n"
    let bytes = Encoding.UTF8.GetBytes stream
    let whole = OutputBuffer.create 24 5 3
    OutputBuffer.feedUtf8 bytes 0 bytes.Length whole
    OutputBuffer.finish whole
    let expected = OutputBuffer.snapshot 0 whole
    for split = 0 to bytes.Length do
        let fragmented = OutputBuffer.create 24 5 3
        OutputBuffer.feedUtf8 bytes 0 split fragmented
        OutputBuffer.feedUtf8 bytes split (bytes.Length-split) fragmented
        OutputBuffer.finish fragmented
        same expected (OutputBuffer.snapshot 0 fragmented)
    let single = OutputBuffer.create 24 5 3
    for i = 0 to bytes.Length-1 do OutputBuffer.feedUtf8 bytes i 1 single
    same expected (OutputBuffer.snapshot 0 single)
    check (expected.Title="A title") "OSC title missing"
    check (Encoding.ASCII.GetString(OutputBuffer.drainResponses whole).Contains "R") "Cursor query response missing"
    check ((OutputBuffer.drainResponses whole).Length=0) "Query responses were not drained"

    let history = OutputBuffer.create 8 2 3
    for i = 0 to 19 do OutputBuffer.feedText (string i+"\n") history
    let tail = OutputBuffer.snapshot 0 history
    check (tail.HistoryLines=3 && tail.TotalScrolledLines=19L) "History bound or monotonic counter incorrect"
    let old = OutputBuffer.snapshot 3 history
    check (not old.CursorVisible && old.ScrollOffset=3) "Manual history cursor/offset incorrect"
    let detached = Array.copy old.Frame.Cells
    OutputBuffer.feedText "later\n" history
    check (Array.forall2 Cell.equal detached old.Frame.Cells) "Snapshot aliases mutable buffer"
    OutputBuffer.resize 4 3 history
    let resized = OutputBuffer.snapshot 100 history
    check (resized.Frame.Width=4 && resized.Frame.Height=3 && resized.HistoryLines<=3 && resized.ScrollOffset<=3) "Resize/history limits"
    OutputBuffer.clear history
    let cleared = OutputBuffer.snapshot 0 history
    check (cleared.HistoryLines=0 && cleared.TotalScrolledLines=0L && cleared.CursorX=0 && cleared.CursorY=0) "Explicit clear reset"

    let terminal = OutputBuffer.create 10 4 2
    let feed (text: string) = let b=Encoding.UTF8.GetBytes text in OutputBuffer.feedUtf8 b 0 b.Length terminal
    feed "main\u001b[?1049hALT\u001b[?2004h\u001b[?25l"
    let alt=OutputBuffer.snapshot 0 terminal
    check (alt.AlternateScreen && alt.BracketedPaste && not alt.CursorVisible && (row 0 alt).StartsWith "ALT") "Alternate screen/modes"
    feed "\u001b[?1049l"
    check ((row 0 (OutputBuffer.snapshot 0 terminal)).StartsWith "main") "Alternate screen restoration"
    feed "\u001b[2J\u001b[1;1HAAAA\u001b[2;1HBBBB\u001b[3;1HCCCC\u001b[4;1HDDDD\u001b[2;3r\u001b[1S"
    let scrolled=OutputBuffer.snapshot 0 terminal
    check ((row 0 scrolled).StartsWith "AAAA" && (row 1 scrolled).StartsWith "CCCC" && (row 3 scrolled).StartsWith "DDDD") "Scoped scroll corrupted siblings"
    feed "\u001b[r\u001b[1;1H界\u001b[1;2Hx"
    let repaired=OutputBuffer.snapshot 0 terminal
    check (repaired.Frame.Cells[0].Glyph=" " && repaired.Frame.Cells[1].Glyph="x") "Wide-pair overwrite failed"
    feed "\u001b[2;2H\u001b7\u001b[4;5H\u001b8z"
    check ((OutputBuffer.snapshot 0 terminal).Frame.Cells[11].Glyph="z") "Saved cursor restoration"

    let hostile = OutputBuffer.create 5 2 1
    let bad = Encoding.UTF8.GetBytes ("\u001b]52;"+String.replicate 100000 "x"+"\u0007OK")
    OutputBuffer.feedUtf8 bad 0 bad.Length hostile
    check ((row 0 (OutputBuffer.snapshot 0 hostile)).StartsWith "OK") "Oversized unsupported control leaked"
    let malformed=[|0xF0uy;0x9Fuy|]
    OutputBuffer.feedUtf8 malformed 0 malformed.Length hostile
    OutputBuffer.finish hostile
    check ((row 0 (OutputBuffer.snapshot 0 hostile)).Contains "�") "Truncated UTF8 was not replaced"
    let queries=Encoding.UTF8.GetBytes(String.replicate 2000 "\u001b[6n")
    OutputBuffer.feedUtf8 queries 0 queries.Length hostile
    check ((OutputBuffer.drainResponses hostile).Length<=4096) "Query replies unbounded"
    OutputBuffer.clear hostile
    OutputBuffer.feedText ("e"+String.replicate 4000 "\u0301") hostile
    check ((OutputBuffer.snapshot 0 hostile).Frame.Cells |> Array.forall (fun cell -> cell.Glyph.Length<=256)) "Combining cluster storage unbounded"
    OutputBuffer.clear hostile
    let discard=Encoding.UTF8.GetBytes ("\u001bP"+String.replicate 100000 "x"+"\u001b\u001b\\OK")
    OutputBuffer.feedUtf8 discard 0 discard.Length hostile
    check ((row 0 (OutputBuffer.snapshot 0 hostile)).StartsWith "OK") "DCS string recovery failed"
    let invalidBytes=[|0xE2uy;0x28uy;0xA1uy;0xFFuy;0xF0uy;0x9Fuy|]
    let invalidWhole=OutputBuffer.create 20 2 0
    OutputBuffer.feedUtf8 invalidBytes 0 invalidBytes.Length invalidWhole
    OutputBuffer.finish invalidWhole
    for split=0 to invalidBytes.Length do
        let chunked=OutputBuffer.create 20 2 0
        OutputBuffer.feedUtf8 invalidBytes 0 split chunked
        OutputBuffer.feedUtf8 invalidBytes split (invalidBytes.Length-split) chunked
        OutputBuffer.finish chunked
        same (OutputBuffer.snapshot 0 invalidWhole) (OutputBuffer.snapshot 0 chunked)

    let boundary=OutputBuffer.create 4 3 2
    let feedBoundary (text: string) = let b=Encoding.UTF8.GetBytes text in OutputBuffer.feedUtf8 b 0 b.Length boundary
    feedBoundary "abcd\u001b[31mE"
    let wrapped=OutputBuffer.snapshot 0 boundary
    check (row 0 wrapped="abcd" && (row 1 wrapped).StartsWith "E") "SGR canceled deferred wrapping"
    OutputBuffer.clear boundary
    feedBoundary "abcd\u001b[?1049hZ\u001b[?1049lE"
    let restored=OutputBuffer.snapshot 0 boundary
    check (row 0 restored="abcd" && (row 1 restored).StartsWith "E") "Alternate mode lost deferred wrap"
    OutputBuffer.clear boundary
    feedBoundary "\u001b[38;2;12;34;56;48;5;201;1;4mX\u001b[22;24mY"
    let styled=OutputBuffer.snapshot 0 boundary
    check (styled.Frame.Cells[0].Paint.Foreground=Rgb 0x0C2238 && styled.Frame.Cells[0].Paint.Background=Indexed 201uy) "Extended color parsing"
    check (styled.Frame.Cells[0].Paint.Attributes=(Attributes.Bold ||| Attributes.Underline) && styled.Frame.Cells[1].Paint.Attributes=Attributes.None) "Attribute set/reset"
    OutputBuffer.clear boundary
    feedBoundary "x\u001b[5b"
    let repeated=OutputBuffer.snapshot 0 boundary
    check (row 0 repeated="xxxx" && (row 1 repeated).StartsWith "xx") "Repeat graphic command"
    OutputBuffer.clear boundary
    feedBoundary "\u001b[H\u001b[2M"
    check ((OutputBuffer.snapshot 0 boundary).HistoryLines=0) "Deleted lines entered scrollback"

    let literalStream="A\u0301界👩‍💻🇦🇺\rprogress\nnext\tend"
    let literalWhole=OutputBuffer.create 15 5 3
    OutputBuffer.feedText literalStream literalWhole
    OutputBuffer.finish literalWhole
    for split=0 to literalStream.Length do
        let chunked=OutputBuffer.create 15 5 3
        OutputBuffer.feedText (literalStream.Substring(0,split)) chunked
        OutputBuffer.feedText (literalStream.Substring split) chunked
        OutputBuffer.finish chunked
        same (OutputBuffer.snapshot 0 literalWhole) (OutputBuffer.snapshot 0 chunked)

    let random=Random 78217
    let fuzz=OutputBuffer.create 13 5 7
    let operations=[|"a";"界";"é";"👩‍💻";"\r\n";"\b";"\t";"\u001b[2J";"\u001b[1P";"\u001b[2@";"\u001b[X";"\u001b[2L";"\u001b[M";"\u001b[1S";"\u001b[2T";"\u001b[?7l";"\u001b[?7h";"\u001b[99999999;99999999H";"\u001b[H"|]
    for round=0 to 499 do
        let data=Encoding.UTF8.GetBytes operations[random.Next operations.Length]
        for i=0 to data.Length-1 do OutputBuffer.feedUtf8 data i 1 fuzz
        if round%23=0 then OutputBuffer.resize (random.Next(1,16)) (random.Next(1,8)) fuzz
        let snap=OutputBuffer.snapshot (random.Next 20) fuzz
        check (snap.HistoryLines<=7 && snap.CursorX>=0 && snap.CursorX<snap.Frame.Width && snap.CursorY>=0 && snap.CursorY<snap.Frame.Height) "Fuzz bounds"
        for y=0 to snap.Frame.Height-1 do
            for x=0 to snap.Frame.Width-1 do
                let cell=snap.Frame.Cells[y*snap.Frame.Width+x]
                check (cell.Glyph.Length<=256 && not (cell.Glyph.Contains '\u001b')) "Unbounded/unsafe cell glyph"
                if cell.Width=0uy then check (x>0 && snap.Frame.Cells[y*snap.Frame.Width+x-1].Width=2uy) "Orphan wide tail"
                if cell.Width=2uy then check (x+1<snap.Frame.Width && snap.Frame.Cells[y*snap.Frame.Width+x+1].Width=0uy) "Orphan wide head"

    let textReference=OutputBuffer.create 11 4 0
    let lines=Array.init 4 (fun _ -> Array.create 11 ' ')
    let mutable x=0
    let mutable y=0
    let mutable pending=false
    let newline () =
        pending <- false
        if y=3 then
            for i=0 to 2 do Array.Copy(lines[i+1],lines[i],11)
            Array.Fill(lines[3],' ')
        else y <- y+1
    for _=0 to 999 do
        let c=[|'a';'b';' ';'\r';'\n';'\b';'\t'|][random.Next 7]
        match c with
        | '\r' -> x <- 0;pending <- false
        | '\n' -> x <- 0;newline()
        | '\b' -> x <- max 0 (x-1);pending <- false
        | '\t' -> x <- min 10 ((x/8+1)*8);pending <- false
        | _ ->
            if pending then x <- 0;newline()
            lines[y][x] <- c
            if x=10 then pending <- true else x <- x+1
        OutputBuffer.feedText (string c) textReference
        let snap=OutputBuffer.snapshot 0 textReference
        for rowIndex=0 to 3 do check (row rowIndex snap=String lines[rowIndex]) "Independent ASCII stream model differs"
