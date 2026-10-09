// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Andrew J. Willshire / Diametrical Ltd

module Fuaran.UI.SchemaForm

// ============================================================================
//  Phase 1816 — JSON Schema -> `Form`, as a DERIVATION.
//
//  Wherever a form is wanted there is very often a JSON Schema already: a
//  tool's input schema, an API's request body, an elicitation contract.
//  Writing the `Form` node by hand from it is transcription, and transcription
//  is where required-ness, bounds and enums get lost. `derive` is a pure
//  function — schema in, `Form` node out — over vocabulary that already
//  exists: no new node kind, no wire change, and the output passes the
//  ordinary pre-emit validator like any authored tree.
//
//  The schema is read as data through the tier's own ordered JSON value,
//  `Fuaran.Core.JVal` (object members are an ordered list, so field order is
//  the schema's `properties` order), with no schema library. FSharp.Core and
//  Fuaran.Core only, so it runs identically under .NET and Fable.
//
//  ── The mapping table ──────────────────────────────────────────────────────
//
//  | Schema                                         | Control                                  |
//  |------------------------------------------------|------------------------------------------|
//  | `string`                                       | `Text`                                   |
//  | `string`, `maxLength` > `TextAreaThreshold`,   | `TextArea` (`TextAreaRows` rows)         |
//  |   no `format`                                  |                                          |
//  | `string`, `format: date` / `time` / `date-time`| `DateTime`, variant `Date`/`Time`/`DateTime` |
//  | `string`, `format: color`                      | `Color`                                  |
//  | `string`, `format: email` / `uri`              | `Text` + rule `format` `email` / `url`   |
//  | `number` / `integer`, no bound, no step        | `Number`                                 |
//  | `number` / `integer` with ANY of `minimum`,    | `RangedNumber` (min / max / step)        |
//  |   `maximum`, `multipleOf`, or `integer`        |   `integer` steps by 1 (or `multipleOf`) |
//  | `boolean`                                      | `Checkbox`, or `Toggle` by option        |
//  | `enum` (strings), count <= `SegmentedMax`      | `SegmentedChoice` (horizontal)           |
//  | `enum` (strings), count <= `ChoiceMax`         | `Choice`                                 |
//  | `enum` (strings), count > `ChoiceMax`          | `Combobox`, no free text                 |
//  | `array` whose `items` is a string `enum`       | `Tokens`, no free text, enum as suggestions |
//  | `object` one level below the root              | its fields, in place, as a labelled group: |
//  |                                                |   id `<parent>.<child>`, label `<Parent>: <Child>` |
//  | `required`                                     | `FormField.Required`                     |
//  | `minLength` / `maxLength` / `pattern`          | the Phase 864 `FieldRule` slots          |
//  | `minimum` / `maximum`                          | the control's own bound (never a rule — the |
//  |                                                |   Phase 864 reuse rule)                  |
//  | `title` / `description`                        | label (property name when absent) / help |
//  | `default`                                      | the value binding `State(<field id>, default)` |
//  |                                                |   (none when it equals the control's own empty |
//  |                                                |   placeholder — the §16 canonical absence) |
//  | `integer` `exclusiveMinimum` / `exclusiveMaximum` | the next whole number inside the bound |
//  | local `$ref` (`#/...`)                         | resolved; sibling `title` / `description` win |
//  | `type: [T, "null"]`                            | `T` (the form's absence is the null)     |
//  | `additionalProperties: true / false`           | accepted — a form emits declared keys only |
//  | `$schema` `$id` `$comment` `examples` `$defs` `definitions` | annotations, not constraints: read, not rendered |
//
//  A nested object's own `description`, and the root's `title` /
//  `description`, have nowhere to go: `FormSpec` has no group or heading slot,
//  and adding one is a vocabulary change this derivation deliberately does not
//  make. They are annotations, so the tree is not weakened by their absence.
//
//  ── Refuse by name, never guess ────────────────────────────────────────────
//
//  Every construct the table does not cover is a typed `SchemaFormRefusal`
//  naming the schema path (RFC 6901 JSON Pointer), and every refusal in the
//  schema is reported, not just the first: `oneOf` / `anyOf` / `allOf` / `not`
//  / conditionals, a `$ref` cycle or a non-local `$ref`, an `object` more than
//  one level below the root, a keyword the chosen control cannot carry (a
//  `pattern` on a date, an `exclusiveMinimum` on a `number`), an unknown
//  keyword, a non-string enum, and a `required` child under an OPTIONAL
//  nested object (the flat form cannot say "required only if the parent is
//  present", and guessing either way changes what the schema accepts).
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// Which control a `boolean` property derives to.
[<RequireQualifiedAccess>]
type BooleanControl =
    | Checkbox
    | Toggle

/// The knobs of the derivation. Every threshold is inclusive on the smaller
/// control: an enum of exactly `SegmentedMax` members is a `SegmentedChoice`.
type SchemaFormOptions<'Msg> =
    {
        /// The `Form` node's id.
        FormId: string
        SubmitLabel: TextSource
        OnSubmit: Action<'Msg>
        /// A `string` whose `maxLength` exceeds this becomes a `TextArea`.
        TextAreaThreshold: int
        TextAreaRows: int
        /// An enum of at most this many members is a `SegmentedChoice`.
        SegmentedMax: int
        /// An enum of at most this many members (and more than
        /// `SegmentedMax`) is a `Choice`; a larger one is a `Combobox`.
        ChoiceMax: int
        BooleanControl: BooleanControl
    }

module SchemaFormOptions =

    /// The defaults the AI tool and the CLI verb both use. The submit action is
    /// the empty `Chain` — wire-representable and inert, for the host to bind —
    /// because a derivation has no way to know what submitting means.
    let defaults<'Msg> : SchemaFormOptions<'Msg> =
        { FormId = "schema-form"
          SubmitLabel = TextSource.Literal "Submit"
          OnSubmit = Action.Chain []
          TextAreaThreshold = 200
          TextAreaRows = 4
          SegmentedMax = 4
          ChoiceMax = 12
          BooleanControl = BooleanControl.Checkbox }

/// Why a schema (or a part of it) was not derived. Each case names the thing it
/// refused; the path is carried beside it on `SchemaFormRefusal`.
[<RequireQualifiedAccess>]
type SchemaFormRefusalCode =
    /// The input text is not JSON the tier's parser accepts.
    | SchemaNotJson of message: string
    /// The root is not an object schema.
    | RootNotObject
    /// The root object declares no properties, so there is no form.
    | NoFields
    /// `oneOf` / `anyOf` / `allOf` / `not` / `if` / `then` / `else` /
    /// `dependentSchemas` / `dependentRequired` / `dependencies`.
    | Combinator of keyword: string
    /// A `$ref` that reaches itself.
    | RefCycle of reference: string
    /// A `$ref` that is not a local pointer, or points at nothing.
    | RefUnresolved of reference: string
    /// An `object` more than one level below the root.
    | NestingTooDeep
    /// A `type` the table has no control for (`null`, an array of non-enum
    /// items, a union of several types, or no type at all).
    | UnsupportedType of typeName: string
    /// A string `format` the table has no control or rule for.
    | UnsupportedFormat of format: string
    /// A keyword the table does not read at all for this type.
    | UnsupportedKeyword of keyword: string
    /// A keyword the table reads, but the control the property derived to
    /// cannot carry it.
    | KeywordNotCarried of keyword: string * control: string
    /// A keyword whose value is malformed or contradicts another.
    | InvalidKeywordValue of keyword: string
    /// An `enum` with a member that is not a string.
    | EnumNotStrings
    /// A property name that cannot be a field id (empty, or a host-reserved
    /// state key).
    | InvalidPropertyName of name: string
    /// Two properties derive to the same field id.
    | DuplicateFieldId of fieldId: string
    /// A child listed in `required` under a nested object that is itself
    /// optional.
    | RequiredUnderOptionalObject of property: string

/// A refusal and the schema path it is about (RFC 6901 JSON Pointer; `""` is
/// the root).
type SchemaFormRefusal =
    { Path: string
      Code: SchemaFormRefusalCode }

module SchemaFormRefusal =

    /// The stable kebab-case code a refusal carries on the wire.
    let codeName (code: SchemaFormRefusalCode) : string =
        match code with
        | SchemaFormRefusalCode.SchemaNotJson _ -> "schema-not-json"
        | SchemaFormRefusalCode.RootNotObject -> "root-not-object"
        | SchemaFormRefusalCode.NoFields -> "no-fields"
        | SchemaFormRefusalCode.Combinator _ -> "combinator"
        | SchemaFormRefusalCode.RefCycle _ -> "ref-cycle"
        | SchemaFormRefusalCode.RefUnresolved _ -> "ref-unresolved"
        | SchemaFormRefusalCode.NestingTooDeep -> "nesting-too-deep"
        | SchemaFormRefusalCode.UnsupportedType _ -> "unsupported-type"
        | SchemaFormRefusalCode.UnsupportedFormat _ -> "unsupported-format"
        | SchemaFormRefusalCode.UnsupportedKeyword _ -> "unsupported-keyword"
        | SchemaFormRefusalCode.KeywordNotCarried _ -> "keyword-not-carried"
        | SchemaFormRefusalCode.InvalidKeywordValue _ -> "invalid-keyword-value"
        | SchemaFormRefusalCode.EnumNotStrings -> "enum-not-strings"
        | SchemaFormRefusalCode.InvalidPropertyName _ -> "invalid-property-name"
        | SchemaFormRefusalCode.DuplicateFieldId _ -> "duplicate-field-id"
        | SchemaFormRefusalCode.RequiredUnderOptionalObject _ -> "required-under-optional-object"

    /// A one-sentence account of the refusal.
    let message (code: SchemaFormRefusalCode) : string =
        match code with
        | SchemaFormRefusalCode.SchemaNotJson m -> "the schema is not JSON: " + m
        | SchemaFormRefusalCode.RootNotObject -> "the root schema is not an object schema"
        | SchemaFormRefusalCode.NoFields -> "the root object declares no properties"
        | SchemaFormRefusalCode.Combinator k -> "'" + k + "' has no single form; it is refused rather than guessed"
        | SchemaFormRefusalCode.RefCycle r -> "'$ref' " + r + " reaches itself"
        | SchemaFormRefusalCode.RefUnresolved r ->
            "'$ref' " + r + " is not a local pointer to a schema in this document"
        | SchemaFormRefusalCode.NestingTooDeep ->
            "an object more than one level below the root has no form; only one nested level lowers to a group"
        | SchemaFormRefusalCode.UnsupportedType t -> "type " + t + " has no form control"
        | SchemaFormRefusalCode.UnsupportedFormat f -> "format '" + f + "' has no form control or rule"
        | SchemaFormRefusalCode.UnsupportedKeyword k -> "keyword '" + k + "' is not read by this derivation"
        | SchemaFormRefusalCode.KeywordNotCarried(k, c) -> "keyword '" + k + "' cannot be carried by a " + c + " field"
        | SchemaFormRefusalCode.InvalidKeywordValue k -> "keyword '" + k + "' has a malformed or contradictory value"
        | SchemaFormRefusalCode.EnumNotStrings -> "an enum member is not a string; a choice field submits strings"
        | SchemaFormRefusalCode.InvalidPropertyName n ->
            "property name '"
            + n
            + "' cannot be a field id (empty, or a host-reserved state key)"
        | SchemaFormRefusalCode.DuplicateFieldId i -> "two properties derive to field id '" + i + "'"
        | SchemaFormRefusalCode.RequiredUnderOptionalObject p ->
            "'"
            + p
            + "' is required only when its optional parent object is present, which a flat form cannot say"

    /// The canonical JSON of a refusal list: `{"refusals":[{"code","message","path"}]}`.
    let toJson (refusals: SchemaFormRefusal list) : JVal =
        JObj
            [ "refusals",
              JArr(
                  refusals
                  |> List.map (fun r ->
                      JObj
                          [ "code", JStr(codeName r.Code)
                            "message", JStr(message r.Code)
                            "path", JStr r.Path ])
              ) ]

// ─── Schema reading helpers ────────────────────────────────────────────────

let private member' (name: string) (v: JVal) : JVal option =
    match v with
    | JObj members -> members |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

let private escapeToken (t: string) = t.Replace("~", "~0").Replace("/", "~1")

let private unescapeToken (t: string) = t.Replace("~1", "/").Replace("~0", "~")

let private child (path: string) (token: string) = path + "/" + escapeToken token

let private annotationKeywords =
    set
        [ "$schema"
          "$id"
          "$comment"
          "title"
          "description"
          "examples"
          "$defs"
          "definitions" ]

let private combinatorKeywords =
    set
        [ "oneOf"
          "anyOf"
          "allOf"
          "not"
          "if"
          "then"
          "else"
          "dependentSchemas"
          "dependentRequired"
          "dependencies" ]

let private isWhole (f: float) = f = System.Math.Floor f

/// Resolve a local JSON Pointer (`#`, `#/a/b`) against the root.
let private resolvePointer (root: JVal) (reference: string) : JVal option =
    if reference = "#" then
        Some root
    elif reference.StartsWith "#/" then
        reference.Substring(2).Split('/')
        |> Array.fold
            (fun (acc: JVal option) raw ->
                match acc with
                | None -> None
                | Some node ->
                    let token = unescapeToken raw

                    match node with
                    | JObj _ -> member' token node
                    | JArr items ->
                        match System.Int32.TryParse token with
                        | true, i when i >= 0 && i < List.length items -> Some(List.item i items)
                        | _ -> None
                    | _ -> None)
            (Some root)
    else
        None

// ─── The derivation ────────────────────────────────────────────────────────

type private Ctx<'Msg> =
    { Root: JVal
      Options: SchemaFormOptions<'Msg>
      Refusals: ResizeArray<SchemaFormRefusal> }

let private refuse (ctx: Ctx<'Msg>) (path: string) (code: SchemaFormRefusalCode) =
    ctx.Refusals.Add { Path = path; Code = code }

/// Follow `$ref` (chained) to a concrete schema. `stack` is the refs already
/// being expanded on this descent, which is what a cycle is. A sibling `title`
/// / `description` overrides the target's; any other sibling is refused.
let rec private resolveRefs
    (ctx: Ctx<'Msg>)
    (path: string)
    (stack: string list)
    (schema: JVal)
    : (JVal * string list) option =
    match member' "$ref" schema with
    | None -> Some(schema, stack)
    | Some(JStr reference) ->
        let siblings =
            match schema with
            | JObj ms -> ms |> List.filter (fun (k, _) -> k <> "$ref")
            | _ -> []

        let foreign =
            siblings |> List.filter (fun (k, _) -> not (Set.contains k annotationKeywords))

        for (k, _) in foreign do
            refuse ctx (child path k) (SchemaFormRefusalCode.UnsupportedKeyword k)

        if not foreign.IsEmpty then
            None
        elif List.contains reference stack then
            refuse ctx (child path "$ref") (SchemaFormRefusalCode.RefCycle reference)
            None
        else
            match resolvePointer ctx.Root reference with
            | None ->
                refuse ctx (child path "$ref") (SchemaFormRefusalCode.RefUnresolved reference)
                None
            | Some target ->
                match resolveRefs ctx path (reference :: stack) target with
                | None -> None
                | Some(JObj targetMembers, stack') ->
                    let overriding = siblings |> List.map fst |> Set.ofList

                    let merged =
                        siblings
                        @ (targetMembers |> List.filter (fun (k, _) -> not (Set.contains k overriding)))

                    Some(JObj merged, stack')
                | Some _ ->
                    refuse ctx (child path "$ref") (SchemaFormRefusalCode.RefUnresolved reference)
                    None
    | Some _ ->
        refuse ctx (child path "$ref") (SchemaFormRefusalCode.InvalidKeywordValue "$ref")
        None

/// The effective `type`, with `[T, "null"]` read as `T`.
let private effectiveType (ctx: Ctx<'Msg>) (path: string) (schema: JVal) : string option =
    let describe (v: JVal) = Json.render v

    match member' "type" schema with
    | Some(JStr t) when t <> "null" -> Some t
    | Some(JArr [ JStr t; JStr "null" ])
    | Some(JArr [ JStr "null"; JStr t ]) when t <> "null" -> Some t
    | Some other ->
        refuse ctx (child path "type") (SchemaFormRefusalCode.UnsupportedType(describe other))
        None
    | None ->
        let combinators =
            match schema with
            | JObj ms -> ms |> List.map fst |> List.filter (fun k -> Set.contains k combinatorKeywords)
            | _ -> []

        if not combinators.IsEmpty then
            // An untyped `oneOf` / `anyOf` IS the combinator: name it, rather
            // than reporting a missing type the combinator was standing in for.
            for k in combinators do
                refuse ctx (child path k) (SchemaFormRefusalCode.Combinator k)

            None
        elif (member' "enum" schema).IsSome then
            Some "string"
        elif (member' "properties" schema).IsSome then
            Some "object"
        else
            refuse ctx path (SchemaFormRefusalCode.UnsupportedType "(absent)")
            None

let private allowedFor (typeName: string) : Set<string> =
    match typeName with
    | "string" -> set [ "type"; "enum"; "default"; "minLength"; "maxLength"; "pattern"; "format" ]
    | "number"
    | "integer" ->
        set
            [ "type"
              "default"
              "minimum"
              "maximum"
              "exclusiveMinimum"
              "exclusiveMaximum"
              "multipleOf" ]
    | "boolean" -> set [ "type"; "default" ]
    | "array" -> set [ "type"; "items"; "default"; "uniqueItems" ]
    | "object" -> set [ "type"; "properties"; "required"; "additionalProperties" ]
    | _ -> Set.empty

/// Refuse every keyword the table does not read for this type. Returns false
/// when anything was refused.
let private checkKeywords (ctx: Ctx<'Msg>) (path: string) (typeName: string) (schema: JVal) : bool =
    let allowed = allowedFor typeName

    let members =
        match schema with
        | JObj ms -> ms
        | _ -> []

    let mutable ok = true

    for (k, _) in members do
        if Set.contains k annotationKeywords || Set.contains k allowed then
            ()
        elif Set.contains k combinatorKeywords then
            refuse ctx (child path k) (SchemaFormRefusalCode.Combinator k)
            ok <- false
        else
            refuse ctx (child path k) (SchemaFormRefusalCode.UnsupportedKeyword k)
            ok <- false

    ok

let private nonEmptyString (v: JVal option) : string option =
    match v with
    | Some(JStr s) when s <> "" -> Some s
    | _ -> None

let private readCount (ctx: Ctx<'Msg>) (path: string) (keyword: string) (schema: JVal) : int option =
    match member' keyword schema with
    | None -> None
    | Some(JInt n) when n >= 0 -> Some n
    | Some _ ->
        refuse ctx (child path keyword) (SchemaFormRefusalCode.InvalidKeywordValue keyword)
        None

let private readNumber (ctx: Ctx<'Msg>) (path: string) (keyword: string) (schema: JVal) : float option =
    match member' keyword schema with
    | None -> None
    | Some v ->
        match JVal.asFloat v with
        | Some f -> Some f
        | None ->
            refuse ctx (child path keyword) (SchemaFormRefusalCode.InvalidKeywordValue keyword)
            None

/// The enum members, when every one is a string and there is at least one and
/// no duplicate.
let private readEnum (ctx: Ctx<'Msg>) (path: string) (schema: JVal) : string list option =
    match member' "enum" schema with
    | None -> None
    | Some(JArr items) ->
        let strings =
            items
            |> List.choose (function
                | JStr s -> Some s
                | _ -> None)

        if strings.Length <> items.Length then
            refuse ctx (child path "enum") SchemaFormRefusalCode.EnumNotStrings
            None
        elif strings.IsEmpty || (List.distinct strings).Length <> strings.Length then
            refuse ctx (child path "enum") (SchemaFormRefusalCode.InvalidKeywordValue "enum")
            None
        else
            Some strings
    | Some _ ->
        refuse ctx (child path "enum") (SchemaFormRefusalCode.InvalidKeywordValue "enum")
        None

let private options (values: string list) : SelectOption list =
    values |> List.map (fun v -> { Label = v; Value = v })

let private stateDefault (enc: 'T -> JVal) (fieldId: string) (value: 'T option) : Binding<JVal> option =
    value |> Option.map (fun v -> Binding.State(fieldId, Some(enc v)))

/// Phase 1921 — a value slot that spells the field's exact auto-binding,
/// `State(<field id>, <the control's own placeholder>)`, IS the omitted slot
/// (WIRE_FORMAT §16; the placeholders are `Defaults.ControlValueDefaults`, the
/// one table decode, the canonical encoder's collapse and the resolver share).
/// A schema `default` equal to that placeholder (`""`, `0`, `false`, `[]`,
/// `#000000`) therefore derives to NO value slot, so every tree `derive` returns
/// is already canonical and `deriveWire`'s structural encoding is byte-identical
/// to `CanonicalJson.encodeNode`'s. The arms are the value-bearing kinds this
/// derivation emits; the choice family is absent because its placeholder is "no
/// selection" and a derived default is always a selection. Pinned against the
/// canonical encoder over the cross-host parity table (`SchemaFormParityTests`).
let private withoutAutoValue (field: FormField<'Msg>) : FormField<'Msg> =
    let collapse (placeholder: 'v) (value: Binding<'v> option) : Binding<'v> option =
        match value with
        | Some(Binding.State(key, Some v)) when key = field.Id && v = placeholder -> None
        | v -> v

    let kind =
        match field.Kind with
        | FormFieldKind.Text(value, oc) ->
            FormFieldKind.Text(collapse (FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.text) value, oc)
        | FormFieldKind.TextArea(value, oc, rows) ->
            FormFieldKind.TextArea(
                collapse (FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.text) value,
                oc,
                rows
            )
        | FormFieldKind.Number(value, oc) ->
            FormFieldKind.Number(
                collapse (FieldValue.encodeNumber Fuaran.UI.Defaults.ControlValueDefaults.number) value,
                oc
            )
        | FormFieldKind.RangedNumber(value, oc, mn, mx, st) ->
            FormFieldKind.RangedNumber(
                collapse (FieldValue.encodeNumber Fuaran.UI.Defaults.ControlValueDefaults.number) value,
                oc,
                mn,
                mx,
                st
            )
        | FormFieldKind.Checkbox(value, ot) ->
            FormFieldKind.Checkbox(
                collapse (FieldValue.encodeBool Fuaran.UI.Defaults.ControlValueDefaults.checkbox) value,
                ot
            )
        | FormFieldKind.Toggle(value, ot) ->
            FormFieldKind.Toggle(
                collapse (FieldValue.encodeBool Fuaran.UI.Defaults.ControlValueDefaults.checkbox) value,
                ot
            )
        | FormFieldKind.DateTime(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateTime(
                collapse (FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.dateTime) value,
                oc,
                variant,
                mn,
                mx,
                st
            )
        | FormFieldKind.Color(oc, value) ->
            FormFieldKind.Color(
                oc,
                collapse (FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.color) value
            )
        | FormFieldKind.Tokens(allowFreeText, oc, suggestions, value) ->
            FormFieldKind.Tokens(
                allowFreeText,
                oc,
                suggestions,
                collapse (FieldValue.encodeTokens Fuaran.UI.Defaults.ControlValueDefaults.tokens) value
            )
        | kind -> kind

    { field with Kind = kind }

/// The control a property derives to, or `None` when it was refused (the
/// refusal is already recorded).
let private deriveControl
    (ctx: Ctx<'Msg>)
    (path: string)
    (fieldId: string)
    (typeName: string)
    (schema: JVal)
    : (FormFieldKind<'Msg> * FieldRule option) option =
    let defaultValue = member' "default" schema

    let badDefault () =
        refuse ctx (child path "default") (SchemaFormRefusalCode.InvalidKeywordValue "default")

    let enumControl (values: string list) =
        let dflt =
            match defaultValue with
            | None -> Some None
            | Some(JStr d) when List.contains d values -> Some(Some d)
            | Some _ ->
                badDefault ()
                None

        dflt
        |> Option.map (fun d ->
            let value = stateDefault FieldValue.encodeText fieldId d
            let opts = Binding.Static(Some(options values))

            let kind =
                if values.Length <= ctx.Options.SegmentedMax then
                    FormFieldKind.SegmentedChoice(opts, value, None, Orientation.Horizontal)
                elif values.Length <= ctx.Options.ChoiceMax then
                    FormFieldKind.Choice(opts, value, None)
                else
                    FormFieldKind.Combobox(false, None, opts, value)

            kind, None)

    let refuseTextKeywords (control: string) =
        let present =
            [ "minLength"; "maxLength"; "pattern" ]
            |> List.filter (fun k -> (member' k schema).IsSome)

        for k in present do
            refuse ctx (child path k) (SchemaFormRefusalCode.KeywordNotCarried(k, control))

        present.IsEmpty

    match typeName with
    | "string" ->
        match member' "enum" schema with
        | Some _ ->
            match readEnum ctx path schema with
            | None -> None
            | Some values ->
                let control =
                    if values.Length <= ctx.Options.SegmentedMax then
                        "SegmentedChoice"
                    elif values.Length <= ctx.Options.ChoiceMax then
                        "Choice"
                    else
                        "Combobox"

                let carried = refuseTextKeywords control

                if (member' "format" schema).IsSome then
                    refuse ctx (child path "format") (SchemaFormRefusalCode.KeywordNotCarried("format", control))
                    None
                elif carried then
                    enumControl values
                else
                    None
        | None ->
            let format =
                match member' "format" schema with
                | None -> Ok None
                | Some(JStr f) -> Ok(Some f)
                | Some _ -> Error()

            let stringDefault =
                match defaultValue with
                | None -> Some None
                | Some(JStr d) -> Some(Some d)
                | Some _ ->
                    badDefault ()
                    None

            match format, stringDefault with
            | Error(), _ ->
                refuse ctx (child path "format") (SchemaFormRefusalCode.InvalidKeywordValue "format")
                None
            | _, None -> None
            | Ok fmt, Some dflt ->
                let value = stateDefault FieldValue.encodeText fieldId dflt

                let temporal variant =
                    if refuseTextKeywords "DateTime" then
                        Some(FormFieldKind.DateTime(value, None, variant, None, None, None), None)
                    else
                        None

                match fmt with
                | Some "date" -> temporal DateTimeVariant.Date
                | Some "time" -> temporal DateTimeVariant.Time
                | Some "date-time" -> temporal DateTimeVariant.DateTime
                | Some "color" ->
                    if refuseTextKeywords "Color" then
                        Some(FormFieldKind.Color(None, value), None)
                    else
                        None
                | Some f when f <> "email" && f <> "uri" ->
                    refuse ctx (child path "format") (SchemaFormRefusalCode.UnsupportedFormat f)
                    None
                | _ ->
                    let minLength = readCount ctx path "minLength" schema
                    let maxLength = readCount ctx path "maxLength" schema

                    let pattern =
                        match member' "pattern" schema with
                        | None -> Ok None
                        | Some(JStr p) -> Ok(Some p)
                        | Some _ -> Error()

                    let lengthsOk =
                        match minLength, maxLength with
                        | Some lo, Some hi when lo > hi ->
                            refuse ctx (child path "minLength") (SchemaFormRefusalCode.InvalidKeywordValue "minLength")
                            false
                        | _ -> true

                    match pattern with
                    | Error() ->
                        refuse ctx (child path "pattern") (SchemaFormRefusalCode.InvalidKeywordValue "pattern")
                        None
                    | Ok _ when not lengthsOk -> None
                    | Ok pattern ->
                        let textFormat =
                            match fmt with
                            | Some "email" -> Some TextFormat.Email
                            | Some "uri" -> Some TextFormat.Url
                            | _ -> None

                        let rule: FieldRule =
                            { Compare = None
                              Format = textFormat
                              MaxLength = maxLength
                              Message = None
                              MinLength = minLength
                              Pattern = pattern }

                        let rule =
                            if
                                rule.Format.IsNone
                                && rule.MaxLength.IsNone
                                && rule.MinLength.IsNone
                                && rule.Pattern.IsNone
                            then
                                None
                            else
                                Some rule

                        let long =
                            match maxLength with
                            | Some n -> n > ctx.Options.TextAreaThreshold && textFormat.IsNone
                            | None -> false

                        if long then
                            Some(FormFieldKind.TextArea(value, None, ctx.Options.TextAreaRows), rule)
                        else
                            Some(FormFieldKind.Text(value, None), rule)
    | "number"
    | "integer" ->
        let isInteger = typeName = "integer"

        if (member' "enum" schema).IsSome then
            refuse ctx (child path "enum") SchemaFormRefusalCode.EnumNotStrings
            None
        else
            let refusalsBefore = ctx.Refusals.Count
            let minimum = readNumber ctx path "minimum" schema
            let maximum = readNumber ctx path "maximum" schema
            let exMin = readNumber ctx path "exclusiveMinimum" schema
            let exMax = readNumber ctx path "exclusiveMaximum" schema
            let multipleOf = readNumber ctx path "multipleOf" schema

            if not isInteger then
                if exMin.IsSome then
                    refuse
                        ctx
                        (child path "exclusiveMinimum")
                        (SchemaFormRefusalCode.KeywordNotCarried("exclusiveMinimum", "RangedNumber"))

                if exMax.IsSome then
                    refuse
                        ctx
                        (child path "exclusiveMaximum")
                        (SchemaFormRefusalCode.KeywordNotCarried("exclusiveMaximum", "RangedNumber"))

            match multipleOf with
            | Some m when m <= 0.0 || (isInteger && not (isWhole m)) ->
                refuse ctx (child path "multipleOf") (SchemaFormRefusalCode.InvalidKeywordValue "multipleOf")
            | _ -> ()

            let lower =
                match
                    minimum,
                    (if isInteger then
                         exMin |> Option.map (fun x -> System.Math.Floor x + 1.0)
                     else
                         None)
                with
                | Some a, Some b -> Some(max a b)
                | a, b -> Option.orElse b a

            let upper =
                match
                    maximum,
                    (if isInteger then
                         exMax |> Option.map (fun x -> System.Math.Ceiling x - 1.0)
                     else
                         None)
                with
                | Some a, Some b -> Some(min a b)
                | a, b -> Option.orElse b a

            match lower, upper with
            | Some lo, Some hi when lo > hi ->
                refuse ctx (child path "minimum") (SchemaFormRefusalCode.InvalidKeywordValue "minimum")
            | _ -> ()

            let dflt =
                match defaultValue with
                | None -> Some None
                | Some v ->
                    match JVal.asFloat v with
                    | Some f when
                        (not isInteger || isWhole f)
                        && (lower |> Option.forall (fun lo -> f >= lo))
                        && (upper |> Option.forall (fun hi -> f <= hi))
                        ->
                        Some(Some f)
                    | _ ->
                        badDefault ()
                        None

            match dflt with
            | Some dflt when ctx.Refusals.Count = refusalsBefore ->
                let value = stateDefault FieldValue.encodeNumber fieldId dflt

                let step =
                    if isInteger then
                        Some(defaultArg multipleOf 1.0)
                    else
                        multipleOf

                if lower.IsNone && upper.IsNone && step.IsNone then
                    Some(FormFieldKind.Number(value, None), None)
                else
                    Some(FormFieldKind.RangedNumber(value, None, lower, upper, step), None)
            | _ -> None
    | "boolean" ->
        let dflt =
            match defaultValue with
            | None -> Some None
            | Some(JBool b) -> Some(Some b)
            | Some _ ->
                badDefault ()
                None

        dflt
        |> Option.map (fun d ->
            let value = stateDefault FieldValue.encodeBool fieldId d

            match ctx.Options.BooleanControl with
            | BooleanControl.Checkbox -> FormFieldKind.Checkbox(value, None), None
            | BooleanControl.Toggle -> FormFieldKind.Toggle(value, None), None)
    | "array" ->
        let itemsPath = child path "items"

        match member' "items" schema with
        | None ->
            refuse ctx path (SchemaFormRefusalCode.UnsupportedType "array without items")
            None
        | Some items ->
            match resolveRefs ctx itemsPath [] items with
            | None -> None
            | Some(items, _) ->
                let itemType = effectiveType ctx itemsPath items

                match itemType with
                | None -> None
                | Some "string" when (member' "enum" items).IsSome ->
                    if checkKeywords ctx itemsPath "string" items && refuseTextKeywords "Tokens" then
                        if (member' "format" items).IsSome then
                            refuse
                                ctx
                                (child itemsPath "format")
                                (SchemaFormRefusalCode.KeywordNotCarried("format", "Tokens"))

                            None
                        else
                            match readEnum ctx itemsPath items with
                            | None -> None
                            | Some values ->
                                let dflt =
                                    match defaultValue with
                                    | None -> Some None
                                    | Some(JArr ds) ->
                                        let strings =
                                            ds
                                            |> List.choose (function
                                                | JStr s when List.contains s values -> Some s
                                                | _ -> None)

                                        if
                                            strings.Length = ds.Length
                                            && (List.distinct strings).Length = strings.Length
                                        then
                                            Some(Some strings)
                                        else
                                            badDefault ()
                                            None
                                    | Some _ ->
                                        badDefault ()
                                        None

                                dflt
                                |> Option.map (fun d ->
                                    FormFieldKind.Tokens(
                                        false,
                                        None,
                                        Some(Binding.Static(Some(options values))),
                                        stateDefault FieldValue.encodeTokens fieldId d
                                    ),
                                    None)
                    else
                        None
                | Some t ->
                    refuse ctx itemsPath (SchemaFormRefusalCode.UnsupportedType("array of " + t))
                    None
    | other ->
        refuse ctx (child path "type") (SchemaFormRefusalCode.UnsupportedType other)
        None

/// The `required` list of an object schema, checked against its properties.
let private readRequired (ctx: Ctx<'Msg>) (path: string) (schema: JVal) (names: string list) : Set<string> =
    match member' "required" schema with
    | None -> Set.empty
    | Some(JArr items) ->
        let strings =
            items
            |> List.choose (function
                | JStr s -> Some s
                | _ -> None)

        if
            strings.Length <> items.Length
            || strings |> List.exists (fun s -> not (List.contains s names))
        then
            refuse ctx (child path "required") (SchemaFormRefusalCode.InvalidKeywordValue "required")

        Set.ofList strings
    | Some _ ->
        refuse ctx (child path "required") (SchemaFormRefusalCode.InvalidKeywordValue "required")
        Set.empty

let private checkAdditional (ctx: Ctx<'Msg>) (path: string) (schema: JVal) =
    match member' "additionalProperties" schema with
    | None
    | Some(JBool _) -> ()
    | Some _ ->
        refuse
            ctx
            (child path "additionalProperties")
            (SchemaFormRefusalCode.KeywordNotCarried("additionalProperties", "Form"))

let private properties (ctx: Ctx<'Msg>) (path: string) (schema: JVal) : (string * JVal) list option =
    match member' "properties" schema with
    | None -> Some []
    | Some(JObj ps) -> Some ps
    | Some _ ->
        refuse ctx (child path "properties") (SchemaFormRefusalCode.InvalidKeywordValue "properties")
        None

/// Derive the fields of one object's properties. `depth` 0 is the root; a
/// property that is itself an object at depth 0 lowers to a group (its
/// children at depth 1); an object at depth 1 is refused.
let rec private deriveFields
    (ctx: Ctx<'Msg>)
    (path: string)
    (stack: string list)
    (depth: int)
    (prefix: (string * string) option)
    (parentRequired: bool)
    (schema: JVal)
    : FormField<'Msg> list =
    match properties ctx path schema with
    | None -> []
    | Some props ->
        let names = props |> List.map fst
        let required = readRequired ctx path schema names
        checkAdditional ctx path schema

        props
        |> List.collect (fun (name, propSchema) ->
            let propPath = child (child path "properties") name

            let fieldId =
                match prefix with
                | Some(parentId, _) -> parentId + "." + name
                | None -> name

            if name = "" || StateKeyPolicy.isReserved fieldId then
                refuse ctx propPath (SchemaFormRefusalCode.InvalidPropertyName name)
                []
            else
                match resolveRefs ctx propPath stack propSchema with
                | None -> []
                | Some(propSchema, stack') ->
                    match effectiveType ctx propPath propSchema with
                    | None -> []
                    | Some typeName ->
                        let isRequired = Set.contains name required

                        if isRequired && not parentRequired then
                            refuse ctx propPath (SchemaFormRefusalCode.RequiredUnderOptionalObject name)

                        if not (checkKeywords ctx propPath typeName propSchema) then
                            []
                        else
                            let title = nonEmptyString (member' "title" propSchema) |> Option.defaultValue name

                            let label =
                                match prefix with
                                | Some(_, parentLabel) -> parentLabel + ": " + title
                                | None -> title

                            if typeName = "object" then
                                if depth >= 1 then
                                    refuse ctx propPath SchemaFormRefusalCode.NestingTooDeep
                                    []
                                else
                                    deriveFields
                                        ctx
                                        propPath
                                        stack'
                                        (depth + 1)
                                        (Some(fieldId, label))
                                        isRequired
                                        propSchema
                            else
                                match deriveControl ctx propPath fieldId typeName propSchema with
                                | None -> []
                                | Some(kind, rule) ->
                                    [ { Id = fieldId
                                        Kind = kind
                                        Label = TextSource.Literal label
                                        Required = isRequired && parentRequired
                                        Help =
                                          nonEmptyString (member' "description" propSchema)
                                          |> Option.map TextSource.Literal
                                        Rule = rule } ])

/// Derive a `Form` node from a JSON Schema. `Ok` carries the node; `Error`
/// carries EVERY refusal in the schema (not only the first), each naming its
/// path. Pure and deterministic: field order is the schema's `properties`
/// order, and the same schema and options always yield the same tree.
let derive (options: SchemaFormOptions<'Msg>) (schema: JVal) : Result<Node<'Msg>, SchemaFormRefusal list> =
    let ctx =
        { Root = schema
          Options = options
          Refusals = ResizeArray() }

    let fields =
        match schema with
        | JObj _ ->
            match resolveRefs ctx "" [] schema with
            | None -> []
            | Some(root, stack) ->
                match effectiveType ctx "" root with
                | Some "object" ->
                    if checkKeywords ctx "" "object" root then
                        match member' "properties" root with
                        | Some(JObj(_ :: _)) -> deriveFields ctx "" stack 0 None true root
                        | Some(JObj [])
                        | None ->
                            refuse ctx "" SchemaFormRefusalCode.NoFields
                            []
                        | Some _ -> deriveFields ctx "" stack 0 None true root
                    else
                        []
                | Some _ ->
                    refuse ctx "" SchemaFormRefusalCode.RootNotObject
                    []
                | None -> []
        | _ ->
            refuse ctx "" SchemaFormRefusalCode.RootNotObject
            []

    // Two properties that derive to one id (`a.b` at the root beside `a: {b}`).
    fields
    |> List.countBy _.Id
    |> List.filter (fun (_, n) -> n > 1)
    |> List.iter (fun (id, _) -> refuse ctx "" (SchemaFormRefusalCode.DuplicateFieldId id))

    if ctx.Refusals.Count > 0 then
        Error(List.ofSeq ctx.Refusals)
    else
        Ok(
            Fuaran.UI.Fuaran.form
                options.FormId
                { Fields = fields |> List.map withoutAutoValue
                  OnSubmit = options.OnSubmit
                  SubmitLabel = options.SubmitLabel
                  Disabled = None }
        )

/// `derive`, rendered to canonical wire JSON: `Ok` is the node's canonical
/// encoding, `Error` is the refusal list's (`SchemaFormRefusal.toJson`). The AI
/// tool (`fuaran.formFromSchema`) and the CLI verb (`fuaran scaffold form`) are
/// both this function, which is what makes their bytes the same.
///
/// The encoder is `Generated.encodeNode`, the STRUCTURAL one, because this
/// module sits below the §16 projection (`Introspect.canonicalForm`, in
/// `Fuaran.UI.Ops`). Its bytes equal `CanonicalJson.encodeNode`'s only because
/// `derive` returns canonical trees (Phase 1921, `withoutAutoValue`) — the rule
/// `CanonicalJson.encodeNode`'s own doc comment states for every tool that
/// emits wire JSON.
let deriveWire (options: SchemaFormOptions<'Msg>) (schema: JVal) : Result<string, string> =
    match derive options schema with
    | Ok node -> Ok(Generated.encodeNode node)
    | Error refusals -> Error(Canon.render (SchemaFormRefusal.toJson refusals))

/// `deriveWire` over schema TEXT. The read is null-tolerant (`"default": null`
/// is member absence); text the tier's parser refuses is a `schema-not-json`
/// refusal, in the same envelope.
let deriveWireFromText (options: SchemaFormOptions<'Msg>) (text: string) : Result<string, string> =
    match Json.parseTolerantOfNull text with
    | Ok schema -> deriveWire options schema
    | Error message ->
        Error(
            Canon.render (
                SchemaFormRefusal.toJson
                    [ { Path = ""
                        Code = SchemaFormRefusalCode.SchemaNotJson message } ]
            )
        )
