module Fuaran.UI.Tests.IdlCertification

// ============================================================================
//  Phase 1668 — the shared reading surface for the three UI-scale IDL
//  certifications this tier took over.
//
//  `fuaran-core#123` deleted the UI byte-pin fixture and, with it, the seven
//  suites it fed. Three of those certified things no NEUTRAL vocabulary can
//  certify, because what they measure is SCALE and CORPUS rather than
//  mechanism: the generated JSON schema evaluated by an off-the-shelf Draft
//  2020-12 validator against the whole node corpus, the IDL op codec against
//  the op corpus, and a generative three-way sweep at the real ~43-kind scale.
//  They come home here, to the tier that owns the vocabulary, over the PACKAGED
//  engine.
//
//  This module is what the three of them share, and it exists rather than
//  being copied three ways for the reason Phase 1647 gave when it collapsed
//  twenty near-identical corpus walks into one resolver: three copies of a
//  reading rule agree on the common case and diverge on the ones that matter.
//
//  What it deliberately does NOT hold is any vocabulary. Every suite reads the
//  HOMED declaration at `src/Fuaran.UI.Idl/` — there is no second copy of the
//  kind set, the op set or the field lists anywhere in this test project, which
//  is the property that made the Core exception retirable in the first place.
// ============================================================================

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core.Idl

/// The vocabulary the three certifications run over: the homed declaration, in
/// the shape the committed artefact projects it.
///
/// `Artifact.canonicalise` is applied deliberately, and it is not cosmetic.
///
///  * It moves NO encoder's bytes. The function sorts the top-level collections
///    by identity and preserves authored order WITHIN an entry (field lists,
///    union cases, the node envelope), and it is emitted key order that a
///    field list decides — so every byte comparison below is the comparison it
///    would have been against the raw value.
///  * It is the exact form `src/Fuaran.UI/Generated.fs` is emitted from. The
///    regeneration triple emits from `Artifact.parse (read idl.json)`, and
///    `parse (render v) = canonicalise v` is pinned in `Fuaran.UI.Idl.Tests`.
///    The fuzz sweep compares the interpreter against that generated module, so
///    reading the same shape makes the two agree by construction rather than by
///    the accident of the authored order matching.
///  * It makes the generative sweep's PINNED SEED stable. The sampler cycles
///    kinds in list order, so on the raw value every vector in the sweep would
///    change when someone reshuffled `Vocabulary.fs` — a diff in a file that
///    declares no new vocabulary silently re-rolling the gate.
let vocabulary: Idl = Artifact.canonicalise Fuaran.UI.Vocabulary.uiIdl

/// Every kind tag in the real vocabulary — the sampler's tag cycle and the
/// generated TypeScript module's kind set, which must be the same set or a kind
/// missing from the TS side surfaces as `"kind":undefined` rather than as a
/// missing-kind error.
let allKindTags: string list = vocabulary.Kinds |> List.map _.Tag

// ─── the corpus, through the ONE resolver ──────────────────────────────────

/// The shared corpus root, or `None` on a genuine single-repo checkout.
/// Phase 1647's resolver and no second path — in a worktree it is
/// `FUARAN_WIRE_FIXTURES` that answers, and a suite that walked for itself
/// would skip instead and certify nothing.
let corpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind ()

/// Every `<family>/*.json` fixture as (file name, trimmed contents), sorted
/// Ordinal by full path so a failure report is reproducible.
///
/// The two absences are kept apart, exactly as `CorpusRoot`'s own header asks:
/// no corpus at all is `[]` (the caller skips, naming the override), while a
/// corpus that is present and lacks the family is a FAILURE — that is a corpus
/// whose shape has changed under a certification, and reading it as "nothing to
/// check" is how a green gate comes to mean nothing.
let familyFixtures (family: string) : (string * string) list =
    match corpusRoot () with
    | None -> []
    | Some root ->
        let dir = Path.Combine(root, family)

        if not (Directory.Exists dir) then
            failtestf
                "the corpus at %s holds no '%s/' family — a certification cannot read what is not there, and skipping would report green having compared nothing"
                root
                family

        Directory.GetFiles(dir, "*.json")
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> Array.toList
        |> List.map (fun p ->
            // `Path.GetFileName` is `string | null` under F# 10 nullness; a path
            // from `GetFiles` always has one, so fall back to the whole path
            // rather than threading an option no caller can act on.
            let name = Path.GetFileName p |> Option.ofObj |> Option.defaultValue p
            name, (File.ReadAllText p).Trim())

/// How many fixtures of one `kind` the corpus MANIFEST enumerates.
///
/// The same posture `GeneratedLayerTests` takes, and for the same reason: the
/// corpus is a SEPARATE repository, so a literal here is a forward-coupling
/// trap in the one direction this repo cannot control — a fixture lands there
/// with no commit in this one, and the pin then goes red in whatever session
/// next runs the gate rather than in the one that moved the corpus. The
/// manifest is the corpus's own authoritative enumeration and it moves WITH
/// the fixture, in the same corpus commit.
///
/// Fails rather than returning 0 when the corpus is absent: both sides of a
/// comparison against 0 would agree and the assertion would pass while
/// measuring nothing.
let manifestFamilySize (kind: string) : int =
    match corpusRoot () with
    | None -> failtestf "wire-format-fixtures/manifest.json not found — %s" Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some root ->
        use manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

        let count =
            manifest.RootElement.GetProperty("fixtures").EnumerateArray()
            |> Seq.filter (fun entry ->
                match entry.TryGetProperty "kind" with
                | true, value -> value.GetString() = kind
                | _ -> false)
            |> Seq.length

        if count = 0 then
            failtestf "manifest.json enumerates no '%s' fixtures — the corpus or the family name is wrong" kind

        count

/// The sentence a suite prints when it degrades to a skip, so the reader is
/// told about the override rather than left to conclude the checkout is wrong.
let absentCorpusSkip: string = Fuaran.Tests.CorpusRoot.AbsentSkipReason

// ─── the engine's omit-default blind spot — retired at Fuaran.Core 0.34.0 ──
//
//  A sweep-side pass used to steer sampled values away from an `OmitDefault` on
//  a CASE-MAPPED enum (`SemanticStyle.direction`, default `Auto` / wire `auto`),
//  because the three legs disagreed about the alphabet the default was written
//  in: the F# backend compared host cases, the TypeScript backend and the
//  interpreter compared wire strings. Fuaran.Core 0.34.0 settled it in the
//  engine — `VEnum` is the WIRE token throughout, the F# backend resolves it to
//  the host case (`IdlEnum.CaseOf`), and an undeclared token is refused when the
//  vocabulary is checked — so the vocabulary now declares `auto`, all three legs
//  agree about the at-default draw, and the pass and the test pinning its scope
//  were retired with the pin raise, as that test said they should be.

// ─── running a generated module under node ─────────────────────────────────

/// Run a generated ES module + harness under node, returning its stdout.
/// `None` when node is not on PATH — the leg that needs it skips and SAYS so
/// rather than passing quietly.
///
/// Written here rather than in the sweep because the shape has three traps and
/// they are all easy to get wrong once, let alone three times: the child's
/// stdout must be read BEFORE `WaitForExit` (a full pipe deadlocks a chatty
/// harness), the encoding must be pinned or a redirected child's non-ASCII
/// output decodes through the parent's console code page, and a non-zero exit
/// must FAIL rather than yield an empty comparison set.
let runNode (source: string) : string option =
    let tmp =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-ui-idl-fuzz-%s.mjs" (Guid.NewGuid().ToString "N"))

    File.WriteAllText(tmp, source)

    try
        let psi = ProcessStartInfo("node", "\"" + tmp + "\"")
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        // A redirected child's stdout decodes with the PARENT's console code
        // page unless pinned, so a mangled byte in a sampled string would read
        // as a cross-host divergence.
        psi.StandardOutputEncoding <- Text.UTF8Encoding false
        psi.StandardErrorEncoding <- Text.UTF8Encoding false

        // `Process.Start` is `Process | null` under F# 10 nullness, and a missing
        // executable throws rather than returning null — both spellings of
        // "node is not here" collapse to the same `None`.
        let started =
            try
                Process.Start psi
            with _ ->
                null

        match started with
        | null -> None
        | p ->
            use p = p
            let stdout = p.StandardOutput.ReadToEnd()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
                failtestf "node failed running the generated TypeScript harness: %s" stderr

            Some stdout
    finally
        if Environment.GetEnvironmentVariable "FUARAN_KEEP_HARNESS" = "1" then
            printfn "harness kept at %s" tmp
        else
            try
                File.Delete tmp
            with _ ->
                ()
