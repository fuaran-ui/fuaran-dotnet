module Fuaran.UI.Ops.Benchmarks.Program

open System
open System.IO
open BenchmarkDotNet.Running
open Fuaran.UI.Ops.Benchmarks

// ============================================================================
//  Entry point (Phase 200 + Wave-T op-append follow-on).
//
//  Subcommands:
//   - `emit-template [path]` — (build-time, safe) write the PENDING apply /
//        re-derivation baseline template JSON. This is the committed artifact the
//        Phase 201 gate reads; it declares the metric IDs + units with no
//        captured numbers.
//   - `emit-op-template [path]` — (build-time, safe) the same for the op-stream
//        WRITE-path baseline (`op-append-baseline.json`).
//   - `hit-rate`             — (RUN — deferred) print the memo reuse fraction
//        over a representative edit session per corpus size.
//   - `append-rate [count]`  — (RUN — deferred) print the durable-append mean_ns
//        + alloc_b per op shape over `count` appends (default 20000) to a fresh
//        InMemory sink — the off-hot-path half of the op-stream write baseline.
//   - (default / BDN args)   — (RUN — deferred) run the BenchmarkDotNet suite
//        (both the apply and op-stream classes; filter with `--filter *OpStream*`).
//        Capturing the numbers + refreshing the baseline to `captured` is the
//        deferred benchmark run (see README.md); building this exe only
//        compile-checks the harness.
// ============================================================================

let private defaultBaselinePath =
    Path.Combine(__SOURCE_DIRECTORY__, "apply-rederivation-baseline.json")

let private defaultOpBaselinePath =
    Path.Combine(__SOURCE_DIRECTORY__, "op-append-baseline.json")

let private emitTemplate (path: string) =
    let json = Baseline.PerfBaseline.toJson Baseline.PerfBaseline.applyPendingTemplate
    File.WriteAllText(path, json)
    printfn "Wrote pending baseline template: %s" path

let private emitOpTemplate (path: string) =
    let json = Baseline.PerfBaseline.toJson Baseline.PerfBaseline.opPendingTemplate
    File.WriteAllText(path, json)
    printfn "Wrote pending op-append baseline template: %s" path

let private printHitRates () =
    for s in Corpus.all do
        let rate = HitRate.measure s 64 16
        printfn "memo.hit_rate.%s = %.4f" s.Name rate

let private printAppendRates (count: int) =
    for s in OpCorpus.all do
        let meanNs, allocB = AppendRate.measure s count
        printfn "opstream.append.%s.mean_ns = %.1f" s.Name meanNs
        printfn "opstream.append.%s.alloc_b = %.1f" s.Name allocB

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | "emit-template" :: rest ->
        let path = rest |> List.tryHead |> Option.defaultValue defaultBaselinePath
        emitTemplate path
        0
    | "emit-op-template" :: rest ->
        let path = rest |> List.tryHead |> Option.defaultValue defaultOpBaselinePath
        emitOpTemplate path
        0
    | "hit-rate" :: _ ->
        printHitRates ()
        0
    | "append-rate" :: rest ->
        let count =
            rest
            |> List.tryHead
            |> Option.bind (fun s ->
                match Int32.TryParse s with
                | true, n -> Some n
                | _ -> None)
            |> Option.defaultValue 20000

        printAppendRates count
        0
    | _ ->
        BenchmarkSwitcher
            .FromTypes(
                [| typeof<ApplyBenchmarks.ApplyBenchmarks>
                   typeof<OpStreamBenchmarks.OpStreamBenchmarks> |]
            )
            .Run(argv)
        |> ignore

        0
