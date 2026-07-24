namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  DagAudition — audition any node by its content-addressed coordinate
//  (Phase 186).
//
//  "Audition every variation by its content-addressed permalink" (the arranger
//  frontier): select a node's hash, get back the tree AS IT STOOD at that node.
//  The reconstruction is `DagReplay.replay` — fold ops along the node's
//  primary-parent spine from genesis — so a coordinate's snapshot is fully
//  determined by its hash + the records on its spine (no wall-clock, no head
//  state). A merge node's snapshot is the merged tree (its op is the replay
//  delta committing to the outcome).
//
//  The optional `previewHook` is the host's domain-specific render/playback
//  seam: a `Node<'Msg> -> 'Preview` the host supplies (HTML for a UI host, an
//  audio-render handle for a music host, a recalculated grid for a Calc host).
//  The substrate stays domain-general — it hands the host the reconstructed
//  tree and lets the host produce whatever preview it can play. `None` ⇒ a
//  snapshot-only audition (the permalink still resolves to its tree).
// ============================================================================

/// The result of auditioning one coordinate: the node's `Hash`, the
/// reconstructed `Snapshot` tree at that node, and the host's `Preview` if a
/// `previewHook` was supplied (else `None`).
type AuditionResult<'Msg, 'Preview> =
    { Coordinate: string
      Snapshot: Node<'Msg>
      Preview: 'Preview option }

module DagAudition =

    /// Reconstruct the tree at `coordinate` by replaying its primary-parent
    /// spine over `initial`. `getRecord` resolves a hash to its record
    /// (typically a pre-loaded map of the stream). Errors (`UnknownHash`,
    /// `TombstonedOnSpine`, `ApplyFailed`) surface verbatim from `DagReplay`.
    let snapshotAt<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (coordinate: string)
        : Result<Node<'Msg>, DagReplayError> =
        DagReplay.replay getRecord initial coordinate

    /// Audition `coordinate`: reconstruct its snapshot and, if `previewHook` is
    /// supplied, render the host preview from that snapshot. The preview is
    /// computed only on a successful replay — a node that cannot be
    /// reconstructed (unknown / tombstoned-on-spine) produces no preview.
    let audition<'Msg, 'Preview>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (previewHook: (Node<'Msg> -> 'Preview) option)
        (coordinate: string)
        : Result<AuditionResult<'Msg, 'Preview>, DagReplayError> =
        match DagReplay.replay getRecord initial coordinate with
        | Error e -> Error e
        | Ok snapshot ->
            Ok
                { Coordinate = coordinate
                  Snapshot = snapshot
                  Preview = previewHook |> Option.map (fun hook -> hook snapshot) }

    /// Audition straight from a sink: load the stream's records, build the
    /// lookup, and audition `coordinate`. The convenience entry point for an
    /// interactive inspector that holds a live `IDagOpStreamSink`.
    let auditionFromSink<'Msg, 'Preview>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (previewHook: (Node<'Msg> -> 'Preview) option)
        (coordinate: string)
        : Async<Result<AuditionResult<'Msg, 'Preview>, DagReplayError>> =
        async {
            let! records = sink.Records streamId

            let byHash = records |> List.map (fun r -> r.Hash, r) |> Map.ofList

            let getRecord h = Map.tryFind h byHash
            return audition getRecord initial previewHook coordinate
        }
