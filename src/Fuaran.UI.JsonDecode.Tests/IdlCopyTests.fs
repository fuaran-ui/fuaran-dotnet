module Fuaran.UI.JsonDecode.Tests.IdlCopyTests

// ============================================================================
//  The corpus's `idl.json` is a COPY, and this is the guard — Phase 1647.
//
//  `manifest.json` has pointed at `idl.json` since Phase 696 as the corpus's
//  structural source ("what IS the vocabulary?"), and nothing wrote it: a human
//  carried `src/Fuaran.UI.Idl/idl.json` across when they remembered.
//
//  What makes that worse than an ordinary stale copy is which check reads it.
//  The Phase 699 marker-block projection test compares the committed
//  `WIRE_FORMAT.md` against the CORPUS copy — so when the copy is behind, the
//  spec and the copy are CONSISTENTLY behind and `--check-spec` reports "in
//  sync". Phase 1122 watched the §11 Motion enum line keep eight cases through
//  exactly that: two artefacts agreeing with each other about a vocabulary
//  neither of them still described. A guard that compares the two GENERATED
//  projections cannot see it; only a guard against the AUTHORED source can.
//
//  `--emit-corpus` now writes the copy, so the hand-carry has ended. This is the
//  authoring-side gate for the remaining case: a session regenerates the
//  vocabulary (`FUARAN_REGEN=1 …`), commits `idl.json` and `Generated.fs`, and
//  does not run an emit — which is the ordinary shape of a vocabulary change,
//  since the emit is a separate step.
//
//  The `CssCheck` posture, and for the reason that file records: a sibling
//  absent from the checkout is reported as NOT CHECKED rather than passing
//  quietly, because "nothing to check here" must not read as "everything
//  checked".
// ============================================================================

open System.IO
open Expecto

[<Tests>]
let tests =
    testList
        "Phase 1647 — the corpus IDL copy is not hand-carried"
        [ test "the corpus idl.json is byte-identical to the authored vocabulary" {
              match Fuaran.Tests.CorpusRoot.tryRepoRoot (), Fuaran.Tests.CorpusRoot.tryFind () with
              | None, _ -> skiptest "repo root not found from the test binary (Fuaran.sln absent walking up)"
              | _, None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some repoRoot, Some corpusRoot ->
                  let authored = Path.Combine(repoRoot, "src", "Fuaran.UI.Idl", "idl.json")
                  let copy = Path.Combine(corpusRoot, "idl.json")

                  Expect.isTrue (File.Exists authored) (sprintf "the authored vocabulary exists at %s" authored)

                  if not (File.Exists copy) then
                      failwithf
                          "%s is absent — the corpus is present but carries no IDL vocabulary, which the manifest points at. Regenerate with `--emit-corpus`."
                          copy

                  // LF-normalised, because the two live in repos with the same
                  // eol=lf rule but need not be checked out with the same line
                  // endings on every machine. The BYTES that matter are the
                  // content's, and a CRLF checkout is not drift.
                  Expect.equal
                      (File.ReadAllText(copy).Replace("\r\n", "\n"))
                      (File.ReadAllText(authored).Replace("\r\n", "\n"))
                      "the corpus copy of idl.json has drifted from src/Fuaran.UI.Idl/idl.json. It is CO-EMITTED by `--emit-corpus`, so the usual cause is a vocabulary regeneration committed without one. Note that `--check-spec` cannot catch this: it compares WIRE_FORMAT.md against this same stale copy, so both read as in sync."
          } ]
