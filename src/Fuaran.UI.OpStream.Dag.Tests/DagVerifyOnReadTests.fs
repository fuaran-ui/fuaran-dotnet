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
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Phase 793 wired `DagVerify` at the DAG sinks' READ paths; Phase 2716 moved
//  it to LOAD (docs/DECISIONS.md D32): a sink verifies a stream when it reads it
//  from its backing store and every record on write, and a read trusts what the
//  sink holds. `Reverify` is the old guarantee on demand. Every test below is a
//  GO-RED proof that a check is live — the Sqlite ones corrupt the DATABASE FILE
//  with raw SQL — and the ones marked D32 pin what a read no longer detects, so
//  a contract that quietly moves back (or further) fails here.
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
        Node =
            { r.Node with
                Op =
                    { r.Entry with
                        Timestamp = r.Timestamp.AddSeconds 1.0 } } }

let private refusalMessage (what: string) (read: unit -> unit) : string =
    try
        read ()
        failtestf "GO-RED FAILED: %s returned a corrupt record instead of refusing" what
    with :? InvalidOperationException as ex ->
        ex.Message

let private corruptRowInDb (path: string) (hash: string) =
    use conn = new SqliteConnection(sprintf "Data Source=%s" path)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "UPDATE dag_node SET timestamp = timestamp + 1 WHERE hash = @h;"
    cmd.Parameters.AddWithValue("@h", hash) |> ignore
    let affected = cmd.ExecuteNonQuery()

    if affected <> 1 then
        failtestf "corruptRowInDb: expected to corrupt exactly 1 row, touched %d" affected

[<Tests>]
let tests =
    testList
        "Dag — verify at load, on write and on demand (Phase 793, D32)"
        [

          // ── InMemoryDagSink ─────────────────────────────────────────────

          test "InMemoryDagSink: a record whose address does not recompute never reaches a read" {
              // Phase 1937 — `Add` recomputes the address (`DagWriteAdmission`), so
              // the corrupt record this read path used to be handed is refused at
              // the write, by name, and every later read of the stream is clean.
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              add sink g
              let bad = corruptContent (stepRecord "s" (Some g) removeRight 2L)

              let message = refusalMessage "InMemoryDagSink.Add" (fun () -> add sink bad)

              Expect.stringContains message "'s'" "names the stream"
              Expect.stringContains message "does not recompute" "names the violation"
              Expect.equal (sink.Records "s" |> Async.RunSynchronously).Length 1 "the stream reads back clean"
              Expect.isNone (sink.TryGet("s", bad.Hash) |> Async.RunSynchronously) "nothing is held at the address"
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
                      (Actor.Human "tester")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              add sink guestGenesis

              let records = sink.Records "guest-g1" |> Async.RunSynchronously
              Expect.equal records.Length 1 "the guest branch reads back without being called corrupt"
          }

          // ── The read-path dangling-parent leg, AFTER Phase 1525 ──────────
          //
          // This test used to build the dangling edge by `Add`ing a record whose
          // parent was in no stream, then asserting the READ refused it. Phase
          // 1525 requires parent presence at the write choke point, so that
          // record can no longer get in — the write path refuses it first, which
          // `WriteIntegrityTests` proves on both sinks.
          //
          // The read-path check is NOT redundant and is not dropped here. It
          // catches the case the write path structurally cannot see: a store that
          // LOST a record after the link to it was made. That is reproduced the
          // only way it actually happens — out of band, against the file — so the
          // proof is now Sqlite-side. The in-memory sink has no out-of-band
          // mutation path, so after 1525 it has no way to reach this state
          // through its own API at all; that is a real gap in coverage on that
          // sink and is stated rather than faked by relaxing the write check.
          test "a store that LOST a parent row is refused on read (truncation, out of band)" {
              use db = TempDb.create "dagverify"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              let g = stepRecord "s" None brand 1L
              let child = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink child

              Expect.equal
                  (sink.Records "s" |> Async.RunSynchronously).Length
                  2
                  "the store verifies clean before truncation"

              // Delete the parent ROW, leaving the child's link to it — a
              // truncated store, exactly what the read-path leg exists for.
              do
                  use conn = new SqliteConnection(sprintf "Data Source=%s" path)
                  conn.Open()
                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "DELETE FROM dag_node WHERE hash = @h;"
                  cmd.Parameters.AddWithValue("@h", g.Hash) |> ignore

                  if cmd.ExecuteNonQuery() <> 1 then
                      failtest "expected to delete exactly the parent row"

              let reopened =
                  SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              let message =
                  refusalMessage "SqliteDagSink.Records" (fun () ->
                      reopened.Records "s" |> Async.RunSynchronously |> ignore)

              Expect.stringContains message "not in the store" "names the dangling parent"
              Expect.stringContains message g.Hash "names the parent that went missing"
          }

          test "LoadVerification.Off hands the corrupt record back — the opt-out is real, and named" {
              // Over the database file: since Phase 1937 a corrupt record cannot be
              // written through `Add`, so the corruption is made where it happens in
              // the world — a row edited underneath the sink.
              use db = TempDb.create "dagverify"
              let path = db.Path
              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec
              let g = stepRecord "s" None brand 1L
              add sink g
              corruptRowInDb path g.Hash

              let unchecked' =
                  SqliteDagSink.createWith<TestMsg> LoadVerification.Off (sprintf "Data Source=%s" path) testCodec

              let records = unchecked'.Records "s" |> Async.RunSynchronously
              Expect.equal records.Length 1 "Off returns the record unchecked"
          }

          // ── SqliteDagSink — corruption of the DATABASE FILE ─────────────

          test "SqliteDagSink.Records REFUSES, at load, a row corrupted in the database file" {
              use db = TempDb.create "dagverify"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

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
                  SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              let message =
                  refusalMessage "SqliteDagSink.Records" (fun () ->
                      reopened.Records "s" |> Async.RunSynchronously |> ignore)

              Expect.stringContains message "'s'" "names the stream"
              Expect.stringContains message child.Hash "names the corrupt record by its stored address"
          }

          test "SqliteDagSink.TryGet REFUSES, at load, a row corrupted in the database file" {
              use db = TempDb.create "dagverify"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              let g = stepRecord "s" None brand 1L
              add sink g

              Expect.isSome (sink.TryGet("s", g.Hash) |> Async.RunSynchronously) "reads back clean before corruption"

              corruptRowInDb path g.Hash

              let reopened =
                  SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              refusalMessage "SqliteDagSink.TryGet" (fun () ->
                  reopened.TryGet("s", g.Hash) |> Async.RunSynchronously |> ignore)
              |> ignore
          }

          test "SqliteDagSink under LoadVerification.Off returns the corrupted row" {
              use db = TempDb.create "dagverify"
              let path = db.Path

              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

              let g = stepRecord "s" None brand 1L
              add sink g
              corruptRowInDb path g.Hash

              let unchecked' =
                  SqliteDagSink.createWith<TestMsg> LoadVerification.Off (sprintf "Data Source=%s" path) testCodec

              Expect.equal (unchecked'.Records "s" |> Async.RunSynchronously).Length 1 "Off returns the row unchecked"
          }

          // ── D32 (Phase 2716): what a read no longer re-checks, and Reverify ─

          test
              "D32: a row corrupted AFTER load is not seen by a read; Reverify refuses it by name, and so does every read after" {
              use db = TempDb.create "dagverify"
              let path = db.Path
              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec
              let g = stepRecord "s" None brand 1L
              let child = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink child
              Expect.equal (sink.Records "s" |> Async.RunSynchronously).Length 2 "loaded clean"

              corruptRowInDb path child.Hash

              // The contract, pinned: the read hands back what the sink holds.
              let held = sink.Records "s" |> Async.RunSynchronously
              Expect.equal held.Length 2 "a read trusts the records the sink holds"

              Expect.equal
                  (held |> List.find (fun r -> r.Hash = child.Hash)).Timestamp
                  child.Timestamp
                  "and hands back the record as loaded, not the row as edited"

              let indexed = Option.get (DagSinkCapabilities.tryIndexed sink)

              let message =
                  refusalMessage "SqliteDagSink.Reverify" (fun () -> indexed.Reverify "s" |> Async.RunSynchronously)

              Expect.stringContains message "'s'" "names the stream"
              Expect.stringContains message child.Hash "names the corrupt record by its stored address"

              // A refused re-verify holds nothing: the next read loads, and refuses.
              refusalMessage "SqliteDagSink.Records after a refused Reverify" (fun () ->
                  sink.Records "s" |> Async.RunSynchronously |> ignore)
              |> ignore
          }

          test "D32: Reverify verifies under LoadVerification.Off too, because asking is the opt-in" {
              use db = TempDb.create "dagverify"
              let path = db.Path
              let sink = SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec
              let g = stepRecord "s" None brand 1L
              add sink g
              corruptRowInDb path g.Hash

              let unchecked' =
                  SqliteDagSink.createWith<TestMsg> LoadVerification.Off (sprintf "Data Source=%s" path) testCodec

              Expect.equal (unchecked'.Records "s" |> Async.RunSynchronously).Length 1 "Off loads the row unchecked"

              refusalMessage "SqliteDagSink.Reverify under Off" (fun () ->
                  (Option.get (DagSinkCapabilities.tryIndexed unchecked')).Reverify "s"
                  |> Async.RunSynchronously)
              |> ignore
          }

          test "D32: a record ANOTHER WRITER adds is read and verified at the next read; a forged row is refused there" {
              use db = TempDb.create "dagverify"
              let cs = sprintf "Data Source=%s" db.Path
              let reader = SqliteDagSink.create<TestMsg> cs testCodec
              let writer = SqliteDagSink.create<TestMsg> cs testCodec
              let g = stepRecord "s" None brand 1L
              add writer g
              Expect.equal (reader.Records "s" |> Async.RunSynchronously).Length 1 "the reader loads the stream"

              let child = stepRecord "s" (Some g) removeRight 2L
              add writer child
              let records = reader.Records "s" |> Async.RunSynchronously
              Expect.equal records.Length 2 "the other writer's record is held at the next read"

              let reach =
                  (Option.get (DagSinkCapabilities.tryIndexed reader)).Reach "s"
                  |> Async.RunSynchronously

              Expect.isTrue
                  (Fuaran.Core.Dag.Reach.reaches reach g.Hash child.Hash)
                  "and is in the index the reader holds"

              // A row another program inserts under an address its content does not
              // recompute to: refused when the reader picks it up, as at load.
              let forged = String('f', 64)

              do
                  use conn = new SqliteConnection(cs)
                  conn.Open()
                  use cmd = conn.CreateCommand()

                  cmd.CommandText <-
                      "INSERT INTO dag_node SELECT stream_id, @f, parents_json, op_json, prompt_id, actor_json, timestamp, result_envelope_json FROM dag_node WHERE hash = @h;"

                  cmd.Parameters.AddWithValue("@f", forged) |> ignore
                  cmd.Parameters.AddWithValue("@h", child.Hash) |> ignore

                  if cmd.ExecuteNonQuery() <> 1 then
                      failtest "expected to insert exactly one forged row"

              let message =
                  refusalMessage "SqliteDagSink.Records over another writer's forged row" (fun () ->
                      reader.Records "s" |> Async.RunSynchronously |> ignore)

              Expect.stringContains message forged "names the forged record"
          }

          test "D32: a prune by another writer re-loads the stream at the next read" {
              use db = TempDb.create "dagverify"
              let cs = sprintf "Data Source=%s" db.Path
              let reader = SqliteDagSink.create<TestMsg> cs testCodec
              let writer = SqliteDagSink.create<TestMsg> cs testCodec
              let g = stepRecord "s" None brand 1L
              let child = stepRecord "s" (Some g) removeRight 2L
              add writer g
              add writer child
              Expect.equal (reader.Records "s" |> Async.RunSynchronously).Length 2 "loaded"
              Expect.isTrue (writer.Tombstone("s", child.Hash) |> Async.RunSynchronously) "pruned by the other writer"

              Expect.equal
                  (reader.Records "s" |> Async.RunSynchronously |> List.map _.Hash)
                  [ g.Hash ]
                  "the reader holds the live set after the prune"

              Expect.isNone (reader.TryGet("s", child.Hash) |> Async.RunSynchronously) "and no longer the pruned record"
          }

          test "D32: the in-memory sink re-verifies a clean stream on demand" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let g = stepRecord "s" None brand 1L
              add sink g
              add sink (stepRecord "s" (Some g) removeRight 2L)

              (Option.get (DagSinkCapabilities.tryIndexed sink)).Reverify "s"
              |> Async.RunSynchronously

              Expect.equal (sink.Records "s" |> Async.RunSynchronously).Length 2 "the stream reads back after Reverify"
          } ]
