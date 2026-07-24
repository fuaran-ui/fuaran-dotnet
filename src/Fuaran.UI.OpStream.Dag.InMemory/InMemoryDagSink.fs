namespace Fuaran.UI.OpStream.Dag.InMemory

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
//  Topology queries delegate to the pure `DagTopology` algorithms over the
//  store's own parent lookup — no bespoke graph code here.
// ============================================================================

type private StreamState<'Msg> =
    { Records: Dictionary<string, DagOpRecord<'Msg>>
      mutable Head: string option }

type InMemoryDagSink<'Msg>() =

    let streams = Dictionary<string, StreamState<'Msg>>()
    let lockObj = obj ()

    let getOrCreate (streamId: string) : StreamState<'Msg> =
        match streams.TryGetValue streamId with
        | true, existing -> existing
        | false, _ ->
            let fresh =
                { Records = Dictionary<string, DagOpRecord<'Msg>>()
                  Head = None }

            streams[streamId] <- fresh
            fresh

    /// Parent lookup for `DagTopology`, reading the stream's record map under
    /// the caller's lock.
    let parentsOf (state: StreamState<'Msg>) (hash: string) : string list =
        match state.Records.TryGetValue hash with
        | true, r -> r.Parents
        | false, _ -> []

    interface IDagOpStreamSink<'Msg> with

        member _.Add(record: DagOpRecord<'Msg>) : Async<unit> =
            async {
                lock lockObj (fun () ->
                    let state = getOrCreate record.StreamId

                    match state.Records.TryGetValue record.Hash with
                    | true, existing ->
                        // Content addressing: an identical re-append is a no-op;
                        // a hash collision with differing content is a defect.
                        // Tombstone state is allowed to differ (pruning mutates
                        // it in place), so compare on the content-bearing fields.
                        if existing.Parents <> record.Parents || existing.OutcomeHash <> record.OutcomeHash then
                            invalidOp (
                                sprintf
                                    "InMemoryDagSink: hash collision at %s with differing content — content addressing violated."
                                    record.Hash
                            )
                    | false, _ -> state.Records[record.Hash] <- record)
            }

        member _.TryGet(streamId: string, hash: string) : Async<DagOpRecord<'Msg> option> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> None
                        | true, state ->
                            match state.Records.TryGetValue hash with
                            | true, r -> Some r
                            | false, _ -> None)
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
                        | true, state -> DagTopology.reachable (parentsOf state) hash)
            }

        member _.Lca(streamId: string, a: string, b: string) : Async<LcaResult> =
            async {
                return
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> LcaResult.None
                        | true, state -> DagTopology.lca (parentsOf state) a b)
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
                                // Drop the payload (reset op to a placeholder),
                                // preserve hash + parents so the chain still
                                // links + verifies.
                                state.Records[hash] <-
                                    { r with
                                        Op = TreeOp.Batch []
                                        OutcomeHash = None
                                        Tombstoned = true }

                                true)
            }

        member _.Streams() : Async<string list> =
            async { return lock lockObj (fun () -> streams.Keys |> List.ofSeq) }

module InMemoryDagSink =
    /// Fresh sink as the abstraction interface.
    let create<'Msg> () : IDagOpStreamSink<'Msg> = upcast InMemoryDagSink<'Msg>()
