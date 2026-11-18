namespace Ranvier.Terminal.View

open System
open Ranvier
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

    let dispatch (commands: Command array) = function
        | Key key when not key.Released && not key.Paste ->
            let matching command =
                key.Key = command.Key && key.Modifiers = command.Modifiers &&
                (key.Key <> Key.Character || key.Scalar = command.Scalar) && command.Enabled()
            match Array.tryFind matching commands with
            | Some command -> command.Invoke(); true
            | None -> false
        | _ -> false

    let private label command = command.Shortcut + " " + command.Caption

    let help declarations commands expanded =
        let node = Node.create declarations [||]
        createEffect (fun () ->
            commands |> Array.filter (fun command -> command.Enabled())
            |> Array.map label |> String.concat (if expanded() then "\n" else "  ·  ")
            |> fun text -> Node.setText text node)
        node

    /// A searchable command editor and its anchored results. Selection leaves the editor ready for another query.
    let palette editorStyle popupStyle maxRows commands =
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
