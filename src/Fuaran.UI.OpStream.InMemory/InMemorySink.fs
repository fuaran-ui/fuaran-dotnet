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
// ============================================================================

/// Per-stream bookkeeping: the records plus O(1)-maintained metadata so the
/// hot methods avoid full-bucket scans. `MaxSequence` makes LatestSequence
/// O(1); `Seen` makes Append's duplicate check O(1); `Ascending` lets Replay
/// skip the sort when records arrived in ascending sequence order (the common
/// case — hosts append at LatestSequence + 1). Without this, persisting N ops
/// (LatestSequence + Replay per op) was O(N²).
type private StreamState<'Msg> =
    { Records: ResizeArray<OpRecord<'Msg>>
      Seen: HashSet<int>
      mutable MaxSequence: int
      mutable Ascending: bool }

    static member Create() : StreamState<'Msg> =
        { Records = ResizeArray<OpRecord<'Msg>>()
          Seen = HashSet<int>()
          MaxSequence = 0
          Ascending = true }

type InMemorySink<'Msg>() =

    let streams = Dictionary<string, StreamState<'Msg>>()
    let checkpoints = Dictionary<string, ResizeArray<Checkpoint<'Msg>>>()
    let lockObj = obj ()

    let getOrCreateStream (streamId: string) : StreamState<'Msg> =
        match streams.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh = StreamState<'Msg>.Create()
            streams[streamId] <- fresh
            fresh

    let getOrCreateCheckpointBucket (streamId: string) : ResizeArray<Checkpoint<'Msg>> =
        match checkpoints.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh = ResizeArray<Checkpoint<'Msg>>()
            checkpoints[streamId] <- fresh
            fresh

    interface IOpStreamCheckpointSink<'Msg> with

        member _.Append(record: OpRecord<'Msg>) : Async<unit> =
            async {
                lock lockObj (fun () ->
                    let st = getOrCreateStream record.StreamId

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

                    st.Records.Add record)
            }

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

                            ordered |> List.ofSeq)
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
            async {
                return
                    lock lockObj (fun () ->
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

                            st.MaxSequence <-
                                if st.Records.Count = 0 then
                                    0
                                else
                                    st.Records |> Seq.map _.Sequence |> Seq.max

                            removed)
            }

        member _.TruncateCheckpointsBefore(streamId: string, beforeSequence: int) : Async<int> =
            async {
                return
                    lock lockObj (fun () ->
                        match checkpoints.TryGetValue streamId with
                        | false, _ -> 0
                        | true, bucket ->
                            let toRemove =
                                bucket |> Seq.filter (fun c -> c.Sequence < beforeSequence) |> Seq.toList

                            for c in toRemove do
                                bucket.Remove c |> ignore

                            toRemove.Length)
            }

module InMemorySink =
    /// Convenience factory returning a fresh sink as the abstraction interface.
    let create<'Msg> () : IOpStreamSink<'Msg> = upcast InMemorySink<'Msg>()

    /// Convenience factory returning the checkpoint-aware sink
    /// interface. The underlying instance is the same class — InMemorySink
    /// always implements `IOpStreamCheckpointSink<'Msg>`. Use this factory
    /// when the consumer needs the checkpoint methods on the static surface.
    let createWithCheckpoints<'Msg> () : IOpStreamCheckpointSink<'Msg> = upcast InMemorySink<'Msg>()
