module Fuaran.UI.Tests.SchemaForm

// ============================================================================
//  Phase 1816 — JSON Schema -> `Form` is a derivation. These pin the mapping
//  table row by row, that every derived tree passes the pre-emit validator
//  (and the wire-bound one), that a refusal names its schema path, and that
//  the derivation is deterministic with field order = `properties` order.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.SchemaForm

let private parse (text: string) : JVal =
    match Json.parseTolerantOfNull text with
    | Ok v -> v
    | Error e -> failwithf "test schema does not parse: %s" e

let private opts = SchemaFormOptions.defaults<unit>

let private deriveOk (schemaText: string) : Node<unit> =
    match derive opts (parse schemaText) with
    | Ok node -> node
    | Error refusals -> failwithf "expected a form, got refusals %A" refusals

let private fieldsOf (node: Node<unit>) : FormField<unit> list =
    match node.Kind with
    | NodeKind.Form spec -> spec.Fields
    | other -> failwithf "expected a Form, got %A" other

/// The single field a one-property schema derives to.
let private one (propertySchema: string) : FormField<unit> =
    match fieldsOf (deriveOk ("""{"type":"object","properties":{"f":""" + propertySchema + "}}")) with
    | [ f ] -> f
    | fs -> failwithf "expected one field, got %d" fs.Length

let private refusalsOf (schemaText: string) : SchemaFormRefusal list =
    match derive opts (parse schemaText) with
    | Ok _ -> failwith "expected refusals, got a form"
    | Error refusals -> refusals

let private expectValid (node: Node<unit>) =
    match PreEmitValidate.validate node with
    | Ok() -> ()
    | Error defects -> failtestf "PreEmitValidate refused the derived tree: %A" defects

    match PreEmitValidate.validateForTransport node with
    | Ok() -> ()
    | Error defects -> failtestf "the wire-bound validator refused the derived tree: %A" defects

/// The acceptance schema: string, number, enum, boolean and bounded fields.
let private acceptanceSchema =
    """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Booking",
  "type": "object",
  "required": ["name", "guests", "room"],
  "properties": {
    "name":    { "type": "string", "title": "Full name", "minLength": 2, "maxLength": 80 },
    "email":   { "type": "string", "format": "email", "description": "We send the confirmation here." },
    "guests":  { "type": "integer", "minimum": 1, "maximum": 8 },
    "budget":  { "type": "number" },
    "room":    { "enum": ["single", "double", "suite"] },
    "smoking": { "type": "boolean", "default": false },
    "arrival": { "type": "string", "format": "date" },
    "notes":   { "type": "string", "maxLength": 2000 },
    "extras":  { "type": "array", "items": { "enum": ["breakfast", "parking", "late-checkout"] } },
    "address": {
      "type": "object",
      "title": "Address",
      "properties": {
        "street": { "type": "string" },
        "zip":    { "type": "string", "pattern": "^[0-9]{5}$" }
      }
    }
  }
}"""

/// A label or help text as its literal text (TextSource carries bindings, so it
/// has no structural equality of its own).
let private lit (t: TextSource) : string =
    match t with
    | TextSource.Literal s -> s
    | other -> sprintf "<non-literal %A>" other

let private kindName (k: FormFieldKind<unit>) =
    match k with
    | FormFieldKind.Text _ -> "Text"
    | FormFieldKind.TextArea _ -> "TextArea"
    | FormFieldKind.Number _ -> "Number"
    | FormFieldKind.RangedNumber _ -> "RangedNumber"
    | FormFieldKind.Checkbox _ -> "Checkbox"
    | FormFieldKind.Toggle _ -> "Toggle"
    | FormFieldKind.SegmentedChoice _ -> "SegmentedChoice"
    | FormFieldKind.Choice _ -> "Choice"
    | FormFieldKind.Combobox _ -> "Combobox"
    | FormFieldKind.DateTime _ -> "DateTime"
    | FormFieldKind.Color _ -> "Color"
    | FormFieldKind.Tokens _ -> "Tokens"
    | other -> sprintf "%A" other

let private enumOf (n: int) =
    "[" + (List.init n (fun i -> sprintf "\"v%d\"" i) |> String.concat ",") + "]"

[<Tests>]
let mappingTable =
    testList
        "SchemaForm mapping table (Phase 1816)"
        [ test "string -> Text" {
              let f = one """{"type":"string"}"""
              Expect.equal (kindName f.Kind) "Text" "control"
              Expect.isNone f.Rule "no rule when the schema declares none"
          }
          test "string past the maxLength threshold -> TextArea, carrying the bound" {
              let f = one """{"type":"string","maxLength":201}"""

              match f.Kind with
              | FormFieldKind.TextArea(None, None, rows) -> Expect.equal rows opts.TextAreaRows "rows"
              | k -> failtestf "expected TextArea, got %s" (kindName k)

              Expect.equal (f.Rule |> Option.bind _.MaxLength) (Some 201) "maxLength carried"
          }
          test "string AT the maxLength threshold stays Text" {
              Expect.equal (kindName (one """{"type":"string","maxLength":200}""").Kind) "Text" "inclusive threshold"
          }
          test "format date | time | date-time -> DateTime with the matching variant" {
              for fmt, variant in
                  [ "date", DateTimeVariant.Date
                    "time", DateTimeVariant.Time
                    "date-time", DateTimeVariant.DateTime ] do
                  match (one (sprintf """{"type":"string","format":"%s"}""" fmt)).Kind with
                  | FormFieldKind.DateTime(_, _, v, None, None, None) -> Expect.equal v variant fmt
                  | k -> failtestf "%s: expected DateTime, got %s" fmt (kindName k)
          }
          test "format color -> Color" {
              Expect.equal (kindName (one """{"type":"string","format":"color"}""").Kind) "Color" "control"
          }
          test "format email | uri -> Text with the Phase 864 format rule" {
              Expect.equal
                  ((one """{"type":"string","format":"email"}""").Rule |> Option.bind _.Format)
                  (Some TextFormat.Email)
                  "email"

              Expect.equal
                  ((one """{"type":"string","format":"uri"}""").Rule |> Option.bind _.Format)
                  (Some TextFormat.Url)
                  "uri"
          }
          test "minLength / maxLength / pattern -> the Phase 864 rule slots" {
              let f = one """{"type":"string","minLength":2,"maxLength":10,"pattern":"^a"}"""

              match f.Rule with
              | Some r ->
                  Expect.equal r.MinLength (Some 2) "minLength"
                  Expect.equal r.MaxLength (Some 10) "maxLength"
                  Expect.equal r.Pattern (Some "^a") "pattern"
                  Expect.isNone r.Compare "no compare"
              | None -> failtest "expected a rule"
          }
          test "number with no bound -> Number" {
              Expect.equal (kindName (one """{"type":"number"}""").Kind) "Number" "control"
          }
          test "number with both bounds -> RangedNumber carrying both" {
              match (one """{"type":"number","minimum":0.5,"maximum":9.5}""").Kind with
              | FormFieldKind.RangedNumber(_, _, Some 0.5, Some 9.5, None) -> ()
              | k -> failtestf "got %A" k
          }
          test "number with ONE bound -> RangedNumber, so the bound is not lost" {
              match (one """{"type":"number","minimum":3}""").Kind with
              | FormFieldKind.RangedNumber(_, _, Some 3.0, None, None) -> ()
              | k -> failtestf "got %A" k
          }
          test "integer -> RangedNumber stepping by 1 (or multipleOf)" {
              match (one """{"type":"integer"}""").Kind with
              | FormFieldKind.RangedNumber(_, _, None, None, Some 1.0) -> ()
              | k -> failtestf "got %A" k

              match (one """{"type":"integer","multipleOf":5}""").Kind with
              | FormFieldKind.RangedNumber(_, _, None, None, Some 5.0) -> ()
              | k -> failtestf "got %A" k
          }
          test "integer exclusive bounds -> the next whole number inside" {
              match (one """{"type":"integer","exclusiveMinimum":0,"exclusiveMaximum":10}""").Kind with
              | FormFieldKind.RangedNumber(_, _, Some 1.0, Some 9.0, Some 1.0) -> ()
              | k -> failtestf "got %A" k
          }
          test "boolean -> Checkbox by default, Toggle by option" {
              Expect.equal (kindName (one """{"type":"boolean"}""").Kind) "Checkbox" "default"

              let toggled =
                  derive
                      { opts with
                          BooleanControl = BooleanControl.Toggle }
                      (parse """{"type":"object","properties":{"b":{"type":"boolean"}}}""")

              match toggled with
              | Ok node -> Expect.equal (fieldsOf node |> List.map (_.Kind >> kindName)) [ "Toggle" ] "option"
              | Error r -> failtestf "%A" r
          }
          test "enum -> SegmentedChoice / Choice / Combobox by size" {
              Expect.equal (kindName (one (sprintf """{"enum":%s}""" (enumOf 4))).Kind) "SegmentedChoice" "4"
              Expect.equal (kindName (one (sprintf """{"enum":%s}""" (enumOf 5))).Kind) "Choice" "5"
              Expect.equal (kindName (one (sprintf """{"enum":%s}""" (enumOf 12))).Kind) "Choice" "12"

              match (one (sprintf """{"type":"string","enum":%s}""" (enumOf 13))).Kind with
              | FormFieldKind.Combobox(false, None, Binding.Static(Some os), None) ->
                  Expect.equal os.Length 13 "every member is an option"
              | k -> failtestf "13: got %s" (kindName k)
          }
          test "enum options keep the schema's member order" {
              match (one """{"enum":["c","a","b"]}""").Kind with
              | FormFieldKind.SegmentedChoice(Binding.Static(Some os), _, _, _) ->
                  Expect.equal (os |> List.map _.Value) [ "c"; "a"; "b" ] "order"
              | k -> failtestf "got %s" (kindName k)
          }
          test "array of enum -> Tokens with no free text" {
              match (one """{"type":"array","items":{"type":"string","enum":["x","y"]},"uniqueItems":true}""").Kind with
              | FormFieldKind.Tokens(false, None, Some(Binding.Static(Some os)), None) ->
                  Expect.equal (os |> List.map _.Value) [ "x"; "y" ] "suggestions"
              | k -> failtestf "got %s" (kindName k)
          }
          test "required -> FormField.Required" {
              let fields =
                  fieldsOf (
                      deriveOk
                          """{"type":"object","required":["a"],"properties":{"a":{"type":"string"},"b":{"type":"string"}}}"""
                  )

              Expect.equal (fields |> List.map (fun f -> f.Id, f.Required)) [ "a", true; "b", false ] "required"
          }
          test "title / description -> label / help; the property name when untitled" {
              let f =
                  one """{"type":"string","title":"Given name","description":"As on your passport"}"""

              Expect.equal (lit f.Label) "Given name" "label"
              Expect.equal (f.Help |> Option.map lit) (Some "As on your passport") "help"
              Expect.equal (lit (one """{"type":"string"}""").Label) "f" "fallback label"
          }
          test "default -> the value binding State(field id, default)" {
              match (one """{"type":"string","default":"hi"}""").Kind with
              | FormFieldKind.Text(Some(Binding.State("f", Some "hi")), None) -> ()
              | k -> failtestf "got %A" k

              match (one """{"type":"integer","minimum":1,"default":3}""").Kind with
              | FormFieldKind.RangedNumber(Some(Binding.State("f", Some 3.0)), _, _, _, _) -> ()
              | k -> failtestf "got %A" k
          }
          test "one nested object lowers to a labelled group of prefixed fields, in place" {
              let fields =
                  fieldsOf (
                      deriveOk
                          """{"type":"object","required":["addr"],"properties":{
                               "before":{"type":"string"},
                               "addr":{"type":"object","title":"Address","required":["street"],
                                       "properties":{"street":{"type":"string"},"zip":{"type":"string"}}},
                               "after":{"type":"string"}}}"""
                  )

              Expect.equal
                  (fields |> List.map (fun f -> f.Id, lit f.Label, f.Required))
                  [ "before", "before", false
                    "addr.street", "Address: street", true
                    "addr.zip", "Address: zip", false
                    "after", "after", false ]
                  "group lowering"
          }
          test "a local $ref resolves, and a sibling title wins" {
              let fields =
                  fieldsOf (
                      deriveOk
                          """{"type":"object","$defs":{"size":{"enum":["s","m","l"],"title":"Size"}},
                              "properties":{"a":{"$ref":"#/$defs/size"},"b":{"$ref":"#/$defs/size","title":"Other"}}}"""
                  )

              Expect.equal
                  (fields |> List.map (fun f -> kindName f.Kind, lit f.Label))
                  [ "SegmentedChoice", "Size"; "SegmentedChoice", "Other" ]
                  "resolved"
          }
          test "type [T, null] reads as T" {
              Expect.equal (kindName (one """{"type":["string","null"]}""").Kind) "Text" "nullable string"
          } ]

[<Tests>]
let validity =
    testList
        "SchemaForm derived trees pass the validator (Phase 1816)"
        [ test "the acceptance schema derives a Form carrying every required and bound" {
              let node = deriveOk acceptanceSchema
              expectValid node
              let fields = fieldsOf node

              Expect.equal
                  (fields |> List.map (fun f -> f.Id, kindName f.Kind, f.Required))
                  [ "name", "Text", true
                    "email", "Text", false
                    "guests", "RangedNumber", true
                    "budget", "Number", false
                    "room", "SegmentedChoice", true
                    "smoking", "Checkbox", false
                    "arrival", "DateTime", false
                    "notes", "TextArea", false
                    "extras", "Tokens", false
                    "address.street", "Text", false
                    "address.zip", "Text", false ]
                  "field order = properties order; controls per the table"

              let byId id =
                  fields |> List.find (fun f -> f.Id = id)

              match (byId "guests").Kind with
              | FormFieldKind.RangedNumber(_, _, Some 1.0, Some 8.0, Some 1.0) -> ()
              | k -> failtestf "guests bounds lost: %A" k

              Expect.equal
                  ((byId "name").Rule |> Option.map (fun r -> r.MinLength, r.MaxLength))
                  (Some(Some 2, Some 80))
                  "name bounds"

              Expect.equal ((byId "address.zip").Rule |> Option.bind _.Pattern) (Some "^[0-9]{5}$") "pattern"
          }
          test "every mapping-table row derives a tree the validator accepts" {
              let rows =
                  [ """{"type":"string"}"""
                    """{"type":"string","maxLength":5000,"minLength":1}"""
                    """{"type":"string","format":"date"}"""
                    """{"type":"string","format":"time"}"""
                    """{"type":"string","format":"date-time"}"""
                    """{"type":"string","format":"color"}"""
                    """{"type":"string","format":"email","maxLength":5000}"""
                    """{"type":"string","format":"uri"}"""
                    """{"type":"string","pattern":"^x"}"""
                    """{"type":"number"}"""
                    """{"type":"number","minimum":0}"""
                    """{"type":"number","maximum":0,"multipleOf":0.25}"""
                    """{"type":"integer","minimum":0,"maximum":10,"default":2}"""
                    """{"type":"boolean","default":true}"""
                    sprintf """{"enum":%s,"default":"v1"}""" (enumOf 3)
                    sprintf """{"enum":%s}""" (enumOf 9)
                    sprintf """{"enum":%s}""" (enumOf 40)
                    """{"type":"array","items":{"enum":["a","b"]},"default":["a"]}"""
                    """{"type":"object","properties":{"x":{"type":"string"},"y":{"type":"integer"}}}""" ]

              for row in rows do
                  let node =
                      deriveOk ("""{"type":"object","required":["f"],"properties":{"f":""" + row + "}}")

                  expectValid node
          }
          test "the canonical wire bytes decode back to the same tree" {
              match deriveWire opts (parse acceptanceSchema) with
              | Ok json ->
                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
                  | Ok decoded -> Expect.equal (Generated.encodeNode decoded) json "round-trips byte-for-byte"
                  | Error e -> failtestf "the derived wire does not decode: %s at %s" e.Message e.Path
              | Error refusals -> failtestf "%s" refusals
          } ]

let private expectRefusal (schemaText: string) (path: string) (code: string) =
    let refusals = refusalsOf schemaText

    Expect.exists
        refusals
        (fun r -> r.Path = path && SchemaFormRefusal.codeName r.Code = code)
        (sprintf "a %s refusal at %s; got %A" code path refusals)

[<Tests>]
let refusals =
    testList
        "SchemaForm refuses by name, with the schema path (Phase 1816)"
        [ test "oneOf" {
              expectRefusal
                  """{"type":"object","properties":{"a":{"type":"string","oneOf":[{"minLength":1}]}}}"""
                  "/properties/a/oneOf"
                  "combinator"
          }
          test "anyOf" {
              expectRefusal
                  """{"type":"object","properties":{"a":{"type":"string","anyOf":[{"minLength":1}]}}}"""
                  "/properties/a/anyOf"
                  "combinator"
          }
          test "a $ref cycle" {
              expectRefusal
                  """{"type":"object","properties":{"self":{"$ref":"#"}}}"""
                  "/properties/self/properties/self/$ref"
                  "ref-cycle"

              expectRefusal
                  """{"type":"object","$defs":{"a":{"$ref":"#/$defs/b"},"b":{"$ref":"#/$defs/a"}},"properties":{"p":{"$ref":"#/$defs/a"}}}"""
                  "/properties/p/$ref"
                  "ref-cycle"
          }
          test "a non-local or dangling $ref" {
              expectRefusal
                  """{"type":"object","properties":{"a":{"$ref":"https://example.com/s.json"}}}"""
                  "/properties/a/$ref"
                  "ref-unresolved"

              expectRefusal
                  """{"type":"object","properties":{"a":{"$ref":"#/$defs/missing"}}}"""
                  "/properties/a/$ref"
                  "ref-unresolved"
          }
          test "an object beyond one nested level" {
              expectRefusal
                  """{"type":"object","properties":{"a":{"type":"object","properties":{"b":{"type":"object","properties":{"c":{"type":"string"}}}}}}}"""
                  "/properties/a/properties/b"
                  "nesting-too-deep"
          }
          test "a keyword the chosen control cannot carry" {
              expectRefusal
                  """{"type":"object","properties":{"d":{"type":"string","format":"date","pattern":"x"}}}"""
                  "/properties/d/pattern"
                  "keyword-not-carried"

              expectRefusal
                  """{"type":"object","properties":{"n":{"type":"number","exclusiveMinimum":0}}}"""
                  "/properties/n/exclusiveMinimum"
                  "keyword-not-carried"
          }
          test "an unknown keyword, an unknown format, a non-string enum" {
              expectRefusal
                  """{"type":"object","properties":{"a":{"type":"string","readOnly":true}}}"""
                  "/properties/a/readOnly"
                  "unsupported-keyword"

              expectRefusal
                  """{"type":"object","properties":{"a":{"type":"string","format":"ipv4"}}}"""
                  "/properties/a/format"
                  "unsupported-format"

              expectRefusal
                  """{"type":"object","properties":{"a":{"enum":["x",1]}}}"""
                  "/properties/a/enum"
                  "enum-not-strings"
          }
          test "a required child under an optional nested object" {
              expectRefusal
                  """{"type":"object","properties":{"o":{"type":"object","required":["x"],"properties":{"x":{"type":"string"}}}}}"""
                  "/properties/o/properties/x"
                  "required-under-optional-object"
          }
          test "a root that is not an object, and a root with no properties" {
              expectRefusal """{"type":"string"}""" "" "root-not-object"
              expectRefusal """{"type":"object","properties":{}}""" "" "no-fields"
          }
          test "a pointer token is escaped per RFC 6901" {
              expectRefusal
                  """{"type":"object","properties":{"a/b~c":{"type":"string","anyOf":[]}}}"""
                  "/properties/a~1b~0c/anyOf"
                  "combinator"
          }
          test "every refusal is reported, not only the first; nothing is silently skipped" {
              let refusals =
                  refusalsOf
                      """{"type":"object","properties":{"a":{"type":"string","oneOf":[]},"b":{"type":"null"},"c":{"type":"string"}}}"""

              Expect.equal
                  (refusals |> List.map (fun r -> r.Path, SchemaFormRefusal.codeName r.Code))
                  [ "/properties/a/oneOf", "combinator"
                    "/properties/b/type", "unsupported-type" ]
                  "both refusals, in schema order"
          }
          test "the wire refusal envelope names code, message and path" {
              match deriveWireFromText opts """{"type":"object","properties":{"a":{"anyOf":[]}}}""" with
              | Error json ->
                  Expect.stringContains json "\"code\":\"combinator\"" "code"
                  Expect.stringContains json "\"path\":\"/properties/a/anyOf\"" "path"
              | Ok _ -> failtest "expected a refusal"

              match deriveWireFromText opts "{not json" with
              | Error json -> Expect.stringContains json "\"code\":\"schema-not-json\"" "unparseable text"
              | Ok _ -> failtest "expected a refusal"
          } ]

[<Tests>]
let determinism =
    testList
        "SchemaForm is deterministic (Phase 1816)"
        [ test "same schema, same bytes" {
              let a = deriveWire opts (parse acceptanceSchema)
              let b = deriveWire opts (parse acceptanceSchema)
              Expect.equal a b "byte-identical"
          }
          test "field order follows properties order, not the alphabet" {
              let ids text =
                  fieldsOf (deriveOk text) |> List.map _.Id

              Expect.equal
                  (ids
                      """{"type":"object","properties":{"z":{"type":"string"},"a":{"type":"string"},"m":{"type":"string"}}}""")
                  [ "z"; "a"; "m" ]
                  "schema order"

              Expect.equal
                  (ids
                      """{"type":"object","properties":{"m":{"type":"string"},"z":{"type":"string"},"a":{"type":"string"}}}""")
                  [ "m"; "z"; "a" ]
                  "reordered schema, reordered fields"
          } ]
