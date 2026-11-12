module Fuaran.UI.OpStream.Dag.Inspect.Tests.OverlayTests

open Expecto
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.TestSupport
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagOverlay — human-primacy + retention/tombstone overlay (task 3).
// ============================================================================

/// The graph over the corpus sink's live records AND its skeleton, so a
/// tombstoned node is drawn (pruned, links intact).
let private graphOf (c: Corpus) : DagGraph =
    let records = c.Sink.Records "s" |> Async.RunSynchronously
    let skeleton = c.Sink.Skeleton "s" |> Async.RunSynchronously

    match DagGraphModel.tryBuildWith "s" records skeleton with
    | Ok g -> g
    | Error cycle -> failtestf "corpus graph is cyclic: %s" (DagGraphCycle.describe cycle)

/// The host's precedence predicate for the overlay: a graph node with no
/// `PromptId` is `Primary` (this host's human-pin semantics). The overlay no
/// longer decodes `PromptId` itself (Phase 331) — the consumer supplies this.
let private isPrimary (n: DagGraphNode) : bool = Option.isNone n.PromptId

/// The overlay under `roots`, failing the test on a refused root.
let private overlayOf (graph: DagGraph) (roots: GcRoots) : DagOverlay =
    match DagOverlay.build isPrimary graph roots with
    | Ok o -> o
    | Error fault -> failtestf "overlay refused: %A" fault

/// GC roots holding only the merge head — branchC (off `a`, never merged) is
/// then reachable from no root.
let private headOnlyRoots (c: Corpus) : GcRoots =
    { Heads = [ c.Merge.Hash ]
      Checkpoints = []
      ResumeCoordinates = [] }

let private overlayNode (o: DagOverlay) (hash: string) : OverlayNode =
    match o.Nodes |> List.tryFind (fun n -> n.Node.Hash = hash) with
    | Some n -> n
    | None -> failtestf "overlay node %s absent" hash

[<Tests>]
let tests =
    testList
        "DagOverlay"
        [ test "per-node human-primacy follows PromptId provenance" {
              let c = build ()
              let o = overlayOf (graphOf c) (headOnlyRoots c)
              Expect.isTrue (overlayNode o c.A.Hash).IsPrimary "a carried no PromptId"
              Expect.isTrue (overlayNode o c.BranchB.Hash).IsPrimary "branchB is human"
              Expect.isFalse (overlayNode o c.BranchA.Hash).IsPrimary "branchA is AI"
              Expect.isFalse (overlayNode o c.BranchC.Hash).IsPrimary "branchC is AI"
          }

          test "nodes reachable from the head are Live; the unmerged branch is Prunable" {
              let c = build ()
              let o = overlayOf (graphOf c) (headOnlyRoots c)

              for h in [ c.A.Hash; c.BranchA.Hash; c.BranchB.Hash; c.Merge.Hash ] do
                  Expect.equal (overlayNode o h).Retention RetentionState.Live "reachable from the merge head"

              Expect.equal (overlayNode o c.BranchC.Hash).Retention RetentionState.Prunable "branchC reaches no root"
              Expect.isTrue (o.Prunable.Contains c.BranchC.Hash) "branchC in the prunable set"
              Expect.isFalse (o.Live.Contains c.BranchC.Hash) "branchC not in the live set"
          }

          test "a tombstoned record overrides retention to Tombstoned" {
              let c = build ()
              let pruned = c.Sink.Tombstone("s", c.BranchC.Hash) |> Async.RunSynchronously
              Expect.isTrue pruned "branchC is a childless leaf, so the sink prunes it into the skeleton"
              let o = overlayOf (graphOf c) (headOnlyRoots c)

              Expect.equal
                  (overlayNode o c.BranchC.Hash).Retention
                  RetentionState.Tombstoned
                  "payload pruned ⇒ Tombstoned"

              let drawn = (overlayNode o c.BranchC.Hash).Node
              Expect.equal drawn.Parents [ c.A.Hash ] "the skeleton keeps the pruned node's links"
              Expect.isNone drawn.Actor "the payload (and its actor) is gone"
              Expect.isNone drawn.Timestamp "the payload (and its timestamp) is gone"
          } ]
