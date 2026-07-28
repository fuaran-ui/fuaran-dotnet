module Fuaran.UI.Tests.QuerySourceTests

// Phase 323 task 1 + Phase 324 typed core — the client query-source seam +
// resolve→check→render gate. Proves the async `IClientQuerySource` wrapper feeds
// a typed result schema into `QueryBinding.check` so a type-mismatched dashboard
// NEVER renders (default-deny before render), and that the schema-only (privacy)
// path types a complete dashboard with no rows crossing the boundary.
//
// .NET pipeline (Expecto). The module under test is FSharp.Core +
// Fuaran.Core.Column/DataFrame only and Fable-clean, so the same gate runs in the
// fuaran-live fable-host; `Async.RunSynchronously` is the .NET test driver only.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.QuerySource
open Fuaran.UI.QueryBinding
open Fuaran.Core

// ─── fixtures ────────────────────────────────────────────────────────────────

/// A Metric whose numeric `Source` is bound to query column `col`.
let private metricBoundTo (id: string) (col: string) : Node<obj> =
    Fuaran.metric
        id
        { Defaults.metric with
            Value = binding.query col (fun (x: float) -> x) }

/// A two-column result with one row: revenue (Float) + category (String).
let private salesTable: Table =
    { Schema = [ "revenue", FloatType; "category", StringType ]
      Columns =
        [ Column.create "revenue" FloatType [ Float 42.0 ]
          Column.create "category" StringType [ Str "books" ] ] }

/// The same schema with ZERO rows — the privacy-mode shape (no row data).
let private salesSchemaOnly: Table =
    { Schema = [ "revenue", FloatType; "category", StringType ]
      Columns = [ Column.create "revenue" FloatType []; Column.create "category" StringType [] ] }

let private aRequest = QueryRequest.Dialect "select revenue, category from sales"

let private run (a: Async<'T>) : 'T = Async.RunSynchronously a

// ─── the seam + gate ─────────────────────────────────────────────────────────

let private gateTests =
    testList
        "resolve → check → render gate"
        [ testCase "a well-typed dashboard resolves clean and carries the rows"
          <| fun _ ->
              let src = InMemoryQuerySource.ofTable salesTable
              let dashboard = metricBoundTo "m1" "revenue"

              match run (resolveAndCheck src aRequest false dashboard) with
              | Ok(tree, resolution) ->
                  Expect.equal tree.Id "m1" "the validated tree is returned for render"
                  Expect.isSome (QueryResolution.rows resolution) "full fetch carries the rows"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "a type-mismatched dashboard NEVER renders (default-deny before render)"
          <| fun _ ->
              let src = InMemoryQuerySource.ofTable salesTable
              // bind the string `category` column into a numeric Metric sink
              let dashboard = metricBoundTo "m1" "category"

              match run (resolveAndCheck src aRequest false dashboard) with
              | Error(PortalError.TypeMismatch [ d ]) ->
                  Expect.equal d.Code TypeMismatchCode "FUARAN066 — rejected, not rendered"
                  Expect.equal d.Column "category" "names the offending column"
              | other -> failtestf "expected a TypeMismatch PortalError, got %A" other

          testCase "a source failure surfaces as PortalError.Source (no render)"
          <| fun _ ->
              let failing =
                  InMemoryQuerySource.ofResolver (fun _ -> Error(QuerySourceError.ExecutionFailed "bad SQL"))

              match run (resolveAndCheck failing aRequest false (metricBoundTo "m1" "revenue")) with
              | Error(PortalError.Source(QuerySourceError.ExecutionFailed d)) ->
                  Expect.stringContains d "bad SQL" "carries the resolver detail"
              | other -> failtestf "expected PortalError.Source, got %A" other ]

// ─── schema-only privacy mode ────────────────────────────────────────────────

let private privacyTests =
    testList
        "schema-only privacy mode"
        [ testCase "schemaOnly=true types the dashboard with NO rows crossing the boundary"
          <| fun _ ->
              let src = InMemoryQuerySource.ofTable salesTable
              let dashboard = metricBoundTo "m1" "revenue"

              match run (resolveAndCheck src aRequest true dashboard) with
              | Ok(_tree, resolution) ->
                  Expect.equal (QueryResolution.rows resolution) None "privacy mode carries no rows"

                  Expect.equal
                      (QueryResolution.schema resolution)
                      [ "revenue", FloatType; "category", StringType ]
                      "but the schema is present to type the UI"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "checkDashboard types a complete dashboard against a ZERO-ROW schema"
          <| fun _ ->
              // The whole privacy proof: structure alone is sufficient to validate.
              let schema = salesSchemaOnly.Schema

              Expect.isOk
                  (checkDashboard schema (metricBoundTo "m1" "revenue"))
                  "float→numeric Metric types clean from schema alone"

              match checkDashboard schema (metricBoundTo "m1" "category") with
              | Error [ d ] -> Expect.equal d.Code TypeMismatchCode "and the mismatch is still caught with no rows"
              | other -> failtestf "expected one FUARAN066, got %A" other

          testCase "a schema-only resolution still proves the offline path resolves"
          <| fun _ ->
              let src = InMemoryQuerySource.ofTable salesSchemaOnly

              match run (src.Resolve(aRequest, true)) with
              | Ok(QueryResolution.SchemaOnly s) -> Expect.equal (List.length s) 2 "schema-only resolution, two columns"
              | other -> failtestf "expected SchemaOnly, got %A" other ]

[<Tests>]
let tests =
    testList "Phase 323/324 — client query-source seam + gate" [ gateTests; privacyTests ]
