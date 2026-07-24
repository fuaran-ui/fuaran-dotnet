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
    [ { Name = "points"
        Type = PropType.PString
        Required = true }
      { Name = "width"
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

[<Tests>]
let tests =
    testList
        "task 15 — first-class Custom extension registry"
        [ test "createWithSchema rejects a schema whose keys disagree with the encoder" {
              let wrongSchema: PropSchema =
                  [ { Name = "points"
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
                  [ { Name = "tone"
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
          } ]
