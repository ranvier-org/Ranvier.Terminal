# Streaming output and subprocess sessions

`OutputBuffer` stores a terminal-sized screen and bounded history. `OutputView` displays detached
snapshots through the existing retained layout and renderers. `Subprocess` supplies command or
pseudoterminal output; it never writes that output directly to the outer terminal.

## Stream text into an output frame

Create the buffer and signals on the owning Ranvier graph thread:

```fsharp
let buffer = OutputBuffer.create width height historyLines
let offset = createSignal 0
let snapshot = createSignal (OutputBuffer.snapshot 0 buffer)
let node = OutputView.create [ Css.border Rounded ] snapshot offset

let append text =
    OutputBuffer.feedText text buffer
    snapshot.Value <- OutputBuffer.snapshot offset.Peek buffer
```

`feedText` is for literal streaming text and carriage-return progress updates. `feedUtf8` accepts
incremental UTF8/VT bytes, retaining decoder and parser state across chunks. Call `finish` when
the stream ends. `clear` clears screen, history and parser state. `resize` preserves useful content
without reflowing historical text. Snapshots own detached frame arrays; treat published snapshots
as read-only. [The output-buffer contract](docs/OUTPUT-BUFFER.md) lists supported commands and bounds.

Zero history offset follows the tail. The read-only widget uses wheel, PageUp/PageDown and Home/End
to browse. Republish a snapshot whenever the buffer or offset changes. The widget adjusts a nonzero
offset as new history arrives; the producer must not apply the same adjustment again. History is
bounded, so the oldest anchored line eventually expires. Styling decorates the outer node; snapshot
colors, including terminal-default colors, remain independent of the surrounding theme.

## Start a subprocess

```fsharp
let options = Subprocess.command executable arguments
let session = Subprocess.start options cancellation
```

Arguments are a structured string list. No shell is inserted implicitly. `WorkingDirectory` and
`Environment` customize the child; an environment value of `None` removes that variable.
`Redirected` mode works with ordinary command streams. On Windows, use
`WindowsPseudoConsole (width, height)` for a real interactive terminal with input and resize.
Use `PosixPseudoTerminal (width, height)` for a native controlling terminal on supported POSIX hosts.
For an application that can use either transport, select
`Subprocess.tryPseudoTerminal width height |> Option.defaultValue Redirected`.
This probes the local platform and ABI before starting a child.
**From documentation:** the platform guards in `src/Ranvier.Terminal/PosixPty.fs` accept Linux
with glibc >= 2.29 on x64/arm64 and macOS x64. The macOS backend is source-reviewed and awaits native
CI. macOS ARM64 explicitly rejects PTY startup because its native variadic-call ABI bridge is
unverified; musl Linux and other architectures are also unsupported. These are implementation
boundaries, not a claim that the final native verification gate has passed. Redirected mode remains
available without PTY semantics.

The POSIX child starts in its own session and initial process group, with the slave PTY as its
controlling terminal. Cleanup terminates that initial group, including descendants that remain
in it, and reaps the direct child. Normal leader exit also terminates remaining initial-group
descendants before waiting for terminal EOF. Descendants that create another group or session
are outside this ownership boundary; this is not a process-containment facility.

Native child reaping must remain exclusive to the transport. Do not install an external
`waitpid(-1)` reaper, ignore `SIGCHLD`, or enable automatic child reaping. The backend rejects
incompatible signal dispositions. **From documentation:** `PosixPty.fs` also rejects a host running
as PID 1; use an init process, for example `docker run --init`, for container execution.

Use `SubprocessOutput.bind maxBytes readSession receive ended` during owned setup for an
event-driven app. Read your current session signal in `readSession` and call the returned
pump once per host update. A ready session wakes the graph. Each pump delivers at most
one batch, allowing input and rendering between batches. After the final bytes, `ended`
receives the completed exit task once. Replacing the session or disposing the owner cancels
its reader and suppresses stale callbacks. The caller owns subprocess shutdown.

For manual consumption, await `session.WaitToRead token` and schedule a bounded
`session.Drain maxBytes` on the graph thread. A successful wait leaves the bytes queued.
Re-arm after draining. A false result means the session has finished and its queue is empty.
Cancelling a wait leaves the child running. Observe `session.Exit` for its exit code or failure.
Use `OutputView.tryViewportSize` after layout when sizing the backing buffer and PTY.
It accounts for padding, borders and scrollback space.

Chunks identify standard
output, standard error or the combined pseudoterminal stream. Do not interleave independent stdout
and stderr decoder state: keep separate decoders/buffers, or decode each stream independently before
appending literal text. Feed the combined pseudoterminal stream through `OutputBuffer.feedUtf8`.
Send `OutputBuffer.drainResponses buffer` back to the child when the parser answers a terminal query.

Output capacity is a byte bound. Normal producers apply backpressure when it is full rather than
discard partial UTF8/VT sequences. Consequently, normal `Exit` completion requires draining output.
`Close()` is asynchronous and idempotent; it unblocks pumps and tears down the child session without
requiring continued caller draining. `DroppedBytes()` records data discarded during shutdown.
Await close in the host's final cleanup; an owner cleanup can initiate it without blocking the graph.

If changed child credentials prevent POSIX group termination, close faults after releasing the
transport descriptors. An exact-child waiter retains reaping responsibility until that child exits;
the API cannot terminate a process it no longer has permission to signal.

`Write bytes cancellation` serializes asynchronous input; retain each byte array unchanged until
its task completes, observe failures and bound any application queue of pending writes. Cancelling
a write may leave an already-written prefix in the child; it does not undo input or terminate the
session. Cancelling the token passed to `start` closes the session instead. POSIX reads and writes
use nonblocking descriptors with cancellation wakeups; shutdown can interrupt blocked I/O.
`Resize width height` updates a pseudoterminal and rejects invalid dimensions; redirected mode
rejects resize. Resize the output buffer to the same content dimensions. A concurrent close can
make resize fail, so skip completed sessions and handle disposal races. `Exit` produces the process
exit code after output processing ends; session cancellation terminates the child rather than
cancelling the `Exit` task. Drain any remaining queued chunks after completion.

## Interactive retained view

Use `OutputView.interactive declarations snapshot offset onInput`. Encode forwarded events with
`TerminalKeys.encode snapshot.Peek.BracketedPaste event`, then write the resulting bytes to the
session. Key releases and unsupported events produce no bytes. Encoding covers Unicode, common
navigation/function keys, Shift/Alt/Control modifiers and bracketed-paste boundaries; it is not a
complete enhanced keyboard or mouse protocol implementation.

Shift+PageUp/PageDown/Home/End and the wheel browse history in an interactive frame. Plain navigation
keys and Tab reach the child. Ctrl+Tab moves focus out to the enclosing application. Clicking a text
run focuses its enclosing focusable output frame. The terminal host permits a handler to consume
Ctrl+C; an unhandled Ctrl+C keeps its existing exit behavior. Interactive snapshots display their
visible cursor independently of host focus; history browsing hides it. Read-only snapshots have no
cursor overlay.

`Node.setInheritColors false node` isolates a subtree's default colors. It is used by output frames
and is also available when another embedded surface must preserve terminal-default paint.

See `examples/Utilities` for a live sample with a hot-reload page registry and verification modes.
Native results are recorded separately in [VERIFICATION.md](VERIFICATION.md); source availability
does not establish native execution on every platform. No remote-display, input-to-visible latency
or universal terminal-emulation claim is made.
