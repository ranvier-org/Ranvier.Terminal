# Feature showcase

A live terminal gallery built with `Ranvier.Terminal.View.Dsl` and typed terminal styles.
The page registry is in `Kit.fs`, with independent page builders in `Pages.fs`. The model remains
alive while page scopes are mounted and disposed.

The **Live output** page streams a clock, colored progress and Unicode into a bounded output
frame. Add history, browse it with the wheel or PageUp, return to the live tail, or clear it.
The **Subprocess** page starts an echo child in Windows ConPTY or a supported POSIX PTY;
other supported sample configurations use redirected streams. Send hello, type into the frame,
request its terminal size, stop it, or type `exit`. Resizing the showcase resizes the child.
Ctrl+Tab leaves the process frame; Shift+PageUp browses its history. Ctrl+C in the focused
process frame reaches the child; elsewhere it exits the showcase. Leaving the page or reloading
the view closes its process, and application shutdown awaits cleanup.

These pages live in `UtilityPages.fs` and share the Utilities sample's model through a source link,
so bounded input queues, output decoding, viewport resizing and subprocess cleanup use the same
implementation. The stream is always live; the child starts explicitly through its Start control.
The registry exposes the new pages through F9 and F10 or their sidebar buttons.

Double-click `Showcase.cmd` in the repository, or run `scripts/launch-showcase.ps1` from a terminal you opened yourself. Both run under `dotnet watch` by default. Launching from an agent-owned command session can tie the app lifetime to that session: its termination can close the window without a managed exception. The Explorer launcher avoids that ownership.
The metadata-update handler remounts the registered view after supported hot reloads while retaining
the model. Type changes may restart the process. Use `-NoWatch` for the Release executable.

| Page | Features to try |
| --- | --- |
| Components | Compare spinner and pulse animations under Task status and progress. Open Meters for line, block, segmented, dotted, ASCII, fractional and gradient fills at the same value. Tables has plain and sortable examples. Popups pairs a hover tooltip with a caret-anchored completion field. |
| Overview | Live clock, signal updates, progress animation and pause |
| Controls | Hover over Reset, or focus it and hold Alt on terminals that report modifier press and release. Type a planet name and choose a completion. Under Mission loadout, choose a destination and check multiple payloads. Escape dismisses the list. |
| Layout | Fixed cells, percentage widths, weighted flex, reactive gap, alignment, padding, margins, wrapping, min/max bounds |
| Style gallery | Border variants, text attributes, indexed colors, RGB gradient, class/ID rules, hover, focus, reactive styles |
| Unicode | Combining marks, wide glyphs, emoji, grapheme-aware editing, sanitized escape characters |
| Lifetimes | Conditional owned branch, observable mount/cleanup counts, dynamic array content |
| Rendering | Actual per-frame rendering measurements, sparse overwrite, full invalidation, color fallback and synchronized-output control |
| Input / scroll | Add or remove log rows, then make the viewport taller or shorter. Drag the scrollbar thumb, click the track to jump, or focus the log and use the wheel, arrows or Page Up / Down. The scrollbar disappears when all rows fit. The workspace browser below the log demonstrates collapsible folders and tabs. |
| Live output | Streaming Unicode and colored progress, bounded history, scroll anchoring, clear and follow-live |
| Subprocess | Interactive ConPTY/POSIX child, queued input, size reporting, stop and owned cleanup |
| Instruments | Select a simulated channel in the virtual table with the keyboard or mouse. The titled panel shows its live braille trace and level meter. Toggle Run simulation to freeze or resume the trace. |
| Tasks | Watch a simulated build under a responsive FIGlet heading. Click the Step or Task header to sort and select a row to inspect its progress. Type in Build commands to fail, cancel or restart the run, or use the shortcuts shown below the field. |

Use the sidebar or function keys to switch pages. Tab and Shift+Tab move focus, Enter and Space
activate controls, and Ctrl+C or the Exit button closes the showcase. Small terminal windows can
scroll page content with the mouse wheel. Tab and Shift+Tab reveal the focused control inside
scrolling viewports. With mouse input enabled, Windows console records and POSIX terminals
supporting all-motion tracking report hover without a pressed button.

The Rendering page labels its measurements and states their scope. Synchronization is requested,
not proof that an emulator supports it. GPU work is performed by the terminal host and is not measured.
User model updates and text preparation can allocate; the sample does not claim an allocation-free
whole application loop.

`--verify` renders every registered page at different viewport sizes and checks interactive actions,
stream history, and a real redirected child's input, stop, page-leave cleanup and remount without
opening a window. `--capture=<directory>` with verification writes framebuffer text snapshots.
`--smoke` visits the pages and exchanges input and size reports with a real child terminal in a short native session; `scripts/showcase-smoke.ps1` runs that session
hidden on Windows. `--report=<path>` records successful first-frame presentation or an exception.
