module Fuaran.UI.Renderer.Server.Tests.DrawingSvgTests

// ============================================================================
//  Phase 525 — the canonical Drawing SVG builder (Fuaran.UI.Renderer.DrawingSvg).
//  Pins the exact inline-SVG bytes the ALL hosts emit: coordinate form, the
//  typed CurveCommand → path `d`, open-shape fill defaults, XML escaping, and
//  the `role="img"` + `<title>`/`<desc>` a11y root. The TS + Python ports are
//  held to this shape.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private noStyle: DrawStyle =
    { Fill = None
      Stroke = None
      StrokeWidth = None
      Opacity = None
      TextAnchor = None
      FontSize = None
      Emphasis = None
      FontFamily = None
      MarkId = None }

let private textOf (t: TextSource) : string =
    match t with
    | TextSource.Literal s -> s
    | _ -> "?"

let private render spec =
    DrawingSvg.render BindingResolver.empty textOf spec

let private drawing shapes title : DrawingSpec =
    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = 200.0
          Height = 100.0 }
      Shapes = shapes
      Style = noStyle
      Title = title
      Description = None }

let private contains (needle: string) (h: string) =
    h.Contains(needle, System.StringComparison.Ordinal)

[<Tests>]
let drawingSvgTests =
    testList
        "Drawing SVG builder (Phase 525)"
        [ test "root carries role=img + viewBox + fuaran-drawing" {
              let svg = render (drawing [] None)

              Expect.isTrue
                  (contains "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\">" svg)
                  "canonical svg root"

              Expect.equal
                  svg
                  "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\"></svg>"
                  "empty drawing"
          }

          test "title + desc render as a11y children" {
              let svg =
                  render
                      { drawing [] (Some(TextSource.Literal "Bars")) with
                          Description = Some(TextSource.Literal "A bar chart") }

              Expect.isTrue (contains "<title>Bars</title>" svg) "title"
              Expect.isTrue (contains "<desc>A bar chart</desc>" svg) "desc"
          }

          test "Rectangle with cornerRadius + resolved style attrs" {
              let styled: DrawStyle =
                  { noStyle with
                      Fill = Some(Binding.Static(Some "#39c"))
                      Stroke = Some(Binding.Static(Some "#123"))
                      StrokeWidth = Some(Binding.Static(Some 1.5))
                      Opacity = Some(Binding.Static(Some 0.9)) }

              let svg =
                  render (drawing [ Shape.Rectangle(10.0, 10.0, 80.0, 40.0, Some 4.0, styled) ] None)

              Expect.isTrue
                  (contains
                      "<rect class=\"fuaran-drawing-rect\" x=\"10\" y=\"10\" width=\"80\" height=\"40\" rx=\"4\" fill=\"#39c\" opacity=\"0.9\" stroke=\"#123\" stroke-width=\"1.5\"/>"
                      svg)
                  "rect with corner radius + Ordinal-ordered style attrs"
          }

          test "Curve lowers the typed command list to a path d" {
              let svg =
                  render (
                      drawing
                          [ Shape.Curve(
                                [ CurveCommand.MoveTo { X = 0.0; Y = 0.0 }
                                  CurveCommand.LineTo { X = 10.0; Y = 10.0 }
                                  CurveCommand.CubicTo(
                                      { X = 20.0; Y = 0.0 },
                                      { X = 30.0; Y = 20.0 },
                                      { X = 40.0; Y = 10.0 }
                                  )
                                  CurveCommand.QuadraticTo({ X = 50.0; Y = 0.0 }, { X = 60.0; Y = 10.0 })
                                  CurveCommand.Close ],
                                noStyle
                            ) ]
                          None
                  )

              Expect.isTrue
                  (contains
                      "<path class=\"fuaran-drawing-curve\" d=\"M0 0 L10 10 C20 0 30 20 40 10 Q50 0 60 10 Z\" fill=\"none\"/>"
                      svg)
                  "curve path d + open-shape fill=none default"
          }

          test "Polyline defaults fill=none; Polygon does not" {
              let pts: DrawPoint list = [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 20.0 } ]
              let poly = render (drawing [ Shape.Polyline(pts, noStyle) ] None)
              let pgon = render (drawing [ Shape.Polygon(pts, noStyle) ] None)

              Expect.isTrue
                  (contains "<polyline class=\"fuaran-drawing-polyline\" points=\"0,0 10,20\" fill=\"none\"/>" poly)
                  "polyline fill=none"

              Expect.isTrue
                  (contains "<polygon class=\"fuaran-drawing-polygon\" points=\"0,0 10,20\"/>" pgon)
                  "polygon no default fill"
          }

          test "Label text style — anchor + font + size + weight (Phase 528.1)" {
              let styled: DrawStyle =
                  { noStyle with
                      Fill = Some(Binding.Static(Some "#111"))
                      TextAnchor = Some TextAnchor.End
                      FontFamily = Some "system-ui, sans-serif"
                      FontSize = Some 16.0
                      Emphasis = Some Emphasis.Loud }

              let svg =
                  render (drawing [ Shape.Label(40.0, 12.0, TextSource.Literal "Title", styled) ] None)

              Expect.isTrue
                  (contains
                      "<text class=\"fuaran-drawing-label\" x=\"40\" y=\"12\" fill=\"#111\" text-anchor=\"end\" font-family=\"system-ui, sans-serif\" font-size=\"16px\" font-weight=\"700\">Title</text>"
                      svg)
                  "text presentation attrs in canonical order"
          }

          test "Label text is XML-escaped" {
              let svg =
                  render (drawing [ Shape.Label(5.0, 5.0, TextSource.Literal "R&D <x> \"q\"", noStyle) ] None)

              Expect.isTrue
                  (contains
                      "<text class=\"fuaran-drawing-label\" x=\"5\" y=\"5\">R&amp;D &lt;x&gt; &quot;q&quot;</text>"
                      svg)
                  "label escaping"
          }

          test "Group nests children under a <g> and passes its style" {
              let svg =
                  render (
                      drawing
                          [ Shape.Group(
                                [ Shape.Circle(5.0, 5.0, 2.0, noStyle) ],
                                { noStyle with
                                    Stroke = Some(Binding.Static(Some "#000")) }
                            ) ]
                          None
                  )

              Expect.isTrue (contains "<g class=\"fuaran-drawing-group\" stroke=\"#000\">" svg) "group open + style"

              Expect.isTrue
                  (contains "<circle class=\"fuaran-drawing-circle\" cx=\"5\" cy=\"5\" r=\"2\"/></g>" svg)
                  "nested circle"
          }

          test "Ellipse + Line canonical shapes" {
              let svg =
                  render (
                      drawing
                          [ Shape.Line(0.0, 0.0, 100.0, 50.0, noStyle)
                            Shape.Ellipse(50.0, 25.0, 30.0, 15.0, noStyle) ]
                          None
                  )

              Expect.isTrue
                  (contains "<line class=\"fuaran-drawing-line\" x1=\"0\" y1=\"0\" x2=\"100\" y2=\"50\"/>" svg)
                  "line"

              Expect.isTrue
                  (contains "<ellipse class=\"fuaran-drawing-ellipse\" cx=\"50\" cy=\"25\" rx=\"30\" ry=\"15\"/>" svg)
                  "ellipse"
          } ]
