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

The source dependency revision inspected and built here was
`25a4452669c0853f75c59724f7f7264959c3fa1a` in the sibling Ranvier checkout
(**measured here:** `git rev-parse HEAD`).
