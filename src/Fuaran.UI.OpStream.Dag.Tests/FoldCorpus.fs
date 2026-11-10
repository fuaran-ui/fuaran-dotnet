module Fuaran.UI.OpStream.Dag.Tests.FoldCorpus

open System.IO
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Merge-by-fold corpus (Phase 2293, additive: the `foldFixtures` key of the
//  merge-conformance manifest).
//
//  Each fixture is a base tree and the two branches' OP DELTAS — the ops as
//  recorded on each branch, never a diff of trees — with the verdict
//  `DagReconcile` reaches: the derived tree, the refusal envelope, or the
//  validator gate's introduced-defect verdict. The deltas are authored ops
//  (style edits as `PatchStyle`, the field they change, per Phase 2291), so the
//  footprint Core reads is the one a writer records.
//
//  The fixtures whose description starts PINNED fix a child order the policy
//  gives and the tree merge does not (operator ruling 2026-11-10); the ones
//  naming the tree merge's refusal pin a class the fold derives where the tree
//  merge refuses, accepted by the same ruling (Phase 2671).
//
//  Held under its own manifest key, the Phase 1497 precedent: every host reads
//  the keys it ports and ignores the rest, so adding the family moves no
//  host's existing legs. The triad families beside it are retired by the
//  phase that makes the fold the DAG's only merge.
//
//  Regenerate (from fuaran-dotnet/), with the rest of the merge corpus:
//      dotnet run --project src/Fuaran.UI.OpStream.Dag.Tests -- --emit-merge-corpus ..\wire-format-fixtures
// ============================================================================

let private patch (id: NodeId) (f: StyleDelta -> StyleDelta) : TreeOp<TestMsg> =
    TreeOp.PatchStyle(id, f StyleDelta.empty)

let private toneOf (t: ToneVariant) (id: NodeId) =
    patch id (fun d -> { d with Tone = Some t })

let private nestedTree () : Node<TestMsg> =
    container "dash" [ container "boxa" [ Fuaran.markdown "m" "Movable" ]; container "boxb" [] ]

/// What a fixture asserts.
type FoldExpectation =
    /// The fold derives a tree.
    | Derives
    /// The policy refuses, with an envelope.
    | Refuses
    /// The fold derives, and the validator gate refuses (`MergeCorpus.gatedValidator`).
    | Gated

/// `(id, expectation, description, base, ops a, ops b)`.
let fixtures: (string * FoldExpectation * string * Node<TestMsg> * TreeOp<TestMsg> list * TreeOp<TestMsg> list) list =
    let flat = buildDashboard ()
    let nested = nestedTree ()

    [ "fold-disjoint",
      Derives,
      "Retones of two different panes — independent under Core's footprint, so the fold composes them",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ toneOf ToneVariant.Success rightChildId ]

      "fold-style-fields",
      Derives,
      "Two style edits of one pane that change DIFFERENT fields (tone, voice): two slots, no conflict",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ patch leftChildId (fun d ->
            { d with
                Voice = Some FontVoice.Display }) ]

      "fold-insert-interleave",
      Derives,
      "Both branches append a different node to one parent — an admitted InsertPositionClash, ordered by the declared child-order rule (NodeId ordinal)",
      flat,
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "zzz" "Z") ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "aaa" "A") ]

      "fold-insert-interleave-runs",
      Derives,
      "Each branch appends a run of two nodes under one parent — the runs keep their own order and merge by comparing heads ordinally",
      flat,
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "b1" "B1")
        TreeOp.InsertChild(dashboardId, Fuaran.markdown "a2" "A2") ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "a1" "A1")
        TreeOp.InsertChild(dashboardId, Fuaran.markdown "c1" "C1") ]

      "fold-agreement-remove",
      Derives,
      "Both branches remove the same pane — an agreement: the fold already holds the effect when it reaches the second removal",
      flat,
      [ TreeOp.RemoveNode rightChildId ],
      [ TreeOp.RemoveNode rightChildId ]

      "fold-agreement-tone",
      Derives,
      "Both branches set the same tone on the same pane — byte-identical ops, an agreement",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ toneOf ToneVariant.Brand leftChildId ]

      "fold-move-and-source-edit",
      Derives,
      "One branch moves a node out of a container while the other retones the container itself. The move writes only the container's child list, which the base-aware footprint declares as the container's kind slot (Phase 2651), and the retone writes its style: two slots, no conflict, so the fold composes them",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ],
      [ toneOf ToneVariant.Brand (NodeId "boxa") ]

      "fold-move-and-target-edit",
      Derives,
      "One branch moves a node while the other retones it in place. The move writes the moved node's position, which the base-aware footprint declares as its parent slot (Phase 2670), and the retone writes its style: two slots, no conflict, so the fold composes them",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ],
      [ toneOf ToneVariant.Brand (NodeId "m") ]

      "fold-reorder-and-child-edit",
      Derives,
      "One branch reorders a parent's children while the other retones one of them. The reorder writes the parent's child list (its kind slot) and reads each listed child only at its parent slot (Phase 2670), so the retone commutes with it and the fold composes them",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ toneOf ToneVariant.Brand leftChildId ]

      "fold-reorder-and-insert",
      Derives,
      "One branch reorders a parent's children while the other appends a child to it. Core reports the pair (the reorder writes the parent's kind slot, the insert touches the parent whole), and the policy admits it as a rebase (Phase 2671): the reorder is applied to the children the parent holds when the fold reaches it, so the reordered run keeps its declared order and the append follows it. The tree merge refuses this pair (ReorderVsStructural); the fold's answer is accepted by the 2026-11-10 ruling",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "Appended") ]

      "fold-reorder-and-remove",
      Derives,
      "One branch reorders a parent's children while the other removes one of them. Admitted as a rebase: the removed child is dropped from the reordered run, and the rest keep the declared order",
      nested,
      [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxb"; NodeId "boxa" ]) ],
      [ TreeOp.RemoveNode(NodeId "boxa") ]

      "fold-reorder-and-move-in",
      Derives,
      "One branch reorders a parent's children while the other moves a node into that parent. Admitted as a rebase: the moved node is an append, placed after the reordered run",
      nested,
      [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxb"; NodeId "boxa" ]) ],
      [ TreeOp.MoveNode(NodeId "m", NodeId "dash") ]

      "fold-reorder-and-move-out",
      Derives,
      "One branch reorders a parent's children while the other moves one of them to another parent. Admitted as a rebase: the moved child leaves the reordered run, as a removed one does",
      nested,
      [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxb"; NodeId "boxa" ]) ],
      [ TreeOp.MoveNode(NodeId "boxb", NodeId "boxa") ]

      "fold-double-move-and-appends",
      Derives,
      "Both branches move the same node into one parent, each beside an append of its own there. The two moves agree; the child-order rule lists the agreed node ONCE, where the merge of the two runs first reaches it, so the recorded reorder applies (before Phase 2671 it named the node twice and the fold faulted). The tree merge refuses this (ReorderVsStructural: the two child lists share a new id); the fold's answer is accepted by the 2026-11-10 ruling",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "dash")
        TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "zz" "Z") ],
      [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "aa" "A")
        TreeOp.MoveNode(NodeId "m", NodeId "dash") ]

      "fold-agreed-move-and-edit",
      Derives,
      "Both branches move the same node to the same parent and one also retones it. The moves agree and the retone commutes with them. The tree merge refuses this (ConcurrentEdit on the moved node, which it reads as inserted by both sides with different content); the fold's answer is accepted by the 2026-11-10 ruling",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb")
        toneOf ToneVariant.Brand (NodeId "m") ],
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ]

      "fold-own-parent-move-and-insert",
      Derives,
      "One branch moves a node to the parent it already sits under while the other appends a child there. Both are appends, interleaved by the child-order rule. The tree merge refuses this (ReorderVsStructural: the mover's list is not a pure addition); the fold's answer is accepted by the 2026-11-10 ruling",
      flat,
      [ TreeOp.MoveNode(leftChildId, dashboardId) ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "Appended") ]

      "fold-pinned-own-parent-move",
      Derives,
      "PINNED (operator ruling 2026-11-10): a MoveNode to the parent the node already sits under is an append, so the node goes last. One branch moves the last pane to its own parent (its own head is unchanged) while the other reorders the parent; the policy rebases the reorder and appends the moved node after it, the tree applying the move after the reorder. The tree merge sees no change on the mover's side and keeps the reorder's order, which no application of the two ops produces",
      flat,
      [ TreeOp.MoveNode(rightChildId, dashboardId) ],
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ]

      "fold-pinned-append-order",
      Derives,
      "PINNED (operator ruling 2026-11-10): a branch's appends keep the order the branch made them in, and the two runs merge by their heads (b1, a2 then c1). Where both branches append to one parent the tree merge sorts every appended id by NodeId (a2, b1, c1), which is not an order anyone authored",
      flat,
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "b1" "B1")
        TreeOp.InsertChild(dashboardId, Fuaran.markdown "a2" "A2") ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "c1" "C1") ]

      "fold-refusal-concurrent-tone",
      Refuses,
      "Both branches set a different tone on the same pane — a SlotClash on style.tone, refused as a Conflict",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ toneOf ToneVariant.Critical leftChildId ]

      "fold-refusal-concurrent-direction",
      Refuses,
      "Both branches set a different text direction on the same pane — the SlotClash names style.direction",
      flat,
      [ patch leftChildId (fun d ->
            { d with
                Direction = Some TextDirection.Ltr }) ],
      [ patch leftChildId (fun d ->
            { d with
                Direction = Some TextDirection.Rtl }) ]

      "fold-refusal-same-id-insert",
      Refuses,
      "Both branches insert a node under one id with different content — not an agreement and not an interleave",
      flat,
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A wrote this") ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B wrote this") ]

      "fold-refusal-delete-modify",
      Refuses,
      "One branch retones a pane the other removes — a DeleteModify refusal",
      flat,
      [ toneOf ToneVariant.Success rightChildId ],
      [ TreeOp.RemoveNode rightChildId ]

      "fold-refusal-remove-vs-move",
      Refuses,
      "One branch removes a node the other moves to another parent — a DeleteModify refusal, as against an edit. The tree-level merge let the move win; the fold refuses it",
      nested,
      [ TreeOp.RemoveNode(NodeId "m") ],
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ]

      "fold-refusal-two-reorders",
      Refuses,
      "Both branches reorder one parent's children to different orders — each writes the parent's kind slot, a SlotClash no rule decides, refused as a Conflict",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ TreeOp.ReorderChildren(dashboardId, [ leftChildId; rightChildId ]) ]

      "fold-refusal-tooltip",
      Refuses,
      "Both branches set a different tooltip on the same pane",
      flat,
      [ TreeOp.UpdateTooltip(leftChildId, Some(TextSource.Literal "A hint")) ],
      [ TreeOp.UpdateTooltip(leftChildId, Some(TextSource.Literal "B hint")) ]

      "fold-validator-gated-brand",
      Gated,
      "Brand tone on two sibling panes, one per branch — the fold derives, and the derived tree carries a duplicate-Brand defect neither branch had",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ toneOf ToneVariant.Brand rightChildId ] ]

/// The policy's verdict for a fixture.
let verdictOf (expectation: FoldExpectation) baseT opsA opsB =
    match expectation with
    | Gated -> DagReconcile.reconcileGated MergeCorpus.gatedValidator baseT opsA opsB
    | Derives
    | Refuses -> DagReconcile.reconcile baseT opsA opsB

/// The canonical ops array a delta file holds.
let encodeOps (ops: TreeOp<TestMsg> list) : string =
    "[" + (ops |> List.map CanonicalJson.encodeOp |> String.concat ",") + "]"

/// The artefact a fixture pins: `(file suffix, bytes, manifest file key, manifest hash key)`.
let artefactOf (id: string) expectation baseT opsA opsB : string * string * string * string =
    match expectation, verdictOf expectation baseT opsA opsB with
    | Derives, Ok r -> ".expected.json", CanonicalJson.encodeNode r.Tree, "expectedFile", "outcomeHash"
    | Refuses, Error(ReconcileFault.Refused refusals) ->
        ".envelope.json", DagReconcile.encodeEnvelope refusals, "envelopeFile", "envelopeHash"
    | Gated, Error(ReconcileFault.IntroducedDefects defects) ->
        ".verdict.json", ValidatorGate.encodeVerdict defects, "verdictFile", "verdictHash"
    | e, v -> failwithf "fold fixture %s: expected %A, the policy answered %A" id e v

let private kindOf expectation =
    match expectation with
    | Derives -> "merge-fold-derive"
    | Refuses -> "merge-fold-refusal"
    | Gated -> "merge-fold-gated"

let private description =
    "merge-fold (Phase 2293, additive, SEPARATE key): decode base and the two branches' op deltas (aOpsFile, bOpsFile: canonical TreeOp arrays, each applying from base op by op), reconcile them by fold under the policy of WIRE_FORMAT.md section 34 (Core's conflict report over the base-aware footprint; agreements applied as no change; two appends of distinct nodes under one parent admitted and ordered by the declared child-order rule; a reorder of a parent beside an append to it or a removal or move of a child it lists admitted as a rebase (Phase 2671); every other reported pair refused, a removal against a concurrent touch of the removed node as DeleteModify). merge-fold-derive: the derived tree is byte-equal to expectedFile and sha256(expected) == outcomeHash. merge-fold-refusal: the policy refuses and the envelope is byte-equal to envelopeFile with sha256 == envelopeHash; swapping a and b swaps every entry's left and right and re-sorts. merge-fold-gated: the fold derives and the sample validator over the derived tree introduces defects neither branch head carries; the verdict is byte-equal to verdictFile with sha256 == verdictHash."

/// Write the family's payloads into `dir` and return its manifest section (the
/// `foldDescription` and `foldFixtures` members, comma-led).
let emitInto (dir: string) : string =
    let entries =
        [ for (id, expectation, desc, baseT, opsA, opsB) in fixtures do
              let suffix, bytes, fileKey, hashKey = artefactOf id expectation baseT opsA opsB
              File.WriteAllText(Path.Combine(dir, id + ".base.json"), CanonicalJson.encodeNode baseT)
              File.WriteAllText(Path.Combine(dir, id + ".a.ops.json"), encodeOps opsA)
              File.WriteAllText(Path.Combine(dir, id + ".b.ops.json"), encodeOps opsB)
              File.WriteAllText(Path.Combine(dir, id + suffix), bytes)

              sprintf
                  "    {\n      \"id\": \"%s\",\n      \"kind\": \"%s\",\n      \"baseFile\": \"%s.base.json\",\n      \"aOpsFile\": \"%s.a.ops.json\",\n      \"bOpsFile\": \"%s.b.ops.json\",\n      \"%s\": \"%s%s\",\n      \"%s\": \"%s\",\n      \"description\": \"%s\"\n    }"
                  id
                  (kindOf expectation)
                  id
                  id
                  id
                  fileKey
                  id
                  suffix
                  hashKey
                  (Fuaran.Core.Hash.sha256Hex bytes)
                  desc ]

    ",\n  \"foldDescription\": \""
    + description
    + "\",\n  \"foldFixtures\": [\n"
    + System.String.Join(",\n", entries)
    + "\n  ]"

/// Emit the whole merge-conformance corpus: the triad families and this one, in one manifest.
let emit (root: string) : unit = MergeCorpus.emitWith emitInto root
