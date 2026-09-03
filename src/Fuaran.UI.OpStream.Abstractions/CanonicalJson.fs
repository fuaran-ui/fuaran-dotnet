module Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ============================================================================
//  Canonical-JSON encoder.
//
//  Pinned algorithm in docs/migrations/12-Z-op-stream.md. Two consumers:
//
//   1. Hash chain (server-side). `HashChain.computeHash` calls `encodeOp`
//      to produce the canonical string that feeds SHA-256.
//
//   2. AI pre-emit self-check (Fable-side). Authors call `encodeNode tree`
//      and pipe the result through `ArgsJsonContract.validate` to catch
//      wire-shape violations cheaper than the apply-engine envelope.
//
//  Fable-compatible: no reflection, no `System.Security.*`, no captured
//  closures. Walks the Fuaran.UI.Types DU surface directly.
//
//  Closure rendering: function-typed payloads (Binding accessors, Action
//  callbacks, FormFieldKind onChange handlers, CellKindErased actions,
//  Column.Value projections, etc.) render as the sentinel `"<closure>"`.
//  This is the v1 limitation called out in the migration doc — two ops
//  differing only in opaque `'Msg` / closure payloads hash identically.
//
//  obj-typed values: best-effort dispatch. Recognised primitives (string,
//  bool, integer, float, list/seq, tuple, F# record / option / DU) encode
//  via their structural shape; unrecognised CLR objects render as the
//  sentinel `"<opaque>"`. No reflection over arbitrary types.
// ============================================================================

open System
open System.Globalization
open System.Text
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

let private closureSentinel = "<closure>"
let private opaqueSentinel = "<opaque>"

// ─── Primitive appenders ──────────────────────────────────────────────────

let private appendRawString (sb: StringBuilder) (s: string) : unit =
    sb.Append '"' |> ignore

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore

let private appendInt (sb: StringBuilder) (n: int) : unit =
    sb.Append(n.ToString(CultureInfo.InvariantCulture)) |> ignore

let private appendInt64 (sb: StringBuilder) (n: int64) : unit =
    sb.Append(n.ToString(CultureInfo.InvariantCulture)) |> ignore

#if FABLE_COMPILER
// Fable does not support the `Double.ToString("R", …)` round-trip specifier
// the canonical numeric form (WIRE_FORMAT.md §2 rule 5) mandates. Both runtimes
// compute the SAME shortest round-trip *digits* (.NET "R" and JS
// `Number.prototype.toString()` are both shortest-round-trip since .NET Core
// 3.0); they diverge only in *layout* — .NET uses fixed notation iff the
// leading-digit decimal exponent is in [-4, 16], else scientific with an
// uppercase `E`, an always-present sign, and a ≥2-digit zero-padded exponent
// (`1E+21`, `1E-07`); JS's thresholds are wider and its scientific form is
// lowercase with an unpadded exponent. `formatFiniteDouble` extracts JS's own
// shortest digits + exponent and re-lays them in .NET form, so the Fable
// encoder is byte-identical to the .NET `"R"` encoder across the whole
// finite-double range. Ported verbatim from the `fuaran-ts` `@fuaran-ui/ops`
// `formatFiniteDouble` (encode.ts) — the proven byte-identical encoder-parity
// oracle. (Phase 192 — cross-pipeline apply parity.)
[<Fable.Core.Emit("$0.toString()")>]
let private jsNumberToString (n: float) : string = Fable.Core.Util.jsNative

let private formatFiniteDouble (n: float) : string =
    if n = 0.0 then
        "0"
    else
        let neg = n < 0.0
        let s = jsNumberToString (abs n)

        // Decompose into significant `digits` (no point) + `exp`, the base-10
        // exponent of the leading digit (value = d0.d1d2… × 10^exp).
        let mutable digits = ""
        let mutable exp = 0
        let eIdx = s.IndexOf 'e'

        if eIdx >= 0 then
            let mant = s.Substring(0, eIdx)
            let mantExp = int (s.Substring(eIdx + 1))
            let dot = mant.IndexOf '.'

            if dot < 0 then
                digits <- mant
                exp <- mantExp + (mant.Length - 1)
            else
                digits <- mant.Substring(0, dot) + mant.Substring(dot + 1)
                exp <- mantExp + (dot - 1)
        else
            let dot = s.IndexOf '.'

            if dot < 0 then
                digits <- s
                exp <- s.Length - 1
            else
                let intPart = s.Substring(0, dot)
                let fracPart = s.Substring(dot + 1)

                if intPart = "0" then
                    let trimmed = fracPart.TrimStart('0')
                    let leadingZeros = fracPart.Length - trimmed.Length
                    digits <- fracPart.Substring(leadingZeros)
                    exp <- -(leadingZeros + 1)
                else
                    digits <- intPart + fracPart
                    exp <- intPart.Length - 1

        // Reduce to shortest significant digits (only trailing zeros can drop —
        // the leading digit is already significant).
        digits <- digits.TrimEnd('0')

        if digits = "" then
            digits <- "0"

        let out =
            if exp >= -4 && exp <= 16 then
                // Fixed-point layout.
                if exp >= 0 then
                    if digits.Length <= exp + 1 then
                        digits + String.replicate (exp + 1 - digits.Length) "0"
                    else
                        digits.Substring(0, exp + 1) + "." + digits.Substring(exp + 1)
                else
                    "0." + String.replicate (-exp - 1) "0" + digits
            else
                // Scientific layout: uppercase E, signed, ≥2-digit zero-padded exponent.
                let mantissa =
                    if digits.Length = 1 then
                        digits
                    else
                        string digits[0] + "." + digits.Substring(1)

                let expSign = if exp >= 0 then "+" else "-"
                let expDigits = (abs exp).ToString().PadLeft(2, '0')
                mantissa + "E" + expSign + expDigits

        if neg then "-" + out else out
#endif

let private appendFloat (sb: StringBuilder) (n: float) : unit =
    if Double.IsNaN n then
        sb.Append "\"NaN\"" |> ignore
    elif Double.IsPositiveInfinity n then
        sb.Append "\"Infinity\"" |> ignore
    elif Double.IsNegativeInfinity n then
        sb.Append "\"-Infinity\"" |> ignore
    else
        // Collapse -0 to 0 per algorithm rule 5.
        let v = if n = 0.0 then 0.0 else n
#if FABLE_COMPILER
        sb.Append(formatFiniteDouble v) |> ignore
#else
        sb.Append(v.ToString("R", CultureInfo.InvariantCulture)) |> ignore
#endif

let private appendBool (sb: StringBuilder) (b: bool) : unit =
    sb.Append(if b then "true" else "false") |> ignore

let private appendNull (sb: StringBuilder) : unit = sb.Append "null" |> ignore

// ─── Structural appenders ─────────────────────────────────────────────────

type private Appender = StringBuilder -> unit
type private Field = string * Appender

/// Object emit with keys sorted Ordinal. `fields` are pre-filtered — `None`
/// option fields are simply omitted by the caller (algorithm rule 4).
let private appendObject (sb: StringBuilder) (fields: Field list) : unit =
    let sorted =
        fields |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))

    sb.Append '{' |> ignore
    let mutable first = true

    for (key, valueFn) in sorted do
        if not first then
            sb.Append ',' |> ignore

        first <- false
        appendRawString sb key
        sb.Append ':' |> ignore
        valueFn sb

    sb.Append '}' |> ignore

let private appendArrayWith (sb: StringBuilder) (items: Appender list) : unit =
    sb.Append '[' |> ignore
    let mutable first = true

    for valueFn in items do
        if not first then
            sb.Append ',' |> ignore

        first <- false
        valueFn sb

    sb.Append ']' |> ignore

let private case (discriminator: string) (extraFields: Field list) : Field list =
    ("$type", (fun sb -> appendRawString sb discriminator)) :: extraFields

let private sentinel (s: string) : Appender = fun sb -> appendRawString sb s

let private str (s: string) : Appender = fun sb -> appendRawString sb s

let private int_ (n: int) : Appender = fun sb -> appendInt sb n
let private nodeIdStr (NodeId raw) : string = raw

/// The canonical `args` array of a `Binding.Invoke` / `Action.Invoke` (Phase 283) — scalar
/// `(addr, value)` pairs as `[{"addr":…,"value":…}]`. Shared by both encoders; key order is
/// canonical (`addr` < `value`) per the `appendObject` pre-sort.
let private invokeArgsAppender (args: (string * string) list) : Appender =
    fun sb ->
        appendArrayWith
            sb
            (args
             |> List.map (fun (a, v) -> (fun sb -> appendObject sb [ "addr", str a; "value", str v ])))

// ─── obj best-effort encoder ──────────────────────────────────────────────
//
// Used by PropValue.Native / untyped Binding.Static slots / legacy obj-erased
// seams. Recognises a handful of structural primitives without touching
// reflection (which Fable does not support). Anything else collapses to the
// `"<opaque>"` sentinel — preserves canonicalisation without inventing content
// the encoder can't see. Since Phase 429 the enumerated slot-typed payloads
// (options / values / series / markers) bypass this via `encodeBindingWith`'s
// typed static encoders; this catch-all is the residual-opaque boundary.

let private appendObj (sb: StringBuilder) (v: obj | null) : unit =
    match v with
    | null -> appendNull sb
    | :? string as s -> appendRawString sb s
    | :? bool as b -> appendBool sb b
    | :? int as n -> appendInt sb n
    | :? int64 as n -> appendInt64 sb n
    | :? float as f -> appendFloat sb f
    | :? float32 as f -> appendFloat sb (float f)
    | :? DateTimeOffset as t -> appendInt64 sb (t.ToUnixTimeSeconds())
    | :? DateTime as t -> appendInt64 sb (DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds())
    | _ -> appendRawString sb opaqueSentinel

// ─── Fuaran.UI.Types encoders ───────────────────────────────────────────────
let private encodeFileReadEncoding (e: FileReadEncoding) : Appender =
    fun sb ->
        match e with
        | FileReadEncoding.Text -> appendRawString sb "Text"
        | FileReadEncoding.Base64 -> appendRawString sb "Base64"
        | FileReadEncoding.DataUrl -> appendRawString sb "DataUrl"

let rec private encodeJVal (j: JVal) : Appender =
    fun sb ->
        match j with
        | JStr s -> appendRawString sb s
        | JInt i -> appendInt sb i
        | JBool b -> appendBool sb b
        | JFloat f -> appendFloat sb f
        | JArr xs -> appendArrayWith sb (xs |> List.map encodeJVal)
        | JObj fields -> appendObject sb (fields |> List.map (fun (k, v) -> k, encodeJVal v))

/// `UpdateProp`'s two-population payload: `Wire` renders its `JVal`
/// faithfully; `Native` keeps the legacy best-effort scalar encode (non-scalar
/// → `"<opaque>"` — in-process-only values are not wire-representable).
let private encodePropValue (v: PropValue) : Appender =
    fun sb ->
        match v with
        | PropValue.Native o -> appendObj sb o
        | PropValue.Wire j -> encodeJVal j sb

let private encodeDateStyle (s: DateStyle) : Appender =
    fun sb ->
        match s with
        | DateStyle.Short -> appendRawString sb "Short"
        | DateStyle.Medium -> appendRawString sb "Medium"
        | DateStyle.Long -> appendRawString sb "Long"
        | DateStyle.Full -> appendRawString sb "Full"

let private encodeRelativeTimeUnit (u: RelativeTimeUnit) : Appender =
    fun sb ->
        match u with
        | RelativeTimeUnit.Second -> appendRawString sb "Second"
        | RelativeTimeUnit.Minute -> appendRawString sb "Minute"
        | RelativeTimeUnit.Hour -> appendRawString sb "Hour"
        | RelativeTimeUnit.Day -> appendRawString sb "Day"
        | RelativeTimeUnit.Week -> appendRawString sb "Week"
        | RelativeTimeUnit.Month -> appendRawString sb "Month"
        | RelativeTimeUnit.Year -> appendRawString sb "Year"

// Phase 819 — Duration format enums; bare strings like every other variant
// DU in this file.
let private encodeDurationUnit (u: DurationUnit) : Appender =
    fun sb ->
        match u with
        | DurationUnit.Seconds -> appendRawString sb "Seconds"
        | DurationUnit.Minutes -> appendRawString sb "Minutes"
        | DurationUnit.Hours -> appendRawString sb "Hours"

let private encodeDurationStyle (s: DurationStyle) : Appender =
    fun sb ->
        match s with
        | DurationStyle.Compact -> appendRawString sb "Compact"
        | DurationStyle.Clock -> appendRawString sb "Clock"
        | DurationStyle.Long -> appendRawString sb "Long"

/// Locale-aware Format DU (Phase 102). Numeric Number / Percent cases omit
/// `decimals` when `None` (algorithm rule 4) so an unspecified-fraction payload
/// stays minimal; DateStyle / RelativeTimeUnit render as bare strings (matching
/// every other variant DU in this file).
let private encodeFormat (f: Format) : Appender =
    fun sb ->
        match f with
        | Format.Number decimals ->
            let fields =
                match decimals with
                | Some d -> [ "decimals", int_ d ]
                | None -> []

            appendObject sb (case "Number" fields)
        | Format.Currency isoCode -> appendObject sb (case "Currency" [ "isoCode", str isoCode ])
        | Format.Percent decimals ->
            let fields =
                match decimals with
                | Some d -> [ "decimals", int_ d ]
                | None -> []

            appendObject sb (case "Percent" fields)
        | Format.Date dateStyle -> appendObject sb (case "Date" [ "dateStyle", encodeDateStyle dateStyle ])
        | Format.RelativeTime unit -> appendObject sb (case "RelativeTime" [ "unit", encodeRelativeTimeUnit unit ])
        | Format.Duration(unit, style) ->
            // Phase 819 — alphabetical field order (style before unit), the
            // canonical ordering rule.
            appendObject sb (case "Duration" [ "style", encodeDurationStyle style; "unit", encodeDurationUnit unit ])

let private encodeLocaleSource (l: LocaleSource) : Appender =
    fun sb ->
        match l with
        | LocaleSource.Ambient -> appendObject sb (case "Ambient" [])
        | LocaleSource.Explicit tag -> appendObject sb (case "Explicit" [ "tag", str tag ])

let rec private isAbsentPayload<'T> (v: 'T) : bool = isNull (box v)

and private encodeBindingWith<'T> (staticEnc: 'T -> Appender) (b: Binding<'T>) : Appender =
    // Phase 429 — the typed-static-payload seam. `staticEnc` names the slot's
    // own encoding for the `'T` payload positions (`Static.value` and
    // `State.defaultValue`; `Local.InitialFrom` recurses with the same
    // encoder). Fable-safe by construction: the slot's call site supplies the
    // element encoding, so no erased runtime type inspection is needed (Fable
    // erases list element types — `:? (SelectOption list)` cannot dispatch in
    // the JS host). The mirror of the typed `decodeBinding*` decoders'
    // `parseStatic` parameter.
    fun sb ->
        match b with
        | Binding.Static v ->
            // Closures-as-Static aren't a thing — Static carries values.
            // Phase 677: an absent payload omits the key; it never emits null.
            // Since the swap absence is the OUTER `None` of the generated
            // `value: 'T option`; the inner `isAbsentPayload` check keeps a
            // legacy `Some null` / option-typed-slot `Some None` byte-identical.
            let valueField =
                match v with
                | Some p when not (isAbsentPayload p) -> [ "value", staticEnc p ]
                | _ -> []

            appendObject sb (case "Static" valueField)
        | Binding.Query(name, _accessor, dependsOn) ->
            // §4i — accessor is a wire-expression closure; canonical form renders the name only and
            // the accessor as a sentinel. Phase 421 — `dependsOn` (the declared filter dependency
            // edge) rides as a string array, omitted-when-empty/absent so the degenerate Query is
            // byte-stable. Ordinal order: `$type` < `accessor` < `dependsOn` < `name`.
            let dependsOnField =
                match dependsOn with
                | None
                | Some [] -> []
                | Some deps -> [ "dependsOn", (fun sb -> appendArrayWith sb (deps |> List.map str)) ]

            appendObject sb (case "Query" (dependsOnField @ [ "name", str name ]))
        | Binding.Filter(name, defaultValue) ->
            // 0.2.0 — `defaultValue` rides the wire when present (typed via the
            // slot's static encoder, mirroring `State.defaultValue`); omitted
            // when None so the minimal form stays byte-identical.
            let defaultField =
                match defaultValue with
                | Some d -> [ "defaultValue", staticEnc d ]
                | None -> []

            appendObject sb (case "Filter" (defaultField @ [ "name", str name ]))
        | Binding.Selection(nodeId, _accessor, defaultValue, field) ->
            // 0.2.9 (Phase 629) — `defaultValue` rides the wire when present,
            // exactly the `Filter.defaultValue` convention; omitted when None
            // so the pre-629 minimal form stays byte-identical.
            // 0.2.10 (Phase 632) — `field` (the declarative row-field
            // projection) rides the same convention; the accessor closure is
            // not serialisable, so the carried name is the encode source.
            let defaultField =
                match defaultValue with
                | Some d -> [ "defaultValue", staticEnc d ]
                | None -> []

            let fieldField =
                match field with
                | Some f -> [ "field", str f ]
                | None -> []

            appendObject sb (case "Selection" (defaultField @ fieldField @ [ "nodeId", str nodeId ]))
        | Binding.State(key, defaultValue) ->
            // Phase 677: same rule as `Static` — absence omits, never null
            // (outer `None` since the swap; inner absence still omits too).
            let defaultField =
                match defaultValue with
                | Some d when not (isAbsentPayload d) -> [ "defaultValue", staticEnc d ]
                | _ -> []

            appendObject sb (case "State" (defaultField @ [ "key", str key ]))
        | Binding.Computed _ -> appendObject sb (case "Computed" [ "fn", sentinel closureSentinel ])
        // Phase 765 — no wire fields: the instant is furnished by the host at
        // resolve time, never carried on the wire. Byte-identical to the
        // generated encoder's `Canon.typed "Now" []`.
        | Binding.Now _ -> appendObject sb (case "Now" [])
        | Binding.I18n(key, args) ->
            // i18n binding. Args are `Map<string, Binding<JVal>> option` (the
            // swap's typed verbatim carrier); each renders via the JVal-typed
            // binding encoder recursively. None args ⇒ omit the field; Some ⇒
            // encode an object map keyed by arg name. Field order matches
            // `TextSource.I18n` (args first, key second) per the existing
            // canonical convention.
            let argsField =
                args
                |> Option.map (fun (m: Map<string, Binding<JVal>>) ->
                    let fields =
                        m
                        |> Map.toList
                        |> List.map (fun (k, v) -> k, encodeBindingWith<JVal> (fun jv -> encodeJVal jv) v)

                    let argsAppender: Appender = fun sb -> appendObject sb fields
                    "args", argsAppender)
                |> Option.toList

            appendObject sb (case "I18n" (argsField @ [ "key", str key ]))
        | Binding.Local(flushOn, _format, initialFrom, onCommit, _parse) ->
            // Local binding (positional since the swap). `format` / `parse` are
            // closures encoded as `<closure>` sentinels unconditionally (both
            // slots are required); `onCommit` rides only when present — decode
            // always restores `Some`, so the corpus stays byte-identical.
            // `initialFrom` recurses through the same 'T; `flushOn` is its own DU.
            let flushAppender: Appender =
                fun sb ->
                    match flushOn with
                    | LocalFlushTrigger.OnBlur -> appendObject sb (case "OnBlur" [])
                    | LocalFlushTrigger.OnSubmit -> appendObject sb (case "OnSubmit" [])
                    | LocalFlushTrigger.OnCommitAction -> appendObject sb (case "OnCommitAction" [])
                    | LocalFlushTrigger.OnDebounce ms ->
                        appendObject
                            sb
                            (case "OnDebounce" [ "milliseconds", (fun sb -> sb.Append(string ms) |> ignore) ])

            let onCommitField =
                match onCommit with
                | Some _ -> [ "onCommit", sentinel closureSentinel ]
                | None -> []

            appendObject
                sb
                (case
                    "Local"
                    ([ "flushOn", flushAppender
                       "format", sentinel closureSentinel
                       "initialFrom", encodeBindingWith<'T> staticEnc initialFrom ]
                     @ onCommitField
                     @ [ "parse", sentinel closureSentinel ]))
        | Binding.Format(source, format, locale) ->
            // Locale-aware formatted value (Phase 102). `source`
            // is always `Binding<float>` (independent of 'T); `format` /
            // `locale` are bounded DUs. Field order is canonical
            // lexicographic (format < locale < source) per the appendObject
            // pre-sort.
            appendObject
                sb
                (case
                    "Format"
                    [ "format", encodeFormat format
                      "locale", encodeLocaleSource locale
                      "source", encodeBinding<float> source ])
        | Binding.Transform(source, pipeline, parameters) ->
            // Phase 282 — the Compute layer. `source` + `pipeline` are `Fuaran.Core` values whose
            // codecs now render under the SAME `Canon` `$type` discipline this host uses
            // (core-ui-spine-unification): `$type`-tagged, Ordinal-sorted keys, the pinned float
            // layout. So the Core-rendered JSON is already canonical and splices in raw — `$type`
            // (0x24) sorts before `params` < `pipeline` < `source`, so the composite stays canonical +
            // byte-stable. No mirror types, no second wire form.
            // Phase 424 — `params` binds `ColExpr.Param` names to scalar `Binding<obj>` sources;
            // omitted-when-empty so a param-free Transform is byte-identical to the Phase 282 wire.
            let paramField =
                match parameters with
                | None
                | Some [] -> []
                | Some ps ->
                    [ "params",
                      (fun sb ->
                          appendArrayWith
                              sb
                              (ps
                               |> List.map (fun (p: TransformParam) ->
                                   fun sb ->
                                       appendObject
                                           sb
                                           [ "from", encodeBindingWith<JVal> (fun jv -> encodeJVal jv) p.From
                                             "name", str p.Name ]))) ]

            // Phase 818 — a `Data` source splices Core's canonical columnar
            // rendering (byte-identical to pre-818); a `Live` source re-encodes
            // the preserved binding itself (one wire dialect — the derived
            // initial snapshot is never encoded).
            let sourceAppender: Appender =
                match source with
                | TransformSource.Data ds -> (fun sb -> sb.Append(Fuaran.Core.ColumnCodec.encode ds) |> ignore)
                | TransformSource.Live(b, _) -> encodeBindingWith<JVal> (fun jv -> encodeJVal jv) b

            appendObject
                sb
                (case
                    "Transform"
                    (paramField
                     @ [ "pipeline", (fun sb -> sb.Append(Fuaran.Core.DataFrameCodec.encodePipeline pipeline) |> ignore)
                         "source", sourceAppender ]))
        | Binding.Invoke(capabilityId, args) ->
            // Phase 283 — invoke a host-registered capability for a value. `args` are scalar
            // `InvokeArg` records since the swap (validated host-side against the capability's
            // signature); the body is never on the wire. Field order is canonical
            // (`$type` < `args` < `capabilityId`).
            appendObject
                sb
                (case
                    "Invoke"
                    [ "args", invokeArgsAppender (args |> List.map (fun (a: InvokeArg) -> a.Addr, a.Value))
                      "capabilityId", str capabilityId ])

and private encodeBinding<'T> (b: Binding<'T>) : Appender =
    // Default static encoding — the `appendObj` best-effort primitives +
    // `"<opaque>"` catch-all. Untyped slots stay byte-identical (GP 11); the
    // enumerated typed slots pass their own encoder via `encodeBindingWith`
    // (Phase 429).
    encodeBindingWith<'T> (fun v sb -> appendObj sb (box v)) b

and private encodeAction<'Msg> (a: Action<'Msg>) : Appender =
    fun sb ->
        match a with
        | Action.Dispatch _ ->
            // The 'Msg payload is opaque from the encoder's perspective;
            // render as a closure sentinel — see v1 limitation in migration doc.
            appendObject sb (case "Dispatch" ([]: Field list))
        | Action.Call(endpoint, onResult, into) ->
            // Phase 428: `onResult` rides the wire only when present (a `Some`
            // closure → the `"<closure>"` sentinel, byte-identical to before);
            // `into` is the declarative result target, a `$type`-discriminated
            // object omitted when `None`. Field order: endpoint < into < onResult.
            let optionals =
                [ onResult |> Option.map (fun _ -> "onResult", sentinel closureSentinel)
                  into
                  |> Option.map (fun target ->
                      "into",
                      (fun sb ->
                          match target with
                          | CallResultTarget.State key -> appendObject sb (case "State" [ "key", str key ])
                          | CallResultTarget.Query name -> appendObject sb (case "Query" [ "name", str name ]))) ]
                |> List.choose id

            appendObject sb (case "Call" ([ "endpoint", str endpoint ] @ optionals))
        | Action.Notify(channel, payload) ->
            appendObject sb (case "Notify" [ "channel", str channel; "payload", encodeJVal payload ])
        | Action.Navigate route -> appendObject sb (case "Navigate" [ "route", str route ])
        | Action.SetState(key, value, valueFrom) ->
            // Phase 818 — `value` XOR `valueFrom`; each rides only when present
            // (`appendObject` pre-sorts, so field order stays canonical).
            let optionals =
                [ value |> Option.map (fun v -> "value", encodeJVal v)
                  valueFrom
                  |> Option.map (fun b -> "valueFrom", encodeBindingWith<JVal> (fun jv -> encodeJVal jv) b) ]
                |> List.choose id

            appendObject sb (case "SetState" ([ "key", str key ] @ optionals))
        | Action.AiTool(name, args) -> appendObject sb (case "AiTool" [ "args", encodeJVal args; "toolName", str name ])
        | Action.Chain inner ->
            let items = inner |> List.map encodeAction
            appendObject sb (case "Chain" [ "ops", (fun sb -> appendArrayWith sb items) ])
        | Action.CommitLocal nodeId ->
            // Explicit-commit boundary for a Local-bound input.
            // Carries only the target NodeId; the renderer dispatches a
            // DOM custom event to drain the buffer.
            appendObject sb (case "CommitLocal" [ "nodeId", str nodeId ])
        | Action.WriteToClipboard text ->
            // Phase 1126 — the payload is a `TextSource`, encoded through the
            // GENERATED encoder rather than a second hand-rolled one. That
            // matters here specifically: `TextSource.Literal`'s canonical form
            // is the bare JSON string (§3.6), which is exactly the rule a
            // duplicate encoder would eventually get wrong, and this encoder
            // feeds the hash chain — a divergence would present as an
            // unexplained hash mismatch rather than as a wrong-looking
            // document. Every pre-1126 literal payload hashes identically.
            appendObject
                sb
                (case "WriteToClipboard" [ "text", encodeJVal (Fuaran.UI.Generated.encodeTextSourceJson text) ])
        | Action.Print ->
            // Phase 1124 — payload-free. `{"$type":"Print"}` is the whole
            // encoding, and the empty field list is not an omission: there is
            // no page size, margin, sheet range or target subtree to carry,
            // because the paged medium belongs to the host and the dialogue to
            // the reader. Same emitted shape as `Dispatch`, reached for the
            // opposite reason — that one has a payload the wire cannot see,
            // this one has none to see.
            appendObject sb (case "Print" ([]: Field list))
        | Action.ReadFileBody(fileRef, _fileHandle, encoding, onRead) ->
            // Phase 136 — file-read intent. Only the wire `fileRef` token
            // + the requested `encoding` cross the wire; the blob is host-held
            // (the host-only `fileHandle` slot since the swap, never encoded)
            // and `onRead` is unobservable (closure sentinel, §4; emitted when
            // present — decode always restores `Some`, so the wire is stable).
            // Fields sort to encoding < fileRef < onRead.
            let onReadField =
                match onRead with
                | Some _ -> [ "onRead", sentinel closureSentinel ]
                | None -> []

            appendObject
                sb
                (case
                    "ReadFileBody"
                    ([ "encoding", encodeFileReadEncoding encoding; "fileRef", str fileRef ]
                     @ onReadField))
        | Action.Invoke(capabilityId, args) ->
            // Phase 283 — invoke a host-registered capability as an effect. Same wire shape as
            // `Binding.Invoke`: scalar `InvokeArg` records, the body never on the wire.
            appendObject
                sb
                (case
                    "Invoke"
                    [ "args", invokeArgsAppender (args |> List.map (fun (a: InvokeArg) -> a.Addr, a.Value))
                      "capabilityId", str capabilityId ])

// ─── Typed Static payload encoders (Phase 429) ────────────────────────────
//
// The `appendObj` catch-all collapses non-primitive `Binding.Static` payloads
// to `"<opaque>"`. For the shapes the language itself enumerates — options,
// values, series, markers — the encoder emits the typed forms the
// `decodeBinding*` decoders already parse, supplied per slot through
// `encodeBindingWith`'s `staticEnc` parameter. Empty collections encode as
// their typed empty forms (`[]` / `null` for `None`), fixing the
// boxes-to-`null` asymmetry (`box ([] : 'a list)` is a null reference). A
// `Static` of a host domain type (`obj seq` grid/table rows, `PropValue.
// Native`) still falls through to the catch-all — the residual-opaque
// boundary is by design (WIRE_FORMAT.md §"Typed Static payloads").

// ─── Node / kind / envelope-record encoding — the GENERATED encoder ────────
//
// Phase 694: the hand-written per-kind mirror is DELETED. `Fuaran.UI.Generated`
// owns node / kind / state / style encoding (proven byte-identical over the
// wire corpus), and these appenders splice its canonical renderings into the
// op-codec stream. Adding a kind no longer touches this file.
//
// The §16 canonical-form projection (`Introspect.canonicalForm`) runs ABOVE
// the structural encoder — the policy half the hand-written encoder carried
// inline (an explicit auto-binding value, an all-default style, an all-None
// state each re-encode as the canonical ABSENCE). Identity on
// already-canonical trees, so decoded trees pay nothing.
//
// THE INVARIANT: every appender that splices a payload which can CONTAIN a
// `Node` routes through the projection. Three do — `nodeAppender`,
// `nodeKindAppender`, `stateBehaviourAppender` — and the third only since the
// cross-host fuzz exchange caught it bypassing them (see its own note below).
// The rest carry no `Node` and so have nothing to project: `SemanticStyle` is
// five bare enum fields (its omit-when-default is structural, owned by the
// generated encoder); `PropValue` is a scalar/`"<opaque>"` `Native` or an
// already-canonical `Wire` JVal; `Binding` and `Action` resolve to values and
// messages. Adding a Node-bearing payload to any of them means adding the
// projection in the same change.

let private nodeAppender<'Msg> (n: Node<'Msg>) : Appender =
    fun sb ->
        sb.Append(Fuaran.UI.Generated.encodeNode (Fuaran.UI.Ops.Introspect.canonicalForm n): string)
        |> ignore

let private nodeKindAppender<'Msg> (k: NodeKind<'Msg>) : Appender =
    fun sb ->
        sb.Append(
            Fuaran.Core.Canon.render (
                Fuaran.UI.Generated.encodeNodeKindJson (Fuaran.UI.Ops.Introspect.canonicalFormKind k)
            )
        )
        |> ignore

/// `StateBehaviour` carries `onLoading` / `onEmpty` **Node** payloads, so it
/// needs the §16 projection exactly as the two appenders above do — this one
/// spliced the raw record straight into the structural encoder, and a
/// `TreeOp.UpdateState` whose state node was (say) a `Filters` strip with an
/// explicit self-referential auto binding therefore emitted PRE-canonical
/// bytes on the one op path that skipped the projection. Found by the
/// cross-host fuzz exchange; pinned deterministically in
/// `Fuaran.UI.OpStream.Tests/CanonicalFormOpTests.fs`.
///
/// The projection maps over the two Node slots rather than routing a scratch
/// envelope through `canonicalForm` (the `canonicalFormKind` shape), because
/// `canonicalForm` collapses an all-`None` state to absence — correct for a
/// node's own `State` field, wrong for an op that explicitly SETS one, where
/// there is no absence to collapse to. `OnError` is `ErrorPayload -> Node`:
/// no node to project until it is applied. Nested state inside the spliced
/// nodes is reached by `canonicalForm`'s own recursion.
let private stateBehaviourAppender<'Msg> (s: StateBehaviour<'Msg>) : Appender =
    fun sb ->
        let canonical =
            { s with
                OnLoading = s.OnLoading |> Option.map Fuaran.UI.Ops.Introspect.canonicalForm
                OnEmpty = s.OnEmpty |> Option.map Fuaran.UI.Ops.Introspect.canonicalForm }

        sb.Append(Fuaran.Core.Canon.render (Fuaran.UI.Generated.encodeStateBehaviourJson canonical))
        |> ignore

let private semanticStyleAppender (s: SemanticStyle) : Appender =
    fun sb ->
        sb.Append(Fuaran.Core.Canon.render (Fuaran.UI.Generated.encodeSemanticStyleJson s))
        |> ignore

// ─── TreeOp encoder ───────────────────────────────────────────────────────

let private encodeTreeOp<'Msg> (op: TreeOp<'Msg>) : Appender =
    let rec walk (op: TreeOp<'Msg>) : Appender =
        fun sb ->
            match op with
            | TreeOp.EditNode(target, newKind) ->
                appendObject
                    sb
                    (case "EditNode" [ "newKind", nodeKindAppender newKind; "target", str (nodeIdStr target) ])
            | TreeOp.UpdateProp(target, path, value) ->
                appendObject
                    sb
                    (case
                        "UpdateProp"
                        [ "path", str path
                          "target", str (nodeIdStr target)
                          "value", encodePropValue value ])
            | TreeOp.ReplaceBinding(target, slot, binding) ->
                appendObject
                    sb
                    (case
                        "ReplaceBinding"
                        [ "binding", encodeBinding<obj> binding
                          "slot", str slot
                          "target", str (nodeIdStr target) ])
            | TreeOp.UpdateStyle(target, style) ->
                appendObject
                    sb
                    (case "UpdateStyle" [ "style", semanticStyleAppender style; "target", str (nodeIdStr target) ])
            | TreeOp.UpdateState(target, state) ->
                appendObject
                    sb
                    (case "UpdateState" [ "state", stateBehaviourAppender state; "target", str (nodeIdStr target) ])
            | TreeOp.InsertChild(parentId, child) ->
                appendObject
                    sb
                    (case "InsertChild" [ "child", nodeAppender child; "parentId", str (nodeIdStr parentId) ])
            | TreeOp.RemoveNode target -> appendObject sb (case "RemoveNode" [ "target", str (nodeIdStr target) ])
            | TreeOp.MoveNode(target, newParentId) ->
                appendObject
                    sb
                    (case "MoveNode" [ "newParentId", str (nodeIdStr newParentId); "target", str (nodeIdStr target) ])
            | TreeOp.ReorderChildren(parentId, newOrder) ->
                appendObject
                    sb
                    (case
                        "ReorderChildren"
                        [ "newOrder",
                          (fun sb -> appendArrayWith sb (newOrder |> List.map (fun id -> str (nodeIdStr id))))
                          "parentId", str (nodeIdStr parentId) ])
            | TreeOp.ReplaceRoot node -> appendObject sb (case "ReplaceRoot" [ "node", nodeAppender node ])
            | TreeOp.Batch ops ->
                appendObject sb (case "Batch" [ "ops", (fun sb -> appendArrayWith sb (ops |> List.map walk)) ])

    walk op

// ─── Public surface ───────────────────────────────────────────────────────

/// Encode a `Node<'Msg>` to its canonical JSON string. Fable-compatible —
/// no reflection, no `System.Security.*`. Closure-bearing payloads render
/// as the `"<closure>"` sentinel (v1 limitation, see migration doc).
///
/// Pairs with `ArgsJsonContract.validate` as the AI pre-emit self-check —
/// `encodeNode tree |> ArgsJsonContract.validate` catches wire-shape
/// violations cheaper than the apply-engine envelope.
let encodeNode<'Msg> (node: Node<'Msg>) : string =
    let sb = StringBuilder()
    nodeAppender node sb
    sb.ToString()

/// One place a tree would lose behaviour on the way to the wire (Phase 577):
/// the node carrying the closure, and which slot holds it.
///
/// `Slot` is spelled with `SlotCapability`'s `Type.slot` names
/// (`"Action.Dispatch.msg"`, `"Action.Call.onResult"`,
/// `"Action.ReadFileBody.onRead"`), so a report from here names the same slot
/// the survivability table, the validator's FUARAN112 and the §4 placeholder
/// enumeration name.
type LossyPath = { NodeId: string; Slot: string }

/// Encode a `Node<'Msg>` for TRANSPORT — the same canonical bytes as
/// `encodeNode`, but refusing a tree whose interaction would not survive the
/// round trip.
///
/// **Why this is a second function rather than a flag on the first.**
/// `encodeNode` feeds the hash chain, and its closure-blindness there is
/// DELIBERATE: two ops differing only in an opaque `'Msg` payload hash
/// identically, by design, because the chain records the shape of a change and
/// not the identity of the host code that requested it. Making that path refuse
/// would break the property it exists to have. What was missing was a path
/// where the author's INTENT is known — and calling a function named for
/// transport is that intent, stated at the one moment it can be.
///
/// Refuses on the `Action` DU's three closure slots — `Dispatch`'s `msg`,
/// `Call`'s `onResult`, `ReadFileBody`'s `onRead` — each of which decodes to an
/// inert placeholder, so the affordance arrives able to fire and unable to do
/// anything. Every offending path is returned, not the first: an author repairs
/// a tree in one pass or discovers its defects one turn at a time.
///
/// **The loss leaves no trace in the bytes**, which is why it has to be caught
/// here. `encodeNode` emits the discriminator and DROPS the payload — a
/// `Dispatch` becomes `{"$type":"Dispatch"}` — and `"<closure>"` is the
/// DECODER's reconstruction. A downstream reader of the emission cannot tell a
/// `Dispatch` that lost a message from one that never carried a payload, so the
/// encoding side is the last place the question is answerable at all.
///
/// **What it does NOT claim.** Other slots erase too (a `FormFieldKind.onChange`,
/// a `TabsSpec.onSelect`) and are not refused, because the renderers'
/// write-back default reconstructs their behaviour from the control's own
/// writable binding — the closure is lost and the interaction is not.
/// `Binding.Computed` is likewise untouched here; it is FUARAN084's subject.
/// A tree this function accepts carries no *unrecoverable* interaction, which
/// is a narrower claim than "nothing about it erased".
///
/// The wire-representable way to reach a host is `Action.Notify` or
/// `Action.Call` with `into:`; typed dispatch is obtained by binding handlers
/// to the artifact's declared action holes, host-side.
let encodeNodeForTransport<'Msg> (node: Node<'Msg>) : Result<string, LossyPath list> =
    let facts = Fuaran.UI.BindingWalk.collect node

    // Deduplicated on (node, slot) so a `Chain` of two `Dispatch`es is one
    // repair rather than two reports of it. Ordered by the walk, which is
    // depth-first pre-order, so the paths read in tree order.
    let seen = System.Collections.Generic.HashSet<string>()

    let lossy =
        facts.Closures
        |> List.filter (fun c -> seen.Add(c.Reader + " " + c.Slot))
        |> List.map (fun c -> { NodeId = c.Reader; Slot = c.Slot })

    match lossy with
    | [] -> Ok(encodeNode node)
    | paths -> Error paths

/// Encode a `TreeOp<'Msg>` to its canonical JSON string. Server-side hash
/// chain calls this to feed SHA-256; the encoded form is also useful for
/// op-stream sinks that persist as JSON text (Sqlite `op_json` column).
let encodeOp<'Msg> (op: TreeOp<'Msg>) : string =
    let sb = StringBuilder()
    encodeTreeOp op sb
    sb.ToString()

/// Encode an `OpResultEnvelope` to its canonical JSON string. Used by the
/// Sqlite sink for the `result_envelope_json` column.
let encodeResultEnvelopeShape (status: string) (errorCode: string option) (errorMessage: string option) : string =
    let sb = StringBuilder()

    let fields =
        ("status", str status)
        :: ([ errorCode |> Option.map (fun c -> "errorCode", str c)
              errorMessage |> Option.map (fun m -> "errorMessage", str m) ]
            |> List.choose id)

    appendObject sb fields
    sb.ToString()
