module Fuaran.UI.OpStream.Dag.Tests.FoldCorpus

open System.IO
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Merge-by-fold corpus (Phase 2293): the `foldFixtures` key of the
//  merge-conformance manifest — since Phase 2645 (the cut) the whole of it.
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
//  The triad families that sat beside it (`fixtures`, `refusalFixtures`,
//  `totalityFixtures`: the tree merge's 3-way verdicts) were retired at the cut,
//  which made the fold the DAG's only merge; the manifest moved to version 2 and
//  carries this family alone, and `emit` removes every file it did not write.
//
//  Regenerate (from fuaran-dotnet/):
//      dotnet run --project src/Fuaran.UI.OpStream.Dag.Tests -- --emit-merge-corpus ..\wire-format-fixtures
// ============================================================================

let private patch (id: NodeId) (f: StyleDelta -> StyleDelta) : TreeOp<TestMsg> =
    TreeOp.PatchStyle(id, f StyleDelta.empty)

let private toneOf (t: ToneVariant) (id: NodeId) =
    patch id (fun d -> { d with Tone = Some t })

let private nestedTree () : Node<TestMsg> =
    container "dash" [ container "boxa" [ Fuaran.markdown "m" "Movable" ]; container "boxb" [] ]

/// A dashboard holding a `Mount` whose slot inputs carry nodes no ordered child
/// list reaches (Phase 2701): `hdr` sits AT the `header` input, `s1` and `s2`
/// sit inside the container at the `body` input. A footprint that does not walk
/// slot arguments cannot see an op on any of them.
let private mountTree () : Node<TestMsg> =
    let guest: MountSpec<TestMsg> =
        { Capabilities = []
          Channel = Fuaran.guestOutChannel
          Inputs =
            Some(
                Map.ofList
                    [ "header", FragmentArg.SlotArg(Fuaran.markdown "hdr" "Header")
                      "body",
                      FragmentArg.SlotArg(
                          container "slotbody" [ Fuaran.markdown "s1" "First"; Fuaran.markdown "s2" "Second" ]
                      )
                      "title", FragmentArg.Str "Guest" ]
            )
          OnBubble = None
          ScopeId = "guest-scope" }

    container "dash" [ Fuaran.mount "guest" guest; container "boxb" [] ]

/// The sample DOMAIN validator the gated fixtures certify against: "at most one
/// `Brand`-toned pane per dashboard". Each offending child is a defect on its
/// `style.tone` cell. A host MUST port this exact invariant to reproduce the
/// verdict.
let gatedValidator: MergeValidator<TestMsg> =
    fun tree ->
        match tree.Kind with
        | NodeKind.Box(spec) ->
            let brandKids =
                spec.Children
                |> List.filter (fun c -> (c.Style |> Option.defaultValue Defaults.style).Tone = ToneVariant.Brand)

            if List.length brandKids > 1 then
                brandKids
                |> List.map (fun c ->
                    { Code = "TESTBRAND001"
                      NodeId = c.Id
                      Facet = "style.tone"
                      Message =
                        sprintf
                            "Pane '%s' shares Brand tone with a sibling — at most one Brand pane per dashboard."
                            c.Id })
            else
                []
        | _ -> []

/// What a fixture asserts.
type FoldExpectation =
    /// The fold derives a tree.
    | Derives
    /// The policy refuses, with an envelope.
    | Refuses
    /// The fold derives, and the validator gate refuses (`gatedValidator`).
    | Gated

/// `(id, expectation, description, base, ops a, ops b)`.
let fixtures: (string * FoldExpectation * string * Node<TestMsg> * TreeOp<TestMsg> list * TreeOp<TestMsg> list) list =
    let flat = buildDashboard ()
    let nested = nestedTree ()
    let mounted = mountTree ()

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
      "One branch moves a node out of a container while the other retones the container itself. The move writes only the container's child list, which the base-aware footprint declares as the container's kind slot (Phase 2651) and Core's child-list slot (Phase 2691), and the retone writes its style: different slots, no conflict, so the fold composes them",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ],
      [ toneOf ToneVariant.Brand (NodeId "boxa") ]

      "fold-insert-and-container-restyle",
      Derives,
      "One branch appends a child to a container while the other retones the container itself. Under Core D246 (the 0.42.0 pin, Phase 2691) the insert writes the container's child-list slot and the retone its style: different slots, no conflict, so the fold composes them. Before the pin a structure write collided with every slot write of its node and the pair was refused",
      nested,
      [ TreeOp.InsertChild(NodeId "boxa", Fuaran.markdown "new" "Appended") ],
      [ toneOf ToneVariant.Brand (NodeId "boxa") ]

      "fold-move-and-target-edit",
      Derives,
      "One branch moves a node while the other retones it in place. The move writes the moved node's position, which the base-aware footprint declares as its parent slot (Phase 2670), and the retone writes its style: two slots, no conflict, so the fold composes them",
      nested,
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ],
      [ toneOf ToneVariant.Brand (NodeId "m") ]

      "fold-reorder-and-child-edit",
      Derives,
      "One branch reorders a parent's children while the other retones one of them. The reorder writes the parent's child list (its kind slot and Core's child-list slot) and reads each listed child only at its parent slot (Phase 2670), so the retone commutes with it and the fold composes them",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ toneOf ToneVariant.Brand leftChildId ]

      "fold-reorder-and-insert",
      Derives,
      "One branch reorders a parent's children while the other appends a child to it. Core reports the pair (the reorder writes the parent's child-list slot, which the insert writes too: a SlotClash, Core D246), and the policy admits it as a rebase (Phase 2671): the reorder is applied to the children the parent holds when the fold reaches it, so the reordered run keeps its declared order and the append follows it. The tree merge refuses this pair (ReorderVsStructural); the fold's answer is accepted by the 2026-11-10 ruling",
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

      "fold-mount-input-insert",
      Derives,
      "One branch appends a child to a container held only in a Mount's slot input while the other retones a pane outside the Mount. The structural ops locate their parent through every position the tree holds a node in, slot arguments included (Phase 2701), so the insert is a structure write of that container and the retone a style write elsewhere: independent, and the fold composes them",
      mounted,
      [ TreeOp.InsertChild(NodeId "slotbody", Fuaran.markdown "s3" "Third") ],
      [ toneOf ToneVariant.Brand (NodeId "boxb") ]

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

      "fold-refusal-mount-input-tone",
      Refuses,
      "Both branches set a different tone on the node held at a Mount's slot input, which no ordered child list reaches. A footprint's node walk includes slot arguments (Phase 2701), so the pair collides on that node and is refused as a Conflict; a host that does not walk them sees no collision and derives",
      mounted,
      [ toneOf ToneVariant.Brand (NodeId "hdr") ],
      [ toneOf ToneVariant.Critical (NodeId "hdr") ]

      "fold-refusal-mount-input-disjoint",
      Refuses,
      "Each branch retones a DIFFERENT node held only in one Mount's slot inputs (the node at its header input, and a node inside the container at its body input). A node no ordered child list reaches takes the footprint's pure arm, an in-place write under a parent the op cannot name, so the pair is reported MoveVsRemove on both nodes and refused as a Conflict: the policy's answer since Phase 2327, pinned by Phase 2701",
      mounted,
      [ toneOf ToneVariant.Brand (NodeId "hdr") ],
      [ toneOf ToneVariant.Success (NodeId "s1") ]

      "fold-refusal-mount-input-delete-modify",
      Refuses,
      "One branch removes a node inside a Mount's slot input while the other retones it — a DeleteModify refusal, as for a node any ordered child list reaches (Phase 2701)",
      mounted,
      [ TreeOp.RemoveNode(NodeId "s2") ],
      [ toneOf ToneVariant.Success (NodeId "s2") ]

      "fold-refusal-remove-vs-move",
      Refuses,
      "One branch removes a node the other moves to another parent — a DeleteModify refusal, as against an edit. The tree-level merge let the move win; the fold refuses it",
      nested,
      [ TreeOp.RemoveNode(NodeId "m") ],
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb") ]

      "fold-refusal-two-reorders",
      Refuses,
      "Both branches reorder one parent's children to different orders — each writes the parent's child-list and kind slots, SlotClashes no rule decides, refused as a Conflict",
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
    | Gated -> DagReconcile.reconcileGated gatedValidator baseT opsA opsB
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
    "merge-fold (Phase 2293; the merge-conformance corpus's only family since Phase 2645, manifest version 2): decode base and the two branches' op deltas (aOpsFile, bOpsFile: canonical TreeOp arrays, each applying from base op by op), reconcile them by fold under the policy of WIRE_FORMAT.md section 34 (Core's conflict report over the base-aware footprint; agreements applied as no change; two appends of distinct nodes under one parent admitted and ordered by the declared child-order rule; a reorder of a parent beside an append to it or a removal or move of a child it lists admitted as a rebase (Phase 2671); every other reported pair refused, a removal against a concurrent touch of the removed node as DeleteModify). merge-fold-derive: the derived tree is byte-equal to expectedFile and sha256(expected) == outcomeHash. merge-fold-refusal: the policy refuses and the envelope is byte-equal to envelopeFile with sha256 == envelopeHash; swapping a and b swaps every entry's left and right and re-sorts. merge-fold-gated: the fold derives and the sample validator over the derived tree introduces defects neither branch head carries; the verdict is byte-equal to verdictFile with sha256 == verdictHash."

/// Write the family's payloads into `dir`, remove every other file there (the
/// retired triad families), and write the manifest.
let emitInto (dir: string) : unit =
    let written = System.Collections.Generic.HashSet<string>()

    let write (name: string) (bytes: string) =
        File.WriteAllText(Path.Combine(dir, name), bytes)
        written.Add name |> ignore

    let entries =
        [ for (id, expectation, desc, baseT, opsA, opsB) in fixtures do
              let suffix, bytes, fileKey, hashKey = artefactOf id expectation baseT opsA opsB
              write (id + ".base.json") (CanonicalJson.encodeNode baseT)
              write (id + ".a.ops.json") (encodeOps opsA)
              write (id + ".b.ops.json") (encodeOps opsB)
              write (id + suffix) bytes

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

    write
        "manifest.json"
        ("{\n  \"version\": 2,\n  \"description\": \"Fuaran merge-conformance corpus. Since Phase 2645 (the cut) the UI op-stream DAG is Fuaran.Core's DAG and its only merge is the fold (WIRE_FORMAT.md section 34), so this corpus is the foldFixtures family alone; the tree merge's triad families (fixtures, refusalFixtures, totalityFixtures) were retired with it.\",\n  \"foldDescription\": \""
         + description
         + "\",\n  \"foldFixtures\": [\n"
         + System.String.Join(",\n", entries)
         + "\n  ]\n}\n")

    for path in Directory.GetFiles dir do
        if not (written.Contains(Path.GetFileName path |> Option.ofObj |> Option.defaultValue "")) then
            File.Delete path

/// Emit the merge-conformance corpus into `root`/merge-conformance.
let emit (root: string) : unit =
    let dir = Path.Combine(root, "merge-conformance")
    Directory.CreateDirectory dir |> ignore
    emitInto dir
