module Fuaran.UI.Tests.QueryRefineTests

// Phase 324 task 4 — client-side refinement = local data, not round-trips.
// Proves a follow-on tweak (sort / filter / regroup) applies through the
// Fuaran.Core.DataFrame evaluator over the already-fetched rows with ZERO
// re-query and ZERO LLM call, and that a refinement which changes the schema is
// re-typed against the dashboard (the 323 thread) before re-render.
//
// .NET pipeline (Expecto). DataFrame.evalPipeline is the pinned cross-host
// evaluator, so the refinement resolves identically in the fuaran-live fable-host.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.QuerySource
open Fuaran.UI.QueryRefine
open Fuaran.UI.QueryBinding
open Fuaran.Core

// ─── fixtures ────────────────────────────────────────────────────────────────

let private metricBoundTo (id: string) (col: string) : Node<obj> =
    Fuaran.metric
        id
        { Defaults.metric with
            Value = binding.query col (fun (x: float) -> x) }

/// revenue (Float) + category (String), three rows.
let private salesTable: Table =
    { Schema = [ "revenue", FloatType; "category", StringType ]
      Columns =
        [ Column.create "revenue" FloatType [ Float 30.0; Float 10.0; Float 50.0 ]
          Column.create "category" StringType [ Str "books"; Str "toys"; Str "games" ] ] }

let private run (a: Async<'T>) : 'T = Async.RunSynchronously a

// ─── the fast-path ───────────────────────────────────────────────────────────

let private fastPathTests =
    testList
        "local refinement (fast-path)"
        [ testCase "sort descending refines locally, schema unchanged, dashboard still types"
          <| fun _ ->
              let dashboard = metricBoundTo "m1" "revenue"

              match refineLocally salesTable [ Sort [ "revenue", Desc ] ] dashboard with
              | Ok(refined, tree) ->
                  Expect.equal refined.Schema salesTable.Schema "schema unchanged by a sort"
                  Expect.equal (Table.rowCount refined) 3 "all rows retained"
                  Expect.equal tree.Id "m1" "dashboard still valid → render it"
              | Error e -> failtestf "expected Ok, got %s" (RefineError.message e)

          testCase "filter refines locally to fewer rows, dashboard still types"
          <| fun _ ->
              let dashboard = metricBoundTo "m1" "revenue"
              let keepBig = [ Filter(Binary(Gt, Col "revenue", Lit(Float 20.0))) ]

              match refineLocally salesTable keepBig dashboard with
              | Ok(refined, _) -> Expect.equal (Table.rowCount refined) 2 "two rows above 20"
              | Error e -> failtestf "expected Ok, got %s" (RefineError.message e)

          testCase "a schema-changing refinement that drops a bound column is a typed defect (not a wrong render)"
          <| fun _ ->
              // Project to category only — revenue is dropped; the Metric binds revenue.
              let dashboard = metricBoundTo "m1" "revenue"
              let dropRevenue = [ Project [ "category", "category" ] ]

              match refineLocally salesTable dropRevenue dashboard with
              | Error(RefineError.TypeMismatch [ d ]) ->
                  Expect.equal d.Code ColumnAbsentCode "FUARAN067 — revenue gone after the refinement"
                  Expect.equal d.Column "revenue" "names the now-absent column"
              | other -> failtestf "expected a TypeMismatch RefineError, got %A" other

          testCase "a refinement over an unknown column is a typed Eval error"
          <| fun _ ->
              let dashboard = metricBoundTo "m1" "revenue"
              let bad = [ Filter(Binary(Gt, Col "nonexistent", Lit(Float 1.0))) ]

              match refineLocally salesTable bad dashboard with
              | Error(RefineError.Eval(UnknownColumn(name, _))) ->
                  Expect.equal name "nonexistent" "names the unknown column"
              | other -> failtestf "expected an Eval RefineError, got %A" other ]

// ─── the zero-re-query property ──────────────────────────────────────────────

let private zeroRequeryTests =
    testList
        "zero re-query / zero LLM"
        [ testCase "after one resolve, N local refinements cost zero additional source calls"
          <| fun _ ->
              let calls = ref 0

              let src =
                  { new IClientQuerySource with
                      member _.Resolve(_request, schemaOnly) =
                          async {
                              calls.Value <- calls.Value + 1

                              return
                                  Ok(
                                      if schemaOnly then
                                          QueryResolution.SchemaOnly salesTable.Schema
                                      else
                                          QueryResolution.WithRows salesTable
                                  )
                          } }

              let dashboard = metricBoundTo "m1" "revenue"
              let request = QueryRequest.Dialect "select revenue, category from sales"

              match run (resolveAndCheck src request false dashboard) with
              | Ok(_, QueryResolution.WithRows rows) ->
                  // Two follow-on refinements — both local, neither touches the source.
                  Expect.isOk (refineLocally rows [ Sort [ "revenue", Desc ] ] dashboard) "sort is local"

                  Expect.isOk
                      (refineLocally rows [ Filter(Binary(Gt, Col "revenue", Lit(Float 20.0))) ] dashboard)
                      "filter is local"

                  Expect.equal calls.Value 1 "exactly one source call — refinements never re-query"
              | other -> failtestf "expected Ok WithRows, got %A" other ]

[<Tests>]
let tests =
    testList "Phase 324 — client-side refinement" [ fastPathTests; zeroRequeryTests ]
