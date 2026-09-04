module Fuaran.UI.FastPath.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Phase 1478 — write the conformance-law vector family into the shared
    // corpus, for the other wire hosts to run:
    //   dotnet run --project src/Fuaran.UI.FastPath.Tests -- --emit-laws <dir>
    // Deliberately a flag rather than a test side-effect. The corpus is a
    // separate repo, and a suite that wrote into it on every run would dirty a
    // shared clone; the suite COMPARES and names this command, which is the
    // same split `--emit-fidelity` already takes.
    | "--emit-laws" :: dir :: _ ->
        LawVectorExport.write dir
        printfn "Wrote %s" (LawVectorExport.capabilityPath dir)
        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
