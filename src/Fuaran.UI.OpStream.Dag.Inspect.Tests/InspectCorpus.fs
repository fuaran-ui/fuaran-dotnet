module Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge

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

type TestMsg =
    | NoOp
    | Selected of int

let ts (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

let leftChildId = NodeId "left"
let rightChildId = NodeId "right"

let buildDashboard () : Node<TestMsg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<TestMsg> with
            Children = [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "right" "Right pane" ] }

let private restyle (id: NodeId) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

let private add (sink: IDagOpStreamSink<TestMsg>) (r: DagOpRecord<TestMsg>) : unit =
    sink.Add r |> Async.RunSynchronously

/// The corpus records, by their role in the forest.
type Corpus =
    { Sink: IDagOpStreamSink<TestMsg>
      Initial: Node<TestMsg>
      A: DagOpRecord<TestMsg>
      BranchA: DagOpRecord<TestMsg>
      BranchB: DagOpRecord<TestMsg>
      BranchC: DagOpRecord<TestMsg>
      Merge: DagOpRecord<TestMsg> }

/// Host-side precedence classifier: no `PromptId` ⇒ `Primary` (the human pin in
/// this host's chosen semantics), else `Secondary` carrying the prompt id as an
/// opaque tag. The merge layer no longer bakes this in (Phase 331) — the
/// consumer supplies it.
let recordAuthor (r: DagOpRecord<TestMsg>) : MergeAuthor =
    if Option.isNone r.PromptId then
        MergeAuthor.Primary
    else
        MergeAuthor.Secondary r.PromptId

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
        DagOpRecord.create "s" [] (restyle leftChildId ToneVariant.Brand) None "human" (ts 1L) OpResultEnvelope.Success

    add sink a

    // branchA — AI, restyles the RIGHT pane.
    let branchA =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle rightChildId ToneVariant.Success)
            (Some "prompt-ai")
            "ai"
            (ts 2L)
            OpResultEnvelope.Success

    add sink branchA

    // branchB — HUMAN, restyles the LEFT pane (disjoint from branchA's right).
    let branchB =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle leftChildId ToneVariant.Critical)
            None
            "human"
            (ts 3L)
            OpResultEnvelope.Success

    add sink branchB

    // branchC — AI, a dangling unmerged leaf off `a` (the prune candidate).
    let branchC =
        DagOpRecord.create
            "s"
            [ a.Hash ]
            (restyle rightChildId ToneVariant.Brand)
            (Some "prompt-ai-2")
            "ai"
            (ts 4L)
            OpResultEnvelope.Success

    add sink branchC

    // merge branchA ⋈ branchB (primary = branchA).
    let mergeRecord =
        match
            DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash (ts 5L)
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
