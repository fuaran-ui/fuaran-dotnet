module Fuaran.Samples.Catalog.Matrix

// ============================================================================
//  Component matrix declaration.
//
//  For each NodeKind the catalog shows, a hand-picked representative
//  spec is paired with the cartesian sweep of (Tone × Weight × Emphasis)
//  the renderer dispatches against. The matrix is the catalog's data
//  model — the gallery view (Catalog.fs) projects it; the Playwright
//  harness walks it for screenshot coverage.
//
//  Adding a new component when sessions extend the type
//  contract: add a new `KindEntry` to `entries` below + give it an
//  `id`, a label for the side-nav, a one-line `hint`, and a builder
//  function that takes a `(Tone, Weight, Emphasis)` triple and returns
//  the example `Node<Msg>`. The sweep is automatic.
//
//  Catalog Msg type lives in Catalog.fs (this module is upstream of
//  it for compile-order reasons); the matrix is parameterised over
//  `'Msg` so the builder closures stay typed.
// ============================================================================

open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types

// ─── Cross-cutting axes ────────────────────────────────────────────────────

let allTones: ToneVariant list =
    [ ToneVariant.Default
      ToneVariant.Subdued
      ToneVariant.Brand
      ToneVariant.Success
      ToneVariant.Warning
      ToneVariant.Critical
      ToneVariant.Info ]

let allWeights: StyleWeight list =
    [ StyleWeight.Compact; StyleWeight.Standard; StyleWeight.Spacious ]

let allEmphases: Emphasis list = [ Emphasis.Quiet; Emphasis.Normal; Emphasis.Loud ]

let toneLabel (tone: ToneVariant) : string =
    match tone with
    | ToneVariant.Default -> "Default"
    | ToneVariant.Subdued -> "Subdued"
    | ToneVariant.Brand -> "Brand"
    | ToneVariant.Success -> "Success"
    | ToneVariant.Warning -> "Warning"
    | ToneVariant.Critical -> "Critical"
    | ToneVariant.Info -> "Info"

let weightLabel (weight: StyleWeight) : string =
    match weight with
    | StyleWeight.Compact -> "Compact"
    | StyleWeight.Standard -> "Standard"
    | StyleWeight.Spacious -> "Spacious"

let emphasisLabel (emphasis: Emphasis) : string =
    match emphasis with
    | Emphasis.Quiet -> "Quiet"
    | Emphasis.Normal -> "Normal"
    | Emphasis.Loud -> "Loud"

// ─── Per-NodeKind entry shape ──────────────────────────────────────────────

/// One entry in the catalog matrix. `Build` constructs a representative
/// `Node<unit>` for the supplied (Tone × Weight × Emphasis) triple. The
/// matrix is `'Msg`-agnostic by design — every interactive handler in
/// the catalog wires to `Action.Chain []`, so a concrete unit Msg
/// avoids the F# value-restriction cascade that comes from a list of
/// closures over an unresolved type variable. The catalog's own Msg
/// (side-nav + theme switcher) lives outside the matrix.
type KindEntry =
    { Id: string
      Label: string
      Hint: string
      Group: string
      Build: ToneVariant * StyleWeight * Emphasis -> Node<unit> }

// ─── Hand-picked representatives ───────────────────────────────────────────

let private idFor (kind: string) (tone: ToneVariant) (weight: StyleWeight) (emphasis: Emphasis) : string =
    sprintf "%s-%s-%s-%s" kind (toneLabel tone) (weightLabel weight) (emphasisLabel emphasis)

let private demoMetric (tone, weight, emphasis) : Node<unit> =
    // Catalog-axis tail: Metric's `Label` is bound via
    // `Binding.I18n` so the catalog's locale switcher visibly retranslates
    // it. The key is the canonical `catalog.<spec-id>.<field>` shape
    // Catalog.fs carries in its `localeOptions` map.
    Fuaran.metric
        (idFor "metric" tone weight emphasis)
        { Defaults.metric with
            Label = TextSource.Bound(Binding.I18n("catalog.metric.label", None))
            Value = Binding.Static(Some 142_500.0)
            Format = format.currency "GBP"
            Tone = tone
            Weight = weight
            Emphasis = emphasis
            Trend = Some(Binding.Static(Some 0.083))
            TrendFormat = Some(format.percent (Some 1)) }

let private demoBadge (tone, weight, emphasis) : Node<unit> =
    // Badge's per-spec `Variant` is a separate vocabulary from the Node-level
    // `ToneVariant`; HOST-STYLING-CHECKLIST.md §"BadgeVariant ↔ ToneVariant"
    // documents the deliberate fork. The catalog maps tone→variant for the
    // sweep so each row shows the matching badge colour.
    let variant =
        match tone with
        | ToneVariant.Brand -> BadgeVariant.Brand
        | ToneVariant.Success -> BadgeVariant.Success
        | ToneVariant.Warning -> BadgeVariant.Warning
        | ToneVariant.Critical -> BadgeVariant.Critical
        | ToneVariant.Info -> BadgeVariant.Info
        | ToneVariant.Subdued
        | ToneVariant.Default -> BadgeVariant.Neutral

    { Id = idFor "badge" tone weight emphasis
      Kind =
        NodeKind.Badge(
            { Label = TextSource.Literal(toneLabel tone)
              Variant = variant }
        )
      State = None
      Style =
        Some
            { Tone = tone
              Weight = weight
              Emphasis = emphasis
              Role = StyleRole.None
              Voice = FontVoice.Default }
      Accessibility = Defaults.Accessibility.none
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

let private demoHeading (tone, weight, emphasis) : Node<unit> =
    Fuaran.heading
        (idFor "heading" tone weight emphasis)
        { Defaults.heading with
            Level = 3
            Text = TextSource.Literal "Section heading"
            Variant = HeadingVariant.Standard }

let private demoMarkdown (tone, weight, emphasis) : Node<unit> =
    Fuaran.markdown
        (idFor "markdown" tone weight emphasis)
        "Rendered **markdown** text — *italic*, `inline code`, and a short paragraph."

let private demoCallout (tone, weight, emphasis) : Node<unit> =
    // Catalog-axis tail: Callout's Heading + Body are bound
    // via `Binding.I18n` against per-tone keys, so the locale switcher
    // retranslates per row. Some tones have partial-locale coverage in
    // Catalog.fs (de carries only Default / Warning / Critical headings)
    // so the catalog visibly demonstrates the `[i18n:<key>]` fallback
    // alongside resolved strings on the same render.
    let headingKey = sprintf "catalog.callout.heading.%s" ((toneLabel tone).ToLower())

    Fuaran.callout
        (idFor "callout" tone weight emphasis)
        { Defaults.callout with
            Tone = tone
            Heading = Some(TextSource.Bound(Binding.I18n(headingKey, None)))
            Body = TextSource.Bound(Binding.I18n("catalog.callout.body", None))
            Dismissable = false }

let private demoProgress (tone, weight, emphasis) : Node<unit> =
    Fuaran.progress
        (idFor "progress" tone weight emphasis)
        { Defaults.progress with
            Fraction = Binding.Static(Some 0.42)
            Tone = tone
            Label = Some(TextSource.Literal "Loading channels…") }

let private demoSparkline (tone, weight, emphasis) : Node<unit> =
    let id = idFor "sparkline" tone weight emphasis

    { Id = id
      Kind = NodeKind.Sparkline({ Source = Binding.Static(Some [ 1.0; 4.0; 2.0; 6.0; 3.0; 8.0; 5.0; 9.0 ]) })
      State = None
      Style =
        Some
            { Tone = tone
              Weight = weight
              Emphasis = emphasis
              Role = StyleRole.None
              Voice = FontVoice.Default }
      Accessibility = Defaults.Accessibility.none
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

let private demoSkeleton (tone, weight, emphasis) : Node<unit> =
    Fuaran.skeleton (idFor "skeleton" tone weight emphasis) 3
    |> Node.withTone tone
    |> Node.withWeight weight
    |> Node.withEmphasis emphasis

let private demoLabelValueRow (tone, weight, emphasis) : Node<unit> =
    Fuaran.labelValueRow
        (idFor "labelValueRow" tone weight emphasis)
        { Defaults.labelValueRow with
            Label = TextSource.Bound(Binding.I18n("catalog.labelValueRow.label", None))
            Value = Binding.Static(Some 142_500.0)
            Format = format.currency "GBP"
            Emphasis = (emphasis = Emphasis.Loud)
            Help = Some(TextSource.Literal "Year-to-date, gross.") }

let private demoButton (tone, weight, emphasis) : Node<unit> =
    let variant =
        match emphasis with
        | Emphasis.Loud -> ButtonVariant.Primary
        | Emphasis.Normal -> ButtonVariant.Secondary
        | Emphasis.Quiet -> ButtonVariant.Tertiary

    Fuaran.button
        (idFor "button" tone weight emphasis)
        { Defaults.button<unit> with
            Label = TextSource.Bound(Binding.I18n("catalog.button.label", None))
            Variant = variant
            OnClick = Action.Chain [] }

let private demoSelect (tone, weight, emphasis) : Node<unit> =
    Fuaran.select
        (idFor "select" tone weight emphasis)
        { Defaults.select<unit> with
            Label = TextSource.Literal "Contributor peer"
            Source =
                Binding.Static(
                    Some
                        [ { Value = "buyer-peer"
                            Label = "Buyer peer" }
                          { Value = "seller-peer"
                            Label = "Seller peer" }
                          { Value = "portal-peer"
                            Label = "Portal peer" } ]
                )
            Value = Binding.Static None
            OnChange = Some(fun _ -> Action.Chain [])
            Placeholder = Some(TextSource.Literal "Choose a peer…") }

let private demoForm (tone, weight, emphasis) : Node<unit> =
    Fuaran.form
        (idFor "form" tone weight emphasis)
        { Defaults.form<unit> with
            SubmitLabel = TextSource.Literal "Submit"
            Fields =
                [ { Defaults.formField<unit> with
                      Id = "form-text"
                      Label = TextSource.Literal "Cohort name"
                      Required = true
                      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> Action.Chain [])) }
                  { Defaults.formField<unit> with
                      Id = "form-number"
                      Label = TextSource.Literal "Sample size"
                      Kind = FormFieldKind.Number(Some(Binding.Static(Some 100.0)), Some(fun _ -> Action.Chain [])) } ] }

let private demoFormRangedNumber (tone, weight, emphasis) : Node<unit> =
    // Catalog axis. Each field exercises one of the eight
    // (Min × Max × Step) presence/absence combinations the renderer
    // projects to HTML `min` / `max` / `step` attributes. The `Help`
    // slot names the combo so operators can verify the projection at
    // a glance against the rendered DOM.
    let mkField
        (id: string)
        (label: string)
        (help: string)
        (init: float)
        (minV: float option)
        (maxV: float option)
        (stepV: float option)
        : FormField<unit> =
        { Defaults.formField<unit> with
            Id = id
            Label = TextSource.Literal label
            Help = Some(TextSource.Literal help)
            Kind = FormFieldKind.rangedNumber (Binding.Static(Some init)) (fun _ -> Action.Chain []) minV maxV stepV }

    Fuaran.form
        (idFor "formRangedNumber" tone weight emphasis)
        { Defaults.form<unit> with
            SubmitLabel = TextSource.Literal "Submit"
            Fields =
                [ mkField
                      "min-max-step"
                      "Year (min + max + step)"
                      "min=1979, max=2028, step=1"
                      2024.0
                      (Some 1979.0)
                      (Some 2028.0)
                      (Some 1.0)
                  mkField "min-max" "Year (min + max)" "min=1979, max=2028" 2024.0 (Some 1979.0) (Some 2028.0) None
                  mkField "min-step" "Above floor (min + step)" "min=0, step=0.5" 1.0 (Some 0.0) None (Some 0.5)
                  mkField "max-step" "Below ceiling (max + step)" "max=100, step=5" 50.0 None (Some 100.0) (Some 5.0)
                  mkField "min-only" "Non-negative (min only)" "min=0" 10.0 (Some 0.0) None None
                  mkField "max-only" "Capped (max only)" "max=100" 50.0 None (Some 100.0) None
                  mkField "step-only" "Stepped (step only)" "step=0.25" 1.0 None None (Some 0.25)
                  mkField "no-bounds" "Unconstrained" "no min/max/step" 0.0 None None None ] }

let private demoFilters (tone, weight, emphasis) : Node<unit> =
    Fuaran.filters
        (idFor "filters" tone weight emphasis)
        [ { Defaults.filter<unit> with
              Name = "text-filter"
              Label = TextSource.Literal "Search"
              Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> Action.Chain [])) } ]

let private demoFormSegmentedChoice (tone, weight, emphasis) : Node<unit> =
    // Catalog axis. Three exclusive-choice surfaces:
    //   1. Horizontal segmented control (canonical "select view mode" shape;
    //      3 options matches a typical heatmap metric selector).
    //   2. Vertical radio-button list (canonical "settings preference"
    //      shape; same option count, distinct visual surface).
    //   3. Horizontal segmented control with current selection bound — so
    //      the active-state styling can be inspected against the
    //      `--fuaran-segmented-active-*` tokens.
    let metricOpts: SelectOption list =
        [ { Value = "effective"
            Label = "Effective rate" }
          { Value = "marginal"
            Label = "Marginal rate" }
          { Value = "takeHome"
            Label = "Take-home %" } ]

    let tierOpts: SelectOption list =
        [ { Value = "free"; Label = "Free" }
          { Value = "pro"; Label = "Pro" }
          { Value = "team"; Label = "Team" } ]

    Fuaran.form
        (idFor "formSegmentedChoice" tone weight emphasis)
        { Defaults.form<unit> with
            SubmitLabel = TextSource.Literal "Apply"
            Fields =
                [ { Defaults.formField<unit> with
                      Id = "metric-horizontal"
                      Label = TextSource.Literal "Metric (horizontal)"
                      Help = Some(TextSource.Literal "3-way segmented control — no initial selection")
                      Kind =
                          FormFieldKind.SegmentedChoice(
                              Binding.Static(Some metricOpts),
                              Some(Binding.Static None),
                              Some(fun _ -> Action.Chain []),
                              Orientation.Horizontal
                          ) }
                  { Defaults.formField<unit> with
                      Id = "tier-vertical"
                      Label = TextSource.Literal "Tier (vertical)"
                      Help = Some(TextSource.Literal "Native radio-button list — browser handles arrow-key nav")
                      Kind =
                          FormFieldKind.SegmentedChoice(
                              Binding.Static(Some tierOpts),
                              Some(Binding.Static None),
                              Some(fun _ -> Action.Chain []),
                              Orientation.Vertical
                          ) }
                  { Defaults.formField<unit> with
                      Id = "metric-active"
                      Label = TextSource.Literal "Metric (horizontal, active)"
                      Help = Some(TextSource.Literal "Initial selection — exercises active-state styling")
                      Kind =
                          FormFieldKind.SegmentedChoice(
                              Binding.Static(Some metricOpts),
                              Some(Binding.Static(Some "marginal")),
                              Some(fun _ -> Action.Chain []),
                              Orientation.Horizontal
                          ) } ] }

let private demoFileUpload (tone, weight, emphasis) : Node<unit> =
    Fuaran.fileUpload
        (idFor "fileUpload" tone weight emphasis)
        { Defaults.fileUpload<unit> with
            Label = TextSource.Literal "Upload fitted-parameters file"
            Accept = [ ".csv"; ".json" ]
            Multiple = false
            OnSelect = Some(fun _ -> Action.Chain []) }

let private demoTable (tone, weight, emphasis) : Node<unit> =
    Fuaran.table
        (idFor "table" tone weight emphasis)
        { Defaults.table<unit> with
            Headers =
                [ TextSource.Literal "Tier"
                  TextSource.Literal "Description"
                  TextSource.Literal "Status" ]
            Rows =
                [ [ TextSource.Literal "Tier 0"
                    TextSource.Literal "Plaintext federation"
                    TextSource.Literal "Default" ]
                  [ TextSource.Literal "Tier 1"
                    TextSource.Literal "HMAC pseudonymisation"
                    TextSource.Literal "Available" ] ] }

let private demoMap (tone, weight, emphasis) : Node<unit> =
    let markers: MapMarker list =
        [ { Latitude = 51.5074
            Longitude = -0.1278
            Label = "London" }
          { Latitude = 40.7128
            Longitude = -74.006
            Label = "New York" } ]

    Fuaran.map
        (idFor "map" tone weight emphasis)
        { Defaults.map<unit> with
            Source = Binding.Static(Some markers)
            CentreLatitude = 30.0
            CentreLongitude = -30.0
            Zoom = 3 }

let private demoChart (tone, weight, emphasis) : Node<unit> =
    Fuaran.chart
        (idFor "chart" tone weight emphasis)
        { Defaults.chart<unit> with
            Source = Binding.Static(Some Seq.empty)
            Kind = ChartKind.Bar
            XField = "x"
            YFields = [ "y" ]
            Title = Some(TextSource.Literal "Sample chart") }

type private GridRow =
    { Row: int; Name: string; Value: float }

let private gridRows: GridRow list =
    [ { Row = 1
        Name = "Search"
        Value = 18.4 }
      { Row = 2
        Name = "Display"
        Value = 12.1 }
      { Row = 3; Name = "TV"; Value = 35.6 } ]

// fuaran#665 — accessors read the projected `Row` by name; these two helpers
// keep the cell reads terse.
let private cellText (field: string) (r: Row) : string =
    defaultArg (Map.tryFind field r |> Option.map string) ""

let private cellFloat (field: string) (r: Row) : float =
    match Map.tryFind field r with
    | Some v -> unbox<float> v
    | None -> 0.0

let private demoGrid (tone, weight, emphasis) : Node<unit> =
    Fuaran.grid
        (idFor "grid" tone weight emphasis)
        // fuaran#665 — the required `toRow` projection to the wire-expressible Row.
        (fun (r: GridRow) -> Map.ofList [ "name", box r.Name; "row", box r.Row; "value", box r.Value ])
        { Defaults.grid<GridRow, unit> with
            Source = Binding.Static(Some gridRows)
            RowKey = cellText "row"
            Columns =
                [ Column.text "Channel" (cellText "name")
                  Column.numeric "GRPs" (cellFloat "value")
                  |> Column.withFormat (format.number (Some 1))
                  // Pill column: the typed value renders as a tone-bearing pill
                  // via `Column.withPill`, mapping the row to a `ToneVariant`
                  // (no renderer change — the Pill cell already ships).
                  Column.text "Status" (fun r -> if cellFloat "value" r >= 15.0 then "High" else "Low")
                  |> Column.withPill (fun r ->
                      if cellFloat "value" r >= 15.0 then
                          ToneVariant.Success
                      else
                          ToneVariant.Subdued) ] }

// ─── Layout examples ──────────────────────────────────────────────────────
//
// Layouts wrap two trivial Markdown children so the catalog can show the
// container chrome (gap / wrap / split-weight / tab bar / stepper rail)
// without polluting the matrix with deeply-nested trees.

let private layoutKids: Node<unit> list =
    [ Fuaran.markdown "_l1" "First child."; Fuaran.markdown "_l2" "Second child." ]

let private demoDashboard (tone, weight, emphasis) : Node<unit> =
    Fuaran.dashboard
        (idFor "dashboard" tone weight emphasis)
        { Defaults.dashboard with
            Children = layoutKids }

let private demoStack (tone, weight, emphasis) : Node<unit> =
    Fuaran.stack
        (idFor "stack" tone weight emphasis)
        { Defaults.stack<unit> with
            Orientation = Orientation.Horizontal
            Children = layoutKids
            Wrap = (emphasis = Emphasis.Loud) }

let private demoGridLayout (tone, weight, emphasis) : Node<unit> =
    Fuaran.gridLayout
        (idFor "gridLayout" tone weight emphasis)
        { Defaults.gridLayout<unit> with
            Cols = 2
            Children = layoutKids }

let private demoGridLayoutIrregular (tone, weight, emphasis) : Node<unit> =
    // Irregular-grid axis exercising the
    // canonical `100px repeat(N, minmax(30px, 1fr))` shape — the
    // worked-example shape that motivated the additive escape (a
    // heatmap row-label + N-data-columns grid). Other
    // common shapes (`1fr 2fr`, `auto 1fr auto`, `min-content max-content`,
    // `repeat(auto-fit, minmax(150px, 1fr))`) are exercised via the
    // round-trip / renderer test fixtures rather than the visual catalog
    // to keep the catalog page scannable.
    Fuaran.gridLayoutTemplated
        (idFor "gridLayoutIrregular" tone weight emphasis)
        "100px repeat(3, minmax(30px, 1fr))"
        { Defaults.gridLayout<unit> with
            Children = layoutKids }

let private demoSplitPanel (tone, weight, emphasis) : Node<unit> =
    Fuaran.splitPanel
        (idFor "splitPanel" tone weight emphasis)
        { Defaults.splitPanel<unit> with
            Weight = 0.6
            Children = layoutKids }

let private demoTabs (tone, weight, emphasis) : Node<unit> =
    Fuaran.tabs
        (idFor "tabs" tone weight emphasis)
        { Defaults.tabs<unit> with
            ActiveIndex = Binding.Static(Some 0)
            Children =
                [ Fuaran.card
                      "_tab1"
                      { Defaults.card<unit> with
                          Heading = Some(TextSource.Literal "Overview")
                          Children = [ Fuaran.markdown "_tabM1" "Overview pane." ] }
                  Fuaran.card
                      "_tab2"
                      { Defaults.card<unit> with
                          Heading = Some(TextSource.Literal "Details")
                          Children = [ Fuaran.markdown "_tabM2" "Details pane." ] } ] }

let private demoCard (tone, weight, emphasis) : Node<unit> =
    Fuaran.card
        (idFor "card" tone weight emphasis)
        { Defaults.card<unit> with
            Heading = Some(TextSource.Literal "Card heading")
            Children = layoutKids }

let private demoStepper (tone, weight, emphasis) : Node<unit> =
    Fuaran.stepper
        (idFor "stepper" tone weight emphasis)
        { Defaults.stepper<unit> with
            ActiveStep = Binding.Static(Some 1)
            Children =
                [ Fuaran.markdown "_s1" "Step 1 — define cohort."
                  Fuaran.markdown "_s2" "Step 2 — choose dimensions."
                  Fuaran.markdown "_s3" "Step 3 — review." ] }

let private demoSummaryList (tone, weight, emphasis) : Node<unit> =
    Fuaran.summaryList
        (idFor "summaryList" tone weight emphasis)
        { Defaults.summaryList<unit> with
            Heading = Some(TextSource.Literal "Year-to-date stats")
            Children =
                [ Fuaran.labelValueRow
                      "_lvr1"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Revenue"
                          Value = Binding.Static(Some 142_500.0)
                          Format = format.currency "GBP" }
                  Fuaran.labelValueRow
                      "_lvr2"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Total"
                          Value = Binding.Static(Some 158_900.0)
                          Format = format.currency "GBP"
                          Emphasis = true } ] }

let private demoDisclosure (tone, weight, emphasis) : Node<unit> =
    // Default closed, with a single nested-disclosure
    // child so the catalog exercises the accordion-in-accordion case.
    // `Open = Binding.Static false` keeps the renderer's
    // controlled-mode path on the happy path; `DefaultOpen = true` opens
    // the OUTER disclosure on initial mount before the binding resolves —
    // operators see the nested case at a glance.
    Fuaran.disclosure
        (idFor "disclosure" tone weight emphasis)
        { Defaults.disclosure<unit> with
            Heading = TextSource.Literal "Additional details"
            DefaultOpen = true
            Children =
                [ Fuaran.markdown "_d1" "Body content for the outer disclosure."
                  Fuaran.disclosure
                      "_dnested"
                      { Defaults.disclosure<unit> with
                          Heading = TextSource.Literal "Nested disclosure"
                          Children = [ Fuaran.markdown "_d2" "Inner body — closed by default." ] } ] }

let private demoCustom (tone, weight, emphasis) : Node<unit> =
    let id = idFor "custom" tone weight emphasis

    { Id = id
      Kind =
        NodeKind.Custom(
            { ModuleId = "catalog-sample"
              ComponentId = "PlaceholderWidget"
              Props = Map.ofList [ "label", JStr "Custom component sample" ]
              ContentHash = None
              ExposedNodeIds = None }
        )
      State = None
      Style =
        Some
            { Tone = tone
              Weight = weight
              Emphasis = emphasis
              Role = StyleRole.None
              Voice = FontVoice.Default }
      Accessibility = Defaults.Accessibility.none
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

// ─── Public entries ───────────────────────────────────────────────────────

// `entries` is concrete (returns `KindEntry list` — i.e.
// `KindEntry<unit> list` conceptually). The matrix doesn't dispatch
// any messages: every interactive handler wires to `Action.Chain []`.
// Keeping the type concrete avoids the value-restriction cascade
// (FS0670) that generic builders captured into a record-literal
// list trigger.

let entries: KindEntry list =
    [ // Display
      { Id = "Metric"
        Label = "Metric"
        Hint = "Headline metric with optional tone/format/trend."
        Group = "Display"
        Build = demoMetric }
      { Id = "Heading"
        Label = "Heading"
        Hint = "Semantic section title (Variant axis)."
        Group = "Display"
        Build = demoHeading }
      { Id = "Markdown"
        Label = "Markdown"
        Hint = "Rendered markdown text via the `marked` npm library."
        Group = "Display"
        Build = demoMarkdown }
      { Id = "Badge"
        Label = "Badge"
        Hint = "Inline status pill."
        Group = "Display"
        Build = demoBadge }
      { Id = "Callout"
        Label = "Callout"
        Hint = "Tier/mode/status banner sized for content blocks."
        Group = "Display"
        Build = demoCallout }
      { Id = "Progress"
        Label = "Progress"
        Hint = "Determinate or indeterminate progress bar."
        Group = "Display"
        Build = demoProgress }
      { Id = "Sparkline"
        Label = "Sparkline"
        Hint = "Inline trend microviz."
        Group = "Display"
        Build = demoSparkline }
      { Id = "Skeleton"
        Label = "Skeleton"
        Hint = "Loading placeholder."
        Group = "Display"
        Build = demoSkeleton }
      { Id = "LabelValueRow"
        Label = "LabelValueRow"
        Hint = "Label-left / value-right."
        Group = "Display"
        Build = demoLabelValueRow }
      // Input
      { Id = "Button"
        Label = "Button"
        Hint = "Clickable action emitter."
        Group = "Input"
        Build = demoButton }
      { Id = "Select"
        Label = "Select"
        Hint = "Filterable picker."
        Group = "Input"
        Build = demoSelect }
      { Id = "Form"
        Label = "Form"
        Hint = "Schema-driven form."
        Group = "Input"
        Build = demoForm }
      { Id = "FormRangedNumber"
        Label = "Form — Number constraints"
        Hint = "`FormFieldKind.rangedNumber` axis: all (Min × Max × Step) presence combos."
        Group = "Input"
        Build = demoFormRangedNumber }
      { Id = "FormSegmentedChoice"
        Label = "Form — Segmented choice"
        Hint = "`FormFieldKind.SegmentedChoice` axis: Horizontal + Vertical orientations + active state."
        Group = "Input"
        Build = demoFormSegmentedChoice }
      { Id = "Filters"
        Label = "Filters"
        Hint = "Filter strip."
        Group = "Input"
        Build = demoFilters }
      { Id = "FileUpload"
        Label = "FileUpload"
        Hint = "Upload affordance."
        Group = "Input"
        Build = demoFileUpload }
      // Visualisation
      { Id = "Grid"
        Label = "Grid"
        Hint = "AG Grid-backed editable table."
        Group = "Visualisation"
        Build = demoGrid }
      { Id = "Chart"
        Label = "Chart"
        Hint = "AG Charts-backed chart."
        Group = "Visualisation"
        Build = demoChart }
      { Id = "Table"
        Label = "Table"
        Hint = "Simple HTML table (not data-bound)."
        Group = "Visualisation"
        Build = demoTable }
      { Id = "Map"
        Label = "Map"
        Hint = "Geographic map with markers."
        Group = "Visualisation"
        Build = demoMap }
      // Layout
      { Id = "Dashboard"
        Label = "Dashboard"
        Hint = "Top-level scope; one per page."
        Group = "Layout"
        Build = demoDashboard }
      { Id = "Stack"
        Label = "Stack"
        Hint = "Vertical / horizontal flow (Wrap axis)."
        Group = "Layout"
        Build = demoStack }
      { Id = "GridLayout"
        Label = "GridLayout"
        Hint = "N-column grid."
        Group = "Layout"
        Build = demoGridLayout }
      { Id = "GridLayoutIrregular"
        Label = "GridLayout (irregular)"
        Hint = "Irregular column sizing via TemplateColumns escape."
        Group = "Layout"
        Build = demoGridLayoutIrregular }
      { Id = "SplitPanel"
        Label = "SplitPanel"
        Hint = "Resizable splitter."
        Group = "Layout"
        Build = demoSplitPanel }
      { Id = "Tabs"
        Label = "Tabs"
        Hint = "Tabbed container."
        Group = "Layout"
        Build = demoTabs }
      { Id = "Card"
        Label = "Card"
        Hint = "Bordered container with title."
        Group = "Layout"
        Build = demoCard }
      { Id = "Stepper"
        Label = "Stepper"
        Hint = "Multi-step wizard."
        Group = "Layout"
        Build = demoStepper }
      { Id = "SummaryList"
        Label = "SummaryList"
        Hint = "Single-card container of label/value rows."
        Group = "Layout"
        Build = demoSummaryList }
      { Id = "Disclosure"
        Label = "Disclosure"
        Hint = "Accordion / collapsible section."
        Group = "Layout"
        Build = demoDisclosure }
      // Custom
      { Id = "Custom"
        Label = "Custom"
        Hint = "Module-registered escape hatch."
        Group = "Custom"
        Build = demoCustom } ]

// ─── Coverage assertion ──────────────────────────────────────────────────
//
// At catalog start, assert every NodeKind variant in the type contract has
// at least one entry. Surfaces missing-coverage as a startup error rather
// than a silent gap. The check is by string id against a pinned list of
// kinds the §4b contract currently defines.

let expectedKindIds: string list =
    [ "Metric"
      "Heading"
      "Markdown"
      "Badge"
      "Callout"
      "Progress"
      "Sparkline"
      "Skeleton"
      "LabelValueRow"
      "Button"
      "Select"
      "Form"
      "FormRangedNumber"
      "FormSegmentedChoice"
      "Filters"
      "FileUpload"
      "Grid"
      "Chart"
      "Table"
      "Map"
      "Dashboard"
      "Stack"
      "GridLayout"
      "GridLayoutIrregular"
      "SplitPanel"
      "Tabs"
      "Card"
      "Stepper"
      "SummaryList"
      "Disclosure"
      "Custom" ]

let missingKinds () : string list =
    let present = entries |> List.map (fun e -> e.Id) |> Set.ofList
    expectedKindIds |> List.filter (fun id -> not (Set.contains id present))
