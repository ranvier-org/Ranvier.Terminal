module StyledChecks

open Ranvier
open Ranvier.Terminal
open Ranvier.Terminal.View

let run () =
    use graph = new Graph()
    let title, fraction, samples = graph.Run(fun () ->
        createSignal "界", createSignal 0.4, createSignal [| 100.0; 100.0 |])
    let ink, edge = Rgb 0xABCABC, Rgb 0x444444
    let mounted = View.mount graph (fun () ->
        let content = Node.create [] [|
            Styled.meterWith '#' '.' [] 5 ink edge (fun () -> fraction.Value)
            Styled.brailleChart [] 2 1 ink false (fun () -> samples.Value)
            Styled.brailleChart [] 2 1 ink true (fun () -> samples.Value)
        |]
        let glyphs = { Styled.rounded with BottomRight = '+' }
        Styled.panelWith glyphs [] 13 5 edge
            [ 2, ink, (fun () -> title.Value); 8, ink, (fun () -> "zzzzzz") ]
            [ (1, ink, (fun () -> "end")) ] content)
    let frame = Frame.create 13 5
    let cell x y = frame.Cells[y * frame.Width + x]
    let expect x y glyph color =
        let actual = cell x y
        if actual.Glyph <> glyph || actual.Paint.Foreground <> color then
            failwithf "At (%d,%d) expected %A in %A, got %A" x y glyph color actual
    try
        Layout.renderReactive mounted frame |> ignore
        expect 2 0 "界" ink
        if (cell 3 0).Width <> 0uy then failwith "Wide caption lost its continuation cell"
        expect 4 0 "─" edge
        expect 8 0 "z" ink
        expect 12 0 "╮" edge
        expect 2 1 "#" ink
        expect 3 1 "." edge
        expect 1 2 " " ink
        expect 2 2 "⣿" ink
        expect 1 4 "e" ink
        expect 12 4 "+" edge
        graph.Run(fun () ->
            title.Value <- ""
            fraction.Value <- nan
            samples.Value <- [| 25.0; 0.0 |])
        Layout.renderReactive mounted frame |> ignore
        expect 2 0 "─" edge
        expect 1 1 "." edge
        expect 2 2 "⡀" ink
        expect 2 3 "⠁" ink
    finally
        View.dispose mounted
