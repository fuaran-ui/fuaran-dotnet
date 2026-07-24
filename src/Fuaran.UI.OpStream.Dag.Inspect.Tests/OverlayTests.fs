module Fuaran.UI.OpStream.Dag.Inspect.Tests.OverlayTests

open Expecto
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagOverlay — human-primacy + retention/tombstone overlay (task 3).
// ============================================================================

let private graphOf (c: Corpus) : DagGraph =
    DagGraphModel.build "s" (c.Sink.Records "s" |> Async.RunSynchronously)

/// The host's precedence predicate for the overlay: a graph node with no
/// `PromptId` is `Primary` (this host's human-pin semantics). The overlay no
/// longer decodes `PromptId` itself (Phase 331) — the consumer supplies this.
let private isPrimary (n: DagGraphNode) : bool = Option.isNone n.PromptId

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
              let o = DagOverlay.build isPrimary (graphOf c) (headOnlyRoots c)
              Expect.isTrue (overlayNode o c.A.Hash).IsPrimary "a carried no PromptId"
              Expect.isTrue (overlayNode o c.BranchB.Hash).IsPrimary "branchB is human"
              Expect.isFalse (overlayNode o c.BranchA.Hash).IsPrimary "branchA is AI"
              Expect.isFalse (overlayNode o c.BranchC.Hash).IsPrimary "branchC is AI"
          }

          test "nodes reachable from the head are Live; the unmerged branch is Prunable" {
              let c = build ()
              let o = DagOverlay.build isPrimary (graphOf c) (headOnlyRoots c)

              for h in [ c.A.Hash; c.BranchA.Hash; c.BranchB.Hash; c.Merge.Hash ] do
                  Expect.equal (overlayNode o h).Retention RetentionState.Live "reachable from the merge head"

              Expect.equal (overlayNode o c.BranchC.Hash).Retention RetentionState.Prunable "branchC reaches no root"
              Expect.isTrue (o.Prunable.Contains c.BranchC.Hash) "branchC in the prunable set"
              Expect.isFalse (o.Live.Contains c.BranchC.Hash) "branchC not in the live set"
          }

          test "a tombstoned record overrides retention to Tombstoned" {
              let c = build ()
              c.Sink.Tombstone("s", c.BranchC.Hash) |> Async.RunSynchronously |> ignore
              let o = DagOverlay.build isPrimary (graphOf c) (headOnlyRoots c)

              Expect.equal
                  (overlayNode o c.BranchC.Hash).Retention
                  RetentionState.Tombstoned
                  "payload pruned ⇒ Tombstoned"
          }

          test "human-pinned cells are the most-recent human writers on the spine" {
              let c = build ()
              let getRecord = getRecordOf c
              // branchA's primary spine is branchA (AI, right pane) → a (human,
              // left pane): the left cells are human-pinned, the right are not.
              let pinned =
                  DagOverlay.primaryPinnedCells recordAuthor getRecord None c.BranchA.Hash

              Expect.isTrue (pinned.Contains("left", "style.tone")) "left restyled by the human genesis"
              Expect.isFalse (pinned.Contains("right", "style.tone")) "right restyled by the AI branch"
          } ]
