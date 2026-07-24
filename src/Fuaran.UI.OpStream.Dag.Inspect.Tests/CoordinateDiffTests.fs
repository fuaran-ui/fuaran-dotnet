module Fuaran.UI.OpStream.Dag.Inspect.Tests.CoordinateDiffTests

open Expecto
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagCoordinateDiff — diff any two DAG coordinates (task 4 / N-way diff).
// ============================================================================

[<Tests>]
let tests =
    testList
        "DagCoordinateDiff"
        [ test "two sibling branches diff to a non-empty structural delta" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagCoordinateDiff.diff getRecord c.Initial c.BranchA.Hash c.BranchB.Hash with
              | Ok cd ->
                  Expect.equal cd.From c.BranchA.Hash "from branchA"
                  Expect.equal cd.To c.BranchB.Hash "to branchB"
                  Expect.isNonEmpty cd.Diff.Changes "the two variations differ"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "diffing a coordinate against itself is empty" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagCoordinateDiff.diff getRecord c.Initial c.Merge.Hash c.Merge.Hash with
              | Ok cd -> Expect.isEmpty cd.Diff.Changes "no delta vs itself"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "diffMany fans a baseline out against every coordinate, in order" {
              let c = build ()
              let getRecord = getRecordOf c
              let others = [ c.BranchA.Hash; c.BranchB.Hash; c.Merge.Hash ]

              match DagCoordinateDiff.diffMany getRecord c.Initial c.A.Hash others with
              | Ok diffs ->
                  Expect.equal (List.length diffs) 3 "one diff per other coordinate"
                  Expect.equal (diffs |> List.map _.To) others "order preserved"
                  Expect.isTrue (diffs |> List.forall (fun d -> d.From = c.A.Hash)) "all share the baseline"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "an unknown endpoint surfaces the replay error" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagCoordinateDiff.diff getRecord c.Initial c.A.Hash "nope" with
              | Error(DagReplayError.UnknownHash h) -> Expect.equal h "nope" "the missing endpoint"
              | other -> failtestf "expected UnknownHash, got %A" other
          }

          test "diffFromSink matches the explicit-lookup diff" {
              let c = build ()
              let getRecord = getRecordOf c

              let expected =
                  match DagCoordinateDiff.diff getRecord c.Initial c.BranchA.Hash c.Merge.Hash with
                  | Ok cd -> cd.Diff.Changes
                  | Error e -> failtestf "baseline diff failed: %A" e

              let fromSink =
                  DagCoordinateDiff.diffFromSink c.Sink "s" c.Initial c.BranchA.Hash c.Merge.Hash
                  |> Async.RunSynchronously

              match fromSink with
              | Ok cd -> Expect.equal cd.Diff.Changes expected "same structural delta"
              | Error e -> failtestf "expected Ok, got %A" e
          } ]
