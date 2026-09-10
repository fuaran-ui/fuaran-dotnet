module Fuaran.UI.JsonDecode.Tests.RenderTextArtifact

// ============================================================================
//  The generated render-TEXT conformance family (Phase 1663) — `render-text.json`
//  at the corpus root, pointed at by `manifest.json`'s `renderText` key.
//
//  A FIFTH artefact answering a fifth question. `schema.json` asks *is this
//  payload legal on the wire?*; `idl.json` asks *what IS the vocabulary?*;
//  `validator/defect-vocabulary.json` asks *what may a pre-emit validator
//  refuse?*; `render-fidelity.json` asks *which render tiers exist for this
//  kind, and what does the parity-checked fallback pin?* — in sentences. This
//  one asks *what TEXT does this slot read, under these host sources?*, and it
//  is the first of the five a machine can evaluate rather than merely name.
//
//  THE PROOF IS HERE, NOT IN A HOST'S SUITE. `toJson` decodes each vector's
//  named corpus fixture, resolves the named slot through the reference
//  resolver under the vector's pinned sources, and RAISES when the produced
//  text differs from the authored expectation — so the artefact cannot be
//  written carrying a claim the reference host does not meet. Same discipline
//  as the §16 and §15 families, whose emitters prove the normalisation and
//  tolerance laws before writing a byte.
//
//  Co-emitted by `Corpus.emit`, and writable alone with
//  `--emit-render-text <dir>` for the case where the fixtures are not being
//  regenerated (the `--emit-fidelity` precedent). A stale-artefact guard in
//  `RenderTextTests` asserts byte-equality with the committed file.
// ============================================================================

open System.IO
open System.Text.Json
open System.Text.Encodings.Web

open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

/// Stable identifier for the published artefact. The `/v1/` segment pins the
/// wire-format major version, matching `schema.json` and `render-fidelity.json`.
[<Literal>]
let artifactId = "https://fuaran.dev/wire-format/v1/render-text.json"

/// The artefact's file name at the corpus root.
[<Literal>]
let fileName = "render-text.json"

/// The family's CLOSED slot vocabulary, enumerated in the artefact so a reading
/// host can report a slot it has no reader for instead of silently accepting
/// one it cannot name — the `obligationVocabulary` posture of §13, for the same
/// reason.
///
/// Closed, and deliberately small: it carries exactly the slots the seeded
/// vectors read. Extending it is a change to this list, to the artefact, and to
/// every adopting host's reader in one change-set, under the §11
/// forward-coupling rule.
let slotVocabulary: (string * string) list =
    [ "Fact.value", "the `value` TextSource of a `Fact` — the figure beside its label"
      "Markdown.text", "the `text` TextSource of a `Markdown` node — its raw source before the GFM render (§14)" ]

/// The text slot a vector names, out of a decoded node. `None` for a slot the
/// vocabulary above does not carry, and for a node whose kind does not match
/// the slot's — both of which the caller reports by name rather than skipping.
let tryTextSlot (slot: string) (node: Node<obj>) : TextSource option =
    match slot, node.Kind with
    | "Fact.value", NodeKind.Fact spec -> Some spec.Value
    | "Markdown.text", NodeKind.Markdown spec -> Some spec.Text
    | _ -> None

/// The node with `id` inside a decoded fixture tree, or `None`.
///
/// Descends the STRUCTURAL container kinds the family's fixtures use. A local
/// walk rather than a call into the renderer's `childNodes`: that lives in the
/// Fable renderer package, which this project does not reference, and the
/// family's fixtures are Box-rooted by construction. A vector naming an id this
/// walk cannot reach fails loudly in `resolvedText` below, which is the honest
/// signal to widen the walk rather than to widen it speculatively now.
let rec tryFindNode (id: string) (node: Node<obj>) : Node<obj> option =
    if node.Id = id then
        Some node
    else
        let children =
            match node.Kind with
            | NodeKind.Box spec -> spec.Children
            | NodeKind.SplitPanel spec -> spec.Children
            | _ -> []

        children |> List.tryPick (tryFindNode id)

/// The `BindingSources` a vector's pinned sources denote. The three-member
/// shape Phase 1663 decided, projected onto the reference host's record: the
/// identity-keyed maps stay empty (no seeded vector pins a host value), the
/// instant and the locale come from the vector.
let sourcesOf (pinned: RenderTextFixtures.PinnedSources) : Fuaran.UI.BindingSources =
    { Fuaran.UI.BindingSources.empty with
        Now = pinned.Now
        Locale = pinned.Locale }

/// Render one vector against a corpus root: read the named fixture, decode it,
/// find the named node, resolve the named slot under the pinned sources.
///
/// Every failure is a `failwithf` rather than an `Error`, because each one means
/// the artefact would otherwise publish a claim nobody can evaluate — a fixture
/// that moved, a node id that is not in it, a slot the vocabulary does not
/// carry, a document that no longer decodes.
let resolvedText (corpusRoot: string) (v: RenderTextFixtures.Vector) : string =
    let path =
        Path.Combine(corpusRoot, v.Fixture.Replace('/', Path.DirectorySeparatorChar))

    if not (File.Exists path) then
        failwithf "render-text vector '%s' names a fixture that is not in the corpus: %s" v.Id v.Fixture

    let node =
        match decodeNodeObj (File.ReadAllText path) with
        | Ok n -> n
        | Error e -> failwithf "render-text vector '%s': fixture %s did not decode — %s" v.Id v.Fixture e.Message

    let target =
        match tryFindNode v.NodeId node with
        | Some t -> t
        | None -> failwithf "render-text vector '%s': fixture %s carries no node with id '%s'" v.Id v.Fixture v.NodeId

    let slot =
        match tryTextSlot v.Slot target with
        | Some s -> s
        | None ->
            failwithf
                "render-text vector '%s': slot '%s' is not in the family's closed vocabulary, or node '%s' is not of that kind"
                v.Id
                v.Slot
                v.NodeId

    Fuaran.UI.Renderer.BindingResolver.resolveTextSource (sourcesOf v.Sources) slot

let private description =
    "The Fuaran render-TEXT conformance family (WIRE_FORMAT.md 13). Each vector is "
    + "(fixture, pinned sources, expected text): `fixture` + `nodeId` + `slot` name a committed node "
    + "fixture and the text slot inside it, `sources` PINS the host instant (`now`, an ISO-8601 UTC "
    + "string; `\"\"` means this host furnishes no clock), the ambient BCP-47 locale (`locale`; `\"\"` "
    + "means the runtime default) and the identity-keyed host value map (`values`), and `expectedText` "
    + "is the text every conformant host must produce for that slot. The sources are pinned because "
    + "that is what makes the family checkable at all: a `Binding.Now` slot rendered against the "
    + "machine's own clock has no expected text. `expectedText` is the FALLBACK-tier render (13 tier "
    + "2) - the deterministic, non-Intl text a no-JS reader, a crawler, an email client or a "
    + "non-browser host receives. `slotVocabulary` is CLOSED, so a host can report a slot it has no "
    + "reader for rather than silently accept one it cannot name; `excluded` enumerates the slots the "
    + "family deliberately does NOT pin, each with the reason - those are the legitimately-differing "
    + "tier, where an Intl-backed host and a stdlib-only host produce different bytes for the same "
    + "input and both are correct. Generated from the reference host's own resolver, which PROVES "
    + "every vector before this file is written. See WIRE_FORMAT.md 13."

/// Render the artefact. `corpusRoot` is where the vectors' fixtures are read
/// from — the same directory the artefact is written into, so the family
/// certifies the corpus's own committed bytes.
let toJson (corpusRoot: string) : string =
    // Every emitted artefact writes LF on every platform: a CR is invisible to
    // `git status` under the corpus's `eol=lf` normalisation and visible only to
    // a consumer that byte-compares the working tree.
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)
    w.WriteString("description", description)

    w.WriteStartArray("slotVocabulary")

    for (slot, meaning) in slotVocabulary do
        w.WriteStartObject()
        w.WriteString("slot", slot)
        w.WriteString("meaning", meaning)
        w.WriteEndObject()

    w.WriteEndArray()

    w.WriteStartArray("excluded")

    for e in RenderTextFixtures.excluded do
        w.WriteStartObject()
        w.WriteString("slot", e.Slot)
        w.WriteString("fixture", e.Fixture)
        w.WriteString("nodeId", e.NodeId)
        w.WriteString("reason", e.Reason)
        w.WriteEndObject()

    w.WriteEndArray()

    w.WriteStartArray("vectors")

    for v in RenderTextFixtures.all do
        // THE PROOF. The reference host renders the slot and must agree with the
        // authored expectation, or nothing is written.
        let produced = resolvedText corpusRoot v

        if produced <> v.ExpectedText then
            failwithf
                "render-text vector '%s' violates its own expectation:\n  slot     → %s at %s in %s\n  expected → %s\n  produced → %s"
                v.Id
                v.Slot
                v.NodeId
                v.Fixture
                v.ExpectedText
                produced

        w.WriteStartObject()
        w.WriteString("id", v.Id)
        w.WriteString("fixture", v.Fixture)
        w.WriteString("nodeId", v.NodeId)
        w.WriteString("slot", v.Slot)

        w.WriteStartObject("sources")
        w.WriteString("now", v.Sources.Now)
        w.WriteString("locale", v.Sources.Locale)
        w.WriteStartObject("values")
        w.WriteEndObject()
        w.WriteEndObject()

        w.WriteString("expectedText", v.ExpectedText)
        w.WriteString("description", v.Description)
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the artefact into a corpus directory.
let write (outputDir: string) : unit =
    File.WriteAllText(Path.Combine(outputDir, fileName), toJson outputDir)
