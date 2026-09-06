namespace Fuaran.UI.OpStream.Abstractions

open System
open Fuaran.UI.Types

// ============================================================================
//  Checkpoint primitive + chain-aware compaction policy.
//
//  A Checkpoint is a materialised snapshot of the apply-engine tree at one
//  op-index. Replay can start from the nearest checkpoint ≤ target rather
//  than from genesis; only the tail (checkpoint.Sequence + 1 .. target)
//  walks the apply engine. Long-session replay cost becomes bounded by
//  (checkpoint interval) rather than (total history).
//
//  Hash-chain back-link. `Checkpoint.PreviousChainHead` pins the chain
//  head AT the checkpoint's op-index — equal to `OpRecord(Sequence).Hash`
//  (or `HashChain.genesisPreviousHash` for a Sequence-0 checkpoint over
//  the initial tree). When replay resumes at op M+1, the first tail
//  record's `PreviousHash` MUST equal `PreviousChainHead`; that's the
//  integrity boundary that lets compaction discard ops 1..M while
//  preserving end-to-end verifiability of the surviving M+1..N segment.
//
//  Archived-segment verifiability. A compaction policy that truncates
//  ops before the oldest retained checkpoint may also archive them. The
//  archive's final record's `Hash` must equal the retained checkpoint's
//  `PreviousChainHead`, otherwise the archive isn't a faithful prefix of
//  the original chain. Per FGP 5 the op stream stays the source of truth
//  — compaction can shorten the live tail, never silently break the chain.
//
//  FGP 2 / FGP 6. Adds no dependency of its own: `Snapshot` is a
//  `Node<'Msg>`, so this file needs only what the tier already carries. (It
//  used to claim "`FSharp.Core` + `Fuaran.UI` only" for the whole package;
//  since Phase 406/465 the package also references the Fuaran.Core op-stream
//  and function tiers, both FSharp.Core-only and Fable-clean, so the FGP-2
//  property holds and the enumeration did not. Corrected Phase 1525.) The
//  checkpoint primitive ships in the abstractions tier alongside `OpRecord` and
//  stays Apache-2.0-clean.
// ============================================================================

/// A materialised snapshot at one op-index. The `Snapshot` is the state the
/// apply engine would produce by folding `OpRecord[1..Sequence]` against
/// the genesis tree. `PreviousChainHead` is the chain head AT this op-index —
/// equal to `OpRecord(Sequence).Hash` for `Sequence >= 1`, or
/// `HashChain.genesisPreviousHash` for a Sequence-0 checkpoint over an initial
/// tree.
///
/// `SnapshotHash` is `HashChain.snapshotHash PreviousChainHead Sequence
/// (CanonicalJson.encodeNode Snapshot)` — a POSITION-BOUND content address
/// (Phase 412). Corrected here in Phase 1525: this comment still described the
/// pre-412 rule, `sha256Hex (CanonicalJson.encodeNode Snapshot)` alone, which a
/// reader recomputing the field from it would have got wrong, and which
/// understates what the field protects.
///
/// The binding is what the position adds: a valid snapshot and its hash, taken
/// from one `(PreviousChainHead, Sequence)`, no longer validate at a different
/// one — so replaying a real older snapshot as a later checkpoint
/// (cross-position substitution) is caught, as is accidental corruption. It is
/// defence in depth, not tamper-proofing: an adversary who rewrites the whole
/// checkpoint record consistently recomputes this too. Catching that is the
/// signed attestation seam's job (`Attestation.fs`).
type Checkpoint<'Msg> =
    { StreamId: string
      Sequence: int
      PreviousChainHead: string
      SnapshotHash: string
      Snapshot: Node<'Msg>
      Timestamp: DateTimeOffset }

/// Host-provided node codec for `Node<'Msg>` JSON serialisation. Sinks
/// that persist checkpoints to text storage (Sqlite, future Postgres,
/// etc.) take a codec because closure-bearing typed nodes cannot
/// round-trip generically — the `'Msg` shape is host-owned. Hosts that
/// only need integrity verification (no checkpoint resume) can use the
/// `encodeOnly` factory and accept that `LatestCheckpointAtOrBefore`
/// will surface decoder errors.
type INodeJsonCodec<'Msg> =
    abstract member EncodeNode: Node<'Msg> -> string
    abstract member DecodeNode: string -> Result<Node<'Msg>, string>

module NodeJsonCodec =
    /// Codec that encodes via `CanonicalJson.encodeNode` and rejects every
    /// decode. Useful for hosts that need durable hash-chain verification
    /// + checkpoint integrity but never resume from a persisted snapshot
    /// (in-process retention only).
    let encodeOnly<'Msg> () : INodeJsonCodec<'Msg> =
        { new INodeJsonCodec<'Msg> with
            member _.EncodeNode node = CanonicalJson.encodeNode node

            member _.DecodeNode _ =
                Error "NodeJsonCodec.encodeOnly does not implement DecodeNode" }

/// Configurable retention policy. Compaction keeps the last
/// `KeepCheckpoints` checkpoints; ops with `Sequence ≤ oldest-retained-
/// checkpoint.Sequence` are truncated from the live sink (they are
/// collapsed into the snapshot and unreachable from any surviving
/// replay path). `KeepCheckpoints` of `1` is the minimum useful setting;
/// `0` or negative disables retention so compaction is a no-op.
type CompactionPolicy = { KeepCheckpoints: int }

module CompactionPolicy =
    /// Default policy — retain the three most recent checkpoints.
    let defaults: CompactionPolicy = { KeepCheckpoints = 3 }

    /// Single-knob constructor.
    let keep (n: int) : CompactionPolicy = { KeepCheckpoints = n }

/// Outcome of the checkpoint→tail boundary integrity check performed by
/// `Replay.applyFromCheckpoint`. `BoundaryMismatch` means the first tail
/// record's `PreviousHash` does not link to the retained checkpoint;
/// `SnapshotHashMismatch` means the snapshot's recomputed POSITION-BOUND
/// address (`HashChain.snapshotHash`, Phase 412) no longer matches the recorded
/// `SnapshotHash` — so it covers a snapshot that was tampered with AND one
/// lifted from another position, which the pre-412 "canonical-JSON hash" this
/// comment used to name could not distinguish (corrected Phase 1525).
[<RequireQualifiedAccess>]
type CheckpointVerificationError =
    | BoundaryMismatch of checkpointSequence: int * expected: string * actual: string
    | SnapshotHashMismatch of checkpointSequence: int * expected: string * actual: string

/// Extension of `IOpStreamSink<'Msg>` adding checkpoint persistence +
/// truncation primitives. Concrete sinks that ship as part of the
/// language tier (InMemory, Sqlite) implement this interface in addition
/// to the base; existing `IOpStreamSink<'Msg>` consumers are unaffected
/// (Stability impact = additive minor bump).
type IOpStreamCheckpointSink<'Msg> =
    inherit IOpStreamSink<'Msg>

    /// Persist a checkpoint. Sinks reject duplicate
    /// `(StreamId, Sequence)` checkpoints as structural defects — the
    /// host should query `LatestCheckpointAtOrBefore` (or maintain its
    /// own cadence) before materialising a new one.
    abstract member AppendCheckpoint: checkpoint: Checkpoint<'Msg> -> Async<unit>

    /// Return the most recent checkpoint with `Sequence ≤ upToSequence`
    /// for `streamId`, or `None` if no checkpoint is in range.
    abstract member LatestCheckpointAtOrBefore: streamId: string * upToSequence: int -> Async<Checkpoint<'Msg> option>

    /// All checkpoints for `streamId`, in ascending `Sequence` order.
    abstract member ListCheckpoints: streamId: string -> Async<Checkpoint<'Msg> list>

    /// Truncate ops with `Sequence ≤ throughSequence` from the live
    /// stream. Returns the number of records removed. Callers responsible
    /// for archiving the truncated records first if integrity-verifiable
    /// archival is required (compaction-policy concern, not sink concern).
    abstract member TruncateOpsThrough: streamId: string * throughSequence: int -> Async<int>

    /// Truncate checkpoints with `Sequence < beforeSequence` from the
    /// stream. Returns the number of checkpoints removed. Paired with
    /// `TruncateOpsThrough` by `Compaction.applyPolicy` — keeps the
    /// last K checkpoints by dropping the rest.
    abstract member TruncateCheckpointsBefore: streamId: string * beforeSequence: int -> Async<int>
