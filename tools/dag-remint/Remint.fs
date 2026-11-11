namespace Fuaran.UI.OpStream.Dag.Remint

open System
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  Remint — a pre-2645 record set re-addressed to Core's `Dag.nodeId`
//  (Phase 2648; the cut is Phase 2645, STABILITY.md 0.104.0).
//
//  Store-wide, in topological order (a guest branch's genesis hangs off a record
//  in its HOST stream, so the address map spans every stream):
//
//   - every live record's stored address must recompute under the pre-cut rule
//     (`Legacy.address`) — a store that was already broken is refused, never
//     laundered into one that verifies;
//   - an ordinary record becomes the node of (its parents' NEW addresses, its
//     actor, its own envelope) — `DagOpRecord.ofEntry`, so the op, timestamp,
//     prompt and result are carried byte for byte and only the address moves;
//   - a merge record is re-minted as a SCRIPT node by re-running the one merge
//     (`DagMerge.mergeDag`) over its re-minted parents, attributed exactly as the
//     record was, and the tree that merge reaches must be the tree the record
//     committed to (its `OutcomeHash`) — a merge whose policy verdict differs from
//     the deleted tree merge's is refused by name, never re-minted to something
//     else;
//   - a tombstoned record keeps its address and goes to the skeleton (address and
//     parents, no payload): its payload was pruned, so no new address can be
//     computed for it, and the address it has is what its live children name.
//
//  The re-mint is pure; `Stores` reads and writes the two store forms.
// ============================================================================

/// Why a record set cannot be re-minted. Each case names the record.
[<RequireQualifiedAccess>]
type RemintFault =
    /// A live record's stored address does not recompute under the pre-cut rule.
    | LegacyAddressBroken of streamId: string * stored: string * recomputed: string
    /// A record names a parent the store does not hold.
    | UnknownParent of streamId: string * hash: string * parent: string
    /// The parent links do not form a DAG.
    | Cyclic of hashes: string list
    /// A record with more than two parents (the cut's merge is two-headed).
    | MergeArity of streamId: string * hash: string * parents: int
    /// A two-parent record with no outcome hash, or an outcome hash on a non-merge.
    | MalformedMerge of streamId: string * hash: string
    /// The store holds a merge and no initial tree was supplied to replay it from.
    | MergeNeedsInitial of streamId: string * hash: string
    /// Re-running the merge did not mint a node; the reason as the merge reported it.
    | MergeNotRemintable of streamId: string * hash: string * reason: string
    /// The cut's reconciliation policy refuses what the deleted tree merge accepted.
    | MergeRefused of streamId: string * hash: string * fault: string
    /// The re-run merge reached a different tree from the one the record committed to.
    | MergeOutcomeMoved of streamId: string * hash: string * committed: string * reached: string
    /// Two pre-cut addresses re-mint to one new address (identical content in one stream).
    | AddressCollision of streamId: string * hashes: string list

module RemintFault =
    let describe (f: RemintFault) : string =
        match f with
        | RemintFault.LegacyAddressBroken(s, stored, recomputed) ->
            sprintf
                "stream '%s': record %s does not recompute under the pre-2645 address rule (it recomputes to %s); the store was already broken, so it is not re-minted"
                s
                stored
                recomputed
        | RemintFault.UnknownParent(s, h, p) ->
            sprintf "stream '%s': record %s names a parent the store does not hold (%s)" s h p
        | RemintFault.Cyclic hs -> sprintf "the parent links form a cycle through %s" (String.Join(", ", hs))
        | RemintFault.MergeArity(s, h, n) ->
            sprintf "stream '%s': record %s has %d parents; the re-mint re-runs two-headed merges only" s h n
        | RemintFault.MalformedMerge(s, h) ->
            sprintf "stream '%s': record %s is malformed (a merge needs two parents AND an outcome hash)" s h
        | RemintFault.MergeNeedsInitial(s, h) ->
            sprintf
                "stream '%s': merge %s is re-minted by re-running the merge, which replays from the stream's initial tree; supply it (--initial <node.json>)"
                s
                h
        | RemintFault.MergeNotRemintable(s, h, why) -> sprintf "stream '%s': merge %s could not be re-run: %s" s h why
        | RemintFault.MergeRefused(s, h, why) ->
            sprintf
                "stream '%s': merge %s is refused by the reconciliation policy the cut merges with (%s); the deleted tree merge accepted it. Re-run with --precedence a|b if the original merge resolved by precedence"
                s
                h
                why
        | RemintFault.MergeOutcomeMoved(s, h, committed, reached) ->
            sprintf
                "stream '%s': merge %s committed to tree %s and the re-run merge reaches %s; it is not re-minted to a different state"
                s
                h
                committed
                reached
        | RemintFault.AddressCollision(s, hs) ->
            sprintf "stream '%s': records %s re-mint to one address" s (String.Join(", ", hs))

/// The re-mint's options.
type RemintOptions<'Msg> =
    {
        /// The tree a stream's history starts from; needed only to re-run merges.
        Initial: Node<'Msg> option
        /// The side a re-run merge re-asserts on a refused pair (`DagMerge`'s
        /// `MergeContext.Precedence`); `None` refuses what the policy refuses.
        Precedence: ReconcileSide option
    }

module RemintOptions =
    let defaults<'Msg> : RemintOptions<'Msg> = { Initial = None; Precedence = None }

/// One stream's line of the receipt.
type StreamReceipt =
    {
        StreamId: string
        /// The head the store recorded (or, for a record set, its tips), old → new.
        Heads: (string * string) list
        Nodes: int
        Merges: int
        Skeleton: int
    }

/// What a re-mint did: the old → new address of every record, per stream.
type RemintReceipt =
    {
        /// `true` when the input was already at the cut's addresses and nothing moved.
        AlreadyCurrent: bool
        Streams: StreamReceipt list
        /// Every pre-cut address to its new one (a skeleton address maps to itself).
        Addresses: Map<string, string>
    }

module RemintReceipt =
    let private q (s: string) = Fuaran.Core.JsonString.quote s

    /// The receipt as canonical JSON (keys Ordinal-sorted, no whitespace).
    let encode (r: RemintReceipt) : string =
        let stream (s: StreamReceipt) =
            let heads =
                s.Heads
                |> List.map (fun (o, n) -> "{\"new\":" + q n + ",\"old\":" + q o + "}")
                |> String.concat ","

            sprintf
                "{\"heads\":[%s],\"merges\":%d,\"nodes\":%d,\"skeleton\":%d,\"streamId\":%s}"
                heads
                s.Merges
                s.Nodes
                s.Skeleton
                (q s.StreamId)

        let addresses =
            r.Addresses
            |> Map.toList
            |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> List.map (fun (o, n) -> q o + ":" + q n)
            |> String.concat ","

        "{\"addresses\":{"
        + addresses
        + "},\"alreadyCurrent\":"
        + (if r.AlreadyCurrent then "true" else "false")
        + ",\"from\":\"pre-2645\",\"streams\":["
        + (r.Streams |> List.map stream |> String.concat ",")
        + "],\"to\":\"Dag.nodeId\",\"tool\":\"dag-remint\"}"

/// A re-minted store: the live records (parents before children), the skeleton,
/// and the receipt.
type Reminted<'Msg> =
    { Records: DagOpRecord<'Msg> list
      Skeleton: DagSkeletonNode list
      Receipt: RemintReceipt }

module Remint =

    /// Parents before children over the whole set (store-wide), ties broken by
    /// timestamp then address so the order is deterministic.
    let private topoOrder (records: LegacyRecord<'Msg> list) : Result<LegacyRecord<'Msg> list, RemintFault> =
        let held = records |> List.map (fun r -> r.Hash) |> Set.ofList

        match
            records
            |> List.tryPick (fun r ->
                r.Parents
                |> List.tryFind (fun p -> not (held.Contains p))
                |> Option.map (fun p -> RemintFault.UnknownParent(r.StreamId, r.Hash, p)))
        with
        | Some fault -> Error fault
        | None ->
            let key (r: LegacyRecord<'Msg>) = r.Timestamp, r.Hash, r.StreamId
            let mutable placed = Set.empty
            let mutable remaining = records |> List.sortBy key
            let out = ResizeArray<LegacyRecord<'Msg>>()
            let mutable progress = true

            while progress && not remaining.IsEmpty do
                let ready, waiting =
                    remaining |> List.partition (fun r -> r.Parents |> List.forall placed.Contains)

                progress <- not ready.IsEmpty

                for r in ready do
                    out.Add r
                    placed <- placed.Add r.Hash

                remaining <- waiting

            if remaining.IsEmpty then
                Ok(List.ofSeq out)
            else
                Error(RemintFault.Cyclic(remaining |> List.map (fun r -> r.Hash) |> List.distinct))

    let private canonicalHash (tree: Node<'Msg>) : string =
        CanonicalJson.encodeNode tree |> Fuaran.Core.Hash.sha256Hex

    let private remintMerge
        (options: RemintOptions<'Msg>)
        (live: DagOpRecord<'Msg> list)
        (r: LegacyRecord<'Msg>)
        (headA: string)
        (headB: string)
        (outcome: string)
        : Result<DagOpRecord<'Msg>, RemintFault> =
        match options.Initial with
        | None -> Error(RemintFault.MergeNeedsInitial(r.StreamId, r.Hash))
        | Some initial ->
            let context =
                { MergeContext.defaults<'Msg> with
                    Attribution =
                        { Actor = r.Actor
                          Prompt = r.PromptId
                          ResultEnvelope = r.ResultEnvelope }
                    Precedence = options.Precedence }

            let notRemintable why =
                Error(RemintFault.MergeNotRemintable(r.StreamId, r.Hash, why))

            let gated =
                DagMerge.mergeDag context r.StreamId initial (DagOpRecord.toDag live) headA headB r.Timestamp

            match gated.Result with
            | MergeResult.Merged(record, tree) ->
                let reached = canonicalHash tree

                if reached = outcome then
                    Ok record
                else
                    Error(RemintFault.MergeOutcomeMoved(r.StreamId, r.Hash, outcome, reached))
            | MergeResult.Refused fault -> Error(RemintFault.MergeRefused(r.StreamId, r.Hash, sprintf "%A" fault))
            | MergeResult.AlreadyMerged h -> notRemintable (sprintf "its parents are one head (%s)" h)
            | MergeResult.FastForward h ->
                notRemintable (sprintf "one parent is the other's ancestor (fast-forward to %s)" h)
            | MergeResult.NoCommonBase -> notRemintable "its parents share no ancestor in the live set"
            | MergeResult.ReplayFailed f -> notRemintable (sprintf "replay failed: %A" f)
            | MergeResult.MintRefused f -> notRemintable (sprintf "Core refused the mint: %A" f)
            | MergeResult.DeltaNotReplayable(_, m) ->
                notRemintable (sprintf "the derived script does not replay (derived %s)" m.DerivedHash)

    /// Re-mint a pre-cut record set. `heads` is each stream's recorded head
    /// (pre-cut addresses); a stream without one reports its tips instead.
    let records
        (options: RemintOptions<'Msg>)
        (heads: Map<string, string>)
        (input: LegacyRecord<'Msg> list)
        : Result<Reminted<'Msg>, RemintFault> =
        let broken =
            input
            |> List.tryPick (fun r ->
                if r.Tombstoned then
                    None
                else
                    let recomputed = Legacy.address r

                    if recomputed = r.Hash then
                        None
                    else
                        Some(RemintFault.LegacyAddressBroken(r.StreamId, r.Hash, recomputed)))

        match broken with
        | Some fault -> Error fault
        | None ->
            match topoOrder input with
            | Error fault -> Error fault
            | Ok ordered ->
                let rec step
                    (map: Map<string, string>)
                    (live: DagOpRecord<'Msg> list)
                    (skeleton: DagSkeletonNode list)
                    (rest: LegacyRecord<'Msg> list)
                    =
                    match rest with
                    | [] -> Ok(map, List.rev live, List.rev skeleton)
                    | r :: tail ->
                        let parents = r.Parents |> List.map (fun p -> Map.find p map)

                        if r.Tombstoned then
                            step
                                (Map.add r.Hash r.Hash map)
                                live
                                ({ StreamId = r.StreamId
                                   Hash = r.Hash
                                   Parents = parents }
                                 :: skeleton)
                                tail
                        else
                            let minted =
                                match parents, r.OutcomeHash with
                                | ([] | [ _ ]), None ->
                                    Ok(
                                        DagOpRecord.ofEntry
                                            r.StreamId
                                            parents
                                            r.Actor
                                            { Op = r.Op
                                              Timestamp = r.Timestamp
                                              PromptId = r.PromptId
                                              ResultEnvelope = r.ResultEnvelope }
                                    )
                                | [ a; b ], Some outcome -> remintMerge options (List.rev live) r a b outcome
                                | ps, _ when List.length ps > 2 ->
                                    Error(RemintFault.MergeArity(r.StreamId, r.Hash, List.length ps))
                                | _ -> Error(RemintFault.MalformedMerge(r.StreamId, r.Hash))

                            match minted with
                            | Error fault -> Error fault
                            | Ok record -> step (Map.add r.Hash record.Hash map) (record :: live) skeleton tail

                match step Map.empty [] [] ordered with
                | Error fault -> Error fault
                | Ok(map, live, skeleton) ->
                    let collision =
                        live
                        |> List.groupBy (fun r -> r.StreamId, r.Hash)
                        |> List.tryFind (fun (_, rs) -> List.length rs > 1)

                    match collision with
                    | Some((s, h), _) ->
                        let olds = map |> Map.filter (fun _ n -> n = h) |> Map.keys |> List.ofSeq
                        Error(RemintFault.AddressCollision(s, olds))
                    | None ->
                        let streams =
                            input
                            |> List.map (fun r -> r.StreamId)
                            |> List.distinct
                            |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

                        let receiptOf (s: string) =
                            let mine = input |> List.filter (fun r -> r.StreamId = s)

                            let oldHeads =
                                match Map.tryFind s heads with
                                | Some h -> [ h ]
                                | None ->
                                    let parented = mine |> List.collect (fun r -> r.Parents) |> Set.ofList

                                    mine
                                    |> List.map (fun r -> r.Hash)
                                    |> List.filter (parented.Contains >> not)
                                    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

                            { StreamId = s
                              Heads = oldHeads |> List.map (fun h -> h, Map.tryFind h map |> Option.defaultValue h)
                              Nodes = mine |> List.filter (fun r -> not r.Tombstoned) |> List.length
                              Merges = mine |> List.filter (fun r -> r.OutcomeHash.IsSome) |> List.length
                              Skeleton = mine |> List.filter (fun r -> r.Tombstoned) |> List.length }

                        Ok
                            { Records = live
                              Skeleton = skeleton
                              Receipt =
                                { AlreadyCurrent = false
                                  Streams = streams |> List.map receiptOf
                                  Addresses = map } }
