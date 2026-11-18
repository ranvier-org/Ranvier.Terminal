# Terminal rendering research

Research date: November 18, 2026 (client timezone). This is a source-informed proposal, not an implementation or a cross-library benchmark. Priority expresses engineering judgment about this codebase, not measured speedup. Upstream main branches and online documentation can change.

## Findings in our implementation

Inspected `Terminal.run`, `Layout.render`, `measure`, `draw`, `Renderer.encode`, `commit`, `Cell.equal`, `TextRun.lineEnd`, `Frame.textWithWrapping`, `Node`, `Dsl.signalText`, `bindStyle`, and `each`, alongside the existing tests and showcase.

- The host polls, ticks and renders on a fixed schedule even without visual changes.
- Every render measures and arranges the tree, clears the back framebuffer and repaints. Clipping prevents out-of-bounds writes, but text painting still traverses the complete prepared run.
- Encoding compares the whole framebuffer. Commit copies the whole back framebuffer even when encoding emits nothing.
- The encoder already skips unchanged cells, avoids redundant cursor moves inside contiguous runs, tracks paint within a frame, batches output into a reusable byte buffer and commits only after successful output.
- Cells are structs in a flat array already. They contain a managed glyph string and paint values; equality includes ordinal string comparison. Emitted glyphs go through UTF-8 byte counting and encoding each time.
- Word-wrap measurement and painting share a correct common breaker, but each traverses text again. There is no width-keyed line-index cache.
- Ranvier bindings update retained nodes, but nodes have no layout/paint invalidation state. Public mutable fields permit writes outside setters.
- `each` rebuilds its children array when its source changes. It is not a virtualized viewport or keyed reconciliation API.

These findings distinguish existing optimizations from actual gaps. Zero managed allocation in a warmed render does not establish minimal CPU work or low terminal latency.

## Techniques worth adopting

| Priority | Technique and primary reference | Proposed adaptation | Main constraint |
| --- | --- | --- | --- |
| First | Demand-driven scheduling: [OpenTUI renderer](https://opentui.com/docs/core-concepts/renderer/) | Schedule after visual invalidation, input or animation deadlines. Coalesce mutations before a presentation. Block on input/wakeup when idle. | Preserve explicit continuous ticking for applications that need it; graph posts, cancellation and resize must wake the loop. |
| First | Damage regions and horizontal spans: [Textual compositor](https://github.com/Textualize/textual/blob/main/src/textual/_compositor.py) | Separate style, measurement, arrangement and paint dirtiness. Maintain bounded dirty-row spans and repaint only intersecting content. | Movement/removal invalidates old and new bounds; sibling layout and inherited styles can propagate damage. |
| First | Viewport-oriented line rendering: [Textual line API](https://textual.textualize.io/guide/widgets/#line-api) | Virtual lists/logs/tables, reusable visible row nodes, cached wrap line starts and direct access to visible text lines. | Variable-height rows need an extent index; owners/focus must follow stable logical identities when rows are recycled. |
| Next | Byte-cost-aware output: [Ultraviolet renderer](https://github.com/charmbracelet/ultraviolet/blob/main/terminal_renderer.go) | Select cursor moves, style deltas, short-gap overwrites, erase commands and ASCII repeat commands according to cost and capability. | Smaller byte count does not guarantee less emulator work. Background erase, margins, wide cells and terminal state matter. |
| Next | Buffer lifecycle: [Ratatui Terminal](https://docs.rs/ratatui/latest/ratatui/struct.Terminal.html) | Full-repaint mode can swap completed buffers instead of copying them. Incremental mode should commit dirty spans instead. | Swapping changes Back identity and its contents. Existing low-level callers can retain or incrementally edit Back; do not silently change that contract. |
| Next | Small inline glyph representation and pooled extended graphemes: [Notcurses cell documentation](https://notcurses.com/notcurses_cell.3.html) | Prepared glyph IDs, pre-encoded UTF-8, compact paint IDs and an ASCII fast path; compare compact values in the diff. | Bound pool/cache lifetime; IDs cannot be recycled while either framebuffer references them. Width policy changes invalidate prepared data. |
| Workload-dependent | Scrolling and row matching: [Ultraviolet](https://github.com/charmbracelet/ultraviolet) | Explicit scroll intents from log/list widgets; rotate row storage and emit a safe scroll command, then paint exposed rows. | Terminal scroll regions can move unrelated content. Restrict eligibility and keep ordinary diff as fallback. |
| Later | Protocol negotiation: [libvaxis](https://github.com/rockorager/libvaxis) | Async startup capability probes with bounded replies/timeouts and conservative defaults. Enable optimized paths only when supported. | Multiplexers and remote topology can change semantics. Never make every frame wait for a reply. |

### Invalidation is the main architectural opportunity

Use the existing Ranvier binding callbacks to mark nodes dirty. Text replacement prepares glyphs once, invalidates its line index and marks measurement/paint dirty. A foreground-only change should not force layout. Structural changes invalidate arrangement and affected areas. Focus and hover must re-evaluate relevant style rules and distinguish geometry changes from paint-only changes.

Keep graph ownership and evaluation on the existing thread. Use mutable records, flags, revision counters and preallocated row-span arrays rather than introducing an interface hierarchy or a separate reactive scheduler. Default to conservative propagation until measured equality cutoffs prove that a parent's output constraints and size are unchanged.

Direct field writes need an explicit contract: tracked setter functions for normal use and `invalidate` for low-level writes. Keep a conservative full-render path for callers that cannot provide reliable damage information. Checking every field every tick would undermine the purpose of incremental rendering.

For painting, damage means recompositing all intersecting content in the affected region, not simply repainting the node that changed. Clear the damaged area to the correct background, account for exposed old bounds, and expand spans around wide-cell pairs. When damage becomes fragmented or covers much of the screen, switch to the simpler dense path using a benchmark-selected threshold.

Do not use row hashes as a correctness oracle. Hashing an entire screen every tick can duplicate the scan we wanted to remove. Prefer authoritative mutation revisions/dirty ranges; if hashes help discover scrolling, confirm candidate matches before suppressing writes.

### Output optimization details

Our encoder already combines output into a buffer and tracks adjacent cells. The missing step is choosing among equivalent terminal operations. Start with safe cursor movement and attribute deltas, then evaluate overwrite-through-small-gaps, erase-to-end-of-line and ASCII repeat. Ultraviolet explicitly restricts its repeat optimization to ASCII because terminal support for complex graphemes differs.

Maintain speculative encoder state separately from accepted terminal state. Advance the latter only after successful output. On partial failure invalidate the screen and cursor/style assumptions. This preserves our existing failure recovery contract.

For scrolling, explicit widget intent can avoid discovering motion by hashing every row. Use terminal scrolling initially only for an eligible full-width region with no overlapping fixed content. A logical scroll and terminal scroll are separate operations; clipping alone does not make a terminal scroll safe.

Demand-driven scheduling should not create an unbounded queue of completed frames. Our synchronous writer currently blocks the graph thread during output, rather than accumulating such a queue. If measurements justify a writer thread, use bounded ownership of immutable pending output and coalesce not-yet-encoded visual states. Never discard a partially written escape stream. Output completion is not proof that the terminal has displayed the frame.

### Text and memory

[FTXUI's changelog](https://github.com/ArthurSonzogni/FTXUI/blob/main/CHANGELOG.md) documents gains from ASCII-specialized width work and output preallocation. We already prepare widths and reuse the output buffer, so copying those headlines is not a new optimization. Our actionable differences are avoiding per-emission UTF-8 conversion, avoiding repeated whitespace classification, and reusing width-keyed wrap line indices across measurement and painting.

Notcurses demonstrates inline short UTF-8 clusters and a pool for longer clusters. An F# adaptation can retain the author-facing DU/record API while using compact numeric data internally. Only then consider `System.Numerics` or `System.Runtime.Intrinsics` for dense comparisons. Our array is already contiguous; replacing a flat array with another flat array is not an optimization. Profile actual cell size, memory traffic, string comparisons and JIT output before choosing packing or SIMD.

## Features with selective performance value

- Native cursor placement/blinking can remove application repaint work for a standard input caret. Keep a software caret option for custom appearance and verify host behavior.
- Virtualized text viewers, logs and tables offer more performance value than expanding a generic widget catalog.
- Kitty graphics separates image transmission from placements, allowing retained image reuse: [protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/). This is useful for image-heavy applications, not a reason to rasterize ordinary text UIs. Local shared-memory transport needs detection; remote sessions need stream transport. GPU work remains terminal-owned.
- OpenTUI documents cases where synchronized output through a remote multiplexer increases transmitted work. Preserve explicit override and test the actual topology rather than treating synchronization as a universal performance switch.

## Native dependency decision

Notcurses is an attractive C reference for composition, terminal state and graphics. FTXUI is a useful C++ reference for text fast paths. Neither is justified as a runtime replacement by this investigation: it would add ownership/build/deployment work while duplicating capabilities already implemented here. The highest-priority proposals fit F# plus System APIs and OS calls. Reconsider an optional ABI boundary only after a specific kernel or media workload demonstrates a reproducible advantage including crossing, copying and lifetime costs.

## How to establish performance, not just plausibility

Do not reuse upstream percentage claims as projected Ranvier gains. No cross-engine performance ranking or new speedup measurement was made in this investigation.

Use identical prepared scenes and deterministic update traces for a baseline and each candidate. Separate null-output engine timings, actual output completion, remote bandwidth and user-visible presentation latency.

| Workload | What it discriminates | Acceptance evidence |
| --- | --- | --- |
| Static screen | Demand scheduling | No render passes, diff scans or framebuffer copies between real invalidations; measure idle CPU/wakeups separately. |
| Sparse changing label / caret / selection | Damage propagation | Fewer visited nodes/cells, unchanged unaffected regions, identical final screen. |
| Dense animation and alternating styles | Dense fallback, packing, encoder | CPU time, bytes, allocations and output completion under the same trace; avoid sparse-only wins. |
| Long wrapped Unicode text | Prepared line indices | Viewport work does not repeatedly traverse preceding text; resize and edits rebuild the right indices. |
| Large virtual list and scrolling log | Virtualization / scroll intent | Track mounted nodes, retained memory, visited rows and emitted bytes as logical content grows. |
| Slow writer / SSH / multiplexer | Scheduling and backpressure | Input responsiveness, bounded pending memory, queue age and measured bytes on the actual link. |
| Resize, removal, overlap, failed writes | Invalidation correctness | Compare to the conservative reference renderer and existing terminal-screen model. |

Instrument stage durations, visited nodes, painted/diffed/committed cells, bytes, write calls, allocations and cache size. Report distributions and tails rather than only throughput averages. Keep input-to-write latency separate from input-to-visible latency. Continue the changing-frame allocation gate and extend the screen-model interpreter before enabling new terminal commands.

Recommended implementation order: establish stage/work counters; add tracked invalidation and demand scheduling; introduce cached layout/line indices and virtualization; add dirty-span painting/diff/commit with dense fallback; optimize encoder operations; then evaluate compact cells/SIMD and terminal scrolling on the workloads that justify them.
