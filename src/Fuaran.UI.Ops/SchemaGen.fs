module Fuaran.UI.Ops.SchemaGen

// ============================================================================
//  Canonical wire-format JSON Schema generator (Draft 2020-12).
//
//  Phase 96. The third co-equal expression of the Fuaran UI wire-format
//  contract, alongside the encoder (Fuaran.UI.OpStream.Abstractions.CanonicalJson)
//  and the decoder (Fuaran.UI.Ops.JsonDecode). This module emits a machine-
//  readable JSON Schema that DESCRIBES the canonical JSON the encoder produces
//  (and the decoder accepts) — so external validators, editor tooling, and
//  provider-native constrained emission have a drop-in artefact without
//  reading F# source.
//
//  Design posture (mirrors the rest of the wire-format tier):
//    - This is a STRUCTURAL hand-walk of the same DU surface CanonicalJson
//      walks. It is NOT reflection-derived — Fable does not support reflection,
//      and JsonDecode itself is a hand-written mirror of CanonicalJson, so the
//      schema is the third hand-written mirror under the same forward-coupling
//      discipline (WIRE_FORMAT.md §11). Adding a NodeKind/Spec/TreeOp/Binding/
//      Action case updates encoder + decoder + corpus + THIS generator together.
//    - Fable-compatible: no reflection, no System.Text.Json, no captured state.
//      A tiny internal JSON value DU + a deterministic pretty-printer build the
//      schema string, the same way CanonicalJson hand-rolls instance JSON.
//    - The published artefact is `wire-format-fixtures/schema.json` at the
//      workspace root, generated from `wireFormatSchema` below (the corpus
//      emitter writes it). A stale-schema guard test re-derives the string and
//      asserts byte-equality with the committed file.
//
//  Schema shape:
//    - DU positions encode as `oneOf` of branch objects, each pinned by a
//      `$type` const discriminator — an unrecognised `$type` matches no branch
//      and the value is rejected, mirroring the decoder's UNKNOWN_DU_CASE /
//      WRONG_NODE_KIND surface.
//    - Bare-string enums (Orientation, ToneVariant, …) encode as
//      `{ "type":"string", "enum":[…] }`.
//    - Closure-bearing slots (§4) encode as the const `"<closure>"`.
//    - `Binding<'T>` is emitted ONCE PER INSTANTIATED ELEMENT TYPE (Phase 1068)
//      — `Binding_float`, `Binding_str`, `Binding_list_SelectOption`, … — so a
//      slot's `Static` payload is constrained by the type the slot declares.
//      The two element types that stay `true` (any JSON) are the §5 abstentions
//      the encoder genuinely cannot decompose: a structured JSON payload and a
//      HOSTED row feed.
//    - Optional (None-omitted) fields are absent from `required`; the schema
//      does not set `additionalProperties:false`, matching the decoder's
//      tolerance of unknown keys (rule 2 / field-lookup-by-name).
// ============================================================================

open System.Text

/// Stable identifier for the published schema. The `/v1/` segment pins the
/// wire-format major version (WIRE_FORMAT.md "Version: wire format v1").
[<Literal>]
let schemaId = "https://fuaran.dev/wire-format/v1/schema.json"

// ─── Internal JSON value DU + deterministic pretty-printer ──────────────────
//
// Key order is preserved as authored (deterministic by construction), so the
// generated string is byte-stable across runs / machines / .NET vs Fable — the
// property the stale-schema guard depends on.

type private J =
    | JStr of string
    | JInt of int
    | JBool of bool
    | JArr of J list
    | JObj of (string * J) list

let private appendEscaped (sb: StringBuilder) (s: string) : unit =
    sb.Append '"' |> ignore

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore

let rec private writeJ (sb: StringBuilder) (indent: int) (j: J) : unit =
    match j with
    | JStr s -> appendEscaped sb s
    | JInt n -> sb.Append(string n) |> ignore
    | JBool b -> sb.Append(if b then "true" else "false") |> ignore
    | JArr [] -> sb.Append "[]" |> ignore
    | JArr items ->
        let childPad = String.replicate (indent + 2) " "
        sb.Append "[\n" |> ignore
        let mutable first = true

        for item in items do
            if not first then
                sb.Append ",\n" |> ignore

            first <- false
            sb.Append childPad |> ignore
            writeJ sb (indent + 2) item

        sb.Append '\n' |> ignore
        sb.Append(String.replicate indent " ") |> ignore
        sb.Append ']' |> ignore
    | JObj [] -> sb.Append "{}" |> ignore
    | JObj fields ->
        let childPad = String.replicate (indent + 2) " "
        sb.Append "{\n" |> ignore
        let mutable first = true

        for (key, value) in fields do
            if not first then
                sb.Append ",\n" |> ignore

            first <- false
            sb.Append childPad |> ignore
            appendEscaped sb key
            sb.Append ": " |> ignore
            writeJ sb (indent + 2) value

        sb.Append '\n' |> ignore
        sb.Append(String.replicate indent " ") |> ignore
        sb.Append '}' |> ignore

// ─── Schema-fragment builders ───────────────────────────────────────────────

/// `{ "$ref": "#/$defs/<name>" }`
let private ref (name: string) : J =
    JObj [ "$ref", JStr("#/$defs/" + name) ]

let private typed (t: string) : J = JObj [ "type", JStr t ]

let private str = typed "string"
let private integer = typed "integer"

/// A float slot (WIRE_FORMAT.md §5 / §7) — a JSON number, OR one of the three
/// quoted non-finite sentinels. JSON has no literal for NaN or an infinity, so
/// §5 spells them as strings and §7 requires a decoder to accept them back
/// **at a float slot**; a schema that said `type: number` here would refuse the
/// canonical output of every conformant host, which is the accept-fixtures-
/// validate-against-schema leg's whole point.
///
/// `integer` above is deliberately NOT widened, for the same reason the
/// decoder's integer path is not: §7 stops at the float slot, and the corpus
/// carries two integer controls (`Heading.level`, `Map.zoom`) that pin the
/// refusal. Widening both would have made the fixture that proves the boundary
/// pass for the wrong reason.
let private number =
    JObj
        [ "anyOf",
          JArr
              [ typed "number"
                JObj [ "enum", JArr [ JStr "NaN"; JStr "Infinity"; JStr "-Infinity" ] ] ] ]

let private boolean = typed "boolean"
let private object_ = typed "object"

/// Any JSON value — `true` is the Draft 2020-12 "match anything" schema. Used
/// for opaque seams (`Binding.Static.value`, `PropValue.Native` payloads) the encoder
/// cannot decompose (WIRE_FORMAT.md §5 / §11 rule 11).
let private anyJson: J = JBool true

/// A structured JVal position (rule 12): any JSON value EXCEPT null — the wire
/// model has no null, and the decoder rejects it at these positions
/// (`WRONG_TYPE` naming the rule). Distinct from `anyJson`, which is reserved
/// for the §5 obj-erased opaque seams where a boxed null legitimately occurs.
/// The published schema expressing not-null here keeps it an honest mirror of
/// the decoder — the reject-null corpus fixtures fail schema validation too.
let private jsonValue: J = JObj [ "not", JObj [ "type", JStr "null" ] ]

/// A JSON object whose (arbitrary-keyed) values are structured JVal positions
/// — `Custom.props` / `I18n.args` (a null prop/arg value violates rule 12).
let private jsonValueMap: J =
    JObj [ "type", JStr "object"; "additionalProperties", jsonValue ]

/// The closure sentinel slot (§4) — always exactly the string `"<closure>"`.
let private closure: J = JObj [ "const", JStr "<closure>" ]

let private arrayOf (item: J) : J =
    JObj [ "type", JStr "array"; "items", item ]

/// A bare-string enum DU (WIRE_FORMAT.md §3.5).
let private enumDef (cases: string list) : J =
    JObj [ "type", JStr "string"; "enum", JArr(cases |> List.map JStr) ]

/// An object record. `required` lists the always-emitted keys; optional
/// (None-omitted) keys appear in `props` but not `required`.
let private record (required: string list) (props: (string * J) list) : J =
    let baseFields = [ "type", JStr "object"; "properties", JObj props ]

    let fields =
        match required with
        | [] -> baseFields
        | _ -> baseFields @ [ "required", JArr(required |> List.map JStr) ]

    JObj fields

/// Phase 863 — forbid an ENUMERATED set of near-miss property names on a
/// record, as `allOf: [{ "not": { "required": ["<name>"] } }, …]`.
///
/// This is the schema half of the decoder's near-miss didactics. The schema
/// deliberately does NOT set `additionalProperties: false` (see the header —
/// it mirrors the decoder's rule-2 tolerance of unknown keys), so a bare
/// property-name check is the only way to say "not this one" without
/// abandoning that tolerance wholesale. The corpus contract requires every
/// reject fixture to fail the schema as well as the decoder, and "must not
/// carry property X" is trivially expressible in Draft 2020-12 — so a
/// near-miss belongs here rather than in the schema-inexpressible exemption
/// list, which is reserved for rules the dialect genuinely cannot state (a
/// relation between two sibling values).
let private forbidding (names: string list) (r: J) : J =
    match r, names with
    | _, [] -> r
    | JObj fields, _ ->
        JObj(
            fields
            @ [ "allOf",
                JArr(
                    names
                    |> List.map (fun n -> JObj [ "not", JObj [ "required", JArr [ JStr n ] ] ])
                ) ]
        )
    | _ -> r

/// One `$type`-discriminated DU branch (WIRE_FORMAT.md §3). `$type` is pinned
/// by `const`, so an unrecognised discriminator matches no branch.
let private duCase (disc: string) (required: string list) (props: (string * J) list) : J =
    record ("$type" :: required) (("$type", JObj [ "const", JStr disc ]) :: props)

/// A DU position — `oneOf` of its branches.
let private union (branches: J list) : J = JObj [ "oneOf", JArr branches ]

/// A `Binding<'T>` slot (Phase 1068) — a `$ref` to the definition instantiated
/// at `elem`, not to one shared type-erased `Binding`. `elem` is the element
/// type's mangled name (`str` / `float` / `list_SelectOption` / …), spelled the
/// same way the IDL's own schema leg mangles a type argument so the two
/// artefacts' `$defs` carry the same names for the same instantiations.
let private binding (elem: string) : J = ref ("Binding_" + elem)

/// One `Binding<'T>` definition, instantiated at a single element type
/// (Phase 1068). Every `'T`-typed wire slot — `Static.value` and the three
/// `defaultValue`s — carries `elem` rather than the any-JSON envelope the single
/// shared definition had to use. That envelope was not a limit of the dialect:
/// the element type is present in the IDL at every slot, and discarding it left
/// the published schema unable to refuse a boolean at `Metric.trend` or a §7
/// sentinel at the integer `Stepper.activeStep` (Phase 1064 measured all four).
///
/// `self` is this instantiation's own `$defs` name: `Local.initialFrom` is a
/// `Binding<'T>` at the SAME element type, so it points back here. The two
/// slots that are NOT at `'T` keep their own fixed instantiations —
/// `Format.source` is always `Binding<float>` and `I18n.args` always
/// `Binding<JSON>` — which is the polymorphic recursion the IDL declares.
///
/// Two element types stay any-JSON on purpose, and that is abstention rather
/// than erasure: a JSON payload position (§ rule 12) and a HOSTED slot (grid /
/// chart row feeds) carry content the wire deliberately does not decompose —
/// "don't constrain content the encoder doesn't decompose" (§5 / §13). What
/// changed is that they are now *named* abstentions at their own slots instead
/// of every slot inheriting one.
let private bindingDef (self: string) (elem: J) : J =
    union
        // Phase 677 — `value` is OPTIONAL: absence is structural, so a binding
        // carrying no value omits the key rather than emitting JSON null (for
        // which the wire model has no case).
        [ duCase "Static" [] [ "value", elem ]
          // `dependsOn` (Phase 421) is optional (omitted-when-empty) — the declared filter edge.
          duCase "Query" [ "name" ] [ "dependsOn", arrayOf str; "name", str ]
          duCase "Filter" [ "name" ] [ "defaultValue", elem; "name", str ]
          // `defaultValue` (0.2.9, Phase 629) is optional — yielded until the
          // user first selects a row; the Filter.defaultValue convention.
          // `field` (0.2.10, Phase 632) is optional — the declarative
          // row-field projection off the clicked row.
          duCase "Selection" [ "nodeId" ] [ "defaultValue", elem; "field", str; "nodeId", str ]
          // Phase 677 — `defaultValue` is OPTIONAL for the same reason as `Static.value`.
          duCase "State" [ "key" ] [ "defaultValue", elem; "key", str ]
          duCase "Computed" [ "fn" ] [ "fn", closure ]
          // Phase 765 — `Now` carries NO wire fields: the instant is furnished
          // by the host at resolve time, never serialised. `{"$type":"Now"}`
          // is the whole form.
          duCase "Now" [] []
          duCase
              "I18n"
              [ "key" ]
              [ "args", JObj [ "type", JStr "object"; "additionalProperties", binding "json" ]
                "key", str ]
          duCase
              "Local"
              [ "flushOn"; "format"; "initialFrom"; "onCommit"; "parse" ]
              [ "flushOn", ref "LocalFlushTrigger"
                "format", closure
                "initialFrom", ref self
                "onCommit", closure
                "parse", closure ]
          // Locale-aware formatted binding (Phase 102). `source` is always a
          // Binding<float>; `format` / `locale` are the bounded DUs.
          duCase
              "Format"
              [ "format"; "locale"; "source" ]
              [ "format", ref "Format"
                "locale", ref "LocaleSource"
                "source", binding "float" ]
          // Declarative dataframe transform (Phase 282 — the Compute layer). `source` (a
          // Fuaran.Core.DataSource object) + `pipeline` (a Fuaran.Core Transform-step array) are
          // Fuaran.Core values whose detailed shape is owned + certified by Fuaran.Core's own
          // codec; the host schema describes them structurally (array / object) without re-deriving
          // Core's algebra schema — the same "don't constrain content the encoder doesn't
          // decompose" posture as an obj-erased JSON payload (§5 / §13).
          // `params` (Phase 424) is optional (omitted-when-empty) — each entry binds a `ColExpr.Param`
          // name to a scalar `Binding` source; absent leaves the Phase 282 shape byte-identical.
          // `source` (Phase 818) is EITHER a Fuaran.Core DataSource object OR a live
          // binding-shaped source (`{"$type":"State"|"Selection"|"Query",…}` — preserved for
          // subscription-semantics re-evaluation); both are objects, so the structural
          // `object_` posture above covers the widened slot without re-deriving either shape.
          duCase
              "Transform"
              [ "pipeline"; "source" ]
              [ "params", arrayOf (record [ "from"; "name" ] [ "from", binding "json"; "name", str ])
                "pipeline", arrayOf anyJson
                "source", object_ ]
          // Invoke a host-registered compute capability (Phase 283). `capabilityId` references a
          // capability in the host registry; `args` are scalar `(addr, value)` pairs validated
          // host-side against the capability signature. The body is never on the wire.
          duCase "Invoke" [ "args"; "capabilityId" ] [ "args", arrayOf object_; "capabilityId", str ] ]

/// A `$type`-discriminated branch whose spec fields are hoisted to the top level
/// — the flat wire carries no `spec` wrapper (WIRE_FORMAT.md §3.2). The value
/// must validate as the spec record AND carry the `$type` const, so we `allOf`
/// the discriminator with the spec `$ref` (the spec $def constrains its own
/// fields; neither side sets `additionalProperties:false`, so they compose).
let private duCaseHoisted (disc: string) (specDef: string) : J =
    JObj [ "allOf", JArr [ duCase disc [] []; ref specDef ] ]

// ─── $defs ──────────────────────────────────────────────────────────────────
//
// Authored in dependency-friendly reading order; `$ref` resolution is by name
// so order does not affect correctness. Field names + required/optional splits
// mirror CanonicalJson.fs exactly.

let private defs: (string * J) list =
    [
      // ── Bare-string enums (§3.5) ──────────────────────────────────────────
      "Orientation", enumDef [ "Vertical"; "Horizontal" ]
      "BadgeVariant", enumDef [ "Neutral"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ]
      "ButtonVariant", enumDef [ "Primary"; "Secondary"; "Tertiary"; "Destructive" ]
      "HeadingVariant", enumDef [ "Standard"; "Eyebrow"; "Caption"; "Lead" ]
      // Phase 812 — anti-scraper render strategy for a mailto Link.
      "LinkProtection", enumDef [ "email" ]
      "ImageVariant", enumDef [ "Default"; "Avatar"; "Rounded" ]
      // Phase 1077 — the three `Image` presentation vocabularies.
      "ImageFit", enumDef [ "Natural"; "Cover"; "Contain" ]
      // Phase 1110 — closed at four; `metadata` is deliberately not a case.
      "TrackKind", enumDef [ "Subtitles"; "Captions"; "Descriptions"; "Chapters" ]
      "ImageAspect", enumDef [ "Natural"; "Square"; "FourThree"; "ThreeTwo"; "SixteenNine" ]
      // Phase 1111 — closed at four. `Embed.aspectRatio` REUSES `ImageAspect`
      // above rather than minting a second def with identical cases.
      "EmbedPermission", enumDef [ "AllowScripts"; "AllowSameOrigin"; "AllowForms"; "AllowFullscreen" ]
      "ImageLoading", enumDef [ "Eager"; "Lazy" ]
      "ScrollOrientation", enumDef [ "Vertical"; "Horizontal"; "Both" ]
      // Phase 1119 — which overlay a `Modal` node is. Omitted at `Modal`, so the
      // member is NOT in `ModalSpec`'s required list.
      "ModalityKind", enumDef [ "Modal"; "Popover" ]
      // Phase 1116 — closed at the two devices a file picker can stand in front
      // of. A screen is deliberately not a case: the HTML `capture` attribute
      // cannot express one, and the charter rules display capture Host chrome.
      "CaptureSource", enumDef [ "Camera"; "Microphone" ]
      "DateVariant", enumDef [ "Date"; "Time"; "DateTime" ]
      // Phase 864 — both lower-case on the wire, the `LinkProtection` posture.
      "TextFormat", enumDef [ "email"; "url"; "tel" ]
      "CompareOp", enumDef [ "eq"; "neq"; "lt"; "lte"; "gt"; "gte" ]
      "MathDisplay", enumDef [ "Inline"; "Block" ]
      "ToneVariant", enumDef [ "Default"; "Subdued"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ]
      "StyleWeight", enumDef [ "Compact"; "Standard"; "Spacious" ]
      "Emphasis", enumDef [ "Quiet"; "Normal"; "Loud" ]
      // Phase 867 - `Neutral` is RESERVED, not admitted: it is deliberately
      // absent here so a schema-guided emitter cannot produce it.
      "TrendPolarity", enumDef [ "HigherIsBetter"; "LowerIsBetter" ]
      "StyleRole", enumDef [ "None"; "Eyebrow"; "Data"; "Lede"; "Caption" ]
      "FontVoice", enumDef [ "Default"; "Display"; "Structural" ]
      // Phase 1472 - lower-case on the wire, the `LiveRegionKind` posture.
      // `auto` is the identity and is omitted at it, but it stays spellable so a
      // document may state the default explicitly.
      "TextDirection", enumDef [ "auto"; "ltr"; "rtl" ]
      "ChartKind", enumDef [ "Line"; "Bar"; "Area"; "Pie"; "Scatter"; "Heatmap" ]
      "ChartLegendPosition", enumDef [ "Top"; "Right"; "Bottom"; "None" ]
      "ChartDataLabels", enumDef [ "Off"; "Ends" ]
      "ChartXScale", enumDef [ "Category"; "Temporal" ]
      "LiveRegionKind", enumDef [ "polite"; "assertive"; "off" ]
      "HashStrictness", enumDef [ "StrictReplay"; "AdvisoryWarning"; "Enforced" ]
      // Locale-aware formatting enums (Phase 102).
      "DateStyle", enumDef [ "Short"; "Medium"; "Long"; "Full" ]
      "RelativeTimeUnit", enumDef [ "Second"; "Minute"; "Hour"; "Day"; "Week"; "Month"; "Year" ]
      // Duration formatting enums (Phase 819).
      "DurationUnit", enumDef [ "Seconds"; "Minutes"; "Hours" ]
      "DurationStyle", enumDef [ "Compact"; "Clock"; "Long" ]
      // Icon display-kind size class (Phase 821).
      "IconSize", enumDef [ "Small"; "Medium"; "Large" ]
      // Action.ReadFileBody encoding (Phase 136).
      "FileReadEncoding", enumDef [ "Text"; "Base64"; "DataUrl" ]
      // AriaRole encodes as the raw ARIA string (named roles + Custom raw),
      // so any string is valid (§3.5).
      "AriaRole", str

      // ── TextSource (§3.3) ─────────────────────────────────────────────────
      // 0.2.0 — the CANONICAL Literal form is the bare JSON string; the
      // {"$type":"Literal"} envelope stays decode-accepted.
      "TextSource",
      JObj
          [ "oneOf",
            JArr
                [ str
                  union
                      [ duCase "Literal" [ "text" ] [ "text", str ]
                        duCase "Bound" [ "binding" ] [ "binding", binding "str" ]
                        duCase "I18n" [ "args"; "key" ] [ "args", jsonValueMap; "key", str ] ] ] ]

      // ── Binding<'T> (§3.3) — ONE DEFINITION PER INSTANTIATED ELEMENT TYPE ─
      //
      // Phase 1068. Every `Binding` slot used to be one `$ref` to a single
      // type-erased `#/$defs/Binding` whose `Static` arm was `"value": true`,
      // so a boolean at `Metric.trend`, a non-sentinel string at
      // `Metric.value` and a §7 sentinel at the integer `Stepper.activeStep`
      // were all structurally well-formed by every rule the schema stated
      // while the decoder refused all four (Phase 1064's measurement, pinned
      // inversely so the finding could not go quiet). The element type was
      // never lost on the way — the IDL carries it at every slot — so this
      // was expressible in Draft 2020-12 and simply not expressed.
      //
      // Phase 429's typed `Static` payloads (options / values / series /
      // markers) and fuaran#665's grid/chart ROW feeds are what these
      // instantiations name: the element record shapes (`SelectOption`,
      // `MapMarker`) were already defined below and are now REACHED from
      // their slots rather than sitting beside them. The rows feed stays
      // any-JSON — see `bindingDef` for why that is abstention, not erasure.
      //
      // Names are sorted so this block reads as an enumeration; the `$defs`
      // object is order-free (resolution is by name).
      "Binding_bool", bindingDef "Binding_bool" boolean
      "Binding_float", bindingDef "Binding_float" number
      "Binding_hosted", bindingDef "Binding_hosted" anyJson
      "Binding_int", bindingDef "Binding_int" integer
      "Binding_json", bindingDef "Binding_json" anyJson
      "Binding_list_MapMarker", bindingDef "Binding_list_MapMarker" (arrayOf (ref "MapMarker"))
      "Binding_list_SelectOption", bindingDef "Binding_list_SelectOption" (arrayOf (ref "SelectOption"))
      "Binding_list_float", bindingDef "Binding_list_float" (arrayOf number)
      "Binding_list_str", bindingDef "Binding_list_str" (arrayOf str)
      "Binding_str", bindingDef "Binding_str" str

      "LocalFlushTrigger",
      union
          [ duCase "OnBlur" [] []
            duCase "OnSubmit" [] []
            duCase "OnCommitAction" [] []
            duCase "OnDebounce" [ "milliseconds" ] [ "milliseconds", integer ] ]

      // ── Action<'Msg> (§3.3) ───────────────────────────────────────────────
      // `Call.onResult` is optional since Phase 428 (present as the closure
      // sentinel when F#-authored); `into` is the declarative result target
      // (omitted when absent) — see `CallResultTarget`.
      "CallResultTarget",
      union
          [ duCase "State" [ "key" ] [ "key", str ]
            duCase "Query" [ "name" ] [ "name", str ] ]

      "Action",
      union
          [ duCase "Dispatch" [] []
            duCase "Call" [ "endpoint" ] [ "endpoint", str; "onResult", closure; "into", ref "CallResultTarget" ]
            duCase "Notify" [ "channel"; "payload" ] [ "channel", str; "payload", jsonValue ]
            duCase "Navigate" [ "route" ] [ "route", str ]
            // Phase 818 — `value` XOR `valueFrom` (a Binding evaluated at
            // dispatch time): both leave `required`, and the `oneOf` over the
            // two required-lists is the exclusive-or itself — neither-present
            // fails (the decoder's MISSING_FIELD) AND both-present fails (the
            // decoder's didactic), so the schema mirrors the decoder on both
            // edges. (The Switch stateKey/on precedent uses `anyOf` because
            // its decoder tolerates both; SetState's does not.)
            JObj
                [ "allOf",
                  JArr
                      [ duCase "SetState" [ "key" ] [ "key", str; "value", jsonValue; "valueFrom", binding "json" ]
                        JObj
                            [ "oneOf",
                              JArr
                                  [ JObj [ "required", JArr [ JStr "value" ] ]
                                    JObj [ "required", JArr [ JStr "valueFrom" ] ] ] ] ] ]
            duCase "AiTool" [ "args"; "toolName" ] [ "args", jsonValue; "toolName", str ]
            duCase "Chain" [ "ops" ] [ "ops", arrayOf (ref "Action") ]
            duCase "CommitLocal" [ "nodeId" ] [ "nodeId", str ]
            // Phase 1126 — the payload is a `TextSource`, not a bare string:
            // a reader may copy a bound value. `TextSource`'s own schema carries
            // the bare-string Literal shorthand, so the pre-1126 spelling is
            // still valid against this schema.
            duCase "WriteToClipboard" [ "text" ] [ "text", ref "TextSource" ]
            duCase
                "ReadFileBody"
                [ "encoding"; "fileRef"; "onRead" ]
                [ "encoding", ref "FileReadEncoding"; "fileRef", str; "onRead", closure ]
            // Invoke a host-registered compute capability as an effect (Phase 283) — same wire shape
            // as `Binding.Invoke`.
            duCase "Invoke" [ "args"; "capabilityId" ] [ "args", arrayOf object_; "capabilityId", str ]
            // Phase 1124 — payload-free, and the ONLY branch in this union that
            // closes the object. `additionalProperties: false` is not a
            // tightening for its own sake: it is how the schema mirrors the
            // decoder, which refuses a member here rather than dropping it,
            // because there is no printing parameter a document may state. The
            // other branches stay open for the ordinary reason — an
            // unrecognised member there is one a reader has not learned yet.
            JObj
                [ "type", JStr "object"
                  "properties", JObj [ "$type", JObj [ "const", JStr "Print" ] ]
                  "required", JArr [ JStr "$type" ]
                  "additionalProperties", JBool false ] ]

      // ── CellFormat / CellValue / ColumnWidth (§3.3) ───────────────────────
      "CellFormat",
      union
          [ duCase "None" [] []
            duCase "Number" [] [ "decimals", integer ]
            duCase "Currency" [ "code" ] [ "code", str ]
            duCase "Percent" [] [ "decimals", integer ]
            duCase "SignificantDigits" [ "digits" ] [ "digits", integer ]
            duCase "Date" [ "format" ] [ "format", str ]
            // Phase 819 — duration cells + cell-level relative time.
            duCase "Duration" [ "style"; "unit" ] [ "style", ref "DurationStyle"; "unit", ref "DurationUnit" ]
            duCase "RelativeTime" [ "unit" ] [ "unit", ref "RelativeTimeUnit" ]
            duCase "Custom" [ "fn" ] [ "fn", closure ] ]

      "CellValue",
      union
          [ duCase "Numeric" [ "value" ] [ "value", number ]
            duCase "Text" [ "value" ] [ "value", str ]
            duCase "Bool" [ "value" ] [ "value", boolean ]
            duCase "Date" [ "unixSeconds" ] [ "unixSeconds", integer ]
            duCase "Empty" [] [] ]

      "ColumnWidth",
      union
          [ duCase "Auto" [] []
            duCase "Fixed" [ "pixels" ] [ "pixels", integer ]
            duCase "Flex" [ "weight" ] [ "weight", number ] ]

      // ── Format / LocaleSource (Phase 102) ─────────────────────────────────
      "Format",
      union
          [ duCase "Number" [] [ "decimals", integer ]
            duCase "Currency" [ "isoCode" ] [ "isoCode", str ]
            duCase "Percent" [] [ "decimals", integer ]
            duCase "Date" [ "dateStyle" ] [ "dateStyle", ref "DateStyle" ]
            duCase "RelativeTime" [ "unit" ] [ "unit", ref "RelativeTimeUnit" ]
            // Phase 819 — locale-independent duration formatting.
            duCase "Duration" [ "style"; "unit" ] [ "style", ref "DurationStyle"; "unit", ref "DurationUnit" ] ]

      "LocaleSource", union [ duCase "Ambient" [] []; duCase "Explicit" [ "tag" ] [ "tag", str ] ]

      // ── FormFieldKind / FilterKind (§3.3) ─────────────────────────────────
      // `onChange` / `onToggle` are optional (Phase 426, the control write-back
      // default) — present as the `"<closure>"` const when an F#-authored
      // closure is set, omitted for a declarative (AI-authored) field — so each
      // stays in `props` but leaves every case's `required` list (the Phase 423
      // `FilterKind` treatment, generalised).
      "FormFieldKind",
      union
          [ duCase "Text" [] [ "onChange", closure; "value", binding "str" ]
            duCase "Number" [] [ "onChange", closure; "value", binding "float" ]
            duCase
                "Range"
                []
                [ "onChange", closure
                  "value", anyJson
                  "min", number
                  "max", number
                  "step", number ]
            duCase "Checkbox" [] [ "onToggle", closure; "value", binding "bool" ]
            duCase "Toggle" [] [ "onToggle", closure; "value", binding "bool" ]
            duCase
                "Choice"
                [ "options" ]
                [ "onChange", closure
                  "options", binding "list_SelectOption"
                  "value", binding "str" ]
            duCase
                "RangedNumber"
                []
                [ "onChange", closure
                  "value", binding "float"
                  "min", number
                  "max", number
                  "step", number ]
            duCase
                "SegmentedChoice"
                [ "options"; "orientation" ]
                [ "onChange", closure
                  "options", binding "list_SelectOption"
                  "orientation", ref "Orientation"
                  "value", binding "str" ]
            duCase "TextArea" [ "rows" ] [ "onChange", closure; "rows", integer; "value", binding "str" ]
            duCase
                "Date"
                [ "variant" ]
                [ "onChange", closure
                  "value", binding "str"
                  "variant", ref "DateVariant"
                  "min", str
                  "max", str
                  "step", number ]
            // Phase 725 — the single-control date range. `value` carries the
            // ordered ISO-8601 (from, to) pair, canonically the bare
            // {from, to} object (like `Range`'s bare {min, max}), so the slot
            // is `anyJson` rather than a `Binding` ref.
            duCase
                "DateRange"
                [ "variant" ]
                [ "onChange", closure
                  "value", anyJson
                  "variant", ref "DateVariant"
                  "min", str
                  "max", str
                  "step", number ]
            // Phase 1113 — the typeahead / autocomplete control. `options` is
            // required (a combobox with no source is not a control); everything
            // else is optional, `allowFreeText` omitting at `false`.
            duCase
                "Combobox"
                [ "options" ]
                [ "allowFreeText", boolean
                  "onChange", closure
                  "options", binding "list_SelectOption"
                  "value", binding "str" ]
            // Phase 1130 — the star scale. `max` is required (a rating with no
            // declared ceiling is not a scale); `allowHalf` omits at `false`, so
            // it stays out of `required` on the Phase 460 discipline.
            duCase
                "Rating"
                [ "max" ]
                [ "allowHalf", boolean
                  "max", integer
                  "onChange", closure
                  "value", binding "float" ]
            // Phase 1130 — the colour control. Everything optional: an
            // unspecified value is the auto-bind, exactly as on every other
            // control. The `#rrggbb` shape is a decode-time refusal and a
            // validator rule rather than a schema pattern, because the value
            // slot is a `Binding` and only its `Static` case carries text at
            // all — a `pattern` here would either miss the bound case or refuse
            // it.
            duCase "Color" [] [ "onChange", closure; "value", binding "str" ]
            // Phase 1121 — the multi-token input. Everything optional: the
            // suggestion source is genuinely absent on a plain token box, and
            // `allowFreeText` omits at TRUE, so it stays out of `required` on the
            // Phase 460 discipline exactly as `allowFreeText` does on `Combobox`
            // — with the OPPOSITE default, which the schema cannot express and
            // the spec's omit-at-default table states instead.
            //
            // The cross-member refusal (`allowFreeText` false with no
            // `suggestions`) is a decode-time rule and not a schema constraint,
            // deliberately. A `dependentRequired` here would refuse the document
            // on ABSENCE of a member rather than on its value, and the shape
            // that has to be caught is the pair, which no per-member keyword
            // names.
            duCase
                "Tokens"
                []
                [ "allowFreeText", boolean
                  "onChange", closure
                  "suggestions", binding "list_SelectOption"
                  "value", binding "list_str" ] ]

      // `onChange` is optional (Phase 423) — present as the `"<closure>"` const when an F#-authored
      // closure is set, omitted for a declarative (AI-authored) chip — so it stays in `props` but
      // leaves every case's `required` list. `RangeFilter.value` rides as a typed `{min,max}` object
      // (the AI-authorable bounds), with the legacy `"<opaque>"` sentinel accepted for read-compat.
      // ── CellKindErased (§3.3) ─────────────────────────────────────────────
      "CellKindErased",
      union
          [ duCase "Text" [] []
            duCase "Numeric" [] []
            duCase "Date" [] []
            duCase "Editable" [ "onEdit" ] [ "onEdit", closure ]
            duCase "Checkbox" [ "get"; "onToggle" ] [ "get", closure; "onToggle", closure ]
            duCase "Button" [ "label"; "onClick" ] [ "label", ref "TextSource"; "onClick", closure ]
            duCase
                "ButtonGroup"
                [ "buttons" ]
                [ "buttons", arrayOf (record [ "label"; "onClick" ] [ "label", ref "TextSource"; "onClick", closure ]) ]
            duCase "Link" [ "hrefFn"; "labelFn" ] [ "hrefFn", closure; "labelFn", closure ]
            duCase "Pill" [ "labelFn"; "toneFn" ] [ "labelFn", closure; "toneFn", closure ]
            // Phase 750 — the declarative pill. `default` is omitted-when-`Default`, so
            // it stays out of `required` (the Phase 460 discipline). `map` is a
            // string-keyed object of `ToneVariant`s — the `additionalProperties` shape
            // the i18n-args / fragment-args slots already use, which is what makes the
            // legal tone names externally checkable rather than decoder-only.
            duCase
                "TonedPill"
                [ "field"; "map" ]
                [ "default", ref "ToneVariant"
                  "field", str
                  "map", JObj [ "type", JStr "object"; "additionalProperties", ref "ToneVariant" ] ]
            duCase "Progress" [ "fractionFn"; "labelFn" ] [ "fractionFn", closure; "labelFn", closure ]
            duCase "Custom" [ "fn" ] [ "fn", closure ] ]

      // ── Display specs ─────────────────────────────────────────────────────
      "MetricSpec",
      record
          // Phase 460 — stylistic `format`/`tone`/`weight`/`emphasis` are omitted-when-default;
          // out of `required`, they stay in `props`. Only the semantic fields remain required.
          [ "label"; "value" ]
          [ "emphasis", ref "Emphasis"
            "format", ref "CellFormat"
            "label", ref "TextSource"
            "value", binding "float"
            "tone", ref "ToneVariant"
            "weight", ref "StyleWeight"
            "trend", binding "float"
            "trendFormat", ref "CellFormat"
            // Phase 867 - omitted-when-default like the stylistic fields above,
            // so it stays out of `required` and lives in `props`.
            "trendPolarity", ref "TrendPolarity"
            "icon", str
            "subtext", ref "TextSource" ]

      "HeadingSpec",
      record
          [ "level"; "text"; "variant" ]
          [ "level", integer; "text", ref "TextSource"; "variant", ref "HeadingVariant" ]

      "MarkdownSpec", record [ "text" ] [ "text", ref "TextSource" ]

      "BadgeSpec", record [ "label"; "variant" ] [ "label", ref "TextSource"; "variant", ref "BadgeVariant" ]

      "LinkSpec",
      record
          [ "download"; "href"; "label" ]
          [ "href", binding "str"
            "label", ref "TextSource"
            "download", boolean
            "rel", str
            "target", str
            // Phase 812 — "email" marks a mailto: link whose address the
            // renderers must not emit in plaintext (SSR entity-encodes it).
            "protection", ref "LinkProtection" ]

      "ImageSpec",
      // Phase 1077 — `fit` / `aspectRatio` / `loading` are omitted-when-default;
      // out of `required`, present in `props`. Phase 1078 — `caption` is
      // optional content, absent-means-`None`; same position in the schema for
      // a different reason. Phase 1080 — `srcSet` is omitted-when-EMPTY, a third
      // reason for the same position: absent and `[]` denote the same document.
      // Phase 1079 — `expandable` is omitted-when-`false`, a fourth reason and
      // the plainest: the identity of a bool declaration is not declaring it.
      record
          [ "alt"; "src"; "variant" ]
          [ "alt", ref "TextSource"
            "aspectRatio", ref "ImageAspect"
            "caption", ref "TextSource"
            "expandable", boolean
            "fit", ref "ImageFit"
            "loading", ref "ImageLoading"
            "src", binding "str"
            "srcSet", arrayOf (ref "SrcSetEntry")
            "variant", ref "ImageVariant" ]

      // Phase 1080 — one candidate rendition. `minimum: 1` is the schema's half
      // of the positive-width floor the policy decoder states; a schema that
      // admitted `0` while the decoder refused it would make the two documents
      // disagree about what the wire permits.
      "SrcSetEntry",
      record
          [ "src"; "width" ]
          [ "src", binding "str"
            "width", JObj [ "type", JStr "integer"; "minimum", JInt 1 ] ]

      // Phase 1076 — `controls` and `loop` are omitted-when-default and so sit
      // out of `required`, but for OPPOSITE reasons: `loop` omits at `false`,
      // `controls` at `true`. A schema cannot express that asymmetry and does
      // not try — it says only that the members are optional booleans, and the
      // defaults live in the normative text where the two polarities are
      // stated. `label` IS required, which is the a11y floor at its most
      // enforceable point.
      "MediaSpec",
      record
          [ "kind"; "label"; "src" ]
          [ "controls", boolean
            "kind", ref "MediaKind"
            "label", ref "TextSource"
            "loop", boolean
            "src", binding "str"
            // Phase 1110 — `tracks` is omitted-when-EMPTY (the `srcSet`
            // position) and `transcript` is optional content (the `caption`
            // position); both therefore sit out of `required`, again for two
            // different reasons the schema cannot tell apart.
            "tracks", arrayOf (ref "TrackEntry")
            "transcript", ref "TextSource" ]

      // Phase 1110 — one timed-text track. FOUR required members, which makes
      // this the strictest record in the schema; `default` is the only optional
      // one and it omits at `false`. `srcLang` is required for every kind, where
      // HTML asks for it only on subtitles — the schema states the wire's rule,
      // not the element's.
      "TrackEntry",
      record
          [ "kind"; "label"; "src"; "srcLang" ]
          [ "default", boolean
            "kind", ref "TrackKind"
            "label", ref "TextSource"
            "src", binding "str"
            "srcLang", str ]

      // Phase 1076 — `Video` carries the two video-only slots (`autoplay`
      // omitted at `false`, `poster` optional); `Audio` carries none, and the
      // empty case is the point rather than a gap.
      "MediaKind",
      union
          [ duCase "Video" [] [ "autoplay", boolean; "poster", binding "str" ]
            duCase "Audio" [] [] ]

      // Phase 1111 — `title` and `src` are required; `aspectRatio` omits at
      // `Natural` and `permissions` at EMPTY, so both sit out of `required`. The
      // schema cannot say that the empty permission list is TOTAL DENIAL rather
      // than an unset value, and does not try — that rule lives in the normative
      // text, where a default that is also a security posture can be stated as
      // one. Nor can it constrain `src` to `https`: the `embed` egress class is
      // a render obligation, and a schema that refused the bytes would make this
      // artefact stricter than the decoder it describes.
      "EmbedSpec",
      record
          [ "src"; "title" ]
          [ "aspectRatio", ref "ImageAspect"
            "permissions", arrayOf (ref "EmbedPermission")
            "src", binding "str"
            "title", ref "TextSource" ]

      // Phase 1120 — the first SELF-REFERENTIAL def in this artefact:
      // `TreeItem.children` is an array of `TreeItem`. JSON Schema references
      // are by name, so the recursion needs nothing beyond the `$ref` the
      // non-recursive record shapes already emit.
      //
      // What the schema CANNOT say is the part that matters, and it is stated
      // here rather than left to be noticed: it cannot express that row ids are
      // unique within a tree, and it cannot express the §21 item-depth bound. The
      // first is FUARAN126 and the second is a decoder refusal; a schema that
      // tried either would be describing a different language from the one the
      // decoder implements. `children` and `icon` sit out of `required` because
      // both are omitted — `children` at the empty list, which is most rows.
      "TreeItem",
      record
          [ "id"; "label" ]
          [ "children", arrayOf (ref "TreeItem")
            "icon", str
            "id", str
            "label", ref "TextSource" ]

      // Phase 1120 — `items` is the only required member. Both State keys are
      // optional strings and the schema says nothing about WHAT they hold: the
      // slot shapes (an array of row ids; a bare row id) are the host's state,
      // not this document's, so they are fixed in the normative text where a
      // reader can be told why, and nowhere here.
      "TreeSpec",
      record
          [ "items" ]
          [ "expandedStateKey", str
            "items", arrayOf (ref "TreeItem")
            "onSelect", closure
            "selectionStateKey", str ]

      "ListSpec", record [ "items"; "ordered" ] [ "items", arrayOf (ref "TextSource"); "ordered", boolean ]


      "ToastSpec",
      record
          // Phase 460 — `tone` omitted-when-default; out of `required`, stays in `props`.
          [ "message"; "open" ]
          [ "dismissable", boolean
            "message", ref "TextSource"
            "open", binding "bool"
            "tone", ref "ToneVariant" ]

      "CodeBlockSpec",
      record
          [ "code"; "copyable"; "highlightLines"; "language"; "lineNumbers" ]
          [ "code", str
            "copyable", boolean
            "highlightLines", arrayOf integer
            "language", str
            "lineNumbers", boolean ]

      "MathSpec", record [ "display"; "source" ] [ "display", ref "MathDisplay"; "source", str ]

      "SparklineSpec", record [ "source" ] [ "source", binding "list_float" ]


      "SkeletonSpec", record [ "rows" ] [ "rows", integer ]

      // Phase 821 — the standalone icon-only display kind. Only `icon` is
      // required: `size` omitted-when-Medium, `tone` omitted-when-default,
      // `label` omitted-when-decorative.
      "IconSpec", record [ "icon" ] [ "icon", str; "label", str; "size", ref "IconSize"; "tone", ref "ToneVariant" ]

      "CalloutSpec",
      record
          // Phase 460 — `tone` omitted-when-default; out of `required`, stays in `props`.
          [ "body" ]
          [ "body", ref "TextSource"
            "dismissable", boolean
            "tone", ref "ToneVariant"
            "heading", ref "TextSource"
            "icon", str ]

      "ProgressSpec",
      record
          // Phase 460 — `tone` omitted-when-default; out of `required`, stays in `props`.
          [ "fraction" ]
          [ "fraction", binding "float"
            "indeterminate", boolean
            "tone", ref "ToneVariant"
            "label", ref "TextSource"
            "caveat", ref "TextSource" ]

      "LabelValueRowSpec",
      record
          // Phase 460 — `format` omitted-when-default; out of `required`. The `emphasis`
          // bool is behavioural (not the style DU) — out of scope, stays required.
          [ "label"; "value" ]
          [ "emphasis", boolean
            "format", ref "CellFormat"
            "label", ref "TextSource"
            "value", binding "float"
            "help", ref "TextSource" ]

      "FactSpec",
      record
          // New kind (2026-07-17): minimal wire — only label + value required;
          // tone / emphasis omitted-when-default on both boundaries.
          [ "label"; "value" ]
          [ "label", ref "TextSource"
            "value", ref "TextSource"
            "tone", ref "ToneVariant"
            "emphasis", boolean
            "help", ref "TextSource"
            "icon", str ]

      // ── Drawing (Phase 524) ───────────────────────────────────────────────
      // Geometry is static floats (a Drawing is a resolved artefact); only
      // DrawStyle carries Binding colours. The Shape DU is closed + typed — no
      // raw SVG / `Path` / `d` string (the typed-surface guard, §5).
      "ViewBox",
      record [ "height"; "minX"; "minY"; "width" ] [ "height", number; "minX", number; "minY", number; "width", number ]

      "DrawPoint", record [ "x"; "y" ] [ "x", number; "y", number ]

      "TextAnchor", enumDef [ "Start"; "Middle"; "End" ]

      "DrawStyle",
      record
          []
          [ "fill", binding "str"
            "stroke", binding "str"
            "strokeWidth", binding "float"
            "opacity", binding "float"
            // Text-only fields (Phase 528.1), all optional.
            "textAnchor", ref "TextAnchor"
            "fontSize", number
            "emphasis", ref "Emphasis"
            "fontFamily", str
            // Phase 642 — keyed mark identity, optional.
            "markId", str
            // Phase 877 — `Label` text rotation in degrees, optional.
            "rotation", number
            // Phase 883 — the shape's hover readout, emitted as an SVG
            // `<title>` child of the shape's own element; optional, and the one
            // style field that applies to every shape rather than only `Label`.
            "tip", ref "TextSource" ]

      "CurveCommand",
      union
          [ duCase "MoveTo" [ "to" ] [ "to", ref "DrawPoint" ]
            duCase "LineTo" [ "to" ] [ "to", ref "DrawPoint" ]
            duCase
                "CubicTo"
                [ "control1"; "control2"; "to" ]
                [ "control1", ref "DrawPoint"
                  "control2", ref "DrawPoint"
                  "to", ref "DrawPoint" ]
            duCase "QuadraticTo" [ "control"; "to" ] [ "control", ref "DrawPoint"; "to", ref "DrawPoint" ]
            duCase "Close" [] [] ]

      "Shape",
      union
          [ duCase "Group" [ "children"; "style" ] [ "children", arrayOf (ref "Shape"); "style", ref "DrawStyle" ]
            duCase
                "Rectangle"
                [ "height"; "style"; "width"; "x"; "y" ]
                [ "height", number
                  "style", ref "DrawStyle"
                  "width", number
                  "x", number
                  "y", number
                  "cornerRadius", number ]
            duCase
                "Line"
                [ "style"; "x1"; "x2"; "y1"; "y2" ]
                [ "style", ref "DrawStyle"
                  "x1", number
                  "x2", number
                  "y1", number
                  "y2", number ]
            duCase "Polyline" [ "points"; "style" ] [ "points", arrayOf (ref "DrawPoint"); "style", ref "DrawStyle" ]
            duCase "Polygon" [ "points"; "style" ] [ "points", arrayOf (ref "DrawPoint"); "style", ref "DrawStyle" ]
            duCase
                "Curve"
                [ "commands"; "style" ]
                [ "commands", arrayOf (ref "CurveCommand"); "style", ref "DrawStyle" ]
            duCase
                "Circle"
                [ "cx"; "cy"; "r"; "style" ]
                [ "cx", number; "cy", number; "r", number; "style", ref "DrawStyle" ]
            duCase
                "Ellipse"
                [ "cx"; "cy"; "rx"; "ry"; "style" ]
                [ "cx", number
                  "cy", number
                  "rx", number
                  "ry", number
                  "style", ref "DrawStyle" ]
            duCase
                "Label"
                [ "style"; "text"; "x"; "y" ]
                [ "style", ref "DrawStyle"; "text", ref "TextSource"; "x", number; "y", number ] ]

      "DrawingSpec",
      record
          [ "shapes"; "style"; "viewBox" ]
          [ "shapes", arrayOf (ref "Shape")
            "style", ref "DrawStyle"
            "viewBox", ref "ViewBox"
            "title", ref "TextSource"
            "description", ref "TextSource" ]

      "DisplayKind",
      union
          [ duCaseHoisted "Heading" "HeadingSpec"
            duCaseHoisted "Markdown" "MarkdownSpec"
            duCaseHoisted "Metric" "MetricSpec"
            duCaseHoisted "Badge" "BadgeSpec"
            duCaseHoisted "Sparkline" "SparklineSpec"
            duCaseHoisted "Callout" "CalloutSpec"
            duCaseHoisted "Progress" "ProgressSpec"
            duCaseHoisted "Skeleton" "SkeletonSpec"
            duCaseHoisted "Icon" "IconSpec"
            duCaseHoisted "LabelValueRow" "LabelValueRowSpec"
            duCaseHoisted "Fact" "FactSpec"
            duCaseHoisted "Link" "LinkSpec"
            duCaseHoisted "Image" "ImageSpec"
            duCaseHoisted "Media" "MediaSpec"
            duCaseHoisted "Embed" "EmbedSpec"
            duCaseHoisted "Tree" "TreeSpec"
            duCaseHoisted "List" "ListSpec"
            duCaseHoisted "Toast" "ToastSpec"
            duCaseHoisted "CodeBlock" "CodeBlockSpec"
            duCaseHoisted "Math" "MathSpec"
            duCaseHoisted "Drawing" "DrawingSpec" ]

      // ── Input specs ───────────────────────────────────────────────────────
      "SelectOption", record [ "label"; "value" ] [ "label", ref "TextSource"; "value", str ]

      // Phase 864 — the declared per-field constraint. `FormFieldKind` names the
      // CONTROL; `rule` names the ACCEPTED SET.
      "CompareRule", record [ "against"; "op" ] [ "against", binding "json"; "op", ref "CompareOp" ]

      // Two of the decoder's three refusals are expressible here and both are
      // stated rather than exempted. `anyOf` over the five constraint slots is
      // "a rule must constrain something" — a rule carrying only `message` is
      // the help-text failure wearing the new vocabulary's clothes, and it fails
      // the schema exactly as it fails the decoder. The THIRD refusal
      // (`minLength` above `maxLength`) is a relation between two sibling
      // values, which the dialect genuinely cannot state — so it, and only it,
      // joins `schemaInexpressibleRejects` beside the `DateRange` ordered-pair
      // rule it is modelled on.
      "FieldRule",
      (match
          record
              []
              [ "compare", ref "CompareRule"
                "format", ref "TextFormat"
                "maxLength", integer
                "message", ref "TextSource"
                "minLength", integer
                "pattern", str ]
       with
       | JObj fields ->
           JObj(
               fields
               @ [ "anyOf",
                   JArr(
                       [ "format"; "pattern"; "minLength"; "maxLength"; "compare" ]
                       |> List.map (fun n -> JObj [ "required", JArr [ JStr n ] ])
                   ) ]
           )
       | other -> other)

      "FormField",
      // `validation` / `constraints` / `validate` are the enumerated near misses
      // of `rule` (the Phase 863 discipline). Forbidden by name rather than by
      // `additionalProperties: false`, so rule 2's tolerance of genuinely-unknown
      // keys survives.
      forbidding
          [ "validation"; "constraints"; "validate" ]
          (record
              [ "id"; "kind"; "label"; "required" ]
              [ "id", str
                "kind", ref "FormFieldKind"
                "label", ref "TextSource"
                "required", boolean
                "help", ref "TextSource"
                "rule", ref "FieldRule" ])

      "FormSpec",
      record
          [ "fields"; "onSubmit"; "submitLabel" ]
          [ "fields", arrayOf (ref "FormField")
            "onSubmit", ref "Action"
            "submitLabel", ref "TextSource"
            "disabled", binding "bool" ]

      "FilterSpec",
      record [ "kind"; "label"; "name" ] [ "kind", ref "FormFieldKind"; "label", ref "TextSource"; "name", str ]

      "ButtonSpec",
      record
          [ "label"; "onClick"; "variant" ]
          [ "label", ref "TextSource"
            "onClick", ref "Action"
            "variant", ref "ButtonVariant"
            "icon", str
            "disabled", binding "bool" ]

      "SelectSpec",
      record
          // `onChange` is optional since Phase 426 (the control write-back
          // default) — omitted for a declarative select; `onChangeMulti` is the
          // multi-select handler's own sentinel key (Phase 426; previously the
          // handler was never encoded).
          [ "label"; "source"; "value" ]
          [ "label", ref "TextSource"
            "onChange", closure
            "source", binding "list_SelectOption"
            "value", binding "str"
            "placeholder", ref "TextSource"
            "disabled", binding "bool"
            // Phase 291 — multi-select. Optional in the schema (omitted when
            // single-select), matching the decoder's tolerance.
            "multiple", boolean
            "values", binding "list_str"
            "onChangeMulti", closure ]

      "FileUploadSpec",
      record
          [ "accept"; "label"; "multiple"; "onSelect" ]
          [ "accept", arrayOf str
            "label", ref "TextSource"
            "multiple", boolean
            "onSelect", closure
            "disabled", binding "bool"
            // Phase 1115 — the ingress gestures. Out of `required`, matching the
            // decoder's omit-at-`false`: an absent member is the plain picker.
            "dropTarget", boolean
            "acceptPaste", boolean
            // Phase 1116 — likewise out of `required`: an absent member is the
            // ordinary picker, not a device the emitter forgot to name.
            "capture", ref "CaptureSource" ]

      "InputKind",
      union
          [ duCaseHoisted "Form" "FormSpec"
            duCase "Filters" [ "items" ] [ "items", arrayOf (ref "FilterSpec") ]
            duCaseHoisted "Button" "ButtonSpec"
            duCaseHoisted "FileUpload" "FileUploadSpec"
            duCaseHoisted "Select" "SelectSpec" ]

      // ── Visualisation specs ───────────────────────────────────────────────
      // Phase 425 — `value` (closure) + `field` (declarative row-property name) are sibling optional
      // slots (both out of `required`); likewise `rowKey` + `rowKeyField` on the grid.
      "ColumnErased",
      // Phase 863 — `readOnly` is the column's one near miss; the decoder
      // refuses it didactically rather than aliasing it to `editable: false`.
      forbidding [ "readOnly" ]
      <| record
          // Phase 460 — `format`/`width` omitted-when-default (CellFormat.None / ColumnWidth.Auto);
          // out of `required`, they stay in `props`.
          [ "kind"; "label" ]
          [ "format", ref "CellFormat"
            "kind", ref "CellKindErased"
            "label", str
            "value", closure
            "field", str
            // Phase 861 — per-column sort narrowing on the bound path.
            "sortable", boolean
            // Phase 863 — per-column editability narrowing.
            "editable", boolean
            "width", ref "ColumnWidth" ]

      "GridSpec",
      // Phase 863 — the grid's near-miss set, refused here as it is at decode.
      // `sortable` is forbidden at GRID level only: it is a real field one
      // level down on `staticRows`, which is exactly why a model reaches for it
      // here.
      forbidding
          [ "currentPage"
            "page"
            "pageIndex"
            "sortable"
            "onEdit"
            "behaviour"
            "behavior" ]
      <| record
          [ "columns"; "source" ]
          [ "columns", arrayOf (ref "ColumnErased")
            "editable", boolean
            "rowKey", closure
            "rowKeyField", str
            // Phase 818 — the grid-sort header affordance: the State key
            // carrying the `{column, direction}` sort descriptor a data-bound
            // grid's runtime sorts by (and whose sortable headers write).
            "sortStateKey", str
            // Phase 862 — declarative pagination. `pageStateKey` names the State
            // key carrying `{"page": N}` (1-based); `pageSize` is the rows per
            // page and carries `minimum: 1`, so the schema refuses a zero or
            // negative page exactly where the validator does. Both optional, so
            // the pre-862 shape validates unchanged.
            "pageSize", JObj [ "type", JStr "integer"; "minimum", JInt 1 ]
            "pageStateKey", str
            // Phase 863 — the declared edit destination.
            "editStateKey", str
            // Phase 934 — declarative row reorder; omit-when-false, as `editable`.
            "reorderable", JObj [ "type", JStr "boolean" ]
            // Phase 1123 — cross-container transfer: the two sides of ONE shared
            // State key. Both plain optional strings here, and that is the whole
            // of what a structural schema can say about them: whether any OTHER
            // grid declares the counterpart is a cross-node relation, which
            // Draft 2020-12 cannot state at all — so the pairing rule lives at
            // pre-emit (FUARAN129), beside `pageSize`-without-`pageStateKey` and
            // for the same reason.
            "transferInKey", str
            "transferOutKey", str
            // Phase 1473 — the grid's own two print-break declarations, on the
            // same omit-when-false convention. `keepRowsTogether` names the row
            // boundary, `repeatHeader` the header row group — the two the
            // container pair on `BoxSpec` structurally cannot reach.
            "keepRowsTogether", JObj [ "type", JStr "boolean" ]
            "repeatHeader", JObj [ "type", JStr "boolean" ]
            // Phase 1125 — the export affordance; omit-when-false like its
            // neighbours, and out of `required` for the same reason.
            "exportable", JObj [ "type", JStr "boolean" ]
            // Phase 861 — the bound path's declared initial order. Same record
            // and same `minimum: 0` bound the `staticRows` spelling carries.
            "defaultSort",
            record
                [ "column"; "direction" ]
                [ "column", JObj [ "type", JStr "integer"; "minimum", JInt 0 ]
                  "direction", enumDef [ "asc"; "desc" ] ]
            "source", binding "hosted"
            "onRowClick", closure
            // Phase 393 — the static read-only mode (folded in from the retired `Table`).
            // Phase 801 — plus the optional declarative sort intent. `column` carries
            // `minimum: 0` so the schema rejects a negative header index exactly where
            // the decoder does; both fields are optional, so the pre-801 shape validates
            // unchanged.
            "staticRows",
            record
                [ "headers"; "rows" ]
                [ "defaultSort",
                  record
                      [ "column"; "direction" ]
                      [ "column", JObj [ "type", JStr "integer"; "minimum", JInt 0 ]
                        "direction", enumDef [ "asc"; "desc" ] ]
                  "headers", arrayOf (ref "TextSource")
                  "rows", arrayOf (arrayOf (ref "TextSource"))
                  "sortable", boolean ] ]

      "ChartSpec",
      record
          [ "kind"; "source"; "xField"; "yFields" ]
          [ "kind", ref "ChartKind"
            "source", binding "hosted"
            "xField", str
            "yFields", arrayOf str
            // `stacked` (Phase 126) is now carried; optional in the schema to
            // match the decoder's tolerance of legacy wire that omits it.
            "stacked", boolean
            "title", ref "TextSource"
            // `valueFormat` (Phase 876) — the value axis's number format,
            // reusing the existing `Format` vocabulary; optional (absent means
            // the lowering's canonical default rendering).
            "valueFormat", ref "Format"
            // `xTitle` / `yTitle` / `subtitle` (Phase 878) — the axis names and
            // the muted line under the title; all optional, since an absent
            // axis title falls back to the capitalised field name.
            "xTitle", ref "TextSource"
            "yTitle", ref "TextSource"
            "subtitle", ref "TextSource"
            // `legendPosition` (Phase 880) — which edge the legend occupies, or
            // `None` to suppress it. Optional: absent means the host style's
            // default (`Right`), never "no legend".
            "legendPosition", ref "ChartLegendPosition"
            // `dataLabels` (Phase 881) — whether the values are written onto
            // the picture. Optional: absent means `Off`, which is also the
            // default. Two values only; there is no all-points mode by design.
            "dataLabels", ref "ChartDataLabels"
            // `xScale` (Phase 882) — what the x column MEANS: discrete
            // `Category` bands or `Temporal` dates on a continuous day-scale.
            // Optional: absent means `Category`, which is also the default.
            "xScale", ref "ChartXScale"
            "onPointClick", closure ]

      "MapMarker",
      record [ "label"; "latitude"; "longitude" ] [ "label", ref "TextSource"; "latitude", number; "longitude", number ]

      "MapSpec",
      record
          [ "centreLatitude"; "centreLongitude"; "source"; "zoom" ]
          [ "centreLatitude", number
            "centreLongitude", number
            "source", binding "list_MapMarker"
            "zoom", integer
            "onMarkerClick", closure ]

      "VisKind",
      union
          [ duCaseHoisted "DataGrid" "GridSpec"
            duCaseHoisted "Chart" "ChartSpec"
            duCaseHoisted "Map" "MapSpec" ]

      // ── Layout specs ──────────────────────────────────────────────────────
      // Phase 390 — the unified container. `layout` is a nested $type-DU
      // (Flex | Grid | Auto); `role` a string enum; `heading` optional.
      "BoxSpec",
      record
          [ "children"; "layout"; "role" ]
          [ "children", arrayOf (ref "Node")
            "heading", ref "TextSource"
            "layout", ref "BoxLayout"
            "role", enumDef [ "Group"; "Card"; "Dashboard"; "Separator" ]
            // Phase 1473 — the print-break declarations. Out of `required`,
            // matching the decoder's omit-at-`false`: an absent flag is a
            // container that declares nothing about pagination.
            "keepTogether", boolean
            "breakBefore", boolean ]

      "FlexLayout", record [ "direction"; "wrap" ] [ "direction", ref "Orientation"; "gap", integer; "wrap", boolean ]

      "GridTemplate", record [ "cols" ] [ "cols", integer; "gap", integer; "templateColumns", str ]

      // Phase 1082 — column-fill. `minimum: 1` is the schema's half of the
      // decoder's positive-column floor, so the two expressions of the contract
      // agree (the `srcSet` width precedent). No `templateColumns`: the case does
      // not carry one, and a schema that admitted it would describe a wire the
      // decoder refuses.
      "MasonryLayout", record [ "cols" ] [ "cols", JObj [ "type", JStr "integer"; "minimum", JInt 1 ]; "gap", integer ]

      "BoxLayout",
      union
          [ duCaseHoisted "Flex" "FlexLayout"
            duCaseHoisted "Grid" "GridTemplate"
            duCaseHoisted "Masonry" "MasonryLayout"
            duCase "Auto" [] [] ]

      "SplitPanelSpec", record [ "children"; "weight" ] [ "children", arrayOf (ref "Node"); "weight", number ]

      "TabHeader", record [ "label" ] [ "label", ref "TextSource"; "icon", str; "disabled", binding "bool" ]

      "TabsSpec",
      record
          // 0.2.0 — `orientation` omitted-when-Horizontal.
          [ "children" ]
          [ "children", arrayOf (ref "Node")
            "orientation", ref "Orientation"
            // `activeIndex` (Phase 126) is carried; optional in the schema to
            // match the decoder's tolerance of legacy wire that omits it.
            // `onSelect` / `onSelectTag` (Phase 426) are optional closures —
            // present as the sentinel when closure-authored, omitted for the
            // declarative (write-back) shape.
            "activeIndex", binding "int"
            "onSelect", closure
            "tabHeaders", arrayOf (ref "TabHeader")
            "tabTags", arrayOf str
            "activeTag", binding "str"
            "onSelectTag", closure ]

      "StepperSpec",
      record
          [ "activeStep"; "children" ]
          [ "activeStep", binding "int"
            "children", arrayOf (ref "Node")
            // `onSelect` is now carried; optional in the schema to match the
            // decoder's tolerance of legacy wire that omits it (the closure
            // sentinel) — same treatment as Tabs `onSelect`.
            "onSelect", closure ]

      "SummaryListSpec", record [ "children" ] [ "children", arrayOf (ref "Node"); "heading", ref "TextSource" ]

      "DisclosureSpec",
      record
          // `onToggle` (Phase 426) is an optional closure — present as the
          // sentinel when closure-authored (previously never encoded), omitted
          // for the declarative (write-back) shape.
          [ "children"; "defaultOpen"; "heading"; "open" ]
          [ "children", arrayOf (ref "Node")
            "defaultOpen", boolean
            "heading", ref "TextSource"
            "open", binding "bool"
            "onToggle", closure ]

      "ModalSpec",
      record
          // `onDismiss` is optional since Phase 426 (the control write-back
          // default) — a declarative modal omits it and the renderer writes
          // `false` to a writable `open` slot on dismiss.
          //
          // Phase 1119 — `modality` omits at `Modal` and `anchor` is optional, so
          // neither joins the required set; a pre-1119 document validates against
          // this schema unchanged.
          [ "children"; "dismissable"; "open" ]
          [ "children", arrayOf (ref "Node")
            "dismissable", boolean
            "onDismiss", ref "Action"
            "open", binding "bool"
            "heading", ref "TextSource"
            "modality", ref "ModalityKind"
            "anchor", str ]

      "ScrollAreaSpec",
      record
          [ "children"; "orientation" ]
          [ "children", arrayOf (ref "Node")
            "orientation", ref "ScrollOrientation"
            "maxHeight", integer
            "maxWidth", integer ]

      "LayoutKind",
      union
          [ duCaseHoisted "Box" "BoxSpec"
            duCaseHoisted "SplitPanel" "SplitPanelSpec"
            duCaseHoisted "Tabs" "TabsSpec"
            duCaseHoisted "Stepper" "StepperSpec"
            duCaseHoisted "SummaryList" "SummaryListSpec"
            duCaseHoisted "Disclosure" "DisclosureSpec"
            duCaseHoisted "Modal" "ModalSpec"
            duCaseHoisted "ScrollArea" "ScrollAreaSpec" ]

      // ── Custom envelope (§3.2) ────────────────────────────────────────────
      "ContentHash",
      record [ "algorithm"; "hash"; "strictness" ] [ "algorithm", str; "hash", str; "strictness", ref "HashStrictness" ]

      // ── Parameterised-fragment surface (Phase 180) ────────────────────────
      "HoleValueSpace",
      union
          [ duCase "IntRange" [ "max"; "min" ] [ "max", integer; "min", integer ]
            duCase "FloatRange" [ "max"; "min" ] [ "max", number; "min", number ]
            duCase "StringLen" [ "maxLen"; "minLen" ] [ "maxLen", integer; "minLen", integer ]
            duCase "Enum" [ "choices" ] [ "choices", arrayOf str ]
            duCase "AnyString" [] [] ]

      // A self-describing boxed scalar — a value default or value argument.
      "Scalar",
      union
          [ duCase "Int" [ "value" ] [ "value", integer ]
            duCase "Float" [ "value" ] [ "value", number ]
            duCase "Bool" [ "value" ] [ "value", boolean ]
            duCase "Str" [ "value" ] [ "value", str ] ]

      "HoleDecl",
      union
          [ duCase "Value" [ "name"; "space" ] [ "name", str; "space", ref "HoleValueSpace"; "default", ref "Scalar" ]
            duCase "Slot" [ "name" ] [ "name", str; "kindConstraint", str ]
            duCase "Repeat" [ "countSpace"; "name" ] [ "countSpace", ref "HoleValueSpace"; "name", str ] ]

      "EffectClass",
      record
          [ "determinism"; "hostEffect" ]
          [ "determinism", enumDef [ "Deterministic"; "Clock"; "Random"; "Network" ]
            "hostEffect", enumDef [ "Pure"; "ReadsHost"; "WritesHost" ] ]

      // A bound argument at a FragmentRef — a value scalar (Int/Float/Bool/Str)
      // or a slot subtree. Also reused by `MountSpec.inputs` (§4o).
      "FragmentArg",
      union
          [ duCase "Int" [ "value" ] [ "value", integer ]
            duCase "Float" [ "value" ] [ "value", number ]
            duCase "Bool" [ "value" ] [ "value", boolean ]
            duCase "Str" [ "value" ] [ "value", str ]
            duCase "SlotArg" [ "tree" ] [ "tree", ref "Node" ] ]

      // The declared out-channel of a Mount (§4o.4). `direction` required;
      // `messageShape` optional (omitted at None per rule 4).
      "GuestChannel", record [ "direction" ] [ "direction", enumDef [ "OutOnly"; "TwoWay" ]; "messageShape", str ]

      // ── NodeKind (§3.2) ───────────────────────────────────────────────────
      // The four behavioural categories are flat on the wire: a node's `kind`
      // carries the primitive discriminator directly, so NodeKind is the union
      // of the four inner-kind $defs (each itself a $type-discriminated oneOf)
      // plus the structural primitives below. Every $type const across the four
      // inner kinds is globally unique (the historical "Grid" collision is gone:
      // since the Phase 390 Box merge, Grid is a BoxLayout mode and DataGrid is
      // the only grid-named kind), so exactly one branch matches any node.
      "NodeKind",
      union
          [ ref "LayoutKind"
            ref "DisplayKind"
            ref "InputKind"
            ref "VisKind"
            duCase
                "Custom"
                [ "componentId"; "moduleId"; "props" ]
                [ "componentId", str
                  "moduleId", str
                  "props", jsonValueMap
                  "contentHash", ref "ContentHash"
                  "exposedNodeIds", arrayOf str ]
            duCase "ErrorBoundary" [ "child"; "fallback" ] [ "child", ref "Node"; "fallback", ref "Node" ]
            // Binding-driven conditional child (Phase 392; selector widened by
            // Phase 768). The selector is `on` (any Binding) OR the compact
            // `stateKey` (the State form's canonical spelling) — exactly one is
            // required, expressed as an `anyOf` over the two `required` lists so
            // the both-absent reject fixture still fails the schema, keeping the
            // schema an honest mirror of the decoder's MISSING_FIELD.
            JObj
                [ "allOf",
                  JArr
                      [ duCase
                            "Switch"
                            [ "cases"; "default" ]
                            [ "cases", arrayOf (record [ "child"; "match" ] [ "child", ref "Node"; "match", str ])
                              "default", ref "Node"
                              "stateKey", str
                              "on", binding "str"
                              // Phase 1122 — the timed-advance interval. The
                              // POSITIVE floor is expressed here as
                              // `minimum: 1` rather than left to the
                              // decoder alone, so the schema leg and the reject
                              // fixture agree: a `0` fails both, which is what
                              // keeps this file an honest mirror of the decoder
                              // rather than a looser second opinion.
                              "autoAdvanceMs", JObj [ "type", JStr "integer"; "minimum", JInt 1 ] ]
                        JObj
                            [ "anyOf",
                              JArr
                                  [ JObj [ "required", JArr [ JStr "stateKey" ] ]
                                    JObj [ "required", JArr [ JStr "on" ] ] ] ] ] ]
            duCase
                "FragmentDecl"
                [ "body"; "name" ]
                [ "body", ref "Node"
                  "name", str
                  "holes", arrayOf (ref "HoleDecl")
                  "effect", ref "EffectClass" ]
            duCase
                "FragmentRef"
                [ "name" ]
                [ "name", str
                  "args", JObj [ "type", JStr "object"; "additionalProperties", ref "FragmentArg" ] ]
            // Isolation/embedding boundary (§4o). `scopeId` + `channel` +
            // `capabilities` + the `onBubble` closure sentinel always present;
            // `inputs` (a FragmentArg map) additive.
            duCase
                "Mount"
                [ "capabilities"; "channel"; "onBubble"; "scopeId" ]
                [ "capabilities", arrayOf str
                  "channel", ref "GuestChannel"
                  "onBubble", closure
                  "scopeId", str
                  "inputs", JObj [ "type", JStr "object"; "additionalProperties", ref "FragmentArg" ] ] ]

      // ── Node envelope (§3.1) ──────────────────────────────────────────────
      // StateBehaviour: all-optional (onError is the closure sentinel when
      // present). SemanticStyle: every field optional — tone/weight/emphasis
      // (Phase 460) join role/voice (Phase 147) and direction (Phase 1472) as
      // omitted-at-default.
      // Accessibility: all-optional.
      "StateBehaviour", record [] [ "onLoading", ref "Node"; "onEmpty", ref "Node"; "onError", closure ]

      "SemanticStyle",
      record
          []
          [ "emphasis", ref "Emphasis"
            "tone", ref "ToneVariant"
            "weight", ref "StyleWeight"
            "role", ref "StyleRole"
            "voice", ref "FontVoice"
            "direction", ref "TextDirection" ]

      "Accessibility",
      // Phase 959 — the trait's near-miss set, refused here as it is at decode.
      // Grouped by the slot each points at, in the decoder's declaration order,
      // so the two artefacts read as one table.
      forbidding
          [ "aria-label"
            "ariaLabel"
            "aria-labelledby"
            "ariaLabelledBy"
            "labelledby"
            "aria-describedby"
            "ariaDescribedBy"
            "describedby"
            "aria-role"
            "ariaRole"
            "aria-live"
            "ariaLive"
            "live"
            "liveregion"
            "aria-hidden"
            "ariaHidden" ]
      <| record
          []
          [ "label", binding "str"
            "labelledBy", str
            "describedBy", str
            "role", ref "AriaRole"
            "liveRegion", ref "LiveRegionKind"
            "hidden", binding "bool" ]

      "Node",
      // `state` and `style` are optional on the flat wire — omitted when empty /
      // all-default, restored to their default on decode (WIRE_FORMAT §3.1).
      record
          [ "id"; "kind" ]
          [ "id", JObj [ "type", JStr "string"; "minLength", JInt 1 ]
            "kind", ref "NodeKind"
            "state", ref "StateBehaviour"
            "style", ref "SemanticStyle"
            "accessibility", ref "Accessibility"
            // Phase 1112 — the node-level tooltip trait, optional and omitted
            // when absent. `TextSource` rather than a bare string: the schema
            // describes the CANONICAL form the encoder produces, and the §16
            // bare-string shorthand the decoder also accepts is a lenient
            // profile this artefact deliberately does not widen to (the same
            // call every other `TextSource` slot already made).
            "tooltip", ref "TextSource" ]

      // ── TreeOp (§3.4) ─────────────────────────────────────────────────────
      "TreeOp",
      union
          [ duCase "EditNode" [ "newKind"; "target" ] [ "newKind", ref "NodeKind"; "target", str ]
            duCase "UpdateProp" [ "path"; "target"; "value" ] [ "path", str; "target", str; "value", jsonValue ]
            duCase
                "ReplaceBinding"
                [ "binding"; "slot"; "target" ]
                [ "binding", binding "json"; "slot", str; "target", str ]
            duCase "UpdateStyle" [ "style"; "target" ] [ "style", ref "SemanticStyle"; "target", str ]
            duCase "UpdateState" [ "state"; "target" ] [ "state", ref "StateBehaviour"; "target", str ]
            // `position` / `newPosition` are the RETIRED positional slots (Phase
            // 681 removed them, Phase 687 closed the accept-and-ignore window).
            // Forbidden BY NAME for the same reason the `FormField` near misses
            // are: rule 2's tolerance of genuinely-unknown keys has to survive,
            // so `additionalProperties: false` is the wrong instrument. This is
            // the structural mirror of the decoders' `retiredPositionalField`
            // refusal — the schema and the hosts must agree, or a payload the
            // corpus calls a reject validates clean here.
            forbidding
                [ "position" ]
                (duCase "InsertChild" [ "child"; "parentId" ] [ "child", ref "Node"; "parentId", str ])
            duCase "RemoveNode" [ "target" ] [ "target", str ]
            forbidding
                [ "newPosition" ]
                (duCase "MoveNode" [ "newParentId"; "target" ] [ "newParentId", str; "target", str ])
            duCase "ReorderChildren" [ "newOrder"; "parentId" ] [ "newOrder", arrayOf str; "parentId", str ]
            duCase "ReplaceRoot" [ "node" ] [ "node", ref "Node" ]
            duCase "Batch" [ "ops" ] [ "ops", arrayOf (ref "TreeOp") ] ] ]

// ─── Public surface ───────────────────────────────────────────────────────

/// The canonical wire-format JSON Schema (Draft 2020-12) as a deterministic,
/// pretty-printed string. The top-level schema accepts either a `Node` or a
/// `TreeOp` (the two decode entry points); `$defs` exposes `Node` and `TreeOp`
/// directly for hosts that want to validate one shape specifically.
///
/// Generated, not hand-authored — re-derive and overwrite
/// `wire-format-fixtures/schema.json` whenever the contract changes (the
/// forward-coupling rule, WIRE_FORMAT.md §11). The stale-schema guard test
/// asserts byte-equality between this string and the committed artefact.
let wireFormatSchema: string =
    let root =
        JObj
            [ "$schema", JStr "https://json-schema.org/draft/2020-12/schema"
              "$id", JStr schemaId
              "title", JStr "Fuaran UI wire format (v1)"
              "description",
              JStr(
                  "Canonical JSON wire format for the Fuaran UI tree (Node) and tree edits (TreeOp), "
                  + "wire format v1. Generated from Fuaran.UI.Types via Fuaran.UI.Ops.SchemaGen; "
                  + "describes the output of CanonicalJson.encode* / the input JsonDecode.decode* accepts. "
                  + "See fuaran-dotnet/docs/WIRE_FORMAT.md."
              )
              "oneOf", JArr [ ref "Node"; ref "TreeOp" ]
              "$defs", JObj defs ]

    let sb = StringBuilder()
    writeJ sb 0 root
    sb.Append '\n' |> ignore
    sb.ToString()
