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
//  FGP 2 / FGP 5 / FGP 6. SHA-256 (used to compute snapshot hashes) lives
//  in `Fuaran.UI.OpStream.Abstractions.HashChain` behind a
//  `#if !FABLE_COMPILER` guard — this module is .NET-only by transitive
//  consequence (the entire Replay package is, by design — `Apply.apply`
//  ships on both pipelines, but the persistence wrappers are server-side).
//  The op stream stays the source of truth: checkpoints accelerate
//  replay, they do not replace the chain as the canonical record.
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
                    let! truncatedOps = sink.TruncateOpsThrough(streamId, oldestRetained.Sequence)
                    // Drop the older checkpoints in the same pass so a future
                    // `ListCheckpoints` reflects the retention policy. The
                    // older checkpoints' snapshots are collapsed into history
                    // once their op prefix is gone — keeping them would only
                    // confuse the next compaction pass.
                    let! _droppedCheckpoints = sink.TruncateCheckpointsBefore(streamId, oldestRetained.Sequence)

                    return truncatedOps
        }
