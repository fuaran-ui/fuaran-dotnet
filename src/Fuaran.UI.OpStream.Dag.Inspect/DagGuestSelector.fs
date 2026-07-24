namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagGuestSelector — the guest selector over the DAG inspector (Phase 270,
//  §4o, extending Phase 186).
//
//  With N mounted guests running, the variation forest holds the host's
//  records interleaved with each guest's anchored branch (the guest-fork
//  contract journals a guest under the `guest-<scopeId>` stream key). The
//  selector scopes the inspector to one region: enumerate the guests present,
//  pick one, and build the renderable graph over just that guest's branch —
//  sibling guests' and the host's records are excluded by construction.
//
//  Guests are enumerated from the records' STREAM KEYS (`GuestStream`), never
//  from a parallel registration surface — the op-stream is the source of
//  truth and this stays a pure, derived, read-only projection of it (FGP 5),
//  exactly like `DagGraphModel` beneath it.
//
//  A guest branch's genesis is anchored on the host-stream `Mount` op, so in
//  a guest-scoped view its anchor parent is (deliberately) absent: the graph
//  renders what it holds — the guest genesis sits at depth 0 with its anchor
//  edge pointing out of the selection (the documented missing-parent
//  behaviour of `DagGraphModel.build`).
//
//  `Rollup` is the opt-in aggregate — everything at once, for the operator
//  who wants the whole forest. The default posture is per-guest isolation:
//  a selector UI starts from `Host` / `Guest` and offers `Rollup` explicitly.
// ============================================================================

/// Which slice of the variation forest the inspector renders: the host's own
/// records, one mounted guest's branch, or the opt-in everything-at-once
/// aggregate.
[<RequireQualifiedAccess>]
type DagGuestSelection =
    | Host
    | Guest of scopeId: string
    | Rollup

[<RequireQualifiedAccess>]
module DagGuestSelector =

    /// Enumerate the guest scope ids present in a record set — derived from
    /// the `guest-<scopeId>` stream keys (FGP 5 — never a registry). Sorted
    /// + distinct for stable selector rendering.
    let guests (records: DagOpRecord<'Msg> list) : string list =
        records
        |> List.choose (fun r -> GuestStream.tryScopeOf r.StreamId)
        |> List.distinct
        |> List.sort

    /// The records a selection admits: the host view drops every guest
    /// stream, a guest view keeps exactly that guest's stream, and the
    /// rollup keeps everything.
    let select (selection: DagGuestSelection) (records: DagOpRecord<'Msg> list) : DagOpRecord<'Msg> list =
        match selection with
        | DagGuestSelection.Host -> records |> List.filter (fun r -> not (GuestStream.isGuestStream r.StreamId))
        | DagGuestSelection.Guest scopeId ->
            let streamId = GuestStream.streamId scopeId
            records |> List.filter (fun r -> r.StreamId = streamId)
        | DagGuestSelection.Rollup -> records

    /// Build the renderable graph for a selection — `DagGraphModel.build`
    /// over just the selected slice, so selecting guest A excludes sibling
    /// guests' and the host's records by construction. The graph's
    /// `StreamId` labels the selection: the guest's stream key, the host's
    /// own stream id (when the host slice is single-stream), or `"rollup"`.
    let graphFor (selection: DagGuestSelection) (records: DagOpRecord<'Msg> list) : DagGraph =
        let selected = select selection records

        let streamLabel =
            match selection with
            | DagGuestSelection.Guest scopeId -> GuestStream.streamId scopeId
            | DagGuestSelection.Rollup -> "rollup"
            | DagGuestSelection.Host ->
                match selected |> List.map _.StreamId |> List.distinct with
                | [ single ] -> single
                | _ -> "host"

        DagGraphModel.build streamLabel selected
