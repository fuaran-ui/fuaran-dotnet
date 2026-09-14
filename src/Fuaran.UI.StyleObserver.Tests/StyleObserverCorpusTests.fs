module Fuaran.UI.StyleObserver.Tests.StyleObserverCorpusTests

// ============================================================================
//  The reference host's certification against the `style-observer/` family —
//  Phase 1752, on the `render-text.json` precedent (Phase 1663).
//
//  This tier EMITS the family, so it would be easy to assume it needs no
//  certification. It does, for two reasons that are different statements:
//
//   * **Staleness.** An emitted artefact is only as current as the last run of
//     the emitter. A change to the derivation that nobody re-emitted leaves the
//     corpus asserting the OLD bytes and every sibling host certifying against
//     them — green everywhere, and wrong everywhere. The first test re-renders
//     the family in memory and requires the committed files to match.
//   * **It makes this a FOUR-host law.** The family binds Python, Go and Rust
//     through their own checkers; this suite is the fourth, so a divergence in
//     the tier that produced the bytes is caught in the tier that produced them.
//
//  The go-red test proves the comparison can fail: a family whose checker cannot
//  go red certifies nothing, and that is the failure mode a byte comparison
//  silently falls into (an absent corpus, an empty enumeration, a skipped list).
// ============================================================================

open System.IO
open Expecto

let private familyDir (corpusRoot: string) =
    Path.Combine(corpusRoot, StyleObserverCorpus.familyDirectory)

[<Tests>]
let tests =
    testList
        "Phase 1752 — the style-observer corpus family"
        [ test "the committed family is byte-identical to a fresh render" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some corpusRoot ->
                  let dir = familyDir corpusRoot

                  Expect.isTrue
                      (Directory.Exists dir)
                      $"the corpus at {corpusRoot} carries no {StyleObserverCorpus.familyDirectory}/ family — \
                        regenerate it with `--emit-style-observer`"

                  // The measurement must not be vacuous: an empty case list would
                  // pass every assertion below while proving nothing at all.
                  Expect.isGreaterThan
                      (List.length StyleObserverCorpus.cases)
                      0
                      "the authored case list is empty, so this suite measured nothing"

                  for case in StyleObserverCorpus.cases do
                      let id = StyleObserverCorpus.caseId case
                      let path = Path.Combine(dir, id + ".json")

                      Expect.isTrue
                          (File.Exists path)
                          $"the family is missing '{id}.json' — regenerate it with `--emit-style-observer`"

                      let committed = File.ReadAllText(path).Replace("\r\n", "\n")
                      let fresh = (StyleObserverCorpus.render case).Replace("\r\n", "\n")

                      Expect.equal
                          committed
                          fresh
                          $"the committed '{id}.json' disagrees with what the reference implementation produces \
                            today. Either the derivation changed and the family was not re-emitted, or it \
                            regressed — run `--emit-style-observer` and read the diff before committing it"

                  // The other direction: a file the authored list no longer
                  // declares is a vector every sibling host is still certifying
                  // against, with nothing here that would notice it.
                  let declared =
                      StyleObserverCorpus.cases
                      |> List.map (fun c -> StyleObserverCorpus.caseId c + ".json")
                      |> Set.ofList

                  let orphans =
                      Directory.GetFiles(dir, "*.json")
                      |> Array.choose (fun f ->
                          match Path.GetFileName f with
                          | null -> None
                          | name when declared.Contains name -> None
                          | name -> Some name)

                  Expect.isEmpty
                      orphans
                      "these files sit in the family but no authored case declares them — the sibling hosts are \
                       certifying against vectors this tier no longer produces"
          }

          test "every case is listed in manifest.json, and nothing else claims the family" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some corpusRoot ->
                  let manifest = File.ReadAllText(Path.Combine(corpusRoot, "manifest.json"))

                  for case in StyleObserverCorpus.cases do
                      let id = StyleObserverCorpus.caseId case
                      let inputFile = $"{StyleObserverCorpus.familyDirectory}/{id}.json"

                      Expect.stringContains
                          manifest
                          inputFile
                          $"manifest.json does not list '{inputFile}'. A fixture nothing lists is a fixture the \
                            hosts never load — they discover the family from the manifest, not from the directory"

                  // The rows are hand-registered (the `teleport-*` precedent: a
                  // family the wholesale emitter does not author keeps its rows
                  // across a regen through Corpus.fs's `preservedRows`). So the
                  // count is the thing that drifts, and this is what notices.
                  let listed =
                      System.Text.RegularExpressions.Regex.Matches(
                          manifest,
                          System.Text.RegularExpressions.Regex.Escape(StyleObserverCorpus.familyDirectory + "/")
                      )
                      |> Seq.length

                  Expect.equal
                      listed
                      (List.length StyleObserverCorpus.cases)
                      "manifest.json lists a different number of style-observer fixtures than the family has cases \
                       — a row was left behind by a rename, or a new case was never registered"
          }

          test "GO-RED — a single flipped byte in a fixture is caught" {
              // The comparison the suite above performs, run against a
              // deliberately perturbed copy. Without this, "every file matched"
              // is indistinguishable from "nothing was compared".
              // NOTE the escaping: the expectations are STRINGS inside the
              // document, so the contrast ratio reads `\"contrastRatio\":21.00`
              // on disk. Perturbing the unescaped spelling matches nothing —
              // which the vacuity check below caught on this test's first run,
              // and is exactly the "a probe that measured nothing" failure.
              let case = List.head StyleObserverCorpus.cases
              let genuine = StyleObserverCorpus.render case
              let perturbed = genuine.Replace("21.00", "21.01")

              Expect.notEqual
                  perturbed
                  genuine
                  "the perturbation changed nothing, so this test proves nothing — the probe, not the subject, is \
                   what failed"

              Expect.notEqual
                  (perturbed.Replace("\r\n", "\n"))
                  (genuine.Replace("\r\n", "\n"))
                  "a flipped expectation byte compared equal to the genuine render"
          } ]
