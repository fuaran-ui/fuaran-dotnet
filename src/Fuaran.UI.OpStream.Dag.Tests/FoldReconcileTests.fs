module Fuaran.UI.OpStream.Dag.Tests.FoldReconcileTests

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The reconciliation policy over Core's conflict report (Phase 2293).
//
//  Leg A of the `foldFixtures` family — the F# policy reproduces every
//  committed verdict byte for byte — plus the properties the corpus alone
//  cannot pin: the verdict is symmetric under swapping the branches, the
//  safety net refuses a fold whose two orders differ, and a delta that does
//  not apply is refused before anything is folded.
// ============================================================================

let private corpusDir =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root -> Path.Combine(root, "merge-conformance"))
    |> Option.defaultValue (
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures", "merge-conformance")
    )

let private read (name: string) =
    File.ReadAllText(Path.Combine(corpusDir, name)).TrimEnd('\n', '\r')

/// The refusals with each conflict's sides swapped — what swapping the branches must produce.
let private mirrored (refusals: Refusal<TestMsg> list) : Refusal<TestMsg> list =
    refusals
    |> List.map (fun r ->
        { r with
            Conflict =
                { r.Conflict with
                    Left = r.Conflict.Right
                    Right = r.Conflict.Left } })

let private byId id =
    FoldCorpus.fixtures |> List.find (fun (i, _, _, _, _, _) -> i = id)

/// The children of `parent` in `tree`, by id.
let private childIds (tree: Node<TestMsg>) (parent: string) : string list =
    let rec find (n: Node<TestMsg>) =
        if n.Id = parent then
            Some n
        else
            Fuaran.UI.Ops.Introspect.getChildren n.Kind
            |> Option.defaultValue []
            |> List.tryPick find

    find tree
    |> Option.bind (fun n -> Fuaran.UI.Ops.Introspect.getChildren n.Kind)
    |> Option.defaultValue []
    |> List.map _.Id

[<Tests>]
let tests =
    testList
        "Dag.FoldReconcile"
        [ test "committed fold corpus matches the F# policy byte for byte" {
              if not (Directory.Exists corpusDir) then
                  skiptest "wire-format-fixtures/merge-conformance corpus absent (single-repo checkout)"
              else
                  for (id, expectation, _, baseT, opsA, opsB) in FoldCorpus.fixtures do
                      let suffix, bytes, _, _ = FoldCorpus.artefactOf id expectation baseT opsA opsB
                      Expect.equal (read (id + suffix)) bytes (sprintf "%s: F# verdict == committed artefact" id)
                      Expect.equal (read (id + ".base.json")) (canonical baseT) (sprintf "%s: base payload" id)
                      Expect.equal (read (id + ".a.ops.json")) (FoldCorpus.encodeOps opsA) (sprintf "%s: a ops" id)
                      Expect.equal (read (id + ".b.ops.json")) (FoldCorpus.encodeOps opsB) (sprintf "%s: b ops" id)
          }

          test "every fixture's verdict is symmetric under swapping the branches" {
              for (id, expectation, _, baseT, opsA, opsB) in FoldCorpus.fixtures do
                  match
                      FoldCorpus.verdictOf expectation baseT opsA opsB, FoldCorpus.verdictOf expectation baseT opsB opsA
                  with
                  | Ok ab, Ok ba ->
                      Expect.equal (canonical ba.Tree) (canonical ab.Tree) (sprintf "%s: one derived tree" id)
                  | Error(ReconcileFault.Refused ab), Error(ReconcileFault.Refused ba) ->
                      Expect.equal
                          (DagReconcile.encodeEnvelope ba)
                          (DagReconcile.encodeEnvelope (mirrored ab))
                          (sprintf "%s: the swapped envelope is the mirrored one" id)
                  | Error(ReconcileFault.IntroducedDefects ab), Error(ReconcileFault.IntroducedDefects ba) ->
                      Expect.equal ba ab (sprintf "%s: one verdict" id)
                  | x, y -> failtestf "%s: the two namings disagree: %A / %A" id x y
          }

          test "the remove-versus-move pair refuses as DeleteModify" {
              let _, _, _, baseT, opsA, opsB =
                  FoldCorpus.fixtures
                  |> List.find (fun (id, _, _, _, _, _) -> id = "fold-refusal-remove-vs-move")

              match DagReconcile.reconcile baseT opsA opsB with
              | Error(ReconcileFault.Refused refusals) ->
                  Expect.isTrue
                      (refusals |> List.exists (fun r -> r.Class = RefusalClass.DeleteModify))
                      "a DeleteModify entry"
              | other -> failtestf "expected a refusal, got %A" other
          }

          test "an interleave records the reorder the rule asks for, and an agreement records nothing" {
              let byId id =
                  FoldCorpus.fixtures |> List.find (fun (i, _, _, _, _, _) -> i = id)

              let _, _, _, b1, a1, c1 = byId "fold-insert-interleave"

              match DagReconcile.reconcile b1 a1 c1 with
              | Ok r ->
                  Expect.equal r.Interleaves 1 "one interleaved parent"
                  Expect.equal r.Script.Length 1 "the fold's A-first order (zzz, aaa) is reordered"
              | Error e -> failtestf "expected a derivation, got %A" e

              let _, _, _, b2, a2, c2 = byId "fold-agreement-remove"

              match DagReconcile.reconcile b2 a2 c2 with
              | Ok r ->
                  Expect.equal r.Agreements 1 "one agreement"
                  Expect.isEmpty r.Script "an agreement needs no script"
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a rebased reorder keeps its declared order and the append follows it" {
              let _, _, _, b, a, c = byId "fold-reorder-and-insert"

              match DagReconcile.reconcile b a c with
              | Ok r ->
                  Expect.equal (childIds r.Tree "dash") [ "right"; "left"; "new" ] "reordered run, then the append"
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a node both branches move into one parent beside other appends is listed once" {
              let _, _, _, b, a, c = byId "fold-double-move-and-appends"

              match DagReconcile.reconcile b a c with
              | Ok r ->
                  Expect.equal
                      (childIds r.Tree "dash")
                      [ "boxa"; "boxb"; "aa"; "m"; "zz" ]
                      "the agreed node once, where the merged runs first reach it"

                  for op in r.Script do
                      match op with
                      | TreeOp.ReorderChildren(_, order) ->
                          Expect.equal (List.distinct order) order "the recorded reorder lists every child once"
                      | other -> failtestf "a script holds reorders only, got %A" other
              | Error e -> failtestf "expected a derivation, got %A" e
          }

          test "a delta that does not apply is refused before anything is folded" {
              let baseT = buildDashboard ()

              match DagReconcile.reconcile baseT [] [ TreeOp.RemoveNode(NodeId "absent") ] with
              | Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.B, 0, _)) -> ()
              | other -> failtestf "expected DeltaDoesNotApply on B at 0, got %A" other
          }

          test "applyHeld applies an op whose effect is already held as no change, and nothing else" {
              let baseT = buildDashboard ()
              let removed = baseT |> applyOk (TreeOp.RemoveNode rightChildId)

              Expect.equal
                  (DagReconcile.applyHeld (TreeOp.RemoveNode rightChildId) removed
                   |> Result.map canonical)
                  (Ok(canonical removed))
                  "a second removal of a removed node is no change"

              let inserted =
                  baseT |> applyOk (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A"))

              Expect.equal
                  (DagReconcile.applyHeld (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A")) inserted
                   |> Result.map canonical)
                  (Ok(canonical inserted))
                  "a second insert of the identical node is no change"

              Expect.isError
                  (DagReconcile.applyHeld (TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B")) inserted)
                  "an insert under a held id with DIFFERENT content is still the engine's DuplicateNodeId"
          } ]
