/// Phase 1878 — what each ADOPTED law family of the pinned conformance kit ran in THIS process,
/// for the census's `Cases` column (roadmap-engine#619: on an adopted row, the number of cases the
/// family ran over in the run that regenerated the census, in the kit's `caseState` grammar; `—`
/// elsewhere).
///
/// An adopting test does not call its family directly. It reads the shared lazy this module hands
/// back from a MODULE-LEVEL `let` in `CoreAdoptionTests.fs` — forced there, not inside a `testCase`
/// closure, because Expecto does not promise test-case order and the census's own test is one case
/// among many. F# initialises a module's top-level bindings in declaration order before any of them
/// is called from outside, so registration is complete before the census forces anything, whichever
/// case Expecto happens to run first.
///
/// **This process is `Fuaran.UI.Tests` only.** Most of this repo's `Adopted` rows run in a
/// DIFFERENT test project's own process (`Fuaran.UI.OpStream.Tests`, `.OpStream.Dag.Tests`,
/// `.ServerDriven.Tests`, `.FastPath.Tests`) — a fact this module cannot see and does not pretend
/// to: those rows read `—` from `CoreConformanceCensus.render`, the same mark `NotUsed` /
/// `SiblingHost` / `CarriedBy` already use for "not measured by this run", never a fabricated count.
/// Extending measurement into those four projects is out of this phase's declared key files and is
/// left as a named follow-on (see the census's own committed prose).
///
/// **Several rows share one adopting run.** `Conformance.certify` answers for `witnessLaws`,
/// `diffLaws`, `streamLaws` and `opAlgebra` in one call; `certifyStream` answers for `reducer` (and,
/// via its Detail column, `streamLaws` a second time). `adopt` therefore takes the LIST of family
/// keys one run answers for, rather than one key — a genuine need this repo's aggregate entry
/// points create and Core's own kit does not otherwise force on an adopter, so it earns its keep
/// here rather than being invented against a single call site.
module Fuaran.UI.Tests.LawCases

open System.Collections.Concurrent
open Fuaran.Core

/// One registered run: the iteration count it was driven over and the results it answered with.
type Run =
    { Iterations: int
      Results: Lazy<LawResult list> }

let private runs = ConcurrentDictionary<string, Run list>()

/// Register the adopting run of every family in `families` (each a census key, `"<Module>.<fn>"`),
/// driven over `iterations`. `run` receives that same count, so the figure the census multiplies by
/// cannot drift from the one the family was actually driven over. All families in the list share
/// the SAME lazy, so `run` executes at most once however many of them the census later forces.
/// Returns that lazy for the adopting test(s) to force.
let adopt (families: string list) (iterations: int) (run: int -> LawResult list) : Lazy<LawResult list> =
    let results = lazy (run iterations)

    let entry =
        { Iterations = iterations
          Results = results }

    for family in families do
        runs.AddOrUpdate(family, [ entry ], (fun _ existing -> existing @ [ entry ]))
        |> ignore

    results

/// The runs registered for `family`, in registration order; empty when none is.
let runsOf (family: string) : Run list =
    match runs.TryGetValue family with
    | true, rs -> rs
    | _ -> []

/// Every family with a registered run, sorted.
let families () : string list = runs.Keys |> Seq.sort |> List.ofSeq

/// The kit's adequacy-guard law prefix (`SampleAdequacy.guardOpening` from Fuaran.Core; the pinned
/// kit names its guard laws the same way but does not export the literal to a consumer).
let private guardOpening = "sample adequacy ("

/// The `Cases` cell for one family, in the kit's `caseState` grammar: subject laws (every law that
/// is not the adequacy guard's own) times iterations, summed over every registered run for that
/// family; `vacuous (<dimension>; …)` when a guard law went red — a side of the family the sample
/// never reached — and `vacuous` when the run made no assertion. A family with no registered run is
/// `unmeasured`: ungraded, never a pass. Deliberately the same shape Core's own
/// `SampleAdequacy.cases` renders, so a reader who has seen one census reads this one for free.
let cell (family: string) : string =
    match runsOf family with
    | [] -> "unmeasured"
    | rs ->
        let isGuard (r: LawResult) =
            r.Law.StartsWith(guardOpening, System.StringComparison.Ordinal)

        let dimension (law: string) =
            match law.IndexOf "): " with
            | at when at >= 0 -> law.Substring(at + 3)
            | _ -> law

        let measured =
            rs
            |> List.map (fun r ->
                let results = r.Results.Force()
                let subject = results |> List.filter (isGuard >> not) |> List.length

                let starved =
                    results
                    |> List.filter (fun l -> isGuard l && not l.Passed)
                    |> List.map (fun l -> dimension l.Law)

                subject * max 0 r.Iterations, starved)

        let cases = measured |> List.sumBy fst
        let starved = measured |> List.collect snd |> List.distinct

        if not (List.isEmpty starved) then
            "vacuous ("
            + (starved
               |> List.map (fun s -> s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "))
               |> String.concat "; ")
            + ")"
        elif cases <= 0 then
            "vacuous"
        else
            string cases
