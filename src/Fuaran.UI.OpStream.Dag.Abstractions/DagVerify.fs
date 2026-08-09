namespace Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagVerify — integrity verification over a set of DAG op-records.
//
//  The DAG analogue of the linear `Verify.chain`. Two checks per record:
//
//   1. Parent-linkage. Every hash in `record.Parents` resolves to a record in
//      the set (no dangling parent) — the structural integrity of the Merkle
//      links.
//   2. Content address. For a LIVE record, the stored `Hash` recomputes from
//      `(parents, op, timestamp)` via `DagOpRecord.computeHash`. A TOMBSTONED
//      record's payload was pruned (its `Op` is a placeholder), so its hash is
//      accepted as-stored — the chain STILL verifies because its children
//      reference it by the preserved hash and the structural linkage holds.
//      This is the FGP-5 "op-stream is the source of truth" guarantee carried
//      through retention: pruning shortens payloads, never breaks the chain.
//
//  Portable on BOTH pipelines. The pre-405 `#if !FABLE_COMPILER` fence here was
//  stale over-caution — it existed because recomputing a content address needed
//  SHA-256 and SHA-256 used to be BCL-only. Phase 405 made `sha256Hex` a pure,
//  Fable-safe FIPS 180-4 implementation, and the linear `Verify.chain` was
//  de-fenced with it; `DagOpRecord.computeHash` followed at Phase 408. This
//  module was the last fence between a browser host and DAG verification, so a
//  Fable client could hold a DAG but never check it — exactly the asymmetry the
//  linear chain no longer has. It is gone: the whole body is `Map` +
//  `DagOpRecord.recomputeHash`, both Fable-clean.
//
//  ── CALLERS (Phase 793) ────────────────────────────────────────────────────
//  Until Phase 793 this module had ZERO production callers — it was exercised
//  only by its own tests, which is the worst state verification code can be in:
//  it reads as coverage that does not exist, and a reader who greps for "is the
//  DAG checked anywhere" finds a module that says yes. It is now wired at the
//  DAG sinks' READ paths, which is where a stored DAG re-enters the process and
//  therefore the only place the check can pay for itself:
//
//   - `IDagOpStreamSink.Records` -> `DagVerify.recordsResolving` (both legs;
//     the sink supplies store-wide parent resolution — see the note on that
//     function for why the set-closed `records` is the wrong form here).
//   - `IDagOpStreamSink.TryGet`  -> `DagVerify.record` (the content-address leg
//     alone; one record in isolation has no parent set to resolve against, so
//     running the set-closed form over a singleton would report every real
//     parent as dangling).
//
//  What this proves is what an UNKEYED content address proves — accidental
//  corruption of a stored record, and a structurally broken link. It is not
//  tamper evidence: a writer who edits a record recomputes its address and
//  re-parents its children, and verification passes. See CRYPTO.md.
// ============================================================================

[<RequireQualifiedAccess>]
type DagVerificationError =
    /// `record.Parents` names a hash with no record in the set.
    | DanglingParent of hash: string * missingParent: string
    /// A LIVE `record.Hash` does not recompute to the content address of its
    /// `(parents, op, timestamp)`.
    | HashMismatch of expected: string * actual: string

module DagVerify =

    /// The CONTENT-ADDRESS leg for ONE record, read in isolation: the stored
    /// `Hash` recomputes from its own `(parents, op, timestamp, provenance)`.
    /// A tombstoned record's payload was pruned, so its hash is accepted
    /// as-stored — the same rule `records` applies.
    ///
    /// Parent linkage is deliberately NOT checked here: a single record carries
    /// only its parents' hashes, with no set to resolve them against, so the
    /// only honest answer for that leg is "not checked" rather than "dangling".
    /// A caller holding the whole set wants `records`.
    let record<'Msg> (r: DagOpRecord<'Msg>) : Result<unit, DagVerificationError> =
        if r.Tombstoned then
            Ok()
        else
            let recomputed = DagOpRecord.recomputeHash r

            if recomputed = r.Hash then
                Ok()
            else
                Error(DagVerificationError.HashMismatch(r.Hash, recomputed))

    /// Verify a set of DAG records: every parent resolves, and every live
    /// record's stored hash recomputes. Returns the first violation, or `Ok ()`
    /// on a clean DAG. Order-independent — the records are indexed by hash
    /// first, so a topologically-unsorted input verifies the same, and the walk
    /// is in Ordinal hash order so two hosts given the same broken DAG name the
    /// same violation (the `Dictionary` this replaced iterated in insertion
    /// order, which made "the first violation" host-dependent).
    ///
    /// A duplicate `Hash` is a re-add of identical content (content addressing
    /// makes re-adds idempotent), so last-wins indexing loses nothing.
    ///
    /// `parentResolves` decides the parent-linkage leg. `records` closes it over
    /// the input set — the right answer for a CLOSED DAG. A **stream-scoped**
    /// read is not closed, and assuming it was is a real defect this
    /// parameterisation exists to prevent: the guest-fork contract (Phase 267)
    /// anchors a guest branch's genesis on the `Mount` op in the HOST stream, so
    /// every `Records "guest-<scopeId>"` legitimately contains a record whose
    /// parent is elsewhere. A sink therefore resolves against its whole store,
    /// not against the slice it is returning. (Wiring this over the set-closed
    /// form reported six such branches as corrupt — the check working, against
    /// the wrong universe.)
    let recordsResolving<'Msg>
        (parentResolves: string -> bool)
        (recs: DagOpRecord<'Msg> seq)
        : Result<unit, DagVerificationError> =
        let byHash = recs |> Seq.map (fun r -> r.Hash, r) |> Map.ofSeq

        let violation (r: DagOpRecord<'Msg>) : DagVerificationError option =
            // 1. Parent linkage.
            match r.Parents |> List.tryFind (parentResolves >> not) with
            | Some missing -> Some(DagVerificationError.DanglingParent(r.Hash, missing))
            | None ->
                // 2. Content address — live records only. A tombstoned record's
                // payload was pruned, so its hash is accepted as-stored.
                // `record` picks the op-hash or merge-outcome-hash rule.
                match record r with
                | Ok() -> None
                | Error e -> Some e

        match byHash |> Map.toSeq |> Seq.tryPick (snd >> violation) with
        | Some e -> Error e
        | None -> Ok()

    /// `recordsResolving` over a CLOSED set — parents must resolve within `recs`
    /// itself. Correct for a whole-DAG check; see `recordsResolving` for why a
    /// stream-scoped sink read must not use this form.
    let records<'Msg> (recs: DagOpRecord<'Msg> seq) : Result<unit, DagVerificationError> =
        let recordList = List.ofSeq recs
        let present = recordList |> List.map _.Hash |> Set.ofList
        recordsResolving present.Contains recordList

    /// Human-readable rendering of a violation — the shape a read path with no
    /// `Result` channel (the sink interfaces return bare records) uses to refuse
    /// BY NAME rather than by an opaque throw.
    let describe (streamId: string) (error: DagVerificationError) : string =
        match error with
        | DagVerificationError.DanglingParent(hash, missingParent) ->
            "DAG integrity broken in stream '"
            + streamId
            + "': record "
            + hash
            + " names parent "
            + missingParent
            + ", which is not in the store"
        | DagVerificationError.HashMismatch(expected, actual) ->
            "DAG integrity broken in stream '"
            + streamId
            + "': stored content address "
            + expected
            + " does not recompute (recomputed "
            + actual
            + ")"
