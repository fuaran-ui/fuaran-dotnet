namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  DagAudition — audition any node by its content-addressed coordinate
//  (Phase 186).
//
//  "Audition every variation by its content-addressed permalink" (the arranger
//  frontier): select a node's hash, get back the tree AS IT STOOD at that node.
//  The reconstruction is Core's closure fold (`DagFold.replayTo`, Phase 2645)
//  — every node of the coordinate's ancestor closure, in Core's deterministic
//  order, from genesis — so a coordinate's snapshot is fully determined by its
//  hash + its closure (no wall-clock, no head state). A merge node's snapshot is
//  the merged tree: both branches folded, then its script.
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

    /// Reconstruct the tree at `coordinate` by Core's closure fold over `dag`
    /// from `initial`. Core's replay faults (`UnknownHead`, `Cyclic`,
    /// `Rejected`) surface verbatim.
    let snapshotAt<'Msg>
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (initial: Node<'Msg>)
        (coordinate: string)
        : Result<Node<'Msg>, Fuaran.Core.Dag.ReplayFault<ApplyError>> =
        DagFold.replayTo initial dag coordinate

    /// Audition `coordinate`: reconstruct its snapshot and, if `previewHook` is
    /// supplied, render the host preview from that snapshot. The preview is
    /// computed only on a successful replay — a node that cannot be
    /// reconstructed (unknown / tombstoned-on-spine) produces no preview.
    let audition<'Msg, 'Preview>
        (dag: Fuaran.Core.Dag.T<StreamEntry<'Msg>>)
        (initial: Node<'Msg>)
        (previewHook: (Node<'Msg> -> 'Preview) option)
        (coordinate: string)
        : Result<AuditionResult<'Msg, 'Preview>, Fuaran.Core.Dag.ReplayFault<ApplyError>> =
        match DagFold.replayTo initial dag coordinate with
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
        : Async<Result<AuditionResult<'Msg, 'Preview>, Fuaran.Core.Dag.ReplayFault<ApplyError>>> =
        async {
            let! records = sink.Records streamId

            let dag = DagOpRecord.toDag records
            return audition dag initial previewHook coordinate
        }
