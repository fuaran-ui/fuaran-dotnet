module Fuaran.UI.StyleObserver.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Phase 1752 — write the `style-observer/` conformance family into a corpus
    // directory:
    //   dotnet run --project src/Fuaran.UI.StyleObserver.Tests -- --emit-style-observer [<dir>]
    // Deliberately NOT part of `--emit-corpus`: that emitter is wholesale over
    // the directories it owns, and this family is not one of them (the
    // teleport/ and laws/ precedent), so a regen can never delete it. `<dir>` is
    // the corpus root and is optional, resolving through the one resolver every
    // corpus-reading suite uses (Phase 1647). The writer PROVES every vector
    // against the reference derivation before writing, so this cannot publish a
    // claim the reference host does not meet.
    | "--emit-style-observer" :: rest ->
        let dir =
            match rest with
            | d :: _ -> d
            | [] -> Fuaran.Tests.CorpusRoot.find ()

        let ids = StyleObserverCorpus.write dir

        printfn "Emitted %d %s/ vectors to %s" (List.length ids) StyleObserverCorpus.familyDirectory dir

        // The rows are hand-registered in manifest.json (a family the wholesale
        // emitter does not author keeps its rows across a regen, through
        // Corpus.fs's `preservedRows`). Print them, so a new case is registered
        // by copy rather than by recall.
        printfn "manifest.json rows for this family:"

        for case in StyleObserverCorpus.cases do
            printfn "    %s," (StyleObserverCorpus.manifestRow case)

        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
