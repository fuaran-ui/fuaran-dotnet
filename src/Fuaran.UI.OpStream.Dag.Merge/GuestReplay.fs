namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.Ops
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
    /// its own stream alone — the host stream is never consulted. A genuinely
    /// unknown hash mid-spine (not the anchor: a hash the guest stream should
    /// contain but does not) still surfaces as `UnknownHash`; a tombstoned spine
    /// node as `TombstonedOnSpine`; an apply failure as `ApplyFailed`.
    let replayInterior<'Msg>
        (getGuestRecord: string -> DagOpRecord<'Msg> option)
        (initialGuestTree: Node<'Msg>)
        (guestHead: string)
        : Result<Node<'Msg>, DagReplayError> =
        // Collect the guest spine head→genesis. The genesis is the deepest node
        // still resolvable in the guest lookup whose primary parent is NOT a
        // guest record (the anchor) — or a true 0-parent root, the degenerate
        // case. Either way the walk stops without following the anchor.
        let rec collect (hash: string) (acc: DagOpRecord<'Msg> list) : Result<DagOpRecord<'Msg> list, DagReplayError> =
            match getGuestRecord hash with
            | None -> Error(DagReplayError.UnknownHash hash)
            | Some r when r.Tombstoned -> Error(DagReplayError.TombstonedOnSpine hash)
            | Some r ->
                match r.Parents with
                | [] -> Ok(r :: acc) // a true genesis (no anchor) — degenerate
                | primary :: _ ->
                    match getGuestRecord primary with
                    | Some _ -> collect primary (r :: acc) // still inside the guest stream
                    | None -> Ok(r :: acc) // primary is the host-side Mount anchor — stop here

        match collect guestHead [] with
        | Error e -> Error e
        | Ok spine ->
            spine
            |> List.fold
                (fun (acc: Result<Node<'Msg>, DagReplayError>) (r: DagOpRecord<'Msg>) ->
                    match acc with
                    | Error _ -> acc
                    | Ok tree ->
                        match Apply.apply r.Op tree with
                        | Ok tree' -> Ok tree'
                        | Error e -> Error(DagReplayError.ApplyFailed(r.Hash, e)))
                (Ok initialGuestTree)
