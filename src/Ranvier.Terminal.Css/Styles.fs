namespace Ranvier.Terminal.Css

open Ranvier.Terminal

[<Struct>]
type Length =
    | Auto
    | Cells of cells: int
    | Percent of percent: int
    | Flex of weight: int

type Direction =
    | Row
    | Column

type Alignment =
    | Start
    | Center
    | End
    | Stretch

type Border =
    | NoBorder
    | Single
    | Double
    | Rounded
    | Ascii

[<Struct>]
type Insets =
    { Left: int
      Top: int
      Right: int
      Bottom: int }

[<Struct>]
type Style =
    { Width: Length
      Height: Length
      MinWidth: int
      MinHeight: int
      MaxWidth: int
      MaxHeight: int
      Direction: Direction
      Align: Alignment
      Justify: Alignment
      Padding: Insets
      Margin: Insets
      Gap: int
      Border: Border
      Paint: Paint
      Wrap: bool
      Visible: bool }

type Declaration =
    | Width of Length
    | Height of Length
    | MinWidth of int
    | MinHeight of int
    | MaxWidth of int
    | MaxHeight of int
    | Direction of Direction
    | Align of Alignment
    | Justify of Alignment
    | Padding of Insets
    | Margin of Insets
    | Gap of int
    | Border of Border
    | Foreground of Color
    | Background of Color
    | Attributes of Attributes
    | Wrap of bool
    | Visible of bool

module Style =
    let zero =
        { Left = 0
          Top = 0
          Right = 0
          Bottom = 0 }

    let defaults =
        { Width = Auto
          Height = Auto
          MinWidth = 0
          MinHeight = 0
          MaxWidth = System.Int32.MaxValue
          MaxHeight = System.Int32.MaxValue
          Direction = Column
          Align = Stretch
          Justify = Start
          Padding = zero
          Margin = zero
          Gap = 0
          Border = NoBorder
          Paint = Paint.empty
          Wrap = false
          Visible = true }

    let apply value (style: Style) =
        match value with
        | Width x -> { style with Width = x }
        | Height x -> { style with Height = x }
        | MinWidth x -> { style with MinWidth = max 0 x }
        | MinHeight x -> { style with MinHeight = max 0 x }
        | MaxWidth x -> { style with MaxWidth = max 0 x }
        | MaxHeight x -> { style with MaxHeight = max 0 x }
        | Direction x -> { style with Direction = x }
        | Align x -> { style with Align = x }
        | Justify x -> { style with Justify = x }
        | Padding x -> { style with Padding = x }
        | Margin x -> { style with Margin = x }
        | Gap x -> { style with Gap = max 0 x }
        | Border x -> { style with Border = x }
        | Foreground x ->
            { style with
                Paint = { style.Paint with Foreground = x } }
        | Background x ->
            { style with
                Paint = { style.Paint with Background = x } }
        | Attributes x ->
            { style with
                Paint = { style.Paint with Attributes = x } }
        | Wrap x -> { style with Wrap = x }
        | Visible x -> { style with Visible = x }

    let create declarations =
        List.fold (fun style declaration -> apply declaration style) defaults declarations

    let merge declarations style =
        List.fold (fun current declaration -> apply declaration current) style declarations

type StylesBuilder() =
    member _.Yield(value: Declaration) = [ value ]
    member _.YieldFrom(values: Declaration list) = values
    member _.Zero() = []
    member _.Combine(first, second) = first @ second
    member _.Delay(body: unit -> Declaration list) = body ()

    member _.For(values: seq<'a>, body: 'a -> Declaration list) =
        values |> Seq.collect body |> Seq.toList

    member _.Run(values: Declaration list) = values

[<AutoOpen>]
module Authoring =
    let styles = StylesBuilder()

module Css =
    let cells value = Cells(max 0 value)
    let percent value = Percent(max 0 value)
    let flex value = Flex(max 1 value)
    let auto = Auto
    let width value = Width value
    let height value = Height value
    let minWidth value = MinWidth value
    let minHeight value = MinHeight value
    let maxWidth value = MaxWidth value
    let maxHeight value = MaxHeight value
    let direction value = Direction value
    let align value = Align value
    let justify value = Justify value
    let gap value = Gap value
    let border value = Border value
    let color value = Foreground value
    let backgroundColor value = Background value
    let attributes value = Attributes value
    let wrap value = Wrap value
    let visible value = Visible value

    let padding value =
        Padding
            { Left = max 0 value
              Top = max 0 value
              Right = max 0 value
              Bottom = max 0 value }

    let paddingXY horizontal vertical =
        Padding
            { Left = max 0 horizontal
              Right = max 0 horizontal
              Top = max 0 vertical
              Bottom = max 0 vertical }

    let margin value =
        Margin
            { Left = max 0 value
              Top = max 0 value
              Right = max 0 value
              Bottom = max 0 value }

    let rgb red green blue =
        if red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255 then
            invalidArg "red" "RGB channels must be bytes."

        Rgb((red <<< 16) ||| (green <<< 8) ||| blue)

type Selector =
    { Id: string option
      Class: string option
      Focused: bool option
      Hovered: bool option }

type Rule =
    { Selector: Selector
      Declarations: Declaration list }

module Rule =
    let all declarations =
        { Selector =
            { Id = None
              Class = None
              Focused = None
              Hovered = None }
          Declarations = declarations }

    let className name declarations =
        { all declarations with
            Selector =
                { Id = None
                  Class = Some name
                  Focused = None
                  Hovered = None } }

    let focus declarations =
        { all declarations with
            Selector =
                { Id = None
                  Class = None
                  Focused = Some true
                  Hovered = None } }
