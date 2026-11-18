namespace Showcase

open Ranvier.Elmish
open Ranvier.Terminal.Css
open Ranvier.Terminal.View.Templates

module ElmishPage =
    type Counter = { Count: int; Previous: int option }
    type Msg = Increment | Undo

    let update message model =
        match message, model.Previous with
        | Increment, _ -> { Count = model.Count + 1; Previous = Some model.Count }
        | Undo, Some previous -> { Count = previous; Previous = None }
        | Undo, None -> model

    let build _ =
        let app = Mvu.create { Count = 0; Previous = None } update
        let caption = app.Select (fun model -> sprintf "Count: %d" model.Count)
        let view =
            column (column.style (Css.gap 1)) {
                "Elmish counter"
                row (row.id "elmish-count") { from (fun () -> caption) }
                button (button.id "elmish-increment", button.onClick (fun () _ -> app.Dispatch Increment)) { "Increment" }
                button (button.id "elmish-undo", button.onClick (fun () _ -> app.Dispatch Undo)) { "Undo last increment" }
            }
        render view ()
