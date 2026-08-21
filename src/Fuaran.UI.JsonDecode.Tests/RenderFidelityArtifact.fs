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

        w.WriteString("contract", r.Contract)
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the artefact into a corpus directory.
let write (outputDir: string) : unit =
    File.WriteAllText(Path.Combine(outputDir, fileName), toJson ())
