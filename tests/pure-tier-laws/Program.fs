module PureTierLaws.Program

// The pure tier's structural-predicate ALGEBRA, checked law by law over the shared wire-format
// corpus, in a form that is byte-comparable between the .NET run and the Fable-under-node run.
// `./run-pure-tier-laws.ps1` runs it both ways and diffs.
//
// TWO claims, and they are different claims:
//
//  1. THE LAWS HOLD UNDER NODE. `Fuaran.UI.Tests` enumerates these same laws on .NET; the repo's
//     only Fable leg is `-- Catalog`, which is a COMPILE gate and says nothing about behaviour.
//     Between the two there was no evidence at all that the transpiled algebra still obeys
//     idempotence, commutativity, associativity, the identities, involution, complement or de
//     Morgan.
//
//  2. THE TWO PIPELINES AGREE ABOUT THE MATCH SETS. Every line carries the SIZE of each pool
//     predicate's match set per fixture, not merely a law verdict — so a divergence that leaves
//     both sides internally lawful (a collection ordering difference, an arithmetic lowering, a
//     decode that drops a slot on one side) still shows up as a differing line. A law-only probe
//     would report two disagreeing pipelines as two green runs.
//
// Output discipline: every line is ASCII, fixed-width-indexed and derived only from COUNTS and
// fixture NAMES. Nothing echoes fixture CONTENT, because the two runtimes do not agree about
// writing a lone surrogate to a terminal and a probe that reported console encoding as an algebra
// divergence would be worse than no probe. That is the lesson tests/ids-parity-probe records; it
// applies unchanged here.
//
// THE LAWS ARE NOT DEFINED HERE. They live in `Laws.fs` beside this file, which the Expecto suite
// compiles as a linked source too — one definition, two executors. This file is the executor that
// COUNTS: it folds each `Claim` into a per-law claims/violations pair and prints it. What the
// suite does with the same claims is assert them one by one. A law edited in `Laws.fs` therefore
// reddens both, which is exactly what two hand-maintained copies of the laws could not promise —
// and had already stopped delivering, this side having enumerated the cubic laws over five of the
// eleven pool members and checked neither absorption nor distributivity at all.

open Fuaran.UI
open Fuaran.UI.Types

/// One law's verdict over one fixture: how many claims it made, and how many failed.
type private Verdict = { Claims: int; Violations: int }

let private zero = { Claims = 0; Violations = 0 }

let private check (v: Verdict) (holds: bool) : Verdict =
    { Claims = v.Claims + 1
      Violations = v.Violations + (if holds then 0 else 1) }

/// Fold one law's claims over one fixture. The claims stream, so the ~4,700 a fixture now produces
/// are never all live at once.
let private verdict (claims: Laws.Claim seq) : Verdict =
    claims |> Seq.fold (fun v c -> check v (Laws.holds c)) zero

// ── the run ─────────────────────────────────────────────────────────────────

/// Zero-padded to three digits without `sprintf "%03d"` — Fable lowers the format string, and a
/// probe whose INDEX formatting differs between pipelines would report a padding difference as an
/// algebra divergence. Hand-rolled, so both sides run the same arithmetic.
let private pad3 (n: int) : string =
    let s = string n

    if s.Length >= 3 then s
    elif s.Length = 2 then "0" + s
    else "00" + s

[<EntryPoint>]
let main _ =
    let fixtures = Corpus.fixtures |> List.sortBy fst

    // A pool member that matches everywhere, or nowhere, makes every law below a statement about a
    // constant. Counted per predicate across the corpus and printed, so vacuity is visible rather
    // than assumed — the .NET suite asserts the same property and this is its portable half.
    let mutable hitCounts = Laws.pool |> List.map (fun _ -> 0)
    let mutable totalClaims = 0
    let mutable totalViolations = 0
    let mutable decoded = 0

    fixtures
    |> List.iteri (fun i (name, json) ->
        match Generated.decodeNode json with
        | Error _ ->
            // A fixture the pure tier cannot decode is reported, never skipped: both pipelines must
            // agree about WHICH fixtures decode, and a silent skip would hide exactly the
            // divergence this probe exists to find.
            printfn "FIX %s %s DECODE-FAIL" (pad3 i) name
        | Ok t ->
            decoded <- decoded + 1

            let sizes = Laws.pool |> List.map (fun p -> Set.count (Laws.matched p t))

            hitCounts <- List.map2 (fun total size -> total + (if size > 0 then 1 else 0)) hitCounts sizes

            printfn
                "FIX %s %s nodes=%d sets=%s"
                (pad3 i)
                name
                (Set.count (Laws.everyNode t))
                (sizes |> List.map string |> String.concat ",")

            let verdicts = Laws.laws |> List.map (fun (label, law) -> label, verdict (law t))

            for (_, v) in verdicts do
                totalClaims <- totalClaims + v.Claims
                totalViolations <- totalViolations + v.Violations

            printfn
                "LAW %s %s %s"
                (pad3 i)
                name
                (verdicts
                 |> List.map (fun (label, v) -> label + "=" + string v.Claims + "/" + string v.Violations)
                 |> String.concat " "))

    printfn
        "POOL discriminating=%s"
        (hitCounts
         |> List.map (fun hits ->
             if hits = 0 then "never"
             elif hits = decoded then "always"
             else string hits)
         |> String.concat ",")

    printfn
        "TOTAL fixtures=%d decoded=%d claims=%d violations=%d"
        (List.length fixtures)
        decoded
        totalClaims
        totalViolations

    if totalViolations = 0 then 0 else 1
