module Fuaran.UI.OpStream.Dag.Tests.SqliteDagTests

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  SqliteDagSink durability + CAS (Phase 178 task — Sqlite impl of the DAG
//  sink). Each test runs against a fresh temp-file database so the conditional
//  UPDATE … WHERE head = expected CAS is exercised against real durable state.
// ============================================================================

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private success =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Success }
    )

let private removeRight = TreeOp.RemoveNode rightChildId

/// A Sqlite DAG sink over the database at `path` (a `TempDb`'s, so the file goes with the test).
let private sinkAt (path: string) : IDagOpStreamSink<TestMsg> =
    SqliteDagSink.create<TestMsg> (sprintf "Data Source=%s" path) testCodec

[<Tests>]
let tests =
    testList
        "Dag.Sqlite"
        [ test "add then get round-trips a record (parents + content survive)" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink a

              match sink.TryGet("s", a.Hash) |> Async.RunSynchronously with
              | Some r ->
                  Expect.equal r.Hash a.Hash "hash round-trips"
                  Expect.equal r.Parents [ g.Hash ] "parents round-trip"
                  Expect.equal (CanonicalJson.encodeOp r.Op) (CanonicalJson.encodeOp a.Op) "op round-trips"
                  Expect.equal (DagOpRecord.recomputeHash r) a.Hash "the read record re-addresses to its hash"
              | None -> failtest "record not found after add"
          }

          test "tryAdvanceHead CAS is durable and conditional" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) removeRight 2L
              add sink g
              add sink a

              Expect.isTrue (sink.TryAdvanceHead("s", None, g.Hash) |> Async.RunSynchronously) "genesis advance"

              Expect.isFalse
                  (sink.TryAdvanceHead("s", None, a.Hash) |> Async.RunSynchronously)
                  "stale expected (None) loses"

              Expect.isTrue
                  (sink.TryAdvanceHead("s", Some g.Hash, a.Hash) |> Async.RunSynchronously)
                  "matching expected advances"

              Expect.equal (sink.Head "s" |> Async.RunSynchronously) (Some a.Hash) "head is a"
          }

          test "idempotent re-add; Core's mergeBase over the durable graph" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              let b = stepRecord "s" (Some a) removeRight 3L
              let c = stepRecord "s" (Some a) brand 4L
              add sink g
              add sink a
              add sink a // idempotent
              add sink b
              add sink c

              Expect.equal (sink.Records "s" |> Async.RunSynchronously |> List.length) 4 "re-add was a no-op"

              let dag = sink.Records "s" |> Async.RunSynchronously |> DagOpRecord.toDag
              Expect.equal (Fuaran.Core.Dag.mergeBase dag b.Hash c.Hash) (Some a.Hash) "mergeBase(b,c) = a"
          }

          test "pruning moves a leaf to the durable skeleton and the live set still verifies" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              add sink g
              add sink a

              Expect.isTrue (sink.Tombstone("s", a.Hash) |> Async.RunSynchronously) "pruned"

              let recs = sink.Records "s" |> Async.RunSynchronously
              Expect.equal (recs |> List.map _.Hash) [ g.Hash ] "a left the live records"
              Expect.isNone (sink.TryGet("s", a.Hash) |> Async.RunSynchronously) "TryGet no longer returns a"

              let skeleton = sink.Skeleton "s" |> Async.RunSynchronously

              Expect.equal
                  (skeleton |> List.map (fun n -> n.Hash, n.Parents))
                  [ a.Hash, [ g.Hash ] ]
                  "a's address and parent link survive in the skeleton"

              Expect.equal
                  (sink.Parents("s", a.Hash) |> Async.RunSynchronously)
                  [ g.Hash ]
                  "Parents still answers for a"

              match DagVerify.records recs with
              | Ok() -> ()
              | Error e -> failtestf "chain must verify after tombstone: %A" e
          }

          test "degenerate linear history replays through the Sqlite sink" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              let b = stepRecord "s" (Some a) removeRight 3L
              add sink g
              add sink a
              add sink b

              let initial = buildDashboard ()
              let replayed = replayHead sink "s" initial b.Hash
              let expected = initial |> applyOk brand |> applyOk success |> applyOk removeRight
              Expect.equal (canonical replayed) (canonical expected) "Sqlite replay matches the linear fold"
          } ]
