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

/// Hoists a spec's fields directly under the kind's `$type` discriminator — the
/// flat wire carries no `spec` wrapper (WIRE_FORMAT.md §3.2). `specAppender`
/// emits the spec as a `{…}` object with its keys already Ordinal-sorted; we
/// splice `"$type":"<disc>"` in front of those fields. This is byte-correct
/// because `$` (0x24) sorts before every lowercase spec field name, so `$type`
/// is always the canonical first key — the spliced result stays sorted.
let private hoistSpec (disc: string) (specAppender: Appender) : Appender =
    fun sb ->
        let inner = StringBuilder()
        specAppender inner
        let innerStr = inner.ToString()
        sb.Append '{' |> ignore
        appendRawString sb "$type"
        sb.Append ':' |> ignore
        appendRawString sb disc
        // innerStr is "{…}" (or "{}" for a field-less spec); splice its fields
        // in after $type when present.
        if innerStr.Length > 2 then
            sb.Append ',' |> ignore
            sb.Append(innerStr.Substring(1, innerStr.Length - 2)) |> ignore

        sb.Append '}' |> ignore

let private sentinel (s: string) : Appender = fun sb -> appendRawString sb s

let private str (s: string) : Appender = fun sb -> appendRawString sb s

let private int_ (n: int) : Appender = fun sb -> appendInt sb n

let private float_ (n: float) : Appender = fun sb -> appendFloat sb n

let private bool_ (b: bool) : Appender = fun sb -> appendBool sb b

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

let private encodeOrientation (o: Orientation) : Appender =
    fun sb ->
        match o with
        | Vertical -> appendRawString sb "Vertical"
        | Horizontal -> appendRawString sb "Horizontal"

let private encodeFileReadEncoding (e: FileReadEncoding) : Appender =
    fun sb ->
        match e with
        | FileReadEncoding.Text -> appendRawString sb "Text"
        | FileReadEncoding.Base64 -> appendRawString sb "Base64"
        | FileReadEncoding.DataUrl -> appendRawString sb "DataUrl"

let private encodeBadgeVariant (v: BadgeVariant) : Appender =
    fun sb ->
        match v with
        | BadgeVariant.Neutral -> appendRawString sb "Neutral"
        | BadgeVariant.Brand -> appendRawString sb "Brand"
        | BadgeVariant.Success -> appendRawString sb "Success"
        | BadgeVariant.Warning -> appendRawString sb "Warning"
        | BadgeVariant.Critical -> appendRawString sb "Critical"
        | BadgeVariant.Info -> appendRawString sb "Info"

let private encodeButtonVariant (v: ButtonVariant) : Appender =
    fun sb ->
        match v with
        | ButtonVariant.Primary -> appendRawString sb "Primary"
        | ButtonVariant.Secondary -> appendRawString sb "Secondary"
        | ButtonVariant.Tertiary -> appendRawString sb "Tertiary"
        | ButtonVariant.Destructive -> appendRawString sb "Destructive"

let private encodeImageVariant (v: ImageVariant) : Appender =
    fun sb ->
        match v with
        | ImageVariant.Default -> appendRawString sb "Default"
        | ImageVariant.Avatar -> appendRawString sb "Avatar"
        | ImageVariant.Rounded -> appendRawString sb "Rounded"

let private encodeScrollOrientation (o: ScrollOrientation) : Appender =
    fun sb ->
        match o with
        | ScrollOrientation.Vertical -> appendRawString sb "Vertical"
        | ScrollOrientation.Horizontal -> appendRawString sb "Horizontal"
        | ScrollOrientation.Both -> appendRawString sb "Both"

let private encodeDateVariant (v: DateVariant) : Appender =
    fun sb ->
        match v with
        | DateVariant.Date -> appendRawString sb "Date"
        | DateVariant.Time -> appendRawString sb "Time"
        | DateVariant.DateTime -> appendRawString sb "DateTime"

let private encodeMathDisplay (d: MathDisplay) : Appender =
    fun sb ->
        match d with
        | MathDisplay.Inline -> appendRawString sb "Inline"
        | MathDisplay.Block -> appendRawString sb "Block"

let private encodeHeadingVariant (v: HeadingVariant) : Appender =
    // HeadingVariant DU encoded as a bare string in the
    // canonical-JSON output (matches the encoding of every other variant DU
    // in this file — Orientation, ToneVariant, etc.).
    fun sb ->
        match v with
        | HeadingVariant.Standard -> appendRawString sb "Standard"
        | HeadingVariant.Eyebrow -> appendRawString sb "Eyebrow"
        | HeadingVariant.Caption -> appendRawString sb "Caption"
        | HeadingVariant.Lead -> appendRawString sb "Lead"

let private encodeTone (t: ToneVariant) : Appender =
    fun sb ->
        match t with
        | Default -> appendRawString sb "Default"
        | Subdued -> appendRawString sb "Subdued"
        | Brand -> appendRawString sb "Brand"
        | Success -> appendRawString sb "Success"
        | Warning -> appendRawString sb "Warning"
        | Critical -> appendRawString sb "Critical"
        | Info -> appendRawString sb "Info"

let private encodeWeight (w: StyleWeight) : Appender =
    fun sb ->
        match w with
        | Compact -> appendRawString sb "Compact"
        | Standard -> appendRawString sb "Standard"
        | Spacious -> appendRawString sb "Spacious"

let private encodeEmphasis (e: Emphasis) : Appender =
    fun sb ->
        match e with
        | Quiet -> appendRawString sb "Quiet"
        | Normal -> appendRawString sb "Normal"
        | Loud -> appendRawString sb "Loud"

// Phase 147 — the additive style-role / font-voice DUs.  PascalCase-matched
// to the F# cases (same discipline as `encodeTone`).  Emitted on the style
// object only when non-default (see `semanticStyleAppender`), so existing
// fixtures round-trip byte-identically.
let private encodeStyleRole (r: StyleRole) : Appender =
    fun sb ->
        match r with
        | StyleRole.None -> appendRawString sb "None"
        | StyleRole.Eyebrow -> appendRawString sb "Eyebrow"
        | StyleRole.Data -> appendRawString sb "Data"
        | StyleRole.Lede -> appendRawString sb "Lede"
        | StyleRole.Caption -> appendRawString sb "Caption"

let private encodeFontVoice (v: FontVoice) : Appender =
    fun sb ->
        match v with
        | FontVoice.Default -> appendRawString sb "Default"
        | FontVoice.Display -> appendRawString sb "Display"
        | FontVoice.Structural -> appendRawString sb "Structural"

let private encodeColumnWidth (w: ColumnWidth) : Appender =
    fun sb ->
        match w with
        | ColumnWidth.Auto -> appendObject sb (case "Auto" [])
        | ColumnWidth.Fixed pixels -> appendObject sb (case "Fixed" [ "pixels", int_ pixels ])
        | ColumnWidth.Flex weight -> appendObject sb (case "Flex" [ "weight", float_ weight ])

let private encodeErrorKind (k: ErrorKind) : Appender =
    fun sb ->
        match k with
        | NotFound -> appendRawString sb "NotFound"
        | Forbidden -> appendRawString sb "Forbidden"
        | Server -> appendRawString sb "Server"
        | Network -> appendRawString sb "Network"
        | Timeout -> appendRawString sb "Timeout"
        | BindingResolution -> appendRawString sb "BindingResolution"

let private encodeIconSource (IconSource raw) : Appender = str raw

/// Faithful canonical appender for the structured `JVal` payload positions
/// (Custom props, action payloads, I18n args, `PropValue.Wire` values): keys
/// Ordinal-sorted recursively via `appendObject`, the pinned float layout,
/// canonical escaping. Nested objects / arrays round-trip byte-identically —
/// the `"<opaque>"` collapse is now exclusive to the genuinely obj-erased
/// seams (`PropValue.Native`, `Binding<obj>` statics).
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

let private encodeMap (m: Map<string, JVal>) : Appender =
    fun sb ->
        let fields = m |> Map.toList |> List.map (fun (k, v) -> k, encodeJVal v)

        appendObject sb fields

let private encodeChartKind (k: ChartKind) : Appender =
    fun sb ->
        match k with
        | ChartKind.Line -> appendRawString sb "Line"
        | ChartKind.Bar -> appendRawString sb "Bar"
        | ChartKind.Area -> appendRawString sb "Area"
        | ChartKind.Pie -> appendRawString sb "Pie"
        | ChartKind.Scatter -> appendRawString sb "Scatter"
        | ChartKind.Heatmap -> appendRawString sb "Heatmap"

let private encodeCellFormat (f: CellFormat) : Appender =
    fun sb ->
        match f with
        | CellFormat.None -> appendObject sb (case "None" [])
        | CellFormat.Number decimals ->
            let fields =
                match decimals with
                | Some d -> [ "decimals", int_ d ]
                | None -> []

            appendObject sb (case "Number" fields)
        | CellFormat.Currency code -> appendObject sb (case "Currency" [ "code", str code ])
        | CellFormat.Percent decimals ->
            let fields =
                match decimals with
                | Some d -> [ "decimals", int_ d ]
                | None -> []

            appendObject sb (case "Percent" fields)
        | CellFormat.SignificantDigits d -> appendObject sb (case "SignificantDigits" [ "digits", int_ d ])
        | CellFormat.Date fmt -> appendObject sb (case "Date" [ "format", str fmt ])
        | CellFormat.Custom _ -> appendObject sb (case "Custom" [ "fn", sentinel closureSentinel ])

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

let private encodeLocaleSource (l: LocaleSource) : Appender =
    fun sb ->
        match l with
        | LocaleSource.Ambient -> appendObject sb (case "Ambient" [])
        | LocaleSource.Explicit tag -> appendObject sb (case "Explicit" [ "tag", str tag ])

let private encodeCellValue (v: CellValue) : Appender =
    fun sb ->
        match v with
        | CellValue.Numeric f -> appendObject sb (case "Numeric" [ "value", float_ f ])
        | CellValue.Text s -> appendObject sb (case "Text" [ "value", str s ])
        | CellValue.Bool b -> appendObject sb (case "Bool" [ "value", bool_ b ])
        | CellValue.Date t ->
            appendObject sb (case "Date" [ "unixSeconds", (fun sb -> appendInt64 sb (t.ToUnixTimeSeconds())) ])
        | CellValue.Empty -> appendObject sb (case "Empty" [])

// ─── TextSource / Binding / Action (mutually recursive) ───────────────────
//
// Bindings carry obj accessors (Defect (2) resolution in Types.fs); they
// render as `"<closure>"` sentinels. TextSource references Binding<string>,
// Binding<_> references nothing here, Action references JVal + 'Msg
// (sentinel for the dispatched value).

let rec private encodeTextSource (t: TextSource) : Appender =
    fun sb ->
        match t with
        // 0.2.0 — the bare string IS the canonical Literal encoding (6.6% of
        // emission chars were envelope ceremony; the decoder has accepted the
        // bare form since §16.1 and the envelope stays decode-compatible).
        | TextSource.Literal raw -> (str raw) sb
        | TextSource.Bound b -> appendObject sb (case "Bound" [ "binding", encodeBinding<string> b ])
        | TextSource.I18n(key, args) -> appendObject sb (case "I18n" [ "args", encodeMap args; "key", str key ])

/// Phase 677 — absence is STRUCTURAL, never a value. A payload that is absent
/// (an obj-erased `null`, or a `None` in an option-typed slot such as
/// `SelectSpec.Value : Binding<string option>`) omits its key entirely rather
/// than emitting JSON `null`, for which the wire model has no case.
///
/// `isNull (box v)` is uniform across both: an F# `option` is a reference type
/// whose `None` is represented as null, and Fable's `== null` catches its
/// `undefined`. A real empty collection (`[]`, `Seq.empty`) is a live object and
/// is NOT absent — "no selection" and "selected nothing" stay distinguishable.
and private isAbsentPayload<'T> (v: 'T) : bool = isNull (box v)

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
            let valueField = if isAbsentPayload v then [] else [ "value", staticEnc v ]

            appendObject sb (case "Static" valueField)
        | Binding.Query(name, _accessor, dependsOn) ->
            // §4i — accessor is a wire-expression closure; canonical form renders the name only and
            // the accessor as a sentinel. Phase 421 — `dependsOn` (the declared filter dependency
            // edge) rides as a string array, omitted-when-empty so the degenerate Query is byte-stable.
            // Ordinal order: `$type` < `accessor` < `dependsOn` < `name`.
            let dependsOnField =
                if List.isEmpty dependsOn then
                    []
                else
                    [ "dependsOn", (fun sb -> appendArrayWith sb (dependsOn |> List.map str)) ]

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

            appendObject sb (case "Selection" (defaultField @ fieldField @ [ "nodeId", str (nodeIdStr nodeId) ]))
        | Binding.State(key, defaultValue) ->
            // Phase 677: same rule as `Static` — absence omits, never null.
            let defaultField =
                if isAbsentPayload defaultValue then
                    []
                else
                    [ "defaultValue", staticEnc defaultValue ]

            appendObject sb (case "State" (defaultField @ [ "key", str key ]))
        | Binding.Computed _ -> appendObject sb (case "Computed" [ "fn", sentinel closureSentinel ])
        | Binding.I18n(key, args) ->
            // i18n binding. Args are `Map<string, Binding<obj>>
            // option`; each `Binding<obj>` renders via `encodeBinding<obj>`
            // recursively. None args ⇒ omit the field; Some ⇒ encode an
            // object map keyed by arg name. Field order matches `TextSource.I18n`
            // (args first, key second) per the existing canonical convention.
            let argsField =
                args
                |> Option.map (fun (m: Map<string, Binding<obj>>) ->
                    let fields = m |> Map.toList |> List.map (fun (k, v) -> k, encodeBinding<obj> v)

                    let argsAppender: Appender = fun sb -> appendObject sb fields
                    "args", argsAppender)
                |> Option.toList

            appendObject sb (case "I18n" (argsField @ [ "key", str key ]))
        | Binding.Local local ->
            // Local binding. `OnCommit` / `Format` / `Parse` are
            // closures; encode as `<closure>` sentinels. `InitialFrom`
            // recurses through encodeBinding for the same 'T; `FlushOn`
            // is its own DU. Field order matches the established lexicographic
            // shape: flushOn, initialFrom, then the three closure sentinels.
            let flushAppender: Appender =
                fun sb ->
                    match local.FlushOn with
                    | LocalFlushTrigger.OnBlur -> appendObject sb (case "OnBlur" [])
                    | LocalFlushTrigger.OnSubmit -> appendObject sb (case "OnSubmit" [])
                    | LocalFlushTrigger.OnCommitAction -> appendObject sb (case "OnCommitAction" [])
                    | LocalFlushTrigger.OnDebounce ms ->
                        appendObject
                            sb
                            (case "OnDebounce" [ "milliseconds", (fun sb -> sb.Append(string ms) |> ignore) ])

            appendObject
                sb
                (case
                    "Local"
                    [ "flushOn", flushAppender
                      "format", sentinel closureSentinel
                      "initialFrom", encodeBindingWith<'T> staticEnc local.InitialFrom
                      "onCommit", sentinel closureSentinel
                      "parse", sentinel closureSentinel ])
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
                if List.isEmpty parameters then
                    []
                else
                    [ "params",
                      (fun sb ->
                          appendArrayWith
                              sb
                              (parameters
                               |> List.map (fun (name, fromB) ->
                                   fun sb -> appendObject sb [ "from", encodeBinding<obj> fromB; "name", str name ]))) ]

            appendObject
                sb
                (case
                    "Transform"
                    (paramField
                     @ [ "pipeline", (fun sb -> sb.Append(Fuaran.Core.DataFrameCodec.encodePipeline pipeline) |> ignore)
                         "source", (fun sb -> sb.Append(Fuaran.Core.ColumnCodec.encode source) |> ignore) ]))
        | Binding.Invoke(capabilityId, args) ->
            // Phase 283 — invoke a host-registered capability for a value. `args` are scalar
            // `(addr, value)` pairs (validated host-side against the capability's signature); the
            // body is never on the wire. Field order is canonical (`$type` < `args` < `capabilityId`).
            appendObject sb (case "Invoke" [ "args", invokeArgsAppender args; "capabilityId", str capabilityId ])

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
        | Action.Call(ApiEndpoint endpoint, onResult, into) ->
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
                          | CallResultTarget.IntoState key -> appendObject sb (case "State" [ "key", str key ])
                          | CallResultTarget.IntoQuery name -> appendObject sb (case "Query" [ "name", str name ]))) ]
                |> List.choose id

            appendObject sb (case "Call" ([ "endpoint", str endpoint ] @ optionals))
        | Action.Notify(channel, payload) ->
            appendObject sb (case "Notify" [ "channel", str channel; "payload", encodeJVal payload ])
        | Action.Navigate route -> appendObject sb (case "Navigate" [ "route", str route ])
        | Action.SetState(key, value) -> appendObject sb (case "SetState" [ "key", str key; "value", encodeJVal value ])
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
            // Literal-string clipboard intent. Renderer routes
            // through `IFuaranRuntime.WriteToClipboard`.
            appendObject sb (case "WriteToClipboard" [ "text", str text ])
        | Action.ReadFileBody(file, encoding, _onRead) ->
            // Phase 136 — file-read intent. Only the opaque `file.Id` token
            // + the requested `encoding` cross the wire; the blob is browser-
            // held (`file.Handle`) and `onRead` is unobservable (closure
            // sentinel, §4). Fields sort to encoding < fileRef < onRead.
            appendObject
                sb
                (case
                    "ReadFileBody"
                    [ "encoding", encodeFileReadEncoding encoding
                      "fileRef", str file.Id
                      "onRead", sentinel closureSentinel ])
        | Action.Invoke(capabilityId, args) ->
            // Phase 283 — invoke a host-registered capability as an effect. Same wire shape as
            // `Binding.Invoke`: scalar `(addr, value)` args, the body never on the wire.
            appendObject sb (case "Invoke" [ "args", invokeArgsAppender args; "capabilityId", str capabilityId ])

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

let private encodeSelectOption (o: SelectOption) : Appender =
    fun sb -> appendObject sb [ "label", encodeTextSource o.Label; "value", str o.Value ]

let private staticSelectOptions (options: SelectOption list) : Appender =
    fun sb -> appendArrayWith sb (options |> List.map encodeSelectOption)

let private staticStringOpt (v: string option) : Appender =
    fun sb ->
        match v with
        | Some s -> appendRawString sb s
        // Phase 677: unreachable — an absent payload omits its key upstream in
        // `encodeBindingWith`, so this branch never renders. Kept total, and
        // deliberately NOT emitting null: the wire model has no such value.
        | None -> appendRawString sb ""

let private staticStringList (xs: string list) : Appender =
    fun sb -> appendArrayWith sb (xs |> List.map str)

let private staticFloatSeq (xs: float seq) : Appender =
    fun sb -> appendArrayWith sb (xs |> Seq.map float_ |> List.ofSeq)

let private encodeMapMarker (m: MapMarker) : Appender =
    fun sb ->
        appendObject
            sb
            [ "label", encodeTextSource m.Label
              "latitude", float_ m.Latitude
              "longitude", float_ m.Longitude ]

let private staticMarkerSeq (ms: MapMarker seq) : Appender =
    fun sb -> appendArrayWith sb (ms |> Seq.map encodeMapMarker |> List.ofSeq)

// ─── Spec records ─────────────────────────────────────────────────────────

// Phase 460 — the stylistic fields are emitted only when non-default (identity:
// `CellFormat.None` / `ColumnWidth.Auto` / `ToneVariant.Default` /
// `StyleWeight.Standard` / `Emphasis.Normal`), per the `semanticStyleAppender`
// role/voice precedent, so a default-styled tree round-trips minimal. `CellFormat`
// carries a function payload (`Custom`) so it has no structural equality — match the
// `None` case rather than compare.
let private cellFormatOptional (key: string) (f: CellFormat) : (string * Appender) option =
    match f with
    | CellFormat.None -> Option.None
    | fmt -> Some(key, encodeCellFormat fmt)

let private columnWidthOptional (key: string) (w: ColumnWidth) : (string * Appender) option =
    match w with
    | ColumnWidth.Auto -> Option.None
    | width -> Some(key, encodeColumnWidth width)

let private toneOptional (t: ToneVariant) : (string * Appender) option =
    if t <> ToneVariant.Default then
        Some("tone", encodeTone t)
    else
        Option.None

let private weightOptional (w: StyleWeight) : (string * Appender) option =
    if w <> StyleWeight.Standard then
        Some("weight", encodeWeight w)
    else
        Option.None

let private emphasisOptional (e: Emphasis) : (string * Appender) option =
    if e <> Emphasis.Normal then
        Some("emphasis", encodeEmphasis e)
    else
        Option.None

let private encodeMetricSpec (s: MetricSpec) : Appender =
    fun sb ->
        let fields =
            [ "label", encodeTextSource s.Label; "value", encodeBinding<float> s.Value ]

        let optionals =
            [ cellFormatOptional "format" s.Format
              toneOptional s.Tone
              weightOptional s.Weight
              emphasisOptional s.Emphasis
              s.Trend |> Option.map (fun b -> "trend", encodeBinding<float> b)
              s.TrendFormat |> Option.map (fun f -> "trendFormat", encodeCellFormat f)
              s.Icon |> Option.map (fun i -> "icon", encodeIconSource i)
              s.Subtext |> Option.map (fun t -> "subtext", encodeTextSource t) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeHeadingSpec (s: HeadingSpec) : Appender =
    // Include Variant. Field order is sorted-by-name
    // ("level" / "text" / "variant") per the canonical-JSON convention used
    // elsewhere in this file.
    fun sb ->
        appendObject
            sb
            [ "level", int_ s.Level
              "text", encodeTextSource s.Text
              "variant", encodeHeadingVariant s.Variant ]

let private encodeLabelValueRowSpec (s: LabelValueRowSpec) : Appender =
    // Optional `Help` follows the existing pattern of
    // appending optionals after the required fields (see encodeMetricSpec).
    // Phase 460 — `format` omitted-when-default; the `emphasis` bool is behavioural
    // (not the style DU) and stays required.
    fun sb ->
        let fields =
            [ "label", encodeTextSource s.Label; "value", encodeBinding<float> s.Value ]

        let optionals =
            [ // 0.2.2 — omitted-when-false, aligning with Fact's identical flag.
              (if s.Emphasis then Some("emphasis", bool_ true) else None)
              cellFormatOptional "format" s.Format
              s.Help |> Option.map (fun t -> "help", encodeTextSource t) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeFactSpec (s: FactSpec) : Appender =
    // A labeled TEXT fact. `emphasis` is the behavioural bool (LVR precedent,
    // required); `tone` is omitted-when-default (Phase 460 posture); `help` /
    // `icon` are optional. `value` is a TextSource — the same encoder the
    // labels use, so Bound / I18n values ride the existing vocabulary.
    fun sb ->
        let fields =
            [ "label", encodeTextSource s.Label; "value", encodeTextSource s.Value ]

        let optionals =
            [ (if s.Emphasis then Some("emphasis", bool_ true) else None)
              toneOptional s.Tone
              s.Help |> Option.map (fun t -> "help", encodeTextSource t)
              s.Icon |> Option.map (fun i -> "icon", encodeIconSource i) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeMarkdownSpec (s: MarkdownSpec) : Appender =
    fun sb -> appendObject sb [ "text", encodeTextSource s.Text ]

let private encodeBadgeSpec (s: BadgeSpec) : Appender =
    fun sb -> appendObject sb [ "label", encodeTextSource s.Label; "variant", encodeBadgeVariant s.Variant ]

let private encodeLinkSpec (s: LinkSpec) : Appender =
    fun sb ->
        let fields =
            [ "href", encodeBinding<string> s.Href
              "label", encodeTextSource s.Label
              "download", bool_ s.Download ]

        let optionals =
            [ s.Rel |> Option.map (fun r -> "rel", str r)
              s.Target |> Option.map (fun t -> "target", str t) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeImageSpec (s: ImageSpec) : Appender =
    fun sb ->
        appendObject
            sb
            [ "alt", encodeTextSource s.Alt
              "src", encodeBinding<string> s.Src
              "variant", encodeImageVariant s.Variant ]

let private encodeListSpec (s: ListSpec) : Appender =
    fun sb ->
        appendObject
            sb
            [ "items", (fun sb -> appendArrayWith sb (s.Items |> List.map encodeTextSource))
              "ordered", bool_ s.Ordered ]

let private encodeToastSpec (s: ToastSpec) : Appender =
    // Phase 460 — `tone` omitted-when-default (`ToneVariant.Default`).
    fun sb ->
        let fields =
            [ // 0.2.0 — omitted-when-TRUE (a toast defaults dismissable).
              if not s.Dismissable then
                  yield "dismissable", bool_ false
              yield "message", encodeTextSource s.Message
              yield "open", encodeBinding<bool> s.Open ]

        let optionals = [ toneOptional s.Tone ] |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeCodeBlockSpec (s: CodeBlockSpec) : Appender =
    fun sb ->
        appendObject
            sb
            [ "code", str s.Code
              "copyable", bool_ s.Copyable
              "highlightLines", (fun sb -> appendArrayWith sb (s.HighlightLines |> List.map int_))
              "language", str s.Language
              "lineNumbers", bool_ s.LineNumbers ]

let private encodeMathSpec (s: MathSpec) : Appender =
    fun sb -> appendObject sb [ "display", encodeMathDisplay s.Display; "source", str s.Source ]

let private encodeSparklineSpec (s: SparklineSpec) : Appender =
    fun sb -> appendObject sb [ "source", encodeBindingWith staticFloatSeq s.Source ]

let private encodeSkeletonSpec (s: SkeletonSpec) : Appender =
    fun sb -> appendObject sb [ "rows", int_ s.Rows ]

let private encodeCalloutSpec (s: CalloutSpec) : Appender =
    fun sb ->
        let fields = [ "body", encodeTextSource s.Body ]

        let optionals =
            // 0.2.0 — `dismissable` omitted-when-false; Phase 460 — `tone`
            // omitted-when-default (`ToneVariant.Default`).
            [ (if s.Dismissable then
                   Some("dismissable", bool_ true)
               else
                   None)
              toneOptional s.Tone
              s.Heading |> Option.map (fun t -> "heading", encodeTextSource t)
              s.Icon |> Option.map (fun i -> "icon", encodeIconSource i) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeProgressSpec (s: ProgressSpec) : Appender =
    fun sb ->
        let fields =
            [ yield "fraction", encodeBinding<float> s.Fraction
              // 0.2.0 — omitted-when-false (100% of observed usage is false).
              if s.Indeterminate then
                  yield "indeterminate", bool_ true ]

        let optionals =
            // Phase 460 — `tone` omitted-when-default (`ToneVariant.Default`).
            [ toneOptional s.Tone
              s.Label |> Option.map (fun t -> "label", encodeTextSource t)
              s.Caveat |> Option.map (fun t -> "caveat", encodeTextSource t) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

// ─── Drawing (Phase 524) ────────────────────────────────────────────────────

let private encodeDrawPoint (p: DrawPoint) : Appender =
    fun sb -> appendObject sb [ "x", float_ p.X; "y", float_ p.Y ]

let private encodeViewBox (v: ViewBox) : Appender =
    fun sb ->
        appendObject
            sb
            [ "height", float_ v.Height
              "minX", float_ v.MinX
              "minY", float_ v.MinY
              "width", float_ v.Width ]

/// Fill/stroke bindings, each omitted when `None` (rule 4). Always an object
/// (`{}` when all default) so the `style` slot is a stable shape per shape.
let private encodeTextAnchor (a: TextAnchor) : Appender =
    fun sb ->
        match a with
        | TextAnchor.Start -> appendRawString sb "Start"
        | TextAnchor.Middle -> appendRawString sb "Middle"
        | TextAnchor.End -> appendRawString sb "End"

let private encodeDrawStyle (s: DrawStyle) : Appender =
    fun sb ->
        let fields =
            [ s.Fill |> Option.map (fun b -> "fill", encodeBinding<string> b)
              s.Stroke |> Option.map (fun b -> "stroke", encodeBinding<string> b)
              s.StrokeWidth |> Option.map (fun b -> "strokeWidth", encodeBinding<float> b)
              s.Opacity |> Option.map (fun b -> "opacity", encodeBinding<float> b)
              // Text-only fields (Phase 528.1) — omitted when None (byte-unchanged
              // for shapes that don't set them).
              s.TextAnchor |> Option.map (fun a -> "textAnchor", encodeTextAnchor a)
              s.FontSize |> Option.map (fun n -> "fontSize", float_ n)
              s.Emphasis |> Option.map (fun e -> "emphasis", encodeEmphasis e)
              s.FontFamily |> Option.map (fun f -> "fontFamily", str f)
              // Phase 642 — keyed mark identity; omitted when None.
              s.MarkId |> Option.map (fun m -> "markId", str m) ]
            |> List.choose id

        appendObject sb fields

let private encodeCurveCommand (c: CurveCommand) : Appender =
    fun sb ->
        match c with
        | CurveCommand.MoveTo p -> appendObject sb (case "MoveTo" [ "to", encodeDrawPoint p ])
        | CurveCommand.LineTo p -> appendObject sb (case "LineTo" [ "to", encodeDrawPoint p ])
        | CurveCommand.CubicTo(c1, c2, e) ->
            appendObject
                sb
                (case
                    "CubicTo"
                    [ "control1", encodeDrawPoint c1
                      "control2", encodeDrawPoint c2
                      "to", encodeDrawPoint e ])
        | CurveCommand.QuadraticTo(ctrl, e) ->
            appendObject sb (case "QuadraticTo" [ "control", encodeDrawPoint ctrl; "to", encodeDrawPoint e ])
        | CurveCommand.Close -> appendObject sb (case "Close" [])

let rec private encodeShape (shape: Shape) : Appender =
    fun sb ->
        match shape with
        | Shape.Group(children, style) ->
            appendObject
                sb
                (case
                    "Group"
                    [ "children", (fun sb -> appendArrayWith sb (children |> List.map encodeShape))
                      "style", encodeDrawStyle style ])
        | Shape.Rectangle(x, y, w, h, cornerRadius, style) ->
            let fields =
                [ "height", float_ h
                  "style", encodeDrawStyle style
                  "width", float_ w
                  "x", float_ x
                  "y", float_ y ]

            let optionals =
                [ cornerRadius |> Option.map (fun r -> "cornerRadius", float_ r) ]
                |> List.choose id

            appendObject sb (case "Rectangle" (fields @ optionals))
        | Shape.Line(x1, y1, x2, y2, style) ->
            appendObject
                sb
                (case
                    "Line"
                    [ "style", encodeDrawStyle style
                      "x1", float_ x1
                      "x2", float_ x2
                      "y1", float_ y1
                      "y2", float_ y2 ])
        | Shape.Polyline(points, style) ->
            appendObject
                sb
                (case
                    "Polyline"
                    [ "points", (fun sb -> appendArrayWith sb (points |> List.map encodeDrawPoint))
                      "style", encodeDrawStyle style ])
        | Shape.Polygon(points, style) ->
            appendObject
                sb
                (case
                    "Polygon"
                    [ "points", (fun sb -> appendArrayWith sb (points |> List.map encodeDrawPoint))
                      "style", encodeDrawStyle style ])
        | Shape.Curve(commands, style) ->
            appendObject
                sb
                (case
                    "Curve"
                    [ "commands", (fun sb -> appendArrayWith sb (commands |> List.map encodeCurveCommand))
                      "style", encodeDrawStyle style ])
        | Shape.Circle(cx, cy, r, style) ->
            appendObject
                sb
                (case
                    "Circle"
                    [ "cx", float_ cx
                      "cy", float_ cy
                      "r", float_ r
                      "style", encodeDrawStyle style ])
        | Shape.Ellipse(cx, cy, rx, ry, style) ->
            appendObject
                sb
                (case
                    "Ellipse"
                    [ "cx", float_ cx
                      "cy", float_ cy
                      "rx", float_ rx
                      "ry", float_ ry
                      "style", encodeDrawStyle style ])
        | Shape.Label(x, y, text, style) ->
            appendObject
                sb
                (case
                    "Label"
                    [ "style", encodeDrawStyle style
                      "text", encodeTextSource text
                      "x", float_ x
                      "y", float_ y ])

let private encodeDrawingSpec (s: DrawingSpec) : Appender =
    fun sb ->
        let fields =
            [ "shapes", (fun sb -> appendArrayWith sb (s.Shapes |> List.map encodeShape))
              "style", encodeDrawStyle s.Style
              "viewBox", encodeViewBox s.ViewBox ]

        let optionals =
            [ s.Title |> Option.map (fun t -> "title", encodeTextSource t)
              s.Description |> Option.map (fun d -> "description", encodeTextSource d) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

/// Phase 596 — the auto-bind context for a control's `value` slot, mirroring
/// the decoder's synthesis exactly. A `value` that is the context's exact
/// auto-binding is OMITTED — `Filter(name, None)` on a chip (0.2.0),
/// `State(field id, typed placeholder)` on a form field (596; placeholders
/// pinned in `Fuaran.UI.Defaults.ControlValueDefaults`) — so the canonical
/// minimal control carries no `value` key at all. Any other binding emits.
type private ControlAutoBind =
    | NoAutoBind
    | FilterChip of name: string
    | FormFieldId of id: string

let private encodeFormFieldKind<'Msg> (autoBind: ControlAutoBind) (k: FormFieldKind<'Msg>) : Appender =
    fun sb ->
        let valueField (enc: Binding<'v> -> Appender) (autoDefault: 'v) (v: Binding<'v>) : Field list =
            match autoBind, v with
            | FilterChip n, Binding.Filter(fn, None) when fn = n -> []
            | FormFieldId fieldId, Binding.State(key, d) when key = fieldId && d = autoDefault -> []
            | _ -> [ "value", enc v ]

        // Handlers ride the wire only when present (Phase 426, generalising the
        // Phase 423 `FilterKind.onChange` mechanics): a `Some` closure → the
        // `"<closure>"` sentinel (byte-identical to before); a `None`
        // (declarative / AI-authored) field omits the key entirely.
        // `appendObject` sorts keys Ordinal, so omission leaves every other
        // field byte-stable.
        let handlerField (name: string) (h: 'a option) : Field list =
            match h with
            | Some _ -> [ name, sentinel closureSentinel ]
            | None -> []

        match k with
        | FormFieldKind.Text(value, oc) ->
            appendObject
                sb
                (case
                    "Text"
                    (handlerField "onChange" oc
                     @ valueField encodeBinding<string> Fuaran.UI.Defaults.ControlValueDefaults.text value))
        | FormFieldKind.Number(value, oc) ->
            appendObject
                sb
                (case
                    "Number"
                    (handlerField "onChange" oc
                     @ valueField encodeBinding<float> Fuaran.UI.Defaults.ControlValueDefaults.number value))
        | FormFieldKind.Checkbox(value, ot) ->
            appendObject
                sb
                (case
                    "Checkbox"
                    (handlerField "onToggle" ot
                     @ valueField encodeBinding<bool> Fuaran.UI.Defaults.ControlValueDefaults.checkbox value))
        | FormFieldKind.Choice(options, value, oc) ->
            appendObject
                sb
                (case
                    "Choice"
                    (handlerField "onChange" oc
                     @ [ "options", encodeBindingWith staticSelectOptions options ]
                     @ valueField
                         (encodeBindingWith staticStringOpt)
                         Fuaran.UI.Defaults.ControlValueDefaults.choice
                         value))
        | FormFieldKind.RangedNumber(value, oc, constraints) ->
            // Parallel-additive arm. Omit absent constraint
            // fields per algorithm rule 4 so the hash output for
            // an authored-equivalent `Number` payload differs only by the
            // discriminator + any present bound.
            let constraintFields =
                [ match constraints.Min with
                  | Some m -> "min", float_ m
                  | None -> ()
                  match constraints.Max with
                  | Some m -> "max", float_ m
                  | None -> ()
                  match constraints.Step with
                  | Some s -> "step", float_ s
                  | None -> () ]

            appendObject
                sb
                (case
                    "RangedNumber"
                    (handlerField "onChange" oc
                     @ valueField encodeBinding<float> Fuaran.UI.Defaults.ControlValueDefaults.number value
                     @ constraintFields))
        | FormFieldKind.SegmentedChoice(options, value, oc, orientation) ->
            // Parallel-additive Choice case with an orientation
            // discriminator. Field ordering: onChange → options →
            // orientation → value, matching the algorithm-rule key-sort
            // (alphabetical-by-key).
            appendObject
                sb
                (case
                    "SegmentedChoice"
                    (handlerField "onChange" oc
                     @ [ "options", encodeBindingWith staticSelectOptions options
                         "orientation", encodeOrientation orientation ]
                     @ valueField
                         (encodeBindingWith staticStringOpt)
                         Fuaran.UI.Defaults.ControlValueDefaults.choice
                         value))
        | FormFieldKind.TextArea(value, oc, rows) ->
            appendObject
                sb
                (case
                    "TextArea"
                    (handlerField "onChange" oc
                     @ [ "rows", int_ rows ]
                     @ valueField encodeBinding<string> Fuaran.UI.Defaults.ControlValueDefaults.text value))
        | FormFieldKind.Range(value, oc, constraints) ->
            // 0.2.0 — dual-thumb range (absorbed FilterKind.RangeFilter). The
            // Static pair rides as the typed {min, max} object; constraints
            // omitted when absent (rule 4).
            let rangeValue (v: Binding<float * float>) : Appender =
                match v with
                | Binding.Static(minV, maxV) -> fun sb2 -> appendObject sb2 [ "max", float_ maxV; "min", float_ minV ]
                | other ->
                    encodeBindingWith
                        (fun (a, b) -> fun sb2 -> appendObject sb2 [ "max", float_ b; "min", float_ a ])
                        other

            let constraintFields =
                match constraints with
                | Some c ->
                    [ match c.Min with
                      | Some m -> "min", float_ m
                      | None -> ()
                      match c.Max with
                      | Some m -> "max", float_ m
                      | None -> ()
                      match c.Step with
                      | Some st -> "step", float_ st
                      | None -> () ]
                | None -> []

            let vField =
                match autoBind, value with
                | FilterChip n, Binding.Filter(fn, None) when fn = n -> []
                | FormFieldId fieldId, Binding.State(key, d) when
                    key = fieldId && d = Fuaran.UI.Defaults.ControlValueDefaults.range
                    ->
                    []
                | _ -> [ "value", rangeValue value ]

            appendObject sb (case "Range" (handlerField "onChange" oc @ vField @ constraintFields))
        | FormFieldKind.Date(value, oc, variant, constraints) ->
            // Phase 288 — date/time field. Optional min/max (ISO strings) +
            // step (seconds) omitted when absent (rule 4). `variant` is always
            // emitted (bare-string DU); `value` is a Binding<string> ISO value.
            let constraintFields =
                [ match constraints.Min with
                  | Some m -> "min", str m
                  | None -> ()
                  match constraints.Max with
                  | Some m -> "max", str m
                  | None -> ()
                  match constraints.Step with
                  | Some s -> "step", float_ s
                  | None -> () ]

            appendObject
                sb
                (case
                    "Date"
                    (handlerField "onChange" oc
                     @ valueField encodeBinding<string> Fuaran.UI.Defaults.ControlValueDefaults.date value
                     @ [ "variant", encodeDateVariant variant ]
                     @ constraintFields))
        | FormFieldKind.DateRange(value, oc, variant, constraints) ->
            // Phase 725 — single-control date range. `Range`'s pair posture
            // with `Date`'s value conventions: the Static pair rides as the
            // BARE {from, to} object (no `Static` envelope), `variant` is
            // always emitted, and the ISO min/max + numeric step are omitted
            // when absent (rule 4).
            let pairValue (v: Binding<string * string>) : Appender =
                match v with
                | Binding.Static(fromV, toV) -> fun sb2 -> appendObject sb2 [ "from", str fromV; "to", str toV ]
                | other ->
                    encodeBindingWith (fun (a, b) -> fun sb2 -> appendObject sb2 [ "from", str a; "to", str b ]) other

            let constraintFields =
                [ match constraints.Min with
                  | Some m -> "min", str m
                  | None -> ()
                  match constraints.Max with
                  | Some m -> "max", str m
                  | None -> ()
                  match constraints.Step with
                  | Some s -> "step", float_ s
                  | None -> () ]

            let vField =
                match autoBind, value with
                | FilterChip n, Binding.Filter(fn, None) when fn = n -> []
                | FormFieldId fieldId, Binding.State(key, d) when
                    key = fieldId && d = Fuaran.UI.Defaults.ControlValueDefaults.dateRange
                    ->
                    []
                | _ -> [ "value", pairValue value ]

            appendObject
                sb
                (case
                    "DateRange"
                    (handlerField "onChange" oc
                     @ vField
                     @ [ "variant", encodeDateVariant variant ]
                     @ constraintFields))

let private encodeFormField<'Msg> (f: FormField<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "id", str f.Id
              "kind", encodeFormFieldKind (FormFieldId f.Id) f.Kind
              "label", encodeTextSource f.Label
              "required", bool_ f.Required ]

        let optionals =
            [ f.Help |> Option.map (fun t -> "help", encodeTextSource t) ] |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeFormSpec<'Msg> (s: FormSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "fields", (fun sb -> appendArrayWith sb (s.Fields |> List.map encodeFormField))
              "onSubmit", encodeAction s.OnSubmit
              "submitLabel", encodeTextSource s.SubmitLabel ]

        let optionals =
            // Phase 130: optional bound form-level disabled-state — emitted
            // only when `Some`, mirroring ButtonSpec.Disabled.
            [ s.Disabled |> Option.map (fun b -> "disabled", encodeBinding<bool> b) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeFilterSpec<'Msg> (s: FilterSpec<'Msg>) : Appender =
    fun sb ->
        appendObject
            sb
            [ "kind", encodeFormFieldKind (FilterChip s.Name) s.Field
              "label", encodeTextSource s.Label
              "name", str s.Name ]

let private encodeButtonSpec<'Msg> (s: ButtonSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "label", encodeTextSource s.Label
              "onClick", encodeAction s.OnClick
              "variant", encodeButtonVariant s.Variant ]

        let optionals =
            [ s.Icon |> Option.map (fun i -> "icon", encodeIconSource i)
              // Optional bound disabled-state — emitted only when `Some`,
              // mirroring `MetricSpec.Trend`'s optional-binding shape.
              s.Disabled |> Option.map (fun b -> "disabled", encodeBinding<bool> b) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeSelectSpec<'Msg> (s: SelectSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "label", encodeTextSource s.Label
              "source", encodeBindingWith staticSelectOptions s.Source
              "value", encodeBindingWith staticStringOpt s.Value ]

        let optionals =
            [ // Phase 426: `onChange` rides the wire only when present. A `Some`
              // closure → the `"<closure>"` sentinel (byte-identical to before);
              // a declarative (handler-free) select omits the key and the
              // renderer writes the chosen option back to the `value` slot.
              s.OnChange |> Option.map (fun _ -> "onChange", sentinel closureSentinel)
              s.Placeholder |> Option.map (fun t -> "placeholder", encodeTextSource t)
              // Phase 130: optional bound disabled-state.
              s.Disabled |> Option.map (fun b -> "disabled", encodeBinding<bool> b)
              // Phase 291: multi-select. `multiple` omitted when `false` and
              // `values` omitted when `None`, so single-select wire is
              // byte-identical to pre-multi-select fixtures (the degenerate case).
              (if s.Multiple then Some("multiple", bool_ true) else None)
              s.Values |> Option.map (fun b -> "values", encodeBindingWith staticStringList b)
              // Phase 426: the multi-select handler now carries its own sentinel
              // key when present (previously never encoded), so a closure-authored
              // multi-select round-trips distinguishably from the declarative one.
              s.OnChangeMulti
              |> Option.map (fun _ -> "onChangeMulti", sentinel closureSentinel) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeFileUploadSpec<'Msg> (s: FileUploadSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "accept", (fun sb -> appendArrayWith sb (s.Accept |> List.map str))
              "label", encodeTextSource s.Label
              "multiple", bool_ s.Multiple
              "onSelect", sentinel closureSentinel ]

        let optionals =
            // Phase 130: optional bound disabled-state.
            [ s.Disabled |> Option.map (fun b -> "disabled", encodeBinding<bool> b) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeCellKindErased<'Msg> (k: CellKindErased<'Msg>) : Appender =
    fun sb ->
        match k with
        | CellKindErased.Text -> appendObject sb (case "Text" [])
        | CellKindErased.Numeric -> appendObject sb (case "Numeric" [])
        | CellKindErased.Date -> appendObject sb (case "Date" [])
        | CellKindErased.Editable _ -> appendObject sb (case "Editable" [ "onEdit", sentinel closureSentinel ])
        | CellKindErased.Checkbox(_, _) ->
            appendObject sb (case "Checkbox" [ "get", sentinel closureSentinel; "onToggle", sentinel closureSentinel ])
        | CellKindErased.Button(label, _) ->
            appendObject sb (case "Button" [ "label", encodeTextSource label; "onClick", sentinel closureSentinel ])
        | CellKindErased.ButtonGroup items ->
            let buttons =
                items
                |> List.map (fun (label, _) ->
                    fun sb -> appendObject sb [ "label", encodeTextSource label; "onClick", sentinel closureSentinel ])

            appendObject sb (case "ButtonGroup" [ "buttons", (fun sb -> appendArrayWith sb buttons) ])
        | CellKindErased.Link(_, _) ->
            appendObject sb (case "Link" [ "hrefFn", sentinel closureSentinel; "labelFn", sentinel closureSentinel ])
        | CellKindErased.Pill(_, _) ->
            appendObject sb (case "Pill" [ "labelFn", sentinel closureSentinel; "toneFn", sentinel closureSentinel ])
        | CellKindErased.Progress(_, _) ->
            appendObject
                sb
                (case "Progress" [ "fractionFn", sentinel closureSentinel; "labelFn", sentinel closureSentinel ])
        | CellKindErased.Custom _ -> appendObject sb (case "Custom" [ "fn", sentinel closureSentinel ])

let private encodeColumnErased<'Msg> (c: ColumnErased<'Msg>) : Appender =
    fun sb ->
        // Phase 425 — `value` (the closure) and `field` (the declarative row-property name) are sibling
        // optional slots, each omitted-when-None. A closure-authored column keeps `"value":"<closure>"`
        // (byte-stable); a decoded/field-named column carries `"field":"…"` instead.
        // Phase 460 — `format`/`width` omitted-when-default (`CellFormat.None`/`ColumnWidth.Auto`).
        let optionals =
            [ cellFormatOptional "format" c.Format
              columnWidthOptional "width" c.Width
              c.Value |> Option.map (fun _ -> "value", sentinel closureSentinel)
              c.Field |> Option.map (fun fld -> "field", str fld) ]
            |> List.choose id

        appendObject sb ([ "kind", encodeCellKindErased c.Kind; "label", str c.Label ] @ optionals)

let private encodeGridSpec<'Msg> (s: GridSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ yield "columns", (fun sb -> appendArrayWith sb (s.Columns |> List.map encodeColumnErased))
              // 0.2.0 — omitted-when-false (100% of observed usage is false).
              if s.Editable then
                  yield "editable", bool_ true
              yield "source", encodeBinding<obj seq> s.Source ]

        // Phase 425 — `rowKey` (closure) + `rowKeyField` (declarative) are sibling optional slots.
        // Phase 393 — `staticRows` (the read-only mode's TextSource matrix) is optional; omitted for a
        // data-bound grid, so every existing grid fixture stays byte-identical.
        let optionals =
            [ s.OnRowClick |> Option.map (fun _ -> "onRowClick", sentinel closureSentinel)
              s.RowKey |> Option.map (fun _ -> "rowKey", sentinel closureSentinel)
              s.RowKeyField |> Option.map (fun fld -> "rowKeyField", str fld)
              s.StaticRows
              |> Option.map (fun (headers, rows) ->
                  "staticRows",
                  (fun sb ->
                      appendObject
                          sb
                          [ "headers", (fun sb -> appendArrayWith sb (headers |> List.map encodeTextSource))
                            "rows",
                            (fun sb ->
                                let rowAppenders =
                                    rows
                                    |> List.map (fun row ->
                                        fun sb -> appendArrayWith sb (row |> List.map encodeTextSource))

                                appendArrayWith sb rowAppenders) ])) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeChartSpec<'Msg> (s: ChartSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "kind", encodeChartKind s.Kind
              "source", encodeBinding<obj seq> s.Source
              // `stacked` (Phase 126): previously dropped on the wire — now
              // carried so a chart's stacked-vs-grouped intent round-trips.
              "stacked", bool_ s.Stacked
              "xField", str s.XField
              "yFields", (fun sb -> appendArrayWith sb (s.YFields |> List.map str)) ]

        let optionals =
            [ s.Title |> Option.map (fun t -> "title", encodeTextSource t)
              s.OnPointClick |> Option.map (fun _ -> "onPointClick", sentinel closureSentinel) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

let private encodeMapSpec<'Msg> (s: MapSpec<'Msg>) : Appender =
    fun sb ->
        let fields =
            [ "centreLatitude", float_ s.CentreLatitude
              "centreLongitude", float_ s.CentreLongitude
              "source", encodeBindingWith staticMarkerSeq s.Source
              "zoom", int_ s.Zoom ]

        let optionals =
            [ s.OnMarkerClick
              |> Option.map (fun _ -> "onMarkerClick", sentinel closureSentinel) ]
            |> List.choose id

        appendObject sb (fields @ optionals)

// ─── Parameterised-fragment hole / effect / scalar encoders (Phase 180) ─────
//
// Additive to the FragmentDecl / FragmentRef wire shape. None of these reference
// `Node`, so they sit ahead of the recursive node group; the slot-argument
// subtree (the only `Node`-bearing arg shape) is encoded inline in the
// `FragmentRef` arm via `nodeAppender`.

let private encodeHoleValueSpace (s: HoleValueSpace) : Appender =
    fun sb ->
        match s with
        | HoleValueSpace.IntRange(lo, hi) -> appendObject sb (case "IntRange" [ "max", int_ hi; "min", int_ lo ])
        | HoleValueSpace.FloatRange(lo, hi) ->
            appendObject sb (case "FloatRange" [ "max", float_ hi; "min", float_ lo ])
        | HoleValueSpace.StringLen(lo, hi) ->
            appendObject sb (case "StringLen" [ "maxLen", int_ hi; "minLen", int_ lo ])
        | HoleValueSpace.Enum choices ->
            appendObject sb (case "Enum" [ "choices", (fun sb -> appendArrayWith sb (choices |> List.map str)) ])
        | HoleValueSpace.AnyString -> appendObject sb (case "AnyString" [])

/// A boxed scalar (a value default or a value argument). Self-describing on the
/// wire — the `$type` tag pins the CLR shape so the decoder reconstructs the
/// right `obj` without consulting the (possibly out-of-scope) hole value-space.
let private encodeScalar (v: obj) : Appender =
    fun sb ->
        match v with
        | :? int as n -> appendObject sb (case "Int" [ "value", int_ n ])
        | :? float as f -> appendObject sb (case "Float" [ "value", float_ f ])
        | :? bool as b -> appendObject sb (case "Bool" [ "value", bool_ b ])
        | :? string as s -> appendObject sb (case "Str" [ "value", str s ])
        | _ -> appendObject sb (case "Str" [ "value", sentinel opaqueSentinel ])

let private encodeHoleDecl (h: HoleDecl) : Appender =
    fun sb ->
        match h with
        | HoleDecl.Value(name, space, def) ->
            let fields =
                [ "name", str name; "space", encodeHoleValueSpace space ]
                @ (match def with
                   | Some d -> [ "default", encodeScalar d ]
                   | None -> [])

            appendObject sb (case "Value" fields)
        | HoleDecl.Slot(name, kc) ->
            let fields =
                [ "name", str name ]
                @ (match kc with
                   | Some k -> [ "kindConstraint", str k ]
                   | None -> [])

            appendObject sb (case "Slot" fields)
        | HoleDecl.Repeat(name, space) ->
            appendObject sb (case "Repeat" [ "countSpace", encodeHoleValueSpace space; "name", str name ])

let private encodeHostEffect =
    function
    | HostEffect.Pure -> "Pure"
    | HostEffect.ReadsHost -> "ReadsHost"
    | HostEffect.WritesHost -> "WritesHost"

let private encodeDeterminism =
    function
    | DeterminismSource.Deterministic -> "Deterministic"
    | DeterminismSource.Clock -> "Clock"
    | DeterminismSource.Random -> "Random"
    | DeterminismSource.Network -> "Network"

let private encodeEffectClass (e: EffectClass) : Appender =
    fun sb ->
        appendObject
            sb
            [ "determinism", str (encodeDeterminism e.Determinism)
              "hostEffect", str (encodeHostEffect e.HostEffect) ]

// LayoutKind references Node<'Msg> via Children — mutually recursive with
// encodeNode below.

let rec private nodeKindAppender<'Msg> (k: NodeKind<'Msg>) : Appender =
    fun sb ->
        match k with
        // The four behavioural categories are flat on the wire (WIRE_FORMAT
        // §3.2): a node's `kind` carries the primitive discriminator directly
        // (e.g. {"$type":"LabelValueRow",…}). The category — Layout / Display
        // / Input / Visualisation — is a host-side classification recovered on
        // decode, not a wire nesting level, so we emit the inner kind appender
        // straight into the `kind` slot with no category envelope.
        // -- Layout --
        | NodeKind.Box spec ->
            // Phase 390 — the unified container. Ordinal key order:
            // children < heading < layout < role. `layout` is a discriminated
            // object (Flex | Grid | Auto), `role` a string; `heading` emits
            // only when Some. Every nested optional (gap / templateColumns) is
            // emitted only when Some, preserving byte-identical encoding.
            let layoutAppender: Appender =
                match spec.Layout with
                | BoxLayout.Flex f ->
                    hoistSpec "Flex" (fun sb ->
                        // direction < gap < wrap
                        let required = [ "direction", encodeOrientation f.Direction ]
                        let optionals = [ f.Gap |> Option.map (fun g -> "gap", int_ g) ] |> List.choose id
                        appendObject sb (required @ optionals @ [ "wrap", bool_ f.Wrap ]))
                | BoxLayout.Grid g ->
                    hoistSpec "Grid" (fun sb ->
                        // cols < gap < templateColumns
                        let required = [ "cols", int_ g.Cols ]

                        let optionals =
                            [ g.Gap |> Option.map (fun n -> "gap", int_ n)
                              g.TemplateColumns |> Option.map (fun t -> "templateColumns", str t) ]
                            |> List.choose id

                        appendObject sb (required @ optionals))
                | BoxLayout.Auto -> hoistSpec "Auto" (fun sb -> appendObject sb [])

            let roleStr =
                match spec.Role with
                | BoxRole.Group -> "Group"
                | BoxRole.Card -> "Card"
                | BoxRole.Dashboard -> "Dashboard"
                | BoxRole.Separator -> "Separator"

            let inner: Appender =
                fun sb ->
                    let head =
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender)) ]

                    let headingOpt =
                        [ spec.Heading |> Option.map (fun t -> "heading", encodeTextSource t) ]
                        |> List.choose id

                    let tail = [ "layout", layoutAppender; "role", str roleStr ]
                    appendObject sb (head @ headingOpt @ tail)

            hoistSpec "Box" (inner) sb
        | NodeKind.SplitPanel spec ->
            hoistSpec
                "SplitPanel"
                (fun sb ->
                    appendObject
                        sb
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          "weight", float_ spec.Weight ])
                sb
        | NodeKind.Tabs spec ->
            let inner: Appender =
                fun sb ->
                    // Canonical fields (children + orientation)
                    // stay required; the additive fields
                    // (tabHeaders, tabTags, activeTag) emit only when `Some`
                    // so existing fixtures continue to encode byte-identical.
                    // `activeIndex` (Phase 126) is carried so the selected-tab
                    // binding round-trips. `onSelect` / `onSelectTag` (Phase
                    // 426): each rides the wire only when present — a `Some`
                    // closure → the `"<closure>"` sentinel (byte-identical to
                    // the pre-426 always-emitted `onSelect`); `None` — the
                    // declarative shape — omits the key and arms the renderer's
                    // ActiveIndex/ActiveTag write-back default.
                    let requiredFields =
                        [ yield "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          // 0.2.0 — omitted-when-Horizontal (the universal default).
                          if spec.Orientation <> Horizontal then
                              yield "orientation", encodeOrientation spec.Orientation
                          yield "activeIndex", encodeBinding<int> spec.ActiveIndex ]

                    let encodeTabHeader (h: TabHeader) : Appender =
                        fun sb ->
                            let fields = [ "label", encodeTextSource h.Label ]

                            let optionals =
                                [ h.Icon |> Option.map (fun ic -> "icon", encodeIconSource ic)
                                  h.Disabled |> Option.map (fun b -> "disabled", encodeBinding<bool> b) ]
                                |> List.choose id

                            appendObject sb (fields @ optionals)

                    let optionals =
                        [ spec.OnSelect |> Option.map (fun _ -> "onSelect", sentinel closureSentinel)
                          spec.TabHeaders
                          |> Option.map (fun hs ->
                              "tabHeaders", (fun sb -> appendArrayWith sb (hs |> List.map encodeTabHeader)))
                          spec.TabTags
                          |> Option.map (fun ts ->
                              "tabTags", (fun sb -> appendArrayWith sb (ts |> List.map (fun s -> str s))))
                          spec.ActiveTag |> Option.map (fun b -> "activeTag", encodeBinding<string> b)
                          spec.OnSelectTag
                          |> Option.map (fun _ -> "onSelectTag", sentinel closureSentinel) ]
                        |> List.choose id

                    appendObject sb (requiredFields @ optionals)

            hoistSpec "Tabs" (inner) sb
        | NodeKind.Stepper spec ->
            // `onSelect` is a closure → the `"<closure>"` sentinel (decodes to
            // a no-op, re-encodes to the same sentinel; byte-stable) — same
            // treatment as Tabs `onSelect` (Phase 126).
            hoistSpec
                "Stepper"
                (fun sb ->
                    appendObject
                        sb
                        [ "activeStep", encodeBinding<int> spec.ActiveStep
                          "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          "onSelect", sentinel closureSentinel ])
                sb
        | NodeKind.SummaryList spec ->
            // SummaryList — Heading is optional + appended
            // after Children, matching Card's encoding pattern.
            let inner =
                fun sb ->
                    let fields =
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender)) ]

                    let optionals =
                        [ spec.Heading |> Option.map (fun t -> "heading", encodeTextSource t) ]
                        |> List.choose id

                    appendObject sb (fields @ optionals)

            hoistSpec "SummaryList" (inner) sb
        | NodeKind.Disclosure spec ->
            // Disclosure — required heading +
            // open binding + defaultOpen bool + children. `OnToggle` (Phase
            // 426): rides the wire only when present — a `Some` closure → the
            // `"onToggle":"<closure>"` sentinel (previously never encoded, so
            // every pre-426 fixture omits it and stays byte-identical); `None`
            // arms the renderer's `Open` write-back default.
            hoistSpec
                "Disclosure"
                (fun sb ->
                    let fields =
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          "defaultOpen", bool_ spec.DefaultOpen
                          "heading", encodeTextSource spec.Heading
                          "open", encodeBinding<bool> spec.Open ]

                    let optionals =
                        [ spec.OnToggle |> Option.map (fun _ -> "onToggle", sentinel closureSentinel) ]
                        |> List.choose id

                    appendObject sb (fields @ optionals))
                sb
        | NodeKind.Modal spec ->
            // Phase 289 — overlay dialog. `onDismiss` is a wire-survivable
            // Action (like Form.OnSubmit), encoded as the action value; `open`
            // is the visibility binding; `heading` optional. Since Phase 426
            // `onDismiss` is optional: `Some action` encodes exactly as before
            // (byte-stable); `None` omits the key and arms the renderer's
            // `Open` write-back default (dismiss writes `false` to the slot).
            let inner =
                fun sb ->
                    let fields =
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          "dismissable", bool_ spec.Dismissable
                          "open", encodeBinding<bool> spec.Open ]

                    let optionals =
                        [ spec.OnDismiss |> Option.map (fun a -> "onDismiss", encodeAction a)
                          spec.Heading |> Option.map (fun t -> "heading", encodeTextSource t) ]
                        |> List.choose id

                    appendObject sb (fields @ optionals)

            hoistSpec "Modal" inner sb
        | NodeKind.ScrollArea spec ->
            // Phase 289 — overflow/scroll container. Optional maxHeight/maxWidth
            // (pixels) omitted when absent (rule 4); `orientation` always
            // emitted (scroll-axis DU).
            let inner =
                fun sb ->
                    let fields =
                        [ "children", (fun sb -> appendArrayWith sb (spec.Children |> List.map nodeAppender))
                          "orientation", encodeScrollOrientation spec.Orientation ]

                    let optionals =
                        [ spec.MaxHeight |> Option.map (fun h -> "maxHeight", int_ h)
                          spec.MaxWidth |> Option.map (fun w -> "maxWidth", int_ w) ]
                        |> List.choose id

                    appendObject sb (fields @ optionals)

            hoistSpec "ScrollArea" inner sb
        // -- Display --
        | NodeKind.Heading s -> hoistSpec "Heading" (encodeHeadingSpec s) sb
        | NodeKind.Markdown s -> hoistSpec "Markdown" (encodeMarkdownSpec s) sb
        | NodeKind.Metric s -> hoistSpec "Metric" (encodeMetricSpec s) sb
        | NodeKind.Badge s -> hoistSpec "Badge" (encodeBadgeSpec s) sb
        | NodeKind.Sparkline s -> hoistSpec "Sparkline" (encodeSparklineSpec s) sb
        | NodeKind.Callout s -> hoistSpec "Callout" (encodeCalloutSpec s) sb
        | NodeKind.Progress s -> hoistSpec "Progress" (encodeProgressSpec s) sb
        | NodeKind.Skeleton s -> hoistSpec "Skeleton" (encodeSkeletonSpec s) sb
        | NodeKind.LabelValueRow s -> hoistSpec "LabelValueRow" (encodeLabelValueRowSpec s) sb
        | NodeKind.Fact s -> hoistSpec "Fact" (encodeFactSpec s) sb
        | NodeKind.Link s -> hoistSpec "Link" (encodeLinkSpec s) sb
        | NodeKind.Image s -> hoistSpec "Image" (encodeImageSpec s) sb
        | NodeKind.List s -> hoistSpec "List" (encodeListSpec s) sb
        | NodeKind.Toast s -> hoistSpec "Toast" (encodeToastSpec s) sb
        | NodeKind.CodeBlock s -> hoistSpec "CodeBlock" (encodeCodeBlockSpec s) sb
        | NodeKind.Math s -> hoistSpec "Math" (encodeMathSpec s) sb
        | NodeKind.Drawing s -> hoistSpec "Drawing" (encodeDrawingSpec s) sb
        // -- Input --
        | NodeKind.Form s -> hoistSpec "Form" (encodeFormSpec s) sb
        | NodeKind.Filters items ->
            appendObject
                sb
                (case "Filters" [ "items", (fun sb -> appendArrayWith sb (items |> List.map encodeFilterSpec)) ])
        | NodeKind.Button s -> hoistSpec "Button" (encodeButtonSpec s) sb
        | NodeKind.FileUpload s -> hoistSpec "FileUpload" (encodeFileUploadSpec s) sb
        | NodeKind.Select s -> hoistSpec "Select" (encodeSelectSpec s) sb
        // -- Vis --
        | NodeKind.DataGrid s -> hoistSpec "DataGrid" (encodeGridSpec s) sb
        | NodeKind.Chart s -> hoistSpec "Chart" (encodeChartSpec s) sb
        | NodeKind.Map s -> hoistSpec "Map" (encodeMapSpec s) sb
        | NodeKind.ErrorBoundary spec ->
            // The render-time error boundary is an
            // AI-emittable shape, so it round-trips through the wire form
            // like every other NodeKind. Emit `child` + `fallback` as full
            // Node subtrees — both consumed by `decodeNodeAst` on the
            // inverse side. Field order is canonical (lexicographic) per
            // the `appendObject` pre-sort.
            appendObject
                sb
                (case "ErrorBoundary" [ "child", nodeAppender spec.Child; "fallback", nodeAppender spec.Fallback ])
        | NodeKind.Switch spec ->
            // State-bound conditional child (Phase 392). Emit `stateKey`
            // (string), `cases` (array of `{child,match}` objects — each case's
            // keys sort `child` < `match`), and `default` (a full Node subtree).
            // Canonical field order is enforced by `appendObject`'s pre-sort;
            // consumed by `decodeNodeAst` on the inverse side.
            appendObject
                sb
                (case
                    "Switch"
                    [ "cases",
                      (fun sb ->
                          appendArrayWith
                              sb
                              (spec.Cases
                               |> List.map (fun (matchValue, child) ->
                                   (fun sb ->
                                       appendObject sb [ "child", nodeAppender child; "match", str matchValue ]))))
                      "default", nodeAppender spec.Default
                      "stateKey", str spec.StateKey ])
        | NodeKind.Custom(moduleId, componentId, props, contentHash, exposedNodeIds) ->
            // Emit `contentHash` + `exposedNodeIds`
            // when populated; omit when default (None / []). Canonical
            // lexicographic field order is enforced by `appendObject`'s
            // pre-sort, so we just yield the populated entries. The wire
            // shape is the stable structural-decoder surface — JsonDecode
            // breakage on either field is a major-version bump (same shape
            // as the Local wire-lock declaration).
            let strictnessStr (s: HashStrictness) =
                match s with
                | HashStrictness.StrictReplay -> "StrictReplay"
                | HashStrictness.AdvisoryWarning -> "AdvisoryWarning"
                | HashStrictness.Enforced -> "Enforced"

            let hashField =
                contentHash
                |> Option.map (fun h ->
                    "contentHash",
                    (fun sb ->
                        appendObject
                            sb
                            [ "algorithm", str h.Algorithm
                              "hash", str h.Hash
                              "strictness", str (strictnessStr h.Strictness) ]))

            let exposedField =
                match exposedNodeIds with
                | [] -> None
                | ids ->
                    Some("exposedNodeIds", (fun sb -> appendArrayWith sb (ids |> List.map (fun (NodeId s) -> str s))))

            let fields =
                [ "componentId", str componentId
                  "moduleId", str moduleId
                  "props", encodeMap props ]
                @ (Option.toList hashField)
                @ (Option.toList exposedField)

            appendObject sb (case "Custom" fields)
        | NodeKind.FragmentDecl spec ->
            // Named reusable subtree — the
            // declaration half. Emit `name` (string) + `body` (full Node
            // subtree). Field order is canonical lexicographic per the
            // appendObject pre-sort. The wire shape is the stable
            // structural-decoder surface — JsonDecode breakage on either
            // field is a major-version bump (per the stability
            // impact note).
            // Phase 180 — `holes` + `effect` are additive. A zero-hole decl with
            // a pure-deterministic effect omits both (algorithm rule 4), so the
            // Phase 61 fixed-body shape stays byte-identical.
            let (FragmentId rawName) = spec.Name

            let holesField =
                match spec.Holes with
                | [] -> []
                | hs -> [ "holes", (fun sb -> appendArrayWith sb (hs |> List.map encodeHoleDecl)) ]

            let effectField =
                if spec.Effect = EffectClass.pureDeterministic then
                    []
                else
                    [ "effect", encodeEffectClass spec.Effect ]

            appendObject
                sb
                (case
                    "FragmentDecl"
                    ([ "body", nodeAppender spec.Body; "name", str rawName ]
                     @ holesField
                     @ effectField))
        | NodeKind.FragmentRef spec ->
            // Named reusable subtree — the
            // reference half. Emit `name` (the ref points at a decl by
            // name; the body lives at the decl site, not duplicated
            // here — that's the entire emission-economy win of fragments).
            // Phase 180 — `args` is additive: a zero-arg ref omits it, so the
            // Phase 61 name-only shape stays byte-identical.
            let (FragmentId rawName) = spec.Name

            let argsField =
                if Map.isEmpty spec.Args then
                    []
                else
                    let argAppender (a: FragmentArg<'Msg>) : Appender =
                        fun sb ->
                            match a with
                            | FragmentArg.Value v -> encodeScalar v sb
                            | FragmentArg.Slot tree -> appendObject sb (case "SlotArg" [ "tree", nodeAppender tree ])

                    [ "args",
                      (fun sb -> appendObject sb (spec.Args |> Map.toList |> List.map (fun (k, a) -> k, argAppender a))) ]

            appendObject sb (case "FragmentRef" ([ "name", str rawName ] @ argsField))
        | NodeKind.Mount spec ->
            // Isolation/embedding boundary (Phase 265, §4o). The guest
            // interior is a scope *reference*, not an inlined Node, so the
            // wire carries `scopeId` + the declared `channel` + the
            // default-deny `capabilities` + the `onBubble` closure sentinel.
            // `inputs` is additive (omitted when empty, mirroring FragmentRef
            // `args`) and reuses the FragmentArg scalar/slot encoding.
            let directionStr (d: ChannelDirection) =
                match d with
                | ChannelDirection.OutOnly -> "OutOnly"
                | ChannelDirection.TwoWay -> "TwoWay"

            let channelField =
                "channel",
                (fun sb ->
                    let msgShapeField =
                        match spec.Channel.MessageShape with
                        | Some s -> [ "messageShape", str s ]
                        | None -> []

                    appendObject sb ([ "direction", str (directionStr spec.Channel.Direction) ] @ msgShapeField))

            // `capabilities` is always emitted (even when empty → `[]`): an
            // explicit empty list is the visible default-deny posture, so the
            // security-relevant field is never silently absent from the wire.
            let capabilitiesField =
                "capabilities",
                (fun sb -> appendArrayWith sb (spec.Capabilities |> List.map (fun (CapabilityTag t) -> str t)))

            let inputsField =
                if Map.isEmpty spec.Inputs then
                    []
                else
                    let argAppender (a: FragmentArg<'Msg>) : Appender =
                        fun sb ->
                            match a with
                            | FragmentArg.Value v -> encodeScalar v sb
                            | FragmentArg.Slot tree -> appendObject sb (case "SlotArg" [ "tree", nodeAppender tree ])

                    [ "inputs",
                      (fun sb ->
                          appendObject sb (spec.Inputs |> Map.toList |> List.map (fun (k, a) -> k, argAppender a))) ]

            appendObject
                sb
                (case
                    "Mount"
                    ([ "scopeId", str spec.ScopeId
                       channelField
                       capabilitiesField
                       "onBubble", sentinel closureSentinel ]
                     @ inputsField))

and private stateBehaviourAppender<'Msg> (s: StateBehaviour<'Msg>) : Appender =
    fun sb ->
        let optionals =
            [ s.OnLoading |> Option.map (fun n -> "onLoading", nodeAppender n)
              s.OnEmpty |> Option.map (fun n -> "onEmpty", nodeAppender n)
              s.OnError |> Option.map (fun _ -> "onError", sentinel closureSentinel) ]
            |> List.choose id

        appendObject sb optionals

and private semanticStyleAppender (s: SemanticStyle) : Appender =
    fun sb ->
        // Every field is emitted only when non-default: `role`/`voice` since
        // Phase 147, and `emphasis`/`tone`/`weight` since Phase 460. A style object
        // authored before a field existed round-trips byte-identically, and an
        // all-default style encodes as `{}`. `appendObject` sorts keys, so order is
        // irrelevant.
        let optionals =
            [ if s.Emphasis <> Emphasis.Normal then
                  "emphasis", encodeEmphasis s.Emphasis
              if s.Tone <> ToneVariant.Default then
                  "tone", encodeTone s.Tone
              if s.Weight <> StyleWeight.Standard then
                  "weight", encodeWeight s.Weight
              if s.Role <> StyleRole.None then
                  "role", encodeStyleRole s.Role
              if s.Voice <> FontVoice.Default then
                  "voice", encodeFontVoice s.Voice ]

        appendObject sb optionals

// Accessibility encoders. AriaRole / LiveRegionKind canonicalise
// as their HTML/ARIA string form (matches Render.fs's emission). The
// Accessibility record renders all six fields as optional object keys, omitted
// when None.

and private encodeAriaRole (r: AriaRole) : Appender =
    fun sb ->
        let s =
            match r with
            | AriaRole.Button -> "button"
            | AriaRole.Link -> "link"
            | AriaRole.Dialog -> "dialog"
            | AriaRole.Alert -> "alert"
            | AriaRole.Status -> "status"
            | AriaRole.Banner -> "banner"
            | AriaRole.Navigation -> "navigation"
            | AriaRole.Main -> "main"
            | AriaRole.Form -> "form"
            | AriaRole.Region -> "region"
            | AriaRole.Heading -> "heading"
            | AriaRole.Progressbar -> "progressbar"
            | AriaRole.Tab -> "tab"
            | AriaRole.Tablist -> "tablist"
            | AriaRole.Tabpanel -> "tabpanel"
            | AriaRole.Custom raw -> raw

        appendRawString sb s

and private encodeLiveRegion (k: LiveRegionKind) : Appender =
    fun sb ->
        let s =
            match k with
            | LiveRegionKind.Polite -> "polite"
            | LiveRegionKind.Assertive -> "assertive"
            | LiveRegionKind.Off -> "off"

        appendRawString sb s

and private encodeAccessibility (a: Accessibility) : Appender =
    fun sb ->
        let optionals =
            [ a.Label |> Option.map (fun b -> "label", encodeBinding<string> b)
              a.LabelledBy |> Option.map (fun id -> "labelledBy", str (nodeIdStr id))
              a.DescribedBy |> Option.map (fun id -> "describedBy", str (nodeIdStr id))
              a.Role |> Option.map (fun r -> "role", encodeAriaRole r)
              a.LiveRegion |> Option.map (fun k -> "liveRegion", encodeLiveRegion k)
              a.Hidden |> Option.map (fun b -> "hidden", encodeBinding<bool> b) ]
            |> List.choose id

        appendObject sb optionals

and private isEmptyState<'Msg> (s: StateBehaviour<'Msg>) : bool =
    Option.isNone s.OnLoading && Option.isNone s.OnEmpty && Option.isNone s.OnError

and private isDefaultStyle (s: SemanticStyle) : bool =
    s.Emphasis = Emphasis.Normal
    && s.Tone = ToneVariant.Default
    && s.Weight = StyleWeight.Standard
    && s.Role = StyleRole.None
    && s.Voice = FontVoice.Default

and private nodeAppender<'Msg> (n: Node<'Msg>) : Appender =
    fun sb ->
        let required = [ "id", str (nodeIdStr n.Id); "kind", nodeKindAppender n.Kind ]

        // `state` and `style` are omitted when empty / all-default — the common
        // case, so most nodes carry neither. The decoder restores the default on
        // absence (WIRE_FORMAT §3.1). Accessibility is likewise optional (omitted
        // when None).
        let optionals =
            [ (if isEmptyState n.State then
                   None
               else
                   Some("state", stateBehaviourAppender n.State))
              (if isDefaultStyle n.Style then
                   None
               else
                   Some("style", semanticStyleAppender n.Style))
              n.Accessibility |> Option.map (fun a -> "accessibility", encodeAccessibility a) ]
            |> List.choose id

        appendObject sb (required @ optionals)

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
