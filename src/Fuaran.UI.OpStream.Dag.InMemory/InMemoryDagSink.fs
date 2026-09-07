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
//  `Add`'s checks are a different question — the write path asks "does this hash
//  already mean something else" and "does this record link to anything real";
//  verification asks "does this record still hash to its address", and only the
//  last catches a store that changed underneath the process. A host that has
//  measured the cost passes `LoadVerification.Off` explicitly; there is no
//  silent fast path.
//
//  ── VERIFY ON WRITE (Phase 1525) ───────────────────────────────────────────
//  Two checks run at the write choke point, both refusing BY NAME:
//
//   1. **Content-address collision.** A record arriving at an address the store
//      already holds must BE the record already there. It is compared on
//      `DagWire.contentFingerprint` — the digest of its full canonical wire form
//      — rather than on a hand-picked pair of fields. The old check compared
//      `Parents` and `OutcomeHash` only, so a record with the same hash and a
//      DIFFERENT `Op` matched, was classified as an idempotent duplicate, and
//      was silently dropped. That is the collision case, and dropping the
//      newcomer is the one response that leaves no trace of it having happened.
//   2. **Parent presence.** Every hash in `Parents` must already name a record
//      somewhere in this store. A record admitted with a parent the store does
//      not hold is a dangling edge nothing notices until a later `Records` read
//      — or a replay — trips over it, by which time the write that caused it is
//      long gone and unattributable. The read-path check stays as it was: it
//      catches a store that lost a record AFTER the link was made, which no
//      write-time check can see.
//
//  Fingerprints live in their own map and are deliberately NOT re-derived from
//  the stored record: `Tombstone` prunes a record's payload, so the canonical
//  bytes of what remains are not the bytes the address was minted over, and a
//  check that recomputed them would call every post-compaction re-add of an
//  unchanged record a collision.
// ============================================================================

type private StreamState<'Msg> =
    {
        Records: Dictionary<string, DagOpRecord<'Msg>>
        /// Content fingerprint per stored address, captured at first insert and
        /// NEVER rewritten — not by a re-add, not by `Tombstone`. It is what makes
        /// an identical re-add after a retention sweep still resolve as the
        /// duplicate it is.
        Fingerprints: Dictionary<string, string>
        mutable Head: string option
    }

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
                  Fingerprints = Dictionary<string, string>()
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
                    // Phase 1587 — `contentFingerprint` takes the op encoding
                    // rather than choosing one. This sink holds no codec (it
                    // stores typed records, not text), so it names the canonical
                    // encoder directly: the same bytes the check has always
                    // compared, and the same ones the Sqlite sink uses, so a
                    // record's fingerprint does not depend on which store it
                    // landed in.
                    let fingerprint = DagWire.contentFingerprint CanonicalJson.encodeOp record

                    // ── Check 1: parent presence (Phase 1525) ───────────────
                    // Store-wide, for the same reason the read path resolves
                    // store-wide: a guest branch's genesis legitimately hangs off
                    // a record in the HOST stream, so a stream-scoped universe
                    // would refuse a healthy fork.
                    record.Parents
                    |> List.tryFind (knownHash >> not)
                    |> Option.iter (fun missing ->
                        invalidOp (
                            sprintf
                                "InMemoryDagSink: refused record %s in stream '%s' — parent-presence check failed: parent %s is not in the store."
                                record.Hash
                                record.StreamId
                                missing
                        ))

                    // ── Check 2: content-address collision (Phase 1525) ─────
                    match state.Fingerprints.TryGetValue record.Hash with
                    | true, storedFingerprint ->
                        // An identical re-append is a no-op; the SAME address
                        // carrying DIFFERENT canonical content is a collision,
                        // never a duplicate. Compared on the full canonical wire
                        // form (`Tombstoned` normalised), so a differing `Op` —
                        // which the pre-1525 parents+outcome comparison could not
                        // see at all — is caught here rather than dropped.
                        if storedFingerprint <> fingerprint then
                            invalidOp (
                                sprintf
                                    "InMemoryDagSink: refused record %s in stream '%s' — content-address collision: the stored record at this address has different canonical content (stored fingerprint %s, incoming %s). Content addressing violated."
                                    record.Hash
                                    record.StreamId
                                    storedFingerprint
                                    fingerprint
                            )
                    | false, _ ->
                        state.Records[record.Hash] <- record
                        state.Fingerprints[record.Hash] <- fingerprint)
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
                                // Drop the PAYLOAD (reset op to a placeholder),
                                // preserve hash + parents so the chain still
                                // links + verifies.
                                //
                                // `OutcomeHash` is KEPT (Phase 1525). It used to
                                // be cleared, which cost the store the one thing
                                // that distinguishes a pruned merge node from a
                                // pruned ordinary one, for no retention gain — it
                                // is a 64-hex address, not payload. What it broke
                                // was idempotence across a sweep: a re-add of the
                                // unchanged record no longer matched what the
                                // store held. The fingerprint map is likewise
                                // untouched here, for the same reason.
                                state.Records[hash] <-
                                    { r with
                                        Op = TreeOp.Batch []
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
