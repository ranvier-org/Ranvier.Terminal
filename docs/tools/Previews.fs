module Previews

open System
open System.Globalization
open System.IO
open System.Security
open System.Text
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css
open Ranvier.Terminal.View
open Ranvier.Terminal.View.Dsl

let destination = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../static/previews"))

let color fallback = function
    | Rgb value -> sprintf "#%06x" value
    | Default -> fallback
    | Indexed index -> failwithf "Unhandled indexed color %d" index

// Plot Braille and block cells on the terminal grid, including on systems without a Braille font.
let save name title (frame: Frame) top rows =
    let svg = StringBuilder()
    let append (value: string) = svg.AppendLine(value) |> ignore
    let rect x y width height fill =
        append (sprintf "<rect x='%d' y='%d' width='%d' height='%d' fill='%s'/>" x y width height fill)
    append (sprintf "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 %d %d' width='%d' height='%d'>" (8 * frame.Width) (16 * rows) (8 * frame.Width) (16 * rows))
    append (sprintf "<title>%s</title>" (SecurityElement.Escape title))
    append "<g font-family='Consolas,DejaVu Sans Mono,monospace' font-size='13' xml:space='preserve'>"
    let cell x y = frame.Cells[(top + y) * frame.Width + x]
    let colors (paint: Paint) =
        let fg, bg = color "#e6effa" paint.Foreground, color "#0c121d" paint.Background
        if paint.Attributes.HasFlag Attributes.Reverse then bg, fg else fg, bg
    for y = 0 to rows - 1 do
        let mutable x = 0
        while x < frame.Width do
            let _, bg = colors (cell x y).Paint
            let start = x
            x <- x + 1
            while x < frame.Width && snd (colors (cell x y).Paint) = bg do x <- x + 1
            rect (8 * start) (16 * y) (8 * (x - start)) 16 bg
    for y = 0 to rows - 1 do
        let mutable x = 0
        while x < frame.Width do
            let current = cell x y
            let glyph = current.Glyph
            let fg, _ = colors current.Paint
            let px, py = x * 8, y * 16
            if glyph.Length = 1 && glyph[0] >= '⠀' && glyph[0] <= '⣿' then
                let mask = int glyph[0] - 0x2800
                for i, (dx, dy) in Array.indexed [|0,0;0,1;0,2;1,0;1,1;1,2;0,3;1,3|] do
                    if mask &&& (1 <<< i) <> 0 then
                        rect (px + 1 + dx * 4) (py + 1 + dy * 4) 2 2 fg
            elif glyph = "─" then
                let start = x
                while x + 1 < frame.Width && (cell (x + 1) y).Glyph = glyph && (cell (x + 1) y).Paint = current.Paint do x <- x + 1
                rect px (py + 8) (8 * (x - start + 1)) 1 fg
            elif glyph = "│" then rect (px + 4) py 1 16 fg
            elif glyph = "├" || glyph = "┤" then
                rect (px + 4) py 1 16 fg
                rect (if glyph = "├" then px + 4 else px) (py + 8) 4 1 fg
            elif [ "╭"; "╮"; "╰"; "╯" ] |> List.contains glyph then
                let path =
                    match glyph with
                    | "╭" -> "M4.5 16V12Q4.5 8.5 8 8.5"
                    | "╮" -> "M0 8.5Q4.5 8.5 4.5 12V16"
                    | "╰" -> "M4.5 0V5Q4.5 8.5 8 8.5"
                    | _ -> "M0 8.5Q4.5 8.5 4.5 5V0"
                append (sprintf "<path transform='translate(%d %d)' d='%s' stroke='%s' fill='none'/>" px py path fg)
            elif glyph = "▄" then rect px (py + 8) 8 8 fg
            elif glyph.Length = 1 && glyph[0] >= '█' && glyph[0] <= '▏' then
                rect px py (8 - (int glyph[0] - int '█')) 16 fg
            elif current.Width > 0uy && glyph <> " " then
                let run = StringBuilder(glyph)
                let mutable width = int current.Width
                while x + width < frame.Width &&
                      (let next = cell (x + width) y
                       next.Width = 1uy && next.Paint = current.Paint && next.Glyph.Length = 1 &&
                       next.Glyph[0] >= ' ' && next.Glyph[0] <= '~') do
                    run.Append((cell (x + width) y).Glyph) |> ignore
                    width <- width + 1
                let content = run.ToString().TrimEnd()
                let cells = width - (run.Length - content.Length)
                let weight = if current.Paint.Attributes.HasFlag Attributes.Bold then "bold" else "normal"
                append (sprintf "<text x='%d' y='%d' fill='%s' font-weight='%s' textLength='%d' lengthAdjust='spacingAndGlyphs'>%s</text>" px (py + 13) fg weight (8 * cells) (SecurityElement.Escape content))
                x <- x + width - 1
            x <- x + 1
    append "</g></svg>"
    Directory.CreateDirectory destination |> ignore
    File.WriteAllText(Path.Combine(destination, name + ".svg"), svg.ToString().ReplaceLineEndings("\n"))

let saveMeterScene (frame: Frame) top checkboxRow (model: Showcase.Model) mounted =
    let palette = ResizeArray<string>()
    let rows = ResizeArray<string array>()
    let rowIds = Collections.Generic.Dictionary<string * string,int>()
    let idForPaint (paint: Paint) =
        let fg, bg = color "#e6effa" paint.Foreground, color "#0c121d" paint.Background
        let fg, bg = if paint.Attributes.HasFlag Attributes.Reverse then bg, fg else fg, bg
        let style = sprintf "color:%s;background-color:%s;font-weight:%s" fg bg (if paint.Attributes.HasFlag Attributes.Bold then "bold" else "normal")
        match palette.IndexOf style with
        | -1 -> palette.Add style; palette.Count - 1
        | index -> index
    let row y =
        let cells = frame.Cells[y * frame.Width .. (y + 1) * frame.Width - 1]
        let glyphs = cells |> Array.map _.Glyph |> String.concat ""
        let paints = cells |> Array.map (fun c -> char (65 + idForPaint c.Paint)) |> String
        if glyphs.Length <> frame.Width then failwith "The meter scene requires single-column glyphs."
        match rowIds.TryGetValue((glyphs, paints)) with
        | true, index -> index
        | _ ->
            let index = rows.Count
            rows.Add [|glyphs; paints|]
            rowIds.Add((glyphs, paints), index)
            index
    let height = checkboxRow - top + 2
    let frames = Array.init 64 (fun _ ->
        Showcase.Model.tick (2. * Math.PI / 64.) model
        Layout.render mounted frame
        Array.init height (fun y -> row (top + y)))
    let mouse action x y =
        View.dispatch (Mouse { Action = action; X = x; Y = y; Button = 0; Delta = 0; Modifiers = Modifiers.None }) mounted |> ignore
    let rec findBox (n: Node) =
        if n.Focusable && n.Text.Source.Contains("Animate examples") then Some n
        else Array.tryPick findBox n.Children
    let box = findBox mounted.Root |> Option.get
    let states = [| for running in [true;false] do
                        if model.Animate.Peek <> running then
                            mouse MouseAction.Press box.Bounds.X checkboxRow
                            mouse MouseAction.Release box.Bounds.X checkboxRow
                        if model.Animate.Peek <> running then failwith "Showcase checkbox did not toggle."
                        for focused in [false;true] do
                            for hovered in [false;true] do
                                View.focus (if focused then Some box else None) mounted
                                mouse MouseAction.Move (if hovered then box.Bounds.X else 0) checkboxRow
                                Layout.render mounted frame
                                yield row checkboxRow |]
    let scene = {| width = frame.Width; height = height; frames = frames; rows = rows; palette = palette
                   checkbox = checkboxRow-top; x = box.Bounds.X; w = box.Bounds.Width; states = states |}
    let json = System.Text.Json.JsonSerializer.Serialize scene
    File.WriteAllText(Path.Combine(destination,"../showcase-frames.js"), "window.ranvierShowcase=" + json + ";\n")

let components () =
    let width, height = 48, 44
    use graph = new Graph()
    let model = graph.Run Showcase.Model.create
    model.Columns.Value <- width
    model.Phase.Value <- 0.625
    model.Elapsed.Value <- 0.375
    let view = View.mount graph (fun () ->
        column [ Css.color Showcase.Theme.bright; Css.backgroundColor Showcase.Theme.background ] {
            Showcase.Components.build model
        })
    view.Rules <- Showcase.Theme.rules
    let frame = Frame.create width height
    let render () = Layout.render view frame
    let send key scalar =
        View.dispatch (Key { Key = key; Scalar = scalar; Modifiers = Modifiers.None; Released = false; Paste = false }) view |> ignore
    let next () =
        Showcase.Program.press "component-group" view
        send Key.Down 0
        send Key.Enter 0
        render ()
    let crop name title heading before rows =
        let lines = Array.init height (fun y ->
            frame.Cells[y * width .. (y + 1) * width - 1] |> Array.map _.Glyph |> String.concat "")
        let first =
            lines |> Array.tryFindIndex (fun line -> line.Contains(heading: string))
            |> Option.defaultWith (fun () -> failwithf "Missing %s in frame:\n%s" heading (String.concat "\n" lines))
        save name title frame (first - before) rows
    try
        render ()
        crop "indicators" "Spinner frames from the component gallery" "SPINNER" 1 10
        next ()
        crop "meters" "Two-color and fractional meter styles" "TWO-COLOR METERS" 1 15
        let findRow (caption: string) =
            [0..height-1] |> List.find (fun y ->
                frame.Cells[y*width .. (y+1)*width-1] |> Array.map _.Glyph |> String.concat "" |> fun s -> s.Contains caption)
        saveMeterScene frame (findRow "TWO-COLOR METERS" - 1) (findRow "Animate examples") model view
        next ()
        next ()
        let editor = Showcase.Program.find "gallery-completion" view.Root |> Option.get
        let rec popupHost (node: Node) =
            if node.PopupHost then node else popupHost (Option.get node.Parent)
        // Leave room below the editor so the native caret-anchored menu can open downwards.
        popupHost editor |> Dsl.styled [Css.height (Cells 20)] |> ignore
        render ()
        View.focus (Some editor) view
        send Key.Character (int 'A')
        send Key.Down 0
        render ()
        crop "popups" "Caret completion for A with Azure highlighted" "CARET COMPLETION" 1 7
        send Key.Escape 0
        for _ = 1 to 5 do next ()
        crop "tables" "Keyed sortable tree table" "KEYED SORTABLE TABLE" 1 9
    finally
        View.dispose view
        Showcase.Model.finish model

CultureInfo.CurrentCulture <- CultureInfo.InvariantCulture
components ()
