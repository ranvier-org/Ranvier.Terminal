namespace Ranvier.Terminal

open System
open System.Globalization
open System.Text

type OutputSnapshot =
    { Frame: Frame
      CursorX: int
      CursorY: int
      CursorVisible: bool
      Title: string
      Revision: int64
      HistoryLines: int
      TotalScrolledLines: int64
      ScrollOffset: int
      AlternateScreen: bool
      BracketedPaste: bool }

type private OutputScreen =
    { mutable Frame: Frame
      mutable X: int
      mutable Y: int
      mutable Top: int
      mutable Bottom: int
      mutable Paint: Paint
      mutable SavedX: int
      mutable SavedY: int
      mutable SavedPaint: Paint
      mutable SavedWrap: bool
      mutable SavedOrigin: bool
      mutable SavedAutoWrap: bool
      mutable CursorVisible: bool
      mutable AutoWrap: bool
      mutable WrapPending: bool
      mutable Origin: bool
      mutable Insert: bool
      mutable NewLine: bool }

/// Single-owner incremental output state. Feed and snapshot it on the owning graph thread.
type OutputBuffer =
    private
        { Primary: OutputScreen
          Alternate: OutputScreen
          History: Cell array array
          mutable HistoryStart: int
          mutable HistoryCount: int
          mutable TotalScrolled: int64
          mutable UsingAlternate: bool
          mutable BracketedPaste: bool
          mutable Title: string
          mutable Revision: int64
          Decoder: Decoder
          Characters: char array
          Control: StringBuilder
          mutable ParserState: int
          mutable Overflow: bool
          mutable HighSurrogate: char
          mutable LastText: string
          mutable RepeatText: string
          mutable LastX: int
          mutable LastY: int
          mutable LastWidth: int
          mutable LastPaint: Paint
          mutable CanExtend: bool
          Replies: byte array
          mutable ReplyCount: int }

module OutputBuffer =
    [<Literal>]
    let private maxControl = 512
    [<Literal>]
    let private maxCluster = 256

    let private validate width height history =
        if width < 1 || height < 1 || history < 0
           || int64 width > int64 Int32.MaxValue/(int64 height*2L+int64 history) then
            invalidArg "width" "Positive dimensions and bounded combined screen/history capacity are required."

    let private screen width height =
        {Frame=Frame.create width height;X=0;Y=0;Top=0;Bottom=height-1;Paint=Paint.empty
         SavedX=0;SavedY=0;SavedPaint=Paint.empty;SavedWrap=false;SavedOrigin=false;SavedAutoWrap=true;CursorVisible=true;AutoWrap=true
         WrapPending=false;Origin=false;Insert=false;NewLine=false}

    let create width height historyLines =
        validate width height historyLines
        {Primary=screen width height;Alternate=screen width height;History=Array.zeroCreate historyLines
         HistoryStart=0;HistoryCount=0;TotalScrolled=0L;UsingAlternate=false;BracketedPaste=false
         Title="";Revision=0L;Decoder=Encoding.UTF8.GetDecoder();Characters=Array.zeroCreate 256
         Control=StringBuilder(maxControl);ParserState=0;Overflow=false;HighSurrogate='\000'
         LastText="";RepeatText=" ";LastX=0;LastY=0;LastWidth=1;LastPaint=Paint.empty;CanExtend=false
         Replies=Array.zeroCreate 4096;ReplyCount=0}

    let private active buffer = if buffer.UsingAlternate then buffer.Alternate else buffer.Primary
    let private forget buffer = buffer.CanExtend <- false;buffer.LastText <- ""

    let private resetScreen state =
        Frame.clear Paint.empty state.Frame
        state.X <- 0
        state.Y <- 0
        state.Top <- 0
        state.Bottom <- state.Frame.Height-1
        state.Paint <- Paint.empty
        state.SavedX <- 0
        state.SavedY <- 0
        state.SavedPaint <- Paint.empty
        state.SavedWrap <- false
        state.SavedOrigin <- false
        state.SavedAutoWrap <- true
        state.CursorVisible <- true
        state.AutoWrap <- true
        state.WrapPending <- false
        state.Origin <- false
        state.Insert <- false
        state.NewLine <- false

    let private clearHistory buffer =
        Array.Clear buffer.History
        buffer.HistoryStart <- 0
        buffer.HistoryCount <- 0

    let private reset buffer =
        resetScreen buffer.Primary
        resetScreen buffer.Alternate
        clearHistory buffer
        buffer.TotalScrolled <- 0L
        buffer.UsingAlternate <- false
        buffer.BracketedPaste <- false
        buffer.Title <- ""
        buffer.ParserState <- 0
        buffer.Control.Clear() |> ignore
        buffer.Overflow <- false
        buffer.ReplyCount <- 0
        buffer.RepeatText <- " "
        forget buffer

    /// Clear both screens and history, reset parser/modes and reset the history navigation epoch.
    let clear buffer =
        reset buffer
        buffer.Decoder.Reset()
        buffer.HighSurrogate <- '\000'
        buffer.Revision <- buffer.Revision+1L

    let private historyLine row buffer =
        buffer.TotalScrolled <- buffer.TotalScrolled+1L
        if buffer.History.Length > 0 then
            let index =
                if buffer.HistoryCount < buffer.History.Length then
                    let index=(buffer.HistoryStart+buffer.HistoryCount)%buffer.History.Length
                    buffer.HistoryCount <- buffer.HistoryCount+1
                    index
                else
                    let index=buffer.HistoryStart
                    buffer.HistoryStart <- (buffer.HistoryStart+1)%buffer.History.Length
                    index
            let width=buffer.Primary.Frame.Width
            if isNull buffer.History[index] then buffer.History[index] <- Array.zeroCreate width
            Array.Copy(buffer.Primary.Frame.Cells,row*width,buffer.History[index],0,width)

    let private scroll top bottom amount up recordHistory buffer =
        let state=active buffer
        let width=state.Frame.Width
        let count=min (max 0 amount) (bottom-top+1)
        for _ = 1 to count do
            if up then
                if recordHistory && not buffer.UsingAlternate && top=0 && bottom=state.Frame.Height-1 then historyLine top buffer
                Array.Copy(state.Frame.Cells,(top+1)*width,state.Frame.Cells,top*width,(bottom-top)*width)
                Array.Fill(state.Frame.Cells,Cell.blank state.Paint,bottom*width,width)
            else
                Array.Copy(state.Frame.Cells,top*width,state.Frame.Cells,(top+1)*width,(bottom-top)*width)
                Array.Fill(state.Frame.Cells,Cell.blank state.Paint,top*width,width)

    let private lineFeed buffer =
        let state=active buffer
        state.WrapPending <- false
        if state.Y=state.Bottom then scroll state.Top state.Bottom 1 true true buffer
        else state.Y <- min (state.Frame.Height-1) (state.Y+1)

    let private reverseIndex buffer =
        let state=active buffer
        state.WrapPending <- false
        if state.Y=state.Top then scroll state.Top state.Bottom 1 false false buffer
        else state.Y <- max 0 (state.Y-1)

    let private repairRow y (frame: Frame) =
        for x = 0 to frame.Width-1 do
            let index=y*frame.Width+x
            let cell=frame.Cells[index]
            if (cell.Width=0uy && (x=0 || frame.Cells[index-1].Width<>2uy))
               || (cell.Width=2uy && (x+1=frame.Width || frame.Cells[index+1].Width<>0uy)) then
                frame.Cells[index] <- Cell.blank cell.Paint

    let private shiftCharacters x count insert buffer =
        let state=active buffer
        let frame=state.Frame
        let count=min (max 0 count) (frame.Width-x)
        let start=state.Y*frame.Width+x
        if insert then
            Array.Copy(frame.Cells,start,frame.Cells,start+count,frame.Width-x-count)
            Array.Fill(frame.Cells,Cell.blank state.Paint,start,count)
        else
            Array.Copy(frame.Cells,start+count,frame.Cells,start,frame.Width-x-count)
            Array.Fill(frame.Cells,Cell.blank state.Paint,(state.Y+1)*frame.Width-count,count)
        repairRow state.Y frame

    let private writeCluster text extending buffer =
        let state=active buffer
        let mutable text=text
        let mutable width=TextRun.defaultWidth text
        if width > state.Frame.Width then text <- "�";width <- 1
        if extending then
            state.X <- buffer.LastX
            state.Y <- buffer.LastY
            state.WrapPending <- false
            if width > buffer.LastWidth && state.Insert && state.X+width <= state.Frame.Width then
                shiftCharacters (state.X+buffer.LastWidth) (width-buffer.LastWidth) true buffer
            if state.X+width > state.Frame.Width then
                Frame.put (Frame.bounds state.Frame) state.X state.Y " " 1 buffer.LastPaint state.Frame
        if state.AutoWrap && (state.WrapPending || state.X+width > state.Frame.Width) then
            state.X <- 0
            lineFeed buffer
        if not state.AutoWrap && state.X+width > state.Frame.Width then text <- "�";width <- 1
        if state.Insert && not extending then shiftCharacters state.X width true buffer
        let paint=if extending then buffer.LastPaint else state.Paint
        Frame.put (Frame.bounds state.Frame) state.X state.Y text width paint state.Frame
        buffer.LastX <- state.X
        buffer.LastY <- state.Y
        buffer.LastWidth <- width
        buffer.LastText <- text
        buffer.RepeatText <- text
        buffer.LastPaint <- paint
        buffer.CanExtend <- true
        if state.X+width >= state.Frame.Width then
            state.X <- state.Frame.Width-1
            state.WrapPending <- state.AutoWrap
        else
            state.X <- state.X+width
            state.WrapPending <- false

    let private scalar (text: string) buffer =
        if buffer.CanExtend && buffer.LastText.Length+text.Length <= maxCluster then
            let combined=buffer.LastText+text
            if StringInfo.ParseCombiningCharacters(combined).Length=1 then writeCluster combined true buffer
            else writeCluster text false buffer
        elif buffer.CanExtend && StringInfo.ParseCombiningCharacters(buffer.LastText+text).Length=1 then
            forget buffer
            writeCluster "�" false buffer
        else writeCluster text false buffer

    let private printable c buffer =
        if buffer.HighSurrogate<>'\000' then
            let high=buffer.HighSurrogate
            buffer.HighSurrogate <- '\000'
            if Char.IsLowSurrogate c then scalar (String([|high;c|])) buffer
            else
                scalar "�" buffer
                if Char.IsHighSurrogate c then buffer.HighSurrogate <- c
                elif Char.IsLowSurrogate c then scalar "�" buffer
                else scalar (string c) buffer
        elif Char.IsHighSurrogate c then buffer.HighSurrogate <- c
        elif Char.IsLowSurrogate c then scalar "�" buffer
        else scalar (string c) buffer

    let private flushHigh buffer =
        if buffer.HighSurrogate<>'\000' then
            buffer.HighSurrogate <- '\000'
            scalar "�" buffer

    let private control c literal buffer =
        flushHigh buffer
        forget buffer
        let state=active buffer
        match c with
        | '\r' -> state.WrapPending <- false;state.X <- 0
        | '\n' | '\u000b' | '\u000c' ->
            if literal || state.NewLine then state.X <- 0
            lineFeed buffer
        | '\b' -> state.WrapPending <- false;state.X <- max 0 (state.X-1)
        | '\t' -> state.WrapPending <- false;state.X <- min (state.Frame.Width-1) ((state.X/8+1)*8)
        | _ -> ()

    let private save state =
        state.SavedX <- state.X
        state.SavedY <- state.Y
        state.SavedPaint <- state.Paint
        state.SavedWrap <- state.WrapPending
        state.SavedOrigin <- state.Origin
        state.SavedAutoWrap <- state.AutoWrap

    let private restore state =
        state.X <- min (state.Frame.Width-1) state.SavedX
        state.Y <- min (state.Frame.Height-1) state.SavedY
        state.Paint <- state.SavedPaint
        state.WrapPending <- state.SavedWrap
        state.Origin <- state.SavedOrigin
        state.AutoWrap <- state.SavedAutoWrap

    let private reply (text: string) buffer =
        if text.Length <= buffer.Replies.Length-buffer.ReplyCount then
            for c in text do
                buffer.Replies[buffer.ReplyCount] <- byte c
                buffer.ReplyCount <- buffer.ReplyCount+1

    let private rendition (values: int array) state =
        let mutable index=0
        let flags flag enabled =
            state.Paint <- {state.Paint with Attributes=(if enabled then state.Paint.Attributes ||| flag else state.Paint.Attributes &&& ~~~flag)}
        while index<values.Length do
            let value=values[index]
            match value with
            | 0 -> state.Paint <- Paint.empty
            | 1 -> flags Attributes.Bold true
            | 2 -> flags Attributes.Dim true
            | 3 -> flags Attributes.Italic true
            | 4 -> flags Attributes.Underline true
            | 7 -> flags Attributes.Reverse true
            | 9 -> flags Attributes.Strike true
            | 21 | 22 -> flags (Attributes.Bold ||| Attributes.Dim) false
            | 23 -> flags Attributes.Italic false
            | 24 -> flags Attributes.Underline false
            | 27 -> flags Attributes.Reverse false
            | 29 -> flags Attributes.Strike false
            | 39 -> state.Paint <- {state.Paint with Foreground=Default}
            | 49 -> state.Paint <- {state.Paint with Background=Default}
            | n when n>=30 && n<=37 -> state.Paint <- {state.Paint with Foreground=Indexed(byte (n-30))}
            | n when n>=40 && n<=47 -> state.Paint <- {state.Paint with Background=Indexed(byte (n-40))}
            | n when n>=90 && n<=97 -> state.Paint <- {state.Paint with Foreground=Indexed(byte (n-90+8))}
            | n when n>=100 && n<=107 -> state.Paint <- {state.Paint with Background=Indexed(byte (n-100+8))}
            | 38 | 48 when index+2<values.Length ->
                let mutable color=ValueNone
                if values[index+1]=5 then
                    color <- ValueSome(Indexed(byte (min 255 values[index+2])))
                    index <- index+2
                elif values[index+1]=2 && index+4<values.Length then
                    color <- ValueSome(Rgb((min 255 values[index+2] <<< 16) ||| (min 255 values[index+3] <<< 8) ||| min 255 values[index+4]))
                    index <- index+4
                match color with
                | ValueSome color -> state.Paint <- if value=38 then {state.Paint with Foreground=color} else {state.Paint with Background=color}
                | ValueNone -> ()
            | _ -> ()
            index <- index+1

    let private setMode privateMode enabled value buffer =
        let state=active buffer
        if privateMode then
            match value with
            | 6 -> state.Origin <- enabled;state.X <- 0;state.Y <- if enabled then state.Top else 0
            | 7 -> state.AutoWrap <- enabled;state.WrapPending <- false
            | 25 -> state.CursorVisible <- enabled
            | 47 | 1047 | 1049 ->
                if enabled && not buffer.UsingAlternate then
                    if value<>47 then resetScreen buffer.Alternate
                    buffer.UsingAlternate <- true
                elif not enabled then buffer.UsingAlternate <- false
            | 1048 -> if enabled then save state else restore state
            | 2004 -> buffer.BracketedPaste <- enabled
            | _ -> ()
        else
            match value with
            | 4 -> state.Insert <- enabled
            | 20 -> state.NewLine <- enabled
            | _ -> ()

    let private csi final buffer =
        let text=buffer.Control.ToString()
        let privateMode=text.StartsWith "?"
        let secondary=text.StartsWith ">"
        let content=if privateMode || secondary then text.Substring 1 else text
        let pieces=content.Split ';'
        let values=Array.zeroCreate pieces.Length
        let mutable valid=pieces.Length<=32
        for i=0 to pieces.Length-1 do
            if pieces[i]<>"" then
                let mutable value=0
                if Int32.TryParse(pieces[i],NumberStyles.None,CultureInfo.InvariantCulture,&value) then values[i] <- min 32767 value
                else valid <- false
        if valid && ((not privateMode && not secondary) || (privateMode && (final='h' || final='l' || final='n')) || (secondary && final='c')) then
            let state=active buffer
            let first=values[0]
            let parameter index fallback = if index>=values.Length || values[index]=0 then fallback else values[index]
            let count=parameter 0 1
            let top=if state.Origin then state.Top else 0
            let bottom=if state.Origin then state.Bottom else state.Frame.Height-1
            if "ABCDEFGHIf`deGaZJKX@PLMSTr".Contains final then state.WrapPending <- false
            let erase x y width height = Frame.fill {X=x;Y=y;Width=width;Height=height} state.Paint state.Frame
            match final with
            | 'A' -> state.Y <- max top (state.Y-count)
            | 'B' | 'e' -> state.Y <- min bottom (state.Y+count)
            | 'C' | 'a' -> state.X <- min (state.Frame.Width-1) (state.X+count)
            | 'D' -> state.X <- max 0 (state.X-count)
            | 'E' -> state.X <- 0;state.Y <- min bottom (state.Y+count)
            | 'F' -> state.X <- 0;state.Y <- max top (state.Y-count)
            | 'G' | '`' -> state.X <- min (state.Frame.Width-1) (count-1)
            | 'd' -> state.Y <- min bottom (top+count-1)
            | 'H' | 'f' -> state.Y <- min bottom (top+count-1);state.X <- min (state.Frame.Width-1) (parameter 1 1-1)
            | 'I' -> state.X <- min (state.Frame.Width-1) ((state.X/8+count)*8)
            | 'Z' -> state.X <- max 0 (((state.X-1)/8-count+1)*8)
            | 'J' ->
                match first with
                | 0 -> erase state.X state.Y (state.Frame.Width-state.X) 1;erase 0 (state.Y+1) state.Frame.Width (state.Frame.Height-state.Y-1)
                | 1 -> erase 0 0 state.Frame.Width state.Y;erase 0 state.Y (state.X+1) 1
                | 2 -> Frame.clear state.Paint state.Frame
                | 3 -> clearHistory buffer
                | _ -> ()
            | 'K' ->
                match first with
                | 0 -> erase state.X state.Y (state.Frame.Width-state.X) 1
                | 1 -> erase 0 state.Y (state.X+1) 1
                | 2 -> erase 0 state.Y state.Frame.Width 1
                | _ -> ()
            | 'X' -> erase state.X state.Y (min count (state.Frame.Width-state.X)) 1
            | '@' -> shiftCharacters state.X count true buffer
            | 'P' -> shiftCharacters state.X count false buffer
            | 'L' when state.Y>=state.Top && state.Y<=state.Bottom -> scroll state.Y state.Bottom count false false buffer
            | 'M' when state.Y>=state.Top && state.Y<=state.Bottom -> scroll state.Y state.Bottom count true false buffer
            | 'S' -> scroll state.Top state.Bottom count true true buffer
            | 'T' -> scroll state.Top state.Bottom count false false buffer
            | 'r' when not privateMode ->
                let newTop=parameter 0 1-1
                let newBottom=parameter 1 state.Frame.Height-1
                if newTop<newBottom && newBottom<state.Frame.Height then
                    state.Top <- newTop;state.Bottom <- newBottom;state.X <- 0;state.Y <- if state.Origin then newTop else 0
            | 'm' when not privateMode -> rendition values state
            | 'b' ->
                let text=buffer.RepeatText
                for _=1 to count do writeCluster text false buffer
            | 's' -> save state
            | 'u' -> restore state
            | 'h' | 'l' -> for value in values do setMode privateMode (final='h') value buffer
            | 'n' ->
                if first=5 && not privateMode then reply "\u001b[0n" buffer
                elif first=6 then reply (sprintf "\u001b[%s%d;%dR" (if privateMode then "?" else "") (state.Y-top+1) (state.X+1)) buffer
            | 'c' -> reply (if secondary then "\u001b[>0;0;0c" else "\u001b[?1;0c") buffer
            | 't' when first=18 -> reply (sprintf "\u001b[8;%d;%dt" state.Frame.Height state.Frame.Width) buffer
            | _ -> ()

    let private endOsc buffer =
        if not buffer.Overflow then
            let text=buffer.Control.ToString()
            let separator=text.IndexOf ';'
            if separator>=0 && (text.Substring(0,separator)="0" || text.Substring(0,separator)="2") then
                let title=text.Substring(separator+1)
                buffer.Title <- String(title |> Seq.filter (fun c -> not (Char.IsControl c)) |> Seq.truncate 256 |> Seq.toArray)
        buffer.ParserState <- 0
        buffer.Control.Clear() |> ignore

    let private appendControl (c: char) buffer =
        if buffer.Control.Length<maxControl then buffer.Control.Append c |> ignore
        else buffer.Overflow <- true

    let private beginControl state buffer =
        buffer.ParserState <- state
        buffer.Control.Clear() |> ignore
        buffer.Overflow <- false

    let private vt c buffer =
        match buffer.ParserState with
        | 0 ->
            if c='\u001b' then flushHigh buffer;forget buffer;buffer.ParserState <- 1
            elif c='\u009b' then flushHigh buffer;forget buffer;beginControl 2 buffer
            elif c='\u009d' then flushHigh buffer;forget buffer;beginControl 3 buffer
            elif c='\u0090' || c='\u009e' || c='\u009f' then flushHigh buffer;forget buffer;beginControl 5 buffer
            elif Char.IsControl c then control c false buffer
            else printable c buffer
        | 1 ->
            buffer.ParserState <- 0
            match c with
            | '[' -> beginControl 2 buffer
            | ']' -> beginControl 3 buffer
            | 'P' | '^' | '_' -> beginControl 5 buffer
            | '(' | ')' | '*' | '+' | '%' | '#' -> buffer.ParserState <- 7
            | '7' -> save (active buffer)
            | '8' -> restore (active buffer)
            | 'D' -> lineFeed buffer
            | 'E' -> (active buffer).X <- 0;lineFeed buffer
            | 'M' -> reverseIndex buffer
            | 'c' -> reset buffer
            | '\u001b' -> buffer.ParserState <- 1
            | _ -> ()
        | 2 ->
            if c='\u001b' then beginControl 1 buffer
            elif c='\u0018' || c='\u001a' then buffer.ParserState <- 0
            elif c>='@' && c<='~' then
                if not buffer.Overflow then csi c buffer
                buffer.ParserState <- 0
                buffer.Control.Clear() |> ignore
            elif c>=' ' && c<='?' then appendControl c buffer
            elif Char.IsControl c then control c false buffer
            else buffer.Overflow <- true
        | 3 ->
            if c='\u0007' || c='\u009c' then endOsc buffer
            elif c='\u001b' then buffer.ParserState <- 4
            elif c='\u0018' || c='\u001a' then buffer.ParserState <- 0
            else appendControl c buffer
        | 4 ->
            if c='\\' then endOsc buffer
            elif c='\u001b' then buffer.Overflow <- true
            else buffer.ParserState <- 3;buffer.Overflow <- true
        | 5 ->
            if c='\u001b' then buffer.ParserState <- 6
            elif c='\u0018' || c='\u001a' || c='\u009c' then buffer.ParserState <- 0
        | 6 -> if c='\\' then buffer.ParserState <- 0 elif c<>'\u001b' then buffer.ParserState <- 5
        | _ -> buffer.ParserState <- 0

    let private flushDecoder buffer =
        let count=buffer.Decoder.GetChars(Array.empty<byte>,0,0,buffer.Characters,0,true)
        for index=0 to count-1 do vt buffer.Characters[index] buffer

    /// Literal output: newline starts a new line; CR/BS/TAB retain terminal-like progress behavior.
    /// Escape and other non-layout controls are sanitized, never interpreted as VT.
    let feedText (text: string) buffer =
        if isNull text then nullArg "text"
        if text.Length>0 then
            flushDecoder buffer
            buffer.ParserState <- 0
            buffer.Control.Clear() |> ignore
            for c in text do
                if c='\r' || c='\n' || c='\b' || c='\t' then control c true buffer
                elif Char.IsControl c then printable '\uFFFD' buffer
                else printable c buffer
            buffer.Revision <- buffer.Revision+1L

    /// Decode incremental UTF8 and a bounded VT subset. Unsupported controls are consumed safely.
    let feedUtf8 (bytes: byte array) offset count buffer =
        if isNull bytes then nullArg "bytes"
        if offset<0 || count<0 || offset>bytes.Length-count then invalidArg "offset" "Invalid byte range."
        let mutable offset=offset
        let mutable remaining=count
        while remaining>0 do
            let mutable bytesUsed=0
            let mutable charsUsed=0
            let mutable completed=false
            buffer.Decoder.Convert(bytes,offset,remaining,buffer.Characters,0,buffer.Characters.Length,false,&bytesUsed,&charsUsed,&completed)
            for index=0 to charsUsed-1 do vt buffer.Characters[index] buffer
            offset <- offset+bytesUsed
            remaining <- remaining-bytesUsed
        if count>0 then buffer.Revision <- buffer.Revision+1L

    /// Finish a drained stream: replace truncated UTF8/surrogates and discard incomplete controls.
    let finish buffer =
        flushDecoder buffer
        flushHigh buffer
        buffer.ParserState <- 0
        buffer.Control.Clear() |> ignore
        forget buffer
        buffer.Revision <- buffer.Revision+1L

    /// Crop/pad existing rows without reflow; resize history rows too so retained cells stay bounded.
    let resize width height buffer =
        validate width height buffer.History.Length
        let resizeScreen state =
            let old=state.Frame
            let next=Frame.create width height
            for y=0 to min old.Height height-1 do
                Array.Copy(old.Cells,y*old.Width,next.Cells,y*width,min old.Width width)
                repairRow y next
            state.Frame <- next
            state.X <- min (width-1) state.X
            state.Y <- min (height-1) state.Y
            state.SavedX <- min (width-1) state.SavedX
            state.SavedY <- min (height-1) state.SavedY
            state.SavedWrap <- false
            state.Top <- 0
            state.Bottom <- height-1
            state.WrapPending <- false
        for index=0 to buffer.History.Length-1 do
            if not (isNull buffer.History[index]) then
                let row=Frame.create width 1
                Array.Copy(buffer.History[index],row.Cells,min width buffer.History[index].Length)
                repairRow 0 row
                buffer.History[index] <- row.Cells
        resizeScreen buffer.Primary
        resizeScreen buffer.Alternate
        forget buffer
        buffer.Revision <- buffer.Revision+1L

    /// Detached viewport: positive offsets browse bounded primary history; alternate screens have no history.
    let snapshot scrollOffset buffer =
        let state=active buffer
        let history=if buffer.UsingAlternate then 0 else buffer.HistoryCount
        let offset=min history (max 0 scrollOffset)
        let result=Frame.create state.Frame.Width state.Frame.Height
        for y=0 to result.Height-1 do
            let source=history-offset+y
            if source<history then
                let row=buffer.History[(buffer.HistoryStart+source)%buffer.History.Length]
                Array.Copy(row,0,result.Cells,y*result.Width,result.Width)
            else Array.Copy(state.Frame.Cells,(source-history)*result.Width,result.Cells,y*result.Width,result.Width)
        {Frame=result;CursorX=state.X;CursorY=state.Y;CursorVisible=state.CursorVisible && offset=0
         Title=buffer.Title;Revision=buffer.Revision;HistoryLines=history;TotalScrolledLines=buffer.TotalScrolled
         ScrollOffset=offset;AlternateScreen=buffer.UsingAlternate;BracketedPaste=buffer.BracketedPaste}

    /// Drain bounded protocol replies for the subprocess input transport, never the outer terminal.
    let drainResponses buffer =
        let result=Array.zeroCreate buffer.ReplyCount
        Array.Copy(buffer.Replies,result,buffer.ReplyCount)
        buffer.ReplyCount <- 0
        result
