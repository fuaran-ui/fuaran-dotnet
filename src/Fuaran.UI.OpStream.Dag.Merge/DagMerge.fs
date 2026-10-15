namespace Fuaran.UI.OpStream.Dag.Merge

// `NeedsThreeWayMerge` is obsolete (Phase 2062) and nothing constructs it, but
// the trunk outcome still maps it case for case, so the map names it.
#nowarn "44"

open System
open System.Collections.Generic
open Fuaran.UI.Types
open Fuaran.UI.Ops
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

/// Why a merge node could not be MINTED even though the three-way merge
/// succeeded (Phase 1526).
///
/// A merge node commits to two things that must agree: `OutcomeHash`, the
/// canonical hash of the merged TREE, and `Op`, the replay delta from the
/// node's primary parent. `DagReplay` folds the delta; a verifier compares the
/// hash; M1's whole claim — two hosts agree iff they reach the same tree —
/// rests on the two being the same tree.
///
/// They can differ, because the delta is `TreeOpDiff.diffBatched` and the
/// `TreeOp` vocabulary is NOT total over the node record: no op sets
/// `Accessibility` or `Tooltip`, both of which `TreeMerge` merges as facets of
/// their own. A merge that took the other branch's accessibility produced a
/// delta that could not carry it, and every layer downstream — mint, commit,
/// replay, verify — accepted it in silence.
///
/// So the mint verifies its own delta and refuses. The refusal is deliberately
/// not a fallback to a lossy node: what is lost is a facet a human or an agent
/// deliberately set on the other branch, and a merge node that quietly drops it
/// is exactly the artefact the content address exists to make impossible.
type MergeDeltaMismatch =
    {
        /// The canonical hash of the merged tree the node would have committed to.
        OutcomeHash: string
        /// The canonical hash of the tree the node's own delta actually replays
        /// to over its primary parent. `None` when the delta did not apply at all.
        ReplayedHash: string option
        /// The apply failure, when the delta could not be folded at all.
        ApplyError: ApplyError option
    }

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
    /// The three-way merge SUCCEEDED and its result is `tree`, but the replay
    /// delta a merge node would carry cannot reproduce that tree, so no node was
    /// minted (Phase 1526). The merged tree is handed back so a host can show
    /// it, diff it, or write it through another path — the refusal is about what
    /// the DAG can honestly record, not about what the merge computed.
    | DeltaNotReplayable of tree: Node<'Msg> * mismatch: MergeDeltaMismatch
    /// Never constructed. A criss-cross history (≥2 LCAs) has been resolved by
    /// recursive-base merge since Phase 179, so no merge returns this; it stays
    /// only so a consumer's existing match still compiles (Phase 2062).
    | [<Obsolete("Never constructed: a criss-cross history is resolved by recursive-base merge (Phase 179). Remove the match arm (Phase 2062).")>] NeedsThreeWayMerge of
        candidates: string list
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
/// A record rather than one overload per knob: the knobs here are "something the
/// host knows and the engine cannot derive", and a fresh overload for each would
/// multiply the entry points every time another appears. Phase 2062 put the
/// validator `Policy` here too, so `DagMerge.mergeLookups` is the one merge and
/// every other entry point is a wrapper that fills this record in.
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
        /// The validator gate over a structurally clean merge (Phase 184).
        /// `MergePolicy.lenient` runs none.
        Policy: MergePolicy<'Msg>
    }

module MergeContext =
    /// Anonymous attribution, no checkpoint, no validator — the pre-1525
    /// behaviour exactly.
    let defaults<'Msg> : MergeContext<'Msg> =
        { Attribution = MergeAttribution.anonymous
          Checkpoint = None
          Policy = MergePolicy.lenient }

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
    /// The merge SUCCEEDED but no node could be minted for it, because the
    /// replay delta cannot reproduce the merged tree (Phase 1526). Distinct from
    /// `Conflicted`: nothing is contended and there is nothing to resolve — the
    /// merged tree is right and the DAG cannot express how it was reached.
    | DeltaNotReplayable of tree: Node<'Msg> * mismatch: MergeDeltaMismatch
    /// Never constructed, as `MergeResult.NeedsThreeWayMerge` is not (Phase 2062).
    | [<Obsolete("Never constructed: a criss-cross history is resolved by recursive-base merge (Phase 179). Remove the match arm (Phase 2062).")>] NeedsThreeWayMerge of
        candidates: string list
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
        : Result<DagOpRecord<'Msg>, MergeDeltaMismatch> =
        let mergedJson = CanonicalJson.encodeNode merged
        let delta = TreeOpDiff.diffBatched treeA merged

        let deltaOp =
            match delta with
            | [] -> TreeOp.Batch []
            | [ single ] -> single
            | many -> TreeOp.Batch many

        // Phase 1526 — replay the delta before minting the node that carries
        // it. This is the SAME fold `DagReplay.replay` performs on the spine
        // (`Apply.apply record.Op` over the primary parent's tree), so agreement
        // here is agreement there, on this host and on every other one. It costs
        // one apply + one canonical encode per merge, paid once at mint against
        // a divergence that is otherwise permanent and undetectable.
        //
        // The two encodings are compared as text and hashed once (Phase 2062):
        // equal text is an equal hash, so a merge that mints pays one SHA-256.
        match Apply.apply deltaOp treeA with
        | Error e ->
            Error
                { OutcomeHash = HashChain.sha256Hex mergedJson
                  ReplayedHash = None
                  ApplyError = Some e }
        | Ok replayed ->
            let replayedJson = CanonicalJson.encodeNode replayed

            if replayedJson <> mergedJson then
                Error
                    { OutcomeHash = HashChain.sha256Hex mergedJson
                      ReplayedHash = Some(HashChain.sha256Hex replayedJson)
                      ApplyError = None }
            else
                let outcomeHash = HashChain.sha256Hex mergedJson

                Ok(
                    DagOpRecord.createMerge
                        streamId
                        [ headA; headB ]
                        deltaOp
                        outcomeHash
                        attribution.Prompt
                        attribution.Actor
                        now
                        attribution.ResultEnvelope
                )

    /// The synthetic virtual-ancestor TREE for a criss-cross (multiple-LCA)
    /// history — git's recursive-base merge. The candidate bases arrive sorted
    /// (`LcaResult.Ambiguous` is sorted, for determinism) and are folded: each
    /// consecutive pair is merged LENIENTLY (conflicts resolved to base — a
    /// virtual base never blocks) over their own LCA. The result is a
    /// deterministic, host-reproducible synthetic base the real
    /// (conflict-surfacing) merge then runs against. The first candidate comes
    /// apart from the rest because an ambiguous LCA always has two or more:
    /// there is no empty or single-candidate case to answer (Phase 2062 removed
    /// both arms).
    let private virtualAncestorTree<'Msg>
        (replay: string -> Result<Node<'Msg>, DagReplayError>)
        (getParents: string -> string list)
        (first: string)
        (rest: string list)
        : Result<Node<'Msg>, DagReplayError> =

        let ancestorOf (a: string) (b: string) : Result<Node<'Msg>, DagReplayError> =
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
            (replay first, first)
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
    let private mergeTrees<'Msg>
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
            let defects =
                match policy.Validator with
                | None -> []
                | Some validator -> ValidatorGate.introducedDefects validator treeA treeB merged

            if not (List.isEmpty defects) && policy.GateOnIntroducedDefect then
                // A merge-introduced defect is a SEMANTIC conflict — refuse,
                // naming the offending nodes + enumerated recovery. Decided
                // BEFORE the mint (Phase 1526): a merge the gate refuses needs
                // no record, and minting first would let a recordability
                // refusal mask the defect a host would rather hear about.
                { Result = MergeResult.NeedsManualMerge(defects |> List.map ValidatorGate.toConflict)
                  Diagnostics = defects }
            else
                // Gating off (or nothing introduced) — the clean structural
                // merge proceeds; any introduced defects ride along as a
                // post-merge diagnostic, on the refusal below as well as on a
                // `Merged`, because they are a fact about the tree either way.
                match buildMergeRecord streamId headA headB treeA merged now attribution with
                | Error mismatch ->
                    { Result = MergeResult.DeltaNotReplayable(merged, mismatch)
                      Diagnostics = defects }
                | Ok record ->
                    { Result = MergeResult.Merged(record, merged)
                      Diagnostics = defects }

    /// THE merge (Phase 2062): every other entry point fills in a
    /// `MergeContext` and calls this. It reads the DAG only through `getRec`
    /// and `getParents`, so the caller loads the records once: a sink face
    /// loads them per call, the trunk loop once per trunk merge rather than per
    /// CAS retry, and guest convergence over the union of two streams with no
    /// stand-in sink. Pure and synchronous.
    ///
    /// What it costs past the divergence point: the LCA paints down from both
    /// heads and stops just below the base; the base is replayed once and each
    /// head folds only its own spine above it; every replay is memoised by head
    /// hash, which keeps the recursive base of a criss-cross from replaying the
    /// same candidates over and over. Replaying the base itself still folds the
    /// history beneath it unless `context.Checkpoint` bounds it — that is what
    /// a checkpoint is for.
    ///
    /// Per-side precedence comes from the host-supplied `recordAuthor`
    /// classifier (the merge layer interprets no record field); under
    /// precedence a conflict lists `KeepPrimary` first. `initial` is the genesis
    /// tree; `now` stamps a created merge node.
    let mergeLookups<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (getRec: string -> DagOpRecord<'Msg> option)
        (getParents: string -> string list)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        (context: MergeContext<'Msg>)
        : GatedMergeOutcome<'Msg> =
        if headA = headB then
            plain (MergeResult.AlreadyMerged headA)
        else
            let tipA = authorOf recordAuthor getRec headA
            let tipB = authorOf recordAuthor getRec headB

            // Checkpoint-bounded replay (Phase 1525). A checkpoint that does NOT
            // bound a head is not an error: its `AtHash` simply is not on that
            // head's primary spine, which `replayFromCheckpoint` reports as
            // `UnknownHash AtHash` (its `collect` stops AT `AtHash` before any
            // lookup, so that error can arise no other way). Those fall back to a
            // full replay. A snapshot that fails its own position-bound hash does
            // NOT fall back — that is tamper evidence, and quietly replaying
            // around it would discard the one signal the check exists to raise.
            let replayFull (head: string) : Result<Node<'Msg>, DagReplayError> =
                match context.Checkpoint with
                | None -> DagReplay.replay getRec initial head
                | Some checkpoint ->
                    match DagReplay.replayFromCheckpoint getRec checkpoint head with
                    | Ok tree -> Ok tree
                    | Error(DagReplayError.UnknownHash h) when h = checkpoint.AtHash ->
                        DagReplay.replay getRec initial head
                    | Error e -> Error e

            let replays = Dictionary<string, Result<Node<'Msg>, DagReplayError>>()

            let remember (head: string) (tree: Result<Node<'Msg>, DagReplayError>) =
                replays[head] <- tree
                tree

            let replayAt (head: string) : Result<Node<'Msg>, DagReplayError> =
                match replays.TryGetValue head with
                | true, tree -> tree
                | false, _ -> remember head (replayFull head)

            // `head`'s tree folded over `anchor`'s, when `anchor` is on `head`'s
            // primary spine: the same records in the same order as a replay from
            // genesis, less the ones `anchor`'s own replay already folded. A
            // spine that reaches genesis without meeting `anchor` replays in
            // full. A hole above `anchor` is the walk's error, the one a full
            // replay meets first too; an `anchor` that did not replay fails the
            // merge on its own error, which is read before this result.
            let replayAbove (anchor: string) (head: string) : Result<Node<'Msg>, DagReplayError> =
                match replays.TryGetValue head with
                | true, tree -> tree
                | false, _ ->
                    match replayAt anchor with
                    | Error e -> Error e
                    | Ok anchorTree ->
                        match DagReplay.collectSpine (SpineBound.ExclusiveOrGenesis anchor) getRec head with
                        | Error e -> remember head (Error e)
                        | Ok(first :: _) when List.isEmpty first.Parents -> replayAt head
                        | Ok spine ->
                            spine
                            |> List.fold
                                (fun (acc: Result<Node<'Msg>, DagReplayError>) (r: DagOpRecord<'Msg>) ->
                                    match acc with
                                    | Error _ -> acc
                                    | Ok tree -> DagReplay.foldRecord tree r)
                                (Ok anchorTree)
                            |> remember head

            // Per-cell authorship: the last writer of each cell on each
            // branch since `stopAt` (the base), falling back to the branch
            // tip for cells the backward walk did not attribute.
            let divergent
                (stopAt: string option)
                (baseTreeR: Result<Node<'Msg>, DagReplayError>)
                (treeAR: Result<Node<'Msg>, DagReplayError>)
                (treeBR: Result<Node<'Msg>, DagReplayError>)
                =
                match baseTreeR, treeAR, treeBR with
                | Error e, _, _
                | _, Error e, _
                | _, _, Error e -> plain (MergeResult.ReplayFailed e)
                | Ok baseTree, Ok treeA, Ok treeB ->
                    // A hole in either branch's spine is the replay error it
                    // is everywhere else (Phase 2043) — never a silent
                    // fallback to the tip author.
                    match
                        DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headA tipA,
                        DagPrimacy.cellAuthorFn recordAuthor getRec stopAt headB tipB
                    with
                    | Error e, _
                    | _, Error e -> plain (MergeResult.ReplayFailed e)
                    | Ok authA, Ok authB ->
                        let cellAuthor (nodeId: string) (facet: string) = authA nodeId facet, authB nodeId facet

                        mergeTrees
                            streamId
                            headA
                            headB
                            cellAuthor
                            context.Policy
                            baseTree
                            treeA
                            treeB
                            now
                            context.Attribution

            match DagTopology.lca getParents headA headB with
            | LcaResult.None
            | LcaResult.Ambiguous [] -> plain MergeResult.NoCommonBase
            | LcaResult.Ambiguous(first :: rest) ->
                // M2: recursive-base merge over the synthetic virtual ancestor.
                // No single base hash — the per-cell walk runs to genesis.
                let virtualBase = virtualAncestorTree replayAt getParents first rest
                divergent None virtualBase (replayAt headA) (replayAt headB)
            | LcaResult.Unique baseHash ->
                if baseHash = headA then
                    plain (MergeResult.FastForward headB)
                elif baseHash = headB then
                    plain (MergeResult.FastForward headA)
                else
                    let baseTree = replayAt baseHash
                    divergent (Some baseHash) baseTree (replayAbove baseHash headA) (replayAbove baseHash headB)

    /// Load `streamId`'s records once, keyed by hash (the last wins on a
    /// repeated hash).
    let private load<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        : Async<Dictionary<string, DagOpRecord<'Msg>>> =
        async {
            let! records = sink.Records streamId
            let byHash = Dictionary<string, DagOpRecord<'Msg>>()

            for r in records do
                byHash[r.Hash] <- r

            return byHash
        }

    /// The two lookups `mergeLookups` reads, over loaded records.
    let private lookupsOf<'Msg> (byHash: Dictionary<string, DagOpRecord<'Msg>>) =
        let getRec (h: string) =
            match byHash.TryGetValue h with
            | true, r -> Some r
            | false, _ -> None

        let getParents (h: string) =
            match byHash.TryGetValue h with
            | true, r -> r.Parents
            | false, _ -> []

        getRec, getParents

    /// `mergeLookups` over a sink: load the stream's records, then merge.
    let private mergeInSink<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (headA: string)
        (headB: string)
        (now: DateTimeOffset)
        (context: MergeContext<'Msg>)
        : Async<GatedMergeOutcome<'Msg>> =
        async {
            if headA = headB then
                return plain (MergeResult.AlreadyMerged headA)
            else
                let! byHash = load sink streamId
                let getRec, getParents = lookupsOf byHash
                return mergeLookups recordAuthor getRec getParents streamId initial headA headB now context
        }

    /// Attempt an M1+M2 merge of `headA` and `headB` in `streamId`: `mergeLookups`
    /// over the sink's records under `MergeContext.defaults`. Criss-cross
    /// (multiple-LCA) histories are resolved by recursive-base merge. No
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
            let! outcome = mergeInSink recordAuthor sink streamId initial headA headB now MergeContext.defaults
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
        mergeInSink
            recordAuthor
            sink
            streamId
            initial
            headA
            headB
            now
            { MergeContext.defaults with
                Policy = policy }

    /// `mergeGated` under an explicit `MergeContext` (Phase 1525): the merge
    /// node's ATTRIBUTION and an optional replay CHECKPOINT from the caller.
    /// `policy` predates the context's `Policy` field and wins over it.
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
        mergeInSink recordAuthor sink streamId initial headA headB now { context with Policy = policy }

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
    /// writes. Reports WHAT happened (Phase 1525) under an explicit
    /// `MergeContext`, its `Policy` included.
    ///
    /// The `string option` face below folds a refused merge, a missing common
    /// base, a replay failure and an exhausted retry budget into one `None`. This
    /// one keeps them apart — and, in the refusal case, hands back the
    /// `MergeConflict` envelopes the merge already built: the LCA value, both
    /// sides' values with their provenance tags, the enumerated resolution
    /// choices and the `ApplyHint`.
    ///
    /// The stream is loaded ONCE (Phase 2062). A retry fetches, record by
    /// record, only the ancestors the moved trunk added since; a record is
    /// content-addressed, so one already loaded has not changed.
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
        async {
            let! byHash = load sink streamId
            let getRec, getParents = lookupsOf byHash

            // Fetch the ancestors of `pending` the loaded set does not hold.
            let rec extend (pending: string list) =
                async {
                    match pending with
                    | [] -> return ()
                    | h :: rest when byHash.ContainsKey h -> return! extend rest
                    | h :: rest ->
                        match! sink.TryGet(streamId, h) with
                        | Some r ->
                            byHash[h] <- r
                            return! extend (r.Parents @ rest)
                        | None -> return! extend rest
                }

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
                        do! extend [ trunk; branchHead ]

                        match
                            (mergeLookups recordAuthor getRec getParents streamId initial trunk branchHead now context)
                                .Result
                        with
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
                        | MergeResult.DeltaNotReplayable(tree, mismatch) ->
                            return TrunkMergeOutcome.DeltaNotReplayable(tree, mismatch)
                        | MergeResult.NeedsThreeWayMerge candidates ->
                            return TrunkMergeOutcome.NeedsThreeWayMerge candidates
                        | MergeResult.NoCommonBase -> return TrunkMergeOutcome.NoCommonBase
                        | MergeResult.ReplayFailed e -> return TrunkMergeOutcome.ReplayFailed e
                }

            return! attempt maxRetries
        }

    /// `mergeIntoTrunkWith` under `MergeContext.defaults`, projected to the new
    /// trunk head or `None` — the lossy face, kept for existing callers.
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
            match!
                mergeIntoTrunkWith recordAuthor sink streamId initial branchHead now maxRetries MergeContext.defaults
            with
            | TrunkMergeOutcome.Advanced h -> return Some h
            | _ -> return None
        }
