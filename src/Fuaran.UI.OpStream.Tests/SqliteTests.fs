module Fuaran.UI.OpStream.Tests.SqliteTests

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  SqliteSink — file-backed durability. Each test owns a unique DB file
//  under the system temp dir; cleanup is best-effort (Microsoft.Data.Sqlite
//  holds the pool open even after the sink is GC'd, so deletion may race
//  in noisy environments — the temp file is small and tests are idempotent).
// ============================================================================

let private freshDbPath () : string =
    let name = sprintf "fuaran-opstream-%s.db" (Guid.NewGuid().ToString("N"))
    Path.Combine(Path.GetTempPath(), name)

let private connStringFor (path: string) : string = sprintf "Data Source=%s" path

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — SqliteSink durability"
        [ test "Append then Replay (in-process) returns identical records" {
              let path = freshDbPath ()

              try
                  let sink: IOpStreamSink<TestMsg> = SqliteSink.create (connStringFor path) testCodec

                  let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
                  let record = buildRecord "stream" 1 op None (timestamp 100L)

                  sink.Append record |> Async.RunSynchronously

                  let replayed = sink.Replay("stream", 1, 10) |> Async.RunSynchronously

                  Expect.equal replayed.Length 1 "One record replayed"
                  Expect.equal replayed[0].StreamId record.StreamId "StreamId round-trips"
                  Expect.equal replayed[0].Sequence record.Sequence "Sequence round-trips"
                  Expect.equal replayed[0].Hash record.Hash "Hash round-trips"
                  Expect.equal replayed[0].PreviousHash record.PreviousHash "PreviousHash round-trips"

                  Expect.equal
                      replayed[0].Actor
                      record.Actor
                      "Actor round-trips (typed-actor JSON in the user_id column)"

                  Expect.equal
                      (replayed[0].Timestamp.ToUnixTimeSeconds())
                      (record.Timestamp.ToUnixTimeSeconds())
                      "Timestamp round-trips at second precision"

                  match replayed[0].Op with
                  | TreeOp.RemoveNode(NodeId raw) -> Expect.equal raw "x" "Op decoded as RemoveNode 'x'"
                  | other -> failtestf "Expected RemoveNode 'x', got %A" other
              finally
                  try
                      File.Delete path
                  with _ ->
                      ()
          }

          test "Records survive sink disposal + reopen (durability)" {
              let path = freshDbPath ()

              try
                  let connStr = connStringFor path

                  // First sink — append two records, then drop the reference.
                  let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
                  let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
                  let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
                  let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

                  do
                      let sink: IOpStreamSink<TestMsg> = SqliteSink.create connStr testCodec
                      sink.Append r1 |> Async.RunSynchronously
                      sink.Append r2 |> Async.RunSynchronously

                  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                  // Second sink against the same file.
                  let reopened: IOpStreamSink<TestMsg> = SqliteSink.create connStr testCodec

                  let records = reopened.Replay("stream", 1, 10) |> Async.RunSynchronously

                  Expect.equal records.Length 2 "Both records survive sink disposal + reopen"
                  Expect.equal records[0].Sequence 1 "First record sequence"
                  Expect.equal records[1].Sequence 2 "Second record sequence"

                  // Hash chain verifies post-reopen.
                  match Verify.chain records with
                  | Ok() -> ()
                  | Error e -> failtestf "Hash chain failed after reopen: %A" e
              finally
                  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                  try
                      File.Delete path
                  with _ ->
                      ()
          }

          test "Streams() returns distinct stream ids" {
              let path = freshDbPath ()

              try
                  let sink: IOpStreamSink<TestMsg> = SqliteSink.create (connStringFor path) testCodec

                  let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
                  let rA = buildRecord "stream-a" 1 op None (timestamp 100L)
                  let rB = buildRecord "stream-b" 1 op None (timestamp 100L)
                  let rA2 = buildRecord "stream-a" 2 op (Some rA) (timestamp 200L)

                  sink.Append rA |> Async.RunSynchronously
                  sink.Append rB |> Async.RunSynchronously
                  sink.Append rA2 |> Async.RunSynchronously

                  let streams = sink.Streams() |> Async.RunSynchronously |> List.sort
                  Expect.equal streams [ "stream-a"; "stream-b" ] "Two distinct stream ids"
              finally
                  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                  try
                      File.Delete path
                  with _ ->
                      ()
          }

          test "Duplicate (StreamId, Sequence) is rejected by primary-key constraint" {
              let path = freshDbPath ()

              try
                  let sink: IOpStreamSink<TestMsg> = SqliteSink.create (connStringFor path) testCodec

                  let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
                  let record = buildRecord "stream" 1 op None (timestamp 100L)

                  sink.Append record |> Async.RunSynchronously

                  Expect.throws
                      (fun () -> sink.Append record |> Async.RunSynchronously)
                      "Duplicate (StreamId, Sequence) raises via SQLITE_CONSTRAINT"
              finally
                  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                  try
                      File.Delete path
                  with _ ->
                      ()
          } ]
