---
title: Getting started
description: Run the native gallery and build a reactive counter from a source checkout.
---

## Run the gallery

Clone `ranvier-org/Ranvier.Terminal`. Use the .NET 10 SDK specified in `Ranvier.Terminal/global.json`. From the Terminal repository root, run this command in an interactive terminal.

```sh
dotnet run --project examples/Showcase/Showcase.fsproj
```

Choose **Controls** in the sidebar. Press Tab to focus a button and Enter to activate it. Click an input to edit its value. Ctrl+C exits when the application handles the keyboard directly. The **Subprocess** page has its own input routing when the child pane has focus.

For a terminal-free first run, the Dashboard sample renders into a byte-counting output sink and exits.

```sh
dotnet run --project examples/Dashboard/Dashboard.fsproj -- --headless
```

## Build a counter

Create a console app and install the terminal host. NuGet restores the View, Css and Core libraries as dependencies.

```sh
dotnet new console --language F# --framework net10.0 --output TerminalCounter
dotnet add TerminalCounter/TerminalCounter.fsproj package Ranvier.Terminal
```

Replace the contents of `TerminalCounter/Program.fs` with this program.

```fsharp
open System.Threading
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Templates

type Counter = { Count: Signal<int> }

let counter =
    column (column.style (Css.padding 1), column.style (Css.gap 1)) {
        "Counter"
        fun model -> $"Count: {model.Count.Value}"
        button (button.onClick (fun model _ -> model.Count.Value <- model.Count.Value + 1)) {
            "[ Increment ]"
        }
    }

let setup _ =
    render counter { Count = createSignal 0 }, (fun _ -> None)

Terminal.runApp Terminal.defaults CancellationToken.None setup
```

```sh
dotnet run --project TerminalCounter/TerminalCounter.fsproj
```

Press Tab to focus **Increment**, then press Space. The button changes the signal. The text reader reads the new value, so the count changes in place. Setup runs inside the mounted view's owner, which releases its bindings when the application exits.

## Wake on input or on a deadline

The second value returned from `setup` is an update function. Its argument is elapsed time. Returning `None` lets the counter sleep until input, resize, posted graph work or cancellation. A clock can instead return `Some delay` to request another update. For a responsive layout, give the setup argument a name and call `viewport ()` inside a binding to read the current width and height in cells.

[Compose a view with reactive text and styles](composing-views.md)
