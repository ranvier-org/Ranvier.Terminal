# Performance decisions and measurements

This records the resumed optimization work on the Windows development host. Figures marked
**measured here** come from this session; they are engine or output-completion measurements,
not terminal presentation latency or GPU timings. Ranvier signals, bindings, effects and owners
remain responsible for state and lifetime.

## Retained changes

- Reactive invalidation distinguishes geometry from paint. Idle presentation skips the rendering
  pipeline; changes recomposite independent complete rows and limit diff/commit to those rows.
  Geometry measurement reuses clean subtrees and arrangement reuses unchanged bounds.
  Existing full rendering stays available.
- Native `runReactive` and the showcase use tracked presentation. Animation ticks and graph pumping
  continue; input and graph posts wake the native wait. An explicit event-driven API can sleep
  indefinitely when the application has no animation deadline.
- Prepared text caches line boundaries and jumps directly to vertically visible lines. A column
  offset index locates horizontally visible glyphs by binary search without splitting wide glyphs.
- Bulk fills repair wide pairs at their boundaries and fill the interior in bulk.
- Output skips unchanged commits, writes ASCII directly, uses relative cursor moves when smaller,
  and emits color deltas when attributes are unchanged.
- Virtual lists retain a bounded row pool. Materialized lists now retain row identity and change
  selection paint rather than reconstructing every row.
- Conservative rendering invalidates reactive geometry. Capacity/write/flush failures remain
  retryable without another signal mutation. Existing front-buffer commit-after-success semantics
  are preserved.

## Measurement method

`tools/PerfBench` is part of the solution. Run `scripts/measure-performance.ps1 -Name comparison`
to build Release and run full and reactive modes sequentially. **From documentation: the benchmark
source** uses a timed warmup, seven timed batches and reports the median and maximum batch-average
microseconds per iteration. These are not per-frame latency percentiles. It also records allocations,
output bytes and compared/copied cells. Runs share a development machine and are not CPU-isolated.

Pipeline workloads include signal updates and text preparation. The allocation guarantee excludes
those operations. Prepared rendering and native output have separate zero-allocation regression
gates. Output workloads include framebuffer mutations; they do not isolate just the encoder.
Horizontal text measures clear, clipped painting, encoding and null-output presentation together.
Work counters for the horizontal-text workload are not instrumented; its zero counter columns
must not be interpreted as zero comparisons.

The wrapped-text scenario now fixes the paragraph's viewport height so its changing sibling is
visible. Earlier `perf-tracked` and `perf-reactive` wrapped results changed an offscreen row and are
not evidence of visible update performance. An intermediate `perf-resumed-full` run overlapped
development checks and is not used for conclusions.

**Measured here:** earlier full-versus-reactive medians, in µs per iteration:

| Workload | Full rendering | Reactive rendering |
| --- | ---: | ---: |
| Idle pipeline | 139.390 | 0.010 |
| Visible sparse label | 143.739 | 12.448 |
| Dense changing labels | 164.245 | 145.967 |
| Static wrapped paragraph plus visible changing sibling | 184.796 | 3.766 |
| Materialized-list selection | 735.228 | 153.375 |
| Virtual-list selection | 36.317 | 9.632 |

**Measured here:** sparse reactive frames compared and copied 160 cells rather than 8,000;
idle reactive calls compared and copied none. The extremely small idle timing is loop/branch
overhead near the timer's useful resolution, not a claim about OS wakeup or terminal latency.

**Measured here:** in the same horizontal-text benchmark, the preserved pre-index snapshot took
10.313 µs and the corrected implementation took 3.095 µs. Both allocated zero managed bytes in
their accepted runs. The index increases text-preparation storage and allocation; it does not make
signal-driven text creation free.

**Measured here:** retained materialized rows reduced the same reactive list-selection benchmark
from 28,266.577 to 153.375 µs, and allocation from roughly 15,994,089 to 248 bytes per update.
That earlier virtual-list workload took 9.632 µs with 4,736 allocated bytes per update, including its
Ranvier effect and style declarations. Those callback allocations are outside the render guarantee.

Raw CSV evidence is retained in `performance/`. The before snapshot for list and horizontal-text comparisons already includes the
earlier cache, fill, encoder and reactive optimizations. It is not the original repository commit.
The final correctness-only newline fix followed these measurements; the benchmark inputs do not
contain the affected whitespace-before-newline case.

## Rejected experiments

**Measured here:** the short-ASCII-gap overwrite experiment reduced its output from about
13,667 to 8,369 bytes per iteration, but raised median engine time from 133.355 to 215.388 µs
in the same `output-gaps` workload. CPU performance takes priority, so the implementation and
its experiment-specific test were removed. The workload remains useful for future encoder work.
See the paired experiment CSVs for batch maxima and the other workloads.

**Measured here:** a scratch F# Interactive probe compared an unchanged framebuffer scan with
compact-ID conversion. Scalar `Cell.equal` took 19.157 µs; an already-packed SIMD comparison took
0.523 µs excluding packing; dictionary packing plus SIMD took 87.382 µs. The dictionary-sidecar
approach was rejected and its scratch source removed. This does not establish that a native packed
framebuffer design would lose: such a design would need stable glyph/style identity, lifetime
management, write-path measurements and a public-API migration. No speculative representation
replacement was retained.

## Allocation failures retained as evidence

**Measured here:** an intermediate cache fix allocated 80,016 managed bytes across the mixed
full/partial prepared-render gate. Reusing the existing option value removed that allocation.

**Measured here:** the first horizontal binary search allocated 24 bytes per call; the targeted
gate measured 240,000 bytes over 10,000 draws. IL inspection found a tuple constructor emitted for
the paired mutable bounds. Separate mutable bindings removed it. The pre-fix benchmark CSV is
retained; it is not an accepted zero-allocation result.

## Continuation measurements

**Measured here:** matched Release benchmark driver, preserved pre-continuation assemblies versus
current assemblies, null output, median microseconds per update. Source changes and callbacks are
included. These comparisons isolate this continuation; earlier tables describe earlier snapshots.

| Workload | Before | After |
| --- | ---: | ---: |
| Distant changing rows | 131.224 | 6.940 |
| Local geometry update | 143.128 | 5.183 |
| Fragmented changing rows | 143.169 | 73.003 |
| Materialized-list selection | 128.492 | 34.338 |
| Fixed virtual-list selection | 7.990 | 3.855 |

Raw evidence: `performance/backlog-before.csv` and `performance/backlog-after.csv`.
Sparse, wrapped and horizontal workloads did not establish a further improvement.
The apparent gap-output regression in the broad run was checked with the same driver in an
isolated before/after/after/before sequence. **Measured here:** medians were respectively
104.850, 92.021, 111.982 and 112.111 microseconds, with no consistent regression or gain.
The `performance/gaps-*.csv` files retain that evidence.

Separate comparisons and tradeoffs are recorded in [host measurements](performance/HOST.md),
[virtualization measurements](performance/VIRTUALIZATION.md) and
[encoder experiments](performance/ENCODER.md).

## Backlog disposition

The continuation investigates the former remaining items in [the optimization ledger](OPTIMIZATION-LEDGER.md).
Its dispositions distinguish accepted implementations, rejected measured experiments and features
outside the text-rendering workload. Native verification boundaries remain explicit.

Linux/macOS native behavior and input-to-visible latency remain unverified.
No source-only test or successful write is used as evidence for those claims.
