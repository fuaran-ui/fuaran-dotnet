namespace Fuaran.UI.OpStream.Abstractions

open Fuaran.UI.Ops.Types

// ============================================================================
//  IOpStreamSink — the durable-append interface every concrete sink ships.
//
//  Concrete sinks live in companion packages:
//   - Fuaran.UI.OpStream.InMemory  — Dictionary-backed, ephemeral
//   - Fuaran.UI.OpStream.Sqlite    — Microsoft.Data.Sqlite-backed
//
//  Both implement the same interface; the v1 acceptance set exercises
//  them via interface-targeted Expecto so any future companion sink
//  (Postgres / Kafka / Event Store) wires into the same suite.
// ============================================================================

/// Host-provided codec for `TreeOp<'Msg>` JSON serialisation. Sinks that
/// persist to text storage (Sqlite, future Postgres, etc.) take a codec
/// because closure-bearing typed ops cannot round-trip generically — the
/// `'Msg` shape is host-owned. Hosts that need integrity verification only
/// (no read-back) can use the `defaultEncodeOnly` factory and accept that
/// `Replay` will return decoder errors.
type IOpJsonCodec<'Msg> =
    abstract member EncodeOp: TreeOp<'Msg> -> string
    abstract member DecodeOp: string -> Result<TreeOp<'Msg>, string>

module OpJsonCodec =
    /// Codec that encodes via `CanonicalJson.encodeOp` and rejects every
    /// decode. Useful for hosts that need durable hash-chain verification
    /// but never invoke `Replay`.
    let encodeOnly<'Msg> () : IOpJsonCodec<'Msg> =
        { new IOpJsonCodec<'Msg> with
            member _.EncodeOp op = CanonicalJson.encodeOp op

            member _.DecodeOp _ =
                Error "OpJsonCodec.encodeOnly does not implement DecodeOp" }

/// The durable sink contract. All methods are `Async`; concrete sinks
/// implement either truly asynchronously (Sqlite I/O) or synchronously
/// inside `async { ... }` (InMemory). Sinks reject duplicate
/// `(StreamId, Sequence)` pairs as structural defects — the host should
/// query `LatestSequence` before assigning a new record's sequence.
type IOpStreamSink<'Msg> =
    /// Append `record` to its stream. Throws on duplicate
    /// `(StreamId, Sequence)`; otherwise returns when durability for the
    /// underlying sink has been achieved (in-process for InMemory; flushed
    /// for Sqlite).
    abstract member Append: record: OpRecord<'Msg> -> Async<unit>

    /// Return records for `streamId` whose sequence is in
    /// `[fromSequence, toSequence]` (inclusive on both ends), in ascending
    /// sequence order. Empty list if no records in range.
    abstract member Replay: streamId: string * fromSequence: int * toSequence: int -> Async<OpRecord<'Msg> list>

    /// Highest sequence number observed in `streamId`; `0` if the stream
    /// has no records yet. (Sequences begin at `1`.)
    abstract member LatestSequence: streamId: string -> Async<int>

    /// Distinct stream ids the sink currently holds records for. Order
    /// unspecified.
    abstract member Streams: unit -> Async<string list>

// ============================================================================
//  Phase 1485 — the two contracts a durable port owes a consumer, as OPTIONAL
//  extension interfaces rather than as members on `IOpStreamSink<'Msg>`.
//
//  Why extensions. `IOpStreamSink<'Msg>` is a shipped public interface with
//  implementors outside this repo; adding an abstract member to it breaks every
//  one of them at compile time. `IOpStreamCheckpointSink<'Msg>` (Checkpoint.fs)
//  already established the shape here — inherit the base, add the members, let
//  a consumer type-test for the capability — and it is what makes this an
//  additive minor rather than a major.
//
//  Why two interfaces and not one. A compare-and-append and a keyed append are
//  independently implementable: a store that can compare a head cheaply may
//  carry no key index, and vice versa. Both sinks that ship here implement
//  both; a third-party sink is not made to claim what it does not do.
// ============================================================================

/// What an accepted append yields to the caller: the address of the record now
/// in the stream, and its chain hash. Deliberately NOT the whole `OpRecord` —
/// a receipt is what a retry compares against, and the record it names is
/// already recoverable by `Replay` at this address.
type AppendReceipt =
    { StreamId: string
      Sequence: int
      Hash: string }

/// The outcome of a compare-and-append. `StaleHead` NAMES the head the store
/// actually holds, which is the part `IOpStreamSink.Append`'s throwing
/// duplicate-sequence guard could never give a caller: a retry loop can rebuild
/// its record against `actual` without a second round trip, and a conflict is a
/// value rather than an exception.
[<RequireQualifiedAccess>]
type CasAppendOutcome =
    | Appended of receipt: AppendReceipt
    | StaleHead of expected: string * actual: string

/// The outcome of a keyed append. Both cases carry the SAME receipt for one
/// invocation key — that is the contract — and the discriminator says whether
/// this call is what persisted it. A caller that only wants the address can
/// ignore the discriminator; a caller that wants to know whether it was the
/// writer has it without inspecting the store.
[<RequireQualifiedAccess>]
type KeyedAppendOutcome =
    | Appended of receipt: AppendReceipt
    | Duplicate of receipt: AppendReceipt

/// Extension of `IOpStreamSink<'Msg>` adding a typed compare-and-append.
/// Existing `IOpStreamSink<'Msg>` consumers and implementors are unaffected
/// (Stability impact = additive minor bump).
type IOpStreamCasSink<'Msg> =
    inherit IOpStreamSink<'Msg>

    /// The chain hash of the record at `LatestSequence`, or
    /// `HashChain.genesisPreviousHash` when the stream is empty. This is the
    /// value a caller passes as `expectedHead` below, and the value
    /// `CasAppendOutcome.StaleHead` reports as `actual`.
    abstract member Head: streamId: string -> Async<string>

    /// Append `record` ONLY IF the stream's current head is `expectedHead`.
    ///
    /// At the true head this is exactly `Append` plus a receipt. At a stale head
    /// NOTHING is persisted and `StaleHead (expectedHead, actual)` is returned.
    /// The head compared is the one `Head` reports, so a well-formed caller
    /// passes the same value it put in `record.PreviousHash`.
    ///
    /// The no-expectation path is unchanged: `Append` still takes no head and
    /// still THROWS on a duplicate `(StreamId, Sequence)`. A duplicate sequence
    /// reached through `AppendIf` at a matching head is the same structural
    /// defect and throws for the same reason — it is not a stale head, and
    /// reporting it as one would tell the caller to retry a record that is
    /// already there.
    abstract member AppendIf: record: OpRecord<'Msg> * expectedHead: string -> Async<CasAppendOutcome>

/// Extension of `IOpStreamSink<'Msg>` adding a keyed, receipt-returning append —
/// the retry contract. Existing consumers and implementors are unaffected
/// (Stability impact = additive minor bump).
type IOpStreamKeyedSink<'Msg> =
    inherit IOpStreamSink<'Msg>

    /// Append `record` under `invocationKey`, scoped to `record.StreamId`.
    ///
    /// The FIRST call for a key persists the record and returns
    /// `Appended receipt`. Every later call for the same `(StreamId,
    /// invocationKey)` persists NOTHING and returns `Duplicate receipt` — the
    /// same receipt, naming the record the first call produced, whatever the
    /// second call's `record` says. That asymmetry is deliberate: a retry is
    /// recognised by its key, and a caller that rebuilt the record after a lost
    /// acknowledgement (a fresh timestamp, a re-derived sequence) must still be
    /// told about the record it already has, not given a second one.
    ///
    /// The key is opaque to the sink. Choosing one that separates genuinely
    /// distinct invocations is the caller's contract, exactly as it is for
    /// Core's `keyOf` projection.
    abstract member AppendKeyed: record: OpRecord<'Msg> * invocationKey: string -> Async<KeyedAppendOutcome>
