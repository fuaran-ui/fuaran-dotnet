module Fuaran.UI.OpStream.Dag.Tests.DagPromoteTests

// ============================================================================
//  Phase 2117 — promotion of a linear stream to a DAG in one atomic step.
//
//  `IDagOpStreamBatchSink.AddAll` writes a branch as every record or none, and
//  `DagLift.promote` composes verify, lift, that atomic write and the head
//  compare-and-swap from "no head". Three go-red proofs, on both backends:
//
//   1. a write failure after the first record leaves no record and no head
//      (a loop of `Add`s leaves the first record behind);
//   2. a concurrent head advance makes the promotion fail cleanly — a typed
//      refusal, the other writer's head kept, and no partial branch;
//   3. the linear store is byte-identical before and after.
//
//  Then the stated rules: a second promotion of the same linear head is
//  idempotent while the head is still the branch head and refused once the
//  stream has moved on; a sink without the capability is refused, never
//  emulated with a loop.
// ============================================================================

open System
open System.IO
open System.Security.Cryptography
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.TestSupport

let private run (a: Async<'T>) : 'T = Async.RunSynchronously a

let private restyle (id: NodeId) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

let private brand = restyle leftChildId ToneVariant.Brand
let private success = restyle rightChildId ToneVariant.Success

/// The op the failing codec below cannot encode: the third record's.
let private poison = restyle rightChildId ToneVariant.Critical

/// A sound three-record linear chain on stream `s`.
let private chain () : OpRecord<TestMsg> list =
    let r1 = buildRecord "s" 1 brand None (timestamp 1L)
    let r2 = buildRecord "s" 2 success (Some r1) (timestamp 2L)
    let r3 = buildRecord "s" 3 poison (Some r2) (timestamp 3L)
    [ r1; r2; r3 ]

let private lifted () =
    match DagLift.ofLinearVerified (chain ()) with
    | Ok l -> l
    | Error refusal -> failtestf "the fixture chain must lift: %s" (DagLift.describe refusal)

/// The reference codec, except that encoding `poison` throws: a write that fails
/// AFTER the first record of a batch has been stored inside the transaction.
let private failingCodec: IOpJsonCodec<TestMsg> =
    { new IOpJsonCodec<TestMsg> with
        member _.EncodeOp op =
            if CanonicalJson.encodeOp op = CanonicalJson.encodeOp poison then
                failwith "codec: refusing to encode the poison op"
            else
                testCodec.EncodeOp op

        member _.DecodeOp json = testCodec.DecodeOp json }

/// One DAG store for one test: the sink as the plain interface, and its cleanup.
type private Store =
    { Sink: IDagOpStreamSink<TestMsg>
      Cleanup: IDisposable }

let private inMemory () =
    { Sink = InMemoryDagSink.create<TestMsg> ()
      Cleanup =
        { new IDisposable with
            member _.Dispose() = () } }

let private sqliteWith (codec: IOpJsonCodec<TestMsg>) () =
    let db = TempDb.create "dag-promote"

    { Sink = SqliteDagSink.create (sprintf "Data Source=%s" db.Path) codec
      Cleanup = db }

let private backends: (string * (unit -> Store)) list =
    [ "in-memory", inMemory; "SQLite", sqliteWith testCodec ]

let private batchOf (sink: IDagOpStreamSink<TestMsg>) : IDagOpStreamBatchSink<TestMsg> =
    DagSinkCapabilities.tryBatch sink
    |> Option.defaultWith (fun () -> failtest "both shipped DAG sinks carry the atomic batch add")

let private recordCount (sink: IDagOpStreamSink<TestMsg>) = sink.Records "s" |> run |> List.length

/// The same sink seen as a plain `IDagOpStreamSink` — the capability hidden.
let private withoutBatch (inner: IDagOpStreamSink<TestMsg>) : IDagOpStreamSink<TestMsg> =
    { new IDagOpStreamSink<TestMsg> with
        member _.Add r = inner.Add r
        member _.TryGet(s, h) = inner.TryGet(s, h)
        member _.Head s = inner.Head s
        member _.TryAdvanceHead(s, e, n) = inner.TryAdvanceHead(s, e, n)
        member _.Parents(s, h) = inner.Parents(s, h)
        member _.Reachable(s, h) = inner.Reachable(s, h)
        member _.Heads s = inner.Heads s
        member _.Records s = inner.Records s
        member _.Tombstone(s, h) = inner.Tombstone(s, h)
        member _.Skeleton s = inner.Skeleton s
        member _.Streams() = inner.Streams() }

/// The batch sink, except that once its `AddAll` has written, ANOTHER writer
/// stores `rival` and moves the head to it — the race between a promotion's
/// head check and its compare-and-swap, made deterministic.
let private racedBy (rival: DagOpRecord<TestMsg>) (inner: IDagOpStreamBatchSink<TestMsg>) =
    { new IDagOpStreamBatchSink<TestMsg> with
        member _.AddAll records =
            async {
                do! inner.AddAll records
                do! inner.Add rival
                let! moved = inner.TryAdvanceHead(rival.StreamId, None, rival.Hash)

                if not moved then
                    failwith "the rival writer could not take the empty head"
            }

        member _.Add r = inner.Add r
        member _.TryGet(s, h) = inner.TryGet(s, h)
        member _.Head s = inner.Head s
        member _.TryAdvanceHead(s, e, n) = inner.TryAdvanceHead(s, e, n)
        member _.Parents(s, h) = inner.Parents(s, h)
        member _.Reachable(s, h) = inner.Reachable(s, h)
        member _.Heads s = inner.Heads s
        member _.Records s = inner.Records s
        member _.Tombstone(s, h) = inner.Tombstone(s, h)
        member _.Skeleton s = inner.Skeleton s
        member _.Streams() = inner.Streams() }

/// A genesis record in stream `s` that is not part of the promoted branch.
let private rival () =
    stepRecord "s" None (TreeOp.RemoveNode leftChildId) 99L

let private expectPromoted (result: Result<Promotion, PromotionRefusal>) : LiftReceipt =
    match result with
    | Ok(Promotion.Promoted receipt) -> receipt
    | other -> failtestf "expected Promoted, got %A" other

let private perBackend (name: string) (body: IDagOpStreamSink<TestMsg> -> unit) : Test =
    testList
        name
        [ for backend, make in backends ->
              test backend {
                  let store = make ()

                  try
                      body store.Sink
                  finally
                      store.Cleanup.Dispose()
              } ]

/// SHA-256 over the database file and any sidecar beside it, with every pooled
/// connection released first so nothing is mid-write.
let private fileDigest (path: string) : string =
    SqliteConnection.ClearAllPools()

    [ path; path + "-wal"; path + "-shm" ]
    |> List.filter File.Exists
    |> List.map (fun p -> p + "=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes p)))
    |> String.concat ";"

[<Tests>]
let tests =
    testList
        "Dag.Promote (Phase 2117)"
        [ testList
              "the capability probe"
              [ perBackend "a shipped DAG sink answers Some" (fun sink ->
                    Expect.isSome (DagSinkCapabilities.tryBatch sink) "the shipped sink claims AddAll")

                test "a sink without the capability answers None" {
                    Expect.isNone
                        (DagSinkCapabilities.tryBatch (withoutBatch (InMemoryDagSink.create<TestMsg> ())))
                        "a plain IDagOpStreamSink claims nothing"
                } ]

          testList
              "AddAll"
              [ perBackend "stores every record of a branch whose parents are earlier in the batch" (fun sink ->
                    let branch = lifted ()
                    (batchOf sink).AddAll branch.Records |> run
                    Expect.equal (recordCount sink) 3 "every record stored"
                    Expect.isNone (sink.Head "s" |> run) "AddAll does not touch the head"

                    for r in branch.Records do
                        Expect.equal
                            (sink.TryGet("s", r.Hash) |> run |> Option.map _.Hash)
                            (Some r.Hash)
                            "each record reads back at its address")

                perBackend "re-adding a held branch is a no-op" (fun sink ->
                    let branch = lifted ()
                    (batchOf sink).AddAll branch.Records |> run
                    (batchOf sink).AddAll branch.Records |> run
                    Expect.equal (recordCount sink) 3 "nothing stored twice")

                perBackend "an empty batch is a no-op" (fun sink ->
                    (batchOf sink).AddAll [] |> run
                    Expect.isEmpty (sink.Streams() |> run) "no stream is created")

                // GO-RED 1, at the capability: the second record is refused, so the
                // first must not be stored either. A loop of `Add`s keeps it.
                perBackend "a refused record after the first leaves no record, no head and no stream" (fun sink ->
                    let branch = lifted ()

                    let forged =
                        match branch.Records with
                        | [ a; b; c ] ->
                            [ a
                              { b with
                                  Node =
                                      { b.Node with
                                          Op = { b.Entry with Op = brand } } }
                              c ]
                        | _ -> failtest "three records"

                    Expect.throws (fun () -> (batchOf sink).AddAll forged |> run) "the forged record refuses the batch"
                    Expect.equal (recordCount sink) 0 "no record of the batch was stored"
                    Expect.isNone (sink.Head "s" |> run) "no head"
                    Expect.isEmpty (sink.Streams() |> run) "no stream is listed") ]

          testList
              "DagLift.promote"
              [ perBackend
                    "writes the branch, advances the head from none, returns a receipt that recomputes"
                    (fun sink ->
                        let receipt = DagLift.promote sink (chain ()) |> run |> expectPromoted
                        Expect.equal (sink.Head "s" |> run) (Some receipt.DagHead) "the head is the branch head"
                        Expect.equal (recordCount sink) 3 "the whole branch is stored"
                        Expect.equal receipt (lifted ()).Receipt "the receipt is the lift's"

                        match LiftReceipt.verify receipt (chain ()) with
                        | LiftVerdict.Recomputes _ -> ()
                        | v -> failtestf "the receipt must recompute from the linear records: %A" v)

                // GO-RED 1, through the promotion: the SQLite write of the second
                // record fails inside the transaction after the first was stored.
                test "a write failure after the first record leaves no record and no head (SQLite)" {
                    let store = sqliteWith failingCodec ()

                    try
                        match DagLift.promote store.Sink (chain ()) |> run with
                        | Error(PromotionRefusal.WriteRefused message) ->
                            Expect.stringContains message "poison" "the refusal carries the write's failure"
                        | other -> failtestf "expected WriteRefused, got %A" other

                        Expect.equal (recordCount store.Sink) 0 "no record of the branch was stored"
                        Expect.isNone (store.Sink.Head "s" |> run) "no head"
                        Expect.isEmpty (store.Sink.Streams() |> run) "no stream is listed"
                    finally
                        store.Cleanup.Dispose()
                }

                // GO-RED 2: the head moves between the check and the swap.
                perBackend "a concurrent head advance fails the promotion cleanly" (fun sink ->
                    let other = rival ()

                    match DagLift.promoteWith (racedBy other (batchOf sink)) (chain ()) |> run with
                    | Error(PromotionRefusal.HeadAdvancedConcurrently head) ->
                        Expect.equal head other.Hash "the refusal names the winner's head"
                    | result -> failtestf "expected HeadAdvancedConcurrently, got %A" result

                    Expect.equal (sink.Head "s" |> run) (Some other.Hash) "the other writer's head is kept"

                    let held = sink.Records "s" |> run |> List.map _.Hash |> Set.ofList

                    Expect.isTrue
                        ((lifted ()).Records |> List.forall (fun r -> held.Contains r.Hash))
                        "the branch is stored whole, never a prefix"

                    Expect.equal held.Count 4 "the branch and the rival, nothing else")

                perBackend "a stream that already has another head is refused before anything is written" (fun sink ->
                    let other = rival ()
                    sink.Add other |> run
                    sink.TryAdvanceHead("s", None, other.Hash) |> run |> ignore

                    match DagLift.promote sink (chain ()) |> run with
                    | Error(PromotionRefusal.HeadTaken head) -> Expect.equal head other.Hash "names the head"
                    | result -> failtestf "expected HeadTaken, got %A" result

                    Expect.equal (recordCount sink) 1 "nothing of the branch was written")

                perBackend "a second promotion of the same linear head is idempotent" (fun sink ->
                    let first = DagLift.promote sink (chain ()) |> run |> expectPromoted

                    match DagLift.promote sink (chain ()) |> run with
                    | Ok(Promotion.AlreadyPromoted receipt) -> Expect.equal receipt first "the same receipt"
                    | result -> failtestf "expected AlreadyPromoted, got %A" result

                    Expect.equal (recordCount sink) 3 "nothing written twice"
                    Expect.equal (sink.Head "s" |> run) (Some first.DagHead) "the head is unchanged")

                perBackend "a promotion after the stream moved on is refused" (fun sink ->
                    let first = DagLift.promote sink (chain ()) |> run |> expectPromoted
                    let tip = sink.TryGet("s", first.DagHead) |> run
                    let next = stepRecord "s" tip (TreeOp.RemoveNode rightChildId) 4L
                    sink.Add next |> run
                    sink.TryAdvanceHead("s", Some first.DagHead, next.Hash) |> run |> ignore

                    match DagLift.promote sink (chain ()) |> run with
                    | Error(PromotionRefusal.HeadTaken head) -> Expect.equal head next.Hash "names the moved head"
                    | result -> failtestf "expected HeadTaken, got %A" result)

                perBackend "a sink without the atomic batch add is refused, not looped over" (fun sink ->
                    match DagLift.promote (withoutBatch sink) (chain ()) |> run with
                    | Error PromotionRefusal.NoAtomicBatch -> ()
                    | result -> failtestf "expected NoAtomicBatch, got %A" result

                    Expect.equal (recordCount sink) 0 "nothing was written"
                    Expect.isNone (sink.Head "s" |> run) "no head")

                perBackend "a linear segment that does not re-walk is refused before anything is written" (fun sink ->
                    let broken =
                        match chain () with
                        | [ a; b; c ] -> [ a; { b with PreviousHash = c.Hash }; c ]
                        | _ -> failtest "three records"

                    match DagLift.promote sink broken |> run with
                    | Error(PromotionRefusal.NotLifted(LiftRefusal.BrokenLinearSegment _)) -> ()
                    | result -> failtestf "expected NotLifted, got %A" result

                    Expect.equal (recordCount sink) 0 "nothing was written") ]

          // GO-RED 3: a promotion reads the linear store and never writes it.
          testList
              "the linear store is unchanged by a promotion"
              [ test "SQLite linear store: byte-identical file" {
                    use linearDb = TempDb.create "dag-promote-linear"
                    let connection = sprintf "Data Source=%s" linearDb.Path

                    let linear = SqliteSink.create connection testCodec

                    for r in chain () do
                        linear.Append r |> run

                    let before = fileDigest linearDb.Path

                    let records =
                        (SqliteSink.create connection testCodec).Replay("s", 1, Int32.MaxValue) |> run

                    let dag = sqliteWith testCodec ()

                    try
                        DagLift.promote dag.Sink records |> run |> expectPromoted |> ignore
                    finally
                        dag.Cleanup.Dispose()

                    Expect.equal (fileDigest linearDb.Path) before "the linear database's bytes are unchanged"
                }

                test "in-memory linear store: every record and the stream unchanged" {
                    let linear = InMemorySink.create<TestMsg> ()

                    for r in chain () do
                        linear.Append r |> run

                    let records () =
                        linear.Replay("s", 1, Int32.MaxValue) |> run

                    // Every field, the op canonically encoded (`TreeOp` carries no equality).
                    let read () =
                        records ()
                        |> List.map (fun r ->
                            r.Sequence, r.PreviousHash, r.Hash, CanonicalJson.encodeOp r.Op, r.Actor, r.Timestamp),
                        linear.Streams() |> run

                    let before = read ()

                    DagLift.promote (InMemoryDagSink.create ()) (records ())
                    |> run
                    |> expectPromoted
                    |> ignore

                    Expect.equal (read ()) before "the linear store reads back exactly as it was"
                } ] ]
