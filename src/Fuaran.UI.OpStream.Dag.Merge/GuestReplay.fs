namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  GuestReplay — reconstruct a mounted guest's interior from its OWN op-stream
//  alone (Phase 267, §4o).
//
//  A guest stream (`guest-<scopeId>`) is anchored to the host's `Mount` creation
//  op: the guest genesis is a DAG child of the Mount op (`GuestFork.genesis`).
//  That anchor is what lets host+guest CONVERGE (`GuestConvergence`) — but for
//  replaying the guest's OWN interior it is a boundary, not followed: the
//  interior is the guest stream's ops folded over the guest's initial tree.
//
//  Since Phase 2645 that fold is Core's (`DagFold.replayTo` over the guest
//  stream's nodes alone). The anchor is a parent the guest DAG does not hold,
//  and Core's drain lets a parent the DAG does not hold constrain nothing, so
//  the fold starts at the guest genesis without consulting the host stream.
// ============================================================================

/// Replay a mounted guest's interior from its own stream (Phase 267).
[<RequireQualifiedAccess>]
module GuestReplay =

    /// Reconstruct the guest tree at `guestHead` by folding the GUEST stream's
    /// records over `initialGuestTree` (the guest's seed tree at instantiation).
    /// The host stream is never read. Core's replay faults: an unknown head is
    /// `UnknownHead`, an op the tree refuses `Rejected`.
    let replayInterior<'Msg>
        (guestRecords: DagOpRecord<'Msg> list)
        (initialGuestTree: Node<'Msg>)
        (guestHead: string)
        : Result<Node<'Msg>, Fuaran.Core.Dag.ReplayFault<ApplyError>> =
        DagFold.replayRecords initialGuestTree guestRecords guestHead
