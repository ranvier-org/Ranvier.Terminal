---
title: Navigation and components
description: Compose focusable tabs, trees and selection popups with a model your application can read.
---

## Switch between retained pages

Build the page nodes inside owned setup. Each tab has a stable key, a visible label and a content node. Share the selected key with the rest of the application to switch pages from a command or a button.

```fsharp
let selectedTab = createSignal (Some "overview")
let tabs =
    createSignal [|
        { Key = "overview"; Label = "Overview"; Content = label "Mission overview" }
        { Key = "crew"; Label = "Crew"; Content = label "Crew manifest" }
    |]

Navigation.tabs [] tabs selectedTab
```

Focus the tab strip and use Left or Right to cycle through pages. Home and End select the endpoints. If an updated tab array removes the active key, the strip selects the first remaining tab. An empty strip writes `None` to the selection signal.

## Reveal a hierarchy

`Navigation.collapsible` accepts a caption, an expanded signal and a content node. The content survives collapsing, so reopening the section preserves its controls. For a hierarchy, use `Navigation.tree` with a signal of `TreeItem` roots, a signal containing the set of expanded keys and a writable selected-key signal. Keys must be unique across the entire tree.

In **Input / scroll** in the Showcase, focus the tree in the workspace browser. Right opens a branch and Right again selects its first child. Left closes an open branch or selects the parent. Enter or Space toggles the selected branch.

## Float a picker above the page

The picker returns an anchor and a popup. Put the anchor in the page flow and give the popup to `Popup.host` so it can paint above the page. The following expression uses the same namespace imports as [Getting started](getting-started.md).

```fsharp
let destinations = createSignal [| "luna", "Moon"; "mars", "Mars" |]
let destination = createSignal None
let anchor, popup =
    Select.single [] [ Css.border Rounded ] "Choose destination"
        4 destinations destination

let page =
    column [ Css.padding 1 ] {
        "Destination"
        anchor
        text (fun () -> defaultArg destination.Value "No destination selected")
    }

Popup.host page [| popup |]
```

With focus on the anchor, press Enter to open the picker. Use the arrow keys to highlight a choice and Enter to accept it. Escape dismisses the popup. `Select.multiple` takes a signal containing a set of checked keys. Enter or Space toggles a check and leaves the list open for the next choice.

## Choose a component

| When the view needs… | Reach for… |
| --- | --- |
| A short static list | `Widgets.list` |
| A large array with fixed-height rows | `Widgets.virtualList`, which retains a pool of visible rows |
| Wrapped items of different heights | `Widgets.virtualTextList` |
| A table whose selection survives sorting | `Table.bindSortable` with a stable row key |
| A hint beside a control or suggestions at a caret | `PopupWidgets.tooltip` or `PopupWidgets.completion` |
| A task spinner, pulse or styled meter | `Spinner.create`, `Pulse.create` or `Meter.create` from the optional `Ranvier.Terminal.Components` project |

The Showcase **Components** page lets you compare indicators driven by the same clock or progress value. Its **Tasks** page combines a sortable table, a command palette and a task indicator.

## Recognize a shortcut

For a `Key` input event, match `key.LogicalKey` for navigation or use `key.Chord` to include modifiers.
Both use Ranvier.Keyboard types. Assign a `KeyChord` to `Commands.Binding.Chord` and use
`Commands.dispatchBindings` as the event handler. The binding's `Shortcut` string controls
the label in `Commands.bindingHelp` and `Commands.bindingPalette`. Dispatch ignores releases
and pasted input. The Showcase **Components** page uses the same bindings for all three.
