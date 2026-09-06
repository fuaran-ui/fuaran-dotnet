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

/// Who a minted merge node is attributed to, and the telemetry context it
/// carries (Phase 1525).
///
/// A merge node is a real op-record on the stream: it has an author, a prompt may
/// be behind it, and it carries a result envelope like every other record. The
/// engine used to stamp all three itself — `Actor.ofLegacyString "merge"`, no
/// prompt id, a bare `Success` — so every merge in every stream was authored by
/// the same anonymous string and none could be traced to the session, operator or
/// agent that ran it. That is the provenance hole the content address was widened
/// to close for ordinary nodes (the actor has been INSIDE the digest since Phase
/// 1144), left open on the one node kind the engine mints itself.
///
/// The host supplies it now. `MergeAttribution.anonymous` IS the pre-1525
/// stamping, byte for byte — which is why the merge-conformance corpus does not
/// move under this change.
type MergeAttribution =
    {
        /// The typed author of the merge — the same `Human | Agent` every other
        /// record carries, folded into the merge node's content address.
        Actor: Actor
        /// The prompt id the host attributes this merge to, when a prompt drove
        /// it. `None` for a merge with no prompt behind it (a scheduled
        /// reconciliation, an operator's own action).
        ///
        /// Named `Prompt` and not after the record field it lands in, deliberately:
        /// the open-core invariant proves by source scan that this package derives
        /// NOTHING from that field, and the point of the scan is that the merge
        /// layer never reads a record's own provenance to decide anything. This
        /// carries a value the HOST supplies straight through to the node it mints
        /// — no record is read, and the token stays absent so the scan keeps
        /// meaning what it says.
        Prompt: string option
        /// The record's telemetry-bearing slot: how the merge REPORTED. A host that
        /// merges under a gate it wants recorded puts the verdict here rather than
        /// having the engine assert `Success` on its behalf.
        ResultEnvelope: OpResultEnvelope
    }

module MergeAttribution =
    /// The pre-1525 stamping, preserved exactly: the legacy `"merge"` actor, no
    /// prompt, `Success`. This is what `merge` / `mergeGated` / `mergeIntoTrunk`
    /// pass, so those entry points are unchanged in behaviour AND in the bytes
    /// they produce.
    let anonymous: MergeAttribution =
        { Actor = Actor.ofLegacyString "merge"
          Prompt = None
          ResultEnvelope = OpResultEnvelope.Success }

    /// Attribute a merge to `actor`, no prompt, `Success` — the common case for
    /// an operator-driven merge.
    let ofActor (actor: Actor) : MergeAttribution = { anonymous with Actor = actor }

/// The optional context a caller threads into a merge (Phase 1525) — ADDITIVE by
/// construction: `MergeContext.defaults` reproduces the pre-1525 behaviour of
/// every existing entry point, so `merge` / `mergeGated` / `mergeIntoTrunk` keep
/// both their signatures and their results.
///
/// A record rather than one overload per knob: both knobs here are "something the
/// host knows and the engine cannot derive", and a fresh overload for each would
/// multiply the entry points every time another appears.
type MergeContext<'Msg> =
    {
        /// Who the minted merge node is attributed to.
        Attribution: MergeAttribution
        /// A checkpoint to replay the branch trees FROM, when the host holds one.
        /// A merge replays three trees (base, headA, headB) and did so from genesis
        /// every time, so its cost tracked the length of the whole history rather
        /// than the length of the divergence. A checkpoint that bounds a head folds
        /// only the tail past it; one that does NOT bound a head falls back to a
        /// full replay for that head, because a checkpoint is an optimisation and a
        /// head it does not cover is not an error. A checkpoint whose snapshot
        /// fails its own position-bound hash is NOT fallen back from — that is
        /// tamper evidence and it propagates.
        Checkpoint: DagCheckpoint<'Msg> option
    }

module MergeContext =
    /// Anonymous attribution, no checkpoint — the pre-1525 behaviour exactly.
    let defaults<'Msg> : MergeContext<'Msg> =
        { Attribution = MergeAttribution.anonymous
          Checkpoint = None }

/// What a `mergeIntoTrunk` attempt actually did (Phase 1525).
///
/// The trunk merge used to answer `string option`: the new head, or `None`. Four
/// unrelated things collapsed into that `None` — a refused merge with a full
/// conflict envelope in hand, two heads with no common ancestor, a replay
/// failure, and simply running out of CAS retries — and a caller could not tell
/// which, let alone act on it. The conflicts in particular were already built:
/// `MergeConflict` carries the LCA value, both sides, the enumerated resolution
/// choices and an `ApplyHint`, all of it discarded one line before the caller
/// could see it.
[<RequireQualifiedAccess>]
type TrunkMergeOutcome<'Msg> =
    /// The trunk now points at this head (a fast-forward, a committed merge node,
    /// or a branch that was already the trunk).
    | Advanced of head: string
    /// The merge refused: these cells could not be auto-merged. The caller
    /// resolves them and re-merges.
    | Conflicted of contended: MergeConflict list
    /// Criss-cross history the recursive-base merge declined to resolve.
    | NeedsThreeWayMerge of candidates: string list
    /// The branch and the trunk share no common ancestor.
    | NoCommonBase
    /// A base or branch tree could not be replayed.
    | ReplayFailed of DagReplayError
    /// The trunk CAS lost its race `maxRetries` times over. Distinct from every
    /// case above: nothing is wrong, the caller was simply outrun, and retrying
    /// is the correct response.
    | Contended

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

    /// Build the merge node + outcome hash for a successful merged tree, under
    /// the caller's `attribution` (Phase 1525 — the actor, prompt id and result
    /// envelope were hard-coded here before).
    let private buildMergeRecord<'Msg>
        (streamId: string)
        (headA: string)
        (headB: string)
        (treeA: Node<'Msg>)
        (merged: Node<'Msg>)
        (now: DateTimeOffset)
        (attribution: MergeAttribution)
        : DagOpRecord<'Msg> =
        let outcomeHash = CanonicalJson.encodeNode merged |> HashChain.sha256Hex
        let delta = TreeOpDiff.diffBatched treeA merged

        let deltaOp =
            match delta with
            | [] -> TreeOp.Batch []
            | [ single ] -> single
            | many -> TreeOp.Batch many

        DagOpRecord.createMerge
            streamId
            [ headA; headB ]
            deltaOp
            outcomeHash
            attribution.Prompt
            attribution.Actor
            now
            attribution.ResultEnvelope

    /// The synthetic virtual-ancestor TREE for a criss-cross (multiple-LCA)
    /// history — git's recursive-base merge. The candidate bases are sorted
    /// (determinism), then folded: each consecutive pair is merged LENIENTLY
    /// (conflicts resolved to base — a virtual base never blocks) over their own
    /// LCA. The result is a deterministic, host-reproducible synthetic base the
    /// real (conflict-surfacing) merge then runs against.
    let private virtualAncestorTree<'Msg>
        (replay: string -> Result<Node<'Msg>, DagReplayError>)
        (getParents: string -> string list)
        (initial: Node<'Msg>)
        (candidates: string list)
        : Result<Node<'Msg>, DagReplayError> =

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
        (attribution: MergeAttribution)
        : GatedMergeOutcome<'Msg> =
        match TreeMerge.merge3WayWithCellAuthor cellAuthor baseTree treeA treeB with
        | Error conflicts -> plain (MergeResult.NeedsManualMerge conflicts)
        | Ok merged ->
            let record = buildMergeRecord streamId headA headB treeA merged now attribution

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
        (context: MergeContext<'Msg>)
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

                // Checkpoint-bounded replay (Phase 1525). A merge folds three
                // trees, and every one of them used to be folded from GENESIS —
                // so the cost of merging two branches that diverged one op ago
                // was the length of the entire stream, three times over, on a
                // history a checkpoint had already summarised.
                //
                // A checkpoint that does NOT bound a head is not an error: its
                // `AtHash` simply is not on that head's primary spine, which
                // `replayFromCheckpoint` reports as `UnknownHash AtHash` (its
                // `collect` stops AT `AtHash` before any lookup, so that error can
                // arise no other way). Those fall back to a full replay. A
                // snapshot that fails its own position-bound hash does NOT fall
                // back — that is tamper evidence, and quietly replaying around it
                // would discard the one signal the check exists to raise.
                let replayAt (head: string) : Result<Node<'Msg>, DagReplayError> =
                    match context.Checkpoint with
                    | None -> DagReplay.replay getRec initial head
                    | Some checkpoint ->
                        match DagReplay.replayFromCheckpoint getRec checkpoint head with
                        | Ok tree -> Ok tree
                        | Error(DagReplayError.UnknownHash h) when h = checkpoint.AtHash ->
                            DagReplay.replay getRec initial head
                        | Error e -> Error e

                // The LCA is computed from the records ALREADY LOADED above
                // (Phase 1525). `sink.Lca` resolves parents over exactly the same
                // stream-scoped relation, so this is the same answer — it just no
                // longer re-reads and re-decodes the whole stream a second time to
                // reach it.
                let lca = DagTopology.lca getParents headA headB

                // Per-cell authorship: the last writer of each cell on each
                // branch since `stopAt` (the base), falling back to the branch
                // tip for cells the backward walk did not attribute.
                let divergent (stopAt: string option) (baseTreeR: Result<Node<'Msg>, DagReplayError>) =
                    match baseTreeR, replayAt headA, replayAt headB with
                    | Error e, _, _
                    | _, Error e, _
                    | _, _, Error e -> plain (MergeResult.ReplayFailed e)
                    | Ok baseTree, Ok treeA, Ok treeB ->
                        let authA = DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headA tipA
                        let authB = DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headB tipB
                        let cellAuthor (nodeId: string) (facet: string) = authA nodeId facet, authB nodeId facet

                        mergeOver streamId headA headB cellAuthor policy baseTree treeA treeB now context.Attribution

                match lca with
                | LcaResult.None -> return plain MergeResult.NoCommonBase
                | LcaResult.Ambiguous candidates ->
                    // M2: recursive-base merge over the synthetic virtual ancestor.
                    // No single base hash — the per-cell walk runs to genesis.
                    return divergent None (virtualAncestorTree replayAt getParents initial candidates)
                | LcaResult.Unique baseHash ->
                    if baseHash = headA then
                        return plain (MergeResult.FastForward headB)
                    elif baseHash = headB then
                        return plain (MergeResult.FastForward headA)
                    else
                        return divergent (Some baseHash) (replayAt baseHash)
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
            let! outcome =
                mergeImpl recordAuthor sink streamId initial headA headB now MergePolicy.lenient MergeContext.defaults

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
        mergeImpl recordAuthor sink streamId initial headA headB now policy MergeContext.defaults

    /// `mergeGated` under an explicit `MergeContext` (Phase 1525) — the entry
    /// point that takes the merge node's ATTRIBUTION and an optional replay
    /// CHECKPOINT from the caller.
    ///
    /// Additive by construction: `mergeGated ... policy` is exactly this with
    /// `MergeContext.defaults`, and that default reproduces the pre-1525 record
    /// byte for byte, so no existing caller's signature, behaviour or content
    /// addresses move.
    let mergeGatedWith<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        (policy: MergePolicy<'Msg>)
        (context: MergeContext<'Msg>)
        : Async<GatedMergeOutcome<'Msg>> =
        mergeImpl recordAuthor sink streamId initial headA headB now policy context

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
    /// `mergeIntoTrunk` reporting WHAT happened (Phase 1525), under an explicit
    /// `MergeContext`.
    ///
    /// The `string option` face below folds a refused merge, a missing common
    /// base, a replay failure and an exhausted retry budget into one `None`. This
    /// one keeps them apart — and, in the refusal case, hands back the
    /// `MergeConflict` envelopes the merge already built: the LCA value, both
    /// sides' values with their provenance tags, the enumerated resolution
    /// choices and the `ApplyHint`. Those were constructed and then discarded one
    /// line before the caller could see them, which left "resolve the conflict
    /// and re-merge" — the documented recovery — with nothing to resolve.
    let mergeIntoTrunkWith<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (branchHead: string)
        (now: DateTimeOffset)
        (maxRetries: int)
        (context: MergeContext<'Msg>)
        : Async<TrunkMergeOutcome<'Msg>> =
        let rec attempt (remaining: int) =
            async {
                let! trunkOpt = sink.Head streamId

                match trunkOpt with
                | None ->
                    // No trunk yet — fast-forward the branch to the trunk.
                    let! ok = sink.TryAdvanceHead(streamId, None, branchHead)

                    if ok then return TrunkMergeOutcome.Advanced branchHead
                    elif remaining > 0 then return! attempt (remaining - 1)
                    else return TrunkMergeOutcome.Contended
                | Some trunk when trunk = branchHead -> return TrunkMergeOutcome.Advanced trunk
                | Some trunk ->
                    let! outcome =
                        mergeImpl recordAuthor sink streamId initial trunk branchHead now MergePolicy.lenient context

                    match outcome.Result with
                    | MergeResult.AlreadyMerged h -> return TrunkMergeOutcome.Advanced h
                    | MergeResult.FastForward h ->
                        let! ok = sink.TryAdvanceHead(streamId, Some trunk, h)

                        if ok then return TrunkMergeOutcome.Advanced h
                        elif remaining > 0 then return! attempt (remaining - 1)
                        else return TrunkMergeOutcome.Contended
                    | MergeResult.Merged(record, _) ->
                        let! ok = commitMerge sink streamId (Some trunk) record

                        if ok then return TrunkMergeOutcome.Advanced record.Hash
                        elif remaining > 0 then return! attempt (remaining - 1)
                        else return TrunkMergeOutcome.Contended
                    | MergeResult.NeedsManualMerge contended -> return TrunkMergeOutcome.Conflicted contended
                    | MergeResult.NeedsThreeWayMerge candidates ->
                        return TrunkMergeOutcome.NeedsThreeWayMerge candidates
                    | MergeResult.NoCommonBase -> return TrunkMergeOutcome.NoCommonBase
                    | MergeResult.ReplayFailed e -> return TrunkMergeOutcome.ReplayFailed e
            }

        attempt maxRetries

    let mergeIntoTrunk<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (branchHead: string)
        (now: DateTimeOffset)
        (maxRetries: int)
        : Async<string option> =
        async {
            let! outcome =
                mergeIntoTrunkWith recordAuthor sink streamId initial branchHead now maxRetries MergeContext.defaults

            // The lossy projection, kept verbatim so existing callers are
            // untouched. `mergeIntoTrunkWith` is where the discarded detail is.
            return
                match outcome with
                | TrunkMergeOutcome.Advanced h -> Some h
                | _ -> None
        }
