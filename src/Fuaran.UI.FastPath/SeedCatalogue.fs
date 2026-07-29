namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.FastPath

/// A curated, domain-neutral standard library of UI patterns — each
/// signature-searchable and instantiable into a real Fuaran tree. These are the
/// universally-useful primitives and the canonical "how to author a pattern"
/// reference; you extend the bank by registering additional entries into the same
/// registry type (`FastPath.bank`), never by forking the search engine.
///
/// A few patterns are **ComputeLayer-bound**: their data binding is a real
/// `Fuaran.Core.DataFrame` transform pipeline, so the bank demonstrates retrieval
/// and the serverless compute layer together — pick the pattern, and its value is
/// computed client-side from data with no server.
module SeedCatalogue =

    // ── value readers (unbound holes fall back to a sensible default) ────────
    let private strOf (values: Map<string, string>) (addr: string) (dflt: string) : string =
        match Map.tryFind addr values with
        | Some s when s <> "" -> s
        | _ -> dflt

    let private numOf (values: Map<string, string>) (addr: string) (dflt: float) : float =
        match Map.tryFind addr values with
        | Some s ->
            match System.Double.TryParse s with
            | true, v -> v
            | _ -> dflt
        | None -> dflt

    // ── tree helpers ─────────────────────────────────────────────────────────
    let private metricNode (id: string) (label: string) (value: float) (tone: ToneVariant) : Node<unit> =
        Fuaran.metric
            id
            { Defaults.metric with
                Label = TextSource.Literal label
                Value = Binding.Static(Some value)
                Tone = tone }

    let private vbox
        (id: string)
        (role: BoxRole)
        (heading: TextSource option)
        (children: Node<unit> list)
        : Node<unit> =
        Fuaran.box
            id
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, Some 12)
              Role = role
              Heading = heading
              Children = children }

    let private hbox (id: string) (children: Node<unit> list) : Node<unit> =
        Fuaran.box
            id
            { Layout = BoxLayout.Flex(Orientation.Horizontal, true, Some 12)
              Role = BoxRole.Group
              Heading = None
              Children = children }

    let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
        Fuaran.heading
            id
            { Level = level
              Text = TextSource.Literal text
              Variant = HeadingVariant.Standard }

    let private calloutNode (id: string) (tone: ToneVariant) (heading: string) (body: string) : Node<unit> =
        Fuaran.callout
            id
            { Defaults.callout with
                Tone = tone
                Heading = Some(TextSource.Literal heading)
                Body = TextSource.Literal body }

    let private ctaButton (id: string) (label: string) : Node<unit> =
        Fuaran.button
            id
            { Defaults.button with
                Label = TextSource.Literal label
                OnClick = Action.Navigate "cta"
                Variant = ButtonVariant.Primary }

    // ── ComputeLayer sample: an embedded table + a real transform pipeline ───
    let private salesTable: Table =
        { Schema = [ "region", StringType; "revenue", FloatType; "units", IntType ]
          Columns =
            [ { Name = "region"
                Type = StringType
                Cells = [ Cell.Str "North"; Cell.Str "South"; Cell.Str "East" ] }
              { Name = "revenue"
                Type = FloatType
                Cells = [ Cell.Float 4800.0; Cell.Float 9100.0; Cell.Float 3600.0 ] }
              { Name = "units"
                Type = IntType
                Cells = [ Cell.Int 120; Cell.Int 140; Cell.Int 90 ] } ] }

    /// Group by region, sum revenue — a real serialisable pipeline, evaluated
    /// client-side by the consumer's `Fuaran.Core.DataFrame` evaluator.
    let private revenueByRegion: Transform list =
        [ GroupBy(
              [ "region" ],
              [ { Name = "revenue"
                  Fn = Sum
                  Of = "revenue" } ]
          ) ]

    /// A data binding computed from the embedded table by the transform pipeline —
    /// no server, no hard-coded number.
    let private computedRevenue: Binding<float> =
        Binding.Transform(DataSource.Embedded salesTable, revenueByRegion, None)

    // ── the patterns ─────────────────────────────────────────────────────────

    let private singleMetric: Pattern =
        { Id = "single-metric"
          Title = "Single metric"
          Summary = "One labelled KPI value."
          ResultType = "Metric"
          Holes = [ textHole "metric.label" "label"; numberHole "metric.value" "value" 0 1000000 ]
          Build =
            fun v -> metricNode "sm" (strOf v "metric.label" "Metric") (numOf v "metric.value" 0.0) ToneVariant.Default }

    let private metricStrip: Pattern =
        { Id = "metric-strip"
          Title = "Metric strip"
          Summary = "A horizontal strip of three KPIs."
          ResultType = "Box"
          Holes =
            [ textHole "m0.label" "first label"
              numberHole "m0.value" "first value" 0 1000000
              textHole "m1.label" "second label"
              numberHole "m1.value" "second value" 0 1000000
              textHole "m2.label" "third label"
              numberHole "m2.value" "third value" 0 1000000 ]
          Build =
            fun v ->
                hbox
                    "strip"
                    [ metricNode "strip-0" (strOf v "m0.label" "First") (numOf v "m0.value" 0.0) ToneVariant.Default
                      metricNode "strip-1" (strOf v "m1.label" "Second") (numOf v "m1.value" 0.0) ToneVariant.Default
                      metricNode "strip-2" (strOf v "m2.label" "Third") (numOf v "m2.value" 0.0) ToneVariant.Default ] }

    let private kpiCard: Pattern =
        { Id = "kpi-card"
          Title = "KPI card"
          Summary = "A single big value inside a titled card."
          ResultType = "Card"
          Holes = [ textHole "card.label" "label"; textHole "card.value" "value" ]
          Build =
            fun v ->
                Fuaran.card
                    "kpi"
                    { Defaults.card with
                        Heading = Some(TextSource.Literal(strOf v "card.label" "Metric"))
                        Children = [ Fuaran.markdown "kpi-v" (strOf v "card.value" "—") ] } }

    let private dashboardShell: Pattern =
        { Id = "dashboard-shell"
          Title = "Dashboard shell"
          Summary = "A titled dashboard with a two-metric strip."
          ResultType = "Box"
          Holes =
            [ textHole "title" "title"
              textHole "m0.label" "first label"
              numberHole "m0.value" "first value" 0 1000000
              textHole "m1.label" "second label"
              numberHole "m1.value" "second value" 0 1000000 ]
          Build =
            fun v ->
                vbox
                    "dash"
                    BoxRole.Dashboard
                    (Some(TextSource.Literal(strOf v "title" "Dashboard")))
                    [ hbox
                          "dash-strip"
                          [ metricNode
                                "dash-0"
                                (strOf v "m0.label" "First")
                                (numOf v "m0.value" 0.0)
                                ToneVariant.Default
                            metricNode
                                "dash-1"
                                (strOf v "m1.label" "Second")
                                (numOf v "m1.value" 0.0)
                                ToneVariant.Default ] ] }

    let private hero: Pattern =
        { Id = "hero"
          Title = "Hero"
          Summary = "A headline, a supporting line, and a call to action."
          ResultType = "Box"
          Holes =
            [ textHole "headline" "headline"
              textHole "sub" "supporting line"
              textHole "cta" "button label" ]
          Build =
            fun v ->
                vbox
                    "hero"
                    BoxRole.Group
                    None
                    [ headingNode "hero-h" 2 (strOf v "headline" "Ship your ideas faster")
                      Fuaran.markdown "hero-sub" (strOf v "sub" "The fastest way to build.")
                      ctaButton "hero-cta" (strOf v "cta" "Get started") ] }

    let private calloutInfo: Pattern =
        { Id = "callout-info"
          Title = "Info callout"
          Summary = "A titled informational callout."
          ResultType = "Callout"
          Holes = [ textHole "heading" "heading"; textHole "body" "body" ]
          Build = fun v -> calloutNode "info" ToneVariant.Info (strOf v "heading" "Note") (strOf v "body" "") }

    let private emptyState: Pattern =
        { Id = "empty-state"
          Title = "Empty state"
          Summary = "A subdued placeholder for when there is nothing to show."
          ResultType = "Callout"
          Holes = [ textHole "message" "message" ]
          Build =
            fun v ->
                calloutNode
                    "empty"
                    ToneVariant.Subdued
                    "Nothing here yet"
                    (strOf v "message" "Add your first item to get started.") }

    let private errorState: Pattern =
        { Id = "error-state"
          Title = "Error state"
          Summary = "A critical-tone message for a failed operation."
          ResultType = "Callout"
          Holes = [ textHole "message" "message" ]
          Build =
            fun v ->
                calloutNode "error" ToneVariant.Critical "Something went wrong" (strOf v "message" "Please try again.") }

    let private featureList: Pattern =
        { Id = "feature-list"
          Title = "Feature list"
          Summary = "A three-item bulleted list."
          ResultType = "Markdown"
          Holes = [ textHole "f0" "first"; textHole "f1" "second"; textHole "f2" "third" ]
          Build =
            fun v ->
                Fuaran.markdown
                    "features"
                    ("- "
                     + strOf v "f0" "Fast"
                     + "\n- "
                     + strOf v "f1" "Portable"
                     + "\n- "
                     + strOf v "f2" "Typed") }

    let private section: Pattern =
        { Id = "section"
          Title = "Section"
          Summary = "A heading with a body paragraph."
          ResultType = "Box"
          Holes = [ textHole "title" "title"; textHole "body" "body" ]
          Build =
            fun v ->
                vbox
                    "section"
                    BoxRole.Group
                    None
                    [ headingNode "section-h" 3 (strOf v "title" "Section")
                      Fuaran.markdown "section-b" (strOf v "body" "") ] }

    let private ctaBanner: Pattern =
        { Id = "cta-banner"
          Title = "CTA banner"
          Summary = "A brand callout with a call-to-action button."
          ResultType = "Box"
          Holes = [ textHole "message" "message"; textHole "cta" "button label" ]
          Build =
            fun v ->
                vbox
                    "banner"
                    BoxRole.Group
                    None
                    [ calloutNode
                          "banner-c"
                          ToneVariant.Brand
                          "Ready?"
                          (strOf v "message" "Start your free trial today.")
                      ctaButton "banner-cta" (strOf v "cta" "Start free") ] }

    /// ComputeLayer-bound: a metric whose value is computed by a real transform
    /// pipeline over an embedded table — client-side, no server.
    let private computeMetric: Pattern =
        { Id = "compute-metric"
          Title = "Computed metric"
          Summary = "A KPI whose value is computed live from data by a transform pipeline (no server)."
          ResultType = "Metric"
          Holes = [ textHole "metric.label" "label" ]
          Build =
            fun v ->
                Fuaran.metric
                    "cm"
                    { Defaults.metric with
                        Label = TextSource.Literal(strOf v "metric.label" "Revenue by region")
                        Value = computedRevenue } }

    /// ComputeLayer-bound: a dashboard whose metric is computed from a grouped
    /// transform — the serverless compute layer, discoverable by signature.
    let private computeDashboard: Pattern =
        { Id = "compute-dashboard"
          Title = "Computed dashboard"
          Summary = "A titled dashboard whose figure is computed live from data (no server)."
          ResultType = "Box"
          Holes = [ textHole "title" "title" ]
          Build =
            fun v ->
                vbox
                    "cd"
                    BoxRole.Dashboard
                    (Some(TextSource.Literal(strOf v "title" "Revenue")))
                    [ Fuaran.metric
                          "cd-m"
                          { Defaults.metric with
                              Label = TextSource.Literal "Revenue by region"
                              Value = computedRevenue } ] }

    /// The full seed catalogue.
    let all: Pattern list =
        [ singleMetric
          metricStrip
          kpiCard
          dashboardShell
          hero
          calloutInfo
          emptyState
          errorState
          featureList
          section
          ctaBanner
          computeMetric
          computeDashboard ]

    /// The default public bank — search-ready.
    let defaultBank: Bank = bank all
