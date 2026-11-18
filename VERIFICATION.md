# Verification record

All results below are **measured here**, on the Windows development host, using the Release build
with Ranvier tracing disabled. The commands are `scripts/verify.ps1` and `scripts/native-smoke.ps1`.
No figures are terminal-emulator GPU timings.

| Check | Observed result |
| --- | --- |
| Solution build | Passed with no warnings or errors |
| Executable regression suite | 14 checks passed; no failures |
| Randomized framebuffer roundtrip | 500 mutation rounds matched an independent terminal-screen model |
| Prepared UI render allocation gate | 10,000 changing frames; zero managed bytes |
| Native Windows output allocation gate | 1,000 changing frames through actual console output; zero managed bytes |
| Native Windows input | Injected repeated key and mouse records decoded correctly |
| OS mode restoration | Original input/output mode words and output code page restored exactly |
| Native Unicode output | Encoded text read back correctly from the Windows screen buffer |
| Hidden live dashboard | Started, rendered, cancelled, cleaned up and exited successfully |
| Headless live dashboard | 120 changing frames completed |

The UI allocation benchmark measures layout, framebuffer painting, comparison, UTF-8 encoding,
null output invocation and snapshot commit together. The native benchmark measures comparison,
encoding, actual console write/flush and snapshot commit. These are different benchmarks and their
timings must not be compared. Neither measurement includes user callbacks, graph scheduling, text
preparation, input dispatch, resize, buffer growth or GPU work.

An earlier short warmup produced a transient allocation result; an immediate rerun passed. The gate
now performs a timed warmup before collecting allocations so tiered JIT activity can settle. It does
not discard measured nonzero runs or retry until passing. Nonzero measured allocations fail the gate.

Linux and macOS native execution are **not verified here**. Attempts to access WSL and the Docker
engine were denied by the local environment. The CI matrix is prepared but has not been run remotely.

## Feature showcase

**Measured here:** `Showcase --verify` passed after rendering every registered page at the
configured viewport sizes (144 frames). It checked button actions, text editing, branch cleanup,
collection changes, output-mode changes, invalidation, scrolling and function-key navigation.
The same checks passed under `dotnet watch` with hot reload enabled. A hidden native session
visited all pages and exited successfully through `scripts/showcase-smoke.ps1`.

## Word-aware wrapping

**Measured here:** `scripts/verify.ps1` passed the word-wrap regression checks, showcase verification and build. Checks cover default word boundaries, the character-wrap style opt-out, oversized words, wide and combining glyphs, explicit newlines, intrinsic height and clipping without reflow. The updated allocation benchmark includes changing word-wrapped text: 10,000 changing frames allocated zero managed bytes. The executable regression suite now contains 15 passing checks.

## Disappearing-window investigation

**Measured here:** the expanded showcase verification passed held-arrow navigation through each page's focus order, text-caret bounds, selection bounds and ignored key releases. Native Windows tests injected every arrow virtual key with repeated key-down and key-up records and verified decoded values and event counts. The render allocation gates continued to pass.

A hidden heartbeat child launched through the command executor stopped when its parent command session ended, without running its cleanup. The disappearing showcase's report contained first-frame success and no managed exception. This supports launcher-session termination as the likely cause; it does not prove that every possible keyboard sequence is safe. `Showcase.cmd` provides a user-owned hot-reload launch from Explorer.

The source dependency revision inspected and built here was
`25a4452669c0853f75c59724f7f7264959c3fa1a` in the sibling Ranvier checkout
(**measured here:** `git rev-parse HEAD`).

## Resumed optimization verification

**Measured here:** `rfs check --fail-on-error` reported no errors or warnings after checking the
solution including PerfBench. `scripts/verify.ps1` then built Release without warnings or errors;
the executable regression suite reported 25 checks and no failures. `dotnet test` on the solution
also exited successfully; this repository's substantive assertions run in the executable suite,
not an external test adapter.

**Measured here:** the conservative prepared-render gate, mixed full/partial reactive gate, and
horizontal-scroll painting gate each ran 10,000 changing frames with zero managed bytes.
`scripts/native-smoke.ps1` passed Windows mode restoration, injected input and its actual output
gate of 1,000 changing frames with zero managed bytes. `scripts/showcase-smoke.ps1` visited all
pages in a hidden session and exited successfully. The headless dashboard and showcase verifier
also passed. No visible demo was opened.

Regression fixtures now cover independent-tree full/incremental comparison, resize and structural
changes, wide-fill boundaries, active style rules and inheritance, retained and virtual list rows,
horizontal Unicode clipping, renderer invalidation, and retry after capacity or flush failure
without another reactive mutation. Conservative rendering into another framebuffer now invalidates
the geometry cache; a dedicated regression failed before that fix.

Independent review found a soft-wrap/newline interaction that inserted an extra blank line.
**Measured here:** the compiled probe failed before the fix and the Release suite passed after it,
including fixtures for adjacent, consecutive and trailing explicit newlines. The source now consumes
the newline once when it immediately follows discarded soft-break whitespace.

Intermediate allocation failures and rejected performance experiments are recorded in
[PERFORMANCE.md](PERFORMANCE.md), with raw CSV results in `performance/`. They have not been hidden
or treated as successful runs. Linux/macOS native execution and remote presentation remain unverified.

## Completed optimization backlog

**Measured here:** final `rfs check --fail-on-error` checked the loaded solution with no errors or
warnings. A stale cached diagnostic for the newly added adapter fixture disappeared after an
explicit signature edit triggered rechecking; the independent compiler also accepted the fixture.

The pre-existing Dashboard process held its loaded output assemblies, so the default build could
not copy replacements. It was left running. `scripts/verify.ps1 -ArtifactsPath` built into
`artifacts/optimization-verification` and passed without warnings or errors. The native scripts
accept the same parameter, making this verification reproducible without closing a running app.

**Measured here:** the executable suite passed 32 checks with no failures. Prepared retained
painting, owned packed text painting, scoped scrolling, ordinary prepared rendering, mixed reactive
rendering and horizontal clipping each passed their 10,000-changing-frame zero-managed-allocation
gate. Tests include persistent virtualized renderers, independent framebuffer comparisons, distant
damage, root hide/show, geometry reuse, cross-renderer handles, registration exhaustion/retry,
long graphemes, color modes and partial scroll write recovery.

**Measured here:** unfiltered `dotnet test` with the isolated artifact path exited successfully;
the substantive checks remain in the executable suite. Dashboard and showcase headless checks
completed 120 and 144 frames respectively. Both hidden native smoke scripts passed, including
1,000 actual console-output frames with zero managed bytes and exact native mode restoration.
The production-linked HostParserProbe passed actual parser-deadline cases after non-input wakes.
The expanded hidden HostProbe passed graph posts after returning to sleep, native Ctrl+C exit,
periodic compatibility hosts and cleanup; see `performance/host-native-edges-final.txt`.

Independent review findings were fixed before the final gate. The prepared adapter remains opt-in:
its dense reusable-text workload improves, while sparse newly prepared text loses to ordinary
reactive rendering. All rejected experiments and their measurements remain documented in
[the completed ledger](OPTIMIZATION-LEDGER.md) and linked performance records.

## Streaming output and subprocess utilities

**Measured here:** final fresh-workspace rfs checks for the tests and Utilities sample reported no
errors or warnings. Fresh nested workspaces were used because the existing root daemon retained
an obsolete project file list after reload; no daemon was killed. The independent final Release
solution build into `artifacts/utility-verification` passed without warnings or errors.

**Measured here:** the final executable suite passed 35 checks with no failures, including the
existing prepared-render allocation gates. New fixtures cover every selected UTF8/UTF16 split,
an independent literal-stream model, randomized wide-cell invariants, bounded hostile controls,
history eviction, alternate screens, styles, cursor overlay, snapshot isolation, focus routing,
input encoding and owned disposal. Unfiltered solution `dotnet test` also exited successfully.
The headless Utilities sample verified live output, real redirected child input and exit, queued
type-ahead, viewport resize, immediate anchored history, registry replacement and cleanup.

**Measured here:** `scripts/utilities-smoke.ps1` passed with the isolated Release binaries. The
hidden native host exchanged input and output with an actual Windows ConPTY child, then cleaned
up. `performance/subprocess-final.txt` records the dedicated redirected/ConPTY probe, including
structured quoting, Unicode, environment/cwd, actual terminal detection, resize, sustained bounded
output, concurrent blocked I/O close, cancellation, failed startup, split UTF8 and parent-exited
descendant-held pipe cleanup. Source review found and fixed double-close risk during stream
construction and the redirected descendant-pipe shutdown hang before these final probes.

**Measured here:** `performance/host-utility-final.txt` verifies both handled and unhandled native
Ctrl+C, sleeping graph wakeups, animation deadlines, legacy ticks and cleanup. Existing native
and showcase smoke scripts passed; the actual Windows output gate still allocated zero managed
bytes over 1,000 changing frames. These are correctness checks, not new latency comparisons.

**Measured here:** `scripts/utilities-watch-smoke.ps1 -Name utilities-watch-final` ran the final
headless sample under the Debug watch host with hot reload enabled. The headless assertions invoke
the registry revision callback and verify branch replacement; they do not claim an externally
edited source file was patched live. The initial launcher selected an incompatible SDK outside
the repository (`utilities-watch.txt`). Launching from the repository selected its pinned SDK;
the intermediate Release run (`utilities-watch-repo-sdk.*`) used restart fallback because of
optimization. The final script uses the repository directory and Debug configuration and rejects
that fallback. Earlier failure/fallback logs are preserved.

The VT command subset and storage bounds are documented in `docs/OUTPUT-BUFFER.md`.
At that earlier checkpoint, Windows ConPTY was verified here, while native POSIX subprocess
execution and PTY creation were still deferred. The continuation below supersedes that limit. Input/output
decoding, snapshots, process pumps and callbacks are outside the prepared-render allocation claim.

## POSIX PTY continuation

Implemented `PosixPseudoTerminal` with a controlling terminal, native session/process group,
structured arguments, child-only working directory/environment, cancellable nonblocking I/O,
kernel resize and exact child reaping. Linux uses atomic close-on-exec wake pipes. The leader
remains unreaped until the final owned-group signal, preventing PID reuse during normal cleanup.

**Measured here:** `scripts/verify-posix.sh subprocess-posix-hardened` ran in a Linux x64 glibc
Docker container with init. The isolated Release solution build and unfiltered solution test
command succeeded. The executable regression suite passed 35 checks with zero failures; existing
prepared-render allocation gates remained at zero managed bytes. See
[`performance/posix-linux-gate.txt`](performance/posix-linux-gate.txt).
These are functional/allocation checks, not a cross-platform timing comparison.

**Measured here:** [`subprocess-posix-hardened.txt`](performance/subprocess-posix-hardened.txt)
passed redirected and native PTY lifecycle checks: actual terminal/session/foreground identity,
arguments/cwd/environment, input, bounded output, SIGWINCH and kernel dimensions, Ctrl+C exit
status, resize bounds and close races, blocked-write cancellation, pre-cancelled/racing startup,
repeated live cancellation, descriptor cleanup, exact reaping and descendants retaining the slave.
The first run, preserved in [`subprocess-posix-first.txt`](performance/subprocess-posix-first.txt),
found a parent-exit deadlock. The completion path now terminates the owned group before joining
the output pumps; subsequent focused and full runs passed the same regression.

**Measured here:** isolated container probes verified rejection of PID-one hosts and ignored
SIGCHLD. The credential fixture deliberately denied group signalling: close faulted promptly,
released descriptors and retained an exact-child waiter; restoring fixture credentials and
terminating the child allowed that waiter to reap it. Evidence is in
[`posix-pid-one.txt`](performance/posix-pid-one.txt),
[`posix-denied-signal.txt`](performance/posix-denied-signal.txt), and the Linux gate log.

**Measured here:** `scripts/utilities-posix-smoke.sh` ran with an allocated outer container PTY;
the actual Linux host exchanged input/output with its PTY child and cleaned up successfully.
Its terminal capture is [`utilities-posix-hardened-smoke.txt`](performance/utilities-posix-hardened-smoke.txt).
The headless Utilities verification also passed.

**Measured here:** the final isolated Windows solution build, `scripts/verify.ps1`, unfiltered
solution tests, source-linked redirected/ConPTY probe, native terminal smoke, Utilities smoke
and Showcase smoke passed. The executable suite again passed 35 checks with zero failures;
the Windows console output allocation gate remained zero over 1,000 changing frames. Evidence:
[`posix-windows-gate.txt`](performance/posix-windows-gate.txt) and
[`subprocess-posix-windows-final.txt`](performance/subprocess-posix-windows-final.txt).
Scoped rfs checks reported no errors or warnings. An independent lifecycle/ABI review identified
the failed-signal cleanup and non-atomic Linux wake-pipe setup; both were corrected before these gates.

Linux ARM64 and macOS x64 are implemented but were not executed locally. macOS ARM64 explicitly
rejects the PTY mode until a verified bridge handles Darwin's variadic native ABI; Utilities uses
redirected streams there. macOS x64 still has a non-atomic pipe/close-on-exec setup window for
unrelated concurrent native spawns. The CI definition adds the lifecycle probe and an Intel macOS
runner alongside the existing matrix; no remote run was dispatched or claimed. The Intel label
is from [GitHub's runner documentation](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
Native child reaping must remain exclusive. Descendants that escape the initial process group,
or credentials that remove signalling permission, are outside the termination guarantee.

## Showcase output and terminal pages

The showcase now registers Live output and Subprocess pages, sharing the Utilities model through
a source link. Page ownership closes child sessions on navigation or hot reload; shutdown awaits
the outstanding cleanup tasks. Input dispatch lets a focused terminal consume Ctrl+C before the
showcase treats it as an application exit shortcut.

**Measured here:** the final scoped rfs check, Release solution build, executable regression and
allocation suite, and unfiltered solution test command passed. The suite retained 35 passing
checks and its zero-allocation prepared-render gates. Showcase verification rendered 180 frames
across the registered pages and viewport fixtures, then exercised history/clear/follow-live,
real redirected child input, focus escape, stop, page-leave cleanup and process restart after
remount. Evidence: [`showcase-utilities-headless.txt`](performance/showcase-utilities-headless.txt).
Framebuffer text captures were inspected in `artifacts/showcase-utility-captures`.

**Measured here:** the hidden Windows showcase smoke visited all pages and exchanged hello and
terminal-size reports with its ConPTY child. The existing native terminal/allocation smoke passed
too. Evidence: [`showcase-utilities-native.txt`](performance/showcase-utilities-native.txt) and
[`showcase-utilities-host.txt`](performance/showcase-utilities-host.txt).
The Linux build, headless verification and actual POSIX terminal showcase smoke passed in an
allocated container terminal: [`showcase-utilities-posix-foreground.txt`](performance/showcase-utilities-posix-foreground.txt).
The smoke launcher uses `timeout --foreground` so the terminal host retains foreground job control;
the earlier launch failure is preserved in `showcase-utilities-posix.txt`.

**Measured here:** `scripts/showcase-watch-smoke.ps1` passed with hot reload enabled under the
Debug watch host, recorded in [`showcase-utilities-watch.txt`](performance/showcase-utilities-watch.txt)
and its stderr companion. This verifies running under watch, not an externally edited source patch.
The initial reserved-identifier compile failure and redirected-parent ConPTY verification failure
remain in `showcase-utilities-verification.txt` and `showcase-utilities-final.txt`. Headless checks
now deliberately use redirected streams; the native smoke checks exercise actual PTYs.

## System monitor reference replica

The standalone `examples/SystemMonitor` reproduces the supplied tall and compact layouts using
the terminal framebuffer, retained OutputView and View.Dsl. Default animated reference data is
explicitly marked DEMO; live collection runs off the graph thread and reports unavailable sensors.
The process table supports filtering, sorting, reversal, selection and read-only details.

**Measured here:** the scoped rfs check and Release solution build passed. The existing executable
suite retained its 35 passing checks and zero managed allocations in the prepared-render gates.
Unfiltered solution tests exited successfully. Monitor-specific checks passed for retained/frame
parity, tall/compact/tiny layouts, animation, pause, filtering including Unicode deletion, empty
results, sorting, selection, registry remount and live host sampling with cancellation. Evidence:
`performance/system-monitor-final.txt`; the final app verification also exported its frame cells
to `artifacts/system-monitor-captures` for visual inspection. The PNGs are framebuffer previews,
not native-window screenshots; font choice and terminal transparency remain host settings.

**Measured here:** the final portable app binary passed verification and native demo/live smoke
checks on Windows and Linux. The Windows windows were hidden; Linux ran in an allocated container
terminal with an init process and foreground timeout. Evidence:
`performance/system-monitor-native-complete.txt` and `performance/system-monitor-posix-complete.txt`.
A separate Linux source build passed in `performance/system-monitor-posix-final.txt`.
Debug verification ran under the hot-reload watch host with hot reload enabled, as recorded in
`performance/system-monitor-watch.txt` and its stderr companion. No visible app was launched.

No library hot path was changed, and no new performance improvement is claimed for the monitor.
Sampling, chart preparation and detached snapshots allocate outside the renderer allocation claim.
macOS native execution was not tested here. Process CPU/rate collection needs an initial baseline;
permissions and platform API scope limit which live measurements can be collected.

## Adjustable monitor polling and process scrolling

The monitor defaults to a configurable polling interval, exposes keyboard and header controls,
and accepts `--interval-ms`. Input now wakes the event-driven host to redraw immediately. Collection
stays on a single background task; slow reads cannot overlap or queue a backlog. Process navigation
supports the wheel, scrollbar clicks and keyboard selection. A passive `OutputView.display` prevents
the embedded output-history handler from swallowing the monitor's wheel events.

**Measured here:** `MonitorProbe` reproduced the swallowed-wheel failure before that fix, then passed
real view-dispatch checks for viewport scrolling, pointer bounds, scrollbar endpoints, Home, shifted
plus, header controls and interval validation. A deliberately blocked sampler allowed input and
snapshot updates, prevented overlapping reads after interval changes, and withheld publication while
paused until resume.

**Measured here:** the hidden native probe timed injected navigation through model update, snapshot,
retained layout, diff, encoding and console output with actual background host telemetry. At the
recorded tall viewport of 120 by 61 cells, the two 500 ms cases had UI means of 4.388 and 4.246 ms,
p99 of 17.274 and 17.358 ms, and maxima of 18.829 and 18.692 ms. The 100 ms case had a UI mean of
5.140 ms, p99 of 18.283 ms and maximum of 19.141 ms. No UI operation exceeded 50 ms and no scheduled
frame gap exceeded 100 ms in these runs. Each case lasted 8 seconds; whole-process CPU time increased
from 1953.1/2093.8 ms at 500 ms polling to 6671.9 ms at 100 ms polling. Background reads averaged
roughly 110 ms, so the shortest configured interval was limited by collection time.

Evidence: `performance/monitor-poll-tall.txt`, `performance/monitor-poll-candidate.txt` and
`performance/monitor-poll-baseline.txt`. The baseline includes timing instrumentation. This is evidence
against polling-induced UI stalls in this workload, not a rendering speedup or a guarantee about
physical input latency, terminal compositor presentation or other machines. The nominal probe event
schedule was limited by host scheduler timing; the reports include actual frame counts and gaps.

**Measured here:** final Release solution build passed with no warnings or errors using
`scripts/verify.ps1 -ArtifactsPath artifacts/monitor-poll-verification`. Its executable regression
suite passed all 35 checks, all sample verifications passed, and the new monitor regressions passed.
Unfiltered `dotnet test` on that solution and artifact directory exited successfully. Hidden Windows
monitor demo/live smoke checks passed at both 500 ms and 100 ms, as did native input, mode-restoration
and dashboard smoke checks. This polling change was not re-executed on Linux or macOS.

Measured in the [initial GitHub verification run](https://github.com/ranvier-org/Ranvier.Terminal/actions/runs/44278590636), the Intel macOS changing-frame allocation gate recorded 12,336 managed bytes over 10,000 frames with tiered compilation enabled. CI runs the steady-state suite with fully optimized JIT compilation from startup. The allocation limit is zero bytes. Application and subprocess checks use the default runtime settings.

## Release verification

Measured in the [release verification run](https://github.com/ranvier-org/Ranvier.Terminal/actions/runs/44290675150), Intel macOS passed native controlling-terminal identity, resize signals, interrupts, concurrent writes and cancellation, process-group cleanup and exact reaping. A fresh NuGet consumer exercised the packaged Intel launcher via /dev/tty. The same matrix passed on Linux, Windows and Apple Silicon; Apple Silicon explicitly exercises redirected subprocesses and rejects native subprocess PTY mode.
