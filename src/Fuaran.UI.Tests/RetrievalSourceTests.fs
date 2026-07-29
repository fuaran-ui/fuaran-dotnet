module Fuaran.UI.Tests.RetrievalSourceTests

// Phase 325 core — the semantic-retrieval seam + canonical hit schema. Proves
// retrieval hits project into the canonical Table and type-check through the SAME
// QueryBinding/QuerySource thread as relational rows (one UI contract for both
// data planes): a hit's `title` (string) cannot bind into a numeric sink, the
// identical FUARAN066 the relational plane raises.
//
// .NET pipeline (Expecto). The seam is FSharp.Core + Fuaran.Core.Column only and
// Fable-clean; transports (hosted HTTP / paired-device RAG) are deferred host impls.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.RetrievalSource
open Fuaran.UI.QuerySource
open Fuaran.UI.QueryBinding
open Fuaran.Core

// ─── fixtures ────────────────────────────────────────────────────────────────

let private hits: Hit list =
    [ { Score = 0.92
        Title = "Smith v. Jones"
        Snippet = "prior holding on…"
        SourceId = "case-1"
        Date = "2024-01-15" }
      { Score = 0.71
        Title = "Doe v. Roe"
        Snippet = "analogous facts…"
        SourceId = "case-2"
        Date = "2023-11-02" } ]

/// A canonical "find prior cases like this" emission — a relevance Metric + a
/// score-over-date chart, both bound by column name into the retrieval schema.
let private caseHistoryDashboard: Node<obj> =
    Fuaran.dashboard
        "cases"
        { Children =
            [ Fuaran.metric
                  "topScore"
                  { Defaults.metric with
                      Value = binding.query "score" (fun (x: float) -> x) }
              Fuaran.chart
                  "scoreByDate"
                  { Defaults.chart with
                      Source = binding.query "hits" (fun (x: obj seq) -> x)
                      Kind = ChartKind.Bar
                      XField = "date"
                      YFields = [ "score" ] } ] }

let private run (a: Async<'T>) : 'T = Async.RunSynchronously a

// ─── the canonical schema + projection ───────────────────────────────────────

let private projectionTests =
    testList
        "hit → canonical retrieval Table"
        [ testCase "toTable yields the canonical hitSchema and preserves rank order"
          <| fun _ ->
              let table = Hit.toTable hits
              Expect.equal table.Schema hitSchema "schema is the canonical retrieval contract"
              Expect.equal (Table.rowCount table) 2 "both hits projected"

          testCase "the seam returns the canonical Table and honours TopK"
          <| fun _ ->
              let src = InMemoryRetrievalSource.ofHits hits

              match run (src.Retrieve(RetrievalRequest.create "find prior cases" 1)) with
              | Ok table ->
                  Expect.equal table.Schema hitSchema "retrieval result uses the canonical schema"
                  Expect.equal (Table.rowCount table) 1 "TopK=1 truncates to the top hit"
              | Error e -> failtestf "expected Ok, got %s" (RetrievalError.message e)

          testCase "a retrieval failure surfaces as a typed RetrievalError"
          <| fun _ ->
              let failing =
                  InMemoryRetrievalSource.ofResolver (fun _ -> Error(RetrievalError.RetrievalFailed "index offline"))

              match run (failing.Retrieve(RetrievalRequest.create "q" 5)) with
              | Error(RetrievalError.RetrievalFailed d) -> Expect.stringContains d "index offline" "carries the detail"
              | other -> failtestf "expected RetrievalFailed, got %A" other ]

// ─── one UI contract for both data planes ────────────────────────────────────

let private oneContractTests =
    testList
        "one typed thread for retrieval + relational"
        [ testCase "a case-history dashboard types clean against the retrieval schema (same validator)"
          <| fun _ ->
              // checkDashboard is the SAME function the relational portal uses.
              match checkDashboard hitSchema caseHistoryDashboard with
              | Ok tree -> Expect.equal tree.Id "cases" "retrieval dashboard types clean → render it"
              | Error defects -> failtestf "expected clean, got %A" defects

          testCase "binding a string hit column into a numeric sink is the identical FUARAN066"
          <| fun _ ->
              // A Metric bound to `title` (string) — exactly the relational mismatch, same code.
              let mistyped =
                  Fuaran.metric
                      "m1"
                      { Defaults.metric with
                          Value = binding.query "title" (fun (x: float) -> x) }

              match checkDashboard hitSchema mistyped with
              | Error [ d ] ->
                  Expect.equal d.Code TypeMismatchCode "FUARAN066 — same defect as the relational plane"
                  Expect.equal d.Column "title" "the string citation column"
              | other -> failtestf "expected one FUARAN066, got %A" other

          testCase "end-to-end: retrieve → check → render-or-reject through the shared gate"
          <| fun _ ->
              let src = InMemoryRetrievalSource.ofHits hits

              match run (src.Retrieve(RetrievalRequest.create "prior cases" 10)) with
              | Ok table ->
                  match checkDashboard table.Schema caseHistoryDashboard with
                  | Ok _ -> ()
                  | Error defects -> failtestf "retrieval dashboard should type clean, got %A" defects
              | Error e -> failtestf "retrieve failed: %s" (RetrievalError.message e) ]

[<Tests>]
let tests =
    testList "Phase 325 — RAG-backed retrieval source" [ projectionTests; oneContractTests ]
