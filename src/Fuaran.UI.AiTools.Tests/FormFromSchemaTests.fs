module Fuaran.UI.AiTools.Tests.FormFromSchema

// ============================================================================
//  Phase 1816 — `fuaran.formFromSchema`, the AI-tool face of the JSON Schema ->
//  Form derivation. The tool is `SchemaForm.deriveWire` over its `schema`
//  argument, so its bytes are pinned against `SchemaForm.deriveWireFromText` —
//  the exact function `fuaran scaffold form --schema <file>` prints (the CLI
//  side of the same pin lives in the CLI tests).
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI.AiTools
open Fuaran.UI.SchemaForm

let private schemaText =
    """{"type":"object","required":["name"],"properties":{
         "name":{"type":"string","maxLength":40},
         "size":{"enum":["s","m","l"]},
         "count":{"type":"integer","minimum":1,"maximum":9},
         "opt":{"type":"boolean"}}}"""

let private parse (text: string) =
    match Json.parseTolerantOfNull text with
    | Ok v -> v
    | Error e -> failwithf "test schema does not parse: %s" e

[<Tests>]
let tests =
    testList
        "fuaran.formFromSchema (Phase 1816)"
        [ test "the tool is named fuaran.formFromSchema and declares a required schema argument" {
              Expect.equal Tools.FormFromSchemaToolName "fuaran.formFromSchema" "name"

              Expect.stringContains
                  (Json.render Tools.formFromSchemaInputSchema)
                  "\"required\":[\"schema\"]"
                  "schema is required"
          }
          test "the tool returns exactly the bytes the CLI verb's derivation prints" {
              let tool = Tools.formFromSchema (JObj [ "schema", parse schemaText ])
              let cli = deriveWireFromText SchemaFormOptions.defaults<obj> schemaText

              match tool, cli with
              | Ok a, Ok b -> Expect.equal a b "same bytes"
              | _ -> failtestf "expected two forms, got %A / %A" tool cli
          }
          test "formId and submitLabel reach the derived node" {
              match
                  Tools.formFromSchema (
                      JObj
                          [ "schema", parse schemaText
                            "formId", JStr "booking"
                            "submitLabel", JStr "Book" ]
                  )
              with
              | Ok json ->
                  Expect.stringContains json "\"id\":\"booking\"" "form id"
                  Expect.stringContains json "\"submitLabel\":\"Book\"" "submit label"
              | Error e -> failtestf "%s" e
          }
          test "a refused schema returns the refusal envelope, byte-identical to the CLI's" {
              let refused = """{"type":"object","properties":{"a":{"oneOf":[]}}}"""
              let tool = Tools.formFromSchema (JObj [ "schema", parse refused ])
              let cli = deriveWireFromText SchemaFormOptions.defaults<obj> refused

              match tool, cli with
              | Error a, Error b ->
                  Expect.equal a b "same refusal bytes"
                  Expect.stringContains a "\"path\":\"/properties/a/oneOf\"" "names the path"
              | _ -> failtestf "expected two refusals, got %A / %A" tool cli
          }
          test "wrong arguments are an invalid-arguments refusal, not a throw" {
              for args, path in
                  [ JStr "x", ""
                    JObj [], "/schema"
                    JObj [ "schema", parse schemaText; "extra", JInt 1 ], "/extra"
                    JObj [ "schema", parse schemaText; "formId", JStr "" ], "/formId" ] do
                  match Tools.formFromSchema args with
                  | Error json ->
                      Expect.stringContains json "\"code\":\"invalid-arguments\"" "code"
                      Expect.stringContains json (sprintf "\"path\":\"%s\"" path) "path"
                  | Ok _ -> failtestf "expected a refusal for %A" args
          } ]
