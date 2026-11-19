module FableRender.Program

// ============================================================================
//  Phase 2550 — the client render suite's entry point.
//
//  Fable-only. `drive.mjs` sets up a DOM, imports the compiled module, and reads
//  the verdict this publishes; the Fable stage (`tests/fable-laws/fable-check.ps1`)
//  runs `drive.mjs` as a named leg. The project still restores and cracks on
//  .NET (Fable needs that), where it does nothing but say so and fail, so a
//  .NET run can never be mistaken for the suite.
//
//  Two signals, so neither is the only one: `process.exitCode` (Fable drops
//  `main`'s return value, so it is set by hand, as `tests/fable-laws/Program.fs`
//  does) and `globalThis.__fuaranRenderVerdict`, which `drive.mjs` requires to be
//  present and to count at least one test.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core.JsInterop
open FableRender.Expecto

let private publish (verdict: Verdict) : unit =
    emitJsStatement (verdict.Tests, verdict.Failed) "globalThis.__fuaranRenderVerdict = { tests: $0, failed: $1 }"

    if verdict.Tests = 0 || verdict.Failed > 0 then
        emitJsStatement () "process.exitCode = 1"

[<EntryPoint>]
let main _ =
    let verdict =
        runTests (testList "fable-render" [ FableRender.SubtreeMemoTests.tests; FableRender.RawSinkPropTests.tests ])

    publish verdict
    0
#else
[<EntryPoint>]
let main _ =
    eprintfn "tests/fable-render runs under Fable only: compile it with dotnet fable and run drive.mjs."
    1
#endif
