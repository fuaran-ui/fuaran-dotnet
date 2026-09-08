module Fuaran.UI.Tests.LaneTests

// ============================================================================
//  Phase 1553 — the gate lanes' own contract.
//
//  Three things are asserted here, and they are three DIFFERENT claims:
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
