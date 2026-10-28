module Fuaran.UI.Renderer.Server.Tests.MetricComparisonTests

// ============================================================================
//  Phase 2472 — a Metric can carry a second comparison.
//
//  The design-seam pilot's trading row compared one figure against two periods
//  at once and had to push the second comparison into `subtext` as text, which
//  lost its glyph, its sentiment and its polarity. `MetricSpec.comparisons`
//  carries each further comparison; these pins hold that the server render
//  draws each one as its own trend line — glyph, sentiment class, figure and
//  label — under its own polarity, that the email and speech projections carry
//  it, and that a tile with no comparisons renders exactly as before.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private count (needle: string) (haystack: string) =
    let rec go (from: int) (n: int) =
        match haystack.IndexOf(needle, from, System.StringComparison.Ordinal) with
        | -1 -> n
        | i -> go (i + needle.Length) (n + 1)

    go 0 0

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private comparison label value polarity : MetricComparison =
    { Label = TextSource.Literal label
      Value = Binding.Static(Some value)
      Format = CellFormat.Percent(Some 1)
      Polarity = polarity }

/// The pilot's KPI row figure: orders yesterday, against last week and last year.
let private ordersSpec: MetricSpec =
    { Defaults.metric with
        Label = TextSource.Literal "Orders yesterday"
        Value = Binding.Static(Some 1284.0)
        Format = CellFormat.Number(Some 0)
        Comparisons =
            [ comparison "vs same day last week" 0.042 TrendPolarity.HigherIsBetter
              comparison "vs same day last year" -0.031 TrendPolarity.HigherIsBetter ] }

let private node (spec: MetricSpec) : Node<obj> = Fuaran.metric "orders" spec

let private html (spec: MetricSpec) =
    Render.render BindingResolver.empty (node spec)

[<Tests>]
let tests =
    testList
        "Phase 2472 — a Metric carries further comparisons as trend lines"
        [ test "each comparison is its own trend line, with its own glyph, sentiment and label" {
              let out = html ordersSpec
              Expect.equal (count "fuaran-metric-trend-glyph" out) 2 "one glyph per comparison"
              Expect.isTrue (contains "fuaran-metric-trend fuaran-metric-trend-improving" out) "+4.2% improves"
              Expect.isTrue (contains "fuaran-metric-trend fuaran-metric-trend-regressing" out) "−3.1% regresses"
              Expect.isTrue (contains "▲" out) "the improving glyph"
              Expect.isTrue (contains "▼" out) "the regressing glyph"
              Expect.isTrue (contains "vs same day last week" out) "the first label follows its figure"
              Expect.isTrue (contains "vs same day last year" out) "the second label follows its figure"

              Expect.isLessThan
                  (out.IndexOf("vs same day last week", System.StringComparison.Ordinal))
                  (out.IndexOf("vs same day last year", System.StringComparison.Ordinal))
                  "comparisons render in authored order"
          }

          test "a comparison's polarity is its own: LowerIsBetter reads a rise as a regression" {
              let spec =
                  { ordersSpec with
                      Comparisons = [ comparison "vs last year" 0.12 TrendPolarity.LowerIsBetter ] }

              let out = html spec
              Expect.isTrue (contains "fuaran-metric-trend-regressing" out) "a rise on a lower-is-better figure"
              Expect.isFalse (contains "fuaran-metric-trend-improving" out) "and nothing improving"
          }

          test "the primary trend keeps its line, first, with no label" {
              let spec =
                  { ordersSpec with
                      Trend = Some(Binding.Static(Some 0.01))
                      TrendFormat = Some(CellFormat.Percent(Some 1)) }

              let out = html spec
              Expect.equal (count "fuaran-metric-trend-glyph" out) 3 "the trend plus two comparisons"

              let firstTrend =
                  out.IndexOf("fuaran-metric-trend ", System.StringComparison.Ordinal)

              let firstLabel =
                  out.IndexOf("vs same day last week", System.StringComparison.Ordinal)

              Expect.isLessThan firstTrend firstLabel "the primary trend line comes first"
          }

          test "no comparisons renders exactly the tile it always did" {
              let without =
                  html
                      { ordersSpec with
                          Comparisons = []
                          Trend = Some(Binding.Static(Some 0.01)) }

              Expect.equal (count "fuaran-metric-trend-glyph" without) 1 "only the primary trend"
              Expect.isFalse (contains " vs " without) "no comparison label"
          }

          test "an unresolved comparison value keeps its line empty rather than inventing a sentiment" {
              let spec =
                  { ordersSpec with
                      Comparisons =
                          [ { comparison "vs last week" 0.0 TrendPolarity.HigherIsBetter with
                                Value = Binding.State("missing", None) } ] }

              let out = html spec
              Expect.isFalse (contains "fuaran-metric-trend-glyph" out) "no glyph without a value"
              Expect.isFalse (contains "vs last week" out) "no label without a figure"
          }

          test "the email projection carries each comparison as a line" {
              let out = Email.renderWith Email.defaults BindingResolver.empty (node ordersSpec)
              Expect.isTrue (contains "vs same day last week" out) "the first comparison"
              Expect.isTrue (contains "vs same day last year" out) "the second comparison"
          }

          test "the speech projection speaks each comparison after the figure" {
              let script, _ = Speech.projectStatic (node ordersSpec)
              let words = Speech.toPlainText script
              Expect.isTrue (contains "vs same day last week" words) "the first comparison is spoken"
              Expect.isTrue (contains "vs same day last year" words) "the second comparison is spoken"
          } ]
