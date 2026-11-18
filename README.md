# Ranvier.Terminal

A native F# terminal UI library built on Ranvier. The UI is a retained tree of mutable records;
Ranvier signals and effects update its text and styles. Layout paints into a reusable cell
framebuffer, and the output encoder writes only changed cells at absolute cursor positions.

The [documentation site](https://ranvier-org.github.io/Ranvier.Terminal/) includes a runnable first view, component
previews, subprocess and input guides, and an interactive rendering diagram.

The public abstractions use records, discriminated unions and functions. There are no handwritten
interfaces, browser dependencies, terminal-framework dependencies or native third-party libraries.
System APIs provide terminal access. FSharp.Core and Ranvier are dependencies; Ranvier's existing
project also declares Fable.Core and build-time tooling. “Dependency-free” here means no additional
third-party runtime beyond those required by Ranvier and F#.

This is an initial implementation with executable correctness and allocation gates. Windows native
execution has been verified locally. GitHub Actions also exercises native terminal sessions on Linux and macOS. See [VERIFICATION.md](VERIFICATION.md).

## Projects

| Project | Responsibility |
| --- | --- |
| `Ranvier.Terminal.Core` | Cells, prepared Unicode text, framebuffer, diff encoder, streaming output buffer, input parsing and subprocess key encoding |
| `Ranvier.Terminal.Css` | Typed style declarations, style computation expression, lengths, layout properties, state rules |
| `Ranvier.Terminal.View` | Retained nodes, Ranvier ownership and bindings, layout, rendering, DSL, widgets, focus and event routing |
| `Ranvier.Terminal` | Windows and POSIX input, terminal modes, session cleanup, application loop, redirected subprocesses, Windows ConPTY and POSIX PTY sessions |
| `Ranvier.Terminal.Tests` | Dependency-free executable regression suite and native Windows verification |
| `Dashboard` | Live clock, animation, reactive counter, editable text and checkbox |
| `Showcase` | Interactive gallery of controls, layout, styles, Unicode, lifetimes, rendering, streaming output and subprocess terminals; see [the guide](examples/Showcase/README.md) |
| `SystemMonitor` | Responsive btop-style monitor replica, animated reference mode and live host telemetry; see [the guide](examples/SystemMonitor/README.md) |
| `Utilities` | Live output frames and embedded subprocess terminal; see [the guide](examples/Utilities/README.md) |

## Build and run

The SDK target is .NET 10 (**from documentation:** `global.json` and `Directory.Build.props`).
Install the host with `dotnet add package Ranvier.Terminal`. For the optional spinner, pulse and meter presets, add `Ranvier.Terminal.Components`. The source build restores Ranvier from NuGet.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify.ps1
```

For a terminal you have opened yourself:

```powershell
dotnet ./examples/Dashboard/bin/Release/net10.0/Dashboard.dll
```

Add `--sync` when the terminal supports synchronized output. Tab and Shift+Tab move focus,
Enter or Space activates controls, mouse clicks activate buttons, and Ctrl+C exits. The dashboard
also has `--headless` for deterministic noninteractive execution and `--smoke` for a short native run.

During development, `dotnet watch --project examples/Dashboard/Dashboard.fsproj run` supports
method-body hot reload. The dashboard creates its signals and update callback together in
`setup`. Restart the application after changing the structure of its mounted tree.

On other platforms the equivalent verification commands are:

```sh
dotnet build Ranvier.Terminal.slnx -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false
dotnet tests/Ranvier.Terminal.Tests/bin/Release/net10.0/Ranvier.Terminal.Tests.dll
dotnet examples/Dashboard/bin/Release/net10.0/Dashboard.dll --headless
```

## UI and styling

```fsharp
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

let setup viewport =
    let count = createSignal 0
    column (styles {
        Css.padding 1
        Css.gap 1
        Css.border Rounded
        Css.color (Css.rgb 210 225 245)
    }) {
        text (fun () ->
            let struct (width, height) = viewport ()
            $"Viewport: {width} × {height}")
        text (fun () -> $"Count: {count.Value}")
        Widgets.button [] "[ Increment ]" (fun () -> count.Value <- count.Value + 1)
    }, (fun _ -> None)

Terminal.runApp Terminal.defaults System.Threading.CancellationToken.None setup
```

`panel`, `row` and `column` are computation-expression builders accepting nodes, literal text,
loops and yielded sequences. `signalText` uses Ranvier's fixed-dependency binding.
`styled` merges base declarations. `bindStyle` adds a reactive layer to them. Later layers
take precedence, and releasing a layer restores the styles beneath it. `onEvent` runs the
newest handler first. Return `false` to continue to the next handler or the widget's built-in
behavior. Handlers installed in an owner are removed when it is disposed.

`show` tracks its condition and owns the active branch. `each` rebuilds its children when
the collection changes. Use `eachKeyed key readRows build` for editable or focusable rows.
Supply a unique, stable key. The builder runs once per key and receives an accessor for the
current item. Read that accessor inside a text or style binding to follow replacement values.
Removing the key releases the row's effects and cleanups.

`View.mount` gives the application ownership of a subtree on an existing `Graph`. Call
`View.dispose` to remove its bindings. The graph stays owned by its caller. All tree mutations and
rendering occur on that graph's thread; the terminal host pumps posted graph work between frames.

Styles support fixed cells, percentages, weighted flex lengths, minimum/maximum dimensions,
rows/columns, gaps, padding, margins, cross-axis alignment, main-axis justification, clipping,
scrolling, borders, text wrapping, colors and text attributes. The viewport determines root size.
Wrapped leaf text contributes its wrapped height to column layout. `Css.wrap true` keeps whitespace-delimited words together by default; `Css.wordWrap false` opts into wrapping at any grapheme, while `Css.wrap false` disables wrapping. Words wider than the content area fall back to grapheme breaks. Whitespace at a soft line break is discarded; explicit newlines are preserved. Nonbreaking spaces are not word separators. This is whitespace-based word wrapping, not full Unicode line-breaking or language-specific hyphenation. This is a terminal layout
model, not a browser CSS implementation; it does not claim browser flexbox, CSS grid, CSS text
parsing, selector specificity, animation or full CSS cascade compatibility.

Assign `Mounted.Rules` for ordered state rules. ID, class, focus and hover conditions can be combined
in a selector record. Matching rules override the node style in array order. Default child foreground
and background inherit their parent's resolved color. The widget set includes buttons, checkboxes,
single-line grapheme-aware inputs with a software caret, selection lists and progress bars. Custom
widgets use `Node`, `OnEvent` and ordinary components.

## Rendering contract

`Layout.render mounted renderer.Back` clears and repaints the reusable back framebuffer.
`Renderer.present output renderer` computes differences, encodes them into its existing byte buffer,
writes and flushes, then copies the accepted frame to the front snapshot. An unchanged frame produces
no output. A failed write invalidates the snapshot and attempts to terminate synchronization; the next
successful presentation redraws the whole viewport. No per-frame clear-screen command is used.

For tracked views, use `Layout.presentReactive output mounted renderer`. Ranvier bindings mark their
nodes dirty. Idle calls skip layout, painting, comparison and copying. Paint-only changes repaint and
commit independent complete rows. Geometry updates reuse clean measurements and unchanged bounds;
rule replacement and framebuffer resize use a full pass. Whole rows preserve wide-cell pairs and recomposite overlapping content. Rendering
through the conservative API invalidates the cached geometry. Presentation failures remain retryable
even when no new signal changes.

`Terminal.runApp options cancellation setup` calls setup inside the mounted Ranvier owner.
The viewport reader returns the initial window size in cells and tracks later resizes in bindings.
Setup returns the root node and an update callback that can share local signals. Register
`onCleanup` there for app-owned resources. Return `None` from update to sleep until input,
resize, posted graph work or cancellation. Return `Some delay` for animation or a clock.

`Terminal.runReactive` connects this path to the native host. It wakes on input and graph posts and
invokes the application's tick at `FrameInterval`, so clocks and animations remain live.
`Terminal.runEventDriven options cancellation build tick` additionally lets tick return a positive
delay for its next deadline, or `None` to sleep until input, graph work or cancellation. Tick runs
initially and after each wake. `Terminal.run` retains conservative rendering for existing applications
that mutate public fields directly.

Use `Node.setText`, `setPreparedText`, `setStyle`, `setChildren`, `setScroll` and `setCaret` for tracked
updates. After direct node-field or array edits call `Node.invalidate` on the changed node; for in-place
rule-array edits invalidate the root. `invalidatePaint` is only for changes known not to alter geometry.
Keep the framebuffer exclusively owned by its mounted view while using reactive presentation.

Text preparation reserves a reusable line index. Measurement and painting share cached boundaries;
vertical clipping jumps to the visible lines, and horizontal clipping searches a prepared column index.
For dashboard views, `Styled.panel` composes reactive captions into either border,
`Styled.meterWith` accepts fill and track glyphs and colors, and `Styled.brailleChart`
plots a percentage history. `Table.createWithThumb` retains the visible rows and
accepts styles for headers, cells, selection, scrollbar track and thumb. The F11
Instruments page in [Showcase](examples/Showcase/Pages.fs) combines them in a live example.

Use `Table.bindSortable` with a row accessor, a writable selected-key signal and a table
constructor. User navigation publishes the selected key. Reordering or replacing a snapshot
preserves that key and its visible position where possible. Removing its row selects the
nearest remaining index, and an empty snapshot clears selection. The Tasks and SystemMonitor
examples use this binding.

`Ranvier.Terminal.Components` is an optional library for indicator styles. Reference its
project alongside the terminal host to use spinner frame sets, pulse animations, and meter
presets. The Showcase **Components** page compares them at the same clock or progress value.

```fsharp
open Ranvier.Terminal.Components

Meter.create Meter.ThinLine [] 24 (Rgb 0x65E0C8) (Rgb 0x556270) progress
Spinner.create Spinner.Braille [] "Loading" tick
```

`progress` reads a fraction between zero and one. `Meter.fractional` accepts a function
from horizontal position to fill color for solid or gradient subcell bars.

Overflowing viewports show scrollbars automatically. Set their glyphs and paints with
ordinary style declarations, including declarations in selector rules:

```fsharp
column [
    Css.height (Css.cells 8)
    Css.scrollbarTrack "│" { Paint.empty with Foreground = Rgb 0x64748B }
    Css.scrollbarThumb "┃" { Paint.empty with Foreground = Rgb 0x67E8F9 }
] { yield! rows }
```

Each glyph occupies one terminal cell. To hide the indicator while preserving scrolling,
use `Css.scrollbarVisibility ScrollbarVisibility.Hidden`.

Drag a thumb with the primary mouse button, or click the track to jump to that
position. You can keep dragging after a track click or move the pointer outside
the gutter. Tables use the same gestures on the track beside their data rows.

For a thin `│` track and a single-cell `█` thumb, apply
`Css.tableScrollbar trackPaint thumbPaint` to the viewport. The Showcase page viewport
uses this preset. `Css.scrollbarThumbSize` also accepts `ScrollbarThumbSize.SingleCell`
or `ScrollbarThumbSize.Proportional` independently of the glyphs and paints.

Use `Popup.host content popups` to float surfaces over a page. `Popup.atNode` anchors a
surface to a control and `Popup.atCaret` follows an editor's caret. The Controls showcase
uses `PopupWidgets.tooltip` and `PopupWidgets.completion`. Completion takes a suggestion
function, keeps the editor focused, and replaces its entire value when the user accepts.
Tooltips follow hover. Keyboard users can focus the anchor and hold Alt on backends that
report modifier press and release. Releasing Alt or leaving the terminal clears keyboard reveal.

The Tasks showcase combines `TaskStatus.createWith`, a keyed sortable table and
`Figlet.banner`. The task indicator reads the host clock during spinner and pulse animations.
Table sort descriptors compare typed row values and a stable key identifies the selected row
across refreshes. Supply a fresh array when data changes. FIGlet accepts a parsed font or
the bundled compact face and falls back to plain text when a banner exceeds the available width.
The same page uses `Commands.dispatch`, `Commands.help` and `Commands.palette` with one set of actions.

`Widgets.virtualList declarations visibleRows items selected`
takes Ranvier signals for the item array and selection, retains a fixed row pool, and follows selection
through that viewport. It supports Up/Down and PageUp/PageDown. The ordinary `Widgets.list` also retains
row identity when selection changes, but still mounts the whole static item array.

`Widgets.virtualTextList declarations viewport items selected scroll` supports wrapped variable-height
items, with Ranvier signals for its content viewport dimensions, items, selection and scroll offset.
It retains visible rows and a bounded prepared-text cache. `virtualTextListWithCache` takes an explicit
cache budget; a zero budget minimizes retained prepared text at the cost of more preparation during
resize. See [the measured tradeoffs](performance/VIRTUALIZATION.md).

The encoder uses ASCII writes, color-only style deltas and cheaper relative cursor moves when safe.
See [PERFORMANCE.md](PERFORMANCE.md) for measured comparisons and the boundaries of each benchmark.

For dense workloads with reusable text, `PreparedRenderer` offers an explicit packed framebuffer.
Create it with fixed glyph, paint, UTF8 and output budgets, register text and paint outside drawing,
then paint using its renderer-owned handles. Handles cannot cross renderer instances. Registration
fails clearly when its lifetime budget is exhausted; resizing retains registrations. Create a new
renderer when a new vocabulary needs a new budget.

`PreparedLayout.create mounted renderer` adapts an existing retained tree. Call
`PreparedLayout.refresh false cache` after tree changes, then `PreparedLayout.present output cache`.
Refresh performs geometry and registration and may allocate; draw/present reuse prepared state.
Failed refresh prevents drawing until a successful refresh. This path benefits dense reusable text;
newly formatted sparse text performs better through ordinary reactive rendering in the measured
workload. See [encoder evidence and tradeoffs](performance/ENCODER.md).

`PreparedRenderer.presentScrolled` accepts an explicit full-width scrolling hint and capability
policy. Unknown support stays off. The renderer compares the entire desired frame against the
logically scrolled snapshot and commits only after successful output, so a hint does not require
the caller to guarantee that every cell moved unchanged. The output stream and scrolling region
must be exclusively owned by the renderer.

Use the `Output` function record to supply a stream, transport, capture, or test sink. Its `Write`
function must synchronously consume the entire range or throw, and must not retain the buffer.
`encode` and `commit` are also available separately: commit only after the bytes have been accepted.
Do not modify the back frame between encoding and committing it. The renderer requires exclusive
ownership of terminal output; after external output, call `Renderer.invalidate`.

`Renderer.reserve` and `Renderer.resize` are capacity-changing operations outside the steady-state
rendering guarantee. Capacity exhaustion fails before any frame bytes are written. No automatic
allocation is hidden inside the encoder. Low-level `Frame.put` callers must supply a sanitized single
grapheme with an accurate column width; `TextRun.prepare` provides the safe text preparation path.

Wide graphemes use a leading cell and a continuation cell. Overwriting either repairs the old pair.
Clipped wide glyphs are omitted rather than splitting their bytes or spilling into another row.
Text preparation segments grapheme clusters and replaces terminal control characters. The default
width policy treats ambiguous characters as narrow and uses a practical wide-character policy;
terminal Unicode versions and font policies can disagree. Use `TextRun.prepareWith` and assign the
prepared `Node.Text` for a host-specific width policy. This is not a claim of universal Unicode-width
agreement with every emulator.

## Allocation boundary

Streaming output frames and subprocess sessions are described in [UTILITIES.md](UTILITIES.md).
Native POSIX subprocess terminals use a controlling terminal and an owned initial process group.
**From documentation:** `src/Ranvier.Terminal/PosixPty.fs` accepts Linux glibc >= 2.29 on x64/arm64
and macOS x64. The host package includes its Intel macOS PTY launcher. macOS ARM64 is explicitly rejected
until its variadic-call ABI bridge is verified. Redirected mode remains available. See the guide
for exclusive child-reaping requirements, container init usage, cancellation and cleanup boundaries;
these platform guards do not establish a completed native verification gate.
Their decoding, snapshot publication, process I/O and input handling occur outside the prepared
renderer allocation guarantee.

The warmed-up layout, cell painting, framebuffer comparison, UTF-8 encoding, output invocation and
snapshot commit allocate no managed memory for prepared nodes/text and sufficient buffer capacity.
The tests exercise changing frames, not just an idle fast path. The native Windows test also measures
the actual standard-output write and flush.

Setup, JIT warmup, input dispatch, text preparation, application callbacks, reactive graph scheduling,
collection rebuilding, resize and capacity growth are outside that guarantee. In particular, formatting
a new clock string or constructing a new style list on a signal update can allocate. No claim is made
that arbitrary user code or a complete application tick is allocation-free.

## Terminal and GPU behavior

The accepted GPU scope is terminal-host acceleration. This library emits terminal output; the terminal
emulator owns rasterization and any GPU acceleration. There is no direct GPU device, native window
renderer or GPU acceleration claim for CPU-side framebuffer diffing.

Synchronized output is capability-controlled and defaults off. Set `SynchronizedOutput = true` for a
supporting terminal. The mode identifier is **from documentation:**
[Contour's synchronized-output specification](https://contour-terminal.org/vt-extensions/synchronized-output/).
Unsupported terminals cannot be forced to present atomically. Applications choose truecolor, indexed
palette fallback or monochrome explicitly. The host requires an interactive VT-capable terminal;
embedded or redirected rendering uses the core `Output` API.

Windows uses console records for keyboard, mouse, resize and focus. Linux/macOS use libc raw-mode
input and incremental terminal-sequence parsing. With mouse input enabled, the POSIX host requests
all-motion SGR reports so hover updates while the pointer moves with no button pressed.
The parser handles fragmented UTF-8, common legacy
keys, modifiers, SGR mouse reports, focus, bracketed paste, bounded malformed sequences and a subset
of CSI-u key reports. It does not negotiate or fully implement every kitty keyboard enhancement.
Paste protection is available on the protocol-input path; Windows console records do not preserve
clipboard-paste boundaries. Input processing is bounded per tick to avoid starving rendering.

The session selects UTF-8 output, restores the original encoding and saved OS modes, and leaves the alternate screen on normal completion or managed
exceptions. Forced process termination cannot run cleanup. Synchronized output, cursor visibility,
autowrap and protocol modes are reset on exit; nested ownership of terminal protocol modes is not
supported.

## Verification

Run `scripts/verify.ps1` for build, regressions, allocation measurement and the headless live dashboard.
On Windows, `scripts/native-smoke.ps1` launches hidden verification and dashboard sessions. It checks
actual console modes before/during/after, injects keyboard and mouse events, and measures actual output
allocations. No visible test window is needed.

The CI definition builds the source and runs portable checks on Windows, Linux and macOS. A checked-in
workflow is not evidence that those platforms have passed; local evidence is recorded separately in
[VERIFICATION.md](VERIFICATION.md).
