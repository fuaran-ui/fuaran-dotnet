module Fuaran.UI.OpStream.Replay.ChainMigration

// ============================================================================
//  Op-stream chain-format migrator (Phase 406).
//
//  Phase 406 re-expressed the linear chain over `Fuaran.Core.OpStream`'s
//  canonical delimited payload + the `StreamEntry` provenance envelope, so the
//  hash of every persisted record changes (the pre-image now folds in `PromptId`
//  + `ResultEnvelope` and delimits `seq`/`ts`). The RECORD shape is unchanged —
//  only the `PreviousHash` / `Hash` bytes move — so migration is a re-chain in
//  place, not a schema change: read a stream's records, `migrate`, write them
//  back (or into a fresh sink).
//
//  The op / actor / timestamp / promptId / result on each record are the source
//  of truth; migration re-derives the two hash columns from them. `migrateVerified`
//  first re-checks the source under the FROZEN pre-406 pre-image (`legacyChainHash`)
//  and refuses a stream that does not verify — a corrupt / tampered legacy chain
//  must not be silently re-blessed under the new format.
//
//  `legacyChainHash` is the pre-406 formula, retained ONLY here as migration
//  scaffolding. Nothing else computes it — the live chain authority is
//  `HashChain.computeHash` (Core-canonical). Once every persisted stream is
//  migrated, this module can be retired.
// ============================================================================

open System.Globalization
open Fuaran.UI.OpStream.Abstractions

/// The **pre-406** chain hash for one record over a given previous hash: SHA-256
/// of `prev ++ encodeOp ++ sequence ++ unixSeconds ++ Actor.encode`, undelimited,
/// with `PromptId` / `ResultEnvelope` EXCLUDED (the provenance hole 406 closes).
let legacyChainHash (previousHash: string) (record: OpRecord<'Msg>) : string =
    HashChain.sha256Hex (
        previousHash
        + CanonicalJson.encodeOp record.Op
        + record.Sequence.ToString(CultureInfo.InvariantCulture)
        + record.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        + Actor.encode record.Actor
    )

/// Does `records` verify under the pre-406 chain format? Walks from genesis
/// asserting contiguous 1-based sequence, prev-hash linkage, and the legacy hash.
let verifyLegacy (records: OpRecord<'Msg> list) : bool =
    let rec go (prev: string) (expectedSeq: int) =
        function
        | [] -> true
        | (r: OpRecord<'Msg>) :: rest ->
            if r.Sequence <> expectedSeq then false
            elif r.PreviousHash <> prev then false
            elif legacyChainHash prev r <> r.Hash then false
            else go r.Hash (expectedSeq + 1) rest

    go HashChain.genesisPreviousHash 1 records

/// Re-chain `records` under the Core-canonical Phase-406 format, preserving every
/// field (op / actor / timestamp / promptId / result). The result `Verify.chain`s
/// under the new format; replay produces the same tree (the ops are untouched).
let migrate (records: OpRecord<'Msg> list) : OpRecord<'Msg> list =
    (([], HashChain.genesisPreviousHash), records)
    ||> List.fold (fun (acc, prev) (r: OpRecord<'Msg>) ->
        let newHash =
            HashChain.computeHash prev r.Op r.Sequence r.Timestamp r.Actor r.PromptId r.ResultEnvelope

        let migrated =
            { r with
                PreviousHash = prev
                Hash = newHash }

        migrated :: acc, newHash)
    |> fst
    |> List.rev

/// Verify the source under the pre-406 format, then migrate. A stream that does
/// not verify as a clean legacy chain is a named `Error` — never silently rebuilt.
let migrateVerified (records: OpRecord<'Msg> list) : Result<OpRecord<'Msg> list, string> =
    if verifyLegacy records then
        Ok(migrate records)
    else
        Error "ChainMigration.migrateVerified: source stream does not verify under the pre-406 chain format"
