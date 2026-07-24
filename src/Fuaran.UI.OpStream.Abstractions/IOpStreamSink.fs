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
