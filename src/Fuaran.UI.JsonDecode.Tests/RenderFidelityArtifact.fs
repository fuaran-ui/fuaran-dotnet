module Fuaran.UI.JsonDecode.Tests.RenderFidelityArtifact

// ============================================================================
//  The generated render-fidelity manifest (Phase 442) — `render-fidelity.json`
//  at the corpus root, beside `schema.json` and `idl.json`.
//
//  A fourth artefact answering a fourth question. `schema.json` is the
//  validation surface ("is this payload legal on the wire?"); `idl.json` is the
//  structural source ("what IS the vocabulary?"); `validator/defect-vocabulary.json`
//  is the authoring contract ("what may a pre-emit validator refuse?"). This one
//  is the RENDER contract: "for this kind, which render tiers exist, what does
//  the parity-checked fallback pin, and what is declared client-only rich?".
//
//  None of that is new behaviour. It is the shipped fidelity contracts (Phases
//  289 / 290 / 292 / 293-658) made machine-readable, so a consumer that must
//  STATE which tier it is delivering — a per-node fidelity badge, a
//  certification report, a degradation exhibit — derives it instead of
//  hand-annotating it.
//
//  Emitted here rather than from `Fuaran.UI` itself, on the
//  `DefectVocabulary` precedent: the shipped package carries the declaration
//  (`Fuaran.UI.RenderFidelity`, Fable-safe and consumable by every F# tier),
//  the artefact writer lives with the other corpus writers. `Corpus.emit`
//  co-emits it, and `--emit-fidelity <dir>` writes just this file for the case
//  where the fixtures are not being regenerated. A stale-artefact guard asserts
//  byte-equality with the committed file, exactly as the stale-schema guard
//  does.
// ============================================================================

open System.IO
open System.Text.Json
open System.Text.Encodings.Web

open Fuaran.UI.RenderFidelity

/// Stable identifier for the published artefact. The `/v1/` segment pins the
/// wire-format major version, matching `SchemaGen.schemaId`.
[<Literal>]
let artifactId = "https://fuaran.dev/wire-format/v1/render-fidelity.json"

/// The artefact's file name at the corpus root.
[<Literal>]
let fileName = "render-fidelity.json"

let private writeRich (w: Utf8JsonWriter) (rich: RichTier) : unit =
    w.WriteStartObject("rich")

    match rich with
    | RichTier.None ->
        w.WriteString("class", "none")

        w.WriteString("meaning", "no client-only tier - the parity-checked fallback is the whole render")
    | RichTier.Behavioural(enhancement, seam) ->
        w.WriteString("class", "behavioural")
        w.WriteString("enhancement", enhancement)
        w.WriteString("seam", seam)

        w.WriteString(
            "meaning",
            "attached on hydration; adds behaviour, never DOM - it cannot cause a hydration mismatch, so it stays inside the parity contract"
        )
    | RichTier.ClientOnly(technique, seam) ->
        w.WriteString("class", "clientOnly")
        w.WriteString("technique", technique)
        w.WriteString("seam", seam)

        w.WriteString(
            "meaning",
            "changes the DOM after hydration and is excluded from every parity comparison by contract"
        )

    w.WriteEndObject()

/// The closed obligation vocabulary, emitted once at the top level (Phase 1105).
///
/// A host reads this to know the FULL set of claims that exist, not merely the
/// ones the kinds it renders happen to declare. That matters for the
/// "not checked is not passed" rule: a host whose checker registry is keyed by
/// claim id can report an id it does not implement only if it can enumerate the
/// vocabulary independently of the rows.
let private writeVocabulary (w: Utf8JsonWriter) : unit =
    w.WriteStartArray("obligationVocabulary")

    for claim in allClaims do
        w.WriteStartObject()
        w.WriteString("id", claimId claim)
        w.WriteString("meaning", claimMeaning claim)
        w.WriteEndObject()

    w.WriteEndArray()

/// The kind-intrinsic ARIA emissions (Phase 1591) — the roles and live regions
/// the renderer pins for a kind WHATEVER the node's `Accessibility` trait says.
///
/// An empty array is a positive statement, exactly as it is in the declaration:
/// the kind announces nothing of itself, so what it announces is exactly what
/// its trait declares. `role` / `live` / `condition` are omitted when absent,
/// per the corpus's omit-at-default convention; `tier` is always present
/// because "which pipelines emit this" has no default a reader could assume.
let private writeIntrinsic (w: Utf8JsonWriter) (entries: IntrinsicAria list) : unit =
    w.WriteStartArray("intrinsic")

    for a in entries do
        w.WriteStartObject()
        w.WriteString("element", a.Element)

        match a.Role with
        | Some r -> w.WriteString("role", r)
        | None -> ()

        match a.Live with
        | Some l -> w.WriteString("live", liveRegionToken l)
        | None -> ()

        match a.Condition with
        | Some c -> w.WriteString("condition", c)
        | None -> ()

        match a.Tier with
        | IntrinsicTier.BothPipelines -> w.WriteString("tier", "bothPipelines")
        | IntrinsicTier.ClientOnly why ->
            w.WriteString("tier", "clientOnly")
            w.WriteString("tierNote", why)

        w.WriteEndObject()

    w.WriteEndArray()

/// The node-level TRAITS (Phase 1696) — the claims a rendering host owes for a
/// member that rides the node ENVELOPE rather than any one kind.
///
/// A second top-level array beside `kinds`, not a synthetic row inside it: a
/// trait is owed by every kind that carries the member, and spelling that as
/// forty-three identical rows would state forty-three claims where there is one.
/// The claim ids come from the SAME closed `obligationVocabulary`, so a host
/// enumerates the claims that exist independently of which subject owes them.
let private writeTraits (w: Utf8JsonWriter) : unit =
    w.WriteStartArray("traits")

    for t in allTraits do
        w.WriteStartObject()
        w.WriteString("trait", t.Trait)
        w.WriteString("summary", t.Summary)

        w.WriteStartObject("appliesTo")

        match t.Scope with
        | TraitScope.AllKinds ->
            w.WriteString("scope", "allKinds")
            w.WriteStartArray("kinds")
            w.WriteEndArray()
        | TraitScope.NamedKinds kinds ->
            w.WriteString("scope", "namedKinds")
            w.WriteStartArray("kinds")

            for k in kinds do
                w.WriteStringValue(k)

            w.WriteEndArray()

        w.WriteEndObject()

        w.WriteStartArray("fixtures")

        for f in t.Fixtures do
            w.WriteStringValue("nodes/" + f + ".json")

        w.WriteEndArray()

        w.WriteStartArray("obligations")

        for o in t.Obligations do
            w.WriteStartObject()
            w.WriteString("id", claimId o.Claim)

            match o.Rule with
            | Some n -> w.WriteNumber("rule", n)
            | None -> ()

            w.WriteString("statement", o.Statement)
            w.WriteString("section", o.Section)
            w.WriteEndObject()

        w.WriteEndArray()

        w.WriteString("contract", t.Contract)
        w.WriteEndObject()

    w.WriteEndArray()

let private writeObligations (w: Utf8JsonWriter) (obligations: Obligation list) : unit =
    w.WriteStartArray("obligations")

    for o in obligations do
        w.WriteStartObject()
        w.WriteString("id", claimId o.Claim)
        w.WriteString("statement", o.Statement)
        w.WriteString("section", o.Section)
        w.WriteEndObject()

    w.WriteEndArray()

/// The artefact as a deterministic UTF-8 string.
///
/// `NewLine = "\n"` is load-bearing on Windows, for the reason recorded beside
/// `Corpus.writeManifest`: `Utf8JsonWriter` indents with `Environment.NewLine`,
/// and the corpus `.gitattributes` pins `eol=lf`, so a CRLF emission normalises
/// on commit while consumers that byte-compare the WORKING TREE see drift they
/// cannot clear.
let toJson () : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)

    w.WriteString(
        "description",
        "Per-NodeKind render-fidelity declaration for the Fuaran UI wire format. For each canonical "
        + "kind.$type: what the wire carries (source), what the parity-checked render pins (fallback), "
        + "and what, if anything, is declared client-only rich. Generated from Fuaran.UI.RenderFidelity; "
        + "it transcribes the shipped fidelity contracts and introduces no wire or renderer behaviour. "
        + "A consumer deriving per-node fidelity badges reads this rather than hand-annotating. "
        + "Each kind also declares the checkable render obligations it owes, drawn from the closed "
        + "obligationVocabulary and bound to the spec section that states each one; a host render "
        + "suite asserts every obligation for the kinds it renders and REPORTS any it cannot check, "
        + "because not checked is not passed. Each kind also declares its INTRINSIC ARIA: the roles "
        + "and live regions the renderer emits whatever the node's accessibility trait says, with the "
        + "condition where the emission turns on the kind's own spec and the tier that emits it. A "
        + "consumer reads that to know whether a kind is already announced before authoring a trait "
        + "it does not need; an empty array means the kind announces nothing of itself. "
        + "Beside kinds, traits declares the obligations owed for a member that rides the node "
        + "envelope rather than any one kind - a trait is identified by the wire path of the member "
        + "it governs, names the kinds it applies to, and draws its claim ids from the same closed "
        + "obligationVocabulary, so a host reports an unchecked trait claim exactly as it reports an "
        + "unchecked kind claim. "
        + "See WIRE_FORMAT.md 13."
    )

    w.WriteStartArray("tiers")

    for (tier, meaning) in
        [ "source", "the deterministic, parity-clean data the wire carries; never a rendered form"
          "fallback",
          "the deterministic render the SSR-parity corpus and the cross-host byte-diff compare - what a no-JS reader, a crawler, or a non-browser host gets"
          "rich",
          "the declared client-only render, explicitly outside every parity comparison rather than silently divergent" ] do
        w.WriteStartObject()
        w.WriteString("tier", tier)
        w.WriteString("meaning", meaning)
        w.WriteEndObject()

    w.WriteEndArray()

    writeVocabulary w

    w.WriteStartArray("kinds")

    for r in all do
        w.WriteStartObject()
        w.WriteString("kind", r.Kind)
        w.WriteBoolean("sensitive", r.Sensitive)
        w.WriteString("source", r.Source)
        w.WriteString("fallback", r.Fallback)
        writeRich w r.Rich

        w.WriteStartArray("fixtures")

        for f in r.Fixtures do
            w.WriteStringValue("nodes/" + f + ".json")

        w.WriteEndArray()

        writeObligations w r.Obligations
        writeIntrinsic w r.Intrinsic

        w.WriteString("contract", r.Contract)
        w.WriteEndObject()

    w.WriteEndArray()

    writeTraits w

    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the artefact into a corpus directory.
let write (outputDir: string) : unit =
    File.WriteAllText(Path.Combine(outputDir, fileName), toJson ())
