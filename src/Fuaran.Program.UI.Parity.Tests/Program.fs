module Fuaran.Program.Parity.Tests.Main

open Expecto
open Fuaran.Program.Parity.Runner
open Fuaran.Program.Parity

/// Regenerate the corpus's driver-semantics scenarios from the seeds.
///
/// Emitting is DELIBERATE and refuses to record a disagreement: the expectation
/// is only written once the placements already agree, because an expectation
/// minted from a divergence would enshrine the bug as the contract. That check
/// is the whole reason this is a mode rather than a build step. It survives the
/// scenarios' move into the corpus unchanged — the home moved, the rule did not.
///
/// It writes the scenario BYTES and nothing else. The corpus's manifest is the
/// authoritative enumeration and belongs to the corpus, so refreshing its
/// digests and step counts is the corpus's own tool's job
/// (`node wire-fixtures/check-scenarios.mjs --write`) rather than a foreign
/// host's. A host that rewrote the index it is certified against would be
/// grading its own paper.
let private emit (root: string) : int =
    let mutable failures = 0

    for seed in Seeds.all do
        match checkFixture seed with
        | Error e ->
            eprintfn "%s: %s" seed.Name e
            failures <- failures + 1
        | Ok(_ :: _ as divergences) ->
            eprintfn "%s: placements disagree — refusing to emit an expectation" seed.Name

            for d in divergences do
                eprintfn "%s" (Divergence.describe d)

            failures <- failures + 1
        | Ok [] ->
            match runClientPlacement seed with
            | Error e ->
                eprintfn "%s: %s" seed.Name e
                failures <- failures + 1
            | Ok observations ->
                FixtureIo.writeTree root seed
                FixtureIo.writeExpectation root seed observations
                printfn "emitted %s (%d step(s))" seed.Name (List.length observations)

    if failures > 0 then
        eprintfn "%d fixture(s) not emitted" failures

    failures

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "--emit-fixtures" :: rest ->
        // Deliberately NOT `ParityTests.fixturesRoot`: touching that module
        // forces its `[<Tests>]` binding, which loads the corpus — and the one
        // moment a regeneration is needed is the moment the corpus does not yet
        // describe what is about to be written.
        let root =
            match rest with
            | path :: _ -> path
            | [] -> FixtureIo.fixturesRoot

        emit root
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
