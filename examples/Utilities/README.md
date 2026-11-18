# Live terminal utilities

The left pane continuously streams a clock, carriage-return progress and Unicode text into a
bounded output buffer. The right pane hosts this executable as a child, launched with structured
arguments through `dotnet`; it does not insert a shell. Start the child, send hello, type into its
focused pane, stop it or clear both panes using the buttons. Type `exit` to finish normally.

Windows uses an actual ConPTY by default; Linux and supported macOS hosts select a POSIX controlling
terminal. **From documentation:** `src/Ranvier.Terminal/PosixPty.fs` accepts Linux glibc >= 2.29 on
x64/arm64 and macOS x64. The macOS backend is source-reviewed pending native CI. macOS ARM64 rejects
explicit PTY creation because its variadic-call ABI bridge is unverified, so this sample defaults to
redirected pipes there. `--redirected` selects portable redirected pipes on any platform, including
Linux environments that do not meet the PTY requirements. Redirected stdout and stderr have
independent incremental UTF8 decoders and are displayed as literal text; they do not emulate a PTY.
These platform selections are not a claim that the final native verification gate has passed.

POSIX cleanup terminates the child's initial process group and reaps the direct child; descendants
that create another group or session are outside that boundary. Child reaping must stay exclusive
to the transport: do not ignore `SIGCHLD`, enable automatic reaping or install an external
`waitpid(-1)` reaper. **From documentation:** the POSIX backend rejects hosts running as PID 1;
use an init process for containers, for example `docker run --init`.

Use Tab to reach controls. Ctrl+Tab always leaves process focus. The process receives ordinary
Tab and Ctrl+C when focused; Ctrl+C elsewhere exits the host. Shift+PageUp/PageDown/Home/End browses
process history; unmodified navigation browses the read-only stream. End returns the read-only
stream to its tail; Shift+End does the same in the process pane.

Run from a terminal you opened yourself:

```powershell
Set-Location C:\wk\ranvier-release\Ranvier.Terminal
dotnet watch --project C:\wk\ranvier-release\Ranvier.Terminal\examples\Utilities\Utilities.fsproj run
```

The page registry in `Kit.fs` is reread by an owned dynamic branch when the metadata update handler
changes its revision. Page method edits rebuild the view while preserving buffers and the process.
Restart the host after type changes, entry-point changes or new functions in earlier files.
All signal, output-buffer and node mutations occur on the graph owner thread. Tick drains bounded
transport queues, follows the actual pane dimensions and publishes snapshots only when needed.
The view anchors history; the host republishes the selected offset without anchoring it again.
Input writes run serially with a fixed byte budget for pending writes and type-ahead. Input is rejected
with a visible status only when that budget is full or the child has exited.
Owner cleanup initiates async process close, and the host awaits it after terminal teardown.
Stop cancels the owned session without requiring further UI draining. Individual input-write
cancellation can leave a delivered prefix; it does not undo input. Pane resize updates PTY dimensions
and the output buffer; redirected mode only resizes its buffer. Completed sessions and close races
are handled by the sample. See [the transport guide](../../UTILITIES.md) for the full contract.

`--verify` runs without a terminal and checks live rendering, a real redirected child, input, exit,
resize, history, hot-reload branch replacement and disposal. `--smoke` runs a native host, exchanges
hello with its child and cancels itself; launch it hidden through the verification script.
`scripts/utilities-smoke.ps1` supplies that hidden launch and accepts the same `-ArtifactsPath`
as `scripts/verify.ps1`.
Never launch a visible test window. No complete terminal-emulator compatibility is claimed.
