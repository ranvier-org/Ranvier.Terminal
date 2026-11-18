# Encoder and representation decisions

Initial experiments used isolated source copies. The accepted opt-in implementation is now
`Core/Prepared.fs` with the retained adapter in `View/PreparedLayout.fs`; the ordinary renderer
and its public mutable framebuffer remain available. All timings
below are **measured here** on the Windows development host, with the coordinator holding other
agents out of CPU/native measurement. They measure engine/output completion, never presentation.
Raw evidence is in `encoder-representation.csv`, `encoder-region-protocol.csv`, and `encoder-native.txt`.

## Accepted implementation and actual API results

`PreparedRenderer` stores native packed cells and owns fixed-capacity glyph/style tables. Callers
register sanitized glyphs, text and paint before painting. Owner-checked handles cannot cross
renderers. Unicode payloads are preencoded at registration and accessed directly by identity;
there is no dictionary lookup or transcoding in the render loop. This is distinct from the
rejected per-output dictionary cache below. UTF8 storage has an explicit byte budget, and
exhaustion fails during registration. IDs are never recycled while snapshots/handles can refer
to them. No process-global cache or new runtime dependency is used.

**Measured here:** the actual public API comparison in `prepared-owned-api-final.csv` includes
owner checks, framebuffer writes, comparison, encoding, null-output invocation and commit:

| Workload | Ordinary API median µs | Prepared API median µs |
|---|---:|---:|
| ASCII sparse dirty row | 0.572 | 0.211 |
| ASCII dense | 190.634 | 51.544 |
| Unicode sparse dirty row | 0.581 | 0.216 |
| Unicode dense | 251.563 | 68.872 |
| Wrapped text clear/paint/present | 76.609 | 13.041 |
| Warm text registration | 2.715 | 3.904 |
| Renderer creation plus text registration | 141.065 | 16.325 |

**Measured here:** all prepared rendering rows allocated zero managed bytes. Warm registration
allocated 10,828 bytes normally versus 11,876 with prepared handles. Creation plus registration
allocated 904,095.200 versus 424,613.440 bytes per iteration; this is measured managed allocation,
not process working set. Registration/allocation cost is outside the steady-state guarantee.

The initial `prepared-owned-api.csv` included a benchmark-only list literal inside the timed
wrapped-paint lambda. **Measured here:** both implementations allocated 160 bytes per iteration
from that harness. Replacing the literal iteration with an integer loop removed the harness
allocation; the original evidence remains. No failed result was discarded or retried unchanged.

### Existing retained-tree integration

`PreparedLayout` uses the existing `Layout.plan` geometry planner, existing Ranvier mounted tree,
style inheritance, clipping, wrapping, borders and painted carets. It does not introduce a second
reactive graph or geometry engine. Its node cache retains only the current and previous prepared
text per node. Refresh registers changed resources before drawing; failure leaves the cache
unready and cannot silently draw a partially updated style. Detached node mirrors are released
on the next structural refresh. Renderer registration capacity is not reclaimed automatically;
create a renderer with a suitable budget or replace it at an explicit application boundary.

**Measured here:** `prepared-retained.csv` compares mutation, refresh/layout, painting, output
and commit on independent retained trees:

| Retained workload | Ordinary reactive µs / allocated bytes | Prepared µs / allocated bytes |
|---|---:|---:|
| Dense updates using existing prepared text | 120.316 / 0 | 22.238 / 0 |
| Sparse newly formatted and prepared text | 4.165 / 1,521.104 | 16.567 / 4,147.440 |

Decision: accept the owned API and adapter as explicit opt-ins for dense prepared workloads.
Keep ordinary reactive rendering as the default, especially for sparse dynamically created text.
The adapter presently repaints its complete framebuffer; it does not claim the ordinary dirty-row
pipeline's sparse-update behavior. Its measured benefit is not extrapolated to arbitrary callbacks.

**Measured here:** regression coverage compares independent normal/prepared trees through style
changes, wrapped Unicode, host-specific widths, carets, clipping, structural replacement and resize.
Prepared text, scoped scrolling, and stable retained caret/refresh paths each passed their changing
frame allocation gates. Random frame output is checked against an independent screen interpreter;
color modes are also compared with the ordinary encoder. Ownership rejection, long graphemes,
table/byte exhaustion, failed writes/flushes, borrowed encoding and successful-only commit have
dedicated checks in `PreparedChecks.fs`.

### Using the opt-in API

The following capacities and dimensions are illustrative configuration, **from documentation:**
the public `PreparedCapacity` and `PreparedRenderer` API.

```fsharp
let capacity = { Glyphs = 512; Paints = 128; Utf8Bytes = 65536; OutputBytes = 262144 }
let renderer = PreparedRenderer.create 160 50 capacity capabilities
let text = PreparedRenderer.prepareText "Prepared é 界 text" renderer
let paint = PreparedRenderer.preparePaint Paint.empty renderer
let clip = PreparedRenderer.bounds renderer
PreparedRenderer.clear paint renderer
PreparedRenderer.text clip 0 0 clip.Width true true text paint renderer
PreparedRenderer.present output renderer
```

For a mounted view, create `PreparedLayout.create mounted renderer` once. After changes call
`PreparedLayout.refresh false cache`, then `PreparedLayout.present output cache` on the existing
owning graph thread. Refresh may allocate for new text, styles or structure. Already prepared draw
and presentation remain allocation-free. Direct mutations of public ordinary nodes retain their
existing invalidation requirements; refresh must succeed before drawing. A custom prepared text
width policy survives `prepareRun` and the adapter. Resize keeps existing handles valid.

Registration budgets are explicit and fixed. There is no eviction that could alias a live handle.
Prepared renderers and caches own their data for their object lifetime; disposing the mounted view
still disposes its Ranvier bindings. The opt-in cache does not take over that ownership.

## Native cells and write-time identities

The prior rejected sidecar rebuilt IDs during each comparison. This probe instead stores glyph and
paint IDs in the cell itself and resolves them at output. `Frame.put`, clear, wide-pair repair,
comparison, encoding, null output and successful snapshot copy are included in the timed loop.
It does not time a prepacked comparison alone. The normal-cell and packed variants use the same
encoder implementation copied from this checkout. Prepared glyph/style dictionary hits occur on
the write path; cold dictionary population happens during setup. Pools have fixed entry capacity
and are isolated to the probe process. They are not a proposed process-global production cache.

**Measured here**, median microseconds per complete iteration:

| Workload | Normal cells | Write-time packed IDs |
|---|---:|---:|
| ASCII sparse, full framebuffer comparison | 32.298 | 27.712 |
| ASCII dense | 229.461 | 277.278 |
| Unicode sparse, full framebuffer comparison | 35.984 | 25.370 |
| Unicode dense | 278.437 | 298.973 |

A separate paired run used the dirty-row presentation path for sparse changes. It includes the
same framebuffer writes and successful commit; compare only values within this paired run.

**Measured here**, median microseconds per complete iteration:

| Workload | Normal cells | Write-time packed IDs |
|---|---:|---:|
| ASCII sparse, dirty row | 0.753 | 0.389 |
| ASCII dense | 207.638 | 302.278 |
| Unicode sparse, dirty row | 0.726 | 0.617 |
| Unicode dense | 278.646 | 381.359 |

Decision: reject write-time dictionary mapping as the default representation. Its sparse benefit
does not offset the dense write regression, especially after damage limits the comparison region.
This is an end-to-end rejection, not an inference from the earlier sidecar scan.

`Frame.Cells`, `Cell`, `Paint`, and `Glyph` are public records/arrays. Their actual callers include
the painter, output encoder, example inspection and tests. Arbitrary direct array writes cannot
update a hidden packed buffer. A second renderer must therefore be explicitly opt-in, own prepared
handles, and forbid direct mutation of its private cells. Replacing the existing public array or
trusting stale sidecar IDs would violate the current API contract.

### Prepared handles and a native packed word

The stronger alternative prepares glyph/style handles once, then paints a packed word directly.
It does not perform a dictionary lookup during painting. The key encodes glyph ID, paint ID and
width; output resolves the payload only for a changed cell. The existing conservative API remains
unchanged; this probe is an isolated opt-in representation, not a hidden sidecar.

**Measured here:** a paired dirty-row/dense run, including write, compare, encode, null output and
commit, produced these median microseconds:

| Workload | Normal cells | Direct prepared packed word |
|---|---:|---:|
| ASCII sparse dirty row | 0.579 | 0.266 |
| ASCII dense | 190.658 | 98.061 |
| Unicode sparse dirty row | 0.689 | 0.220 |
| Unicode dense | 267.413 | 156.428 |
| Warm text preparation and handle registration | 0.239 | 0.379 |

**Measured here:** prepared rendering allocated zero managed bytes. The warm text-preparation
workload allocated 929.760 bytes per iteration normally and 984.960 with a parallel prepared-ID
array. See `encoder-prepared-ids.csv`. The packed key uses a native unsigned word and the pool has
fixed entry capacity (**from documentation:** probe implementation). This is a real engine gain;
it must not be conflated with the rejected dictionary mapping on every write. It also does not
establish retained-tree end-to-end performance: the probe calls the prepared painter directly,
while today's retained painter and public `Frame.Cells` still use normal cells.

## Preencoded Unicode

The bounded-entry cache prototype preserves the ASCII fast path and uses ordinal glyph lookup
only for Unicode, with direct UTF8 fallback on a cache miss. Registration is outside rendering.
The same loop includes framebuffer mutation, encoding and commit. The warm preparation workload
also includes segmentation and cache/ID registration; its repeated digit-plus-Unicode vocabulary
does not measure unbounded cold unique graphemes despite the historical CSV label `prepare-unique`.

**Measured here:** in the first pair, dense Unicode took 278.437 microseconds normally and 272.830
with cached bytes; in the separate dirty-row comparison run, it took 278.646 normally and 318.016
with the cache. Prepared rendering allocated zero managed bytes in every recorded variant. These
results do not show a stable CPU improvement. Reject the cache as a production optimization.
Entry count alone also cannot bound retained byte storage for arbitrarily long grapheme strings;
a production design would need an explicit byte budget and fallback, further increasing lookup work.

## Erase and repeat

The candidate is disabled by default. When enabled it scans same-style, same-width, printable ASCII
runs. Repeat never applies to wide cells, combining clusters, controls or mixed paint. Erase applies
only to plain blanks with default background and no attributes. Other cells use the ordinary encoder.
Cursor tracking accounts for erase leaving the cursor in place. It retains success-only snapshot commit.

**Measured here:** the run-scanning candidate took 198.800 microseconds versus 207.638 for dense
repeated ASCII, but 327.182 versus 278.646 for dense Unicode in the same paired benchmark. Sparse
dirty-row ASCII also regressed from 0.753 to 0.965 microseconds. Reject automatic run scanning for
the default pipeline: byte savings do not establish a broad CPU win.

**Measured here:** independent screen-model fixtures and hidden Windows screen-buffer readback
agree for default-background erase, ASCII repeat and a restricted scroll region. Blue-background
erase preserves the observed Windows cell attributes. This does not establish erase semantics for
arbitrary terminals, reverse/underline styles, wide/combining characters, or scrollback behavior.
Unknown support remains off; no terminal is classified from a heuristic environment variable.

The first native harness report falsely compared a StringBuilder beyond the API's reported read
length. The corrected harness uses the returned character count. Both the failed harness output
(`encoder-native-harness-failure.txt`) and corrected output are retained; this was a readback harness
defect, not evidence that the terminal rejected the sequences.

## Terminal scrolling

The scrolling probe compares a specialized ordinary row diff with a verified whole-region upward
shift followed by repainting the exposed row. Both include framebuffer shift/mutation, detection,
byte encoding, synchronous output/flush when enabled, and snapshot copy. It uses prepared ASCII
with default paint, a fixed viewport, autowrap disabled, and reserved byte storage. It is a kernel
comparison, not a timing of the full retained layout or the general production renderer.

**Measured here:** the null-output ordinary diff took 0.093 microseconds per iteration, whereas
scroll detection/output took 0.387. Actual hidden Windows output changed the result: ordinary diff
took 78.626 microseconds and scroll took 24.472. Output sizes were 1,739 and 109 bytes respectively;
all timed cases allocated zero managed bytes. The independent screen interpreter matched every
frame in the modeled sequence, and actual final screen characters matched each native benchmark's
framebuffer. See `encoder-scroll.csv` and the retained native fixture.

The specialized prototype alone did not justify default automatic scrolling. The real owned API
was subsequently implemented with logical front-index remapping and measured separately below. A
general renderer cannot infer a safe scroll solely from a widget's offset: sibling content, full
terminal-width scroll regions, wide-pair boundaries, styles, scrollback and failure recovery all
matter. The benchmark's restriction to a fully owned region is substantive. Unknown capabilities
stay off. No cross-platform or input-to-visible improvement is claimed from this result.

### Accepted explicit owned scrolling

`PreparedRenderer.scrollRows` mutates complete back-buffer rows without output.
`presentScrolled policy output region distance renderer` treats that movement as a hint and
compares every back cell against a logically scrolled front snapshot; arbitrary mismatching back
contents are repainted. It never shifts the accepted front before successful output and flush.
Failure invalidates the snapshot, attempts margin/style/synchronization cleanup, and carries a
margin-reset requirement into retry before the full redraw. Registration and ordinary rendering
remain independent of this opt-in.

**Measured here:** `prepared-owned-scroll.csv`, using the actual API, includes back-buffer shift,
exposed-row painting, comparison/encoding, output when enabled and commit:

| Output | Ordinary prepared diff median µs | Explicit scroll median µs |
|---|---:|---:|
| Null writer | 4.245 | 3.186 |
| Hidden Windows synchronous write/flush | 77.117 | 24.530 |

**Measured here:** output sizes were 1,745 and 119 bytes, and both paths allocated zero managed
bytes. Native screen-buffer readback matched the accepted frame. Independent model fixtures cover
arbitrary back contents, upward/downward movement, clipped full-width regions, invalidation and
partial output failure followed by retry without another mutation. The real API's CPU result
differs from the specialized prototype; comparisons are kept within their own benchmarks.

Decision: retain explicit scrolling for an owned full-width region with known host support.
`PreparedRenderer.noScroll` is the default-off policy. Enabling `FullWidthRegions` is an explicit
host assertion, not environment-variable guessing or automatic protocol negotiation. Partial-width
regions and unsupported region shapes are rejected. This does not verify Linux/macOS behavior,
remote presentation, scrollback preservation outside the owned screen, or terminal GPU timing.

## Slow synchronous output and commit ordering

**Measured here:** `slow-writer.fsx` held a write for 120 milliseconds. During the hold there was
exactly one outstanding write, the same reserved byte array remained in use, and the front frame
still contained the accepted old value. Releasing the writer allowed the commit. A subsequent
flush failure preserved the old front and invalidated the renderer; retry without another mutation
committed the new frame. `encoder-slow-writer.txt` retains the result. This verifies bounded
synchronous backpressure and success-only commit; it does not simulate network presentation or
claim that a slow writer leaves the owning UI thread responsive.

## Native cursor, image protocols and native backends

Native cursor is rejected as a replacement for painted carets in this optimization task. The
retained view has clipped, styled caret cells; a terminal has one global cursor whose shape,
blink and color are emulator-owned. A native cursor can complement accessibility/input focus but
cannot preserve arbitrary caret paint or multiple independently rendered views. It adds a distinct
focus-to-output state and invalidation contract. Readback of cursor position/visibility checks
the platform operation; it cannot establish lower input-to-visible latency or equivalent appearance.
**Measured here:** the hidden Windows fixture confirmed the requested cursor position and visible
state through console APIs. Existing mode-cleanup coverage remains the coordinator's final gate.

Images are rejected as an optimization of this text pipeline. The cell model contains sanitized
graphemes and paint, with no image data, placement, deletion or lifetime. A graphics protocol would
introduce image resource ownership, capability queries/replies, transfer limits and reconnect
recovery. Rasterizing text into images would lose terminal text selection/accessibility and the
existing grapheme semantics. No bytes are emitted speculatively to an unknown terminal.

A native-library backend is rejected under this repository's no-new-runtime-dependencies rule.
The dependency-free Windows `WriteConsoleOutput` alternative also fails semantic parity: its
character-cell ABI uses a UTF16 code unit and console attribute bits rather than an arbitrary
grapheme and truecolor paint. It cannot replace the current output on redirected or remote streams.
The existing System/OS ABI host plus VT output is the supported backend. This is a concrete contract
decision, not a claim that an untested backend is slower.

Protocol and ABI facts above are **from documentation**:
[Microsoft VT sequences](https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences),
[xterm control sequences](https://invisible-island.net/xterm/ctlseqs/ctlseqs-contents.html),
[CHAR_INFO](https://learn.microsoft.com/en-us/windows/console/char-info-str), and
[kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/).

Linux/macOS native execution, emulator-specific presentation timing, and real remote transports
remain external validation limits. Local models and successful Windows writes are not evidence
for those properties.

## Reproduction and discarded code

Build `tools/PreparedBench/PreparedBench.fsproj` in Release, then run its executable for paired
actual-API and retained-tree timings. `tools/PreparedBench/native-scroll.ps1` launches the actual
API output comparison hidden and fails if final native screen readback disagrees. The coordinator's
normal executable suite includes `PreparedChecks.run` and `PreparedChecks.runLayout`; the standalone
`tools/PreparedBench/check.fsx` invokes those same fixtures after the tests project is built.

`tools/EncoderNative` retains only the independent protocol/cursor readback fixture and bounded
synchronous-writer check. Build its project before running `run.ps1`. No visible benchmark window
is opened. The specialized scroll microbenchmark source was discarded after the actual API
comparison superseded it; its historical CSV remains.

All isolated baseline/cached/write-mapped/packed/run-scan renderer copies, generators and their
build outputs under task-created `tools/EncoderProbe` were removed. None of the discarded
implementations is on the production path. Evidence includes the rejected results and harness
failures above. The accepted implementation, durable fixtures, and actual API benchmark remain.
