# Completed optimization ledger

The continuation treats every remaining item in PERFORMANCE.md as work to investigate, not as
an automatic deferral. Acceptance requires equivalent output, appropriate edge cases, no prepared
render allocations, and a measured advantage on a stated workload. Failed experiments are removed;
their observations remain in the evidence. Windows native checks stay hidden.

| Area | Current action | Disposition |
| --- | --- | --- |
| Independent damage regions | Reusable dirty-row mask, contiguous-run painting/commit | Retained; distant and fragmented workloads improve |
| Incremental geometry | Cached subtree measurement and unchanged-bounds arrangement | Retained; local geometry workload improves; root hide/show regression added |
| Sleeping host | Native wake plus Ranvier dispatcher, explicit animation deadlines | Retained; parser-wake regression and expanded hidden native edges passed |
| Fixed virtualization | Avoid unchanged row updates/style construction | Retained; callback time and allocation improve |
| Variable-height virtualization | Prefix extents, viewport signals, bounded retained rows | Retained; cached default improves scroll/resize; explicit low-memory mode trades resize cost |
| Compact cells/write-time IDs | Owned packed renderer and retained-tree adapter | Retained opt-in for dense reusable text; ordinary reactive stays default for sparse new text |
| Prepared UTF8 | Bounded cache vs direct encoding, preparation costs included | Dictionary cache rejected; bounded owned payloads retained with packed handles |
| Erase/repeat/scroll protocols | Independent screen model, native checks, CPU/byte comparison | Automatic scanning rejected; explicit capability-gated scrolling retained after native gain |
| Native cursor/images/native backend | Evaluate against text-workload scope and measured alternatives | Not equivalent replacements for the text contract; reasons and native cursor probe in ENCODER.md |
| Slow writer/backpressure and latency | Verify bounded synchronous output and input-to-write behavior | Synchronous bounded commit verified; input-to-console readback measured, visible/remote latency unclaimed |

Timed runs and heavy build/native probes receive an exclusive coordinator slot. Source inspection
and edits use disjoint ownership. Existing full-render and low-level framebuffer APIs remain
available; optimized paths must not silently change direct mutable-write contracts.

Evidence and workload definitions are in [PERFORMANCE.md](PERFORMANCE.md),
[HOST.md](performance/HOST.md), [VIRTUALIZATION.md](performance/VIRTUALIZATION.md), and
[ENCODER.md](performance/ENCODER.md). The broad-run output-gap regression did not reproduce
consistently in the isolated reversed-order comparison; it is neither an accepted gain nor a
reason to retain a speculative encoder rewrite. Every listed item now has a disposition.

**Measured here:** final integrated verification passed the semantic check, isolated Release build,
executable suite, unfiltered solution test command, headless examples, hidden native session and
showcase checks, and the production-linked parser probe. Review findings (root visibility,
parser wake timing, adapter registration retry) were fixed and covered by regressions. See
[VERIFICATION.md](VERIFICATION.md). POSIX native execution and remote/visible latency remain
validation limits rather than claimed successes.
