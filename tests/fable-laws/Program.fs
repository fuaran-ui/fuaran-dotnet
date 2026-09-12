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

    // ---- law 3: the raw-DEFLATE inflater over a foreign dynamic-Huffman stream ----
    // The block type no host of ours emits and every standard deflater does. Its .NET
    // conformance cross-check sits behind `#if !FABLE_COMPILER` (`System.IO.Compression`
    // does not transpile), so until this line the browser's inflater was uncertified on
    // exactly the input every foreign share link carries.
    let deflateCases = Laws.deflateCases ()

    for line in Laws.deflateLines deflateCases do
        printfn "%s" line

    // ---- law 4: the selection-field projection, on both pipelines ----
    // The one claim in this repo that is ABOUT two runtimes: a text cell in a
    // numeric slot raised on .NET and rendered NaN in the browser, and neither
    // pipeline's own suite could see the other's answer.
    let selectionCases = Laws.selectionFieldCases ()

    for line in Laws.selectionFieldLines selectionCases do
        printfn "%s" line

    // ---- law 5: a Format.Date slot never throws, on either pipeline ----
    let dateCases = Laws.dateSentinelCases ()

    for line in Laws.dateSentinelLines dateCases do
        printfn "%s" line

    // ---- law 6: the DAG checkpoint's two refusals, under Node ----
    // The de-fenced DAG stack's Fable-cleanliness was proved once from a
    // scratchpad harness that persisted nothing. The COMPILE half is covered by
    // the derived portability set; this is the RUN half, and it is here rather
    // than in a second entry project because that measurement was made first
    // (see the law's header).
    let checkpointCases = Laws.checkpointCases ()

    for line in Laws.checkpointLines checkpointCases do
        printfn "%s" line

    let violations =
        Laws.mergeViolations mergeVerdict
        + laneFailures
        + List.length mergeAdequacy
        + Laws.deflateViolations deflateCases
        + Laws.selectionFieldViolations selectionCases
        + Laws.dateSentinelViolations dateCases
        + Laws.checkpointViolations checkpointCases

    printfn "TOTAL violations=%d" violations

    let exitCode = if violations = 0 then 0 else 1
    setExitCode exitCode
    exitCode
