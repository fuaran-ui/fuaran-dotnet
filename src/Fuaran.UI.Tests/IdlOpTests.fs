module Fuaran.UI.Tests.IdlOp

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.UI.Tests.IdlCertification

// ---------------------------------------------------------------------------
//  Phase 1668, certification (2) — the IDL op codec against the op corpus.
//
//  Recovered from `Fuaran-Core@ccead29^:tests/Fuaran.Core.Tests/IdlOpTests.fs`
//  (Phase 703 there), which `fuaran-core#123` deleted with the UI byte-pin it
//  read. `Fuaran.Core` still certifies `decodeOp`'s MECHANISM over vocabularies
//  authored for the purpose; what left with the fixture is the certification
//  against the real op corpus, which is this one.
//
//  The discipline is the same oracle the node legs use, and it is the only one
//  that means anything for a codec: DECODE each committed fixture through the
//  IDL, RE-ENCODE it, and compare BYTES. A decode alone proves the shapes
//  parse; the round-trip proves nothing was silently dropped, reordered or
//  widened on the way through — a field the IDL forgot to declare decodes fine
//  and vanishes on re-encode, and only the byte compare catches it.
//
//  SCOPE — shapes only, and the boundary is load-bearing. Apply SEMANTICS stay
//  hand-written above the IDL: what `UpdateProp`'s dotted `path` addresses,
//  whether a `target` resolves, §3.4's error mapping. This is the same split
//  the node legs run, where decode POLICY (§16 lenient-accept, the reject set)
//  composes above a structural decoder rather than inside it.
//
//  WHAT CHANGED IN THE PORT. The vocabulary is the homed `src/Fuaran.UI.Idl/`
//  declaration through `IdlCertification.vocabulary`; the corpus comes through
//  Phase 1647's ONE resolver; the family-size pin reads the corpus MANIFEST
//  instead of the literal `22` Core carried — the family holds 24 fixtures
//  today, which is exactly why a literal in this repo is a forward-coupling
//  trap (the corpus is a separate repository, so a fixture lands there with no
//  commit here); and the go-red is named as such and widened by one case, a
//  MUTATED REAL FIXTURE, so the refusal evidence is over documents the sweep
//  itself round-trips rather than over two hand-written payloads.
// ---------------------------------------------------------------------------

let private opFixtures = lazy (familyFixtures "ops")

/// The op tags the corpus actually exercises — the coverage denominator.
let private coveredTags =
    lazy
        (opFixtures.Value
         |> List.choose (fun (_, wire) ->
             match Json.parse wire with
             | Ok(JObj fs) ->
                 fs
                 |> List.tryPick (function
                     | "$type", JStr t -> Some t
                     | _ -> None)
             | _ -> None)
         |> List.distinct
         |> Set.ofList)

[<Tests>]
let tests =
    testList
        "Phase 1668 (2) — the IDL op vocabulary, corpus-certified"
        [

          // ── the declaration ──────────────────────────────────────────────

          testCase "the vocabulary declares the wire's op set" (fun _ ->
              let tags = vocabulary.Ops |> List.map _.Tag |> Set.ofList

              Expect.equal
                  tags
                  (set
                      [ "Batch"
                        "EditNode"
                        "InsertChild"
                        "MoveNode"
                        "RemoveNode"
                        "ReorderChildren"
                        "ReplaceBinding"
                        "ReplaceRoot"
                        "UpdateProp"
                        "UpdateState"
                        "UpdateStyle" ])
                  "the 11 WIRE_FORMAT.md §3.4 op cases")

          testCase "InsertChild carries no `position` — read from the corpus, not old prose" (fun _ ->
              // Phase 681 removed it. The phase body that commissioned the
              // original work said to read the bytes rather than the prose, and
              // this is why.
              let insert = vocabulary.Ops |> List.find (fun o -> o.Tag = "InsertChild")
              let names = insert.Fields |> List.map _.Name |> Set.ofList
              Expect.equal names (set [ "child"; "parentId" ]) "no positional index survives")

          // ── the certification ────────────────────────────────────────────

          testCase "every op fixture round-trips through the IDL byte-identically" (fun _ ->
              match opFixtures.Value with
              | [] -> skiptest absentCorpusSkip
              | files ->
                  let failures =
                      [ for name, wire in files do
                            match Decode.decodeOp vocabulary wire with
                            | Error e -> name, "decode failed: " + e
                            | Ok value ->
                                match Encode.encodeOp vocabulary value with
                                | Error e -> name, "re-encode failed: " + e
                                | Ok actual when actual <> wire ->
                                    name, sprintf "bytes differ\n    expected: %s\n    actual:   %s" wire actual
                                | Ok _ -> () ]

                  Expect.isEmpty
                      failures
                      (sprintf
                          "%d of %d op fixtures failed:\n  %s"
                          failures.Length
                          files.Length
                          (failures |> List.map (fun (f, m) -> f + " — " + m) |> String.concat "\n  ")))

          testCase "the certification is not vacuous — the whole op family was read" (fun _ ->
              // A skip that read zero fixtures looks identical to a pass. Against
              // the corpus MANIFEST rather than Core's literal: the manifest is
              // the corpus's own enumeration and moves in the same corpus commit
              // as a fixture, so this also catches a file added or deleted
              // without the manifest following.
              match corpusRoot () with
              | None -> skiptest absentCorpusSkip
              | Some _ ->
                  Expect.equal
                      (List.length opFixtures.Value)
                      (manifestFamilySize "op-round-trip")
                      "the ops/ directory and the corpus manifest enumerate the same fixture set")

          testCase "every declared op is exercised by at least one fixture" (fun _ ->
              // The other direction: a declared op no fixture covers is a shape
              // nothing has ever validated, which is exactly the state the node
              // vocabulary was in for `Separator` before the stage-3 swap found
              // it.
              match opFixtures.Value with
              | [] -> skiptest absentCorpusSkip
              | _ ->
                  let declared = vocabulary.Ops |> List.map _.Tag |> Set.ofList
                  let unexercised = Set.difference declared coveredTags.Value

                  Expect.isEmpty
                      unexercised
                      (sprintf "declared but never in a fixture: %s" (String.concat ", " unexercised)))

          testCase "Batch recurses — a nested op is decoded as an op, not as opaque JSON" (fun _ ->
              // `TOp` exists for exactly this. If `Batch.ops` were `TJson` the
              // fixture would still round-trip byte-identically while carrying no
              // structure at all, so the byte gate alone cannot prove this.
              match opFixtures.Value |> List.tryFind (fun (n, _) -> n = "op-batch.json") with
              | None -> skiptest "op-batch fixture not present"
              | Some(_, wire) ->
                  match Decode.decodeOp vocabulary wire with
                  | Error e -> failtestf "decode failed: %s" e
                  | Ok(VUnion("Batch", fields)) ->
                      match fields |> List.tryFind (fun (n, _) -> n = "ops") |> Option.map snd with
                      | Some(VList inner) ->
                          Expect.isNonEmpty inner "the batch carries nested ops"

                          for op in inner do
                              match op with
                              | VUnion(tag, _) ->
                                  Expect.isTrue
                                      (vocabulary.Ops |> List.exists (fun o -> o.Tag = tag))
                                      (sprintf "nested '%s' resolved against the op vocabulary" tag)
                              | other -> failtestf "nested op decoded as %A, not a tagged op" other
                      | other -> failtestf "Batch.ops decoded as %A" other
                  | Ok other -> failtestf "op-batch decoded as %A" other)

          testCase "EditNode.newKind is a BARE kind, not a node" (fun _ ->
              // The `TKind` / `TNode` distinction, which the wire makes by the
              // presence of `id`. Decoding a bare kind as a node would fail;
              // decoding it as opaque JSON would succeed and lose the
              // vocabulary.
              match opFixtures.Value |> List.tryFind (fun (n, _) -> n = "op-editnode.json") with
              | None -> skiptest "op-editnode fixture not present"
              | Some(_, wire) ->
                  match Decode.decodeOp vocabulary wire with
                  | Ok(VUnion("EditNode", fields)) ->
                      match fields |> List.tryFind (fun (n, _) -> n = "newKind") |> Option.map snd with
                      | Some(VUnion(tag, _)) ->
                          Expect.isTrue
                              (vocabulary.Kinds |> List.exists (fun k -> k.Tag = tag))
                              (sprintf "'%s' resolved against the KIND vocabulary" tag)
                      | other -> failtestf "newKind decoded as %A — expected a bare tagged kind" other
                  | other -> failtestf "op-editnode decoded as %A" other)

          // ── the go-red: the certification can FAIL ───────────────────────

          testCase "GO-RED — a malformed op is rejected, not silently absorbed" (fun _ ->
              let unknownOp = """{"$type":"NoSuchOp","target":"n1"}"""
              let missingField = """{"$type":"MoveNode","target":"n1"}"""

              Expect.isError (Decode.decodeOp vocabulary unknownOp) "an unknown op tag"
              Expect.isError (Decode.decodeOp vocabulary missingField) "a missing required field")

          testCase "GO-RED — a MUTATED real fixture no longer round-trips" (fun _ ->
              // Widened from Core's version. The two payloads above are
              // hand-written, so they prove the codec refuses shapes nobody
              // emits. This takes each fixture the sweep has just round-tripped,
              // replaces its top-level op discriminator with an unknown tag, and
              // requires that every one of them now FAILS — so the accept sweep
              // and the refusal evidence are over the same documents, and a
              // codec loosened to absorb an unknown tag fails here rather than
              // going quietly green.
              match opFixtures.Value with
              | [] -> skiptest absentCorpusSkip
              | files ->
                  let mutated =
                      files
                      |> List.choose (fun (name, wire) ->
                          if wire.Contains "\"$type\":\"" then
                              Some(name, wire.Replace("\"$type\":\"", "\"$type\":\"NoSuch"))
                          else
                              None)

                  Expect.equal
                      (List.length mutated)
                      (List.length files)
                      "every op fixture is `$type`-discriminated at its root, so every one is mutable — if not, this go-red covers less than the sweep and must be re-derived"

                  let survived =
                      mutated
                      |> List.filter (fun (_, wire) ->
                          match Decode.decodeOp vocabulary wire with
                          | Error _ -> false
                          | Ok value ->
                              match Encode.encodeOp vocabulary value with
                              | Ok actual -> actual = wire
                              | Error _ -> false)
                      |> List.map fst

                  Expect.isEmpty
                      survived
                      (sprintf
                          "%d mutated fixture(s) still round-tripped, so the byte gate is not measuring the op vocabulary: %s"
                          survived.Length
                          (survived |> List.truncate 8 |> String.concat ", ")))

          // ── the derived artefacts pick the vocabulary up ─────────────────

          testCase "the schema gains the second root and the op definitions" (fun _ ->
              let schema = Gen.jsonSchema vocabulary

              Expect.stringContains schema "#/$defs/TreeOp" "the root alternation names TreeOp"
              Expect.stringContains schema "\"NodeKind\"" "the kind alternation is named, for TKind to reference"

              for o in vocabulary.Ops do
                  Expect.stringContains schema ("\"" + o.Tag + "\"") (sprintf "op '%s' has a definition" o.Tag))

          testCase "an op-free IDL's schema root is unchanged" (fun _ ->
              // The whole additive claim: a domain that declares no ops gets
              // exactly the single-root schema it had before the op vocabulary
              // existed.
              let schema = Gen.jsonSchema { vocabulary with Ops = [] }
              Expect.stringContains schema "\"$ref\":\"#/$defs/Node\"" "single root"
              Expect.isFalse (schema.Contains "TreeOp") "no op vocabulary leaks in")

          testCase "idl.json carries the op vocabulary" (fun _ ->
              let text = Artifact.render vocabulary

              for o in vocabulary.Ops do
                  Expect.stringContains text ("\"" + o.Tag + "\"") (sprintf "the artefact publishes op '%s'" o.Tag)

              // Additive, on the same terms: an op-free vocabulary's artefact
              // gains nothing, so every pre-op emission is byte-for-byte what it
              // was.
              //
              // Asserted on an op TAG, not on the key `"ops"` — `Action.Chain`
              // already has a field of that name, so the obvious probe answers a
              // different question and passes either way.
              let opFree = Artifact.render { vocabulary with Ops = [] }
              Expect.isFalse (opFree.Contains "\"ReorderChildren\"") "an op-free vocabulary adds no op family") ]
