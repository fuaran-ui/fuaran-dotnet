namespace Fuaran.UI.ServerDriven

#if !FABLE_COMPILER
open System
open System.Collections.Generic
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  Server-driven session durability (Phase 155, Wave 18).
//
//  Phase 152's driver holds the per-connection model in SERVER MEMORY, so a
//  restart / deploy / scale-out loses every in-flight session — unacceptable for
//  the business / regulated / multi-tenant app classes the server-driven tier
//  targets. This module closes that gap by reusing primitives that are ALREADY
//  shipped: the `Fuaran.UI.OpStream` `Checkpoint<'Msg>` (a hash-chain-linked tree
//  snapshot) + the `OpRecord<'Msg>` journal + `CheckpointedReplay` + `Verify`.
//
//  ── The model: tree-level durability ───────────────────────────────────────
//  A live session's client-visible state IS its rendered `Node` tree. We make it
//  durable by (a) appending every applied `TreeOp` to a hash-chained journal and
//  (b) periodically checkpointing the rendered tree at the current op-sequence.
//  Reconstruction = load the latest checkpoint ≤ head + replay the journal tail
//  (cp.Sequence+1 .. head), folding through the apply engine — the exact
//  `Fuaran.UI.OpStream.Replay.CheckpointedReplay` recipe, run through the
//  `IFuaranSessionStore` seam so the same code path serves an in-memory store
//  (zero-config, single-node) and a durable / shared store (Sqlite today, a
//  future Redis) without a driver change.
//
//  ── Integrity before replay (default-deny by shape) ─────────────────────────
//  Reconstruction verifies the checkpoint's snapshot hash AND walks the journal
//  tail's hash chain (contiguous sequences + `PreviousHash` links + recomputed
//  `Hash`) BEFORE folding. A tampered snapshot or a forked / corrupt journal
//  segment surfaces an explicit `SessionReconstructError` — never a silent bad
//  resume. (FGP 5: the op stream is the source of truth; durability accelerates,
//  it never weakens, the chain.)
//
//  ── Portability posture (the six rules) ─────────────────────────────────────
//  `IFuaranSessionStore<'Msg>` is a pure storage port — stateless between calls,
//  identity-by-value (`string` sessionId == `OpRecord/Checkpoint.StreamId`). The
//  default in-memory impl needs no package dependency; `overSink` adapts ANY
//  shipped `IOpStreamCheckpointSink<'Msg>` (the Sqlite durable / shared backend),
//  so the host chooses the backend without the core knowing which.
//
//  Server-only by construction: SHA-256 (the hash chain) + the OpStream replay
//  engine are .NET-only, so the whole file sits under `#if !FABLE_COMPILER`
//  (the same posture as `HashChain` / `Replay.Checkpoint`). The Fable-portable
//  wire types — `DomPatch` / `ClientEffect` / `Frame` — are untouched.
// ============================================================================

/// The durable per-session storage port. `sessionId` is the value identity (it
/// equals the `StreamId` carried on the `Checkpoint` / `OpRecord` it persists).
/// Stateless between calls — every method takes the `sessionId` explicitly, so a
/// single store instance backs every session (and, when the backend is shared,
/// every node).
type IFuaranSessionStore<'Msg> =
    /// Persist a checkpoint of the session's rendered tree at its current
    /// op-sequence. The caller builds the `Checkpoint` (snapshot hash +
    /// chain-head back-link) from the live connection's tracked head.
    abstract member Checkpoint: sessionId: string * checkpoint: Checkpoint<'Msg> -> Async<unit>

    /// The most recent checkpoint for the session, or `None` if none exists yet.
    abstract member LoadLatest: sessionId: string -> Async<Checkpoint<'Msg> option>

    /// Append one applied op to the session's hash-chained journal.
    abstract member AppendOp: sessionId: string * record: OpRecord<'Msg> -> Async<unit>

    /// Every journal record with `Sequence > sequence`, ascending — the tail to
    /// replay from a checkpoint (or from genesis when `sequence = 0`).
    abstract member OpsSince: sessionId: string * sequence: int -> Async<OpRecord<'Msg> list>

/// Zero-config in-process session store — the single-node default. Backs the
/// session journal + checkpoints in memory (thread-safe); a process restart
/// loses it, so this is the "current 152 behaviour preserved" default, NOT the
/// restart-durable backend. Use `SessionStore.overSink` with a Sqlite sink for
/// restart durability, or share one instance by reference to model a shared
/// multi-node store in a single-process test.
type InMemorySessionStore<'Msg>() =
    let gate = obj ()
    let ops = Dictionary<string, ResizeArray<OpRecord<'Msg>>>()
    let checkpoints = Dictionary<string, ResizeArray<Checkpoint<'Msg>>>()

    let opsFor (sessionId: string) : ResizeArray<OpRecord<'Msg>> =
        match ops.TryGetValue sessionId with
        | true, v -> v
        | _ ->
            let v = ResizeArray<OpRecord<'Msg>>()
            ops[sessionId] <- v
            v

    let checkpointsFor (sessionId: string) : ResizeArray<Checkpoint<'Msg>> =
        match checkpoints.TryGetValue sessionId with
        | true, v -> v
        | _ ->
            let v = ResizeArray<Checkpoint<'Msg>>()
            checkpoints[sessionId] <- v
            v

    interface IFuaranSessionStore<'Msg> with
        member _.Checkpoint(sessionId, checkpoint) =
            async { lock gate (fun () -> (checkpointsFor sessionId).Add checkpoint) }

        member _.LoadLatest(sessionId) =
            async {
                return
                    lock gate (fun () ->
                        match checkpoints.TryGetValue sessionId with
                        | true, v when v.Count > 0 -> Some(v |> Seq.maxBy _.Sequence)
                        | _ -> None)
            }

        member _.AppendOp(sessionId, record) =
            async { lock gate (fun () -> (opsFor sessionId).Add record) }

        member _.OpsSince(sessionId, sequence) =
            async {
                return
                    lock gate (fun () ->
                        match ops.TryGetValue sessionId with
                        | true, v ->
                            v
                            |> Seq.filter (fun r -> r.Sequence > sequence)
                            |> Seq.sortBy _.Sequence
                            |> List.ofSeq
                        | _ -> [])
            }

module SessionStore =
    /// The zero-config single-node default (in-memory, lost on restart).
    let inMemory<'Msg> () : IFuaranSessionStore<'Msg> =
        InMemorySessionStore<'Msg>() :> IFuaranSessionStore<'Msg>

    /// Adapt any shipped `IOpStreamCheckpointSink<'Msg>` (the Sqlite durable /
    /// shared backend, a future Redis) as a session store. `sessionId` maps to
    /// the sink's `streamId`; the caller MUST set `StreamId = sessionId` on every
    /// `Checkpoint` / `OpRecord` it persists (the `SessionJournal` helper does).
    let overSink<'Msg> (sink: IOpStreamCheckpointSink<'Msg>) : IFuaranSessionStore<'Msg> =
        { new IFuaranSessionStore<'Msg> with
            member _.Checkpoint(_sessionId, checkpoint) = sink.AppendCheckpoint checkpoint

            member _.LoadLatest(sessionId) =
                sink.LatestCheckpointAtOrBefore(sessionId, Int32.MaxValue)

            member _.AppendOp(_sessionId, record) = sink.Append record

            member _.OpsSince(sessionId, sequence) =
                sink.Replay(sessionId, sequence + 1, Int32.MaxValue) }

module SessionJournal =
    /// Append a step's applied `TreeOp`s to the session journal as a contiguous
    /// hash-chained run, threading `headHash` and the op `sequence`. Returns the
    /// new `(headHash, sequence)` — the live connection tracks these so it can
    /// later build a checkpoint and verify reconstruction. An empty op list is a
    /// no-op (head unchanged). Every record's `StreamId` is set to `sessionId` so
    /// `SessionStore.overSink` routes it to the right stream.
    let append<'Msg>
        (store: IFuaranSessionStore<'Msg>)
        (sessionId: string)
        (userId: string)
        (timestamp: DateTimeOffset)
        (headHash: string)
        (sequence: int)
        (ops: TreeOp<'Msg> list)
        : Async<string * int> =
        async {
            let mutable head = headHash
            let mutable seq = sequence
            let actor = Actor.ofLegacyString userId

            for op in ops do
                seq <- seq + 1
                // Phase 406: promptId (None here) + resultEnvelope are folded into the chain hash.
                let hash =
                    HashChain.computeHash head op seq timestamp actor None OpResultEnvelope.Success

                let record: OpRecord<'Msg> =
                    { StreamId = sessionId
                      Sequence = seq
                      PreviousHash = head
                      Hash = hash
                      Op = op
                      PromptId = None
                      Actor = actor
                      Timestamp = timestamp
                      ResultEnvelope = OpResultEnvelope.Success }

                do! store.AppendOp(sessionId, record)
                head <- hash

            return head, seq
        }

    /// Build (but do not persist) a checkpoint of `tree` at op-sequence `sequence`
    /// with chain head `headHash`. Mirrors `Replay.Checkpoint.create` but uses the
    /// connection's tracked head instead of querying a sink — the snapshot hash is
    /// `sha256Hex (CanonicalJson.encodeNode tree)` and `PreviousChainHead` is the
    /// head at `sequence`.
    let makeCheckpoint<'Msg>
        (sessionId: string)
        (sequence: int)
        (headHash: string)
        (timestamp: DateTimeOffset)
        (tree: Node<'Msg>)
        : Checkpoint<'Msg> =
        // Phase 412 (A2): chain-bound snapshot hash (head + seq + tree), not tree alone.
        let snapshotHash =
            HashChain.snapshotHash headHash sequence (CanonicalJson.encodeNode tree)

        { StreamId = sessionId
          Sequence = sequence
          PreviousChainHead = headHash
          SnapshotHash = snapshotHash
          Snapshot = tree
          Timestamp = timestamp }

/// Why session reconstruction refused to resume — every arm is an explicit
/// integrity failure surfaced BEFORE the apply engine runs (default-deny).
[<RequireQualifiedAccess>]
type SessionReconstructError =
    /// The retained checkpoint's `SnapshotHash` does not recompute against its
    /// `Snapshot` — the snapshot was tampered with.
    | SnapshotHashMismatch of checkpointSequence: int * expected: string * actual: string
    /// The journal tail's hash chain is broken (out-of-order sequence, a
    /// `PreviousHash` that does not link, or a record whose `Hash` does not
    /// recompute) — a forked / corrupt / tampered segment.
    | JournalIntegrity of VerificationError
    /// The tail records folded cleanly past verification but the apply engine
    /// rejected one (a structural defect in the recorded op).
    | ReplayFailed of ReplayError

module SessionReplay =
    /// Walk a journal tail starting from `(startPreviousHash, startSequence)`,
    /// asserting contiguous sequence, `PreviousHash` linkage, and recomputed
    /// `Hash` for every record. It catches the checkpoint→tail boundary too,
    /// since the first record's `PreviousHash` must equal the checkpoint's
    /// `PreviousChainHead`.
    ///
    /// Phase 793 — this was a hand-rolled walker, written because "the shipped
    /// `Verify.chain` hardcodes a genesis start". That gap is now closed at the
    /// source: `Verify.segmentFrom` is the anchored segment verifier in the
    /// abstractions package, delegating to Core's `firstChainBreakWith` like
    /// `Verify.chain` does. One definition of chain integrity, per the Phase-411
    /// F14 posture — the duplicate walker is retired, and the error shapes are
    /// unchanged (same cases, same argument order).
    let private verifyTail<'Msg>
        (startPreviousHash: string)
        (startSequence: int)
        (records: OpRecord<'Msg> list)
        : Result<unit, VerificationError> =
        Verify.segmentFrom startPreviousHash startSequence records

    /// Reconstruct a session's current rendered tree from durable storage:
    /// load the latest checkpoint, verify its snapshot hash, verify + replay the
    /// journal tail, and return the reconstructed `(tree, headSequence, headHash)`.
    /// With no checkpoint the whole journal is verified + replayed from
    /// `genesisTree`. `headHash` is the chain head at `headSequence` — a resumed
    /// connection threads it so its next journaled op links the chain unbroken.
    /// Works against an in-memory, a durable, or a SHARED store identically — a
    /// reconnect on a different node reconstructs from the shared backend.
    let reconstruct<'Msg>
        (store: IFuaranSessionStore<'Msg>)
        (sessionId: string)
        (genesisTree: Node<'Msg>)
        : Async<Result<Node<'Msg> * int * string, SessionReconstructError>> =
        async {
            let! checkpointOpt = store.LoadLatest sessionId

            match checkpointOpt with
            | None ->
                // No checkpoint — verify + replay the whole journal from genesis.
                let! all = store.OpsSince(sessionId, 0)

                match verifyTail HashChain.genesisPreviousHash 1 all with
                | Error e -> return Error(SessionReconstructError.JournalIntegrity e)
                | Ok() ->
                    // Already verified above against the genesis anchor — the
                    // stronger check, so `applyToUnverified` skips a second walk.
                    match Replay.applyToUnverified genesisTree all with
                    | Ok tree ->
                        let headSeq = all |> List.tryLast |> Option.map _.Sequence |> Option.defaultValue 0

                        let headHash =
                            all
                            |> List.tryLast
                            |> Option.map _.Hash
                            |> Option.defaultValue HashChain.genesisPreviousHash

                        return Ok(tree, headSeq, headHash)
                    | Error e -> return Error(SessionReconstructError.ReplayFailed e)
            | Some checkpoint ->
                // Snapshot tamper check first — a bad snapshot makes the tail
                // meaningless.
                match Checkpoint.verifySnapshotHash checkpoint with
                | Error(recomputed, stored) ->
                    return Error(SessionReconstructError.SnapshotHashMismatch(checkpoint.Sequence, stored, recomputed))
                | Ok() ->
                    let! tail = store.OpsSince(sessionId, checkpoint.Sequence)

                    match tail with
                    // No tail — the checkpoint IS the head; its `PreviousChainHead`
                    // is the chain head at `checkpoint.Sequence`.
                    | [] -> return Ok(checkpoint.Snapshot, checkpoint.Sequence, checkpoint.PreviousChainHead)
                    | _ ->
                        match verifyTail checkpoint.PreviousChainHead (checkpoint.Sequence + 1) tail with
                        | Error e -> return Error(SessionReconstructError.JournalIntegrity e)
                        | Ok() ->
                            // Already verified above against the checkpoint's own
                            // chain head — stronger than the anchor-relative check
                            // `applyTo` would repeat.
                            match Replay.applyToUnverified checkpoint.Snapshot tail with
                            | Ok tree ->
                                let last = List.last tail
                                return Ok(tree, last.Sequence, last.Hash)
                            | Error e -> return Error(SessionReconstructError.ReplayFailed e)
        }

/// In-memory session-lifecycle bookkeeping: tracks each live session's last
/// activity so abandoned sessions can be garbage-collected, and supports
/// explicit session-end. The DURABLE journal is retained in the store (per the
/// store's retention policy) — this registry only governs the in-process notion
/// of "which sessions are still live", which is the multi-node sticky-routing /
/// idle-timeout concern. Thread-safe.
type SessionRegistry() =
    let gate = obj ()
    let lastActive = Dictionary<string, DateTimeOffset>()

    /// Record activity on a session (call on each handled event + on connect).
    member _.Touch(sessionId: string, nowUtc: DateTimeOffset) =
        lock gate (fun () -> lastActive[sessionId] <- nowUtc)

    /// The session ids the registry currently considers live.
    member _.Active: string list = lock gate (fun () -> lastActive.Keys |> List.ofSeq)

    /// Explicit session-end — drops the session from the live set. The caller
    /// flushes a final checkpoint first (the connection's `CheckpointNow`) when
    /// the session's journal must remain replayable per the app's retention
    /// policy (load-bearing for the regulated / audited class).
    member _.End(sessionId: string) =
        lock gate (fun () -> lastActive.Remove sessionId |> ignore)

    /// Garbage-collect sessions idle for at least `idleFor` as of `nowUtc`,
    /// returning the collected session ids. The store keeps their journals; only
    /// the in-process live-set entry is dropped.
    member _.GarbageCollect(idleFor: TimeSpan, nowUtc: DateTimeOffset) : string list =
        lock gate (fun () ->
            let dead =
                lastActive
                |> Seq.filter (fun kv -> nowUtc - kv.Value >= idleFor)
                |> Seq.map _.Key
                |> List.ofSeq

            for sessionId in dead do
                lastActive.Remove sessionId |> ignore

            dead)
#endif
