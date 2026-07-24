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
        [ testCase "node corpus has one entry per Fixtures.allNodes fixture" (fun () ->
              Expect.equal
                  (List.length nodeEntries)
                  (List.length Fixtures.allNodes)
                  "node-round-trip corpus count must equal Fixtures.allNodes — regenerate with `--emit-corpus` after adding a fixture")

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
                          use doc = System.Text.Json.JsonDocument.Parse(CanonicalJson.encodeNode decoded)

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
