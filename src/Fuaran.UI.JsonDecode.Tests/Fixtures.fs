module Fuaran.UI.JsonDecode.Tests.Fixtures

// ============================================================================
//  Round-trip fixture corpus.
//
//  Every fixture is a `Node<obj>` (storage-shape erasure target) so the
//  encoder-decoder round-trip stays type-compatible (`encodeNode` returns
//  a string regardless of 'Msg; `decodeNode` always returns `Node<obj>`).
//
//  Coverage gate. The fixtures here MUST cover every shipped NodeKind
//  case (and every Spec record under it). If a future phase ships a new
//  NodeKind / Spec, both this file and the round-trip suite update in
//  the same commit per the forward-coupling rule in JsonDecode.fs.
//  The current floor (5 NodeKind cases × N specs):
//   - Layout: Dashboard, Stack, Grid, SplitPanel, Tabs, Card, Stepper, SummaryList,
//     Disclosure
//   - Display: Heading, Markdown, Metric, Badge, Sparkline, Spacer, Callout,
//     Progress, Skeleton, LabelValueRow
//   - Input: Form (Text + Number + RangedNumber + Checkbox + Choice + TextArea
//     fields), Filters (TextFilter + ChoiceFilter), Button, FileUpload, Select
//   - Visualisation: Grid (one Text column), Chart (Line + xField + yFields),
//     Table (one header row + two body rows), Map (one MapMarker)
//   - Custom (single moduleId+componentId+props)
//   - Composite (Dashboard containing a Card containing a Metric — covers
//     the recursive Children path through layoutKindAppender)
// ============================================================================

#nowarn "3261"

open System
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Shared default fragments ────────────────────────────────────────────

let private defaultStyle: SemanticStyle =
    { Tone = ToneVariant.Default
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Role = StyleRole.None
      Voice = FontVoice.Default }

let private node (id: string) (kind: NodeKind<obj>) (accessibility: Accessibility option) : Node<obj> =
    // `State = None` / `Style = None` (Phase 692–694 swap): the pre-swap
    // required empty-state / default-style records were omitted by the encoder,
    // and omission is the `None` shape post-swap — byte-identical corpus.
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = accessibility
      Motion = None
      ExtraAttributes = None }

/// Phase 695 — a sample child reused in two slots of one composite fixture needs
/// a distinct id in each: NodeIds are unique WITHIN a tree (`WIRE_FORMAT.md` §8),
/// and `PreEmitValidate` refuses a tree that repeats one. Sharing the node value
/// is still the point — only the identity varies.
let private withId (id: string) (n: Node<obj>) : Node<obj> = { n with Id = id }

// ─── Display fixtures ────────────────────────────────────────────────────

let private metricSpec: MetricSpec =
    { Label = TextSource.Literal "Revenue"
      Value = Binding.Static(Some 1234.5)
      Format = CellFormat.Currency "GBP"
      Tone = ToneVariant.Brand
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Trend = Some(Binding.Static(Some 0.07))
      TrendFormat = Some(CellFormat.Percent(Some 1))
      Icon = Some "trending-up"
      Subtext = Some(TextSource.Literal "vs last month") }

let metric: Node<obj> = node "metric-1" (NodeKind.Metric(metricSpec)) None

// ─── Numeric divergence-zone fixtures (Phase 117) ────────────────────────
//
// Every other float fixture in the corpus is a small plain-decimal value
// (1234.5, 0.42, 0.07) — exactly the int53 range where JS `String(n)` and
// .NET `Double.ToString("R")` happen to agree. These four push a float through
// the `Binding.Static` / Metric `source` path in the zone where the two
// formats diverge in LAYOUT (the shortest digits are shared; the spelling is
// not), so the cross-host conformance gate + the property fuzzer actually
// exercise the .NET-"R" numeric contract WIRE_FORMAT.md §2 rule 5 mandates:
//   - exp-pos:  1e21                  → "1E+21"                 (high-magnitude scientific)
//   - exp-neg:  1e-7                  → "1E-07"                 (small-magnitude scientific, padded exponent)
//   - 17sig:    0.1 + 0.2             → "0.30000000000000004"   (17 significant digits, fixed)
//   - bigint:   123456789012345680.0  → "1.2345678901234568E+17" (integer > 2^53; JS renders fixed, .NET scientific — the nastiest divergence)
let private metricFloat (id: string) (value: float) : Node<obj> =
    node
        id
        (NodeKind.Metric(
            { metricSpec with
                Value = Binding.Static(Some value)
                Trend = None
                TrendFormat = None }
        ))
        None

let metricFloatExpPos: Node<obj> = metricFloat "metric-float-exp-pos" 1e21
let metricFloatExpNeg: Node<obj> = metricFloat "metric-float-exp-neg" 1e-7

let metricFloat17Sig: Node<obj> =
    metricFloat "metric-float-17sig" 0.30000000000000004

let metricFloatBigInt: Node<obj> =
    metricFloat "metric-float-bigint" 123456789012345680.0

let heading: Node<obj> =
    node
        "heading-1"
        (NodeKind.Heading(
            { Level = 2
              Text = TextSource.Literal "Channel performance"
              Variant = HeadingVariant.Standard }
        ))
        None

/// Phase 147 — a node carrying the new bounded `Role` / `Voice` style
/// vocabulary (non-default), exercising the optional-emit wire path: the
/// `role` / `voice` keys appear on the `style` object only because they
/// differ from `StyleRole.None` / `FontVoice.Default`.
let styleRoleVoice: Node<obj> =
    { node "style-role-voice-1" (NodeKind.Markdown({ Text = TextSource.Literal "Q3 revenue" })) None with
        Style =
            Some
                { defaultStyle with
                    Role = StyleRole.Data
                    Voice = FontVoice.Display } }

let markdown: Node<obj> =
    node "markdown-1" (NodeKind.Markdown({ Text = TextSource.Literal "Updated hourly." })) None

let badge: Node<obj> =
    node
        "badge-1"
        (NodeKind.Badge(
            { Label = TextSource.Literal "Beta"
              Variant = BadgeVariant.Info }
        ))
        None

let link: Node<obj> =
    node
        "link-1"
        (NodeKind.Link(
            { Href = Binding.Static(Some "/about")
              Label = TextSource.Literal "About us"
              Rel = Some "noopener"
              Target = Some "_blank"
              Download = false
              Protection = None }
        ))
        None

let linkProtected: Node<obj> =
    // Phase 812 — the optional `protection` field ("email" on the wire),
    // omitted when absent so every pre-812 tree stays byte-identical.
    node
        "link-protected-1"
        (NodeKind.Link(
            { Href = Binding.Static(Some "mailto:contact@example.com")
              Label = TextSource.Literal "Email us"
              Rel = None
              Target = None
              Download = false
              Protection = Some LinkProtection.Email }
        ))
        None

let image: Node<obj> =
    // Phase 287 — Avatar variant exercises the variant DU; Src round-trips a
    // Binding<string> (sanitised at render, not at wire).
    node
        "image-1"
        (NodeKind.Image(
            { Src = Binding.Static(Some "/avatar.png")
              Alt = TextSource.Literal "User avatar"
              Variant = ImageVariant.Avatar }
        ))
        None

let listDisplay: Node<obj> =
    // Phase 287 — ordered list with two items.
    node
        "list-1"
        (NodeKind.List(
            { Items = [ TextSource.Literal "First"; TextSource.Literal "Second" ]
              Ordered = true }
        ))
        None

let toast: Node<obj> =
    // Phase 289 — Success tone, open + dismissable. Open round-trips a
    // Binding<bool>.
    node
        "toast-1"
        (NodeKind.Toast(
            { Message = TextSource.Literal "Saved"
              Tone = ToneVariant.Success
              Open = Binding.Static(Some true)
              Dismissable = true }
        ))
        None

let codeBlock: Node<obj> =
    // Phase 290 — line numbers on, two highlight lines, copyable. Exercises the
    // int-array `highlightLines` field.
    node
        "code-1"
        (NodeKind.CodeBlock(
            { Code = "let x = 1\nlet y = 2"
              Language = "fsharp"
              LineNumbers = true
              HighlightLines = [ 1; 2 ]
              Copyable = true }
        ))
        None

let math: Node<obj> =
    // Phase 293 — block display LaTeX.
    node
        "math-1"
        (NodeKind.Math(
            { Source = "x^2 + y^2 = z^2"
              Display = MathDisplay.Block }
        ))
        None

let sparkline: Node<obj> =
    node "spark-1" (NodeKind.Sparkline({ Source = Binding.Static(Some [ 1.0; 2.0; 3.0; 2.0; 4.0 ]) })) None

let private bareDrawStyle: DrawStyle =
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

let private styledDraw (fill: string) (stroke: string) : DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static(Some fill))
        Stroke = Some(Binding.Static(Some stroke))
        StrokeWidth = Some(Binding.Static(Some 1.5))
        Opacity = Some(Binding.Static(Some 0.9)) }

/// A `Label` text style exercising the Phase 528.1 fields (anchor / size /
/// weight / font-family) — pins them cross-host in the corpus.
let private labelTextStyle: DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static(Some "#111111"))
        TextAnchor = Some TextAnchor.Middle
        FontSize = Some 14.0
        Emphasis = Some Emphasis.Loud
        FontFamily = Some "system-ui, sans-serif" }

let drawing: Node<obj> =
    // Phase 524 — exercises Group nesting + every Shape case + all CurveCommand
    // variants + typed DrawStyle Bindings (Static colours/widths) + Title/Desc.
    node
        "drawing-1"
        (NodeKind.Drawing(
            { ViewBox =
                { MinX = 0.0
                  MinY = 0.0
                  Width = 200.0
                  Height = 100.0 }
              Shapes =
                [ Shape.Rectangle(10.0, 10.0, 80.0, 40.0, Some 4.0, styledDraw "#3366cc" "#102040")
                  Shape.Line(0.0, 0.0, 200.0, 100.0, bareDrawStyle)
                  Shape.Polyline([ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 20.0 }; { X = 20.0; Y = 5.0 } ], bareDrawStyle)
                  Shape.Polygon(
                      [ { X = 100.0; Y = 10.0 }; { X = 120.0; Y = 30.0 }; { X = 90.0; Y = 40.0 } ],
                      styledDraw "#cc6633" "#402010"
                  )
                  Shape.Curve(
                      [ CurveCommand.MoveTo { X = 0.0; Y = 0.0 }
                        CurveCommand.LineTo { X = 10.0; Y = 10.0 }
                        CurveCommand.CubicTo({ X = 20.0; Y = 0.0 }, { X = 30.0; Y = 20.0 }, { X = 40.0; Y = 10.0 })
                        CurveCommand.QuadraticTo({ X = 50.0; Y = 0.0 }, { X = 60.0; Y = 10.0 })
                        CurveCommand.Close ],
                      bareDrawStyle
                  )
                  Shape.Circle(150.0, 50.0, 20.0, styledDraw "#33aa55" "#0a2010")
                  Shape.Ellipse(50.0, 80.0, 30.0, 15.0, bareDrawStyle)
                  Shape.Label(100.0, 90.0, TextSource.Literal "Revenue", labelTextStyle)
                  Shape.Group(
                      [ Shape.Circle(5.0, 5.0, 2.0, bareDrawStyle)
                        Shape.Line(0.0, 0.0, 10.0, 10.0, bareDrawStyle) ],
                      styledDraw "#999999" "#333333"
                  ) ]
              Style = styledDraw "#ffffff" "#000000"
              Title = Some(TextSource.Literal "Quarterly revenue chart")
              Description = Some(TextSource.Literal "A bar and line chart of revenue by quarter.") }
        ))
        None

let drawingMinimal: Node<obj> =
    // The degenerate drawing — empty shape list, all-default root style, no
    // title/description (the byte-minimal Drawing wire shape).
    node
        "drawing-empty"
        (NodeKind.Drawing(
            { ViewBox =
                { MinX = 0.0
                  MinY = 0.0
                  Width = 100.0
                  Height = 100.0 }
              Shapes = []
              Style = bareDrawStyle
              Title = None
              Description = None }
        ))
        None

let drawingRotatedLabels: Node<obj> =
    // Phase 877 — `Label` rotation. Pins the optional `rotation` field
    // cross-host over the three demands that motivated it, each with a
    // different sign / magnitude / anchor pairing, plus the cases where the
    // encoding is easy to get subtly wrong:
    //
    //   * a tilted category label      — -30°, Middle-anchored (the legibility
    //                                    default of the chart-style doctrine);
    //   * a vertical escalation        — -90°, End-anchored (the crowded case);
    //   * a rotated y-axis title       — +90° (opposite sign — pins that the
    //                                    convention is clockwise, not absolute);
    //   * a 2-dp fractional angle      — 12.34°, exercising the canonical
    //                                    rounding form on the wire (a whole
    //                                    number would let a host that truncates
    //                                    to integers pass by accident);
    //   * an explicit 0°               — PRESENT-and-zero is not the same wire
    //                                    shape as ABSENT, and a host that
    //                                    conflates them (omit-when-falsy) round-
    //                                    trips to different bytes. The whole
    //                                    "absent = byte-unchanged" guarantee
    //                                    rests on that distinction;
    //   * a negative fractional angle  — -0.5°, the sign × fraction combination.
    //
    // The unrotated label at the end keeps an omit-the-field shape in the same
    // fixture, so a host that emits `"rotation":null` fails here rather than
    // silently in some later chart.
    let rot (deg: float) (anchor: TextAnchor) : DrawStyle =
        { labelTextStyle with
            TextAnchor = Some anchor
            Rotation = Some deg }

    node
        "drawing-rotated-labels"
        (NodeKind.Drawing(
            { ViewBox =
                { MinX = 0.0
                  MinY = 0.0
                  Width = 200.0
                  Height = 120.0 }
              Shapes =
                [ Shape.Label(30.0, 100.0, TextSource.Literal "Q1 2026", rot -30.0 TextAnchor.Middle)
                  Shape.Label(70.0, 100.0, TextSource.Literal "Q2 2026", rot -90.0 TextAnchor.End)
                  Shape.Label(8.0, 60.0, TextSource.Literal "Revenue", rot 90.0 TextAnchor.Middle)
                  Shape.Label(110.0, 100.0, TextSource.Literal "Fractional", rot 12.34 TextAnchor.Start)
                  Shape.Label(150.0, 100.0, TextSource.Literal "Explicit zero", rot 0.0 TextAnchor.Middle)
                  Shape.Label(180.0, 100.0, TextSource.Literal "Hairline", rot -0.5 TextAnchor.End)
                  Shape.Label(100.0, 20.0, TextSource.Literal "Upright", labelTextStyle) ]
              Style = bareDrawStyle
              Title = Some(TextSource.Literal "Rotated axis labels")
              Description = None }
        ))
        None

let drawingTippedShapes: Node<obj> =
    // Phase 883 — `DrawStyle.Tip`, the per-mark hover readout an emitter turns
    // into an SVG `<title>` CHILD of the shape's own element. This fixture pins
    // the field cross-host over the things that are easy to get subtly wrong:
    //
    //   * EVERY SHAPE, not just `Label`. Unlike the Phase 528.1 text cluster (and
    //     unlike `Rotation`, which would move geometry off `Label`), a tip is
    //     meaningful and inert on every shape — the marks a reader hovers are
    //     bars, wedges and points. Rectangle / Circle / Curve / Polyline / Group
    //     / Label all carry one here, so a host that wired the field into the
    //     Label arm alone fails on the second shape rather than silently in some
    //     later chart.
    //   * A LITERAL AND A BOUND tip. The slot is a full `TextSource`, so the
    //     canonical bare-string `Literal` form AND the tagged `Bound` envelope
    //     both have to survive; a host that special-cased "tip is a string"
    //     fails on the second.
    //   * THE MIDDLE-DOT SEPARATOR (U+00B7) in the chart lowering's own
    //     "Series · Category · value" shape — a non-ASCII character in a slot
    //     every host escapes and re-encodes.
    //   * HOSTILE TEXT. The tip is written into XML TEXT CONTENT and its source
    //     is a category string straight off an untrusted data feed, so the
    //     fixture carries a would-be script tag with all five escapable
    //     characters (`& < > " '`). This is a CODEC fixture, so what it pins is
    //     that the round-trip does not mangle those characters; the ESCAPING is
    //     the emitter's obligation and is tested in each host's renderer suite.
    //   * AN EXPLICITLY EMPTY tip. Present-and-empty is not the same wire shape
    //     as ABSENT, and `if (tip)` / `if tip:` is the natural — and wrong — test
    //     in the JavaScript and Python hosts. A host that omits it round-trips to
    //     different bytes, exactly as an omitted explicit `rotation: 0` would.
    //
    // The untipped shape at the end keeps an omit-the-field shape in the same
    // fixture, so a host that emits `"tip":null` fails here.
    let tipped (t: string) : DrawStyle =
        { bareDrawStyle with
            Fill = Some(Binding.Static(Some "#3366cc"))
            Tip = Some(TextSource.Literal t) }

    node
        "drawing-tipped-shapes"
        (NodeKind.Drawing(
            { ViewBox =
                { MinX = 0.0
                  MinY = 0.0
                  Width = 200.0
                  Height = 120.0 }
              Shapes =
                [ Shape.Rectangle(10.0, 40.0, 30.0, 60.0, None, tipped "revenue · Q1 2026 · 1,234,567.89")
                  Shape.Circle(70.0, 60.0, 5.0, tipped "revenue · Q2 2026 · -0.5%")
                  Shape.Curve(
                      [ CurveCommand.MoveTo { X = 100.0; Y = 60.0 }
                        CurveCommand.LineTo { X = 130.0; Y = 60.0 }
                        CurveCommand.Close ],
                      tipped "share · Other · £42.00"
                  )
                  Shape.Polyline(
                      [ { X = 140.0; Y = 20.0 }; { X = 160.0; Y = 80.0 } ],
                      { bareDrawStyle with
                          Stroke = Some(Binding.Static(Some "#cc6633"))
                          // A SERIES-level mark names the series and nothing
                          // else: one element carries the whole line, so a
                          // single `<title>` cannot honestly report one point.
                          Tip = Some(TextSource.Literal "revenue") }
                  )
                  Shape.Group(
                      [ Shape.Circle(170.0, 100.0, 3.0, bareDrawStyle) ],
                      { bareDrawStyle with
                          Tip = Some(TextSource.Bound(Binding.Static(Some "resolved at render time"))) }
                  )
                  Shape.Label(
                      100.0,
                      110.0,
                      TextSource.Literal "Hover me",
                      { labelTextStyle with
                          Tip = Some(TextSource.Literal "<script>alert(\"xss\") & 'done'</script>") }
                  )
                  Shape.Ellipse(
                      30.0,
                      110.0,
                      6.0,
                      3.0,
                      { bareDrawStyle with
                          Tip = Some(TextSource.Literal "") }
                  )
                  Shape.Line(0.0, 0.0, 200.0, 0.0, bareDrawStyle) ]
              Style = bareDrawStyle
              Title = Some(TextSource.Literal "Tipped marks")
              Description = None }
        ))
        None


let skeleton: Node<obj> = node "skel-1" (NodeKind.Skeleton({ Rows = 3 })) None

let callout: Node<obj> =
    node
        "callout-1"
        (NodeKind.Callout(
            { Tone = ToneVariant.Warning
              Heading = Some(TextSource.Literal "Heads up")
              Body = TextSource.Literal "Live data is delayed."
              Icon = Some "alert"
              Dismissable = true }
        ))
        None

let progress: Node<obj> =
    node
        "progress-1"
        (NodeKind.Progress(
            { Fraction = Binding.Static(Some 0.42)
              Label = Some(TextSource.Literal "Loading...")
              Caveat = None
              Indeterminate = false
              Tone = ToneVariant.Brand }
        ))
        None

let labelValueRow: Node<obj> =
    node
        "lvr-1"
        (NodeKind.LabelValueRow(
            { Label = TextSource.Literal "Total"
              Value = Binding.Static(Some 42.0)
              Format = CellFormat.Number(Some 2)
              Emphasis = true
              Help = Some(TextSource.Literal "Last 30 days") }
        ))
        None

let fact: Node<obj> =
    // Exercises the full optional surface (tone / emphasis / help / icon) —
    // the minimal {label,value} form is pinned by the lenient fixture family.
    node
        "fact-1"
        (NodeKind.Fact(
            { Label = TextSource.Literal "Patient"
              Value = TextSource.Literal "Alice Smith"
              Icon = Some "user"
              Tone = ToneVariant.Brand
              Emphasis = true
              Help = Some(TextSource.Literal "Primary insured") }
        ))
        None

/// Phase 819 — a duration Metric: the raw value counts MINUTES (80 → "1h
/// 20m" under Compact), and the trend slot carries the cell-vocabulary
/// RelativeTime parity case (a signed count of the unit).
let metricDuration: Node<obj> =
    node
        "metric-duration-1"
        (NodeKind.Metric(
            { Label = TextSource.Literal "Avg wait"
              Value = Binding.Static(Some 80.0)
              Format = CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact)
              Tone = ToneVariant.Default
              Weight = StyleWeight.Standard
              Emphasis = Emphasis.Normal
              Trend = Some(Binding.Static(Some(-3.0)))
              TrendFormat = Some(CellFormat.RelativeTime RelativeTimeUnit.Minute)
              Icon = None
              Subtext = None }
        ))
        None

/// Phase 821 — the standalone icon-only display kind, decorative form: no
/// label (renderers emit `aria-hidden="true"`), non-default `size` so the
/// optional key appears on the wire, default tone omitted.
let iconDecorative: Node<obj> =
    node
        "icon-1"
        (NodeKind.Icon(
            { Icon = "sparkles"
              Size = IconSize.Large
              Tone = ToneVariant.Default
              Label = None }
        ))
        None

// ─── Layout fixtures ─────────────────────────────────────────────────────

let dashboardEmpty: Node<obj> =
    node
        "dash-empty"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children = [] }
        ))
        None

let stack: Node<obj> =
    node
        "stack-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric; markdown ] }
        ))
        None

let gridLayout: Node<obj> =
    node
        "glayout-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(12, None, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

// Irregular-grid round-trip fixture exercising
// the additive `TemplateColumns` escape. Three sibling fixtures cover
// the canonical CSS-grammar shapes the migration doc names: simple ratio
// `1fr 2fr`, fixed-plus-flex `100px repeat(3, minmax(30px, 1fr))`, and
// auto-fit `repeat(auto-fit, minmax(150px, 1fr))`.
let gridLayoutTemplatedRatio: Node<obj> =
    node
        "glayout-tpl-ratio"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(2, Some "1fr 2fr", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

let gridLayoutTemplatedFixedPlusFlex: Node<obj> =
    node
        "glayout-tpl-fixed"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(4, Some "100px repeat(3, minmax(30px, 1fr))", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

let gridLayoutTemplatedAutoFit: Node<obj> =
    node
        "glayout-tpl-autofit"
        (NodeKind.Box(
            { Layout = BoxLayout.Grid(1, Some "repeat(auto-fit, minmax(150px, 1fr))", None)
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

let splitPanel: Node<obj> =
    node
        "split-1"
        (NodeKind.SplitPanel(
            { Weight = 0.6
              Children = [ metric; markdown ] }
        ))
        None

let tabs: Node<obj> =
    node
        "tabs-1"
        (NodeKind.Tabs(
            { Orientation = Orientation.Horizontal
              Children = [ metric ]
              ActiveIndex = Binding.Static(Some 0)
              // `Some` (Phase 426) — keeps the `"onSelect":"<closure>"`
              // sentinel on the wire, so this fixture stays byte-identical
              // to the pre-426 corpus (the closure-authored shape).
              OnSelect = Some(fun _ -> Action.Chain [])
              TabHeaders = Option.None
              TabTags = Option.None
              ActiveTag = Option.None
              OnSelectTag = Option.None }
        ))
        None

// Tabs with TabHeaders + TabTags + ActiveTag wire
// surface. Exercises the additive optional fields end-to-end. The two
// children are simple Markdown leaves; the headers carry mixed icon /
// disabled state to exercise the full TabHeader record.
let tabsExplicitHeaders: Node<obj> =
    node
        "tabs-explicit-1"
        (NodeKind.Tabs(
            { Orientation = Orientation.Horizontal
              Children = [ markdown; sparkline ]
              // Non-zero ActiveIndex (Phase 126) — exercises the
              // now-carried selected-tab binding round-tripping a value
              // other than the default 0.
              ActiveIndex = Binding.Static(Some 1)
              OnSelect = Some(fun _ -> Action.Chain [])
              TabHeaders =
                Some
                    [ { Label = TextSource.Literal "Overview"
                        Icon = Some "overview-glyph"
                        Disabled = Option.None }
                      { Label = TextSource.Literal "Detail"
                        Icon = Option.None
                        Disabled = Some(Binding.Static(Some false)) } ]
              TabTags = Some [ "overview"; "detail" ]
              ActiveTag = Some(Binding.Static(Some "overview"))
              OnSelectTag = Option.None }
        ))
        None

/// Phase 841 — the composite structural-wrap fixture.
///
/// The rule→fixture coverage matrix (`docs/tools/coverage-matrix.json`) found
/// `idiom:container-in-wrapper` exercised by ZERO fixtures in the whole corpus: no
/// tree anywhere puts a container INSIDE a wrapper, so the commonest screen shape
/// there is — a tabbed view whose panels are cards — had nothing an author could copy,
/// and every wrapper fixture held bare leaves.
///
/// It is deliberately dense rather than minimal: one tree absorbs three thinly-covered
/// classes so it can replace several sparse singles. Containers nested inside a `Tabs`
/// wrapper; an explicit `Grid` box layout beside a `Flex` one; and controls arriving
/// PRE-FILLED — a `Static` value on both the text and the choice field, which is the
/// shape a prompt naming a default asks for and which no existing wrapper fixture
/// shows. Every optional handler slot is omitted: this is the self-wiring emission
/// shape, not the closure-authored one.
let compositeTabsPanels: Node<obj> =
    let nameField: FormField<obj> =
        { Id = "displayName"
          Label = TextSource.Literal "Display name"
          Kind = FormFieldKind.Text(Some(Binding.Static(Some "Ada Lovelace")), Option.None)
          Required = true
          Help = Option.None }

    let themeField: FormField<obj> =
        { Id = "theme"
          Label = TextSource.Literal "Theme"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "light"; Label = "Light" }; { Value = "dark"; Label = "Dark" } ]),
                Some(Binding.Static(Some "dark")),
                Option.None
            )
          Required = true
          Help = Option.None }

    let preferencesForm: Node<obj> =
        node
            "preferences-form"
            (NodeKind.Form(
                { Fields = [ nameField; themeField ]
                  OnSubmit = Action.Call("/api/preferences", Option.None, Option.None)
                  SubmitLabel = TextSource.Literal "Save preferences"
                  Disabled = Option.None }
            ))
            None

    let overviewPanel: Node<obj> =
        node
            "overview-panel"
            (NodeKind.Box(
                { Layout = BoxLayout.Grid(2, Option.None, Some 16)
                  Role = BoxRole.Card
                  Heading = Some(TextSource.Literal "This month")
                  // Sparkline + Badge rather than the fat shared `metric`: this tree is
                  // meant to SUPERSEDE `tabs-explicit-1` as the pack's Tabs exemplar, so
                  // it has to carry that fixture's leaf vocabulary, and Metric is already
                  // exemplified several times over while Sparkline is exemplified once.
                  Children = [ sparkline; badge ] }
            ))
            None

    let settingsPanel: Node<obj> =
        node
            "settings-panel"
            (NodeKind.Box(
                { Layout = BoxLayout.Flex(Orientation.Vertical, false, Some 12)
                  Role = BoxRole.Card
                  Heading = Some(TextSource.Literal "Preferences")
                  Children = [ preferencesForm ] }
            ))
            None

    node
        "composite-tabs-panels"
        (NodeKind.Tabs(
            { Orientation = Orientation.Horizontal
              Children = [ overviewPanel; settingsPanel ]
              ActiveIndex = Binding.Static(Some 0)
              OnSelect = Option.None
              TabHeaders =
                Some
                    [ { Label = TextSource.Literal "Overview"
                        Icon = Some "chart-glyph"
                        Disabled = Option.None }
                      { Label = TextSource.Literal "Settings"
                        Icon = Option.None
                        Disabled = Some(Binding.Static(Some false)) } ]
              TabTags = Some [ "overview"; "settings" ]
              ActiveTag = Some(Binding.Static(Some "overview"))
              OnSelectTag = Option.None }
        ))
        None

let card: Node<obj> =
    node
        "card-1"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "Insights")
              Children = [ metric ] }
        ))
        None

let stepper: Node<obj> =
    node
        "step-1"
        (NodeKind.Stepper(
            { ActiveStep = Binding.Static(Some 1)
              Children = [ markdown; withId "markdown-2" markdown ]
              // `Some` (Phase 692–694 swap) — the slot is optional now; Some
              // keeps the `"onSelect":"<closure>"` sentinel on the wire.
              OnSelect = Some(fun _ -> Action.Chain []) }
        ))
        None

let summaryList: Node<obj> =
    node
        "summary-1"
        (NodeKind.SummaryList(
            { Heading = Some(TextSource.Literal "Stats")
              Children = [ labelValueRow ] }
        ))
        None

let disclosure: Node<obj> =
    // Typed accordion. Round-trips heading + Open binding +
    // DefaultOpen + Children — every encoder-side wire field. `OnToggle = None`
    // (Phase 426): the pre-426 encoder never emitted the handler, so the
    // handler-free shape keeps this fixture byte-identical (a `Some` closure
    // would now add the `"onToggle":"<closure>"` sentinel).
    node
        "discl-1"
        (NodeKind.Disclosure(
            { Heading = TextSource.Literal "Additional entitlements"
              Open = Binding.Static(Some false)
              OnToggle = Option.None
              Children = [ markdown ]
              DefaultOpen = true }
        ))
        None

let modal: Node<obj> =
    // Phase 289 — overlay dialog. Heading (Some) + child + OnDismiss action +
    // Open binding all round-trip; OnDismiss is a wire-survivable Action
    // (Chain []), unlike the renderer-only closures (Tabs.OnSelect). `Some`
    // (Phase 426) keeps the encoded action on the wire, byte-identical.
    node
        "modal-1"
        (NodeKind.Modal(
            { Open = Binding.Static(Some false)
              Heading = Some(TextSource.Literal "Confirm")
              Dismissable = true
              Children = [ markdown ]
              OnDismiss = Some(Action.Chain []) }
        ))
        None

let scrollArea: Node<obj> =
    // Phase 289 — vertical scroll container with a maxHeight bound present and
    // maxWidth absent (exercises the omit-when-None path).
    node
        "scroll-1"
        (NodeKind.ScrollArea(
            { Orientation = ScrollOrientation.Vertical
              Children = [ markdown ]
              MaxHeight = Some 320
              MaxWidth = Option.None }
        ))
        None

// ─── Input fixtures ──────────────────────────────────────────────────────

let private placeholderChain: Action<obj> = Action.Chain []

let formAllFields: Node<obj> =
    let textField: FormField<obj> =
        { Id = "name"
          Label = TextSource.Literal "Name"
          Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain))
          Required = true
          Help = Some(TextSource.Literal "Full legal name") }

    let numberField: FormField<obj> =
        { Id = "age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Number(Some(Binding.Static(Some 0.0)), Some(fun _ -> placeholderChain))
          Required = false
          Help = None }

    let checkboxField: FormField<obj> =
        { Id = "agree"
          Label = TextSource.Literal "I agree"
          Kind = FormFieldKind.Checkbox(Some(Binding.Static(Some false)), Some(fun _ -> placeholderChain))
          Required = true
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "basic"; Label = "Basic" }; { Value = "pro"; Label = "Pro" } ]),
                Some(Binding.Static(Some "basic")),
                Some(fun _ -> placeholderChain)
            )
          Required = false
          Help = None }

    let textareaField: FormField<obj> =
        { Id = "notes"
          Label = TextSource.Literal "Notes"
          Kind = FormFieldKind.TextArea(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain), 5)
          Required = false
          Help = None }

    node
        "form-1"
        (NodeKind.Form(
            { Fields = [ textField; numberField; checkboxField; choiceField; textareaField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              // Phase 130: bound form-level disabled-state (the
              // interactive-state class-fix); exercises the new optional
              // slot in the corpus round-trip.
              Disabled = Some(Binding.State("formBusy", Some false)) }
        ))
        None

/// Round-trip cover for the parallel-additive
/// `FormFieldKind.RangedNumber` case. Exercises every combination of
/// present/absent Min, Max, Step so the canonical-JSON encoder's
/// omit-when-None discipline and the decoder's optional-field branch
/// stay in lockstep.
let formRangedNumber: Node<obj> =
    let allBoundsField: FormField<obj> =
        { Id = "year"
          Label = TextSource.Literal "Year"
          Kind =
            FormFieldKind.RangedNumber(
                Some(Binding.Static(Some 2024.0)),
                Some(fun _ -> placeholderChain),
                Some 1979.0,
                Some 2028.0,
                Some 1.0
            )
          Required = true
          Help = None }

    let minOnlyField: FormField<obj> =
        { Id = "years"
          Label = TextSource.Literal "Years contributed"
          Kind =
            FormFieldKind.RangedNumber(
                Some(Binding.Static(Some 10.0)),
                Some(fun _ -> placeholderChain),
                Some 0.0,
                None,
                None
            )
          Required = false
          Help = None }

    let noBoundsField: FormField<obj> =
        { Id = "amount"
          Label = TextSource.Literal "Amount"
          Kind =
            FormFieldKind.RangedNumber(
                Some(Binding.Static(Some 100.0)),
                Some(fun _ -> placeholderChain),
                None,
                None,
                None
            )
          Required = false
          Help = None }

    node
        "form-ranged"
        (NodeKind.Form(
            { Fields = [ allBoundsField; minOnlyField; noBoundsField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

let filtersBoth: Node<obj> =
    // `Some` closures — the F#-authored shape, byte-identical to pre-Phase-423 (`"onChange":"<closure>"`).
    let textFilter: FilterSpec<obj> =
        { Name = "q"
          Label = TextSource.Literal "Search"
          Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> placeholderChain)) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "all"; Label = "All" } ]),
                Some(Binding.Static(Some "all")),
                Some(fun _ -> placeholderChain)
            ) }

    node "filters-1" (NodeKind.Filters { Items = [ textFilter; choiceFilter ] }) None

/// Declarative chips (Phase 423): every `FilterKind` case with `onChange = None` — the AI-authored
/// shape whose `onChange` field is omitted on the wire, `value` self-reads its own `$filters.<name>`,
/// and `RangeFilter` carries typed `{min,max}` bounds. Proves the omitted-onChange + typed-range wire.
let filtersDeclarative: Node<obj> =
    let textFilter: FilterSpec<obj> =
        { Name = "q"
          Label = TextSource.Literal "Search"
          Kind = FormFieldKind.Text(Some(Binding.Filter("q", None)), None) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "all"; Label = "All" } ]),
                Some(Binding.Filter("tier", None)),
                None
            ) }

    let rangeFilter: FilterSpec<obj> =
        { Name = "age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Range(Some(Binding.Static(Some { Min = 0.0; Max = 100.0 })), None, None, None, None) }

    node "filters-declarative" (NodeKind.Filters { Items = [ textFilter; choiceFilter; rangeFilter ] }) None

/// Round-trip cover for the parallel-additive
/// `FormFieldKind.SegmentedChoice` + `FilterKind.SegmentedFilter` cases.
/// Exercises both orientations so the canonical-JSON encoder's
/// orientation field and the decoder's required-field branch stay in
/// lockstep.
let formSegmentedChoice: Node<obj> =
    let opts: SelectOption list =
        [ { Value = "effective"
            Label = "Effective" }
          { Value = "marginal"
            Label = "Marginal" }
          { Value = "takeHome"
            Label = "Take-home" } ]

    let horizontalField: FormField<obj> =
        { Id = "metric"
          Label = TextSource.Literal "Metric"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static(Some opts),
                Some(Binding.Static(Some "effective")),
                Some(fun _ -> placeholderChain),
                Orientation.Horizontal
            )
          Required = false
          Help = None }

    let verticalField: FormField<obj> =
        { Id = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static(Some [ { Value = "low"; Label = "Low" }; { Value = "high"; Label = "High" } ]),
                Some(Binding.Static None),
                Some(fun _ -> placeholderChain),
                Orientation.Vertical
            )
          Required = true
          Help = None }

    node
        "form-segmented"
        (NodeKind.Form(
            { Fields = [ horizontalField; verticalField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

/// Round-trip cover for `FilterKind.SegmentedFilter`. Parallel
/// to `filtersBoth`'s ChoiceFilter; uses Horizontal orientation.
let filtersSegmented: Node<obj> =
    let segmentedFilter: FilterSpec<obj> =
        { Name = "view"
          Label = TextSource.Literal "View"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static(Some [ { Value = "table"; Label = "Table" }; { Value = "chart"; Label = "Chart" } ]),
                Some(Binding.Static(Some "table")),
                Some(fun _ -> placeholderChain),
                Orientation.Horizontal
            ) }

    node "filters-segmented" (NodeKind.Filters { Items = [ segmentedFilter ] }) None

/// Round-trip cover for the additive `FormFieldKind.Date` case (Phase 288).
/// Exercises all three variants (Date / Time / DateTime) and every
/// present/absent combination of the optional Min / Max / Step constraints so
/// the encoder's omit-when-None discipline and the decoder's optional-field
/// branch stay in lockstep. Values are ISO-8601 strings.
let formDate: Node<obj> =
    let dateField: FormField<obj> =
        { Id = "checkIn"
          Label = TextSource.Literal "Check in"
          Kind =
            FormFieldKind.Date(
                Some(Binding.Static(Some "2026-01-15")),
                Some(fun _ -> placeholderChain),
                DateVariant.Date,
                Some "2026-01-01",
                Some "2026-12-31",
                None
            )
          Required = true
          Help = None }

    let timeField: FormField<obj> =
        { Id = "alarm"
          Label = TextSource.Literal "Alarm"
          Kind =
            FormFieldKind.Date(
                Some(Binding.Static(Some "08:30")),
                Some(fun _ -> placeholderChain),
                DateVariant.Time,
                None,
                None,
                Some 60.0
            )
          Required = false
          Help = None }

    let dateTimeField: FormField<obj> =
        { Id = "meeting"
          Label = TextSource.Literal "Meeting"
          Kind =
            FormFieldKind.Date(
                Some(Binding.Static(Some "2026-03-01T14:00")),
                Some(fun _ -> placeholderChain),
                DateVariant.DateTime,
                None,
                None,
                None
            )
          Required = false
          Help = None }

    node
        "form-date"
        (NodeKind.Form(
            { Fields = [ dateField; timeField; dateTimeField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Book"
              Disabled = Option.None }
        ))
        None

/// Round-trip cover for `FormFieldKind.DateRange` (Phase 725) — the
/// single-control date range. Exercises all three variants and the
/// present/absent constraint combinations, plus the Phase 426 handler-free
/// shape, so the encoder's omit-when-None discipline and the decoder's
/// optional-field branch stay in lockstep. The Static pair rides as the bare
/// `{from, to}` object (the `Range` posture, no `Static` envelope).
let formDateRange: Node<obj> =
    let stayField: FormField<obj> =
        { Id = "stay"
          Label = TextSource.Literal "Stay"
          Kind =
            FormFieldKind.DateRange(
                Some(
                    Binding.Static(
                        Some
                            { From = "2026-03-01"
                              To = "2026-03-08" }
                    )
                ),
                Some(fun _ -> placeholderChain),
                DateVariant.Date,
                Some "2026-01-01",
                Some "2026-12-31",
                None
            )
          Required = true
          Help = None }

    // Handler-free (Phase 426) — `onChange` is omitted on the wire and the
    // renderer writes the changed pair back to the value slot.
    let shiftField: FormField<obj> =
        { Id = "shift"
          Label = TextSource.Literal "Shift"
          Kind =
            FormFieldKind.DateRange(
                Some(Binding.State("shift", Some { From = "08:00"; To = "17:00" })),
                None,
                DateVariant.Time,
                None,
                None,
                Some 900.0
            )
          Required = false
          Help = None }

    let windowField: FormField<obj> =
        { Id = "window"
          Label = TextSource.Literal "Window"
          Kind =
            FormFieldKind.DateRange(
                Some(
                    Binding.Static(
                        Some
                            { From = "2026-03-01T09:00"
                              To = "2026-03-01T17:00" }
                    )
                ),
                Some(fun _ -> placeholderChain),
                DateVariant.DateTime,
                None,
                None,
                None
            )
          Required = false
          Help = None }

    node
        "form-date-range"
        (NodeKind.Form(
            { Fields = [ stayField; shiftField; windowField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Book"
              Disabled = Option.None }
        ))
        None

/// Filter-context cover for `FormFieldKind.DateRange` (Phase 725). The chip's
/// `value` is the exact auto-binding (`Filter(name)`), so it is OMITTED on the
/// wire per the FilterSpec auto-bind rule — and the pair binds ONE filter
/// param, not two, which is the case's reason to exist.
let filtersDateRange: Node<obj> =
    let stayChip: FilterSpec<obj> =
        { Name = "stay"
          Label = TextSource.Literal "Stay"
          Kind = FormFieldKind.DateRange(Some(Binding.Filter("stay", None)), None, DateVariant.Date, None, None, None) }

    node "filters-date-range" (NodeKind.Filters { Items = [ stayChip ] }) None

let button: Node<obj> =
    node
        "btn-1"
        (NodeKind.Button(
            { Label = TextSource.Literal "Refresh"
              OnClick = placeholderChain
              Variant = ButtonVariant.Primary
              Icon = Some "refresh"
              Tooltip = None
              // Phase 129: bound disabled-state — the canonical
              // "disabled while a calc is in flight" shape.
              Disabled = Some(Binding.State("loading", Some false)) }
        ))
        None

// Action.WriteToClipboard fixture for the round-trip suite.
// The canonical share-link copy-button shape: a button whose
// OnClick chains a clipboard write with a follow-on dispatch the model
// listens to. Confirms the encoder/decoder forward-coupling holds for
// the new DU case AND that Chain composes cleanly across renderer-
// native (WriteToClipboard) and renderer-substrate (Dispatch) cases.
let buttonClipboard: Node<obj> =
    node
        "btn-copy-link"
        (NodeKind.Button(
            { Label = TextSource.Literal "Copy share link"
              OnClick =
                Action.Chain
                    [ Action.WriteToClipboard "https://example.com/share/abc123"
                      Action.Dispatch(box "ClipboardCopied") ]
              Variant = ButtonVariant.Secondary
              Icon = None
              Tooltip = None
              Disabled = None }
        ))
        None

// Phase 676 — the JSON-payload actions, with payloads worth testing.
//
// `Notify` / `SetState` / `AiTool` each carry a `JVal` of arbitrary JSON, and
// until now the corpus exercised them ONLY in the reject family (the null cases).
// Nothing pinned what a real payload's bytes look like, so a codec that got
// nesting, key order, escaping or float layout wrong would have passed.
//
// The payload is deliberately awkward, because that is the whole point of the
// fixture: nested object inside an array inside an object, keys authored OUT of
// order (rule 2 sorts them Ordinal on the way out), an empty object and an empty
// array (neither is absence), a whole-valued float and one needing scientific
// layout (rule 5), and strings carrying the two characters JSON must escape plus
// a control character and an astral-plane codepoint (rule 6).
let buttonJsonPayloads: Node<obj> =
    let awkward =
        JObj
            [ "zeta", JStr "last key authored first"
              "alpha", JInt 1
              "nested",
              JArr
                  [ JObj [ "b", JBool true; "a", JFloat 1e-7 ]
                    JArr [ JInt 0; JFloat 3.0 ]
                    JObj []
                    JArr [] ]
              "escapes", JStr "quote\" back\\slash  astral-\U0001F600"
              "float-whole", JFloat 2.0 ]

    node
        "btn-json-payloads"
        (NodeKind.Button(
            { Label = TextSource.Literal "Fire the JSON-payload actions"
              OnClick =
                Action.Chain
                    [ Action.Notify("audit.channel", awkward)
                      Action.SetState("draft", Some(awkward), None)
                      Action.AiTool("summarise", awkward) ]
              Variant = ButtonVariant.Primary
              Icon = None
              Tooltip = None
              Disabled = None }
        ))
        None

// Action.ReadFileBody fixture for the round-trip suite (Phase 136). The
// canonical "read the selected workbook as base64, then dispatch it to the
// model" shape — placed on a Button.OnClick because OnSelect is a closure
// (its body never serialises). Confirms the encoder/decoder forward-coupling
// holds for the new DU case: only `fileRef` (the opaque id) + `encoding`
// cross the wire; the blob handle is absent and `onRead` is the closure
// sentinel.
let buttonReadFile: Node<obj> =
    node
        "btn-read-workbook"
        (NodeKind.Button(
            { Label = TextSource.Literal "Load workbook"
              OnClick =
                Action.ReadFileBody(
                    "workbook-upload:0",
                    None,
                    FileReadEncoding.Base64,
                    Some(fun _ -> box "WorkbookLoaded")
                )
              Variant = ButtonVariant.Primary
              Icon = None
              Tooltip = None
              Disabled = None }
        ))
        None

/// `Action.Call` result targets (Phase 428): a closure-authored `Call` (the
/// `"onResult":"<closure>"` sentinel — previously only generator-covered, now
/// corpus-pinned), a declarative `Call … into State` with a `$state` reader,
/// and a declarative `Call … into Query` with a `Binding.Query` reader — the
/// AI-authorable fetch loop end-to-end on the wire.
let callInto: Node<obj> =
    let closureButton =
        node
            "btn-call-closure"
            (NodeKind.Button(
                { Label = TextSource.Literal "Refresh (closure)"
                  OnClick = Action.Call("/api/refresh", Some(fun _ -> placeholderChain), None)
                  Variant = ButtonVariant.Secondary
                  Icon = None
                  Tooltip = None
                  Disabled = None }
            ))
            None

    let intoStateButton =
        node
            "btn-fetch-total"
            (NodeKind.Button(
                { Label = TextSource.Literal "Fetch total"
                  OnClick = Action.Call("/api/total", None, Some(CallResultTarget.State "total"))
                  Variant = ButtonVariant.Primary
                  Icon = None
                  Tooltip = None
                  Disabled = None }
            ))
            None

    let stateReader =
        node
            "total-metric"
            (NodeKind.Metric(
                { Fuaran.UI.Defaults.metric with
                    Label = TextSource.Literal "Total"
                    Value = Binding.State("total", Some 0.0) }
            ))
            None

    let intoQueryButton =
        node
            "btn-fetch-orders"
            (NodeKind.Button(
                { Label = TextSource.Literal "Fetch orders"
                  OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders"))
                  Variant = ButtonVariant.Primary
                  Icon = None
                  Tooltip = None
                  Disabled = None }
            ))
            None

    let queryReader =
        node
            "orders-metric"
            (NodeKind.Metric(
                { Fuaran.UI.Defaults.metric with
                    Label = TextSource.Literal "Orders"
                    Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), None) }
            ))
            None

    node
        "call-into"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ closureButton; intoStateButton; stateReader; intoQueryButton; queryReader ] }
        ))
        None

// Phase 283 — the Compute layer's invocable-capability seam. A Metric whose Source is a
// `Binding.Invoke` (a host-registered capability dispatched for a value), and a Button whose
// OnClick is an `Action.Invoke` (the effectful sibling). Both carry a capabilityId + scalar
// `(addr, value)` args; the body is never on the wire. Exercises the round-trip of the new
// Binding/Action cases under the canonical `$type` discipline.
let metricInvoke: Node<obj> =
    node
        "metric-invoke"
        (NodeKind.Metric(
            { metricSpec with
                Value =
                    Binding.Invoke(
                        "forecast.revenue",
                        [ { Addr = "horizon"; Value = "12" }; { Addr = "scenario"; Value = "base" } ]
                    )
                Trend = None
                TrendFormat = None }
        ))
        None

let buttonInvoke: Node<obj> =
    node
        "btn-invoke"
        (NodeKind.Button(
            { Label = TextSource.Literal "Run model"
              // qualified — `Action.Invoke` alone resolves to `System.Action.Invoke`.
              OnClick = Fuaran.UI.Types.Action.Invoke("model.score", [ { Addr = "rows"; Value = "all" } ])
              Variant = ButtonVariant.Primary
              Icon = None
              Tooltip = None
              Disabled = None }
        ))
        None

let fileUpload: Node<obj> =
    node
        "upload-1"
        (NodeKind.FileUpload(
            { Label = TextSource.Literal "Upload CSV"
              Accept = [ ".csv"; "text/csv" ]
              Multiple = false
              OnSelect = Some(fun _ -> placeholderChain)
              // Phase 130: bound disabled-state corpus coverage.
              Disabled = Some(Binding.State("uploadBusy", Some false)) }
        ))
        None

let select: Node<obj> =
    node
        "select-1"
        (NodeKind.Select(
            { Label = TextSource.Literal "Region"
              Source = Binding.Static(Some [ { Value = "uk"; Label = "UK" } ])
              Value = Binding.Static(Some "uk")
              // `Some` (Phase 426) — keeps `"onChange":"<closure>"` on the
              // wire, byte-identical to the pre-426 corpus.
              OnChange = Some(fun _ -> placeholderChain)
              Placeholder = Some(TextSource.Literal "Choose one")
              // Phase 130: bound disabled-state corpus coverage.
              Disabled = Some(Binding.State("selectBusy", Some false))
              // Phase 291: single-select — Multiple/Values/OnChangeMulti
              // omitted on the wire (the degenerate case stays byte-stable).
              Multiple = None
              Values = Option.None
              OnChangeMulti = Option.None }
        ))
        None

/// Round-trip cover for the Phase 291 multi-select `Select`. `Multiple = true`
/// + a `Values` `Binding<string list>` (non-empty Static → the `<opaque>`
/// sentinel, mirroring `Source`). `OnChangeMulti = None` (Phase 426): the
/// pre-426 encoder never emitted the multi handler, so the handler-free shape
/// keeps this fixture byte-identical (a `Some` closure would now add the
/// `"onChangeMulti":"<closure>"` sentinel — covered by `multiSelectClosure`).
let multiSelect: Node<obj> =
    node
        "multiselect-1"
        (NodeKind.Select(
            { Label = TextSource.Literal "Tags"
              Source = Binding.Static(Some [ { Value = "red"; Label = "Red" }; { Value = "green"; Label = "Green" } ])
              Value = Binding.Static None
              OnChange = Some(fun _ -> placeholderChain)
              Placeholder = Option.None
              Disabled = Option.None
              Multiple = Some true
              Values = Some(Binding.Static(Some [ "red"; "green" ]))
              OnChangeMulti = Option.None }
        ))
        None

/// Handler-free form (Phase 426 — the control write-back default): every
/// handler is `None` (omitted on the wire) and every value binding is directly
/// `Binding.State`, the shape an AI author emits — the renderer writes each
/// typed change back to the field's own `$state.<key>` slot with zero host
/// code. Proves the omitted-handler wire across Text / Number / Checkbox /
/// Choice.
let formDeclarative: Node<obj> =
    let textField: FormField<obj> =
        { Id = "profile-name"
          Label = TextSource.Literal "Name"
          Kind = FormFieldKind.Text(Some(Binding.State("profileName", Some "")), Option.None)
          Required = true
          Help = None }

    let numberField: FormField<obj> =
        { Id = "profile-age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Number(Some(Binding.State("profileAge", Some 0.0)), Option.None)
          Required = false
          Help = None }

    let checkboxField: FormField<obj> =
        { Id = "profile-agree"
          Label = TextSource.Literal "I agree"
          Kind = FormFieldKind.Checkbox(Some(Binding.State("profileAgree", Some false)), Option.None)
          Required = true
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "profile-tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ { Value = "basic"; Label = "Basic" }; { Value = "pro"; Label = "Pro" } ]),
                Some(Binding.State("profileTier", None)),
                Option.None
            )
          Required = false
          Help = None }

    node
        "form-declarative"
        (NodeKind.Form(
            { Fields = [ textField; numberField; checkboxField; choiceField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

/// Phase 596 — the symmetric-auto-bind minimal form: every field's value IS
/// the exact auto-binding `State(field.Id, typed placeholder)`, so the
// Phase 767 — the CANONICAL empty state with a call to action, authored to
// settle a question rather than to add vocabulary.
//
// 020/c4 (MUST, x2) failed on "the parts read as a single unified empty-state
// element, not three disconnected pieces". The emissions put a `Callout` and a
// `Button` side by side inside a Card `Box`, and the judge was right about the
// render: a Callout carries its OWN chrome, so nesting one inside a Card gives a
// bordered region inside a bordered region with the button outside the inner
// border — two elements, visually.
//
// The fix is NOT a `Callout.actions` field or an `EmptyState` kind. `Box` with
// `role: "Card"` already carries `heading` + `children`, so ONE bordered region
// holding heading, prose and action is expressible today — which is exactly the
// vocabulary charter's §1.2 irreducibility gate ("a shape no combination of
// existing kinds, roles and variants can express"). This composition is that
// combination. A `Callout` is the right kind for a *banner* — a message with no
// action; it is the wrong kind for an actionable empty state.
let emptyStateCard: Node<obj> =
    node
        "empty-state-card"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "No saved searches yet")
              Children =
                [ node
                      "empty-state-note"
                      (NodeKind.Markdown(
                          { Text =
                              TextSource.Literal
                                  "Searches you save will appear here so you can re-run them with one click." }
                      ))
                      None
                  node
                      "empty-state-cta"
                      (NodeKind.Button(
                          { Label = TextSource.Literal "Browse jobs"
                            OnClick = Action.Chain []
                            Variant = ButtonVariant.Primary
                            Icon = Some "search"
                            Tooltip = Option.None
                            Disabled = Option.None }
                      ))
                      None ] }
        ))
        None

// Phase 766 — the boolean TOGGLE affordance, beside a Checkbox so the corpus
// records the distinction the vocabulary now draws. Same data (a bool, the same
// write-back), different control and a11y contract: the toggle renders
// `role="switch"` + `aria-checked`, which a screen reader announces as on/off
// rather than checked.
//
// The demand: 017/c2 x3 + 043/c3 x3 across two unrelated tasks, every sighting
// substituting a `Select` or a `Checkbox` because no switch existed — the
// `contains`/Core#90 fingerprint. `NodeKind.Switch` is the state-bound
// CONDITIONAL and always was; the widget had no spelling until now.
let formToggle: Node<obj> =
    let toggleField: FormField<obj> =
        { Id = "irrigation-running"
          Label = TextSource.Literal "Irrigation"
          Kind =
            FormFieldKind.Toggle(
                Some(Binding.State("irrigation-running", Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox)),
                Option.None
            )
          Required = false
          Help = None }

    // The contrast case: consent stays a Checkbox. Both in one fixture so the
    // corpus shows a reader WHEN each applies, not merely that both exist.
    let consentField: FormField<obj> =
        { Id = "accept-terms"
          Label = TextSource.Literal "I accept the terms"
          Kind =
            FormFieldKind.Checkbox(
                Some(Binding.State("accept-terms", Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox)),
                Option.None
            )
          Required = true
          Help = None }

    node
        "form-toggle"
        (NodeKind.Form(
            { Fields = [ toggleField; consentField ]
              OnSubmit = Action.Chain []
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

/// canonical bytes carry NO `value` key on any field (mirror of the
/// filters-declarative minimal chip). Pins the round-trip: decode
/// synthesises the same bindings back, encode omits them again.
let formDeclarativeMinimal: Node<obj> =
    let textField: FormField<obj> =
        { Id = "guest-name"
          Label = TextSource.Literal "Name"
          Kind =
            FormFieldKind.Text(
                Some(Binding.State("guest-name", Some Fuaran.UI.Defaults.ControlValueDefaults.text)),
                Option.None
            )
          Required = true
          Help = None }

    let numberField: FormField<obj> =
        { Id = "party-size"
          Label = TextSource.Literal "Party size"
          Kind =
            FormFieldKind.Number(
                Some(Binding.State("party-size", Some Fuaran.UI.Defaults.ControlValueDefaults.number)),
                Option.None
            )
          Required = false
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "seating"
          Label = TextSource.Literal "Seating"
          Kind =
            FormFieldKind.Choice(
                Binding.Static(
                    Some
                        [ { Value = "indoor"; Label = "Indoor" }
                          { Value = "terrace"; Label = "Terrace" } ]
                ),
                Some(Binding.State("seating", Fuaran.UI.Defaults.ControlValueDefaults.choice)),
                Option.None
            )
          Required = false
          Help = None }

    let dateField: FormField<obj> =
        { Id = "visit-date"
          Label = TextSource.Literal "Date"
          Kind =
            FormFieldKind.Date(
                Some(Binding.State("visit-date", Some Fuaran.UI.Defaults.ControlValueDefaults.date)),
                Option.None,
                DateVariant.Date,
                None,
                None,
                None
            )
          Required = true
          Help = None }

    node
        "form-declarative-minimal"
        (NodeKind.Form(
            { Fields = [ textField; numberField; choiceField; dateField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Book"
              Disabled = Option.None }
        ))
        None

/// Handler-free interactive layouts + select (Phase 426): tabs whose
/// `ActiveIndex` is State-bound with `onSelect` omitted (a click writes the
/// index back), a dismissable modal whose `Open` is State-bound with
/// `onDismiss` omitted (dismiss writes `false`), a disclosure whose `Open` is
/// State-bound with `onToggle` omitted, and a single-select whose `Value` is
/// State-bound with `onChange` omitted — the full declarative-floor wire.
let controlsDeclarative: Node<obj> =
    let tabsNode =
        node
            "decl-tabs"
            (NodeKind.Tabs(
                { Orientation = Orientation.Horizontal
                  Children = [ markdown ]
                  ActiveIndex = Binding.State("activePane", Some 0)
                  OnSelect = Option.None
                  TabHeaders = Option.None
                  TabTags = Option.None
                  ActiveTag = Option.None
                  OnSelectTag = Option.None }
            ))
            None

    let modalNode =
        node
            "decl-modal"
            (NodeKind.Modal(
                { Open = Binding.State("modalOpen", Some false)
                  Heading = Some(TextSource.Literal "Confirm")
                  Dismissable = true
                  Children = [ withId "markdown-2" markdown ]
                  OnDismiss = Option.None }
            ))
            None

    let disclosureNode =
        node
            "decl-disclosure"
            (NodeKind.Disclosure(
                { Heading = TextSource.Literal "Advanced"
                  Open = Binding.State("advancedOpen", Some false)
                  OnToggle = Option.None
                  Children = [ withId "markdown-3" markdown ]
                  DefaultOpen = false }
            ))
            None

    let selectNode =
        node
            "decl-select"
            (NodeKind.Select(
                { Label = TextSource.Literal "Region"
                  Source = Binding.Static(Some [ { Value = "uk"; Label = "UK" } ])
                  Value = Binding.State("region", None)
                  OnChange = Option.None
                  Placeholder = Some(TextSource.Literal "Choose one")
                  Disabled = Option.None
                  Multiple = None
                  Values = Option.None
                  OnChangeMulti = Option.None }
            ))
            None

    node
        "controls-declarative"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ tabsNode; modalNode; disclosureNode; selectNode ] }
        ))
        None

/// Closure-authored counterparts for the Phase 426 sentinel keys that were
/// previously never encoded: tabs with a tag-overlay `onSelectTag` closure, a
/// disclosure with an `onToggle` closure, and a multi-select with an
/// `onChangeMulti` closure — each now rides the wire as its own `"<closure>"`
/// sentinel and decodes to the inert `Some` placeholder.
let multiSelectClosure: Node<obj> =
    let tabsNode =
        node
            "closure-tabs"
            (NodeKind.Tabs(
                { Orientation = Orientation.Horizontal
                  Children = [ markdown; sparkline ]
                  ActiveIndex = Binding.Static(Some 0)
                  OnSelect = Some(fun _ -> placeholderChain)
                  TabHeaders = Option.None
                  TabTags = Some [ "overview"; "detail" ]
                  ActiveTag = Some(Binding.Static(Some "overview"))
                  OnSelectTag = Some(fun _ -> placeholderChain) }
            ))
            None

    let disclosureNode =
        node
            "closure-disclosure"
            (NodeKind.Disclosure(
                { Heading = TextSource.Literal "Advanced"
                  Open = Binding.Static(Some false)
                  OnToggle = Some(fun _ -> placeholderChain)
                  Children = [ withId "markdown-2" markdown ]
                  DefaultOpen = false }
            ))
            None

    let multiNode =
        node
            "closure-multiselect"
            (NodeKind.Select(
                { Label = TextSource.Literal "Tags"
                  Source = Binding.Static(Some [ { Value = "red"; Label = "Red" } ])
                  Value = Binding.Static None
                  OnChange = Some(fun _ -> placeholderChain)
                  Placeholder = Option.None
                  Disabled = Option.None
                  Multiple = Some true
                  Values = Some(Binding.Static(Some [ "red" ]))
                  OnChangeMulti = Some(fun _ -> placeholderChain) }
            ))
            None

    node
        "controls-closure"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = [ tabsNode; disclosureNode; multiNode ] }
        ))
        None

// ─── Visualisation fixtures ──────────────────────────────────────────────

let gridVis: Node<obj> =
    let col: ColumnErased<obj> =
        { Label = "Channel"
          Value = Some(fun _ -> CellValue.Empty)
          Field = None
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-1"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source =
                // fuaran#665 — typed rows: a Static rows payload IS wire-representable
                // (int cells ride rule 5's integer form). Mirrors Fuaran-Core's
                // authored `gridNode` sample byte-for-byte.
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "channel", box "Direct"; "revenue", box 1200 ]: Row)
                              Map.ofList [ "channel", box "Referral"; "revenue", box 830 ] ]
                    )
                )
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 750 — `CellKindErased.TonedPill`, the wire-expressible conditional tone.
/// The whole point of the fixture is that EVERY part of it rides the wire: the rows
/// are embedded columnar data, the two display columns project row properties by
/// `field` (no `value` closure), and the status column's tone comes from a declared
/// value→tone map. Nothing here erases to a sentinel, so a decoded tree renders the
/// delayed rows visually distinguished with zero host code — which is exactly what a
/// hosted `Pill` cannot express.
///
/// Deliberately exercises both `default` postures in one document: the status column
/// declares `Subdued` for a value the map does not mention (emitted), the carrier
/// column leaves it at `ToneVariant.Default` (omitted-when-default, so the key is
/// absent). One fixture, both branches of the omit rule.
let gridTonedPill: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "carrier", Fuaran.Core.StringType
                  "status", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "SHP-1001"
                        Fuaran.Core.Str "SHP-1002"
                        Fuaran.Core.Str "SHP-1003" ]
                  Fuaran.Core.Column.create
                      "carrier"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "Northwind"
                        Fuaran.Core.Str "Meridian"
                        Fuaran.Core.Str "Northwind" ]
                  Fuaran.Core.Column.create
                      "status"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "On time"
                        Fuaran.Core.Str "Delayed"
                        Fuaran.Core.Str "Cancelled" ] ] }

    let declarative (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          // Phase 425 — no `value` closure: `Field` alone drives the projection.
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-toned-pill"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), [], None)
              RowKey = None
              RowKeyField = Some "id"
              Columns =
                [ declarative "Shipment" "id" CellKindErased.Text
                  declarative
                      "Carrier"
                      "carrier"
                      (CellKindErased.TonedPill(
                          "carrier",
                          Map [ "Meridian", ToneVariant.Info ],
                          // Left at the identity default — the `default` key is OMITTED.
                          ToneVariant.Default
                      ))
                  declarative
                      "Status"
                      "status"
                      (CellKindErased.TonedPill(
                          "status",
                          Map
                              [ "On time", ToneVariant.Success
                                "Delayed", ToneVariant.Warning
                                "Cancelled", ToneVariant.Critical ],
                          // Non-identity — the `default` key IS emitted.
                          ToneVariant.Subdued
                      )) ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

let chart: Node<obj> =
    node
        "chart-1"
        (NodeKind.Chart(
            { Source =
                // fuaran#665 — typed rows (see grid-1). Mirrors Fuaran-Core's
                // authored `chartNode` sample byte-for-byte.
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "cost", box 420; "month", box "Jan"; "revenue", box 980 ]: Row)
                              Map.ofList [ "cost", box 390; "month", box "Feb"; "revenue", box 1105 ] ]
                    )
                )
              Kind = ChartKind.Line
              XField = "month"
              YFields = [ "revenue"; "cost" ]
              Title = Some(TextSource.Literal "Channel mix")
              // Absent (Phase 876) — the ordinary shape: no declared value
              // meaning, so the lowering's canonical default rendering applies.
              ValueFormat = None
              // Absent (Phase 878) — likewise the ordinary shape: both axis
              // titles fall back to their capitalised field names and no
              // subtitle draws. `chart-axis-titles` pins the present half.
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = None
              DataLabels = None
              XScale = None
              OnPointClick = None
              // Stacked = true (Phase 126) — exercises the now-carried
              // stacked-vs-grouped chart intent round-tripping.
              Stacked = true }
        ))
        None

/// Phase 876 — `valueFormat`: the value axis's declared number format, reusing
/// the existing `Format` vocabulary (no parallel formatting DU was minted). The
/// PRESENT half of the pair (`chart-1` pins the absent half).
let chartValueFormat: Node<obj> =
    node
        "chart-value-format"
        (NodeKind.Chart(
            { Source =
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "month", box "Jan"; "revenue", box 12500000 ]: Row)
                              Map.ofList [ "month", box "Feb"; "revenue", box 15200000 ] ]
                    )
                )
              Kind = ChartKind.Bar
              XField = "month"
              YFields = [ "revenue" ]
              Title = Some(TextSource.Literal "Revenue")
              ValueFormat = Some(Format.Currency "GBP")
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = None
              DataLabels = None
              XScale = None
              OnPointClick = None
              Stacked = false }
        ))
        None

/// Phase 878 — `xTitle` / `yTitle` / `subtitle`: the axis NAMES and the muted
/// line under the title. The PRESENT half of the pair (`chart-1` pins the
/// absent half, where both axes fall back to their capitalised field names).
/// The subtitle states the unit, which is also the shape that exercises the
/// dedupe rule: an explicit subtitle suppresses the lowering's own
/// display-unit slot.
let chartAxisTitles: Node<obj> =
    node
        "chart-axis-titles"
        (NodeKind.Chart(
            { Source =
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "quarter", box "Q1"; "revenue", box 12500000 ]: Row)
                              Map.ofList [ "quarter", box "Q2"; "revenue", box 15200000 ] ]
                    )
                )
              Kind = ChartKind.Bar
              XField = "quarter"
              YFields = [ "revenue" ]
              Title = Some(TextSource.Literal "Revenue by quarter")
              ValueFormat = Some(Format.Currency "GBP")
              XTitle = Some(TextSource.Literal "Quarter")
              YTitle = Some(TextSource.Literal "Revenue")
              Subtitle = Some(TextSource.Literal "Millions of £")
              LegendPosition = None
              DataLabels = None
              XScale = None
              OnPointClick = None
              Stacked = false }
        ))
        None

/// Phase 880 — `legendPosition`: WHERE the legend sits. The PRESENT half of the
/// pair (every other chart fixture pins the absent half, which means "the host
/// style's default", not "no legend"). Two series, so the chart genuinely has a
/// legend to place, and `Bottom` is chosen deliberately over the default
/// `Right`: a fixture that names the value the style would have picked anyway
/// cannot show that the wire field was read at all.
let chartLegendPosition: Node<obj> =
    node
        "chart-legend-position"
        (NodeKind.Chart(
            { Source =
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "region", box "North"; "sales", box 80; "target", box 100 ]: Row)
                              Map.ofList [ "region", box "South"; "sales", box 130; "target", box 110 ] ]
                    )
                )
              Kind = ChartKind.Bar
              XField = "region"
              YFields = [ "sales"; "target" ]
              Title = Some(TextSource.Literal "Sales vs target")
              ValueFormat = None
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = Some ChartLegendPosition.Bottom
              DataLabels = None
              XScale = None
              OnPointClick = None
              Stacked = false }
        ))
        None

/// Phase 881 — `dataLabels`: whether the values are written onto the picture.
/// The PRESENT half of the pair; every other chart fixture pins the absent
/// half, which means `Off` — and `Off` is also the default, so an absent field
/// lowers to the pre-881 picture byte-for-byte. `Ends` is the only other value
/// there is: the vocabulary carries no all-points case, by design.
let chartDataLabels: Node<obj> =
    node
        "chart-data-labels"
        (NodeKind.Chart(
            { Source =
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "quarter", box "Q1"; "revenue", box 120 ]: Row)
                              Map.ofList [ "quarter", box "Q2"; "revenue", box 150 ] ]
                    )
                )
              Kind = ChartKind.Bar
              XField = "quarter"
              YFields = [ "revenue" ]
              Title = Some(TextSource.Literal "Revenue by quarter")
              ValueFormat = None
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = None
              DataLabels = Some ChartDataLabels.Ends
              XScale = None
              OnPointClick = None
              Stacked = false }
        ))
        None

/// Phase 882 — `xScale`: what the x column MEANS. The PRESENT half of the pair
/// (`Temporal`); every other chart fixture pins the absent half, which means
/// `Category` — also the default, so an absent field lowers to the pre-882
/// picture byte-for-byte. The x cells are canonical ISO-8601 dates, which is
/// what makes the declaration groundable: a `Temporal` axis over a non-date
/// column is a FUARAN097 refusal, not a silent coercion.
let chartTemporalX: Node<obj> =
    node
        "chart-temporal-x"
        (NodeKind.Chart(
            { Source =
                Binding.Static(
                    Some(
                        Seq.ofList
                            [ (Map.ofList [ "day", box "2026-01-05"; "sessions", box 1200 ]: Row)
                              Map.ofList [ "day", box "2026-01-12"; "sessions", box 1450 ]
                              Map.ofList [ "day", box "2026-01-19"; "sessions", box 1310 ]
                              Map.ofList [ "day", box "2026-01-26"; "sessions", box 1580 ] ]
                    )
                )
              Kind = ChartKind.Line
              XField = "day"
              YFields = [ "sessions" ]
              Title = Some(TextSource.Literal "Sessions by week")
              ValueFormat = None
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = None
              DataLabels = None
              XScale = Some ChartXScale.Temporal
              OnPointClick = None
              Stacked = false }
        ))
        None

// ─── fuaran#665 — the Phase 663 editable-grid anchor (grid + chart on ONE state key) ──
//
// The corpus carried NO `editable: true` fixture at all, so the cross-host
// parity harness had nothing to certify the editable-grid write-back floor
// against. This pair is the canonical Phase 663 shape: `editable: true` over a
// DIRECT `Binding.State` rows source (the one write-back-capable shape), typed
// rows riding the wire in `defaultValue`, field-named Text/Numeric columns
// (closure-free — the declarative floor), and a Chart sourced on the SAME state
// key, so an edit committed by the grid re-renders the chart.

let private planRows: Row list =
    [ Map.ofList [ "month", box "Jan"; "revenue", box 980 ]
      Map.ofList [ "month", box "Feb"; "revenue", box 1105 ] ]

let gridEditableState: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-editable-state"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("planRows", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = true
              Reorderable = false
              StaticRows = None }
        ))
        None

let chartStateRows: Node<obj> =
    node
        "chart-state-rows"
        (NodeKind.Chart(
            { Source = Binding.State("planRows", Some(Seq.ofList planRows))
              Kind = ChartKind.Bar
              XField = "month"
              YFields = [ "revenue" ]
              Title = None
              ValueFormat = None
              XTitle = None
              YTitle = None
              Subtitle = None
              LegendPosition = None
              DataLabels = None
              XScale = None
              OnPointClick = None
              Stacked = false }
        ))
        None

// Phase 282 — the Compute layer. A DataGrid whose Source is a `Binding.Transform`: a declarative
// `Fuaran.Core.DataFrame` pipeline (filter → groupBy → sort, with a null cell + a typed source)
// over an embedded columnar `DataSource`. Exercises the round-trip of the compute sub-tree through
// the shared `Canon` `$type` discipline (the codecs bridge to `Fuaran.Core.ColumnCodec` /
// `DataFrameCodec`).
let gridTransform: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
                  Fuaran.Core.Column.create
                      "amount"
                      Fuaran.Core.IntType
                      [ Fuaran.Core.Int 100; Fuaran.Core.Int 120; Fuaran.Core.Null ] ] }

    let pipeline: Fuaran.Core.Transform list =
        [ Fuaran.Core.Filter(
              Fuaran.Core.Binary(Fuaran.Core.Gt, Fuaran.Core.Col "amount", Fuaran.Core.Lit(Fuaran.Core.Int 0))
          )
          Fuaran.Core.GroupBy(
              [ "dept" ],
              [ ({ Name = "total"
                   Fn = Fuaran.Core.Sum
                   Of = "amount" }
                : Fuaran.Core.Agg) ]
          )
          Fuaran.Core.Sort [ "total", Fuaran.Core.Desc ] ]

    node
        "grid-transform"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), pipeline, None)
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 424 — a parameterised `Binding.Transform`: the pipeline's `filter` step compares a `col`
/// against a `param` sourced from a `Binding.Filter` chip, so the grid is scoped by a live filter
/// with zero host code. Proves the `params` wire (omitted-when-empty elsewhere).
let gridTransformParam: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
                  Fuaran.Core.Column.create "amount" Fuaran.Core.IntType [ Fuaran.Core.Int 100; Fuaran.Core.Int 90 ] ] }

    let pipeline: Fuaran.Core.Transform list =
        [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "dept")) ]

    node
        "grid-transform-param"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source =
                Binding.Transform(
                    TransformSource.Data(source),
                    pipeline,
                    Some
                        [ { From = Binding.Filter("dept", None)
                            Name = "dept" } ]
                )
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 425 — a fully field-named `DataGrid`: columns carry `Field` (no closure), the spec carries
/// `RowKeyField`, and the source is a `Transform`. A decoded grid renders data + stable identity with
/// zero host code. Proves the `field` / `rowKeyField` wire (omitted-when-None elsewhere).
let gridFieldNamed: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create "dept" Fuaran.Core.StringType [ Fuaran.Core.Str "eng" ]
                  Fuaran.Core.Column.create "amount" Fuaran.Core.IntType [ Fuaran.Core.Int 100 ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-field-named"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Transform(TransformSource.Data(source), [], None)
              RowKey = None
              RowKeyField = Some "dept"
              Columns = [ fieldCol "Dept" "dept"; fieldCol "Amount" "amount" ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

// The MASTER-DETAIL-PRESELECTED composition: a grid beside a detail card whose
// slots bind `State` with a `defaultValue` — the selected-row id lives in state,
// pre-selected on load. The 2026-07-19 eval campaign's second judge-layer
// cluster (task 040 criterion c3): models INVENT grid selection properties
// (`defaultSelection`, `initialSelection`, `selectedRowKey` — none exist) or
// omit selection entirely; the passing emissions all used exactly this State
// idiom. Row-click wiring is host-side (`OnRowClick` is a closure); the
// declaratively-emittable half — the default + the detail binding — is what
// this fixture teaches.
let masterDetailPreselected: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "priority", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"; Fuaran.Core.Str "TCK-2042" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "low" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "master-detail-preselected"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ node
                                    "detail-ticket"
                                    (NodeKind.Fact(
                                        { Label = TextSource.Literal "Selected ticket"
                                          // 0.2.9 (Phase 629): the defaulted-Selection form —
                                          // the composition models emit naturally, now
                                          // expressible (was the State workaround pre-629).
                                          // 0.2.10 (Phase 632): + `field` — the projected
                                          // row-key stays scalar after a real click (the
                                          // identity form yielded the whole row).
                                          Value =
                                            TextSource.Bound(
                                                Binding.Selection(
                                                    "ticket-grid",
                                                    Binding.projectSelectionField<string> "id",
                                                    Some "TCK-2041",
                                                    Some "id"
                                                )
                                            )
                                          Icon = None
                                          Tone = ToneVariant.Default
                                          Emphasis = true
                                          Help = None }
                                    ))
                                    None ] }
                      ))
                      None
                  // 2026-07-20 demand pin: `Selection` as a `Transform.params`
                  // source (the sol 040 r42 emission's composition) — the
                  // related-items grid filters the embedded rows by the
                  // ticket-grid's selection, defaulted. Wire-confirms the
                  // param `from` slot accepts any Binding, Selection included.
                  node
                      "related-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(
                                  TransformSource.Data(source),
                                  [ Fuaran.Core.Filter(
                                        Fuaran.Core.Binary(
                                            Fuaran.Core.Eq,
                                            Fuaran.Core.Col "id",
                                            Fuaran.Core.Param "ticketId"
                                        )
                                    ) ],
                                  Some
                                      [ { From =
                                            // 0.2.10 (Phase 632): `field` keeps the param SCALAR
                                            // after a real click — the identity form handed the
                                            // whole row to `objToCell` (a loud non-scalar error).
                                            Binding.Selection(
                                                "ticket-grid",
                                                Binding.projectSelectionField<JVal> "id",
                                                Some(JStr "TCK-2041"),
                                                Some "id"
                                            )
                                          Name = "ticketId" } ]
                              )
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None ] }
        ))
        None

// The MULTI-FIELD twin of `masterDetailPreselected`. Every existing Selection
// fixture projects the SAME field (`id`) into every slot, so "one selection can
// feed N slots, each projecting a DIFFERENT column" is nowhere demonstrated —
// and that is exactly the shape models miss. The 2026-08-01 n=3 review found
// 032/c6 + 036/c8 (×6, two tasks) emitting a correct `Selection` + `defaultValue`
// for ONE slot and then HARD-CODING every sibling in the same detail card:
//   "selected-flight Fact is bound via Selection … with defaultValue UA451, but
//    crew names and route are static hard-coded values not driven by selection."
// `Binding.Selection(nodeId, accessor, defaultValue, field)` has carried `field`
// since 0.2.10 (Phase 632), so the composition was expressible the whole time —
// the models learned `defaultValue` (Phase 629) and never learned `field`. This
// fixture teaches the projection by showing three sibling Facts off ONE grid,
// each naming a different column.
let masterDetailMultiField: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "priority", Fuaran.Core.StringType
                  "assignee", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"; Fuaran.Core.Str "TCK-2042" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "low" ]
                  Fuaran.Core.Column.create
                      "assignee"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "R. Okafor"; Fuaran.Core.Str "M. Lindqvist" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    // One slot of the detail card: a Fact projecting ONE named column off the
    // shared selection. The only thing that varies across the three is `field`.
    let projectedFact (nodeId: string) (label: string) (field: string) : Node<obj> =
        node
            nodeId
            (NodeKind.Fact(
                { Label = TextSource.Literal label
                  Value =
                    TextSource.Bound(
                        Binding.Selection(
                            "ticket-grid",
                            Binding.projectSelectionField<string> field,
                            Some "TCK-2041",
                            Some field
                        )
                    )
                  Icon = None
                  Tone = ToneVariant.Default
                  Emphasis = false
                  Help = None }
            ))
            None

    node
        "master-detail-multi-field"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Assignee" "assignee" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ projectedFact "detail-ticket" "Selected ticket" "id"
                                projectedFact "detail-priority" "Priority" "priority"
                                projectedFact "detail-assignee" "Assignee" "assignee"
                                // A PROSE slot on the same selection. The 2026-08-02
                                // flip-4 shakedown found models wiring the Facts
                                // correctly and leaving the narration literal, so the
                                // panel's numbers follow the click and its sentence
                                // does not (036/c8, 5/6). `Callout.Body` is a
                                // TextSource like any other — this is the SIMPLE
                                // bound-body form; the only other bound body in the
                                // corpus is the heavy Transform composition in
                                // `scalar-transform-composition`, which is not a
                                // shape a model reaches for to write one sentence.
                                node
                                    "detail-note"
                                    (NodeKind.Callout(
                                        { Tone = ToneVariant.Info
                                          Heading = Some(TextSource.Literal "Assigned to")
                                          Body =
                                            TextSource.Bound(
                                                Binding.Selection(
                                                    "ticket-grid",
                                                    Binding.projectSelectionField<string> "assignee",
                                                    Some "R. Okafor",
                                                    Some "assignee"
                                                )
                                            )
                                          Icon = None
                                          Dismissable = false }
                                    ))
                                    None ] }
                      ))
                      None ] }
        ))
        None

// The NON-FIRST-ROW twin of `masterDetailPreselected`, and a deliberate
// near-clone of it: same composition, one different default. Every existing
// Selection fixture defaults to the FIRST row, which makes prune-vs-seed
// UNOBSERVABLE — a host that *prunes* an unbound-param filter (the "unset
// choice filter ⇒ no constraint" rule, WIRE_FORMAT §"Binding.Transform params")
// instead of *seeding* the param from `defaultValue` still shows row 1's data,
// because row 1 is what an unfiltered pipeline surfaces first. Defaulting to
// `TCK-2042` — index 1 of 3, neither first nor last, so a wrong-FIRST and a
// wrong-LAST are both caught — makes the two behaviours diverge visibly.
//
// The third column is per-row-DISTINCT (`note`) so the scalar leg diverges by
// VALUE, not merely by row count: a pruning host renders "Payment gateway
// timeout" where a seeding host renders "Search index stale". A count-only
// divergence can be mistaken for a fixture-shape difference; a wrong string
// cannot.
//
// Four nodes, three of them observing the SAME default through different
// machinery, so a host that gets one leg right and another wrong is caught:
//   - `ticket-grid`   — the master and the Selection's `nodeId` target. The
//                       control: unaffected by the default either way.
//   - `detail-ticket` — the plain-Selection SCALAR leg, no Transform at all.
//                       A pruning host has no filter to prune here, so this
//                       isolates `defaultValue` resolution itself (NotResolved
//                       when the default is ignored).
//   - `related-grid`  — the ROW-CONTEXT leg: Selection feeding a Transform
//                       param. 1 row when seeded, all 3 when pruned.
//   - `detail-note`   — the MASKING-KILLER: the exact `filter -> project ->
//                       limit 1` shape a first-row default hides, terminating
//                       in a scalar Callout body. This is the leg that reports
//                       a wrong VALUE rather than a wrong count.
let masterDetailPreselectedSecondRow: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "priority", Fuaran.Core.StringType
                  "note", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"
                        Fuaran.Core.Str "TCK-2042"
                        Fuaran.Core.Str "TCK-2043" ]
                  Fuaran.Core.Column.create
                      "priority"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "high"; Fuaran.Core.Str "medium"; Fuaran.Core.Str "low" ]
                  Fuaran.Core.Column.create
                      "note"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "Payment gateway timeout"
                        Fuaran.Core.Str "Search index stale"
                        Fuaran.Core.Str "Avatar upload fails" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    /// The one `Transform.params` entry every non-control leg shares: the
    /// ticket-grid's selection, defaulted to the SECOND row, projected through
    /// `field` so the param stays scalar after a real click (Phase 632).
    let ticketIdParam: TransformParam =
        { From =
            Binding.Selection("ticket-grid", Binding.projectSelectionField<JVal> "id", Some(JStr "TCK-2042"), Some "id")
          Name = "ticketId" }

    let filterById =
        Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "id", Fuaran.Core.Param "ticketId"))

    node
        "master-detail-preselected-second-row"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Note" "note" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Ticket detail")
                            Children =
                              [ node
                                    "detail-ticket"
                                    (NodeKind.Fact(
                                        { Label = TextSource.Literal "Selected ticket"
                                          Value =
                                            TextSource.Bound(
                                                Binding.Selection(
                                                    "ticket-grid",
                                                    Binding.projectSelectionField<string> "id",
                                                    Some "TCK-2042",
                                                    Some "id"
                                                )
                                            )
                                          Icon = None
                                          Tone = ToneVariant.Default
                                          Emphasis = true
                                          Help = None }
                                    ))
                                    None ] }
                      ))
                      None
                  node
                      "related-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(TransformSource.Data(source), [ filterById ], Some [ ticketIdParam ])
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Ticket" "id"
                                fieldCol "Priority" "priority"
                                fieldCol "Note" "note" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "detail-note"
                      (NodeKind.Callout(
                          { Tone = ToneVariant.Info
                            Heading = Some(TextSource.Literal "Ticket note")
                            Body =
                              TextSource.Bound(
                                  Binding.Transform(
                                      TransformSource.Data(source),
                                      [ filterById; Fuaran.Core.Project [ "note", "note" ]; Fuaran.Core.Limit(1, 0) ],
                                      Some [ ticketIdParam ]
                                  )
                              )
                            Icon = None
                            Dismissable = false }
                      ))
                      None ] }
        ))
        None

// The SCALAR-TRANSFORM composition (0.2.10, Phase 632): Transform in SCALAR
// slots — the strongest 2026-07-20 demand cluster, emitted unprompted by the
// tier-a-040 sol r42 cell and scored 1.000 by the judge while the language
// refused the slot. Two canonical scalar terminals, one fixture:
//   - a Callout body derived from the SELECTED row (`Selection`-fed param →
//     `filter` → `project` one column → `limit` 1 — the r42 shape verbatim,
//     with the Phase-632 `field` projection keeping the param scalar
//     post-click);
//   - a Badge count over the same data (`filter` → `groupBy(keys: [],
//     aggs: [one count])` — the global-aggregate terminal; empty ⇒ 0).
// Phase 765 — the host-furnished instant, in BOTH the positions the demand
// evidence asked for:
//
//   - `today-fact`   — `Now` straight into a text slot ("the current date in a
//                      header", the pilot-5 row dispositioned "the strongest
//                      new capability demand"; models hardcoded a date and were
//                      judged PARTIAL because nothing else was expressible).
//   - `overdue-grid` — `Now` as a `Transform` PARAM feeding `dateDiffDays`, so
//                      "days overdue" is derived rather than baked. This is the
//                      leg that proves the composition: the verbs already
//                      shipped (Core's `DateDiffDays` reads the leading
//                      `YYYY-MM-DD`, so the ISO-8601 instant works unchanged) —
//                      only the operand was missing.
//
// No clock appears on the wire: `{"$type":"Now"}` has no fields. The host
// resolves it once per render into `BindingSources.Now`, which is what keeps a
// replayed op-stream reproducing its ORIGINAL render instead of drifting to
// replay-time "now".
let nowEnvironmentBinding: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "due", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "INV-1001"; Fuaran.Core.Str "INV-1002" ]
                  Fuaran.Core.Column.create
                      "due"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "2026-07-01"; Fuaran.Core.Str "2026-07-28" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "now-environment-binding"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "today-fact"
                      (NodeKind.Fact(
                          { Label = TextSource.Literal "Today"
                            Value = TextSource.Bound(Binding.Now(fun (o: obj) -> unbox<string> o))
                            Icon = None
                            Tone = ToneVariant.Default
                            Emphasis = false
                            Help = None }
                      ))
                      None
                  node
                      "overdue-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Transform(
                                  TransformSource.Data(source),
                                  // days overdue = dateDiffDays(today, due) — the
                                  // param is the ONLY new part; the verb is Core's.
                                  [ Fuaran.Core.Derive(
                                        "daysOverdue",
                                        Fuaran.Core.ApplyFn(
                                            Fuaran.Core.DateDiffDays,
                                            [ Fuaran.Core.Param "today"; Fuaran.Core.Col "due" ]
                                        )
                                    ) ],
                                  Some
                                      [ { From = Binding.Now(fun (o: obj) -> JStr(unbox<string> o))
                                          Name = "today" } ]
                              )
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ fieldCol "Invoice" "id"
                                fieldCol "Due" "due"
                                fieldCol "Days overdue" "daysOverdue" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None ] }
        ))
        None

let scalarTransformComposition: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "id", Fuaran.Core.StringType
                  "alert", Fuaran.Core.StringType
                  "severity", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041"
                        Fuaran.Core.Str "TCK-2042"
                        Fuaran.Core.Str "TCK-2043" ]
                  Fuaran.Core.Column.create
                      "alert"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "TCK-2041 breaches SLA in 2 hours"
                        Fuaran.Core.Str "TCK-2042 breaches SLA in 5 hours"
                        Fuaran.Core.Str "TCK-2043 breaches SLA in 9 hours" ]
                  Fuaran.Core.Column.create
                      "severity"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "critical"
                        Fuaran.Core.Str "high"
                        Fuaran.Core.Str "critical" ] ] }

    node
        "scalar-transform-composition"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "scalar-ticket-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ { Label = "Ticket"
                                  Value = None
                                  Field = Some "id"
                                  Sortable = None
                                  Editable = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto }
                                { Label = "Severity"
                                  Value = None
                                  Field = Some "severity"
                                  Sortable = None
                                  Editable = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "critical-count-badge"
                      (NodeKind.Badge(
                          { Label =
                              TextSource.Bound(
                                  Binding.Transform(
                                      TransformSource.Data(source),
                                      [ Fuaran.Core.Filter(
                                            Fuaran.Core.Binary(
                                                Fuaran.Core.Eq,
                                                Fuaran.Core.Col "severity",
                                                Fuaran.Core.Lit(Fuaran.Core.Str "critical")
                                            )
                                        )
                                        Fuaran.Core.GroupBy(
                                            [],
                                            [ { Name = "n"
                                                Fn = Fuaran.Core.AggFn.Count
                                                Of = "id" } ]
                                        ) ],
                                      None
                                  )
                              )
                            Variant = BadgeVariant.Critical }
                      ))
                      None
                  node
                      "sla-warning"
                      (NodeKind.Callout(
                          { Tone = ToneVariant.Warning
                            Heading = Some(TextSource.Literal "SLA breach imminent")
                            Body =
                              TextSource.Bound(
                                  Binding.Transform(
                                      TransformSource.Data(source),
                                      [ Fuaran.Core.Filter(
                                            Fuaran.Core.Binary(
                                                Fuaran.Core.Eq,
                                                Fuaran.Core.Col "id",
                                                Fuaran.Core.Param "ticketId"
                                            )
                                        )
                                        Fuaran.Core.Project [ "alert", "alert" ]
                                        Fuaran.Core.Limit(1, 0) ],
                                      Some
                                          [ { From =
                                                Binding.Selection(
                                                    "scalar-ticket-grid",
                                                    Binding.projectSelectionField<JVal> "id",
                                                    Some(JStr "TCK-2041"),
                                                    Some "id"
                                                )
                                              Name = "ticketId" } ]
                                  )
                              )
                            Icon = None
                            Dismissable = false }
                      ))
                      None ] }
        ))
        None

// The FILTERABLE-STATIC composition: a Filters node whose named params drive
// Transform consumers over embedded data. The 2026-07-19 eval campaign found the
// judge-layer failure every model family repeats on this intent (task 035
// criterion c7): filters declared, consumers fed unwired static arrays. The
// wiring is `params` pulling the live filter values by name + pipeline steps
// applying them — declaring filters without it leaves consumers inert. This
// fixture is the canonical worked example the pack teaches the composition from.
let filterableStaticDashboard: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema =
                [ "region", Fuaran.Core.StringType
                  "genre", Fuaran.Core.StringType
                  "month", Fuaran.Core.StringType
                  "retention", Fuaran.Core.FloatType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "region"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "emea"; Fuaran.Core.Str "amer" ]
                  Fuaran.Core.Column.create
                      "genre"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "drama"; Fuaran.Core.Str "docs" ]
                  Fuaran.Core.Column.create
                      "month"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "jan"; Fuaran.Core.Str "jan" ]
                  Fuaran.Core.Column.create
                      "retention"
                      Fuaran.Core.FloatType
                      [ Fuaran.Core.Float 0.62; Fuaran.Core.Float 0.55 ] ] }

    // Both consumers share the same wiring; a fresh Binding per consumer.
    let filteredSource () =
        Binding.Transform(
            TransformSource.Data(source),
            [ Fuaran.Core.Filter(
                  Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "region", Fuaran.Core.Param "region")
              )
              Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "genre", Fuaran.Core.Param "genre")) ],
            Some
                [ { From = Binding.Filter("region", None)
                    Name = "region" }
                  { From = Binding.Filter("genre", None)
                    Name = "genre" } ]
        )

    let choice (name: string) (label: string) (options: (string * string) list) : FilterSpec<obj> =
        { Name = name
          Label = TextSource.Literal label
          Kind =
            FormFieldKind.Choice(
                Binding.Static(Some [ for value, optLabel in options -> { Value = value; Label = optLabel } ]),
                Some(Binding.Filter(name, None)),
                None
            ) }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "filterable-static-dashboard"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = Some(TextSource.Literal "Content performance")
              Children =
                [ node
                      "content-filters"
                      (NodeKind.Filters(
                          { Items =
                              [ choice "region" "Region" [ "emea", "EMEA"; "amer", "Americas" ]
                                choice "genre" "Genre" [ "drama", "Drama"; "docs", "Documentary" ] ] }
                      ))
                      None
                  node
                      "retention-chart"
                      (NodeKind.Chart(
                          { Source = filteredSource ()
                            Kind = ChartKind.Line
                            XField = "month"
                            YFields = [ "retention" ]
                            Title = Some(TextSource.Literal "Retention")
                            ValueFormat = None
                            XTitle = None
                            YTitle = None
                            Subtitle = None
                            LegendPosition = None
                            DataLabels = None
                            XScale = None
                            OnPointClick = None
                            Stacked = false }
                      ))
                      None
                  node
                      "episode-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = filteredSource ()
                            RowKey = None
                            RowKeyField = Some "month"
                            Columns = [ fieldCol "Month" "month"; fieldCol "Retention" "retention" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None ] }
        ))
        None

/// Phase 421 — a `Metric` whose `Source` is a host-computed `Query` that declares its filter
/// dependency edge (`dependsOn`). Proves the `dependsOn` wire (omitted-when-empty elsewhere) — the
/// tree owns the edge, the host closure owns the predicate.
let queryDependsOn: Node<obj> =
    node
        "query-dependson"
        (NodeKind.Metric(
            { metricSpec with
                Value = Binding.Query("orders", (fun _ -> 0.0), Some [ "status"; "region" ])
                Trend = None
                TrendFormat = None }
        ))
        None

let table: Node<obj> =
    node
        "table-1"
        (
        // Phase 393 — the static read-only table is now the `StaticRows` mode of `DataGrid`.
        NodeKind.DataGrid
            { SortStateKey = None
              Reorderable = false
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some
                    // Phase 801 — the sort-intent slots stay ABSENT here deliberately.
                    // `table-1.json` is the byte-identity anchor for the pre-801 wire:
                    // if this fixture's payload ever moves, the addition was not additive.
                    { DefaultSort = None
                      Headers = [ TextSource.Literal "Term"; TextSource.Literal "Definition" ]
                      Rows =
                        [ [ TextSource.Literal "MVU"; TextSource.Literal "Model-View-Update" ]
                          [ TextSource.Literal "DSL"; TextSource.Literal "Domain-specific language" ] ]
                      Sortable = None } })
        None

/// Phase 801 — the same static table DECLARING sort intent: `sortable: true` plus a
/// `defaultSort` naming the second column descending. The round-trip leg proves both
/// optional slots survive canonical encode/decode in every host; sitting beside
/// `table-1` it also proves the two forms are distinguishable on the wire, which is
/// the whole point of modelling absence as absence.
let tableSortable: Node<obj> =
    node
        "table-sortable-1"
        (NodeKind.DataGrid
            { SortStateKey = None
              Reorderable = false
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some
                    { DefaultSort =
                        Some
                            { Column = 1
                              Direction = SortDirection.Desc }
                      Headers = [ TextSource.Literal "Region"; TextSource.Literal "Revenue" ]
                      Rows =
                        [ [ TextSource.Literal "North"; TextSource.Literal "1200" ]
                          [ TextSource.Literal "South"; TextSource.Literal "980" ] ]
                      Sortable = Some true } })
        None

let mapVis: Node<obj> =
    node
        "map-1"
        (NodeKind.Map(
            { Source =
                Binding.Static(
                    Some(
                        [ { Latitude = 51.5
                            Longitude = -0.12
                            Label = "London" } ]
                        : MapMarker list
                    )
                )
              CentreLatitude = 51.5
              CentreLongitude = -0.12
              Zoom = 6
              OnMarkerClick = None }
        ))
        None

// ─── Custom + composite ─────────────────────────────────────────────────

let custom: Node<obj> =
    node
        "custom-1"
        (NodeKind.Custom(
            { ModuleId = "analytics"
              ComponentId = "trend-card"
              Props = Map.empty
              ContentHash = None
              // `None` ≡ the old `[]` — the key stays off the wire.
              ExposedNodeIds = None }
        ))
        None

// Custom with the bounded-escape additive
// fields populated. Exercises the wire-shape lock: contentHash + exposed-
// NodeIds round-trip through canonical JSON without precision loss.
let customBounded: Node<obj> =
    node
        "custom-bounded-1"
        (NodeKind.Custom(
            { ModuleId = "deal-flow"
              ComponentId = "QualityRing"
              Props = Map.empty
              ContentHash =
                Some
                    { Algorithm = "SHA256"
                      Hash = "abc123def456"
                      Strictness = HashStrictness.StrictReplay }
              ExposedNodeIds = Some [ "quality-ring-segment-1"; "quality-ring-segment-2" ] }
        ))
        None

let customBoundedAdvisory: Node<obj> =
    node
        "custom-bounded-advisory"
        (NodeKind.Custom(
            { ModuleId = "deal-flow"
              ComponentId = "TrendCard"
              Props = Map.empty
              ContentHash =
                Some
                    { Algorithm = "SHA256"
                      Hash = "fedcba654321"
                      Strictness = HashStrictness.AdvisoryWarning }
              // `None` ≡ the old `[]` — the key stays off the wire.
              ExposedNodeIds = None }
        ))
        None

// ErrorBoundary fixture pinning the wire-form
// round-trip. The canonical-JSON encoder emits `{ "$type": "ErrorBoundary",
// "child": <node>, "fallback": <node> }`; the decoder reverses it back
// to `NodeKind.ErrorBoundary { Child = ...; Fallback = ... }`.
let errorBoundary: Node<obj> =
    node
        "boundary-1"
        (NodeKind.ErrorBoundary
            { Child = node "boundary-child" (NodeKind.Markdown({ Text = TextSource.Literal "Child body" })) None
              Fallback =
                node
                    "boundary-fallback"
                    (NodeKind.Callout(
                        { Tone = ToneVariant.Warning
                          Heading = Some(TextSource.Literal "Couldn't render")
                          Body = TextSource.Literal "Fallback rendered"
                          Icon = None
                          Dismissable = false }
                    ))
                    None })
        None

// Switch fixture (Phase 392) pinning the wire-form round-trip. The encoder
// emits `{ "$type":"Switch", "cases":[{"child":<node>,"match":<string>},…],
// "default":<node>, "stateKey":<string> }`; the decoder reverses it to
// `NodeKind.Switch { StateKey = …; Cases = …; Default = … }`. Two cases + a
// distinct default exercise the case-array round-trip and the fallback leg.
// Phase 768 — the Switch selector widened to any Binding: `on` takes a
// Selection, so the branch FOLLOWS THE CLICKED ROW with no writer at all.
//
// This is the 032/c6 shape done right. The failing emissions wired a Switch to
// a stateKey nothing emittable could write (SetState.value is a literal; a
// grid's onRowClick is a host closure) — the models had the right intent
// against a dead end. Moving the READ side is what closes it: the selector
// resolves the selected row's `status` field (defaulted pre-click per the
// Phase 629 law), and first-match-wins picks the branch.
//
// The State form keeps its compact `stateKey` spelling on the wire — this
// fixture is the `on` spelling's coverage; `switch-1` pins the compact form.
let switchOnSelection: Node<obj> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType; "status", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "WARD-A"; Fuaran.Core.Str "WARD-B" ]
                  Fuaran.Core.Column.create
                      "status"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "steady"; Fuaran.Core.Str "critical" ] ] }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "switch-on-selection"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "ward-grid"
                      (NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Transform(TransformSource.Data(source), [], None)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ward" "id"; fieldCol "Status" "status" ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "ward-status-panel"
                      (NodeKind.Switch(
                          { On =
                              Binding.Selection(
                                  "ward-grid",
                                  Binding.projectSelectionField<string> "status",
                                  Some "steady",
                                  Some "status"
                              )
                            Cases =
                              [ { Match = "critical"
                                  Child =
                                    node
                                        "ward-critical"
                                        (NodeKind.Callout(
                                            { Tone = ToneVariant.Critical
                                              Heading = Some(TextSource.Literal "Ward at capacity")
                                              Body = TextSource.Literal "Escalate admissions to the on-call manager."
                                              Icon = None
                                              Dismissable = false }
                                        ))
                                        None } ]
                            Default =
                              node
                                  "ward-steady"
                                  (NodeKind.Markdown({ Text = TextSource.Literal "Occupancy within normal range." }))
                                  None }
                      ))
                      None ] }
        ))
        None

let switchBasic: Node<obj> =
    node
        "switch-1"
        (NodeKind.Switch
            { On = Binding.State("view", None)
              Cases =
                [ { Match = "details"
                    Child = node "switch-details" (NodeKind.Markdown({ Text = TextSource.Literal "Details view" })) None }
                  { Match = "summary"
                    Child = node "switch-summary" (NodeKind.Markdown({ Text = TextSource.Literal "Summary view" })) None } ]
              Default =
                node
                    "switch-default"
                    (NodeKind.Callout(
                        { Tone = ToneVariant.Info
                          Heading = Some(TextSource.Literal "Pick a view")
                          Body = TextSource.Literal "No view selected"
                          Icon = None
                          Dismissable = false }
                    ))
                    None })
        None

// Fragment fixtures. `fragmentDecl` carries a labelled body
// (Markdown so the round-trip exercises a leaf inside the body too);
// `fragmentRef` carries only the target name (the body lives at the
// decl site, not duplicated on the wire).
let fragmentDecl: Node<obj> =
    node
        "frag-decl-1"
        (NodeKind.FragmentDecl
            { Name = "card-template"
              Body = node "frag-body" (NodeKind.Markdown({ Text = TextSource.Literal "Template body" })) None
              // `None` ≡ the old zero-holes / pure-deterministic defaults —
              // both keys stay off the wire.
              Holes = None
              Effect = None })
        None

let fragmentRef: Node<obj> =
    node
        "frag-ref-1"
        (NodeKind.FragmentRef
            { Name = "card-template"
              // `None` ≡ the old empty Map — the key stays off the wire.
              Args = None })
        None

// Parameterised-fragment fixtures (Phase 180). The decl exercises every hole
// shape (a defaulted value hole, a bounded value hole, a slot with a kind
// constraint, a bounded Repeat) plus a non-pure effect class; the ref binds a
// value scalar + a slot subtree. These are additive — the two fixed-body
// fixtures above stay byte-identical (zero holes / zero args ⇒ fields omitted).
let fragmentDeclParam: Node<obj> =
    node
        "frag-decl-param"
        (NodeKind.FragmentDecl
            { Name = "stat-card"
              Body = node "param-body" (NodeKind.Markdown({ Text = TextSource.Literal "Parameterised body" })) None
              Holes =
                Some
                    [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "Untitled"))
                      HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), None)
                      HoleDecl.Slot("content", Some "Display")
                      HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 12)) ]
              Effect =
                Some
                    { HostEffect = HostEffect.ReadsHost
                      Determinism = DeterminismSource.Clock } })
        None

let fragmentRefArgs: Node<obj> =
    node
        "frag-ref-args"
        (NodeKind.FragmentRef
            { Name = "stat-card"
              Args =
                Some(
                    Map.ofList
                        [ "count", FragmentArg.Int 7
                          "content",
                          FragmentArg.SlotArg(
                              node "slot-tree" (NodeKind.Markdown({ Text = TextSource.Literal "Bound slot" })) None
                          ) ]
                ) })
        None

// Mount fixtures (Phase 265, §4o — the isolation/embedding boundary).
// `mountMinimal` is the default-deny, out-only, zero-input degenerate shape
// (empty capabilities → the explicit `[]` default-deny posture on the wire, no
// `inputs`, no `messageShape`); `mountFull` exercises a capability list, a
// declared TwoWay channel + message shape, and both a value + slot input
// (reusing the FragmentArg encoding). `onBubble` is a closure → the
// `"<closure>"` sentinel on the wire; the guest interior is a scope reference,
// never an inlined tree.
let mountMinimal: Node<obj> =
    node
        "mount-1"
        (NodeKind.Mount
            { ScopeId = "guest-sidebar"
              // `None` ≡ the old empty Map — the key stays off the wire.
              Inputs = None
              Channel =
                { Direction = ChannelDirection.OutOnly
                  MessageShape = None }
              // `Some` — keeps the `"onBubble":"<closure>"` sentinel on the wire.
              OnBubble = Some(fun _ -> Action.Chain [])
              Capabilities = [] })
        None

let mountFull: Node<obj> =
    node
        "mount-2"
        (NodeKind.Mount
            { ScopeId = "guest-metrics"
              Inputs =
                Some(
                    Map.ofList
                        [ "title", FragmentArg.Str "Metrics"
                          "seed",
                          FragmentArg.SlotArg(
                              node
                                  "seed-tree"
                                  (NodeKind.Markdown({ Text = TextSource.Literal "Initial guest state" }))
                                  None
                          ) ]
                )
              Channel =
                { Direction = ChannelDirection.TwoWay
                  MessageShape = Some "MetricsMsg" }
              OnBubble = Some(fun _ -> Action.Chain [])
              Capabilities = [ "notify"; "call:reports.*" ] })
        None

let composite: Node<obj> =
    node
        "composite-root"
        (NodeKind.Box(
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children =
                [ node
                      "composite-card"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Composite")
                            Children = [ withId "metric-2" metric; labelValueRow ] }
                      ))
                      None
                  stack ] }
        ))
        None

// ─── TreeOp fixtures (10 cases) ─────────────────────────────────────────

let opEditNode: TreeOp<obj> =
    TreeOp.EditNode(NodeId "metric-1", NodeKind.Markdown({ Text = TextSource.Literal "Edited" }))

let opUpdateProp: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "metric-1", "Label", PropValue.Wire(JStr "Updated revenue"))

let opReplaceBinding: TreeOp<obj> =
    TreeOp.ReplaceBinding(NodeId "metric-1", "Value", Binding.Static(Some(box 99.5)))

let opUpdateStyle: TreeOp<obj> =
    TreeOp.UpdateStyle(
        NodeId "metric-1",
        { Tone = ToneVariant.Success
          Weight = StyleWeight.Spacious
          Emphasis = Emphasis.Loud
          Role = StyleRole.None
          Voice = FontVoice.Default }
    )

/// Phase 147 — an `UpdateStyle` op whose `SemanticStyle` carries a non-default
/// `Role` / `Voice`, exercising the optional-emit wire path at the op level.
let opUpdateStyleRoleVoice: TreeOp<obj> =
    TreeOp.UpdateStyle(
        NodeId "metric-1",
        { Tone = ToneVariant.Default
          Weight = StyleWeight.Standard
          Emphasis = Emphasis.Normal
          Role = StyleRole.Eyebrow
          Voice = FontVoice.Structural }
    )

let opUpdateState: TreeOp<obj> =
    TreeOp.UpdateState(
        NodeId "metric-1",
        { OnLoading = Some skeleton
          OnEmpty = None
          OnError = None }
    )

let opInsertChild: TreeOp<obj> = TreeOp.InsertChild(NodeId "dash-empty", metric)

let opRemoveNode: TreeOp<obj> = TreeOp.RemoveNode(NodeId "metric-1")

let opMoveNode: TreeOp<obj> = TreeOp.MoveNode(NodeId "metric-1", NodeId "card-1")

let opReorderChildren: TreeOp<obj> =
    TreeOp.ReorderChildren(NodeId "stack-1", [ NodeId "markdown-1"; NodeId "metric-1" ])

let opBatch: TreeOp<obj> = TreeOp.Batch [ opUpdateStyle; opRemoveNode ]

// ─── Nested-path UpdateProp fixtures (Phase 364 — WIRE_FORMAT.md §3.4) ────
//
// Six primitive-valued round-trips + one OBJECT-valued round-trip + three
// canonical apply-rejects. The `value` payload is a structured JSON position
// (rule 12): an object-valued `UpdateProp.value` (a `$type` object such as a
// `TextSource`) round-trips byte-identically — the object-valued fixture
// pins exactly the class the pre-`PropValue` encoder collapsed to
// `"<opaque>"`. Targets (`grid-1` / `chart-1` / `form-1` / `tabs-1`) live in
// the apply-parity base tree, so each fixture's apply outcome is also pinned
// cross-pipeline by the Phase 192 golden — the three reject fixtures land
// there as ERR:PositionOutOfRange / ERR:FieldNotFound / ERR:PathInvalid.

let opUpdatePropNestedColumn0Label: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[0].Label", PropValue.Wire(JStr "Channel name"))

let opUpdatePropNestedColumn1Label: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[1].Label", PropValue.Wire(JStr "Spend (GBP)"))

let opUpdatePropNestedYField0: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "chart-1", "YFields[0]", PropValue.Wire(JStr "sales"))

let opUpdatePropNestedYField1: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "chart-1", "YFields[1]", PropValue.Wire(JStr "profit"))

let opUpdatePropNestedField0Required: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "form-1", "Fields[0].Required", PropValue.Wire(JBool true))

let opUpdatePropNestedField1Required: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "form-1", "Fields[1].Required", PropValue.Wire(JBool false))

/// Object-valued nested UpdateProp (rule 12): a `CellFormat` `$type` object
/// as the value — the class the pre-`PropValue` encoder collapsed to
/// `"<opaque>"` on re-encode. Byte-stable round-trip AND a clean apply
/// (grid-1's `Columns[0].Format` takes a `CellFormat`) are the pinned claims.
let opUpdatePropNestedObjectValue: TreeOp<obj> =
    TreeOp.UpdateProp(
        NodeId "grid-1",
        "Columns[0].Format",
        PropValue.Wire(JObj [ "$type", JStr "Currency"; "code", JStr "GBP" ])
    )

let opUpdatePropNestedBadIndex: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[9].Label", PropValue.Wire(JStr "Out of range"))

let opUpdatePropNestedBadField: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[0].Nope", PropValue.Wire(JStr "No such sub-field"))

let opUpdatePropNestedMalformed: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "grid-1", "Columns[x].Label", PropValue.Wire(JStr "Bad index literal"))

// ─── Binding.Local fixture ───────────────────────────────────
//
// Form whose single Text field is bound to a `Binding.Local` carrying:
//   - InitialFrom = State "salary" default 50000.0
//   - FlushOn = OnBlur
//   - OnCommit = a closure returning a placeholder Action.Chain []
//   - Format = Some <closure>
//   - Parse = <closure>
//
// The encoder writes Format / Parse / OnCommit as <closure> sentinels,
// the decoder rebuilds Format = None / Parse = placeholder-error / OnCommit
// returning the sentinel. The round-trip stays clean because re-encoding
// the decoded shape produces the same <closure> sentinel string in each
// slot.

let formLocalText: Node<obj> =
    let localFloat: Binding<string> =
        Binding.Local(
            LocalFlushTrigger.OnBlur,
            (fun (s: string) -> s),
            Binding.State("salary", Some ""),
            Some(fun _ -> box (Action.Chain []: Action<obj>)),
            (fun (raw: string) -> Ok raw)
        )

    let textField: FormField<obj> =
        { Id = "salary-input"
          Label = TextSource.Literal "Salary"
          Kind = FormFieldKind.Text(Some localFloat, Some(fun _ -> placeholderChain))
          Required = false
          Help = None }

    node
        "form-local-1"
        (NodeKind.Form(
            { Fields = [ textField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

// Form whose single Text field uses `Binding.Local` with FlushOn =
// OnDebounce 250 — exercises the payload-carrying flush trigger.
let formLocalDebounce: Node<obj> =
    let localDebounce: Binding<string> =
        Binding.Local(
            LocalFlushTrigger.OnDebounce 250,
            id,
            Binding.Static(Some "draft@example.com"),
            Some(fun _ -> box (Action.Chain []: Action<obj>)),
            (fun raw -> Ok raw)
        )

    let textField: FormField<obj> =
        { Id = "email-input"
          Label = TextSource.Literal "Email"
          Kind = FormFieldKind.Text(Some localDebounce, Some(fun _ -> placeholderChain))
          Required = true
          Help = None }

    node
        "form-local-debounce"
        (NodeKind.Form(
            { Fields = [ textField ]
              OnSubmit = placeholderChain
              SubmitLabel = TextSource.Literal "Save"
              Disabled = Option.None }
        ))
        None

// Action.CommitLocal fixture for the TreeOp round-trip suite.
let opUpdatePropCommitLocal: TreeOp<obj> =
    // A typical "Apply" button shape: an UpdateStyle on a button whose
    // OnClick = Action.CommitLocal "salary-input" — encoded as a typed
    // ButtonSpec carrying the action. Routes through UpdateProp's
    // Binding/style/state slots rather than introducing a fresh op kind;
    // the Action surfaces via the canonical ButtonSpec.OnClick path.
    TreeOp.UpdateStyle(
        NodeId "btn-apply",
        { Tone = ToneVariant.Brand
          Weight = StyleWeight.Standard
          Emphasis = Emphasis.Normal
          Role = StyleRole.None
          Voice = FontVoice.Default }
    )

// Binding.Format (Phase 102) — a Stack of Markdown nodes whose Text is a
// `TextSource.Bound(Binding.Format(...))`, exercising every Format case
// (Number / Currency / Percent / Date / RelativeTime) and both LocaleSource
// variants (Explicit + Ambient) across locales. The numeric source is a
// `Binding.Static` so the round-trip is fully faithful.
let formatBindings: Node<obj> =
    let md (id: string) (b: Binding<string>) : Node<obj> =
        node id (NodeKind.Markdown({ Text = TextSource.Bound b })) None

    node
        "format-bindings"
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ md
                      "fmt-number"
                      (Binding.Format(Binding.Static(Some 1234.5), Format.Number(Some 2), LocaleSource.Explicit "en-US"))
                  md
                      "fmt-currency"
                      (Binding.Format(Binding.Static(Some 1234.5), Format.Currency "GBP", LocaleSource.Explicit "en-GB"))
                  md
                      "fmt-percent"
                      (Binding.Format(Binding.Static(Some 0.42), Format.Percent None, LocaleSource.Ambient))
                  md
                      "fmt-date"
                      (Binding.Format(
                          Binding.Static(Some 1700000000.0),
                          Format.Date DateStyle.Medium,
                          LocaleSource.Explicit "fr-FR"
                      ))
                  md
                      "fmt-relative"
                      (Binding.Format(
                          Binding.Static(Some(-3.0)),
                          Format.RelativeTime RelativeTimeUnit.Day,
                          LocaleSource.Explicit "en-US"
                      )) ] }
        ))
        None

// ─── Phase 818 — the reactive-derivation first cut ────────────────────────
//
// Four wire shapes, one rule (any read slot may take a Binding; subscription
// semantics; the Transform verbs stay the only computation vocabulary):
//   1. a LIVE State-sourced Transform — the Tier-D count badge done right
//      (the 815 snapshot upgraded: the source binding is preserved, its
//      carried defaultValue is the initial snapshot);
//   2. `SetState.valueFrom` — a derived state write (value XOR valueFrom);
//   3. `sortStateKey` — the data-bound grid-sort header affordance.
// (`Switch.on` shipped with Phase 768 — `switchOnSelection` above pins it.)

/// The Tier-D count badge, live: a Badge whose label derives from a
/// State-carried request log (initial rows in the binding's defaultValue), so
/// a `SetState("request-log", …)` re-derives the count.
let badgeTransformLive: Node<obj> =
    let defaultRows =
        JArr
            [ JObj [ "medication", JStr "Amoxicillin"; "quantity", JInt 20 ]
              JObj [ "medication", JStr "Ibuprofen"; "quantity", JInt 50 ] ]

    let source: Binding<JVal> = Binding.State("request-log", Some defaultRows)

    let initial =
        match Fuaran.UI.HostPrelude.TransformLive.initialSource defaultRows with
        | Ok ds -> ds
        | Error e -> failwithf "badgeTransformLive initial snapshot failed: %A" e

    let pipeline =
        [ Fuaran.Core.GroupBy(
              [],
              [ { Name = "n"
                  Fn = Fuaran.Core.AggFn.Count
                  Of = "medication" } ]
          ) ]

    node
        "badge-transform-live"
        (NodeKind.Badge(
            { Label = TextSource.Bound(Binding.Transform(TransformSource.Live(source, initial), pipeline, None))
              Variant = BadgeVariant.Info }
        ))
        None

/// Phase 861 — sort on a DATA-BOUND grid: per-column `sortable` narrowing plus
/// a declared initial order, reusing the `defaultSort` record and field name the
/// `staticRows` path already carries (Phase 801). The middle column opts OUT —
/// the declaration "implied by omission" could not previously express.
let gridBoundSort: Node<obj> =
    let col (label: string) (field: string) (sortable: bool option) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = sortable
          Editable = None
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-bound-sort"
        (NodeKind.DataGrid(
            { SortStateKey = Some "ledger-sort"
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort =
                Some
                    { Column = 1
                      Direction = SortDirection.Desc }
              Source = Binding.State("ledger", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" None
                  col "Revenue" "revenue" None
                  col "Note" "note" (Some false) ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 863 — the grid's write side, declared: `editStateKey` names where an
/// edit commits, and the third column is explicitly read-only under a
/// grid-level `editable: true`. Both are declarations that previously had no
/// spelling — the destination was a closure erasing to `"<closure>"`, and
/// read-only-ness was implied by omission.
let gridDeclaredEdit: Node<obj> =
    let col (label: string) (field: string) (editable: bool option) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = editable
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-declared-edit"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = Some "stock-adjustments"
              DefaultSort = None
              Source = Binding.Query("stock", (fun _ -> Seq.ofList planRows), None)
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" None
                  col "Revenue" "revenue" None
                  col "Note" "note" (Some false) ]
              OnRowClick = None
              Editable = true
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 862 — declarative pagination: `pageStateKey` names the State slot
/// carrying `{"page": N}` (1-based) and `pageSize` how many rows a page holds.
/// The pager that writes the key is renderer-owned, so the tree names the
/// behaviour and never a control — which is why there is no pager node here to
/// pair the grid with.
let gridPaged: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-paged"
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = Some 20
              PageStateKey = Some "members-page"
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("members", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// Phase 862 — paging and sorting compose on one grid: two behaviours, two
/// state keys, one rule. Present as a fixture because the pair is the shape the
/// charter's "one rule, three instances" claim is actually cashed in, and a
/// host that special-cased either would round-trip this one wrongly.
let gridPagedSorted: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-paged-sorted"
        (NodeKind.DataGrid(
            { SortStateKey = Some "ledger-sort"
              PageSize = Some 10
              PageStateKey = Some "ledger-page"
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("ledger", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

/// A derived state write: clicking the button writes the SELECTED row's `id`
/// field to the `chosen-id` state slot — `valueFrom` evaluated at dispatch
/// time, no closure, no literal.
let buttonSetStateValueFrom: Node<obj> =
    let selectedId: Binding<JVal> =
        Binding.Selection("orders-grid", Binding.projectSelectionField<JVal> "id", None, Some "id")

    node
        "button-setstate-valuefrom"
        (NodeKind.Button(
            { Label = TextSource.Literal "Track this order"
              OnClick = Action.SetState("chosen-id", None, Some selectedId)
              Variant = ButtonVariant.Secondary
              Icon = None
              Tooltip = None
              Disabled = None }
        ))
        None

/// The data-bound grid-sort affordance: `sortStateKey` names the State slot
/// carrying `{column, direction}`; the runtime renders sortable headers
/// (field-named columns only) and sorts resolved rows by the descriptor.
let gridSortStateKey: Node<obj> =
    let col (label: string) (field: string) (kind: CellKindErased<obj>) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
          Sortable = None
          Editable = None
          Format = CellFormat.None
          Kind = kind
          Width = ColumnWidth.Auto }

    node
        "grid-sort-state-key"
        (NodeKind.DataGrid(
            { SortStateKey = Some "inventory-sort"
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.State("inventory", Some(Seq.ofList planRows))
              RowKey = None
              RowKeyField = Some "month"
              Columns =
                [ col "Month" "month" CellKindErased.Text
                  col "Revenue" "revenue" CellKindErased.Numeric ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        ))
        None

// ─── Public collections ─────────────────────────────────────────────────

let allNodes: (string * Node<obj>) list =
    [ "Display/Heading", heading
      "Display/Markdown (Phase 147 Role=Data + Voice=Display)", styleRoleVoice
      "Display/Markdown", markdown
      "Display/Metric", metric
      "Display/Metric (float divergence-zone — 1e21 scientific)", metricFloatExpPos
      "Display/Metric (float divergence-zone — 1e-7 scientific)", metricFloatExpNeg
      "Display/Metric (float divergence-zone — 17 significant digits)", metricFloat17Sig
      "Display/Metric (float divergence-zone — integer > 2^53)", metricFloatBigInt
      "Display/Badge", badge
      "Display/Link", link
      "Display/Link (protected email — Phase 812 protection field)", linkProtected
      "Display/Image (Avatar variant)", image
      "Display/List (ordered)", listDisplay
      "Display/Toast (Success tone, open)", toast
      "Display/CodeBlock (fsharp, line numbers + highlights)", codeBlock
      "Display/Math (block LaTeX)", math
      "Display/Drawing (all shapes + curve commands + styled bindings)", drawing
      "Display/Drawing (degenerate — empty)", drawingMinimal
      "Display/Drawing (Phase 877 — rotated Labels, incl. explicit 0 and 2-dp fraction)", drawingRotatedLabels
      "Display/Drawing (Phase 883 — tipped shapes, incl. hostile text, Bound tip and explicit empty)",
      drawingTippedShapes
      "Display/Sparkline", sparkline
      "Display/Skeleton", skeleton
      "Display/Callout", callout
      "Display/Progress", progress
      "Display/LabelValueRow", labelValueRow
      "Display/Fact", fact
      "Display/Metric (Phase 819 — CellFormat.Duration value + cell RelativeTime trend)", metricDuration
      "Display/Icon (Phase 821 — decorative, no label, Large)", iconDecorative
      "Layout/Dashboard (empty)", dashboardEmpty
      "Layout/Stack", stack
      "Layout/Grid", gridLayout
      "Layout/Grid (TemplateColumns 1fr 2fr ratio)", gridLayoutTemplatedRatio
      "Layout/Grid (TemplateColumns 100px + repeat fixed-plus-flex)", gridLayoutTemplatedFixedPlusFlex
      "Layout/Grid (TemplateColumns auto-fit minmax)", gridLayoutTemplatedAutoFit
      "Layout/SplitPanel", splitPanel
      "Layout/Tabs", tabs
      "Layout/Tabs (explicit headers + tags + activeTag)", tabsExplicitHeaders
      "Layout/Tabs (composite — containers inside a wrapper, grid panel, pre-filled controls)", compositeTabsPanels
      "Layout/Card", card
      "Layout/Stepper", stepper
      "Layout/SummaryList", summaryList
      "Layout/Disclosure", disclosure
      "Layout/Modal (heading + child + onDismiss)", modal
      "Layout/ScrollArea (vertical, maxHeight)", scrollArea
      "Input/Form (all fields)", formAllFields
      "Input/Form (Phase 766 — the Toggle switch affordance beside a Checkbox)", formToggle
      "Input/Form (RangedNumber — all/min-only/no bounds)", formRangedNumber
      "Input/Form (Local-bound text, OnBlur)", formLocalText
      "Input/Form (Local-bound text, OnDebounce 250)", formLocalDebounce
      "Input/Filters (text + choice)", filtersBoth
      "Input/Filters (declarative — omitted onChange + typed range bounds)", filtersDeclarative
      "Input/Form (SegmentedChoice horizontal + vertical)", formSegmentedChoice
      "Input/Form (Date — date/time/datetime variants + bounds)", formDate
      "Input/Form (Phase 725 — DateRange: single-control date range, bare {from,to} pair + bounds)", formDateRange
      "Input/Filters (Phase 725 — DateRange chip: one filter param carries the pair, value auto-bound)",
      filtersDateRange
      "Input/Filters (SegmentedFilter horizontal)", filtersSegmented
      "Input/Button", button
      "Input/Button (Action.WriteToClipboard chained with Dispatch)", buttonClipboard
      "Input/Button (Action.ReadFileBody base64)", buttonReadFile
      "Input/Button (Notify / SetState / AiTool — JSON payloads)", buttonJsonPayloads
      "Input/FileUpload", fileUpload
      "Input/Select", select
      "Input/Select (multi-select — list value)", multiSelect
      "Input/Form (Phase 426 — handler-free write-back fields, State-bound)", formDeclarative
      "Input/Form (Phase 596 — symmetric auto-bind, omitted-value fields)", formDeclarativeMinimal
      "Layout/Stack (Phase 426 — declarative tabs + modal + disclosure + select, handlers omitted)", controlsDeclarative
      "Layout/Stack (Phase 426 — closure-authored onSelectTag / onToggle / onChangeMulti sentinels)", multiSelectClosure
      "Layout/Stack (Phase 428 — Action.Call result targets: closure / into State / into Query)", callInto
      "Visualisation/Grid", gridVis
      "Visualisation/Grid (Phase 282 — Binding.Transform compute source)", gridTransform
      "Visualisation/Grid (Phase 424 — parameterised Binding.Transform, filter param from a chip)", gridTransformParam
      "Display/Metric (Phase 421 — Binding.Query with a declared dependsOn filter edge)", queryDependsOn
      "Visualisation/Grid (Phase 425 — field-named columns + RowKeyField, closure-free)", gridFieldNamed
      "Visualisation/Grid (Phase 750 — TonedPill: value-conditional cell tone declared as a value→tone map)",
      gridTonedPill
      "Layout/Box (filterable-static dashboard — Filters params wired through Transform to chart + grid)",
      filterableStaticDashboard
      "Layout/Box (master-detail — grid + detail card State-bound with a pre-selected defaultValue)",
      masterDetailPreselected
      "Layout/Box (master-detail — Selection defaultValue naming a NON-FIRST row: prune-vs-seed is observable)",
      masterDetailPreselectedSecondRow
      "Layout/Box (Phase 767 — the canonical empty state with a CTA: one Card region, no nested Callout)",
      emptyStateCard
      "Layout/Box (master-detail — ONE selection feeding N slots, each projecting a DIFFERENT field)",
      masterDetailMultiField
      "Layout/Box (Phase 632 — Transform in scalar slots: selected-row Callout body + Badge count)",
      scalarTransformComposition
      "Binding/Now (Phase 765 — the host-furnished instant: a text slot + a Transform param feeding dateDiffDays)",
      nowEnvironmentBinding
      "Display/Metric (Phase 283 — Binding.Invoke capability source)", metricInvoke
      "Input/Button (Phase 283 — Action.Invoke capability effect)", buttonInvoke
      "Visualisation/Chart", chart
      "Visualisation/Grid (Phase 663/665 — editable State-sourced grid, typed rows on the wire)", gridEditableState
      "Visualisation/Chart (Phase 663/665 — chart on the editable grid's state key)", chartStateRows
      "Visualisation/Chart (Phase 876 — valueFormat: the value axis's declared number format)", chartValueFormat
      "Visualisation/Chart (Phase 878 — xTitle/yTitle/subtitle: the axis names + the muted subtitle)", chartAxisTitles
      "Visualisation/Chart (Phase 880 — legendPosition: the legend's declared edge)", chartLegendPosition
      "Visualisation/Chart (Phase 881 — dataLabels: values written onto the picture)", chartDataLabels
      "Visualisation/Chart (Phase 882 — xScale: a temporal x-axis over ISO-8601 date cells)", chartTemporalX
      "Visualisation/Grid (static-table mode — staticRows; absorbed the retired Table kind)", table
      "Visualisation/Grid (Phase 801 — static-table mode declaring sort intent: sortable + defaultSort)", tableSortable
      "Visualisation/Grid (Phase 818 — sortStateKey: the data-bound grid-sort header affordance)", gridSortStateKey
      "Visualisation/Grid (Phase 861 — bound-path sort: per-column sortable narrowing + a declared initial order)",
      gridBoundSort
      "Visualisation/Grid (Phase 863 — declared edit destination + per-column read-only narrowing)", gridDeclaredEdit
      "Visualisation/Grid (Phase 862 — pageStateKey + pageSize: declarative pagination, renderer-owned pager)",
      gridPaged
      "Visualisation/Grid (Phase 862 — paging and sorting composed: two behaviours, two state keys, one rule)",
      gridPagedSorted
      "Display/Badge (Phase 818 — LIVE State-sourced Transform: the Tier-D count badge, preserved source + initial snapshot)",
      badgeTransformLive
      "Input/Button (Phase 818 — SetState.valueFrom: a derived state write from the selected row's field)",
      buttonSetStateValueFrom
      "Visualisation/Map", mapVis
      "Custom", custom
      "Custom (bounded escape, StrictReplay hash + exposed-ids)", customBounded
      "Custom (bounded escape, AdvisoryWarning hash + no exposed-ids)", customBoundedAdvisory
      "ErrorBoundary (Markdown child + Callout fallback)", errorBoundary
      "Switch (view state → details/summary cases + info default)", switchBasic
      "Meta/Switch (Phase 768 — the selector widened: `on` takes a Selection, the branch follows the clicked row)",
      switchOnSelection
      "FragmentDecl (named template with Markdown body)", fragmentDecl
      "FragmentRef (name-only wire shape)", fragmentRef
      "FragmentDecl (parameterised — value/slot/repeat holes + effect class)", fragmentDeclParam
      "FragmentRef (parameterised — value + slot args)", fragmentRefArgs
      "Mount (§4o — out-only, default-deny, zero-input degenerate)", mountMinimal
      "Mount (§4o — capabilities + TwoWay message shape + value/slot inputs)", mountFull
      "Composite (Dashboard ⊃ Card ⊃ Metric + Stack)", composite
      "Binding.Format (number/currency/percent/date/relativeTime across locales)", formatBindings ]

let opReplaceRoot: TreeOp<obj> = TreeOp.ReplaceRoot composite

let allOps: (string * TreeOp<obj>) list =
    [ "EditNode", opEditNode
      "UpdateProp", opUpdateProp
      "ReplaceBinding", opReplaceBinding
      "UpdateStyle", opUpdateStyle
      "UpdateStyle (Phase 147 Role=Eyebrow + Voice=Structural)", opUpdateStyleRoleVoice
      "UpdateState", opUpdateState
      "InsertChild", opInsertChild
      "RemoveNode", opRemoveNode
      "MoveNode", opMoveNode
      "ReorderChildren", opReorderChildren
      "ReplaceRoot", opReplaceRoot
      "Batch", opBatch
      "UpdateProp-nested-column0-label", opUpdatePropNestedColumn0Label
      "UpdateProp-nested-column1-label", opUpdatePropNestedColumn1Label
      "UpdateProp-nested-yfield0", opUpdatePropNestedYField0
      "UpdateProp-nested-yfield1", opUpdatePropNestedYField1
      "UpdateProp-nested-field0-required", opUpdatePropNestedField0Required
      "UpdateProp-nested-field1-required", opUpdatePropNestedField1Required
      "UpdateProp-nested-object-value", opUpdatePropNestedObjectValue
      "UpdateProp-nested-badindex", opUpdatePropNestedBadIndex
      "UpdateProp-nested-badfield", opUpdatePropNestedBadField
      "UpdateProp-nested-malformed", opUpdatePropNestedMalformed ]
