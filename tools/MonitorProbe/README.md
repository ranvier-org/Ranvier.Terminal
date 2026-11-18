# Monitor polling and input probe

This project compiles the real monitor source and uses the normal retained view and terminal
output path. Running without arguments executes deterministic regressions: dispatched wheel
scrolling, pointer bounds, scrollbar jumps, keyboard/header interval controls, and a blocked
sampler that must not overlap reads or prevent input-driven redraws. The wheel regression failed
before the display-only OutputView path was added: the output-history handler consumed the event.

For timing, build Release, then use `run.ps1 -Name <fresh-evidence-name> -ArtifactsPath <path>`.
It opens a hidden native terminal and compares configured polling intervals in slow/default/
aggressive/default/slow order. `-Tall` requests a taller native console; `-Headless` uses null output at a fixed tall viewport. Runs are
sequential, with warmup, real live telemetry and injected process navigation. Do not run other
builds or benchmarks concurrently. Reports identify viewport dimensions and all timing scopes.

UI timing includes input dispatch, model and component updates, retained layout, diff,
encoding and output. It excludes physical input transport and terminal compositor presentation.
Frame-gap timing includes scheduler jitter. Sampling runs in the background, is timed separately,
and is included in whole-process CPU/allocation figures. The first collection can be colder than
later ones. No samples overlap, even at a configured interval shorter than collection time.

Native terminal timing is not a refresh-rate or input-to-visible latency guarantee. Zero
observed long frames is evidence about the recorded workload, not every machine or display.
