namespace Fuaran.UI.OpStream.Dag.Abstractions

open System.Collections.Generic

// ============================================================================
//  DagTopology — pure graph queries over the op-record DAG.
//
//  Every query is parameterised by a `getParents : hash -> string list`
//  lookup, so the same algorithms serve both concrete sinks (in-memory ref,
//  Sqlite) and the merge engine without coupling to storage. The DAG is finite
//  and acyclic (content addressing forbids cycles: a node's hash folds in its
//  parents' hashes, so a node can never be its own ancestor), so every walk
//  terminates.
//
//  `reachable` is the ancestor closure (a node is reachable from itself). `lca`
//  is the lowest-common-ancestor over two nodes — the load-bearing query M1
//  merge routes through. M1 guarantees a UNIQUE base or none (fast-forward, or
//  disjoint branches off one point); the multiple-LCA / criss-cross case is
//  out of scope here and surfaces as `LcaResult.Ambiguous` so the caller can
//  refuse with "needs 3-way merge — Phase 179".
// ============================================================================

/// Outcome of `DagTopology.lca`. `Unique` is the only case M1 merge proceeds
/// on; `None` (disjoint roots) means no fast-forward / merge base exists, and
/// `Ambiguous` (≥2 maximal common ancestors — a criss-cross history) is the
/// Phase-179 3-way-merge territory this foundation rung explicitly defers.
[<RequireQualifiedAccess>]
type LcaResult =
    | None
    | Unique of hash: string
    | Ambiguous of candidates: string list

module DagTopology =

    /// Ancestor closure of `start` (inclusive of `start` itself), following
    /// `getParents`. Iterative worklist so deep chains don't blow the stack.
    let reachable (getParents: string -> string list) (start: string) : Set<string> =
        let seen = HashSet<string>()
        let stack = Stack<string>()
        stack.Push start

        while stack.Count > 0 do
            let h = stack.Pop()

            if seen.Add h then
                for p in getParents h do
                    if not (seen.Contains p) then
                        stack.Push p

        Set.ofSeq seen

    /// `true` when `ancestor` is reachable from `descendant` (i.e. `ancestor`
    /// is `descendant` itself or one of its transitive parents). A
    /// fast-forward check: `headA` is an ancestor of `headB` ⇒ merging them is
    /// trivially `headB`.
    let isAncestor (getParents: string -> string list) (ancestor: string) (descendant: string) : bool =
        (reachable getParents descendant).Contains ancestor

    /// Lowest-common-ancestor of `a` and `b`. Computes the common ancestor set,
    /// then keeps only its *maximal* (lowest) elements — a common ancestor `c`
    /// is dominated when some other common ancestor is its descendant. Exactly
    /// one survivor ⇒ `Unique`; none ⇒ `None`; two-or-more (criss-cross) ⇒
    /// `Ambiguous`.
    let lca (getParents: string -> string list) (a: string) (b: string) : LcaResult =
        // Memoise ancestor closures for the duration of this lca. The dominance
        // test below probes reachable(d) for every common node d (inside a filter
        // over the same set) — O(|common|²) full graph walks, recomputed. One
        // closure per distinct hash, cached, makes it O(|common|) walks. The
        // memo is transparent: `reachable` is pure given a stable `getParents`.
        let cache = Dictionary<string, Set<string>>()

        let reachableMemo (h: string) : Set<string> =
            match cache.TryGetValue h with
            | true, v -> v
            | false, _ ->
                let v = reachable getParents h
                cache[h] <- v
                v

        let ra = reachableMemo a
        let rb = reachableMemo b
        let common = Set.intersect ra rb

        if Set.isEmpty common then
            LcaResult.None
        else
            // c is an LCA iff no *other* common node has c among its proper
            // ancestors (i.e. c is not an ancestor of another common node).
            let isDominated (c: string) =
                common |> Set.exists (fun d -> d <> c && (reachableMemo d).Contains c)

            let maximal = common |> Set.filter (fun c -> not (isDominated c)) |> Set.toList

            match maximal with
            | [] -> LcaResult.None
            | [ single ] -> LcaResult.Unique single
            | many -> LcaResult.Ambiguous(List.sort many)
