module Fuaran.UI.Program.Server.Tests.DemandedCorpusTests

// ─── The demanded corpus is regenerated or the gate is red (Phase 1978) ───
//
// `DemandedCorpus.emit` is what `emit-demanded-conformance.fsx` beside this file
// writes, and `conformance/demanded-effect-projection.json` beside it is what was
// committed (a byte copy of the program repository's canonical file). The
// emitter fell two document versions and one clause behind the document while
// nothing ran it; these tests are what runs it now.
//
//  1. THE COMMITTED VECTORS ARE THE EMITTED ONES, byte for byte. A change to
//     the document's shape, version or reader that does not regenerate the
//     corpus fails here, naming the first stale vector's line.
//
//  2. EVERY LITERAL VECTOR IS AT THE CURRENT VERSION, or deliberately not. The
//     staleness this phase repaired was literal documents left at an old
//     version, every one of which the reader then refused for the wrong reason.
//
//  3. THE COMPARISON CAN GO RED, and names the vector when it does — so 1 is a
//     measurement rather than a comparison of a string with itself.

open System.IO
open Expecto
open Fuaran.Program.Bounded
open Fuaran.UI.Program.Server.Tests

let private regenerate =
    "regenerate with `dotnet fsi src/Fuaran.UI.Program.Server.Tests/emit-demanded-conformance.fsx src/Fuaran.UI.Program.Server.Tests/conformance/demanded-effect-projection.json` after a build, then copy the file to the program repository's conformance/ directory (the canonical copy)"

[<Tests>]
let tests =
    testList
        "demanded corpus (Phase 1978)"
        [ test "the committed vectors are the emitted ones" {
              Expect.isTrue
                  (File.Exists DemandedCorpus.committedPath)
                  $"the corpus is committed at {DemandedCorpus.committedPath}"

              let committed = File.ReadAllText DemandedCorpus.committedPath
              let emitted = DemandedCorpus.emit ()

              match DemandedCorpus.firstDifference committed emitted with
              | None -> ()
              | Some(line, expected, actual) ->
                  failtestf
                      "the committed corpus is stale at line %d — %s\n  committed: %s\n  emitted:   %s"
                      line
                      regenerate
                      expected
                      actual
          }

          test "every literal vector is written at the version the reader reads, except the two that test the version" {
              let versionTagged =
                  DemandedCorpus.vectors
                  |> List.filter (fun (_, document, _) -> document.Contains "\"version\":")

              Expect.isNonEmpty versionTagged "the corpus carries versioned documents"

              let current = $"\"version\":{Demanded.Version},"

              let off =
                  versionTagged
                  |> List.filter (fun (_, document, _) -> not (document.Contains current))
                  |> List.map (fun (id, _, _) -> id)
                  |> List.sort

              Expect.equal off [ "previous-version"; "unknown-version" ] "only the version vectors leave the version"
              Expect.equal Demanded.decodableVersions [ Demanded.Version ] "the reader reads one version"
          }

          test "a stale vector is caught, and named by its line" {
              let emitted = DemandedCorpus.emit ()
              let stale = emitted.Replace("\"id\":\"op-reach\"", "\"id\":\"op-reach-stale\"")

              Expect.notEqual stale emitted "the perturbation moved a byte"

              match DemandedCorpus.firstDifference stale emitted with
              | None -> failtest "a stale vector compared equal"
              | Some(_, expected, _) ->
                  Expect.stringContains expected "op-reach-stale" "the difference is the perturbed vector's own line"

              Expect.isNone (DemandedCorpus.firstDifference emitted emitted) "a corpus agrees with itself"
          }

          test "the richest vector is a real harvest carrying reach, replay and undo" {
              let _, harvested, _ =
                  DemandedCorpus.vectors |> List.find (fun (id, _, _) -> id = "harvest-full")

              match Demanded.decode harvested with
              | Error failure -> failtestf "the harvest does not decode: %A" failure
              | Ok projection ->
                  let tier =
                      match projection.Server with
                      | Some t -> t
                      | None -> failtest "the harvest walked the server tier"

                  Expect.isNonEmpty tier.Reach "its op names a node"
                  Expect.isNonEmpty tier.Replay "a replay posture"
                  Expect.isNonEmpty tier.Undo "an undo posture"
          } ]
