namespace Ranvier.Terminal

open System
open System.Collections.Generic
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Win32.SafeHandles

type SubprocessMode =
    | Redirected
    | WindowsPseudoConsole of columns: int * rows: int
    /// Native PTY for glibc Linux x64/arm64 and macOS x64. The macOS backend is
    /// source-reviewed but awaits native CI; macOS arm64 requires a verified variadic ABI bridge.
    | PosixPseudoTerminal of columns: int * rows: int

type SubprocessOptions =
    { FileName: string
      Arguments: string list
      WorkingDirectory: string option
      Environment: (string * string option) list
      Mode: SubprocessMode
      OutputCapacity: int }

type SubprocessOutputKind = StandardOutput | StandardError | TerminalOutput

type SubprocessChunk =
    { Kind: SubprocessOutputKind
      Bytes: byte[] }

type SubprocessSession =
    { Drain: int -> SubprocessChunk array
      /// Wait for queued output without consuming it. False means the session has finished and its queue is empty.
      /// Cancelling this wait leaves the child and other readers running. Observe Exit for the exit code or failure.
      WaitToRead: CancellationToken -> Task<bool>
      DroppedBytes: unit -> int64
      Write: byte[] -> CancellationToken -> Task
      Resize: int -> int -> unit
      Exit: Task<int>
      Close: unit -> Task }

module private SubprocessNative =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type Coord =
        val mutable X: int16
        val mutable Y: int16
        new(x, y) = { X = int16 x; Y = int16 y }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type Startup =
        val mutable Size: uint32
        val mutable Reserved: nativeint
        val mutable Desktop: nativeint
        val mutable Title: nativeint
        val mutable X: uint32
        val mutable Y: uint32
        val mutable Width: uint32
        val mutable Height: uint32
        val mutable Columns: uint32
        val mutable Rows: uint32
        val mutable Fill: uint32
        val mutable Flags: uint32
        val mutable Show: uint16
        val mutable ReservedSize: uint16
        val mutable ReservedData: nativeint
        val mutable Input: nativeint
        val mutable Output: nativeint
        val mutable Error: nativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type StartupEx =
        val mutable Startup: Startup
        val mutable Attributes: nativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type ProcessInfo =
        val mutable Process: nativeint
        val mutable Thread: nativeint
        val mutable Id: uint32
        val mutable ThreadId: uint32

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool CreatePipe(nativeint& read, nativeint& write, nativeint security, uint32 size)
    [<DllImport("kernel32.dll")>]
    extern int CreatePseudoConsole(Coord size, nativeint input, nativeint output, uint32 flags, nativeint& console)
    [<DllImport("kernel32.dll")>]
    extern int ResizePseudoConsole(nativeint console, Coord size)
    [<DllImport("kernel32.dll")>]
    extern int ReleasePseudoConsole(nativeint console)
    [<DllImport("kernel32.dll")>]
    extern void ClosePseudoConsole(nativeint console)
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool InitializeProcThreadAttributeList(nativeint attributes, int count, int flags, unativeint& size)
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool UpdateProcThreadAttribute(nativeint attributes, uint32 flags, unativeint attribute, nativeint value, unativeint size, nativeint previous, nativeint returned)
    [<DllImport("kernel32.dll")>]
    extern void DeleteProcThreadAttributeList(nativeint attributes)
    [<DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)>]
    extern bool CreateProcessW(string application, StringBuilder command, nativeint processSecurity, nativeint threadSecurity, bool inheritHandles, uint32 flags, nativeint environment, string directory, StartupEx& startup, ProcessInfo& information)
    [<DllImport("kernel32.dll")>]
    extern bool CloseHandle(nativeint handle)
    [<DllImport("kernel32.dll")>]
    extern bool TerminateProcess(nativeint handle, uint32 exitCode)

    let check result = if not result then raise (Win32Exception(Marshal.GetLastPInvokeError()))
    let hresult result = if result < 0 then Marshal.ThrowExceptionForHR result
    let dimensions columns rows =
        if columns < 1 || rows < 1 || columns > int Int16.MaxValue || rows > int Int16.MaxValue then
            invalidArg "dimensions" "Pseudoconsole dimensions must fit positive signed console coordinates."
        Coord(columns, rows)

    let quote (value: string) =
        let result = StringBuilder("\"")
        let mutable slashes = 0
        for character in value do
            if character = '\\' then slashes <- slashes + 1
            else
                result.Append('\\', if character = '"' then slashes * 2 + 1 else slashes) |> ignore
                result.Append character |> ignore
                slashes <- 0
        result.Append('\\', slashes * 2).Append('"').ToString()

    let start options columns rows =
        let size = dimensions columns rows
        let mutable inRead, inWrite, outRead, outWrite, console, attributes, environment = 0n, 0n, 0n, 0n, 0n, 0n, 0n
        let mutable initialized = false
        let mutable info = Unchecked.defaultof<ProcessInfo>
        let mutable ownedInput: Stream = null
        let mutable ownedOutput: Stream = null
        let mutable ownedChild: Process = null
        try
            try
                check (CreatePipe(&inRead, &inWrite, 0n, 0u))
                check (CreatePipe(&outRead, &outWrite, 0n, 0u))
                hresult (CreatePseudoConsole(size, inRead, outWrite, 0u, &console))
                let mutable attributeSize = 0un
                InitializeProcThreadAttributeList(0n, 1, 0, &attributeSize) |> ignore
                attributes <- Marshal.AllocHGlobal(nativeint attributeSize)
                check (InitializeProcThreadAttributeList(attributes, 1, 0, &attributeSize))
                initialized <- true
                check (UpdateProcThreadAttribute(attributes, 0u, 0x00020016un, console, unativeint IntPtr.Size, 0n, 0n))
                let variables = SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                for entry in Environment.GetEnvironmentVariables() do
                    let pair = unbox<System.Collections.DictionaryEntry> entry
                    variables[string pair.Key] <- string pair.Value
                for key, value in options.Environment do
                    match value with Some text -> variables[key] <- text | None -> variables.Remove key |> ignore
                let block = (variables |> Seq.map (fun pair -> pair.Key + "=" + pair.Value) |> String.concat "\000") + "\000\000"
                environment <- Marshal.StringToHGlobalUni block
                let command = StringBuilder(String.concat " " (("\"" + options.FileName + "\"") :: List.map quote options.Arguments))
                let mutable startup = Unchecked.defaultof<StartupEx>
                let mutable basic = Unchecked.defaultof<Startup>
                basic.Size <- uint32 (Marshal.SizeOf<StartupEx>())
                startup.Startup <- basic
                startup.Attributes <- attributes
                check (CreateProcessW(null, command, 0n, 0n, false, 0x00080400u, environment, Option.toObj options.WorkingDirectory, &startup, &info))
                let autoExit = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)
                if autoExit then hresult (ReleasePseudoConsole console)
                let child = Process.GetProcessById(int info.Id)
                ownedChild <- child
                child.SafeHandle |> ignore
                let inputHandle = new SafeFileHandle(inWrite, true)
                inWrite <- 0n
                let input =
                    try new FileStream(inputHandle, FileAccess.Write, 4096, false)
                    with _ -> inputHandle.Dispose(); reraise ()
                ownedInput <- input
                let outputHandle = new SafeFileHandle(outRead, true)
                outRead <- 0n
                let output =
                    try new FileStream(outputHandle, FileAccess.Read, 4096, false)
                    with _ -> outputHandle.Dispose(); reraise ()
                ownedOutput <- output
                let gate = obj ()
                let mutable ownedConsole = console
                console <- 0n
                let resize columns rows =
                    let size = dimensions columns rows
                    lock gate (fun () ->
                        if ownedConsole = 0n then raise (ObjectDisposedException "SubprocessSession")
                        hresult (ResizePseudoConsole(ownedConsole, size)))
                let closeConsole () =
                    let handle = lock gate (fun () -> let handle = ownedConsole in ownedConsole <- 0n; handle)
                    if handle <> 0n then ClosePseudoConsole handle
                ownedInput <- null
                ownedOutput <- null
                ownedChild <- null
                child, (input :> Stream), [| TerminalOutput, (output :> Stream) |], resize, closeConsole, autoExit
            with _ ->
                if info.Process <> 0n then TerminateProcess(info.Process, 1u) |> ignore
                // No reader exists on a failed startup: close this end before closing ConPTY.
                if not (isNull ownedOutput) then ownedOutput.Dispose()
                if outRead <> 0n then CloseHandle outRead |> ignore; outRead <- 0n
                if console <> 0n then ClosePseudoConsole console; console <- 0n
                if not (isNull ownedInput) then ownedInput.Dispose()
                if not (isNull ownedChild) then ownedChild.Dispose()
                reraise ()
        finally
            if initialized then DeleteProcThreadAttributeList attributes
            if attributes <> 0n then Marshal.FreeHGlobal attributes
            if environment <> 0n then Marshal.FreeHGlobal environment
            for handle in [| inRead; inWrite; outRead; outWrite; info.Thread; info.Process |] do
                if handle <> 0n then CloseHandle handle |> ignore

module Subprocess =
    /// Select a backend supported by this host's platform and ABI. Starting a process
    /// still checks the executable, process ownership and available OS resources.
    let tryPseudoTerminal columns rows =
        if OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) then
            SubprocessNative.dimensions columns rows |> ignore
            Some(WindowsPseudoConsole(columns, rows))
        elif PosixPty.supportsSize columns rows then Some(PosixPseudoTerminal(columns, rows))
        else None

    type private Backend =
        { Outputs: (SubprocessOutputKind * (byte[] -> CancellationToken -> Task<int>) * (unit -> unit)) array
          Write: byte[] -> CancellationToken -> Task
          Resize: int -> int -> unit
          Wait: unit -> Task
          Code: unit -> int
          Terminate: unit -> unit
          CloseConsole: unit -> unit
          Dispose: unit -> unit
          CancelReads: bool }

    let private streams cancelReads (child: Process, input: Stream, outputs: (SubprocessOutputKind * Stream) array, resize, closeConsole, autoExit) =
        { Outputs = outputs |> Array.map (fun (kind, stream) -> kind, (fun bytes token -> stream.ReadAsync(bytes.AsMemory(), token).AsTask()), stream.Dispose)
          Write = fun bytes token -> task {
              do! input.WriteAsync(bytes.AsMemory(), token).AsTask()
              do! input.FlushAsync token
          }
          Resize = resize
          Wait = fun () -> child.WaitForExitAsync()
          Code = fun () -> child.ExitCode
          Terminate = fun () ->
              try if not child.HasExited then child.Kill(true)
              with :? InvalidOperationException -> ()
              if autoExit then closeConsole ()
          CloseConsole = if autoExit then ignore else closeConsole
          Dispose = fun () ->
              if autoExit then closeConsole ()
              input.Dispose()
              for _, stream in outputs do stream.Dispose()
              child.Dispose()
          CancelReads = cancelReads }

    let command fileName arguments =
        { FileName = fileName
          Arguments = arguments
          WorkingDirectory = None
          Environment = []
          Mode = Redirected
          OutputCapacity = 262144 }

    let private redirected options =
        let start = ProcessStartInfo(options.FileName)
        start.UseShellExecute <- false
        start.CreateNoWindow <- true
        start.RedirectStandardInput <- true
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        for argument in options.Arguments do start.ArgumentList.Add argument
        options.WorkingDirectory |> Option.iter (fun directory -> start.WorkingDirectory <- directory)
        for key, value in options.Environment do
            match value with Some text -> start.Environment[key] <- text | None -> start.Environment.Remove key |> ignore
        let child = new Process(StartInfo = start)
        try
            if not (child.Start()) then invalidOp "The subprocess did not start."
            child, child.StandardInput.BaseStream,
            [| StandardOutput, child.StandardOutput.BaseStream; StandardError, child.StandardError.BaseStream |],
            (fun _ _ -> invalidOp "Resize requires a pseudoterminal mode."), ignore, false
        with _ -> child.Dispose(); reraise ()

    /// Output uses bounded backpressure. Drain while awaiting Exit; Close/cancellation discard
    /// incoming output and unblock readers, so shutdown never requires a caller to keep draining.
    /// POSIX PTYs own the initial process group and reap their direct child. Descendants that
    /// create another session/group are not a containment guarantee. Native child reaping must
    /// remain exclusive: do not use SIGCHLD ignore/auto-reap or an external waitpid(-1) reaper.
    let start options (cancellation: CancellationToken) =
        let validString name (value: string) =
            if isNull value || value.Contains '\000' then invalidArg name "Values must be non-null and contain no NUL characters."
        validString "FileName" options.FileName
        if String.IsNullOrWhiteSpace options.FileName then invalidArg "FileName" "An executable path is required."
        for argument in options.Arguments do validString "Arguments" argument
        options.WorkingDirectory |> Option.iter (validString "WorkingDirectory")
        for key, value in options.Environment do
            validString "Environment" key
            if key.Length = 0 || key.Contains '=' then invalidArg "Environment" "Environment names must be nonempty and contain no equals sign."
            value |> Option.iter (validString "Environment")
        if options.OutputCapacity < 1 then invalidArg "OutputCapacity" "Output capacity must be positive."
        cancellation.ThrowIfCancellationRequested()
        let backend =
            match options.Mode with
            | Redirected -> redirected options |> streams true
            | WindowsPseudoConsole(columns, rows) ->
                if not (OperatingSystem.IsWindows()) then raise (PlatformNotSupportedException "Pseudoconsole mode requires Windows ConPTY. Redirected mode is portable.")
                if options.FileName.Contains '"' then invalidArg "FileName" "Windows executable paths cannot contain quote characters."
                SubprocessNative.start options columns rows |> streams false
            | PosixPseudoTerminal(columns, rows) ->
                let native = PosixPty.start options.FileName options.Arguments options.WorkingDirectory options.Environment columns rows
                { Outputs = [| TerminalOutput, native.Read, ignore |]
                  Write = native.Write
                  Resize = native.Resize
                  Wait = native.WaitForExit
                  Code = native.ExitCode
                  Terminate = native.Terminate
                  // The observed-exit leader is still unreaped, so its group ID remains owned.
                  // Stop inherited-group descendants before waiting for the slave's final EOF.
                  CloseConsole = native.Terminate
                  Dispose = native.Dispose
                  CancelReads = true }
        let gate = obj ()
        let queue = LinkedList<SubprocessChunk>()
        let mutable queued = 0
        let mutable dropped = 0L
        let mutable closing = false
        let mutable finished = false
        let mutable readable: TaskCompletionSource<bool> option = None
        let notifyReaders available =
            match readable with
            | Some wait ->
                readable <- None
                wait.TrySetResult available |> ignore
            | None -> ()
        let ioCancellation = new CancellationTokenSource()
        let ioToken = ioCancellation.Token
        let mutable space = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let terminationFailure = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let notifySpace () =
            let previous = space
            space <- TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            previous.TrySetResult() |> ignore
        let stopOutput () = lock gate (fun () -> closing <- true; notifySpace ())
        let enqueue kind (bytes: byte[]) = task {
            let mutable accepted = false
            while not accepted do
                let pending = lock gate (fun () ->
                    if closing then
                        dropped <- dropped + int64 bytes.Length
                        accepted <- true
                        Task.CompletedTask
                    elif bytes.Length <= options.OutputCapacity - queued then
                        queue.AddLast { Kind = kind; Bytes = bytes } |> ignore
                        queued <- queued + bytes.Length
                        notifyReaders true
                        accepted <- true
                        Task.CompletedTask
                    else space.Task)
                do! pending
        }
        let kill () =
            stopOutput ()
            // A child may already have exited while a descendant still holds redirected pipes.
            if backend.CancelReads then
                try ioCancellation.Cancel()
                with :? ObjectDisposedException -> ()
            try backend.Terminate()
            with error -> terminationFailure.TrySetException error |> ignore
        let pumps =
            backend.Outputs |> Array.map (fun (kind, read, closeReader) -> Task.Run<unit>(fun () -> task {
                try
                    let buffer = Array.zeroCreate<byte> (min 8192 options.OutputCapacity)
                    let mutable reading = true
                    while reading do
                        let token = if backend.CancelReads then ioToken else CancellationToken.None
                        let! count = read buffer token
                        if count = 0 then reading <- false
                        else do! enqueue kind buffer[..count - 1]
                with
                | :? OperationCanceledException when ioToken.IsCancellationRequested -> ()
                | error ->
                    // A failed reader cannot service ConPTY shutdown; close its pipe before teardown.
                    closeReader ()
                    kill ()
                    return raise error
            }))
        for pump in pumps do
            pump.ContinueWith((fun (finished: Task<unit>) -> if finished.IsFaulted then finished.Exception |> ignore), TaskScheduler.Default) |> ignore
        let writeGate = new SemaphoreSlim(1, 1)
        let completion = Task.Run<int>(fun () -> task {
            let mutable code = 0
            let mutable failure = None
            try
                let waiting = backend.Wait()
                waiting.ContinueWith((fun (finished: Task) -> if finished.IsFaulted then finished.Exception |> ignore), TaskScheduler.Default) |> ignore
                let! finished = Task.WhenAny(waiting, terminationFailure.Task)
                do! finished
                // Close may synchronously emit output: pumps run independently until EOF.
                backend.CloseConsole()
                let! _ = Task.WhenAll pumps
                code <- backend.Code()
            with error -> failure <- Some error
            let cleanup action =
                try action ()
                with error -> if failure.IsNone then failure <- Some error
            cleanup stopOutput
            cleanup ioCancellation.Cancel
            cleanup backend.CloseConsole
            cleanup backend.Dispose
            cleanup ioCancellation.Dispose
            match failure with
            | Some error -> return raise error
            | None -> return code
        })
        let registration = cancellation.Register(Action kill)
        completion.ContinueWith((fun (_: Task<int>) ->
            lock gate (fun () ->
                finished <- true
                notifyReaders (queued > 0))
            registration.Dispose()), TaskScheduler.Default) |> ignore
        { Drain = fun maximum ->
              if maximum < 1 then invalidArg "maximum" "Drain byte limit must be positive."
              lock gate (fun () ->
                  let result = ResizeArray<SubprocessChunk>()
                  let mutable budget = maximum
                  while budget > 0 && queue.Count > 0 do
                      let chunk = queue.First.Value
                      let count = min budget chunk.Bytes.Length
                      if count = chunk.Bytes.Length then
                          queue.RemoveFirst()
                          result.Add chunk
                      else
                          result.Add { chunk with Bytes = chunk.Bytes[..count - 1] }
                          queue.First.Value <- { chunk with Bytes = chunk.Bytes[count..] }
                      budget <- budget - count
                      queued <- queued - count
                  if budget <> maximum then notifySpace ()
                  result.ToArray())
          WaitToRead = fun token ->
              if token.IsCancellationRequested then Task.FromCanceled<bool> token
              else
                  lock gate (fun () ->
                      if queued > 0 then Task.FromResult true
                      elif finished then Task.FromResult false
                      else
                          let wait =
                              match readable with
                              | Some pending -> pending
                              | None ->
                                  let pending = TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                                  readable <- Some pending
                                  pending
                          wait.Task.WaitAsync token)
          DroppedBytes = fun () -> lock gate (fun () -> dropped)
          Write = fun bytes token -> task {
              if isNull bytes then nullArg "bytes"
              use linked = CancellationTokenSource.CreateLinkedTokenSource(token, ioToken)
              do! writeGate.WaitAsync linked.Token
              try
                  if lock gate (fun () -> closing) then raise (ObjectDisposedException "SubprocessSession")
                  do! backend.Write bytes linked.Token
              finally writeGate.Release() |> ignore
          }
          Resize = backend.Resize
          Exit = completion
          Close = fun () -> kill (); completion :> Task }
