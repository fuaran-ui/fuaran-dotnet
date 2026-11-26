module Fuaran.UI.Tests.Program

open Expecto
open Fuaran.UI.Testing

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Phase 2767 — write ONLY the derivation-trace law family into a corpus
    // directory (`laws/derivation-trace-laws.json`, WIRE_FORMAT §35):
    //   dotnet run --project src/Fuaran.UI.Tests -- --emit-derivation-trace [<dir>]
    // `<dir>` is the corpus root, resolved as every suite resolves it when
    // omitted. The vectors are drawn from the transform law files already in
    // that corpus, and every expectation is the reference trace's own text.
    | "--emit-derivation-trace" :: rest ->
        let dir =
            match rest with
            | d :: _ -> d
            | [] -> Fuaran.Tests.CorpusRoot.find ()

        printfn "Emitted %s" (Fuaran.UI.Tests.Derivation.DerivationLaws.write dir)
        0
    | _ ->
        // Phase 1553 — the ONE place this suite reads the gate lane. `runTestsInAssemblyWithCLIArgs`
        // discovers and runs in one call, so the lane filter needs the two steps separated: discover the
        // assembly's tests, keep what the lane admits (`Lanes.applyTo`), then run. The full lane (the
        // default, and the only lane a ship may cite) returns the discovered tree untouched, so the ship
        // gate is byte-identical to the pre-phase run.
        match Impl.testFromThisAssembly () with
        | None ->
            eprintfn "no tests discovered in the assembly"
            1
        | Some tests -> runTestsWithCLIArgs [] argv (Lanes.applyTo Lanes.current tests)
