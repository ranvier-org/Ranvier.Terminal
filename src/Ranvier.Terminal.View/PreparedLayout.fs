namespace Ranvier.Terminal.View

open System
open System.Collections.Generic
open Ranvier.Terminal
open Ranvier.Terminal.Css

type private PreparedNode =
    { Node: Node
      mutable Source: TextRun
      mutable Text: PreparedText
      mutable PreviousSource: TextRun
      mutable PreviousText: PreparedText
      mutable PaintValue: Paint
      mutable Paint: PreparedPaint
      mutable TrackGlyph: PreparedGlyph
      mutable ThumbGlyph: PreparedGlyph
      mutable TrackPaint: PreparedPaint
      mutable ThumbPaint: PreparedPaint
      mutable Children: PreparedNode array }

/// An explicit preparation cache for the existing retained tree and its existing geometry planner.
type PreparedView =
    private
        { Mounted: Mounted
          Renderer: PreparedRenderer
          Paints: Dictionary<Paint,PreparedPaint>
          ScrollGlyphs: Dictionary<string,PreparedGlyph>
          Borders: PreparedGlyph array array
          Plain: PreparedPaint
          mutable Ready: bool
          mutable Root: PreparedNode option }

module PreparedLayout =
    let private effective inherited (node: Node) =
        let paint = node.Computed.Paint
        {paint with
            Foreground=(if node.InheritColors && paint.Foreground=Default then inherited.Foreground else paint.Foreground)
            Background=(if node.InheritColors && paint.Background=Default then inherited.Background else paint.Background)}

    let private paint value cache =
        let mutable handle = Unchecked.defaultof<PreparedPaint>
        if not (cache.Paints.TryGetValue(value,&handle)) then
            handle <- PreparedRenderer.preparePaint value cache.Renderer
            cache.Paints.Add(value,handle)
        handle

    let private registerPaint value cache =
        let handle = paint value cache
        paint {value with Attributes=value.Attributes ^^^ Attributes.Reverse} cache |> ignore
        handle

    let private scrollGlyph (part: ScrollbarPart) cache =
        let mutable glyph = Unchecked.defaultof<PreparedGlyph>
        if not (cache.ScrollGlyphs.TryGetValue(part.Glyph, &glyph)) then
            glyph <- PreparedRenderer.prepareGlyph part.Glyph 1 cache.Renderer
            cache.ScrollGlyphs.Add(part.Glyph, glyph)
        glyph

    let private prepareScrollbars prepared cache =
        let node = prepared.Node
        let style = node.Computed
        if node.VerticalScrollbar || node.HorizontalScrollbar then
            prepared.TrackGlyph <- scrollGlyph style.ScrollbarTrack cache
            prepared.ThumbGlyph <- scrollGlyph style.ScrollbarThumb cache
            prepared.TrackPaint <- registerPaint (Layout.scrollbarPaint prepared.PaintValue style.ScrollbarTrack) cache
            prepared.ThumbPaint <- registerPaint (Layout.scrollbarPaint prepared.PaintValue style.ScrollbarThumb) cache

    let rec private createNode inherited node cache =
        let value = effective inherited node
        let text = PreparedRenderer.prepareRun node.Text cache.Renderer
        let prepared =
            {Node=node;Source=node.Text;Text=text;PreviousSource=node.Text;PreviousText=text
             PaintValue=value;Paint=registerPaint value cache
             TrackGlyph=Unchecked.defaultof<_>;ThumbGlyph=Unchecked.defaultof<_>
             TrackPaint=Unchecked.defaultof<_>;ThumbPaint=Unchecked.defaultof<_>
             Children=node.Children |> Array.map (fun child -> createNode value child cache)}
        prepareScrollbars prepared cache
        prepared

    let rec private refreshNode inherited prepared cache =
        let node = prepared.Node
        let value = effective inherited node
        if not (Paint.equal value prepared.PaintValue) then
            let handle = registerPaint value cache
            prepared.PaintValue <- value
            prepared.Paint <- handle
        prepareScrollbars prepared cache
        if not (Object.ReferenceEquals(node.Text,prepared.Source)) then
            let next =
                if Object.ReferenceEquals(node.Text,prepared.PreviousSource) then prepared.PreviousText
                else PreparedRenderer.prepareRun node.Text cache.Renderer
            prepared.PreviousSource <- prepared.Source
            prepared.PreviousText <- prepared.Text
            prepared.Text <- next
            prepared.Source <- node.Text
        if node.Children.Length <> prepared.Children.Length then
            prepared.Children <- node.Children |> Array.map (fun child -> createNode value child cache)
        else
            for i = 0 to node.Children.Length-1 do
                if not (Object.ReferenceEquals(node.Children[i],prepared.Children[i].Node)) then
                    prepared.Children[i] <- createNode value node.Children[i] cache
                else refreshNode value prepared.Children[i] cache

    /// Preparation may allocate and can reject exhausted registration budgets before drawing.
    let refresh force cache =
        cache.Ready <- false
        Layout.plan force cache.Mounted (PreparedRenderer.bounds cache.Renderer)
        match cache.Root with
        | None -> cache.Root <- Some (createNode Paint.empty cache.Mounted.Root cache)
        | Some root -> refreshNode Paint.empty root cache
        cache.Ready <- true

    let create mounted renderer =
        let borderStrings =
            [|[|"┌";"┐";"└";"┘";"─";"│"|]
              [|"╔";"╗";"╚";"╝";"═";"║"|]
              [|"╭";"╮";"╰";"╯";"─";"│"|]
              [|"+";"+";"+";"+";"-";"|"|]|]
        let plain = PreparedRenderer.preparePaint Paint.empty renderer
        let cache =
            {Mounted=mounted;Renderer=renderer;Plain=plain;Ready=false;Paints=Dictionary<Paint,PreparedPaint>()
             ScrollGlyphs=Dictionary<string,PreparedGlyph>()
             Borders=borderStrings |> Array.map (Array.map (fun glyph -> PreparedRenderer.prepareGlyph glyph 1 renderer))
             Root=None}
        registerPaint Paint.empty cache |> ignore
        refresh true cache
        cache

    let rec private drawNode prepared cache =
        let node = prepared.Node
        let style = node.Computed
        let nodeClip = Rect.intersect node.Clip (PreparedRenderer.bounds cache.Renderer)
        if style.Visible && nodeClip.Width > 0 && nodeClip.Height > 0 then
            let r = cache.Renderer
            let bounds = node.Bounds
            let paint = prepared.Paint
            PreparedRenderer.fill nodeClip paint r
            let edge = if style.Border=NoBorder then 0 else 1
            if edge > 0 && bounds.Width >= 2 && bounds.Height >= 2 then
                let border = cache.Borders[match style.Border with Double -> 1 | Rounded -> 2 | Ascii -> 3 | _ -> 0]
                for x = bounds.X+1 to bounds.X+bounds.Width-2 do
                    PreparedRenderer.put nodeClip x bounds.Y border[4] paint r
                    PreparedRenderer.put nodeClip x (bounds.Y+bounds.Height-1) border[4] paint r
                for y = bounds.Y+1 to bounds.Y+bounds.Height-2 do
                    PreparedRenderer.put nodeClip bounds.X y border[5] paint r
                    PreparedRenderer.put nodeClip (bounds.X+bounds.Width-1) y border[5] paint r
                PreparedRenderer.put nodeClip bounds.X bounds.Y border[0] paint r
                PreparedRenderer.put nodeClip (bounds.X+bounds.Width-1) bounds.Y border[1] paint r
                PreparedRenderer.put nodeClip bounds.X (bounds.Y+bounds.Height-1) border[2] paint r
                PreparedRenderer.put nodeClip (bounds.X+bounds.Width-1) (bounds.Y+bounds.Height-1) border[3] paint r
            let content = node.Content
            if node.Focused && node.CaretColumn >= 0 && content.Width > 0 then
                if node.CaretColumn < node.ScrollX then node.ScrollX <- node.CaretColumn
                elif node.CaretColumn >= node.ScrollX+content.Width then node.ScrollX <- node.CaretColumn-content.Width+1
            PreparedRenderer.text (Rect.intersect nodeClip content) (content.X-node.ScrollX) (content.Y-node.ScrollY)
                content.Width style.Wrap style.WordWrap prepared.Text paint r
            for i = node.VisibleStart to node.VisibleEnd do drawNode prepared.Children[i] cache
            Layout.paintScrollbars node (fun x y thumb ->
                if thumb then PreparedRenderer.put nodeClip x y prepared.ThumbGlyph prepared.ThumbPaint r
                else PreparedRenderer.put nodeClip x y prepared.TrackGlyph prepared.TrackPaint r)
            if node.Focused && node.CaretColumn >= 0 then
                let x = content.X+node.CaretColumn-node.ScrollX
                let y = content.Y
                let clip = Rect.intersect nodeClip content
                if Rect.contains x y clip then
                    let cell = PreparedRenderer.cell x y false r
                    let reverse = {cell.Paint with Attributes=cell.Paint.Attributes ^^^ Attributes.Reverse}
                    PreparedRenderer.repaint clip x y cache.Paints[reverse] r

    /// Paint a refreshed tree; no text/style registration or geometry allocation occurs here.
    let draw cache =
        if cache.Mounted.Disposed then invalidOp "The view has been disposed."
        if not cache.Ready then invalidOp "Refresh must complete successfully before drawing."
        match cache.Root with
        | None -> invalidOp "The prepared view has not been refreshed."
        | Some root ->
            PreparedRenderer.clear cache.Plain cache.Renderer
            drawNode root cache

    let present output cache =
        draw cache
        PreparedRenderer.present output cache.Renderer
