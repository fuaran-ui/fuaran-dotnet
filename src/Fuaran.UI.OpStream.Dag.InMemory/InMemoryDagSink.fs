namespace Fuaran.UI.OpStream.Dag.InMemory

open System
open System.Collections.Generic
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  InMemoryDagSink — per-process content-addressed IDagOpStreamSink<'Msg>.
//
//  The single-process counterpart of the Sqlite DAG sink: a guarded ref is the
//  CAS primitive. Records live in a per-stream `hash -> DagOpRecord` map;
//  branch appends never contend (they just add a node whose parents the writer
//  chose), so the only serialised operation is `TryAdvanceHead` against the
//  per-stream trunk-head cell. A single lock guards every mutation, which is
//  more than the contract requires but keeps the in-memory sink's invariants
//  obvious for tests.
//
//  Topology is Core's — no bespoke graph code here. A pruned node moves to the
//  stream's skeleton (Phase 2645).
//
//  ── THE READ CONTRACT (Phase 2716, docs/DECISIONS.md D32) ──────────────────
//  Verified on WRITE, trusted on READ. Every record this sink holds entered
//  through `Add`'s admission below, and nothing else can reach the maps: there
//  is no backing store, so there is no LOAD and nothing for a read to re-check.
//  `Records`, `TryGet`, `Reachable` and `Reach` hand back what is held. Before
//  Phase 2716 `Records` and `TryGet` re-verified every record on every call
//  (Phase 793); on this sink that check could never fail through its own API.
//  `Reverify` (`IDagOpStreamIndexedSink`) re-checks a stream on demand. The
//  `LoadVerification` this sink is built with governs nothing here (there is no
//  load) and is kept so the constructors keep their signatures.
//
//  ── THE HELD INDEX (Phase 2716) ────────────────────────────────────────────
//  Each stream holds Core's reachability index (`Dag.Reach`) beside its node
//  map. It is built once, at the first question that needs it (`Reachable`,
//  `Reach`, or a merge through `DagMerge.mergeWith`), and from then on every
//  admitted record extends it through Core's `Dag.appendIndexed` /
//  `Dag.mergeIndexed` (`DagSinkIndex.tryExtend`), O(N) per write. A prune drops
//  it; the next question rebuilds it once. A write-only stream never builds it.
//
//  ── VERIFY ON WRITE (Phase 1525; one admission since Phase 1937) ───────────
//  `Add` runs `DagWriteAdmission.admit`, the check both DAG sinks share: the
//  record's address is recomputed (a wrong `Hash` is refused by name), every
//  parent must already be in this store, and an address the stream already
//  holds is an idempotent no-op. Admission runs before the stream exists, so a
//  refused first `Add` leaves no empty stream behind for `Streams` to list.
//
//  `AddAll` (Phase 2117, `IDagOpStreamBatchSink`) is the same admission over a
//  list, staged under the lock and stored only when every record is admitted;
//  `Add` is its one-record case.
//
//  A record is stored with its timestamp as its content address reads it —
//  whole unix seconds, UTC — which is what the SQLite DAG sink stores, so a
//  record reads back the same from either (Phase 1937).
// ============================================================================

type private StreamState<'Msg> =
    {
        Records: Dictionary<string, DagOpRecord<'Msg>>
        Skeleton: Dictionary<string, DagSkeletonNode>
        mutable Head: string option
        /// Core's reachability index over `Records`, once a question has built it.
        mutable Reach: Fuaran.Core.Dag.Reach<StreamEntry<'Msg>> option
    }

type InMemoryDagSink<'Msg>(loadVerification: LoadVerification) =

    let streams = Dictionary<string, StreamState<'Msg>>()
    let lockObj = obj ()

    // There is no load (see the header); the mode is accepted and governs nothing.
    do ignore loadVerification

    let getOrCreate (streamId: string) : StreamState<'Msg> =
        match streams.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh =
                { Records = Dictionary<string, DagOpRecord<'Msg>>()
                  Skeleton = Dictionary<string, DagSkeletonNode>()
                  Head = None
                  Reach = None }

            streams[streamId] <- fresh
            fresh

    /// The stream's index, built now if no question has built it yet. Caller
    /// holds the lock.
    let reachOf (state: StreamState<'Msg>) : Fuaran.Core.Dag.Reach<StreamEntry<'Msg>> =
        match state.Reach with
        | Some reach -> reach
        | None ->
            let reach = DagSinkIndex.build state.Records.Values
            state.Reach <- Some reach
            reach

    /// The parents of a live or pruned node, reading the stream's maps under the
    /// caller's lock.
    let parentsOf (state: StreamState<'Msg>) (hash: string) : string list =
        match state.Records.TryGetValue hash with
        | true, r -> r.Parents
        | false, _ ->
            match state.Skeleton.TryGetValue hash with
            | true, k -> k.Parents
            | false, _ -> []

    /// Does `hash` name a record ANYWHERE in this store? Parent linkage is
    /// resolved store-wide, not within the stream being read: a guest branch's
    /// genesis is anchored on the `Mount` op in the HOST stream, so a
    /// stream-scoped set is not a closed parent universe. Caller holds the lock.
    let knownHash (hash: string) : bool =
        streams.Values
        |> Seq.exists (fun state -> state.Records.ContainsKey hash || state.Skeleton.ContainsKey hash)

    /// Admit every record of `records`, in order, against the store PLUS the
    /// records staged before it, and store the staged set only when every one was
    /// admitted (Phase 2117) — so a refusal anywhere leaves the store exactly as
    /// it was. `Add` is the one-record case. An address already held, in the
    /// store or earlier in the batch, is a no-op. Takes the lock.
    let addAll (records: DagOpRecord<'Msg> list) : unit =
        lock lockObj (fun () ->
            let staged = Dictionary<string * string, DagOpRecord<'Msg>>()
            // The admission order, parents before children, for extending indexes.
            let order = ResizeArray<string * string>()
            let stagedHash = HashSet<string>()

            for record in records do
                let held (hash: string) =
                    staged.ContainsKey((record.StreamId, hash))
                    || (match streams.TryGetValue record.StreamId with
                        | true, state -> state.Records.ContainsKey hash || state.Skeleton.ContainsKey hash
                        | false, _ -> false)

                let present (hash: string) =
                    stagedHash.Contains hash || knownHash hash

                match DagWriteAdmission.admit present held record with
                | Error refusal -> invalidOp (DagWriteAdmission.describe "InMemoryDagSink" record refusal)
                | Ok DagAdmission.AlreadyHeld -> ()
                | Ok DagAdmission.Store ->
                    // `held` covers the batch, so an address is staged at most once.
                    order.Add((record.StreamId, record.Hash))

                    staged[(record.StreamId, record.Hash)] <-
                        { record with
                            Node =
                                { record.Node with
                                    Op =
                                        { record.Entry with
                                            Timestamp =
                                                DateTimeOffset.FromUnixTimeSeconds(
                                                    record.Timestamp.ToUnixTimeSeconds()
                                                ) } } }

                    stagedHash.Add record.Hash |> ignore

            // Every record admitted: only now does any stream's state change. A
            // held index is extended record by record, in admission order, which
            // puts every parent before its children; one Core's indexed writes
            // cannot extend is built over the stream's live set instead.
            for (streamId, hash) in order do
                let state = getOrCreate streamId
                let record = staged[(streamId, hash)]
                state.Records[hash] <- record

                match state.Reach with
                | None -> ()
                | Some reach ->
                    state.Reach <-
                        match DagSinkIndex.tryExtend reach record with
                        | Some extended -> Some extended
                        | None -> Some(DagSinkIndex.build state.Records.Values))

    /// Default construction. The mode governs nothing on this sink (no load).
    new() = InMemoryDagSink<'Msg>(LoadVerification.Full)

    interface IDagOpStreamSink<'Msg> with

        member _.Add(record: DagOpRecord<'Msg>) : Async<unit> = async { addAll [ record ] }

        member _.TryGet(streamId: string, hash: string) : Async<DagOpRecord<'Msg> option> =
            async {
                let found =
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> None
                        | true, state ->
                            match state.Records.TryGetValue hash with
                            | true, r -> Some r
                            | false, _ -> None)

                return found
            }

        member _.Head(streamId: string) : Async<string option> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> None
                        | true, state -> state.Head)
            }

        member _.TryAdvanceHead(streamId: string, expected: string option, newHead: string) : Async<bool> =
            async {
                return
                    lock lockObj (fun () ->
                        let state = getOrCreate streamId

                        // CAS: swap iff the head is still `expected`.
                        if state.Head = expected then
                            state.Head <- Some newHead
                            true
                        else
                            false)
            }

        member _.Parents(streamId: string, hash: string) : Async<string list> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> []
                        | true, state -> parentsOf state hash)
            }

        member _.Reachable(streamId: string, hash: string) : Async<Set<string>> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> Set.empty
                        | true, state -> Fuaran.Core.Dag.Reach.ancestors (reachOf state) hash)
            }

        member _.Heads(streamId: string) : Async<string list> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> []
                        | true, state ->
                            let referenced = HashSet<string>()

                            for KeyValue(_, r) in state.Records do
                                for p in r.Parents do
                                    referenced.Add p |> ignore

                            state.Records.Keys
                            |> Seq.filter (fun h -> not (referenced.Contains h))
                            |> List.ofSeq)
            }

        member _.Records(streamId: string) : Async<DagOpRecord<'Msg> list> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> []
                        | true, state -> state.Records.Values |> List.ofSeq)
            }

        member _.Tombstone(streamId: string, hash: string) : Async<bool> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> false
                        | true, state ->
                            match state.Records.TryGetValue hash with
                            | false, _ -> false
                            | true, r ->
                                state.Records.Remove hash |> ignore
                                // Core's index has no removal: the next question rebuilds it.
                                state.Reach <- None

                                state.Skeleton[hash] <-
                                    { StreamId = streamId
                                      Hash = hash
                                      Parents = r.Parents }

                                true)
            }

        member _.Skeleton(streamId: string) : Async<DagSkeletonNode list> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> []
                        | true, state -> state.Skeleton.Values |> List.ofSeq)
            }

        member _.Streams() : Async<string list> =
            async { return lock lockObj (fun () -> streams.Keys |> List.ofSeq) }

    /// Phase 2117 — the atomic batch add. Admission and staging run under the
    /// sink's one lock and nothing is stored until every record is admitted.
    interface IDagOpStreamBatchSink<'Msg> with
        member _.AddAll(records: DagOpRecord<'Msg> list) : Async<unit> = async { addAll records }

    /// Phase 2716 — the held index, and the verify-on-read guarantee on demand.
    interface IDagOpStreamIndexedSink<'Msg> with
        member _.Reach(streamId: string) : Async<Fuaran.Core.Dag.Reach<StreamEntry<'Msg>>> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> Fuaran.Core.Dag.Reach.ofDag Fuaran.Core.Dag.empty
                        | true, state -> reachOf state)
            }

        /// The maps are reachable only through `Add`'s admission, so this re-check
        /// is the write-time check again; it is here so a caller written against
        /// the contract asks one way whatever sink it holds.
        member _.Reverify(streamId: string) : Async<unit> =
            async {
                lock lockObj (fun () ->
                    match streams.TryGetValue streamId with
                    | false, _ -> ()
                    | true, state ->
                        let records = state.Records.Values |> List.ofSeq

                        match DagVerify.recordsResolving knownHash records with
                        | Ok() -> state.Reach <- Some(DagSinkIndex.build records)
                        | Error e ->
                            state.Reach <- None
                            invalidOp ("InMemoryDagSink: " + DagVerify.describe streamId e))
            }

module InMemoryDagSink =
    /// Fresh sink as the abstraction interface. Records are verified as they are
    /// added and trusted when read (D32); it is also an `IDagOpStreamIndexedSink`.
    let create<'Msg> () : IDagOpStreamSink<'Msg> = upcast InMemoryDagSink<'Msg>()

    /// `create` naming a `LoadVerification`, kept for its signature: this sink
    /// has no load, so the mode governs nothing (D32).
    let createWith<'Msg> (loadVerification: LoadVerification) : IDagOpStreamSink<'Msg> =
        upcast InMemoryDagSink<'Msg>(loadVerification)
