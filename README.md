# Ranvier.Terminal

A native F# terminal UI library built on Ranvier. The UI is a retained tree of mutable records;
Ranvier signals and effects update its text and styles. Layout paints into a reusable cell
framebuffer, and the output encoder writes only changed cells at absolute cursor positions.

The public abstractions use records, discriminated unions and functions. There are no handwritten
interfaces, browser dependencies, terminal-framework dependencies or native third-party libraries.
System APIs provide terminal access. FSharp.Core and Ranvier are dependencies; Ranvier's existing
project also declares Fable.Core and build-time tooling. “Dependency-free” here means no additional
third-party runtime beyond those required by Ranvier and F#.

This is an initial implementation with executable correctness and allocation gates. Windows native
execution has been verified locally. Linux and macOS host implementations are present, but native
execution on those platforms has not been verified in this environment. See [VERIFICATION.md](VERIFICATION.md).

## Projects

| Project | Responsibility |
| --- | --- |
| `Ranvier.Terminal.Core` | Cells, prepared Unicode text, framebuffer, diff encoder, output record, incremental input parser |
| `Ranvier.Terminal.Css` | Typed style declarations, style computation expression, lengths, layout properties, state rules |
| `Ranvier.Terminal.View` | Retained nodes, Ranvier ownership and bindings, layout, rendering, DSL, widgets, focus and event routing |
| `Ranvier.Terminal` | Windows and POSIX input, terminal modes, session cleanup, application loop |
| `Ranvier.Terminal.Tests` | Dependency-free executable regression suite and native Windows verification |
| `Dashboard` | Live clock, animation, reactive counter, editable text and checkbox |

## Build and run

The SDK target is .NET 10 (**from documentation:** `global.json` and `Directory.Build.props`).
Keep the Ranvier checkout next to this repository; the solution references its source directly.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File C:\wk\ranvier-release\Ranvier.Terminal\scripts\verify.ps1
```

For a terminal you have opened yourself:

```powershell
dotnet C:\wk\ranvier-release\Ranvier.Terminal\examples\Dashboard\bin\Release\net10.0\Dashboard.dll
```

Add `--sync` when the terminal supports synchronized output. Tab and Shift+Tab move focus,
Enter or Space activates controls, mouse clicks activate buttons, and Ctrl+C exits. The dashboard
also has `--headless` for deterministic noninteractive execution and `--smoke` for a short native run.

During development, `dotnet watch --project examples/Dashboard/Dashboard.fsproj run` supports
method-body hot reload. Changes to mounted tree structure require restarting the application;
the example constructs its view in `build` and keeps model updates in `tick`.

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

let build () =
    let count = createSignal 0
    column (styles {
        Css.padding 1
        Css.gap 1
        Css.border Rounded
        Css.color (Css.rgb 210 225 245)
    }) {
        "A native Ranvier UI"
        text (fun () -> $"Count: {count.Value}")
        Widgets.button [] "[ Increment ]" (fun () -> count.Value <- count.Value + 1)
    }

Terminal.run Terminal.defaults System.Threading.CancellationToken.None build (fun _ -> ())
```

`panel`, `row` and `column` are computation-expression builders accepting nodes, literal text,
loops and yielded sequences. `signalText` uses Ranvier's fixed-dependency binding. `text` and
`bindStyle` use owned reactive effects. `show` and `each` create owned dynamic branches; their old
effects and cleanups are disposed when rebuilt. `each` rebuilds its array on changes; it does not
promise keyed reconciliation.

`View.mount` gives the application ownership of a subtree on an existing `Graph`. Call
`View.dispose` to remove its bindings. The graph stays owned by its caller. All tree mutations and
rendering occur on that graph's thread; the terminal host pumps posted graph work between frames.

Styles support fixed cells, percentages, weighted flex lengths, minimum/maximum dimensions,
rows/columns, gaps, padding, margins, cross-axis alignment, main-axis justification, clipping,
scrolling, borders, text wrapping, colors and text attributes. The viewport determines root size.
Wrapped leaf text contributes its wrapped height to column layout. This is a terminal layout
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
input and incremental terminal-sequence parsing. The parser handles fragmented UTF-8, common legacy
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
