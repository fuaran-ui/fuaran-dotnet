module Fuaran.UI.Ops.Benchmarks.OpStreamBenchmarks

open System
open BenchmarkDotNet.Attributes
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  OpStreamBenchmarks (Wave-T op-append follow-on) — the BenchmarkDotNet class
//  measuring the op-stream WRITE path the Phase 200 harness left unmeasured:
//  canonical encode → SHA-256 hash-chain → durable record construction,
//  parameterised over the three op shapes (OpCorpus). `[<MemoryDiagnoser>]`
//  captures per-op allocation alongside wall-time so both the `mean_ns` and
//  `alloc_b` catalogue metrics come from one run.
//
//   - EncodeOp     — `CanonicalJson.encodeOp` alone (the baseline): the canonical
//                    JSON string the hash pre-image is built from. Isolating it
//                    answers "is the generated codec cheap?" independent of the
//                    SHA-256 cost layered on top.
//   - ComputeHash  — `HashChain.computeHash`: encode + the genesis-pre-image
//                    SHA-256. The headline — its delta over EncodeOp is the pure
//                    hashing cost, and across shapes shows whether a larger
//                    hashed pre-image (Phase 320's actor-in-hash concern) is
//                    cheap.
//   - BuildRecord  — the full per-op record-production cost: compute the hash
//                    AND materialise the `OpRecord<unit>` value the sink appends.
//                    The CPU + alloc a host pays to turn an applied op into a
//                    durable, hash-chained record (the synchronous half of the
//                    write path; the async durable Append throughput is measured
//                    deterministically off the hot path in `AppendRate.fs`).
//
//  RUNNING THIS IS A DEFERRED STEP (like ApplyBenchmarks): building the class
//  compile-checks the API usage; capturing the numbers is a benchmark run
//  (`dotnet run -c Release -- --filter *OpStream*`). See README.md.
// ============================================================================

[<MemoryDiagnoser>]
type OpStreamBenchmarks() =

    // The genesis previous-hash is the Sequence = 1 pre-image prefix — the same
    // constant every stream's first record chains from. Using it keeps the
    // measured hash cost independent of any prior-record lookup.
    let previousHash = HashChain.genesisPreviousHash
    let timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L)

    let mutable scenario = OpCorpus.updateProp

    /// BenchmarkDotNet parameterises across the three op shapes; each becomes a
    /// separate row in the captured baseline.
    [<Params("UpdateProp", "InsertChild", "Batch16")>]
    member val Shape = "UpdateProp" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        scenario <- OpCorpus.all |> List.find (fun s -> s.Name = this.Shape)

    [<Benchmark(Baseline = true)>]
    member _.EncodeOp() : string = CanonicalJson.encodeOp scenario.Op

    [<Benchmark>]
    member _.ComputeHash() : string =
        HashChain.computeHash
            previousHash
            scenario.Op
            1
            timestamp
            (Actor.Human "bench-user")
            None
            OpResultEnvelope.Success

    [<Benchmark>]
    member _.BuildRecord() : OpRecord<unit> =
        let actor = Actor.Human "bench-user"

        let hash =
            HashChain.computeHash previousHash scenario.Op 1 timestamp actor None OpResultEnvelope.Success

        { StreamId = "bench-stream"
          Sequence = 1
          PreviousHash = previousHash
          Hash = hash
          Op = scenario.Op
          PromptId = None
          Actor = actor
          Timestamp = timestamp
          ResultEnvelope = OpResultEnvelope.Success }
