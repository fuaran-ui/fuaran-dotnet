namespace Fuaran.UI.OpStream.Dag.Merge

open System
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagMerge — the M1+M2 merge orchestration.
//
//  `merge` resolves the LCA, then:
//
//   - Ambiguous LCA (criss-cross) ⇒ **recursive-base merge** (M2) over a
//     synthetic virtual-ancestor tree — no longer deferred (that was the M1
//     boundary).
//   - No common base ⇒ `NoCommonBase`.
//   - One head is the LCA (an ancestor of the other) ⇒ `FastForward` to the
//     descendant head — no new node.
//   - Unique base, neither head an ancestor ⇒ replay base / headA / headB and
//     run the facet-refined 3-way merge under host-classified per-side
//     authorship. Auto-resolved ⇒ `Merged` carrying a freshly built **merge
//     node** (committed to the OUTCOME tree hash, not the op-path); conflicting
//     ⇒ `NeedsManualMerge` with the rich three-up `MergeConflict` envelopes
//     (KeepPrimary first under precedence).
//
//  The merge node's primary parent is `headA` (author order `[headA; headB]`)
//  and its `Op` is the replay delta from `treeA` to the merged tree, so the
//  spine replay through the new node reconstructs the merged tree. The engine
//  BUILDS the record but does not commit it — the caller adds it and advances
//  the trunk head under the `TryAdvanceHead` CAS (the transactional boundary).
// ============================================================================

/// Outcome of an M1 merge attempt.
[<RequireQualifiedAccess>]
type MergeResult<'Msg> =
    /// The two heads are identical — nothing to merge.
    | AlreadyMerged of head: string
    /// One head is an ancestor of the other; advance the trunk to `resultHead`
    /// (the descendant). No new node.
    | FastForward of resultHead: string
    /// Disjoint auto-merge succeeded. `record` is the new merge node (add it,
    /// then `TryAdvanceHead` to `record.Hash`); `tree` is the merged tree.
    | Merged of record: DagOpRecord<'Msg> * tree: Node<'Msg>
    /// Overlapping change — the contended `(NodeId, facet)` cells (M2 / 179).
    | NeedsManualMerge of contended: MergeConflict list
    /// Criss-cross history (≥2 LCAs) — recursive-base 3-way merge (Phase 179).
    | NeedsThreeWayMerge of candidates: string list
    /// The two heads share no common ancestor.
    | NoCommonBase
    /// A base / branch tree could not be replayed.
    | ReplayFailed of DagReplayError

/// The outcome of a validator-GATED merge (Phase 184). `Result` is the ordinary
/// `MergeResult` (so existing callers' pattern-matches are unchanged); on a
/// gated-on refusal it is a `NeedsManualMerge` carrying the lifted
/// `CombinedCycle` conflicts. `Diagnostics` carries the merge-INTRODUCED
/// defects: on a refusal they are also the source of the conflicts; on a
/// gated-OFF proceed they are the post-merge diagnostic accompanying a clean
/// `Merged`. Empty when no validator ran or the merge introduced nothing.
type GatedMergeOutcome<'Msg> =
    { Result: MergeResult<'Msg>
      Diagnostics: MergeDefect list }

module DagMerge =

    /// The precedence class of a branch tip, as classified by the host-supplied
    /// `recordAuthor` (the merge layer reads no record field to decide it). This
    /// tip classification is the FALLBACK author; the merge resolves precedence
    /// PER CELL via the backward DAG walk (`DagPrimacy.cellAuthorFn`), using this
    /// tip only for cells the walk did not attribute.
    let private authorOf<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (getRec: string -> DagOpRecord<'Msg> option)
        (hash: string)
        : MergeAuthor =
        match getRec hash with
        | Some r -> recordAuthor r
        | None -> MergeAuthor.Secondary None

    /// Build the merge node + outcome hash for a successful merged tree.
    let private buildMergeRecord<'Msg>
        (streamId: string)
        (headA: string)
        (headB: string)
        (treeA: Node<'Msg>)
        (merged: Node<'Msg>)
        (now: DateTimeOffset)
        : DagOpRecord<'Msg> =
        let outcomeHash = CanonicalJson.encodeNode merged |> HashChain.sha256Hex
        let delta = TreeOpDiff.diffBatched treeA merged

        let deltaOp =
            match delta with
            | [] -> TreeOp.Batch []
            | [ single ] -> single
            | many -> TreeOp.Batch many

        DagOpRecord.createMerge streamId [ headA; headB ] deltaOp outcomeHash None "merge" now OpResultEnvelope.Success

    /// The synthetic virtual-ancestor TREE for a criss-cross (multiple-LCA)
    /// history — git's recursive-base merge. The candidate bases are sorted
    /// (determinism), then folded: each consecutive pair is merged LENIENTLY
    /// (conflicts resolved to base — a virtual base never blocks) over their own
    /// LCA. The result is a deterministic, host-reproducible synthetic base the
    /// real (conflict-surfacing) merge then runs against.
    let private virtualAncestorTree<'Msg>
        (getRec: string -> DagOpRecord<'Msg> option)
        (getParents: string -> string list)
        (initial: Node<'Msg>)
        (candidates: string list)
        : Result<Node<'Msg>, DagReplayError> =
        let replay h = DagReplay.replay getRec initial h

        let rec ancestorOf (a: string) (b: string) : Result<Node<'Msg>, DagReplayError> =
            match DagTopology.lca getParents a b with
            | LcaResult.Unique bse when bse = a -> replay a
            | LcaResult.Unique bse when bse = b -> replay b
            | LcaResult.Unique bse ->
                match replay bse, replay a, replay b with
                | Ok bt, Ok at, Ok bbt -> Ok(TreeMerge.merge3WayLenient bt at bbt)
                | Error e, _, _
                | _, Error e, _
                | _, _, Error e -> Error e
            | _ -> replay a // no/ambiguous deeper base — deterministic fallback

        match List.sort candidates with
        | [] -> Ok initial
        | [ single ] -> replay single
        | first :: rest ->
            (replay first, first)
            |> fun seed ->
                rest
                |> List.fold
                    (fun (acc: Result<Node<'Msg>, DagReplayError> * string) (c: string) ->
                        let accTree, prev = acc

                        match accTree with
                        | Error e -> Error e, c
                        | Ok at ->
                            match ancestorOf prev c, replay c with
                            | Ok baseTree, Ok ct -> Ok(TreeMerge.merge3WayLenient baseTree at ct), c
                            | Error e, _
                            | _, Error e -> Error e, c)
                    seed
            |> fst

    /// Wrap a `MergeResult` with no diagnostics (the non-gated outcome shape).
    let private plain<'Msg> (result: MergeResult<'Msg>) : GatedMergeOutcome<'Msg> =
        { Result = result; Diagnostics = [] }

    /// Merge two heads over a (real or synthetic) `baseTree`, threading PER-CELL
    /// authorship (`cellAuthor nodeId facet` = each side's last writer of that
    /// cell). Disjoint/auto-resolved ⇒ `Merged`; conflicting ⇒ `NeedsManualMerge`
    /// with the rich three-up envelopes.
    ///
    /// When `policy.Validator` is set, a structurally-clean merge is then
    /// VALIDATED (Phase 184): defects the merge INTRODUCED (present in the merged
    /// tree but in neither parent) refuse the merge under `GateOnIntroducedDefect`
    /// (lifted into `CombinedCycle` conflicts), or — gating off — ride along as
    /// `Diagnostics` on a clean `Merged`.
    let private mergeOver<'Msg>
        (streamId: string)
        (headA: string)
        (headB: string)
        (cellAuthor: string -> string -> MergeAuthor * MergeAuthor)
        (policy: MergePolicy<'Msg>)
        (baseTree: Node<'Msg>)
        (treeA: Node<'Msg>)
        (treeB: Node<'Msg>)
        (now: DateTimeOffset)
        : GatedMergeOutcome<'Msg> =
        match TreeMerge.merge3WayWithCellAuthor cellAuthor baseTree treeA treeB with
        | Error conflicts -> plain (MergeResult.NeedsManualMerge conflicts)
        | Ok merged ->
            let record = buildMergeRecord streamId headA headB treeA merged now

            match policy.Validator with
            | None -> plain (MergeResult.Merged(record, merged))
            | Some validator ->
                match ValidatorGate.introducedDefects validator treeA treeB merged with
                | [] -> plain (MergeResult.Merged(record, merged))
                | defects when policy.GateOnIntroducedDefect ->
                    // A merge-introduced defect is a SEMANTIC conflict — refuse,
                    // naming the offending nodes + enumerated recovery.
                    { Result = MergeResult.NeedsManualMerge(defects |> List.map ValidatorGate.toConflict)
                      Diagnostics = defects }
                | defects ->
                    // Gating off — the clean structural merge proceeds; the
                    // introduced defects are surfaced as a post-merge diagnostic.
                    { Result = MergeResult.Merged(record, merged)
                      Diagnostics = defects }

    /// Core M1+M2 merge under a `MergePolicy` (Phase 184), returning the rich
    /// `GatedMergeOutcome`. `merge` / `mergeGated` are the public faces.
    let private mergeImpl<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        (policy: MergePolicy<'Msg>)
        : Async<GatedMergeOutcome<'Msg>> =
        async {
            if headA = headB then
                return plain (MergeResult.AlreadyMerged headA)
            else
                let! records = sink.Records streamId
                let recMap = records |> List.map (fun r -> r.Hash, r) |> Map.ofList
                let getRec h = Map.tryFind h recMap

                let getParents h =
                    getRec h |> Option.map _.Parents |> Option.defaultValue []

                let tipA = authorOf recordAuthor getRec headA
                let tipB = authorOf recordAuthor getRec headB

                let! lca = sink.Lca(streamId, headA, headB)

                // Per-cell authorship: the last writer of each cell on each
                // branch since `stopAt` (the base), falling back to the branch
                // tip for cells the backward walk did not attribute.
                let divergent (stopAt: string option) (baseTreeR: Result<Node<'Msg>, DagReplayError>) =
                    match baseTreeR, DagReplay.replay getRec initial headA, DagReplay.replay getRec initial headB with
                    | Error e, _, _
                    | _, Error e, _
                    | _, _, Error e -> plain (MergeResult.ReplayFailed e)
                    | Ok baseTree, Ok treeA, Ok treeB ->
                        let authA = DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headA tipA
                        let authB = DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headB tipB
                        let cellAuthor (nodeId: string) (facet: string) = authA nodeId facet, authB nodeId facet
                        mergeOver streamId headA headB cellAuthor policy baseTree treeA treeB now

                match lca with
                | LcaResult.None -> return plain MergeResult.NoCommonBase
                | LcaResult.Ambiguous candidates ->
                    // M2: recursive-base merge over the synthetic virtual ancestor.
                    // No single base hash — the per-cell walk runs to genesis.
                    return divergent None (virtualAncestorTree getRec getParents initial candidates)
                | LcaResult.Unique baseHash ->
                    if baseHash = headA then
                        return plain (MergeResult.FastForward headB)
                    elif baseHash = headB then
                        return plain (MergeResult.FastForward headA)
                    else
                        return divergent (Some baseHash) (DagReplay.replay getRec initial baseHash)
        }

    /// Attempt an M1+M2 merge of `headA` and `headB` in `streamId`. Per-side
    /// precedence comes from the host-supplied `recordAuthor` classifier (the
    /// merge layer interprets no record field); under precedence a conflict lists
    /// `KeepPrimary` first. Criss-cross (multiple-LCA) histories are resolved by
    /// recursive-base merge (no longer deferred — that was the M1 boundary).
    /// `initial` is the genesis tree; `now` stamps a created merge node. No
    /// validator-gating (Phase 184) — use `mergeGated` for the semantic-conflict gate.
    let merge<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        : Async<MergeResult<'Msg>> =
        async {
            let! outcome = mergeImpl recordAuthor sink streamId initial headA headB now MergePolicy.lenient
            return outcome.Result
        }

    /// Validator-GATED merge (Phase 184): same M1+M2 merge, then the domain
    /// validator in `policy` runs over a structurally-clean result. A defect the
    /// merge INTRODUCED (in the merged tree but in neither parent) refuses the
    /// merge under `policy.GateOnIntroducedDefect` (`NeedsManualMerge` carrying
    /// `CombinedCycle` conflicts that name the offending nodes), or — gating off
    /// — rides along in `Diagnostics` on a clean `Merged`. A defect already
    /// present in a parent is carried through, never flagged. With
    /// `MergePolicy.lenient` this is exactly `merge` wrapped in a `GatedMergeOutcome`.
    let mergeGated<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        (policy: MergePolicy<'Msg>)
        : Async<GatedMergeOutcome<'Msg>> =
        mergeImpl recordAuthor sink streamId initial headA headB now policy

    /// Commit a `Merged` result: add the merge node, then CAS the trunk head
    /// from `expectedHead` to the merge node. Returns `true` on a clean commit,
    /// `false` if the CAS lost the race (the caller re-reads + re-merges).
    /// `FastForward` is committed the same way by the caller (advance to the
    /// descendant head); this helper covers the new-node case.
    let commitMerge<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (expectedHead: string option)
        (record: DagOpRecord<'Msg>)
        : Async<bool> =
        async {
            do! sink.Add record
            return! sink.TryAdvanceHead(streamId, expectedHead, record.Hash)
        }

    /// Peer-autonomous reconciliation (Phase 179, criterion E): merge `branchHead`
    /// into the current trunk and commit it under `tryAdvanceHead` CAS, RETRYING
    /// if a concurrent writer advanced the trunk first (the loser rebases —
    /// re-reads the new trunk, re-merges, re-commits). Correctness needs no
    /// locking: branches are contention-free; the trunk CAS is the only
    /// serialisation point, and the DAG retains every branch so there are no lost
    /// writes. Returns the final trunk head, or `None` if it gave up after
    /// `maxRetries` (or the merge surfaced a conflict / replay failure).
    let mergeIntoTrunk<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (branchHead: string)
        (now: DateTimeOffset)
        (maxRetries: int)
        : Async<string option> =
        let rec attempt (remaining: int) =
            async {
                let! trunkOpt = sink.Head streamId

                match trunkOpt with
                | None ->
                    // No trunk yet — fast-forward the branch to the trunk.
                    let! ok = sink.TryAdvanceHead(streamId, None, branchHead)

                    if ok then return Some branchHead
                    elif remaining > 0 then return! attempt (remaining - 1)
                    else return None
                | Some trunk when trunk = branchHead -> return Some trunk
                | Some trunk ->
                    let! result = merge recordAuthor sink streamId initial trunk branchHead now

                    match result with
                    | MergeResult.AlreadyMerged h -> return Some h
                    | MergeResult.FastForward h ->
                        let! ok = sink.TryAdvanceHead(streamId, Some trunk, h)

                        if ok then return Some h
                        elif remaining > 0 then return! attempt (remaining - 1)
                        else return None
                    | MergeResult.Merged(record, _) ->
                        let! ok = commitMerge sink streamId (Some trunk) record

                        if ok then return Some record.Hash
                        elif remaining > 0 then return! attempt (remaining - 1)
                        else return None
                    | _ -> return None // conflict / no-base / replay failure — caller resolves
            }

        attempt maxRetries
