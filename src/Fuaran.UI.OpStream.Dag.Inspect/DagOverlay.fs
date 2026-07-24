namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  DagOverlay — the precedence + retention/tombstone overlay (Phase 186).
//
//  The render model (`DagGraphModel`) draws the branch/merge shape; this overlay
//  colours it with the two provenance axes the DAG made first-class:
//
//   1. **Precedence.** A node is `Primary` when the host's `isPrimary` predicate
//      classifies it so (the same host classification `DagPrimacy` resolves
//      per-cell precedence from — the overlay interprets no record field itself).
//      The per-node flag is the coarse view; `primaryPinnedCells` exposes the
//      fine view — the `(nodeId, facet)` cells whose MOST-RECENT writer on a
//      branch's spine was `Primary` (a backward DAG walk, so a secondary-tipped
//      branch still shows the earlier primary edits it preserved).
//
//   2. **Retention.** A node is `Live` when reachable from a GC root (a head,
//      checkpoint, or live resume-coordinate/permalink), `Prunable` when
//      reachable from none, and `Tombstoned` when its payload has already been
//      pruned (hash + parent links preserved so the chain still verifies). The
//      classification routes through `DagRetention`'s reachability mechanism —
//      grace/auto-pin POLICY is the host's, not shown here.
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

    /// The parent lookup induced by a built graph (a node's `Parents`, or `[]`
    /// for an unknown hash) — feeds the `DagRetention` / `DagTopology`
    /// reachability walks without a second sink round-trip.
    let parentsOf (graph: DagGraph) : string -> string list =
        let byHash = graph.Nodes |> List.map (fun n -> n.Hash, n.Parents) |> Map.ofList

        fun h ->
            match Map.tryFind h byHash with
            | Some ps -> ps
            | None -> []

    /// Build the overlay for `graph` under the GC `roots`: per-node precedence
    /// (via the host's `isPrimary` predicate) + retention state, plus the
    /// live/prunable partition.
    let build (isPrimary: DagGraphNode -> bool) (graph: DagGraph) (roots: GcRoots) : DagOverlay =
        let getParents = parentsOf graph
        let live = DagRetention.liveSet getParents roots

        let allHashes = graph.Nodes |> List.map _.Hash
        let prunable = DagRetention.prunable allHashes getParents roots

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
          Prunable = prunable }

    /// The `(nodeId, facet)` cells whose most-recent writer on `head`'s primary
    /// spine (walking back to `stopAt`, exclusive, or genesis) was classified
    /// `Primary` by `recordAuthor` — the primacy-pinned cells the overlay
    /// highlights. Wraps `DagPrimacy.cellAuthors` and keeps only the
    /// `Primary`-classified cells.
    let primaryPinnedCells<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (getRecord: string -> DagOpRecord<'Msg> option)
        (stopAt: string option)
        (head: string)
        : Set<string * string> =
        DagPrimacy.cellAuthors recordAuthor getRecord stopAt head
        |> Map.toSeq
        |> Seq.choose (fun (cell, author) ->
            match author with
            | MergeAuthor.Primary -> Some cell
            | MergeAuthor.Secondary _ -> None)
        |> Set.ofSeq
