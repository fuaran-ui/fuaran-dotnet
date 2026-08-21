module Fuaran.UI.JsonDecode.Tests.Program

open System
open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Corpus generator. Regenerate the workspace-root
    // wire-format-fixtures/ corpus from the F# fixture values:
    //   dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus <dir>
    | "--emit-corpus" :: dir :: _ ->
        Corpus.emit dir
        0
    // Phase 442 — write ONLY the render-fidelity manifest into a corpus
    // directory:
    //   dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-fidelity <dir>
    // `--emit-corpus` co-emits it, so this exists for the declaration-only
    // change: the fidelity table moved, no fixture did, and regenerating the
    // whole corpus to publish one artefact would put unrelated churn in a
    // shared repo.
    | "--emit-fidelity" :: dir :: _ ->
        RenderFidelityArtifact.write dir
        printfn "Emitted %s to %s" RenderFidelityArtifact.fileName dir
        0
    // Phase 101 cross-host fuzz-sample exchange. `--emit-fuzz-samples <dir>
    // <count>` writes F#-canonical generated samples; `--check-fuzz-samples
    // <dir> [host]` validates the <host>-canonical samples that host's emitter
    // wrote to <dir>/<host>/. `host` defaults to `typescript` (the Phase 101
    // leg the cross-host runner drives); `python` is the Phase 236 exchange.
    | "--emit-fuzz-samples" :: dir :: countStr :: _ -> FuzzSamples.emit dir (int countStr)
    | "--check-fuzz-samples" :: dir :: host :: _ -> FuzzSamples.check dir host
    | "--check-fuzz-samples" :: dir :: _ -> FuzzSamples.check dir "typescript"
    // Phase 779 decoder robustness fuzz — the LONG mode. The bounded run is a
    // test in this same assembly and joins the repo gate automatically; this is
    // the on-demand deep run, and it regenerates docs/DECODER-ROBUSTNESS.md from
    // its own result so the published figures cannot drift from the run that
    // produced them.
    //   dotnet run --project src/Fuaran.UI.JsonDecode.Tests -c Release -- --fuzz-long 250000 [seed]
    // The seed defaults to the clock: a long run is exploration, and a fixed
    // seed would explore the same region forever. It is recorded in the note.
    | "--fuzz-long" :: rest ->
        let iterations =
            match rest with
            | n :: _ ->
                match Int32.TryParse n with
                | true, v when v > 0 -> v
                | _ -> failwithf "--fuzz-long: '%s' is not a positive iteration count" n
            | [] -> 250_000

        let seed =
            match rest with
            | _ :: s :: _ ->
                match UInt64.TryParse s with
                | true, v -> v
                | _ -> failwithf "--fuzz-long: '%s' is not a seed" s
            | _ -> uint64 (DateTime.UtcNow.Ticks)

        let stats =
            DecoderFuzz.run
                DecoderFuzz.realSubjects
                DecoderFuzz.defaultBudgets
                DecoderFuzz.longConfig
                seed
                iterations
                true

        printfn "decoder fuzz (seed %d): %s" seed (DecoderFuzz.summarise stats)

        for c in stats.Counterexamples do
            let path = DecoderFuzz.persist c

            eprintfn
                "COUNTEREXAMPLE %s iteration %d (%s): %s — repro at %s"
                c.Subject
                c.Iteration
                c.Origin
                (DecoderFuzz.describeVerdict c.Verdict)
                path

        match FuzzEvidence.write stats DecoderFuzz.longConfig DecoderFuzz.defaultBudgets with
        | Some path -> printfn "Evidence note regenerated: %s" path
        | None ->
            // Loud, not silent: the whole point of the long run is the note, so
            // a run that produced no note has not done its job.
            eprintfn "Could not locate the repo root; docs/DECODER-ROBUSTNESS.md was NOT written."

        if List.isEmpty stats.Counterexamples then 0 else 1
    // Replay a generated stream over an iteration range — the investigative
    // path a persisted repro's notes point at.
    //   -- --fuzz-replay <seed> <fromIteration> <toIteration> [bounded|long]
    | "--fuzz-replay" :: seedStr :: fromStr :: toStr :: rest ->
        let cfg =
            match rest with
            | "bounded" :: _ -> DecoderFuzz.boundedConfig
            | _ -> DecoderFuzz.longConfig

        let found =
            DecoderFuzz.replay
                DecoderFuzz.realSubjects
                DecoderFuzz.defaultBudgets
                cfg
                (UInt64.Parse seedStr)
                (Int32.Parse fromStr)
                (Int32.Parse toStr)

        printfn ""
        printfn "%d contract violation(s) in the replayed range." found
        if found = 0 then 0 else 1
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
