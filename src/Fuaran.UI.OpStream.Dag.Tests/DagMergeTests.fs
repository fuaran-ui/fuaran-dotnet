module Fuaran.UI.OpStream.Dag.Tests.DagMergeTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The one merge, over Core's DAG (Phase 2645, the cut).
//
//  `DagMerge.mergeDag` derives a merge with the reconciliation policy and mints
//  the merge node through Core: a node with the two heads as parents whose op is
//  the script. These tests hold the properties the cut rests on — the node is
//  addressed by Core's `Dag.nodeId`, Core's fold of the new head reaches the
//  derived tree, the address is parent-order independent, a refusal is the
//  policy's, precedence settles what it can as script, a checkpoint replays the
//  same answer and a tampered one is refused, and a criss-cross folds.
// ============================================================================

let private streamId = "merge-stream"
let private now = timestamp 1700000500L

let private tone (t: ToneVariant) (id: NodeId) : TreeOp<TestMsg> =
    TreeOp.PatchStyle(id, { StyleDelta.empty with Tone = Some t })

/// A sink holding a genesis node (an empty Batch: the base IS the dashboard)
/// and two branches off it.
let private fork (opsA: TreeOp<TestMsg> list) (opsB: TreeOp<TestMsg> list) =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let genesis = stepRecord streamId None (TreeOp.Batch []) 1700000000L
    add sink genesis

    let branch (ops: TreeOp<TestMsg> list) (t0: int64) =
        ops
        |> List.indexed
        |> List.fold
            (fun (parent: DagOpRecord<TestMsg>) (i, op) ->
                let r = stepRecord streamId (Some parent) op (t0 + int64 i)
                add sink r
                r)
            genesis

    let a = branch opsA 1700000100L
    let b = branch opsB 1700000200L
    sink, genesis, a.Hash, b.Hash

let private run context (sink: IDagOpStreamSink<TestMsg>) a b =
    DagMerge.mergeWith context sink streamId (buildDashboard ()) a b now
    |> Async.RunSynchronously

let private op (o: TreeOp<TestMsg>) : string = CanonicalJson.encodeOp o

let private kind (r: MergeResult<TestMsg>) : string =
    match r with
    | MergeResult.AlreadyMerged h -> "already:" + h
    | MergeResult.FastForward h -> "ff:" + h
    | other -> sprintf "%A" other

let private merged (outcome: GatedMergeOutcome<TestMsg>) =
    match outcome.Result with
    | MergeResult.Merged(record, tree) -> record, tree
    | other -> failtestf "expected Merged, got %A" other

[<Tests>]
let tests =
    testList
        "Dag.Merge (Phase 2645)"
        [ test "equal heads are AlreadyMerged; an ancestor head fast-forwards" {
              let sink, genesis, a, _ = fork [ tone ToneVariant.Brand leftChildId ] []

              Expect.equal (kind (run MergeContext.defaults sink a a).Result) ("already:" + a) "equal heads"

              Expect.equal
                  (kind (run MergeContext.defaults sink genesis.Hash a).Result)
                  ("ff:" + a)
                  "ancestor -> descendant"

              Expect.equal
                  (kind (run MergeContext.defaults sink a genesis.Hash).Result)
                  ("ff:" + a)
                  "descendant <- ancestor"
          }

          test "a disjoint merge is a Core node over the script, and Core's fold reaches the derived tree" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success rightChildId ]

              let record, tree = merged (run MergeContext.defaults sink a b)
              Expect.equal record.Parents [ a; b ] "parents in author order"
              Expect.equal (op record.Op) (op (TreeOp.Batch [])) "no script: independent ops need none"
              Expect.equal record.Hash (DagOpRecord.recomputeHash record) "addressed by Dag.nodeId"
              Expect.isTrue (DagMerge.commitMerge sink streamId None record |> Async.RunSynchronously) "commits"

              Expect.equal
                  (canonical (replayHead sink streamId (buildDashboard ()) record.Hash))
                  (canonical tree)
                  "replays"

              let expected =
                  buildDashboard ()
                  |> applyOk (tone ToneVariant.Brand leftChildId)
                  |> applyOk (tone ToneVariant.Success rightChildId)

              Expect.equal (canonical tree) (canonical expected) "both edits"
          }

          test "merge(A, B) and merge(B, A) mint ONE node" {
              let sink, _, a, b =
                  fork
                      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "zzz" "Z") ]
                      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "aaa" "A") ]

              let ab, treeAb = merged (run MergeContext.defaults sink a b)
              let ba, treeBa = merged (run MergeContext.defaults sink b a)
              Expect.equal ab.Hash ba.Hash "one address: Core sorts the parents"
              Expect.equal (canonical treeAb) (canonical treeBa) "one tree"

              Expect.notEqual (op ab.Op) (op (TreeOp.Batch [])) "the child-order rule recorded a reorder"
          }

          test "an interleave replays through Core's fold in its own drain order" {
              let sink, _, a, b =
                  fork
                      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "b1" "B1")
                        TreeOp.InsertChild(dashboardId, Fuaran.markdown "b2" "B2") ]
                      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "a1" "A1") ]

              let record, tree = merged (run MergeContext.defaults sink a b)
              add sink record

              Expect.equal
                  (canonical (replayHead sink streamId (buildDashboard ()) record.Hash))
                  (canonical tree)
                  "replays"
          }

          test "a pair the policy does not admit refuses with its envelope" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success leftChildId ]

              match (run MergeContext.defaults sink a b).Result with
              | MergeResult.Refused(ReconcileFault.Refused [ r ]) ->
                  Expect.equal r.Class RefusalClass.Conflict "one slot written twice"
              | other -> failtestf "expected one refusal, got %A" other
          }

          test "precedence re-asserts the named side's refused op as script" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success leftChildId ]

              for side, expected in [ ReconcileSide.A, ToneVariant.Brand; ReconcileSide.B, ToneVariant.Success ] do
                  let record, tree =
                      merged (
                          run
                              { MergeContext.defaults with
                                  Precedence = Some side }
                              sink
                              a
                              b
                      )

                  let winner = buildDashboard () |> applyOk (tone expected leftChildId)

                  Expect.equal (canonical tree) (canonical winner) (sprintf "%A wins" side)

                  Expect.equal
                      (op record.Op)
                      (op (TreeOp.Batch [ tone expected leftChildId ]))
                      "the re-asserted op is the script"
          }

          test "precedence cannot resurrect: an edit beside a removal still refuses" {
              let sink, _, a, b =
                  fork [ TreeOp.RemoveNode leftChildId ] [ tone ToneVariant.Success leftChildId ]

              match
                  (run
                      { MergeContext.defaults with
                          Precedence = Some ReconcileSide.B }
                      sink
                      a
                      b)
                      .Result
              with
              | MergeResult.Refused(ReconcileFault.FoldFaulted _) -> ()
              | other -> failtestf "expected FoldFaulted, got %A" other
          }

          test "the validator gate refuses a derived tree with an introduced defect" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Brand rightChildId ]

              let outcome =
                  run
                      { MergeContext.defaults with
                          Policy = MergePolicy.gated FoldCorpus.gatedValidator }
                      sink
                      a
                      b

              match outcome.Result with
              | MergeResult.Refused(ReconcileFault.IntroducedDefects defects) ->
                  Expect.equal defects outcome.Diagnostics "the diagnostics are the refusal's"
                  Expect.equal defects.Length 2 "both Brand panes"
              | other -> failtestf "expected IntroducedDefects, got %A" other

              let diagnostic =
                  run
                      { MergeContext.defaults with
                          Policy = MergePolicy.diagnostic FoldCorpus.gatedValidator }
                      sink
                      a
                      b

              merged diagnostic |> ignore
              Expect.equal diagnostic.Diagnostics.Length 2 "a diagnostic policy reports and proceeds"
          }

          test "a checkpoint replays the same merge, and a tampered one is refused" {
              let sink, genesis, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success rightChildId ]

              let records = sink.Records streamId |> Async.RunSynchronously
              let dag = DagOpRecord.toDag records

              let cp =
                  match DagFold.checkpointAt (buildDashboard ()) dag genesis.Hash with
                  | Ok cp -> cp
                  | Error e -> failtestf "checkpoint: %A" e

              let plainRecord, _ = merged (run MergeContext.defaults sink a b)

              let viaCheckpoint, _ =
                  merged (
                      run
                          { MergeContext.defaults with
                              Checkpoint = Some cp }
                          sink
                          a
                          b
                  )

              Expect.equal viaCheckpoint.Hash plainRecord.Hash "same node either way"

              let tampered =
                  { cp with
                      State = buildDashboard () |> applyOk (tone ToneVariant.Critical leftChildId) }

              match
                  (run
                      { MergeContext.defaults with
                          Checkpoint = Some tampered }
                      sink
                      a
                      b)
                      .Result
              with
              | MergeResult.ReplayFailed(MergeReplayFault.Checkpoint _) -> ()
              | other -> failtestf "expected a refused checkpoint, got %A" other
          }

          test "a criss-cross of two merges of one pair folds" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success rightChildId ]

              let m1, _ = merged (run MergeContext.defaults sink a b)

              let m2, _ =
                  merged (
                      run
                          { MergeContext.defaults with
                              Attribution = MergeAttribution.ofActor (Actor.Human "other") }
                          sink
                          b
                          a
                  )

              Expect.notEqual m1.Hash m2.Hash "two merge nodes (different authors)"
              add sink m1
              add sink m2
              let final, tree = merged (run MergeContext.defaults sink m1.Hash m2.Hash)
              add sink final

              Expect.equal
                  (canonical (replayHead sink streamId (buildDashboard ()) final.Hash))
                  (canonical tree)
                  "replays"
          }

          test "mergeIntoTrunk advances the trunk to the minted node" {
              let sink, _, a, b =
                  fork [ tone ToneVariant.Brand leftChildId ] [ tone ToneVariant.Success rightChildId ]

              Expect.isTrue (sink.TryAdvanceHead(streamId, None, a) |> Async.RunSynchronously) "trunk at a"

              match
                  DagMerge.mergeIntoTrunk sink streamId (buildDashboard ()) b now 3
                  |> Async.RunSynchronously
              with
              | Some head ->
                  Expect.equal (sink.Head streamId |> Async.RunSynchronously) (Some head) "trunk moved"

                  let record = sink.TryGet(streamId, head) |> Async.RunSynchronously
                  Expect.equal (record |> Option.map _.Parents) (Some [ a; b ]) "the merge node"
              | None -> failtest "trunk merge did not advance"
          } ]
