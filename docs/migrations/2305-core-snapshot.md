# Migration — the linear checkpoint is Core's `Snapshot` (0.103.0, Phase 2305)

**Every persisted linear checkpoint hash moves, once.** The checkpoint seal is now Fuaran.Core's strict snapshot
seal, whose pre-image says `"state"` where the tier's own seal said `"tree"`. Nothing else in a store moves: the op
records, their chain (`chainFormatVersion` 2), the checkpoints' boundaries and the retained invocation keys are
byte-identical. If you persist checkpoints, read *Persisted data* before upgrading.

Affected packages: `Fuaran.UI.OpStream.Abstractions`, `Fuaran.UI.OpStream.Replay`, `Fuaran.UI.OpStream.Sqlite`
(0.103.0), and `Fuaran.UI.ServerDriven`, whose session checkpoints are sealed the same way. The DAG checkpoint
(`DagCheckpoint`) is unchanged by this phase. A consumer that never takes a checkpoint has nothing to do.

## What changed

```text
pre-image, 0.102.0   sha256Hash head {"snapshot":true,"seq":3,"tree":<canonical tree>}
pre-image, 0.103.0   sha256Hash head {"snapshot":true,"seq":3,"state":<canonical tree>}
```

The 0.103.0 bytes are `Fuaran.Core.Sealed.seal SnapshotMode.Strict` over the canonical tree as `State`, anchored at
`{ Seq = Sequence; PrevHash = PreviousChainHead }`: a UI checkpoint IS a Core `Snapshot`, and its hash reproduces
from Core's seal. Resuming from it is Core's too: the seal and the WHOLE tail are verified from
`(PreviousChainHead, Sequence)` by `Sealed.firstBreak`, then the tail is folded by `Compacted.replayFrom`. Where the
old path checked only the tail's first link before applying, a break in the middle of the tail is now refused
before anything is applied, as the new `CheckpointReplayError.TailBroken`.

## Code changes

**Sealing.** `HashChain.snapshotHash` is REMOVED. Build a checkpoint through the one constructor:

```fsharp
// 0.102.0
let hash = HashChain.snapshotHash head sequence (CanonicalJson.encodeNode tree)
let cp = { StreamId = s; Sequence = sequence; PreviousChainHead = head; SnapshotHash = hash; Snapshot = tree; Timestamp = ts }
// 0.103.0
let cp = CheckpointSeal.create s sequence head ts tree
// …or, for the hash alone:
let hash = CheckpointSeal.seal head sequence tree
```

`CheckpointSeal.toSnapshot` is the checkpoint as a `Fuaran.Core.Snapshot<Node<'Msg>>`, and
`CheckpointSeal.firstBreak cp tail` is Core's verifier over it. `Checkpoint.verifySnapshotHash` keeps its signature.

**Matching.** `CheckpointReplayError` gains `TailBroken of checkpointSequence * VerificationError`, its last case. A
break below the tail's first link used to arrive as `TailApplyFailed(ReplayError.ChainBroken …)`; it now arrives as
`TailBroken`, so an exhaustive match gains an arm. `BoundaryMismatch` still names a tail that does not hang from the
checkpoint, and `SnapshotHashMismatch` a seal that does not recompute.

**Removed, unused.** `CheckpointVerificationError` (Abstractions) had no producer and no reader; it is gone.

## Persisted data

A checkpoint stored by 0.102.0 or earlier fails the new seal — `applyFromCheckpoint` answers
`SnapshotHashMismatch`, and `SessionReplay.reconstruct` its own `SnapshotHashMismatch` — until it is re-sealed. The
re-seal is ONE-SHOT and refuses rather than re-blessing anything it cannot verify:

- **SQLite stores:** `SqliteSink.resealCheckpoints connectionString nodeCodec` reads every `op_checkpoint` row in one
  immediate transaction, decodes each snapshot through `nodeCodec`, re-seals a checkpoint that verifies under the
  0.102.0 seal, leaves one already under Core's seal alone, and refuses the WHOLE store by name — stream and
  sequence, nothing written — if any row verifies under neither or does not decode. Only `snapshot_hash` is
  updated. It returns the number re-sealed, so a second run returns `Ok 0`. Run it once per database, with the same
  node codec the store reads snapshots through, before the first resume under 0.103.0.
- **Any other store:** `CheckpointReseal.reseal checkpoint` is the same decision for one checkpoint —
  `Resealed cp` (write `cp.SnapshotHash` back), `AlreadySealed`, or a `Refusal` naming it (`CheckpointReseal.describe`).
  Verify the whole store before writing any of it, as the SQLite path does.
- **In-memory stores** hold nothing across a process, so they have nothing to migrate.

A checkpoint the re-seal refuses was already failing the 0.102.0 seal: it is corrupt, and the honest options are the
ordinary ones — drop it (the ops it covers replay from the previous checkpoint or from genesis, where they are still
held) or keep the store read-only under a 0.102.x reader.

**The re-seal is removed in the release after 0.103.0.** `CheckpointReseal` and `SqliteSink.resealCheckpoints` are
migration scaffolding: the 0.102.0 pre-image survives only there, and nothing on a live path computes it.

## Verifying the upgrade

After the re-seal, `CheckpointedReplay.applyFromCheckpoint` resumes every stream to the state a replay from genesis
reaches. The `chain/` corpus carries a checkpoint sealed by Core's rule and its tail-break vectors (`checkpoint` in
`chain/chain-corpus.json`); a host that seals checkpoints reproduces its `snapshotHash` byte for byte and refuses
each vector at the index it names. The suites are `CoreSnapshotTests` (Core's `Snapshots.take` / `verify` and
`Compacted.replayFrom` over both stores, the tail breaks and the re-seal) and `CheckpointTests`.
