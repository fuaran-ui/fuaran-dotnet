namespace Fuaran.UI.OpStream.Dag.Merge

open System
open System.Text
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DagReconcile — the UI's reconciliation policy over Core's conflict report
//  (Phase 2293, first slice).
//
//  A merge of two branches is a DERIVATION, not an assertion: both branches'
//  op deltas are folded from their common base, and the merge records only
//  what the fold cannot decide on its own — a script. Core decides which op
//  pairs interfere (`Dag.conflictsIn` over the tier's base-aware footprint,
//  `TreeOpFootprint.ofOpIn`) and decides nothing else (its GP6). This module is
//  the domain policy Core's design hands back, and it is deliberately thin.
//  Each pair Core reports is one of three things:
//
//   1. An AGREEMENT. The two ops encode to the same canonical bytes, so the
//      fold already holds the effect when it reaches the second one. The fold
//      applies it as no change (`applyHeld`: a removal of a node that is
//      already gone, an insert of a node already present, identical bytes,
//      under the same parent). Admitted.
//   2. An INTERLEAVE. Both ops append distinct nodes to the same parent (an
//      `InsertChild` or a `MoveNode` into it): Core reports the parent's child
//      list as an `InsertPositionClash`, because the two appends land in a
//      different order depending on which applies first. Admitted, and ordered
//      by ONE declared rule (below), which the merge records as a
//      `ReorderChildren` script.
//   3. A REBASE (Phase 2671). One op reorders a parent `p`'s children
//      (`ReorderChildren`) and the other changes that child list: an append to
//      `p` (an `InsertChild` under it or a `MoveNode` to it), or a removal or
//      move of a child the reorder lists. Core reports the pair, because the
//      reorder's list is stale in one fold order. Admitted: the fold applies
//      the reorder REBASED to the children `p` holds when it is reached — the
//      listed children still there, in the declared order, then the rest in
//      the order they hold — and the child-order rule below places the appends.
//   4. Anything else. Refused, naming Core's shape and address. A removal
//      against any concurrent touch of the removed node — an edit, a move, an
//      insert beneath it — is classed `DeleteModify`; every other pair is
//      `Conflict`. Two reorders of one parent that differ stay refused.
//
//  THE YIELD RULE (Phase 2724, operator ruling 2026-11-14), before Core is
//  asked: a reorder that leaves its parent's order as it was on its own
//  branch carries no intent, so beside the other branch's REAL reorder of the
//  same parent it is dropped from its delta (`yieldUnchangedReorders`), the
//  real order survives, and the script records it for that parent. Without
//  the rule the pair refuses: replayed after the real reorder, the unchanged
//  one would put the old order back.
//
//  THE CHILD-ORDER RULE (declared). Under a parent the policy interleaves or
//  rebases, the children neither branch appended keep the order the fold gives
//  them (a rebased reorder's order, where a branch reordered the parent), and
//  the appended ones follow: each branch's appends in that branch's own order,
//  the two runs merged by comparing their heads ordinally by NodeId, the
//  smaller first, and a node both runs hold (two agreeing moves of it) listed
//  once, where the merge first reaches it. An append a later reorder of the
//  same parent on the same branch lists is settled by that reorder and leaves
//  the run. Two runs merge to one order whichever branch is named first, so
//  the rule is symmetric by construction. It is the NodeId-byte tie-break the
//  merge-conformance corpus already pins (`merge-insert-tiebreak`), extended
//  from one append per side to a run.
//
//  The rule is the policy's, not the tree merge's, in two places the operator
//  ruled to pin (2026-11-10): a `MoveNode` to the parent a node already sits
//  under is an append (the tree applying the op puts it last), and appended
//  ids keep their branch's order (a NodeId sort is no authored order).
//
//  WHAT IS CHECKED, not assumed. The policy admits pairs Core does not call
//  independent, so Core's order-independence theorem does not cover them. The
//  fold is therefore run in BOTH orders (A then B, B then A), each normalised
//  by the same script, and a merge whose two trees differ is refused as
//  `OrderSensitive` rather than recorded. A delta that does not apply from the
//  base is refused before anything is folded.
//
//  THE VALIDATOR GATE. A structurally clean fold can still introduce a domain
//  defect neither branch had (Phase 184). `reconcileGated` runs
//  `ValidatorGate.introducedDefects` over the two branch heads and the derived
//  tree and refuses on any.
//
//  PRECEDENCE, AS A SCRIPT (Phase 2645, optional; bounded by §34.4 at Phases
//  2695 and 2725). A caller that names a side (`reconcileWith (Some side)`)
//  admits the pairs the policy would refuse, but for the kinds below, and
//  re-asserts that side's ops of them after the fold, in its delta's order, as
//  the tail of the script. Both fold orders still have to reach one tree with
//  the script, so precedence settles a pair whose two ops both apply in either
//  order (two writes of one slot), and nothing else. A pair it cannot settle
//  refuses, naming a pair the default refusal names, under a class that says
//  why (Phase 2743), and a precedence merge never answers `FoldFaulted`
//  (operator ruling 2026-11-14):
//   - before the fold, a `DeleteModify` pair whose removal takes the other
//     op's node (an edit, restyle, move or insert beside a removal of the node
//     or of an ancestor; Phase 2695), with the default entry, `DeleteModify`.
//     A `DeleteModify` pair whose removal is of another node (an insert beside
//     the removal of a sibling) applies in either order and is settled;
//   - before the fold, a pair that does not apply in both orders (Phase 2725):
//     two inserts of one id, or one op that, in the tree its own branch
//     applied it to, leaves the other unable to apply there. Class
//     `NotInBothOrders`: no named side settles it;
//   - after the fold, a pair whose named-side op does not apply, as written,
//     where it is re-asserted (Phase 2725): its own branch later removed or
//     moved what it addresses, or the other branch changed the child list a
//     re-asserted reorder names. Class `StaleReassertion`.
//  `judge` classes every pair in both contexts and the fold never classes one;
//  the two precedence-only classes are given only on the named-side path.
//
//  THE REPLAY WITNESS (Phase 2645, the cut). The policy is the DAG's only merge:
//  a merge node records the script, and Core's closure fold replays it. That
//  fold applies every op through `applyFold` — `applyHeld`, with a reorder whose
//  list the tree no longer matches applied rebased — which is what lets an
//  agreeing pair and a rebased reorder replay in Core's drain order.
//
//  Normative statement, envelope and fixtures: `WIRE_FORMAT.md` §"Merge by
//  fold" and the `foldFixtures` family of the merge-conformance corpus. Every
//  host certifies the same verdicts from the same deltas. Fable-clean: Map,
//  List, StringBuilder and the tier's canonical encoders only.
// ============================================================================

/// Which branch of a two-branch reconcile an op or a fault belongs to.
[<RequireQualifiedAccess>]
type ReconcileSide =
    | A
    | B

/// How the policy classes a pair it refuses. The default context gives only
/// `DeleteModify` and `Conflict`; the last two are given only under a named
/// precedence side, each naming why that side could not settle the pair
/// (Phase 2743, WIRE_FORMAT §34.4).
[<RequireQualifiedAccess>]
type RefusalClass =
    /// One op removes a node the other touches (edits, moves or inserts under).
    | DeleteModify
    /// Any other interference Core reports that is neither an agreement nor an
    /// interleave.
    | Conflict
    /// Precedence only: the two ops do not both apply in either order (two
    /// inserts of one id, or one op that, in its own branch's tree, leaves the
    /// other unable to apply), so no named side settles the pair.
    | NotInBothOrders
    /// Precedence only: the named side's op no longer applies where it is
    /// re-asserted (its own branch later removed or moved what it addresses, or
    /// the other branch changed the child list a re-asserted reorder names).
    | StaleReassertion

/// One refused pair: Core's conflict report entry (the two ops, the address
/// they collide on and its shape) with the policy's class.
type Refusal<'Msg> =
    { Conflict: Fuaran.Core.MergeConflict<TreeOp<'Msg>>
      Class: RefusalClass }

/// Why a reconcile refused.
[<RequireQualifiedAccess>]
type ReconcileFault<'Msg> =
    /// A branch's own delta does not apply from the base: the side, the index of
    /// the first op that fails, and the engine's error.
    | DeltaDoesNotApply of side: ReconcileSide * index: int * error: ApplyError
    /// Core reported pairs the policy does not admit, sorted canonically.
    | Refused of Refusal<'Msg> list
    /// Every pair was admitted, and the two fold orders still reached different
    /// trees (the canonical encodings of A-then-B and B-then-A).
    | OrderSensitive of aThenB: string * bThenA: string
    /// Every pair was admitted and the fold faulted applying an op. A pair a named
    /// precedence side cannot settle refuses instead (Phase 2725), so no fixture
    /// or generated pair reaches this under any context.
    | FoldFaulted of error: ApplyError
    /// The derived tree carries a defect neither branch head had (Phase 184).
    | IntroducedDefects of MergeDefect list

/// A derived merge: the tree the fold reaches, the script the merge records
/// (one `ReorderChildren` per interleaved, rebased or yielded-to parent, parents
/// in ordinal order, then any re-asserted precedence ops), how many distinct agreeing ops
/// the fold applied as no change, and how many parents it interleaved or
/// rebased.
type Reconciled<'Msg> =
    {
        Tree: Node<'Msg>
        Script: TreeOp<'Msg> list
        Agreements: int
        Interleaves: int
        /// How many refused ops a named precedence side re-asserted (0 without one).
        Reasserted: int
    }

module DagReconcile =

    let private canon (op: TreeOp<'Msg>) : string = CanonicalJson.encodeOp op

    let private childrenOf (n: Node<'Msg>) : Node<'Msg> list =
        getChildren n.Kind |> Option.defaultValue []

    let rec private tryFind (id: string) (n: Node<'Msg>) : Node<'Msg> option =
        if n.Id = id then
            Some n
        else
            childrenOf n |> List.tryPick (tryFind id)

    /// Is the effect of `op` already held by `tree`, so that a second
    /// application of an agreeing op is no change? Only the two cases where
    /// re-applying an identical op FAILS are named; every other op is
    /// idempotent under `Apply.apply` (a style, prop, state or kind set to the
    /// value it holds, a reorder to the order it has, a move to the parent the
    /// node is last in).
    let private held (op: TreeOp<'Msg>) (tree: Node<'Msg>) : bool =
        match op with
        | TreeOp.RemoveNode(NodeId id) -> (tryFind id tree).IsNone
        | TreeOp.InsertChild(NodeId parent, child) ->
            match tryFind parent tree with
            | Some p ->
                childrenOf p
                |> List.exists (fun c -> c.Id = child.Id && CanonicalJson.encodeNode c = CanonicalJson.encodeNode child)
            | None -> false
        | _ -> false

    /// `Apply.apply`, with an op whose effect `tree` already holds applied as no
    /// change — the fold's reading of an agreement. A `Batch` is applied member
    /// by member under the same rule, atomically: the first member that fails
    /// and is not held fails the batch with the pre-batch tree discarded.
    let rec applyHeld (op: TreeOp<'Msg>) (tree: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
        match op with
        | TreeOp.Batch ops -> ops |> List.fold (fun acc o -> acc |> Result.bind (applyHeld o)) (Ok tree)
        | _ ->
            match Apply.apply op tree with
            | Ok t -> Ok t
            | Error _ when held op tree -> Ok tree
            | Error e -> Error e

    /// A reorder of `parent` rebased to the children it holds in `tree`: the
    /// listed children still there, in the declared order, then the others in
    /// the order they hold. `op` itself where the parent is not in the tree.
    let private rebased (tree: Node<'Msg>) (parent: string) (order: NodeId list) (op: TreeOp<'Msg>) : TreeOp<'Msg> =
        match tryFind parent tree with
        | Some p ->
            let kids = childrenOf p |> List.map _.Id
            let present = Set.ofList kids
            let listed = order |> List.map (fun (NodeId c) -> c) |> List.filter present.Contains
            let listedSet = Set.ofList listed
            let rest = kids |> List.filter (listedSet.Contains >> not)
            TreeOp.ReorderChildren(NodeId parent, listed @ rest |> List.map NodeId)
        | None -> op

    /// The fold: `applyHeld` op by op, with a reorder of a parent in `rebase`
    /// applied rebased to the children the parent holds when the fold reaches it.
    let private foldOps
        (rebase: Set<string>)
        (ops: TreeOp<'Msg> list)
        (start: Node<'Msg>)
        : Result<Node<'Msg>, ApplyError> =
        ops
        |> List.fold
            (fun acc o ->
                acc
                |> Result.bind (fun t ->
                    match o with
                    | TreeOp.ReorderChildren(NodeId p, order) when rebase.Contains p ->
                        applyHeld (rebased t p order o) t
                    | _ -> applyHeld o t))
            (Ok start)

    /// The replay witness's apply (Phase 2645): `applyHeld`, and a
    /// `ReorderChildren` that does not apply as written — its list is stale
    /// because a concurrent branch appended to the parent or took a child from
    /// it — applied rebased to the children the parent holds. On a linear
    /// history every op applies as written, so this is `Apply.apply` there.
    let applyFold (op: TreeOp<'Msg>) (tree: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
        match applyHeld op tree, op with
        | Ok t, _ -> Ok t
        | Error e, TreeOp.ReorderChildren(NodeId p, order) ->
            match applyHeld (rebased tree p order op) tree with
            | Ok t -> Ok t
            | Error _ -> Error e
        | Error e, _ -> Error e

    /// The first op of `delta` that does not apply from `baseTree`, with its index.
    let private firstFailure (delta: TreeOp<'Msg> list) (baseTree: Node<'Msg>) : Result<Node<'Msg>, int * ApplyError> =
        delta
        |> List.indexed
        |> List.fold
            (fun acc (i, op) ->
                acc
                |> Result.bind (fun t -> Apply.apply op t |> Result.mapError (fun e -> i, e)))
            (Ok baseTree)

    let rec private removes (op: TreeOp<'Msg>) : bool =
        match op with
        | TreeOp.RemoveNode _ -> true
        | TreeOp.Batch ops -> List.exists removes ops
        | _ -> false

    /// Every id in the subtrees `op` inserts as a child (an `InsertChild`, or one
    /// inside a `Batch`), through every position a node is held in.
    let rec private insertedIds (op: TreeOp<'Msg>) : Set<string> =
        match op with
        | TreeOp.InsertChild(_, child) -> allNodeIds child |> List.map (fun (NodeId id) -> id) |> Set.ofList
        | TreeOp.Batch ops -> ops |> List.fold (fun acc o -> Set.union acc (insertedIds o)) Set.empty
        | _ -> Set.empty

    /// The node `op` appends to `parent`, when it is an append there.
    let private appendInto (parent: string) (op: TreeOp<'Msg>) : string option =
        match op with
        | TreeOp.InsertChild(NodeId p, child) when p = parent -> Some child.Id
        | TreeOp.MoveNode(NodeId moved, NodeId p) when p = parent -> Some moved
        | _ -> None

    /// The parent whose child list `reorder` and `other` both change, when one is
    /// a `ReorderChildren` and the other an append to its parent or a removal or
    /// move of a child it lists.
    let private rebaseOf (reorder: TreeOp<'Msg>) (other: TreeOp<'Msg>) : string option =
        match reorder with
        | TreeOp.ReorderChildren(NodeId p, order) ->
            let listed (c: string) = order |> List.contains (NodeId c)

            match other with
            | TreeOp.InsertChild(NodeId q, _) when q = p -> Some p
            | TreeOp.MoveNode(NodeId c, NodeId q) when q = p || listed c -> Some p
            | TreeOp.RemoveNode(NodeId c) when listed c -> Some p
            | _ -> None
        | _ -> None

    type private Verdict =
        | Agreement
        | Interleave of parent: string
        | Rebase of parent: string
        | Refuse of RefusalClass

    let private judge (c: Fuaran.Core.MergeConflict<TreeOp<'Msg>>) : Verdict =
        if canon c.Left = canon c.Right then
            Agreement
        else
            match c.Shape, appendInto c.Address c.Left, appendInto c.Address c.Right with
            | Fuaran.Core.MergeConflictShape.InsertPositionClash, Some l, Some r when l <> r -> Interleave c.Address
            | _ ->
                match rebaseOf c.Left c.Right |> Option.orElse (rebaseOf c.Right c.Left) with
                | Some p -> Rebase p
                | None ->
                    if removes c.Left <> removes c.Right then
                        Refuse RefusalClass.DeleteModify
                    else
                        Refuse RefusalClass.Conflict

    let private shapeText (s: Fuaran.Core.MergeConflictShape) : string =
        match s with
        | Fuaran.Core.MergeConflictShape.ConcurrentUpdate -> "ConcurrentUpdate"
        | Fuaran.Core.MergeConflictShape.InsertPositionClash -> "InsertPositionClash"
        | Fuaran.Core.MergeConflictShape.MoveVsRemove -> "MoveVsRemove"
        | Fuaran.Core.MergeConflictShape.SlotClash _ -> "SlotClash"

    let private slotOf (s: Fuaran.Core.MergeConflictShape) : string =
        match s with
        | Fuaran.Core.MergeConflictShape.SlotClash slot -> slot
        | _ -> ""

    let private classText (c: RefusalClass) : string =
        match c with
        | RefusalClass.DeleteModify -> "DeleteModify"
        | RefusalClass.Conflict -> "Conflict"
        | RefusalClass.NotInBothOrders -> "NotInBothOrders"
        | RefusalClass.StaleReassertion -> "StaleReassertion"

    let private orderKey (r: Refusal<'Msg>) =
        r.Conflict.Address,
        shapeText r.Conflict.Shape,
        slotOf r.Conflict.Shape,
        canon r.Conflict.Left,
        canon r.Conflict.Right

    let private ordinal (a: string) (b: string) = String.CompareOrdinal(a, b)

    let private compareKeys (a1, a2, a3, a4, a5) (b1, b2, b3, b4, b5) =
        [ ordinal a1 b1; ordinal a2 b2; ordinal a3 b3; ordinal a4 b4; ordinal a5 b5 ]
        |> List.tryFind ((<>) 0)
        |> Option.defaultValue 0

    /// Refusals in the canonical order — address, shape, slot, left op bytes,
    /// right op bytes, each ordinal — with exact duplicates removed. The order
    /// every host emits the envelope in.
    let sortCanonical (refusals: Refusal<'Msg> list) : Refusal<'Msg> list =
        refusals
        |> List.distinctBy orderKey
        |> List.sortWith (fun a b -> compareKeys (orderKey a) (orderKey b))

    /// The run-merge of the child-order rule: two runs, each kept in its own
    /// order, merged by comparing heads ordinally. An id both runs hold appears
    /// twice; `reorderFor` keeps its first occurrence.
    let rec private interleave (a: string list) (b: string list) : string list =
        match a, b with
        | [], rest
        | rest, [] -> rest
        | x :: xs, y :: ys ->
            if ordinal x y <= 0 then
                x :: interleave xs b
            else
                y :: interleave a ys

    /// The `ReorderChildren` the rule asks of `parent` in `tree`, given each
    /// branch's appends into it in branch order; `None` when the parent is not
    /// in the tree. Recorded even where the sequential fold already gives the
    /// rule's order (Phase 2645): a merge node's replay is Core's closure fold,
    /// which applies the two branches in its own drain order, so the script —
    /// not the fold order the policy happened to check — has to fix the order.
    let private reorderFor
        (tree: Node<'Msg>)
        (parent: string)
        (runA: string list)
        (runB: string list)
        : TreeOp<'Msg> option =
        tryFind parent tree
        |> Option.bind (fun p ->
            let kids = childrenOf p |> List.map _.Id
            let present = Set.ofList kids
            let appended = Set.union (Set.ofList runA) (Set.ofList runB)

            let keep (run: string list) =
                run |> List.filter present.Contains |> List.distinct

            // A node both runs hold (two agreeing moves of it) is listed once,
            // where the merge first reaches it: the merge of two runs is the same
            // sequence whichever is named first, so its first occurrence is too.
            let prefix = kids |> List.filter (appended.Contains >> not)
            let order = prefix @ (interleave (keep runA) (keep runB) |> List.distinct)
            Some(TreeOp.ReorderChildren(NodeId parent, order |> List.map NodeId)))

    /// A branch's run of appends into `parent`, in branch order. An append a
    /// later reorder of the same parent on the branch lists is settled by that
    /// reorder: the branch placed it, so it leaves the run.
    let private appendsOf (parent: string) (delta: TreeOp<'Msg> list) : string list =
        delta
        |> List.fold
            (fun run op ->
                match op with
                | TreeOp.ReorderChildren(NodeId p, order) when p = parent ->
                    run |> List.filter (fun id -> not (List.contains (NodeId id) order))
                | _ ->
                    match appendInto parent op with
                    | Some id when not (List.contains id run) -> run @ [ id ]
                    | _ -> run)
            []

    /// Each op of `delta` with the tree its own branch applies it to, threaded
    /// from `baseTree`. `reconcileWith` refuses a delta that does not apply
    /// before it reads this, so the fallback never decides a verdict.
    let private threaded (baseTree: Node<'Msg>) (delta: TreeOp<'Msg> list) : (TreeOp<'Msg> * Node<'Msg>) list =
        delta
        |> List.mapFold (fun t op -> (op, t), (Apply.apply op t |> Result.defaultValue t)) baseTree
        |> fst

    /// The parent a top-level `ReorderChildren` reorders, with whether its list
    /// is the parent's child order in `tree` — the same ids, in the same order,
    /// of the same length: the equality `TreeOpFootprint`'s unchanged-reorder arm
    /// states (Phase 2696). `None` for any other op, a `Batch` included, and for
    /// a reorder of a parent `tree` does not hold.
    let private reorderIn (tree: Node<'Msg>) (op: TreeOp<'Msg>) : (string * bool) option =
        match op with
        | TreeOp.ReorderChildren(NodeId p, order) ->
            findNode (NodeId p) tree
            |> Option.map (fun node -> p, (childrenOf node |> List.map (fun c -> NodeId c.Id)) = order)
        | _ -> None

    /// THE YIELD RULE (Phase 2724, WIRE_FORMAT §34.3), applied before anything is
    /// judged: a top-level `ReorderChildren` that leaves its parent's order as it
    /// was in the tree its own branch applies it to is dropped from its delta
    /// when the other delta holds a top-level `ReorderChildren` of the same parent
    /// that changes the order on its own branch. The dropped op carries no intent
    /// (on its own branch it is no change), so removing it leaves every later
    /// tree of its branch as it was and the delta still applies op by op; what it
    /// would otherwise do is restore the order it lists when replayed after the
    /// real reorder. Nothing else is dropped: an unchanged reorder beside an
    /// append or a removal in its parent, beside another unchanged reorder, or
    /// beside a reorder inside a `Batch` keeps its reads and is judged as before.
    /// Returns the two deltas without the yielded reorders and the parents they
    /// reordered (ordinal, distinct): the merge script records each such
    /// parent's surviving order, because a merge node's replay still applies the
    /// yielded op, in Core's drain order.
    let yieldUnchangedReorders
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : TreeOp<'Msg> list * TreeOp<'Msg> list * string list =
        let a = threaded baseTree deltaA
        let b = threaded baseTree deltaB

        let realParents (ops: (TreeOp<'Msg> * Node<'Msg>) list) =
            ops
            |> List.choose (fun (op, t) ->
                match reorderIn t op with
                | Some(p, false) -> Some p
                | _ -> None)
            |> Set.ofList

        let yields (otherReal: Set<string>) (op, t) =
            match reorderIn t op with
            | Some(p, true) when otherReal.Contains p -> Some p
            | _ -> None

        let split (ops: (TreeOp<'Msg> * Node<'Msg>) list) (otherReal: Set<string>) =
            ops |> List.filter (yields otherReal >> Option.isNone) |> List.map fst,
            ops |> List.choose (yields otherReal)

        let keptA, yieldedA = split a (realParents b)
        let keptB, yieldedB = split b (realParents a)
        keptA, keptB, yieldedA @ yieldedB |> List.distinct |> List.sortWith ordinal

    let private scriptFor
        (tree: Node<'Msg>)
        (parents: string list)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : TreeOp<'Msg> list =
        parents
        |> List.choose (fun p -> reorderFor tree p (appendsOf p deltaA) (appendsOf p deltaB))

    /// Reconcile two branch deltas from their common base under the policy, with
    /// an optional precedence side: the derived tree and the script a merge
    /// records, or a typed refusal. Both deltas apply from `baseTree`, op by op,
    /// as recorded on their branches. With `precedence = Some side`, a pair the
    /// policy refuses is admitted and `side`'s op of it is re-asserted at the end
    /// of the script, except a `DeleteModify` pair whose removal takes the other
    /// op's node, a pair that does not apply in both orders, and a pair whose
    /// re-asserted op no longer applies, each of which refuses as a pair the
    /// default context refuses: the first with the default entry, the second as
    /// `NotInBothOrders`, the third as `StaleReassertion` (see the header). No
    /// context answers `FoldFaulted` for a pair; the case remains for a fold that faults on an op the policy
    /// itself admitted, which no fixture or generated pair reaches.
    let reconcileWith
        (precedence: ReconcileSide option)
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : Result<Reconciled<'Msg>, ReconcileFault<'Msg>> =
        match firstFailure deltaA baseTree, firstFailure deltaB baseTree with
        | Error(i, e), _ -> Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.A, i, e))
        | _, Error(i, e) -> Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.B, i, e))
        | Ok headA, Ok headB ->
            // The yield rule runs first, in every context (Phase 2724): what follows judges,
            // folds and re-asserts the deltas without the yielded reorders.
            let deltaA, deltaB, yielded = yieldUnchangedReorders baseTree deltaA deltaB

            let verdicts =
                Fuaran.Core.Dag.conflictsIn TreeOpFootprint.ofOpIn Apply.apply baseTree deltaA deltaB
                |> List.map (fun c -> c, judge c)

            let refusals =
                verdicts
                |> List.choose (fun (c, v) ->
                    match v with
                    | Refuse cls -> Some { Conflict = c; Class = cls }
                    | _ -> None)

            // THE ONE PLACE the two contexts differ (Phase 2695, WIRE_FORMAT §34.4). Both class
            // every pair through `judge`; the default refuses every pair it does not admit. A
            // named side settles two writes of one slot and nothing else, so it refuses here,
            // before anything is folded and with the entries the default context gives them,
            // the `DeleteModify` pairs whose removal takes the other op's node (Phase 2743 leaves
            // their class as it is; the two later readings re-class theirs): a node the other
            // op addresses that the base holds and the removing branch's head does not. The
            // class still decides the pair is a delete beside a modify; which node the removal
            // took is read from the branch head, and the fold is never the classifier. A
            // `DeleteModify` pair whose removal is of another node (an insert beside the removal
            // of a sibling, both changing the parent's child list) applies in either order, and
            // precedence settles it as it does a `Conflict`.
            let unsettled =
                match precedence with
                | None -> refusals
                | Some _ ->
                    let takesItsNode (r: Refusal<'Msg>) =
                        let other, removingHead =
                            if removes r.Conflict.Left then
                                r.Conflict.Right, headA
                            else
                                r.Conflict.Left, headB

                        Fuaran.UI.Ops.TreeOp.targets other
                        |> List.exists (fun id ->
                            // `findNode` reaches every position the engine addresses, a mount's
                            // slot arguments included.
                            (findNode id baseTree).IsSome && (findNode id removingHead).IsNone)

                    // Phase 2725: a pair whose two ops do not both apply in either order is not two
                    // writes of one slot, so a named side cannot settle it either, and it refuses
                    // here, before the fold, as `NotInBothOrders` (Phase 2743). Two
                    // readings, both of the deltas and never of the fold. (1) The two ops insert a
                    // node under one id (with different bytes, or the pair would be an agreement):
                    // whichever applies second meets the id the first placed. (2) One op, in the
                    // tree its own branch applied it to, takes away what the other needs there: the
                    // other applies to that tree, and no longer applies once the first has (two
                    // moves that would put each node inside the other, for one). Where the other
                    // does not apply to that tree at all, the reading says nothing.
                    let insertsOneId (r: Refusal<'Msg>) =
                        not (Set.isEmpty (Set.intersect (insertedIds r.Conflict.Left) (insertedIds r.Conflict.Right)))

                    let ownTree (delta: TreeOp<'Msg> list) =
                        let trees = threaded baseTree delta

                        fun (op: TreeOp<'Msg>) ->
                            trees |> List.tryFind (fun (o, _) -> canon o = canon op) |> Option.map snd

                    let treeA, treeB = ownTree deltaA, ownTree deltaB

                    // `first`, applied to `t` (the tree its own branch applied it to), leaves
                    // `other` unable to apply, where `other` applies to `t`.
                    let takesWhatItNeeds (first: TreeOp<'Msg>) (other: TreeOp<'Msg>) (t: Node<'Msg> option) =
                        match t with
                        | Some t when Result.isOk (applyHeld other t) ->
                            match applyHeld first t with
                            | Ok after -> Result.isError (applyHeld other after)
                            | Error _ -> false
                        | _ -> false

                    let notInBothOrders (r: Refusal<'Msg>) =
                        insertsOneId r
                        || takesWhatItNeeds r.Conflict.Left r.Conflict.Right (treeA r.Conflict.Left)
                        || takesWhatItNeeds r.Conflict.Right r.Conflict.Left (treeB r.Conflict.Right)

                    // Reading 1 is checked first, so a removal of the other op's node keeps the
                    // `DeleteModify` it has in every context (such a pair also fails reading 2);
                    // a pair only reading 2 refuses says so (Phase 2743).
                    refusals
                    |> List.choose (fun r ->
                        if r.Class = RefusalClass.DeleteModify && takesItsNode r then
                            Some r
                        elif notInBothOrders r then
                            Some
                                { r with
                                    Class = RefusalClass.NotInBothOrders }
                        else
                            None)

            // The named side's op of a refused pair.
            let mine (r: Refusal<'Msg>) =
                match precedence with
                | Some ReconcileSide.B -> r.Conflict.Right
                | _ -> r.Conflict.Left

            match unsettled with
            | _ :: _ -> Error(ReconcileFault.Refused(sortCanonical unsettled))
            | [] ->
                // The winner's refused ops, in its delta's order, each once.
                let reasserted =
                    match precedence with
                    | None -> []
                    | Some side ->
                        let named = refusals |> List.map (mine >> canon) |> Set.ofList
                        let delta = if side = ReconcileSide.A then deltaA else deltaB

                        delta |> List.filter (canon >> named.Contains) |> List.distinctBy canon

                let parentsOf pick =
                    verdicts |> List.choose (snd >> pick) |> List.distinct |> List.sortWith ordinal

                let rebase =
                    parentsOf (function
                        | Rebase p -> Some p
                        | _ -> None)
                    |> Set.ofList

                let parents =
                    parentsOf (function
                        | Interleave p
                        | Rebase p -> Some p
                        | _ -> None)

                // A pair is reported once per address it collides on, so count the distinct
                // agreeing ops, not the report's entries.
                let agreements =
                    verdicts
                    |> List.filter (fun (_, v) -> v = Agreement)
                    |> List.map (fun (c, _) -> canon c.Left)
                    |> List.distinct
                    |> List.length

                // A parent whose reorder yielded is recorded too: its order in the
                // fold is the surviving reorder's, which the script fixes for replay.
                let scripted = parents @ yielded |> List.distinct |> List.sortWith ordinal

                // The fold, the child-order script, then the re-asserted ops one by one. A
                // re-asserted op that does not apply, as written, where it is re-asserted is
                // skipped and named (Phase 2725): its own branch later removed or moved what it
                // addresses, or the other branch changed the child list a re-asserted reorder
                // names. Skipping one changes what the later ones meet, so every host walks the
                // list in the same order and skips the same way.
                let derive first second =
                    foldOps rebase (first @ second) baseTree
                    |> Result.bind (fun t ->
                        let reorders = scriptFor t scripted deltaA deltaB

                        foldOps Set.empty reorders t
                        |> Result.map (fun t' ->
                            let tail, stale =
                                reasserted
                                |> List.fold
                                    (fun (tree, stale) op ->
                                        match applyHeld op tree with
                                        | Ok next -> next, stale
                                        | Error _ -> tree, Set.add (canon op) stale)
                                    (t', Set.empty)

                            tail, reorders @ reasserted, stale))

                match derive deltaA deltaB, derive deltaB deltaA with
                | Error e, _
                | _, Error e -> Error(ReconcileFault.FoldFaulted e)
                | Ok(_, _, staleAB), Ok(_, _, staleBA) when not (Set.isEmpty staleAB && Set.isEmpty staleBA) ->
                    // A named side settles a pair only by re-asserting its op, so a pair whose op
                    // no longer applies in either fold order is NOT settled: it refuses as
                    // `StaleReassertion` (Phase 2743), beside every other pair whose named-side op
                    // is the same.
                    let stale = Set.union staleAB staleBA

                    Error(
                        ReconcileFault.Refused(
                            sortCanonical (
                                refusals
                                |> List.filter (mine >> canon >> stale.Contains)
                                |> List.map (fun r ->
                                    { r with
                                        Class = RefusalClass.StaleReassertion })
                            )
                        )
                    )
                | Ok(ab, script, _), Ok(ba, _, _) ->
                    let abText = CanonicalJson.encodeNode ab
                    let baText = CanonicalJson.encodeNode ba

                    if abText <> baText then
                        Error(ReconcileFault.OrderSensitive(abText, baText))
                    else
                        Ok
                            { Tree = ab
                              Script = script
                              Agreements = agreements
                              Interleaves = parents.Length
                              Reasserted = reasserted.Length }

    /// `reconcileWith` with no precedence: every pair the policy does not admit
    /// refuses.
    let reconcile
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : Result<Reconciled<'Msg>, ReconcileFault<'Msg>> =
        reconcileWith None baseTree deltaA deltaB

    /// `reconcile`, then the validator gate on the derived tree: a defect the
    /// derived tree carries and neither branch head does refuses the merge.
    let reconcileGated
        (validator: MergeValidator<'Msg>)
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : Result<Reconciled<'Msg>, ReconcileFault<'Msg>> =
        reconcileWith None baseTree deltaA deltaB
        |> Result.bind (fun r ->
            // Both deltas applied from the base inside `reconcile`, so the heads exist.
            let head delta =
                foldOps Set.empty delta baseTree |> Result.defaultValue baseTree

            match ValidatorGate.introducedDefects validator (head deltaA) (head deltaB) r.Tree with
            | [] -> Ok r
            | defects -> Error(ReconcileFault.IntroducedDefects defects))

    /// The refusal envelope: a JSON array of the refused pairs in canonical
    /// order, each an object with ordinal-sorted keys —
    /// `{"address":…,"class":<class>,"left":<op>,"right":<op>,"shape":…[,"slot":…]}`
    /// — `class` one of `Conflict` and `DeleteModify` (every context) or
    /// `NotInBothOrders` and `StaleReassertion` (a named precedence side only),
    /// the ops embedded as their canonical encodings and `slot` present only
    /// for a `SlotClash`. Byte-stable across hosts; `sha256Hex` over it is the
    /// cross-host refusal hash.
    let encodeEnvelope (refusals: Refusal<'Msg> list) : string =
        let sb = StringBuilder()
        let q (s: string) = Fuaran.Core.JsonString.quote s
        sb.Append '[' |> ignore

        sortCanonical refusals
        |> List.iteri (fun i r ->
            if i > 0 then
                sb.Append ',' |> ignore

            sb
                .Append("{\"address\":")
                .Append(q r.Conflict.Address)
                .Append(",\"class\":")
                .Append(q (classText r.Class))
                .Append(",\"left\":")
                .Append(canon r.Conflict.Left)
                .Append(",\"right\":")
                .Append(canon r.Conflict.Right)
                .Append(",\"shape\":")
                .Append(q (shapeText r.Conflict.Shape))
            |> ignore

            match r.Conflict.Shape with
            | Fuaran.Core.MergeConflictShape.SlotClash slot -> sb.Append(",\"slot\":").Append(q slot) |> ignore
            | _ -> ()

            sb.Append '}' |> ignore)

        sb.Append(']').ToString()
