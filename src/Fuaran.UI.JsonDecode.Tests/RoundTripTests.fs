module Fuaran.UI.JsonDecode.Tests.RoundTrip

// ============================================================================
//  Round-trip acceptance against the corpus.
//
//  Strategy: the canonical wire payload lives in the
//  `wire-format-fixtures/` corpus at workspace root. Each round-trip case
//  reads its JSON file, decodes it, re-encodes the decoded tree, and asserts
//  the two canonical-JSON strings are byte-equal. This is the language-neutral
//  conformance assertion a second host (the Wave 9 TS decoder) runs verbatim —
//  it never touches the F# fixture VALUES, only the emitted JSON.
//
//  Closure-bearing slots collapse to the `"<closure>"` sentinel on both
//  encode passes, so a byte-equal round-trip proves the decoder rebuilt every
//  closure slot with a placeholder the encoder ALSO collapses to `"<closure>"`,
//  proving structural shape was preserved.
//
//  Coverage gate. `Fixtures.allNodes` + `Fixtures.allOps` are the canonical
//  enumeration that GENERATES the corpus (`Corpus.emit`). The coverage tests
//  below assert the corpus has one entry per F# fixture, so adding a fixture
//  without regenerating the corpus (`dotnet run -- --emit-corpus <dir>`) fails
//  loudly — the forward-coupling rule, now spanning the corpus.
// ============================================================================

open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

let private corpus = Corpus.load ()
let private corpusRoot = fst corpus
let private corpusEntries = snd corpus

let private nodeEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "node-round-trip")

let private opEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "op-round-trip")

let private lenientEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "lenient-accept")

let private envelopeEntries =
    corpusEntries
    |> List.filter (fun e -> e.Kind = "envelope-round-trip" || e.Kind = "envelope-reject")

let private roundTripNode (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Node — %s (%s) — decode/encode matches corpus" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match JsonDecode.decodeNodeObj wire with
        | Ok decoded ->
            let reencoded = CanonicalJson.encodeNode decoded
            Expect.equal reencoded wire "round-trip preserves canonical-JSON byte form"
        | Error err -> failtestf "decode failed: %A\n  on input: %s" err wire)

let private roundTripOp (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "TreeOp — %s (%s) — decode/encode matches corpus" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match JsonDecode.decodeOp wire with
        | Ok decoded ->
            let reencoded = CanonicalJson.encodeOp decoded
            Expect.equal reencoded wire "round-trip preserves canonical-JSON byte form"
        | Error err -> failtestf "decode failed: %A\n  on input: %s" err wire)

[<Tests>]
let nodeRoundTrips =
    nodeEntries
    |> List.map roundTripNode
    |> testList "Fuaran.UI.Ops.JsonDecode — Node round-trip (corpus)"

[<Tests>]
let opRoundTrips =
    opEntries
    |> List.map roundTripOp
    |> testList "Fuaran.UI.Ops.JsonDecode — TreeOp round-trip (corpus)"

// ── Lenient-accept (WIRE_FORMAT §16) — corpus-driven ────────────────────────
//
// The shorthand inputFile MUST decode and re-encode to exactly the verbose
// canonical expectedFile — the assertion every conformant host runs, making
// §16 corpus-enforceable rather than per-host-unit-test folklore. (The richer
// in-repo shorthand coverage lives in LenientIngestTests.fs; this suite pins
// the cross-host subset.)
let private lenientAccept (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Lenient — %s (%s) — shorthand normalises to canonical" e.Description e.Id) (fun () ->
        let shorthand = Corpus.readPayload corpusRoot e.InputFile

        let expected =
            match e.ExpectedFile with
            | Some f -> Corpus.readPayload corpusRoot f
            | None -> failtestf "lenient-accept fixture '%s' has no expectedFile" e.Id

        match JsonDecode.decodeNodeObj shorthand with
        | Ok decoded ->
            Expect.equal
                (CanonicalJson.encodeNode decoded)
                expected
                "the §16 shorthand decodes to the value its verbose form denotes (byte-equal canonical re-encode)"
        | Error err -> failtestf "a conformant decoder MUST accept the §16 shorthand; decode failed: %A" err)

[<Tests>]
let lenientAccepts =
    lenientEntries
    |> List.map lenientAccept
    |> testList "Fuaran.UI.Ops.JsonDecode — lenient-accept (corpus, §16)"

// ── Envelope / tolerance (WIRE_FORMAT §15) — corpus-driven ──────────────────
//
// The F# host re-proves at test time exactly what the emitter proved: a
// round-trip envelope negotiates (Current/Behind), tolerantly decodes its
// payload (unknown kinds preserved verbatim), and re-renders byte-identical to
// expectedFile; a Foreign envelope refuses with FOREIGN_PROFILE at $.$profile.
// The TS host re-proves the same via the conformance kit — so §15 is
// corpus-certified cross-host, not host-private (WIRE_FORMAT §15.5).
let private envelopeCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Envelope — %s (%s) — §15 negotiate/tolerate" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match e.Kind with
        | "envelope-round-trip" ->
            let expected =
                match e.ExpectedFile with
                | Some f -> Corpus.readPayload corpusRoot f
                | None -> wire

            match EnvelopeFixtures.negotiateReencode wire with
            | Ok out ->
                Expect.equal
                    out
                    expected
                    "the envelope negotiates + tolerantly decodes + re-renders byte-identically (must-ignore-but-preserve)"
            | Error(code, path) -> failtestf "expected a §15 round-trip; negotiate refused: %s at %s" code path
        | _ ->
            match EnvelopeFixtures.negotiateReencode wire with
            | Error(code, path) ->
                Expect.equal code (Option.defaultValue "" e.ExpectedErrorCode) "reject code matches the manifest"

                Expect.isTrue
                    (path.StartsWith(Option.defaultValue "$" e.ExpectedPath))
                    "reject path starts with the manifest prefix"
            | Ok _ -> failtest "expected a Foreign refusal; negotiate accepted the artifact")

[<Tests>]
let envelopeRoundTrips =
    envelopeEntries
    |> List.map envelopeCase
    |> testList "Fuaran.UI.Ops.JsonDecode — envelope/tolerance (corpus, §15)"

[<Tests>]
let coverageGate =
    testList
        "Fuaran.UI.Ops.JsonDecode — corpus coverage gate"
        [ testCase "node corpus has one entry per emitted node fixture" (fun () ->
              Expect.equal
                  (List.length nodeEntries)
                  (List.length Fixtures.allNodes + List.length Fixtures.storedNodes)
                  "node-round-trip corpus count must equal Fixtures.allNodes + Fixtures.storedNodes — regenerate with `--emit-corpus` after adding a fixture")

          // The §21 stored payloads are the one node family the emitter does not
          // derive from a `Node` value, so nothing else holds their generators to
          // the committed bytes. Without this pin a drifted generator would not
          // fail — it would silently rewrite the SHARED corpus on the next
          // `--emit-corpus`, in a repo this one does not track.
          testCase "stored node payloads are byte-identical to the committed corpus" (fun () ->
              for (id, _, payload) in Fixtures.storedNodes do
                  let committed = Corpus.readPayload corpusRoot ("nodes/" + id + ".json")

                  Expect.equal
                      payload
                      committed
                      (sprintf
                          "stored fixture '%s' has drifted from the committed corpus bytes — a regen would rewrite the shared corpus"
                          id))

          // The same pin for the reject family, which HAS always been emitted
          // from `RejectFixtures.all` and so had no need of one — until the §21
          // rejects joined the table and made the generators' byte-exactness
          // load-bearing. It costs one comparison per fixture and covers the
          // whole family rather than the four that motivated it.
          testCase "reject payloads are byte-identical to the committed corpus" (fun () ->
              for f in RejectFixtures.all do
                  let committed = Corpus.readPayload corpusRoot ("reject/" + f.Id + ".json")

                  Expect.equal
                      f.Json
                      committed
                      (sprintf
                          "reject fixture '%s' in RejectFixtures.all has drifted from the committed corpus bytes — regenerate with `--emit-corpus`"
                          f.Id))

          testCase "op corpus has one entry per Fixtures.allOps fixture" (fun () ->
              Expect.equal
                  (List.length opEntries)
                  (List.length Fixtures.allOps)
                  "op-round-trip corpus count must equal Fixtures.allOps — regenerate with `--emit-corpus` after adding a fixture")

          testCase "lenient corpus has one entry per LenientFixtures fixture" (fun () ->
              Expect.equal
                  (List.length lenientEntries)
                  (List.length LenientFixtures.all)
                  "lenient-accept corpus count must equal LenientFixtures.all — regenerate with `--emit-corpus` after adding a fixture")

          testCase "envelope corpus has one entry per EnvelopeFixtures fixture" (fun () ->
              Expect.equal
                  (List.length envelopeEntries)
                  (List.length EnvelopeFixtures.all)
                  "envelope corpus count must equal EnvelopeFixtures.all — regenerate with `--emit-corpus` after adding a fixture") ]

// ── Kind-set attestation pin (Phase 548 — cross-host drift guard) ───────────
// The F# leg of the cross-host kind-set attestation: the set of wire `kind.$type`
// discriminators the decoder produces round-tripping every node fixture must equal
// the generated `manifest.kinds` enumeration. A mismatch names the offending kind
// (the same "host X lacks `Drawing`" failure ergonomics every host's pin carries).
[<Tests>]
let kindSetPin =
    testList
        "Fuaran.UI.Ops.JsonDecode — kind-set attestation (Phase 548)"
        [ testCase "the F# decoder's kind set equals manifest.kinds" (fun () ->
              let manifestKinds = Set.ofList (Corpus.loadKinds ())

              let decodedKinds =
                  nodeEntries
                  |> List.map (fun e ->
                      let wire = Corpus.readPayload corpusRoot e.InputFile

                      match JsonDecode.decodeNodeObj wire with
                      | Ok decoded ->
                          use doc =
                              System.Text.Json.JsonDocument.Parse(
                                  CanonicalJson.encodeNode decoded,
                                  Corpus.wireJsonOptions
                              )

                          match doc.RootElement.GetProperty("kind").GetProperty("$type").GetString() with
                          | null -> failtestf "fixture %s: decoded node has no kind.$type" e.Id
                          | s -> s
                      | Error err -> failtestf "fixture %s: decode failed: %A" e.Id err)
                  |> Set.ofList

              let missing = Set.difference manifestKinds decodedKinds |> Set.toList
              let extra = Set.difference decodedKinds manifestKinds |> Set.toList

              Expect.isEmpty missing (sprintf "manifest declares kinds the F# decoder never produced: %A" missing)

              Expect.isEmpty
                  extra
                  (sprintf "the F# decoder produced kinds the manifest omits (regenerate with --emit-corpus): %A" extra)) ]

// ── FormFieldKind attestation pin (Phase 746 — the control-vocabulary leg) ────
// The second discriminator family to gain an executable attestation. `NodeKind`
// has had one since Phase 548; `FormFieldKind` had none in ANY host, which is how
// `DateRange` (Phase 725) landed in the corpus and sat unadopted in four hosts
// while every gate stayed green. Go's node sweep documented the exclusion in a
// comment; this closes it.
//
// Two directions, and both are load-bearing:
//   - the corpus sweep — every control discriminator the corpus actually carries
//     is one this host declares (a NEW kind in the corpus reddens here, named);
//   - the derivable direction — this host declares no kind the corpus does not
//     know (a kind added to the vocabulary without a fixture reddens here).
[<Tests>]
let formFieldKindSetPin =
    let manifestControlKinds = Set.ofList (Corpus.loadFormFieldKinds ())
    let hostControlKinds = Set.ofList JsonDecode.knownFormFieldKinds

    testList
        "Fuaran.UI.Ops.JsonDecode — FormFieldKind attestation (Phase 746)"
        [ testCase "the F# control vocabulary equals manifest.formFieldKinds" (fun () ->
              Expect.isNonEmpty
                  manifestControlKinds
                  "manifest.json declares no 'formFieldKinds' array — regenerate the corpus with --emit-corpus"

              let missing = Set.difference manifestControlKinds hostControlKinds |> Set.toList

              let extra = Set.difference hostControlKinds manifestControlKinds |> Set.toList

              Expect.isEmpty
                  missing
                  (sprintf "manifest form-field kinds the F# decoder lacks (add the decode arm): %A" missing)

              Expect.isEmpty
                  extra
                  (sprintf "F# decoder form-field kinds the manifest omits (add a fixture, regenerate): %A" extra))

          testCase "every control discriminator in the corpus is in the host vocabulary" (fun () ->
              // The sweep the phase asks for: `Form.fields[]` + `Filters.items[]`
              // over every node fixture. Carriers are matched by their PARENT
              // discriminator, never by property name — `DataGrid.columns[].kind`
              // is a CellKindErased and shares the token `Text` with this family.
              let seen =
                  nodeEntries
                  |> List.collect (fun e -> Corpus.controlKindsIn (Corpus.readPayload corpusRoot e.InputFile))
                  |> Set.ofList

              Expect.isNonEmpty seen "the node corpus carries no Form/Filters control at all — the sweep is blind"

              let undeclared = Set.difference seen hostControlKinds |> Set.toList

              Expect.isEmpty undeclared (sprintf "corpus control kinds the F# decoder does not declare: %A" undeclared)

              // The corpus is what generated the manifest, so the two must agree —
              // this catches a stale manifest committed without a regen.
              let unlisted = Set.difference seen manifestControlKinds |> Set.toList

              Expect.isEmpty
                  unlisted
                  (sprintf "control kinds the corpus carries but manifest.formFieldKinds omits: %A" unlisted))

          testCase "every declared control kind is actually accepted by the decoder" (fun () ->
              // The behavioural direction: a kind named in the vocabulary but
              // absent from `decodeFormFieldKind`'s dispatch would send a model to
              // a discriminator that rejects again. A declared kind must at
              // minimum get PAST the dispatch (it may then fail on its own missing
              // fields — anything but UNKNOWN_DU_CASE at the control's own $type).
              let stillUnknown =
                  JsonDecode.knownFormFieldKinds
                  |> List.filter (fun k ->
                      let json =
                          sprintf
                              """{"id":"f","kind":{"$type":"Form","fields":[{"id":"a","kind":{"$type":"%s"},"label":"L","required":false}],"onSubmit":"<closure>"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
                              k

                      match JsonDecode.decodeNodeObj json with
                      | Ok _ -> false
                      | Error e -> e.Code = "UNKNOWN_DU_CASE" && e.Path.EndsWith ".kind.$type")

              Expect.isEmpty
                  stillUnknown
                  (sprintf "declared control kinds the decoder still rejects as UNKNOWN_DU_CASE: %A" stillUnknown))

          testCase "the control hint names every kind in the vocabulary" (fun () ->
              // The model-facing half. The hint is a pure projection, so this is a
              // regression pin against someone re-typing it by hand again.
              let tokens =
                  JsonDecode.wrongFormFieldKindHint
                  |> String.map (fun c -> if System.Char.IsLetter c then c else ' ')
                  |> fun s -> s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                  |> Set.ofArray

              let missing =
                  Corpus.loadFormFieldKinds () |> List.filter (fun k -> not (tokens.Contains k))

              Expect.isEmpty missing (sprintf "the control-kind hint does not name these kinds: %A" missing)) ]

// ── WRONG_NODE_KIND hint pin (the model-facing half of the kind-set contract) ──
// The kind-set attestation above pins what the decoder ACCEPTS. This pins what it
// TELLS a model when it rejects: the `ExpectedShape` hint on a WRONG_NODE_KIND
// error must enumerate the whole vocabulary. Both halves matter — a hint that has
// silently fallen behind the kind set degrades every repair turn that reads it,
// and no corpus fixture catches it (fixtures certify codes and paths, not prose).
// The hint is projected from `JsonDecode.knownNodeKinds`, so this fails only if
// that enumeration itself drifts from the manifest.
[<Tests>]
let wrongNodeKindHintPin =
    testList
        "Fuaran.UI.Ops.JsonDecode — WRONG_NODE_KIND hint"
        [ testCase "knownNodeKinds equals the manifest kind enumeration" (fun () ->
              let manifestKinds = Set.ofList (Corpus.loadKinds ())
              let hostKinds = Set.ofList JsonDecode.knownNodeKinds

              Expect.isEmpty
                  (Set.difference manifestKinds hostKinds |> Set.toList)
                  "manifest declares kinds JsonDecode.knownNodeKinds omits (the hint is projected from it, so the hint omits them too)"

              Expect.isEmpty
                  (Set.difference hostKinds manifestKinds |> Set.toList)
                  "JsonDecode.knownNodeKinds names kinds the manifest does not declare")

          testCase "the projected hint names every kind as its own token" (fun () ->
              // Tokenise on non-letters so `List` is not satisfied by `SummaryList`
              // (nor `Map` by `Markdown`). Only the forward direction is asserted
              // here — the hint's prose ("Layout", "Display", …) is legitimately
              // present and is not vocabulary; the reverse direction is covered by
              // the set equality above, since the hint is a pure projection.
              let tokens =
                  JsonDecode.wrongNodeKindHint
                  |> String.map (fun c -> if System.Char.IsLetter c then c else ' ')
                  |> fun s -> s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                  |> Set.ofArray

              let missing = Corpus.loadKinds () |> List.filter (fun k -> not (tokens.Contains k))

              Expect.isEmpty missing (sprintf "the WRONG_NODE_KIND hint does not name these kinds: %A" missing))

          testCase "every hint-named kind is actually accepted by the decoder" (fun () ->
              // The other direction: a kind advertised in the hint but absent from
              // `decodeNodeKind` would send a model to a discriminator that rejects
              // again. A hint-named kind must at minimum get PAST the kind dispatch
              // (it will then fail on its own missing spec fields, not WRONG_NODE_KIND).
              let stillWrongKind =
                  JsonDecode.knownNodeKinds
                  |> List.filter (fun k ->
                      let json =
                          sprintf
                              """{"id":"x","kind":{"$type":"%s"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
                              k

                      match JsonDecode.decodeNodeObj json with
                      | Ok _ -> false
                      | Error e -> e.Code = "WRONG_NODE_KIND")

              Expect.isEmpty
                  stillWrongKind
                  (sprintf
                      "kinds named in the hint that the decoder still rejects as WRONG_NODE_KIND: %A"
                      stillWrongKind)) ]

// ── WireTree marker contract (the decoded-vs-authored closure barrier) ──────
[<Tests>]
let wireTreeMarker =
    let sample = Corpus.readPayload corpusRoot (nodeEntries |> List.head).InputFile

    testList
        "Fuaran.UI.Ops.JsonDecode — WireTree marker"
        [ testCase "decodeNode yields a WireTree whose reify equals the raw decodeNodeObj" (fun () ->
              match JsonDecode.decodeNode sample, JsonDecode.decodeNodeObj sample with
              | Ok wire, Ok raw ->
                  Expect.equal
                      (CanonicalJson.encodeNode (WireTree.reify wire))
                      (CanonicalJson.encodeNode raw)
                      "the WireTree marker wraps exactly the raw decoded Node<obj> — reify is the identity unwrap"
              | _ -> failtest "both decode paths should succeed on canonical wire")

          testCase "a WireTree round-trips through reify byte-identically to the wire" (fun () ->
              match JsonDecode.decodeNode sample with
              | Ok wire ->
                  Expect.equal (CanonicalJson.encodeNode (WireTree.reify wire)) sample "reify preserves the wire bytes"
              | Error e -> failtestf "decode failed: %A" e) ]

// ── The non-first-row Selection fixture — INTENT guard ──────────────────────
//
//  `master-detail-preselected-second-row` exists to make a prune-vs-seed
//  divergence OBSERVABLE, and every property that makes it observable is a
//  value someone could "tidy" away without breaking a single round-trip: the
//  round-trip suite above asserts the bytes are stable, never that the bytes
//  still say anything interesting.
//
//  Concretely, the fixture is worthless — while staying perfectly green — if
//  the default drifts back to the first row (masking returns: an unfiltered
//  pipeline surfaces row 1 anyway, so a pruning host looks correct), or if the
//  `note` column stops being per-row-distinct (the scalar leg then diverges
//  only by row COUNT, and a count divergence reads as a fixture-shape
//  difference rather than a wrong answer), or if the `filter -> project ->
//  limit 1` pipeline loses a stage (that exact shape is what a first-row
//  default hides).
//
//  So this pins the intent, not the encoding. It reads the committed fixture —
//  not the F# fixture VALUES — so it is the same artefact every other host
//  certifies against.
//
//  NOTE — scope. These are STRUCTURAL assertions. The behavioural expectations
//  (`related-grid` yields 1 row; `detail-note` reads "Search index stale", not
//  "Payment gateway timeout"; `detail-ticket` resolves rather than NotResolved)
//  need a Transform EVALUATOR, which lives in `Fuaran.UI.Renderer.Core`
//  (`BindingResolver`). This project DOES reference that tier — the codec-level
//  framing above predates the reference — so the evaluation leg now sits beside
//  these guards in `SelectionDerivedValueTests.fs`. The two are complementary
//  and neither subsumes the other: these pin that the fixture still SAYS
//  something interesting, that one pins that a host DERIVES the right answer
//  from it.
[<Tests>]
let nonFirstRowSelectionIntent =
    let fixtureId = "master-detail-preselected-second-row"

    let payload () =
        match corpusEntries |> List.tryFind (fun e -> e.Id = fixtureId) with
        | Some e -> Corpus.readPayload corpusRoot e.InputFile
        | None -> failtestf "fixture '%s' is not in the corpus manifest (regenerate with --emit-corpus)" fixtureId

    /// Every `defaultValue` string anywhere in the fixture, at any depth.
    let rec defaultValues (el: System.Text.Json.JsonElement) : string list =
        match el.ValueKind with
        | System.Text.Json.JsonValueKind.Object ->
            [ for p in el.EnumerateObject() do
                  if
                      p.Name = "defaultValue"
                      && p.Value.ValueKind = System.Text.Json.JsonValueKind.String
                  then
                      yield! (p.Value.GetString() |> Option.ofObj |> Option.toList)

                  yield! defaultValues p.Value ]
        | System.Text.Json.JsonValueKind.Array ->
            [ for item in el.EnumerateArray() -> defaultValues item ] |> List.concat
        | _ -> []

    /// Every pipeline step discriminator, in order, per pipeline found.
    let rec pipelines (el: System.Text.Json.JsonElement) : string list list =
        match el.ValueKind with
        | System.Text.Json.JsonValueKind.Object ->
            [ for p in el.EnumerateObject() do
                  if p.Name = "pipeline" && p.Value.ValueKind = System.Text.Json.JsonValueKind.Array then
                      let steps =
                          [ for step in p.Value.EnumerateArray() do
                                match step.TryGetProperty "$type" with
                                | true, t -> yield! (t.GetString() |> Option.ofObj |> Option.toList)
                                | _ -> () ]

                      if not steps.IsEmpty then
                          yield steps

                  yield! pipelines p.Value ]
        | System.Text.Json.JsonValueKind.Array -> [ for item in el.EnumerateArray() -> pipelines item ] |> List.concat
        | _ -> []

    testList
        "Fuaran.UI.Ops.JsonDecode — non-first-row Selection fixture (intent)"
        [ testCase "every Selection defaultValue names the SECOND row — not the first, not the last" (fun () ->
              use doc = System.Text.Json.JsonDocument.Parse(payload ())
              let defaults = defaultValues doc.RootElement

              Expect.isNonEmpty defaults "the fixture must carry at least one Selection defaultValue"

              // TCK-2042 is index 1 of 3. Pinning the exact value catches BOTH
              // a drift to the first row (masking returns) and to the last.
              Expect.all
                  defaults
                  (fun d -> d = "TCK-2042")
                  (sprintf
                      "a defaultValue drifted off the non-first row — prune-vs-seed stops being observable. Got: %A"
                      defaults))

          testCase "the `note` column is per-row-DISTINCT — so the scalar leg diverges by VALUE" (fun () ->
              use doc = System.Text.Json.JsonDocument.Parse(payload ())

              let noteValues =
                  let rec find (el: System.Text.Json.JsonElement) =
                      match el.ValueKind with
                      | System.Text.Json.JsonValueKind.Object ->
                          [ for p in el.EnumerateObject() do
                                if p.Name = "note" then
                                    match p.Value.TryGetProperty "values" with
                                    | true, vs when vs.ValueKind = System.Text.Json.JsonValueKind.Array ->
                                        yield [ for v in vs.EnumerateArray() -> v.GetString() ]
                                    | _ -> ()

                                yield! find p.Value ]
                      | System.Text.Json.JsonValueKind.Array ->
                          [ for i in el.EnumerateArray() -> find i ] |> List.concat
                      | _ -> []

                  find doc.RootElement

              Expect.isNonEmpty noteValues "the fixture must carry a `note` column"

              for values in noteValues do
                  Expect.equal
                      (List.length (List.distinct values))
                      (List.length values)
                      (sprintf
                          "`note` must be per-row-distinct or a wrong row shows a RIGHT value — the masking this fixture exists to break. Got: %A"
                          values))

          testCase "the scalar leg keeps the `filter -> project -> limit` shape a first-row default hides" (fun () ->
              use doc = System.Text.Json.JsonDocument.Parse(payload ())
              let found = pipelines doc.RootElement

              Expect.contains
                  found
                  [ "filter"; "project"; "limit" ]
                  (sprintf
                      "the masking-killer pipeline (filter -> project -> limit 1) is gone; remaining pipelines: %A"
                      found)) ]
