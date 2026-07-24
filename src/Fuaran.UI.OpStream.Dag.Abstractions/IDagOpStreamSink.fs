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
//  Topology queries (`Parents` / `Reachable` / `Lca`) are exposed on the sink
//  and delegate to the pure `DagTopology` algorithms over the sink's own
//  parent lookup, so a Sqlite sink can override them with a recursive-CTE
//  implementation without changing the contract.
// ============================================================================

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

    /// Lowest-common-ancestor of two hashes in `streamId` (see `LcaResult`).
    abstract member Lca: streamId: string * a: string * b: string -> Async<LcaResult>

    /// Leaf nodes of `streamId` — records that are no parent's parent. These
    /// are the live branch tips (GC roots together with the trunk head,
    /// checkpoints, and resume coordinates).
    abstract member Heads: streamId: string -> Async<string list>

    /// All records held for `streamId`, order unspecified. Used by retention
    /// (reachability GC) and integrity verification.
    abstract member Records: streamId: string -> Async<DagOpRecord<'Msg> list>

    /// Tombstone the record at `hash`: drop its op payload (reset to a
    /// placeholder) while PRESERVING its hash + parent links, so the chain
    /// still verifies (`DagVerify` accepts a tombstoned node's stored hash).
    /// Returns `true` if a record was tombstoned, `false` if absent. A
    /// reachability-GC root (a live head / checkpoint / resume coordinate)
    /// MUST NOT be tombstoned — that policy is the caller's (retention is a
    /// mechanism, not a policy).
    abstract member Tombstone: streamId: string * hash: string -> Async<bool>

    /// Distinct stream ids the sink currently holds records for. Order
    /// unspecified.
    abstract member Streams: unit -> Async<string list>
