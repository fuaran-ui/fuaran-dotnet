module Fuaran.UI.OpStream.Dag.Inspect.Tests.AuditionTests

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagAudition — audition a coordinate to its snapshot + preview (task 2).
// ============================================================================

let private canon (n: Node<TestMsg>) : string = CanonicalJson.encodeNode n

[<Tests>]
let tests =
    testList
        "DagAudition"
        [ test "auditioning the genesis coordinate reconstructs its snapshot" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagAudition.audition getRecord c.Initial None c.A.Hash with
              | Ok r -> Expect.equal r.Coordinate c.A.Hash "coordinate echoed"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "a branch and the merge audition to different snapshots" {
              let c = build ()
              let getRecord = getRecordOf c

              let snap (h: string) =
                  match DagAudition.snapshotAt getRecord c.Initial h with
                  | Ok t -> t
                  | Error e -> failtestf "replay failed for %s: %A" h e

              // branchA restyles only the right pane; the merge also folds in
              // branchB's left-pane restyle — so the two snapshots differ.
              Expect.notEqual (canon (snap c.BranchA.Hash)) (canon (snap c.A.Hash)) "branchA changed the right pane"

              Expect.notEqual
                  (canon (snap c.Merge.Hash))
                  (canon (snap c.BranchA.Hash))
                  "merge folds branchB's left edit"
          }

          test "the preview hook fires on a successful audition" {
              let c = build ()
              let getRecord = getRecordOf c
              let previewHook = Some canon

              match DagAudition.audition getRecord c.Initial previewHook c.Merge.Hash with
              | Ok r ->
                  Expect.isSome r.Preview "preview produced"
                  Expect.equal r.Preview (Some(canon r.Snapshot)) "preview is the hook over the snapshot"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "no preview hook ⇒ snapshot-only audition" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagAudition.audition getRecord c.Initial None c.Merge.Hash with
              | Ok r -> Expect.isNone r.Preview "no hook ⇒ no preview"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "auditioning an unknown coordinate errors UnknownHash" {
              let c = build ()
              let getRecord = getRecordOf c

              match DagAudition.audition getRecord c.Initial (Some canon) "deadbeef" with
              | Error(DagReplayError.UnknownHash h) -> Expect.equal h "deadbeef" "the missing hash"
              | other -> failtestf "expected UnknownHash, got %A" other
          }

          test "auditionFromSink matches the explicit-lookup audition" {
              let c = build ()

              let fromSink =
                  DagAudition.auditionFromSink c.Sink "s" c.Initial (Some canon) c.Merge.Hash
                  |> Async.RunSynchronously

              match fromSink with
              | Ok r -> Expect.equal r.Coordinate c.Merge.Hash "auditioned the merge head"
              | Error e -> failtestf "expected Ok, got %A" e
          } ]
