// SPDX-License-Identifier: Apache-2.0
//
// Emit the conformance corpus for the demanded-effect projection document.
//
//   dotnet build src/Fuaran.Program.Server.UI.Tests
//   dotnet fsi src/Fuaran.Program.Server.UI.Tests/emit-demanded-conformance.fsx src/Fuaran.Program.Server.UI.Tests/conformance/demanded-effect-projection.json
//
// ── What this is ─────────────────────────────────────────────────────────────
//
// A thin wrapper. The corpus — every vector, and what the program tier's own
// pinned reader makes of each — is `DemandedCorpus.emit` in this server UI
// suite (DemandedCorpus.fs beside this file), which carries the full account of
// why the corpus exists. This script only writes it to a file.
//
// The split is Phase 1978's: the corpus used to be computed in a script nothing
// ran, and it fell two document versions and one policy clause behind the
// document it describes without anything noticing. Now `DemandedCorpusTests`
// compares `emit` with the committed file on every gate run, so a document
// change that does not regenerate the vectors is red there.
//
// ── Where the vectors go ─────────────────────────────────────────────────────
//
// The CANONICAL corpus is `conformance/demanded-effect-projection.json` in the
// program repository, beside the codec that is its authority; the file this
// script writes beside it here is a byte copy declared in `copies.json`. Two of
// its vectors are real harvests of a UI program, which only this tier can
// produce, so the emitter moved here with the UI adapters (fuaran#2012): write
// the copy here, then copy it to the program repository. The program
// repository certifies each vector's recorded read against its own decoder.
// It is NOT part of the program wire specification's corpus: that
// specification does not spell the demanded document.
//
// Nothing here names any consumer.

#r "bin/Debug/net10.0/Fuaran.Program.Server.UI.Tests.dll"

let target =
    match fsi.CommandLineArgs |> Array.toList with
    | _ :: path :: _ -> path
    | _ -> failwith "usage: dotnet fsi src/Fuaran.Program.Server.UI.Tests/emit-demanded-conformance.fsx <outputFile>"

let out = Fuaran.Program.Server.Tests.DemandedCorpus.emit ()
System.IO.File.WriteAllText(target, out)

printfn "wrote %d vectors to %s" (List.length Fuaran.Program.Server.Tests.DemandedCorpus.vectors) target
