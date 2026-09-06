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

    /// Build the renderable graph from a stream's records, or report the CYCLE
    /// that makes a layered layout meaningless (Phase 1525).
    ///
    /// A well-formed DAG is acyclic by construction — a content address folds its
    /// parents in, so a node can never be its own ancestor — and the depth pass
    /// used to rely on that being true of its INPUT. That is not the same claim.
    /// This function is handed a record LIST, and a hand-built, replicated or
    /// out-of-band-edited list can carry a cycle whose members are each perfectly
    /// well-formed. The old pass recursed parent-wards with a memo written only
    /// on the way out, so a cycle recursed until the stack ran out — a
    /// `StackOverflowException`, which .NET does not let anyone catch, raised
    /// from a pure projection function.
    ///
    /// The depths are unchanged: `Depth` is still the longest path from a root,
    /// with a parent hash naming no record contributing -1, so a present node
    /// with an absent parent still lands at 0. What changed is how they are
    /// computed — one Kahn topological sweep, O(V+E), each node's depth settled
    /// once its last present parent resolves — and what happens when they cannot
    /// be: every node still unresolved when the queue drains is on or behind a
    /// cycle, and all of them are named.
    let tryBuild<'Msg> (streamId: string) (records: DagOpRecord<'Msg> list) : Result<DagGraph, DagGraphCycle> =
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

        // ── Kahn's algorithm over the parent→child relation ──────────────────
        //
        // `pending` counts a node's parents PRESENT in this set — only those can
        // ever resolve, so a node whose parents are all absent is ready at once.
        // `best` accumulates the running max over resolved parent depths, seeded
        // at -1 for a node with at least one absent parent (the "absent
        // contributes -1" rule) and left unseeded otherwise.
        let pending = Dictionary<string, int>()
        let best = Dictionary<string, int>()
        let children = Dictionary<string, ResizeArray<string>>()
        let depthOf = Dictionary<string, int>()

        for r in records do
            let presentParents = r.Parents |> List.filter byHash.ContainsKey
            pending[r.Hash] <- List.length presentParents

            if List.length presentParents <> List.length r.Parents then
                best[r.Hash] <- -1

            for p in presentParents do
                match children.TryGetValue p with
                | true, cs -> cs.Add r.Hash
                | false, _ ->
                    let cs = ResizeArray<string>()
                    cs.Add r.Hash
                    children[p] <- cs

        let ready = Queue<string>()

        // Seeded in the input's own order, so the sweep is deterministic for a
        // deterministic input. The node order of the RESULT is sorted below
        // regardless; this keeps a cycle report stable too.
        for r in records do
            if pending[r.Hash] = 0 then
                ready.Enqueue r.Hash

        while ready.Count > 0 do
            let hash = ready.Dequeue()

            let d =
                match byHash[hash].Parents with
                | [] -> 0
                | _ ->
                    match best.TryGetValue hash with
                    | true, b -> 1 + b
                    | false, _ -> 0

            depthOf[hash] <- d

            match children.TryGetValue hash with
            | false, _ -> ()
            | true, cs ->
                for child in cs do
                    best[child] <-
                        (match best.TryGetValue child with
                         | true, b -> max b d
                         | false, _ -> d)

                    pending[child] <- pending[child] - 1

                    if pending[child] = 0 then
                        ready.Enqueue child

        let unresolved =
            records
            |> List.map _.Hash
            |> List.distinct
            |> List.filter (depthOf.ContainsKey >> not)
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

        if not (List.isEmpty unresolved) then
            Error
                { StreamId = streamId
                  Unresolved = unresolved }
        else

            let depth (hash: string) : int =
                match depthOf.TryGetValue hash with
                | true, d -> d
                | false, _ -> -1

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

            Ok
                { StreamId = streamId
                  Nodes = nodes
                  Edges = edges
                  Roots = roots
                  Leaves = leaves }

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
