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
//  Since Phase 2725 the manifest also carries `precedenceFixtures`: each case
//  of `precedenceCases` emitted under each named side, certifying the optional
//  precedence merge of WIRE_FORMAT §34.4 (`reconcileWith (Some side)`).
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

    let row =
        container
            "row"
            [ Fuaran.markdown "r0" "Zero"
              Fuaran.markdown "r1" "One"
              Fuaran.markdown "r2" "Two" ]

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

      "fold-two-restated-orders",
      Derives,
      "One branch lists a parent's children in the order it holds; the other appends a child to the parent and then lists the children in the order they then hold. Each reorder changes nothing where it applies, so each reads the parent's child-list and kind slots and writes neither (Phase 2696): the two are not reported against each other, the first is admitted beside the append as a rebase (Phase 2671), and the fold derives the append",
      nested,
      [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxa"; NodeId "boxb" ]) ],
      [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "new" "Appended")
        TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxa"; NodeId "boxb"; NodeId "new" ]) ]

      "fold-reorder-beside-restated-order",
      Derives,
      "One branch reorders a parent's children while the other lists them in the order the parent already holds — the pair the retired fold-refusal-two-reorders refused. The restated reorder changes nothing on its own branch, so it yields to the other branch's real reorder of the same parent and is dropped before the conflict report (Phase 2724); the fold derives the real order and the script records it for the parent, because a merge node's replay still applies the restated op",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ TreeOp.ReorderChildren(dashboardId, [ leftChildId; rightChildId ]) ]

      "fold-restated-order-beside-reorder",
      Derives,
      "The mirror of fold-reorder-beside-restated-order: the restated reorder is on the first branch and the real one on the second. The restated one yields wherever it sits (Phase 2724), and the fold derives the real order",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ leftChildId; rightChildId ]) ],
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ]

      "fold-append-restated-beside-reorder",
      Derives,
      "One branch appends a child to a parent and then lists the children in the order the parent then holds; the other reorders the parent's two children. The list is unchanged where its own branch applies it, so it yields to the real reorder (Phase 2724); the append and the real reorder are a rebase (Phase 2671), and the fold derives the real order with the append after it",
      nested,
      [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "new" "Appended")
        TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxa"; NodeId "boxb"; NodeId "new" ]) ],
      [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "boxb"; NodeId "boxa" ]) ]

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

      "fold-refusal-two-real-reorders",
      Refuses,
      "Both branches reorder one parent's three children, each to an order the parent does not hold and to different orders from each other — each writes the parent's child-list and kind slots, SlotClashes no rule decides, refused as a Conflict (Phase 2696: the case that stays refused)",
      row,
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r2"; NodeId "r1"; NodeId "r0" ]) ],
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r1"; NodeId "r0"; NodeId "r2" ]) ]

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

/// The ids the family has retired, each with the phase that retired it and the
/// fixtures that took its case: `(id, retired by, successors)`. A retired id is
/// never reused — a fixture whose verdict changes moves to a new id — so a
/// host's record of a verdict under an id stays true of that id. The manifest
/// carries the list as `retiredFoldFixtures` (Phase 2724, the convention's first
/// entry; WIRE_FORMAT §34.6).
let retired: (string * string * string list) list =
    [ "fold-refusal-two-reorders",
      "Phase 2724",
      [ "fold-reorder-beside-restated-order"; "fold-restated-order-beside-reorder" ] ]

/// The precedence fixtures (Phase 2725): the `precedenceFixtures` key of the
/// manifest, certifying the optional precedence merge of WIRE_FORMAT §34.4. Each
/// case is authored once and emitted under both named sides — `a` (the first
/// delta takes precedence) and `b` (the second) — as two fixtures,
/// `fold-precedence-<side>-<case>`, so the rule is pinned whichever side is named.
/// `(case, under a, under b, description, base, ops a, ops b)`.
let private precedenceCases
    : (string * FoldExpectation * FoldExpectation * string * Node<TestMsg> * TreeOp<TestMsg> list * TreeOp<TestMsg> list) list =
    let flat = buildDashboard ()
    let nested = nestedTree ()

    // Two panes and a container with one child, so every touch of a removed node §34.4
    // names (an edit, a restyle, an insert beneath, a move) has a node to land on.
    let panes =
        container
            "dash"
            [ Fuaran.markdown "left" "Left pane"
              Fuaran.markdown "right" "Right pane"
              container "box" [ Fuaran.markdown "inner" "Inner" ] ]

    let row =
        container
            "row"
            [ Fuaran.markdown "r0" "Zero"
              Fuaran.markdown "r1" "One"
              Fuaran.markdown "r2" "Two" ]

    [ "concurrent-tone",
      Derives,
      Derives,
      "Both branches set a different tone on one pane: two writes of one slot, which apply in either order, so the named side's tone is re-asserted at the end of the script and the merge takes it",
      flat,
      [ toneOf ToneVariant.Brand leftChildId ],
      [ toneOf ToneVariant.Critical leftChildId ]

      "two-real-reorders",
      Derives,
      Derives,
      "Both branches reorder one parent's three children to different orders: a Conflict the default context refuses; each reorder applies after the other, so the named side's order is re-asserted and the merge takes it",
      row,
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r2"; NodeId "r1"; NodeId "r0" ]) ],
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r1"; NodeId "r0"; NodeId "r2" ]) ]

      "restated-order",
      Derives,
      Derives,
      "One branch reorders a parent's children while the other lists them in the order the parent already holds. The yield rule runs before precedence is consulted (Phase 2724), so the restatement is dropped from its delta whichever side is named, and the merge takes the real order, also when the named side is the one that restated it",
      flat,
      [ TreeOp.ReorderChildren(dashboardId, [ rightChildId; leftChildId ]) ],
      [ TreeOp.ReorderChildren(dashboardId, [ leftChildId; rightChildId ]) ]

      "delete-edit",
      Refuses,
      Refuses,
      "One branch removes a pane the other edits: a DeleteModify pair whose removal takes the other op's node (the base holds it and the removing branch's head does not), so it refuses before the fold with the entry the default context gives it, whichever side is named (Phase 2695)",
      panes,
      [ TreeOp.RemoveNode leftChildId ],
      [ TreeOp.EditNode(leftChildId, (Fuaran.markdown "left" "Edited").Kind) ]

      "delete-restyle",
      Refuses,
      Refuses,
      "One branch removes a pane the other restyles: refused as DeleteModify under either side, as an edit is (Phase 2695)",
      panes,
      [ TreeOp.RemoveNode leftChildId ],
      [ toneOf ToneVariant.Success leftChildId ]

      "delete-insert-beneath",
      Refuses,
      Refuses,
      "One branch removes a container the other inserts a child beneath: the insert addresses the removed node, so the pair refuses as DeleteModify under either side (Phase 2695)",
      panes,
      [ TreeOp.RemoveNode(NodeId "box") ],
      [ TreeOp.InsertChild(NodeId "box", Fuaran.markdown "new" "New") ]

      "delete-move",
      Refuses,
      Refuses,
      "One branch removes a pane the other moves into a container: refused as DeleteModify under either side (Phase 2695)",
      panes,
      [ TreeOp.RemoveNode leftChildId ],
      [ TreeOp.MoveNode(leftChildId, NodeId "box") ]

      "sibling-insert-beside-removal",
      Derives,
      Derives,
      "One branch removes a pane while the other appends a new child to the same parent: a DeleteModify pair the default context refuses, whose removal is of ANOTHER node than the one the insert addresses, so both ops apply in either order and the named side settles it; the merge holds the insert and not the removed pane (Phase 2695's admitted case)",
      panes,
      [ TreeOp.RemoveNode leftChildId ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "New") ]

      "same-id-insert",
      Refuses,
      Refuses,
      "Both branches insert a node under one id with different content. The pair applies in neither order (the second insert meets the id the first placed), so no named side can settle it: it refuses before the fold, naming the pair the default context refuses, as NotInBothOrders, whichever side is named (Phase 2725, classed by Phase 2743; before 2725 the merge faulted)",
      flat,
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "A wrote this") ],
      [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "new" "B wrote this") ]

      "mutual-move",
      Refuses,
      Refuses,
      "One branch moves a container into a second container while the other moves the second into the first. Whichever applies second would put a node inside its own descendant, so the pair applies in neither order and no named side can settle it: it refuses before the fold, naming the pair the default context refuses, as NotInBothOrders, whichever side is named (Phase 2725, classed by Phase 2743; before 2725 the merge faulted)",
      nested,
      [ TreeOp.MoveNode(NodeId "boxa", NodeId "boxb") ],
      [ TreeOp.MoveNode(NodeId "boxb", NodeId "boxa") ]

      "superseded-move",
      Derives,
      Refuses,
      "One branch appends a child to a container while the other moves that container's child out and then removes it: the append and the move both change the container's child list, a Conflict. Under a the append is re-asserted, already held, and the merge derives. Under b the move is re-asserted at the end of the script, and its own branch has since removed the node it moves, so it no longer applies: the pair refuses, naming the pair the default context refuses, as StaleReassertion (Phase 2725, classed by Phase 2743; before 2725 the merge faulted)",
      nested,
      [ TreeOp.InsertChild(NodeId "boxa", Fuaran.markdown "n" "New") ],
      [ TreeOp.MoveNode(NodeId "m", NodeId "boxb"); TreeOp.RemoveNode(NodeId "m") ]

      "stale-reorder",
      Refuses,
      Refuses,
      "Both branches reorder one parent's three children to different orders, and the second branch then removes one of them. Under a the first branch's reorder is re-asserted at the end of the script, and the other branch has changed the child list it names; under b the second branch's reorder is re-asserted, and its own branch has since removed a child it names. Neither applies where it is re-asserted, so under either side the pair refuses, naming the pair the default context refuses, as StaleReassertion (Phase 2725, classed by Phase 2743; before 2725 the merge faulted)",
      row,
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r2"; NodeId "r1"; NodeId "r0" ]) ],
      [ TreeOp.ReorderChildren(NodeId "row", [ NodeId "r1"; NodeId "r0"; NodeId "r2" ])
        TreeOp.RemoveNode(NodeId "r0") ] ]

/// The side a fixture names, as the manifest spells it.
let sideText (side: ReconcileSide) =
    match side with
    | ReconcileSide.A -> "a"
    | ReconcileSide.B -> "b"

/// `(id, side, expectation, description, base, ops a, ops b)`, every case under
/// side `a` then side `b`.
let precedenceFixtures
    : (string * ReconcileSide * FoldExpectation * string * Node<TestMsg> * TreeOp<TestMsg> list * TreeOp<TestMsg> list) list =
    [ for side in [ ReconcileSide.A; ReconcileSide.B ] do
          for case, underA, underB, desc, baseT, opsA, opsB in precedenceCases do
              let expectation = if side = ReconcileSide.A then underA else underB
              sprintf "fold-precedence-%s-%s" (sideText side) case, side, expectation, desc, baseT, opsA, opsB ]

/// The policy's verdict for a fixture.
let verdictOf (expectation: FoldExpectation) baseT opsA opsB =
    match expectation with
    | Gated -> DagReconcile.reconcileGated gatedValidator baseT opsA opsB
    | Derives
    | Refuses -> DagReconcile.reconcile baseT opsA opsB

/// The canonical ops array a delta file holds.
let encodeOps (ops: TreeOp<TestMsg> list) : string =
    "[" + (ops |> List.map CanonicalJson.encodeOp |> String.concat ",") + "]"

/// The artefact a verdict pins: `(file suffix, bytes, manifest file key, manifest hash key)`.
let private artefactOfVerdict (id: string) expectation verdict : string * string * string * string =
    match expectation, verdict with
    | Derives, Ok r -> ".expected.json", CanonicalJson.encodeNode r.Tree, "expectedFile", "outcomeHash"
    | Refuses, Error(ReconcileFault.Refused refusals) ->
        ".envelope.json", DagReconcile.encodeEnvelope refusals, "envelopeFile", "envelopeHash"
    | Gated, Error(ReconcileFault.IntroducedDefects defects) ->
        ".verdict.json", ValidatorGate.encodeVerdict defects, "verdictFile", "verdictHash"
    | e, v -> failwithf "fold fixture %s: expected %A, the policy answered %A" id e v

/// The artefact a fixture pins: `(file suffix, bytes, manifest file key, manifest hash key)`.
let artefactOf (id: string) expectation baseT opsA opsB : string * string * string * string =
    artefactOfVerdict id expectation (verdictOf expectation baseT opsA opsB)

/// The artefact a precedence fixture pins, under its named side.
let precedenceArtefactOf (id: string) side expectation baseT opsA opsB : string * string * string * string =
    artefactOfVerdict id expectation (DagReconcile.reconcileWith (Some side) baseT opsA opsB)

let private kindOf expectation =
    match expectation with
    | Derives -> "merge-fold-derive"
    | Refuses -> "merge-fold-refusal"
    | Gated -> "merge-fold-gated"

let private description =
    "merge-fold (Phase 2293; the merge-conformance corpus's only family since Phase 2645, manifest version 2): decode base and the two branches' op deltas (aOpsFile, bOpsFile: canonical TreeOp arrays, each applying from base op by op), reconcile them by fold under the policy of WIRE_FORMAT.md section 34 (Core's conflict report over the base-aware footprint, in which a reorder to the order its parent already holds reads the child list and writes nothing (Phase 2696); before the report, a top-level reorder that leaves its parent's order as it was on its own branch dropped from its delta when the other delta holds a top-level reorder of the same parent that changes the order on its branch, the script recording that parent's surviving order (Phase 2724); agreements applied as no change; two appends of distinct nodes under one parent admitted and ordered by the declared child-order rule; a reorder of a parent beside an append to it or a removal or move of a child it lists admitted as a rebase (Phase 2671); every other reported pair refused, a removal against a concurrent touch of the removed node as DeleteModify). merge-fold-derive: the derived tree is byte-equal to expectedFile and sha256(expected) == outcomeHash. merge-fold-refusal: the policy refuses and the envelope is byte-equal to envelopeFile with sha256 == envelopeHash; swapping a and b swaps every entry's left and right and re-sorts. merge-fold-gated: the fold derives and the sample validator over the derived tree introduces defects neither branch head carries; the verdict is byte-equal to verdictFile with sha256 == verdictHash. retiredFoldFixtures (Phase 2724): the ids the family has retired, each with the phase that retired it (retiredBy) and the fixtures that took its case (successors); a retired id is never reused, and no foldFixtures entry carries one."

let private precedenceDescription =
    "merge-fold under precedence (Phase 2725; WIRE_FORMAT.md section 34.4, the optional precedence merge): each entry is a foldFixtures entry with a precedence member naming the delta that takes precedence (a: aOpsFile, b: bOpsFile), reconciled under that side. A named side admits the pairs the policy would refuse and re-asserts its own ops of them, in its delta's order, at the end of the script, with three exceptions that refuse, each naming a pair the default context refuses: a DeleteModify pair whose removal takes the other op's node (a node the other op addresses that the base holds and the removing branch's head does not; Phase 2695), checked before the fold, with the default entry (class DeleteModify); a pair whose two ops do not both apply in either order (two inserts of one id, or one op that in its own branch's tree leaves the other unable to apply), checked before the fold, as class NotInBothOrders; and a pair whose named-side op does not apply, as written, where it is re-asserted after the fold and the child-order script, in either fold order, as class StaleReassertion (the two classes Phase 2743; never given in the default context). A precedence merge's refusal envelope therefore names only pairs the default context's envelope names, each entry the default's or the default's pair under one of the two precedence-only classes, and it never answers with a fold fault. merge-fold-derive and merge-fold-refusal are read as in foldDescription; swapping a and b and naming the other side gives the same tree, or the mirrored envelope. A host that offers no precedence merge does not read this array and certifies nothing from it."

/// Write the family's payloads into `dir`, remove every other file there (the
/// retired triad families), and write the manifest.
let emitInto (dir: string) : unit =
    let written = System.Collections.Generic.HashSet<string>()

    let write (name: string) (bytes: string) =
        File.WriteAllText(Path.Combine(dir, name), bytes)
        written.Add name |> ignore

    // One manifest entry and its four payloads; `precedence` names the side of a
    // precedence fixture and is absent from a foldFixtures entry.
    let entryOf
        id
        (precedence: ReconcileSide option)
        expectation
        desc
        baseT
        opsA
        opsB
        (artefact: string * string * string * string)
        =
        let suffix, bytes, fileKey, hashKey = artefact
        write (id + ".base.json") (CanonicalJson.encodeNode baseT)
        write (id + ".a.ops.json") (encodeOps opsA)
        write (id + ".b.ops.json") (encodeOps opsB)
        write (id + suffix) bytes

        let side =
            precedence
            |> Option.map (fun s -> sprintf "      \"precedence\": \"%s\",\n" (sideText s))
            |> Option.defaultValue ""

        sprintf
            "    {\n      \"id\": \"%s\",\n      \"kind\": \"%s\",\n%s      \"baseFile\": \"%s.base.json\",\n      \"aOpsFile\": \"%s.a.ops.json\",\n      \"bOpsFile\": \"%s.b.ops.json\",\n      \"%s\": \"%s%s\",\n      \"%s\": \"%s\",\n      \"description\": \"%s\"\n    }"
            id
            (kindOf expectation)
            side
            id
            id
            id
            fileKey
            id
            suffix
            hashKey
            (Fuaran.Core.Hash.sha256Hex bytes)
            desc

    let entries =
        [ for (id, expectation, desc, baseT, opsA, opsB) in fixtures do
              entryOf id None expectation desc baseT opsA opsB (artefactOf id expectation baseT opsA opsB) ]

    let precedenceEntries =
        [ for (id, side, expectation, desc, baseT, opsA, opsB) in precedenceFixtures do
              entryOf
                  id
                  (Some side)
                  expectation
                  desc
                  baseT
                  opsA
                  opsB
                  (precedenceArtefactOf id side expectation baseT opsA opsB) ]

    let retiredEntries =
        [ for (id, by, successors) in retired do
              if
                  fixtures |> List.exists (fun (live, _, _, _, _, _) -> live = id)
                  || precedenceFixtures |> List.exists (fun (live, _, _, _, _, _, _) -> live = id)
              then
                  failwithf "fold fixture %s: a retired id is never reused" id

              sprintf
                  "    {\n      \"id\": \"%s\",\n      \"retiredBy\": \"%s\",\n      \"successors\": [%s]\n    }"
                  id
                  by
                  (successors |> List.map (sprintf "\"%s\"") |> String.concat ", ") ]

    write
        "manifest.json"
        ("{\n  \"version\": 2,\n  \"description\": \"Fuaran merge-conformance corpus. Since Phase 2645 (the cut) the UI op-stream DAG is Fuaran.Core's DAG and its only merge is the fold (WIRE_FORMAT.md section 34), so this corpus is the foldFixtures family alone; the tree merge's triad families (fixtures, refusalFixtures, totalityFixtures) were retired with it. Since Phase 2725 the family's precedenceFixtures certify the optional precedence merge (section 34.4).\",\n  \"foldDescription\": \""
         + description
         + "\",\n  \"foldFixtures\": [\n"
         + System.String.Join(",\n", entries)
         + "\n  ],\n  \"retiredFoldFixtures\": [\n"
         + System.String.Join(",\n", retiredEntries)
         + "\n  ],\n  \"precedenceDescription\": \""
         + precedenceDescription
         + "\",\n  \"precedenceFixtures\": [\n"
         + System.String.Join(",\n", precedenceEntries)
         + "\n  ]\n}\n")

    for path in Directory.GetFiles dir do
        if not (written.Contains(Path.GetFileName path |> Option.ofObj |> Option.defaultValue "")) then
            File.Delete path

/// Emit the merge-conformance corpus into `root`/merge-conformance.
let emit (root: string) : unit =
    let dir = Path.Combine(root, "merge-conformance")
    Directory.CreateDirectory dir |> ignore
    emitInto dir
