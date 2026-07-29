module Fuaran.UI.JsonDecode.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Corpus generator. Regenerate the workspace-root
    // wire-format-fixtures/ corpus from the F# fixture values:
    //   dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus <dir>
    | "--emit-corpus" :: dir :: _ ->
        Corpus.emit dir
        0
    // Phase 101 cross-host fuzz-sample exchange. `--emit-fuzz-samples <dir>
    // <count>` writes F#-canonical generated samples; `--check-fuzz-samples
    // <dir> [host]` validates the <host>-canonical samples that host's emitter
    // wrote to <dir>/<host>/. `host` defaults to `typescript` (the Phase 101
    // leg the cross-host runner drives); `python` is the Phase 236 exchange.
    | "--emit-fuzz-samples" :: dir :: countStr :: _ -> FuzzSamples.emit dir (int countStr)
    | "--check-fuzz-samples" :: dir :: host :: _ -> FuzzSamples.check dir host
    | "--check-fuzz-samples" :: dir :: _ -> FuzzSamples.check dir "typescript"
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
