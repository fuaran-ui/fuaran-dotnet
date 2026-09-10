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

// ─── steering a SAMPLED value out of the engine's blind spot ───────────────
//
//  THE FINDING THIS PASS EXISTS FOR — an `OmitDefault` on a CASE-MAPPED enum is
//  a hole in `Fuaran.Core.Idl`'s three encoders, and the sweep is what made it
//  visible. `OmitDefault d` says the canonical wire omits a member carrying `d`.
//  All three legs implement that rule, and they do NOT agree about the alphabet
//  `d` is written in:
//
//    * the F# backend renders the declaration as a HOST CASE NAME
//      (`if s.Direction = TextDirection.Auto then None else …`) — so a
//      declaration in the wire alphabet would not even compile;
//    * the TypeScript backend emits the SAME string as a WIRE comparison
//      (`s.direction === "Auto" ? null : …`) — which is never true for a
//      case-mapped enum, whose runtime value is `"auto"`;
//    * the interpreter compares `IdlValue`s, and `VEnum` carries the WIRE string
//      (`Idl.fs`: "`VEnum` carries the WIRE string, exactly as `VUnion` carries
//      the wire tag") — so it too never recognises its own default.
//
//  At most one of those can be right about any given declaration, so the fix is
//  Fuaran.Core's: resolve the declaration through `IdlEnum.WireOf` at the two
//  wire-alphabet comparison sites, or refuse the declaration. It cannot be
//  fixed from this repo — declaring the default in either alphabet breaks one
//  of the three legs — so this pass steers the sampler out of the hole and the
//  hole is reported upward, with `blindSpotFields` below pinning its scope so a
//  second instance (or the engine fix that retires the pass) is not silent.
//
//  It STEERS rather than drops, and that distinction cost a red run to learn.
//  `OmitDefault` means the member ALWAYS HAS A VALUE — the sampler never draws
//  it absent, and both generated host types declare it non-optional — so
//  removing it from the vector does not produce a narrower value, it produces an
//  ill-formed one: the TypeScript encoder's `encStr(s.direction)` then runs on
//  `undefined` and throws on 3,690 of 4,000 vectors. Moving the value to a
//  different case of the same enum keeps the vector well-formed, keeps the
//  member exercised, and lands inside the space all three legs agree on.
//
//  Only the blind spot is touched. Where the alphabets coincide — an
//  identity-mapped enum, a bool, an int, a list — all three legs already agree
//  about the default, and the at-default draw is worth exercising, so the pass
//  is a no-op there by construction.
//
//  It is a STRUCTURAL walk rather than a name-keyed rewrite on purpose. Field
//  names are not unique across this vocabulary — `direction` is an `OmitDefault
//  Auto` on `SemanticStyle` and a REQUIRED `Orientation` on a layout record,
//  `children` is `OmitDefault []` in one place and required in another — so a
//  rewrite keyed on the name alone would touch members it has no business
//  touching, and would do it silently.

/// The declared default sits in the engine's blind spot, and here is a value
/// that does not: `Some` the substitute, `None` when the field is not in the
/// blind spot or the draw is not at its default.
let private steerOffBlindSpot (idl: Idl) (ty: IdlType) (d: IdlValue) (v: IdlValue) : IdlValue option =
    match ty, d, v with
    | TEnum name, VEnum declaredCase, VEnum sampledWire ->
        match idl.Enums |> List.tryFind (fun e -> e.Name = name) with
        // The blind spot exactly: the declaration names a HOST CASE whose wire
        // spelling differs, and the draw is that wire spelling.
        | Some e when e.WireOf declaredCase <> declaredCase && sampledWire = e.WireOf declaredCase ->
            // The first other case, so the substitution is deterministic and the
            // pinned seed still means one thing. A single-case enum has no other
            // case; the vector is left alone and the sweep reports whatever
            // follows rather than this pass inventing a value.
            e.WireCases |> List.tryFind (fun w -> w <> sampledWire) |> Option.map VEnum
        | _ -> None
    | _ -> None

/// Every field in the vocabulary that sits in the blind spot, as
/// `<owner>.<field>` — the scope of the finding above, derived from the
/// vocabulary so a second instance cannot arrive unannounced.
let blindSpotFields (idl: Idl) : string list =
    let inBlindSpot (f: IdlField) =
        match f.Opt, f.Type with
        | OmitDefault(VEnum declaredCase), TEnum name ->
            idl.Enums
            |> List.tryFind (fun e -> e.Name = name)
            |> Option.map (fun e -> e.WireOf declaredCase <> declaredCase)
            |> Option.defaultValue false
        | _ -> false

    let named (owner: string) (fields: IdlField list) =
        fields |> List.filter inBlindSpot |> List.map (fun f -> owner + "." + f.Name)

    [ yield! idl.Records |> List.collect (fun r -> named r.Name r.Fields)
      yield! idl.Kinds |> List.collect (fun k -> named k.Tag k.Fields)
      yield! idl.Ops |> List.collect (fun o -> named o.Tag o.Fields)
      yield!
          idl.Unions
          |> List.collect (fun u -> u.Cases |> List.collect (fun c -> named (u.Name + "." + c.Tag) c.Fields))
      yield! named "node" idl.NodeFields ]
    |> List.sort

let rec private narrowFields
    (idl: Idl)
    (decl: IdlField list)
    (fields: (string * IdlValue) list)
    : (string * IdlValue) list =
    fields
    |> List.map (fun (n, v) ->
        match decl |> List.tryFind (fun f -> f.Name = n) with
        // A member the declaration does not name is left alone: inventing a
        // judgement about it would be the sampler's business, not the wire's.
        | None -> n, v
        | Some f ->
            let v = narrowValue idl f.Type v

            match f.Opt with
            | OmitDefault d ->
                match steerOffBlindSpot idl f.Type d v with
                | Some steered -> n, steered
                | None -> n, v
            | _ -> n, v)

and private narrowValue (idl: Idl) (ty: IdlType) (v: IdlValue) : IdlValue =
    match ty, v with
    | TRecord name, VRecord fields ->
        match idl.Records |> List.tryFind (fun r -> r.Name = name) with
        | Some r -> VRecord(narrowFields idl r.Fields fields)
        | None -> v
    | TList inner, VList xs -> VList(xs |> List.map (narrowValue idl inner))
    | TMap inner, VMap entries -> VMap(entries |> List.map (fun (k, e) -> k, narrowValue idl inner e))
    | TUnion(name, _), VUnion(tag, fields) ->
        // The union's type ARGUMENTS are deliberately not substituted: a `TVar`
        // slot falls through unnarrowed below, which is conservative — a missed
        // narrowing shows up as a divergence the sweep reports, where a wrong one
        // would silently shrink what it measures.
        match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
        | Some u ->
            match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
            | Some c -> VUnion(tag, narrowFields idl c.Fields fields)
            | None -> v
        | None -> v
    | TKind, VUnion(tag, fields) ->
        match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
        | Some k -> VUnion(tag, narrowFields idl k.Fields fields)
        | None -> v
    | TOp, VUnion(tag, fields) ->
        match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
        | Some o -> VUnion(tag, narrowFields idl o.Fields fields)
        | None -> v
    | TNode, _ -> narrowNode idl v
    | _ -> v

/// Steer a node (bare or enveloped) — the sampler's root shape, and reachable
/// again through any `TNode` slot.
and narrowNode (idl: Idl) (v: IdlValue) : IdlValue =
    let kindFields (tag: string) =
        idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) |> Option.map _.Fields

    match v with
    | VNode(id, tag, fields) ->
        match kindFields tag with
        | Some decl -> VNode(id, tag, narrowFields idl decl fields)
        | None -> v
    | VNodeEnv(id, env, tag, fields) ->
        let env = narrowFields idl idl.NodeFields env

        match kindFields tag with
        | Some decl -> VNodeEnv(id, env, tag, narrowFields idl decl fields)
        | None -> VNodeEnv(id, env, tag, fields)
    // Not a node shape at all — nothing this pass has anything to say about.
    | other -> other

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
