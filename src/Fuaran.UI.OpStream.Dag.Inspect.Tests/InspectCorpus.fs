module Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Shared branch/merge DAG corpus for the inspector suite.
//
//  A small "variation forest" over a two-pane dashboard:
//
//        a  (genesis, HUMAN, restyle LEFT brand)          ← branch point
//       /|\
//      / | \
//  brA  brB  brC
//   |    |     (AI, restyle RIGHT brand — a dangling, UNMERGED leaf)
//   |    |
//  (AI) (HUMAN)
//  right left
//   \   /
//   merge  (two parents brA primary, brB secondary)       ← live head
//
//  brA restyles the RIGHT pane, brB restyles the LEFT pane → disjoint cells,
//  so brA ⋈ brB auto-merges with zero conflicts. brC is a third child off `a`
//  that is never merged and not a GC root — the prune candidate.
// ============================================================================

let private restyle (id: NodeId) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

/// The corpus records, by their role in the forest.
type Corpus =
    { Sink: IDagOpStreamSink<TestMsg>
      Initial: Node<TestMsg>
      A: DagOpRecord<TestMsg>
      BranchA: DagOpRecord<TestMsg>
      BranchB: DagOpRecord<TestMsg>
      BranchC: DagOpRecord<TestMsg>
      Merge: DagOpRecord<TestMsg> }

/// `getRecord` over the corpus sink (synchronous; tests are single-threaded).
let getRecordOf (c: Corpus) : string -> DagOpRecord<TestMsg> option =
    let records = c.Sink.Records "s" |> Async.RunSynchronously
    let byHash = records |> List.map (fun r -> r.Hash, r) |> Map.ofList
    fun h -> Map.tryFind h byHash

/// Build the branch/merge DAG described in the module header.
let build () : Corpus =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let initial = buildDashboard ()

    // a — genesis, HUMAN (no PromptId), restyles the LEFT pane.
    let a =
        DagOpRecord.create
            "s"
            []
            (restyle leftChildId ToneVariant.Brand)
            None
            (Actor.Human "human")
            (timestamp 1L)
            OpResultEnvelope.Success

    add sink a

    // branchA — AI, restyles the RIGHT pane.
    let branchA =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle rightChildId ToneVariant.Success)
            (Some "prompt-ai")
            (Actor.Agent("claude", "4.8", "planner"))
            (timestamp 2L)
            OpResultEnvelope.Success

    add sink branchA

    // branchB — HUMAN, restyles the LEFT pane (disjoint from branchA's right).
    let branchB =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle leftChildId ToneVariant.Critical)
            None
            (Actor.Human "human")
            (timestamp 3L)
            OpResultEnvelope.Success

    add sink branchB

    // branchC — AI, a dangling unmerged leaf off `a` (the prune candidate).
    let branchC =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle rightChildId ToneVariant.Brand)
            (Some "prompt-ai-2")
            (Actor.Agent("claude", "4.8", "planner"))
            (timestamp 4L)
            OpResultEnvelope.Success

    add sink branchC

    // merge branchA ⋈ branchB (primary = branchA).
    let mergeRecord =
        match
            DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash (timestamp 5L)
            |> Async.RunSynchronously
        with
        | MergeResult.Merged(record, _) -> record
        | other -> failwithf "corpus: expected a clean auto-merge, got %A" other

    add sink mergeRecord

    { Sink = sink
      Initial = initial
      A = a
      BranchA = branchA
      BranchB = branchB
      BranchC = branchC
      Merge = mergeRecord }
