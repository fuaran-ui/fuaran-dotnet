/// Phase 1553 — gate lanes on ONE `run.ps1`.
///
/// A LANE is a named subset of the gate that a contributor may honestly run before merging, spelled
/// inside the recorded gate COMMAND rather than as a flag a result record drops. Three of them,
/// nested: `pure` ⊂ `fast` ⊂ `full`, with `full` the default and the only lane a release may cite.
///
/// This tier's gate is thirty-one SEPARATE suite processes, not one assembly, so a lane is decided
/// at TWO granularities and the measurement (2026-09-08, recorded in the phase outcome) says which
/// granularity earns which lane:
///
///   * SUITE level, declared in `test-suites.json` (`"lane": "pure" | "slow"`) and read by BOTH
///     entry points, `run.ps1` and `Build.fs`'s `Test` target. This is where `pure` lives: every
///     suite costs a ~1.0s `dotnet run` floor whatever it contains, so a per-commit lane is only
///     seconds-fast if it runs FEWER PROCESSES — filtering inside all thirty-one would cost the
///     floor thirty-one times over and buy nothing.
///
///   * TEST level, the markers in this module, read at each suite's own Expecto entry point via
///     `FUARAN_TEST_LANE`. This is where `slow` lives: fourteen generative/fuzz tests inside
///     `Fuaran.UI.JsonDecode.Tests` consume ~15.4s of a ~73.6s test stage, and no suite-level
///     switch can drop them without also dropping the ~1,420 cheap corpus-conformance assertions
///     beside them, which are exactly what a wire-touching change needs pre-merge.
///
/// A test therefore runs in lane L iff its SUITE admits L and its own MARKER admits L. The default
/// (absent / `full`) filters nothing, so the release gate is byte-identical to the pre-phase run.
module Fuaran.UI.Testing.Lanes

open Expecto

/// The marker label segment. A constant so the runner's filter and the membership tests can never
/// drift from the markers themselves.
///
/// There is exactly ONE marker, and that is the design rather than an omission: `pure` is decided
/// at the SUITE level in `test-suites.json`, so a suite declared pure has every one of its tests in
/// the pure lane by virtue of the declaration. A second `lane-pure` marker would mean a
/// pure-declared suite carrying none of them filtered itself to zero tests and exited 0 — a gate
/// that ran nothing and reported green, which is the one answer a lane must never give. A suite
/// that is mixed is simply not declared pure, and marks its expensive tests slow like any other.
[<Literal>]
let slowLabel = "lane-slow"

type Lane =
    | Full
    | Fast
    | Pure

/// Parse a lane name (the `FUARAN_TEST_LANE` value / the `run.ps1 -Lane` argument). Absent, empty
/// and `full` are the full suite; anything UNRECOGNISED is also the full suite — a typo must fail
/// safe toward running MORE, never toward silently skipping the slow half and reporting green.
let parse (s: string | null) : Lane =
    match s with
    | null -> Full
    | value ->
        match value.Trim().ToLowerInvariant() with
        | "fast" -> Fast
        | "pure" -> Pure
        | _ -> Full

/// The current lane, read once from the environment.
let current: Lane =
    parse (System.Environment.GetEnvironmentVariable "FUARAN_TEST_LANE")

/// Mark a generative / fuzz / process-heavy subtree SLOW: it runs in the full (ship) lane only.
/// Measured 2026-09-08: the four lists carrying this marker are 14 tests (~1% of their suite) and
/// ~15.4s of a ~73.6s test stage.
let slow (t: Test) : Test = testLabel slowLabel t

/// The runner's filter for a lane, over a test's name PARTS (the marker segment IS a name part by
/// construction — `testLabel` prefixes one). Parameterised on the lane so the semantics are
/// unit-testable without touching the process environment.
///
/// `Fast` and `Pure` agree HERE and differ in the roster: within a suite the only question a
/// marker answers is "is this test slow", and which suites run at all is `test-suites.json`'s
/// answer, read by `run.ps1` and `Build.fs`. They are kept as distinct cases so the banner names
/// the lane the operator asked for, and so a future pure-lane-only marker distinction has somewhere
/// to land without re-deriving the vocabulary.
let keepFor (lane: Lane) (parts: string list) : bool =
    match lane with
    | Full -> true
    | Fast
    | Pure -> not (List.contains slowLabel parts)

/// The one place a suite's entry point applies the lane. `full` returns the discovered tree
/// untouched — no filter is constructed at all — so the ship lane cannot differ from the pre-phase
/// run by so much as a traversal.
///
/// A narrow lane that admits NOTHING raises rather than running zero tests and exiting 0. That is
/// not a hypothetical tidy-up: a suite the roster should never have invoked in this lane, or one
/// whose every test acquired a slow marker, would otherwise report the strongest possible green
/// having proved nothing. `run.ps1` and `Build.fs` refuse the same shape one level up, at the
/// roster.
let applyTo (lane: Lane) (tests: Test) : Test =
    match lane with
    | Full -> tests
    | _ ->
        printfn "== test lane: %A (set by FUARAN_TEST_LANE; releases cite the full lane only) ==" lane
        let laned = Test.filter "." (keepFor lane) tests

        if List.isEmpty (Test.toTestCodeList laned) then
            failwithf
                "FUARAN_TEST_LANE=%A admits no test in this suite — it would have run nothing and exited 0. Either the roster invoked a suite this lane does not cover, or every test here is `Lanes.slow`-marked."
                lane

        laned
