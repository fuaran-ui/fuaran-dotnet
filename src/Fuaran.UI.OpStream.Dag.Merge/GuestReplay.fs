namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  GuestReplay — reconstruct a mounted guest's interior from its OWN op-stream
//  alone (Phase 267, §4o).
//
//  A guest stream (`guest-<scopeId>`) is anchored to the host's `Mount` creation
//  op: the guest genesis is a DAG child of the Mount op (`GuestFork.genesis`).
//  That anchor is what lets host+guest CONVERGE (`GuestConvergence`) — but for
//  replaying the guest's OWN interior it must be treated as a boundary, not
//  followed: the guest interior is reconstructed by folding ONLY the guest
//  stream's ops over the guest's initial tree, never crossing back into the host
//  stream through the anchor parent.
//
//  So `replayInterior` walks the guest's primary-parent spine exactly like
//  `DagReplay.replay`, but STOPS as soon as the primary parent is not itself a
//  guest record (that parent is the host-side Mount anchor) — the current node is
//  then the guest genesis, and folding starts from `initialGuestTree`. This is
//  the guest generalisation of "replay each guest stream at its instantiation
//  point": the instantiation point is the anchor boundary, and the interior
//  comes from the guest's own stream.
// ============================================================================

/// Replay a mounted guest's interior from its own stream (Phase 267).
[<RequireQualifiedAccess>]
module GuestReplay =

    /// Reconstruct the guest tree at `guestHead` by folding the guest stream's
    /// ops over `initialGuestTree`, following the primary-parent spine and
    /// STOPPING at the Mount anchor (the first primary parent that is not itself
    /// a guest record). `getGuestRecord` resolves a hash to its record over the
    /// GUEST stream's records only — a hash outside the guest stream (the anchor)
    /// resolves to `None`, which bounds the spine rather than erroring.
    ///
    /// `initialGuestTree` is the guest's seed tree at instantiation (the tree the
    /// guest loader produced), so the fold reconstructs the guest interior from
    /// its own stream alone — the host stream is never consulted. An unknown
    /// `guestHead` surfaces as `UnknownHash`; a tombstoned spine node as
    /// `TombstonedOnSpine`; an apply failure as `ApplyFailed`. Because the
    /// lookup holds the guest stream alone, a guest record missing from the
    /// middle of the spine is indistinguishable from the anchor and bounds the
    /// walk there: the anchor rule cannot tell the two apart without the host
    /// stream, which this function deliberately never reads.
    ///
    /// The walk and the record fold are `DagReplay`'s own (Phase 2043), so a
    /// guest MERGE node is checked against its `OutcomeHash` exactly as on the
    /// host — `MergeOutcomeMismatch` when its delta does not reach the tree it
    /// committed to. Before, this path folded bare `Apply.apply` and replayed
    /// such a node cleanly.
    let replayInterior<'Msg>
        (getGuestRecord: string -> DagOpRecord<'Msg> option)
        (initialGuestTree: Node<'Msg>)
        (guestHead: string)
        : Result<Node<'Msg>, DagReplayError> =
        DagReplay.replayBounded SpineBound.Anchor getGuestRecord initialGuestTree guestHead
