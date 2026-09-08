module Fuaran.UI.Tests.LaneTests

// ============================================================================
//  Phase 1553 — the gate lanes' own contract.
//
//  Four things are asserted here, and they are four DIFFERENT claims:
//
//   1. The FILTER does what it says. A slow-marked leaf is absent from `fast`
//      and from `pure`, an unmarked one is present in both, and every leaf is
//      present in `full`. This is the go-red proof — the assertions are written
//      so that deleting `Lanes.slow` from a marked list, or widening `keepFor`,
//      fails here rather than silently making `fast` equal to `full`. It also
//      pins the composition an earlier draft got wrong: `pure` is decided by the
//      ROSTER, so the marker level answers one question only (slow or not), and
//      a narrow lane that admits nothing RAISES rather than exiting 0.
//
//   2. The ROSTER's suite-level declarations are consistent, and — the part that
//      stops `pure` rotting — a suite declared `pure` is measured pure rather
//      than merely asserted so. `pure` means "no filesystem, no corpus", and a
//      source scan is what holds the declaration to it as those suites grow.
//
//   3. The two ENTRY POINTS share one lane vocabulary. `run.ps1` and `Build.fs`
//      each read `test-suites.json`, and a lane spelling recognised by one and
//      not the other would make the FAKE gate and the script gate run different
//      sets — the exact drift `test-suites.json` was introduced to end.
//
//   4. The lane reaches the FABLE STAGE, and the FULL lane never arms its
//      content-addressed skip (Phase 1623, over Phase 1619's mechanism).
//      Those two are what let the declared pre-merge lane name the Fable
//      stage instead of dropping it; see the block at the foot of this file.
// ============================================================================

open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Testing

// ---------------------------------------------------------------------------
//  locating the checkout
// ---------------------------------------------------------------------------

/// The repo root, found by climbing from the test binary. Identified by two files rather than one,
/// so a directory that merely happens to hold a `run.ps1` cannot be mistaken for it. Same shape as
/// `CoreConformanceCensus.repoRoot`, and failing rather than skipping for the same reason: a roster
/// check that cannot read the roster has proved nothing.
let private repoRoot =
    lazy
        (let rec climb (dir: DirectoryInfo option) =
            match dir with
            | None -> None
            | Some d ->
                if
                    File.Exists(Path.Combine(d.FullName, "run.ps1"))
                    && File.Exists(Path.Combine(d.FullName, "Fuaran.sln"))
                then
                    Some d.FullName
                else
                    climb (Option.ofObj d.Parent)

         match climb (Some(DirectoryInfo(System.AppContext.BaseDirectory))) with
         | Some root -> root
         | None ->
             failwith
                 "LaneTests: could not locate the repo root above the test binary — test-suites.json could not be read, so the lane declarations have been checked against nothing.")

// ---------------------------------------------------------------------------
//  the roster
// ---------------------------------------------------------------------------

type private RosterEntry =
    { Project: string
      Lane: string option
      RequiresCorpus: bool }

let private roster =
    lazy
        (let path = Path.Combine(repoRoot.Value, "test-suites.json")
         use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText path)

         [ for entry in doc.RootElement.GetProperty("suites").EnumerateArray() ->
               { Project =
                   // A rostered entry always has a project; both readers fail at startup otherwise,
                   // so the fallback is unreachable rather than lenient.
                   match Option.ofObj (entry.GetProperty("project").GetString()) with
                   | Some p -> p
                   | None -> failwithf "test-suites.json (%s): an entry has a null `project`." path
                 Lane =
                   match entry.TryGetProperty "lane" with
                   | true, lane -> Option.ofObj (lane.GetString())
                   | _ -> None
                 RequiresCorpus =
                   match entry.TryGetProperty "requiresCorpus" with
                   | true, flag -> flag.GetBoolean()
                   | _ -> false } ])

/// The declared lane vocabulary. Both entry points refuse anything else; test 3 below pins that
/// they still agree on the set.
let private recognisedSuiteLanes = set [ "pure"; "slow" ]

/// What "pure" is DECLARED to mean, as a check rather than a promise: a pure suite's own sources
/// name no filesystem or process API. Deliberately a coarse scan — a false positive is a suite
/// leaving `pure`, which costs a second in the per-commit lane, whereas a false negative is a lane
/// that silently stopped being seconds-fast.
let private filesystemApi =
    Regex(@"System\.IO|(?<![A-Za-z0-9_.])File\.|(?<![A-Za-z0-9_.])Directory\.|Process\.Start|__SOURCE_DIRECTORY__")

let private suiteSources (project: string) =
    let full =
        Path.Combine(repoRoot.Value, project.Replace('/', Path.DirectorySeparatorChar))

    match Option.ofObj (Path.GetDirectoryName full) with
    | None -> []
    | Some dir ->
        [ for pattern in [ "*.fs"; "*.cs"; "*.vb" ] do
              yield! Directory.GetFiles(dir, pattern) ]

// ---------------------------------------------------------------------------
//  1. the filter
// ---------------------------------------------------------------------------

/// A synthetic tree rather than the real suite: the claim is about the FILTER, and measuring it
/// against a tree whose membership is stated three lines above the assertion is what makes a
/// regression in `keepFor` unambiguous.
let private probeTree =
    testList
        "probe"
        [ Lanes.slow <| testList "generative" [ test "slow-leaf" { () } ]
          testList "ordinary" [ test "plain-leaf" { () } ] ]

/// A tree whose EVERY test is slow-marked — the shape a narrow lane must refuse rather than run to
/// zero and exit 0.
let private whollySlowTree =
    Lanes.slow <| testList "all-generative" [ test "only-leaf" { () } ]

/// `Test.filter` rather than `Lanes.applyTo`, so the full lane's assertion below is about the tree
/// and not about a banner line: `applyTo Full` is separately asserted to return the tree untouched.
let private leavesIn (lane: Lanes.Lane) =
    probeTree
    |> Test.filter "." (Lanes.keepFor lane)
    |> Test.toTestCodeList
    // `FlatTest.name` is the joined path ("probe.lane-slow.generative.slow-leaf"); the leaf is its
    // last segment, and the marker segments in the middle are what `keepFor` reads.
    |> List.map (fun flat -> (String.concat "." flat.name).Split('.') |> Array.last)
    |> List.sort

[<Tests>]
let filterTests =
    testList
        "Phase 1553 — gate lanes: the filter"
        [ test "full admits every leaf — the ship lane is the whole tree" {
              Expect.equal (leavesIn Lanes.Full) [ "plain-leaf"; "slow-leaf" ] "full runs everything"

              // And `applyTo Full` does not merely admit everything — it returns the discovered tree
              // WITHOUT constructing a filter at all, which is what makes the ship gate byte-identical
              // to the pre-phase run rather than merely equivalent to it.
              Expect.equal
                  (Lanes.applyTo Lanes.Full probeTree |> Test.toTestCodeList |> List.length)
                  (probeTree |> Test.toTestCodeList |> List.length)
                  "applyTo Full is a no-op"
          }

          test "fast drops the slow-marked leaf and nothing else" {
              // The go-red edge: removing `Lanes.slow` from a marked list makes this list contain
              // "slow-leaf", so the marker's absence fails here rather than making `fast` = `full`.
              Expect.equal (leavesIn Lanes.Fast) [ "plain-leaf" ] "fast = full minus lane-slow"

              Expect.isFalse
                  (List.contains "slow-leaf" (leavesIn Lanes.Fast))
                  "the slow-marked test is ABSENT from fast"
          }

          test "pure and fast agree WITHIN a suite — `pure` is decided by the roster, not by a marker" {
              // The composition that matters, and the one an earlier draft of this module got wrong:
              // a suite the roster declares pure carries no per-test marker, so a marker-level `pure`
              // distinction would have filtered every such suite to nothing and exited 0.
              Expect.equal (leavesIn Lanes.Pure) (leavesIn Lanes.Fast) "the marker answers one question: slow or not"
          }

          test "the narrow lanes are STRICT subsets of full" {
              let fast = leavesIn Lanes.Fast
              let full = leavesIn Lanes.Full

              Expect.isTrue (fast |> List.forall (fun l -> List.contains l full)) "fast ⊆ full"
              Expect.isLessThan (List.length fast) (List.length full) "fast is a STRICT subset of full"
          }

          test "a narrow lane that admits NOTHING raises — it never runs zero tests and exits 0" {
              // The strongest possible green from a suite that proved nothing is the one answer a
              // lane must never give, so `applyTo` refuses the shape rather than returning it.
              Expect.throws
                  (fun () -> Lanes.applyTo Lanes.Fast whollySlowTree |> ignore)
                  "an empty narrow lane is refused"

              // ...and the full lane still runs it, because `full` never filters.
              Expect.equal
                  (Lanes.applyTo Lanes.Full whollySlowTree |> Test.toTestCodeList |> List.length)
                  1
                  "full runs the wholly-slow tree"
          }

          test "an unrecognised lane name fails safe toward the FULL suite" {
              // A typo must never silently skip the slow half and report green.
              Expect.equal (Lanes.parse "fastt") Lanes.Full "a typo reads as full"
              Expect.equal (Lanes.parse "") Lanes.Full "empty reads as full"
              Expect.equal (Lanes.parse null) Lanes.Full "absent reads as full"
              Expect.equal (Lanes.parse " FAST ") Lanes.Fast "recognised names are trimmed + case-insensitive"
              Expect.equal (Lanes.parse "pure") Lanes.Pure "pure"
              Expect.equal (Lanes.parse "full") Lanes.Full "full"
          } ]

// ---------------------------------------------------------------------------
//  2. the roster's suite-level declarations
// ---------------------------------------------------------------------------

[<Tests>]
let rosterTests =
    testList
        "Phase 1553 — gate lanes: the roster"
        [ test "every declared lane is one both entry points recognise" {
              let unknown =
                  roster.Value
                  |> List.choose (fun e ->
                      match e.Lane with
                      | Some l when not (recognisedSuiteLanes.Contains l) -> Some(e.Project, l)
                      | _ -> None)

              Expect.isEmpty unknown "an unrecognised `lane` is refused by run.ps1 and Build.fs alike"
          }

          test "the pure lane is non-empty — a lane that admits nothing is refused by both readers" {
              let pureSuites = roster.Value |> List.filter (fun e -> e.Lane = Some "pure")
              Expect.isNonEmpty pureSuites "at least one suite is declared pure"
          }

          test "no suite is declared pure AND corpus-requiring — that is a contradiction" {
              let contradictory =
                  roster.Value
                  |> List.filter (fun e -> e.Lane = Some "pure" && e.RequiresCorpus)
                  |> List.map (fun e -> e.Project)

              Expect.isEmpty contradictory "`pure` means no corpus; `requiresCorpus` means it needs one"
          }

          test "a suite declared pure names no filesystem or process API in its own sources" {
              // This is what stops the `pure` declaration rotting as those suites grow: the lane is
              // a measured property here, not a comment.
              let offenders =
                  [ for e in roster.Value do
                        if e.Lane = Some "pure" then
                            for file in suiteSources e.Project do
                                if filesystemApi.IsMatch(File.ReadAllText file) then
                                    yield sprintf "%s -> %s" e.Project (Path.GetFileName file) ]

              Expect.isEmpty
                  offenders
                  "a pure suite that grew a filesystem dependency must lose its `\"lane\": \"pure\"` declaration"
          }

          test "every rostered suite resolves to a directory that exists" {
              let missing =
                  roster.Value
                  |> List.filter (fun e ->
                      let p =
                          Path.Combine(repoRoot.Value, e.Project.Replace('/', Path.DirectorySeparatorChar))

                      not (File.Exists p))
                  |> List.map (fun e -> e.Project)

              Expect.isEmpty missing "a rostered project that does not exist would be a lane over nothing"
          } ]

// ---------------------------------------------------------------------------
//  3. the two entry points' shared vocabulary
// ---------------------------------------------------------------------------

[<Tests>]
let entryPointTests =
    testList
        "Phase 1553 — gate lanes: the entry points"
        [ test "run.ps1 offers exactly the three lanes, validated at the parameter" {
              let script = File.ReadAllText(Path.Combine(repoRoot.Value, "run.ps1"))

              Expect.isTrue
                  (Regex.IsMatch(script, @"\[ValidateSet\('pure',\s*'fast',\s*'full'\)\]"))
                  "run.ps1 -Lane is ValidateSet-constrained to pure/fast/full"

              Expect.stringContains script "$env:FUARAN_TEST_LANE = $Lane" "the lane reaches the suite processes"
          }

          test "Build.fs recognises the same SUITE lane vocabulary the roster declares" {
              // The FAKE `Test` target is what CI runs. A spelling one reader accepts and the other
              // refuses would put the two gates back on different sets, which is the drift
              // test-suites.json exists to make impossible.
              let build = File.ReadAllText(Path.Combine(repoRoot.Value, "Build.fs"))

              for lane in recognisedSuiteLanes do
                  Expect.stringContains build (sprintf "\"%s\"" lane) (sprintf "Build.fs names the `%s` lane" lane)

              Expect.stringContains build "FUARAN_TEST_LANE" "Build.fs reads the lane from the environment"
          } ]

// ---------------------------------------------------------------------------
//  4. the lane reaches the FABLE STAGE — and the full lane never arms the skip
//     (Phase 1623)
// ---------------------------------------------------------------------------
//
//  Phase 1619 made the Fable stage cheap on an unchanged tree by keying every
//  compile on a content address and letting a NARROW lane skip a match. Phase
//  1623 is what made that reachable from the declared pre-merge lane: the fast
//  lane now names the Fable stage instead of dropping it with `-SkipFable`.
//
//  That turns two facts about the WIRING into load-bearing ones, so they are
//  pinned here in the shape of the entry-point tests above:
//
//    * `run.ps1` forwards the lane into the Fable STAGE, not only into the test
//      suites. Lose that line and the stage silently runs as `full` inside a
//      `fast` invocation — minutes where the declaration promises seconds, and
//      nothing says so.
//
//    * the FULL lane can never arm the skip. That is 1619's whole safety
//      argument: the lane a release cites writes addresses and consults none,
//      so a stale record cannot reach it. Every read that DECIDES a skip is
//      guarded by the narrow-lane allow-list, and the guard is withdrawn
//      outright if the address function's own go-red proof fails.
//
//  Both are source reads, for the same reason the tests above are: the claim is
//  about what the gate is wired to do, and the only artefact that answers it is
//  the script.

let private runScript =
    lazy (File.ReadAllText(Path.Combine(repoRoot.Value, "run.ps1")))

let private fableCheckPath =
    lazy (Path.Combine(repoRoot.Value, "tests", "fable-laws", "fable-check.ps1"))

let private fableCheckSource = lazy (File.ReadAllText fableCheckPath.Value)

let private fableCheckLines = lazy (File.ReadAllLines fableCheckPath.Value)

/// The `-SkipFable`-guarded block in `run.ps1`, from its own `if` to the invocation of the stage
/// script. Failing to locate it FAILS rather than returning an empty region: a pin that cannot find
/// its subject has proved nothing, and an empty string would satisfy every negative assertion below.
let private fableStageBlock =
    lazy
        (let script = runScript.Value
         let start = script.IndexOf("if (-not $SkipFable)", System.StringComparison.Ordinal)
         let finish = script.IndexOf("& $fableCheck", System.StringComparison.Ordinal)

         if start < 0 || finish <= start then
             failwith
                 "LaneTests: run.ps1 no longer holds a `-SkipFable`-guarded block invoking the Fable stage script — the lane-forwarding pin could not be located, which is a failure, not a pass."

         script.Substring(start, finish - start))

/// The `-Addresses` REPORT region of `fable-check.ps1` — it prints each subject's address and record
/// state and then exits, so the record read inside it decides nothing and is deliberately unguarded.
/// Located as a range so the guard scan below excludes exactly it, rather than excluding by a pattern
/// that would also excuse a real skip decision.
let private addressReportRange =
    lazy
        (let lines = fableCheckLines.Value

         match
             lines
             |> Array.tryFindIndex (fun l -> l.TrimStart().StartsWith "if ($Addresses)")
         with
         | None -> None
         | Some start ->
             lines
             |> Array.skip start
             |> Array.tryFindIndex (fun l -> l.Trim() = "exit 0")
             |> Option.map (fun offset -> start, start + offset))

[<Tests>]
let fableStageLaneTests =
    testList
        "Phase 1623 — gate lanes: the Fable stage"
        [ test "run.ps1 forwards the lane INTO the Fable stage, not only into the suites" {
              // The go-red edge: delete the assignment beside `& $fableCheck` and the stage runs as
              // `full` inside a `fast` invocation — minutes where the declaration promises seconds.
              Expect.stringContains
                  fableStageBlock.Value
                  "$env:FUARAN_TEST_LANE = $Lane"
                  "the Fable stage is invoked with the lane in scope"

              // Counted as well as located, so widening the block search above cannot make the
              // assertion pass on the test stage's assignment.
              let assignments =
                  Regex.Matches(runScript.Value, @"\$env:FUARAN_TEST_LANE = \$Lane").Count

              Expect.isTrue (assignments >= 2) "run.ps1 sets the lane for BOTH the suites and the Fable stage"
          }

          test "the Fable stage reads the lane from the SAME variable run.ps1 sets" {
              // One variable, so the stage cannot be handed a lane the rest of the gate did not run
              // under — the reason the stage takes no lane parameter of its own.
              Expect.stringContains
                  fableCheckSource.Value
                  "$env:FUARAN_TEST_LANE"
                  "fable-check.ps1 reads the lane from the environment"
          }

          test "only the NARROW lanes arm the skip — the full lane is absent from the allow-list" {
              let m =
                  Regex.Match(fableCheckSource.Value, @"\$laneMaySkip\s*=\s*\$lane\s+-in\s+@\(([^)]*)\)")

              Expect.isTrue m.Success "fable-check.ps1 decides the skip with a positive lane allow-list"

              let allowed = m.Groups[1].Value
              Expect.stringContains allowed "'pure'" "pure may skip"
              Expect.stringContains allowed "'fast'" "fast may skip"

              // The claim 1619 rests on, as an assertion rather than a comment: the lane a release
              // cites is not in the list, so the skip is unreachable there by construction.
              Expect.isFalse (allowed.Contains "full") "the FULL lane can never arm the skip"
          }

          test "every record read that DECIDES a skip is guarded by that allow-list" {
              let report = addressReportRange.Value
              Expect.isSome report "the -Addresses report region could be located in fable-check.ps1"
              let reportStart, reportEnd = report.Value

              let calls =
                  fableCheckLines.Value
                  |> Array.mapi (fun i line -> i, line)
                  |> Array.filter (fun (i, line) ->
                      line.Contains "Test-RecordedGreen"
                      && not (line.TrimStart().StartsWith "function ")
                      && not (i >= reportStart && i <= reportEnd))

              // Vacuity guard, asserted BEFORE the contents: a rename that emptied `calls` would
              // satisfy the emptiness assertion below while measuring nothing at all.
              Expect.isTrue
                  (calls.Length >= 2)
                  "both addressed subjects — the portability entries and the law harness — consult a record"

              let unguarded =
                  calls
                  |> Array.filter (fun (_, line) -> not (line.Contains "$laneMaySkip"))
                  |> Array.map (fun (i, line) -> sprintf "line %d: %s" (i + 1) (line.Trim()))

              Expect.isEmpty unguarded "a record read outside the -Addresses report must be guarded by $laneMaySkip"
          }

          test "a failed address proof WITHDRAWS skipping rather than reporting it" {
              // The narrow lane runs the address function's own go-red proof before consulting
              // anything. If that proof fails, skipping is turned OFF for the whole run — a broken
              // address function must not be a warning beside a stage that went on skipping.
              Expect.stringContains
                  fableCheckSource.Value
                  "$laneMaySkip = $false"
                  "a failed addressing proof clears the skip for the whole run"
          } ]
