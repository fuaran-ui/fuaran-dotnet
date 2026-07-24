# Phase 59 – Op-stream compaction + checkpoint retention

Ships the **checkpoint primitive** + **chain-aware compaction policy** on top of the [`Fuaran.UI.OpStream.*`](../../src/) family. The hash-chained op stream from [Phase 12.Z](12-Z-op-stream.md) grows linearly with session length; replay-from-genesis is fine for short sessions but degrades on the long-lived authoring sessions the closed-loop orchestrator produces. This phase makes replay cost a function of the **checkpoint interval** rather than total history, while preserving end-to-end hash-chain integrity.

## What ships

Four packages touched, all additive:

- **`Fuaran.UI.OpStream.Abstractions`** – new `Checkpoint<'Msg>` record, `INodeJsonCodec<'Msg>` (snapshot round-trip codec), `CompactionPolicy`, `IOpStreamCheckpointSink<'Msg>` (extends `IOpStreamSink<'Msg>`), and `CheckpointVerificationError` envelope. File: `Checkpoint.fs`.
- **`Fuaran.UI.OpStream.InMemory`** – `InMemorySink<'Msg>` now implements `IOpStreamCheckpointSink<'Msg>`. New `InMemorySink.createWithCheckpoints` factory returns the extended interface. Checkpoints stored as typed values in a parallel `Dictionary<string, ResizeArray<Checkpoint<'Msg>>>`; no codec needed.
- **`Fuaran.UI.OpStream.Sqlite`** – new `op_checkpoint` table. `SqliteSink<'Msg>` primary constructor now takes a third `INodeJsonCodec<'Msg>` parameter (snapshot round-trip); legacy two-arg constructor preserved via a secondary `new` defaulting the node codec to `NodeJsonCodec.encodeOnly`. New `SqliteSink.createWithCheckpoints` factory.
- **`Fuaran.UI.OpStream.Replay`** – new `Checkpoint.fs` with three modules: `Checkpoint.create` (materialise + persist), `CheckpointedReplay.applyFromCheckpoint` (resolve nearest + replay tail), `Compaction.applyPolicy` (retention).

## The Checkpoint record

```fsharp
type Checkpoint<'Msg> =
    { StreamId: string
      Sequence: int
      PreviousChainHead: string
      SnapshotHash: string
      Snapshot: Node<'Msg>
      Timestamp: DateTimeOffset }
```

- **`Sequence`** – the op-index this checkpoint materialises **at**. `Snapshot` is the state the apply engine would produce by folding `OpRecord[1..Sequence]` against an initial tree.
- **`PreviousChainHead`** – the chain head at `Sequence`. Equal to `OpRecord(Sequence).Hash` for `Sequence ≥ 1`, or `HashChain.genesisPreviousHash` for a `Sequence = 0` checkpoint over an empty stream. **This is the load-bearing back-link** – when replay resumes at op `Sequence + 1`, the first tail record's `PreviousHash` must equal `PreviousChainHead`.
- **`SnapshotHash`** – `HashChain.sha256Hex (CanonicalJson.encodeNode Snapshot)`. The pinned canonical encoding from [Phase 12.Z](12-Z-op-stream.md) means this is reproducible across .NET / Fable / process restarts. Closure-bearing payloads render as `"<closure>"` sentinels (the v1 limitation declared in 12.Z), so two snapshots differing only in opaque `'Msg` / closure payloads hash identically.
- **`Snapshot`** – the materialised tree. Stored typed in `InMemorySink`; serialised via `INodeJsonCodec.EncodeNode` for `SqliteSink`.

## Integrity boundary

`CheckpointedReplay.applyFromCheckpoint` performs two checks before dispatching to the apply engine:

1. **Snapshot-hash recomputation.** `HashChain.sha256Hex (CanonicalJson.encodeNode cp.Snapshot)` must equal `cp.SnapshotHash`. A mismatch surfaces `CheckpointReplayError.SnapshotHashMismatch` and replay aborts – the snapshot was tampered with after persistence.
2. **Chain back-link.** The first tail record's `PreviousHash` must equal `cp.PreviousChainHead`. A mismatch surfaces `CheckpointReplayError.BoundaryMismatch` – either the checkpoint was forged against a different chain, or the tail was rewritten after the checkpoint was taken.

If both checks pass, `Replay.applyTo cp.Snapshot tail` produces the resumed tree. The cost is `O(targetSequence - cp.Sequence)` apply-engine invocations instead of `O(targetSequence)` – the "checkpoint interval" bound the phase goal declares.

### Equivalence property

Pinned by the Expecto suite ([`CheckpointTests.fs`](../../src/Fuaran.UI.OpStream.Tests/CheckpointTests.fs) – "Equivalence – replay-from-checkpoint matches replay-from-genesis"):

```
Replay.applyTo genesis (records[1..N])
  == CheckpointedReplay.applyFromCheckpoint sink genesis streamId N
```

…when the sink contains a valid checkpoint at any sequence in `[1..N]`. The structural-equality check is on `childIds`, not full `Node<'Msg>` equality, because the typed tree carries closures with no structural equality – same posture as the existing `ReplayTests`.

## Compaction policy

```fsharp
type CompactionPolicy = { KeepCheckpoints: int }
```

`Compaction.applyPolicy sink streamId policy`:

1. Lists all checkpoints in ascending sequence order.
2. If `policy.KeepCheckpoints ≤ 0` or `checkpoints.Length ≤ policy.KeepCheckpoints`, returns 0 (no-op).
3. Otherwise: retains the last `KeepCheckpoints`, identifies the oldest retained checkpoint's `Sequence` (= `M`), and calls `sink.TruncateOpsThrough(streamId, M)` + `sink.TruncateCheckpointsBefore(streamId, M)`.

The truncated `1..M` prefix is collapsed into the retained checkpoint's `Snapshot`; the surviving `M+1..N` tail remains hash-chain verifiable on its own because the retained checkpoint's `PreviousChainHead` pins the chain head at `M`. Future replay against this stream uses `applyFromCheckpoint` – `Replay.applyTo` against ops `1..N` would surface `Replay.ReplayError.ApplyFailed` if the genesis prefix is needed, because the truncated ops are gone from the live sink.

### Archive contract

This phase ships **truncation**. Hosts that need integrity-verifiable archival (regulatory retention, audit replay against the original chain) must archive the truncated records **before** calling `Compaction.applyPolicy`. The archived final record's `Hash` must equal the retained checkpoint's `PreviousChainHead`; otherwise the archive is not a faithful prefix and downstream verification will detect the gap. The sink interface does not enforce this – it's a host responsibility, intentionally kept out of scope so the sink stays a pure persistence primitive.

## Schema additions

### `op_checkpoint` table (Sqlite)

```sql
CREATE TABLE IF NOT EXISTS op_checkpoint (
    stream_id            TEXT    NOT NULL,
    sequence             INTEGER NOT NULL,
    previous_chain_head  TEXT    NOT NULL,
    snapshot_hash        TEXT    NOT NULL,
    snapshot_json        TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    PRIMARY KEY (stream_id, sequence)
);
```

Composite primary key matches `op_stream`. `snapshot_json` is the host-codec encoding of `cp.Snapshot` – see "Node-codec contract" below. `AppendCheckpoint` is a single `INSERT`; PK collision throws `invalidOp` (same posture as the duplicate-`OpRecord` invariant). `LatestCheckpointAtOrBefore` is a single `SELECT ... WHERE sequence <= @upto ORDER BY sequence DESC LIMIT 1`. `TruncateOpsThrough` and `TruncateCheckpointsBefore` are direct `DELETE` statements.

### `op_stream` (unchanged)

The base table from [Phase 12.Z](12-Z-op-stream.md) is unmodified. `TruncateOpsThrough` removes rows in-place; the table is the live tail after compaction.

## Node-codec contract

Phase 12.Z introduced `IOpJsonCodec<'Msg>` for op JSON round-trip. Phase 59 introduces the parallel `INodeJsonCodec<'Msg>` for snapshot round-trip:

```fsharp
type INodeJsonCodec<'Msg> =
    abstract member EncodeNode: Node<'Msg> -> string
    abstract member DecodeNode: string -> Result<Node<'Msg>, string>

module NodeJsonCodec =
    /// Encodes via `CanonicalJson.encodeNode`; rejects every decode.
    let encodeOnly<'Msg> () : INodeJsonCodec<'Msg> = ...
```

The host supplies the codec it owns the `'Msg` shape for. Hosts that need integrity verification + checkpoint append only – but never resume from a persisted snapshot – can pass `NodeJsonCodec.encodeOnly`; `AppendCheckpoint` writes successfully (encoding is purely additive), but `LatestCheckpointAtOrBefore` / `ListCheckpoints` will surface a decoder error when a checkpoint exists.

This mirrors the 12.Z codec contract exactly – the same "host-owned `'Msg` decoder" rationale applies.

## Backwards-compatibility posture

The phase is **additive minor bump** for `Fuaran.UI.OpStream.Abstractions`. The existing `IOpStreamSink<'Msg>` interface is unmodified; the new `IOpStreamCheckpointSink<'Msg>` extends it but is a separate interface, so existing implementations compile unchanged.

`SqliteSink<'Msg>`'s primary constructor signature changed (added `nodeCodec` parameter), but a secondary constructor `new(connectionString, codec)` preserves the legacy two-arg shape – existing call sites continue to compile. The class always implements `IOpStreamCheckpointSink<'Msg>`; callers using the legacy factory get the base interface, callers using `createWithCheckpoints` get the extended one.

`InMemorySink<'Msg>` is similar: the class always implements `IOpStreamCheckpointSink<'Msg>`; existing factory `InMemorySink.create` returns the base interface, new `createWithCheckpoints` returns the extended one. No constructor changes; no host code breaks.

`Fuaran.UI.OpStream.Replay`'s `ReplayError` DU is unchanged; the new `CheckpointReplayError` envelope is a separate DU returned by `applyFromCheckpoint`, with a `TailApplyFailed of ReplayError` case to embed the underlying apply-failure when the tail replay fails.

## Verification

1. `dotnet build Fuaran.sln -c Release` – clean.
2. `dotnet run --project src/Fuaran.UI.OpStream.Tests` – 58 tests, including the 9 Phase 59 acceptance tests:
   - Empty stream resolves to initial tree.
   - Equivalence – replay-from-checkpoint == replay-from-genesis.
   - `Checkpoint.create` materialises at the sink's `LatestSequence`.
   - Snapshot-hash mismatch surfaces.
   - Boundary mismatch surfaces.
   - Compaction retains last K, truncates older ops, retains chain-verifiability.
   - Retention disabled (`KeepCheckpoints ≤ 0`) is a no-op.
   - Fewer checkpoints than threshold is a no-op.
   - Sqlite Append + List + Truncate round-trip.
3. `dotnet run --project src/Fuaran.UI.Tests` – 209 tests; no regressions.
4. `dotnet run --project src/Fuaran.UI.Ops.Tests` – 35 tests; no regressions.

## Rollback

The phase is purely additive. Removing `Checkpoint.fs` from the Abstractions and Replay projects, restoring the InMemorySink / SqliteSink to their pre-59 shape (drop the new interface methods + the secondary constructor / new factories), and dropping the `op_checkpoint` table backs out the phase cleanly. Existing 12.Z streams continue to replay through `Replay.applyTo` unchanged.

## See also

- [`12-Z-op-stream.md`](12-Z-op-stream.md) – the canonical-JSON encoder + hash-chain algorithm Phase 59 builds against.
- [`12-E-0-json-decoder.md`](12-E-0-json-decoder.md) – `JsonDecode.decodeNode` (produces `Node<obj>`); the basis a host would build its `INodeJsonCodec<'Msg>` on for snapshot read-back.
- [`Fuaran.UI.OpStream.Replay/Checkpoint.fs`](../../src/Fuaran.UI.OpStream.Replay/Checkpoint.fs) – the three replay/compaction modules.
