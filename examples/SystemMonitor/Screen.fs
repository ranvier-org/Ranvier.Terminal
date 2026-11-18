namespace SystemMonitor

open System
open Ranvier.Terminal

open Ranvier
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

module Screen =
    let background=Rgb 0x262626
    let sand=Rgb 0xCDBB94
    let muted=Rgb 0x807E6B
    let green=Rgb 0x89B482
    let pink=Rgb 0xCA8C9F
    let teal=Rgb 0x78AAA1
    let olive=Rgb 0xA8B66A
    let red=Rgb 0xE76563
    let orange=Rgb 0xD69670
    let track=Rgb 0x4B4845
    let bytes value =
        let magnitude=abs (float value)
        if magnitude>=1099511627776. then sprintf "%.1fT" (float value/1099511627776.)
        elif magnitude>=1073741824. then sprintf "%.1fG" (float value/1073741824.)
        elif magnitude>=1048576. then sprintf "%.0fM" (float value/1048576.)
        elif magnitude>=1024. then sprintf "%.1fK" (float value/1024.) else sprintf "%dB" value
    let private gib value = sprintf "%.1f GiB" (float value/1073741824.)

    let private size width height = [Width(Cells(max 0 width)); Height(Cells(max 0 height))]
    let private position x y = Margin {Style.zero with Left=x; Top=y}
    let private bounds x y width height = position x y :: size width height

    let private styled declarations node =
        Node.setStyle declarations node
        node

    let private caption x value = x, sand, fun () -> "┤" + value() + "├"
    let private title value = caption 2 (fun () -> value)
    let private sampled model select () =
        model.SampleRevision.Value |> ignore
        select model.Sample

    let private state model read () =
        model.DisplayRevision.Value |> ignore
        read ()

    let private field x y width color read =
        text read |> styled (Foreground color :: bounds x y width 1)

    let private literal x y width color value = label value |> styled (Foreground color :: bounds x y width 1)
    let private inside width height = overlay (size (width-2) (height-2))

    let private cores model width count columns =
        let cellWidth=width/columns
        let slots=count*columns
        overlay (bounds 0 1 width count) {
            for i in 0..slots-1 do
                let col=i/count
                let x=col*cellWidth
                let y=i%count
                let core read = sampled model (fun s -> if i<s.Cores.Length then read s.Cores[i] else "")
                if col>0 then field (x-1) y 1 muted (core (fun _ -> "│"))
                field x y 4 sand (core (fun _ -> sprintf "C%d" i))
                let dots=max 0 (cellWidth-9)
                Styled.meterWith '⣿' '⣀' [] dots Default muted
                    (sampled model (fun s -> if i<s.Cores.Length then s.Cores[i]/100. else 0.))
                |> bindStyle (sampled model (fun s ->
                    let load=if i<s.Cores.Length then s.Cores[i] else 0.
                    Visible(i<s.Cores.Length)::Direction Row::Foreground(if load>40. then pink else green)::bounds (x+4) y dots 1))
                field (x+cellWidth-5) y 4 green (core (sprintf "%3.0f%%"))
            field 0 1 width muted (sampled model (fun s -> if s.Cores.Length=0 then "Per-core readings unavailable on this host" else ""))
            |> bindStyle (sampled model (fun s -> Visible(s.Cores.Length=0)::Foreground muted::bounds 0 1 width 1))
        }

    let private cpuPanel model (area: Rect) =
        let w,h=area.Width,area.Height
        let detailWidth=max 35 (w*56/100)
        let coreRows=max 1 (min 11 (h-7))
        let detailsX=w-detailWidth-2
        let detailsY=max 1 ((h-coreRows-5)/2)-1
        let graphWidth=detailsX
        let center=h/2-1
        let chartHeight=max 1 (h/2-2)
        let history () = model.SampleRevision.Value |> ignore; model.CpuHistory.ToArray()
        let headers =
            [title "¹cpu";10,sand,(fun () -> "┤menu [m]├┤preset *├")
             caption (w/2-4) (sampled model (fun s -> s.Stamp.ToString("HH:mm:ss")))
             if w>95 then caption (w-35) (sampled model (fun s -> if s.Demo then "BAT○ 89% ▰▰▰▰ 0.00W" else "sensors: unavailable"))
             caption (w-13) (state model (fun () -> sprintf "-%5dms+" model.PollIntervalMs))]
        let details =
            inside detailWidth (coreRows+5) {
                literal 0 0 4 sand "CPU"
                Styled.meter [position 4 0] (max 4 (detailWidth-22)) green track (sampled model (fun s -> s.Cpu/100.))
                field (detailWidth-17) 0 5 green (sampled model (fun s -> if s.Demo || s.CpuCounters.Length>0 then sprintf "%3.0f%%" s.Cpu else " —% "))
                field (detailWidth-12) 0 5 red (sampled model (fun s -> if s.Demo then "95°C" else " —°C"))
                field (detailWidth-7) 0 6 green (sampled model (fun s -> if s.Demo then "34.0W" else "  —W"))
                cores model (detailWidth-2) coreRows (if h<15 then 4 else 3)
                field 0 (coreRows+1) (detailWidth-2) sand (sampled model (fun s -> "Load avg: "+s.Load))
                field 0 (coreRows+2) (detailWidth-2) olive (sampled model (fun s -> if s.Demo then "GPU ▰▱▱▱▱▱  1% ⣀⣀⣀ 3.1G/4.0G  59°C" else "GPU / temperature / power: unavailable"))
            }
        let body =
            inside w h {
                Styled.brailleChart [position 0 (center-chartHeight)] graphWidth chartHeight green false history
                Styled.brailleChart [position 0 (center+1)] graphWidth (max 1 (chartHeight-1)) teal true history
                literal 0 center graphWidth muted (String('─',graphWidth))
                literal (max 0 (graphWidth/2-11)) center (min 19 graphWidth) sand "total─▲▼─gpu-totals"
                field 0 (h-3) graphWidth sand (sampled model (fun s -> sprintf "up %dd %02d:%02d" s.Uptime.Days s.Uptime.Hours s.Uptime.Minutes))
                Styled.panel [position detailsX detailsY] detailWidth (coreRows+5) muted [caption 2 (sampled model (fun s -> s.Name))] [] details
            }
        Styled.panel [position area.X area.Y] w h pink headers [] body |> id "cpu"

    let private memoryPanel model (area: Rect) =
        let w,h=area.Width,area.Height
        let split=w/2
        let memWidth=split-2
        let stride=max 1 ((h-3)/4)
        let diskStride=if h>16 then 3 else 2
        let metrics =
            ["Used",green,(fun (s: Sample) -> Some(max 0L (s.Total-s.Available)))
             "Available",orange,(fun s -> Some s.Available)
             "Cached",green,(fun s -> s.Cached)
             "Free",teal,(fun s -> if s.Demo then Some(Telemetry.gib 8.37) else None)]
        let body =
            inside w h {
                for y in 0..h-3 do literal (split-1) y 1 muted "│"
                field 0 0 memWidth sand (sampled model (fun s -> "Total: "+(if s.Total>0L then gib s.Total else "unavailable")))
                for index,(name,color,get) in List.indexed metrics do
                    let y=1+index*stride
                    if y<h-2 then
                        field 0 y memWidth sand (sampled model (fun s -> name+": "+(get s |> Option.map gib |> Option.defaultValue "—")))
                        if stride>=3 && y+2<h-2 then
                            let percent (s: Sample) = if s.Total>0L then 100.*float(defaultArg (get s) 0L)/float s.Total else 0.
                            field 1 (y+1) 5 sand (sampled model (percent >> sprintf "%.0f%%"))
                            let chartWidth=max 1 (memWidth-2)
                            Styled.brailleChart [position 1 (y+2)] chartWidth (max 1 (stride-2)) color false (sampled model (percent >> Array.create (chartWidth*2)))
                for i in 0..(h-3)/diskStride do
                    let y=i*diskStride
                    let dw=w-split-2
                    if y+1<h-2 then
                        let readDisk format = sampled model (fun s -> if i<s.Disks.Length then format s.Disks[i] else "")
                        field split y (max 1 (dw-6)) sand (readDisk (fun d -> "─"+d.Name))
                        field (split+dw-6) y 6 sand (readDisk (fun d -> bytes d.Total))
                        let meter=Styled.meter [] (max 1 (dw-9)) green track (sampled model (fun s ->
                            if i<s.Disks.Length && s.Disks[i].Total>0L then 1.-float s.Disks[i].Free/float s.Disks[i].Total else 0.))
                        // Keep empty disk slots blank when fewer devices are mounted.
                        overlay (bounds (split+1) (y+1) (max 1 (dw-9)) 1) { meter }
                        |> bindStyle (sampled model (fun s -> Visible(i<s.Disks.Length)::bounds (split+1) (y+1) (max 1 (dw-9)) 1))
                        field (split+dw-7) (y+1) 7 sand (readDisk (fun d -> bytes d.Free))
            }
        Styled.panel [position area.X area.Y] w h olive [title "²mem"; split,muted,(fun () -> "│");split+1,sand,(fun () -> "┤disks├────┤io├")] [split,muted,(fun () -> "│")] body |> id "memory"

    let private networkPanel model (area: Rect) =
        let w,h=area.Width,area.Height
        let sw=max 18 (w/2)
        let sh=min 8 (h-2)
        let sx=w-sw-2
        let sy=max 1 ((h-7)/2)-1
        let half=max 1 ((h-3)/2)
        let cw=max 1 (sx-1)
        let history () = model.SampleRevision.Value |> ignore; model.NetHistory.ToArray()
        let stats =
            inside sw sh {
                field 0 0 (sw-2) sand (sampled model (fun s -> sprintf "▼ %.2f KiB/s" (s.Download/1024.)))
                field 0 1 (sw-2) sand (sampled model (fun s -> "▼ Total: "+bytes s.Received))
                if sh>=5 then field 0 (if sh>=7 then 3 else 2) (sw-2) sand (sampled model (fun s -> sprintf "▲ %.2f KiB/s" (s.Upload/1024.)))
                if sh>=7 then field 0 4 (sw-2) sand (sampled model (fun s -> "▲ Total: "+bytes s.Sent))
            }
        let body =
            inside w h {
                Styled.brailleChart [] cw half orange false history
                Styled.brailleChart [position 0 half] cw (max 1 (h-half-2)) green true history
                literal 0 0 3 sand "10K"
                literal 0 (h-3) 3 sand "10K"
                Styled.panel [position sx sy] sw sh muted [title "download"]
                    [if sh>=5 then title "upload"
                     elif sh=3 then 1,sand,sampled model (fun s -> "▼ Total: "+bytes s.Received)] stats
            }
        Styled.panel [position area.X area.Y] w h red
            [caption 2 (sampled model (fun s -> "³net "+s.Network)); if w>40 then w-22,teal,(fun () -> "┤sync├┤auto├┤zero├")] [] body |> id "network"

    let private processes (model: Model) (area: Rect) =
        let w,h=area.Width,area.Height
        let inner=w-2
        let visible=max 1 (h-3)
        let hasCommand=inner>=60
        let hasUser=inner>=42
        let nameWidth=if hasCommand then 12 else max 1 (inner-(if hasUser then 31 else 25))
        let commandWidth=if hasCommand then inner-48 else 0
        let color value _ _ = [Foreground value]
        let rank slot _ = [Foreground(if slot<7 then green else muted)]
        let separated style slot item = Padding {Style.zero with Right=1} :: style slot item
        let col sort header width format style : TableColumn<ProcessRow> * string option =
            {Header=header; Width=Cells width; Text=format; Style=style}, sort
        let columns, headerIds =
            [| col (Some "pid") "   Pid:" 8 (fun r -> sprintf "%7d" r.Pid) (color sand)
               col None "Program:" nameWidth (fun r -> r.Name) (separated rank)
               if hasCommand then col None "Command:" commandWidth (fun r -> r.Command) (separated (color sand))
               if hasUser then col None "User:" 6 (fun r -> r.User) (color muted)
               let spacer=inner-17-(8+nameWidth+commandWidth+(if hasUser then 6 else 0))
               if spacer>0 then col None "" spacer (fun _ -> "") (color muted)
               col (Some "mem") "MemB" 7 (fun r -> bytes r.Memory) rank
               col None "" 4 (fun _ -> "⣀⣀⣀⣀") (color muted)
               col (Some "cpu") "Cpu%" 6 (fun r -> sprintf "%4.1f" r.Cpu) rank |]
            |> Array.unzip
        let rail = Css.tableScrollbar { Paint.empty with Foreground = teal; Background = background }
                                      { Paint.empty with Foreground = sand; Background = background }
        let tableBounds overflow = bounds 1 1 (if overflow then w-1 else w-2) (h-2) @ rail
        let mutable overlapsBorder = true
        let readRows () =
            model.DisplayRevision.Value |> ignore
            Model.filteredRows model
        let table =
            Table.bindSortableFrom model.Scroll columns headerIds [Foreground sand] visible readRows model.SelectedPid model.Order (fun navigate ->
                Table.create (tableBounds true) [Foreground sand] [Background(Rgb 0x343B36)] [] (fun selected first ->
                    navigate selected first
                    model.Dirty <- true))
        let handle event =
            match event with
            | Key _ when model.Filtering -> false
            | _ -> Table.handle event table
        table.Root.OnEvent <- handle
        model.ProcessViewport <- Showing table
        createEffect (fun () ->
            let overflow = (readRows ()).Length > visible
            if overflow <> overlapsBorder then
                overlapsBorder <- overflow
                Node.setStyle (tableBounds overflow) table.Root)
        let filter () =
            let query=if model.Filtering then model.Filter+"▌" elif model.Filter="" then "filter /" else model.Filter
            "⁴proc "+query
        let range () =
            let count=(Model.rows model).Length
            sprintf "%d-%d/%d" (if count=0 then 0 else table.FirstVisible+1) (min count (table.FirstVisible+visible)) count
        overlay (bounds area.X area.Y w h) {
            Styled.panel [] w h teal
                [caption 2 (state model filter)
                 if w>44 then caption (w-22) (state model (fun () -> sprintf "%s %s %s" (if model.Order.IsReversed then "reverse" else "sort") model.Order.SortId (if model.Order.Descending then "↓" else "↑")))]
                [title "↑↓ select  ↵ info  / filter"; w-15,sand,state model range] (fragment [||])
            table.Root
            literal (w-1) 1 1 teal "│"
            label "No matching processes" |> bindStyle (state model (fun () -> Visible((Model.rows model).Length=0)::Foreground muted::bounds 2 3 (inner-2) 1))
        } |> id "processes"

    let private helpLines =
        [|"L  Switch reference data / live host metrics";"+/- Poll interval (100–10000 ms); default 500 ms"
          "P  Pause sampling      S  CPU / memory / PID sort";"R  Reverse order       /  Filter processes"
          "↑↓ PageUp/PageDown     Mouse wheel: scroll viewport";"Click scrollbar: jump; Enter: read-only process info"
          "Esc  Close overlay / clear filter    Q  Quit";"GPU / sensors are unavailable in live mode."
          "Demo figures reproduce the reference, not this host."|]

    let private detailLines model =
        let selected = model.SelectedPid.Value
        match Model.rows model |> Array.tryFind (fun row -> Some row.Pid = selected) with
        | None -> [|"No process selected."|]
        | Some row ->
            [|row.Name;sprintf "PID %d    CPU %.2f%%    Memory %s" row.Pid row.Cpu (bytes row.Memory)
              row.Command;"";"Read-only: no signals or priority changes are sent.";"Esc / Enter to close"|]

    let private modal (model: Model) width height =
        let x,y=(model.Width-width)/2,(model.Height-height)/2
        let body =
            inside width height {
                for i in 0..min (height-3) (helpLines.Length-1) do
                    field 1 i (width-4) sand (state model (fun () ->
                        if model.Help then helpLines[i]
                        elif model.Detail then
                            let lines=detailLines model
                            if i<lines.Length then lines[i] else ""
                        else ""))
            }
        overlay (bounds x y width height) {
            Styled.panel [] width height teal [caption 2 (state model (fun () -> if model.Help then "menu / help" else "process details"))] [] body
        } |> bindStyle (state model (fun () -> Visible(model.Help || model.Detail)::bounds x y width height))

    let build (model: Model) =
        let width,height=model.Width,model.Height
        overlay (Background background::Foreground sand::size width height) {
            if width<64 || height<24 then
                model.ProcessViewport <- HiddenAt model.Scroll
                literal 1 1 (width-2) sand "SYSTEM MONITOR"
                literal 1 3 (width-2) teal "Enlarge to at least 64 × 24 cells."
                field 1 5 (width-2) green (sampled model (fun s -> sprintf "CPU %.0f%%  RAM %s" s.Cpu (bytes(s.Total-s.Available))))
            else
                let cpu,mem,net,proc=Model.geometry width height
                cpuPanel model cpu
                memoryPanel model mem
                networkPanel model net
                processes model proc
            field 1 (height-1) (width-2) teal (state model (fun () ->
                let source=if model.Live then (if model.Sample.Demo then "LIVE: sampling…" else "LIVE: measured here") else "DEMO: simulated reference data"
                sprintf "%s%s  L live/demo · P pause · S sort · R reverse · / filter · ? help · Q quit" source (if model.Paused then " [PAUSED]" else "")))
            field 1 (height-2) (width-2) red (state model (fun () -> "Sampling: "+model.Error))
            |> bindStyle (state model (fun () -> Visible(model.Error<>"")::Foreground red::bounds 1 (height-2) (width-2) 1))
            let modalWidth,modalHeight=min 72 (width-4),min 13 (height-4)
            if modalWidth>=8 && modalHeight>=5 then modal model modalWidth modalHeight
        }
