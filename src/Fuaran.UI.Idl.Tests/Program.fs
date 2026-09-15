module Fuaran.UI.Idl.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // The one sanctioned write path for the committed F* model and proof script
    // (Phase 1754). It is a COMMAND rather than an env-var mode because it writes
    // files no assertion here can repair: `FUARAN_REGEN=1` beside it rewrites the
    // three vocabulary artefacts, and conflating the two would let a regeneration
    // of one quietly rewrite the other.
    | "--emit-fstar" :: _ -> Fuaran.UI.FStarVocabularyTests.emit ()
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
