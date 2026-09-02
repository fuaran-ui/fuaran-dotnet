module Fuaran.UI.JsonDecode.Tests.OpVocabularyTests

// ============================================================================
//  Phase 1104 — the TreeOp vocabulary export, pinned to the IDL.
//
//  `JsonDecode.opWireFields` is a DECLARATION of the op half of the wire
//  vocabulary: which `$type` discriminators `decodeOp` accepts, and what wire
//  fields each carries. Before it existed the enumeration lived inside one
//  error-message string in the decoder's fallback arm, so a consumer that needed
//  the op vocabulary — a teaching surface, an authoring surface, another host —
//  had to re-type it, and the re-typed copy is the one nobody updates. That is
//  the same defect `nodeKindGroups` closed on the node side, one wave earlier.
//
//  A declaration is only worth anything if it cannot drift from the thing it
//  declares, so it is pinned on TWO independent legs:
//
//    1. Against `idl.json` — the canonical vocabulary artefact the structural
//       layer is GENERATED from (Phase 696). This is the strong leg: it pins the
//       tags AND the per-op wire field names AND their optionality, so an IDL
//       edit that adds an op, adds a field, or changes a field's optionality
//       fails here in the same session that made it.
//
//    2. Against `TreeOp`'s own union cases by reflection — the mechanism Phase
//       1095 was left with when no export existed. Kept, not replaced: it reads
//       the SHIPPED type rather than the artefact, so the two legs disagree if
//       the generated layer and the corpus ever fall out of step, which neither
//       leg could tell you alone. It is deliberately the weaker leg (case names
//       carry no field information at all — `EditNode of NodeId * NodeKind<'Msg>`
//       has no labels, and the wire calls those two `target` and `newKind`),
//       which is precisely why leg 1 exists.
//
//  Leg 3 is the projection: the decoder's unknown-op hint must BE the
//  declaration rather than agree with it today.
//
//  Leg 4 is the CROSS-HOST leg, and it is the one this file was missing. Legs
//  1–3 are all F#-internal — the IDL is the generation root of this repo's
//  structural layer, the DU is an F# type, the hint is this decoder's error
//  string — so none of them is a surface a second host can fail against. The
//  node vocabulary has had that surface since the `kinds` enumeration landed in
//  `manifest.json` and the control vocabulary since `formFieldKinds`; the op
//  vocabulary had the STRONGER artefact and none of the reach, because every
//  host's harness loads the manifest and no host reads `idl.json`. So a host
//  with no op decode arm certified every op fixture it happened to carry and
//  declared nothing about the op set. The asymmetry was the gap.
// ============================================================================

open System
open System.Text.Json
open System.IO
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.UI.Ops.Types

module JsonDecode = Fuaran.UI.Ops.JsonDecode

let private corpusRoot = Corpus.findRoot ()

/// A required string property. `GetString()` is nullable under F# 10, and a null
/// here means the artefact is malformed rather than that a default applies — so
/// it fails by name instead of widening the type of everything downstream.
let private reqStr (name: string) (el: JsonElement) : string =
    match el.GetProperty(name).GetString() with
    | null -> failwithf "idl.json: '%s' is null" name
    | s -> s

/// `idl.json`'s `ops` array as `(tag, (field, required) list)`, field order
/// normalised — the artefact sorts its fields, the declaration keeps them in the
/// order the decoder matches, and neither order is a claim about the wire.
let private idlOps () : (string * (string * bool) list) list =
    let path = Path.Combine(corpusRoot, "idl.json")
    use doc = JsonDocument.Parse(File.ReadAllText path)

    doc.RootElement.GetProperty("ops").EnumerateArray()
    |> Seq.map (fun op ->
        let tag = reqStr "tag" op

        let fields =
            op.GetProperty("fields").EnumerateArray()
            |> Seq.map (fun f ->
                let name = reqStr "name" f
                let required = reqStr "$type" (f.GetProperty "optionality") = "required"
                name, required)
            |> Seq.sortBy fst
            |> List.ofSeq

        tag, fields)
    |> List.ofSeq
    |> List.sortBy fst

let private declaredOps () =
    JsonDecode.opWireFields
    |> List.map (fun (tag, fields) -> tag, fields |> List.sortBy fst)
    |> List.sortBy fst

let private shippedOpCases =
    FSharpType.GetUnionCases(typeof<TreeOp<unit>>)
    |> Array.map _.Name
    |> Array.toList
    |> List.sort

[<Tests>]
let tests =
    testList
        "Phase 1104 — TreeOp vocabulary export"
        [ testList
              "leg 1 — pinned to idl.json (the generation root)"
              [ testCase "the declared op set is the IDL's op set"
                <| fun () ->
                    let declared = declaredOps () |> List.map fst
                    let idl = idlOps () |> List.map fst

                    Expect.equal
                        declared
                        idl
                        "JsonDecode.knownOpKinds and idl.json's ops disagree — an op was added to one and not the other"

                testCase "every op's wire field set is the IDL's, name for name"
                <| fun () ->
                    let idl = idlOps () |> Map.ofList

                    for tag, fields in declaredOps () do
                        match Map.tryFind tag idl with
                        | None -> failtestf "op '%s' is declared but absent from idl.json" tag
                        | Some idlFields ->
                            Expect.equal
                                fields
                                idlFields
                                (sprintf
                                    "op '%s': the declared wire fields disagree with idl.json (declared %A, IDL %A)"
                                    tag
                                    fields
                                    idlFields)

                testCase "the IDL adds no op the declaration is missing"
                <| fun () ->
                    // The converse of the assertion above, stated separately so a
                    // narrowing of BOTH sides at once cannot satisfy it — the
                    // failure mode the 1095 media-kind assertion was written
                    // against.
                    let declared = declaredOps () |> List.map fst |> Set.ofList

                    for tag, _ in idlOps () do
                        Expect.isTrue
                            (Set.contains tag declared)
                            (sprintf "idl.json declares op '%s' and JsonDecode.opWireFields omits it" tag) ]

          testList
              "leg 2 — pinned to the shipped TreeOp DU"
              [ testCase "the declared op set is TreeOp's case set"
                <| fun () ->
                    // `TreeOp`'s case names ARE the `$type` values (WIRE_FORMAT
                    // §14), so this reads the vocabulary off the shipped type
                    // rather than off the artefact. It cannot see field names.
                    Expect.equal
                        (declaredOps () |> List.map fst)
                        shippedOpCases
                        "the declaration and TreeOp's union cases disagree"

                testCase "no declared op field is a retired positional field"
                <| fun () ->
                    let byTag = declaredOps () |> Map.ofList

                    for tag, retired in JsonDecode.retiredOpFields do
                        match Map.tryFind tag byTag with
                        | None -> failtestf "retiredOpFields names op '%s', which is not in the vocabulary" tag
                        | Some fields ->
                            Expect.isFalse
                                (fields |> List.exists (fun (n, _) -> n = retired))
                                (sprintf
                                    "'%s' is declared as an emittable field of %s, but a conformant decoder refuses it by name"
                                    retired
                                    tag) ]

          testList
              "leg 3 — the decoder's hint is the projection"
              [ testCase "every declared op appears in the unknown-op hint"
                <| fun () ->
                    for tag in JsonDecode.knownOpKinds do
                        Expect.stringContains
                            JsonDecode.unknownOpKindHint
                            tag
                            (sprintf "the unknown-op hint omits '%s'" tag)

                testCase "the hint invents nothing the decoder would refuse"
                <| fun () ->
                    let advertised =
                        JsonDecode.unknownOpKindHint.Split([| '|' |])
                        |> Array.map (fun s -> s.Trim())
                        |> Array.filter (fun s -> s <> "")

                    for name in advertised do
                        Expect.isTrue
                            (List.contains name JsonDecode.knownOpKinds)
                            (sprintf "the hint advertises '%s', which decodeOp refuses" name)

                testCase "an unknown op's error carries the projected hint"
                <| fun () ->
                    // The projection has to reach the wire, not merely exist:
                    // this drives the real decoder and reads the hint it emits.
                    match JsonDecode.decodeOp """{"$type":"Nonesuch","target":"a"}""" with
                    | Ok _ -> failtest "decodeOp accepted an op discriminator that does not exist"
                    | Error e ->
                        Expect.equal
                            e.ExpectedShape
                            (Some JsonDecode.unknownOpKindHint)
                            "the UNKNOWN_DU_CASE hint is not the declared vocabulary" ]

          testList
              "leg 4 — pinned to the manifest's `ops` enumeration"
              [ testCase "the declared op set is manifest.ops"
                <| fun () ->
                    // The cross-host leg (WIRE_FORMAT §11.2). Legs 1 and 2 are
                    // F#-internal: they hold this host's declaration against the
                    // IDL and against its own shipped DU, and a second host can
                    // read neither — the IDL is the generation root of THIS
                    // repo's structural layer, and the DU is an F# type. Every
                    // host's harness already loads `manifest.json`, so the
                    // enumeration that lives there is the one another host can
                    // fail against, in the same both-directions shape the
                    // `kinds` and `formFieldKinds` pins use.
                    //
                    // Stated in both directions rather than as one set equality,
                    // so the failure says which way round it went — "the corpus
                    // knows an op this host does not" and "this host declares an
                    // op the corpus never exercises" have different remedies,
                    // and only the first is an adoption gap.
                    let manifestOps = Set.ofList (Corpus.loadOps ())
                    let declared = Set.ofList JsonDecode.knownOpKinds

                    let missing = Set.difference manifestOps declared |> Set.toList

                    Expect.isEmpty
                        missing
                        (sprintf "manifest.ops declares ops this host's decoder does not know: %A" missing)

                    let extra = Set.difference declared manifestOps |> Set.toList

                    Expect.isEmpty
                        extra
                        (sprintf
                            "this host declares ops the corpus never exercises (add an op fixture, then regenerate with --emit-corpus): %A"
                            extra)

                testCase "manifest.ops is the discriminator every op fixture actually carries"
                <| fun () ->
                    // The enumeration is GENERATED from these bytes, so this
                    // reads it back off the corpus rather than off the emitter —
                    // the leg that catches a manifest regenerated from a
                    // different fixture set than the one committed beside it.
                    let fromFixtures =
                        snd (Corpus.load ())
                        |> List.filter (fun (e: Corpus.FixtureEntry) -> e.Kind = "op-round-trip")
                        |> List.map (fun e ->
                            let wire = Corpus.readPayload corpusRoot e.InputFile
                            use doc = JsonDocument.Parse(wire, Corpus.wireJsonOptions)

                            match doc.RootElement.GetProperty("$type").GetString() with
                            | null -> failtestf "op fixture %s has no $type" e.Id
                            | s -> s)
                        |> Set.ofList

                    Expect.isNonEmpty fromFixtures "the corpus enumerates no op round-trip fixtures"

                    Expect.equal
                        (Set.ofList (Corpus.loadOps ()))
                        fromFixtures
                        "manifest.ops and the op fixtures' own discriminators disagree — regenerate with --emit-corpus" ] ]
