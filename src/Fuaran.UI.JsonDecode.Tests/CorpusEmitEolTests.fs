module Fuaran.UI.JsonDecode.Tests.CorpusEmitEolTests

// ============================================================================
//  `--emit-corpus` writes LF bytes, on every platform.
//
//  The corpus repo's `.gitattributes` pins `* text=auto eol=lf`, which is why
//  this class of defect is so quiet: a regen that wrote CRLF into the working
//  tree was normalised on commit, so `git status` stayed CLEAN throughout —
//  while every consumer that byte-compares the WORKING TREE (fuaran-py's
//  `test_snapshot_top_files_byte_identical`, fuaran-ts's bundled snapshot
//  check, any future byte comparison) read the CRLF and failed, unable to fix
//  it from their side because the authority itself was the CRLF copy. Observed
//  2026-07-31: a local `manifest.json` carrying 2,133 CRs, identical to origin
//  modulo CR.
//
//  The attributes cannot fix this class — only the writer can, and the writers
//  do (`Corpus.writeManifest` and `RenderFidelityArtifact.write` both pass
//  `NewLine = "\n"`, since `Utf8JsonWriter` otherwise indents with
//  `Environment.NewLine`). What did not exist until this test is anything that
//  PINS it: a new emitted artefact added without that option reintroduces the
//  defect silently, and the corpus is a shared repo, so the failure surfaces in
//  someone else's session with no way to attribute it.
//
//  So: emit the whole corpus to a TEMP directory and assert the bytes. A temp
//  directory deliberately — a regen of the real `../wire-format-fixtures/`
//  clone is a separate ceremony with its own discipline (WIRE_FORMAT.md §11),
//  and a test must never perform one as a side effect.
// ============================================================================

open System.IO
open Expecto

[<Tests>]
let corpusEmitLineEndings =
    testList
        "Corpus --emit-corpus — line endings"
        [ testCase "every emitted file carries LF bytes only" (fun () ->
              let dir =
                  Path.Combine(Path.GetTempPath(), "fuaran-corpus-eol-" + Path.GetRandomFileName())

              Directory.CreateDirectory dir |> ignore

              try
                  Corpus.emit dir

                  let emitted =
                      Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) |> List.ofSeq

                  // The probe must be capable of seeing something. An emit that
                  // silently produced nothing would otherwise pass this test
                  // vacuously — the exact shape of green that hides a defect.
                  Expect.isGreaterThan
                      (List.length emitted)
                      100
                      "the emit produced almost nothing — the byte assertion below would pass vacuously"

                  let offenders =
                      emitted
                      |> List.choose (fun p ->
                          let crs = File.ReadAllBytes p |> Array.filter (fun b -> b = 13uy) |> Array.length

                          if crs > 0 then
                              Some(Path.GetRelativePath(dir, p), crs)
                          else
                              None)

                  Expect.isEmpty
                      offenders
                      (sprintf
                          "--emit-corpus wrote CR bytes. Git's eol=lf normalisation hides this from `git status`, so it surfaces only in a consumer that byte-compares the working tree. A new emitted artefact needs its writer pinned to LF (Utf8JsonWriter: NewLine = \"\\n\"). Offenders (file, CR count): %A"
                          offenders)
              finally
                  try
                      Directory.Delete(dir, true)
                  with _ ->
                      ()) ]
