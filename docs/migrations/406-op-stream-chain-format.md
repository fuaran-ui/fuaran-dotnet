# Phase 406 – op-stream chain-format bump (Core-canonical envelope)

> **Addendum (Phase 411, same day).** The pre-image's `seq` is **Core's 0-based record
> index** (`Sequence - 1`), not the domain's public 1-based `Sequence` as originally
> shipped here. Rationale: with the basis aligned, a UI record maps onto `Core.OpRecord`
> via `Seq = Sequence - 1` and **Core's own `firstChainBreakWith` verifies UI chains
> directly** (the domain's hand-rolled `Verify.chain` walker is retired; F14 resolved).
> The public API keeps 1-based `Sequence` – presentation only, never hashed. No
> 406-format streams were ever persisted (the fix landed within hours), so no
> 406→411 migrator exists; `ChainMigration` re-chains pre-406 legacy streams straight
> to the current format.

**Breaking change to `Fuaran.UI.OpStream.*` – the hash chain only.** The persisted
`OpRecord` **shape is unchanged** (same fields, same sink columns); only the
`PreviousHash` / `Hash` bytes move. Pre-1.0, no consumers outside operator control.

## What changed

The linear op-stream chain is now computed over `Fuaran.Core.OpStream`'s canonical
format, with a domain provenance envelope carrying the rich per-record fields:

- **Pre-image (pre-406):** `SHA-256( previousHash ++ encodeOp(op) ++ sequence ++
  unixSeconds ++ Actor.encode(actor) )` – undelimited; `PromptId` and
  `ResultEnvelope` were **outside** the hash.
- **Pre-image (406):** `SHA-256( previousHash | {"seq":N,"actor":<Actor.encode>,"op":
  <StreamEntry.encode>} )`, where `StreamEntry.encode = {"op":<encodeOp>,"ts":<unix>,
  "promptId":<null|"…">,"result":<{"kind":"success"}|{"kind":"failure",…}>}`.

Two defects this closes (finding F13):

1. **Provenance hole.** `PromptId` + `ResultEnvelope` are now inside the pre-image,
   so re-attributing an op to a different prompt, or flipping a recorded `Failure`
   to `Success`, breaks `Verify.chain`. Pre-406 both were undetectable.
2. **Undelimited pre-image.** Raw `sequence ++ unixSeconds` concatenation made
   distinct `(seq, ts)` pairs byte-identical; Core's payload is a delimited object.

The hash is still SHA-256 (Phase 405's portable, Fable-clean implementation), supplied
host-side through Core's certified `HashFn` seam (GP3) – so browser hosts verify chains
in-session, and no new Core surface was added (the rich fields ride in Core's opaque `'Op`).

## Who is affected

Any **persisted** stream (`Fuaran.UI.OpStream.Sqlite`, or a JSONL/`GuestExportBundle`
artefact) written before this phase. In-memory streams (per-process) need nothing – 
they are rebuilt from genesis each run.

## How to migrate

The record shape is unchanged, so migration is a **re-chain in place** – no schema
change. Use `Fuaran.UI.OpStream.Replay.ChainMigration`:

```fsharp
open Fuaran.UI.OpStream.Replay

// 1. Read the stream's records (ascending sequence) from the sink.
let! records = sink.Replay(streamId, 1, System.Int32.MaxValue)

// 2. Verify under the OLD format, then re-chain to the new one. A stream that does
//    not verify as a clean legacy chain is refused (never silently re-blessed).
match ChainMigration.migrateVerified records with
| Error why -> // stop — the source chain is corrupt; investigate before migrating
| Ok migrated ->
    // 3. Persist `migrated` (same StreamId/Sequence; only PreviousHash/Hash changed).
    //    Write into a fresh sink/stream, or a sink that supports replace-in-place.
    ()
```

- `ChainMigration.migrate` – re-chains from genesis, preserving every field
  (`op` / `actor` / `timestamp` / `promptId` / `result` are the source of truth).
- `ChainMigration.verifyLegacy` – checks a stream under the frozen pre-406 formula.
- `ChainMigration.migrateVerified` – `verifyLegacy` then `migrate`; `Error` on a
  stream that does not verify as legacy.

The migrated stream `Verify.chain`s under the new format and **replays to the
identical tree** (the ops are untouched).

## Verification

- `ChainMigrationTests` (`Fuaran.UI.OpStream.Tests`): a legacy stream verifies under
  `verifyLegacy` but **not** under the new `Verify.chain`; after `migrate` it verifies,
  fields preserved; `migrateVerified` refuses a legacy-tampered stream; `migrate` is
  idempotent on an already-migrated chain.
- `HashChainTests`: `computeHash` differs when `promptId` or `resultEnvelope` differs
  (the hole is closed), alongside the existing prev / op / seq / ts / actor coverage.

## Rollback

The pre-406 verifier survives as `ChainMigration.legacyChainHash` / `verifyLegacy`, so
a legacy stream can still be validated. To fully roll back, revert this phase's commit;
streams migrated forward would then need re-chaining under the old formula (the fields
remain the source of truth, so it is symmetric).

## Deferred to a follow-on (Phase 406b)

This phase moves the chain **format** to Core (via `HashChain.computeHash`, the single
authority) and closes the F13 defects. Threading Core's *list* operations – 
`OpStream.verifyChain` / `replay` / `toJsonl` / `fromJsonl` – through the `StreamEntry`
witness (retiring UI's `Verify.chain` walker and switching `GuestExport`'s JSONL to
`Core.toJsonl`) is a shape consolidation with no further format change, tracked
separately so this breaking bump stays focused.
