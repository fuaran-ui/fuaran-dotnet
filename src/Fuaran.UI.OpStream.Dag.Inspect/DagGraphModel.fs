namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagGraphModel — the renderable projection of the op-record DAG (Phase 186).
//
//  The op-stream went from a linear hash-chain to a branching Merkle-DAG (Phase
//  178/179); this is the read-only view that lets a human *see* that forest —
//  the "variation forest" surface. It is a pure, derived projection: it reads
//  the records + the topology relation (`parents`) and emits a layered graph of
//  nodes + edges classified for rendering. It writes nothing back — the stream
//  stays the source of truth and this is a derived view of it (FGP 5).
//
//  Classification:
//   - `Role` is the node's own shape by parent count: `Genesis` (0 parents, a
//     root), `Linear` (1 parent, an ordinary step), `Merge` (≥2 parents).
//   - `IsBranchPoint` is a node with ≥2 *children* — the fork from which the
//     DAG splits into auditionable variations.
//   - `IsLeaf` is a node that is no node's parent — a live branch tip (a GC
//     root together with the trunk head / checkpoints).
//   - `Depth` is the longest path from any root, so a merge node sits strictly
//     below both of its parents and the layered layout reads top-to-bottom.
//
//  Edges carry `IsPrimary` (the parent is `Parents[0]`, the first head named) so a
//  renderer can draw the primary spine solid and secondary merge parents dashed.
//
//  Pure over the nodes + the parent relation only — no apply engine, no I/O.
//  A sink builds the input with one `Records streamId` call and one `Skeleton
//  streamId` call (Phase 2645): a pruned node is drawn from its skeleton, its
//  links intact and its payload absent. Depth is Core's `Dag.ranks` (the longest
//  path from a node with no held parent), and a cycle is Core's `Cyclic` answer;
//  the renderer's interactive queries (ancestor highlight, merge base of two
//  coordinates) are Core's `Dag.ancestorsOf` / `Dag.mergeBase`.
// ============================================================================

/// A node's shape by parent count — the branch/merge classification the
/// renderer draws differently. `Genesis` is a root (no parents); `Linear` is an
/// ordinary one-parent step; `Merge` folds two-or-more branches back together.
[<RequireQualifiedAccess>]
type DagNodeRole =
    | Genesis
    | Linear
    | Merge

/// One renderable DAG node — a `DagOpRecord`'s topology + provenance projected
/// for display. `Depth` is the longest-path layer from a root (top-to-bottom
/// layout); `ChildCount` / `IsBranchPoint` / `IsLeaf` describe the node's
/// position in the fork/merge forest. `Tombstoned` marks a pruned node drawn
/// from the sink's skeleton, whose payload — and so its `PromptId`, `Actor` and
/// `Timestamp` — is gone (`None`); a live node carries all three.
type DagGraphNode =
    { Hash: string
      Parents: string list
      Role: DagNodeRole
      Depth: int
      ChildCount: int
      IsBranchPoint: bool
      IsLeaf: bool
      Tombstoned: bool
      PromptId: string option
      Actor: Actor option
      Timestamp: System.DateTimeOffset option }

/// A parent→child link. `IsPrimary` marks the primary edge (the parent is
/// the child's `Parents[0]`), so a renderer can draw the primary parent solid
/// and the secondary merge parent(s) dashed.
type DagGraphEdge =
    { Child: string
      Parent: string
      IsPrimary: bool }

/// The full renderable graph for one stream. `Nodes` are in deterministic order
/// (by `Depth` then `Hash`) for stable rendering + snapshot tests; `Roots` are
/// the genesis nodes; `Leaves` are the live branch tips.
type DagGraph =
    { StreamId: string
      Nodes: DagGraphNode list
      Edges: DagGraphEdge list
      Roots: string list
      Leaves: string list }

/// A cyclic record set — a graph in which some node is its own ancestor, which
/// no layered layout can assign a depth to (Phase 1525). `Unresolved` names every
/// node the topological sweep could not settle, in Ordinal hash order: the cycle
/// members and everything downstream of them. It deliberately does not claim to
/// have isolated the cycle itself — "these are the nodes I could not place" is
/// what the sweep actually knows, and inventing a single culprit from it would be
/// a guess presented as a finding.
type DagGraphCycle =
    { StreamId: string
      Unresolved: string list }

module DagGraphCycle =
    /// Human-readable rendering — the shape a caller with no `Result` channel
    /// refuses by (`DagGraphModel.build`), matching `DagVerify.describe`.
    let describe (cycle: DagGraphCycle) : string =
        "DagGraphModel: the record set for stream '"
        + cycle.StreamId
        + "' is CYCLIC — "
        + string (List.length cycle.Unresolved)
        + " node(s) could not be assigned a depth because each is reachable from itself: "
        + String.concat ", " cycle.Unresolved

module DagGraphModel =

    let private roleOf (parents: string list) : DagNodeRole =
        match parents with
        | [] -> DagNodeRole.Genesis
        | [ _ ] -> DagNodeRole.Linear
        | _ -> DagNodeRole.Merge

    /// Build the renderable graph from a stream's live records and its pruned
    /// skeleton, or report the CYCLE that makes a layered layout meaningless
    /// (Phase 1525). A record list can carry a cycle (a hand-built, replicated or
    /// edited list); Core's `Dag.ranks` answers it as `Cyclic`, naming every node
    /// the drain could not place, and that is the refusal.
    let tryBuildWith<'Msg>
        (streamId: string)
        (records: DagOpRecord<'Msg> list)
        (skeleton: DagSkeletonNode list)
        : Result<DagGraph, DagGraphCycle> =
        // The topology alone, as Core nodes: a live node's links and a pruned node's.
        let topology =
            (records |> List.map (fun r -> r.Hash, r.Parents))
            @ (skeleton |> List.map (fun k -> k.Hash, k.Parents))
            |> List.map (fun (h, ps) ->
                h,
                ({ Id = h
                   Parents = ps
                   Actor = Actor.ofLegacyString ""
                   Op = () }
                : Fuaran.Core.DagNode<unit>))
            |> Map.ofList

        let dag =
            match Fuaran.Core.Dag.ofNodes topology with
            | Ok d -> d
            | Error m -> invalidOp ("DagGraphModel: node filed under another id: " + m.Key)

        match Fuaran.Core.Dag.ranks dag with
        | Error cyclic ->
            Error
                { StreamId = streamId
                  Unresolved = cyclic.Unplaced }
        | Ok ranks ->
            let childCount =
                topology
                |> Map.toList
                |> List.collect (fun (_, n) -> n.Parents)
                |> List.countBy id
                |> Map.ofList

            let node (hash: string) (parents: string list) (tombstoned: bool) (live: DagOpRecord<'Msg> option) =
                let children = childCount |> Map.tryFind hash |> Option.defaultValue 0

                { Hash = hash
                  Parents = parents
                  Role = roleOf parents
                  Depth = ranks[hash]
                  ChildCount = children
                  IsBranchPoint = children >= 2
                  IsLeaf = children = 0
                  Tombstoned = tombstoned
                  PromptId = live |> Option.bind _.PromptId
                  Actor = live |> Option.map _.Actor
                  Timestamp = live |> Option.map _.Timestamp }

            let nodes =
                (records |> List.map (fun r -> node r.Hash r.Parents false (Some r)))
                @ (skeleton |> List.map (fun k -> node k.Hash k.Parents true None))
                |> List.distinctBy _.Hash
                |> List.sortWith (fun a b ->
                    if a.Depth <> b.Depth then
                        compare a.Depth b.Depth
                    else
                        System.String.CompareOrdinal(a.Hash, b.Hash))

            let edges =
                nodes
                |> List.collect (fun n ->
                    match n.Parents with
                    | [] -> []
                    | primary :: _ ->
                        n.Parents
                        |> List.map (fun p ->
                            { Child = n.Hash
                              Parent = p
                              IsPrimary = (p = primary) }))

            let roots =
                nodes |> List.filter (fun n -> n.Role = DagNodeRole.Genesis) |> List.map _.Hash

            let leaves = nodes |> List.filter _.IsLeaf |> List.map _.Hash

            Ok
                { StreamId = streamId
                  Nodes = nodes
                  Edges = edges
                  Roots = roots
                  Leaves = leaves }

    /// `tryBuildWith` over live records only.
    let tryBuild<'Msg> (streamId: string) (records: DagOpRecord<'Msg> list) : Result<DagGraph, DagGraphCycle> =
        tryBuildWith streamId records []

    /// `tryBuild`, refusing BY NAME on a cyclic input — the idiom the DAG sinks
    /// use for a broken store, for the same reason: this returns a bare `DagGraph`
    /// and has no error channel to report into.
    ///
    /// The refusal is not a new failure mode. A cyclic input previously took the
    /// whole process down inside the depth recursion, with nothing said about
    /// which nodes were involved; a caller that wants to handle the case rather
    /// than be told about it calls `tryBuild`.
    let build<'Msg> (streamId: string) (records: DagOpRecord<'Msg> list) : DagGraph =
        match tryBuild streamId records with
        | Ok graph -> graph
        | Error cycle -> invalidOp (DagGraphCycle.describe cycle)

    /// Look up a built node by hash (renderer click-through / overlay join).
    let tryNode (graph: DagGraph) (hash: string) : DagGraphNode option =
        graph.Nodes |> List.tryFind (fun n -> n.Hash = hash)
