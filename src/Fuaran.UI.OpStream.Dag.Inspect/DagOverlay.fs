namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagOverlay — the precedence + retention overlay (Phase 186).
//
//  The render model (`DagGraphModel`) draws the branch/merge shape; this overlay
//  colours it with two provenance axes:
//
//   1. **Precedence.** A node is `Primary` when the host's `isPrimary` predicate
//      classifies it so — the overlay interprets no record field itself. (The
//      per-cell primacy view retired with the tree merge at Phase 2645: a merge's
//      precedence is now a side named on the merge, re-asserted as script.)
//
//   2. **Retention.** A node is `Live` when a GC root (a head, checkpoint, or
//      live resume-coordinate/permalink) needs it, `Prunable` when none does
//      (Core's `Dag.prunable` over the graph's topology), and `Tombstoned` when
//      it is already pruned — drawn from the sink's skeleton, links intact.
//      Grace/auto-pin POLICY is the host's, not shown here.
//
//  Pure + derived: it joins the graph nodes against the topology + roots and
//  writes nothing back (FGP 5).
// ============================================================================

/// A node's retention state on the graph. `Tombstoned` takes precedence (the
/// payload is already gone); otherwise `Live` (reachable from a GC root) or
/// `Prunable` (reachable from none).
[<RequireQualifiedAccess>]
type RetentionState =
    | Live
    | Prunable
    | Tombstoned

/// A render node annotated with its provenance overlay: `IsPrimary` (the host's
/// precedence classification of the node, via the `isPrimary` predicate) and
/// `Retention` (live/prunable/tombstoned). Joins 1:1 with `DagGraph.Nodes`.
type OverlayNode =
    { Node: DagGraphNode
      IsPrimary: bool
      Retention: RetentionState }

/// The overlay over a whole graph: the annotated nodes plus the resolved
/// `Live` / `Prunable` hash sets (the reachability partition the renderer can
/// use to dim prunable branches).
type DagOverlay =
    { Nodes: OverlayNode list
      Live: Set<string>
      Prunable: Set<string> }

module DagOverlay =

    /// Build the overlay for `graph` under the GC `roots`: per-node precedence
    /// (via the host's `isPrimary` predicate) + retention state, plus the
    /// live/prunable partition. A root the graph does not hold is refused by
    /// name (Core's `PruneFault`), because it would otherwise retain nothing.
    let build
        (isPrimary: DagGraphNode -> bool)
        (graph: DagGraph)
        (roots: GcRoots)
        : Result<DagOverlay, Fuaran.Core.Dag.PruneFault> =
        let topology =
            graph.Nodes
            |> List.map (fun n ->
                n.Hash,
                ({ Id = n.Hash
                   Parents = n.Parents
                   Actor = Fuaran.Core.Actor.ofLegacyString ""
                   Op = () }
                : Fuaran.Core.DagNode<unit>))
            |> Map.ofList

        let dag =
            match Fuaran.Core.Dag.ofNodes topology with
            | Ok d -> d
            | Error m -> invalidOp ("DagOverlay: node filed under another id: " + m.Key)

        Fuaran.Core.Dag.prunable (Fuaran.Core.Dag.Reach.ofDag dag) (DagRetention.rootHashes roots |> Set.toList)
        |> Result.map (fun prunableList ->
            let prunable = Set.ofList prunableList

            let live =
                graph.Nodes
                |> List.map _.Hash
                |> Set.ofList
                |> fun all -> Set.difference all prunable

            let nodes =
                graph.Nodes
                |> List.map (fun n ->
                    let retention =
                        if n.Tombstoned then RetentionState.Tombstoned
                        elif live.Contains n.Hash then RetentionState.Live
                        else RetentionState.Prunable

                    { Node = n
                      IsPrimary = isPrimary n
                      Retention = retention })

            { Nodes = nodes
              Live = live
              Prunable = prunable })
