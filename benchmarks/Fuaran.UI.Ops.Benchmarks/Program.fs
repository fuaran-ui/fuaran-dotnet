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
//   - `emit-render-template [path]` — (build-time, safe) the same for the
//        RENDER-SPINE baseline (`render-allocation-baseline.json`, Phase 207).
//   - `render-alloc [count]` — (RUN — deferred) print mean_ns + alloc_b for the
//        three render-spine families (reactive key walk, live-store merge,
//        per-node class/id vocabulary) at each tree size.
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

let private defaultRenderBaselinePath =
    Path.Combine(__SOURCE_DIRECTORY__, "render-allocation-baseline.json")

let private emitTemplate (path: string) =
    let json = Baseline.PerfBaseline.toJson Baseline.PerfBaseline.applyPendingTemplate
    File.WriteAllText(path, json)
    printfn "Wrote pending baseline template: %s" path

let private emitOpTemplate (path: string) =
    let json = Baseline.PerfBaseline.toJson Baseline.PerfBaseline.opPendingTemplate
    File.WriteAllText(path, json)
    printfn "Wrote pending op-append baseline template: %s" path

let private emitRenderTemplate (path: string) =
    let json = Baseline.PerfBaseline.toJson Baseline.PerfBaseline.renderPendingTemplate
    File.WriteAllText(path, json)
    printfn "Wrote pending render-allocation baseline template: %s" path

let private printHitRates () =
    for s in Corpus.all do
        let rate = HitRate.measure s 64 16
        printfn "memo.hit_rate.%s = %.4f" s.Name rate

let private printRenderAllocations (count: int) =
    for s in RenderAllocation.all do
        let keysNs, keysB = RenderAllocation.measureStateKeys s count
        let mergeNs, mergeB = RenderAllocation.measureLiveStateMerge s count
        let vocabNs, vocabB = RenderAllocation.measureClassVocabulary s count
        printfn "render.state_keys.%s.mean_ns = %.1f" s.Name keysNs
        printfn "render.state_keys.%s.alloc_b = %.1f" s.Name keysB
        printfn "render.live_state_merge.%s.mean_ns = %.1f" s.Name mergeNs
        printfn "render.live_state_merge.%s.alloc_b = %.1f" s.Name mergeB
        printfn "render.class_vocabulary.%s.mean_ns = %.1f" s.Name vocabNs
        printfn "render.class_vocabulary.%s.alloc_b = %.1f" s.Name vocabB

        let rawNs, rawB = RenderAllocation.measureFragmentExpansionUncached s count
        let memoNs, memoB = RenderAllocation.measureFragmentExpansionMemo s count
        printfn "render.fragment_expand_uncached.%s.mean_ns = %.1f" s.Name rawNs
        printfn "render.fragment_expand_uncached.%s.alloc_b = %.1f" s.Name rawB
        printfn "render.fragment_expand_memo.%s.mean_ns = %.1f" s.Name memoNs
        printfn "render.fragment_expand_memo.%s.alloc_b = %.1f" s.Name memoB

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
    | "emit-render-template" :: rest ->
        let path = rest |> List.tryHead |> Option.defaultValue defaultRenderBaselinePath
        emitRenderTemplate path
        0
    | "render-alloc" :: rest ->
        let count =
            rest
            |> List.tryHead
            |> Option.bind (fun s ->
                match Int32.TryParse s with
                | true, n -> Some n
                | _ -> None)
            |> Option.defaultValue 20000

        printRenderAllocations count
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
