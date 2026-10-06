namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

// ============================================================================
//  BatchAccept — batch-aware partial accept (Phase 179).
//
//  A conflicted `Batch` is not accepted op-by-op arbitrarily: partial-accept
//  takes a **dependency-closed subset**, never an incoherent one. Each op's
//  dependency on an earlier op is structural — they reference a shared node, or
//  the later op targets a node the earlier op inserted. The closure of a chosen
//  index set pulls in every earlier op it transitively depends on.
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

    /// The dependency-closed superset of `selected` indices: an op depends on an
    /// EARLIER op when their footprints share a node — they address one, or the
    /// later op addresses a node anywhere in a subtree the earlier op put into
    /// the tree (`TreeOp.footprint`, Phase 2044).
    ///
    /// One pass over the list in REVERSE. Every dependency points backwards, so
    /// by the time op `i` is reached every op that could need it has already
    /// been decided; `i` is kept iff it was selected or its footprint meets the
    /// union of the kept later ops' footprints. That union is the whole state —
    /// no pairwise intersection is recomputed, and no fixpoint is iterated.
    let dependencyClosure<'Msg> (ops: TreeOp<'Msg> list) (selected: Set<int>) : Set<int> =
        let footprints = ops |> List.map TreeOp.footprint |> List.toArray

        let _, closed =
            Array.foldBack
                (fun (i, footprint: Set<NodeId>) (needed: Set<NodeId>, kept: Set<int>) ->
                    if Set.contains i selected || not (Set.isEmpty (Set.intersect footprint needed)) then
                        Set.union needed footprint, Set.add i kept
                    else
                        needed, kept)
                (Array.indexed footprints)
                (Set.empty, Set.empty)

        // An index outside the list depends on nothing and is returned as given.
        Set.union closed selected


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
        let all = Set.ofList [ 0 .. List.length ops - 1 ]
        let closed = dependencyClosure ops selected

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
