module Fuaran.UI.OpStream.Replay.ChainMigration

// ============================================================================
//  Op-stream chain-format migrator (Phase 406).
//
//  Phase 406 re-expressed the linear chain over `Fuaran.Core.OpStream`'s
//  canonical delimited payload + the `StreamEntry` provenance envelope, so the
//  hash of every persisted record changes (the pre-image now folds in `PromptId`
//  + `ResultEnvelope` and delimits `seq`/`ts`). The RECORD shape is unchanged —
//  only the `PreviousHash` / `Hash` bytes move — so migration is a re-chain in
//  place, not a schema change: read a stream's records, `migrateVerified`, write them
//  back (or into a fresh sink).
//
//  The op / actor / timestamp / promptId / result on each record are the source
//  of truth; migration re-derives the two hash columns from them. `migrateVerified`
//  re-checks the source under the FROZEN pre-406 pre-image (`legacyChainHash`)
//  and refuses a stream that does not verify — a corrupt / tampered legacy chain
//  must not be silently re-blessed under the new format. Since Phase 1937 the
//  walk and the re-chain are Core's (`ChainShape.walk` / `ChainShape.rehash`);
//  this module states only the legacy pre-image.
//
//  `legacyChainHash` is the pre-406 formula, retained ONLY here as migration
//  scaffolding. Nothing else computes it — the live chain authority is
//  `HashChain.computeHash` (Core-canonical). Once every persisted stream is
//  migrated, this module can be retired.
// ============================================================================

open System.Globalization
open Fuaran.UI.OpStream.Abstractions

/// The pre-406 pre-image of one record: `encodeOp ++ sequence ++ unixSeconds ++
/// Actor.encode`, undelimited, with `PromptId` / `ResultEnvelope` EXCLUDED (the
/// provenance hole 406 closes).
let private legacyPayload (record: OpRecord<'Msg>) : string =
    CanonicalJson.encodeOp record.Op
    + record.Sequence.ToString(CultureInfo.InvariantCulture)
    + record.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
    + Actor.encode record.Actor

let private legacyHash: Fuaran.Core.HashFn =
    fun previousHash payload -> Fuaran.Core.Hash.sha256Hex (previousHash + payload)

/// The **pre-406** chain hash for one record over a given previous hash: SHA-256
/// of `prev ++` the undelimited pre-406 payload.
let legacyChainHash (previousHash: string) (record: OpRecord<'Msg>) : string =
    legacyHash previousHash (legacyPayload record)

/// The pre-406 chain as a Core `ChainShape` (Phase 1937), so the legacy chain is
/// walked and re-chained by Core's verbs like any other.
let private legacyShape<'Msg> () : Fuaran.Core.ChainShape<OpRecord<'Msg>> =
    { HashChain.recordShape<'Msg> () with
        PayloadOf = legacyPayload }

let private genesis: Fuaran.Core.Anchor =
    { Seq = 0
      PrevHash = HashChain.genesisPreviousHash }

/// Does `records` verify under the pre-406 chain format? Core's
/// `ChainShape.walk` from genesis: contiguous 1-based sequence, prev-hash
/// linkage, and the legacy hash.
let verifyLegacy (records: OpRecord<'Msg> list) : bool =
    Fuaran.Core.ChainShape.walk (legacyShape<'Msg> ()) legacyHash genesis records
    |> Option.isNone

/// Verify the source under the pre-406 format, then re-chain it under the
/// Core-canonical Phase-406 format, preserving every field (op / actor /
/// timestamp / promptId / result) — Core's `ChainShape.rehash`, which refuses a
/// source that does not verify and re-chains nothing (Phase 1937; the hand
/// re-chain fold this module carried, and its unverified `migrate`, are gone).
/// The result `Verify.chain`s under the new format; replay produces the same
/// tree (the ops are untouched). A stream that does not verify as a clean
/// legacy chain is a named `Error` — never silently rebuilt.
let migrateVerified (records: OpRecord<'Msg> list) : Result<OpRecord<'Msg> list, string> =
    Fuaran.Core.ChainShape.rehash
        (legacyShape<'Msg> ())
        legacyHash
        (HashChain.recordShape<'Msg> ())
        StreamEntry.hashFn
        genesis
        HashChain.genesisPreviousHash
        id
        records
    |> Result.mapError (fun b ->
        sprintf
            "ChainMigration.migrateVerified: source stream does not verify under the pre-406 chain format (record %d: %s)"
            (b.Index + 1)
            (Fuaran.Core.ChainBreakReason.toString b.Reason))

// ── The pre-escape payload (Phase 1937; Core Phase 287) ─────────────────────
//
// From Core 0.30.0 to 0.32.0 the linear chain's payload spelled `\n`, `\r` and
// `\t` in an actor's strings as the short escapes; from 0.33.0 every control
// character is `\u00xx` (`StreamConfig.legacyEscape` -> `canonical`). The chain
// FORMAT did not move — `chainFormatVersion` stays 2 (D30) — because every record
// whose actor holds no such character hashes byte-identically under both, so the
// change touches only the records that carry one. A store written at those pins
// whose actors do carry one verifies under this migration's source shape and is
// re-chained to the canonical one; any other store comes through unchanged.

/// The linear chain under Core's pre-287 payload: the same record, the same
/// envelope, the actor's strings spelled with the short escapes.
let private legacyEscapeShape<'Msg> () : Fuaran.Core.ChainShape<OpRecord<'Msg>> =
    { HashChain.recordShape<'Msg> () with
        PayloadOf =
            fun r ->
                Fuaran.Core.StreamConfig.legacyEscape.Payload
                    (r.Sequence - 1)
                    r.Actor
                    (StreamEntry.encode (StreamEntry.ofRecord r)) }

/// Verify `records` under the pre-287 payload, then re-chain them under the
/// canonical one — Core's `ChainShape.rehash`, so a stream that verifies under
/// neither is refused by name and nothing is re-chained. A stream whose actors
/// hold no `\n` / `\r` / `\t` comes back with every hash unchanged.
let migrateLegacyEscape (records: OpRecord<'Msg> list) : Result<OpRecord<'Msg> list, string> =
    Fuaran.Core.ChainShape.rehash
        (legacyEscapeShape<'Msg> ())
        StreamEntry.hashFn
        (HashChain.recordShape<'Msg> ())
        StreamEntry.hashFn
        genesis
        HashChain.genesisPreviousHash
        id
        records
    |> Result.mapError (fun b ->
        sprintf
            "ChainMigration.migrateLegacyEscape: source stream does not verify under the pre-287 payload (record %d: %s)"
            (b.Index + 1)
            (Fuaran.Core.ChainBreakReason.toString b.Reason))
