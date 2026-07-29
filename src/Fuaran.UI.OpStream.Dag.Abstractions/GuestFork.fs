namespace Fuaran.UI.OpStream.Dag.Abstractions

open System
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  GuestFork — the causal anchor for a mounted guest's forked op-stream
//  (Phase 267, §4o).
//
//  A guest scope (`NodeKind.Mount`, Phase 265) forks its OWN op-stream off the
//  host's. The keying convention (`guest-<scopeId>`) lives in the linear
//  `GuestStream`; the causal ANCHOR that makes the fork reconcilable is a DAG
//  concern and lives here:
//
//    The guest's GENESIS op is recorded as a multi-parent `DagOpRecord`
//    (Phase 178) whose single parent is the CONTENT HASH of the op that
//    instantiated the guest — the host stream's `Mount` creation op.
//
//  Without the anchor, host and guest are two independent chains with no shared
//  parent, so a convergence merge has no LCA. Anchoring the guest genesis to the
//  Mount op gives host+guest a well-defined lowest-common-ancestor (the Mount
//  op itself), turning convergence into an ordinary Phase-179 DAG merge — no new
//  merge math (`GuestConvergence`).
//
//  Purely additive: the anchor is just a parent link on the guest stream's first
//  record. The host stream is untouched; a host that never mounts a guest never
//  produces a guest stream and pays nothing (rung-4, FGP 1). FSharp.Core +
//  Fuaran.UI + Ops + the linear abstractions only (FGP 2) — no orchestration
//  dependency, so it stays Apache-2.0-clean alongside the other DAG abstractions.
// ============================================================================

/// The guest-fork causal-anchor helpers (Phase 267). `genesis` builds a guest
/// stream's first record as a DAG child of the Mount creation op; `provenance`
/// projects a guest op's `(scope, prompt)` back out for per-region attribution.
[<RequireQualifiedAccess>]
module GuestFork =

    /// The op-stream id a guest scope's ops append under (`guest-<scopeId>`) —
    /// the linear `GuestStream.streamId`, surfaced here so a guest-fork caller
    /// keys the same way whether it wields the linear or the DAG sink.
    let streamId (scopeId: string) : string = GuestStream.streamId scopeId

    // Both builders below are thin guest-keyed aliases of `DagOpRecord.create`,
    // which has been Fable-visible since Phase 408 (the pre-405 SHA-256 fence
    // was stale). They carried a `#if !FABLE_COMPILER` fence inherited from that
    // era, which left a Fable host able to name a guest stream but not open one.
    // De-fenced with `DagVerify`.

    /// Build a guest stream's GENESIS `DagOpRecord`: the first op applied inside
    /// guest scope `scopeId`, recorded under `guest-<scopeId>` as a DAG child of
    /// `mountOpHash` — the content hash of the host-stream op that instantiated
    /// the guest (the `Mount` creation op). The resulting record's `Parents` is
    /// `[ mountOpHash ]`, so the host branch and this guest branch share
    /// `mountOpHash` as an ancestor — the LCA a convergence merge resolves on.
    ///
    /// Mirrors `DagOpRecord.create`'s parameter shape; the only difference is
    /// the stream id (guest-keyed) and the guaranteed single parent (the anchor).
    let genesis<'Msg>
        (scopeId: string)
        (mountOpHash: string)
        (op: TreeOp<'Msg>)
        (promptId: string option)
        (userId: string)
        (timestamp: DateTimeOffset)
        (resultEnvelope: OpResultEnvelope)
        : DagOpRecord<'Msg> =
        DagOpRecord.create (GuestStream.streamId scopeId) [ mountOpHash ] op promptId userId timestamp resultEnvelope

    /// Append a subsequent op INSIDE a guest scope: an ordinary single-parent
    /// DAG step under `guest-<scopeId>`, its parent the guest's current head
    /// (`priorGuestHash`). This is a thin guest-keyed alias of `DagOpRecord.create`
    /// for the post-genesis interior ops, so a caller never hand-assembles the
    /// guest stream id.
    let step<'Msg>
        (scopeId: string)
        (priorGuestHash: string)
        (op: TreeOp<'Msg>)
        (promptId: string option)
        (userId: string)
        (timestamp: DateTimeOffset)
        (resultEnvelope: OpResultEnvelope)
        : DagOpRecord<'Msg> =
        DagOpRecord.create (GuestStream.streamId scopeId) [ priorGuestHash ] op promptId userId timestamp resultEnvelope

    /// Project a guest op's provenance — `(scopeId, promptId)` — so "which
    /// region, from which prompt" resolves per guest (consumed by the per-guest
    /// telemetry / portable-export follow-ons). `scopeId` is `None` for a host
    /// record (a non-guest stream id); `promptId` is the record's own field.
    let provenance<'Msg> (record: DagOpRecord<'Msg>) : string option * string option =
        GuestStream.tryScopeOf record.StreamId, record.PromptId
