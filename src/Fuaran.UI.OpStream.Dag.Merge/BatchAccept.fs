namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
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

    let private rawId (NodeId s) : string = s

    /// All NodeIds an op references (targets + parents + reordered children).
    let rec private opNodeIds<'Msg> (op: TreeOp<'Msg>) : Set<string> =
        match op with
        | TreeOp.EditNode(t, _) -> Set.singleton (rawId t)
        | TreeOp.UpdateProp(t, _, _) -> Set.singleton (rawId t)
        | TreeOp.ReplaceBinding(t, _, _) -> Set.singleton (rawId t)
        | TreeOp.UpdateStyle(t, _) -> Set.singleton (rawId t)
        | TreeOp.UpdateState(t, _) -> Set.singleton (rawId t)
        | TreeOp.InsertChild(p, _, child) -> Set.ofList [ rawId p; rawId child.Id ]
        | TreeOp.RemoveNode t -> Set.singleton (rawId t)
        | TreeOp.MoveNode(t, np, _) -> Set.ofList [ rawId t; rawId np ]
        | TreeOp.ReorderChildren(p, order) -> Set.ofList (rawId p :: (order |> List.map rawId))
        | TreeOp.ReplaceRoot node -> Set.singleton (rawId node.Id)
        | TreeOp.Batch ops -> ops |> List.map opNodeIds |> Set.unionMany

    /// The dependency-closed superset of `selected` indices: an op depends on an
    /// EARLIER op when they share a referenced node. Iterated to a fixpoint so
    /// transitive chains (op C needs B needs A) are fully closed.
    let dependencyClosure<'Msg> (ops: TreeOp<'Msg> list) (selected: Set<int>) : Set<int> =
        let arr = List.toArray ops
        let ids = arr |> Array.map opNodeIds

        let rec fix (acc: Set<int>) =
            let next =
                acc
                |> Set.fold
                    (fun (a: Set<int>) (j: int) ->
                        // pull in every earlier op j shares a node with
                        let deps =
                            [ for i in 0 .. j - 1 do
                                  if not (Set.isEmpty (Set.intersect ids[i] ids[j])) then
                                      yield i ]

                        Set.union a (Set.ofList deps))
                    acc

            if next = acc then acc else fix next

        fix selected

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
