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

let private emptyState: StateBehaviour<obj> =
    { OnLoading = None
      OnEmpty = None
      OnError = None }

let private node (id: string) (kind: NodeKind<obj>) (accessibility: Accessibility option) : Node<obj> =
    { Id = NodeId id
      Kind = kind
      State = emptyState
      Style = defaultStyle
      Accessibility = accessibility
      Motion = None
      ExtraAttributes = None }

// ─── Display fixtures ────────────────────────────────────────────────────

let private metricSpec: MetricSpec =
    { Label = TextSource.Literal "Revenue"
      Value = Binding.Static 1234.5
      Format = CellFormat.Currency "GBP"
      Tone = ToneVariant.Brand
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Trend = Some(Binding.Static 0.07)
      TrendFormat = Some(CellFormat.Percent(Some 1))
      Icon = Some(IconSource "trending-up")
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
                Value = Binding.Static value
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
            { Href = Binding.Static "/about"
              Label = TextSource.Literal "About us"
              Rel = Some "noopener"
              Target = Some "_blank"
              Download = false }
        ))
        None

let image: Node<obj> =
    // Phase 287 — Avatar variant exercises the variant DU; Src round-trips a
    // Binding<string> (sanitised at render, not at wire).
    node
        "image-1"
        (NodeKind.Image(
            { Src = Binding.Static "/avatar.png"
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
              Open = Binding.Static true
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
    node "spark-1" (NodeKind.Sparkline({ Source = Binding.Static(seq [ 1.0; 2.0; 3.0; 2.0; 4.0 ]) })) None

let private bareDrawStyle: DrawStyle =
    { Fill = None
      Stroke = None
      StrokeWidth = None
      Opacity = None
      TextAnchor = None
      FontSize = None
      Emphasis = None
      FontFamily = None
      MarkId = None }

let private styledDraw (fill: string) (stroke: string) : DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static fill)
        Stroke = Some(Binding.Static stroke)
        StrokeWidth = Some(Binding.Static 1.5)
        Opacity = Some(Binding.Static 0.9) }

/// A `Label` text style exercising the Phase 528.1 fields (anchor / size /
/// weight / font-family) — pins them cross-host in the corpus.
let private labelTextStyle: DrawStyle =
    { bareDrawStyle with
        Fill = Some(Binding.Static "#111111")
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


let skeleton: Node<obj> = node "skel-1" (NodeKind.Skeleton({ Rows = 3 })) None

let callout: Node<obj> =
    node
        "callout-1"
        (NodeKind.Callout(
            { Tone = ToneVariant.Warning
              Heading = Some(TextSource.Literal "Heads up")
              Body = TextSource.Literal "Live data is delayed."
              Icon = Some(IconSource "alert")
              Dismissable = true }
        ))
        None

let progress: Node<obj> =
    node
        "progress-1"
        (NodeKind.Progress(
            { Fraction = Binding.Static 0.42
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
              Value = Binding.Static 42.0
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
              Icon = Some(IconSource "user")
              Tone = ToneVariant.Brand
              Emphasis = true
              Help = Some(TextSource.Literal "Primary insured") }
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
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children = [ metric; markdown ] }
        ))
        None

let gridLayout: Node<obj> =
    node
        "glayout-1"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Grid
                    { Cols = 12
                      TemplateColumns = None
                      Gap = None }
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
            { Layout =
                BoxLayout.Grid
                    { Cols = 2
                      TemplateColumns = Some "1fr 2fr"
                      Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

let gridLayoutTemplatedFixedPlusFlex: Node<obj> =
    node
        "glayout-tpl-fixed"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Grid
                    { Cols = 4
                      TemplateColumns = Some "100px repeat(3, minmax(30px, 1fr))"
                      Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children = [ metric ] }
        ))
        None

let gridLayoutTemplatedAutoFit: Node<obj> =
    node
        "glayout-tpl-autofit"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Grid
                    { Cols = 1
                      TemplateColumns = Some "repeat(auto-fit, minmax(150px, 1fr))"
                      Gap = None }
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
            { Orientation = Horizontal
              Children = [ metric ]
              ActiveIndex = Binding.Static 0
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
            { Orientation = Horizontal
              Children = [ markdown; sparkline ]
              // Non-zero ActiveIndex (Phase 126) — exercises the
              // now-carried selected-tab binding round-tripping a value
              // other than the default 0.
              ActiveIndex = Binding.Static 1
              OnSelect = Some(fun _ -> Action.Chain [])
              TabHeaders =
                Some
                    [ { Label = TextSource.Literal "Overview"
                        Icon = Some(IconSource "overview-glyph")
                        Disabled = Option.None }
                      { Label = TextSource.Literal "Detail"
                        Icon = Option.None
                        Disabled = Some(Binding.Static false) } ]
              TabTags = Some [ "overview"; "detail" ]
              ActiveTag = Some(Binding.Static "overview")
              OnSelectTag = Option.None }
        ))
        None

let card: Node<obj> =
    node
        "card-1"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
              Role = BoxRole.Card
              Heading = Some(TextSource.Literal "Insights")
              Children = [ metric ] }
        ))
        None

let stepper: Node<obj> =
    node
        "step-1"
        (NodeKind.Stepper(
            { ActiveStep = Binding.Static 1
              Children = [ markdown; markdown ]
              OnSelect = (fun _ -> Action.Chain []) }
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
              Open = Binding.Static false
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
            { Open = Binding.Static false
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
          Kind = FormFieldKind.Text(Binding.Static "", Some(fun _ -> placeholderChain))
          Required = true
          Help = Some(TextSource.Literal "Full legal name") }

    let numberField: FormField<obj> =
        { Id = "age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Number(Binding.Static 0.0, Some(fun _ -> placeholderChain))
          Required = false
          Help = None }

    let checkboxField: FormField<obj> =
        { Id = "agree"
          Label = TextSource.Literal "I agree"
          Kind = FormFieldKind.Checkbox(Binding.Static false, Some(fun _ -> placeholderChain))
          Required = true
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static
                    [ { Value = "basic"
                        Label = TextSource.Literal "Basic" }
                      { Value = "pro"
                        Label = TextSource.Literal "Pro" } ],
                Binding.Static(Some "basic"),
                Some(fun _ -> placeholderChain)
            )
          Required = false
          Help = None }

    let textareaField: FormField<obj> =
        { Id = "notes"
          Label = TextSource.Literal "Notes"
          Kind = FormFieldKind.TextArea(Binding.Static "", Some(fun _ -> placeholderChain), 5)
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
              Disabled = Some(Binding.State("formBusy", false)) }
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
                Binding.Static 2024.0,
                Some(fun _ -> placeholderChain),
                { Min = Some 1979.0
                  Max = Some 2028.0
                  Step = Some 1.0 }
            )
          Required = true
          Help = None }

    let minOnlyField: FormField<obj> =
        { Id = "years"
          Label = TextSource.Literal "Years contributed"
          Kind =
            FormFieldKind.RangedNumber(
                Binding.Static 10.0,
                Some(fun _ -> placeholderChain),
                { Min = Some 0.0
                  Max = None
                  Step = None }
            )
          Required = false
          Help = None }

    let noBoundsField: FormField<obj> =
        { Id = "amount"
          Label = TextSource.Literal "Amount"
          Kind =
            FormFieldKind.RangedNumber(
                Binding.Static 100.0,
                Some(fun _ -> placeholderChain),
                { Min = None; Max = None; Step = None }
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
          Field = FormFieldKind.Text(Binding.Static "", Some(fun _ -> placeholderChain)) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Field =
            FormFieldKind.Choice(
                Binding.Static
                    [ { Value = "all"
                        Label = TextSource.Literal "All" } ],
                Binding.Static(Some "all"),
                Some(fun _ -> placeholderChain)
            ) }

    node "filters-1" (NodeKind.Filters([ textFilter; choiceFilter ])) None

/// Declarative chips (Phase 423): every `FilterKind` case with `onChange = None` — the AI-authored
/// shape whose `onChange` field is omitted on the wire, `value` self-reads its own `$filters.<name>`,
/// and `RangeFilter` carries typed `{min,max}` bounds. Proves the omitted-onChange + typed-range wire.
let filtersDeclarative: Node<obj> =
    let textFilter: FilterSpec<obj> =
        { Name = "q"
          Label = TextSource.Literal "Search"
          Field = FormFieldKind.Text(Binding.Filter("q", None), None) }

    let choiceFilter: FilterSpec<obj> =
        { Name = "tier"
          Label = TextSource.Literal "Tier"
          Field =
            FormFieldKind.Choice(
                Binding.Static
                    [ { Value = "all"
                        Label = TextSource.Literal "All" } ],
                Binding.Filter("tier", None),
                None
            ) }

    let rangeFilter: FilterSpec<obj> =
        { Name = "age"
          Label = TextSource.Literal "Age"
          Field = FormFieldKind.Range(Binding.Static(0.0, 100.0), None, None) }

    node "filters-declarative" (NodeKind.Filters([ textFilter; choiceFilter; rangeFilter ])) None

/// Round-trip cover for the parallel-additive
/// `FormFieldKind.SegmentedChoice` + `FilterKind.SegmentedFilter` cases.
/// Exercises both orientations so the canonical-JSON encoder's
/// orientation field and the decoder's required-field branch stay in
/// lockstep.
let formSegmentedChoice: Node<obj> =
    let opts =
        [ { Value = "effective"
            Label = TextSource.Literal "Effective" }
          { Value = "marginal"
            Label = TextSource.Literal "Marginal" }
          { Value = "takeHome"
            Label = TextSource.Literal "Take-home" } ]

    let horizontalField: FormField<obj> =
        { Id = "metric"
          Label = TextSource.Literal "Metric"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static opts,
                Binding.Static(Some "effective"),
                Some(fun _ -> placeholderChain),
                Horizontal
            )
          Required = false
          Help = None }

    let verticalField: FormField<obj> =
        { Id = "tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.SegmentedChoice(
                Binding.Static
                    [ { Value = "low"
                        Label = TextSource.Literal "Low" }
                      { Value = "high"
                        Label = TextSource.Literal "High" } ],
                Binding.Static None,
                Some(fun _ -> placeholderChain),
                Vertical
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
          Field =
            FormFieldKind.SegmentedChoice(
                Binding.Static
                    [ { Value = "table"
                        Label = TextSource.Literal "Table" }
                      { Value = "chart"
                        Label = TextSource.Literal "Chart" } ],
                Binding.Static(Some "table"),
                Some(fun _ -> placeholderChain),
                Horizontal
            ) }

    node "filters-segmented" (NodeKind.Filters([ segmentedFilter ])) None

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
                Binding.Static "2026-01-15",
                Some(fun _ -> placeholderChain),
                DateVariant.Date,
                { Min = Some "2026-01-01"
                  Max = Some "2026-12-31"
                  Step = None }
            )
          Required = true
          Help = None }

    let timeField: FormField<obj> =
        { Id = "alarm"
          Label = TextSource.Literal "Alarm"
          Kind =
            FormFieldKind.Date(
                Binding.Static "08:30",
                Some(fun _ -> placeholderChain),
                DateVariant.Time,
                { Min = None
                  Max = None
                  Step = Some 60.0 }
            )
          Required = false
          Help = None }

    let dateTimeField: FormField<obj> =
        { Id = "meeting"
          Label = TextSource.Literal "Meeting"
          Kind =
            FormFieldKind.Date(
                Binding.Static "2026-03-01T14:00",
                Some(fun _ -> placeholderChain),
                DateVariant.DateTime,
                { Min = None; Max = None; Step = None }
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
                Binding.Static("2026-03-01", "2026-03-08"),
                Some(fun _ -> placeholderChain),
                DateVariant.Date,
                { Min = Some "2026-01-01"
                  Max = Some "2026-12-31"
                  Step = None }
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
                Binding.State("shift", ("08:00", "17:00")),
                None,
                DateVariant.Time,
                { Min = None
                  Max = None
                  Step = Some 900.0 }
            )
          Required = false
          Help = None }

    let windowField: FormField<obj> =
        { Id = "window"
          Label = TextSource.Literal "Window"
          Kind =
            FormFieldKind.DateRange(
                Binding.Static("2026-03-01T09:00", "2026-03-01T17:00"),
                Some(fun _ -> placeholderChain),
                DateVariant.DateTime,
                { Min = None; Max = None; Step = None }
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
          Field =
            FormFieldKind.DateRange(
                Binding.Filter("stay", None),
                None,
                DateVariant.Date,
                { Min = None; Max = None; Step = None }
            ) }

    node "filters-date-range" (NodeKind.Filters([ stayChip ])) None

let button: Node<obj> =
    node
        "btn-1"
        (NodeKind.Button(
            { Label = TextSource.Literal "Refresh"
              OnClick = placeholderChain
              Variant = ButtonVariant.Primary
              Icon = Some(IconSource "refresh")
              Tooltip = None
              // Phase 129: bound disabled-state — the canonical
              // "disabled while a calc is in flight" shape.
              Disabled = Some(Binding.State("loading", false)) }
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
                      Action.SetState("draft", awkward)
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
                    { Id = "workbook-upload:0"
                      Handle = None },
                    FileReadEncoding.Base64,
                    (fun _ -> box "WorkbookLoaded")
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
                  OnClick = Action.Call(ApiEndpoint "/api/refresh", Some(fun _ -> placeholderChain), None)
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
                  OnClick = Action.Call(ApiEndpoint "/api/total", None, Some(CallResultTarget.IntoState "total"))
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
                    Value = Binding.State("total", 0.0) }
            ))
            None

    let intoQueryButton =
        node
            "btn-fetch-orders"
            (NodeKind.Button(
                { Label = TextSource.Literal "Fetch orders"
                  OnClick = Action.Call(ApiEndpoint "/api/orders", None, Some(CallResultTarget.IntoQuery "orders"))
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
                    Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), []) }
            ))
            None

    node
        "call-into"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
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
                Value = Binding.Invoke("forecast.revenue", [ "horizon", "12"; "scenario", "base" ])
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
              OnClick = Fuaran.UI.Types.Action.Invoke("model.score", [ "rows", "all" ])
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
              OnSelect = (fun _ -> placeholderChain)
              // Phase 130: bound disabled-state corpus coverage.
              Disabled = Some(Binding.State("uploadBusy", false)) }
        ))
        None

let select: Node<obj> =
    node
        "select-1"
        (NodeKind.Select(
            { Label = TextSource.Literal "Region"
              Source =
                Binding.Static
                    [ { Value = "uk"
                        Label = TextSource.Literal "UK" } ]
              Value = Binding.Static(Some "uk")
              // `Some` (Phase 426) — keeps `"onChange":"<closure>"` on the
              // wire, byte-identical to the pre-426 corpus.
              OnChange = Some(fun _ -> placeholderChain)
              Placeholder = Some(TextSource.Literal "Choose one")
              // Phase 130: bound disabled-state corpus coverage.
              Disabled = Some(Binding.State("selectBusy", false))
              // Phase 291: single-select — Multiple/Values/OnChangeMulti
              // omitted on the wire (the degenerate case stays byte-stable).
              Multiple = false
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
              Source =
                Binding.Static
                    [ { Value = "red"
                        Label = TextSource.Literal "Red" }
                      { Value = "green"
                        Label = TextSource.Literal "Green" } ]
              Value = Binding.Static Option.None
              OnChange = Some(fun _ -> placeholderChain)
              Placeholder = Option.None
              Disabled = Option.None
              Multiple = true
              Values = Some(Binding.Static [ "red"; "green" ])
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
          Kind = FormFieldKind.Text(Binding.State("profileName", ""), Option.None)
          Required = true
          Help = None }

    let numberField: FormField<obj> =
        { Id = "profile-age"
          Label = TextSource.Literal "Age"
          Kind = FormFieldKind.Number(Binding.State("profileAge", 0.0), Option.None)
          Required = false
          Help = None }

    let checkboxField: FormField<obj> =
        { Id = "profile-agree"
          Label = TextSource.Literal "I agree"
          Kind = FormFieldKind.Checkbox(Binding.State("profileAgree", false), Option.None)
          Required = true
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "profile-tier"
          Label = TextSource.Literal "Tier"
          Kind =
            FormFieldKind.Choice(
                Binding.Static
                    [ { Value = "basic"
                        Label = TextSource.Literal "Basic" }
                      { Value = "pro"
                        Label = TextSource.Literal "Pro" } ],
                Binding.State("profileTier", (Option.None: string option)),
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
/// canonical bytes carry NO `value` key on any field (mirror of the
/// filters-declarative minimal chip). Pins the round-trip: decode
/// synthesises the same bindings back, encode omits them again.
let formDeclarativeMinimal: Node<obj> =
    let textField: FormField<obj> =
        { Id = "guest-name"
          Label = TextSource.Literal "Name"
          Kind =
            FormFieldKind.Text(Binding.State("guest-name", Fuaran.UI.Defaults.ControlValueDefaults.text), Option.None)
          Required = true
          Help = None }

    let numberField: FormField<obj> =
        { Id = "party-size"
          Label = TextSource.Literal "Party size"
          Kind =
            FormFieldKind.Number(
                Binding.State("party-size", Fuaran.UI.Defaults.ControlValueDefaults.number),
                Option.None
            )
          Required = false
          Help = None }

    let choiceField: FormField<obj> =
        { Id = "seating"
          Label = TextSource.Literal "Seating"
          Kind =
            FormFieldKind.Choice(
                Binding.Static
                    [ { Value = "indoor"
                        Label = TextSource.Literal "Indoor" }
                      { Value = "terrace"
                        Label = TextSource.Literal "Terrace" } ],
                Binding.State("seating", Fuaran.UI.Defaults.ControlValueDefaults.choice),
                Option.None
            )
          Required = false
          Help = None }

    let dateField: FormField<obj> =
        { Id = "visit-date"
          Label = TextSource.Literal "Date"
          Kind =
            FormFieldKind.Date(
                Binding.State("visit-date", Fuaran.UI.Defaults.ControlValueDefaults.date),
                Option.None,
                DateVariant.Date,
                { Min = None; Max = None; Step = None }
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
                { Orientation = Horizontal
                  Children = [ markdown ]
                  ActiveIndex = Binding.State("activePane", 0)
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
                { Open = Binding.State("modalOpen", false)
                  Heading = Some(TextSource.Literal "Confirm")
                  Dismissable = true
                  Children = [ markdown ]
                  OnDismiss = Option.None }
            ))
            None

    let disclosureNode =
        node
            "decl-disclosure"
            (NodeKind.Disclosure(
                { Heading = TextSource.Literal "Advanced"
                  Open = Binding.State("advancedOpen", false)
                  OnToggle = Option.None
                  Children = [ markdown ]
                  DefaultOpen = false }
            ))
            None

    let selectNode =
        node
            "decl-select"
            (NodeKind.Select(
                { Label = TextSource.Literal "Region"
                  Source =
                    Binding.Static
                        [ { Value = "uk"
                            Label = TextSource.Literal "UK" } ]
                  Value = Binding.State("region", (Option.None: string option))
                  OnChange = Option.None
                  Placeholder = Some(TextSource.Literal "Choose one")
                  Disabled = Option.None
                  Multiple = false
                  Values = Option.None
                  OnChangeMulti = Option.None }
            ))
            None

    node
        "controls-declarative"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
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
                { Orientation = Horizontal
                  Children = [ markdown; sparkline ]
                  ActiveIndex = Binding.Static 0
                  OnSelect = Some(fun _ -> placeholderChain)
                  TabHeaders = Option.None
                  TabTags = Some [ "overview"; "detail" ]
                  ActiveTag = Some(Binding.Static "overview")
                  OnSelectTag = Some(fun _ -> placeholderChain) }
            ))
            None

    let disclosureNode =
        node
            "closure-disclosure"
            (NodeKind.Disclosure(
                { Heading = TextSource.Literal "Advanced"
                  Open = Binding.Static false
                  OnToggle = Some(fun _ -> placeholderChain)
                  Children = [ markdown ]
                  DefaultOpen = false }
            ))
            None

    let multiNode =
        node
            "closure-multiselect"
            (NodeKind.Select(
                { Label = TextSource.Literal "Tags"
                  Source =
                    Binding.Static
                        [ { Value = "red"
                            Label = TextSource.Literal "Red" } ]
                  Value = Binding.Static Option.None
                  OnChange = Some(fun _ -> placeholderChain)
                  Placeholder = Option.None
                  Disabled = Option.None
                  Multiple = true
                  Values = Some(Binding.Static [ "red" ])
                  OnChangeMulti = Some(fun _ -> placeholderChain) }
            ))
            None

    node
        "controls-closure"
        (NodeKind.Box(
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
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
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-1"
        (NodeKind.DataGrid(
            { Source = Binding.Static Seq.empty
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              StaticRows = None }
        ))
        None

let chart: Node<obj> =
    node
        "chart-1"
        (NodeKind.Chart(
            { Source = Binding.Static Seq.empty
              Kind = ChartKind.Line
              XField = "month"
              YFields = [ "revenue"; "cost" ]
              Title = Some(TextSource.Literal "Channel mix")
              OnPointClick = None
              // Stacked = true (Phase 126) — exercises the now-carried
              // stacked-vs-grouped chart intent round-tripping.
              Stacked = true }
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
            { Source = Binding.Transform(source, pipeline, [])
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
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
            { Source = Binding.Transform(source, pipeline, [ "dept", Binding.Filter("dept", None) ])
              RowKey = Some(fun _ -> "<closure>")
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
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
          Format = CellFormat.None
          Kind = CellKindErased.Text
          Width = ColumnWidth.Auto }

    node
        "grid-field-named"
        (NodeKind.DataGrid(
            { Source = Binding.Transform(source, [], [])
              RowKey = None
              RowKeyField = Some "dept"
              Columns = [ fieldCol "Dept" "dept"; fieldCol "Amount" "amount" ]
              OnRowClick = None
              Editable = false
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
                          { Source = Binding.Transform(source, [], [])
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "ticket-detail"
                      (NodeKind.Box(
                          { Layout =
                              BoxLayout.Flex
                                  { Direction = Vertical
                                    Wrap = false
                                    Gap = None }
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
                                                    NodeId "ticket-grid",
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
                          { Source =
                              Binding.Transform(
                                  source,
                                  [ Fuaran.Core.Filter(
                                        Fuaran.Core.Binary(
                                            Fuaran.Core.Eq,
                                            Fuaran.Core.Col "id",
                                            Fuaran.Core.Param "ticketId"
                                        )
                                    ) ],
                                  [ "ticketId",
                                    // 0.2.10 (Phase 632): `field` keeps the param SCALAR
                                    // after a real click — the identity form handed the
                                    // whole row to `objToCell` (a loud non-scalar error).
                                    Binding.Selection(
                                        NodeId "ticket-grid",
                                        Binding.projectSelectionField<obj> "id",
                                        Some(box "TCK-2041"),
                                        Some "id"
                                    ) ]
                              )
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = [ fieldCol "Ticket" "id"; fieldCol "Priority" "priority" ]
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
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
                          { Source = Binding.Transform(source, [], [])
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ { Label = "Ticket"
                                  Value = None
                                  Field = Some "id"
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto }
                                { Label = "Severity"
                                  Value = None
                                  Field = Some "severity"
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
                      ))
                      None
                  node
                      "critical-count-badge"
                      (NodeKind.Badge(
                          { Label =
                              TextSource.Bound(
                                  Binding.Transform(
                                      source,
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
                                      []
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
                                      source,
                                      [ Fuaran.Core.Filter(
                                            Fuaran.Core.Binary(
                                                Fuaran.Core.Eq,
                                                Fuaran.Core.Col "id",
                                                Fuaran.Core.Param "ticketId"
                                            )
                                        )
                                        Fuaran.Core.Project [ "alert", "alert" ]
                                        Fuaran.Core.Limit(1, 0) ],
                                      [ "ticketId",
                                        Binding.Selection(
                                            NodeId "scalar-ticket-grid",
                                            Binding.projectSelectionField<obj> "id",
                                            Some(box "TCK-2041"),
                                            Some "id"
                                        ) ]
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
            source,
            [ Fuaran.Core.Filter(
                  Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "region", Fuaran.Core.Param "region")
              )
              Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "genre", Fuaran.Core.Param "genre")) ],
            [ "region", Binding.Filter("region", None)
              "genre", Binding.Filter("genre", None) ]
        )

    let choice (name: string) (label: string) (options: (string * string) list) : FilterSpec<obj> =
        { Name = name
          Label = TextSource.Literal label
          Field =
            FormFieldKind.Choice(
                Binding.Static
                    [ for value, optLabel in options ->
                          { Value = value
                            Label = TextSource.Literal optLabel } ],
                Binding.Filter(name, None),
                None
            ) }

    let fieldCol (label: string) (field: string) : ColumnErased<obj> =
        { Label = label
          Value = None
          Field = Some field
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
                          [ choice "region" "Region" [ "emea", "EMEA"; "amer", "Americas" ]
                            choice "genre" "Genre" [ "drama", "Drama"; "docs", "Documentary" ] ]
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
                            OnPointClick = None
                            Stacked = false }
                      ))
                      None
                  node
                      "episode-grid"
                      (NodeKind.DataGrid(
                          { Source = filteredSource ()
                            RowKey = None
                            RowKeyField = Some "month"
                            Columns = [ fieldCol "Month" "month"; fieldCol "Retention" "retention" ]
                            OnRowClick = None
                            Editable = false
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
                Value = Binding.Query("orders", (fun _ -> 0.0), [ "status"; "region" ])
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
            { Source = Binding.Static Seq.empty
              RowKey = None
              RowKeyField = None
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some(
                    [ TextSource.Literal "Term"; TextSource.Literal "Definition" ],
                    [ [ TextSource.Literal "MVU"; TextSource.Literal "Model-View-Update" ]
                      [ TextSource.Literal "DSL"; TextSource.Literal "Domain-specific language" ] ]
                ) })
        None

let mapVis: Node<obj> =
    node
        "map-1"
        (NodeKind.Map(
            { Source =
                Binding.Static(
                    seq
                        [ { Latitude = 51.5
                            Longitude = -0.12
                            Label = TextSource.Literal "London" } ]
                )
              CentreLatitude = 51.5
              CentreLongitude = -0.12
              Zoom = 6
              OnMarkerClick = None }
        ))
        None

// ─── Custom + composite ─────────────────────────────────────────────────

let custom: Node<obj> =
    node "custom-1" (NodeKind.Custom("analytics", "trend-card", Map.empty, None, [])) None

// Custom with the bounded-escape additive
// fields populated. Exercises the wire-shape lock: contentHash + exposed-
// NodeIds round-trip through canonical JSON without precision loss.
let customBounded: Node<obj> =
    node
        "custom-bounded-1"
        (NodeKind.Custom(
            "deal-flow",
            "QualityRing",
            Map.empty,
            Some
                { Algorithm = "SHA256"
                  Hash = "abc123def456"
                  Strictness = HashStrictness.StrictReplay },
            [ NodeId "quality-ring-segment-1"; NodeId "quality-ring-segment-2" ]
        ))
        None

let customBoundedAdvisory: Node<obj> =
    node
        "custom-bounded-advisory"
        (NodeKind.Custom(
            "deal-flow",
            "TrendCard",
            Map.empty,
            Some
                { Algorithm = "SHA256"
                  Hash = "fedcba654321"
                  Strictness = HashStrictness.AdvisoryWarning },
            []
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
let switchBasic: Node<obj> =
    node
        "switch-1"
        (NodeKind.Switch
            { StateKey = "view"
              Cases =
                [ "details",
                  node "switch-details" (NodeKind.Markdown({ Text = TextSource.Literal "Details view" })) None
                  "summary",
                  node "switch-summary" (NodeKind.Markdown({ Text = TextSource.Literal "Summary view" })) None ]
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
            { Name = FragmentId "card-template"
              Body = node "frag-body" (NodeKind.Markdown({ Text = TextSource.Literal "Template body" })) None
              Holes = []
              Effect = EffectClass.pureDeterministic })
        None

let fragmentRef: Node<obj> =
    node
        "frag-ref-1"
        (NodeKind.FragmentRef
            { Name = FragmentId "card-template"
              Args = Map.empty })
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
            { Name = FragmentId "stat-card"
              Body = node "param-body" (NodeKind.Markdown({ Text = TextSource.Literal "Parameterised body" })) None
              Holes =
                [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), Some(box "Untitled"))
                  HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), None)
                  HoleDecl.Slot("content", Some "Display")
                  HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 12)) ]
              Effect =
                { HostEffect = HostEffect.ReadsHost
                  Determinism = DeterminismSource.Clock } })
        None

let fragmentRefArgs: Node<obj> =
    node
        "frag-ref-args"
        (NodeKind.FragmentRef
            { Name = FragmentId "stat-card"
              Args =
                Map.ofList
                    [ "count", FragmentArg.Value(box 7)
                      "content",
                      FragmentArg.Slot(
                          node "slot-tree" (NodeKind.Markdown({ Text = TextSource.Literal "Bound slot" })) None
                      ) ] })
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
              Inputs = Map.empty
              Channel =
                { Direction = ChannelDirection.OutOnly
                  MessageShape = None }
              OnBubble = (fun _ -> Action.Chain [])
              Capabilities = [] })
        None

let mountFull: Node<obj> =
    node
        "mount-2"
        (NodeKind.Mount
            { ScopeId = "guest-metrics"
              Inputs =
                Map.ofList
                    [ "title", FragmentArg.Value(box "Metrics")
                      "seed",
                      FragmentArg.Slot(
                          node "seed-tree" (NodeKind.Markdown({ Text = TextSource.Literal "Initial guest state" })) None
                      ) ]
              Channel =
                { Direction = ChannelDirection.TwoWay
                  MessageShape = Some "MetricsMsg" }
              OnBubble = (fun _ -> Action.Chain [])
              Capabilities = [ CapabilityTag "notify"; CapabilityTag "call:reports.*" ] })
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
                          { Layout =
                              BoxLayout.Flex
                                  { Direction = Vertical
                                    Wrap = false
                                    Gap = None }
                            Role = BoxRole.Card
                            Heading = Some(TextSource.Literal "Composite")
                            Children = [ metric; labelValueRow ] }
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
    TreeOp.ReplaceBinding(NodeId "metric-1", "Value", Binding.Static(box 99.5))

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
    let localFloat: LocalBinding<string> =
        { InitialFrom = Binding.State("salary", "")
          FlushOn = LocalFlushTrigger.OnBlur
          OnCommit = (fun _ -> box (Action.Chain []: Action<obj>))
          Format = Some(fun (s: string) -> s)
          Parse = (fun (raw: string) -> Ok raw) }

    let textField: FormField<obj> =
        { Id = "salary-input"
          Label = TextSource.Literal "Salary"
          Kind = FormFieldKind.Text(Binding.Local localFloat, Some(fun _ -> placeholderChain))
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
    let localDebounce: LocalBinding<string> =
        { InitialFrom = Binding.Static "draft@example.com"
          FlushOn = LocalFlushTrigger.OnDebounce 250
          OnCommit = (fun _ -> box (Action.Chain []: Action<obj>))
          Format = Some id
          Parse = (fun raw -> Ok raw) }

    let textField: FormField<obj> =
        { Id = "email-input"
          Label = TextSource.Literal "Email"
          Kind = FormFieldKind.Text(Binding.Local localDebounce, Some(fun _ -> placeholderChain))
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
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children =
                [ md
                      "fmt-number"
                      (Binding.Format(Binding.Static 1234.5, Format.Number(Some 2), LocaleSource.Explicit "en-US"))
                  md
                      "fmt-currency"
                      (Binding.Format(Binding.Static 1234.5, Format.Currency "GBP", LocaleSource.Explicit "en-GB"))
                  md "fmt-percent" (Binding.Format(Binding.Static 0.42, Format.Percent None, LocaleSource.Ambient))
                  md
                      "fmt-date"
                      (Binding.Format(
                          Binding.Static 1700000000.0,
                          Format.Date DateStyle.Medium,
                          LocaleSource.Explicit "fr-FR"
                      ))
                  md
                      "fmt-relative"
                      (Binding.Format(
                          Binding.Static -3.0,
                          Format.RelativeTime RelativeTimeUnit.Day,
                          LocaleSource.Explicit "en-US"
                      )) ] }
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
      "Display/Image (Avatar variant)", image
      "Display/List (ordered)", listDisplay
      "Display/Toast (Success tone, open)", toast
      "Display/CodeBlock (fsharp, line numbers + highlights)", codeBlock
      "Display/Math (block LaTeX)", math
      "Display/Drawing (all shapes + curve commands + styled bindings)", drawing
      "Display/Drawing (degenerate — empty)", drawingMinimal
      "Display/Sparkline", sparkline
      "Display/Skeleton", skeleton
      "Display/Callout", callout
      "Display/Progress", progress
      "Display/LabelValueRow", labelValueRow
      "Display/Fact", fact
      "Layout/Dashboard (empty)", dashboardEmpty
      "Layout/Stack", stack
      "Layout/Grid", gridLayout
      "Layout/Grid (TemplateColumns 1fr 2fr ratio)", gridLayoutTemplatedRatio
      "Layout/Grid (TemplateColumns 100px + repeat fixed-plus-flex)", gridLayoutTemplatedFixedPlusFlex
      "Layout/Grid (TemplateColumns auto-fit minmax)", gridLayoutTemplatedAutoFit
      "Layout/SplitPanel", splitPanel
      "Layout/Tabs", tabs
      "Layout/Tabs (explicit headers + tags + activeTag)", tabsExplicitHeaders
      "Layout/Card", card
      "Layout/Stepper", stepper
      "Layout/SummaryList", summaryList
      "Layout/Disclosure", disclosure
      "Layout/Modal (heading + child + onDismiss)", modal
      "Layout/ScrollArea (vertical, maxHeight)", scrollArea
      "Input/Form (all fields)", formAllFields
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
      "Layout/Box (filterable-static dashboard — Filters params wired through Transform to chart + grid)",
      filterableStaticDashboard
      "Layout/Box (master-detail — grid + detail card State-bound with a pre-selected defaultValue)",
      masterDetailPreselected
      "Layout/Box (Phase 632 — Transform in scalar slots: selected-row Callout body + Badge count)",
      scalarTransformComposition
      "Display/Metric (Phase 283 — Binding.Invoke capability source)", metricInvoke
      "Input/Button (Phase 283 — Action.Invoke capability effect)", buttonInvoke
      "Visualisation/Chart", chart
      "Visualisation/Grid (static-table mode — staticRows; absorbed the retired Table kind)", table
      "Visualisation/Map", mapVis
      "Custom", custom
      "Custom (bounded escape, StrictReplay hash + exposed-ids)", customBounded
      "Custom (bounded escape, AdvisoryWarning hash + no exposed-ids)", customBoundedAdvisory
      "ErrorBoundary (Markdown child + Callout fallback)", errorBoundary
      "Switch (view state → details/summary cases + info default)", switchBasic
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
