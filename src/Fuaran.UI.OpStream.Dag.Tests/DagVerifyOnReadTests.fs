module Fuaran.UI.OpStream.Dag.Tests.DagVerifyOnReadTests

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Phase 793 — `DagVerify` is wired at the DAG sinks' READ paths.
//
//  Before this phase `DagVerify.records` had ZERO production callers. Dead
//  verification code is worse than none: it reads as coverage that does not
//  exist. It is now called by `Records` and (its content-address leg) by
//  `TryGet` on both sinks, and every test below is a GO-RED proof that the
//  call is live — the Sqlite ones corrupt the DATABASE FILE with raw SQL.
//
//  One design fact these tests pin, because getting it wrong silently breaks
//  a shipped feature: parent linkage is resolved against the WHOLE STORE, not
//  the stream being read. The guest-fork contract anchors a guest branch's
//  genesis on the `Mount` op in the HOST stream, so a stream-scoped record set
//  is not a closed parent universe — verifying against it reported six healthy
//  guest branches as corrupt.
// ============================================================================

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private removeRight = TreeOp.RemoveNode rightChildId

/// A record whose stored content address no longer matches its content: the
/// timestamp moves, the hash does not. Exactly the shape a bit-flip or a
/// hand-run `UPDATE` leaves behind.
let private corruptContent (r: DagOpRecord<TestMsg>) : DagOpRecord<TestMsg> =
    { r with
        Timestamp = r.Timestamp.AddSeconds 1.0 }

let private refusalMessage (what: string) (read: unit -> unit) : string =
    try
        read ()
        failtestf "GO-RED FAILED: %s returned a corrupt record instead of refusing" what
    with :? InvalidOperationException as ex ->
        ex.Message

let private freshDbPath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fuaran-dagverify-%s.db" (Guid.NewGuid().ToString("N")))

let private cleanup (path: string) =
    try
        if File.Exists path then
            File.Delete path
    with _ ->
        ()

let private corruptRowInDb (path: string) (hash: string) =
    use conn = new SqliteConnection(sprintf "Data Source=%s" path)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "UPDATE dag_op_record SET timestamp = timestamp + 1 WHERE hash = @h;"
    cmd.Parameters.AddWithValue("@h", hash) |> ignore
    let affected = cmd.ExecuteNonQuery()

    if affected <> 1 then
        failtestf "corruptRowInDb: expected to corrupt exactly 1 row, touched %d" affected

[<Tests>]
let tests =
    testList
        "Dag — verify on read (Phase 793)"
        [

          // ── InMemoryDagSink ─────────────────────────────────────────────

          test "InMemoryDagSink.Records REFUSES a record whose address does not recompute" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              add sink g
              // Same hash, different content — `Add` only guards collisions, so
              // this lands, which is exactly why the READ has to check.
              add sink (corruptContent (stepRecord "s" (Some g) removeRight 2L))

              let message =
                  refusalMessage "InMemoryDagSink.Records" (fun () ->
                      sink.Records "s" |> Async.RunSynchronously |> ignore)

              Expect.stringContains message "'s'" "names the stream"
              Expect.stringContains message "does not recompute" "names the violation"
          }

          test "InMemoryDagSink.TryGet REFUSES a corrupt record" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let good = stepRecord "s" None brand 1L
              let bad = corruptContent good
              add sink bad

              refusalMessage "InMemoryDagSink.TryGet" (fun () ->
                  sink.TryGet("s", bad.Hash) |> Async.RunSynchronously |> ignore)
              |> ignore
          }

          test "InMemoryDagSink.Records accepts a clean DAG" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              let child = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink child

              let records = sink.Records "s" |> Async.RunSynchronously
              Expect.equal records.Length 2 "both records returned"
          }

          test "a CROSS-STREAM parent is not a dangling parent" {
              // The guest-fork shape: a branch in its own stream whose genesis
              // hangs off a record in another. Resolving parents against the
              // returned slice alone would call this corrupt.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let hostRecord = stepRecord "host" None brand 1L
              add sink hostRecord

              let guestGenesis =
                  DagOpRecord.create
                      "guest-g1"
                      [ hostRecord.Hash ]
                      removeRight
                      None
                      "tester"
                      (ts 2L)
                      OpResultEnvelope.Success

              add sink guestGenesis

              let records = sink.Records "guest-g1" |> Async.RunSynchronously
              Expect.equal records.Length 1 "the guest branch reads back without being called corrupt"
          }

          test "a GENUINELY dangling parent is still refused" {
              // The counterpart to the test above: store-wide resolution must not
              // become "resolve against nothing". A parent that exists in NO
              // stream is a truncated store and is reported.
              let sink = InMemoryDagSink.create<TestMsg> ()

              let orphan =
                  DagOpRecord.create
                      "s"
                      [ String.replicate 64 "c" ]
                      removeRight
                      None
                      "tester"
                      (ts 1L)
                      OpResultEnvelope.Success

              add sink orphan

              let message =
                  refusalMessage "InMemoryDagSink.Records" (fun () ->
                      sink.Records "s" |> Async.RunSynchronously |> ignore)

              Expect.stringContains message "not in the store" "names the dangling parent"
          }

          test "LoadVerification.Off hands the corrupt record back — the opt-out is real, and named" {
              let sink = InMemoryDagSink.createWith<TestMsg> LoadVerification.Off
              add sink (corruptContent (stepRecord "s" None brand 1L))

              let records = sink.Records "s" |> Async.RunSynchronously
              Expect.equal records.Length 1 "Off returns the record unchecked"
          }

          // ── SqliteDagSink — corruption of the DATABASE FILE ─────────────

          test "SqliteDagSink.Records REFUSES a row corrupted in the database file" {
              let path = freshDbPath ()

              try
                  let sink =
                      SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) dagTestCodec

                  let g = stepRecord "s" None brand 1L
                  let child = stepRecord "s" (Some g) removeRight 2L
                  add sink g
                  add sink child

                  // Clean before, so the refusal below is attributable to the
                  // corruption and not to a defect in the fixture.
                  Expect.equal
                      (sink.Records "s" |> Async.RunSynchronously).Length
                      2
                      "the store verifies clean before corruption"

                  corruptRowInDb path child.Hash

                  let reopened =
                      SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) dagTestCodec

                  let message =
                      refusalMessage "SqliteDagSink.Records" (fun () ->
                          reopened.Records "s" |> Async.RunSynchronously |> ignore)

                  Expect.stringContains message "'s'" "names the stream"
                  Expect.stringContains message child.Hash "names the corrupt record by its stored address"
              finally
                  cleanup path
          }

          test "SqliteDagSink.TryGet REFUSES a row corrupted in the database file" {
              let path = freshDbPath ()

              try
                  let sink =
                      SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) dagTestCodec

                  let g = stepRecord "s" None brand 1L
                  add sink g

                  Expect.isSome
                      (sink.TryGet("s", g.Hash) |> Async.RunSynchronously)
                      "reads back clean before corruption"

                  corruptRowInDb path g.Hash

                  let reopened =
                      SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) dagTestCodec

                  refusalMessage "SqliteDagSink.TryGet" (fun () ->
                      reopened.TryGet("s", g.Hash) |> Async.RunSynchronously |> ignore)
                  |> ignore
              finally
                  cleanup path
          }

          test "SqliteDagSink under LoadVerification.Off returns the corrupted row" {
              let path = freshDbPath ()

              try
                  let sink =
                      SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) dagTestCodec

                  let g = stepRecord "s" None brand 1L
                  add sink g
                  corruptRowInDb path g.Hash

                  let unchecked' =
                      SqliteDagSink.createWith<TestMsg>
                          LoadVerification.Off
                          (sprintf "Data Source=%s" path)
                          dagTestCodec

                  Expect.equal
                      (unchecked'.Records "s" |> Async.RunSynchronously).Length
                      1
                      "Off returns the row unchecked"
              finally
                  cleanup path
          } ]
