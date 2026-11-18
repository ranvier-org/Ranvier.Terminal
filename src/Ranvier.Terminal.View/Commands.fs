namespace Ranvier.Terminal.View

open System
open Ranvier
open Ranvier.Keyboard
open Ranvier.Terminal

module Commands =
    type Command =
        { Key: Key
          Scalar: int
          Modifiers: Modifiers
          Shortcut: string
          Caption: string
          Enabled: unit -> bool
          Invoke: unit -> unit }

    type Binding =
        { Chord: KeyChord
          Shortcut: string
          Caption: string
          Enabled: unit -> bool
          Invoke: unit -> unit }

    let private binding (command: Command) =
        let key = { Key = command.Key; Scalar = command.Scalar; Modifiers = command.Modifiers; Released = false; Paste = false }
        { Chord = key.Chord; Shortcut = command.Shortcut; Caption = command.Caption
          Enabled = command.Enabled; Invoke = command.Invoke }

    let dispatchBindings (commands: Binding array) = function
        | Key key when not key.Released && not key.Paste ->
            let chord = key.Chord
            let matching command = chord = command.Chord && command.Enabled()
            match Array.tryFind matching commands with
            | Some command -> command.Invoke(); true
            | None -> false
        | _ -> false

    let dispatch (commands: Command array) = function
        | Key key when not key.Released && not key.Paste ->
            let matching command =
                key.Key = command.Key && key.Modifiers = command.Modifiers &&
                (key.Key <> Key.Character || key.Scalar = command.Scalar) && command.Enabled()
            match Array.tryFind matching commands with
            | Some command -> command.Invoke(); true
            | None -> false
        | _ -> false

    let private label (command: Binding) = command.Shortcut + " " + command.Caption

    let bindingHelp declarations commands expanded =
        let node = Node.create declarations [||]
        createEffect (fun () ->
            commands |> Array.filter (fun command -> command.Enabled())
            |> Array.map label |> String.concat (if expanded() then "\n" else "  ·  ")
            |> fun text -> Node.setText text node)
        node

    /// A searchable command editor and its anchored results. Selection leaves the editor ready for another query.
    let bindingPalette editorStyle popupStyle maxRows (commands: Binding array) =
        let query = createSignal ""
        let available =
            commands |> Array.map (fun command -> command, createMemo (fun _ -> command.Enabled()))
        let suggest (text: string) =
            let words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            available |> Array.choose (fun (command, enabled) ->
                if enabled.Value &&
                   words |> Array.forall (fun word -> (label command).Contains(word, StringComparison.OrdinalIgnoreCase))
                then Some command else None)
        let accept command =
            if command.Enabled() then command.Invoke()
            ""
        PopupWidgets.choices editorStyle popupStyle query suggest (fun a b -> obj.ReferenceEquals(a, b)) label accept maxRows

    let help declarations commands expanded = bindingHelp declarations (Array.map binding commands) expanded
    let palette editorStyle popupStyle maxRows commands = bindingPalette editorStyle popupStyle maxRows (Array.map binding commands)
