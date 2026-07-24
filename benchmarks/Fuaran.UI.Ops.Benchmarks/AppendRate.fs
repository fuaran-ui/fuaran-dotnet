module Fuaran.UI.Ops.Benchmarks.AppendRate

open System
open System.Diagnostics
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory

// ============================================================================
//  AppendRate (Wave-T op-append follow-on) — durable-append throughput, off the
//  BenchmarkDotNet hot path.
//
//  `IOpStreamSink.Append` is `Async` and rejects duplicate (StreamId, Sequence)
//  pairs, so it does not fit BenchmarkDotNet's repeat-the-same-call hot loop the
//  way a pure function does (every invocation must carry a fresh, monotonically
//  increasing sequence into a growing sink). So the append cost is measured here
//  the way `HitRate.fs` measures hit-rate: a deterministic, bounded run over a
//  fresh InMemory sink, reporting mean wall-time and allocation per append.
//
//  The records are hash-chained up front so the loop times the APPEND alone
//  (Dictionary insert + per-stream lock + the async-state-machine cost a host
//  pays per op) — the encode + hash cost is measured separately by
//  `OpStreamBenchmarks`. InMemory is the floor: a durable Sqlite sink adds I/O
//  on top, but the in-process append is the contention + bookkeeping core every
//  sink shares.
// ============================================================================

/// Build `count` hash-chained records for one op shape against a single stream,
/// sequences 1..count. The chain is correct so the append path sees exactly the
/// records a real host would persist.
let private buildChain (op: TreeOp<unit>) (count: int) : OpRecord<unit> array =
    let records = Array.zeroCreate<OpRecord<unit>> count
    let mutable previousHash = HashChain.genesisPreviousHash
    let timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L)

    for i in 0 .. count - 1 do
        let sequence = i + 1
        let actor = Actor.Human "bench-user"

        let hash =
            HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success

        records[i] <-
            { StreamId = "append-rate"
              Sequence = sequence
              PreviousHash = previousHash
              Hash = hash
              Op = op
              PromptId = None
              Actor = actor
              Timestamp = timestamp
              ResultEnvelope = OpResultEnvelope.Success }

        previousHash <- hash

    records

/// Append every record in `records` to `sink`, synchronously, in order.
let private appendAll (sink: IOpStreamSink<unit>) (records: OpRecord<unit> array) : unit =
    for r in records do
        sink.Append r |> Async.RunSynchronously

/// Measure mean append wall-time (ns) and allocation (bytes) per append for one
/// op shape over `count` durable appends to a fresh InMemory sink.
let measure (scenario: OpCorpus.Scenario) (count: int) : float * float =
    let records = buildChain scenario.Op count

    // Warm the JIT + the async machinery on a throwaway sink so the measured run
    // reflects steady state, not first-call compilation.
    appendAll (InMemorySink.create<unit> ()) (buildChain scenario.Op (min count 256))

    let sink = InMemorySink.create<unit> ()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()
    appendAll sink records
    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()

    let meanNs = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / float count
    let allocB = float (allocAfter - allocBefore) / float count
    meanNs, allocB
