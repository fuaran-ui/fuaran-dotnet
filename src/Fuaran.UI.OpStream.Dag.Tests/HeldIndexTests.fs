module Fuaran.UI.OpStream.Dag.Tests.HeldIndexTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The index a sink holds across calls (Phase 2716).
//
//  Both DAG sinks hold Core's reachability index beside their node map, build
//  it once, and extend it with every record they admit (`DagSinkIndex`). The
//  index is a COST device, so what these tests hold is that it is never a
//  different answer: after EVERY write of a generated history — appends, forks,
//  merges through the sink (which now merge over the held index), criss-cross
//  twins, guest branches under their own stream ids — the index the sink holds
//  answers every ancestry question exactly as an index built fresh from the
//  sink's records does. A prune drops it; the next question rebuilds it.
//
//  Go-red: an index not extended by a write (the pre-write index kept), or
//  extended with a node other than the record's, fails the first comparison
//  after that write.
// ============================================================================

type private Reach = Fuaran.Core.Dag.Reach<StreamEntry<TestMsg>>

let private indexedOf (sink: IDagOpStreamSink<TestMsg>) : IDagOpStreamIndexedSink<TestMsg> =
    match DagSinkCapabilities.tryIndexed sink with
    | Some indexed -> indexed
    | None -> failtest "the sink does not hold its index"

let private heldReach (sink: IDagOpStreamSink<TestMsg>) (streamId: string) : Reach =
    (indexedOf sink).Reach streamId |> Async.RunSynchronously

/// The held index answers every question an index built fresh over the sink's
/// live records answers: the node set, each node's ancestors and order, and
/// every pair's merge base, delta and reachability.
let private equalsFresh (label: string) (sink: IDagOpStreamSink<TestMsg>) (streamId: string) =
    let held = heldReach sink streamId
    let records = sink.Records streamId |> Async.RunSynchronously
    let fresh = Fuaran.Core.Dag.Reach.ofDag (DagOpRecord.toDag records)
    let ids = records |> List.map _.Hash |> List.sort

    let between (r: Reach) a b =
        Fuaran.Core.Dag.Reach.between r a b |> List.map _.Id

    Expect.equal
        ((Fuaran.Core.Dag.Reach.dag held).Nodes.Keys |> List.ofSeq |> List.sort)
        ids
        $"{label}: the held index is over the sink's live records"

    for a in ids do
        Expect.equal
            (Fuaran.Core.Dag.Reach.ancestors held a)
            (Fuaran.Core.Dag.Reach.ancestors fresh a)
            $"{label}: ancestors {a}"

        Expect.equal
            (Fuaran.Core.Dag.Reach.tryTopoOrder held a)
            (Fuaran.Core.Dag.Reach.tryTopoOrder fresh a)
            $"{label}: order {a}"

        for b in ids do
            Expect.equal
                (Fuaran.Core.Dag.Reach.mergeBase held a b)
                (Fuaran.Core.Dag.Reach.mergeBase fresh a b)
                $"{label}: mergeBase {a} {b}"

            Expect.equal (between held a b) (between fresh a b) $"{label}: between {a} {b}"

            Expect.equal
                (Fuaran.Core.Dag.Reach.reaches held a b)
                (Fuaran.Core.Dag.Reach.reaches fresh a b)
                $"{label}: reaches {a} {b}"

let private streamId = "held"
let private paneCount = 6

let private initialTree () : Node<TestMsg> =
    container "dash" [ for i in 0 .. paneCount - 1 -> Fuaran.markdown $"p{i}" $"Pane {i}" ]

let private tones =
    [| ToneVariant.Default
       ToneVariant.Brand
       ToneVariant.Success
       ToneVariant.Warning
       ToneVariant.Critical |]

/// A seeded history written THROUGH `sink`, the held index compared with a
/// fresh one after every write. Returns (nodes, merges, twins, guests, prunes).
let private history (sink: IDagOpStreamSink<TestMsg>) (seed: int) (steps: int) =
    let rng = Random seed
    let initial = initialTree ()
    let actor = Actor.Human "gen"
    let genesis = stepRecord streamId None (TreeOp.Batch []) 10L
    add sink genesis
    // Build the index now, so every later write EXTENDS it rather than the next
    // question building it.
    heldReach sink streamId |> ignore
    let heads = ResizeArray<string>([ genesis.Hash ])
    let all = ResizeArray<string>([ genesis.Hash ])
    let guestHeads = ResizeArray<string * string * Node<TestMsg>>()
    let mutable clock = 100L
    let mutable merges = 0
    let mutable twins = 0
    let mutable guests = 0
    let mutable prunes = 0

    let tick () =
        clock <- clock + 1L
        clock

    let label () = $"seed {seed} after write {all.Count}"
    let treeAt (head: string) = replayHead sink streamId initial head

    let nextOp (tree: Node<TestMsg>) =
        let pane = $"p{rng.Next paneCount}"

        let op =
            if rng.Next 8 = 0 && Introspect.findNode (NodeId pane) tree |> Option.isSome then
                TreeOp.RemoveNode(NodeId pane)
            else
                TreeOp.PatchStyle(
                    NodeId pane,
                    { StyleDelta.empty with
                        Tone = Some tones[rng.Next tones.Length] }
                )

        match Apply.apply op tree with
        | Ok next -> Some(op, next)
        | Error _ -> None

    let write (record: DagOpRecord<TestMsg>) =
        add sink record
        equalsFresh (label ()) sink record.StreamId

    let merge (a: string) (b: string) =
        let outcome =
            DagMerge.mergeWith MergeContext.defaults sink streamId initial a b (timestamp (tick ()))
            |> Async.RunSynchronously

        // The merge through the sink is the merge over a fresh index of its records.
        let fresh =
            DagMerge.mergeIndexed
                MergeContext.defaults
                streamId
                initial
                (Fuaran.Core.Dag.Reach.ofDag (DagOpRecord.toDag (sink.Records streamId |> Async.RunSynchronously)))
                a
                b
                (timestamp clock)

        Expect.equal (sprintf "%A" outcome) (sprintf "%A" fresh.Outcome) $"{label ()}: merge {a} {b}"

        match outcome.Result with
        | MergeResult.Merged(record, _) ->
            write record
            all.Add record.Hash
            heads.Add record.Hash
            Some record.Hash
        | MergeResult.FastForward h ->
            heads.Remove a |> ignore
            heads.Remove b |> ignore
            heads.Add h
            None
        | _ -> None

    for _ in 1..steps do
        let roll = rng.Next 100

        if roll < 45 || heads.Count < 2 then
            let i = rng.Next heads.Count

            match nextOp (treeAt heads[i]) with
            | Some(op, _) ->
                let r =
                    DagOpRecord.create
                        streamId
                        [ heads[i] ]
                        op
                        None
                        actor
                        (timestamp (tick ()))
                        OpResultEnvelope.Success

                write r
                all.Add r.Hash
                if roll < 30 then heads[i] <- r.Hash else heads.Add r.Hash
            | None -> ()
        elif roll < 52 then
            // A guest branch: its own stream, its genesis hung off a node of this
            // one (a parent outside the guest stream's live set: the index is
            // built over it rather than extended), then appended to.
            guests <- guests + 1
            let guest = GuestFork.streamId $"g{guests}"
            let anchor = all[rng.Next all.Count]

            match nextOp (treeAt anchor) with
            | Some(op, tree) ->
                let r =
                    DagOpRecord.create guest [ anchor ] op None actor (timestamp (tick ())) OpResultEnvelope.Success

                write r
                guestHeads.Add((guest, r.Hash, tree))
            | None -> ()
        elif roll < 58 && guestHeads.Count > 0 then
            let gi = rng.Next guestHeads.Count
            let guest, head, tree = guestHeads[gi]

            match nextOp tree with
            | Some(op, next) ->
                let r =
                    DagOpRecord.create guest [ head ] op None actor (timestamp (tick ())) OpResultEnvelope.Success

                write r
                guestHeads[gi] <- (guest, r.Hash, next)
            | None -> ()
        elif roll < 61 && guestHeads.Count > 0 then
            // A prune: the guest branch's tip leaves the live set. The index is
            // dropped and the next question rebuilds it.
            let gi = rng.Next guestHeads.Count
            let guest, head, _ = guestHeads[gi]
            let pruned = sink.Tombstone(guest, head) |> Async.RunSynchronously
            Expect.isTrue pruned "the guest tip is pruned"
            guestHeads.RemoveAt gi
            prunes <- prunes + 1
            equalsFresh (label () + " (pruned)") sink guest
        else
            let i = rng.Next heads.Count
            let j = (i + 1 + rng.Next(heads.Count - 1)) % heads.Count
            let a, b = heads[i], heads[j]

            match merge a b with
            | Some _ ->
                merges <- merges + 1

                // Sometimes the swapped twin too: two merge nodes over one pair.
                if rng.Next 3 = 0 then
                    match merge b a with
                    | Some _ -> twins <- twins + 1
                    | None -> ()
            | None -> ()

    all.Count, merges, twins, guests, prunes

let private historyOver (name: string) (make: unit -> IDagOpStreamSink<TestMsg> * IDisposable) (seeds: int list) =
    test $"{name}: the held index equals a fresh one after every write of a generated history" {
        let counts =
            [ for seed in seeds do
                  let sink, scope = make ()
                  use _ = scope
                  history sink seed 45 ]

        let total f = counts |> List.sumBy f
        // Not vacuous: histories grew, merged (twins among them), forked guests and pruned.
        Expect.isGreaterThan (total (fun (n, _, _, _, _) -> n)) (25 * List.length seeds) "the histories hold nodes"
        Expect.isGreaterThan (total (fun (_, m, _, _, _) -> m)) 3 "and merge nodes"
        Expect.isGreaterThan (total (fun (_, _, t, _, _) -> t)) 0 "and criss-cross twins"
        Expect.isGreaterThan (total (fun (_, _, _, g, _) -> g)) 2 "and guest branches"
        Expect.isGreaterThan (total (fun (_, _, _, _, p) -> p)) 0 "and prunes"
    }

let private noScope =
    { new IDisposable with
        member _.Dispose() = () }

[<Tests>]
let tests =
    testList
        "Dag — the index a sink holds across calls (Phase 2716)"
        [ historyOver "InMemoryDagSink" (fun () -> InMemoryDagSink.create<TestMsg> (), noScope) [ 1..6 ]
          historyOver
              "SqliteDagSink"
              (fun () ->
                  let db = TempDb.create "held-index"
                  SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" db.Path) testCodec, (db :> IDisposable))
              [ 1..2 ]

          test "a write the admission refuses leaves the held index as it was" {
              use db = TempDb.create "held-index"

              for name, sink in
                  [ "InMemoryDagSink", InMemoryDagSink.create<TestMsg> ()
                    "SqliteDagSink", SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" db.Path) testCodec ] do
                  let g = stepRecord streamId None (TreeOp.Batch []) 1L
                  add sink g
                  heldReach sink streamId |> ignore
                  let child = stepRecord streamId (Some g) (TreeOp.RemoveNode(NodeId "p0")) 2L

                  let forged =
                      { child with
                          Node =
                              { child.Node with
                                  Op =
                                      { child.Entry with
                                          Timestamp = child.Timestamp.AddSeconds 1.0 } } }

                  Expect.throwsT<InvalidOperationException> (fun () -> add sink forged) $"{name}: refused at write"
                  equalsFresh name sink streamId

                  Expect.equal
                      (Fuaran.Core.Dag.Reach.dag (heldReach sink streamId)).Nodes.Count
                      1
                      $"{name}: the index holds the genesis only"
          } ]
