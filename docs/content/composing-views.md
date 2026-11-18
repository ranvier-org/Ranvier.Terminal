---
title: Composing views
description: Build retained layouts and choose bindings that preserve state as the model changes.
---

## Give the input a stable place

Open `Ranvier.Terminal.View.Templates` as in [Getting started](getting-started.md). Give the editor a value source and use its native input event to update the model. Typing updates the summary while the editor keeps its focus and caret.

```fsharp
type Crew = { Name: Signal<string> }

let manifest =
    column (column.style (Css.border Rounded), column.style (Css.padding 1)) {
        "Crew manifest"
        row (row.style (Css.gap 1)) {
            "Name"
            input (input.style (Css.width (Css.flex 1)),
                   input.props.value (from (fun model -> model.Name)),
                   input.onInput (fun model edit -> model.Name.Value <- edit.Value))
        }
        fun model -> $"On duty: {model.Name.Value}"
    }

let setup _ = render manifest { Name = createSignal "Ada" }, (fun _ -> None)
```

A literal string becomes a label. Yield a state reader for computed text or `from (fun model -> model.Name)` for text supplied by a signal or memo.

## Layer styles

For existing retained nodes, open `Ranvier.Terminal.View.Dsl`. Use `styled` to add base declarations to an existing component. Use `bindStyle` for declarations that depend on signals. Here the border remains visible while the caption changes color as the field is edited.

```fsharp
let name = createSignal "Ada"
label "Call sign"
|> styled [ Css.border Single ]
|> bindStyle (fun () ->
    if System.String.IsNullOrWhiteSpace name.Value then
        [ Css.color (Css.rgb 255 150 100) ]
    else
        [])
```

Style bindings apply in installation order. Returning an empty list reveals the declarations beneath that layer. To style focus or hover across several controls, assign classes with `classes` and supply ordered selector rules through `Mounted.Rules`. Matching rules apply in array order.

## Keep a row while its data changes

When records can move, use a stable key for each row. In this example the first value in each tuple identifies the row. Reading `current ()` *inside* the text binding makes a replacement caption visible even when its key stays the same.

```fsharp
let crew = createSignal [| "pilot", "Ada"; "engineer", "Sam" |]

eachKeyed fst (fun () -> crew.Value) (fun current ->
    text (fun () -> snd (current ())))
```

Supply a fresh array to update `crew.Value`. Removing a key releases that row's owned effects and cleanups. Use `each` for collections whose children can be rebuilt on change. Use `show condition build` when a panel and its bindings should exist only while the condition holds.

## Wrap long text

Give a label `Css.wrap true` to wrap at whitespace. A word wider than the available space breaks at a grapheme boundary. Add `Css.wordWrap false` when a long token should break at any grapheme from the beginning. Explicit newlines are preserved.

[Build a tab strip or a selection popup](navigation.md)
