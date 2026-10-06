namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagReplay — reconstruct a tree at a DAG head.
//
//  Replay follows the PRIMARY-parent spine (`Parents[0]`), folding each node's
//  `Op` through the apply engine over an initial tree. A merge node's `Op` is
//  the replay delta from its primary parent's tree to the merged tree, so the
//  spine fold reconstructs the merged tree without re-walking the secondary
//  branch (whose effect is already captured in the merge delta). A linear
//  (single-parent) chain is the degenerate case: the spine IS the chain.
// ============================================================================

[<RequireQualifiedAccess>]
type DagReplayError =
    /// A hash on the spine has no record in the supplied lookup.
    | UnknownHash of hash: string
    /// A spine node is tombstoned — its payload was pruned, so the tree can no
    /// longer be reconstructed past it. A live head must never have a
    /// tombstoned ancestor on its spine (that is a retention-policy defect).
    | TombstonedOnSpine of hash: string
    /// The apply engine rejected a spine op.
    | ApplyFailed of hash: string * error: ApplyError
    /// A `DagCheckpoint`'s stored `SnapshotHash` does not recompute against its
    /// `Snapshot` at its claimed `AtHash` — the snapshot was tampered with, or a
    /// genuine snapshot is being presented at a position it does not hold.
    /// Replay refuses rather than folding the tail over an unverified base.
    | SnapshotHashMismatch of atHash: string * expected: string * actual: string
    /// A MERGE node's replay delta did not reproduce the `OutcomeHash` it
    /// committed to (Phase 1526). A merge node commits to the merged TREE by
    /// hash and carries a delta that is supposed to reach it; when the two
    /// disagree the node is not a record of the merge it claims, and every tree
    /// folded past it on this spine is a different tree from the one the merge
    /// produced.
    ///
    /// The mint refuses this shape since Phase 1526 (`DagMerge`), so a node
    /// reaching this check was written by an older engine, by another host, or
    /// by hand. Detection on READ is the half a write-time refusal cannot
    /// cover: the stream outlives the process that wrote it.
    | MergeOutcomeMismatch of hash: string * expected: string * actual: string

/// Where a primary-parent spine walk stops (Phase 2043). Every reconstruction
/// in this tier walks the SAME spine — `Parents[0]`, head back towards genesis —
/// and differs only in where the walk ends, so the stop rule is the one thing a
/// caller chooses; the walk itself, and what it calls an error, is shared.
[<RequireQualifiedAccess>]
type internal SpineBound =
    /// Walk to a parentless genesis record, inclusive (`DagReplay.replay`).
    | Genesis
    /// Stop AT this hash, exclusive — its effect is already accounted for. A
    /// genesis reached first means the bound is not on this head's spine, and
    /// is `UnknownHash` of the bound (`DagReplay.replayFromCheckpoint`).
    | Exclusive of bound: string
    /// Stop AT this hash, exclusive, or at a genesis, inclusive — whichever
    /// comes first. A merge base need not lie on a head's PRIMARY spine, so
    /// reaching genesis is not an error here (`DagPrimacy.cellAuthors`).
    | ExclusiveOrGenesis of bound: string
    /// Stop at the first record whose primary parent the lookup does not hold,
    /// inclusive — for a guest stream that parent is the host-side Mount
    /// anchor, a boundary rather than a step (`GuestReplay.replayInterior`).
    | Anchor

module DagReplay =

    /// Collect `head`'s primary-parent spine, oldest first, ending where
    /// `bound` says. The ONE spine walk of the tier (Phase 2043): replay,
    /// checkpoint replay, guest replay and primacy attribution all call it, so
    /// a hole in the spine is the same error on every path. Before, primacy
    /// treated a missing record as "stop walking" while replay treated it as
    /// `UnknownHash`, so one hole was an error in replay and a silent fallback
    /// authorship in a merge.
    ///
    /// A hash the lookup does not hold is `UnknownHash`; a tombstoned record is
    /// `TombstonedOnSpine` — retention keeps every ancestor of a live head live
    /// (`DagRetention`), so a tombstone on a live spine is a defect wherever it
    /// is met.
    let internal collectSpine<'Msg>
        (bound: SpineBound)
        (getRecord: string -> DagOpRecord<'Msg> option)
        (head: string)
        : Result<DagOpRecord<'Msg> list, DagReplayError> =
        let stopsAt (hash: string) =
            match bound with
            | SpineBound.Exclusive b
            | SpineBound.ExclusiveOrGenesis b -> hash = b
            | SpineBound.Genesis
            | SpineBound.Anchor -> false

        let rec walk (hash: string) (acc: DagOpRecord<'Msg> list) : Result<DagOpRecord<'Msg> list, DagReplayError> =
            if stopsAt hash then
                Ok acc
            else
                match getRecord hash with
                | None -> Error(DagReplayError.UnknownHash hash)
                | Some r when r.Tombstoned -> Error(DagReplayError.TombstonedOnSpine hash)
                | Some r ->
                    match r.Parents, bound with
                    // Reached a genesis without meeting the bound — it does
                    // not bound this head.
                    | [], SpineBound.Exclusive b -> Error(DagReplayError.UnknownHash b)
                    | [], _ -> Ok(r :: acc)
                    | primary :: _, SpineBound.Anchor when Option.isNone (getRecord primary) -> Ok(r :: acc)
                    | primary :: _, _ -> walk primary (r :: acc)

        walk head []

    /// Fold one spine record's op over the tree, then — for a MERGE node —
    /// check that the result is the tree the node committed to. The ONE record
    /// fold of the tier (Phase 2043): the guest path folded bare `Apply.apply`
    /// before, so a guest merge node with a wrong `OutcomeHash` replayed cleanly
    /// there and was refused here.
    ///
    /// `OutcomeHash` is populated exactly for a merge node (`Parents.Length ≥
    /// 2`; see `DagOpRecord`), so an ordinary single-parent step costs nothing:
    /// no encode, no hash. That is the reason the check lives here rather than
    /// hashing every folded tree — the invariant is only claimed at merge
    /// nodes, and only there is there a committed value to compare against.
    let internal foldRecord<'Msg> (tree: Node<'Msg>) (r: DagOpRecord<'Msg>) : Result<Node<'Msg>, DagReplayError> =
        match Apply.apply r.Op tree with
        | Error e -> Error(DagReplayError.ApplyFailed(r.Hash, e))
        | Ok tree' ->
            match r.OutcomeHash with
            | None -> Ok tree'
            | Some expected ->
                let actual = CanonicalJson.encodeNode tree' |> HashChain.sha256Hex

                if actual = expected then
                    Ok tree'
                else
                    Error(DagReplayError.MergeOutcomeMismatch(r.Hash, expected, actual))

    /// Collect `head`'s spine under `bound`, then fold it over `initial`
    /// record by record, stopping at the first error.
    let internal replayBounded<'Msg>
        (bound: SpineBound)
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (head: string)
        : Result<Node<'Msg>, DagReplayError> =
        collectSpine bound getRecord head
        |> Result.bind (fun spine ->
            spine
            |> List.fold
                (fun (acc: Result<Node<'Msg>, DagReplayError>) (r: DagOpRecord<'Msg>) ->
                    match acc with
                    | Error _ -> acc
                    | Ok tree -> foldRecord tree r)
                (Ok initial))

    /// Replay `head` to its tree by folding ops along the primary-parent spine.
    /// `getRecord` resolves a hash to its record (typically a pre-loaded map of
    /// the stream's records). `initial` is the genesis tree.
    let replay<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (head: string)
        : Result<Node<'Msg>, DagReplayError> =
        replayBounded SpineBound.Genesis getRecord initial head

    /// Checkpoint-bounded replay: reconstruct `head`'s tree starting from a
    /// `DagCheckpoint`'s snapshot rather than from genesis. The snapshot IS the
    /// tree AT `checkpoint.AtHash` (its op already applied), so only the spine
    /// TAIL — the nodes between `head` and `AtHash`, exclusive of `AtHash` — is
    /// folded over the snapshot. `head = AtHash` yields the snapshot unchanged.
    /// Errors if `AtHash` is not on `head`'s primary spine (the checkpoint does
    /// not bound this head). This is the DAG generalisation of the linear
    /// "replay from op-index N" — the cost bound is (checkpoint→head) tail
    /// length, not total history.
    ///
    /// The snapshot is VERIFIED before it is trusted (the DAG half of the Phase
    /// 412 / A2 fix): its stored `SnapshotHash` must recompute against its
    /// `Snapshot` AT its claimed `AtHash`, so neither a swapped snapshot nor a
    /// genuine snapshot presented at the wrong node is folded over. Pre-fix this
    /// function trusted `checkpoint.Snapshot` outright and never looked at the
    /// hash at all — the whole point of materialising one was silently unchecked.
    /// The check is pure and runs before any record lookup, so a bad checkpoint
    /// costs one canonical encode and never reaches the apply engine.
    let replayFromCheckpoint<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (checkpoint: DagCheckpoint<'Msg>)
        (head: string)
        : Result<Node<'Msg>, DagReplayError> =
        // Verify the snapshot BEFORE folding anything over it — an unverified
        // base makes every op applied on top of it meaningless.
        match DagCheckpoint.verifySnapshotHash checkpoint with
        | Error(recomputed, stored) -> Error(DagReplayError.SnapshotHashMismatch(checkpoint.AtHash, stored, recomputed))
        | Ok() -> replayBounded (SpineBound.Exclusive checkpoint.AtHash) getRecord checkpoint.Snapshot head
