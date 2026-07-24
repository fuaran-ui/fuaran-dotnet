namespace Fuaran.UI.OpStream.Dag.Inspect

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  DagCoordinateDiff — diff any two DAG coordinates (Phase 186).
//
//  Phase 57's `TreeDiff` diffs two ADJACENT snapshots along a linear stream
//  (tree-before vs tree-after one op). The DAG generalises this to ANY two
//  content-addressed coordinates: reconstruct each node's snapshot
//  (`DagReplay`, the same primary-spine fold `DagAudition` uses), then run the
//  identical structural `TreeDiff.diff` between them. The two coordinates need
//  share no ancestry — diffing a branch tip against the trunk head, or two
//  sibling variations against each other, is the load-bearing "what does THIS
//  variation change vs THAT one" query of the variation forest.
//
//  `diffMany` is the N-way fan-out: one baseline coordinate against a list of
//  others (e.g. "show me how each audition differs from the trunk head"),
//  reusing the baseline's single reconstruction.
//
//  The structural classification + closure-sentinel limitations are exactly
//  `TreeDiff`'s (see its preamble) — this module only supplies the two
//  endpoints. Derived + read-only (FGP 5).
// ============================================================================

/// A diff between two DAG coordinates: the `From` / `To` hashes and the
/// structural `TreeDiff` between their reconstructed snapshots.
type CoordinateDiff =
    { From: string
      To: string
      Diff: TreeDiff }

module DagCoordinateDiff =

    /// Diff coordinate `a` (the from-state) against coordinate `b` (the
    /// to-state). Each is reconstructed via `DagReplay.replay` over `initial`;
    /// a replay error on EITHER endpoint surfaces verbatim. The endpoints need
    /// not be related — any two nodes in the stream diff.
    let diff<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (a: string)
        (b: string)
        : Result<CoordinateDiff, DagReplayError> =
        match DagReplay.replay getRecord initial a with
        | Error e -> Error e
        | Ok treeA ->
            match DagReplay.replay getRecord initial b with
            | Error e -> Error e
            | Ok treeB ->
                Ok
                    { From = a
                      To = b
                      Diff = TreeDiff.diff treeA treeB }

    /// Diff `baseline` against each coordinate in `others` (the N-way fan-out).
    /// `baseline` is reconstructed once and reused; the first replay error on
    /// any endpoint aborts with that error. Results preserve `others` order.
    let diffMany<'Msg>
        (getRecord: string -> DagOpRecord<'Msg> option)
        (initial: Node<'Msg>)
        (baseline: string)
        (others: string list)
        : Result<CoordinateDiff list, DagReplayError> =
        match DagReplay.replay getRecord initial baseline with
        | Error e -> Error e
        | Ok baseTree ->
            let rec loop (remaining: string list) (acc: CoordinateDiff list) =
                match remaining with
                | [] -> Ok(List.rev acc)
                | coord :: rest ->
                    match DagReplay.replay getRecord initial coord with
                    | Error e -> Error e
                    | Ok tree ->
                        let cd =
                            { From = baseline
                              To = coord
                              Diff = TreeDiff.diff baseTree tree }

                        loop rest (cd :: acc)

            loop others []

    /// Diff two coordinates straight from a sink: load the stream's records,
    /// build the lookup, and diff. The convenience entry point for an
    /// interactive inspector holding a live `IDagOpStreamSink`.
    let diffFromSink<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (streamId: string)
        (initial: Node<'Msg>)
        (a: string)
        (b: string)
        : Async<Result<CoordinateDiff, DagReplayError>> =
        async {
            let! records = sink.Records streamId

            let byHash = records |> List.map (fun r -> r.Hash, r) |> Map.ofList

            let getRecord h = Map.tryFind h byHash
            return diff getRecord initial a b
        }
