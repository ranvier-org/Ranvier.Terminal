# Event-driven native host

The host uses Ranvier's existing `SynchronizationContextDispatcher` to notify a wait;
the graph inbox, effects, bindings and owners remain Ranvier's. Only `Graph.Pump` on
the host thread drains posted work. There is no independent reactive queue.
The notification-only context intentionally ignores the provided drain callback:
the inspected Ranvier `Core.fs` `PostDrain` posts on every enqueue and has no pending
callback flag to acknowledge; public `Pump` drains that same inbox. This coupling must
be reviewed if Ranvier's dispatcher protocol changes.

`Terminal.runEventDriven options cancellation build tick` is additive. Its tick returns
`Some positiveDelay` for the next animation deadline, or `None` to sleep indefinitely.
Tick runs initially and after input, graph work or deadlines, so input or a posted signal
can resume a suspended animation. `run` and `runReactive` preserve periodic live ticking
at `FrameInterval`; they now wake early for input and graph posts without ticking early.
Public option/session/input records remain unchanged.

Windows waits on its console input handle and a managed wake event. POSIX waits on stdin
and a nonblocking wake pipe; cancellation, graph posts and SIGWINCH write to the pipe.
When the input parser holds a bare Escape, POSIX schedules its parser timeout. **From
documentation (`Native.fs`):** the parser's existing ambiguity interval is `30 ms`;
this is not a periodic idle timer.
POSIX native execution has not been performed on this Windows host.

A review caught a race in the first implementation's inferred input readiness: a
graph/resize/deadline wake could return, then ESC could arrive before owner `Poll`,
leaving the next wait unaware of the consumed byte. That inference was removed.
The actual parser feed now arms the deadline, and an internal callback carries its
remaining delay into the native wait. Public input/session records are unchanged.
The deterministic `tools/HostParserProbe` compiles the production native source and
exercises this ordering without making OS calls. Run after the solution build:

```powershell
dotnet C:\wk\ranvier-release\Ranvier.Terminal\tools\HostParserProbe\bin\Release\net10.0\HostParserProbe.dll
```

This probe verifies parser/deadline composition; it does not verify POSIX syscalls.
**Measured here:** the probe passed after the correction, covering ESC consumed after
a non-input wake, early and overdue deadlines, empty reads, split arrow input, and
one-shot Escape delivery. Both host probe projects built with no warnings or errors.

`tools/HostProbe` is a separate native verification and comparison executable, launched
hidden by its `run.ps1`. Its baseline freezes the prior poll/tick/pump/reactive-present/
sleep policy using the same renderer and native input implementation. The benchmark
keeps dimensions fixed; baseline resize handling is outside this comparison.

Idle measurement counts application tick callbacks and process CPU during an otherwise
inactive interval after warmup. For the event-driven host these count owner-loop wakes
that invoke tick; cancellation is outside the timed idle interval. CPU time includes the
process and runtime, not the terminal emulator. Native input latency is injection to
the expected character read back from the Windows console buffer; it includes the
observer loop and is an output-completion proxy, not input-to-visible latency.

## Measured comparison

**Measured here:** `host-native-first.txt`, Release HostProbe built with tracing disabled,
hidden console, serialized against the other task benchmarks. All native correctness
checks passed, including owner-thread graph bursts, repeated keys, more input records
than the bounded poll batch, resize/Escape, deadline suspension, legacy live ticks,
build/tick exceptions, already-cancelled entry, invalid and saturated deadlines, and
input/output mode and code-page restoration. Sleeping cancellation through cleanup
took `0.682 ms` in this run.

**Measured here:** `host-native-edges-final.txt` passed the expanded hidden edge suite
after the parser deadline correction. It additionally verifies a second graph post
after returning to sleep, native Ctrl+C exit without cancellation, and conservative
`run` periodic ticks. The original comparison remains intact; the POSIX correction
does not change the Windows wait policy measured there.

**Measured here:** each idle interval lasted approximately `2 s`; three matched intervals
per host yielded:

| Host | Tick/wake counts | Process CPU milliseconds |
| --- | --- | --- |
| Old polling policy | 125, 129, 123 | 46.875, 0, 15.625 |
| Event-driven, no deadline | 0, 0, 0 | 0, 0, 0 |

CPU counters have coarse granularity; the zero results mean below recorded resolution,
not a claim of zero runtime activity or power. Wake elimination is the stronger result.

**Measured here:** injection-to-console-buffer-readback latency, `40` samples per host,
same native writer, view and observer. Statistics come from `tools/HostProbe/summarize.ps1`:

| Host | Median milliseconds | Nearest-rank p95 milliseconds | Maximum milliseconds |
| --- | ---: | ---: | ---: |
| Old polling policy | 15.806 | 17.761 | 31.120 |
| Event-driven | 0.5545 | 0.834 | 0.984 |

These are individual observed readback samples, not frame-batch averages. They are a
local console-buffer completion proxy, not terminal GPU or remote-display latency.

## POSIX ABI review and execution boundary

**From documentation:** [Linux fcntl definitions](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/asm-generic/fcntl.h)
and [Apple fcntl definitions](https://raw.githubusercontent.com/apple-oss-distributions/xnu/main/bsd/sys/fcntl.h)
define `F_GETFL=3`, `F_SETFL=4`; nonblocking is `2048` on the targeted Linux ABIs and `4`
on Darwin. [Linux poll documentation](https://man7.org/linux/man-pages/man2/poll.2.html)
and [Apple poll definitions](https://raw.githubusercontent.com/apple-oss-distributions/xnu/main/bsd/sys/poll.h)
give the `int, short, short` poll descriptor layout. Apple's count type is unsigned int;
[glibc's public poll header](https://raw.githubusercontent.com/bminor/glibc/master/io/sys/poll.h)
declares unsigned long. The native imports now distinguish Darwin's count from Linux's
native unsigned size.
This source review does not establish native POSIX correctness: EOF, interrupted waits,
SIGWINCH and split Escape need execution on those systems. No POSIX success is claimed.
