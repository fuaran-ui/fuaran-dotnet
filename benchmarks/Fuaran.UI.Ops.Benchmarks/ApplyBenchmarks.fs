module Fuaran.UI.Ops.Benchmarks.ApplyBenchmarks

open BenchmarkDotNet.Attributes
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Memo
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  ApplyBenchmarks (Phase 200) — the BenchmarkDotNet class measuring the three
//  apply paths the Phase 183 engine distinguishes, parameterised over the three
//  corpus sizes. `[<MemoryDiagnoser>]` captures per-op allocation alongside
//  wall-time so both the `mean_ns` and `alloc_b` catalogue metrics are produced
//  by one run.
//
//   - FullApply        — bare `FragmentApply.apply`, no cache. The cold cost
//                        every derivation pays without memoisation. This is the
//                        regression baseline: if a future change reverts the
//                        engine to whole-tree re-derivation, the memoised /
//                        incremental columns collapse toward this one.
//   - MemoisedApplyHit — `Engine.Apply` against a pre-warmed cache: a structural
//                        HIT, the substituted tree reused with no re-derivation.
//   - IncrementalReapply — `Engine.Reapply` with a single value edit: the tree
//                        is reused (reference-identical) and exactly one hole-
//                        address binding is recomputed. The Phase 183 headline.
//
//  A NoOpSink is used so the telemetry emission costs a single virtual dispatch
//  and nothing else — the memo HIT-RATE (which needs counting) is measured
//  separately in `HitRate.fs`, off the hot path.
//
//  RUNNING THIS IS THE DEFERRED STEP. Building the class compile-checks the API
//  usage; capturing the numbers (`dotnet run -c Release -- --filter *`) is a
//  benchmark run, gated separately (see README.md).
// ============================================================================

[<MemoryDiagnoser>]
type ApplyBenchmarks() =

    let sink = NoOpSink.create ()

    // One engine per benchmark instance; warmed in GlobalSetup so the HIT and
    // incremental paths measure steady state, not a cold miss.
    let mutable engine = Engine<unit>(256, sink)
    let mutable scenario = Corpus.small
    // The recorded base derivation the incremental path re-derives against.
    let mutable baseDerivation = Unchecked.defaultof<Derivation<unit>>

    /// BenchmarkDotNet parameterises across the three corpus sizes; each becomes
    /// a separate row in the captured baseline.
    [<Params("Small", "Medium", "Large")>]
    member val Size = "Small" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        scenario <- Corpus.all |> List.find (fun s -> s.Name = this.Size)

        engine <- Engine<unit>(256, sink)
        // Warm the cache so MemoisedApplyHit measures a HIT, and record the base
        // derivation IncrementalReapply re-derives against.
        baseDerivation <-
            match engine.Apply(scenario.Fragment, scenario.RefId, scenario.BaseArgs, scenario.SlotArgs) with
            | Ok d -> d
            | Error e -> failwith $"benchmark setup apply failed: {e}"

    [<Benchmark(Baseline = true)>]
    member _.FullApply() : Result<FragmentApplication<unit>, string> =
        FragmentApply.apply scenario.Fragment scenario.RefId scenario.BaseArgs scenario.SlotArgs

    [<Benchmark>]
    member _.MemoisedApplyHit() : Result<Derivation<unit>, string> =
        engine.Apply(scenario.Fragment, scenario.RefId, scenario.BaseArgs, scenario.SlotArgs)

    [<Benchmark>]
    member _.IncrementalReapply() : Result<Derivation<unit>, string> =
        engine.Reapply(baseDerivation, scenario.Fragment, scenario.RefId, scenario.OneValueEdit, scenario.SlotArgs)
