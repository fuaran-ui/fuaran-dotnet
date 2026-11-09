namespace Fuaran.UI.OpStream.Replay

open System
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Checkpoint creation + replay-from-checkpoint + compaction.
//
//  Three modules in this file:
//
//   1. `Checkpoint` — materialise a checkpoint from a tree + the sink's
//      current chain head (`CheckpointSeal.create`, Core's strict seal) and
//      call `sink.AppendCheckpoint`.
//
//   2. `CheckpointedReplay` — resolve the nearest checkpoint ≤ target and
//      replay only the tail. Since Phase 2305 the checkpoint IS a Core
//      `Snapshot` and the resume IS Core's: the seal and the WHOLE tail are
//      verified from `(PreviousChainHead, Sequence)` by `Sealed.firstBreak`
//      before anything is applied (a break in the middle of the tail is refused,
//      not only one at its first link), and the tail is folded by `Compacted`'s
//      `replayFrom`. Falls back to replay-from-genesis when no checkpoint exists
//      in range.
//
//   3. `Compaction` — apply a `CompactionPolicy` to a stream. Walks
//      checkpoints, keeps the last K, truncates ops with sequence ≤
//      oldest-retained-checkpoint.Sequence. The hash-chain integrity of
//      the surviving M+1..N segment is preserved because every retained
//      checkpoint's `PreviousChainHead` pins the chain head at its
//      op-index.
//
//  ── WHAT THE CHECKPOINT HASH BINDS ─────────────────────────────────────────
//  A snapshot hash is Core's strict seal (Phase 2305; position-bound since
//  Phase 412): `previousChainHead` + `sequence` + the canonical tree in one
//  delimited pre-image, so a real snapshot from one position does not validate
//  at a different one. `applyFromCheckpoint` additionally anchors
//  `PreviousChainHead` against the actual op-chain on the empty-tail path, so a
//  checkpoint is trusted only at a position the verified chain confirms. Both
//  are defence-in-depth against accidental corruption and cross-position
//  substitution, NOT tamper-proofing against a writer who rewrites the whole
//  checkpoint record consistently — that is the signed attestation seam's job.
//  See CRYPTO.md.
//
//  ── FGP 2 / FGP 5 / FGP 6 (post-405 fence) ─────────────────────────────────
//  SHA-256 no longer sits behind a `#if !FABLE_COMPILER` guard: since Phase 405
//  `HashChain` routes through the pure, Fable-safe `Fuaran.Core.Hash.sha256Hex`
//  and compiles on both pipelines. This module is .NET-only for a different and
//  still-true reason — the whole Replay package is server-side by design, the
//  apply engine being the part that ships on both. The op stream stays the
//  source of truth: checkpoints accelerate replay, they do not replace the
//  chain as the canonical record.
// ============================================================================

[<RequireQualifiedAccess>]
type CheckpointReplayError =
    /// The first tail record's `PreviousHash` does not link to the
    /// retained checkpoint's `PreviousChainHead`. The integrity boundary
    /// is broken — either the checkpoint or the tail was tampered with.
    | BoundaryMismatch of checkpointSequence: int * expected: string * actual: string
    /// The retained checkpoint's `SnapshotHash` does not match the
    /// recomputed hash of its `Snapshot`. The snapshot was tampered with.
    | SnapshotHashMismatch of checkpointSequence: int * expected: string * actual: string
    /// Replay of the tail records failed at the underlying apply engine.
    | TailApplyFailed of replayError: ReplayError
    /// The state at `targetSequence` CANNOT be reconstructed: no retained
    /// checkpoint covers it, and the ops needed to reach it from the initial
    /// tree have been compacted away (Phase 1525, finding M-C2).
    ///
    /// **Why this is an error and not an answer.** The pre-1525 path replayed
    /// whatever ops survived over `initialTree` and returned the result — which,
    /// for a target below the retention horizon, is the INITIAL TREE presented
    /// as the state at `target`. Nothing in the return said so. A caller
    /// auditing "what did the document look like at op 40" on a stream compacted
    /// to op 100 got an empty document and a `Ok`, and could not tell that from
    /// a document that was genuinely empty then. Refusing is the only answer
    /// that is not a lie, and `earliestRetainedSequence` says how far back the
    /// store CAN go, so the caller can ask a question it can answer.
    | BelowRetentionHorizon of targetSequence: int * earliestRetainedSequence: int option
    /// The tail above the checkpoint breaks BELOW its first link (Phase 2305): a
    /// record out of sequence, a `PreviousHash` that does not name its
    /// predecessor, or a `Hash` that does not recompute, anywhere in the tail.
    /// Carries the first violation, named by its sequence. Nothing was applied.
    /// (A break AT the first link — the tail not hanging from the checkpoint —
    /// is `BoundaryMismatch`.)
    | TailBroken of checkpointSequence: int * verificationError: VerificationError

module Checkpoint =

    let private currentTimestamp () : DateTimeOffset = DateTimeOffset.UtcNow

    /// Materialise a checkpoint for `tree` at the sink's current latest
    /// sequence. The caller has already applied ops `1..LatestSequence`
    /// to produce `tree`. `PreviousChainHead` is the latest op's `Hash` (or the
    /// boundary a compaction kept, or `HashChain.genesisPreviousHash` for an
    /// empty stream); the seal is Core's (`CheckpointSeal.create`). Persists via
    /// `sink.AppendCheckpoint`.
    let create<'Msg>
        (sink: IOpStreamCheckpointSink<'Msg>)
        (streamId: string)
        (tree: Node<'Msg>)
        : Async<Checkpoint<'Msg>> =
        async {
            let! latest = sink.LatestSequence streamId

            let! previousChainHead =
                async {
                    if latest = 0 then
                        return HashChain.genesisPreviousHash
                    else
                        let! prev = sink.Replay(streamId, latest, latest)

                        match prev with
                        | r :: _ -> return r.Hash
                        | [] ->
                            // The record at the head is gone: compaction truncated
                            // it into a checkpoint AT the head, and the boundary is
                            // read from that snapshot (Phase 1937) — not reset to
                            // genesis, which made the new checkpoint claim a position
                            // no chain holds. (A store with no such checkpoint
                            // refuses the truncation, so there is no other way here;
                            // the duplicate-sequence refusal of `AppendCheckpoint`
                            // then names a second checkpoint at the same head.)
                            let! boundary = sink.LatestCheckpointAtOrBefore(streamId, latest)

                            return
                                match boundary with
                                | Some cp when cp.Sequence = latest -> cp.PreviousChainHead
                                | _ -> HashChain.genesisPreviousHash
                }

            let cp =
                CheckpointSeal.create streamId latest previousChainHead (currentTimestamp ()) tree

            do! sink.AppendCheckpoint cp
            return cp
        }

    /// Recompute the seal of `checkpoint.Snapshot` and compare to
    /// `checkpoint.SnapshotHash` — Core's `Sealed.firstBreak` over an empty tail.
    /// Pure verification — does not touch the sink. Returns `Ok ()` on match,
    /// `Error (recomputed, stored)` on mismatch.
    let verifySnapshotHash<'Msg> (checkpoint: Checkpoint<'Msg>) : Result<unit, string * string> =
        match CheckpointSeal.firstBreak checkpoint [] with
        | Some(Fuaran.Core.SnapshotBreak.SnapshotHash(recomputed, stored)) -> Error(recomputed, stored)
        | _ -> Ok()

module CheckpointedReplay =

    /// Resolve the nearest checkpoint ≤ `targetSequence` (if any) and
    /// replay from there. Falls back to replay-from-genesis when no
    /// checkpoint exists in range — equivalent to `Replay.applyTo
    /// initialTree (sink.Replay(streamId, 1, targetSequence))`.
    ///
    /// Verifies the checkpoint and the WHOLE tail up front, by Core's
    /// `Sealed.firstBreak` from `(PreviousChainHead, Sequence)` (Phase 2305): the
    /// seal must recompute (`SnapshotHashMismatch`), the first tail record must
    /// hang from the checkpoint (`BoundaryMismatch`), and every later record must
    /// sit in sequence, link and recompute (`TailBroken`). Any of them surfaces
    /// without dispatching to the apply engine; the fold is then
    /// `Compacted.replayFrom` from the checkpoint's snapshot.
    let applyFromCheckpoint<'Msg>
        (sink: IOpStreamCheckpointSink<'Msg>)
        (initialTree: Node<'Msg>)
        (streamId: string)
        (targetSequence: int)
        : Async<Result<Node<'Msg>, CheckpointReplayError>> =
        async {
            let! cpOpt = sink.LatestCheckpointAtOrBefore(streamId, targetSequence)

            match cpOpt with
            | None ->
                // No checkpoint in range — replay from genesis. This is
                // the same path `Replay.applyTo` walks when called with
                // the genesis tree + the full op range; folding it in
                // here lets callers use one entry point regardless of
                // checkpoint availability.
                let! records = sink.Replay(streamId, 1, targetSequence)
                let! latest = sink.LatestSequence streamId

                // ── RETENTION HORIZON (Phase 1525, finding M-C2) ───────────
                // Replaying from `initialTree` is only sound when the ops from
                // sequence 1 are actually there. Compaction removes them, and
                // then this branch folded whatever survived over the initial
                // tree and returned it as the state at `target` — silently
                // wrong, and indistinguishable from a correct answer.
                //
                // Two facts decide it, and the second is the one that is easy to
                // miss. First, is the record at sequence 1 still here? Second —
                // because a stream compacted PAST its own head reports
                // `LatestSequence = 0`, exactly like a stream that never had a
                // record — is there any evidence that ops once existed? A
                // retained CHECKPOINT is that evidence: a checkpoint at sequence
                // N was taken over ops 1..N, so its presence says those ops were
                // written whatever the op table now holds. Without this second
                // fact a fully-compacted stream reads as a fresh one, which is
                // the worst case of the very defect being fixed.
                let! retainedCheckpoints = sink.ListCheckpoints streamId

                let opsOnceExisted =
                    latest > 0 || retainedCheckpoints |> List.exists (fun c -> c.Sequence >= 1)

                let belowHorizon =
                    targetSequence >= 1
                    && opsOnceExisted
                    && (match records with
                        | first :: _ -> first.Sequence <> 1
                        | [] -> true)

                if belowHorizon then
                    // What the store CAN still answer. Read from the surviving
                    // range rather than assumed, so the number a caller is given
                    // is one it can actually use.
                    let! surviving = sink.Replay(streamId, 1, latest)

                    let earliest = surviving |> List.tryHead |> Option.map _.Sequence

                    return Error(CheckpointReplayError.BelowRetentionHorizon(targetSequence, earliest))
                else
                    match Replay.applyTo initialTree records with
                    | Ok tree -> return Ok tree
                    | Error e -> return Error(CheckpointReplayError.TailApplyFailed e)
            | Some cp ->
                // Tail = ops (cp.Sequence + 1) .. targetSequence.
                let! tail = sink.Replay(streamId, cp.Sequence + 1, targetSequence)

                // The seal first, then the whole tail from the checkpoint's
                // anchor — Core's one verifier (Phase 2305).
                match CheckpointSeal.firstBreak cp tail with
                | Some(Fuaran.Core.SnapshotBreak.SnapshotHash(recomputed, stored)) ->
                    return Error(CheckpointReplayError.SnapshotHashMismatch(cp.Sequence, stored, recomputed))
                | Some(Fuaran.Core.SnapshotBreak.Tail b) when
                    b.Index = 0 && b.Reason = Fuaran.Core.ChainBreakReason.PrevHashLinkBroken
                    ->
                    return Error(CheckpointReplayError.BoundaryMismatch(cp.Sequence, b.Expected, b.Got))
                | Some(Fuaran.Core.SnapshotBreak.Tail b) ->
                    return Error(CheckpointReplayError.TailBroken(cp.Sequence, Verify.ofChainBreak (cp.Sequence + 1) b))
                | None ->
                    match tail with
                    | [] ->
                        // The tail is empty (target ≤ cp.Sequence): the snapshot
                        // IS the answer — but its `PreviousChainHead` must still be
                        // ANCHORED to the real op-chain (Phase 412 / A2), which the
                        // seal alone cannot say. We confirm the head equals the
                        // actual chain head at `cp.Sequence` (record `cp.Sequence`'s
                        // `Hash`, or the genesis hash at sequence 0).
                        let! anchor =
                            async {
                                if cp.Sequence = 0 then
                                    return HashChain.genesisPreviousHash
                                else
                                    let! atSeq = sink.Replay(streamId, cp.Sequence, cp.Sequence)

                                    match atSeq with
                                    | r :: _ -> return r.Hash
                                    | [] ->
                                        // Compaction truncated the record at
                                        // `cp.Sequence` (Phase 1937). What still
                                        // anchors the checkpoint is the record
                                        // after it, which names the head it hangs
                                        // from — or, when the checkpoint IS the head
                                        // (`keep 1` at the head, then nothing
                                        // appended), the store's own boundary, which
                                        // is read from this very snapshot.
                                        let! after = sink.Replay(streamId, cp.Sequence + 1, cp.Sequence + 1)
                                        let! latest = sink.LatestSequence streamId

                                        return
                                            match after with
                                            | r :: _ -> r.PreviousHash
                                            | [] when latest = cp.Sequence -> cp.PreviousChainHead
                                            | [] -> HashChain.genesisPreviousHash
                            }

                        if anchor <> cp.PreviousChainHead then
                            return
                                Error(CheckpointReplayError.BoundaryMismatch(cp.Sequence, anchor, cp.PreviousChainHead))
                        else
                            return Ok cp.Snapshot
                    | _ ->
                        // Core's `Compacted` fold from the checkpoint's snapshot.
                        // The tail is already verified above, so the fold's own
                        // seam check (`TailSeqMismatch`) cannot fire; its op
                        // position maps back to the record's 1-based sequence.
                        let compacted: Fuaran.Core.Compacted<StreamEntry<'Msg>, Node<'Msg>> =
                            { Snapshot = CheckpointSeal.toSnapshot cp
                              Tail = tail |> List.map StreamEntry.toCoreRecord
                              Keys = Fuaran.Core.KeyIndex.empty }

                        match Fuaran.Core.Compacted.replayFrom (StreamEntry.coreWitness<'Msg> ()) compacted with
                        | Ok tree -> return Ok tree
                        | Error(Fuaran.Core.SnapshotFault.TailRejected(index, applyError))
                        | Error(Fuaran.Core.SnapshotFault.PrefixRejected(index, applyError)) ->
                            return
                                Error(
                                    CheckpointReplayError.TailApplyFailed(
                                        ReplayError.ApplyFailed((List.item index tail).Sequence, applyError)
                                    )
                                )
                        | Error(Fuaran.Core.SnapshotFault.TailSeqMismatch(expected, got)) ->
                            return
                                Error(
                                    CheckpointReplayError.TailBroken(
                                        cp.Sequence,
                                        VerificationError.OutOfOrder(expected + 1, got + 1)
                                    )
                                )
                        | Error(Fuaran.Core.SnapshotFault.SeqOutOfRange(atSeq, length)) ->
                            return
                                Error(
                                    CheckpointReplayError.TailBroken(
                                        cp.Sequence,
                                        VerificationError.OutOfOrder(length + 1, atSeq + 1)
                                    )
                                )
        }

module Compaction =

    /// Apply `policy` to `streamId`. Lists the stream's checkpoints in
    /// ascending sequence order, retains the last `KeepCheckpoints`, and
    /// truncates ops with `Sequence ≤ oldest-retained-checkpoint.Sequence`
    /// from the live sink. Returns the number of ops truncated; zero
    /// indicates either the policy disabled retention, fewer checkpoints
    /// existed than the retention threshold, or no live ops sat before
    /// the oldest retained checkpoint.
    ///
    /// Hash-chain integrity of the surviving M+1..N segment is preserved
    /// because every retained checkpoint's `PreviousChainHead` pins the
    /// chain head at its op-index, so a future `applyFromCheckpoint` call
    /// can resume against the retained snapshot without re-verifying the
    /// truncated 1..M prefix.
    let applyPolicy<'Msg>
        (sink: IOpStreamCheckpointSink<'Msg>)
        (streamId: string)
        (policy: CompactionPolicy)
        : Async<int> =
        async {
            if policy.KeepCheckpoints <= 0 then
                return 0
            else
                let! all = sink.ListCheckpoints streamId

                if all.Length <= policy.KeepCheckpoints then
                    return 0
                else
                    let dropCount = all.Length - policy.KeepCheckpoints
                    let retained = all |> List.skip dropCount
                    let oldestRetained = retained |> List.head

                    // ── ONE STEP, WHERE THE SINK HAS ONE (Phase 1525, M-C2) ──
                    // The two truncations are one act: dropping the ops and
                    // dropping the checkpoints that justified dropping them.
                    // Run separately there is a window between them in which a
                    // reader sees a stream whose surviving records begin above a
                    // checkpoint that still claims to cover them — and, worse, a
                    // failure between them leaves the store in exactly that
                    // state permanently. A sink that implements
                    // `IOpStreamCompactSink` does both atomically; one that does
                    // not keeps the two-call path, which is what it always had.
                    // (Under Fable the probe always answers `None` — it is a type
                    // test, which Fable cannot express — so a Fable host always
                    // takes the two-call path. See `SinkCapabilities`.)
                    match SinkCapabilities.tryCompact (sink :> IOpStreamSink<'Msg>) with
                    | Some compactable ->
                        let! truncatedOps, _droppedCheckpoints = compactable.Compact(streamId, oldestRetained.Sequence)

                        return truncatedOps
                    | None ->
                        let! truncatedOps = sink.TruncateOpsThrough(streamId, oldestRetained.Sequence)
                        // Drop the older checkpoints in the same pass so a future
                        // `ListCheckpoints` reflects the retention policy. The
                        // older checkpoints' snapshots are collapsed into history
                        // once their op prefix is gone — keeping them would only
                        // confuse the next compaction pass.
                        let! _droppedCheckpoints = sink.TruncateCheckpointsBefore(streamId, oldestRetained.Sequence)

                        return truncatedOps
        }
