namespace SystemMonitor

open System
open System.Diagnostics
open System.IO
open System.Net.NetworkInformation
open System.Runtime.InteropServices
open System.Threading

type ProcessRow = { Pid: int; Name: string; Command: string; User: string; Memory: int64; Cpu: float; CpuTime: float }
type Disk = { Name: string; Total: int64; Free: int64 }
type Sample =
    { Name: string; Cpu: float; Cores: float array; Total: int64; Available: int64; Cached: int64 option
      Disks: Disk array; Processes: ProcessRow array; Received: int64; Sent: int64; Download: float; Upload: float
      Network: string; Load: string; Uptime: TimeSpan; Stamp: DateTime; CpuCounters: (int64 * int64) array; Demo: bool }

module Telemetry =
    let gib value = int64 (value * 1073741824.)
    let demo step =
        let names = [| "Composer"; "dotnet"; "code"; "chromium"; "chromium"; "code"; "chromium"; "code"; "agy"; "Discord"; "btop"; "Hyprland"; "signal-desktop" |]
        let pids=[|869449;235424;2824287;863444;691792;530732;74285;2822914;2824118;404964;903928;1871;3436721|]
        let memory=[|gib 1.5;gib 13.;732L*1048576L;304L*1048576L;299L*1048576L;44L*1048576L;454L*1048576L;165L*1048576L;gib 2.;512L*1048576L;34L*1048576L;104L*1048576L;310L*1048576L|]
        { Name = "RYZEN AI MAX+ 395 w/ Radeon 8060S"; Cpu = 7. + 2. * sin (float step * 0.4)
          Cores = Array.init 32 (fun i -> if i=1 || i=4 || i=17 then 42. + 27. * sin (float(step+i)*0.3) else float ((i+step)%6))
          Total = gib 58.4; Available = gib 21.6; Cached = Some(gib 15.7)
          Disks = [| for name in ["root";"swap";"log";"home";"pkg";"lab-home";"lab-models";"boot"] ->
                       let total,free=match name with "swap" -> 58.,6.6 | "lab-home" -> 14336.,0. | "lab-models" -> 15360.,356. | "boot" -> 2.,0.7 | _ -> 1843.,926.
                       {Name=name; Total=gib total; Free=gib free} |]
          Processes = Array.init 670 (fun i ->
              let name=names[i%names.Length]
              { Pid=(if i<pids.Length then pids[i] else 3000000+i*357); Name=name; Command=(if name="chromium" then "/usr/lib/chromium" else "/home/hhh/repos/"+name)
                User="hhh"; Memory=if i<memory.Length then memory[i] else gib (0.15 + float ((i*7)%16)*0.09)
                Cpu=(if i<2 then 3.14-float i*0.04+0.03*sin(float step) elif i=2 then 0.24 else float(670-i)/100000.); CpuTime=0. })
          Received=gib 17.9; Sent=gib 17.9; Download=1740. + 220.*sin(float step*0.2); Upload=1740.+180.*cos(float step*0.2)
          Network="127.0.0.1"; Load="2.89 3.48 3.68"; Uptime=TimeSpan.FromDays(4.)+TimeSpan.FromMinutes(22.)
          Stamp=DateTime.Now; CpuCounters=[||]; Demo=true }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type MemoryStatus =
        val mutable Length: uint32
        val mutable Load: uint32
        val mutable Total: uint64
        val mutable Available: uint64
        val mutable PageTotal: uint64
        val mutable PageAvailable: uint64
        val mutable VirtualTotal: uint64
        val mutable VirtualAvailable: uint64
        val mutable Extended: uint64
    [<DllImport("kernel32.dll", SetLastError=true)>]
    extern bool GlobalMemoryStatusEx(MemoryStatus& status)
    [<DllImport("kernel32.dll", SetLastError=true)>]
    extern bool GetSystemTimes(int64& idle, int64& kernel, int64& user)

    let private counters () =
        if OperatingSystem.IsWindows() then
            let mutable idle, kernel, user = 0L, 0L, 0L
            if not (GetSystemTimes(&idle,&kernel,&user)) then [||] else [| kernel+user,idle |]
        elif OperatingSystem.IsLinux() then
            File.ReadLines "/proc/stat"
            |> Seq.takeWhile (fun line -> line.StartsWith "cpu")
            |> Seq.map (fun line ->
                let values=line.Split(' ',StringSplitOptions.RemoveEmptyEntries)[1..] |> Array.map Int64.Parse
                Array.sum values[..min 7 (values.Length-1)],values[3]+(if values.Length>4 then values[4] else 0L))
            |> Seq.toArray
        else [||]

    let private memory () =
        if OperatingSystem.IsWindows() then
            let mutable status=Unchecked.defaultof<MemoryStatus>
            status.Length <- uint32 (Marshal.SizeOf<MemoryStatus>())
            if GlobalMemoryStatusEx(&status) then int64 status.Total,int64 status.Available,None else 0L,0L,None
        elif OperatingSystem.IsLinux() then
            let fields=File.ReadLines "/proc/meminfo" |> Seq.map(fun line ->
                let pieces=line.Split([|' ';':'|],StringSplitOptions.RemoveEmptyEntries)
                pieces[0],Int64.Parse(pieces[1])*1024L) |> Map.ofSeq
            fields["MemTotal"],fields["MemAvailable"],Map.tryFind "Cached" fields
        else 0L,0L,None

    let read (previous: Sample option) (cancellation: CancellationToken) =
        cancellation.ThrowIfCancellationRequested()
        let now=DateTime.Now
        let elapsed=previous |> Option.map(fun old -> max 0.001 (now-old.Stamp).TotalSeconds) |> Option.defaultValue 1.
        let counts=counters()
        let loads=counts |> Array.mapi(fun index (total,idle) ->
            match previous with
            | Some old when not old.Demo && index<old.CpuCounters.Length ->
                let oldTotal,oldIdle=old.CpuCounters[index]
                if total>oldTotal then Math.Clamp(100.*(1.-float(idle-oldIdle)/float(total-oldTotal)),0.,100.) else 0.
            | _ -> 0.)
        let total,available,cached=memory()
        let oldRows=previous |> Option.map(fun old -> old.Processes |> Array.map(fun row -> row.Pid,row) |> Map.ofArray) |> Option.defaultValue Map.empty
        let children=Process.GetProcesses()
        let rows=ResizeArray<ProcessRow>()
        try
            for child in children do
                cancellation.ThrowIfCancellationRequested()
                try
                    let cpu=child.TotalProcessorTime.TotalSeconds
                    let fraction=
                        match Map.tryFind child.Id oldRows with
                        | Some old when cpu>=old.CpuTime -> Math.Clamp((cpu-old.CpuTime)/elapsed/float Environment.ProcessorCount*100.,0.,100.)
                        | _ -> 0.
                    let command=try child.MainModule.FileName with _ -> child.ProcessName
                    rows.Add {Pid=child.Id;Name=child.ProcessName;Command=command;User="—";Memory=child.WorkingSet64;Cpu=fraction;CpuTime=cpu}
                with
                | :? InvalidOperationException | :? System.ComponentModel.Win32Exception | :? NotSupportedException -> ()
        finally for child in children do child.Dispose()
        let disks=DriveInfo.GetDrives() |> Array.choose(fun drive ->
            try if drive.IsReady then Some {Name=drive.Name;Total=drive.TotalSize;Free=drive.AvailableFreeSpace} else None
            with :? IOException | :? UnauthorizedAccessException -> None)
        let mutable received,sent=0L,0L
        for network in NetworkInterface.GetAllNetworkInterfaces() do
            try
                if network.OperationalStatus=OperationalStatus.Up then
                    let stats=network.GetIPStatistics()
                    received <- received+stats.BytesReceived
                    sent <- sent+stats.BytesSent
            with :? NetworkInformationException -> ()
        let download,upload=
            match previous with
            | Some old when not old.Demo -> max 0. (float(received-old.Received)/elapsed),max 0. (float(sent-old.Sent)/elapsed)
            | _ -> 0.,0.
        { Name=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") |> Option.ofObj |> Option.defaultValue RuntimeInformation.OSDescription
          Cpu=(if loads.Length>0 then loads[0] else 0.); Cores=(if loads.Length>1 then loads[1..] else [||])
          Total=total; Available=available; Cached=cached; Disks=disks; Processes=rows.ToArray(); Received=received;Sent=sent
          Download=download;Upload=upload; Network="all interfaces"; Load=if OperatingSystem.IsLinux() then String.Join(" ",(File.ReadAllText "/proc/loadavg").Split(' ')[..2]) else "unavailable"
          Uptime=TimeSpan.FromMilliseconds(float Environment.TickCount64);Stamp=now;CpuCounters=counts;Demo=false }
