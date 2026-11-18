# Virtual list investigation

The fixed list preserves its public API and retained row identities. An unchanged viewport and
item-array identity now update only the previous and current selection styles. Style declaration
lists are constructed at mount time. A moved viewport or replacement array updates visible slots;
hidden slots release their prepared text. Signal scheduling and owner disposal remain Ranvier's.

`Widgets.virtualTextList declarations viewport items selected scroll` accepts Ranvier signals for
`VirtualListViewport` (content width and height in terminal cells), immutable replacement string
arrays, selected item index and vertical line offset. The outer node accepts declarations; an inner
node owns the viewport's dimensions. Hosts must publish actual content dimensions on resize.
Width and height must be positive. The viewport wraps words using the same prepared-text routines
as normal layout and painting, including explicit newlines and wide/combining glyphs. Empty items
occupy a blank row. Selection changes reveal the selected item's start; Up/Down move selection,
while PageUp/PageDown move the line offset without changing selection. Offset normalization is
published back through the scroll signal. Oversized items can be explored with line scrolling.

The virtualizer retains a prefix extent array proportional to the item count and a bounded cache
of prepared text. Nodes overlapping successive viewports are recycled by item index. The number
of retained nodes is bounded by viewport height, including partially visible rows. Binary search
identifies the first intersecting item. Replacing the array invalidates the cache; changing width
remeasures every item, reusing prepared runs where cached. Total height beyond the integer
line-offset range is rejected. In-place array edits are unsupported; publish a replacement through
the items signal.

`Widgets.virtualTextListWithCache declarations maxCachedCharacters viewport items selected scroll`
exposes the cache budget. **From documentation: `Widgets.fs`**, the default budget is 1,048,576
UTF-16 code units, charged at a minimum of 64 units per item to bound overhead for short or empty
strings. This is a deterministic retention budget, not an exact heap-byte limit; each prepared
run also retains grapheme strings, glyph arrays, line arrays and column offsets. Actual heap deltas
are measured separately. A zero budget retains prepared text only in visible nodes, trading
resize work and temporary allocation for lower retained memory.

`tools/WidgetBench` preserves the old fixed-list callback as an in-process baseline. Its fixed
pairs time only signal updates and callbacks, including text preparation when scrolling. Its
variable-height pairs time signal updates, rendering, encoding and null-output presentation;
the resize pair includes renderer resize. These are distinct benchmarks and must not be compared
across pairs. They do not measure terminal presentation or GPU behavior. Allocation counts include
callbacks and resize, outside the prepared-render allocation guarantee.

`VirtualizationChecks.run` compares every cell against direct full-text painting over multiline,
wrapped, empty, wide and combining text; partial first/last rows; oversized items; viewport resize;
out-of-range selection/scroll; empty/shrink/regrow; navigation and disposal. It also checks fixed
list selection, retained identities and release of hidden prepared text.

## Results and disposition

**Measured here:** `WidgetBench` Release, Ranvier tracing disabled, on the Windows development
host. Each workload uses a timed warmup and seven batches; values below are the median batch
average in microseconds per operation. The raw files retain maximum batch averages and allocation
counts. No competing agent build or benchmark ran during the measurement.

| Callback-only workload | Preserved fixed-list implementation | Retained implementation |
| --- | ---: | ---: |
| Selection inside viewport | 2.993 | 0.322 |
| Selection scrolling viewport | 4.551 | 4.065 |
| Selection allocated bytes/update | 4,640 | 0 |
| Scrolling allocated bytes/update | 13,086.600 | 8,446.600 |

**Measured here:** the following pairs share identical text, viewport changes, renderer and output
implementation within the same process. The materialized list retains every text row. The cached
virtualizer uses the default budget; the extent-only version disables the prepared-text cache.

| Pipeline workload | Materialized | Cached virtual | Extent-only virtual |
| --- | ---: | ---: | ---: |
| Scroll, microseconds/update | 595.644 | 26.011 | 24.999 |
| Width resize, microseconds/update | 2,768.180 | 1,565.250 | 9,934.580 |
| Scroll allocated bytes/update | 0 | 744.920 | 2,928.920 |
| Resize allocated bytes/update | 47,360 | 50,432 | 41,710,048 |
| Retained managed heap bytes after mounting | 24,577,160 | 20,321,968 | 145,688 |
| Setup allocated bytes | 47,304,088 | 41,992,096 | 41,781,808 |

The retained heap figures are before/after deltas from forced full collection around mounting,
excluding the already-created input strings and signals. They include actual retained prepared
glyph strings, arrays, nodes and effects; they are neither budget estimates nor process working
set. The default cache contains all text in this workload. Lists exceeding the cache budget will
need to prepare uncached text again on width changes. Setup and full source replacement still
visit the entire source; virtualization accelerates view updates, not arbitrary data replacement.

Retain the fixed callback fast path and variable-height API with its bounded default cache and
explicit cache budget. The default passed the scrolling, resizing, memory and correctness checks.
Keep the zero-budget mode for callers prioritizing retained memory and avoiding frequent resize;
do not present its large resize allocation as an improvement. Preserve the separate fixture and
benchmark project so future changes can reproduce the tradeoff.

**Measured here:** the initial uncached prototype took 10,462.960 microseconds per resize against
2,790.960 for its materialized comparator, and allocated 41,750,064 bytes per resize. That result
was not accepted as the default. It motivated the bounded prepared cache. `widgets-paired.csv`
preserves this evidence. Parent layout changes landed between this exploratory run and the final
run, so do not compare absolute pipeline timings across those files or attribute their difference
solely to node recycling. Each final pair in `widgets-cached.csv` shares the same implementation.

**Measured here:** `WidgetBench` built without warnings or errors; cell-reference and lifetime
checks passed with disabled, partially populated and default caches. Final raw measurements are
`widgets-cached.csv` and `widgets-cached.memory.csv`.
