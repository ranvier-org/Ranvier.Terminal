# Terminal utilities implementation plan

> **For agentic workers:** Use executing-plans for coordination and disjoint parallel implementation;
> each component receives an independent correctness review before final integration.

**Goal:** Stream text into retained output frames and host useful subprocess terminal sessions.

**Architecture:** Core output state, a Ranvier snapshot widget, and separate subprocess transport.
Existing renderers remain responsible for the outer terminal. Transport never writes directly to it.

**Tech stack:** Existing F#/.NET, Ranvier, System.Diagnostics.Process and Windows ConPTY ABI.

**Spec:** `docs/superpowers/specs/2026-11-18-terminal-utilities.md`

## Global constraints

No runtime dependencies, handwritten interfaces, visible test windows, or concurrent edits to
shared project/registration files. Coordinator integrates shared files. Preserve current changes.
Serialise performance/native gates. No commits or publishing in this task.

## Review focus

Split Unicode/VT, history eviction and malformed controls; output during shutdown; full pipes
while input is pending; quoting and nonexistent executables; stale/disposed Ranvier consumers.
Each component's fixtures must cover the relevant cases rather than only happy-path echo.

## Components

- [x] Core: create `src/Ranvier.Terminal.Core/OutputBuffer.fs` and
  `tests/Ranvier.Terminal.Tests/OutputBufferChecks.fs`. Specify buffer and detached snapshot API,
  then implement incremental text/VT decoding, bounded history, resize and snapshot operations.
  Test equivalent output across chunk splits, progress, styles, wide cells, malformed controls,
  history eviction and alternate-screen restoration. Coordinator adds compile order.
- [x] Transport: create `src/Ranvier.Terminal/Subprocess.fs` and isolated subprocess probe.
  Provide structured executable/argument configuration, redirected command mode, Windows ConPTY,
  bounded output draining, input, resize, completion, cancellation and idempotent close. Test
  real child output/input, argument quoting, failure, sustained output and teardown.
- [x] View: create `src/Ranvier.Terminal.View/OutputView.fs` plus output-view fixtures after the
  snapshot API is fixed. Use existing nodes and Ranvier bindings; preserve styled cells and Unicode,
  support follow-tail/history navigation and disposal. Compare rendered cells to snapshots.
- [x] Integration: coordinator creates a live hot-reload sample using the output view and both
  transports, with a headless verification path and a hidden native subprocess check. Document
  protocol/platform boundaries and usage. Integrate projects and test registrations.
- [x] Review and final gate: independent source review, rfs check, isolated Release verification,
  unfiltered dotnet test, subprocess probes and existing native smoke. Fix findings before completion.

## Progress and rulings

Ruling: continue in the current checkout under the user's explicit continuation instruction;
the optimization changes are uncommitted and are the dependency for this work.
Ruling: keep unknown capabilities off and name Windows-only PTY support explicitly; portable
redirected processes are useful independently and do not imply interactive terminal semantics.

Completed: source review findings in process shutdown and handle transfer were fixed. History
navigation now publishes immediately through a stable model-owned effect. Verification evidence,
including preserved launcher failures, is recorded in the utilities section of VERIFICATION.md.
