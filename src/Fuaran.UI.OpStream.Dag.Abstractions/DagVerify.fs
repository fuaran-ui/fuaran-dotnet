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
// ============================================================================

[<RequireQualifiedAccess>]
type DagVerificationError =
    /// `record.Parents` names a hash with no record in the set.
    | DanglingParent of hash: string * missingParent: string
    /// A LIVE `record.Hash` does not recompute to the content address of its
    /// `(parents, op, timestamp)`.
    | HashMismatch of expected: string * actual: string

module DagVerify =

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
    let records<'Msg> (recs: DagOpRecord<'Msg> seq) : Result<unit, DagVerificationError> =
        let byHash = recs |> Seq.map (fun r -> r.Hash, r) |> Map.ofSeq

        let violation (r: DagOpRecord<'Msg>) : DagVerificationError option =
            // 1. Parent linkage.
            match r.Parents |> List.tryFind (fun p -> not (byHash.ContainsKey p)) with
            | Some missing -> Some(DagVerificationError.DanglingParent(r.Hash, missing))
            | None ->
                // 2. Content address — live records only. A tombstoned record's
                // payload was pruned, so its hash is accepted as-stored.
                // `recomputeHash` picks the op-hash or merge-outcome-hash rule.
                if r.Tombstoned then
                    None
                else
                    let recomputed = DagOpRecord.recomputeHash r

                    if recomputed = r.Hash then
                        None
                    else
                        Some(DagVerificationError.HashMismatch(r.Hash, recomputed))

        match byHash |> Map.toSeq |> Seq.tryPick (snd >> violation) with
        | Some e -> Error e
        | None -> Ok()
