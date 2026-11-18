namespace Ranvier.Terminal

open System
open System.Collections.Generic
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

type internal PosixPtySession =
    { Read: byte[] -> CancellationToken -> Task<int>
      Write: byte[] -> CancellationToken -> Task
      Resize: int -> int -> unit
      WaitForExit: unit -> Task
      ExitCode: unit -> int
      Terminate: unit -> unit
      Dispose: unit -> unit }

module internal PosixPty =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type WindowSize =
        val mutable Rows: uint16
        val mutable Columns: uint16
        val mutable X: uint16
        val mutable Y: uint16

    [<DllImport("libc", SetLastError = true)>]
    extern int posix_openpt(int flags)
    [<DllImport("libc", SetLastError = true)>]
    extern int grantpt(int descriptor)
    [<DllImport("libc", SetLastError = true)>]
    extern int unlockpt(int descriptor)
    [<DllImport("libc", SetLastError = true)>]
    extern int ptsname_r(int descriptor, byte[] name, unativeint size)
    [<DllImport("libc", SetLastError = true, EntryPoint = "open")>]
    extern int openSlave([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags)
    [<DllImport("libc", SetLastError = true, EntryPoint = "ioctl")>]
    extern int ioctlName(int descriptor, unativeint request, byte[] name)
    [<DllImport("libc", SetLastError = true)>]
    extern int close(int descriptor)
    [<DllImport("libc", SetLastError = true)>]
    extern int pipe(int[] descriptors)
    [<DllImport("libc", SetLastError = true)>]
    extern int pipe2(int[] descriptors, int flags)
    [<DllImport("libc", SetLastError = true)>]
    extern int fcntl(int descriptor, int command, int flags)
    [<DllImport("libc", SetLastError = true)>]
    extern nativeint read(int descriptor, byte[] bytes, unativeint count)
    [<DllImport("libc", SetLastError = true)>]
    extern nativeint write(int descriptor, nativeint bytes, unativeint count)
    [<DllImport("libc", SetLastError = true, EntryPoint = "write")>]
    extern nativeint writeWake(int descriptor, byte[] bytes, unativeint count)
    [<DllImport("libc", SetLastError = true, EntryPoint = "poll")>]
    extern int pollLinux(nativeint descriptors, unativeint count, int timeout)
    [<DllImport("libc", SetLastError = true, EntryPoint = "poll")>]
    extern int pollMac(nativeint descriptors, uint32 count, int timeout)
    [<DllImport("libc", SetLastError = true)>]
    extern int ioctl(int descriptor, unativeint request, WindowSize& size)
    [<DllImport("ranvier_terminal_pty")>]
    extern int ranvier_spawn_pty(int& pid, int master, int slave, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string directory, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string executable, nativeint arguments, nativeint environment)
    [<DllImport("libc")>]
    extern int posix_spawnattr_init(nativeint attributes)
    [<DllImport("libc")>]
    extern int posix_spawnattr_destroy(nativeint attributes)
    [<DllImport("libc")>]
    extern int posix_spawnattr_setflags(nativeint attributes, int16 flags)
    [<DllImport("libc")>]
    extern int posix_spawnattr_setsigdefault(nativeint attributes, nativeint signals)
    [<DllImport("libc")>]
    extern int posix_spawnattr_setsigmask(nativeint attributes, nativeint signals)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_init(nativeint actions)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_destroy(nativeint actions)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_addclose(nativeint actions, int descriptor)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_addopen(nativeint actions, int descriptor, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags, uint32 mode)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_adddup2(nativeint actions, int source, int destination)
    [<DllImport("libc")>]
    extern int posix_spawn_file_actions_addchdir_np(nativeint actions, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path)
    [<DllImport("libc")>]
    extern int posix_spawn(int& pid, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, nativeint actions, nativeint attributes, nativeint arguments, nativeint environment)
    [<DllImport("libc")>]
    extern int sigemptyset(nativeint signals)
    [<DllImport("libc")>]
    extern int sigfillset(nativeint signals)
    [<DllImport("libc")>]
    extern int sigdelset(nativeint signals, int signal)
    [<DllImport("libc", SetLastError = true)>]
    extern int sigaction(int signal, nativeint action, nativeint previous)
    [<DllImport("libc", SetLastError = true)>]
    extern int waitid(int kind, uint32 pid, nativeint information, int options)
    [<DllImport("libc", SetLastError = true)>]
    extern int waitpid(int pid, int& status, int options)
    [<DllImport("libc", SetLastError = true)>]
    extern int kill(int pid, int signal)
    [<DllImport("ranvier_terminal_pty")>]
    extern int ranvier_signal_group(int group, int& deniedPid)
    [<DllImport("libc")>]
    extern int getpid()
    [<DllImport("libc", SetLastError = true)>]
    extern int access([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int mode)
    [<DllImport("libc")>]
    extern nativeint gnu_get_libc_version()

    let private errno () = Marshal.GetLastPInvokeError()
    let private check result = if result < 0 then raise (Win32Exception(errno ()))
    let private spawnCheck result = if result <> 0 then raise (Win32Exception result)
    let private size columns rows =
        if columns < 1 || rows < 1 || columns > int UInt16.MaxValue || rows > int UInt16.MaxValue then
            invalidArg "dimensions" "PTY dimensions must be positive and fit unsigned terminal coordinates."
        let mutable result = Unchecked.defaultof<WindowSize>
        result.Columns <- uint16 columns
        result.Rows <- uint16 rows
        result

    let private requireSupported () =
        let mac = OperatingSystem.IsMacOS()
        if not (mac || OperatingSystem.IsLinux()) then raise (PlatformNotSupportedException "POSIX PTYs require Linux glibc or macOS.")
        if RuntimeInformation.ProcessArchitecture <> Architecture.X64 && RuntimeInformation.ProcessArchitecture <> Architecture.Arm64 then
            raise (PlatformNotSupportedException "POSIX PTY ABI support is restricted to x64 and arm64.")
        if mac && RuntimeInformation.ProcessArchitecture = Architecture.Arm64 then
            raise (PlatformNotSupportedException "macOS arm64 PTYs require a verified native variadic-call bridge. Use redirected mode on this platform.")
        if not mac then
            let version =
                try Version.Parse(Marshal.PtrToStringUTF8(gnu_get_libc_version()))
                with
                | :? EntryPointNotFoundException | :? DllNotFoundException ->
                    raise (PlatformNotSupportedException "Linux PTYs require glibc, not musl.")
            if version < Version(2, 29) then raise (PlatformNotSupportedException "Linux PTYs require glibc with spawn chdir support.")
        if getpid () = 1 then raise (PlatformNotSupportedException "Native PTY child ownership requires an init process; do not run this host as PID 1.")

    let supportsSize columns rows =
        try
            requireSupported ()
            size columns rows |> ignore
            true
        with :? PlatformNotSupportedException -> false

    let start (fileName: string) arguments workingDirectory environment columns rows =
        requireSupported ()
        let mac = OperatingSystem.IsMacOS()
        let disposition = Marshal.AllocHGlobal 256
        try
            check (sigaction((if mac then 20 else 17), 0n, disposition))
            let flags = Marshal.ReadInt32(disposition, if mac then 12 else 136)
            if Marshal.ReadIntPtr disposition = 1n || flags &&& (if mac then 0x20 else 2) <> 0 then
                invalidOp "PTY child ownership requires SIGCHLD not ignored and automatic child reaping disabled."
        finally Marshal.FreeHGlobal disposition
        let mutable dimensions = size columns rows
        let nonblocking = if mac then 4 else 2048
        let noControlling = if mac then 0x20000 else 0x100
        let closeOnExec = if mac then 0x1000000 else 0x80000
        let windowRequest = if mac then 0x80087467un else 0x5414un
        let waitOptions = 4 ||| (if mac then 0x20 else 0x01000000)
        let variables = SortedDictionary<string, string>(StringComparer.Ordinal)
        for entry in Environment.GetEnvironmentVariables() do
            let pair = unbox<System.Collections.DictionaryEntry> entry
            variables[string pair.Key] <- string pair.Value
        for key, value in environment do
            match value with Some text -> variables[key] <- text | None -> variables.Remove key |> ignore
        let directory = Path.GetFullPath(defaultArg workingDirectory Environment.CurrentDirectory)
        let executable =
            if fileName.Contains('/') then Path.GetFullPath(fileName, directory)
            else
                let path = match variables.TryGetValue "PATH" with true, value -> value | _ -> "/bin:/usr/bin"
                let candidates = path.Split(':') |> Array.map (fun part -> Path.GetFullPath(Path.Combine((if part = "" then directory else part), fileName), directory))
                match candidates |> Array.tryFind (fun candidate -> File.Exists candidate && access(candidate, 1) = 0) with
                | Some candidate -> candidate
                | None -> raise (Win32Exception(if candidates |> Array.exists File.Exists then 13 else 2))
        let allocations = ResizeArray<nativeint>()
        let vector values =
            let strings = values |> Array.map (fun value -> let pointer = Marshal.StringToCoTaskMemUTF8 value in allocations.Add pointer; pointer)
            let result = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size)
            for index = 0 to strings.Length - 1 do Marshal.WriteIntPtr(result, index * IntPtr.Size, strings[index])
            Marshal.WriteIntPtr(result, strings.Length * IntPtr.Size, 0n)
            result
        let attributes = Marshal.AllocHGlobal(if mac then IntPtr.Size else 336)
        let actions = Marshal.AllocHGlobal(if mac then IntPtr.Size else 80)
        let signals = Marshal.AllocHGlobal(if mac then 4 else 128)
        let mutable attributesReady, actionsReady = false, false
        let mutable argv, envp = 0n, 0n
        let mutable master = -1
        let mutable slaveGuard = -1
        let mutable pid = 0
        let pipeDescriptors = ResizeArray<int>()
        try
            try
                master <- posix_openpt(2 ||| noControlling ||| closeOnExec ||| nonblocking)
                check master
                check (grantpt master)
                check (unlockpt master)
                let slaveBytes = Array.zeroCreate<byte> (if mac then 128 else 1024)
                if mac then check (ioctlName(master, 0x40807453un, slaveBytes))
                else spawnCheck (ptsname_r(master, slaveBytes, unativeint slaveBytes.Length))
                let slave = Text.Encoding.UTF8.GetString(slaveBytes, 0, Array.IndexOf(slaveBytes, 0uy))
                // Darwin's first slave open resets winsize; retain a non-controlling guard through spawn.
                slaveGuard <- openSlave(slave, 2 ||| noControlling ||| closeOnExec)
                check slaveGuard
                check (ioctl(master, windowRequest, &dimensions))
                argv <- vector (Array.ofList (fileName :: arguments))
                envp <- vector (variables |> Seq.map (fun pair -> pair.Key + "=" + pair.Value) |> Seq.toArray)
                let mutable spawnedPid = 0
                if mac then
                    spawnCheck (ranvier_spawn_pty(&spawnedPid, master, slaveGuard, directory, executable, argv, envp))
                else
                    spawnCheck (posix_spawnattr_init attributes)
                    attributesReady <- true
                    spawnCheck (posix_spawn_file_actions_init actions)
                    actionsReady <- true
                    check (sigfillset signals)
                    check (sigdelset(signals, 9))
                    check (sigdelset(signals, 19))
                    spawnCheck (posix_spawnattr_setsigdefault(attributes, signals))
                    check (sigemptyset signals)
                    spawnCheck (posix_spawnattr_setsigmask(attributes, signals))
                    spawnCheck (posix_spawnattr_setflags(attributes, int16 (0x80 ||| 0x0C)))
                    spawnCheck (posix_spawn_file_actions_addclose(actions, master))
                    spawnCheck (posix_spawn_file_actions_addclose(actions, slaveGuard))
                    spawnCheck (posix_spawn_file_actions_addchdir_np(actions, directory))
                    // Opening after SETSID without O_NOCTTY establishes the child's controlling terminal.
                    spawnCheck (posix_spawn_file_actions_addopen(actions, 0, slave, 2, 0u))
                    spawnCheck (posix_spawn_file_actions_adddup2(actions, 0, 1))
                    spawnCheck (posix_spawn_file_actions_adddup2(actions, 0, 2))
                    spawnCheck (posix_spawn(&spawnedPid, executable, actions, attributes, argv, envp))
                pid <- spawnedPid
                close slaveGuard |> ignore
                slaveGuard <- -1
                let makeWake () =
                    let descriptors = Array.zeroCreate<int> 2
                    if mac then check (pipe descriptors)
                    else check (pipe2(descriptors, closeOnExec ||| nonblocking))
                    pipeDescriptors.AddRange descriptors
                    if mac then
                        // Darwin has no pipe2: unrelated concurrent native spawns can race this setup.
                        for descriptor in descriptors do
                            check (fcntl(descriptor, 2, 1))
                            let flags = fcntl(descriptor, 3, 0)
                            check flags
                            check (fcntl(descriptor, 4, flags ||| nonblocking))
                    descriptors
                let readerWake, writerWake = makeWake (), makeWake ()
                let descriptor = master
                let childId = pid
                let lifeGate, ioGate = obj (), obj ()
                let lifetime = new CancellationTokenSource()
                let lifetimeToken = lifetime.Token
                let mutable reaped = false
                let mutable lost = false
                let mutable deferredReap = false
                let mutable exitCode = 0
                let mutable disposed = false
                let mutable active = 0
                let signalGroup () =
                    if mac then
                        let mutable deniedPid = 0
                        let error = ranvier_signal_group(childId, &deniedPid)
                        if error <> 0 && error <> 3 && deniedPid <> 0 then
                            raise (Win32Exception(error, sprintf "Cannot terminate process %d in PTY group %d." deniedPid childId))
                        error
                    elif kill(-childId, 9) = 0 then 0
                    else errno ()
                let terminate () =
                    lock lifeGate (fun () ->
                        if not reaped && not lost && not deferredReap then
                            let error = signalGroup ()
                            if error <> 0 && error <> 3 then raise (Win32Exception error))
                let reap () =
                    lock lifeGate (fun () ->
                        if not reaped && not lost && not deferredReap then
                            // The unreaped leader reserves its ID until this last group signal.
                            let signalError = signalGroup ()
                            let mutable status = 0
                            let mutable result = waitpid(childId, &status, 1)
                            while result < 0 && errno () = 4 do result <- waitpid(childId, &status, 1)
                            if result < 0 then
                                lost <- true
                                check result
                            elif result = 0 then
                                // If credentials prevent termination, release descriptors promptly.
                                // Hand off ONLY this child to an exact waiter and never signal its ID again.
                                deferredReap <- true
                                Task.Factory.StartNew(Action(fun () ->
                                    let mutable finalStatus = 0
                                    let mutable waited = waitpid(childId, &finalStatus, 0)
                                    while waited < 0 && errno () = 4 do waited <- waitpid(childId, &finalStatus, 0)
                                    lock lifeGate (fun () ->
                                        reaped <- waited = childId
                                        lost <- waited <> childId)), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default) |> ignore
                                if signalError <> 0 && signalError <> 3 then raise (Win32Exception signalError)
                                invalidOp "PTY child cleanup continues in an exact native waiter."
                            else
                                reaped <- true
                                exitCode <- if status &&& 0x7f = 0 then (status >>> 8) &&& 255 else 128 + (status &&& 0x7f)
                            if signalError <> 0 && signalError <> 3 then raise (Win32Exception signalError)
                        if lost then invalidOp "PTY child status was reaped externally; native child ownership was lost."
                        exitCode)
                let waitExit () =
                    Task.Factory.StartNew(Action(fun () ->
                        let info = Marshal.AllocHGlobal 128
                        try
                            let mutable finished = false
                            while not finished do
                                let result = waitid(1, uint32 childId, info, waitOptions)
                                if result < 0 then
                                    let error = errno ()
                                    if error = 10 then lock lifeGate (fun () -> lost <- true)
                                    if error <> 4 then raise (Win32Exception error)
                                else
                                    let code = Marshal.ReadInt32(info, 8)
                                    finished <- code >= 1 && code <= 3
                                    if not finished then Thread.Sleep 10
                        finally Marshal.FreeHGlobal info), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                let enter () =
                    lock ioGate (fun () ->
                        if disposed then raise (ObjectDisposedException "PosixPty")
                        active <- active + 1)
                let leave () = lock ioGate (fun () -> active <- active - 1; Monitor.PulseAll ioGate)
                let operation (wake: int[]) (token: CancellationToken) body =
                    enter ()
                    try
                        use linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken)
                        let notification = [| 1uy |]
                        use registration = linked.Token.Register(fun () ->
                            let mutable result = writeWake(wake[1], notification, 1un)
                            while result < 0n && errno () = 4 do result <- writeWake(wake[1], notification, 1un))
                        let polling = Marshal.AllocHGlobal 16
                        try
                            Marshal.WriteInt32(polling, 0, descriptor)
                            Marshal.WriteInt32(polling, 8, wake[0])
                            Marshal.WriteInt16(polling, 12, 1s)
                            let drain = Array.zeroCreate<byte> 64
                            let wait (events: int16) =
                                linked.Token.ThrowIfCancellationRequested()
                                Marshal.WriteInt16(polling, 4, events)
                                let mutable result = if mac then pollMac(polling, 2u, -1) else pollLinux(polling, 2un, -1)
                                while result < 0 && errno () = 4 do result <- if mac then pollMac(polling, 2u, -1) else pollLinux(polling, 2un, -1)
                                check result
                                if int (Marshal.ReadInt16(polling, 14)) &&& 1 <> 0 then
                                    while read(wake[0], drain, unativeint drain.Length) = nativeint drain.Length do ()
                                linked.Token.ThrowIfCancellationRequested()
                            body linked.Token wait
                        finally Marshal.FreeHGlobal polling
                    finally leave ()
                let readAsync (bytes: byte[]) token = Task.Run<int>(fun () ->
                    operation readerWake token (fun token wait ->
                        let mutable result = -1
                        while result < 0 do
                            token.ThrowIfCancellationRequested()
                            let count = read(descriptor, bytes, unativeint bytes.Length)
                            if count >= 0n then result <- int count
                            else
                                match errno () with
                                | 4 -> ()
                                | error when error = (if mac then 35 else 11) -> wait 1s
                                | 5 when not mac -> result <- 0
                                | error -> raise (Win32Exception error)
                        result))
                let writeAsync (bytes: byte[]) token = Task.Run(Action(fun () ->
                    operation writerWake token (fun token wait ->
                        let pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned)
                        try
                            let mutable offset = 0
                            while offset < bytes.Length do
                                token.ThrowIfCancellationRequested()
                                let count = write(descriptor, pinned.AddrOfPinnedObject() + nativeint offset, unativeint (bytes.Length - offset))
                                if count > 0n then offset <- offset + int count
                                elif count = 0n then raise (IOException "PTY input made no progress.")
                                else
                                    match errno () with
                                    | 4 -> ()
                                    | error when error = (if mac then 35 else 11) -> wait 4s
                                    | error -> raise (IOException("PTY input failed.", Win32Exception error))
                        finally pinned.Free())))
                let dispose () =
                    let owner =
                        lock ioGate (fun () ->
                            if disposed then false
                            else
                                disposed <- true
                                true)
                    if owner then
                        try
                            lifetime.Cancel()
                            lock ioGate (fun () -> while active <> 0 do Monitor.Wait ioGate |> ignore)
                            reap () |> ignore
                        finally
                            close descriptor |> ignore
                            for fd in readerWake do close fd |> ignore
                            for fd in writerWake do close fd |> ignore
                            lifetime.Dispose()
                master <- -1
                pid <- 0
                pipeDescriptors.Clear()
                { Read = readAsync
                  Write = writeAsync
                  Resize = fun columns rows ->
                      let mutable next = size columns rows
                      enter ()
                      try check (ioctl(descriptor, windowRequest, &next))
                      finally leave ()
                  WaitForExit = waitExit
                  ExitCode = reap
                  Terminate = terminate
                  Dispose = dispose }
            with _ ->
                if pid > 0 then
                    kill(-pid, 9) |> ignore
                    let ownedPid = pid
                    let mutable status = 0
                    let mutable result = waitpid(ownedPid, &status, 1)
                    while result < 0 && errno () = 4 do result <- waitpid(ownedPid, &status, 1)
                    if result = 0 then
                        Task.Factory.StartNew(Action(fun () ->
                            let mutable finalStatus = 0
                            let mutable waited = waitpid(ownedPid, &finalStatus, 0)
                            while waited < 0 && errno () = 4 do waited <- waitpid(ownedPid, &finalStatus, 0)), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default) |> ignore
                if master >= 0 then close master |> ignore
                if slaveGuard >= 0 then close slaveGuard |> ignore
                for descriptor in pipeDescriptors do close descriptor |> ignore
                reraise ()
        finally
            if actionsReady then posix_spawn_file_actions_destroy actions |> ignore
            if attributesReady then posix_spawnattr_destroy attributes |> ignore
            Marshal.FreeHGlobal attributes
            Marshal.FreeHGlobal actions
            Marshal.FreeHGlobal signals
            if argv <> 0n then Marshal.FreeHGlobal argv
            if envp <> 0n then Marshal.FreeHGlobal envp
            for pointer in allocations do Marshal.FreeCoTaskMem pointer
