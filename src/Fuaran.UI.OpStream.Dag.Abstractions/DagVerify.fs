namespace Fuaran.UI.OpStream.Dag.Abstractions

open System.Collections.Generic

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
//  Server-side only (recompute needs SHA-256); scoped under `#if
//  !FABLE_COMPILER` like the linear `Verify`.
// ============================================================================

[<RequireQualifiedAccess>]
type DagVerificationError =
    /// `record.Parents` names a hash with no record in the set.
    | DanglingParent of hash: string * missingParent: string
    /// A LIVE `record.Hash` does not recompute to the content address of its
    /// `(parents, op, timestamp)`.
    | HashMismatch of expected: string * actual: string

#if !FABLE_COMPILER
module DagVerify =

    /// Verify a set of DAG records: every parent resolves, and every live
    /// record's stored hash recomputes. Returns the first violation, or `Ok ()`
    /// on a clean DAG. Order-independent — the records are indexed by hash
    /// first, so a topologically-unsorted input verifies the same.
    let records<'Msg> (recs: DagOpRecord<'Msg> seq) : Result<unit, DagVerificationError> =
        let byHash = Dictionary<string, DagOpRecord<'Msg>>()

        for r in recs do
            byHash[r.Hash] <- r

        let mutable result: Result<unit, DagVerificationError> = Ok()
        use e = (byHash.Values :> IEnumerable<DagOpRecord<'Msg>>).GetEnumerator()
        let mutable stop = false

        while not stop && e.MoveNext() do
            let r = e.Current

            // 1. Parent linkage.
            match r.Parents |> List.tryFind (fun p -> not (byHash.ContainsKey p)) with
            | Some missing ->
                result <- Error(DagVerificationError.DanglingParent(r.Hash, missing))
                stop <- true
            | None ->
                // 2. Content address — live records only. `recomputeHash`
                // picks the op-hash or the merge-outcome-hash rule.
                if not r.Tombstoned then
                    let recomputed = DagOpRecord.recomputeHash r

                    if recomputed <> r.Hash then
                        result <- Error(DagVerificationError.HashMismatch(r.Hash, recomputed))
                        stop <- true

        result
#endif
