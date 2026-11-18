# Streaming output and subprocess terminals

The user requested utility capabilities after the completed optimization backlog, including
streaming text into an output frame and subprocess terminals, and instructed continued execution.
Implementation choices below are coordinator decisions within that scope.

Use the existing F# cells, retained nodes, Ranvier ownership and OS ABI approach. No runtime
dependencies or handwritten interfaces. Keep the current checkout and inherited edits. Do not
launch visible windows, publish, push or commit unrelated work.

## Approach

Separate the output state from transport and view. A core output buffer accepts incremental text
or UTF8/VT output and exposes detached viewport snapshots with bounded scrollback. A retained
output-frame widget consumes snapshots through Ranvier signals. Subprocess transport supports
redirected streams for commands and an actual Windows ConPTY for interactive terminal programs.
Ordinary redirected pipes must never be described as a pseudoterminal. POSIX PTY creation is
outside this implementation because safely creating the child session from a managed runtime
requires a separately verified spawn strategy; portable redirected commands remain usable.

Rebuilding an ever-growing label was rejected because it retains unbounded history and cannot
represent cursor movement or styled subprocess output. A terminal-framework dependency conflicts
with the existing repository contract. Full emulator compatibility is not claimed: document the
implemented VT commands and ignore unsupported bounded sequences without executing host controls.

## Contracts

- Output state is single-owner mutable state. Decoding survives arbitrary byte boundaries,
  malformed UTF8, wide/combining text, carriage-return progress, wrapping and scrolling.
- Parser/control storage, history and pending process output are bounded. Unsupported control
  strings cannot grow memory or escape into the outer terminal.
- Snapshots are detached from future buffer mutation. Resize preserves useful content and repairs
  wide pairs. Clearing, follow-tail and manual history navigation have explicit behavior.
- Process arguments use structured lists, with correct Windows command-line quoting for ConPTY.
  No shell is inserted implicitly. Input/output pumps cannot deadlock each other. Teardown drains
  output and releases handles; cancellation, process exit and repeated close are supported.
- Background producers do not mutate Ranvier nodes. The graph owner drains pending data and
  publishes snapshots; process lifetime can be attached to an owner cleanup.
- A live sample and executable fixtures demonstrate streaming, progress replacement, styled
  output, process input, resize, completion and cancellation. Native checks remain hidden.

## Verification

Use deterministic buffer/reference tests, chunk-boundary and malformed-sequence fixtures,
bounded-history tests, retained-view cell comparison, subprocess quoting/exit/large-output tests
and hidden real ConPTY input/resize/cleanup tests. Keep timing claims separate from functional
claims. Run rfs checks, isolated Release build, executable suite and native smoke after integration.

Native design references: [Microsoft pseudoconsole creation](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
and [CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole).
