namespace Ranvier.Terminal.View

open System
open System.Globalization
open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.Css

module Styled =
    type BorderGlyphs =
        { TopLeft: char
          TopRight: char
          BottomLeft: char
          BottomRight: char
          Horizontal: char
          Vertical: char }

    let rounded =
        { TopLeft = '╭'; TopRight = '╮'; BottomLeft = '╰'; BottomRight = '╯'
          Horizontal = '─'; Vertical = '│' }

    let internal checkGlyph name glyph =
        let standalone =
            match Char.GetUnicodeCategory glyph with
            | UnicodeCategory.Control | UnicodeCategory.Surrogate | UnicodeCategory.Format
            | UnicodeCategory.NonSpacingMark | UnicodeCategory.SpacingCombiningMark
            | UnicodeCategory.EnclosingMark | UnicodeCategory.LineSeparator | UnicodeCategory.ParagraphSeparator -> false
            | _ -> true
        if not standalone || TextRun.defaultWidth (string glyph) <> 1 then
            invalidArg name "A glyph must occupy one terminal column."

    let private size width height =
        [ Css.width (Css.cells width); Css.height (Css.cells height) ]

    let private label declarations value =
        let node = Node.create declarations [||]
        Node.setText value node
        node

    let private edge width color horizontal first last captions =
        let line length =
            label [ Css.color color; Css.height (Css.cells 1) ] (String(horizontal, length))

        let children = ResizeArray<Node>()
        if width > 0 then children.Add(label [ Css.color color ] (string first))
        let mutable position = 1
        let slots =
            captions
            |> List.filter (fun (x, _, _) -> x >= 1 && x < width - 1)
            |> List.sortBy (fun (x, _, _) -> x)
            |> List.toArray
        for index = 0 to slots.Length - 1 do
            let x, ink, read = slots[index]
            let finish =
                if index + 1 = slots.Length then width - 1
                else
                    let next, _, _ = slots[index + 1]
                    next
            if x > position then children.Add(line (x - position))
            let length = finish - x
            if length > 0 then
                let caption = Node.create [ Css.color ink; Css.maxWidth length ] [||]
                createEffect (fun () -> Node.setText (read ()) caption)
                let rule = label [ Css.color color; Css.width (Css.flex 1) ] (String(horizontal, length))
                children.Add(Node.create (Direction Row :: size length 1) [| caption; rule |])
            position <- finish
        if position < width - 1 then children.Add(line (width - 1 - position))
        if width > 1 then children.Add(label [ Css.color color ] (string last))
        Node.create (Direction Row :: size width 1) (children.ToArray())

    /// Caption offsets count terminal columns from the left corner on the indicated edge.
    let panelWith glyphs declarations width height borderColor top bottom content =
        [ glyphs.TopLeft; glyphs.TopRight; glyphs.BottomLeft; glyphs.BottomRight; glyphs.Horizontal; glyphs.Vertical ]
        |> List.iter (checkGlyph "glyphs")
        let width, height = max 0 width, max 0 height
        let children = ResizeArray<Node>()
        if height > 0 then children.Add(edge width borderColor glyphs.Horizontal glyphs.TopLeft glyphs.TopRight top)
        if height > 2 then
            let interiorHeight = height - 2
            let side () =
                let rule = String.concat "\n" (Array.create interiorHeight (string glyphs.Vertical))
                label (Css.color borderColor :: size 1 interiorHeight) rule
            let middle =
                [| if width > 0 then side ()
                   if width > 2 then Node.create (size (width - 2) interiorHeight) [| content |]
                   if width > 1 then side () |]
            children.Add(Node.create (Direction Row :: size width interiorHeight) middle)
        if height > 1 then children.Add(edge width borderColor glyphs.Horizontal glyphs.BottomLeft glyphs.BottomRight bottom)
        Node.create (declarations @ (Direction Column :: size width height)) (children.ToArray())

    let panel declarations width height borderColor top bottom content =
        panelWith rounded declarations width height borderColor top bottom content

    let private clamp maximum value =
        if Double.IsNaN value then 0.0 else Math.Clamp(value, 0.0, maximum)

    /// A two-color block meter. Values run from zero to one.
    let meterWith filledGlyph trackGlyph declarations width filledColor trackColor value =
        checkGlyph "filledGlyph" filledGlyph
        checkGlyph "trackGlyph" trackGlyph
        let width = max 0 width
        let blocks = String(filledGlyph, width)
        let filled = label [ Css.color filledColor ] ""
        let track = label [ Css.color trackColor; Css.width (Css.flex 1) ] (String(trackGlyph, width))
        let mutable previous = -1
        createEffect (fun () ->
            let count = int (clamp 1.0 (value ()) * float width)
            if count <> previous then
                Node.setText (blocks.Substring(0, count)) filled
                previous <- count)
        Node.create (declarations @ (Direction Row :: size width 1)) [| filled; track |]

    let meter declarations width filledColor trackColor value =
        meterWith '▄' '▄' declarations width filledColor trackColor value

    let private dots = [| 1; 2; 4; 64; 8; 16; 32; 128 |]

    /// Plot the trailing percentage samples, two per cell. Inverted plots grow downwards.
    let brailleChart declarations width height color inverted values =
        let width, height = max 0 width, max 0 height
        let rows = Array.init height (fun _ -> Node.create (size width 1) [||])
        let levels = Array.zeroCreate<int> (width * 2)
        let glyphs = Array.zeroCreate<char> width
        createEffect (fun () ->
            let samples: float array = values ()
            let start = samples.Length - levels.Length
            for i = 0 to levels.Length - 1 do
                let percent = if start + i < 0 then 0.0 else clamp 100.0 samples[start + i]
                levels[i] <- int (percent / 100.0 * float (height * 4))
            for y = 0 to height - 1 do
                for x = 0 to width - 1 do
                    let mutable mask = 0
                    for dx = 0 to 1 do
                        for dy = 0 to 3 do
                            let dot = y * 4 + dy
                            let level = if inverted then dot else height * 4 - 1 - dot
                            if level < levels[x * 2 + dx] then mask <- mask ||| dots[dx * 4 + dy]
                    glyphs[x] <- if mask = 0 then ' ' else char (0x2800 + mask)
                if not (rows[y].Text.Source.AsSpan().SequenceEqual(glyphs.AsSpan())) then
                    Node.setText (String glyphs) rows[y])
        Node.create (declarations @ (Css.color color :: Direction Column :: size width height)) rows
