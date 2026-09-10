module Fuaran.UI.Tests.IdlSchema

open System.IO
open System.Text.Json
open Expecto
open Json.Schema
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.UI.Tests.IdlCertification

// ---------------------------------------------------------------------------
//  Phase 1668, certification (1) — the GENERATED JSON schema, corpus-certified.
//
//  Recovered from `Fuaran-Core@ccead29^:tests/Fuaran.Core.Tests/IdlSchemaTests.fs`
//  (Phase 697 there), which `fuaran-core#123` deleted with the UI byte-pin it
//  read. What it certified there is what it certifies here: `Gen.jsonSchema`
//  over the FULL vocabulary, evaluated by an off-the-shelf Draft 2020-12
//  validator against every fixture in the shared corpus.
//
//  Why the corpus and not a toy: a generated schema that has never met the
//  corpus is a FOURTH MIRROR of the wire format rather than a projection of it.
//  It can drift from the format silently, and a smoke test on an 8-kind mini
//  vocabulary cannot tell you, because a schema that is wrong in the same way
//  twice still round-trips its own toy input. Passing here means the leg is a
//  genuine drop-in for an external validator, which is the only claim worth
//  making about a schema.
//
//  The three defects Phase 697 went in naming are all still pinned below,
//  because each is a shape the generator could regress to and none of them
//  fails a smoke test:
//
//    * **Dangling `$ref`s.** Records were absent from the `$defs` assembly
//      while every `TRecord` slot emitted `$ref: #/$defs/<name>`. A strict
//      validator treats an unresolvable `$ref` as an ERROR, not a permissive
//      skip, so the leg could not certify at all.
//    * **No transparent-union reflection.** `TextSource.Literal` is on the
//      wire BARE — `"x"`, not `{"$type":"Literal","text":"x"}` — and without
//      reflecting that the schema rejects the canonical form of every literal
//      string in the corpus, which is most fixtures.
//    * **`additionalProperties: false` everywhere.** Resolved by ALIGNING with
//      the format rather than exempting the leg: the decoder tolerates unknown
//      keys (`WIRE_FORMAT.md` §2.1 rule 2) and the published `schema.json` says
//      so in its own header. A schema stricter than the format rejects payloads
//      the format accepts, and breaks the forward compatibility the tolerance
//      exists for.
//
//  WHAT CHANGED IN THE PORT. The vocabulary is the homed `src/Fuaran.UI.Idl/`
//  declaration read through `IdlCertification.vocabulary` (Core read its own
//  test-project fixture); the corpus comes through Phase 1647's ONE resolver
//  rather than a private upward walk; the non-vacuity assertion is against the
//  corpus MANIFEST rather than `> 50`, which is strictly stronger and is the
//  posture `GeneratedLayerTests` already takes here; and the go-red is named as
//  such and widened by one case — a MUTATED REAL FIXTURE, so the refusal
//  evidence is not confined to two hand-written payloads.
// ---------------------------------------------------------------------------

/// The generated schema, built lazily and forced inside the test bodies.
///
/// `lazy` for a specific reason rather than for tidiness: `JsonSchema.FromText`
/// can throw, and a throw in a module initialiser takes the WHOLE assembly's
/// test discovery with it, reported against whichever module lost the race. A
/// failure here must be a failing test, never an assembly that will not load.
let private schemaText = lazy (Gen.jsonSchema vocabulary)

let private schema = lazy (JsonSchema.FromText schemaText.Value)

/// Evaluate a wire payload. `None` ⇒ not parseable JSON (a rejection in its own
/// right); `Some isValid` ⇒ parsed and schema-evaluated.
let private validate (wire: string) : bool option =
    // `JsonDocument.Parse` defaults to a 64-level depth cap, which is BELOW the
    // 256 `WIRE_FORMAT.md` §21 pins — so the §21 at-the-limit node fixture
    // reported "not parseable JSON" and failed the certification on the
    // reader's own cap rather than on anything the schema said. Parse at the
    // format's own bound.
    let parseOptions = JsonDocumentOptions(MaxDepth = 256)

    let parsed =
        try
            Some(JsonDocument.Parse(wire, parseOptions))
        with _ ->
            None

    match parsed with
    | None -> None
    | Some doc ->
        use doc = doc
        Some(schema.Value.Evaluate(doc.RootElement, EvaluationOptions()).IsValid)

/// Every `$ref` target named anywhere in the emitted document.
let rec private refsIn (j: JVal) : string list =
    match j with
    | JObj fields ->
        [ for name, v in fields do
              match name, v with
              | "$ref", JStr target -> target
              | _ -> yield! refsIn v ]
    | JArr items -> items |> List.collect refsIn
    | _ -> []

let private emittedDoc =
    lazy
        (match Json.parse schemaText.Value with
         | Ok j -> j
         | Error e -> failtestf "the generated schema is not parseable JSON: %s" e)

let private definedNames =
    lazy
        (match emittedDoc.Value with
         | JObj fields ->
             match fields |> List.tryFind (fun (n, _) -> n = "$defs") |> Option.map snd with
             | Some(JObj defs) -> defs |> List.map fst |> Set.ofList
             | _ -> failtest "the generated schema has no `$defs` object"
         | _ -> failtest "the generated schema is not a JSON object")

[<Tests>]
let tests =
    testList
        "Phase 1668 (1) — the generated IDL schema, corpus-certified"
        [

          // ── structural integrity of the emitted document ─────────────────

          testCase "every `$ref` resolves — no dangling reference" (fun _ ->
              let dangling =
                  refsIn emittedDoc.Value
                  |> List.distinct
                  |> List.filter (fun r -> r.StartsWith "#/$defs/")
                  |> List.map (fun r -> r.Substring "#/$defs/".Length)
                  |> List.filter (fun n -> not (definedNames.Value.Contains n))

              Expect.isEmpty
                  dangling
                  (sprintf
                      "unresolvable $ref(s): %s — a strict validator ERRORS on these, so the leg cannot certify"
                      (String.concat ", " dangling)))

          testCase "every declared record has a definition" (fun _ ->
              for r in vocabulary.Records do
                  Expect.isTrue
                      (definedNames.Value.Contains r.Name)
                      (sprintf "record '%s' is referenced by TRecord slots but absent from $defs" r.Name))

          testCase "a record definition carries no `$type` const" (fun _ ->
              // The distinction a record schema exists to make: a union case is
              // `$type`-tagged, a record is not. Emitting the const would demand
              // a key no encoder writes for these.
              let recordNames = vocabulary.Records |> List.map _.Name |> Set.ofList

              match emittedDoc.Value with
              | JObj fields ->
                  match fields |> List.tryFind (fun (n, _) -> n = "$defs") |> Option.map snd with
                  | Some(JObj defs) ->
                      for name, def in defs do
                          if recordNames.Contains name then
                              match def with
                              | JObj df ->
                                  match df |> List.tryFind (fun (n, _) -> n = "properties") |> Option.map snd with
                                  | Some(JObj props) ->
                                      Expect.isFalse
                                          (props |> List.exists (fun (p, _) -> p = "$type"))
                                          (sprintf "record '%s' declares a $type property" name)
                                  | _ -> failtestf "record '%s' has no properties object" name
                              | _ -> failtestf "record '%s' is not an object schema" name
                  | _ -> failtest "no $defs"
              | _ -> failtest "not an object")

          testCase "the strictness posture is aligned with the decoder, not stricter" (fun _ ->
              // A recorded DECISION, pinned so it cannot regress silently. The
              // decoder tolerates unknown keys and the published `schema.json`
              // matches that; `additionalProperties: false` anywhere in the
              // emitted document would make this leg reject payloads the format
              // accepts. `TMap` still uses `additionalProperties` as a VALUE
              // schema — a different meaning, and never `false`.
              let rec falseAdditional (j: JVal) : bool =
                  match j with
                  | JObj fields ->
                      fields
                      |> List.exists (fun (n, v) ->
                          (n = "additionalProperties" && v = JBool false) || falseAdditional v)
                  | JArr items -> items |> List.exists falseAdditional
                  | _ -> false

              Expect.isFalse
                  (falseAdditional emittedDoc.Value)
                  "the generated schema closes an object — stricter than WIRE_FORMAT.md §2.1 rule 2")

          // ── the certification itself ─────────────────────────────────────

          testCase "every node accept fixture validates against the generated schema" (fun _ ->
              match familyFixtures "nodes" with
              | [] -> skiptest absentCorpusSkip
              | files ->
                  let failures =
                      [ for name, wire in files do
                            match validate wire with
                            | Some true -> ()
                            | Some false -> name, "schema REJECTED an accept fixture"
                            | None -> name, "fixture is not parseable JSON" ]

                  Expect.isEmpty
                      failures
                      (sprintf
                          "%d of %d node fixtures failed the generated schema:\n  %s"
                          failures.Length
                          files.Length
                          (failures |> List.map (fun (f, m) -> f + " — " + m) |> String.concat "\n  ")))

          testCase "the certification is not vacuous — the whole node family was read" (fun _ ->
              // The guard above SKIPS without the corpus, and a silent skip that
              // reads zero fixtures looks identical to a pass. Core asserted
              // `> 50` here; against the MANIFEST it is strictly stronger — it
              // also catches a fixture file added or deleted without the
              // manifest following, which is the drift the corpus's separate
              // repository makes possible.
              match corpusRoot () with
              | None -> skiptest absentCorpusSkip
              | Some _ ->
                  Expect.equal
                      (List.length (familyFixtures "nodes"))
                      (manifestFamilySize "node-round-trip")
                      "the nodes/ directory and the corpus manifest enumerate the same fixture set")

          // ── the go-red: the certification can FAIL ───────────────────────

          testCase "GO-RED — a structurally-invalid payload is refused" (fun _ ->
              // The other half of a non-vacuous certification: a schema that
              // validated EVERYTHING would pass the accept sweep above too. Two
              // hand-written payloads the generated schema must refuse on
              // structure alone.
              let unknownKind = """{"id":"n1","kind":{"$type":"NoSuchKind"}}"""
              let missingId = """{"kind":{"$type":"Heading","level":2,"text":"x"}}"""

              Expect.equal (validate unknownKind) (Some false) "an unknown kind tag must not validate"
              Expect.equal (validate missingId) (Some false) "a node without `id` must not validate")

          testCase "GO-RED — a MUTATED real fixture is refused" (fun _ ->
              // Widened from Core's version, and the widening is the point: the
              // two payloads above are hand-written, so they prove the schema
              // refuses shapes nobody emits. This takes a fixture the sweep
              // above has just VALIDATED, breaks its discriminator, and requires
              // the refusal — so the accept sweep and the refusal evidence are
              // over the same documents rather than over two disjoint sets, and
              // a schema loosened to admit anything fails here rather than
              // going quietly green.
              match familyFixtures "nodes" with
              | [] -> skiptest absentCorpusSkip
              | files ->
                  let mutated =
                      files
                      |> List.choose (fun (name, wire) ->
                          if wire.Contains "\"$type\":\"" then
                              Some(name, wire.Replace("\"$type\":\"", "\"$type\":\"NoSuch"))
                          else
                              None)

                  Expect.isNonEmpty
                      mutated
                      "no node fixture carries a `$type` discriminator to break — the mutation is inapplicable, so this go-red proves nothing and must be re-derived"

                  let accepted =
                      mutated
                      |> List.filter (fun (_, wire) -> validate wire = Some true)
                      |> List.map fst

                  Expect.isEmpty
                      accepted
                      (sprintf
                          "the generated schema accepted %d fixture(s) whose discriminators were replaced by unknown tags: %s"
                          accepted.Length
                          (accepted |> List.truncate 8 |> String.concat ", ")))

          // ── the enumerated worksheet, reported rather than asserted ──────

          testCase "reject fixtures: how many the schema catches, and which it cannot" (fun _ ->
              // NOT an assertion that every reject fixture fails the schema —
              // many encode rules Draft 2020-12 provably cannot state
              // (cross-field ordering, node-id uniqueness across a tree, §16
              // lenient policy), and the decoder is their only enforcer. The
              // tier's hand-written suite pins its own exemption set BY NAME;
              // this leg is not a subsumption candidate, so what is useful here
              // is the enumerated worksheet, reported rather than asserted.
              match familyFixtures "reject" with
              | [] -> skiptest absentCorpusSkip
              | files ->
                  let caught, uncaught =
                      files
                      |> List.partition (fun (_, wire) ->
                          match validate wire with
                          | Some true -> false
                          | _ -> true)

                  printfn
                      "  reject family: %d/%d caught structurally by the generated schema"
                      caught.Length
                      files.Length

                  printfn "  NOT caught (decoder-only rules):"

                  for name, _ in uncaught do
                      printfn "    %s" name

                  Expect.isNonEmpty caught "the schema should catch SOME reject fixture structurally") ]
