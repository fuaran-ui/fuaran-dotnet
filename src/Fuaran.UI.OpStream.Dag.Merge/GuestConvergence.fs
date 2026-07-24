namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  GuestConvergence — reconcile a mounted guest's forked stream back into the
//  host's, through the SHIPPED Phase-179 DAG merge (Phase 267, §4o).
//
//  A guest stream (`guest-<scopeId>`) is anchored to the host's `Mount` creation
//  op: the guest genesis is a DAG child of that op (`GuestFork.genesis`). So the
//  host branch and the guest branch share the Mount op as an ancestor — a
//  well-defined lowest-common-ancestor. Convergence is therefore an ORDINARY
//  3-way DAG merge with the Mount op as the base; there is NO new merge math —
//  this module only presents the two streams to `DagMerge.merge` as one.
//
//  The presentation is the whole trick. `DagMerge.merge` is single-stream: it
//  reads `sink.Records streamId` and `sink.Lca(streamId, headA, headB)`. Host and
//  guest live in DISTINCT streams, so `converge` builds a read-only VIEW sink
//  whose `Records` returns the UNION of both streams and whose topology
//  (`Lca` / `Parents` / `Reachable`) runs over that union. Record hashes are
//  stream-id-independent (`DagOpRecord.computeHash` folds parents + op +
//  timestamp only — never the stream id), so unioning host + guest records
//  preserves every content hash and parent link: the Mount op stays the shared
//  ancestor, hence the LCA the merge resolves on.
//
//  A guest edit that overlaps a host edit at the same `(NodeId, facet)` cell
//  surfaces the existing `MergeConflict` envelope (`NeedsManualMerge`), never a
//  silent overwrite — exactly the Phase-179 behaviour, inherited unchanged. The
//  default author classifier ranks host ops `Primary` and guest ops `Secondary`
//  (tagged with the guest stream id), so a contended cell lists `KeepPrimary`
//  (keep the host value) first; a host may override the classifier.
// ============================================================================

/// Reconcile a mounted guest's forked op-stream back into the host stream via
/// the shipped Phase-179 merge, with the Mount creation op as the LCA (Phase 267).
[<RequireQualifiedAccess>]
module GuestConvergence =

    /// The default precedence classifier for a host↔guest convergence: a record
    /// on a `guest-<scopeId>` stream is `Secondary` (carrying its guest stream id
    /// as the opaque provenance tag), every other record is `Primary` (the host).
    /// So a contended cell resolves `KeepPrimary` (host) first; pass a custom
    /// classifier to `convergeWith` to change that.
    let hostPrimaryAuthor<'Msg> (record: DagOpRecord<'Msg>) : MergeAuthor =
        if GuestStream.isGuestStream record.StreamId then
            MergeAuthor.Secondary(Some record.StreamId)
        else
            MergeAuthor.Primary

    /// A READ-ONLY `IDagOpStreamSink` view over a fixed record set, all answered
    /// under `viewStreamId`. Only the query surface the merge decision touches
    /// (`Records` / `Lca` / topology) is meaningful; the mutating members are
    /// inert (the caller commits the merge node into the REAL sink). Records keep
    /// their true `StreamId`, so a `recordAuthor` classifier can still tell host
    /// from guest.
    let private viewSink<'Msg> (viewStreamId: string) (records: DagOpRecord<'Msg> list) : IDagOpStreamSink<'Msg> =
        let byHash = records |> List.map (fun r -> r.Hash, r) |> dict

        let getParents (h: string) : string list =
            match byHash.TryGetValue h with
            | true, r -> r.Parents
            | false, _ -> []

        let tryGet (h: string) : DagOpRecord<'Msg> option =
            match byHash.TryGetValue h with
            | true, r -> Some r
            | false, _ -> None

        { new IDagOpStreamSink<'Msg> with
            member _.Records _ = async { return records }
            member _.TryGet(_, hash) = async { return tryGet hash }
            member _.Parents(_, hash) = async { return getParents hash }

            member _.Reachable(_, hash) =
                async { return DagTopology.reachable getParents hash }

            member _.Lca(_, a, b) =
                async { return DagTopology.lca getParents a b }

            member _.Heads _ =
                async {
                    // Leaf nodes of the union — records that are no record's parent.
                    let parentSet = records |> List.collect _.Parents |> Set.ofList
                    return records |> List.map _.Hash |> List.filter (parentSet.Contains >> not)
                }

            member _.Head _ = async { return None }
            member _.Streams() = async { return [ viewStreamId ] }

            // Inert mutators — a convergence merge only READS; the caller commits
            // the built merge node into the real sink via `DagMerge.commitMerge`.
            member _.Add _ = async { return () }
            member _.TryAdvanceHead(_, _, _) = async { return false }
            member _.Tombstone(_, _) = async { return false } }

    /// Converge guest scope `scopeId` into the host stream `hostStreamId` under a
    /// custom precedence classifier. Reads both streams' records from `sink`,
    /// unions them under a `hostStreamId` view, and runs the shipped merge with
    /// `hostHead` as trunk and `guestHead` as the branch. The LCA is the Mount
    /// creation op (the guest genesis's anchor parent), so the result is:
    ///   * `FastForward guestHead` — host made no op since the Mount (guest just
    ///     extends the host);
    ///   * `Merged(record, tree)` — host + guest touched disjoint cells, so they
    ///     auto-merge (`record` is the merge node to commit into the real sink;
    ///     its `Parents` are `[hostHead; guestHead]`, its `StreamId` is
    ///     `hostStreamId`);
    ///   * `NeedsManualMerge conflicts` — host + guest edited the SAME cell, so
    ///     the overlap surfaces as `MergeConflict` envelopes, never a silent
    ///     overwrite.
    /// The built merge node is NOT committed here — the caller adds it to the
    /// real `sink` and advances the host head via `DagMerge.commitMerge`.
    let convergeWith<'Msg>
        (recordAuthor: DagOpRecord<'Msg> -> MergeAuthor)
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
            let view = viewSink hostStreamId (hostRecords @ guestRecords)
            return! DagMerge.merge recordAuthor view hostStreamId initial hostHead guestHead now
        }

    /// `convergeWith` under the default `hostPrimaryAuthor` classifier — host ops
    /// win a contested cell, the guest surfaces as a tagged `Secondary`.
    let converge<'Msg>
        (sink: IDagOpStreamSink<'Msg>)
        (hostStreamId: string)
        (scopeId: string)
        (initial: Node<'Msg>)
        (hostHead: string)
        (guestHead: string)
        (now: System.DateTimeOffset)
        : Async<MergeResult<'Msg>> =
        convergeWith hostPrimaryAuthor sink hostStreamId scopeId initial hostHead guestHead now
