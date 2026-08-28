module Fuaran.UI.JsonDecode.Tests.Corpus

// ============================================================================
//  Language-neutral wire-format conformance corpus.
//
//  The canonical wire-format artefact is the spec (`fuaran-dotnet/docs/WIRE_FORMAT.md`)
//  + this corpus (`wire-format-fixtures/` at the WORKSPACE ROOT — a sibling of
//  the `fuaran-dotnet/` repo, shared with the Wave 9 TypeScript host and any future
//  conformant host). F# is one conformant host of that contract.
//
//  Two halves:
//    - `emit outputDir` writes the corpus from the F# fixture values
//      (`Fixtures.allNodes` / `allOps` run through the canonical encoder, and
//      `RejectFixtures.all` written verbatim). This is the GENERATOR — re-run
//      it whenever a fixture is added (the forward-coupling rule). Invoked via
//      `dotnet run --project <thisproject> -- --emit-corpus <dir>`.
//    - `load ()` reads `manifest.json` at test time and returns the fixture
//      index. The round-trip + reject suites drive off this — they read the
//      JSON payloads, never the F# fixture values, so the assertions a second
//      host (the TS decoder) runs are byte-identical to the F# ones.
//
//  Cross-repo note: the corpus lives at workspace root, OUTSIDE the `fuaran-dotnet/`
//  git repo. `findRoot` walks up from the test binary to find it. This binds
//  the JsonDecode test suite to the Fuaran *workspace* checkout (siblings
//  cloned together) rather than a standalone `fuaran-dotnet/` clone — which is the
//  intended posture: the corpus is a workspace-level shared artefact.
// ============================================================================

open System
open System.IO
open System.Text.Json
open System.Text.Encodings.Web
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

[<Literal>]
let corpusDirName = "wire-format-fixtures"

/// `JsonDocument` options that admit exactly what the wire format's SYNTACTIC
/// bound admits (`WireLimits.MaxJsonDepth`).
///
/// `System.Text.Json` defaults to a maximum nesting of 64, which is far inside
/// that bound and — the part that bites — below what a single `MaxDepth`-deep
/// TREE costs: one node level is several JSON levels, so the §21 accept fixture
/// at exactly 24 node levels is 72 JSON levels. A harness left on the default
/// throws on a payload the decoder accepts, and then reports a conformance
/// failure whose only out-of-contract party is the harness's own parser. Every
/// corpus-reading parse in this suite uses these options; `JsonDecode` itself
/// enforces the real bound and refuses `MaxJsonDepth + 1` with LIMIT_EXCEEDED.
let wireJsonOptions =
    JsonDocumentOptions(MaxDepth = Fuaran.UI.WireLimits.MaxJsonDepth)

/// One row of `manifest.json`. Round-trip fixtures carry `ExpectedFile`
/// (identical to `InputFile` — the conformance assertion is
/// `encode(decode(inputFile)) = expectedFile`); reject fixtures carry
/// `ExpectedErrorCode` + `ExpectedPath`.
type FixtureEntry =
    {
        Id: string
        Kind: string
        InputFile: string
        ExpectedFile: string option
        ExpectedErrorCode: string option
        ExpectedPath: string option
        /// Which decoder entry point a conformant host invokes for this fixture:
        /// `"node"` ⇒ `decodeNode`, `"op"` ⇒ `decodeOp`. Present on every entry.
        Decoder: string
        Description: string
    }

let private nodeIdStr (n: Node<obj>) : string = n.Id

let private opId (name: string) : string = "op-" + name.ToLowerInvariant()

/// Phase 746 — every `FormFieldKind` discriminator carried by an encoded node.
///
/// The control vocabulary rides in exactly TWO wire positions (WIRE_FORMAT §11:
/// one vocabulary, two carriers): a `Form` spec's `fields[]` and a `Filters`
/// spec's `items[]`. Both are matched by the PARENT discriminator rather than by
/// property name, because `DataGrid.columns[].kind.$type` is a `CellKindErased`
/// and shares the token `Text` with `FormFieldKind` — a property-name sweep
/// silently attests the wrong family.
let private controlKindsOf (root: JsonElement) : string list =
    let acc = ResizeArray<string>()

    let controlTag (el: JsonElement) : unit =
        if el.ValueKind = JsonValueKind.Object then
            match el.TryGetProperty "kind" with
            | true, k when k.ValueKind = JsonValueKind.Object ->
                match k.TryGetProperty "$type" with
                | true, t ->
                    match t.GetString() with
                    | null -> ()
                    | s -> acc.Add s
                | _ -> ()
            | _ -> ()

    let rec walk (el: JsonElement) : unit =
        match el.ValueKind with
        | JsonValueKind.Object ->
            let carrier =
                match el.TryGetProperty "$type" with
                | true, t ->
                    match t.GetString() with
                    | "Form" -> Some "fields"
                    | "Filters" -> Some "items"
                    | _ -> None
                | _ -> None

            match carrier with
            | Some prop ->
                match el.TryGetProperty prop with
                | true, arr when arr.ValueKind = JsonValueKind.Array -> Seq.iter controlTag (arr.EnumerateArray())
                | _ -> ()
            | None -> ()

            for p in el.EnumerateObject() do
                walk p.Value
        | JsonValueKind.Array ->
            for v in el.EnumerateArray() do
                walk v
        | _ -> ()

    walk root
    List.ofSeq acc

// ─── Emit (generator) ───────────────────────────────────────────────────────

let private writeManifest
    (outputDir: string)
    (kinds: string list)
    (formFieldKinds: string list)
    (entries: FixtureEntry list)
    : unit =
    let sorted = entries |> List.sortBy (fun e -> e.Kind, e.Id)
    // Relaxed escaping keeps the human-readable descriptions clean (no
    // + / — noise) — this is a spec artefact a human reads.
    //
    // `NewLine = "\n"` is load-bearing on Windows. `Utf8JsonWriter` indents with
    // `Environment.NewLine`, so a regen there wrote CRLF into the one corpus file
    // that has newlines at all (every fixture is a single line). The corpus
    // `.gitattributes` pins `eol=lf`, so git normalised it on commit and `git
    // status` stayed clean — while consumers that byte-compare the WORKING TREE
    // saw drift they could not fix: `fuaran-ts`'s bundled snapshot check reported
    // "identical content, different line endings" and re-running its sync could
    // not clear it, because the authority itself was the CRLF copy.
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = File.Create(Path.Combine(outputDir, "manifest.json"))
    use w = new Utf8JsonWriter(stream, opts)
    w.WriteStartObject()
    w.WriteNumber("version", 1)
    // Pointer to the generated Draft 2020-12 JSON Schema (Phase 96) so a host
    // can discover it from the manifest. Co-emitted by `emit` below.
    w.WriteString("schema", "schema.json")

    // Pointer to the canonical IDL vocabulary artifact (Phase 696) — the same
    // discovery affordance as `schema` above, for the artifact that answers the
    // OTHER question: `schema.json` is the validation surface ("is this payload
    // legal?"), `idl.json` the structural source ("what IS the vocabulary?" —
    // field tables, optionality classes, omit-at-default VALUES and enum
    // vocabularies, none of which survive a JSON Schema projection).
    //
    // Unlike `schema.json` this artifact is NOT co-emitted here: its encoder and
    // the vocabulary it renders both live in Fuaran.Core's test project, which
    // ships in no package and so is unreachable from this repo. It is emitted and
    // drift-guarded on that side; this manifest only points at it. A conformant
    // host reads the pointer, never the emitter. See WIRE_FORMAT.md §13.
    w.WriteString("idl", "idl.json")

    w.WriteString(
        "description",
        "Fuaran canonical wire-format conformance corpus. node-round-trip / op-round-trip "
        + "fixtures: decode inputFile, re-encode, assert byte-equal to expectedFile. reject "
        + "fixtures: decode inputFile, assert DecodeError.Code = expectedErrorCode and "
        + "DecodeError.Path starts with expectedPath. lenient-accept fixtures (WIRE_FORMAT "
        + "16): decode the SHORTHAND inputFile, re-encode, assert byte-equal to expectedFile "
        + "(the verbose canonical form) — a conformant host MUST accept the shorthand and "
        + "normalise it; rejecting it, or decoding it to different bytes, is non-conformant. "
        + "envelope-round-trip / envelope-reject fixtures (WIRE_FORMAT 15): read the $profile/"
        + "$payload envelope, negotiate the authored profile against the host's own core@1.0, "
        + "then either re-render byte-equal to expectedFile (Current/Behind — unknown kinds "
        + "preserved verbatim, must-ignore-but-preserve) or refuse a Foreign profile with "
        + "expectedErrorCode FOREIGN_PROFILE at expectedPath. "
        + "elicitation-round-trip / elicitation-reject fixtures (WIRE_FORMAT 18): decode with the "
        + "decoder-named entry point (elicitation = the envelope codec, elicitation-outcome = the "
        + "outcome codec), then either re-encode byte-equal to expectedFile or assert the structured "
        + "refusal (expectedErrorCode at a path starting with expectedPath). elicitation-answer-accept / "
        + "elicitation-answer-reject fixtures (WIRE_FORMAT 18.4): the inputFile pairs {answer, contract}; "
        + "run the host's answer-conformance validation and assert acceptance or the expected refusal. "
        + "contract-card-round-trip / contract-card-reject fixtures (WIRE_FORMAT 25): decode with the "
        + "decoder-named entry point (contract-card = the single-card codec, contract-card-bundle = the "
        + "bundle codec), then either re-encode byte-equal to expectedFile or assert the structured "
        + "refusal (expectedErrorCode at a path starting with expectedPath). A card is NOT a node — it "
        + "is the description a host reads to label and prop-validate a Custom node it has no renderer "
        + "for — so it is its own family and never appears in nodes/. "
        + "See fuaran-dotnet/docs/WIRE_FORMAT.md."
    )

    // Phase 548 — the canonical NodeKind enumeration: the emittable `kind.$type`
    // vocabulary the corpus exercises (the distinct kind names over the node
    // round-trip fixtures — canonical only; legacy decode-upgrade tags never
    // appear in a canonical node fixture). Generated here so it regenerates with
    // the corpus and cannot drift from the F# reference; every conformant host
    // pins its decoder's kind set against this (WIRE_FORMAT §11 cross-host
    // attestation guard). A host missing a kind fails its pin with the kind named.
    w.WriteStartArray("kinds")

    for k in kinds do
        w.WriteStringValue(k)

    w.WriteEndArray()

    // Phase 746 — the canonical FormFieldKind enumeration: the CONTROL
    // `kind.$type` vocabulary the corpus exercises, derived exactly the way
    // `kinds` is (from the encoded node round-trip fixtures, never a hand list).
    // `kinds` attests the NodeKind family; this attests the control family the
    // Go comment at `conformance_test.go` explicitly excluded from the node
    // sweep — the exclusion that let `DateRange` sit unadopted in four hosts.
    // Every conformant host pins its control vocabulary against this list.
    w.WriteStartArray("formFieldKinds")

    for k in formFieldKinds do
        w.WriteStringValue(k)

    w.WriteEndArray()

    w.WriteStartArray("fixtures")

    for e in sorted do
        w.WriteStartObject()
        w.WriteString("id", e.Id)
        w.WriteString("kind", e.Kind)
        w.WriteString("decoder", e.Decoder)
        w.WriteString("inputFile", e.InputFile)
        e.ExpectedFile |> Option.iter (fun f -> w.WriteString("expectedFile", f))

        e.ExpectedErrorCode
        |> Option.iter (fun c -> w.WriteString("expectedErrorCode", c))

        e.ExpectedPath |> Option.iter (fun p -> w.WriteString("expectedPath", p))
        w.WriteString("description", e.Description)
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

/// Regenerate the corpus at `outputDir` from the F# fixture values. Deletes
/// and rewrites the three payload subdirectories so stale fixtures don't
/// linger after a rename.
let emit (outputDir: string) : unit =
    let nodesDir = Path.Combine(outputDir, "nodes")
    let opsDir = Path.Combine(outputDir, "ops")
    let rejectDir = Path.Combine(outputDir, "reject")
    let lenientDir = Path.Combine(outputDir, "lenient")
    let envelopeDir = Path.Combine(outputDir, "envelope")
    let elicitationDir = Path.Combine(outputDir, "elicitation")
    let cardsDir = Path.Combine(outputDir, "cards")

    for d in
        [ nodesDir
          opsDir
          rejectDir
          lenientDir
          envelopeDir
          elicitationDir
          cardsDir ] do
        if Directory.Exists d then
            Directory.Delete(d, true)

        Directory.CreateDirectory d |> ignore

    let entries = ResizeArray<FixtureEntry>()

    for (desc, node) in Fixtures.allNodes do
        let id = nodeIdStr node
        let rel = "nodes/" + id + ".json"
        File.WriteAllText(Path.Combine(nodesDir, id + ".json"), CanonicalJson.encodeNode node)

        entries.Add
            { Id = id
              Kind = "node-round-trip"
              InputFile = rel
              ExpectedFile = Some rel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = "node"
              Description = desc }

    // Node fixtures whose payload is a stored wire string keyed by an explicit
    // corpus id (the §21 shape-limit family — see `Fixtures.storedNodes` for why
    // they cannot be `Node` values, and why they are declared rather than
    // hand-authored into the corpus).
    for (id, desc, payload) in Fixtures.storedNodes do
        let rel = "nodes/" + id + ".json"
        File.WriteAllText(Path.Combine(nodesDir, id + ".json"), payload)

        entries.Add
            { Id = id
              Kind = "node-round-trip"
              InputFile = rel
              ExpectedFile = Some rel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = "node"
              Description = desc }

    for (name, op) in Fixtures.allOps do
        let id = opId name
        let rel = "ops/" + id + ".json"
        File.WriteAllText(Path.Combine(opsDir, id + ".json"), CanonicalJson.encodeOp op)

        entries.Add
            { Id = id
              Kind = "op-round-trip"
              InputFile = rel
              ExpectedFile = Some rel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = "op"
              Description = name }

    // Lenient-accept family (WIRE_FORMAT §16). The emitter itself PROVES the
    // §16 normalization law before writing anything: the shorthand and its
    // verbose twin must decode to byte-identical canonical re-encodings —
    // `encode(decode(shorthand)) == encode(decode(verbose))`. The proven bytes
    // become the fixture's expectedFile, so every conformant host is held to
    // exactly the law the F# reference just demonstrated.
    for lf in LenientFixtures.all do
        let decodeOrFail (label: string) (json: string) : string =
            match decodeNodeObj json with
            | Ok node -> CanonicalJson.encodeNode node
            | Error e -> failwithf "lenient fixture '%s': %s form failed to decode — %s" lf.Id label e.Message

        let fromShorthand = decodeOrFail "shorthand" lf.LenientJson
        let fromVerbose = decodeOrFail "verbose" lf.VerboseJson

        if fromShorthand <> fromVerbose then
            failwithf
                "lenient fixture '%s' violates the §16 normalization law:\n  shorthand → %s\n  verbose   → %s"
                lf.Id
                fromShorthand
                fromVerbose

        let inputRel = "lenient/" + lf.Id + ".json"
        let expectedRel = "lenient/" + lf.Id + ".expected.json"
        File.WriteAllText(Path.Combine(lenientDir, lf.Id + ".json"), lf.LenientJson)
        File.WriteAllText(Path.Combine(lenientDir, lf.Id + ".expected.json"), fromVerbose)

        entries.Add
            { Id = lf.Id
              Kind = "lenient-accept"
              InputFile = inputRel
              ExpectedFile = Some expectedRel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = "node"
              Description = lf.Description }

    // Envelope / tolerance family (WIRE_FORMAT §15). Like the lenient family, the
    // emitter PROVES the §15 law before writing: a round-trip fixture must
    // negotiate (Current/Behind) → tolerantly decode (unknown kinds preserved)
    // → re-render byte-identical to the input; a Foreign fixture must refuse with
    // FOREIGN_PROFILE at $.$profile. The proof runs the reference F# host's
    // `negotiateReencode` (Fuaran.Core.Wire.Versioning bridged to the UI codec),
    // so every conformant host is held to exactly the law the F# reference
    // demonstrated. Round-trip fixtures carry expectedFile = inputFile (the
    // re-render equals the canonical input); Foreign fixtures carry
    // expectedErrorCode + expectedPath like a reject.
    for ef in EnvelopeFixtures.all do
        let rel = "envelope/" + ef.Id + ".json"
        File.WriteAllText(Path.Combine(envelopeDir, ef.Id + ".json"), ef.Enveloped)

        match ef.Expect with
        | EnvelopeFixtures.RoundTrip ->
            match EnvelopeFixtures.negotiateReencode ef.Enveloped with
            | Ok reRendered when reRendered = ef.Enveloped -> ()
            | Ok other ->
                failwithf
                    "envelope fixture '%s' violates the §15 round-trip law:\n  in  → %s\n  out → %s"
                    ef.Id
                    ef.Enveloped
                    other
            | Error(code, path) ->
                failwithf "envelope fixture '%s' expected a round-trip but negotiate refused (%s at %s)" ef.Id code path

            entries.Add
                { Id = ef.Id
                  Kind = "envelope-round-trip"
                  InputFile = rel
                  ExpectedFile = Some rel
                  ExpectedErrorCode = None
                  ExpectedPath = None
                  Decoder = "node"
                  Description = ef.Description }
        | EnvelopeFixtures.ForeignRefuse ->
            match EnvelopeFixtures.negotiateReencode ef.Enveloped with
            | Error("FOREIGN_PROFILE", _) -> ()
            | Error(code, path) ->
                failwithf "envelope fixture '%s' expected FOREIGN_PROFILE, got %s at %s" ef.Id code path
            | Ok _ -> failwithf "envelope fixture '%s' expected a Foreign refusal but negotiate accepted it" ef.Id

            entries.Add
                { Id = ef.Id
                  Kind = "envelope-reject"
                  InputFile = rel
                  ExpectedFile = None
                  ExpectedErrorCode = Some "FOREIGN_PROFILE"
                  ExpectedPath = Some "$.$profile"
                  Decoder = "node"
                  Description = ef.Description }

    // Elicitation family (WIRE_FORMAT §18). Like the lenient/envelope families,
    // the emitter PROVES each law before writing: a round-trip fixture must
    // decode + re-encode byte-identically through its `decoder`-named entry
    // point; a reject must fail with exactly the expected code at the expected
    // path; an answer-conformance document must validate / refuse as declared.
    for ef in ElicitationFixtures.roundTrips do
        let rel = "elicitation/" + ef.Id + ".json"
        File.WriteAllText(Path.Combine(elicitationDir, ef.Id + ".json"), ef.Wire)

        match ElicitationFixtures.decodeReencode ef.Decoder ef.Wire with
        | Ok reencoded when reencoded = ef.Wire -> ()
        | Ok other ->
            failwithf
                "elicitation fixture '%s' violates the §18 round-trip law:\n  in  → %s\n  out → %s"
                ef.Id
                ef.Wire
                other
        | Error e ->
            failwithf "elicitation fixture '%s' expected a round-trip but decode refused (%s at %s)" ef.Id e.Code e.Path

        entries.Add
            { Id = ef.Id
              Kind = "elicitation-round-trip"
              InputFile = rel
              ExpectedFile = Some rel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = ef.Decoder
              Description = ef.Description }

    for ef in ElicitationFixtures.rejects do
        let rel = "elicitation/" + ef.Id + ".json"
        File.WriteAllText(Path.Combine(elicitationDir, ef.Id + ".json"), ef.Wire)

        match ef.Expect with
        | ElicitationFixtures.RoundTrip ->
            failwithf "elicitation fixture '%s' is in the reject list but expects a round-trip" ef.Id
        | ElicitationFixtures.Refuse(code, path) ->
            match ElicitationFixtures.decodeReencode ef.Decoder ef.Wire with
            | Error e when e.Code = code && e.Path.StartsWith path -> ()
            | Error e ->
                failwithf "elicitation fixture '%s' expected %s at %s, got %s at %s" ef.Id code path e.Code e.Path
            | Ok _ -> failwithf "elicitation fixture '%s' expected a refusal but decode accepted it" ef.Id

            entries.Add
                { Id = ef.Id
                  Kind = "elicitation-reject"
                  InputFile = rel
                  ExpectedFile = None
                  ExpectedErrorCode = Some code
                  ExpectedPath = Some path
                  Decoder = ef.Decoder
                  Description = ef.Description }

    for af in ElicitationFixtures.answerDocs do
        let rel = "elicitation/" + af.Id + ".json"
        File.WriteAllText(Path.Combine(elicitationDir, af.Id + ".json"), af.Wire)

        match af.Expect with
        | ElicitationFixtures.Accept ->
            match Fuaran.UI.OpStream.Abstractions.Elicitation.validateAnswerDocument af.Wire with
            | Ok() -> ()
            | Error e ->
                failwithf "elicitation answer fixture '%s' expected acceptance, got %s at %s" af.Id e.Code e.Path

            entries.Add
                { Id = af.Id
                  Kind = "elicitation-answer-accept"
                  InputFile = rel
                  ExpectedFile = Some rel
                  ExpectedErrorCode = None
                  ExpectedPath = None
                  Decoder = "elicitation-answer"
                  Description = af.Description }
        | ElicitationFixtures.RefuseAnswer(code, path) ->
            match Fuaran.UI.OpStream.Abstractions.Elicitation.validateAnswerDocument af.Wire with
            | Error e when e.Code = code && e.Path.StartsWith path -> ()
            | Error e ->
                failwithf
                    "elicitation answer fixture '%s' expected %s at %s, got %s at %s"
                    af.Id
                    code
                    path
                    e.Code
                    e.Path
            | Ok() -> failwithf "elicitation answer fixture '%s' expected a refusal but validation accepted it" af.Id

            entries.Add
                { Id = af.Id
                  Kind = "elicitation-answer-reject"
                  InputFile = rel
                  ExpectedFile = None
                  ExpectedErrorCode = Some code
                  ExpectedPath = Some path
                  Decoder = "elicitation-answer"
                  Description = af.Description }

    // Contract-card family (WIRE_FORMAT §25). Its own family and its own
    // directory: a card is not a node, so the node family's round-trip law —
    // stated over `CanonicalJson.encodeNode` — has nothing to say about it, and
    // folding these in would have changed what every host's node-corpus leg was
    // asserting. Same emitter-proves-the-law discipline as the families above.
    for cf in CardFixtures.roundTrips do
        let rel = "cards/" + cf.Id + ".json"
        File.WriteAllText(Path.Combine(cardsDir, cf.Id + ".json"), cf.Wire)

        match CardFixtures.decodeReencode cf.Decoder cf.Wire with
        | Ok reencoded when reencoded = cf.Wire -> ()
        | Ok other ->
            failwithf "card fixture '%s' violates the §25 round-trip law:\n  in  → %s\n  out → %s" cf.Id cf.Wire other
        | Error e ->
            failwithf "card fixture '%s' expected a round-trip but decode refused (%s at %s)" cf.Id e.Code e.Path

        entries.Add
            { Id = cf.Id
              Kind = "contract-card-round-trip"
              InputFile = rel
              ExpectedFile = Some rel
              ExpectedErrorCode = None
              ExpectedPath = None
              Decoder = cf.Decoder
              Description = cf.Description }

    for cf in CardFixtures.rejects do
        let rel = "cards/" + cf.Id + ".json"
        File.WriteAllText(Path.Combine(cardsDir, cf.Id + ".json"), cf.Wire)

        match cf.Expect with
        | CardFixtures.RoundTrip -> failwithf "card fixture '%s' is in the reject list but expects a round-trip" cf.Id
        | CardFixtures.Refuse(code, path) ->
            match CardFixtures.decodeReencode cf.Decoder cf.Wire with
            | Error e when e.Code = code && e.Path.StartsWith path -> ()
            | Error e -> failwithf "card fixture '%s' expected %s at %s, got %s at %s" cf.Id code path e.Code e.Path
            | Ok _ -> failwithf "card fixture '%s' expected a refusal but decode accepted it" cf.Id

            entries.Add
                { Id = cf.Id
                  Kind = "contract-card-reject"
                  InputFile = rel
                  ExpectedFile = None
                  ExpectedErrorCode = Some code
                  ExpectedPath = Some path
                  Decoder = cf.Decoder
                  Description = cf.Description }

    for rf in RejectFixtures.all do
        let rel = "reject/" + rf.Id + ".json"
        File.WriteAllText(Path.Combine(rejectDir, rf.Id + ".json"), rf.Json)

        entries.Add
            { Id = rf.Id
              Kind = "reject"
              InputFile = rel
              ExpectedFile = None
              ExpectedErrorCode = Some(DecodeErrorCode.toString rf.ExpectedCode)
              ExpectedPath = Some rf.ExpectedPath
              Decoder = (if rf.IsOp then "op" else "node")
              Description = rf.Description }

    // Co-emit the canonical wire-format JSON Schema (Phase 96) as the
    // published `schema.json` artefact. Generated from the same type contract
    // the corpus exercises (Fuaran.UI.Ops.SchemaGen); the stale-schema guard
    // test asserts byte-equality between this file and SchemaGen.wireFormatSchema.
    File.WriteAllText(Path.Combine(outputDir, "schema.json"), Fuaran.UI.Ops.SchemaGen.wireFormatSchema)

    // Co-emit the canonical pre-emit DEFECT VOCABULARY (Phase 669). Distinct
    // from `schema.json`, which answers "is this payload legal on the wire?" —
    // this answers "what may a conformant host's pre-emit validator refuse?",
    // an AUTHORING-contract question the byte-parity legs are structurally blind
    // to. It is generated by reflecting over the defect DU and asking
    // `PreEmitValidate.describe`, so a new defect case lands here with no edit.
    Directory.CreateDirectory(Path.Combine(outputDir, "validator")) |> ignore

    File.WriteAllText(Path.Combine(outputDir, "validator", "defect-vocabulary.json"), DefectVocabulary.toJson ())

    // Co-emit the per-NodeKind RENDER-FIDELITY manifest (Phase 442). A fourth
    // question again: `schema.json` asks what is legal, `idl.json` what the
    // vocabulary is, `defect-vocabulary.json` what a validator may refuse — and
    // this one, for a given kind, which render tiers exist, what the
    // parity-checked fallback pins, and what is declared client-only rich. It
    // is generated from the `Fuaran.UI.RenderFidelity` declaration and changes
    // no wire byte. `--emit-fidelity <dir>` writes only this file, for the case
    // where the fixtures are not being regenerated.
    RenderFidelityArtifact.write outputDir

    // The canonical NodeKind enumeration is the set of true wire `kind.$type`
    // discriminators over the node round-trip fixtures — extracted from the
    // *encoded* bytes, NOT `Kind.name` (which is a display tag that diverges from
    // the wire name for DataGrid: `Kind.name` → "Grid", wire `$type` → "DataGrid").
    let kinds =
        Fixtures.allNodes
        |> List.map (fun (_, n) ->
            use doc = JsonDocument.Parse(CanonicalJson.encodeNode n)

            match doc.RootElement.GetProperty("kind").GetProperty("$type").GetString() with
            | null -> failwithf "node fixture '%s' has no kind.$type discriminator" (nodeIdStr n)
            | s -> s)
        |> List.distinct
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    // The canonical FormFieldKind enumeration — same derivation, same source
    // bytes, one wire position deeper (see `controlKindsOf`).
    let formFieldKinds =
        Fixtures.allNodes
        |> List.collect (fun (_, n) ->
            use doc = JsonDocument.Parse(CanonicalJson.encodeNode n)
            controlKindsOf doc.RootElement)
        |> List.distinct
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    writeManifest outputDir kinds formFieldKinds (List.ofSeq entries)

    printfn
        "Emitted %d fixtures + %d kinds + %d form-field kinds + schema.json to %s"
        entries.Count
        kinds.Length
        formFieldKinds.Length
        outputDir

// ─── Load (test-time index) ──────────────────────────────────────────────────

/// Walk up from the test binary's base directory to find the workspace-root
/// `wire-format-fixtures/` corpus. Fails loudly if absent.
let findRoot () : string =
    let rec climb (dir: DirectoryInfo | null) : string option =
        match dir with
        | null -> None
        | d ->
            let candidate = Path.Combine(d.FullName, corpusDirName, "manifest.json")

            if File.Exists candidate then
                Some(Path.Combine(d.FullName, corpusDirName))
            else
                climb d.Parent

    match climb (DirectoryInfo(AppContext.BaseDirectory)) with
    | Some root -> root
    | None ->
        failwithf
            "%s/manifest.json not found walking up from %s. The JsonDecode conformance suite requires the Fuaran workspace checkout (corpus lives at workspace root, a sibling of the fuaran-dotnet/ repo). Regenerate with: dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus <workspace-root>/%s"
            corpusDirName
            AppContext.BaseDirectory
            corpusDirName

let private requireStr (el: JsonElement) (name: string) : string =
    match el.TryGetProperty name with
    | true, v ->
        match v.GetString() with
        | null -> failwithf "manifest fixture property '%s' is null" name
        | s -> s
    | _ -> failwithf "manifest fixture missing required property '%s'" name

let private optStr (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v ->
        match v.GetString() with
        | null -> None
        | s -> Some s
    | _ -> None

/// Parse `manifest.json` into the fixture index.
let load () : string * FixtureEntry list =
    let root = findRoot ()
    let manifestText = File.ReadAllText(Path.Combine(root, "manifest.json"))
    use doc = JsonDocument.Parse manifestText

    let entries =
        doc.RootElement.GetProperty("fixtures").EnumerateArray()
        |> Seq.map (fun el ->
            { Id = requireStr el "id"
              Kind = requireStr el "kind"
              InputFile = requireStr el "inputFile"
              ExpectedFile = optStr el "expectedFile"
              ExpectedErrorCode = optStr el "expectedErrorCode"
              ExpectedPath = optStr el "expectedPath"
              Decoder = requireStr el "decoder"
              Description = requireStr el "description" })
        |> List.ofSeq

    root, entries

/// Read a fixture payload by its manifest-relative path (e.g. "nodes/metric-1.json").
let readPayload (root: string) (relativePath: string) : string =
    let native = relativePath.Replace('/', Path.DirectorySeparatorChar)
    File.ReadAllText(Path.Combine(root, native))

let private loadStringArray (property: string) : string list =
    let root = findRoot ()
    let manifestText = File.ReadAllText(Path.Combine(root, "manifest.json"))
    use doc = JsonDocument.Parse manifestText

    match doc.RootElement.TryGetProperty property with
    | true, arr ->
        arr.EnumerateArray()
        |> Seq.map (fun el ->
            match el.GetString() with
            | null -> failwithf "manifest '%s' entry is null" property
            | s -> s)
        |> List.ofSeq
    | _ -> failwithf "manifest.json has no '%s' array — regenerate with --emit-corpus" property

/// The canonical NodeKind enumeration (the `kinds` array) from manifest.json —
/// the Phase 548 cross-host kind-set attestation anchor. Every conformant host
/// pins its decoder's recognised kind set against this generated list.
let loadKinds () : string list = loadStringArray "kinds"

/// The canonical FormFieldKind enumeration (the `formFieldKinds` array) from
/// manifest.json — the Phase 746 cross-host CONTROL-vocabulary attestation
/// anchor, the second discriminator family to gain one.
let loadFormFieldKinds () : string list = loadStringArray "formFieldKinds"

/// Every `FormFieldKind` discriminator a corpus payload carries, in its two wire
/// carriers (`Form.fields[]` / `Filters.items[]`). Shared by the emitter's
/// derivation and the host attestation's fixture sweep, so the two cannot
/// disagree about where the control vocabulary lives.
let controlKindsIn (wire: string) : string list =
    use doc = JsonDocument.Parse(wire, wireJsonOptions)
    controlKindsOf doc.RootElement
