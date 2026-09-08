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
    // Phase 1613 — fold a BenchmarkDotNet run into a baseline artifact. This is
    // the step the README called "the operator step" and the Phase 201 CI
    // workflow carried as a commented placeholder; automating it is what lets a
    // FRESH measurement be produced the same way the committed baseline was,
    // which is the precondition for comparing them honestly.
    //
    //   capture apply  <bdn-artifacts-dir> [out]
    //   capture op     <bdn-artifacts-dir> [out] [--appends N]
    //   capture render [out] [--count N]
    | "capture" :: which :: rest ->
        // Split `rest` into positional paths and `--flag value` pairs in one
        // pass, so a path that happens to look like a number is still a path.
        let rec split (positional: string list) (flags: Map<string, string>) =
            function
            | [] -> List.rev positional, flags
            | (f: string) :: v :: tail when f.StartsWith "--" -> split positional (Map.add f v flags) tail
            | f :: tail when f.StartsWith "--" -> split positional flags tail
            | p :: tail -> split (p :: positional) flags tail

        let positional, flags = split [] Map.empty rest

        let flagValue (name: string) (fallback: int) =
            match Map.tryFind name flags |> Option.map Int32.TryParse with
            | Some(true, n) -> n
            | _ -> fallback

        let at i fallback =
            positional |> List.tryItem i |> Option.defaultValue fallback

        match which with
        | "apply" ->
            let reports = at 0 (Path.Combine(__SOURCE_DIRECTORY__, "BenchmarkDotNet.Artifacts"))
            Capture.write (at 1 defaultBaselinePath) (Capture.captureApply reports)
        | "op" ->
            let reports = at 0 (Path.Combine(__SOURCE_DIRECTORY__, "BenchmarkDotNet.Artifacts"))
            Capture.write (at 1 defaultOpBaselinePath) (Capture.captureOp reports (flagValue "--appends" 20000))
        | "render" -> Capture.write (at 0 defaultRenderBaselinePath) (Capture.captureRender (flagValue "--count" 20000))
        | other ->
            eprintfn "unknown capture target '%s' — expected apply | op | render" other
            2
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
