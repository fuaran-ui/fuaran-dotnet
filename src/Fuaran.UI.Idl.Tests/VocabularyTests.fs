module Fuaran.UI.VocabularyTests

#nowarn "3261" // DirectoryInfo.Parent is legitimately nullable here.

// ============================================================================
//  Phase 1181 — the regeneration triple, in-process.
//
//  This repo owns its wire vocabulary now (Fuaran-Core DECISIONS D14, this
//  repo's docs/DECISIONS.md D5: a domain's vocabulary lives in the domain's own
//  repo). What that ownership has to MEAN, to be worth anything, is that the
//  domain can regenerate its structural layer from its own files against the
//  packaged engine — no sibling checkout, no hand-run byte-copy, nothing read
//  from outside this repository.
//
//  The triple is three files this repo holds:
//
//    1. the VOCABULARY     — `src/Fuaran.UI.Idl/idl.json`
//    2. the SUPPORT record — `src/Fuaran.UI.Idl/support.json`
//    3. the HOST PRELUDE   — `src/Fuaran.UI/HostPrelude.fs`, NAMED by (2) rather
//                            than inlined into it: the generator never reads the
//                            prelude's text, and a copy of compiled source inside
//                            a JSON document would be a drift hazard manufactured
//                            to satisfy the word "data".
//
//  and the emission is `src/Fuaran.UI/Generated.fs`.
//
//  THE LOAD-BEARING DETAIL: the check reads (1) and (2) FROM THE COMMITTED BYTES,
//  not from the F# values beside them. Emitting from the in-memory `uiIdl` would
//  prove only that the generator is a function; it would say nothing about whether
//  the artifacts a second implementation (or a second language, or a reviewer
//  reading a diff) would consume actually describe the same vocabulary. Rendering
//  and parsing are asserted to be an inverse pair over the real ~41-kind
//  vocabulary here, which is the scale at which a gap would show.
//
//  `FUARAN_REGEN=1` rewrites all three artifacts instead of asserting, so a
//  deliberate vocabulary change is one command rather than a hand-edit of
//  generated code.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core.Idl

// ─── Locating the repo's own files ─────────────────────────────────────────
//
// Climb from the test binary to the checkout root (the directory holding
// `Fuaran.sln`). Everything read below is inside THIS repository, which is the
// whole point of the phase — the previous guard resolved a sibling checkout at
// `../../../Fuaran-Core` and was silently a no-op wherever that was absent.

let private repoRoot: string =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            if File.Exists(Path.Combine(d.FullName, "Fuaran.sln")) then
                Some d.FullName
            else
                climb (Option.ofObj d.Parent)

    match climb (Some(DirectoryInfo AppContext.BaseDirectory)) with
    | Some root -> root
    // FAIL, never skip. A conformance check that goes green without its inputs is
    // worse than no check — that is exactly how the retired byte-copy guard could
    // report success on a machine where it compared nothing at all.
    | None ->
        failwithf
            "could not locate the repository root (no Fuaran.sln above %s) — the regeneration triple lives in this repo and must be readable"
            AppContext.BaseDirectory

let private path (parts: string list) =
    Path.Combine(Array.ofList (repoRoot :: parts))

let private idlPath = path [ "src"; "Fuaran.UI.Idl"; "idl.json" ]
let private supportPath = path [ "src"; "Fuaran.UI.Idl"; "support.json" ]
let private generatedPath = path [ "src"; "Fuaran.UI"; "Generated.fs" ]

let private regenerating = Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1"

/// Read a committed artifact exactly as it sits on disk. No trimming and no
/// newline normalisation: these are byte comparisons, and `.gitattributes` pins
/// LF in the working tree, so what is read is what is committed.
let private read (p: string) : string =
    if File.Exists p then File.ReadAllText p else ""

/// The one write path. Deliberately the only one — a check that can repair what
/// it is checking, without being asked, is the failure mode the retired sync
/// script's own header records (a bare run once erased 292 lines).
let private regenerate (p: string) (content: string) =
    File.WriteAllText(p, content)
    printfn "FUARAN_REGEN=1 — rewrote %s" p

let private expectArtifact (name: string) (p: string) (rendered: string) =
    if regenerating then
        regenerate p rendered
    else
        Expect.equal
            (read p)
            rendered
            (sprintf
                "%s is not what this repo's vocabulary sources render. Regenerate with FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests"
                name)

// ─── The emission ──────────────────────────────────────────────────────────

/// The generated module's name is a generator PARAMETER, which is why the tier's
/// copy and Core's certification snapshot were ever byte-identical modulo one
/// line. Here it is simply the name this repo compiles.
[<Literal>]
let private generatedModule = "Fuaran.UI.Generated"

let private emit (support: Gen.GenSupport) (idl: Idl) =
    match Gen.fsharpModuleWith support generatedModule idl (idl.Kinds |> List.map _.Tag) with
    | Ok s -> s
    | Error e -> failtestf "codegen rejected the UI vocabulary: %A" e

// SEQUENCED, and not as a default: the cases below share the three artifact files,
// and under `FUARAN_REGEN=1` the later ones READ what the earlier ones WRITE. Run in
// parallel, the regeneration of `Generated.fs` raced the write of `idl.json` and read
// an empty file — a failure that only ever appears on the regeneration path, i.e. the
// path taken by whoever is least able to tell it from a real vocabulary error.
[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 1181 — the vocabulary regenerates in-process"
        [ testCase "idl.json is what this repo's vocabulary renders" (fun _ ->
              expectArtifact "src/Fuaran.UI.Idl/idl.json" idlPath (Artifact.render Fuaran.UI.Vocabulary.uiIdl))

          testCase "support.json is what this repo's support document renders" (fun _ ->
              expectArtifact
                  "src/Fuaran.UI.Idl/support.json"
                  supportPath
                  (SupportArtifact.render Fuaran.UI.VocabularySupport.document))

          // The round-trip law, stated over the real vocabulary rather than a
          // reference one. It is asserted separately from the emission below
          // because the two failures mean different things: a parse that loses a
          // field is a codec defect, while an emission mismatch with a clean
          // round-trip is a generator or vocabulary change.
          testCase "the committed artifacts parse back to the canonical vocabulary" (fun _ ->
              match Artifact.parse (read idlPath) with
              | Error m -> failtestf "idl.json did not parse: %s" m
              | Ok parsed ->
                  Expect.equal
                      (Artifact.render parsed)
                      (Artifact.render (Artifact.canonicalise Fuaran.UI.Vocabulary.uiIdl))
                      "idl.json does not round-trip to the canonicalised vocabulary"

              match SupportArtifact.parse (read supportPath) with
              | Error m -> failtestf "support.json did not parse: %s" m
              | Ok doc ->
                  Expect.equal
                      (SupportArtifact.render doc)
                      (SupportArtifact.render Fuaran.UI.VocabularySupport.document)
                      "support.json does not round-trip")

          testCase "Generated.fs is what the committed triple regenerates" (fun _ ->
              // FROM BYTES — see the header. The parsed vocabulary is canonicalised
              // by the artifact's ordering contract, so the emission declares its
              // kinds in Ordinal order; that ordering is what this repo commits, and
              // adopting it was the one-time reordering Phase 1181 absorbed.
              let idl =
                  match Artifact.parse (read idlPath) with
                  | Ok v -> v
                  | Error m -> failtestf "idl.json did not parse: %s" m

              let support =
                  match SupportArtifact.parse (read supportPath) with
                  | Ok doc -> doc.Support
                  | Error m -> failtestf "support.json did not parse: %s" m

              let generated = emit support idl

              if regenerating then
                  regenerate generatedPath generated
              else
                  Expect.equal
                      (read generatedPath)
                      generated
                      "src/Fuaran.UI/Generated.fs is not what src/Fuaran.UI.Idl/{idl,support}.json regenerate. If the vocabulary changed deliberately: FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests. If it did not, the generated file was hand-edited — it is generated output, and content it needs belongs in the support document beside the vocabulary.")

          // The prelude the support document NAMES has to be there, or the
          // regenerated module does not compile — a failure that would otherwise
          // surface as an unrelated build break in Fuaran.UI.
          testCase "the named host prelude exists and declares the named module" (fun _ ->
              match Fuaran.UI.VocabularySupport.document.HostPrelude with
              | None -> failtest "the support document names no host prelude, but the vocabulary has hosted slots"
              | Some prelude ->
                  let resolved =
                      Path.GetFullPath(Path.Combine(Path.GetDirectoryName supportPath, prelude.Path))

                  Expect.isTrue
                      (File.Exists resolved)
                      (sprintf
                          "the support document names a host prelude at '%s', which resolves to %s"
                          prelude.Path
                          resolved)

                  Expect.stringContains
                      (File.ReadAllText resolved)
                      ("module " + prelude.Module)
                      "the named host prelude does not declare the module the support document names") ]
