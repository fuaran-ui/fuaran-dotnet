module Fuaran.Samples.Catalog.Parity

// ============================================================================
//  Visual-parity pairs (Medium-J / Feliz-parity-audit
//  follow-on).
//
//  Each pair is (Fuaran node, hand-rolled Feliz function) where both
//  emit visually-equivalent DOM against the same fuaran-reference.css.
//  The Playwright parity-mode harness pixel-diffs the two at fixed
//  viewport; a > 0.5% diff fails CI.
//
//  Pair coverage v1: chip-strip, stats-list, metric-grid, form,
//  tabbed-card, callout-stack. The pair set is deliberately small —
//  these are the patterns the parity audit surfaced as the
//  load-bearing real-world shapes for §4l down-shift portability.
//  When a new shape proves load-bearing, add a pair below + a
//  parity baseline.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types

// ─── Common shape ────────────────────────────────────────────────────────

type ParityPair =
    { Id: string
      Label: string
      Fuaran: Node<unit>
      Feliz: ReactElement }

// ─── Hand-rolled Feliz equivalents ───────────────────────────────────────
//
// These emit the same class hooks the Fuaran renderer would emit
// (HOST-STYLING-CHECKLIST.md §"per-NodeKind base") so the same
// fuaran-reference.css styles them. The renderer-emitted markup is
// the source of truth; if a class name drifts, the pair fails the
// pixel diff loudly.

let private felizChipStrip () : ReactElement =
    let chips =
        [ for label in [ "Search"; "Display"; "TV"; "OOH"; "Radio" ] ->
              Html.span [ prop.className "fuaran-badge fuaran-badge-neutral"; prop.text label ] ]

    Html.div
        [ prop.className
              "fuaran-stack fuaran-stack-horizontal fuaran-stack-wrap fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.children chips ]

let private felizStatsList () : ReactElement =
    let rows =
        [ "Revenue", "£142,500"
          "Cost of sales", "£68,400"
          "Gross profit", "£74,100"
          "Total", "£158,900" ]

    let rowElements =
        [ for label, value in rows ->
              Html.div
                  [ prop.className "fuaran-label-value-row"
                    prop.children
                        [ Html.span [ prop.className "fuaran-label-value-label"; prop.text label ]
                          Html.span [ prop.className "fuaran-label-value-value"; prop.text value ] ] ] ]

    let heading =
        Html.h3 [ prop.className "fuaran-summary-list-heading"; prop.text "Year-to-date stats" ]

    Html.div
        [ prop.className "fuaran-summary-list fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.children (heading :: rowElements) ]

let private felizMetricGrid () : ReactElement =
    let cells =
        [ "Revenue", "£142.5k", "fuaran-tone-brand"
          "Conversions", "1,284", "fuaran-tone-success"
          "Bounce rate", "32%", "fuaran-tone-warning" ]

    let cellElements =
        [ for label, value, tone in cells ->
              Html.div
                  [ prop.className ("fuaran-metric " + tone + " fuaran-weight-standard fuaran-emphasis-normal")
                    prop.children
                        [ Html.div [ prop.className "fuaran-metric-label"; prop.text label ]
                          Html.div [ prop.className "fuaran-metric-value"; prop.text value ] ] ] ]

    // `style.gridTemplateColumns` requires a typed grid-track list in Feliz;
    // for the catalog's hand-rolled parity Feliz, a raw CSS attribute via
    // `prop.custom` is the simpler match for the inline `repeat(3, 1fr)`
    // declaration. The catalog isn't trying to teach the Feliz style API.
    Html.div
        [ prop.className "fuaran-grid-layout fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.custom ("style", "display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px;")
          prop.children cellElements ]

let private felizForm () : ReactElement =
    Html.form
        [ prop.className "fuaran-form fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.children
              [ Html.div
                    [ prop.className "fuaran-form-field"
                      prop.children
                          [ Html.label [ prop.className "fuaran-form-label"; prop.text "Cohort name" ]
                            Html.input [ prop.className "fuaran-form-input"; prop.type' "text" ] ] ]
                Html.div
                    [ prop.className "fuaran-form-field"
                      prop.children
                          [ Html.label [ prop.className "fuaran-form-label"; prop.text "Sample size" ]
                            Html.input [ prop.className "fuaran-form-input"; prop.type' "number" ] ] ]
                Html.button
                    [ prop.className "fuaran-button fuaran-button-secondary"
                      prop.type' "submit"
                      prop.text "Submit" ] ] ]

let private felizTabbedCard () : ReactElement =
    Html.div
        [ prop.className
              "fuaran-tabs fuaran-tabs-horizontal fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.children
              [ Html.div
                    [ prop.className "fuaran-tabs-bar"
                      prop.children
                          [ Html.button [ prop.className "fuaran-tab is-active"; prop.text "Overview" ]
                            Html.button [ prop.className "fuaran-tab"; prop.text "Details" ] ] ]
                Html.div
                    [ prop.className "fuaran-tabs-pane"
                      prop.children
                          [ Html.div
                                [ prop.className
                                      "fuaran-card fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
                                  prop.children
                                      [ Html.h3 [ prop.className "fuaran-card-heading"; prop.text "Overview" ]
                                        Html.div
                                            [ prop.className "fuaran-card-body"; prop.text "Overview pane content." ] ] ] ] ] ] ]

let private felizCalloutStack () : ReactElement =
    let entries =
        [ "info", "Heads up", "Informational callout body."
          "warning", "Heads up", "Warning callout body."
          "critical", "Action required", "Critical callout body." ]

    let calloutElements =
        [ for tone, headline, body in entries ->
              Html.div
                  [ prop.className (
                        "fuaran-callout fuaran-tone-"
                        + tone
                        + " fuaran-weight-standard fuaran-emphasis-normal"
                    )
                    prop.children
                        [ Html.div [ prop.className "fuaran-callout-heading"; prop.text headline ]
                          Html.div [ prop.className "fuaran-callout-body"; prop.text body ] ] ] ]

    Html.div
        [ prop.className
              "fuaran-stack fuaran-stack-vertical fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
          prop.children calloutElements ]

// ─── Fuaran equivalents ────────────────────────────────────────────────────

let private fuaranChipStrip () : Node<unit> =
    Fuaran.stack
        "parity-chip-strip"
        { Defaults.stack<unit> with
            Orientation = Orientation.Horizontal
            Wrap = true
            Children =
                [ for label in [ "Search"; "Display"; "TV"; "OOH"; "Radio" ] ->
                      { Id = "chip-" + label
                        Kind =
                          NodeKind.Badge(
                              { Label = TextSource.Literal label
                                Variant = BadgeVariant.Neutral }
                          )
                        State = None
                        Style = None
                        Accessibility = Defaults.Accessibility.none
                        Motion = Defaults.Motion.none
                        ExtraAttributes = None } ] }

let private fuaranStatsList () : Node<unit> =
    Fuaran.summaryList
        "parity-stats-list"
        { Defaults.summaryList<unit> with
            Heading = Some(TextSource.Literal "Year-to-date stats")
            Children =
                [ for label, value, emph in
                      [ "Revenue", 142_500.0, false
                        "Cost of sales", 68_400.0, false
                        "Gross profit", 74_100.0, false
                        "Total", 158_900.0, true ] ->
                      Fuaran.labelValueRow
                          ("lvr-" + label.Replace(" ", "-"))
                          { Defaults.labelValueRow with
                              Label = TextSource.Literal label
                              Value = Binding.Static(Some value)
                              Format = format.currency "GBP"
                              Emphasis = emph } ] }

let private fuaranMetricGrid () : Node<unit> =
    Fuaran.gridLayout
        "parity-metric-grid"
        { Defaults.gridLayout<unit> with
            Cols = 3
            Children =
                [ for label, value, tone in
                      [ "Revenue", 142_500.0, ToneVariant.Brand
                        "Conversions", 1_284.0, ToneVariant.Success
                        "Bounce rate", 32.0, ToneVariant.Warning ] ->
                      Fuaran.metric
                          ("metric-" + label.Replace(" ", "-"))
                          { Defaults.metric with
                              Label = TextSource.Literal label
                              Value = Binding.Static(Some value)
                              Tone = tone
                              Format =
                                  (if label = "Bounce rate" then
                                       format.percent (Some 0)
                                   else
                                       format.currency "GBP") } ] }

let private fuaranForm () : Node<unit> =
    Fuaran.form
        "parity-form"
        { Defaults.form<unit> with
            SubmitLabel = TextSource.Literal "Submit"
            Fields =
                [ { Defaults.formField<unit> with
                      Id = "cohort-name"
                      Label = TextSource.Literal "Cohort name"
                      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Some(fun _ -> Action.Chain [])) }
                  { Defaults.formField<unit> with
                      Id = "sample-size"
                      Label = TextSource.Literal "Sample size"
                      Kind = FormFieldKind.Number(Some(Binding.Static(Some 0.0)), Some(fun _ -> Action.Chain [])) } ] }

let private fuaranTabbedCard () : Node<unit> =
    Fuaran.tabs
        "parity-tabbed-card"
        { Defaults.tabs<unit> with
            ActiveIndex = Binding.Static(Some 0)
            Children =
                [ Fuaran.card
                      "tab-overview"
                      { Defaults.card<unit> with
                          Heading = Some(TextSource.Literal "Overview")
                          Children = [ Fuaran.markdown "tab-overview-body" "Overview pane content." ] }
                  Fuaran.card
                      "tab-details"
                      { Defaults.card<unit> with
                          Heading = Some(TextSource.Literal "Details")
                          Children = [ Fuaran.markdown "tab-details-body" "Details pane content." ] } ] }

let private fuaranCalloutStack () : Node<unit> =
    Fuaran.stack
        "parity-callout-stack"
        { Defaults.stack<unit> with
            Orientation = Orientation.Vertical
            Children =
                [ for tone, headline, body in
                      [ ToneVariant.Info, "Heads up", "Informational callout body."
                        ToneVariant.Warning, "Heads up", "Warning callout body."
                        ToneVariant.Critical, "Action required", "Critical callout body." ] ->
                      Fuaran.callout
                          ("co-" + Matrix.toneLabel tone)
                          { Defaults.callout with
                              Tone = tone
                              Heading = Some(TextSource.Literal headline)
                              Body = TextSource.Literal body } ] }

// ─── Public pair list ────────────────────────────────────────────────────

let pairs: ParityPair list =
    [ { Id = "chip-strip"
        Label = "Chip strip"
        Fuaran = fuaranChipStrip ()
        Feliz = felizChipStrip () }
      { Id = "stats-list"
        Label = "Stats list"
        Fuaran = fuaranStatsList ()
        Feliz = felizStatsList () }
      { Id = "metric-grid"
        Label = "Metric grid"
        Fuaran = fuaranMetricGrid ()
        Feliz = felizMetricGrid () }
      { Id = "form"
        Label = "Form"
        Fuaran = fuaranForm ()
        Feliz = felizForm () }
      { Id = "tabbed-card"
        Label = "Tabbed card"
        Fuaran = fuaranTabbedCard ()
        Feliz = felizTabbedCard () }
      { Id = "callout-stack"
        Label = "Callout stack"
        Fuaran = fuaranCalloutStack ()
        Feliz = felizCalloutStack () } ]
