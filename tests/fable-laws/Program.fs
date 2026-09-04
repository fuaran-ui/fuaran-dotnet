module FableLaws.Program

// ============================================================================
//  Phase 1488 — the executor. One program, two pipelines.
//
//  Run on .NET (`dotnet run --project`, which is how `test-suites.json` reaches it) it is an
//  ordinary console gate: non-zero exit on any refuted law. Compiled by Fable and run under Node
//  (`fable-check.ps1`) it is the same gate on the transpiled algebra. The two runs emit
//  BYTE-IDENTICAL output when both pipelines agree, which is what lets the runner diff them.
//
//  Output discipline, inherited from `tests/pure-tier-laws/Program.fs` and unchanged here: every
//  line is ASCII, derived only from COUNTS and fixed vocabulary. Nothing echoes tree content,
//  because the two runtimes do not agree about writing arbitrary text to a terminal, and a probe
//  that reported a console-encoding difference as an algebra divergence would be worse than no
//  probe. Refutations are sanitised and capped at the same seam (`TestSupport.sanitise`).
//
//  ONE SEED for the whole harness, so a refutation names a run anyone can reproduce on either
//  pipeline.
// ============================================================================

open FableLaws.TestSupport

/// Fixed. A seed that moved would make a refutation unreproducible and a green run unfalsifiable.
let private seed = 1488

/// Fable does not wire `[<EntryPoint>] main`'s return value to the process exit status: the
/// emitted `Program.js` calls `main` and drops the result, so `node Program.js` exits 0 whatever
/// the harness concluded. A runner that read only that exit code would be permanently green — the
/// same shape of defect as piping `dotnet fable` through `tail` and reading the pipe's status. So
/// it is set explicitly here, and `fable-check.ps1` ALSO asserts on the `TOTAL violations=` line,
/// so neither signal is the only one.
let private setExitCode (code: int) : unit =
#if FABLE_COMPILER
    Fable.Core.JsInterop.emitJsStatement code "process.exitCode = $0"
#else
    ignore code
#endif

[<EntryPoint>]
let main _ =
    // ---- law 1: the browser merge ----------------------------------------
    let mergeVerdict = Laws.mergeOrderLaws seed 300

    for line in Laws.mergeVerdictLines mergeVerdict do
        printfn "%s" line

    let mergeAdequacy = Laws.mergeAdequacyFailures mergeVerdict

    for line in mergeAdequacy do
        printfn "%s" line

    // ---- law 2: the pinned kit's lane fold, over the tier's witness ------
    let laneResults = Laws.laneFoldResults seed 100

    // The label is the census enrolment name (`Laws.laneFoldFablePort`), so the row in
    // `docs/core-conformance.md`, the code that runs the family, and this gate log all name one
    // thing. Printed identically on both legs — the two outputs are byte-compared.
    for line in renderResults Laws.laneFoldFablePort laneResults do
        printfn "%s" line

    let laneFailures = laneResults |> List.filter (fun r -> not r.Passed) |> List.length

    let violations =
        Laws.mergeViolations mergeVerdict + laneFailures + List.length mergeAdequacy

    printfn "TOTAL violations=%d" violations

    let exitCode = if violations = 0 then 0 else 1
    setExitCode exitCode
    exitCode
