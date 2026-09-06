namespace Fuaran.UI.OpStream.InMemory

open System.Collections.Generic
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  InMemorySink — per-process Dictionary-backed IOpStreamSink<'Msg>.
//
//  Useful for tests + ephemeral preview environments. Concurrent appends to
//  the same stream are serialised via a per-stream lock so the
//  `(StreamId, Sequence)` uniqueness invariant holds under contention.
//  Records are stored as typed OpRecord<'Msg> values — no JSON round-trip,
//  no host codec needed.
//
//  Also implements IOpStreamCheckpointSink<'Msg>. Checkpoints
//  are stored as typed Checkpoint<'Msg> values in a parallel
//  Dictionary<string, ResizeArray<Checkpoint<'Msg>>>; TruncateOpsThrough
//  removes ops from the live stream's ResizeArray in-place. No codec
//  needed for InMemory either — snapshots round-trip as typed values.
//
//  ── VERIFY ON LOAD (Phase 793) ─────────────────────────────────────────────
//  `Replay` re-verifies the hash chain of the segment it returns, rather than
//  trusting it because this sink wrote it. That is a weaker-looking case than
//  the durable sinks — the records never leave the process — and it is still
//  the right default for two reasons. The records are handed out as MUTABLE
//  references into a shared store that a host is free to hold, re-add or
//  rebuild; and this sink is the reference implementation every host reads to
//  learn what a sink owes its caller, so a version of it that skips the check
//  teaches the wrong contract. A host that has measured the cost and does not
//  want it passes `LoadVerification.Off` explicitly — see CRYPTO.md.
//
//  ── VERIFY ON WRITE (Phase 1525) ───────────────────────────────────────────
//  And the mirror of it: an offered record's `Hash` is recomputed and its
//  `PreviousHash` required to name this stream's head BEFORE it enters the log.
//  Before this, a broken record was accepted here and refused by `Replay` on
//  every subsequent read — one bad write making the stream permanently
//  unreadable, and the refusal naming the record rather than the writer. Same
//  named-opt-out shape as the read side: `WriteAdmission.Off`.
// ============================================================================

/// Per-stream bookkeeping: the records plus O(1)-maintained metadata so the
/// hot methods avoid full-bucket scans. `MaxSequence` makes LatestSequence
/// O(1); `Seen` makes Append's duplicate check O(1); `Ascending` lets Replay
/// skip the sort when records arrived in ascending sequence order (the common
/// case — hosts append at LatestSequence + 1). Without this, persisting N ops
/// (LatestSequence + Replay per op) was O(N²).
type private StreamState<'Msg> =
    {
        Records: ResizeArray<OpRecord<'Msg>>
        Seen: HashSet<int>
        /// Phase 1485 — the key map that sits BESIDE the log, holding the receipt
        /// the first append under each invocation key produced. Nothing else
        /// reads it, so an unkeyed append is exactly as cheap as it was.
        Keys: Dictionary<string, AppendReceipt>
        mutable MaxSequence: int
        /// Phase 1485 — the chain hash at `MaxSequence`, maintained on append so
        /// `Head` is O(1) like `LatestSequence`. Scanning for it instead would
        /// put the O(N²) back that the metadata above exists to remove.
        mutable HeadHash: string
        mutable Ascending: bool
    }

    static member Create() : StreamState<'Msg> =
        { Records = ResizeArray<OpRecord<'Msg>>()
          Seen = HashSet<int>()
          Keys = Dictionary<string, AppendReceipt>()
          MaxSequence = 0
          HeadHash = HashChain.genesisPreviousHash
          Ascending = true }

/// A copy of one stream's whole mutable state, taken before a batch append so
/// the batch can be undone (Phase 1525). This store has no transaction of its
/// own; snapshot-and-restore is what makes `AppendAll` all-or-nothing, and it
/// is cheaper than the class of bug a half-applied import leaves behind.
type private StreamSnapshot<'Msg> =
    { Records: OpRecord<'Msg> array
      Seen: int array
      Keys: (string * AppendReceipt) array
      MaxSequence: int
      HeadHash: string
      Ascending: bool }

type InMemorySink<'Msg>(loadVerification: LoadVerification, writeAdmission: WriteAdmission) =

    let streams = Dictionary<string, StreamState<'Msg>>()
    let checkpoints = Dictionary<string, ResizeArray<Checkpoint<'Msg>>>()
    let lockObj = obj ()

    /// Verify a segment about to be handed to a caller. `IOpStreamSink` has no
    /// error channel — `Replay` returns a bare list — so a broken chain is
    /// refused the way this sink already refuses a duplicate sequence: by name,
    /// with an `invalidOp` that says which stream and which record.
    let verifyLoaded (streamId: string) (records: OpRecord<'Msg> list) : OpRecord<'Msg> list =
        match Verify.loaded loadVerification records with
        | Ok() -> records
        | Error e -> invalidOp ("InMemorySink: " + Verify.describe streamId e)

    let getOrCreateStream (streamId: string) : StreamState<'Msg> =
        match streams.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh = StreamState<'Msg>.Create()
            streams[streamId] <- fresh
            fresh

    /// The one place a record enters the log. Callers hold `lockObj`. Returns
    /// the receipt so the keyed and compare-and-append paths (Phase 1485) name
    /// the record they just wrote without re-reading the store — and so all
    /// three paths share ONE admission check, ONE duplicate-sequence guard and
    /// ONE metadata update, which is what keeps `Ascending` / `MaxSequence` /
    /// `HeadHash` from drifting between them.
    let appendLocked (record: OpRecord<'Msg>) : AppendReceipt =
        let st = getOrCreateStream record.StreamId

        // ── ADMISSION (Phase 1525, finding H-16) ────────────────────────────
        // The head and latest sequence come from the SAME locked state the
        // insert mutates, so this is not a second race in front of the one the
        // lock exists to remove. An empty stream reads as (genesis, 0), so the
        // first record of a stream needs no special case.
        let admissionHead =
            if st.Records.Count = 0 then
                HashChain.genesisPreviousHash
            else
                st.HeadHash

        match Verify.admission writeAdmission admissionHead st.MaxSequence record with
        | Error e -> invalidOp (Verify.describeAdmission "InMemorySink" record.StreamId e)
        | Ok() -> ()

        // Duplicate (StreamId, Sequence) is a structural defect — the
        // host should have queried LatestSequence + 1 before assigning.
        // `HashSet.Add` returns false (without mutating) when present, so
        // it is both the O(1) membership check and the insert.
        if not (st.Seen.Add record.Sequence) then
            invalidOp (
                sprintf
                    "InMemorySink: duplicate (StreamId=%s, Sequence=%d) — sinks reject overwrites."
                    record.StreamId
                    record.Sequence
            )

        // Records arrive ascending iff each new sequence exceeds the max so
        // far; one out-of-order append flips Replay back to an explicit sort.
        if st.Records.Count > 0 && record.Sequence <= st.MaxSequence then
            st.Ascending <- false

        if record.Sequence > st.MaxSequence then
            st.MaxSequence <- record.Sequence
            st.HeadHash <- record.Hash

        st.Records.Add record

        { StreamId = record.StreamId
          Sequence = record.Sequence
          Hash = record.Hash }

    /// The chain head as `IOpStreamCasSink.Head` reports it: the hash at
    /// `MaxSequence`, or the genesis anchor for a stream with no records.
    let headLocked (streamId: string) : string =
        match streams.TryGetValue streamId with
        | false, _ -> HashChain.genesisPreviousHash
        | true, st when st.Records.Count = 0 -> HashChain.genesisPreviousHash
        | true, st -> st.HeadHash

    let getOrCreateCheckpointBucket (streamId: string) : ResizeArray<Checkpoint<'Msg>> =
        match checkpoints.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh = ResizeArray<Checkpoint<'Msg>>()
            checkpoints[streamId] <- fresh
            fresh

    /// Remove the ops at or below `throughSequence`. Callers hold `lockObj`.
    /// Hoisted out of the interface member (Phase 1525) so the atomic `Compact`
    /// runs exactly this code rather than a second copy of it.
    let truncateOpsLocked (streamId: string) (throughSequence: int) : int =
        match streams.TryGetValue streamId with
        | false, _ -> 0
        | true, st ->
            // Rebuild in one O(n) pass (the prior per-element
            // ResizeArray.Remove loop was O(n²)), keeping Seen +
            // MaxSequence consistent. Removing a sequence-prefix
            // preserves the Ascending property, so it is left as-is.
            let kept = ResizeArray<OpRecord<'Msg>>()
            let mutable removed = 0

            for r in st.Records do
                if r.Sequence <= throughSequence then
                    st.Seen.Remove r.Sequence |> ignore
                    removed <- removed + 1
                else
                    kept.Add r

            st.Records.Clear()
            st.Records.AddRange kept

            // Phase 1525 (finding L-A15) — the invocation-key index is compacted
            // WITH the records it names. Left behind, a key whose record has
            // been truncated answers a later `AppendKeyed` with
            // `Duplicate receipt` naming a sequence the stream no longer holds:
            // the caller is told its op is already durable, and `Replay` at that
            // address returns nothing. A stale index entry is worse than no
            // entry, because it is indistinguishable from a live one.
            let staleKeys =
                st.Keys
                |> Seq.filter (fun kv -> kv.Value.Sequence <= throughSequence)
                |> Seq.map _.Key
                |> List.ofSeq

            for key in staleKeys do
                st.Keys.Remove key |> ignore

            st.MaxSequence <-
                if st.Records.Count = 0 then
                    0
                else
                    st.Records |> Seq.map _.Sequence |> Seq.max

            // The head moves only if truncation emptied the stream — a
            // prefix removal leaves the record at MaxSequence in place.
            // Recomputed rather than assumed, so a policy that ever
            // removes a suffix cannot leave a head naming a gone record.
            st.HeadHash <-
                if st.Records.Count = 0 then
                    HashChain.genesisPreviousHash
                else
                    st.Records |> Seq.maxBy _.Sequence |> _.Hash

            removed

    /// Remove the checkpoints below `beforeSequence`. Callers hold `lockObj`.
    let truncateCheckpointsLocked (streamId: string) (beforeSequence: int) : int =
        match checkpoints.TryGetValue streamId with
        | false, _ -> 0
        | true, bucket ->
            let toRemove =
                bucket |> Seq.filter (fun c -> c.Sequence < beforeSequence) |> Seq.toList

            for c in toRemove do
                bucket.Remove c |> ignore

            toRemove.Length

    /// Snapshot every stream a batch is about to touch, and remember which of
    /// them did not exist, so a refused batch can be undone exactly.
    let snapshotForBatch (records: OpRecord<'Msg> list) : (string * StreamSnapshot<'Msg> option) list =
        records
        |> List.map _.StreamId
        |> List.distinct
        |> List.map (fun id ->
            match streams.TryGetValue id with
            | true, st ->
                id,
                Some
                    { Records = st.Records.ToArray()
                      Seen = Seq.toArray st.Seen
                      Keys = st.Keys |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toArray
                      MaxSequence = st.MaxSequence
                      HeadHash = st.HeadHash
                      Ascending = st.Ascending }
            | false, _ -> id, None)

    let restoreFromBatchSnapshot (snapshots: (string * StreamSnapshot<'Msg> option) list) : unit =
        for id, snapshot in snapshots do
            match snapshot with
            | None ->
                // The stream did not exist before the batch; the batch created
                // it, so undoing the batch removes it.
                streams.Remove id |> ignore
            | Some snap ->
                let st = streams[id]
                st.Records.Clear()
                st.Records.AddRange snap.Records
                st.Seen.Clear()

                for s in snap.Seen do
                    st.Seen.Add s |> ignore

                st.Keys.Clear()

                for key, receipt in snap.Keys do
                    st.Keys[key] <- receipt

                st.MaxSequence <- snap.MaxSequence
                st.HeadHash <- snap.HeadHash
                st.Ascending <- snap.Ascending

    /// Default construction verifies the whole loaded segment (Phase 793) and
    /// checks every offered record before admitting it (Phase 1525).
    new() = InMemorySink<'Msg>(LoadVerification.Full, WriteAdmission.Full)

    /// Read-path mode named, write admission left at its `Full` default. This is
    /// the pre-1525 one-argument constructor, kept by name so every existing
    /// call site compiles unchanged.
    new(loadVerification: LoadVerification) = InMemorySink<'Msg>(loadVerification, WriteAdmission.Full)

    // The BASE interface is implemented explicitly rather than through the
    // checkpoint extension's block, because Phase 1485 gives this type three
    // interfaces that all inherit it and F# then requires the shared base to be
    // implemented once, by name (FS0363). Nothing about the four members moved.
    interface IOpStreamSink<'Msg> with

        member _.Append(record: OpRecord<'Msg>) : Async<unit> =
            async { lock lockObj (fun () -> appendLocked record |> ignore) }

        member _.Replay(streamId: string, fromSequence: int, toSequence: int) : Async<OpRecord<'Msg> list> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> []
                        | true, st ->
                            let inRange =
                                st.Records
                                |> Seq.filter (fun r -> r.Sequence >= fromSequence && r.Sequence <= toSequence)

                            // Insertion order == ascending sequence order in the common
                            // case, so a contiguous filter is already sorted; only sort
                            // when an out-of-order append actually occurred.
                            let ordered =
                                if st.Ascending then
                                    inRange
                                else
                                    inRange |> Seq.sortBy _.Sequence

                            ordered |> List.ofSeq |> verifyLoaded streamId)
            }

        member _.LatestSequence(streamId: string) : Async<int> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> 0
                        | true, st when st.Records.Count = 0 -> 0
                        | true, st -> st.MaxSequence)
            }

        member _.Streams() : Async<string list> =
            async { return lock lockObj (fun () -> streams.Keys |> List.ofSeq) }

    interface IOpStreamCheckpointSink<'Msg> with

        member _.AppendCheckpoint(checkpoint: Checkpoint<'Msg>) : Async<unit> =
            async {
                lock lockObj (fun () ->
                    let bucket = getOrCreateCheckpointBucket checkpoint.StreamId
                    let dup = bucket |> Seq.exists (fun c -> c.Sequence = checkpoint.Sequence)

                    if dup then
                        invalidOp (
                            sprintf
                                "InMemorySink: duplicate checkpoint (StreamId=%s, Sequence=%d) — sinks reject overwrites."
                                checkpoint.StreamId
                                checkpoint.Sequence
                        )

                    bucket.Add checkpoint)
            }

        member _.LatestCheckpointAtOrBefore(streamId: string, upToSequence: int) : Async<Checkpoint<'Msg> option> =
            async {
                return
                    lock lockObj (fun () ->
                        match checkpoints.TryGetValue streamId with
                        | false, _ -> None
                        | true, bucket when bucket.Count = 0 -> None
                        | true, bucket ->
                            bucket
                            |> Seq.filter (fun c -> c.Sequence <= upToSequence)
                            |> Seq.sortByDescending _.Sequence
                            |> Seq.tryHead)
            }

        member _.ListCheckpoints(streamId: string) : Async<Checkpoint<'Msg> list> =
            async {
                return
                    lock lockObj (fun () ->
                        match checkpoints.TryGetValue streamId with
                        | false, _ -> []
                        | true, bucket -> bucket |> Seq.sortBy _.Sequence |> List.ofSeq)
            }

        member _.TruncateOpsThrough(streamId: string, throughSequence: int) : Async<int> =
            async { return lock lockObj (fun () -> truncateOpsLocked streamId throughSequence) }

        member _.TruncateCheckpointsBefore(streamId: string, beforeSequence: int) : Async<int> =
            async { return lock lockObj (fun () -> truncateCheckpointsLocked streamId beforeSequence) }

    // ── Phase 1485: the two contracts a durable port owes a consumer ────────
    // Both run inside the SAME `lockObj` the plain append takes, so the
    // read-then-write each performs is atomic against a concurrent writer.
    // Doing the check outside the lock would give a compare-and-append that
    // reads a head another thread has already moved — which is not a
    // compare-and-append at all, just a slower race.

    interface IOpStreamCasSink<'Msg> with

        member _.Head(streamId: string) : Async<string> =
            async { return lock lockObj (fun () -> headLocked streamId) }

        member _.AppendIf(record: OpRecord<'Msg>, expectedHead: string) : Async<CasAppendOutcome> =
            async {
                return
                    lock lockObj (fun () ->
                        let actual = headLocked record.StreamId

                        if actual <> expectedHead then
                            CasAppendOutcome.StaleHead(expectedHead, actual)
                        else
                            CasAppendOutcome.Appended(appendLocked record))
            }

    interface IOpStreamKeyedSink<'Msg> with

        member _.AppendKeyed(record: OpRecord<'Msg>, invocationKey: string) : Async<KeyedAppendOutcome> =
            async {
                return
                    lock lockObj (fun () ->
                        let st = getOrCreateStream record.StreamId

                        match st.Keys.TryGetValue invocationKey with
                        | true, receipt ->
                            // The retry contract: the FIRST receipt, unchanged, and nothing
                            // written. The second call's `record` is not consulted at all —
                            // a caller that rebuilt it after a lost acknowledgement carries a
                            // fresh timestamp and a re-derived sequence, and must still be
                            // told about the record it already has.
                            KeyedAppendOutcome.Duplicate receipt
                        | false, _ ->
                            let receipt = appendLocked record
                            st.Keys[invocationKey] <- receipt
                            KeyedAppendOutcome.Appended receipt)
            }

    // ── Phase 1525: the atomic batch append + the atomic retention step ─────

    interface IOpStreamBatchSink<'Msg> with

        member _.AppendAll(records: OpRecord<'Msg> list) : Async<unit> =
            async {
                lock lockObj (fun () ->
                    let snapshots = snapshotForBatch records

                    try
                        for record in records do
                            appendLocked record |> ignore
                    with _ ->
                        restoreFromBatchSnapshot snapshots
                        reraise ())
            }

    interface IOpStreamCompactSink<'Msg> with

        member _.Compact(streamId: string, throughSequence: int) : Async<int * int> =
            async {
                return
                    // ONE lock over both deletions. As two calls there is a window
                    // in which the ops are gone and the checkpoints that justified
                    // removing them are not — a reader inside it sees a stream
                    // whose surviving records begin above a checkpoint that still
                    // claims to cover them.
                    lock lockObj (fun () ->
                        let ops = truncateOpsLocked streamId throughSequence
                        let cps = truncateCheckpointsLocked streamId throughSequence
                        ops, cps)
            }

module InMemorySink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    /// Verifies the whole loaded segment on every `Replay` (Phase 793) and
    /// checks every offered record on `Append` (Phase 1525).
    let create<'Msg> () : IOpStreamSink<'Msg> = upcast InMemorySink<'Msg>()

    /// `create` under an explicit read-path verification mode. Naming a cheaper
    /// mode here is the ONLY way to get one — there is no silent fast path.
    let createWith<'Msg> (loadVerification: LoadVerification) : IOpStreamSink<'Msg> =
        upcast InMemorySink<'Msg>(loadVerification)

    /// `create` under explicit read-path AND write-path modes (Phase 1525).
    /// `WriteAdmission.Off` is what a caller deliberately writing a record the
    /// admission checks would refuse — a corruption fixture, a store being
    /// rebuilt out of order and verified afterwards — must name.
    let createWithModes<'Msg>
        (loadVerification: LoadVerification)
        (writeAdmission: WriteAdmission)
        : IOpStreamSink<'Msg> =
        upcast InMemorySink<'Msg>(loadVerification, writeAdmission)

    /// Convenience factory returning the checkpoint-aware sink
    /// interface. The underlying instance is the same class — InMemorySink
    /// always implements `IOpStreamCheckpointSink<'Msg>`. Use this factory
    /// when the consumer needs the checkpoint methods on the static surface.
    let createWithCheckpoints<'Msg> () : IOpStreamCheckpointSink<'Msg> = upcast InMemorySink<'Msg>()

    /// `createWithCheckpoints` under an explicit read-path verification mode.
    let createWithCheckpointsAnd<'Msg> (loadVerification: LoadVerification) : IOpStreamCheckpointSink<'Msg> =
        upcast InMemorySink<'Msg>(loadVerification)

    /// `createWithCheckpoints` under explicit read-path AND write-path modes.
    let createWithCheckpointsAndModes<'Msg>
        (loadVerification: LoadVerification)
        (writeAdmission: WriteAdmission)
        : IOpStreamCheckpointSink<'Msg> =
        upcast InMemorySink<'Msg>(loadVerification, writeAdmission)
