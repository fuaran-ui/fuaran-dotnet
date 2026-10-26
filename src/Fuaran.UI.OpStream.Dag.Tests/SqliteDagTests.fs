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
                  Expect.isFalse r.Tombstoned "live record"
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

          test "idempotent re-add; topology lca over the durable graph" {
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

              match sink.Lca("s", b.Hash, c.Hash) |> Async.RunSynchronously with
              | LcaResult.Unique h -> Expect.equal h a.Hash "lca(b,c) = a"
              | other -> failtestf "expected Unique a, got %A" other
          }

          test "tombstone drops the payload but the chain still verifies" {
              use db = TempDb.create "dag"
              let sink = sinkAt db.Path

              let g = stepRecord "s" None brand 1L
              let a = stepRecord "s" (Some g) success 2L
              add sink g
              add sink a

              Expect.isTrue (sink.Tombstone("s", a.Hash) |> Async.RunSynchronously) "tombstoned"

              let recs = sink.Records "s" |> Async.RunSynchronously
              let ts = recs |> List.find (fun r -> r.Hash = a.Hash)
              Expect.isTrue ts.Tombstoned "a is tombstoned"
              Expect.equal ts.Parents [ g.Hash ] "parent link preserved through tombstone"

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
              let replayed = replaySpine sink "s" initial b.Hash
              let expected = initial |> applyOk brand |> applyOk success |> applyOk removeRight
              Expect.equal (canonical replayed) (canonical expected) "Sqlite replay matches the linear fold"
          } ]
