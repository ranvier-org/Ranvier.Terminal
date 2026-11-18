---
title: Rendering architecture
description: Follow a signal update through the retained tree, cell framebuffer and terminal output.
---

A button changes a signal on the Ranvier graph. Its text or style binding updates a retained node and marks the affected work. At presentation time the renderer compares desired terminal cells with the last frame the output sink accepted.

<div id="architecture-flow"></div>

## What runs on a frame

| Change | Work on the reactive path |
| --- | --- |
| A text, size or children change affects geometry | Layout reuses clean measurements and unchanged bounds, then repaints the affected region. |
| Only paint changes | The renderer repaints complete affected rows, preserving wide-cell pairs and overlapping content. |
| Nothing has changed | An idle presentation skips layout, painting, comparison and snapshot copying. |
| The framebuffer resizes or the rule set is replaced | Layout and painting run across the viewport. |

`Terminal.runApp` drives this path. For an application that owns its output sink, `Layout.presentReactive output mounted renderer` presents a tracked tree. Change it with setters such as `Node.setText`, `Node.setStyle` and `Node.setChildren`. After direct field changes, call `Node.invalidate` on the affected node. Mutate and render on the owning graph thread.

## When a frame becomes visible

Painting fills the reusable back framebuffer. Encoding compares it with the front snapshot and emits cursor movement, style changes and changed glyphs. The output sink must consume the requested bytes synchronously and flush successfully before the renderer commits the new snapshot. If output fails, the snapshot is invalidated and the next successful presentation redraws the viewport. The terminal emulator owns final rasterization.

For a straightforward full repaint, use `Layout.render mounted renderer.Back` followed by `Renderer.present output renderer`. That pair also works with a headless output sink. It is the path used by the Dashboard sample's `--headless` mode.

## Where each responsibility lives

| Project | Responsibility |
| --- | --- |
| `Ranvier.Terminal.Core` | Grapheme preparation, terminal cells, framebuffer comparison and byte encoding |
| `Ranvier.Terminal.Css` | Typed style declarations, layout lengths and selector rules |
| `Ranvier.Terminal.View` | Retained nodes, Ranvier bindings, layout, input routing and widgets |
| `Ranvier.Terminal` | Terminal session lifecycle, native input, the app loop and subprocess transports |
| `Ranvier.Terminal.Components` | Optional spinner, pulse and meter presets |

[See how a subprocess feeds a retained output view](subprocess-output.md)
