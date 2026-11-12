namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  GuestConvergence — reconcile a mounted guest's forked stream back into the
//  host's, through the DAG's merge (Phase 267, §4o; over Core since Phase 2645).
//
//  A guest stream (`guest-<scopeId>`) is anchored to the host's `Mount` creation
//  op: the guest genesis is a DAG child of that op (`GuestFork.genesis`). So the
//  host branch and the guest branch share the Mount op as an ancestor, and
//  convergence is an ORDINARY merge with the Mount op as the base; there is NO
//  new merge math — this module only presents the two streams to the merge as
//  one DAG. A node's address never folds its stream id, so the union of both
//  streams' nodes keeps every address and parent link.
//
//  A guest edit that collides with a host edit refuses by default, exactly as
//  any merge does. `converge` names the HOST as the precedence side, so a pair
//  both of whose ops apply in either order (two writes of one slot) is settled
//  by re-asserting the host's op; `convergeWith` takes the context whole.
// ============================================================================

/// Reconcile a mounted guest's forked op-stream back into the host stream via
/// the DAG merge, with the Mount creation op as the base (Phase 267).
[<RequireQualifiedAccess>]
module GuestConvergence =

    /// Merge the guest head into the host head over the union of both streams'
    /// live records, under `context` (the host head is side `A`). The merge
    /// node, if any, is minted into `hostStreamId`.
    let convergeWith<'Msg>
        (context: MergeContext<'Msg>)
        (sink: IDagOpStreamSink<'Msg>)
        (hostStreamId: string)
        (scopeId: string)
        (initial: Node<'Msg>)
        (hostHead: string)
        (guestHead: string)
        (now: System.DateTimeOffset)
        : Async<MergeResult<'Msg>> =
        async {
            let! hostRecords = sink.Records hostStreamId
            let! guestRecords = sink.Records(GuestStream.streamId scopeId)
            let dag = DagOpRecord.toDag (hostRecords @ guestRecords)
            return (DagMerge.mergeDag context hostStreamId initial dag hostHead guestHead now).Result
        }

    /// `convergeWith` with the host taking precedence.
    let converge<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (hostStreamId: string)
        (scopeId: string)
        (initial: Node<'Msg>)
        (hostHead: string)
        (guestHead: string)
        (now: System.DateTimeOffset)
        : Async<MergeResult<'Msg>> =
        convergeWith
            { MergeContext.defaults with
                Precedence = Some ReconcileSide.A }
            sink
            hostStreamId
            scopeId
            initial
            hostHead
            guestHead
            now
