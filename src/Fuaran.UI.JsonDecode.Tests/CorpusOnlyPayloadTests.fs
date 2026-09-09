module Fuaran.UI.JsonDecode.Tests.CorpusOnlyPayloadTests

// ============================================================================
//  The corpus-only-payload gate — Phase 1647, closing the fuaran#1094 class.
//
//  `--emit-corpus` is WHOLESALE over the directories it owns: it deletes each
//  one and rewrites it from the F# fixture values. A payload authored straight
//  into the corpus and never declared in `Fixtures` / `RejectFixtures` /
//  `LenientFixtures` therefore survives until the next regen and then vanishes,
//  silently, in a commit about something else entirely. It has happened: two
//  Phase-1099 reject vectors (`reject-binding-floatseq-sentinel-case`,
//  `reject-spark-element-nonnumeric`) were authored into the corpus and deleted
//  by successive regens until Phase 1490 adopted them into the F# declarations.
//
//  The gate is the regen itself, run into a THROWAWAY directory: nothing else
//  answers "what would the emitter drop?" without re-deriving the emitter's own
//  file-naming, which is a second source of truth and would drift exactly where
//  it mattered. What the emitter writes there is compared with what the corpus
//  holds here, per owned directory.
//
//  TWO exemptions, and both are declared rather than inferred:
//
//   * Directories the emitter does not own at all (`teleport/`, `laws/`,
//     `chart-lowering/`, `merge-conformance/`, …) are emitted by other tools or
//     authored by hand and are untouched by a regen. Only the directories the
//     emit ACTUALLY WROTE are compared, so this set needs no maintenance: a
//     directory the emitter starts owning joins the comparison automatically.
//   * The nine `teleport/` MANIFEST rows (§12) are dropped from the manifest by
//     a wholesale regen even though their files are not — the known,
//     documented hazard. This test is about FILES, so it does not restate that;
//     the emit-session discipline in the estate hub covers it.
//
//  Cost is one emit into a temp directory, seconds. Skipped where the corpus is
//  absent, like every other corpus-reading assertion here.
// ============================================================================

open System.IO
open Expecto

/// `Path.GetFileName` is `string | null` under F# 10 nullness. Every path here
/// comes from an enumeration and so always has one; the fallback names the whole
/// path rather than an empty label, because a defect report has to attribute.
let private leaf (p: string) : string =
    Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

[<Tests>]
let tests =
    testList
        "Phase 1647 — no corpus-only payload the emitter would drop"
        [ test "every file in an emitter-owned directory is produced by the emitter" {
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some corpusRoot ->
                  let scratch =
                      Path.Combine(Path.GetTempPath(), "fuaran-corpus-drop-probe-" + string (System.Guid.NewGuid()))

                  Directory.CreateDirectory scratch |> ignore

                  try
                      Corpus.emit scratch

                      // The emitter's OWN declaration of which directories it empties, read from the
                      // emitter rather than restated here. Deliberately NOT "every directory the emit
                      // wrote": `validator/` is written into but never cleared, and it also holds
                      // hand-authored cross-host tooling, so a guard over the written-into set would
                      // report the corpus's own scripts as about to be deleted.
                      let owned = Corpus.wholesaleDirectories |> List.toArray |> Array.sort

                      Expect.isGreaterThan
                          owned.Length
                          0
                          "the emit produced no directories at all — the probe measured nothing, which is not a pass"

                      let dropped =
                          [ for dir in owned do
                                let here = Path.Combine(corpusRoot, dir)

                                if Directory.Exists here then
                                    let emitted =
                                        Directory.GetFiles(Path.Combine(scratch, dir)) |> Array.map leaf |> Set.ofArray

                                    for f in Directory.GetFiles here do
                                        let name = leaf f

                                        if not (emitted.Contains name) then
                                            yield dir + "/" + name ]

                      Expect.isEmpty
                          dropped
                          "these corpus payloads are authored into the corpus but declared by no F# fixture, so the next `--emit-corpus` deletes them without saying so (the fuaran#1094 class). Adopt each into Fixtures.fs / RejectFixtures.fs / LenientFixtures.fs, or move it to a directory the emitter does not own"
                  finally
                      try
                          Directory.Delete(scratch, true)
                      with _ ->
                          ()
          } ]
