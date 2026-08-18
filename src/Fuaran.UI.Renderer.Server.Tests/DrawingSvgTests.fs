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
      MarkId = None
      Rotation = None
      Tip = None }

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

          // ── Phase 921 — the root's ANNOUNCED accessible name ──
          //
          // `role="img"` presents the drawing as one graphic and does not
          // traverse into it, and `<desc>` is not uniformly mapped to the
          // accessible description (Chromium has never exposed it). So the
          // description is announced by `aria-label`, which every assistive
          // technology reads unconditionally — the pattern a labelled
          // `Display.Icon` already uses on this renderer.

          test "a described root carries aria-label composing title + description (Phase 921)" {
              let svg =
                  render
                      { drawing [] (Some(TextSource.Literal "Sales vs target")) with
                          Description = Some(TextSource.Literal "Bar chart. 2 series: sales, target.") }

              Expect.equal
                  svg
                  ("<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\" "
                   + "aria-label=\"Sales vs target. Bar chart. 2 series: sales, target.\">"
                   + "<title>Sales vs target</title>"
                   + "<desc>Bar chart. 2 series: sales, target.</desc></svg>")
                  "title, terminated, then the description — and both children survive unchanged"
          }

          test "the title is terminated only when it needs to be (Phase 921)" {
              let withDesc (t: string) =
                  render
                      { drawing [] (Some(TextSource.Literal t)) with
                          Description = Some(TextSource.Literal "D.") }

              Expect.isTrue
                  (contains "aria-label=\"Ends in a period. D.\"" (withDesc "Ends in a period."))
                  "period kept"

              Expect.isTrue (contains "aria-label=\"Really? D.\"" (withDesc "Really?")) "question mark kept"

              Expect.isTrue (contains "aria-label=\"Now! D.\"" (withDesc "Now!")) "exclamation kept"

              Expect.isTrue (contains "aria-label=\"Plain. D.\"" (withDesc "Plain")) "otherwise a period is added"

              // An EMPTY title contributes nothing rather than a bare period.
              Expect.isTrue
                  (contains "aria-label=\"D.\"" (withDesc ""))
                  "an empty title is not terminated into punctuation"
          }

          test "a title-only root is byte-identical to pre-921 (Phase 921)" {
              // The `<title>` child is already the accessible name, so there is
              // nothing for `aria-label` to add — and every drawing authored
              // before this phase keeps its exact bytes.
              let svg = render (drawing [] (Some(TextSource.Literal "Bars")))

              Expect.equal
                  svg
                  "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\"><title>Bars</title></svg>"
                  "no description ⇒ no aria-label"

              Expect.isFalse (contains "aria-label" (render (drawing [] None))) "…and none on a bare root either"
          }

          test "a description-only root announces the description alone (Phase 921)" {
              let svg =
                  render
                      { drawing [] None with
                          Description = Some(TextSource.Literal "One filled circle.") }

              Expect.equal
                  svg
                  ("<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\" aria-label=\"One filled circle.\">"
                   + "<desc>One filled circle.</desc></svg>")
                  "no title to compose in front"
          }

          test "hostile title/description text is inert in the aria-label ATTRIBUTE (Phase 921)" {
              // The builder emits raw markup, so its own XML escape is the whole
              // defence — and an attribute value needs the quote entities the
              // element-content path also emits. The chart lowering feeds this
              // seam untrusted series and category strings straight off the data
              // feed (SANITIZATION.md).
              let svg =
                  render
                      { drawing [] (Some(TextSource.Literal "a\"b")) with
                          Description = Some(TextSource.Literal "<script>alert('x') & \"y\"</script>") }

              Expect.isTrue
                  (contains
                      ("aria-label=\"a&quot;b. &lt;script&gt;alert(&#39;x&#39;) &amp; &quot;y&quot;&lt;/script&gt;\"")
                      svg)
                  "every metacharacter is entity-escaped inside the attribute"

              Expect.isFalse (contains "<script>" svg) "no raw markup survives anywhere"
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

          test "Label rotation emits transform=rotate anchored at the label position (Phase 877)" {
              let rotated deg = { noStyle with Rotation = Some deg }

              // The pivot is the label's own (x, y) — not the viewBox origin —
              // so `TextAnchor` keeps its meaning in the rotated frame.
              let svg =
                  render (drawing [ Shape.Label(40.0, 12.0, TextSource.Literal "Q1", rotated -30.0) ] None)

              Expect.isTrue
                  (contains
                      "<text class=\"fuaran-drawing-label\" x=\"40\" y=\"12\" transform=\"rotate(-30 40 12)\">Q1</text>"
                      svg)
                  "rotate(θ x y) anchored at the label position"

              // Numbers go through `formatNum`, so a whole angle drops its
              // decimal and a fractional one keeps the invariant shortest form —
              // the same rule the coordinates use, culture-independent.
              let frac =
                  render (drawing [ Shape.Label(5.5, 2.25, TextSource.Literal "T", rotated 12.34) ] None)

              Expect.isTrue (contains "transform=\"rotate(12.34 5.5 2.25)\"" frac) "fractional angle canonical form"

              // An explicit 0° is PRESENT — it must still emit, because absent
              // and zero are different wire shapes and the renderer must not
              // re-introduce the conflation the codec is careful to avoid.
              let zero =
                  render (drawing [ Shape.Label(1.0, 2.0, TextSource.Literal "T", rotated 0.0) ] None)

              Expect.isTrue (contains "transform=\"rotate(0 1 2)\"" zero) "explicit zero still emits"

              // Absent rotation emits no transform at all — the byte-unchanged
              // guarantee for every pre-877 drawing.
              let upright =
                  render (drawing [ Shape.Label(1.0, 2.0, TextSource.Literal "T", noStyle) ] None)

              Expect.isFalse (contains "transform=" upright) "no rotation ⇒ no transform attribute"
          }

          test "Rotation on a non-Label shape is inert (Phase 877)" {
              // The Phase 528.1 text fields are documented as ignored off
              // `Label`. For rotation that is load-bearing rather than cosmetic:
              // an SVG `transform` on a <rect> would MOVE GEOMETRY, so emitting
              // it there would make this the one text field with side-effects
              // elsewhere. The emitter never writes it off `Label`.
              let svg =
                  render (
                      drawing
                          [ Shape.Rectangle(0.0, 0.0, 10.0, 10.0, Option.None, { noStyle with Rotation = Some 45.0 })
                            Shape.Circle(5.0, 5.0, 2.0, { noStyle with Rotation = Some 45.0 }) ]
                          None
                  )

              Expect.isFalse (contains "transform=" svg) "rotation ignored on non-text shapes"
          }

          test "Tip emits a <title> child on EVERY shape kind (Phase 883)" {
              let tipped t =
                  { noStyle with
                      Tip = Some(TextSource.Literal t) }

              // A tipped shape can no longer self-close — `<title>` is a CHILD.
              // Each arm must name its own closing tag, and getting that wrong
              // produces markup that still parses somewhere (a browser recovers
              // from a mismatched close), so pin every one by exact bytes.
              let svg =
                  render (
                      drawing
                          [ Shape.Rectangle(0.0, 0.0, 10.0, 10.0, Option.None, tipped "rect")
                            Shape.Line(0.0, 0.0, 1.0, 1.0, tipped "line")
                            Shape.Polyline([ { X = 0.0; Y = 0.0 } ], tipped "polyline")
                            Shape.Polygon([ { X = 0.0; Y = 0.0 } ], tipped "polygon")
                            Shape.Curve([ CurveCommand.Close ], tipped "curve")
                            Shape.Circle(5.0, 5.0, 2.0, tipped "circle")
                            Shape.Ellipse(5.0, 5.0, 3.0, 2.0, tipped "ellipse") ]
                          None
                  )

              Expect.isTrue
                  (contains
                      "<rect class=\"fuaran-drawing-rect\" x=\"0\" y=\"0\" width=\"10\" height=\"10\"><title>rect</title></rect>"
                      svg)
                  "rect"

              Expect.isTrue
                  (contains
                      "<line class=\"fuaran-drawing-line\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><title>line</title></line>"
                      svg)
                  "line"

              Expect.isTrue
                  (contains
                      "<polyline class=\"fuaran-drawing-polyline\" points=\"0,0\" fill=\"none\"><title>polyline</title></polyline>"
                      svg)
                  "polyline"

              Expect.isTrue
                  (contains
                      "<polygon class=\"fuaran-drawing-polygon\" points=\"0,0\"><title>polygon</title></polygon>"
                      svg)
                  "polygon"

              Expect.isTrue
                  (contains "<path class=\"fuaran-drawing-curve\" d=\"Z\" fill=\"none\"><title>curve</title></path>" svg)
                  "curve"

              Expect.isTrue
                  (contains
                      "<circle class=\"fuaran-drawing-circle\" cx=\"5\" cy=\"5\" r=\"2\"><title>circle</title></circle>"
                      svg)
                  "circle"

              Expect.isTrue
                  (contains
                      "<ellipse class=\"fuaran-drawing-ellipse\" cx=\"5\" cy=\"5\" rx=\"3\" ry=\"2\"><title>ellipse</title></ellipse>"
                      svg)
                  "ellipse"

              // A `<title>` must be the FIRST child to be the accessible name —
              // on a Group it precedes the nested shapes, on a Label it precedes
              // the visible run.
              let container =
                  render (
                      drawing
                          [ Shape.Group([ Shape.Circle(5.0, 5.0, 2.0, noStyle) ], tipped "group")
                            Shape.Label(1.0, 2.0, TextSource.Literal "visible", tipped "label") ]
                          None
                  )

              Expect.isTrue
                  (contains
                      "<g class=\"fuaran-drawing-group\"><title>group</title><circle class=\"fuaran-drawing-circle\" cx=\"5\" cy=\"5\" r=\"2\"/></g>"
                      container)
                  "group title precedes the nested shapes"

              Expect.isTrue
                  (contains
                      "<text class=\"fuaran-drawing-label\" x=\"1\" y=\"2\"><title>label</title>visible</text>"
                      container)
                  "label title precedes the visible run"
          }

          test "an untipped drawing is byte-unchanged — shapes still self-close (Phase 883)" {
              // The whole additive guarantee: `Tip = None` must leave the pre-883
              // bytes alone, self-closing tags included.
              let svg =
                  render (
                      drawing
                          [ Shape.Rectangle(0.0, 0.0, 10.0, 10.0, Option.None, noStyle)
                            Shape.Circle(5.0, 5.0, 2.0, noStyle) ]
                          None
                  )

              Expect.equal
                  svg
                  ("<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\"0 0 200 100\">"
                   + "<rect class=\"fuaran-drawing-rect\" x=\"0\" y=\"0\" width=\"10\" height=\"10\"/>"
                   + "<circle class=\"fuaran-drawing-circle\" cx=\"5\" cy=\"5\" r=\"2\"/>"
                   + "</svg>")
                  "no tip ⇒ byte-identical self-closing output"

              Expect.isFalse (contains "<title>" svg) "no tip ⇒ no title element anywhere"
          }

          test "Tip text is XML-escaped — the untrusted-category defence (Phase 883)" {
              // The chart lowering builds a tip out of series + category strings
              // taken straight off a data feed, and the builder emits RAW markup
              // (it rides `dangerouslySetInnerHTML`), so this escape is the whole
              // defence. All five escapable characters, in one string.
              let hostile = "<script>alert(\"x\") & 'y'</script>"

              let svg =
                  render (
                      drawing
                          [ Shape.Rectangle(
                                0.0,
                                0.0,
                                1.0,
                                1.0,
                                Option.None,
                                { noStyle with
                                    Tip = Some(TextSource.Literal hostile) }
                            ) ]
                          None
                  )

              Expect.isTrue
                  (contains "<title>&lt;script&gt;alert(&quot;x&quot;) &amp; &#39;y&#39;&lt;/script&gt;</title>" svg)
                  "hostile tip text is fully entity-escaped"

              Expect.isFalse (contains "<script>" svg) "no live script tag survives"
          }

          test "an explicitly EMPTY tip still emits its title element (Phase 883)" {
              // Present-and-empty is a distinct wire shape from absent, and the
              // emitter must not re-introduce the conflation the codec avoids:
              // an omit-when-falsy renderer would make two different trees emit
              // the same bytes. (The chart lowering never AUTHORS an empty tip —
              // `withTip` drops it, because an empty `<title>` suppresses the
              // tooltip and blanks the accessible name — but a decoded tree can
              // carry one and the renderer must be faithful to it.)
              let svg =
                  render (
                      drawing
                          [ Shape.Circle(
                                1.0,
                                1.0,
                                1.0,
                                { noStyle with
                                    Tip = Some(TextSource.Literal "") }
                            ) ]
                          None
                  )

              Expect.isTrue
                  (contains
                      "<circle class=\"fuaran-drawing-circle\" cx=\"1\" cy=\"1\" r=\"1\"><title></title></circle>"
                      svg)
                  "explicit empty tip still emits the element"
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
          }

          // ── Phase 790 — the output ceiling ──────────────────────────────────
          //
          // A Drawing is ONE node, so a tree-size budget never sees the size of
          // the markup it lowers to. The emitter therefore appends THROUGH a
          // budget and abandons the walk at the ceiling; measuring the finished
          // string would not be a bound, it would be a post-mortem.

          test "an over-budget drawing is refused rather than emitted unbounded (Phase 790)" {
              let huge =
                  drawing [ Shape.Polyline([ for i in 1..20000 -> { X = float i; Y = float i } ], noStyle) ] None

              match DrawingSvg.tryRenderWithLimit 2000 BindingResolver.empty textOf huge with
              | Error(DrawingSvg.OutputTooLarge limit) -> Expect.equal limit 2000 "the breached ceiling is reported"
              | Ok svg -> failtestf "expected OutputTooLarge, got %d chars of markup" svg.Length
          }

          test "the shipped render entry point substitutes a bounded refusal SVG (Phase 790)" {
              // Past the DEFAULT ceiling, so this exercises the path a host
              // actually takes. ~150k points is a couple of megabytes of markup.
              let huge =
                  drawing [ Shape.Polyline([ for i in 1..150000 -> { X = float i; Y = float i } ], noStyle) ] None

              let markup = render huge

              Expect.isLessThan markup.Length DrawingSvg.defaultMaxOutputChars "the refusal markup is bounded"

              Expect.isTrue (contains "not rendered" markup) "the refusal says why it is empty"
              Expect.isFalse (contains "<polyline" markup) "no partial geometry is emitted"

              // Phase 921 — and the refusal is ANNOUNCED, not merely present. A
              // reason only a `<desc>` carries is a reason a screen-reader user
              // never hears, which on an empty picture is the whole message.
              Expect.isTrue
                  (contains "aria-label=\"Drawing not rendered:" markup)
                  "the refusal reaches the accessible name too"
          }

          test "an in-budget drawing is unaffected by the ceiling (Phase 790)" {
              let small = drawing [ Shape.Line(0.0, 0.0, 10.0, 10.0, noStyle) ] None

              match DrawingSvg.tryRenderWithLimit 2000 BindingResolver.empty textOf small with
              | Ok svg ->
                  Expect.isTrue (contains "<line" svg) "an in-budget drawing renders normally"
                  Expect.equal svg (render small) "and byte-identically to the unbudgeted default path"
              | Error e -> failtestf "expected Ok for a small drawing, got %A" e
          }

          test "the default output ceiling is finite (Phase 790)" {
              Expect.isLessThan
                  DrawingSvg.defaultMaxOutputChars
                  System.Int32.MaxValue
                  "the default emitted-character ceiling is finite"
          } ]
