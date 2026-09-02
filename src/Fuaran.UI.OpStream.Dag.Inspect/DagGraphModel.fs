namespace Fuaran.UI.OpStream.Dag.Inspect

open System.Collections.Generic
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
//  Edges carry `IsPrimary` (the parent is `Parents[0]`, the replay spine) so a
//  renderer can draw the primary spine solid and secondary merge parents dashed.
//
//  Pure over `DagOpRecord` + the parent relation only — no apply engine, no I/O.
//  A sink builds the input with one `Records streamId` call; `DagTopology`'s
//  `parents`/`reachable`/`lca` are available on the same sink for the renderer's
//  interactive queries (ancestor highlight, merge-base of two coordinates).
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
/// position in the fork/merge forest; `Tombstoned` / `OutcomeHash` / `PromptId`
/// carry the provenance the overlay (`DagOverlay`) annotates further.
type DagGraphNode =
    { Hash: string
      Parents: string list
      Role: DagNodeRole
      Depth: int
      ChildCount: int
      IsBranchPoint: bool
      IsLeaf: bool
      Tombstoned: bool
      OutcomeHash: string option
      PromptId: string option
      Actor: Actor
      Timestamp: System.DateTimeOffset }

/// A parent→child link. `IsPrimary` marks the replay-spine edge (the parent is
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

module DagGraphModel =

    let private roleOf (parents: string list) : DagNodeRole =
        match parents with
        | [] -> DagNodeRole.Genesis
        | [ _ ] -> DagNodeRole.Linear
        | _ -> DagNodeRole.Merge

    /// Build the renderable graph from a stream's records. The DAG is finite +
    /// acyclic (content addressing folds parents into the hash, so a node can
    /// never be its own ancestor), so the depth recursion terminates. A parent
    /// hash with no record (e.g. pruned beyond the supplied set) contributes
    /// depth 0 and no node — the graph renders what it holds.
    let build<'Msg> (streamId: string) (records: DagOpRecord<'Msg> list) : DagGraph =
        let byHash = Dictionary<string, DagOpRecord<'Msg>>()

        for r in records do
            byHash[r.Hash] <- r

        // Child count per hash — drives IsBranchPoint / IsLeaf.
        let childCount = Dictionary<string, int>()

        for r in records do
            for p in r.Parents do
                childCount[p] <-
                    (match childCount.TryGetValue p with
                     | true, c -> c + 1
                     | false, _ -> 1)

        // Longest path from a root. Memoised; a missing parent bottoms out at -1
        // so a present node with an absent parent still lands at depth 0.
        let depthMemo = Dictionary<string, int>()

        let rec depth (hash: string) : int =
            match depthMemo.TryGetValue hash with
            | true, d -> d
            | false, _ ->
                let d =
                    match byHash.TryGetValue hash with
                    | false, _ -> -1
                    | true, r ->
                        match r.Parents with
                        | [] -> 0
                        | parents -> 1 + (parents |> List.map depth |> List.max)

                depthMemo[hash] <- d
                d

        let nodes =
            records
            |> List.map (fun r ->
                let children =
                    match childCount.TryGetValue r.Hash with
                    | true, c -> c
                    | false, _ -> 0

                { Hash = r.Hash
                  Parents = r.Parents
                  Role = roleOf r.Parents
                  Depth = depth r.Hash
                  ChildCount = children
                  IsBranchPoint = children >= 2
                  IsLeaf = children = 0
                  Tombstoned = r.Tombstoned
                  OutcomeHash = r.OutcomeHash
                  PromptId = r.PromptId
                  Actor = r.Actor
                  Timestamp = r.Timestamp })
            |> List.sortWith (fun a b ->
                if a.Depth <> b.Depth then
                    compare a.Depth b.Depth
                else
                    System.String.CompareOrdinal(a.Hash, b.Hash))

        let edges =
            records
            |> List.collect (fun r ->
                match r.Parents with
                | [] -> []
                | primary :: _ ->
                    r.Parents
                    |> List.map (fun p ->
                        { Child = r.Hash
                          Parent = p
                          IsPrimary = (p = primary) }))

        let roots =
            nodes |> List.filter (fun n -> n.Role = DagNodeRole.Genesis) |> List.map _.Hash

        let leaves = nodes |> List.filter _.IsLeaf |> List.map _.Hash

        { StreamId = streamId
          Nodes = nodes
          Edges = edges
          Roots = roots
          Leaves = leaves }

    /// Look up a built node by hash (renderer click-through / overlay join).
    let tryNode (graph: DagGraph) (hash: string) : DagGraphNode option =
        graph.Nodes |> List.tryFind (fun n -> n.Hash = hash)
