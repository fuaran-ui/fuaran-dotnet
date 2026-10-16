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

    /// Lowest-common-ancestor of `a` and `b`: the MAXIMAL common ancestors —
    /// a common ancestor `c` is dominated when some other common ancestor is
    /// its descendant. Exactly one ⇒ `Unique`; none ⇒ `None`; two-or-more
    /// (criss-cross) ⇒ `Ambiguous`, sorted.
    ///
    /// A two-colour paint down from both heads (Phase 2062). Every node reached
    /// from `a` carries colour A, from `b` colour B; a node carrying both is a
    /// common ancestor, so it is a candidate and everything below it is painted
    /// STALE (an ancestor of a common ancestor is never maximal). The walk stops
    /// once no entry still queued was queued live, so it visits the divergence
    /// and the frontier just below the base, not the shared history beneath it.
    /// It used to build the full ancestor set of every common node, one per
    /// node, which on a long shared history was quadratic in its length.
    ///
    /// Without generation numbers a breadth-first paint can meet a common node
    /// BELOW another before the higher one, so a candidate may be an ancestor
    /// of a later one; candidates are reduced to the maximal ones last. A
    /// maximal common ancestor is always found: every node on a path from
    /// either head down to it is live (a stale one would make it an ancestor of
    /// a common node), so both colours reach it.
    let lca (getParents: string -> string list) (a: string) (b: string) : LcaResult =
        let colourA = 1
        let colourB = 2
        let stale = 4
        let flags = Dictionary<string, int>()
        // FIFO of (hash, flags-at-push); `live` counts entries pushed non-stale
        // and not yet popped.
        let queue = ResizeArray<string * int>()
        let mutable next = 0
        let mutable live = 0

        let paint (h: string) (f: int) =
            let current =
                match flags.TryGetValue h with
                | true, v -> v
                | false, _ -> 0

            if current &&& f <> f then
                let painted = current ||| f
                flags[h] <- painted
                queue.Add((h, painted))

                if painted &&& stale = 0 then
                    live <- live + 1

        paint a colourA
        paint b colourB
        let candidates = ResizeArray<string>()

        while live > 0 do
            let h, pushed = queue[next]
            next <- next + 1

            if pushed &&& stale = 0 then
                live <- live - 1

            let mutable f = flags[h] &&& (colourA ||| colourB ||| stale)

            if f = (colourA ||| colourB) then
                // Marked stale on itself too: a node met by both colours is
                // queued once per colour, and its second entry must not make it
                // a candidate twice.
                candidates.Add h
                f <- f ||| stale
                flags[h] <- flags[h] ||| stale

            for p in getParents h do
                paint p f

        // Drop a candidate that is an ancestor of another (met below it first).
        // Only a criss-cross pays for this; a unique base never does.
        let maximal =
            if candidates.Count < 2 then
                List.ofSeq candidates
            else
                let closures = [ for c in candidates -> c, reachable getParents c ]

                candidates
                |> Seq.filter (fun c -> not (closures |> List.exists (fun (d, rd) -> d <> c && rd.Contains c)))
                |> List.ofSeq

        match maximal with
        | [] -> LcaResult.None
        | [ single ] -> LcaResult.Unique single
        | many -> LcaResult.Ambiguous(List.sort many)
