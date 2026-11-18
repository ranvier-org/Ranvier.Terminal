module Ranvier.Terminal.AnsiModel

open System
open Ranvier.Terminal

// Independent interpreter for the output subset used by the encoder.
let apply (output: string) (screen: Frame) =
    let mutable index, x, y = 0, 0, 0
    let mutable paint = Paint.empty
    while index < output.Length do
        if output[index] = '\u001b' then
            let start = index + 2
            index <- start
            while index < output.Length && not (output[index] >= '@' && output[index] <= '~') do index <- index + 1
            if index >= output.Length then failwith "Incomplete escape sequence"
            let parameters = output.Substring(start, index - start)
            match output[index] with
            | 'H' ->
                let p = parameters.Split ';' |> Array.map int
                y <- p[0] - 1
                x <- p[1] - 1
            | 'C' -> x <- x + int parameters
            | 'm' ->
                let p = parameters.Split ';' |> Array.map (fun s -> if s = "" then 0 else int s)
                let mutable i = 0
                while i < p.Length do
                    match p[i] with
                    | 0 -> paint <- Paint.empty
                    | 1 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Bold }
                    | 2 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Dim }
                    | 3 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Italic }
                    | 4 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Underline }
                    | 7 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Reverse }
                    | 9 -> paint <- { paint with Attributes = paint.Attributes ||| Attributes.Strike }
                    | 39 -> paint <- { paint with Foreground = Default }
                    | 49 -> paint <- { paint with Background = Default }
                    | (38 | 48) as channel ->
                        let color =
                            if p[i+1] = 5 then
                                i <- i + 2
                                Indexed(byte p[i])
                            elif p[i+1] = 2 then
                                let rgb = (p[i+2] <<< 16) ||| (p[i+3] <<< 8) ||| p[i+4]
                                i <- i + 4
                                Rgb rgb
                            else failwith "Unsupported color sequence"
                        paint <- if channel = 38 then { paint with Foreground = color } else { paint with Background = color }
                    | other -> failwithf "Unsupported SGR %d" other
                    i <- i + 1
            | 'h' | 'l' when parameters = "?2026" -> ()
            | other -> failwithf "Unsupported escape %c" other
            index <- index + 1
        else
            let glyph = Globalization.StringInfo.GetNextTextElement(output, index)
            let width = TextRun.defaultWidth glyph
            Frame.put (Frame.bounds screen) x y glyph width paint screen
            x <- x + width
            index <- index + glyph.Length
