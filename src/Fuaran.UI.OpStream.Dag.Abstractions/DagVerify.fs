namespace Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DagVerify — integrity verification over DAG op-records, through Core
//  (Phase 2645). Two checks:
//
//   1. Parent-linkage. Every parent a record names resolves (no dangling parent).
//   2. Content address. The stored address recomputes under Core's `Dag.nodeId`
//      (`DagOpRecord.recomputeHash`). There is no second address function and no
//      tombstone exemption: a pruned node leaves the live set for the sink's
//      skeleton, so every record a sink returns is a whole node.
//
//  `records` is Core's `Dag.firstBreak` over a CLOSED set, its break mapped onto
//  this tier's error. `recordsResolving` is the sinks' read-path form: a stream's
//  records are not closed (a guest branch's genesis hangs off the host stream's
//  `Mount` op, Phase 267), so the parent leg resolves against the whole store.
//
//  What this proves is what an UNKEYED content address proves — accidental
//  corruption of a stored record, and a structurally broken link. It is not
//  tamper evidence: a writer who edits a record recomputes its address and
//  re-parents its children, and verification passes. See CRYPTO.md.
// ============================================================================

[<RequireQualifiedAccess>]
type DagVerificationError =
    /// A record names a parent that does not resolve.
    | DanglingParent of hash: string * missingParent: string
    /// A record's stored address (`expected`) does not recompute (`actual`).
    | HashMismatch of expected: string * actual: string

module DagVerify =

    /// The content-address leg for ONE record, read in isolation. Parent
    /// linkage is not checked: one record has no set to resolve its parents in.
    let record<'Msg> (r: DagOpRecord<'Msg>) : Result<unit, DagVerificationError> =
        let recomputed = DagOpRecord.recomputeHash r

        if recomputed = r.Hash then
            Ok()
        else
            Error(DagVerificationError.HashMismatch(r.Hash, recomputed))

    /// Both legs over a set that need not be closed: a parent resolves when
    /// `parentResolves` says so. Records are checked in ordinal address order,
    /// the first violation returned, the parent leg before the content leg.
    let recordsResolving<'Msg>
        (parentResolves: string -> bool)
        (recs: DagOpRecord<'Msg> seq)
        : Result<unit, DagVerificationError> =
        let violation (r: DagOpRecord<'Msg>) : DagVerificationError option =
            match r.Parents |> List.tryFind (parentResolves >> not) with
            | Some missing -> Some(DagVerificationError.DanglingParent(r.Hash, missing))
            | None ->
                match record r with
                | Ok() -> None
                | Error e -> Some e

        recs
        |> Seq.map (fun r -> r.Hash, r)
        |> Map.ofSeq
        |> Map.toSeq
        |> Seq.tryPick (snd >> violation)
        |> function
            | Some e -> Error e
            | None -> Ok()

    /// Both legs over a CLOSED set: Core's `Dag.firstBreak` under the tier's
    /// stream, its break mapped onto `DagVerificationError`.
    let records<'Msg> (recs: DagOpRecord<'Msg> seq) : Result<unit, DagVerificationError> =
        match Fuaran.Core.Dag.firstBreak (DagOpRecord.stream ()) (DagOpRecord.toDag recs) with
        | None -> Ok()
        | Some b when b.Reason = Fuaran.Core.DagBreakReason.MissingParent ->
            Error(DagVerificationError.DanglingParent(b.NodeId, b.Got))
        | Some b -> Error(DagVerificationError.HashMismatch(b.Got, b.Expected))

    let describe (streamId: string) (error: DagVerificationError) : string =
        match error with
        | DagVerificationError.DanglingParent(hash, missingParent) ->
            "DAG integrity broken in stream '"
            + streamId
            + "': record "
            + hash
            + " names parent "
            + missingParent
            + ", which is not in the store"
        | DagVerificationError.HashMismatch(expected, actual) ->
            "DAG integrity broken in stream '"
            + streamId
            + "': stored content address "
            + expected
            + " does not recompute (recomputed "
            + actual
            + ")"
