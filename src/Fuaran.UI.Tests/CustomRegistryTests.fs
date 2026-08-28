module Fuaran.UI.Tests.CustomRegistry

// The first-class extension registry (task 15): a registered custom component is
// AI-discoverable (its prop schema projects into the prompt context) and its
// props are validated like a built-in kind's.

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

type private Spark = { Points: string; Width: int }

let private encode (p: Spark) : Map<string, JVal> =
    Map.ofList [ "points", JStr p.Points; "width", JInt p.Width ]

let private decode (bag: Map<string, JVal>) : Result<Spark, CustomDecodeError> =
    match Map.tryFind "points" bag, Map.tryFind "width" bag with
    | Some(JStr pts), Some(JInt w) -> Ok { Points = pts; Width = w }
    | _ -> Error(CustomDecodeError.payload "bad spark props")

let private schema: PropSchema =
    [ { Defaults.propDecl with
          Name = "points"
          Type = PropType.PString
          Required = true }
      { Defaults.propDecl with
          Name = "width"
          Type = PropType.PInt
          Required = true } ]

let private typedContract =
    match
        CustomContract.createWithSchema
            "viz"
            "spark"
            schema
            encode
            decode
            { Points = "0,1"; Width = 3 }
            []
            HashStrictness.StrictReplay
    with
    | Ok c -> c
    | Error e -> failwithf "contract build failed: %s" e

let private registry = CustomRegistry.Empty.Register(typedContract)

// ─── Phase 1107 fixtures — the same contract, declared honestly ──────────────
//
// `annotatedSchema` differs from `schema` in one field on one prop: `points`
// says its string is markdown, judged by cmark. Everything else — the encoder,
// the key set, the ids — is identical, which is what makes the hash assertion
// below a test of the hash rather than of the fixture.

let private contractWith (s: PropSchema) =
    match
        CustomContract.createWithSchema
            "viz"
            "spark"
            s
            encode
            decode
            { Points = "0,1"; Width = 3 }
            []
            HashStrictness.StrictReplay
    with
    | Ok c -> c
    | Error e -> failwithf "contract build failed: %s" e

let private annotatedSchema: PropSchema =
    [ { Defaults.propDecl with
          Name = "points"
          Type = PropType.PString
          Required = true
          PayloadLanguage = Some(PayloadLanguage.gated "markdown" "cmark" "0.31") }
      { Defaults.propDecl with
          Name = "width"
          Type = PropType.PInt
          Required = true } ]

let private annotatedContract = contractWith annotatedSchema

let private annotatedRegistry = CustomRegistry.Empty.Register(annotatedContract)

[<Tests>]
let tests =
    testList
        "task 15 — first-class Custom extension registry"
        [ test "createWithSchema rejects a schema whose keys disagree with the encoder" {
              let wrongSchema: PropSchema =
                  [ { Defaults.propDecl with
                        Name = "points"
                        Type = PropType.PString
                        Required = true } ] // missing 'width'

              match
                  CustomContract.createWithSchema
                      "viz"
                      "spark"
                      wrongSchema
                      encode
                      decode
                      { Points = "0,1"; Width = 3 }
                      []
                      HashStrictness.StrictReplay
              with
              | Error msg -> Expect.stringContains msg "key set" "names the key-set mismatch"
              | Ok _ -> failtest "expected a key-set-mismatch error"
          }

          test "a typed and an untyped contract over the same props hash identically" {
              let untyped =
                  CustomContract.create
                      "viz"
                      "spark"
                      encode
                      decode
                      { Points = "0,1"; Width = 3 }
                      []
                      HashStrictness.StrictReplay

              Expect.equal
                  typedContract.Hash.Hash
                  untyped.Hash.Hash
                  "schema type detail does not change the content hash"
          }

          test "describeForAi projects the prop schema for the model's prompt context" {
              match registry.DescribeForAi() with
              | [ card ] ->
                  Expect.equal card.ModuleId "viz" "module id"
                  Expect.equal card.ComponentId "spark" "component id"

                  let byName = card.Props |> List.map (fun p -> p.Name, p.Type) |> Map.ofList
                  Expect.equal (Map.tryFind "points" byName) (Some "string") "points typed string for the AI"
                  Expect.equal (Map.tryFind "width" byName) (Some "int") "width typed int for the AI"
              | other -> failtestf "expected one card, got %A" other
          }

          test "validateProps accepts well-typed props" {
              let props = Map.ofList [ "points", JStr "0,1 2,3"; "width", JInt 5 ]
              Expect.isEmpty (registry.ValidateProps("viz", "spark", props)) "no defects for valid props"
          }

          test "validateProps flags a missing required prop and a mistyped prop" {
              // width omitted (required) + points is a number, not a string.
              let props = Map.ofList [ "points", JInt 9 ]
              let defects = registry.ValidateProps("viz", "spark", props)

              Expect.isTrue (defects |> List.exists (fun d -> d.Key = "width")) "missing required 'width' flagged"
              Expect.isTrue (defects |> List.exists (fun d -> d.Key = "points")) "mistyped 'points' flagged"
          }

          test "validateProps is silent for an unregistered component (host trust boundary)" {
              let props = Map.ofList [ "anything", JStr "x" ]

              Expect.isEmpty
                  (registry.ValidateProps("unknown", "widget", props))
                  "the registry only speaks for what it knows"
          }

          test "defects carry the FUARAN068 code (their own allocation, not the button advisory)" {
              let props = Map.ofList [ "points", JInt 9 ]

              for d in registry.ValidateProps("viz", "spark", props) do
                  Expect.equal d.Code "FUARAN068" "every custom-prop defect carries FUARAN068"

              Expect.equal CustomRegistry.propDefectCode "FUARAN068" "the code constant is pinned"
          }

          // ── validateWithRegistry — the SHIPPED enforcement path ─────────────
          //
          // The registry is no longer a library-only surface a host must
          // remember to call: `PreEmitValidate.validateWithRegistry` folds the
          // schema check into the canonical pre-emit walk.

          test "validateWithRegistry passes a well-typed registered Custom node" {
              let node: Node<unit> =
                  Fuaran.custom
                      "spark-1"
                      "viz"
                      "spark"
                      (Map.ofList [ "points", JStr "0,1 2,3"; "width", JInt 5 ])
                      None
                      []

              Expect.isOk (PreEmitValidate.validateWithRegistry registry node) "valid props pass the shipped path"
          }

          test "validateWithRegistry surfaces FUARAN068 for a schema-violating registered node" {
              // width omitted (required) + points mistyped.
              let node: Node<unit> =
                  Fuaran.custom "spark-1" "viz" "spark" (Map.ofList [ "points", JInt 9 ]) None []

              match PreEmitValidate.validateWithRegistry registry node with
              | Error defects ->
                  match
                      defects
                      |> List.tryPick (function
                          | PreEmitValidate.PreEmitDefect.CustomPropSchemaViolation(nodeId, m, c, ds) ->
                              Some(nodeId, m, c, ds)
                          | _ -> None)
                  with
                  | Some(nodeId, m, c, ds) ->
                      Expect.equal nodeId "spark-1" "names the offending node"
                      Expect.equal (m, c) ("viz", "spark") "names the component identity"

                      Expect.isTrue
                          (ds |> List.forall (fun d -> d.Code = "FUARAN068"))
                          "per-prop defects carry the code"

                      Expect.isTrue (ds |> List.exists (fun d -> d.Key = "width")) "missing required prop named"
                  | None -> failtest "expected a CustomPropSchemaViolation defect"
              | Ok() -> failtest "a schema-violating registered node must fail validateWithRegistry"
          }

          test "validateWithRegistry ignores an UNregistered custom kind (host trust boundary)" {
              let node: Node<unit> =
                  Fuaran.custom "w-1" "unknown" "widget" (Map.ofList [ "anything", JStr "x" ]) None []

              Expect.isOk
                  (PreEmitValidate.validateWithRegistry registry node)
                  "the registry only speaks for what it knows"
          }

          test "an enum prop validates membership" {
              let enumSchema: PropSchema =
                  [ { Defaults.propDecl with
                        Name = "tone"
                        Type = PropType.PEnum [ "warn"; "ok" ]
                        Required = true } ]

              let enc (s: string) = Map.ofList [ "tone", JStr s ]

              let dec (m: Map<string, JVal>) : Result<string, CustomDecodeError> =
                  match Map.tryFind "tone" m with
                  | Some(JStr s) -> Ok s
                  | _ -> Error(CustomDecodeError.payload "bad")

              let c =
                  match
                      CustomContract.createWithSchema
                          "viz"
                          "callout"
                          enumSchema
                          enc
                          dec
                          "ok"
                          []
                          HashStrictness.StrictReplay
                  with
                  | Ok c -> c
                  | Error e -> failwithf "%s" e

              let reg = CustomRegistry.Empty.Register(c)
              Expect.isEmpty (reg.ValidateProps("viz", "callout", enc "warn")) "in-enum value valid"
              Expect.isNonEmpty (reg.ValidateProps("viz", "callout", enc "nope")) "out-of-enum value flagged"
          }

          // ── Phase 1107 — the payload-language declaration ───────────────────
          //
          // A `PString` prop holding a whole inner wire format and a `PString`
          // prop holding a label were the same declaration, so a prose payload
          // passed prop validation and failed only at render. The declaration
          // makes the two different at the schema layer.

          test "an annotated schema hashes identically to the same schema without it" {
              // THE assertion the phase turns on. `customBodyShapeHash` folds the
              // module/component ids, the prop KEY SET and the exposed ids — never
              // a prop's declared detail — so adopting the declaration must not
              // move an existing component's content hash. A moved hash would
              // invalidate every StrictReplay consumer of that component for a
              // change that altered nothing about what it emits.
              let annotated =
                  match
                      CustomContract.createWithSchema
                          "viz"
                          "spark"
                          annotatedSchema
                          encode
                          decode
                          { Points = "0,1"; Width = 3 }
                          []
                          HashStrictness.StrictReplay
                  with
                  | Ok c -> c
                  | Error e -> failwithf "contract build failed: %s" e

              Expect.equal
                  annotated.Hash.Hash
                  typedContract.Hash.Hash
                  "a payload-language annotation does not move the content hash"

              let untyped =
                  CustomContract.create
                      "viz"
                      "spark"
                      encode
                      decode
                      { Points = "0,1"; Width = 3 }
                      []
                      HashStrictness.StrictReplay

              Expect.equal annotated.Hash.Hash untyped.Hash.Hash "nor does it diverge from the permissive derivation"
          }

          test "the content hash of the spark shape is pinned to its recorded value" {
              // The equality above proves the three derivations agree with EACH
              // OTHER; this pins what they agree ON. Together they refuse both a
              // schema-detail leak into the hash and a silent change to the
              // derivation itself — which the first assertion alone could not
              // see, since it compares two calls to the same function.
              //
              // The literal was computed OUTSIDE this codebase (sha256 over the
              // canonical string `Hashing.customBodyShapeHash` documents), so it
              // is evidence about the derivation rather than a recording of it.
              Expect.equal
                  typedContract.Hash.Hash
                  "3df5c6c762b90de640ac76741bb6f89e06ce2cbeb18cb3c21adee0638e2506b3"
                  "the spark body-shape hash is unmoved"

              Expect.equal
                  typedContract.Hash.Hash
                  (Hashing.customBodyShapeHash "viz" "spark" [ "points"; "width" ] [])
                  "and it is the key-set derivation and nothing else"
          }

          test "a declared payload prop is still an ordinary string to the shape check" {
              // The payload IS a string on the wire. The declaration adds a fact
              // about the string; it does not change what shape check applies —
              // which is exactly why a `PWire` PropType case would have had to
              // restate the string arm.
              let props = Map.ofList [ "points", JStr "# heading"; "width", JInt 5 ]

              Expect.isEmpty
                  (annotatedRegistry.ValidateProps("viz", "spark", props))
                  "no schema defect for a string payload"
          }

          test "validateProps distinguishes a plain string prop from a declared-wire prop owing a gate" {
              // The 1081 class, at the schema layer: the same prose payload, seen
              // by two registries that differ only in the annotation.
              let props = Map.ofList [ "points", JStr "just some prose"; "width", JInt 5 ]

              let plain = registry.ValidatePropsDetailed("viz", "spark", props)
              Expect.isEmpty plain.Defects "the undeclared contract sees no defect"
              Expect.isEmpty plain.Obligations "and — the gap — nothing else either"

              let declared = annotatedRegistry.ValidatePropsDetailed("viz", "spark", props)
              Expect.isEmpty declared.Defects "the declared contract still sees no schema DEFECT"

              match declared.Obligations with
              | [ o ] ->
                  Expect.equal o.Key "points" "the obligation names the payload prop"
                  Expect.equal o.Language "markdown" "and its inner language"
                  Expect.equal o.Kind PayloadObligationKind.GateOwed "a gate is named, so a run is owed"
                  Expect.stringContains o.Message "NOT run" "the message says the gate did not run"
              | other -> failtestf "expected exactly one obligation, got %A" other
          }

          test "a declaration naming no gate is its own obligation class" {
              let ungatedSchema: PropSchema =
                  [ { Defaults.propDecl with
                        Name = "points"
                        Type = PropType.PString
                        Required = true
                        PayloadLanguage = Some(PayloadLanguage.ungated "markdown") }
                    { Defaults.propDecl with
                        Name = "width"
                        Type = PropType.PInt
                        Required = true } ]

              let reg = CustomRegistry.Empty.Register(contractWith ungatedSchema)
              let props = Map.ofList [ "points", JStr "# heading"; "width", JInt 5 ]

              match reg.ValidatePayloads("viz", "spark", props) with
              | [ o ] ->
                  Expect.equal
                      o.Kind
                      PayloadObligationKind.Ungated
                      "declared with no gate is NOT the same as a gate owed"

                  Expect.isNone o.Gate "and carries no stamp to run"
                  Expect.stringContains o.Message "no gate" "the message says nothing can judge it"
              | other -> failtestf "expected exactly one ungated obligation, got %A" other
          }

          test "an absent or mistyped payload prop raises no obligation (no double-counting)" {
              // Absent: nothing to judge — and the missing-required defect already
              // says so. Mistyped: the FUARAN068 defect already says the shape is
              // wrong, and a gate obligation on top would report one fault twice.
              let absent =
                  annotatedRegistry.ValidatePropsDetailed("viz", "spark", Map.ofList [ "width", JInt 5 ])

              Expect.isNonEmpty absent.Defects "the missing required prop is a defect"
              Expect.isEmpty absent.Obligations "but not an obligation"

              let mistyped =
                  annotatedRegistry.ValidatePropsDetailed(
                      "viz",
                      "spark",
                      Map.ofList [ "points", JInt 9; "width", JInt 5 ]
                  )

              Expect.isNonEmpty mistyped.Defects "the mistyped prop is a defect"
              Expect.isEmpty mistyped.Obligations "and is reported once, as that"
          }

          test "the card projects the declaration for a teaching surface" {
              match annotatedRegistry.DescribeForAi() with
              | [ card ] ->
                  let byName = card.Props |> List.map (fun p -> p.Name, p) |> Map.ofList
                  let points = Map.find "points" byName
                  let width = Map.find "width" byName

                  Expect.equal points.Type "string" "the JSON shape is unchanged by the declaration"
                  Expect.equal points.PayloadLanguage (Some "markdown") "the inner language is on the card"
                  Expect.equal points.PayloadGate (Some "cmark:0.31") "with the gate stamp beside it"
                  Expect.isNone width.PayloadLanguage "an ordinary prop declares none"
                  Expect.isNone width.PayloadGate "and names no gate"
              | other -> failtestf "expected one card, got %A" other
          }

          test "payloadTag renders the two declared states distinguishably" {
              Expect.equal
                  (CustomRegistry.payloadTag (Some(PayloadLanguage.gated "markdown" "cmark" "0.31")))
                  (Some "markdown (gate cmark:0.31)")
                  "gated"

              Expect.equal
                  (CustomRegistry.payloadTag (Some(PayloadLanguage.ungated "markdown")))
                  (Some "markdown (NO GATE)")
                  "ungated says so loudly rather than by omission"

              Expect.isNone (CustomRegistry.payloadTag Option.None) "an ordinary prop renders nothing"
          }

          test "an empty gate version degrades to the bare gate name" {
              let g: PayloadGate = { Gate = "cmark"; Version = "" }
              Expect.equal g.AsStamp "cmark" "no trailing colon"
          }

          // ── Phase 1107 task 3 — the provenance shape ────────────────────────

          test "provenance is derivable only for a prop the contract declares" {
              let declared =
                  PayloadProvenance.forUpdate annotatedContract "points" PayloadGateVerdict.Accepted

              Expect.isSome declared "a declared-wire prop yields a record"

              Expect.isNone
                  (PayloadProvenance.forUpdate annotatedContract "width" PayloadGateVerdict.Accepted)
                  "an ordinary prop cannot be attributed to a gate"

              Expect.isNone
                  (PayloadProvenance.forUpdate annotatedContract "nope" PayloadGateVerdict.Accepted)
                  "nor can a prop the contract does not declare at all"
          }

          test "the attribution line reads the same for every host, and never hides an unjudged update" {
              let line verdict =
                  PayloadProvenance.forUpdate annotatedContract "points" verdict
                  |> Option.map PayloadProvenance.attribution

              Expect.equal
                  (line PayloadGateVerdict.Accepted)
                  (Some "via markdown gate cmark:0.31 — accepted")
                  "accepted"

              Expect.equal
                  (line (PayloadGateVerdict.Refused "unterminated fence"))
                  (Some "via markdown gate cmark:0.31 — refused: unterminated fence")
                  "refused carries the gate's own reason"

              Expect.equal
                  (line PayloadGateVerdict.NotRun)
                  (Some "via markdown gate cmark:0.31 — NOT RUN")
                  "and an unjudged update says NOT RUN rather than being omitted"
          }

          test "an ungated declaration renders its missing gate in the stamp slot" {
              let ungatedContract =
                  contractWith
                      [ { Defaults.propDecl with
                            Name = "points"
                            Type = PropType.PString
                            Required = true
                            PayloadLanguage = Some(PayloadLanguage.ungated "markdown") }
                        { Defaults.propDecl with
                            Name = "width"
                            Type = PropType.PInt
                            Required = true } ]

              Expect.equal
                  (PayloadProvenance.forUpdate ungatedContract "points" PayloadGateVerdict.NotRun
                   |> Option.map PayloadProvenance.attribution)
                  (Some "via markdown gate <ungated> — NOT RUN")
                  "the line never reads as though a gate were named"
          }

          test "payloadProps enumerates the declared-wire props in schema order" {
              Expect.equal
                  (CustomContract.payloadProps annotatedContract |> List.map fst)
                  [ "points" ]
                  "only the declared prop"

              Expect.isEmpty (CustomContract.payloadProps typedContract) "an unannotated contract declares none"
          } ]
