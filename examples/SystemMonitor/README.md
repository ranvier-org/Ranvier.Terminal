# System monitor replica

A Ranvier.Terminal recreation of the supplied btop-style screenshots: charcoal background,
sand text, muted colored borders, braille history plots, CPU/core details, memory/disk panels,
network traffic and a scrollable process table. It adapts between tall and compact terminals.
Transparency, background blur and the font remain settings of your terminal emulator.

Run from a terminal you open yourself, using the hot-reload host:

```powershell
dotnet watch --project C:\wk\ranvier-release\Ranvier.Terminal\examples\SystemMonitor\SystemMonitor.fsproj run
```

The default **DEMO** mode uses animated, explicitly simulated reference data to reproduce the
screenshots on any host. Press **L**, or pass `--live` after `--`, to sample this computer.
The status line always identifies the source. No background command or process is launched by
the monitor, and process details are read-only.

Polling defaults to **500 ms**. Use **+ / -**, click the header's interval controls, or launch
with `--live --interval-ms=500` after `--`. The configurable range is **100–10000 ms**;
keyboard and mouse adjustments use **100 ms** steps. These are configured intervals, not
measured sample durations. The header displays the current value. A slow read never overlaps
the next read or queues a backlog. Input-triggered redraws run immediately, independently of
polling; completed samples are picked up by the host's short maintenance tick.

| Input | Action |
| --- | --- |
| L | Switch live host / simulated reference data |
| P or Space | Pause/resume sampling |
| / or F | Filter by process name, executable path or PID |
| Enter while filtering | Keep filter and return to navigation |
| Escape | Clear filter or close overlay |
| Up/Down, PageUp/PageDown, Home/End | Select and scroll processes |
| Mouse wheel / click | Scroll process viewport / select row |
| Click scrollbar | Jump within the process list |
| + / - | Increase/decrease polling interval |
| S | Cycle CPU, memory and PID sorting. Click a column heading to sort by that value, then click again to reverse. The arrow marks the current direction. |
| R | Reverse ordering |
| Enter | Inspect selected process |
| M or ? | Help overlay |
| Q or Ctrl+C | Quit and clean up |

The registry is in `Kit.fs`; hot reload replaces the view while preserving the model. Restart
after type changes or changes to the entry point. The view composes styled panels, meters,
braille charts and a virtual process table with `Ranvier.Terminal.View.Dsl`. Updates run on the graph thread;
live sampling uses a single background task with cancellation and no overlapping requests.

Live mode collects Windows system CPU and physical memory, Linux aggregate/per-core CPU and
physical memory/cache, readable process CPU/working sets, mounted-drive free space, and aggregate
interface byte counters. CPU and transfer rates need a baseline sample. Process CPU is normalized
over the host's logical processor count. Permission-denied/exited processes are skipped. User
identity, GPU utilization, temperature, battery, power and Windows per-core counters are not
invented. Unsupported readings are shown as unavailable. Windows system CPU follows the processor
group scope of [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes);
physical memory uses [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex).

`--verify` checks responsive rendering, animation, filtering, selection,
sorting, pause, reload and real telemetry without a window. `--capture=<absolute-directory>`
exports the demo frame cells as JSON; `scripts/render-monitor-capture.ps1` renders those cells
to a PNG on Windows. These are framebuffer previews, not native-window screenshots.
`--smoke` runs the real terminal host briefly and exits; the smoke script launches it hidden.

`tools/MonitorProbe` checks wheel routing through the actual retained view, scrollbar boundaries,
interval controls, and scrolling while a deliberately blocked sampler is pending. Its opt-in
timing probe compares slow/default/aggressive polling with real telemetry and native terminal
output, recording UI-update and frame-gap distributions separately from background sampling.
