module Fuaran.UI.JsonDecode.Tests.ValidatorCoverageTests

// ============================================================================
//  The stale-emit byte-identity guard — Phase 1647.
//
//  Two artefacts are GENERATED from `PreEmitValidate`'s defect DU: the corpus's
//  `validator/defect-vocabulary.json` and this repo's own
//  `validator-coverage.json`. Both claimed to be checked by construction; only
//  one was written by anything, and neither was compared to a fresh emission.
//  The result was a coverage roster that stopped at FUARAN114 while the
//  vocabulary ran to FUARAN148, and a cross-host gate that went red on `main`
//  for a phase that had done nothing wrong except add a defect case.
//
//  These two tests make the claim true where it is cheapest — in the repo where
//  the defect case was added, on the gate its author already runs — and name the
//  one command that repairs it.
// ============================================================================

open System.IO
open Expecto

/// The regeneration command, quoted verbatim by both failures. One string, so a
/// reader who meets either message is told the same thing.
let private regenCommand =
    "dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-vocabulary"

[<Tests>]
let tests =
    testList
        "Phase 1647 — generated validator artefacts are not stale"
        [ test "validator-coverage.json is byte-identical to a fresh emission" {
              match ValidatorCoverage.tryRepoRoot () with
              | None ->
                  // The assembly has been copied out of the repo. Not a defect
                  // in the artefact, and not something to report as one.
                  skiptest "repo root not found from the test binary (Fuaran.sln absent walking up)"
              | Some repoRoot ->
                  let path = Path.Combine(repoRoot, ValidatorCoverage.fileName)
                  Expect.isTrue (File.Exists path) (sprintf "%s exists at the repo root" path)

                  Expect.equal
                      (File.ReadAllText path)
                      (ValidatorCoverage.toJson ())
                      (sprintf
                          "%s is stale or hand-edited. It is GENERATED from the defect DU — regenerate with `%s`, and commit it with the change that moved the vocabulary."
                          ValidatorCoverage.fileName
                          regenCommand)
          }

          test "the corpus defect-vocabulary.json is byte-identical to a fresh emission" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some corpusRoot ->
                  let path = Path.Combine(corpusRoot, "validator", "defect-vocabulary.json")

                  if not (File.Exists path) then
                      failwithf "%s is absent — the corpus is present but carries no defect vocabulary." path

                  Expect.equal
                      (File.ReadAllText(path).Replace("\r\n", "\n"))
                      (DefectVocabulary.toJson ())
                      (sprintf
                          "the corpus's validator/defect-vocabulary.json is behind this host's defect DU. Regenerate with `%s` and commit the corpus change in the SAME change-set (WIRE_FORMAT §11)."
                          regenCommand)
          } ]
