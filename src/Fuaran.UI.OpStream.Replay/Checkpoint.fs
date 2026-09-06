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
//      current chain head. Computes `SnapshotHash` + `PreviousChainHead`
//      and calls `sink.AppendCheckpoint`.
//
//   2. `CheckpointedReplay` — resolve the nearest checkpoint ≤ target and
//      replay only the tail. Verifies the checkpoint→tail boundary up
//      front (first tail record's `PreviousHash` must equal
//      `checkpoint.PreviousChainHead`); falls back to replay-from-genesis
//      when no checkpoint exists in range.
//
//   3. `Compaction` — apply a `CompactionPolicy` to a stream. Walks
//      checkpoints, keeps the last K, truncates ops with sequence ≤
//      oldest-retained-checkpoint.Sequence. The hash-chain integrity of
//      the surviving M+1..N segment is preserved because every retained
//      checkpoint's `PreviousChainHead` pins the chain head at its
//      op-index.
//
//  ── WHAT THE CHECKPOINT HASH BINDS (post-412) ──────────────────────────────
//  A snapshot hash is NOT `sha256(canonicalTree)`. Since Phase 412 (A2) it
//  binds the snapshot to its POSITION — `previousChainHead` + `sequence` + the
//  canonical tree in one delimited pre-image — so a real snapshot from one
//  position no longer validates at a different one. `applyFromCheckpoint`
//  additionally anchors `PreviousChainHead` against the actual op-chain,
//  including on the empty-tail path, so a checkpoint is trusted only at a
//  position the verified chain confirms. Both are defence-in-depth against
//  accidental corruption and cross-position substitution, NOT tamper-proofing
//  against a writer who rewrites the whole checkpoint record consistently —
//  that is the signed attestation seam's job. See CRYPTO.md.
//
//  ── FGP 2 / FGP 5 / FGP 6 (post-405 fence) ─────────────────────────────────
//  SHA-256 no longer sits behind a `#if !FABLE_COMPILER` guard: since Phase 405
//  `HashChain` routes through the pure, Fable-safe `Fuaran.UI.Hashing.sha256Hex`
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

module Checkpoint =

    let private currentTimestamp () : DateTimeOffset = DateTimeOffset.UtcNow

    /// Materialise a checkpoint for `tree` at the sink's current latest
    /// sequence. The caller has already applied ops `1..LatestSequence`
    /// to produce `tree`. Computes `SnapshotHash` (via
    /// `HashChain.sha256Hex (CanonicalJson.encodeNode tree)`) and
    /// `PreviousChainHead` (the latest op's `Hash`, or
    /// `HashChain.genesisPreviousHash` for an empty stream), then
    /// persists via `sink.AppendCheckpoint`.
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
                            // LatestSequence reported >0 but the prior record is
                            // missing — sink invariant violation. Best-effort: use
                            // the genesis hash so the checkpoint is still
                            // verifiable against a freshly-rebuilt chain; the
                            // mismatch would surface at replay-from-checkpoint
                            // boundary verification.
                            return HashChain.genesisPreviousHash
                }

            let canonical = CanonicalJson.encodeNode tree
            // Phase 412 (A2): bind the snapshot to its chain position (head + seq),
            // not just its tree — a forged snapshot can no longer self-certify.
            let snapshotHash = HashChain.snapshotHash previousChainHead latest canonical

            let cp: Checkpoint<'Msg> =
                { StreamId = streamId
                  Sequence = latest
                  PreviousChainHead = previousChainHead
                  SnapshotHash = snapshotHash
                  Snapshot = tree
                  Timestamp = currentTimestamp () }

            do! sink.AppendCheckpoint cp
            return cp
        }

    /// Recompute the SnapshotHash of `checkpoint.Snapshot` and compare to
    /// `checkpoint.SnapshotHash`. Pure verification — does not touch the
    /// sink. Returns `Ok ()` on match, `Error (recomputed, stored)` on
    /// mismatch.
    let verifySnapshotHash<'Msg> (checkpoint: Checkpoint<'Msg>) : Result<unit, string * string> =
        let canonical = CanonicalJson.encodeNode checkpoint.Snapshot
        // Phase 412 (A2): recompute the chain-bound hash from the checkpoint's own
        // PreviousChainHead + Sequence, so a swapped (snapshot, seq, head) triple fails.
        let recomputed =
            HashChain.snapshotHash checkpoint.PreviousChainHead checkpoint.Sequence canonical

        if recomputed = checkpoint.SnapshotHash then
            Ok()
        else
            Error(recomputed, checkpoint.SnapshotHash)

module CheckpointedReplay =

    /// Resolve the nearest checkpoint ≤ `targetSequence` (if any) and
    /// replay from there. Falls back to replay-from-genesis when no
    /// checkpoint exists in range — equivalent to `Replay.applyTo
    /// initialTree (sink.Replay(streamId, 1, targetSequence))`.
    ///
    /// Verifies the checkpoint→tail boundary up front: the first tail
    /// record's `PreviousHash` must equal `checkpoint.PreviousChainHead`,
    /// and the retained checkpoint's `SnapshotHash` must recompute against
    /// its `Snapshot`. Either mismatch surfaces a
    /// `CheckpointReplayError` without dispatching to the apply engine.
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
                // Verify the snapshot first — if the stored hash doesn't
                // recompute, the snapshot was tampered with, and replay
                // from it is meaningless.
                match Checkpoint.verifySnapshotHash cp with
                | Error(recomputed, _) ->
                    return Error(CheckpointReplayError.SnapshotHashMismatch(cp.Sequence, cp.SnapshotHash, recomputed))
                | Ok() ->
                    // Tail = ops (cp.Sequence + 1) .. targetSequence.
                    let! tail = sink.Replay(streamId, cp.Sequence + 1, targetSequence)

                    // Verify the checkpoint→tail boundary. When the tail is
                    // empty (target ≤ cp.Sequence), the snapshot IS the answer —
                    // but its `PreviousChainHead` must still be ANCHORED to the
                    // real op-chain (Phase 412 / A2): the pre-412 path returned the
                    // snapshot with no chain link, so a checkpoint claiming a
                    // position it does not hold was trusted. We confirm the head
                    // equals the actual chain head at `cp.Sequence` (record
                    // `cp.Sequence`'s `Hash`, or the genesis hash at sequence 0).
                    // Combined with the position-bound snapshot hash, a checkpoint
                    // is trusted only at a position the verified chain confirms.
                    match tail with
                    | [] ->
                        let! anchor =
                            async {
                                if cp.Sequence = 0 then
                                    return HashChain.genesisPreviousHash
                                else
                                    let! atSeq = sink.Replay(streamId, cp.Sequence, cp.Sequence)

                                    return
                                        match atSeq with
                                        | r :: _ -> r.Hash
                                        | [] -> HashChain.genesisPreviousHash
                            }

                        if anchor <> cp.PreviousChainHead then
                            return
                                Error(CheckpointReplayError.BoundaryMismatch(cp.Sequence, anchor, cp.PreviousChainHead))
                        else
                            return Ok cp.Snapshot
                    | first :: _ when first.PreviousHash <> cp.PreviousChainHead ->
                        return
                            Error(
                                CheckpointReplayError.BoundaryMismatch(
                                    cp.Sequence,
                                    cp.PreviousChainHead,
                                    first.PreviousHash
                                )
                            )
                    | _ ->
                        match Replay.applyTo cp.Snapshot tail with
                        | Ok tree -> return Ok tree
                        | Error e -> return Error(CheckpointReplayError.TailApplyFailed e)
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
