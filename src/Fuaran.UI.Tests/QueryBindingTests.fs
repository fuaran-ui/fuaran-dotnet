module Fuaran.UI.Tests.QueryBindingTests

// Phase 323 — the typed query-result-schema → Binding<'T> thread.
//
// Proves "the query result schema *is* the UI contract": a query column typed
// `string` cannot be bound into a numeric UI sink (a Metric, a chart Y-series)
// — the mismatch is a typed, named defect (FUARAN066) surfaced before render,
// with the §4d AI-recovery fields. An absent column is FUARAN067 (default-deny
// by shape, FGP 3). The check reads only the schema's (name, ColumnType) pairs,
// so a schema-only, zero-row fetch types a complete dashboard (privacy mode).
//
// .NET pipeline (Expecto). The module under test is FSharp.Core +
// Fuaran.Core.Column only and contains no `System.*` / reflection, so the same
// relation runs identically under Fable — Fable-run parity is left to the
// cross-host harness (none in this repo yet).

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.QueryBinding
open Fuaran.Core

// ─── tree builders (query-bound sinks) ───────────────────────────────────────

/// A Metric whose numeric `Source` is bound to query column `col`.
let private metricBoundTo (id: string) (col: string) : Node<obj> =
    Fuaran.metric
        id
        { Defaults.metric with
            Value = binding.query col (fun (x: float) -> x) }

/// A LabelValueRow whose numeric `Source` is bound to query column `col`.
let private labelValueBoundTo (id: string) (col: string) : Node<obj> =
    Fuaran.labelValueRow
        id
        { Defaults.labelValueRow with
            Value = binding.query col (fun (x: float) -> x) }

/// A line chart whose data IS a query (Source = Binding.Query), with an X axis
/// column + numeric Y-series columns naming result columns.
let private chartBoundTo (id: string) (xField: string) (yFields: string list) : Node<obj> =
    Fuaran.chart
        id
        { Defaults.chart with
            Source = binding.query "sales" (fun (x: obj seq) -> x)
            XField = xField
            YFields = yFields }

let private dashboard (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard id { Children = children }

// ─── the relation (pure, default-deny) ───────────────────────────────────────

let private relationTests =
    testList
        "compatible relation"
        [ testCase "numeric sink accepts int / float only"
          <| fun _ ->
              Expect.isTrue (compatible FloatType BindingSinkClass.Numeric) "float → numeric"
              Expect.isTrue (compatible IntType BindingSinkClass.Numeric) "int → numeric"
              Expect.isFalse (compatible StringType BindingSinkClass.Numeric) "string ✗ numeric"
              Expect.isFalse (compatible BoolType BindingSinkClass.Numeric) "bool ✗ numeric"
              Expect.isFalse (compatible DateType BindingSinkClass.Numeric) "date ✗ numeric"

          testCase "temporal sink accepts date / timestamp only"
          <| fun _ ->
              Expect.isTrue (compatible DateType BindingSinkClass.Temporal) "date → temporal"
              Expect.isTrue (compatible TimestampType BindingSinkClass.Temporal) "timestamp → temporal"
              Expect.isFalse (compatible StringType BindingSinkClass.Temporal) "string ✗ temporal"
              Expect.isFalse (compatible FloatType BindingSinkClass.Temporal) "float ✗ temporal"

          testCase "categorical sink accepts string + date/timestamp-as-label"
          <| fun _ ->
              Expect.isTrue (compatible StringType BindingSinkClass.Categorical) "string → categorical"
              Expect.isTrue (compatible DateType BindingSinkClass.Categorical) "date → categorical (label)"
              Expect.isTrue (compatible TimestampType BindingSinkClass.Categorical) "timestamp → categorical (label)"
              Expect.isFalse (compatible FloatType BindingSinkClass.Categorical) "float ✗ categorical"
              Expect.isFalse (compatible BoolType BindingSinkClass.Categorical) "bool ✗ categorical"

          testCase "boolean sink accepts bool only"
          <| fun _ ->
              Expect.isTrue (compatible BoolType BindingSinkClass.Boolean) "bool → boolean"
              Expect.isFalse (compatible IntType BindingSinkClass.Boolean) "int ✗ boolean"
              Expect.isFalse (compatible StringType BindingSinkClass.Boolean) "string ✗ boolean"

          testCase "default-deny — every ColumnType × sink pairing not allowed is false"
          <| fun _ ->
              // Exactly 8 of the 6×4 = 24 pairings are compatible; the rest deny.
              let allPairs =
                  [ for t in ColumnType.all do
                        for s in
                            [ BindingSinkClass.Numeric
                              BindingSinkClass.Temporal
                              BindingSinkClass.Categorical
                              BindingSinkClass.Boolean ] -> compatible t s ]

              Expect.equal (allPairs |> List.filter id |> List.length) 8 "exactly 8 compatible pairings"

          testCase "compatibleColumnTypes is derived from compatible (one source of truth)"
          <| fun _ ->
              Expect.equal
                  (compatibleColumnTypes BindingSinkClass.Numeric)
                  [ IntType; FloatType ]
                  "numeric ← int, float"

              Expect.equal (compatibleColumnTypes BindingSinkClass.Boolean) [ BoolType ] "boolean ← bool" ]

// ─── positive: a clean dashboard types against its schema ─────────────────────

let private positiveTests =
    testList
        "positive (schema types the UI clean)"
        [ testCase "a currency Float column bound to a Metric passes"
          <| fun _ ->
              let schema: Schema = [ "revenue", FloatType; "region", StringType ]
              let tree = metricBoundTo "m1" "revenue"
              Expect.equal (check schema tree) [] "no defect — float → numeric Metric"

          testCase "schema-only (zero-row) fetch types a complete dashboard with no defects"
          <| fun _ ->
              // A schema is purely (name, ColumnType) pairs — no rows. A whole
              // dashboard (metrics + sparkline + chart) types against it.
              let schema: Schema =
                  [ "revenue", FloatType
                    "units", IntType
                    "month", DateType
                    "category", StringType ]

              let tree =
                  dashboard
                      "dash"
                      [ metricBoundTo "m1" "revenue"
                        metricBoundTo "m2" "units"
                        labelValueBoundTo "lv" "revenue"
                        chartBoundTo "c1" "month" [ "revenue"; "units" ] ]

              Expect.equal (check schema tree) [] "privacy-mode: schema alone types the dashboard" ]

// ─── negative: incompatible / absent bindings are typed defects ───────────────

let private negativeTests =
    testList
        "negative (typed defects before render)"
        [ testCase "a String column bound into a numeric Metric is FUARAN066 with recovery"
          <| fun _ ->
              let schema: Schema = [ "category", StringType; "revenue", FloatType ]
              let tree = metricBoundTo "m1" "category"

              match check schema tree with
              | [ d ] ->
                  Expect.equal d.Code TypeMismatchCode "FUARAN066"
                  Expect.equal d.Code "FUARAN066" "stable code"
                  Expect.equal d.NodeId "m1" "located on the Metric node"
                  Expect.equal d.Column "category" "names the offending column"
                  Expect.equal d.Sink BindingSinkClass.Numeric "names the sink class"
                  // §4d recovery — the schema columns that WOULD type-check here.
                  Expect.equal d.AvailableFields [ "revenue" ] "recovery lists the numeric column"
                  Expect.isTrue d.Suggestion.IsSome "carries a repair suggestion"
              | other -> failtestf "expected exactly one FUARAN066, got %A" other

          testCase "a String column bound to a chart Y-series is FUARAN066"
          <| fun _ ->
              let schema: Schema =
                  [ "month", DateType; "category", StringType; "revenue", FloatType ]

              // XField month (date → categorical axis: ok); YField category
              // (string → numeric series: reject).
              let tree = chartBoundTo "c1" "month" [ "category" ]

              match check schema tree with
              | [ d ] ->
                  Expect.equal d.Code TypeMismatchCode "FUARAN066 on the chart Y-series"
                  Expect.equal d.Column "category" "the string Y-field"
                  Expect.equal d.Sink BindingSinkClass.Numeric "chart Y-series is numeric"
                  Expect.equal d.AvailableFields [ "revenue" ] "recovery lists the numeric column"
              | other -> failtestf "expected one FUARAN066, got %A" other

          testCase "a binding to a column ABSENT from the schema is FUARAN067 (default-deny)"
          <| fun _ ->
              let schema: Schema = [ "revenue", FloatType ]
              let tree = metricBoundTo "m1" "profit"

              match check schema tree with
              | [ d ] ->
                  Expect.equal d.Code ColumnAbsentCode "FUARAN067"
                  Expect.equal d.Column "profit" "the absent column"
                  Expect.equal d.AvailableFields [ "revenue" ] "recovery lists the available columns"
              | other -> failtestf "expected one FUARAN067, got %A" other

          testCase "an empty schema denies every query binding (default-deny by shape)"
          <| fun _ ->
              let tree = metricBoundTo "m1" "revenue"

              match check [] tree with
              | [ d ] -> Expect.equal d.Code ColumnAbsentCode "absent against an empty schema"
              | other -> failtestf "expected one FUARAN067, got %A" other

          testCase "a temporal column bound into a numeric Metric is rejected"
          <| fun _ ->
              let schema: Schema = [ "month", DateType ]
              let tree = metricBoundTo "m1" "month"
              let defects = check schema tree
              Expect.equal (List.length defects) 1 "date ✗ numeric Metric"
              Expect.equal defects.Head.Code TypeMismatchCode "FUARAN066" ]

// ─── the walk surfaces only query-bound slots ────────────────────────────────

let private walkTests =
    testList
        "queryBoundRefs"
        [ testCase "a Static-bound Metric contributes no query ref"
          <| fun _ ->
              let tree =
                  Fuaran.metric
                      "m1"
                      { Defaults.metric with
                          Value = Binding.Static(Some 42.0) }

              Expect.isEmpty (queryBoundRefs tree) "Static binding is not a query ref"

          testCase "a static-source chart's X/Y fields are not checked (schema describes another source)"
          <| fun _ ->
              let tree =
                  Fuaran.chart
                      "c1"
                      { Defaults.chart with
                          Source = Binding.Static(Some Seq.empty)
                          XField = "month"
                          YFields = [ "revenue" ] }

              Expect.isEmpty (queryBoundRefs tree) "non-query chart source ⇒ fields out of schema scope"

          testCase "nested children are walked (defect found under a Dashboard)"
          <| fun _ ->
              let schema: Schema = [ "revenue", FloatType ]
              let tree = dashboard "dash" [ dashboard "inner" [ metricBoundTo "m1" "missing" ] ]
              Expect.equal (List.length (check schema tree)) 1 "the deep Metric's bad binding surfaces" ]

[<Tests>]
let tests =
    testList "Phase 323 — query-binding typed thread" [ relationTests; positiveTests; negativeTests; walkTests ]
