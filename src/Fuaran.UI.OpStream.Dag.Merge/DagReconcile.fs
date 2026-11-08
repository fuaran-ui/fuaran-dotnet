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
//   3. Anything else. Refused, naming Core's shape and address. A removal
//      against any concurrent touch of the removed node — an edit, a move, an
//      insert beneath it — is classed `DeleteModify`; every other pair is
//      `Conflict`.
//
//  THE CHILD-ORDER RULE (declared). Under a parent the policy interleaves,
//  the children neither branch appended keep the order the fold gives them,
//  and the appended ones follow: each branch's appends in that branch's own
//  order, the two runs merged by comparing their heads ordinally by NodeId,
//  the smaller first. Two runs of distinct ids merge to one order whichever
//  branch is named first, so the rule is symmetric by construction. It is the
//  NodeId-byte tie-break the merge-conformance corpus already pins
//  (`merge-insert-tiebreak`), extended from one append per side to a run.
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

/// How the policy classes a pair it refuses.
[<RequireQualifiedAccess>]
type RefusalClass =
    /// One op removes a node the other touches (edits, moves or inserts under).
    | DeleteModify
    /// Any other interference Core reports that is neither an agreement nor an
    /// interleave.
    | Conflict

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
    /// Every pair was admitted and the fold faulted applying an op.
    | FoldFaulted of error: ApplyError
    /// The derived tree carries a defect neither branch head had (Phase 184).
    | IntroducedDefects of MergeDefect list

/// A derived merge: the tree the fold reaches, the script the merge records
/// (one `ReorderChildren` per interleaved parent whose fold order the rule
/// changes, parents in ordinal order), how many distinct agreeing ops the fold
/// applied as no change, and how many parents it interleaved.
type Reconciled<'Msg> =
    { Tree: Node<'Msg>
      Script: TreeOp<'Msg> list
      Agreements: int
      Interleaves: int }

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

    let private foldOps (ops: TreeOp<'Msg> list) (start: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
        ops |> List.fold (fun acc o -> acc |> Result.bind (applyHeld o)) (Ok start)

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

    /// The node `op` appends to `parent`, when it is an append there.
    let private appendInto (parent: string) (op: TreeOp<'Msg>) : string option =
        match op with
        | TreeOp.InsertChild(NodeId p, child) when p = parent -> Some child.Id
        | TreeOp.MoveNode(NodeId moved, NodeId p) when p = parent -> Some moved
        | _ -> None

    type private Verdict =
        | Agreement
        | Interleave of parent: string
        | Refuse of RefusalClass

    let private judge (c: Fuaran.Core.MergeConflict<TreeOp<'Msg>>) : Verdict =
        if canon c.Left = canon c.Right then
            Agreement
        else
            match c.Shape, appendInto c.Address c.Left, appendInto c.Address c.Right with
            | Fuaran.Core.MergeConflictShape.InsertPositionClash, Some l, Some r when l <> r -> Interleave c.Address
            | _ ->
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

    /// The run-merge of the child-order rule: two runs of distinct ids, each kept
    /// in its own order, merged by comparing heads ordinally.
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
    /// branch's appends into it in branch order; `None` when the fold's order is
    /// already the rule's, or the parent is not in the tree.
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

            let prefix = kids |> List.filter (appended.Contains >> not)
            let order = prefix @ interleave (keep runA) (keep runB)

            if order = kids then
                None
            else
                Some(TreeOp.ReorderChildren(NodeId parent, order |> List.map NodeId)))

    let private appendsOf (parent: string) (delta: TreeOp<'Msg> list) : string list =
        delta |> List.choose (appendInto parent)

    let private scriptFor
        (tree: Node<'Msg>)
        (parents: string list)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : TreeOp<'Msg> list =
        parents
        |> List.choose (fun p -> reorderFor tree p (appendsOf p deltaA) (appendsOf p deltaB))

    /// Reconcile two branch deltas from their common base under the policy: the
    /// derived tree and the script a merge records, or a typed refusal. Both
    /// deltas apply from `baseTree`, op by op, as recorded on their branches.
    let reconcile
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : Result<Reconciled<'Msg>, ReconcileFault<'Msg>> =
        match firstFailure deltaA baseTree, firstFailure deltaB baseTree with
        | Error(i, e), _ -> Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.A, i, e))
        | _, Error(i, e) -> Error(ReconcileFault.DeltaDoesNotApply(ReconcileSide.B, i, e))
        | Ok _, Ok _ ->
            let verdicts =
                Fuaran.Core.Dag.conflictsIn TreeOpFootprint.ofOpIn Apply.apply baseTree deltaA deltaB
                |> List.map (fun c -> c, judge c)

            let refusals =
                verdicts
                |> List.choose (fun (c, v) ->
                    match v with
                    | Refuse cls -> Some { Conflict = c; Class = cls }
                    | _ -> None)

            if not refusals.IsEmpty then
                Error(ReconcileFault.Refused(sortCanonical refusals))
            else
                let parents =
                    verdicts
                    |> List.choose (fun (_, v) ->
                        match v with
                        | Interleave p -> Some p
                        | _ -> None)
                    |> List.distinct
                    |> List.sortWith ordinal

                // A pair is reported once per address it collides on, so count the distinct
                // agreeing ops, not the report's entries.
                let agreements =
                    verdicts
                    |> List.filter (fun (_, v) -> v = Agreement)
                    |> List.map (fun (c, _) -> canon c.Left)
                    |> List.distinct
                    |> List.length

                let derive first second =
                    foldOps (first @ second) baseTree
                    |> Result.bind (fun t ->
                        let script = scriptFor t parents deltaA deltaB
                        foldOps script t |> Result.map (fun t' -> t', script))

                match derive deltaA deltaB, derive deltaB deltaA with
                | Error e, _
                | _, Error e -> Error(ReconcileFault.FoldFaulted e)
                | Ok(ab, script), Ok(ba, _) ->
                    let abText = CanonicalJson.encodeNode ab
                    let baText = CanonicalJson.encodeNode ba

                    if abText <> baText then
                        Error(ReconcileFault.OrderSensitive(abText, baText))
                    else
                        Ok
                            { Tree = ab
                              Script = script
                              Agreements = agreements
                              Interleaves = parents.Length }

    /// `reconcile`, then the validator gate on the derived tree: a defect the
    /// derived tree carries and neither branch head does refuses the merge.
    let reconcileGated
        (validator: MergeValidator<'Msg>)
        (baseTree: Node<'Msg>)
        (deltaA: TreeOp<'Msg> list)
        (deltaB: TreeOp<'Msg> list)
        : Result<Reconciled<'Msg>, ReconcileFault<'Msg>> =
        reconcile baseTree deltaA deltaB
        |> Result.bind (fun r ->
            // Both deltas applied from the base inside `reconcile`, so the heads exist.
            let head delta =
                foldOps delta baseTree |> Result.defaultValue baseTree

            match ValidatorGate.introducedDefects validator (head deltaA) (head deltaB) r.Tree with
            | [] -> Ok r
            | defects -> Error(ReconcileFault.IntroducedDefects defects))

    /// The refusal envelope: a JSON array of the refused pairs in canonical
    /// order, each an object with ordinal-sorted keys —
    /// `{"address":…,"class":"Conflict"|"DeleteModify","left":<op>,"right":<op>,"shape":…[,"slot":…]}`
    /// — the ops embedded as their canonical encodings and `slot` present only
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
