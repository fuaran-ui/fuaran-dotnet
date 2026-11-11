namespace Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  IDagOpStreamSink — the durable, content-addressed, branching superset of
//  `IOpStreamSink`.
//
//  Two structural differences from the linear sink:
//
//   1. Append is by CONTENT ADDRESS, not by (StreamId, Sequence). `Add` is
//      idempotent on an identical re-append (same hash ⇒ same content) and
//      rejects only a genuine hash collision with differing content. There is
//      no sequence to assign — a node's coordinate IS its hash.
//
//   2. The trunk head is a single mutable cell advanced by an ATOMIC
//      compare-and-swap (`TryAdvanceHead`). Branch appends are contention-free
//      (each writer owns its branch by adding nodes whose parents it chose);
//      the *only* concurrency primitive is the CAS on the trunk head. A
//      single-process sink implements it with a guarded ref; a Sqlite sink
//      with a conditional `UPDATE … WHERE head = expected`.
//
//  Merge functions (`Fuaran.UI.OpStream.Dag.Merge`) REQUIRE this interface, so
//  DAG behaviour is structurally unreachable from the linear sink — a consumer
//  that never references the DAG packages pays nothing (the rung-4 contract).
//
//  Topology is Core's (Phase 2645): `Reachable` is `Dag.ancestorsOf` over the
//  stream's nodes, and a merge base is `Dag.mergeBase` (the sink no longer
//  answers an LCA of its own). A pruned node leaves the live set for the
//  stream's SKELETON — its address and parent links, no payload — so `Records`
//  returns whole nodes only and Core verifies, replays and checkpoints them.
// ============================================================================

/// A pruned node as the sink keeps it beside the live set: its address and its
/// parent links, the payload withdrawn (Phase 2645). The skeleton lets a reader
/// still name the node and the edges it stood on; it is never replayed.
type DagSkeletonNode =
    { StreamId: string
      Hash: string
      Parents: string list }

/// The durable DAG sink contract. All methods are `Async`; concrete sinks
/// implement either truly asynchronously (Sqlite I/O) or synchronously inside
/// `async { ... }` (in-memory). Topology queries are scoped to one `streamId`.
type IDagOpStreamSink<'Msg> =
    /// Add `record` to its stream, addressed by `record.Hash`. Idempotent: a
    /// re-append of an identical record is a no-op. Throws on a hash collision
    /// with differing content (a structural integrity defect). Adding a branch
    /// node never contends — only `TryAdvanceHead` does.
    abstract member Add: record: DagOpRecord<'Msg> -> Async<unit>

    /// The record at `hash` in `streamId`, or `None` if absent.
    abstract member TryGet: streamId: string * hash: string -> Async<DagOpRecord<'Msg> option>

    /// The current trunk-head hash for `streamId`, or `None` if the stream has
    /// no head yet (empty, or never advanced).
    abstract member Head: streamId: string -> Async<string option>

    /// Atomically set the trunk head of `streamId` to `newHead` IFF it is
    /// currently `expected`. `expected = None` swaps from the empty/unset
    /// state (the genesis advance). Returns `true` on success, `false` if the
    /// head had moved (the caller lost the race and must re-read + retry). The
    /// single concurrency primitive of the whole DAG sink.
    abstract member TryAdvanceHead: streamId: string * expected: string option * newHead: string -> Async<bool>

    /// Parent hashes of `hash` (author order), or `[]` for a genesis node or
    /// an unknown hash.
    abstract member Parents: streamId: string * hash: string -> Async<string list>

    /// Ancestor closure of `hash` (inclusive of `hash` itself).
    abstract member Reachable: streamId: string * hash: string -> Async<Set<string>>


    /// Leaf nodes of `streamId` — records that are no parent's parent. These
    /// are the live branch tips (GC roots together with the trunk head,
    /// checkpoints, and resume coordinates).
    abstract member Heads: streamId: string -> Async<string list>

    /// All LIVE records held for `streamId`, order unspecified — whole nodes,
    /// never a pruned one. Used by replay, retention and integrity verification.
    abstract member Records: streamId: string -> Async<DagOpRecord<'Msg> list>

    /// Prune the record at `hash`: withdraw it from the live set and keep its
    /// address and parents in the stream's skeleton. Returns `true` if a live
    /// record was pruned, `false` if it is absent or already pruned. Which
    /// records to prune is the caller's policy: what no retained root needs
    /// (Core's `Dag.prunable`), or the history behind a checkpoint (compaction,
    /// Core's `Dag.compactAt`) — a live node whose ancestors are pruned replays
    /// only from a checkpoint at or above them (`DagFold.replayFrom`), and the
    /// read path resolves its pruned parents against the skeleton.
    abstract member Tombstone: streamId: string * hash: string -> Async<bool>
    /// The pruned nodes of `streamId`, order unspecified.
    abstract member Skeleton: streamId: string -> Async<DagSkeletonNode list>

    /// Distinct stream ids the sink currently holds records for. Order
    /// unspecified.
    abstract member Streams: unit -> Async<string list>

// ============================================================================
//  Phase 2117 — an OPTIONAL extension interface, on the reasoning the linear
//  tier's `IOpStreamBatchSink` records: `IDagOpStreamSink<'Msg>` is shipped and
//  may be implemented outside this repo, so a new abstract member on it would
//  break every implementor. A sink claims the capability; nothing is made to
//  claim it, and nothing emulates it with a loop of `Add`s — a loop is exactly
//  the partial branch the capability exists to rule out.
// ============================================================================

/// Extension of `IDagOpStreamSink<'Msg>` adding an ATOMIC multi-record add.
///
/// Promoting a linear stream writes a whole branch. Added one record at a time,
/// a failure part-way leaves the first records stored and the rest absent — a
/// partial branch with no head. One transaction removes that state: either
/// every record is in the store or none of the batch was written.
///
/// `AddAll` is `Add`'s contract over a list: each record passes the admission
/// `Add` applies (`DagWriteAdmission.admit`), with a parent satisfied by the
/// store OR by a record earlier in the same batch, and an address already held
/// is a no-op. A refusal of ANY record leaves the store exactly as it was. The
/// head is not touched: advancing it stays `TryAdvanceHead`'s.
type IDagOpStreamBatchSink<'Msg> =
    inherit IDagOpStreamSink<'Msg>

    /// Add every record in `records`, in list order, atomically. An empty list
    /// is a no-op. Throws — leaving the store untouched — if any record is
    /// refused.
    abstract member AddAll: records: DagOpRecord<'Msg> list -> Async<unit>

/// The ONE place a DAG sink is asked what it can do (Phase 2117), fenced for
/// Fable exactly as the linear tier's `SinkCapabilities` is: `:? IInterface`
/// does not compile under Fable (the JS runtime carries no interface identity
/// to test against), so under Fable every probe answers `None`. A Fable host
/// that HOLDS a capable sink reaches the contract by naming it — the `*With`
/// entry points take the extension interface directly, which is not a type test.
module DagSinkCapabilities =

    /// The sink's atomic batch add, if it has one. `None` under Fable — always,
    /// whatever the sink is.
    let tryBatch<'Msg> (sink: IDagOpStreamSink<'Msg>) : IDagOpStreamBatchSink<'Msg> option =
#if FABLE_COMPILER
        ignore sink
        None
#else
        match sink with
        | :? IDagOpStreamBatchSink<'Msg> as batch -> Some batch
        | _ -> None
#endif
