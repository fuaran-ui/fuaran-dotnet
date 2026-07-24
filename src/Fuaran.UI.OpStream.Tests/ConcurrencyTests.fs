module Fuaran.UI.OpStream.Tests.ConcurrencyTests

open System
open System.Threading.Tasks
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  Concurrent appends — the in-memory sink serialises appends via a single
//  lock so the (StreamId, Sequence) uniqueness invariant holds under
//  contention.
// ============================================================================

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Concurrency"
        [ test "Parallel appends to disjoint streams both land" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()

              let opA = TreeOp.RemoveNode(NodeId "a"): TreeOp<TestMsg>
              let opB = TreeOp.RemoveNode(NodeId "b"): TreeOp<TestMsg>
              let recA = buildRecord "stream-a" 1 opA None (timestamp 100L)
              let recB = buildRecord "stream-b" 1 opB None (timestamp 100L)

              let appendA = async { do! sink.Append recA } |> Async.StartAsTask

              let appendB = async { do! sink.Append recB } |> Async.StartAsTask

              Task.WaitAll(appendA, appendB)

              let latestA = sink.LatestSequence "stream-a" |> Async.RunSynchronously
              let latestB = sink.LatestSequence "stream-b" |> Async.RunSynchronously

              Expect.equal latestA 1 "Stream A has sequence 1"
              Expect.equal latestB 1 "Stream B has sequence 1"
          }

          test "Parallel appends to the same stream with distinct sequences both land" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()

              let op1 = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let op2 = TreeOp.RemoveNode(NodeId "y"): TreeOp<TestMsg>
              let r1 = buildRecord "stream" 1 op1 None (timestamp 100L)
              let r2 = buildRecord "stream" 2 op2 (Some r1) (timestamp 200L)

              let appendOne = async { do! sink.Append r1 } |> Async.StartAsTask

              let appendTwo = async { do! sink.Append r2 } |> Async.StartAsTask

              Task.WaitAll(appendOne, appendTwo)

              let latest = sink.LatestSequence "stream" |> Async.RunSynchronously
              Expect.equal latest 2 "LatestSequence is 2 after both appends"

              let records = sink.Replay("stream", 1, 10) |> Async.RunSynchronously

              Expect.equal records.Length 2 "Two records persisted"
          }

          test "Duplicate (StreamId, Sequence) is rejected" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()

              let op = TreeOp.RemoveNode(NodeId "x"): TreeOp<TestMsg>
              let record = buildRecord "stream" 1 op None (timestamp 100L)

              sink.Append record |> Async.RunSynchronously

              Expect.throws
                  (fun () -> sink.Append record |> Async.RunSynchronously)
                  "Duplicate (StreamId, Sequence) raises"
          } ]
