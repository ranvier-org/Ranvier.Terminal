# Subprocess transport verification

`Subprocess.command executable arguments` creates redirected options. `Subprocess.start`
accepts a cancellation token and returns a function record with `Write`, `Drain`, `Resize`,
`Exit`, `DroppedBytes`, and idempotent asynchronous `Close`. No shell is inserted. An
application that wants a shell must explicitly name it and supply its arguments.

Redirected mode works through managed process streams. Standard output and standard error
remain separately tagged; keep separate UTF8 decoder state when consuming them. ConPTY mode
is Windows-only and returns one tagged terminal byte stream. It is a real pseudoconsole:
child console APIs and resize operate on that console, not on ordinary redirected pipes.
`PosixPseudoTerminal` creates a native controlling terminal and returns a combined terminal stream.
**From documentation:** the guards in `src/Ranvier.Terminal/PosixPty.fs` accept Linux glibc >= 2.29
on x64/arm64 and macOS x64. macOS is source-reviewed pending native CI; macOS ARM64 explicitly
rejects startup because its variadic-call ABI bridge remains unverified. Musl Linux and other
architectures are unsupported. No third-party native runtime is required. Windows ConPTY
availability follows the OS API's supported releases. These implementation boundaries do not
constitute a final native verification result.

The queue holds at most `OutputCapacity` bytes. Reader pumps wait for space, preserving all
bytes during normal operation. Each pump also owns a bounded read buffer and one pending
chunk outside the queue. `Drain maximum` returns at most that many bytes, removing them from
the queue. Its returned arrays belong to the consumer and remain valid after later drains.
Keep draining while awaiting `Exit`: a full queue can deliberately backpressure child exit
or ConPTY teardown. Output order within each source is retained; cross-stream chronology is
not defined.

`Close` and session cancellation first unblock reader backpressure and discard/count newly arriving
bytes, then terminate the owned child or group. They require no consumer to keep draining. Previously
queued output remains available. `Exit` completes with the process exit code after readers
and native resources finish; cancellation terminates the child rather than cancelling that
task. Unexpected reader errors fault `Exit`. `Write` is serialized and asynchronous; retain
its byte array unchanged until the returned task completes. Avoid synchronously blocking the
graph owner on writes or close. Neither background pump mutates Ranvier state or UI nodes.
Redirected reads are independently cancellable even when the original child has already
exited and a descendant still holds its pipes. This releases the transport without claiming
ownership or termination of orphaned descendants; applications needing that stronger guarantee
need an explicit process-group/job policy outside redirected mode. Discard accounting covers bytes
received by pumps, not unread bytes abandoned by cancelling redirected reads.

POSIX mode creates its own session and initial process group. Close and cancellation terminate that
group and reap the direct child. Normal leader exit also terminates descendants remaining in that
group before waiting for terminal EOF. Descendants that move into another group or session are
outside the cleanup guarantee. The unreaped leader reserves its identity until the final group
signal; native reaping must remain exclusive. Do not use an external `waitpid(-1)` reaper, ignore
`SIGCHLD`, or enable automatic reaping. Incompatible signal dispositions are rejected.
**From documentation:** `PosixPty.fs` rejects PID 1 hosts; run container probes with an init process,
such as `docker run --init`, rather than making the probe the container's init.

Cancelling an individual `Write` interrupts that write, possibly after a prefix was delivered;
it does not terminate the session. Cancelling the token passed to `start` terminates the session.
POSIX I/O uses nonblocking descriptors and cancellation wakeups. `Resize` updates the PTY window
size; it rejects invalid dimensions and is unsupported in redirected mode. Skip completed sessions
and handle a concurrent close. `Exit` is completion of native cleanup and reader processing, not
proof that the consumer has removed every previously queued chunk; finish draining those chunks.

ConPTY uses the documented synchronous pipe ABI with independently scheduled input/output,
and closes its pseudoconsole while the output reader is still servicing it. On failed startup
or reader failure, its output pipe is closed before native teardown. Implementation references:
[Microsoft session creation](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session),
[ClosePseudoConsole deadlock guidance](https://learn.microsoft.com/en-us/windows/console/closepseudoconsole),
[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw),
and [Microsoft argument parsing](https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments?view=msvc-170).
Arguments follow the standard Windows runtime quoting convention; executables that implement
their own nonstandard command-line parser may interpret arguments differently.

Build the probe project, then run its `run.ps1` with a fresh `-Name`. The probe compiles the
production `PosixPty.fs` and `Subprocess.fs` directly so unrelated in-progress view/core edits cannot change
the transport test binary. The script launches a
hidden process, preserving the report in `performance/`. `-Dll` or `-ArtifactsPath` accepts an isolated build.
The executable self-launches child modes, so it does not depend on PowerShell or another shell
for the transport tests. No visible test windows should be opened.

**Measured here:** `performance/subprocess-drain.txt` passed redirected and real Windows
ConPTY argv/cwd/environment, input, terminal detection, resize, exit-code, sustained-output,
bounded-backpressure, concurrent input/output-close, cancellation, missing-executable and
repeated-close checks. A preceding fixture run (`subprocess-first.txt`) timed out while
draining tiny fragments with a timer delay; the consumer was deliberately too slow for its
deadline. The later run changes the test drain budget, not transport behavior. This is
functional evidence, not a performance measurement. `subprocess-hardened.txt` is a repeat of
the earlier binary accidentally launched after a dependency build failed; it is not evidence
of the later hardening. **Measured here:** the clean source-linked build and
`subprocess-reviewed.txt` passed after cleanup hardening, including a single-byte queue that
preserves split UTF8 and close after parent exit with descendant-held redirected pipes. The
fixture explicitly terminated its own lingering descendant after that assertion.

The POSIX probe additionally checks controlling-terminal/session identity, resize signals, signal
exit status, repeated cancellation, descriptor counts, exact reaping and descendant-held slaves.
`scripts/verify-posix.sh` builds isolated Linux artifacts, runs the regression/allocation suite,
transport probe, ignored-SIGCHLD guard and headless sample, then unfiltered solution tests.
Run the script under a container init (`docker run --init`) when using Docker.
`scripts/utilities-posix-smoke.sh` runs the actual host and PTY child in an allocated outer terminal.

The standalone `--verify-pid-one` probe entry point checks rejection when intentionally run as
the container entry point without init. `--verify-denied-signal` is an isolated root Linux container
fixture: it temporarily changes its own credentials to provoke denied termination, restores them,
cleans up the child and verifies the backend's deferred reaper. It is not part of ordinary CI.
