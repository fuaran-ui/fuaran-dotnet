namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

// ============================================================================
//  BatchAccept — batch-aware partial accept (Phase 179).
//
//  A conflicted `Batch` is not accepted op-by-op arbitrarily: partial-accept
//  takes a **dependency-closed subset**, never an incoherent one. An op depends
//  on an earlier op when their footprints interfere under Fuaran.Core's
//  `Ops.interference` (Phase 2292) — the verdict arbitration and the DAG fold
//  read, so the three cannot disagree about which ops are ordered. The closure
//  of a chosen index set pulls in every earlier op it transitively depends on.
//
//  The precedence lattice is **validity > primacy pins > batch atomicity >
//  convenience**: a `Secondary`-declared `indivisible` batch may *withdraw*
//  (reject + rework) but never override a pin — encoded here as `indivisible`
//  forcing all-or-nothing (the caller falls back to re-split, the joint-loop default).
//  The accepted subset is emitted as `BatchPartiallyAccepted { Kept; Dropped;
//  Reason }`, distinct from `BatchAborted`.
// ============================================================================

/// Outcome of a batch partial-accept: which inner-op indices were kept (a
/// dependency-closed subset) vs dropped, and why.
type BatchPartiallyAccepted =
    { Kept: int list
      Dropped: int list
      Reason: string }

module BatchAccept =

    /// The reverse pass both closures share; see `dependencyClosure`.
    let private closeOver (footprints: Fuaran.Core.Footprint list) (selected: Set<int>) : Set<int> =
        let _, closed =
            List.foldBack
                (fun (i, footprint: Fuaran.Core.Footprint) (needed: Fuaran.Core.Footprint, kept: Set<int>) ->
                    if Set.contains i selected || not (Fuaran.Core.Ops.independent footprint needed) then
                        Fuaran.Core.Footprint.union needed footprint, Set.add i kept
                    else
                        needed, kept)
                (List.indexed footprints)
                (Fuaran.Core.Footprint.empty, Set.empty)

        // An index outside the list depends on nothing and is returned as given.
        Set.union closed selected

    /// The dependency-closed superset of `selected` indices: an op depends on an
    /// EARLIER op when the two INTERFERE — `Fuaran.Core.Ops.interference` over
    /// their footprints (`TreeOpFootprint.ofOp`, Phase 2292) names a clause they
    /// fail. That covers a shared address (an op on a node anywhere in a subtree
    /// an earlier op put in, Phase 2044) and the clause an address set cannot
    /// see: a remove or a move writes a parent it cannot name, so it is ordered
    /// against a later op that writes structure anywhere. A kept
    /// `ReorderChildren` of `P` therefore keeps an earlier `MoveNode` of a child
    /// of `P` (or a `RemoveNode` of one), without which the reorder no longer
    /// names `P`'s children and is refused.
    ///
    /// Sound because every dropped op is independent of every LATER kept op, so
    /// it commutes past them to the end of the batch, where dropping it leaves
    /// a prefix of a batch that applied.
    ///
    /// One pass over the list in REVERSE. Every dependency points backwards, so
    /// by the time op `i` is reached every op that could need it has already
    /// been decided; `i` is kept iff it was selected or it interferes with the
    /// union of the kept later ops' footprints, which is exact because a union
    /// is independent of an op only when each part is. That union is the whole
    /// state — no pairwise interference is recomputed, and no fixpoint is
    /// iterated.
    let dependencyClosure<'Msg> (ops: TreeOp<'Msg> list) (selected: Set<int>) : Set<int> =
        closeOver (ops |> List.map TreeOpFootprint.ofOp) selected

    /// `dependencyClosure` where the batch's base tree is in hand (Phase 2327):
    /// each op is footprinted at the tree the ops before it reach
    /// (`TreeOpFootprint.ofEachIn`), so a removal names the parent it writes and
    /// the subtree it destroys, and an in-place edit is ordered only against the
    /// ops that touch its slot or its node. A selected restyle no longer keeps
    /// every earlier op; it keeps the ones it depends on. The closure argument is
    /// the pure form's: a dropped op is independent of every later kept op.
    let dependencyClosureIn<'Msg> (baseTree: Node<'Msg>) (ops: TreeOp<'Msg> list) (selected: Set<int>) : Set<int> =
        closeOver (TreeOpFootprint.ofEachIn baseTree ops) selected

    /// The kept / dropped split of a closed index set; see `partialAccept`.
    let private acceptClosed<'Msg>
        (ops: TreeOp<'Msg> list)
        (closed: Set<int>)
        (indivisible: bool)
        : TreeOp<'Msg> list * BatchPartiallyAccepted =
        let all = Set.ofList [ 0 .. List.length ops - 1 ]

        if indivisible && closed <> all then
            // Withdraw — an indivisible batch cannot be split.
            [],
            { Kept = []
              Dropped = Set.toList all
              Reason = "indivisible batch — withdrawn for re-split" }
        else
            let keptIdx = Set.toList closed |> List.sort
            let dropped = Set.difference all closed |> Set.toList |> List.sort
            let arr = List.toArray ops
            let keptOps = keptIdx |> List.map (fun i -> arr[i])

            keptOps,
            { Kept = keptIdx
              Dropped = dropped
              Reason =
                if dropped.IsEmpty then
                    "all ops accepted"
                else
                    "dependency-closed partial accept" }

    /// Partially accept a `Batch`'s inner ops, keeping a dependency-closed subset
    /// of `selected`. `indivisible = true` forces all-or-nothing (the batch
    /// atomicity tier of the precedence lattice): a non-full selection drops
    /// everything with a withdraw reason, so the caller re-delegates (re-split
    /// default) rather than applying an incoherent partial.
    let partialAccept<'Msg>
        (ops: TreeOp<'Msg> list)
        (selected: Set<int>)
        (indivisible: bool)
        : TreeOp<'Msg> list * BatchPartiallyAccepted =
        acceptClosed ops (dependencyClosure ops selected) indivisible

    /// `partialAccept` over `dependencyClosureIn`: the batch's base tree in hand.
    let partialAcceptIn<'Msg>
        (baseTree: Node<'Msg>)
        (ops: TreeOp<'Msg> list)
        (selected: Set<int>)
        (indivisible: bool)
        : TreeOp<'Msg> list * BatchPartiallyAccepted =
        acceptClosed ops (dependencyClosureIn baseTree ops selected) indivisible
