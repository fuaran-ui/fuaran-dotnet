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
//
//  ── VERIFY ON READ (Phase 793) ─────────────────────────────────────────────
//  `Records` and `TryGet` re-verify what they hand back
//  (`DagVerify.recordsResolving` / `DagVerify.record`) rather than trusting it
//  because this sink wrote it.
//  `Add` still checks only for a content-addressing collision, which is its own
//  job — the collision check asks "does this hash already mean something else",
//  verification asks "does this record still hash to its address", and only the
//  second catches a store that changed underneath the process. A host that has
//  measured the cost passes `LoadVerification.Off` explicitly; there is no
//  silent fast path.
// ============================================================================

type private StreamState<'Msg> =
    { Records: Dictionary<string, DagOpRecord<'Msg>>
      mutable Head: string option }

type InMemoryDagSink<'Msg>(loadVerification: LoadVerification) =

    let streams = Dictionary<string, StreamState<'Msg>>()
    let lockObj = obj ()

    /// `IDagOpStreamSink` has no error channel — its reads return bare records —
    /// so a broken DAG is refused the way this sink already refuses a hash
    /// collision: by name, with an `invalidOp` naming the stream and record.
    /// `LoadVerification.Tail` has no meaning over a SET (no total order to take
    /// a tail of), so it verifies in full — erring towards more checking, never
    /// less.
    let verifyOnRead (streamId: string) (check: unit -> Result<unit, DagVerificationError>) =
        match loadVerification with
        | LoadVerification.Off -> ()
        | LoadVerification.Full
        | LoadVerification.Tail _ ->
            match check () with
            | Ok() -> ()
            | Error e -> invalidOp ("InMemoryDagSink: " + DagVerify.describe streamId e)

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

    /// Does `hash` name a record ANYWHERE in this store? Parent linkage is
    /// resolved store-wide, not within the stream being read: a guest branch's
    /// genesis is anchored on the `Mount` op in the HOST stream, so a
    /// stream-scoped set is not a closed parent universe. Caller holds the lock.
    let knownHash (hash: string) : bool =
        streams.Values |> Seq.exists (fun state -> state.Records.ContainsKey hash)

    /// Default construction verifies every read (Phase 793).
    new() = InMemoryDagSink<'Msg>(LoadVerification.Full)

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
                let found =
                    lock lockObj (fun () ->
                        match streams.TryGetValue streamId with
                        | false, _ -> None
                        | true, state ->
                            match state.Records.TryGetValue hash with
                            | true, r -> Some r
                            | false, _ -> None)

                match found with
                | None -> ()
                | Some r -> verifyOnRead streamId (fun () -> DagVerify.record r)

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
                let records, present =
                    lock lockObj (fun () ->
                        let recs =
                            match streams.TryGetValue streamId with
                            | false, _ -> []
                            | true, state -> state.Records.Values |> List.ofSeq
                        // Snapshot the store-wide hash set under the same lock as
                        // the records, so verification never sees a torn view.
                        recs, (recs |> List.collect _.Parents |> List.filter knownHash |> Set.ofList))

                verifyOnRead streamId (fun () -> DagVerify.recordsResolving present.Contains records)
                return records
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
    /// Fresh sink as the abstraction interface. Verifies every read (Phase 793).
    let create<'Msg> () : IDagOpStreamSink<'Msg> = upcast InMemoryDagSink<'Msg>()

    /// `create` under an explicit read-path verification mode. Naming a cheaper
    /// mode here is the ONLY way to get one — there is no silent fast path.
    let createWith<'Msg> (loadVerification: LoadVerification) : IDagOpStreamSink<'Msg> =
        upcast InMemoryDagSink<'Msg>(loadVerification)
