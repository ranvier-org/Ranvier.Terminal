module PosixChecks

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Ranvier.Terminal

[<DllImport("libc", SetLastError=true)>]
extern int getsid(int pid)
[<DllImport("libc", SetLastError=true)>]
extern int getpgrp()
[<DllImport("libc", SetLastError=true)>]
extern int tcgetpgrp(int fd)
[<DllImport("libc", SetLastError=true)>]
extern int kill(int pid, int signal)
[<DllImport("libc", SetLastError=true)>]
extern int waitpid(int pid, int& status, int flags)
[<DllImport("libc", SetLastError=true)>]
extern nativeint signal(int number, nativeint handler)
[<DllImport("libc", SetLastError=true)>]
extern int getuid()
[<DllImport("libc", SetLastError=true)>]
extern int setresuid(int real, int effective, int saved)

let private require condition message = if not condition then failwith message

let writePidFile path =
    let pending = path + ".pending"
    File.WriteAllText(pending, string Environment.ProcessId)
    File.Move(pending, path)

let verifyIgnoredSignal () =
    let number = if OperatingSystem.IsMacOS() then 20 else 17
    let previous = signal(number, 1n)
    require (previous <> -1n) "Could not set ignored SIGCHLD fixture"
    try
        let mutable rejected=false
        try
            let session=Subprocess.start {Subprocess.command "/bin/true" [] with Mode=PosixPseudoTerminal(80,24)} CancellationToken.None
            session.Close().GetAwaiter().GetResult()
        with :? InvalidOperationException -> rejected <- true
        require rejected "Ignored SIGCHLD was accepted"
        Console.WriteLine "PASS POSIX ignored SIGCHLD ownership rejection"
    finally signal(number,previous) |> ignore

let private launch arguments =
    let executable = Environment.ProcessPath
    let prefix = if Path.GetFileNameWithoutExtension executable = "dotnet" then [Assembly.GetExecutingAssembly().Location] else []
    executable, prefix @ ["--child"] @ arguments

let tryChild (args: string array) =
    match args[0] with
    | "posix-identity" ->
        use tty = new FileStream("/dev/tty", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
        Console.WriteLine(sprintf "IDENTITY:%d:%d:%d:%d" Environment.ProcessId (getsid 0) (getpgrp()) (tcgetpgrp 0))
        Console.WriteLine "READY"
        Console.Out.Flush()
        Console.ReadLine() |> ignore
        Some 0
    | "posix-stubborn" ->
        use hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, fun context -> context.Cancel <- true)
        use term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, fun context -> context.Cancel <- true)
        Console.WriteLine(sprintf "READY:%d" Environment.ProcessId)
        Console.Out.Flush()
        Thread.Sleep Timeout.Infinite
        Some 0
    | "posix-winch" ->
        let mutable changes=0
        use registration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, fun context ->
            context.Cancel <- true
            Interlocked.Increment(&changes) |> ignore)
        Console.WriteLine "READY"
        Console.Out.Flush()
        Console.ReadLine() |> ignore
        SpinWait.SpinUntil((fun () -> Volatile.Read(&changes)>0),1000) |> ignore
        Console.WriteLine(sprintf "WINCH:%d SIZE:%d,%d" (Volatile.Read(&changes)) Console.WindowWidth Console.WindowHeight)
        Some 0
    | "posix-descendant" ->
        use hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, fun context -> context.Cancel <- true)
        writePidFile args[1]
        Thread.Sleep Timeout.Infinite
        Some 0
    | "posix-orphan" ->
        let executable, arguments = launch ["posix-descendant"; args[1]]
        let start = ProcessStartInfo(executable, UseShellExecute=false)
        for argument in arguments do start.ArgumentList.Add argument
        use descendant = Process.Start start
        let timer = Stopwatch.StartNew()
        while not (File.Exists args[1]) && timer.Elapsed.TotalSeconds < 5. do Thread.Sleep 5
        require (File.Exists args[1]) "descendant failed to initialize"
        Console.WriteLine(sprintf "ROOT:%d" Environment.ProcessId)
        Console.Out.Flush()
        Some 17
    | _ -> None

let private config arguments =
    let executable, arguments = launch arguments
    { Subprocess.command executable arguments with Mode=PosixPseudoTerminal(80,24); OutputCapacity=4096 }

let private bounded (work: Task) = task {
    let! winner = Task.WhenAny(work, Task.Delay 10000)
    require (obj.ReferenceEquals(work,winner)) "POSIX lifecycle operation timed out"
    do! work
}

let private drain (session: SubprocessSession) (text: StringBuilder) =
    for chunk in session.Drain 65536 do
        require (chunk.Kind=TerminalOutput) "PTY output was not unified"
        text.Append(Encoding.UTF8.GetString chunk.Bytes) |> ignore

let private until description condition (session: SubprocessSession) text = task {
    let timer=Stopwatch.StartNew()
    while not (condition()) && timer.Elapsed.TotalSeconds < 10. do
        drain session text
        do! Task.Delay 5
    require (condition()) ("Timeout: "+description+"; "+text.ToString())
}

let private gone pid = task {
    let timer=Stopwatch.StartNew()
    while kill(pid,0)=0 && timer.Elapsed.TotalSeconds < 5. do do! Task.Delay 10
    require (kill(pid,0) = -1 && Marshal.GetLastPInvokeError()=3) (sprintf "Process %d remained after cleanup" pid)
}

let private reaped pid =
    let mutable status=0
    require (waitpid(pid,&status,1) = -1 && Marshal.GetLastPInvokeError()=10) "Owned child was not reaped exactly once"

let verifyDeniedSignal () = task {
    require (OperatingSystem.IsLinux() && getuid()=0 && Environment.ProcessId<>1) "Run credential fixture only in a root Linux test container with init"
    let session=Subprocess.start (config ["posix-stubborn"]) CancellationToken.None
    let text=StringBuilder()
    let mutable pid=0
    try
        do! until "credential fixture child ready" (fun () -> text.ToString().Contains "READY:") session text
        pid <- Int32.Parse(Regex.Match(text.ToString(),"READY:(\\d+)").Groups[1].Value)
        let before=Directory.GetFiles("/proc/self/fd").Length
        require (setresuid(65534,65534,0)=0) "Could not temporarily drop fixture credentials"
        try
            require (kill(pid,0) = -1 && Marshal.GetLastPInvokeError()=1) "Fixture can still signal child"
            let observed=task {
                let mutable failed=false
                try do! session.Close()
                with :? Win32Exception -> failed <- true
                require failed "Denied termination did not surface its failure"
            }
            do! bounded observed
        finally require (setresuid(0,0,0)=0) "Could not restore fixture credentials"
        require (Directory.GetFiles("/proc/self/fd").Length < before) "Denied termination retained transport descriptors"
        require (kill(pid,9)=0) "Could not clean up privileged fixture child"
        do! gone pid
        reaped pid
        Console.WriteLine "PASS POSIX denied group signal faults close, releases descriptors and defers exact child reaping"
    finally
        if getuid()<>0 then setresuid(0,0,0) |> ignore
        if pid>0 && kill(pid,0)=0 then kill(pid,9) |> ignore
        session.Close() |> ignore
}

let run log = task {
    // Initialize the runtime's managed child reaper before creating the native-spawn child.
    use managed = Process.Start(ProcessStartInfo("true", UseShellExecute=false))
    do! managed.WaitForExitAsync()
    let identity=Subprocess.start (config ["posix-identity"]) CancellationToken.None
    let output=StringBuilder()
    try
        do! until "controlling terminal identity" (fun () -> output.ToString().Contains "READY") identity output
        let matched=Regex.Match(output.ToString(),"IDENTITY:(\\d+):(\\d+):(\\d+):(\\d+)")
        require matched.Success "Missing native process identity"
        let pid=Int32.Parse matched.Groups[1].Value
        for index=2 to 4 do require (Int32.Parse matched.Groups[index].Value=pid) "PTY lacks an owned session/process group/foreground terminal"
        for width,height in [1,1;123,37;80,24] do identity.Resize width height
        for width,height in [0,24;80,0;-1,24;65536,24] do
            let mutable rejected=false
            try identity.Resize width height with :? ArgumentException -> rejected <- true
            require rejected "Invalid PTY resize was accepted"
        do! identity.Write (Encoding.UTF8.GetBytes "done\n") CancellationToken.None
        do! until "native/managed reaper coexistence" (fun () -> identity.Exit.IsCompleted) identity output
        let! code=identity.Exit
        require (code=0) "Normal native child exit code changed"
        do! bounded (identity.Close())
        reaped pid
        let mutable closed=false
        try identity.Resize 80 24 with :? ObjectDisposedException -> closed <- true
        require closed "Resize after close did not reject"
    finally identity.Close() |> ignore
    log "PASS POSIX controlling terminal, session identity, managed reaper coexistence, resize bounds and exact reaping"

    let resized=Subprocess.start (config ["posix-winch"]) CancellationToken.None
    let text=StringBuilder()
    try
        do! until "SIGWINCH listener" (fun () -> text.ToString().Contains "READY") resized text
        resized.Resize 97 31
        do! resized.Write (Encoding.UTF8.GetBytes "size\n") CancellationToken.None
        do! until "SIGWINCH child exit" (fun () -> resized.Exit.IsCompleted) resized text
        drain resized text
        let signal=Regex.Match(text.ToString(),"WINCH:(\\d+) SIZE:97,31")
        require (signal.Success && Int32.Parse(signal.Groups[1].Value)>0) "Resize did not deliver SIGWINCH and native dimensions"
        do! bounded (resized.Close())
    finally resized.Close() |> ignore
    log "PASS POSIX resize delivers SIGWINCH and kernel terminal dimensions"

    let interrupt=Subprocess.start {Subprocess.command "/bin/sh" ["-c";"printf READY; exec sleep 30"] with Mode=PosixPseudoTerminal(80,24)} CancellationToken.None
    let text=StringBuilder()
    try
        do! until "interrupt fixture" (fun () -> text.ToString().Contains "READY") interrupt text
        do! interrupt.Write [|3uy|] CancellationToken.None
        do! until "terminal interrupt" (fun () -> interrupt.Exit.IsCompleted) interrupt text
        let! code=interrupt.Exit
        require (code=130) "Terminal Ctrl+C did not signal the foreground process group"
        do! bounded (interrupt.Close())
    finally interrupt.Close() |> ignore
    log "PASS POSIX terminal Ctrl+C and signal exit status"

    let stubborn=Subprocess.start (config ["posix-stubborn"]) CancellationToken.None
    let text=StringBuilder()
    try
        do! until "stubborn child ready" (fun () -> text.ToString().Contains "READY:") stubborn text
        let pid=Int32.Parse(Regex.Match(text.ToString(),"READY:(\\d+)").Groups[1].Value)
        let races = Array.init 8 (fun _ -> Task.Run(fun () ->
            for index=1 to 100 do
                try stubborn.Resize (20+index%20) (10+index%10)
                with :? ObjectDisposedException -> ()))
        do! bounded (stubborn.Close())
        do! Task.WhenAll races
        do! gone pid
        reaped pid
        do! bounded (stubborn.Close())
    finally stubborn.Close() |> ignore
    log "PASS POSIX stubborn child termination, resize/close race and idempotent cleanup"

    let pidFile=Path.Combine(Path.GetTempPath(),"ranvier-pty-"+Guid.NewGuid().ToString("N")+".pid")
    let orphan=Subprocess.start (config ["posix-orphan";pidFile]) CancellationToken.None
    let text=StringBuilder()
    try
        do! until "orphan holder initialization" (fun () -> File.Exists pidFile && text.ToString().Contains "ROOT:") orphan text
        let descendant=Int32.Parse(File.ReadAllText pidFile)
        let root=Int32.Parse(Regex.Match(text.ToString(),"ROOT:(\\d+)").Groups[1].Value)
        do! until "parent exit with inherited slave" (fun () -> orphan.Exit.IsCompleted) orphan text
        let! code=orphan.Exit
        require (code=17) "Orphan cleanup changed parent's exit status"
        do! bounded (orphan.Close())
        do! gone descendant
        reaped root
    finally
        orphan.Close() |> ignore
        if File.Exists pidFile then File.Delete pidFile
    log "PASS POSIX parent exit cleans inherited-group descendant holding the slave"

    let rawOptions = { Subprocess.command "/bin/sh" ["-c";"stty raw -echo; printf READY; exec sleep 30"] with Mode=PosixPseudoTerminal(80,24);OutputCapacity=4096 }
    let raw=Subprocess.start rawOptions CancellationToken.None
    let text=StringBuilder()
    try
        do! until "raw terminal ready" (fun () -> text.ToString().Contains "READY") raw text
        use cancelWrite = new CancellationTokenSource()
        let write=raw.Write (Array.create 1048576 (byte 'x')) cancelWrite.Token
        do! Task.Delay 100
        require (not write.IsCompleted) "Raw input fixture failed to block"
        cancelWrite.Cancel()
        let observed=task {
            let mutable cancelled=false
            try do! write with :? OperationCanceledException -> cancelled <- true
            require cancelled "Individual pending write ignored cancellation"
        }
        do! bounded observed
        require (not raw.Exit.IsCompleted) "Cancelling one write killed the whole session"
        do! bounded (raw.Close())
    finally raw.Close() |> ignore
    log "PASS POSIX pending write cancellation leaves session alive and close finishes"

    use cancelled = new CancellationTokenSource()
    cancelled.Cancel()
    let mutable rejected=false
    try Subprocess.start (config ["posix-stubborn"]) cancelled.Token |> ignore
    with :? OperationCanceledException -> rejected <- true
    require rejected "Pre-cancelled startup spawned a child"
    log "PASS POSIX pre-cancelled startup"

    let descriptors () = Directory.GetFiles("/proc/self/fd").Length
    let beforeCycles = if OperatingSystem.IsLinux() then descriptors() else 0
    for _=1 to 8 do
        use cancellation = new CancellationTokenSource()
        let live=Subprocess.start (config ["posix-stubborn"]) cancellation.Token
        let text=StringBuilder()
        try
            do! until "cancellation child ready" (fun () -> text.ToString().Contains "READY:") live text
            let pid=Int32.Parse(Regex.Match(text.ToString(),"READY:(\\d+)").Groups[1].Value)
            cancellation.Cancel()
            do! bounded live.Exit
            do! bounded (live.Close())
            do! gone pid
            reaped pid
        finally live.Close() |> ignore
    log "PASS POSIX repeated live cancellation kills and reaps stubborn children without caller drain"

    for _=1 to 12 do
        use cancellation = new CancellationTokenSource()
        cancellation.CancelAfter 1
        try
            let race=Subprocess.start (config ["posix-stubborn"]) cancellation.Token
            do! bounded (race.Close())
            do! bounded (race.Close())
        with :? OperationCanceledException -> ()
    log "PASS POSIX cancellation racing native startup"

    if OperatingSystem.IsLinux() then
        require (descriptors() <= beforeCycles) "Successful cancellation cycles leaked native descriptors"
        let before=descriptors()
        for _=1 to 20 do
            let mutable failed=false
            try Subprocess.start {config [] with FileName="/ranvier/no-such-executable"} CancellationToken.None |> ignore
            with :? Win32Exception -> failed <- true
            require failed "Failed exec did not report startup error"
        let cwd=Environment.CurrentDirectory
        let mutable invalidDirectory=false
        try Subprocess.start {config [] with WorkingDirectory=Some "/ranvier/no-such-directory"} CancellationToken.None |> ignore
        with :? Win32Exception -> invalidDirectory <- true
        require (invalidDirectory && Environment.CurrentDirectory=cwd) "Spawn working-directory failure changed parent state"
        require (descriptors() <= before) "Failed startup leaked native descriptors"
        log "PASS POSIX repeated failed startup descriptor cleanup"
}
