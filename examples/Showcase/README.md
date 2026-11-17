# Feature showcase

A live terminal gallery built with `Ranvier.Terminal.View.Dsl` and typed terminal styles.
The page registry is in `Kit.fs`, with independent page builders in `Pages.fs`. The model remains
alive while page scopes are mounted and disposed.

Launch from the repository's `scripts/launch-showcase.ps1`. It runs under `dotnet watch` by default.
The metadata-update handler remounts the registered view after supported hot reloads while retaining
the model. Type changes may restart the process. Use `-NoWatch` for the Release executable.

| Page | Features to try |
| --- | --- |
| Overview | Live clock, signal updates, progress animation and pause |
| Controls | Buttons, signal/memo derivation, editable text, checkbox, progress, selection list |
| Layout | Fixed cells, percentage widths, weighted flex, reactive gap, alignment, padding, margins, wrapping, min/max bounds |
| Style gallery | Border variants, text attributes, indexed colors, RGB gradient, class/ID rules, hover, focus, reactive styles |
| Unicode | Combining marks, wide glyphs, emoji, grapheme-aware editing, sanitized escape characters |
| Lifetimes | Conditional owned branch, observable mount/cleanup counts, dynamic array content |
| Rendering | Actual per-frame rendering measurements, sparse overwrite, full invalidation, color fallback and synchronized-output control |
| Input / scroll | Key/mouse/focus/resize events, editing, nested clipped scroll area, keyboard and mouse-wheel scrolling |

Use the sidebar or function keys to switch pages. Tab and Shift+Tab move focus, Enter and Space
activate controls, and Ctrl+C or the Exit button closes the showcase. Small terminal windows can
scroll page content with the mouse wheel. The native Windows input path reports hover movement.

The Rendering page labels its measurements and states their scope. Synchronization is requested,
not proof that an emulator supports it. GPU work is performed by the terminal host and is not measured.
User model updates and text preparation can allocate; the sample does not claim an allocation-free
whole application loop.

`--verify` renders every registered page at different viewport sizes and checks interactive actions
without opening a window. `--capture=<directory>` with verification writes framebuffer text snapshots.
`--smoke` visits the pages in a short native session; `scripts/showcase-smoke.ps1` runs that session
hidden on Windows. `--report=<path>` records successful first-frame presentation or an exception.
