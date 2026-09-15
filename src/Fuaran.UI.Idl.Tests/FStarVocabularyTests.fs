module Fuaran.UI.FStarVocabularyTests

#nowarn "3261" // DirectoryInfo.Parent is legitimately nullable here.

// ============================================================================
//  Phase 1754 — this tier owns its vocabulary's PROOF.
//
//  Since Phase 1181 this repository homes `src/Fuaran.UI.Idl/idl.json` and
//  regenerates its structural layer in-process against the packaged engine.
//  The vocabulary's F* model lived in `Fuaran-Core`'s proof leg until
//  `fuaran-core#173` moved that leg onto the engine's own certification set —
//  a domain's proof has no business running in the substrate's CI (Core's D14).
//  This file is the other half of that move: the UI vocabulary's model and the
//  theorems over it are generated HERE, from THIS repository's committed
//  `idl.json`, by the same `Fuaran.Core.Idl.Codegen` F* target.
//
//  TWO FAMILIES LIVE HERE, and they check different things:
//
//    `Proofs.Vocabulary` — the GENERATION DIFF. Each committed `proofs/*.fst`
//      must be byte-identical to a fresh generation from the committed
//      `idl.json`. This is the vocabulary-drift discipline `fuaran-core#150`
//      established, now on the file it was always about: a vocabulary that
//      moves without a regeneration means the theorem is about a document this
//      repository no longer sends.
//
//    `Proofs.Ladder` — `../proofs.json` against this tree: every `proved` row
//      names a top-level declaration that exists in the model it names, every
//      model it names is one `proofs/check.ps1` actually checks, and every
//      checked module carries at least one row.
//
//  `dotnet run --project src/Fuaran.UI.Idl.Tests -- --emit-fstar` is the one
//  sanctioned write path, mirroring `FUARAN_REGEN=1` beside it.
//
//  COVERAGE — the decision this phase owns, and the measurement behind it.
//  See `proofs/README.md` "Coverage" for the numbers; the rule is in
//  `selection` below, where it is computed rather than listed.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Idl

// ─── Locating the repo's own files ─────────────────────────────────────────
//
// Climb from the test binary to the checkout root (the directory holding
// `Fuaran.sln`), exactly as `VocabularyTests` does. Everything read below is
// inside THIS repository: the proof leg is not gated on a sibling clone.

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
    // FAIL, never skip — a proof check that goes green without its inputs is
    // worse than no check at all.
    | None ->
        failwithf
            "could not locate the repository root (no Fuaran.sln above %s) — the proof leg lives in this repo and must be readable"
            AppContext.BaseDirectory

let private path (parts: string list) =
    Path.Combine(Array.ofList (repoRoot :: parts))

let private idlPath = path [ "src"; "Fuaran.UI.Idl"; "idl.json" ]
let private proofsDir = path [ "proofs" ]
let private checkScriptPath = path [ "proofs"; "check.ps1" ]
let private ladderPath = path [ "proofs.json" ]

let private lf (s: string) = s.Replace("\r\n", "\n")

let private read (p: string) : string =
    if File.Exists p then File.ReadAllText p else ""

/// THE VOCABULARY, read from the COMMITTED BYTES rather than from
/// `Fuaran.UI.Vocabulary.uiIdl` beside them. Phase 1181's reason applies
/// unchanged: emitting from the in-memory value would prove the generator is a
/// function and say nothing about whether the artefact a second reader consumes
/// describes the same vocabulary. `VocabularyTests` holds the two equal; this
/// reads the one a reviewer reads.
let private committedIdl () : Idl =
    match Artifact.parse (read idlPath) with
    | Ok idl -> idl
    | Error m -> failwithf "src/Fuaran.UI.Idl/idl.json did not parse: %s" m

// ─── Coverage ──────────────────────────────────────────────────────────────

/// The kinds a committed model covers.
///
/// **This is `FStarTarget.proofKinds`, and choosing it over the exhaustive
/// expressible set is this phase's decision — taken against a measurement, not
/// inherited.** The exhaustive set was the instruction; it is not reachable at
/// this vocabulary's scale, and the arithmetic is not close:
///
///   * The emitted proof script splits a constructor with k conditional members
///     into 2^k presence-pattern lemmas (`FStarTarget.presenceSplitAt`), which
///     is `fuaran-core#168`'s per-kind shape and the thing that made the round
///     trip discharge at all. Summed over all 42 expressible kinds of this
///     vocabulary it is **68,800 lemmas**, of which `DataGrid` alone — sixteen
///     conditional members — contributes **65,536**. Each is checked with
///     `--quake 3`.
///   * `Fuaran-Core` measured the other half independently: the whole
///     expressible set of THIS vocabulary did not finish a single check in
///     twenty-five minutes (`FStarTarget.proofKinds`'s own docstring), and at
///     the `proofKinds` selection Phase 150 measured 322–398 s a check for the
///     model alone.
///
/// So the exhaustive decision is recorded as REFUTED rather than deferred, and
/// the refusals the header names are of two kinds: the one construct the target
/// cannot express at all, and the kinds a measured cost control holds out. Both
/// are named in the emitted header, which is the property that matters — a kind
/// that becomes cheap enters at the next regeneration, and nothing here is a
/// list anybody maintains.
let private selection (idl: Idl) : string list = FStarTarget.proofKinds idl

// ─── Provenance ────────────────────────────────────────────────────────────

/// What every header says about the remedy, once — the command and the family
/// that holds the committed file to it.
let private remedyLines =
    [ "DO NOT EDIT: `dotnet run --project src/Fuaran.UI.Idl.Tests -- --emit-fstar`"
      "regenerates it, and the `Proofs.Vocabulary` family holds this file to a fresh"
      "generation from that source — a vocabulary that moves without a regeneration is"
      "VOCABULARY DRIFT, and `proofs/check.ps1` names it." ]

/// What is proved here is a property OF — the clause closing "WHAT THIS IS NOT".
/// The boundary is the load-bearing half: this is about the wire vocabulary's
/// generated encoder and decoder, and about no host's hand-written decoder.
let private provesLines =
    [ "FUARAN UI WIRE VOCABULARY this repository declares in `src/Fuaran.UI.Idl/idl.json`,"
      "as the F* target models it. The five conformant hosts keep their own hand-written,"
      "tuned decoders and are certified against the shared wire-format corpus; nothing here"
      "is a statement about any of them, and no host's build generates this file." ]

let private provenance: FStarTarget.Provenance =
    { Origin =
        [ "this repository's OWN vocabulary — `src/Fuaran.UI.Idl/idl.json`, the structural"
          "source Phase 1181 homed here and `VocabularyTests` holds to `Fuaran.UI.Vocabulary`"
          "(fuaran Phase 1754, the proof kit's first adopter)." ]
        @ remedyLines
      Proves = provesLines }

// ─── The generated pair ────────────────────────────────────────────────────

[<Literal>]
let private modelModule = "Vocabulary"

[<Literal>]
let private proofsModule = "VocabularyProofs"

let private modelPath = path [ "proofs"; modelModule + ".fst" ]
let private proofsPath = path [ "proofs"; proofsModule + ".fst" ]

/// Both generated files from ONE walk: the model and the theorems over it. The
/// two must be generated from the same IDL at the same selection or they are
/// about different vocabularies.
let private pair (idl: Idl) : (string * Result<string, CodegenError>) list =
    let kinds = selection idl

    [ modelPath, FStarTarget.vocabularyModuleFrom provenance modelModule idl kinds
      proofsPath, FStarTarget.proofsModuleFrom provenance proofsModule modelModule idl kinds ]

/// `--emit-fstar` — rewrite both committed files from the committed vocabulary.
/// The command the generation diff names when it fails, and the only sanctioned
/// way those files change.
let emit () : int =
    let idl = committedIdl ()

    let write (p: string) (result: Result<string, CodegenError>) =
        match result with
        | Error e ->
            eprintfn "--emit-fstar: %s" (CodegenError.describe e)
            2
        | Ok text ->
            File.WriteAllText(p, lf text, Text.UTF8Encoding false)
            printfn "regenerated %s" p
            0

    let worst = pair idl |> List.map (fun (p, r) -> write p r) |> List.max

    printfn
        "%s: %d of %d kinds modelled; the rest are named in the emitted header"
        modelModule
        (selection idl |> List.length)
        (List.length idl.Kinds)

    worst

// ─── Family 1: the generation diff ─────────────────────────────────────────

let private expectGenerated (name: string) (p: string) (result: Result<string, CodegenError>) =
    match result with
    | Error e -> failtestf "the F* target refused this repository's vocabulary at %s: %s" name (CodegenError.describe e)
    | Ok text ->
        Expect.equal
            (lf (read p))
            (lf text)
            (sprintf
                "proofs/%s is not what src/Fuaran.UI.Idl/idl.json generates. Regenerate with `dotnet run --project src/Fuaran.UI.Idl.Tests -- --emit-fstar` and commit both files together."
                name)

[<Tests>]
let generationTests =
    testList
        "Proofs.Vocabulary"
        [ testCase "the committed F* model and proof script are a fresh generation of this repo's vocabulary" (fun _ ->
              let idl = committedIdl ()

              for p, result in pair idl do
                  expectGenerated (Path.GetFileName p) p result)

          // The header is what a reader of the theorem meets first, and what
          // tells them what it does NOT cover. An emitted model whose header
          // stopped naming its refusals would still verify.
          testCase "every kind outside the model is NAMED in the model's header" (fun _ ->
              let idl = committedIdl ()
              let covered = selection idl |> Set.ofList
              let header = read modelPath

              for k in idl.Kinds do
                  if not (Set.contains k.Tag covered) then
                      Expect.isTrue
                          (header.Contains("- " + k.Tag + ":"))
                          (sprintf
                              "kind %s is outside the proof vocabulary and is not named in proofs/%s.fst's header — a reader cannot tell what the theorem does not cover"
                              k.Tag
                              modelModule))

          // A kind the target CANNOT express is a different statement from one a
          // cost control holds out, and the emitted header tells them apart. This
          // pins the count so that a vocabulary change making a refusal appear or
          // disappear is a visible diff rather than a quiet one.
          testCase "the target's refusals are exactly the one this vocabulary is known to carry" (fun _ ->
              let idl = committedIdl ()

              let refused =
                  FStarTarget.partition idl
                  |> List.filter (fun v -> v.Refusal.IsSome)
                  |> List.map _.Tag

              Expect.equal
                  refused
                  [ "Tabs" ]
                  "the set of kinds the F* target cannot express has moved. `Tabs` is refused because `Tabs.activeIndex` is a `Binding<int>` omitted at the declared default `Static { value = 0 }`, and the model's opaque numeric carriers have no literal to spell that default's inner `VInt 0` with (the Core ask is in proofs/README.md). A change here is a vocabulary or backend change and needs the README's refusal list moved with it.") ]

// ─── Family 2: the claims ladder ───────────────────────────────────────────
//
// `../proofs.json` is authored by hand and says what this repository PROVES,
// TESTS and ASSUMES. Nothing holds prose to a tree, but the half that is
// mechanical — a row naming a declaration that exists, in a model the leg
// actually checks — is checked here. The kit ships the leg that runs this
// family; what a row is held to is a property of this tree, so the family is
// this repository's.

/// The modules `proofs/check.ps1` actually checks, parsed out of its `$modules`
/// line AS TEXT. Reading the script rather than a second list is deliberate:
/// a model added to the leg without a claim is caught, and there is no second
/// place to keep in step.
let private checkedModules () : string list =
    let m = Regex.Match(read checkScriptPath, @"(?m)^\$modules\s*=\s*@\((.*)\)\s*$")

    Expect.isTrue m.Success "proofs/check.ps1 declares $modules on one literal line"

    [ for g in Regex.Matches(m.Groups[1].Value, @"'([^']+)'") -> g.Groups[1].Value ]

type private Row =
    { Id: string
      Level: string
      Theorem: string option
      Model: string option }

/// The ladder, read with the same JSON reader the vocabulary artefacts use.
let private ladderRows () : Row list =
    match Fuaran.Core.Json.parse (read ladderPath) with
    | Error e -> failtestf "proofs.json did not parse: %s" e
    | Ok v ->
        let str k (o: Fuaran.Core.JVal) =
            match o with
            | Fuaran.Core.JObj ms ->
                ms
                |> List.tryPick (fun (n, x) ->
                    match n = k, x with
                    | true, Fuaran.Core.JStr s -> Some s
                    | _ -> None)
            | _ -> None

        let claims =
            match v with
            | Fuaran.Core.JObj ms ->
                ms
                |> List.tryPick (fun (n, x) ->
                    match n, x with
                    | "claims", Fuaran.Core.JArr xs -> Some xs
                    | _ -> None)
            | _ -> None

        match claims with
        | None -> failtest "proofs.json carries no `claims` array"
        | Some xs ->
            [ for x in xs ->
                  let ev =
                      match x with
                      | Fuaran.Core.JObj ms ->
                          ms |> List.tryPick (fun (n, y) -> if n = "evidence" then Some y else None)
                      | _ -> None

                  { Id = str "id" x |> Option.defaultValue ""
                    Level = str "level" x |> Option.defaultValue ""
                    Theorem = ev |> Option.bind (str "theorem")
                    Model = ev |> Option.bind (str "model") } ]

let private levels = set [ "proved"; "tested"; "assumed"; "policy" ]

[<Tests>]
let ladderTests =
    testList
        "Proofs.Ladder"
        [ testCase "every row's level is one of the closed set" (fun _ ->
              for r in ladderRows () do
                  Expect.isTrue
                      (Set.contains r.Level levels)
                      (sprintf "proofs.json row '%s' declares level '%s', outside the closed set" r.Id r.Level))

          testCase "every proved row names a declaration that exists in the model it names" (fun _ ->
              let checked_ = checkedModules () |> Set.ofList

              for r in ladderRows () do
                  if r.Level = "proved" then
                      let theorem =
                          match r.Theorem with
                          | Some t -> t
                          | None -> failtestf "proofs.json row '%s' is `proved` and names no theorem" r.Id

                      let model =
                          match r.Model with
                          | Some m -> m
                          | None -> failtestf "proofs.json row '%s' is `proved` and names no model" r.Id

                      let modelName = Path.GetFileNameWithoutExtension model

                      Expect.isTrue
                          (Set.contains modelName checked_)
                          (sprintf
                              "proofs.json row '%s' names model %s, which proofs/check.ps1 does not check — a proved row over a module nothing verifies is not a proof"
                              r.Id
                              model)

                      let text =
                          read (Path.Combine(repoRoot, model.Replace("/", string Path.DirectorySeparatorChar)))

                      Expect.isTrue
                          (Regex.IsMatch(text, @"(?m)^(let rec|and|let|val) " + Regex.Escape theorem + @"\b"))
                          (sprintf "proofs.json row '%s' names theorem %s, absent from %s" r.Id theorem model))

          testCase "every module the leg checks carries at least one row" (fun _ ->
              let rows = ladderRows ()

              let claimed =
                  rows
                  |> List.choose (fun r -> r.Model |> Option.map Path.GetFileNameWithoutExtension)
                  |> Set.ofList

              for m in checkedModules () do
                  Expect.isTrue
                      (Set.contains m claimed)
                      (sprintf
                          "proofs/check.ps1 checks %s and proofs.json claims nothing about it — a module verified with no claim is prover time nobody can read"
                          m))

          // The go-red fixture the kit asks for beside each clause: a ladder
          // family that cannot fail is a green tick over an unread file.
          testCase "the theorem clause goes red on a theorem that is not there" (fun _ ->
              let text = read modelPath

              Expect.isFalse
                  (Regex.IsMatch(text, @"(?m)^(let rec|and|let|val) no_such_theorem_here\b"))
                  "the go-red fixture's absent declaration is absent, so the clause above is known to be falsifiable") ]
