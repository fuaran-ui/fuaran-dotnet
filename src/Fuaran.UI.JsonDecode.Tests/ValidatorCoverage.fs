module Fuaran.UI.JsonDecode.Tests.ValidatorCoverage

// ============================================================================
//  `validator-coverage.json`, GENERATED — Phase 1647.
//
//  The file said of itself that "adding a defect case adds it here and to the
//  vocabulary in the same build" and that it was "checked by construction".
//  Neither was true: nothing wrote it, and its `implemented` list had stopped at
//  FUARAN114 while the vocabulary ran past FUARAN148. A file that CLAIMS to be
//  generated and is not is worse than one that admits it is hand-kept, because
//  the claim is what stops a reader checking — and it is how Phase 1076's
//  FUARAN108 entered the vocabulary without a coverage row and left the
//  cross-host gate red on `main` for someone else to find.
//
//  So the whole artefact is emitted here, from the same `DefectVocabulary`
//  reflection the corpus vocabulary is emitted from. That includes the PROSE
//  fields: they are judgements rather than derivations, but a file that is
//  half-generated has to be merged by hand on every regen, and the half nobody
//  regenerates is exactly the half that goes stale. They live here, beside the
//  derivation they describe, and `--emit-vocabulary` writes the file whole.
//
//  The byte-identity guard beside it (`ValidatorCoverageTests`) is what makes
//  the claim TRUE rather than merely repeated: a defect case added without a
//  regen fails the gate in the repo where it was added, naming the command.
// ============================================================================

open System
open System.IO
open System.Text

/// The repo root, via the shared resolver beside the corpus one (Phase 1647).
let tryRepoRoot () : string option = Fuaran.Tests.CorpusRoot.tryRepoRoot ()

[<Literal>]
let fileName = "validator-coverage.json"

/// Every code the reference host's pre-emit validator can raise, ordinally
/// sorted — derived, never listed.
let implementedCodes () : string list =
    DefectVocabulary.entries ()
    |> List.map _.Code
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ── The authored prose ───────────────────────────────────────────────────────
//
// Kept verbatim from the hand-maintained file this replaces, except that the
// `postureReason` no longer asserts a mechanism that did not exist.

[<Literal>]
let private postureReason =
    "The reference host. The vocabulary is GENERATED from this host's own `PreEmitValidate.describe`, and since Phase 1647 so is this declaration — both are emitted by `--emit-vocabulary`, and a byte-identity gate fails the build when either is stale, so `implemented` cannot drift from the vocabulary it claims to equal."

[<Literal>]
let private abstentionDefault =
    "Not applicable — the reference implements the whole vocabulary by definition."

[<Literal>]
let private otherFamiliesNote =
    "The build-time source-AST walker's codes live in a separate project on this host and are a separate family; they are deliberately not listed as pre-emit coverage."

/// Phase 1692. The corpus's citation arm asks every host to account for each
/// FUARAN code its SOURCE names, and a sibling host answers by listing them in
/// `otherFamilies`. This host cannot answer that way and must not pretend to: it
/// is the host the other two registries LIVE in — the build-time source-AST
/// walker and the Roslyn analyzer descriptors — and `scripts/fuaran-codes.ps1`
/// already derives all three from source, collision-checks them, and runs in
/// this repo's own gate. Re-deriving them here to write a list into this file
/// would be a second derivation of one fact, and the copy nobody regenerates is
/// the copy that goes stale — the exact defect the header above records this
/// whole artefact being written to close. So the declaration POINTS at the
/// registry instead, and the corpus arm records the row as `own-registry`.
/// The route is available to the REFERENCE posture only: a subset host owns no
/// registry, so for it the pointer would be an opt-out rather than an answer.
[<Literal>]
let private otherFamiliesSource =
    "scripts/fuaran-codes.ps1 — this host owns the other two FUARAN registries (the build-time source-AST walker and the analyzer descriptors). That script derives all three from source, refuses a code claimed by two registries for different rules, and runs in this repo's gate. Enumerating them here would be a second derivation of the same fact."

[<Literal>]
let private machineCheckedNote =
    "Checked by construction: the vocabulary is derived from this host, so `implemented` is the vocabulary. The gate asserts the equality rather than assuming it, which is what catches a hand-edit of this file."

/// The emitted artefact. Hand-written rather than routed through a JSON library
/// for the same reason `DefectVocabulary.toJson` is: the output shape is pinned
/// here and cannot move under a dependency bump.
let toJson () : string =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    let esc (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")

    line "{"
    line "  \"version\": 1,"
    line "  \"host\": \"fuaran-dotnet\","
    line "  \"family\": \"pre-emit\","
    line "  \"vocabulary\": \"wire-format-fixtures/validator/defect-vocabulary.json\","
    line "  \"posture\": \"reference\","
    line (sprintf "  \"postureReason\": \"%s\"," (esc postureReason))
    line "  \"implemented\": ["
    let codes = implementedCodes ()

    codes
    |> List.iteri (fun i c ->
        let comma = if i = codes.Length - 1 then "" else ","
        line (sprintf "    \"%s\"%s" (esc c) comma))

    line "  ],"
    line (sprintf "  \"abstentionDefault\": \"%s\"," (esc abstentionDefault))
    line "  \"abstained\": {},"
    line "  \"otherFamilies\": {},"
    line (sprintf "  \"otherFamiliesSource\": \"%s\"," (esc otherFamiliesSource))
    line (sprintf "  \"otherFamiliesNote\": \"%s\"," (esc otherFamiliesNote))
    line "  \"machineChecked\": true,"
    line (sprintf "  \"machineCheckedNote\": \"%s\"" (esc machineCheckedNote))
    sb.Append("}\n") |> ignore
    sb.ToString()

/// Write the declaration into a repo root. LF on every platform, matching the
/// corpus emitter — a CR here would be invisible to `git status` under the
/// repo's `eol=lf` normalisation and visible only to a byte-comparing consumer.
let write (repoRoot: string) : string =
    let path = Path.Combine(repoRoot, fileName)
    File.WriteAllText(path, toJson ())
    path
