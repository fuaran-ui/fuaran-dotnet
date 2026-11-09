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
/// `SnapshotHash` is Core's STRICT snapshot seal (Phase 2305): the checkpoint
/// IS a `Fuaran.Core.Snapshot<Node<'Msg>>` — `Seq = Sequence`, `State =
/// Snapshot`, `PrevHash = PreviousChainHead`, `Hash = SnapshotHash`, `Mode =
/// Strict` (`CheckpointSeal.toSnapshot`) — and the hash is `Sealed.seal`'s, over
/// `{"snapshot":true,"seq":Sequence,"state":<CanonicalJson.encodeNode Snapshot>}`
/// chained from `PreviousChainHead` by the stream's SHA-256 `HashFn`. Before
/// 2305 the tier sealed the same shape under a `"tree"` key with its own
/// function; a store sealed that way is re-sealed once by `CheckpointReseal`.
/// `StreamId` and `Timestamp` are storage, outside the seal, as they are outside
/// the record chain.
///
/// What the seal binds is the snapshot's POSITION as well as its bytes (the
/// Phase-412 property, which Core's seal has by construction): a valid snapshot
/// and its hash, taken from one `(PreviousChainHead, Sequence)`, no longer
/// validate at a different one — so replaying a real older snapshot as a later
/// checkpoint (cross-position substitution) is caught, as is accidental
/// corruption. It is defence in depth, not tamper-proofing: an adversary who
/// rewrites the whole checkpoint record consistently recomputes this too.
/// Catching that is the signed attestation seam's job (`Attestation.fs`).
type Checkpoint<'Msg> =
    { StreamId: string
      Sequence: int
      PreviousChainHead: string
      SnapshotHash: string
      Snapshot: Node<'Msg>
      Timestamp: DateTimeOffset }

/// The checkpoint as Core's linear snapshot (Phase 2305). Nothing here seals,
/// walks or folds of its own: the seal is `Fuaran.Core.Sealed.seal` (Strict), the
/// verification is `Sealed.firstBreak` — the seal, then the whole tail walked from
/// `(PreviousChainHead, Sequence)` by Core's one chain walker over
/// `HashChain.recordShape` — and the mapping between the two records is total and
/// lossless for everything the seal covers.
[<RequireQualifiedAccess>]
module CheckpointSeal =

    /// The checkpoint as the Core `Snapshot` it is. A UI checkpoint at
    /// `Sequence = N` has folded records `1..N`, which is Core's boundary `Seq = N`
    /// (the count folded, and so the 0-based `Seq` the tail's first record
    /// carries); `PreviousChainHead` is record N's hash, Core's `PrevHash`. Every
    /// UI checkpoint is `Strict`: the tree has a canonical encoder, so the seal
    /// covers it.
    let toSnapshot (checkpoint: Checkpoint<'Msg>) : Fuaran.Core.Snapshot<Node<'Msg>> =
        { Seq = checkpoint.Sequence
          State = checkpoint.Snapshot
          PrevHash = checkpoint.PreviousChainHead
          Hash = checkpoint.SnapshotHash
          Mode = Fuaran.Core.SnapshotMode.Strict }

    /// Core's strict seal of `tree` at `(previousChainHead, sequence)`.
    let seal (previousChainHead: string) (sequence: int) (tree: Node<'Msg>) : string =
        let atAnchor =
            Fuaran.Core.Sealed.seal
                Fuaran.Core.SnapshotMode.Strict
                StreamEntry.hashFn
                CanonicalJson.encodeNode
                { Seq = sequence
                  PrevHash = previousChainHead }
                tree

        atAnchor.Seal

    /// A checkpoint of `tree` at `(previousChainHead, sequence)`, sealed by `seal`
    /// — the one construction every producer goes through, so the seal cannot be
    /// forgotten or computed a second way.
    let create
        (streamId: string)
        (sequence: int)
        (previousChainHead: string)
        (timestamp: DateTimeOffset)
        (tree: Node<'Msg>)
        : Checkpoint<'Msg> =
        { StreamId = streamId
          Sequence = sequence
          PreviousChainHead = previousChainHead
          SnapshotHash = seal previousChainHead sequence tree
          Snapshot = tree
          Timestamp = timestamp }

    /// Where a checkpoint and the tail above it fail to verify — Core's
    /// `Sealed.firstBreak`: the seal first (`SnapshotBreak.SnapshotHash`, the
    /// recomputed seal and the stored one; the tail is not walked), then the FIRST
    /// break anywhere in `tail`, walked from the checkpoint's anchor
    /// (`SnapshotBreak.Tail`, indexed by position in the tail) — so a break in the
    /// middle of the tail is found, not only one at its first link. An empty tail
    /// checks the seal alone. `None` for an intact boundary.
    let firstBreak (checkpoint: Checkpoint<'Msg>) (tail: OpRecord<'Msg> list) : Fuaran.Core.SnapshotBreak option =
        Fuaran.Core.Sealed.firstBreak
            StreamEntry.hashFn
            CanonicalJson.encodeNode
            (HashChain.recordShape<'Msg> ())
            (Fuaran.Core.Sealed.ofSnapshot (toSnapshot checkpoint))
            tail

/// The ONE-SHOT re-seal of a checkpoint sealed before Phase 2305 (the tier's own
/// `"tree"`-keyed pre-image) under Core's seal. Migration scaffolding: it is the
/// only place the old pre-image survives, nothing on a live path computes it, and
/// it is removed in the release after the one that ships it
/// (`docs/migrations/2305-core-snapshot.md`).
[<RequireQualifiedAccess>]
module CheckpointReseal =

    /// The pre-2305 pre-image, frozen: Core's strict shape with `"tree"` where
    /// Core has `"state"`, chained from `previousChainHead` by the same SHA-256.
    let private legacySeal (previousChainHead: string) (sequence: int) (tree: Node<'Msg>) : string =
        Fuaran.Core.OpStream.sha256Hash
            previousChainHead
            ("{\"snapshot\":true,\"seq\":"
             + string sequence
             + ",\"tree\":"
             + CanonicalJson.encodeNode tree
             + "}")

    /// What a re-seal found.
    [<RequireQualifiedAccess>]
    type Outcome<'Msg> =
        /// The checkpoint already verifies under Core's seal; nothing to do, so a
        /// re-run of the migration is a no-op.
        | AlreadySealed
        /// It verified under the pre-2305 seal and is re-sealed under Core's.
        | Resealed of Checkpoint<'Msg>

    /// Why a checkpoint was refused: its stored hash verifies under NEITHER seal,
    /// so it is corrupt (or of neither format) and is not re-blessed.
    type Refusal =
        { StreamId: string
          Sequence: int
          StoredHash: string }

    /// Verify `checkpoint` under the old seal and re-seal it under Core's, or
    /// refuse it by name. A checkpoint already under Core's seal is `AlreadySealed`.
    let reseal (checkpoint: Checkpoint<'Msg>) : Result<Outcome<'Msg>, Refusal> =
        if CheckpointSeal.firstBreak checkpoint [] |> Option.isNone then
            Ok Outcome.AlreadySealed
        elif
            legacySeal checkpoint.PreviousChainHead checkpoint.Sequence checkpoint.Snapshot = checkpoint.SnapshotHash
        then
            Ok(
                Outcome.Resealed
                    { checkpoint with
                        SnapshotHash =
                            CheckpointSeal.seal checkpoint.PreviousChainHead checkpoint.Sequence checkpoint.Snapshot }
            )
        else
            Error
                { StreamId = checkpoint.StreamId
                  Sequence = checkpoint.Sequence
                  StoredHash = checkpoint.SnapshotHash }

    /// The refusal as text, naming the checkpoint.
    let describe (refusal: Refusal) : string =
        sprintf
            "checkpoint re-seal refused for stream '%s' at sequence %d: its stored hash %s verifies under neither the pre-2305 seal nor Core's, so it is not re-sealed. Nothing was written."
            refusal.StreamId
            refusal.Sequence
            refusal.StoredHash

/// Host-provided node codec for `Node<'Msg>` JSON serialisation. Sinks
/// that persist checkpoints to text storage (Sqlite, future Postgres,
/// etc.) take a codec because closure-bearing typed nodes cannot
/// round-trip generically — the `'Msg` shape is host-owned. Hosts that
/// only need integrity verification (no checkpoint resume) can use the
/// `encodeOnly` factory and accept that `LatestCheckpointAtOrBefore`
/// will surface decoder errors.
///
/// `DecodeNode` refuses with the typed `CodecError` (Phase 2065), the same
/// value `IOpJsonCodec.DecodeOp` returns, so a snapshot refusal keeps its code
/// and path.
type INodeJsonCodec<'Msg> =
    abstract member EncodeNode: Node<'Msg> -> string
    abstract member DecodeNode: string -> Result<Node<'Msg>, CodecError>

module NodeJsonCodec =
    /// Codec that encodes via `CanonicalJson.encodeNode` and rejects every
    /// decode. Useful for hosts that need durable hash-chain verification
    /// + checkpoint integrity but never resume from a persisted snapshot
    /// (in-process retention only).
    let encodeOnly<'Msg> () : INodeJsonCodec<'Msg> =
        { new INodeJsonCodec<'Msg> with
            member _.EncodeNode node = CanonicalJson.encodeNode node

            member _.DecodeNode _ =
                Error(CodecError.DecodeUnsupported("NodeJsonCodec.encodeOnly", "DecodeNode")) }

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
    /// stream. Returns the number of records removed. The head never moves: a
    /// truncation that reaches the head record is refused unless a checkpoint at
    /// the head holds the boundary, which `LatestSequence` / `Head` then read
    /// (Phase 1937, D30); invocation keys of the removed records are kept. Callers responsible
    /// for archiving the truncated records first if integrity-verifiable
    /// archival is required (compaction-policy concern, not sink concern).
    abstract member TruncateOpsThrough: streamId: string * throughSequence: int -> Async<int>

    /// Truncate checkpoints with `Sequence < beforeSequence` from the
    /// stream. Returns the number of checkpoints removed. Paired with
    /// `TruncateOpsThrough` by `Compaction.applyPolicy` — keeps the
    /// last K checkpoints by dropping the rest.
    abstract member TruncateCheckpointsBefore: streamId: string * beforeSequence: int -> Async<int>
