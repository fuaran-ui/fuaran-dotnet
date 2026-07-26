module Fuaran.UI.Ops.JsonDecode

// ============================================================================
//  Structural decoder for the canonical-JSON wire form
//  produced by `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode` /
//  `encodeOp`. Reverses the algorithm pinned in
//  `docs/migrations/12-E-0-json-decoder.md` (algorithm mirrors the encoder
//  pinned in `docs/migrations/12-Z-op-stream.md`).
//
//  Strategic position. Three downstream consumers:
//   - AI-emission Gate 1 entry point: parse the
//     AI's emitted JSON via `decodeNode`; failure flows to the eval's
//     `parse-failed` outcome carrying the structured `DecodeError`.
//   - Visual catalog AI-eval-input mode: backing decoder
//     for the `?ai-eval=1` JSON paste mode.
//   - Downstream AI-driving-the-UI consumer (in a separate sibling tier):
//     decode + apply + replay round-trip against the persistent op stream.
//
//  Module placement specified `Fuaran/src/Fuaran.UI/JsonDecode.fs`,
//  but `decodeOp` consumes `TreeOp<'Msg>` which lives in this package
//  (`Fuaran.UI.Ops`), which already depends on `Fuaran.UI`. Co-locating the
//  decoder in `Fuaran.UI` would close a project-reference cycle. Placing it
//  here mirrors where the encoder sits relative to both packages:
//  `Fuaran.UI.OpStream.Abstractions` sits above both `Fuaran.UI` and
//  `Fuaran.UI.Ops`, owns `encodeNode` + `encodeOp`; the decoder is the
//  inverse and needs the same access. The §4l down-shift portability
//  story is preserved — Fable-only consumers who only need typed-tree
//  construction don't pull the decoder; consumers who already consume
//  `Fuaran.UI.Ops` for `Apply.apply` get the decoder at the same tier they
//  already pay for. See `docs/migrations/12-E-0-json-decoder.md` "Module
//  placement" for the discarded alternatives.
//
//  Storage-shape erasure. Produces `Node<obj>` / `TreeOp<obj>` — the
//  storage-shape direction pinned in `docs/migrations/12-Z-op-stream.md`.
//  Typed callers (downstream AI consumer + module) re-attach a real `'Msg`
//  via their `moduleMsgDecoder: JVal -> 'Msg`.
//
//  Closure / opaque sentinels. Fields the encoder rendered as
//  `"<closure>"` decode to placeholder closures returning the sentinel
//  string (or `Action.Chain []` for callback slots that expect an
//  `Action<obj>`); `"<opaque>"` payloads decode to `box "<opaque>"`. The
//  decoder MUST NOT attempt to reconstruct the original CLR type.
//
//  Object-key order tolerance. The encoder Ordinal-sorts keys; this
//  decoder accepts any source order — the structural shape is what
//  matters, not the bytes. Symmetric: encoder enforces order, decoder
//  accepts any order.
//
//  Number-edge handling. `"NaN"` / `"Infinity"` / `"-Infinity"` string
//  sentinels decode back to the IEEE-754 special values for float fields.
//  Fable.SimpleJson surfaces every number as `JNumber float`; integer
//  slots truncate via `int` and the round-trip is exact for the int-range
//  subset.
//
//  Forward-coupling rule (load-bearing). Adding a new `NodeKind` / `Spec`
//  / `TreeOp` case MUST update this decoder in the
//  same commit — a missing case becomes an `UNKNOWN_DU_CASE` defect at
//  runtime rather than a compile-time error (the decoder consumes JSON,
//  not the F# DU). Same forward-coupling the canonical-JSON encoder
//  carries; both move together.
//
//  Fable-compatible. No reflection, no `System.Text.Json` (server-only),
//  no `Newtonsoft.Json`. Uses `Fable.SimpleJson` (already pinned in
//  `Directory.Packages.props`, already consumed by `Fuaran.UI.Renderer` +
//  the downstream orchestration tier) as the JSON-syntax parser.
// ============================================================================

// F# 10 nullness (FS3261) fires throughout the obj-erasure seam: every
// `box`ed primitive flows through `obj | null` and the decoder's return
// shapes (`Node<obj>`, `TreeOp<obj>`) erase nullability at the boundary
// the way `Fuaran.fs`'s `Column.erase` does. The whole file IS the
// type-erasure surface — `decodeObj` / `decodeJVal` /
// `bindingGeneric<obj>` all box arbitrary JSON shapes into typed obj
// slots. Scope-localising `#nowarn "3261"` would require ~15 paired
// brackets across mutually-recursive groups; the renderer takes
// `<Nullable>disable</Nullable>` for similar pre-nullness library reasons.
// File-scope suppression here, justified by the seam.
#nowarn "3261"

open System
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Local JSON AST + parser ─────────────────────────────────────────────
//
// Design point: `Fable.SimpleJson` is the project's go-to
// JSON parser elsewhere (downstream orchestration tier, renderer), but its
// implementation routes through `Fable.Parsimmon` which is *Fable-only*
// — `Parsimmon.get_digit` calls `Fable.Core.JsInterop.import`, which
// throws `System.Exception: JS only` under .NET. The decoder needs to
// run on both Fable (client-side AI emit gate, eval harness) AND .NET
// (op-stream replay, server-side AI tool dispatch, the Expecto test
// suite). The catalog sample's narrow-decoder pattern proves a
// hand-rolled parser works under both runtimes; this module ports the
// same shape into the canonical decoder.
//
// The parser produces the same `Json` DU shape `Fable.SimpleJson`
// uses (`JNull | JBool | JNumber | JString | JArray | JObject of Map<string, Json>`)
// so the per-NodeKind dispatch downstream stays unchanged.
//
// Anti-pattern note: "Don't add Newtonsoft.Json
// or System.Text.Json. Both are server-only." Hand-rolling is the
// universal answer.

type private Json =
    | JNull
    | JBool of bool
    | JNumber of float
    | JString of string
    | JArray of Json list
    | JObject of Map<string, Json>

type private ParseState = { Text: string; mutable Pos: int }

let private peek (s: ParseState) : char =
    if s.Pos < s.Text.Length then s.Text[s.Pos] else ' '

let private advance (s: ParseState) : unit = s.Pos <- s.Pos + 1

let private skipWs (s: ParseState) : unit =
    while s.Pos < s.Text.Length
          && (let c = s.Text[s.Pos] in c = ' ' || c = '\t' || c = '\n' || c = '\r') do
        advance s

let private parseError (s: ParseState) (msg: string) : Result<'a, string> =
    Error(sprintf "parse error at offset %d: %s" s.Pos msg)

let private expectChar (s: ParseState) (ch: char) : Result<unit, string> =
    if peek s = ch then
        advance s
        Ok()
    else
        parseError s (sprintf "expected '%c' but found '%c'" ch (peek s))

let private parseStringRaw (s: ParseState) : Result<string, string> =
    match expectChar s '"' with
    | Error e -> Error e
    | Ok() ->
        let sb = System.Text.StringBuilder()
        let mutable finished = false
        let mutable error: string option = None

        while not finished && error.IsNone do
            if s.Pos >= s.Text.Length then
                error <- Some "unterminated string"
            else
                let c = s.Text[s.Pos]
                advance s

                if c = '"' then
                    finished <- true
                elif c = '\\' then
                    if s.Pos >= s.Text.Length then
                        error <- Some "unterminated escape"
                    else
                        let esc = s.Text[s.Pos]
                        advance s

                        match esc with
                        | '"' -> sb.Append '"' |> ignore
                        | '\\' -> sb.Append '\\' |> ignore
                        | '/' -> sb.Append '/' |> ignore
                        | 'b' -> sb.Append '\b' |> ignore
                        | 'f' -> sb.Append '\f' |> ignore
                        | 'n' -> sb.Append '\n' |> ignore
                        | 'r' -> sb.Append '\r' |> ignore
                        | 't' -> sb.Append '\t' |> ignore
                        | 'u' ->
                            if s.Pos + 4 > s.Text.Length then
                                error <- Some "incomplete \\u escape"
                            else
                                let hex = s.Text.Substring(s.Pos, 4)
                                s.Pos <- s.Pos + 4
                                // Hex→int by hand; Fable rejects the
                                // (string, NumberStyles, IFormatProvider)
                                // TryParse overload. BMP-only is
                                // sufficient for canonical-JSON shapes.
                                let mutable code = 0
                                let mutable hexOk = true

                                for ch in hex do
                                    let digit =
                                        if ch >= '0' && ch <= '9' then
                                            int ch - int '0'
                                        elif ch >= 'a' && ch <= 'f' then
                                            int ch - int 'a' + 10
                                        elif ch >= 'A' && ch <= 'F' then
                                            int ch - int 'A' + 10
                                        else
                                            hexOk <- false
                                            0

                                    code <- code * 16 + digit

                                if hexOk then
                                    sb.Append(char code) |> ignore
                                else
                                    error <- Some(sprintf "invalid \\u escape '%s'" hex)
                        | other -> error <- Some(sprintf "unknown escape '\\%c'" other)
                else
                    sb.Append c |> ignore

        match error with
        | Some e -> parseError s e
        | None -> Ok(sb.ToString())

let private parseNumberRaw (s: ParseState) : Result<float, string> =
    let start = s.Pos

    let valid (c: char) =
        c = '-' || c = '+' || c = '.' || c = 'e' || c = 'E' || (c >= '0' && c <= '9')

    while s.Pos < s.Text.Length && valid s.Text[s.Pos] do
        advance s

    let slice = s.Text.Substring(start, s.Pos - start)
    // Fable's `Double.TryParse` rejects the multi-arg overload — use the
    // single-arg form. AI emissions are en-US-shaped (dot-decimal); the
    // invariant-culture distinction is moot.
    match System.Double.TryParse slice with
    | true, n -> Ok n
    | false, _ -> parseError s (sprintf "invalid number '%s'" slice)

let rec private parseValue (s: ParseState) : Result<Json, string> =
    skipWs s

    match peek s with
    | '{' -> parseObjectValue s
    | '[' -> parseArrayValue s
    | '"' -> parseStringRaw s |> Result.map JString
    | 't' ->
        if s.Pos + 4 <= s.Text.Length && s.Text.Substring(s.Pos, 4) = "true" then
            s.Pos <- s.Pos + 4
            Ok(JBool true)
        else
            parseError s "expected 'true'"
    | 'f' ->
        if s.Pos + 5 <= s.Text.Length && s.Text.Substring(s.Pos, 5) = "false" then
            s.Pos <- s.Pos + 5
            Ok(JBool false)
        else
            parseError s "expected 'false'"
    | 'n' ->
        if s.Pos + 4 <= s.Text.Length && s.Text.Substring(s.Pos, 4) = "null" then
            s.Pos <- s.Pos + 4
            Ok JNull
        else
            parseError s "expected 'null'"
    | _ -> parseNumberRaw s |> Result.map JNumber

and private parseObjectValue (s: ParseState) : Result<Json, string> =
    match expectChar s '{' with
    | Error e -> Error e
    | Ok() ->
        skipWs s

        if peek s = '}' then
            advance s
            Ok(JObject Map.empty)
        else
            let mutable acc: (string * Json) list = []
            let mutable error: string option = None
            let mutable finished = false

            while not finished && error.IsNone do
                skipWs s

                match parseStringRaw s with
                | Error e -> error <- Some e
                | Ok key ->
                    skipWs s

                    match expectChar s ':' with
                    | Error e -> error <- Some e
                    | Ok() ->
                        match parseValue s with
                        | Error e -> error <- Some e
                        | Ok v ->
                            acc <- (key, v) :: acc
                            skipWs s

                            match peek s with
                            | ',' -> advance s
                            | '}' ->
                                advance s
                                finished <- true
                            | other -> error <- Some(sprintf "expected ',' or '}' but found '%c'" other)

            match error with
            | Some e -> parseError s e
            | None -> Ok(JObject(Map.ofList acc))

and private parseArrayValue (s: ParseState) : Result<Json, string> =
    match expectChar s '[' with
    | Error e -> Error e
    | Ok() ->
        skipWs s

        if peek s = ']' then
            advance s
            Ok(JArray [])
        else
            let mutable acc: Json list = []
            let mutable error: string option = None
            let mutable finished = false

            while not finished && error.IsNone do
                match parseValue s with
                | Error e -> error <- Some e
                | Ok v ->
                    acc <- v :: acc
                    skipWs s

                    match peek s with
                    | ',' -> advance s
                    | ']' ->
                        advance s
                        finished <- true
                    | other -> error <- Some(sprintf "expected ',' or ']' but found '%c'" other)

            match error with
            | Some e -> parseError s e
            | None -> Ok(JArray(List.rev acc))

let private tryParse (input: string) : Result<Json, string> =
    if isNull input then
        Error "input is null"
    else
        let state = { Text = input; Pos = 0 }
        skipWs state

        if state.Pos >= state.Text.Length then
            Error "input is empty"
        else
            parseValue state

// ─── DecodeError surface ─────────────────────────────────────────────────

/// Stable AI-friendly discriminator for decode-time failures. The string
/// form (rendered via `DecodeErrorCode.toString`) lands in `DecodeError.Code`
/// and is what Gate 1 pattern-matches on.
[<RequireQualifiedAccess>]
type DecodeErrorCode =
    /// JSON syntax violation (surfaced by the underlying parser).
    | INVALID_JSON
    /// Required object key absent.
    | MISSING_FIELD
    /// Object value present but wrong JSON kind under the key
    /// (e.g. string where object expected, array where string expected).
    | WRONG_TYPE
    /// `"$type"` discriminator value not recognised for the DU position.
    | UNKNOWN_DU_CASE
    /// Top-level `"kind"` discriminator not a recognised node kind — i.e.
    /// not one of the flat Layout / Display / Input / Visualisation
    /// primitives (WIRE_FORMAT §3.2) nor `Custom` / `ErrorBoundary` /
    /// `FragmentDecl` / `FragmentRef`.
    | WRONG_NODE_KIND
    /// `"id"` field present but empty — same defect
    /// `PreEmitValidate.EmptyNodeId` catches downstream after apply.
    | EMPTY_NODE_ID

module DecodeErrorCode =
    let toString (code: DecodeErrorCode) : string =
        match code with
        | DecodeErrorCode.INVALID_JSON -> "INVALID_JSON"
        | DecodeErrorCode.MISSING_FIELD -> "MISSING_FIELD"
        | DecodeErrorCode.WRONG_TYPE -> "WRONG_TYPE"
        | DecodeErrorCode.UNKNOWN_DU_CASE -> "UNKNOWN_DU_CASE"
        | DecodeErrorCode.WRONG_NODE_KIND -> "WRONG_NODE_KIND"
        | DecodeErrorCode.EMPTY_NODE_ID -> "EMPTY_NODE_ID"

/// AI-recoverable decode-time failure. Mirrors the §4d AI-recovery JSON
/// envelope vocabulary from `Fuaran.UI.Ops.ErrorRender` so eval gate-1
/// failures stay in the same shape downstream consumers (gates 2/3/4 +
/// AI-driving-the-UI consumer) read.
type DecodeError =
    { Code: string
      Path: string
      Message: string
      ExpectedShape: string option }

module DecodeError =
    /// Construct a `DecodeError` from the typed code + path + message.
    let create (code: DecodeErrorCode) (path: string) (message: string) (expectedShape: string option) : DecodeError =
        { Code = DecodeErrorCode.toString code
          Path = path
          Message = message
          ExpectedShape = expectedShape }

// ─── Internal helpers ─────────────────────────────────────────────────────

let private closureSentinel = "<closure>"
let private opaqueSentinel = "<opaque>"

let private err code path message expected : Result<'a, DecodeError> =
    Error(DecodeError.create code path message expected)

let private missingField (path: string) (key: string) (expected: string) : Result<'a, DecodeError> =
    err DecodeErrorCode.MISSING_FIELD (path + "." + key) (sprintf "missing required field '%s'" key) (Some expected)

let private wrongType (path: string) (expected: string) : Result<'a, DecodeError> =
    err DecodeErrorCode.WRONG_TYPE path (sprintf "expected %s" expected) (Some expected)

let private unknownDuCase (path: string) (got: string) (expected: string) : Result<'a, DecodeError> =
    err DecodeErrorCode.UNKNOWN_DU_CASE (path + ".$type") (sprintf "unknown discriminator '%s'" got) (Some expected)

/// Bridge the decoder's `Json` AST to `Fuaran.Core.JVal` so a `Binding.Transform`'s columnar
/// source + pipeline decode through the shared `Fuaran.Core` codecs (Phase 282 — the Compute
/// layer). Both ASTs already share the canonical `$type` discipline; the only impedance is Core's
/// `JInt`/`JFloat` split — an integral-valued JSON number bridges to `JInt` (Core's decoders widen
/// `JInt`→`JFloat` where a float is expected, but require `JInt` where an int is). The compute wire
/// carries no `null` (Column uses a validity mask), so `JNull` is unreachable here.
let rec private jsonToJVal (j: Json) : Fuaran.Core.JVal =
    match j with
    | JNull -> Fuaran.Core.JStr ""
    | JBool b -> Fuaran.Core.JBool b
    | JNumber n ->
        if
            not (System.Double.IsNaN n)
            && not (System.Double.IsInfinity n)
            && floor n = n
            && abs n <= 2147483647.0
        then
            Fuaran.Core.JInt(int n)
        else
            Fuaran.Core.JFloat n
    | JString s -> Fuaran.Core.JStr s
    | JArray xs -> Fuaran.Core.JArr(xs |> List.map jsonToJVal)
    | JObject m -> Fuaran.Core.JObj(m |> Map.toList |> List.map (fun (k, v) -> k, jsonToJVal v))

/// The same AST bridge for the JSON-valued PAYLOAD positions (Custom props,
/// Action.Notify / SetState / AiTool payloads, I18n args, a wire-form
/// `UpdateProp` value): identical number policy, but `null` is REJECTED at any
/// depth — the Fuaran wire model has no null (omit the field instead), and
/// `JVal` makes that unrepresentable by construction. The error names the rule
/// so an AI author recovers by omission, not by retrying encodings of null.
let rec private jsonToJValStrict (path: string) (j: Json) : Result<Fuaran.Core.JVal, DecodeError> =
    match j with
    | JNull ->
        Error(
            DecodeError.create
                DecodeErrorCode.WRONG_TYPE
                path
                "null is not representable in the Fuaran wire model — omit the field instead"
                (Some "any JSON value except null (rule 12: the wire model has no null; omit the field to mean absent)")
        )
    | JBool b -> Ok(Fuaran.Core.JBool b)
    | JNumber n ->
        if
            not (System.Double.IsNaN n)
            && not (System.Double.IsInfinity n)
            && floor n = n
            && abs n <= 2147483647.0
        then
            Ok(Fuaran.Core.JInt(int n))
        else
            Ok(Fuaran.Core.JFloat n)
    | JString s -> Ok(Fuaran.Core.JStr s)
    | JArray xs ->
        let folded =
            (Ok [], List.indexed xs)
            ||> List.fold (fun acc (i, x) ->
                match acc with
                | Error e -> Error e
                | Ok items ->
                    jsonToJValStrict (path + "[" + string i + "]") x
                    |> Result.map (fun v -> v :: items))

        folded |> Result.map (fun items -> Fuaran.Core.JArr(List.rev items))
    | JObject m ->
        let folded =
            (Ok [], m |> Map.toList)
            ||> List.fold (fun acc (k, v) ->
                match acc with
                | Error e -> Error e
                | Ok fields -> jsonToJValStrict (path + "." + k) v |> Result.map (fun jv -> (k, jv) :: fields))

        folded |> Result.map (fun fields -> Fuaran.Core.JObj(List.rev fields))

/// Map a `Fuaran.Core.ColumnError` (the compute codecs' six-code envelope) into this host's
/// `DecodeError` at `path` — surfaced as `WRONG_TYPE` (the closest host code for a
/// structurally-parsed but invalid compute sub-tree), with the Core failure as the message.
let private coreError (path: string) (ce: Fuaran.Core.ColumnError) : DecodeError =
    DecodeError.create DecodeErrorCode.WRONG_TYPE path (Fuaran.Core.ColumnCodec.errorString ce) None

let private requireObject (path: string) (j: Json) : Result<Map<string, Json>, DecodeError> =
    match j with
    | JObject fields -> Ok fields
    | _ -> wrongType path "JSON object"

/// Lenient AI-ingest (WIRE_FORMAT.md §3.6, generalised 2026-07-18): a Static
/// envelope wrapped around a PLAIN scalar unwraps before the scalar readers —
/// the inverse of the bare-scalar-in-Binding-slot confusion, applied at every
/// plain-scalar position in one place (the 0.1.6 pilot found `emphasis`
/// wrapped after `indeterminate` was fixed site-locally; the confusion is
/// generic, so the unwrap is too). Unambiguous: at a plain-scalar position
/// the envelope has exactly one reading. Objects that are NOT a well-formed
/// Static envelope pass through untouched and fail with the normal error.
let private unwrapStaticEnvelope (j: Json) : Json =
    match j with
    | JObject fields ->
        match Map.tryFind "$type" fields, Map.tryFind "value" fields with
        | Some(JString "Static"), Some inner -> inner
        | _ -> j
    | _ -> j

let private requireString (path: string) (j: Json) : Result<string, DecodeError> =
    match unwrapStaticEnvelope j with
    | JString s -> Ok s
    | _ -> wrongType path "JSON string"

let private requireBool (path: string) (j: Json) : Result<bool, DecodeError> =
    match unwrapStaticEnvelope j with
    | JBool b -> Ok b
    | _ -> wrongType path "JSON boolean"

let private requireFloat (path: string) (j: Json) : Result<float, DecodeError> =
    match unwrapStaticEnvelope j with
    | JNumber n -> Ok n
    | JString "NaN" -> Ok Double.NaN
    | JString "Infinity" -> Ok Double.PositiveInfinity
    | JString "-Infinity" -> Ok Double.NegativeInfinity
    | _ -> wrongType path "JSON number (or 'NaN' / 'Infinity' / '-Infinity' sentinel string)"

let private requireInt (path: string) (j: Json) : Result<int, DecodeError> =
    match unwrapStaticEnvelope j with
    | JNumber n -> Ok(int n)
    | _ -> wrongType path "JSON number (integer)"

let private requireArray (path: string) (j: Json) : Result<Json list, DecodeError> =
    match j with
    | JArray xs -> Ok xs
    | _ -> wrongType path "JSON array"

let private tryField (fields: Map<string, Json>) (key: string) : Json option = Map.tryFind key fields

let private requireField
    (path: string)
    (fields: Map<string, Json>)
    (key: string)
    (expected: string)
    : Result<Json, DecodeError> =
    match Map.tryFind key fields with
    | Some j -> Ok j
    | None -> missingField path key expected

// ─── Lenient-ingest FIELD-NAME aliases (decode-only; 2026-07-17) ───────────
// Phase 460 aliased enum VALUES (Positive→Success, Strong→Loud — see the
// variant decoders below); the 2026-07-16 Kimi smokes showed the same
// web-prior guessing on FIELD NAMES — `Navigate` emitted with `href` for the
// required `route`, twice, identically. Same contract as the value aliases:
// decode-only (the canonical encoder never emits an alias; re-encode
// normalises), and faithful same-concept mappings only — a foreign name for
// the SAME concept is aliased (href→route), a name betraying a DIFFERENT
// concept is NOT (Progress `value`/`percent` vs `fraction`: the 0–100 prior
// would silently mis-scale by 100×). The canonical name always wins when both
// are present. The full alias table + rationale live in WIRE_FORMAT.md's
// lenient-ingest section; the corpus's lenient-accept fixtures pin every
// host to the same set.
let private requireFieldAliased
    (path: string)
    (fields: Map<string, Json>)
    (canonical: string)
    (aliases: string list)
    (expected: string)
    : Result<Json, DecodeError> =
    match Map.tryFind canonical fields with
    | Some j -> Ok j
    | None ->
        match aliases |> List.tryPick (fun a -> Map.tryFind a fields) with
        | Some j -> Ok j
        | None -> missingField path canonical expected

let private optFieldAliased (fields: Map<string, Json>) (canonical: string) (aliases: string list) : Json option =
    match Map.tryFind canonical fields with
    | Some j -> Some j
    | None -> aliases |> List.tryPick (fun a -> Map.tryFind a fields)

let private requireDiscriminator (path: string) (fields: Map<string, Json>) : Result<string, DecodeError> =
    match Map.tryFind "$type" fields with
    | Some(JString s) -> Ok s
    | Some _ -> wrongType (path + ".$type") "JSON string discriminator"
    | None -> missingField path "$type" "DU object must carry a '$type' discriminator string"

/// Result-list traverse — fail-fast over a typed mapper.
let private traverse (f: 'a -> Result<'b, DecodeError>) (xs: 'a list) : Result<'b list, DecodeError> =
    let rec loop acc =
        function
        | [] -> Ok(List.rev acc)
        | x :: rest ->
            match f x with
            | Ok y -> loop (y :: acc) rest
            | Error e -> Error e

    loop [] xs

let private traverseIndexed (f: int -> 'a -> Result<'b, DecodeError>) (xs: 'a list) : Result<'b list, DecodeError> =
    let rec loop i acc =
        function
        | [] -> Ok(List.rev acc)
        | x :: rest ->
            match f i x with
            | Ok y -> loop (i + 1) (y :: acc) rest
            | Error e -> Error e

    loop 0 [] xs

/// Decode the `args` array of a `Binding.Invoke` / `Action.Invoke` (Phase 283) — `[{"addr","value"}]`
/// scalar pairs. Shared by both decoders.
let private decodeInvokeArgs (path: string) (j: Json) : Result<(string * string) list, DecodeError> =
    match j with
    | JArray items ->
        items
        |> traverseIndexed (fun i el ->
            let p = sprintf "%s[%d]" path i

            match requireObject p el with
            | Error e -> Error e
            | Ok m ->
                match requireField p m "addr" "invoke arg addr string" with
                | Error e -> Error e
                | Ok addrJ ->
                    match requireString (p + ".addr") addrJ with
                    | Error e -> Error e
                    | Ok addr ->
                        match requireField p m "value" "invoke arg value string" with
                        | Error e -> Error e
                        | Ok vJ -> requireString (p + ".value") vJ |> Result.map (fun v -> addr, v))
    | _ -> wrongType path "JSON array of invoke args"

// ─── Placeholder Node for OnError slot (encoder emits sentinel only) ────

let private placeholderClosureNode: Node<obj> =
    { Id = NodeId closureSentinel
      Kind = NodeKind.Display(DisplayKind.Markdown { Text = TextSource.Literal closureSentinel })
      State =
        { OnLoading = None
          OnEmpty = None
          OnError = None }
      Style =
        { Tone = ToneVariant.Default
          Weight = StyleWeight.Standard
          Emphasis = Emphasis.Normal
          Role = StyleRole.None
          Voice = FontVoice.Default }
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

// ─── Variant DU decoders ─────────────────────────────────────────────────

let private decodeOrientation (path: string) (j: Json) : Result<Orientation, DecodeError> =
    match j with
    | JString "Vertical" -> Ok Vertical
    | JString "Horizontal" -> Ok Horizontal
    // Lenient-ingest aliases (decode-only) — the CSS flex-direction prior:
    // a row lays out horizontally, a column vertically. Same-concept mapping.
    | JString "Row"
    | JString "row" -> Ok Horizontal
    | JString "Column"
    | JString "column" -> Ok Vertical
    | JString s -> unknownDuCase path s "Vertical | Horizontal"
    | _ -> wrongType path "JSON string (Orientation)"

let private decodeFileReadEncoding (path: string) (j: Json) : Result<FileReadEncoding, DecodeError> =
    match j with
    | JString "Text" -> Ok FileReadEncoding.Text
    | JString "Base64" -> Ok FileReadEncoding.Base64
    | JString "DataUrl" -> Ok FileReadEncoding.DataUrl
    | JString s -> unknownDuCase path s "Text | Base64 | DataUrl"
    | _ -> wrongType path "JSON string (FileReadEncoding)"

let private decodeBadgeVariant (path: string) (j: Json) : Result<BadgeVariant, DecodeError> =
    match j with
    | JString "Neutral" -> Ok BadgeVariant.Neutral
    | JString "Brand" -> Ok BadgeVariant.Brand
    | JString "Success" -> Ok BadgeVariant.Success
    | JString "Warning" -> Ok BadgeVariant.Warning
    | JString "Critical" -> Ok BadgeVariant.Critical
    | JString "Info" -> Ok BadgeVariant.Info
    // Lenient-ingest aliases (decode-only): 'Default' is the universal
    // no-special-variant prior (21 observed guesses across the variant enums);
    // BadgeVariant's identity case is Neutral. Danger is the Bootstrap prior.
    | JString "Default" -> Ok BadgeVariant.Neutral
    | JString "Danger" -> Ok BadgeVariant.Critical
    | JString s -> unknownDuCase path s "Neutral | Brand | Success | Warning | Critical | Info"
    | _ -> wrongType path "JSON string (BadgeVariant)"

let private decodeButtonVariant (path: string) (j: Json) : Result<ButtonVariant, DecodeError> =
    match j with
    | JString "Primary" -> Ok ButtonVariant.Primary
    | JString "Secondary" -> Ok ButtonVariant.Secondary
    | JString "Tertiary" -> Ok ButtonVariant.Tertiary
    | JString "Destructive" -> Ok ButtonVariant.Destructive
    // Lenient-ingest alias (decode-only): Bootstrap's 'Danger' names the same
    // concept as Destructive (the red delete-class button).
    | JString "Danger" -> Ok ButtonVariant.Destructive
    | JString s -> unknownDuCase path s "Primary | Secondary | Tertiary | Destructive"
    | _ -> wrongType path "JSON string (ButtonVariant)"

let private decodeImageVariant (path: string) (j: Json) : Result<ImageVariant, DecodeError> =
    match j with
    | JString "Default" -> Ok ImageVariant.Default
    | JString "Avatar" -> Ok ImageVariant.Avatar
    | JString "Rounded" -> Ok ImageVariant.Rounded
    | JString s -> unknownDuCase path s "Default | Avatar | Rounded"
    | _ -> wrongType path "JSON string (ImageVariant)"

let private decodeScrollOrientation (path: string) (j: Json) : Result<ScrollOrientation, DecodeError> =
    match j with
    | JString "Vertical" -> Ok ScrollOrientation.Vertical
    | JString "Horizontal" -> Ok ScrollOrientation.Horizontal
    | JString "Both" -> Ok ScrollOrientation.Both
    | JString s -> unknownDuCase path s "Vertical | Horizontal | Both"
    | _ -> wrongType path "JSON string (ScrollOrientation)"

let private decodeDateVariant (path: string) (j: Json) : Result<DateVariant, DecodeError> =
    match j with
    | JString "Date" -> Ok DateVariant.Date
    | JString "Time" -> Ok DateVariant.Time
    | JString "DateTime" -> Ok DateVariant.DateTime
    | JString s -> unknownDuCase path s "Date | Time | DateTime"
    | _ -> wrongType path "JSON string (DateVariant)"

let private decodeMathDisplay (path: string) (j: Json) : Result<MathDisplay, DecodeError> =
    match j with
    | JString "Inline" -> Ok MathDisplay.Inline
    | JString "Block" -> Ok MathDisplay.Block
    | JString s -> unknownDuCase path s "Inline | Block"
    | _ -> wrongType path "JSON string (MathDisplay)"

let private decodeHeadingVariant (path: string) (j: Json) : Result<HeadingVariant, DecodeError> =
    match j with
    | JString "Standard" -> Ok HeadingVariant.Standard
    | JString "Eyebrow" -> Ok HeadingVariant.Eyebrow
    | JString "Caption" -> Ok HeadingVariant.Caption
    | JString "Lead" -> Ok HeadingVariant.Lead
    // Lenient-ingest alias (decode-only): 'Default' → the identity case. The
    // OTHER observed guesses ('Title', 'Page', 'Section') stay rejects — their
    // mapping is ambiguous (Standard? Lead?), so aliasing would guess intent.
    | JString "Default" -> Ok HeadingVariant.Standard
    | JString s -> unknownDuCase path s "Standard | Eyebrow | Caption | Lead"
    | _ -> wrongType path "JSON string (HeadingVariant)"

let private decodeTone (path: string) (j: Json) : Result<ToneVariant, DecodeError> =
    match j with
    | JString "Default" -> Ok Default
    | JString "Subdued" -> Ok Subdued
    | JString "Brand" -> Ok Brand
    | JString "Success" -> Ok Success
    | JString "Warning" -> Ok Warning
    | JString "Critical" -> Ok Critical
    | JString "Info" -> Ok Info
    // Phase 460 — lenient-ingest aliases (decode-only; never encoded — canonical
    // re-encode normalises to the DU case names). Faithful semantic mappings only:
    // Positive→Success, Danger/Negative→Critical, Neutral→Default. Documented in
    // WIRE_FORMAT.md's lenient-ingest table; `SchemaGen` stays strict-canonical.
    | JString "Positive" -> Ok Success
    | JString "Danger"
    | JString "Negative" -> Ok Critical
    | JString "Neutral" -> Ok Default
    | JString s -> unknownDuCase path s "Default | Subdued | Brand | Success | Warning | Critical | Info"
    | _ -> wrongType path "JSON string (ToneVariant)"

let private decodeWeight (path: string) (j: Json) : Result<StyleWeight, DecodeError> =
    match j with
    | JString "Compact" -> Ok Compact
    | JString "Standard" -> Ok Standard
    | JString "Spacious" -> Ok Spacious
    | JString s -> unknownDuCase path s "Compact | Standard | Spacious"
    | _ -> wrongType path "JSON string (StyleWeight)"

let private decodeEmphasis (path: string) (j: Json) : Result<Emphasis, DecodeError> =
    match j with
    | JString "Quiet" -> Ok Quiet
    | JString "Normal" -> Ok Normal
    | JString "Loud" -> Ok Loud
    // Phase 460 — lenient-ingest aliases (decode-only; never encoded). Prominence
    // intent survives: Strong/Bold→Loud, Subtle/Muted→Quiet. `StyleWeight` is
    // deliberately NOT aliased (Bold/Heavy is font-weight intent, but the language
    // means density Compact|Standard|Spacious — a mapping would misread the author).
    | JString "Strong"
    | JString "Bold" -> Ok Loud
    | JString "Subtle"
    | JString "Muted" -> Ok Quiet
    // 2026-07-19 collision sweep — `emphasis` is a same-name cross-vocabulary
    // collision (style ENUM here vs behavioural BOOL on Fact/LabelValueRow);
    // models cross it in both directions. A bool in the enum slot projects
    // one-to-one: true ⇒ Loud, false ⇒ Normal. The bool sites' direction
    // lives in `decodeEmphasisFlag`.
    | JBool true -> Ok Loud
    | JBool false -> Ok Normal
    | JString s -> unknownDuCase path s "Quiet | Normal | Loud"
    | _ -> wrongType path "JSON string (Emphasis)"

/// The behavioural `emphasis` BOOL (Fact / LabelValueRow) — the other half of
/// the same-name collision with the `Emphasis` style enum. ONE shared reader
/// for every bool site (the 0.2.2 coercion lived only on LabelValueRow and
/// only for the exact enum spellings — Fact hard-failed, and the Phase-460
/// alias set never carried over; the 2026-07-19 sweep closed the asymmetry):
/// booleans pass through; the enum AND its aliases project one-to-one
/// (Loud/Strong/Bold ⇒ true, Normal/Quiet/Subtle/Muted ⇒ false); any other
/// string is the didactic reject naming both vocabularies.
let private decodeEmphasisFlag (path: string) (j: Json) : Result<bool, DecodeError> =
    match j with
    | JBool b -> Ok b
    | JString("Loud" | "Strong" | "Bold") -> Ok true
    | JString("Normal" | "Quiet" | "Subtle" | "Muted") -> Ok false
    | JString other ->
        err
            DecodeErrorCode.WRONG_TYPE
            path
            (sprintf
                "expected JSON boolean, got '%s' — this `emphasis` is a BOOL (is this an emphasised row/fact?); the Emphasis style enum (Quiet|Normal|Loud) lives on style/Metric.emphasis. Write true or false"
                other)
            (Some "JSON boolean")
    | _ -> requireBool path j

// Phase 147 — the additive style-role / font-voice DUs. Optional on the wire
// (omitted at default); the style decoder restores the default on absence.
let private decodeStyleRole (path: string) (j: Json) : Result<StyleRole, DecodeError> =
    match j with
    | JString "None" -> Ok StyleRole.None
    | JString "Eyebrow" -> Ok StyleRole.Eyebrow
    | JString "Data" -> Ok StyleRole.Data
    | JString "Lede" -> Ok StyleRole.Lede
    | JString "Caption" -> Ok StyleRole.Caption
    | JString s -> unknownDuCase path s "None | Eyebrow | Data | Lede | Caption"
    | _ -> wrongType path "JSON string (StyleRole)"

let private decodeFontVoice (path: string) (j: Json) : Result<FontVoice, DecodeError> =
    match j with
    | JString "Default" -> Ok FontVoice.Default
    | JString "Display" -> Ok FontVoice.Display
    | JString "Structural" -> Ok FontVoice.Structural
    | JString s -> unknownDuCase path s "Default | Display | Structural"
    | _ -> wrongType path "JSON string (FontVoice)"

let private decodeColumnWidth (path: string) (j: Json) : Result<ColumnWidth, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Auto" -> Ok ColumnWidth.Auto
        | Ok "Fixed" ->
            match requireField path fields "pixels" "integer pixel count" with
            | Error e -> Error e
            | Ok v -> requireInt (path + ".pixels") v |> Result.map ColumnWidth.Fixed
        | Ok "Flex" ->
            match requireField path fields "weight" "float weight" with
            | Error e -> Error e
            | Ok v -> requireFloat (path + ".weight") v |> Result.map ColumnWidth.Flex
        | Ok s -> unknownDuCase path s "Auto | Fixed | Flex"

let private decodeChartKind (path: string) (j: Json) : Result<ChartKind, DecodeError> =
    match j with
    | JString "Line" -> Ok ChartKind.Line
    | JString "Bar" -> Ok ChartKind.Bar
    | JString "Area" -> Ok ChartKind.Area
    | JString "Pie" -> Ok ChartKind.Pie
    | JString "Scatter" -> Ok ChartKind.Scatter
    | JString "Heatmap" -> Ok ChartKind.Heatmap
    | JString s -> unknownDuCase path s "Line | Bar | Area | Pie | Scatter | Heatmap"
    | _ -> wrongType path "JSON string (ChartKind)"

let private decodeAriaRole (path: string) (j: Json) : Result<AriaRole, DecodeError> =
    match j with
    | JString "button" -> Ok AriaRole.Button
    | JString "link" -> Ok AriaRole.Link
    | JString "dialog" -> Ok AriaRole.Dialog
    | JString "alert" -> Ok AriaRole.Alert
    | JString "status" -> Ok AriaRole.Status
    | JString "banner" -> Ok AriaRole.Banner
    | JString "navigation" -> Ok AriaRole.Navigation
    | JString "main" -> Ok AriaRole.Main
    | JString "form" -> Ok AriaRole.Form
    | JString "region" -> Ok AriaRole.Region
    | JString "heading" -> Ok AriaRole.Heading
    | JString "progressbar" -> Ok AriaRole.Progressbar
    | JString "tab" -> Ok AriaRole.Tab
    | JString "tablist" -> Ok AriaRole.Tablist
    | JString "tabpanel" -> Ok AriaRole.Tabpanel
    // Any other string is treated as AriaRole.Custom — the encoder
    // emits Custom roles as the raw string, indistinguishable on the
    // wire from the named cases. v1 limitation: a future encoder
    // refinement that tags Custom explicitly would let the decoder
    // recover the original case shape; today, decode is best-effort.
    | JString s -> Ok(AriaRole.Custom s)
    | _ -> wrongType path "JSON string (ARIA role)"

let private decodeLiveRegion (path: string) (j: Json) : Result<LiveRegionKind, DecodeError> =
    match j with
    | JString "polite" -> Ok LiveRegionKind.Polite
    | JString "assertive" -> Ok LiveRegionKind.Assertive
    | JString "off" -> Ok LiveRegionKind.Off
    | JString s -> unknownDuCase path s "polite | assertive | off"
    | _ -> wrongType path "JSON string (LiveRegionKind)"

// ─── CellFormat / CellValue ──────────────────────────────────────────────

let private decodeCellFormat (path: string) (j: Json) : Result<CellFormat, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "None" -> Ok CellFormat.None
        | Ok "Number" ->
            match tryField fields "decimals" with
            | None -> Ok(CellFormat.Number None)
            | Some j ->
                requireInt (path + ".decimals") j
                |> Result.map (fun d -> CellFormat.Number(Some d))
        | Ok "Currency" ->
            match requireField path fields "code" "ISO currency code string" with
            | Error e -> Error e
            | Ok j -> requireString (path + ".code") j |> Result.map CellFormat.Currency
        | Ok "Percent" ->
            match tryField fields "decimals" with
            | None -> Ok(CellFormat.Percent None)
            | Some j ->
                requireInt (path + ".decimals") j
                |> Result.map (fun d -> CellFormat.Percent(Some d))
        | Ok "SignificantDigits" ->
            match requireField path fields "digits" "integer digit count" with
            | Error e -> Error e
            | Ok j -> requireInt (path + ".digits") j |> Result.map CellFormat.SignificantDigits
        | Ok "Date" ->
            match requireField path fields "format" "format string" with
            | Error e -> Error e
            | Ok j -> requireString (path + ".format") j |> Result.map CellFormat.Date
        | Ok "Custom" ->
            // Encoder writes the fn as `<closure>`; decode to a placeholder
            // that returns the sentinel for any CellValue input.
            Ok(CellFormat.Custom(fun _ -> closureSentinel))
        | Ok s -> unknownDuCase path s "None | Number | Currency | Percent | SignificantDigits | Date | Custom"

let private decodeCellValue (path: string) (j: Json) : Result<CellValue, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Numeric" ->
            match requireField path fields "value" "float value" with
            | Error e -> Error e
            | Ok j -> requireFloat (path + ".value") j |> Result.map CellValue.Numeric
        | Ok "Text" ->
            match requireField path fields "value" "string value" with
            | Error e -> Error e
            | Ok j -> requireString (path + ".value") j |> Result.map CellValue.Text
        | Ok "Bool" ->
            match requireField path fields "value" "bool value" with
            | Error e -> Error e
            | Ok j -> requireBool (path + ".value") j |> Result.map CellValue.Bool
        | Ok "Date" ->
            match requireField path fields "unixSeconds" "int64 unix seconds" with
            | Error e -> Error e
            | Ok j ->
                requireFloat (path + ".unixSeconds") j
                |> Result.map (fun s -> CellValue.Date(DateTimeOffset.FromUnixTimeSeconds(int64 s)))
        | Ok "Empty" -> Ok CellValue.Empty
        | Ok s -> unknownDuCase path s "Numeric | Text | Bool | Date | Empty"

// ─── Format / LocaleSource (Phase 102) ───────────────────────────────────

let private decodeDateStyle (path: string) (j: Json) : Result<DateStyle, DecodeError> =
    match j with
    | JString "Short" -> Ok DateStyle.Short
    | JString "Medium" -> Ok DateStyle.Medium
    | JString "Long" -> Ok DateStyle.Long
    | JString "Full" -> Ok DateStyle.Full
    | JString s -> unknownDuCase path s "Short | Medium | Long | Full"
    | _ -> wrongType path "JSON string (DateStyle)"

let private decodeRelativeTimeUnit (path: string) (j: Json) : Result<RelativeTimeUnit, DecodeError> =
    match j with
    | JString "Second" -> Ok RelativeTimeUnit.Second
    | JString "Minute" -> Ok RelativeTimeUnit.Minute
    | JString "Hour" -> Ok RelativeTimeUnit.Hour
    | JString "Day" -> Ok RelativeTimeUnit.Day
    | JString "Week" -> Ok RelativeTimeUnit.Week
    | JString "Month" -> Ok RelativeTimeUnit.Month
    | JString "Year" -> Ok RelativeTimeUnit.Year
    | JString s -> unknownDuCase path s "Second | Minute | Hour | Day | Week | Month | Year"
    | _ -> wrongType path "JSON string (RelativeTimeUnit)"

let private decodeFormat (path: string) (j: Json) : Result<Format, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Number" ->
            match tryField fields "decimals" with
            | None -> Ok(Format.Number None)
            | Some j -> requireInt (path + ".decimals") j |> Result.map (fun d -> Format.Number(Some d))
        | Ok "Currency" ->
            match requireField path fields "isoCode" "ISO-4217 currency code string" with
            | Error e -> Error e
            | Ok j -> requireString (path + ".isoCode") j |> Result.map Format.Currency
        | Ok "Percent" ->
            match tryField fields "decimals" with
            | None -> Ok(Format.Percent None)
            | Some j ->
                requireInt (path + ".decimals") j
                |> Result.map (fun d -> Format.Percent(Some d))
        | Ok "Date" ->
            match requireField path fields "dateStyle" "DateStyle string" with
            | Error e -> Error e
            | Ok j -> decodeDateStyle (path + ".dateStyle") j |> Result.map Format.Date
        | Ok "RelativeTime" ->
            match requireField path fields "unit" "RelativeTimeUnit string" with
            | Error e -> Error e
            | Ok j -> decodeRelativeTimeUnit (path + ".unit") j |> Result.map Format.RelativeTime
        | Ok s -> unknownDuCase path s "Number | Currency | Percent | Date | RelativeTime"

let private decodeLocaleSource (path: string) (j: Json) : Result<LocaleSource, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Ambient" -> Ok LocaleSource.Ambient
        | Ok "Explicit" ->
            match requireField path fields "tag" "BCP-47 locale tag string" with
            | Error e -> Error e
            | Ok j -> requireString (path + ".tag") j |> Result.map LocaleSource.Explicit
        | Ok s -> unknownDuCase path s "Ambient | Explicit"

// ─── obj best-effort decoder (Binding<obj> static seam ONLY) ─────────────
//
// Symmetric with `CanonicalJson.appendObj`: the encoder writes recognised
// primitives as JSON natively + everything else as the `"<opaque>"`
// sentinel. The decoder rebuilds the obj structurally from the JSON AST;
// `"<opaque>"` stays as the sentinel string (the encoder explicitly threw
// away the original CLR type, so the decoder doesn't try to reconstruct).
// The JSON-valued payload positions (Custom props / action payloads / I18n
// args / UpdateProp wire values) decode via `decodeJVal` below instead —
// structured, null-rejecting, faithfully re-encodable.
//
// Number caveat: Fable.SimpleJson surfaces every number as float (no
// int/float distinction in the AST). An int written by the encoder
// round-trips back as float — exact for the int53 range; consumers that
// need the int re-cast at the call site.

let rec private decodeObj (j: Json) : obj =
    match j with
    | JNull -> box null
    | JString s when s = opaqueSentinel -> box opaqueSentinel
    | JString s -> box s
    | JBool b -> box b
    | JNumber n -> box n
    | JArray xs -> box (xs |> List.map decodeObj)
    | JObject fields -> box (fields |> Map.map (fun _ v -> decodeObj v))

/// Decode a JSON-valued payload position to the structured `JVal` AST —
/// null-rejecting per the no-null wire model (see `jsonToJValStrict`).
let private decodeJVal (path: string) (j: Json) : Result<JVal, DecodeError> = jsonToJValStrict path j

let private decodeJValMap (path: string) (j: Json) : Result<Map<string, JVal>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let folded =
            (Ok [], fields |> Map.toList)
            ||> List.fold (fun acc (k, v) ->
                match acc with
                | Error e -> Error e
                | Ok pairs -> decodeJVal (path + "." + k) v |> Result.map (fun jv -> (k, jv) :: pairs))

        folded |> Result.map (List.rev >> Map.ofList)

// ─── IconSource ─────────────────────────────────────────────────────────

let private decodeIconSource (path: string) (j: Json) : Result<IconSource, DecodeError> =
    requireString path j |> Result.map IconSource

// ─── Binding decoders — typed per slot ──────────────────────────────────
//
// The decoder is `Node<obj>`-shaped (storage-shape erasure), but
// individual Spec records carry typed Bindings — `MetricSpec.Source :
// Binding<float>`, `StepperSpec.ActiveStep : Binding<int>`, etc. Each
// typed Binding decoder:
//   - Decodes the wire-form discriminator object.
//   - For `Static`, runs the slot-typed value parser.
//   - For `Query` (Phase 421) / `Selection` (Phase 427), returns an
//     IDENTITY accessor (`unbox<'T>`) — the typed closure was lost to the
//     `<closure>` sentinel on encode, but the host-fed / store-written
//     value must still flow to decoded readers.
//   - For `Computed`, returns a placeholder computed.
//   - For `I18n`, decodes the optional args map (always `Binding<obj>`).
//
// `decodeBindingObj` is the obj-typed flavour used by `TreeOp.ReplaceBinding`
// and `Binding.I18n` args; the typed flavours dispatch to a parametric
// generator below.

let rec private decodeBindingObj (path: string) (j: Json) : Result<Binding<obj>, DecodeError> =
    bindingGeneric<obj> path (fun _ v -> Ok(decodeObj v)) (box closureSentinel) j

and private decodeLocalFlushTrigger (path: string) (j: Json) : Result<LocalFlushTrigger, DecodeError> =
    // 4-case DU; one carries a `milliseconds: int` payload.
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "OnBlur" -> Ok LocalFlushTrigger.OnBlur
        | Ok "OnSubmit" -> Ok LocalFlushTrigger.OnSubmit
        | Ok "OnCommitAction" -> Ok LocalFlushTrigger.OnCommitAction
        | Ok "OnDebounce" ->
            match requireField path fields "milliseconds" "debounce milliseconds integer" with
            | Error e -> Error e
            | Ok v -> requireInt (path + ".milliseconds") v |> Result.map LocalFlushTrigger.OnDebounce
        | Ok s -> unknownDuCase path s "OnBlur | OnSubmit | OnDebounce | OnCommitAction"

and private bindingGeneric<'T>
    (path: string)
    (parseStatic: string -> Json -> Result<'T, DecodeError>)
    (placeholder: 'T)
    (j: Json)
    : Result<Binding<'T>, DecodeError> =
    match j with
    // Lenient AI-ingest shape coercion (WIRE_FORMAT.md §3.6): a bare JSON
    // array where a Binding is expected is accepted as `Static` with the
    // array as its value — `options: ["A","B"]` (the HTML select prior,
    // 2/2 observed eval emission failures) and `data: [1,2,3]` (the
    // Chart.js prior). Unambiguous: every Binding case is a
    // `$type`-discriminated object, so an array can only mean Static.
    // Decode-only — the canonical encoder still emits the envelope. Extended
    // 2026-07-17 (launch-eval evidence) from arrays to bare SCALARS: models
    // emit `fraction: 0.9` / `activeStep: 1` where a Binding is expected —
    // same law, same unambiguity (a scalar can only mean Static). Objects
    // stay strict: an object without `$type` is more plausibly a mistyped
    // binding than a Static value; `null` stays strict (ambiguous with
    // absent).
    | JArray _
    | JString _
    | JNumber _
    | JBool _ -> parseStatic path j |> Result.map Binding.Static
    | _ ->

        match requireObject path j with
        | Error e -> Error e
        | Ok fields ->
            match requireDiscriminator path fields with
            | Error e -> Error e
            | Ok "Static" ->
                // Phase 677 — absence is structural: a MISSING `value` means the
                // binding carries none. The legacy `"value": null` form still
                // decodes (§16 shorthand, since models emit null naturally) by
                // routing to the very same per-slot absent handling, so the two
                // spellings cannot disagree.
                let v =
                    match tryField fields "value" with
                    | Some v -> v
                    | None -> JNull

                parseStatic (path + ".value") v |> Result.map Binding.Static
            | Ok "Query" ->
                match requireField path fields "name" "query name string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".name") v
                    |> Result.bind (fun name ->
                        // Phase 421 — optional `dependsOn` string array (the declared filter edge); absent → [].
                        let dependsOnR =
                            // Field aliases: deps/dependencies — the React-hooks prior.
                            match optFieldAliased fields "dependsOn" [ "deps"; "dependencies" ] with
                            | None -> Ok []
                            | Some dJ ->
                                requireArray (path + ".dependsOn") dJ
                                |> Result.bind (traverse (requireString (path + ".dependsOn[]")))

                        dependsOnR
                        |> Result.map (fun dependsOn ->
                            // Phase 421 identity-accessor fix: a decoded `Query` projects the host's
                            // `queryResults.<name>` value straight through (`unbox<'T>`) instead of a
                            // value-discarding sentinel, so host-fed data flows through decoded trees. A
                            // type mismatch surfaces as the resolver's `Errored` (loud), not a silent wrong value.
                            Binding.Query(name, (fun (raw: obj) -> unbox raw), dependsOn)))
            | Ok "Filter" ->
                match requireField path fields "name" "filter name string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".name") v
                    |> Result.map (fun name ->
                        // 0.2.0 — optional `defaultValue`: the value the resolver
                        // yields (and the renderer seeds the store with) before the
                        // filter is first written. Decoded through the slot's typed
                        // static parser, mirroring `State.defaultValue`.
                        let defaultV =
                            match tryField fields "defaultValue" with
                            | Some dv ->
                                match parseStatic (path + ".defaultValue") dv with
                                | Ok parsed -> Some parsed
                                | Error _ -> None
                            | None -> None

                        Binding.Filter(name, defaultV))
            | Ok "Selection" ->
                match requireField path fields "nodeId" "selection NodeId string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".nodeId") v
                    |> Result.map (fun id ->
                        // 0.2.9 (Phase 629) — optional `defaultValue`, the
                        // `Filter.defaultValue` convention: yielded until the
                        // user first selects a row on `nodeId`.
                        let defaultV =
                            match tryField fields "defaultValue" with
                            | Some dv ->
                                match parseStatic (path + ".defaultValue") dv with
                                | Ok parsed -> Some parsed
                                | Error _ -> None
                            | None -> None

                        // Phase 427 identity-accessor fix (the 421 `Query` fix replayed for the second
                        // accessor-bearing binding): a decoded `Selection` projects the stored row
                        // straight through (`unbox<'T>`) instead of a value-discarding placeholder, so
                        // a written selection flows to decoded readers. A type mismatch surfaces as
                        // the resolver's `Errored` (loud), not a silent wrong value.
                        //
                        // 0.2.10 (Phase 632) — optional `field`: the declarative row-field
                        // projection. Present ⇒ the accessor projects that field off the
                        // clicked row (the grid writes the FULL row), so the binding stays
                        // scalar after a real click; absent ⇒ the 427 identity, pre-632
                        // behaviour byte-for-byte. A missing field / non-row value throws
                        // in the accessor — the resolver's loud path, never silent.
                        let fieldV =
                            match tryField fields "field" with
                            | Some fv ->
                                match requireString (path + ".field") fv with
                                | Ok f -> Some f
                                | Error _ -> None
                            | None -> None

                        let accessor: obj -> 'T =
                            match fieldV with
                            | Some f -> Fuaran.UI.Types.Binding.projectSelectionField<'T> f
                            | None -> fun (raw: obj) -> unbox raw

                        Binding.Selection(NodeId id, accessor, defaultV, fieldV))
            | Ok "State" ->
                match requireField path fields "key" "state key string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".key") v
                    |> Result.map (fun key ->
                        // Decode the carried `defaultValue` through the typed
                        // static parser when it parses (Phase 426 — the write-back
                        // default reads a decoded field's own State default, and
                        // the TS decoder already carries it); an absent /
                        // unparseable default falls back to the typed placeholder
                        // (read-compat with the pre-426 behaviour).
                        let defaultV =
                            // Field aliases: initialValue/default — the React useState prior.
                            match optFieldAliased fields "defaultValue" [ "initialValue"; "default" ] with
                            | Some dv ->
                                match parseStatic (path + ".defaultValue") dv with
                                | Ok parsed -> parsed
                                | Error _ -> placeholder
                            // Phase 677 — an ABSENT default now means the binding
                            // carries none, and must decode to the same value the
                            // legacy `"defaultValue": null` did, or the encoder
                            // re-emits a placeholder and the round-trip breaks
                            // (caught by `form-declarative`'s Choice slot). Route
                            // through the identical per-slot absent handling.
                            | None ->
                                match parseStatic (path + ".defaultValue") JNull with
                                | Ok parsed -> parsed
                                | Error _ -> placeholder

                        Binding.State(key, defaultV))
            | Ok "Computed" ->
                // Encoder writes the fn as `<closure>`; decode to a placeholder.
                Ok(Binding.Computed(fun _ -> placeholder))
            | Ok "I18n" ->
                match requireField path fields "key" "i18n key string" with
                | Error e -> Error e
                | Ok v ->
                    match requireString (path + ".key") v with
                    | Error e -> Error e
                    | Ok key ->
                        match tryField fields "args" with
                        | None -> Ok(Binding.I18n(key, None))
                        | Some argsJ ->
                            match decodeBindingObjArgs (path + ".args") argsJ with
                            | Error e -> Error e
                            | Ok argsMap -> Ok(Binding.I18n(key, Some argsMap))
            | Ok "Local" ->
                // Local-binding decode. `initialFrom` recurses
                // through the same `bindingGeneric` machinery; `flushOn`
                // decodes the trigger DU; `format` / `parse` are name
                // references resolved against a host-provided registry
                // (consumer-side, not visible to the decoder), so the
                // decoded `Format` is `None` and `Parse` returns a
                // sentinel-error placeholder. `OnCommit` was rendered as
                // the `<closure>` sentinel on encode and decodes to a
                // closure that always returns the placeholder obj.
                // Authors re-attach typed Format/Parse/OnCommit downstream
                // via their `moduleMsgDecoder` per the storage-
                // shape erasure.
                match requireField path fields "initialFrom" "Local InitialFrom Binding<'T>" with
                | Error e -> Error e
                | Ok ifJ ->
                    match bindingGeneric<'T> (path + ".initialFrom") parseStatic placeholder ifJ with
                    | Error e -> Error e
                    | Ok initialFrom ->
                        let flushR =
                            match tryField fields "flushOn" with
                            | None -> Ok LocalFlushTrigger.OnBlur
                            | Some fJ -> decodeLocalFlushTrigger (path + ".flushOn") fJ

                        match flushR with
                        | Error e -> Error e
                        | Ok flushOn ->
                            Ok(
                                Binding.Local
                                    { InitialFrom = initialFrom
                                      FlushOn = flushOn
                                      OnCommit = (fun _ -> box closureSentinel)
                                      Format = None
                                      Parse = (fun _ -> Error closureSentinel) }
                            )
            | Ok "Format" ->
                // Locale-aware formatted binding (Phase 102). `source`
                // is always a `Binding<float>` regardless of the slot's `'T`;
                // `format` / `locale` are the bounded DUs. The resulting
                // `Binding.Format(...)` types as `Binding<'T>` for any `'T` (the
                // case doesn't constrain `'T`), so it decodes uniformly in every
                // typed slot — semantically it only resolves cleanly in a
                // `Binding<string>` slot (the formatter returns a string).
                match requireField path fields "source" "Binding<float> source object" with
                | Error e -> Error e
                | Ok srcJ ->
                    match bindingGeneric<float> (path + ".source") requireFloat 0.0 srcJ with
                    | Error e -> Error e
                    | Ok source ->
                        match requireField path fields "format" "Format DU object" with
                        | Error e -> Error e
                        | Ok fmtJ ->
                            match decodeFormat (path + ".format") fmtJ with
                            | Error e -> Error e
                            | Ok format ->
                                match requireField path fields "locale" "LocaleSource DU object" with
                                | Error e -> Error e
                                | Ok locJ ->
                                    decodeLocaleSource (path + ".locale") locJ
                                    |> Result.map (fun locale -> Binding.Format(source, format, locale))
            | Ok "Transform" ->
                // Phase 282 — the Compute layer. `source` (a `Fuaran.Core.DataSource`) and `pipeline`
                // (a `Fuaran.Core.DataFrame` `Transform list`) decode through the `Fuaran.Core` codecs,
                // which share this host's `Canon` `$type` discipline. Bridge the parsed `Json` sub-tree
                // to `Fuaran.Core.JVal` and hand it to Core's JVal decoders. Types as `Binding<'T>` for
                // any `'T` (the case doesn't constrain it); semantically resolves in a `Binding<obj seq>`
                // slot at a data-bearing node.
                match requireField path fields "source" "Transform DataSource object" with
                | Error e -> Error e
                | Ok srcJ ->
                    match requireField path fields "pipeline" "Transform pipeline array" with
                    | Error e -> Error e
                    | Ok pipeJ ->
                        match Fuaran.Core.ColumnCodec.decodeJson (jsonToJVal srcJ) with
                        | Error ce -> Error(coreError (path + ".source") ce)
                        | Ok source ->
                            match Fuaran.Core.DataFrameCodec.decodePipelineJson (jsonToJVal pipeJ) with
                            | Error ce -> Error(coreError (path + ".pipeline") ce)
                            | Ok pipeline ->
                                // Phase 424 — optional `params`: [{ "from": <Binding>, "name": <string> }, …]
                                // binding each `ColExpr.Param` name to a scalar `Binding<obj>` source.
                                // Absent → `[]` (byte-identical to the Phase 282 shape).
                                let decodeParam (el: Json) : Result<string * Binding<obj>, DecodeError> =
                                    match requireObject (path + ".params[]") el with
                                    | Error e -> Error e
                                    | Ok pf ->
                                        match
                                            requireField (path + ".params[]") pf "name" "param name string"
                                            |> Result.bind (requireString (path + ".params[].name"))
                                        with
                                        | Error e -> Error e
                                        | Ok name ->
                                            // Field alias: value — the observed repair-attempt
                                            // shape ({name, value}); only two fields exist, so
                                            // the concept is unambiguous.
                                            requireFieldAliased
                                                (path + ".params[]")
                                                pf
                                                "from"
                                                [ "value" ]
                                                "param source Binding"
                                            |> Result.bind (decodeBindingObj (path + ".params." + name + ".from"))
                                            |> Result.map (fun fromB -> name, fromB)

                                let paramsR =
                                    match tryField fields "params" with
                                    | None -> Ok []
                                    // Lenient AI-ingest shape coercion (WIRE_FORMAT.md §3.6,
                                    // 2026-07-17): the name→binding MAP form
                                    // (`"params": {"status": <Binding>}`) coerces to the
                                    // canonical `[{name, from}]` array. Params are a
                                    // NAME-KEYED SET (ColExpr.Param lookup), so object key
                                    // order carries no meaning — unlike the options map,
                                    // which is refused. Observed as every provider's first
                                    // guess (21/31 launch-eval failures repair-proof).
                                    | Some(JObject _ as pJ) ->
                                        match requireObject (path + ".params") pJ with
                                        | Error e -> Error e
                                        | Ok pf ->
                                            pf
                                            |> Map.toList
                                            |> traverse (fun (name, v) ->
                                                decodeBindingObj (path + ".params." + name + ".from") v
                                                |> Result.map (fun b -> name, b))
                                    | Some pJ ->
                                        requireArray (path + ".params") pJ |> Result.bind (traverse decodeParam)

                                paramsR
                                |> Result.map (fun parameters -> Binding.Transform(source, pipeline, parameters))
            | Ok "Invoke" ->
                // Phase 283 — invoke a host-registered capability for a value. `capabilityId` + scalar
                // `(addr, value)` args; the body is never on the wire. Types as `Binding<'T>` for any
                // `'T`; resolves to a `Deferred<'T>` at a data-bearing node.
                match requireField path fields "capabilityId" "capability id string" with
                | Error e -> Error e
                | Ok cidJ ->
                    match requireString (path + ".capabilityId") cidJ with
                    | Error e -> Error e
                    | Ok capabilityId ->
                        match requireField path fields "args" "invoke args array" with
                        | Error e -> Error e
                        | Ok argsJ ->
                            decodeInvokeArgs (path + ".args") argsJ
                            |> Result.map (fun args -> Binding.Invoke(capabilityId, args))
            // Pilot-5 lenient wave 2 — the `TextSource.Bound` wrapper convention
            // transferred to a bare-Binding slot: models emit
            // {"$type":"Bound","binding":X} in Metric.value / LabelValueRow etc.
            // (claude-family, ×7 across the cohort — gate fired at the n=3
            // review). `Bound` carries exactly one payload field, so the unwrap
            // is one-to-one: decode the inner binding in place. Decode-only —
            // the canonical encoder never wraps bare-Binding slots.
            | Ok "Bound" ->
                match requireField path fields "binding" "the wrapped Binding object" with
                | Error e -> Error e
                | Ok inner -> bindingGeneric<'T> (path + ".binding") parseStatic placeholder inner
            | Ok s ->
                unknownDuCase
                    path
                    s
                    "Static | Query | Filter | Selection | State | Computed | I18n | Local | Format | Transform | Invoke"

and private decodeBindingObjArgs (path: string) (j: Json) : Result<Map<string, Binding<obj>>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let entries = fields |> Map.toList

        let mapped =
            traverse (fun (k, v) -> decodeBindingObj (path + "." + k) v |> Result.map (fun b -> k, b)) entries

        mapped |> Result.map Map.ofList

let private decodeBindingFloat (path: string) (j: Json) : Result<Binding<float>, DecodeError> =
    bindingGeneric<float> path requireFloat 0.0 j

let private decodeBindingInt (path: string) (j: Json) : Result<Binding<int>, DecodeError> =
    bindingGeneric<int> path requireInt 0 j

let private decodeBindingString (path: string) (j: Json) : Result<Binding<string>, DecodeError> =
    bindingGeneric<string> path requireString "" j

let private decodeBindingBool (path: string) (j: Json) : Result<Binding<bool>, DecodeError> =
    bindingGeneric<bool> path requireBool false j

let rec private decodeSelectOption (path: string) (j: Json) : Result<SelectOption, DecodeError> =
    match j with
    // Lenient AI-ingest shape coercion (WIRE_FORMAT.md §3.6): a bare JSON
    // string element is accepted as `{value: s, label: Literal s}` — the HTML
    // `<select>` prior (`options: ["A","B"]`), observed as an emission shape
    // on two independent models. Same family as the TextSource string
    // shorthand below: decode-only, the canonical encoder still emits the
    // object form. The value→label map form (`{"A":"A"}`) is deliberately NOT
    // coerced — JSON object key order is not contractual, so coercing it
    // could silently reorder visible options.
    | JString s ->
        Ok
            { Value = s
              Label = TextSource.Literal s }
    | _ ->

        match requireObject path j with
        | Error e -> Error e
        | Ok fields ->
            match requireField path fields "value" "option value string" with
            | Error e -> Error e
            | Ok vJ ->
                match requireString (path + ".value") vJ with
                | Error e -> Error e
                | Ok value ->
                    match requireField path fields "label" "option label TextSource" with
                    | Error e -> Error e
                    | Ok lJ ->
                        decodeTextSource (path + ".label") lJ
                        |> Result.map (fun label -> { Value = value; Label = label })

and private decodeTextSource (path: string) (j: Json) : Result<TextSource, DecodeError> =
    match j with
    // Lenient AI-ingest shorthand (WIRE_FORMAT.md §16): a bare JSON string is
    // accepted as `TextSource.Literal`, so an author can write `"Revenue"`
    // instead of `{"$type":"Literal","text":"Revenue"}` — the single biggest
    // token saver, since labels / headings / help text are everywhere. This is
    // a decode-only convenience: the canonical encoder still emits the object
    // form, so the shorthand re-encodes to canonical (it does not round-trip
    // byte-identically, by design) and every existing fixture is unaffected.
    | JString s -> Ok(TextSource.Literal s)
    | _ ->
        match requireObject path j with
        | Error e -> Error e
        | Ok fields ->
            match requireDiscriminator path fields with
            | Error e -> Error e
            | Ok "Literal" ->
                match requireField path fields "text" "literal text string" with
                | Error e -> Error e
                | Ok v -> requireString (path + ".text") v |> Result.map TextSource.Literal
            | Ok "Bound" ->
                match requireField path fields "binding" "Binding<string> object" with
                | Error e -> Error e
                | Ok v -> decodeBindingString (path + ".binding") v |> Result.map TextSource.Bound
            | Ok "I18n" ->
                match requireField path fields "key" "i18n key string" with
                | Error e -> Error e
                | Ok kJ ->
                    match requireString (path + ".key") kJ with
                    | Error e -> Error e
                    | Ok key ->
                        match tryField fields "args" with
                        | None -> Ok(TextSource.I18n(key, Map.empty))
                        | Some aJ ->
                            decodeJValMap (path + ".args") aJ
                            |> Result.map (fun args -> TextSource.I18n(key, args))
            | Ok s -> unknownDuCase path s "Literal | Bound | I18n"

// Slot-typed `Binding.Static` payloads round-trip TYPED since Phase 429:
// the encoder's `encodeBindingWith` emits the same shapes these decoders
// parse (options as `{"label":…,"value":…}` arrays, values as string
// arrays, series as float arrays, markers as `{label,latitude,longitude}`
// arrays), so encode∘decode is byte-stable AND value-faithful for the
// enumerated shapes. Read-compat is retained indefinitely for the two
// legacy wire forms the pre-429 encoder produced: the `"<opaque>"`
// sentinel (any non-primitive payload — decodes to a tagged placeholder
// whose re-encode is the typed placeholder form) and JSON `null` (the
// F# boxes-to-`null` asymmetry: `box ([] : 'a list)` / `box None` are
// null references — decodes to the typed empty form). Only genuinely
// host-typed payloads (`obj seq` grid/table rows) remain opaque by
// design; the orchestrator's typed re-attachment (`moduleMsgDecoder`)
// re-hydrates those downstream from the per-app schema.

let private decodeBindingSelectOptions (path: string) (j: Json) : Result<Binding<SelectOption list>, DecodeError> =
    // Typed form preferred (Phase 429 — the encoder now emits it); the
    // legacy `"<opaque>"` sentinel and `null` (the pre-429 boxes-to-null
    // empty-list form) stay decode-accepted indefinitely. The opaque
    // placeholder element is a tagged-empty SelectOption so the
    // orchestrator's typed re-attachment can recognise + replace it; its
    // re-encode is the typed one-element placeholder array (stable across
    // both hosts).
    let parseStatic (p: string) (v: Json) : Result<SelectOption list, DecodeError> =
        match v with
        | JNull -> Ok [] // pre-429 read-compat: `box ([] : SelectOption list)` encoded as `null`
        | JString s when s = opaqueSentinel ->
            Ok
                [ { Value = opaqueSentinel
                    Label = TextSource.Literal opaqueSentinel } ]
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> traverseIndexed (fun i item -> decodeSelectOption (sprintf "%s[%d]" p i) item) xs

    bindingGeneric<SelectOption list>
        path
        parseStatic
        [ { Value = opaqueSentinel
            Label = TextSource.Literal opaqueSentinel } ]
        j

let private decodeBindingStringOpt (path: string) (j: Json) : Result<Binding<string option>, DecodeError> =
    // Typed form (Phase 429): `Some s` encodes as the plain string, `None`
    // as `null`. The `"<opaque>"` sentinel (any pre-429 `Some` payload —
    // boxed options hit the old catch-all) stays decode-accepted; its
    // `Some opaqueSentinel` placeholder re-encodes as the same string.
    let parseStatic (p: string) (v: Json) : Result<string option, DecodeError> =
        match v with
        | JNull -> Ok None
        | JString s when s = opaqueSentinel -> Ok(Some opaqueSentinel)
        | JString s -> Ok(Some s)
        | _ -> wrongType p "JSON string or null (string option)"

    bindingGeneric<string option> path parseStatic (Some opaqueSentinel) j

let private decodeBindingStringList (path: string) (j: Json) : Result<Binding<string list>, DecodeError> =
    // Phase 291 — the multi-select `Values` binding. Typed form preferred
    // (Phase 429 — a plain string array); `null` (the pre-429 boxes-to-null
    // empty-list form) and the `"<opaque>"` sentinel stay decode-accepted,
    // the latter as a tagged one-element placeholder list.
    let parseStatic (p: string) (v: Json) : Result<string list, DecodeError> =
        match v with
        | JNull -> Ok []
        | JString s when s = opaqueSentinel -> Ok [ opaqueSentinel ]
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> traverseIndexed (fun i item -> requireString (sprintf "%s[%d]" p i) item) xs

    bindingGeneric<string list> path parseStatic [ opaqueSentinel ] j

let private decodeBindingFloatSeq (path: string) (j: Json) : Result<Binding<float seq>, DecodeError> =
    let parseStatic (p: string) (v: Json) : Result<float seq, DecodeError> =
        match v with
        | JNull -> Ok Seq.empty // pre-429 read-compat: an empty-list-backed seq boxed to `null`
        | JString s when s = opaqueSentinel -> Ok Seq.empty
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs ->
                traverseIndexed (fun i item -> requireFloat (sprintf "%s[%d]" p i) item) xs
                |> Result.map (fun ns -> ns :> float seq)

    bindingGeneric<float seq> path parseStatic Seq.empty j

let private decodeBindingFloatPair (path: string) (j: Json) : Result<Binding<float * float>, DecodeError> =
    // 0.2.0 — the dual-thumb Range control's (min, max) pair. Static forms:
    // the object `{min, max}` (canonical) or a two-element array (lenient).
    let parseStatic (p: string) (v: Json) : Result<float * float, DecodeError> =
        match v with
        | JObject pf ->
            match tryField pf "min", tryField pf "max" with
            | Some mn, Some mx ->
                match requireFloat (p + ".min") mn, requireFloat (p + ".max") mx with
                | Ok a, Ok b -> Ok(a, b)
                | Error e, _
                | _, Error e -> Error e
            | _ -> wrongType p "object with min and max numbers"
        | JArray [ a; b ] ->
            match requireFloat (p + "[0]") a, requireFloat (p + "[1]") b with
            | Ok x, Ok y -> Ok(x, y)
            | Error e, _
            | _, Error e -> Error e
        | _ -> wrongType p "range pair ({min, max} object or [min, max] array)"

    // The canonical Static pair rides as the BARE `{min, max}` object (the
    // Phase-423 range shape, no envelope) — accept it before the generic
    // binding dispatch, which would otherwise demand a `$type`.
    match j with
    | JObject pf when
        (tryField pf "$type").IsNone
        && (tryField pf "min").IsSome
        && (tryField pf "max").IsSome
        ->
        parseStatic path j |> Result.map Binding.Static
    | _ -> bindingGeneric<float * float> path parseStatic (0.0, 0.0) j

let private decodeBindingObjSeq (path: string) (j: Json) : Result<Binding<obj seq>, DecodeError> =
    let parseStatic (p: string) (v: Json) : Result<obj seq, DecodeError> =
        match v with
        | JNull -> Ok Seq.empty // an empty-list-backed seq boxes to `null` (obj seq stays opaque by design)
        | JString s when s = opaqueSentinel -> Ok Seq.empty
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> Ok(xs |> List.map decodeObj |> Seq.ofList)

    bindingGeneric<obj seq> path parseStatic Seq.empty j

let private decodeMapMarker (path: string) (j: Json) : Result<MapMarker, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            match requireField path fields "label" "marker label TextSource" with
            | Error e -> Error e
            | Ok v -> decodeTextSource (path + ".label") v

        let latR =
            match requireField path fields "latitude" "marker latitude float" with
            | Error e -> Error e
            | Ok v -> requireFloat (path + ".latitude") v

        let lonR =
            match requireField path fields "longitude" "marker longitude float" with
            | Error e -> Error e
            | Ok v -> requireFloat (path + ".longitude") v

        match labelR, latR, lonR with
        | Ok label, Ok lat, Ok lon ->
            Ok
                { Label = label
                  Latitude = lat
                  Longitude = lon }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

let private decodeBindingMarkerSeq (path: string) (j: Json) : Result<Binding<MapMarker seq>, DecodeError> =
    let parseStatic (p: string) (v: Json) : Result<MapMarker seq, DecodeError> =
        match v with
        | JNull -> Ok Seq.empty // pre-429 read-compat: an empty-list-backed seq boxed to `null`
        | JString s when s = opaqueSentinel -> Ok Seq.empty
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs ->
                traverseIndexed (fun i m -> decodeMapMarker (sprintf "%s[%d]" p i) m) xs
                |> Result.map (fun ms -> ms :> MapMarker seq)

    bindingGeneric<MapMarker seq> path parseStatic Seq.empty j

// ─── Action<obj> decoder ────────────────────────────────────────────────
//
// Storage-shape erasure: every `Action<'Msg>` decodes to `Action<obj>`.
// Closure-bearing slots (`Dispatch msg`, `Call onResult`) substitute a
// `box closureSentinel` placeholder; non-closure slots decode fully.

let rec private decodeAction (path: string) (j: Json) : Result<Action<obj>, DecodeError> =
    // 0.2.2 DIDACTIC — a bare string in an Action slot (pilot-3: gemini wrote
    // the "<closure>" sentinel as the VALUE 9×). Never coerced — a sentinel
    // action would be a dead control passing the gate — but the error names
    // the fix so the repair channel converts.
    match j with
    | JString s ->
        err
            DecodeErrorCode.WRONG_TYPE
            path
            (sprintf
                "expected JSON object, got the string '%s' — an action is a $type-discriminated object (SetState | Navigate | Call | Notify | Chain | AiTool | WriteToClipboard | Invoke); \"<closure>\" is not authorable. Pick a real action, e.g. {\"$type\":\"SetState\",\"key\":…,\"value\":…}"
                (if s.Length > 24 then s.Substring(0, 24) + "…" else s))
            (Some "Action object")
    | _ ->

        match requireObject path j with
        | Error e -> Error e
        | Ok fields ->
            match requireDiscriminator path fields with
            | Error e -> Error e
            | Ok "Dispatch" -> Ok(Action.Dispatch(box closureSentinel))
            | Ok "Call" ->
                // Field alias: url — the fetch prior for the same concept.
                match requireFieldAliased path fields "endpoint" [ "url" ] "ApiEndpoint string" with
                | Error e -> Error e
                | Ok v ->
                    match requireString (path + ".endpoint") v with
                    | Error e -> Error e
                    | Ok ep ->
                        // Phase 428: `onResult` present `"<closure>"` → the inert
                        // `Some` placeholder; absent → `None` (the declarative /
                        // fire-and-forget shape). `into` is the optional result
                        // target: {"$type":"State","key":…} / {"$type":"Query","name":…}.
                        let onResult =
                            match tryField fields "onResult" with
                            | Some _ -> Some(fun (_: obj) -> box closureSentinel)
                            | None -> None

                        let intoR =
                            match tryField fields "into" with
                            | None -> Ok None
                            | Some intoJ ->
                                match requireObject (path + ".into") intoJ with
                                | Error e -> Error e
                                | Ok intoFields ->
                                    match requireDiscriminator (path + ".into") intoFields with
                                    | Error e -> Error e
                                    | Ok "State" ->
                                        requireField (path + ".into") intoFields "key" "state key string"
                                        |> Result.bind (requireString (path + ".into.key"))
                                        |> Result.map (fun k -> Some(CallResultTarget.IntoState k))
                                    | Ok "Query" ->
                                        requireField (path + ".into") intoFields "name" "query name string"
                                        |> Result.bind (requireString (path + ".into.name"))
                                        |> Result.map (fun n -> Some(CallResultTarget.IntoQuery n))
                                    | Ok s -> unknownDuCase (path + ".into") s "State | Query"

                        intoR |> Result.map (fun into -> Action.Call(ApiEndpoint ep, onResult, into))
            | Ok "Notify" ->
                match requireField path fields "channel" "notification channel string" with
                | Error e -> Error e
                | Ok cJ ->
                    match requireString (path + ".channel") cJ with
                    | Error e -> Error e
                    | Ok channel ->
                        match requireField path fields "payload" "JSON value payload" with
                        | Error e -> Error e
                        | Ok pJ ->
                            decodeJVal (path + ".payload") pJ
                            |> Result.map (fun p -> Action.Notify(channel, p))
            | Ok "Navigate" ->
                // Field aliases: href/url/to — the HTML / React-Router prior for the
                // same concept (observed 2/2 in the 2026-07-16 Kimi smokes).
                match requireFieldAliased path fields "route" [ "href"; "url"; "to" ] "route string" with
                | Error e -> Error e
                | Ok v -> requireString (path + ".route") v |> Result.map Action.Navigate
            | Ok "SetState" ->
                match requireField path fields "key" "state key string" with
                | Error e -> Error e
                | Ok kJ ->
                    match requireString (path + ".key") kJ with
                    | Error e -> Error e
                    | Ok key ->
                        match requireField path fields "value" "JSON value" with
                        | Error e -> Error e
                        | Ok vJ -> decodeJVal (path + ".value") vJ |> Result.map (fun v -> Action.SetState(key, v))
            | Ok "AiTool" ->
                match requireField path fields "toolName" "AI tool name string" with
                | Error e -> Error e
                | Ok nJ ->
                    match requireString (path + ".toolName") nJ with
                    | Error e -> Error e
                    | Ok name ->
                        match requireField path fields "args" "JSON value args" with
                        | Error e -> Error e
                        | Ok aJ -> decodeJVal (path + ".args") aJ |> Result.map (fun a -> Action.AiTool(name, a))
            | Ok "Chain" ->
                match requireField path fields "ops" "Action list (Chain)" with
                | Error e -> Error e
                | Ok oJ ->
                    match requireArray (path + ".ops") oJ with
                    | Error e -> Error e
                    | Ok xs ->
                        traverseIndexed (fun i item -> decodeAction (sprintf "%s.ops[%d]" path i) item) xs
                        |> Result.map Action.Chain
            | Ok "CommitLocal" ->
                // Targets a Local-bound input by NodeId; the
                // renderer dispatches a DOM custom event keyed on the id.
                match requireField path fields "nodeId" "Local-bound input NodeId string" with
                | Error e -> Error e
                | Ok v -> requireString (path + ".nodeId") v |> Result.map Action.CommitLocal
            | Ok "WriteToClipboard" ->
                // Literal-string clipboard payload. The renderer
                // routes through `IFuaranRuntime.WriteToClipboard`; the wire
                // shape carries only the text.
                match requireField path fields "text" "clipboard payload string" with
                | Error e -> Error e
                | Ok v -> requireString (path + ".text") v |> Result.map Action.WriteToClipboard
            | Ok "ReadFileBody" ->
                // Phase 136 — only `fileRef` (the opaque id) + `encoding` cross
                // the wire. The decoded `FileRef` carries `Handle = None` (no
                // browser blob on a decoded tree); `onRead` reconstructs as a
                // no-op closure that re-encodes to the `"<closure>"` sentinel.
                match requireField path fields "fileRef" "FileRef id string" with
                | Error e -> Error e
                | Ok refJ ->
                    match requireString (path + ".fileRef") refJ with
                    | Error e -> Error e
                    | Ok fileId ->
                        match requireField path fields "encoding" "FileReadEncoding" with
                        | Error e -> Error e
                        | Ok encJ ->
                            decodeFileReadEncoding (path + ".encoding") encJ
                            |> Result.map (fun encoding ->
                                Action.ReadFileBody(
                                    { Id = fileId; Handle = None },
                                    encoding,
                                    (fun _ -> box closureSentinel)
                                ))
            | Ok "Invoke" ->
                // Phase 283 — invoke a host-registered capability as an effect. `capabilityId` + scalar
                // `(addr, value)` args; the body is never on the wire.
                match requireField path fields "capabilityId" "capability id string" with
                | Error e -> Error e
                | Ok cidJ ->
                    match requireString (path + ".capabilityId") cidJ with
                    | Error e -> Error e
                    | Ok capabilityId ->
                        match requireField path fields "args" "invoke args array" with
                        | Error e -> Error e
                        | Ok argsJ ->
                            decodeInvokeArgs (path + ".args") argsJ
                            // qualified — `Action.Invoke` alone resolves to `System.Action.Invoke` (a method).
                            |> Result.map (fun args -> Fuaran.UI.Types.Action.Invoke(capabilityId, args))
            | Ok s ->
                unknownDuCase
                    path
                    s
                    "Dispatch | Call | Notify | Navigate | SetState | AiTool | Chain | CommitLocal | WriteToClipboard | ReadFileBody | Invoke"

// ─── Spec decoders ───────────────────────────────────────────────────────

let private decodeMetricSpec (path: string) (j: Json) : Result<MetricSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "Metric label TextSource"
            |> Result.bind (decodeTextSource (path + ".label"))

        let sourceR =
            // Field aliases: value (the universal KPI-card prior) / data.
            // DIDACTIC ERROR (2026-07-17): a text value here is the single
            // biggest observed emission error (~130 launch-eval cells, every
            // provider) — the error names the right kind so the structured
            // repair channel self-corrects instead of dead-ending.
            // 0.2.0 rename (clean break): the scalar displayed value is `value`;
            // `source` is NOT accepted (pre-launch, no legacy alias). `data` stays
            // as a web-prior alias.
            requireFieldAliased path fields "value" [ "data" ] "Metric value binding"
            |> Result.bind (decodeBindingFloat (path + ".value"))
            |> Result.mapError (fun e ->
                if e.Message.Contains "expected JSON number" then
                    { e with
                        Message =
                            e.Message
                            + " — Metric is numeric-only (trendable KPI); a labeled TEXT fact belongs in Fact: {\"$type\":\"Fact\",\"label\":…,\"value\":…}" }
                else
                    e)

        // Phase 460 — the stylistic fields are omitted-when-default on the wire;
        // restore the identity default on absence (`CellFormat.None` /
        // `ToneVariant.Default` / `StyleWeight.Standard` / `Emphasis.Normal`).
        // Explicit values keep decoding (read-compat). Mirrors the Phase 147
        // `role`/`voice` decode in `decodeSemanticStyle`.
        let formatR =
            match tryField fields "format" with
            | None -> Ok CellFormat.None
            | Some v -> decodeCellFormat (path + ".format") v

        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let weightR =
            match tryField fields "weight" with
            | None -> Ok StyleWeight.Standard
            | Some v -> decodeWeight (path + ".weight") v

        let emphasisR =
            match tryField fields "emphasis" with
            | None -> Ok Emphasis.Normal
            | Some v -> decodeEmphasis (path + ".emphasis") v

        let trendR =
            match tryField fields "trend" with
            | None -> Ok None
            | Some v -> decodeBindingFloat (path + ".trend") v |> Result.map Some

        let trendFormatR =
            match tryField fields "trendFormat" with
            | None -> Ok None
            | Some v -> decodeCellFormat (path + ".trendFormat") v |> Result.map Some

        let iconR =
            match tryField fields "icon" with
            | None -> Ok None
            | Some v -> decodeIconSource (path + ".icon") v |> Result.map Some

        let subtextR =
            match tryField fields "subtext" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".subtext") v |> Result.map Some

        match labelR, sourceR, formatR, toneR, weightR, emphasisR, trendR, trendFormatR, iconR, subtextR with
        | Ok label, Ok source, Ok format, Ok tone, Ok weight, Ok emphasis, Ok trend, Ok trendFormat, Ok icon, Ok subtext ->
            Ok
                { Label = label
                  Value = source
                  Format = format
                  Tone = tone
                  Weight = weight
                  Emphasis = emphasis
                  Trend = trend
                  TrendFormat = trendFormat
                  Icon = icon
                  Subtext = subtext }
        | Error e, _, _, _, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _, _, _, _
        | _, _, Error e, _, _, _, _, _, _, _
        | _, _, _, Error e, _, _, _, _, _, _
        | _, _, _, _, Error e, _, _, _, _, _
        | _, _, _, _, _, Error e, _, _, _, _
        | _, _, _, _, _, _, Error e, _, _, _
        | _, _, _, _, _, _, _, Error e, _, _
        | _, _, _, _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, _, _, _, Error e -> Error e

let private decodeHeadingSpec (path: string) (j: Json) : Result<HeadingSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let levelR =
            requireField path fields "level" "heading level integer"
            |> Result.bind (requireInt (path + ".level"))

        let textR =
            requireField path fields "text" "heading TextSource"
            |> Result.bind (decodeTextSource (path + ".text"))

        let variantR =
            requireField path fields "variant" "HeadingVariant"
            |> Result.bind (decodeHeadingVariant (path + ".variant"))

        match levelR, textR, variantR with
        | Ok level, Ok text, Ok variant ->
            Ok
                { Level = level
                  Text = text
                  Variant = variant }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

let private decodeFactSpec (path: string) (j: Json) : Result<FactSpec, DecodeError> =
    // A labeled TEXT fact (2026-07-17 — the complementary kind the launch
    // eval showed missing: every model forced text facts into Metric's
    // numeric source). New kind, so its wire is minimal from day one:
    // only `label` + `value` required; `tone` / `emphasis` are
    // omitted-when-default on BOTH boundaries; `help` / `icon` optional.
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "fact TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let valueR =
            requireField path fields "value" "fact TextSource value"
            |> Result.bind (decodeTextSource (path + ".value"))

        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let emphasisR =
            match tryField fields "emphasis" with
            | None -> Ok false
            | Some v -> decodeEmphasisFlag (path + ".emphasis") v

        let helpR =
            match tryField fields "help" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".help") v |> Result.map Some

        let iconR =
            match tryField fields "icon" with
            | None -> Ok None
            | Some v -> decodeIconSource (path + ".icon") v |> Result.map Some

        match labelR, valueR, toneR, emphasisR, helpR, iconR with
        | Ok label, Ok value, Ok tone, Ok emphasis, Ok help, Ok icon ->
            Ok
                { Label = label
                  Value = value
                  Icon = icon
                  Tone = tone
                  Emphasis = emphasis
                  Help = help }
        | Error e, _, _, _, _, _
        | _, Error e, _, _, _, _
        | _, _, Error e, _, _, _
        | _, _, _, Error e, _, _
        | _, _, _, _, Error e, _
        | _, _, _, _, _, Error e -> Error e

let private decodeLabelValueRowSpec (path: string) (j: Json) : Result<LabelValueRowSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "row TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let sourceR =
            requireFieldAliased path fields "value" [ "data" ] "row Binding<float> value"
            |> Result.bind (decodeBindingFloat (path + ".value"))

        // Phase 460 — `format` omitted-when-default (`CellFormat.None`) on the
        // wire. The `emphasis` bool here is behavioural, not the `Emphasis` style
        // DU — out of this phase's stylistic scope, stays required.
        let formatR =
            match tryField fields "format" with
            | None -> Ok CellFormat.None
            | Some v -> decodeCellFormat (path + ".format") v

        // 0.2.2 — `emphasis` is omitted-when-false (aligning with Fact's
        // identical flag); the cross-vocabulary coercion moved to the shared
        // `decodeEmphasisFlag` (2026-07-19 sweep: the 0.2.2 site-local version
        // missed the Phase-460 aliases — pilot-4 saw 'Strong' hard-fail here —
        // and Fact's identical flag had no coercion at all).
        let emphasisR =
            match tryField fields "emphasis" with
            | None -> Ok false
            | Some v -> decodeEmphasisFlag (path + ".emphasis") v

        let helpR =
            match tryField fields "help" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".help") v |> Result.map Some

        match labelR, sourceR, formatR, emphasisR, helpR with
        | Ok label, Ok source, Ok format, Ok emphasis, Ok help ->
            Ok
                { Label = label
                  Value = source
                  Format = format
                  Emphasis = emphasis
                  Help = help }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeMarkdownSpec (path: string) (j: Json) : Result<MarkdownSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        requireField path fields "text" "markdown TextSource"
        |> Result.bind (decodeTextSource (path + ".text"))
        |> Result.map (fun text -> { Text = text })

let private decodeBadgeSpec (path: string) (j: Json) : Result<BadgeSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "badge label TextSource"
            |> Result.bind (decodeTextSource (path + ".label"))

        let variantR =
            requireField path fields "variant" "BadgeVariant"
            |> Result.bind (decodeBadgeVariant (path + ".variant"))

        match labelR, variantR with
        | Ok label, Ok variant -> Ok { Label = label; Variant = variant }
        | Error e, _
        | _, Error e -> Error e

let private decodeLinkSpec (path: string) (j: Json) : Result<LinkSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let hrefR =
            requireField path fields "href" "link Binding<string> Href"
            |> Result.bind (decodeBindingString (path + ".href"))

        let labelR =
            requireField path fields "label" "link label TextSource"
            |> Result.bind (decodeTextSource (path + ".label"))

        let downloadR =
            requireField path fields "download" "download bool"
            |> Result.bind (requireBool (path + ".download"))

        let relR =
            match tryField fields "rel" with
            | None -> Ok None
            | Some v -> requireString (path + ".rel") v |> Result.map Some

        let targetR =
            match tryField fields "target" with
            | None -> Ok None
            | Some v -> requireString (path + ".target") v |> Result.map Some

        match hrefR, labelR, downloadR, relR, targetR with
        | Ok href, Ok label, Ok download, Ok rel, Ok target ->
            Ok
                { Href = href
                  Label = label
                  Rel = rel
                  Target = target
                  Download = download }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeImageSpec (path: string) (j: Json) : Result<ImageSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let srcR =
            requireField path fields "src" "image Binding<string> Src"
            |> Result.bind (decodeBindingString (path + ".src"))

        let altR =
            requireField path fields "alt" "image alt TextSource"
            |> Result.bind (decodeTextSource (path + ".alt"))

        let variantR =
            requireField path fields "variant" "ImageVariant"
            |> Result.bind (decodeImageVariant (path + ".variant"))

        match srcR, altR, variantR with
        | Ok src, Ok alt, Ok variant ->
            Ok
                { Src = src
                  Alt = alt
                  Variant = variant }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

let private decodeListSpec (path: string) (j: Json) : Result<ListSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let itemsR =
            requireField path fields "items" "list items array"
            |> Result.bind (requireArray (path + ".items"))
            |> Result.bind (fun items ->
                items
                |> List.mapi (fun i it -> i, it)
                |> traverse (fun (i, it) -> decodeTextSource (sprintf "%s.items[%d]" path i) it))

        let orderedR =
            requireField path fields "ordered" "ordered bool"
            |> Result.bind (requireBool (path + ".ordered"))

        match itemsR, orderedR with
        | Ok items, Ok ordered -> Ok { Items = items; Ordered = ordered }
        | Error e, _
        | _, Error e -> Error e

let private decodeToastSpec (path: string) (j: Json) : Result<ToastSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let messageR =
            requireField path fields "message" "toast message TextSource"
            |> Result.bind (decodeTextSource (path + ".message"))

        // Phase 460 — `tone` omitted-when-default (`ToneVariant.Default`).
        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let openR =
            requireField path fields "open" "toast Binding<bool> Open"
            |> Result.bind (decodeBindingBool (path + ".open"))

        let dismissableR =
            match tryField fields "dismissable" with // 0.2.0 omitted-when-TRUE
            | None -> Ok true
            | Some v -> requireBool (path + ".dismissable") v

        match messageR, toneR, openR, dismissableR with
        | Ok message, Ok tone, Ok openB, Ok dismissable ->
            Ok
                { Message = message
                  Tone = tone
                  Open = openB
                  Dismissable = dismissable }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

let private decodeCodeBlockSpec (path: string) (j: Json) : Result<CodeBlockSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let codeR =
            requireField path fields "code" "code-block code string"
            |> Result.bind (requireString (path + ".code"))

        let languageR =
            requireField path fields "language" "code-block language string"
            |> Result.bind (requireString (path + ".language"))

        let lineNumbersR =
            requireField path fields "lineNumbers" "lineNumbers bool"
            |> Result.bind (requireBool (path + ".lineNumbers"))

        let copyableR =
            requireField path fields "copyable" "copyable bool"
            |> Result.bind (requireBool (path + ".copyable"))

        let highlightLinesR =
            requireField path fields "highlightLines" "highlightLines int array"
            |> Result.bind (requireArray (path + ".highlightLines"))
            |> Result.bind (fun items ->
                items
                |> List.mapi (fun i it -> i, it)
                |> traverse (fun (i, it) -> requireInt (sprintf "%s.highlightLines[%d]" path i) it))

        match codeR, languageR, lineNumbersR, copyableR, highlightLinesR with
        | Ok code, Ok language, Ok lineNumbers, Ok copyable, Ok highlightLines ->
            Ok
                { Code = code
                  Language = language
                  LineNumbers = lineNumbers
                  HighlightLines = highlightLines
                  Copyable = copyable }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeMathSpec (path: string) (j: Json) : Result<MathSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let sourceR =
            requireField path fields "source" "math LaTeX source string"
            |> Result.bind (requireString (path + ".source"))

        let displayR =
            requireField path fields "display" "MathDisplay"
            |> Result.bind (decodeMathDisplay (path + ".display"))

        match sourceR, displayR with
        | Ok source, Ok display -> Ok { Source = source; Display = display }
        | Error e, _
        | _, Error e -> Error e

let private decodeSparklineSpec (path: string) (j: Json) : Result<SparklineSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        requireFieldAliased path fields "source" [ "data" ] "sparkline Binding<float seq>"
        |> Result.bind (decodeBindingFloatSeq (path + ".source"))
        |> Result.map (fun source -> { Source = source })

let private decodeSkeletonSpec (path: string) (j: Json) : Result<SkeletonSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        requireField path fields "rows" "skeleton row count integer"
        |> Result.bind (requireInt (path + ".rows"))
        |> Result.map (fun rows -> { Rows = rows })

let private decodeCalloutSpec (path: string) (j: Json) : Result<CalloutSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        // Phase 460 — `tone` omitted-when-default (`ToneVariant.Default`).
        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let bodyR =
            requireField path fields "body" "callout body TextSource"
            |> Result.bind (decodeTextSource (path + ".body"))

        let dismissableR =
            match tryField fields "dismissable" with // 0.2.0 omitted-when-false
            | None -> Ok false
            | Some v -> requireBool (path + ".dismissable") v

        let headingR =
            match optFieldAliased fields "heading" [ "title" ] with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".heading") v |> Result.map Some

        let iconR =
            match tryField fields "icon" with
            | None -> Ok None
            | Some v -> decodeIconSource (path + ".icon") v |> Result.map Some

        match toneR, bodyR, dismissableR, headingR, iconR with
        | Ok tone, Ok body, Ok dismissable, Ok heading, Ok icon ->
            Ok
                { Tone = tone
                  Heading = heading
                  Body = body
                  Icon = icon
                  Dismissable = dismissable }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeProgressSpec (path: string) (j: Json) : Result<ProgressSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let fractionR =
            requireField path fields "fraction" "Binding<float> fraction"
            |> Result.bind (decodeBindingFloat (path + ".fraction"))

        let indeterminateR =
            // 0.2.0 — omitted-when-false on both boundaries.
            match tryField fields "indeterminate" with
            | None -> Ok false
            | Some v -> requireBool (path + ".indeterminate") v

        // Phase 460 — `tone` omitted-when-default (`ToneVariant.Default`).
        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let labelR =
            match tryField fields "label" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".label") v |> Result.map Some

        let caveatR =
            match tryField fields "caveat" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".caveat") v |> Result.map Some

        match fractionR, indeterminateR, toneR, labelR, caveatR with
        | Ok fraction, Ok indeterminate, Ok tone, Ok label, Ok caveat ->
            Ok
                { Fraction = fraction
                  Label = label
                  Caveat = caveat
                  Indeterminate = indeterminate
                  Tone = tone }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

// ─── Drawing (Phase 524) ────────────────────────────────────────────────────

let private emptyDrawStyle: DrawStyle =
    { Fill = None
      Stroke = None
      StrokeWidth = None
      Opacity = None
      TextAnchor = None
      FontSize = None
      Emphasis = None
      FontFamily = None
      MarkId = None }

let private decodeDrawPoint (path: string) (j: Json) : Result<DrawPoint, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let xR =
            requireField path fields "x" "DrawPoint x float"
            |> Result.bind (requireFloat (path + ".x"))

        let yR =
            requireField path fields "y" "DrawPoint y float"
            |> Result.bind (requireFloat (path + ".y"))

        match xR, yR with
        | Ok x, Ok y -> Ok { X = x; Y = y }
        | Error e, _
        | _, Error e -> Error e

let private decodeViewBox (path: string) (j: Json) : Result<ViewBox, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let minXR =
            requireField path fields "minX" "ViewBox minX float"
            |> Result.bind (requireFloat (path + ".minX"))

        let minYR =
            requireField path fields "minY" "ViewBox minY float"
            |> Result.bind (requireFloat (path + ".minY"))

        let widthR =
            requireField path fields "width" "ViewBox width float"
            |> Result.bind (requireFloat (path + ".width"))

        let heightR =
            requireField path fields "height" "ViewBox height float"
            |> Result.bind (requireFloat (path + ".height"))

        match minXR, minYR, widthR, heightR with
        | Ok minX, Ok minY, Ok width, Ok height ->
            Ok
                { MinX = minX
                  MinY = minY
                  Width = width
                  Height = height }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

let private decodeTextAnchor (path: string) (j: Json) : Result<TextAnchor, DecodeError> =
    match requireString path j with
    | Error e -> Error e
    | Ok "Start" -> Ok TextAnchor.Start
    | Ok "Middle" -> Ok TextAnchor.Middle
    | Ok "End" -> Ok TextAnchor.End
    | Ok s -> unknownDuCase path s "Start | Middle | End"

let private decodeDrawStyle (path: string) (j: Json) : Result<DrawStyle, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let fillR =
            match tryField fields "fill" with
            | None -> Ok None
            | Some v -> decodeBindingString (path + ".fill") v |> Result.map Some

        let strokeR =
            match tryField fields "stroke" with
            | None -> Ok None
            | Some v -> decodeBindingString (path + ".stroke") v |> Result.map Some

        let strokeWidthR =
            match tryField fields "strokeWidth" with
            | None -> Ok None
            | Some v -> decodeBindingFloat (path + ".strokeWidth") v |> Result.map Some

        let opacityR =
            match tryField fields "opacity" with
            | None -> Ok None
            | Some v -> decodeBindingFloat (path + ".opacity") v |> Result.map Some

        // Text-only fields (Phase 528.1) — all optional.
        let textAnchorR =
            match tryField fields "textAnchor" with
            | None -> Ok None
            | Some v -> decodeTextAnchor (path + ".textAnchor") v |> Result.map Some

        let fontSizeR =
            match tryField fields "fontSize" with
            | None -> Ok None
            | Some v -> requireFloat (path + ".fontSize") v |> Result.map Some

        let emphasisR =
            match tryField fields "emphasis" with
            | None -> Ok None
            | Some v -> decodeEmphasis (path + ".emphasis") v |> Result.map Some

        let fontFamilyR =
            match tryField fields "fontFamily" with
            | None -> Ok None
            | Some v -> requireString (path + ".fontFamily") v |> Result.map Some

        // Phase 642 — keyed mark identity; optional.
        let markIdR =
            match tryField fields "markId" with
            | None -> Ok None
            | Some v -> requireString (path + ".markId") v |> Result.map Some

        match fillR, strokeR, strokeWidthR, opacityR, textAnchorR, fontSizeR, emphasisR, fontFamilyR, markIdR with
        | Ok fill,
          Ok stroke,
          Ok strokeWidth,
          Ok opacity,
          Ok textAnchor,
          Ok fontSize,
          Ok emphasis,
          Ok fontFamily,
          Ok markId ->
            Ok
                { Fill = fill
                  Stroke = stroke
                  StrokeWidth = strokeWidth
                  Opacity = opacity
                  TextAnchor = textAnchor
                  FontSize = fontSize
                  Emphasis = emphasis
                  FontFamily = fontFamily
                  MarkId = markId }
        | Error e, _, _, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _, _, _
        | _, _, Error e, _, _, _, _, _, _
        | _, _, _, Error e, _, _, _, _, _
        | _, _, _, _, Error e, _, _, _, _
        | _, _, _, _, _, Error e, _, _, _
        | _, _, _, _, _, _, Error e, _, _
        | _, _, _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, _, _, Error e -> Error e

let private decodeCurveCommand (path: string) (j: Json) : Result<CurveCommand, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            let pointField key =
                requireField path fields key ("DrawPoint " + key)
                |> Result.bind (decodeDrawPoint (path + "." + key))

            match disc with
            | "MoveTo" -> pointField "to" |> Result.map CurveCommand.MoveTo
            | "LineTo" -> pointField "to" |> Result.map CurveCommand.LineTo
            | "CubicTo" ->
                match pointField "control1", pointField "control2", pointField "to" with
                | Ok c1, Ok c2, Ok e -> Ok(CurveCommand.CubicTo(c1, c2, e))
                | Error e, _, _
                | _, Error e, _
                | _, _, Error e -> Error e
            | "QuadraticTo" ->
                match pointField "control", pointField "to" with
                | Ok ctrl, Ok e -> Ok(CurveCommand.QuadraticTo(ctrl, e))
                | Error e, _
                | _, Error e -> Error e
            | "Close" -> Ok CurveCommand.Close
            // Default-deny (WIRE_FORMAT §11 / Phase 524): an unrecognised curve
            // command is a typed defect, not a pass-through.
            | s -> unknownDuCase path s "MoveTo | LineTo | CubicTo | QuadraticTo | Close"

let rec private decodeShape (path: string) (j: Json) : Result<Shape, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            let floatField key =
                requireField path fields key ("Shape " + key + " float")
                |> Result.bind (requireFloat (path + "." + key))

            let styleR =
                match tryField fields "style" with
                | None -> Ok emptyDrawStyle
                | Some v -> decodeDrawStyle (path + ".style") v

            let pointArray key =
                requireField path fields key ("Shape " + key + " DrawPoint array")
                |> Result.bind (requireArray (path + "." + key))
                |> Result.bind (traverseIndexed (fun i item -> decodeDrawPoint (sprintf "%s.%s[%d]" path key i) item))

            match disc with
            | "Group" ->
                let childrenR =
                    requireField path fields "children" "Shape children array"
                    |> Result.bind (requireArray (path + ".children"))
                    |> Result.bind (traverseIndexed (fun i item -> decodeShape (sprintf "%s.children[%d]" path i) item))

                match childrenR, styleR with
                | Ok children, Ok style -> Ok(Shape.Group(children, style))
                | Error e, _
                | _, Error e -> Error e
            | "Rectangle" ->
                let cornerRadiusR =
                    match tryField fields "cornerRadius" with
                    | None -> Ok None
                    | Some v -> requireFloat (path + ".cornerRadius") v |> Result.map Some

                match
                    floatField "x", floatField "y", floatField "width", floatField "height", cornerRadiusR, styleR
                with
                | Ok x, Ok y, Ok w, Ok h, Ok cornerRadius, Ok style ->
                    Ok(Shape.Rectangle(x, y, w, h, cornerRadius, style))
                | Error e, _, _, _, _, _
                | _, Error e, _, _, _, _
                | _, _, Error e, _, _, _
                | _, _, _, Error e, _, _
                | _, _, _, _, Error e, _
                | _, _, _, _, _, Error e -> Error e
            | "Line" ->
                match floatField "x1", floatField "y1", floatField "x2", floatField "y2", styleR with
                | Ok x1, Ok y1, Ok x2, Ok y2, Ok style -> Ok(Shape.Line(x1, y1, x2, y2, style))
                | Error e, _, _, _, _
                | _, Error e, _, _, _
                | _, _, Error e, _, _
                | _, _, _, Error e, _
                | _, _, _, _, Error e -> Error e
            | "Polyline" ->
                match pointArray "points", styleR with
                | Ok points, Ok style -> Ok(Shape.Polyline(points, style))
                | Error e, _
                | _, Error e -> Error e
            | "Polygon" ->
                match pointArray "points", styleR with
                | Ok points, Ok style -> Ok(Shape.Polygon(points, style))
                | Error e, _
                | _, Error e -> Error e
            | "Curve" ->
                let commandsR =
                    requireField path fields "commands" "Shape Curve commands array"
                    |> Result.bind (requireArray (path + ".commands"))
                    |> Result.bind (
                        traverseIndexed (fun i item -> decodeCurveCommand (sprintf "%s.commands[%d]" path i) item)
                    )

                match commandsR, styleR with
                | Ok commands, Ok style -> Ok(Shape.Curve(commands, style))
                | Error e, _
                | _, Error e -> Error e
            | "Circle" ->
                match floatField "cx", floatField "cy", floatField "r", styleR with
                | Ok cx, Ok cy, Ok r, Ok style -> Ok(Shape.Circle(cx, cy, r, style))
                | Error e, _, _, _
                | _, Error e, _, _
                | _, _, Error e, _
                | _, _, _, Error e -> Error e
            | "Ellipse" ->
                match floatField "cx", floatField "cy", floatField "rx", floatField "ry", styleR with
                | Ok cx, Ok cy, Ok rx, Ok ry, Ok style -> Ok(Shape.Ellipse(cx, cy, rx, ry, style))
                | Error e, _, _, _, _
                | _, Error e, _, _, _
                | _, _, Error e, _, _
                | _, _, _, Error e, _
                | _, _, _, _, Error e -> Error e
            | "Label" ->
                let textR =
                    requireField path fields "text" "Shape Label text TextSource"
                    |> Result.bind (decodeTextSource (path + ".text"))

                match floatField "x", floatField "y", textR, styleR with
                | Ok x, Ok y, Ok text, Ok style -> Ok(Shape.Label(x, y, text, style))
                | Error e, _, _, _
                | _, Error e, _, _
                | _, _, Error e, _
                | _, _, _, Error e -> Error e
            // Default-deny (WIRE_FORMAT §11 / Phase 524 typed-surface guard): an
            // unrecognised shape is a typed defect, not a pass-through.
            | s ->
                unknownDuCase path s "Group | Rectangle | Line | Polyline | Polygon | Curve | Circle | Ellipse | Label"

let private decodeDrawingSpec (path: string) (j: Json) : Result<DrawingSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let viewBoxR =
            requireField path fields "viewBox" "Drawing ViewBox"
            |> Result.bind (decodeViewBox (path + ".viewBox"))

        let shapesR =
            requireField path fields "shapes" "Drawing shapes array"
            |> Result.bind (requireArray (path + ".shapes"))
            |> Result.bind (traverseIndexed (fun i item -> decodeShape (sprintf "%s.shapes[%d]" path i) item))

        let styleR =
            match tryField fields "style" with
            | None -> Ok emptyDrawStyle
            | Some v -> decodeDrawStyle (path + ".style") v

        let titleR =
            match tryField fields "title" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".title") v |> Result.map Some

        let descriptionR =
            match tryField fields "description" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".description") v |> Result.map Some

        match viewBoxR, shapesR, styleR, titleR, descriptionR with
        | Ok viewBox, Ok shapes, Ok style, Ok title, Ok description ->
            Ok
                { ViewBox = viewBox
                  Shapes = shapes
                  Style = style
                  Title = title
                  Description = description }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeDisplayKind (path: string) (j: Json) : Result<DisplayKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            // Flat wire (WIRE_FORMAT §3.2): spec fields are hoisted directly
            // into the kind object, so the spec decoders read from `j` at
            // `path` (the extra `$type` key is ignored — decoders tolerate
            // unknown fields per rule 2).
            let specPath = path

            let getSpec () = Ok j

            match disc with
            | "Heading" ->
                getSpec ()
                |> Result.bind (decodeHeadingSpec specPath)
                |> Result.map DisplayKind.Heading
            | "Markdown" ->
                getSpec ()
                |> Result.bind (decodeMarkdownSpec specPath)
                |> Result.map DisplayKind.Markdown
            | "Metric" ->
                getSpec ()
                |> Result.bind (decodeMetricSpec specPath)
                |> Result.map DisplayKind.Metric
            | "Badge" ->
                getSpec ()
                |> Result.bind (decodeBadgeSpec specPath)
                |> Result.map DisplayKind.Badge
            | "Sparkline" ->
                getSpec ()
                |> Result.bind (decodeSparklineSpec specPath)
                |> Result.map DisplayKind.Sparkline
            | "Callout" ->
                getSpec ()
                |> Result.bind (decodeCalloutSpec specPath)
                |> Result.map DisplayKind.Callout
            | "Progress" ->
                getSpec ()
                |> Result.bind (decodeProgressSpec specPath)
                |> Result.map DisplayKind.Progress
            | "Skeleton" ->
                getSpec ()
                |> Result.bind (decodeSkeletonSpec specPath)
                |> Result.map DisplayKind.Skeleton
            | "LabelValueRow" ->
                getSpec ()
                |> Result.bind (decodeLabelValueRowSpec specPath)
                |> Result.map DisplayKind.LabelValueRow
            | "Fact" ->
                getSpec ()
                |> Result.bind (decodeFactSpec specPath)
                |> Result.map DisplayKind.Fact
            | "Link" ->
                getSpec ()
                |> Result.bind (decodeLinkSpec specPath)
                |> Result.map DisplayKind.Link
            | "Image" ->
                getSpec ()
                |> Result.bind (decodeImageSpec specPath)
                |> Result.map DisplayKind.Image
            | "List" ->
                getSpec ()
                |> Result.bind (decodeListSpec specPath)
                |> Result.map DisplayKind.List
            | "Toast" ->
                getSpec ()
                |> Result.bind (decodeToastSpec specPath)
                |> Result.map DisplayKind.Toast
            | "CodeBlock" ->
                getSpec ()
                |> Result.bind (decodeCodeBlockSpec specPath)
                |> Result.map DisplayKind.CodeBlock
            | "Math" ->
                getSpec ()
                |> Result.bind (decodeMathSpec specPath)
                |> Result.map DisplayKind.Math
            | "Drawing" ->
                getSpec ()
                |> Result.bind (decodeDrawingSpec specPath)
                |> Result.map DisplayKind.Drawing
            | s ->
                unknownDuCase
                    path
                    s
                    "Heading | Markdown | Metric | Badge | Link | Image | List | Toast | CodeBlock | Math | Drawing | Sparkline | Callout | Progress | Skeleton | LabelValueRow | Fact"

// ─── Input ───────────────────────────────────────────────────────────────

/// Phase 596 — the auto-bind context for a control's ABSENT `value` slot.
/// One rule across the whole control vocabulary: every control may omit
/// `value`; a filter chip auto-binds `$filters.<name>` and a form field
/// auto-binds `$state.<field id>` (with the slot's typed placeholder from
/// `Defaults.ControlValueDefaults` as the State default). `NoAutoBind` (an
/// erased/unknown context) keeps `value` required — MISSING_FIELD.
type internal ControlAutoBind =
    | NoAutoBind
    | FilterChip of name: string
    | FormFieldId of id: string

let private decodeFormFieldKind
    (autoBind: ControlAutoBind)
    (path: string)
    (j: Json)
    : Result<FormFieldKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        // Handlers are optional (Phase 426, the Phase 423 `FilterKind.onChange`
        // mechanics generalised): a present `"<closure>"` decodes to the inert
        // placeholder (`Some (fun _ -> Chain [])` — a decoded F# closure never
        // dispatches, same dead behaviour as before); an absent field decodes
        // to `None`, the shape that arms the renderer's write-back default.
        let inline handlerOpt (name: string) : ('v -> Action<obj>) option =
            match tryField fields name with
            | Some _ -> Some(fun _ -> Action.Chain [])
            | None -> None

        // Value slot: present ⇒ typed decode; absent ⇒ the context's
        // auto-binding — Filter(name) on a chip, State(field id, typed
        // placeholder) on a form field (Phase 596) — else MISSING_FIELD.
        let valueOr
            (dec: string -> Json -> Result<Binding<'v>, DecodeError>)
            (autoDefault: 'v)
            (expected: string)
            : Result<Binding<'v>, DecodeError> =
            match tryField fields "value" with
            | Some v -> dec (path + ".value") v
            | None ->
                match autoBind with
                | FilterChip n -> Ok(Binding.Filter(n, None))
                | FormFieldId id -> Ok(Binding.State(id, autoDefault))
                | NoAutoBind -> missingField path "value" expected

        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Text" ->
            valueOr decodeBindingString Fuaran.UI.Defaults.ControlValueDefaults.text "Binding<string> value"
            |> Result.map (fun value -> FormFieldKind.Text(value, handlerOpt "onChange"))
        | Ok "Number" ->
            valueOr decodeBindingFloat Fuaran.UI.Defaults.ControlValueDefaults.number "Binding<float> value"
            |> Result.map (fun value -> FormFieldKind.Number(value, handlerOpt "onChange"))
        | Ok "Checkbox" ->
            valueOr decodeBindingBool Fuaran.UI.Defaults.ControlValueDefaults.checkbox "Binding<bool> value"
            |> Result.map (fun value -> FormFieldKind.Checkbox(value, handlerOpt "onToggle"))
        | Ok "Choice" ->
            let optionsR =
                requireField path fields "options" "Binding<SelectOption list>"
                |> Result.bind (decodeBindingSelectOptions (path + ".options"))

            let valueR =
                valueOr decodeBindingStringOpt Fuaran.UI.Defaults.ControlValueDefaults.choice "Binding<string option>"

            match optionsR, valueR with
            | Ok options, Ok value -> Ok(FormFieldKind.Choice(options, value, handlerOpt "onChange"))
            | Error e, _
            | _, Error e -> Error e
        | Ok "Range" ->
            // 0.2.0 — dual-thumb numeric range (absorbed FilterKind.RangeFilter).
            let valueR =
                match tryField fields "value" with
                | Some v -> decodeBindingFloatPair (path + ".value") v
                | None ->
                    match autoBind with
                    | FilterChip n -> Ok(Binding.Filter(n, None))
                    | FormFieldId id -> Ok(Binding.State(id, Fuaran.UI.Defaults.ControlValueDefaults.range))
                    | NoAutoBind -> missingField path "value" "Binding<float * float> value"

            valueR
            |> Result.map (fun value -> FormFieldKind.Range(value, handlerOpt "onChange", None))
        | Ok "RangedNumber" ->
            // Parallel-additive Number case carrying optional
            // Min / Max / Step bounds at the field level. Absent keys
            // decode as `None` (mirrors the encoder's omit-when-None
            // discipline). Wire shape:
            //   { "$type": "RangedNumber", "value": <Binding>, "onChange":
            //     "<closure>", "min": <float|absent>, "max": <float|absent>,
            //     "step": <float|absent> }
            let valueR =
                valueOr decodeBindingFloat Fuaran.UI.Defaults.ControlValueDefaults.number "Binding<float> value"

            let minR =
                match tryField fields "min" with
                | None -> Ok None
                | Some j -> requireFloat (path + ".min") j |> Result.map Some

            let maxR =
                match tryField fields "max" with
                | None -> Ok None
                | Some j -> requireFloat (path + ".max") j |> Result.map Some

            let stepR =
                match tryField fields "step" with
                | None -> Ok None
                | Some j -> requireFloat (path + ".step") j |> Result.map Some

            match valueR, minR, maxR, stepR with
            | Ok value, Ok min, Ok max, Ok step ->
                Ok(FormFieldKind.RangedNumber(value, handlerOpt "onChange", { Min = min; Max = max; Step = step }))
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e
        | Ok "TextArea" ->
            let valueR =
                valueOr decodeBindingString Fuaran.UI.Defaults.ControlValueDefaults.text "Binding<string> value"

            let rowsR =
                requireField path fields "rows" "rows integer"
                |> Result.bind (requireInt (path + ".rows"))

            match valueR, rowsR with
            | Ok value, Ok rows -> Ok(FormFieldKind.TextArea(value, handlerOpt "onChange", rows))
            | Error e, _
            | _, Error e -> Error e
        | Ok "SegmentedChoice" ->
            // Parallel-additive Choice case with an `orientation`
            // field. Wire shape mirrors `Choice` plus the orientation
            // string ("Horizontal" / "Vertical" — canonical Orientation
            // discriminator).
            let optionsR =
                requireField path fields "options" "Binding<SelectOption list>"
                |> Result.bind (decodeBindingSelectOptions (path + ".options"))

            let valueR =
                valueOr decodeBindingStringOpt Fuaran.UI.Defaults.ControlValueDefaults.choice "Binding<string option>"

            let orientationR =
                // Lenient AI-ingest omitted-when-default (WIRE_FORMAT.md §3.6
                // family): absent `orientation` restores the language default
                // `Horizontal` (Defaults.fs) — the universal segmented-control
                // prior; observed omitted in eval emission data. Decode-only:
                // the encoder still always emits it.
                match tryField fields "orientation" with
                | None -> Ok Horizontal
                | Some oJ -> decodeOrientation (path + ".orientation") oJ

            match optionsR, valueR, orientationR with
            | Ok options, Ok value, Ok orientation ->
                Ok(FormFieldKind.SegmentedChoice(options, value, handlerOpt "onChange", orientation))
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "Date" ->
            // Phase 288 — date/time field. `value` is a Binding<string> ISO
            // value; `variant` selects the control; optional min/max (ISO
            // strings) + step (seconds) decode to None when absent.
            let valueR =
                valueOr decodeBindingString Fuaran.UI.Defaults.ControlValueDefaults.date "Binding<string> value"

            let variantR =
                requireField path fields "variant" "DateVariant"
                |> Result.bind (decodeDateVariant (path + ".variant"))

            let minR =
                match tryField fields "min" with
                | None -> Ok None
                | Some j -> requireString (path + ".min") j |> Result.map Some

            let maxR =
                match tryField fields "max" with
                | None -> Ok None
                | Some j -> requireString (path + ".max") j |> Result.map Some

            let stepR =
                match tryField fields "step" with
                | None -> Ok None
                | Some j -> requireFloat (path + ".step") j |> Result.map Some

            match valueR, variantR, minR, maxR, stepR with
            | Ok value, Ok variant, Ok min, Ok max, Ok step ->
                Ok(FormFieldKind.Date(value, handlerOpt "onChange", variant, { Min = min; Max = max; Step = step }))
            | Error e, _, _, _, _
            | _, Error e, _, _, _
            | _, _, Error e, _, _
            | _, _, _, Error e, _
            | _, _, _, _, Error e -> Error e
        | Ok s ->
            unknownDuCase path s "Text | Number | RangedNumber | Checkbox | Choice | SegmentedChoice | TextArea | Date"

let private decodeFormField (path: string) (j: Json) : Result<FormField<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let idR =
            // Field alias: name — the HTML-forms prior for the field's identity.
            requireFieldAliased path fields "id" [ "name" ] "form-field id string"
            |> Result.bind (requireString (path + ".id"))

        // Phase 596 — id decodes first so the form context's auto-bind can
        // use it (the chip-name-first precedent from the filters unification).
        let kindR =
            match idR with
            | Error e -> Error e
            | Ok id ->
                requireField path fields "kind" "FormFieldKind"
                |> Result.bind (decodeFormFieldKind (FormFieldId id) (path + ".kind"))

        let labelR =
            requireField path fields "label" "form-field TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let requiredR =
            requireField path fields "required" "required bool"
            |> Result.bind (requireBool (path + ".required"))

        let helpR =
            match tryField fields "help" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".help") v |> Result.map Some

        match idR, kindR, labelR, requiredR, helpR with
        | Ok id, Ok kind, Ok label, Ok required, Ok help ->
            Ok
                { Id = id
                  Label = label
                  Kind = kind
                  Required = required
                  Help = help }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeFormSpec (path: string) (j: Json) : Result<FormSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let fieldsR =
            match requireField path fields "fields" "form-field list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".fields") v with
                | Error e -> Error e
                | Ok xs -> traverseIndexed (fun i item -> decodeFormField (sprintf "%s.fields[%d]" path i) item) xs

        let onSubmitR =
            requireField path fields "onSubmit" "submit Action"
            |> Result.bind (decodeAction (path + ".onSubmit"))

        let submitLabelR =
            requireField path fields "submitLabel" "submit-label TextSource"
            |> Result.bind (decodeTextSource (path + ".submitLabel"))

        let disabledR =
            match tryField fields "disabled" with
            | None -> Ok None
            | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

        match fieldsR, onSubmitR, submitLabelR, disabledR with
        | Ok formFields, Ok onSubmit, Ok submitLabel, Ok disabled ->
            Ok
                { Fields = formFields
                  OnSubmit = onSubmit
                  SubmitLabel = submitLabel
                  Disabled = disabled }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

let private decodeFilterSpec (path: string) (j: Json) : Result<FilterSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let nameR =
            requireField path fields "name" "filter name string"
            |> Result.bind (requireString (path + ".name"))

        let labelR =
            requireField path fields "label" "filter TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        // 0.2.0 filters-unification: the chip's control is an ordinary
        // FormFieldKind; its absent `value` auto-binds Filter(name) (see
        // decodeFormFieldKind). Name decodes first so the synthesis can use it.
        let kindR =
            match nameR with
            | Error e -> Error e
            | Ok name ->
                requireField path fields "kind" "FormFieldKind control"
                |> Result.bind (decodeFormFieldKind (FilterChip name) (path + ".kind"))

        match nameR, labelR, kindR with
        | Ok name, Ok label, Ok field ->
            Ok
                { Name = name
                  Label = label
                  Field = field }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

let private decodeButtonSpec (path: string) (j: Json) : Result<ButtonSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "button TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let onClickR =
            requireField path fields "onClick" "click Action"
            |> Result.bind (decodeAction (path + ".onClick"))

        let variantR =
            requireField path fields "variant" "ButtonVariant"
            |> Result.bind (decodeButtonVariant (path + ".variant"))

        let iconR =
            match tryField fields "icon" with
            | None -> Ok None
            | Some v -> decodeIconSource (path + ".icon") v |> Result.map Some

        let disabledR =
            match tryField fields "disabled" with
            | None -> Ok None
            | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

        match labelR, onClickR, variantR, iconR, disabledR with
        | Ok label, Ok onClick, Ok variant, Ok icon, Ok disabled ->
            // Tooltip is currently not emitted by the encoder; decode to
            // None. See `docs/migrations/12-E-0-json-decoder.md` "Encoder
            // gaps" for the tracked follow-up.
            Ok
                { Label = label
                  OnClick = onClick
                  Variant = variant
                  Icon = icon
                  Tooltip = None
                  Disabled = disabled }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeSelectSpec (path: string) (j: Json) : Result<SelectSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            requireField path fields "label" "select TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let sourceR =
            requireFieldAliased path fields "source" [ "options"; "data" ] "Binding<SelectOption list>"
            |> Result.bind (decodeBindingSelectOptions (path + ".source"))

        let valueR =
            requireField path fields "value" "Binding<string option>"
            |> Result.bind (decodeBindingStringOpt (path + ".value"))

        let placeholderR =
            match tryField fields "placeholder" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".placeholder") v |> Result.map Some

        let disabledR =
            match tryField fields "disabled" with
            | None -> Ok None
            | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

        // Phase 291 — multi-select. `multiple` absent ⇒ false (single-select);
        // `values` absent ⇒ None.
        let multipleR =
            match tryField fields "multiple" with
            | None -> Ok false
            | Some v -> requireBool (path + ".multiple") v

        let valuesR =
            match tryField fields "values" with
            | None -> Ok Option.None
            | Some v -> decodeBindingStringList (path + ".values") v |> Result.map Some

        match labelR, sourceR, valueR, placeholderR, disabledR, multipleR, valuesR with
        | Ok label, Ok source, Ok value, Ok placeholder, Ok disabled, Ok multiple, Ok values ->
            // Handlers are optional (Phase 426): a present `"<closure>"`
            // sentinel decodes to the inert placeholder `Some`; an absent key
            // decodes to `None`, arming the renderer's write-back default
            // against `value` / `values`.
            Ok
                { Label = label
                  Source = source
                  Value = value
                  OnChange =
                    (match tryField fields "onChange" with
                     | Some _ -> Some(fun _ -> Action.Chain [])
                     | None -> Option.None)
                  Placeholder = placeholder
                  Disabled = disabled
                  Multiple = multiple
                  Values = values
                  OnChangeMulti =
                    (match tryField fields "onChangeMulti" with
                     | Some _ -> Some(fun _ -> Action.Chain [])
                     | None -> Option.None) }
        | Error e, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _
        | _, _, Error e, _, _, _, _
        | _, _, _, Error e, _, _, _
        | _, _, _, _, Error e, _, _
        | _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, Error e -> Error e

let private decodeFileUploadSpec (path: string) (j: Json) : Result<FileUploadSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let acceptR =
            match requireField path fields "accept" "accept MIME list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".accept") v with
                | Error e -> Error e
                | Ok xs -> traverseIndexed (fun i item -> requireString (sprintf "%s.accept[%d]" path i) item) xs

        let labelR =
            requireField path fields "label" "upload TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))

        let multipleR =
            requireField path fields "multiple" "multiple bool"
            |> Result.bind (requireBool (path + ".multiple"))

        let disabledR =
            match tryField fields "disabled" with
            | None -> Ok None
            | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

        match acceptR, labelR, multipleR, disabledR with
        | Ok accept, Ok label, Ok multiple, Ok disabled ->
            Ok
                { Label = label
                  Accept = accept
                  Multiple = multiple
                  OnSelect = (fun _ -> Action.Chain [])
                  Disabled = disabled }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

let private decodeInputKind (path: string) (j: Json) : Result<InputKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Form" -> decodeFormSpec path j |> Result.map InputKind.Form
        | Ok "Filters" ->
            requireField path fields "items" "FilterSpec list"
            |> Result.bind (fun v -> requireArray (path + ".items") v)
            |> Result.bind (fun xs ->
                traverseIndexed (fun i item -> decodeFilterSpec (sprintf "%s.items[%d]" path i) item) xs)
            |> Result.map InputKind.Filters
        | Ok "Button" -> decodeButtonSpec path j |> Result.map InputKind.Button
        | Ok "FileUpload" -> decodeFileUploadSpec path j |> Result.map InputKind.FileUpload
        | Ok "Select" -> decodeSelectSpec path j |> Result.map InputKind.Select
        | Ok s -> unknownDuCase path s "Form | Filters | Button | FileUpload | Select"

// ─── Visualisation ──────────────────────────────────────────────────────

let private decodeCellKindErased (path: string) (j: Json) : Result<CellKindErased<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Text" -> Ok CellKindErased.Text
        | Ok "Numeric" -> Ok CellKindErased.Numeric
        | Ok "Date" -> Ok CellKindErased.Date
        | Ok "Editable" -> Ok(CellKindErased.Editable(fun _ -> Action.Chain []))
        | Ok "Checkbox" -> Ok(CellKindErased.Checkbox((fun _ -> false), (fun _ -> Action.Chain [])))
        | Ok "Button" ->
            requireField path fields "label" "button TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))
            |> Result.map (fun label -> CellKindErased.Button(label, (fun _ -> Action.Chain [])))
        | Ok "ButtonGroup" ->
            requireField path fields "buttons" "button-group list"
            |> Result.bind (fun v -> requireArray (path + ".buttons") v)
            |> Result.bind (fun xs ->
                traverseIndexed
                    (fun i item ->
                        let p = sprintf "%s.buttons[%d]" path i

                        match requireObject p item with
                        | Error e -> Error e
                        | Ok bf ->
                            requireField p bf "label" "button TextSource label"
                            |> Result.bind (decodeTextSource (p + ".label"))
                            |> Result.map (fun label -> label, (fun (_: obj) -> Action.Chain [])))
                    xs)
            |> Result.map CellKindErased.ButtonGroup
        | Ok "Link" ->
            Ok(CellKindErased.Link((fun _ -> closureSentinel), (fun _ -> TextSource.Literal closureSentinel)))
        | Ok "Pill" ->
            Ok(CellKindErased.Pill((fun _ -> TextSource.Literal closureSentinel), (fun _ -> ToneVariant.Default)))
        | Ok "Progress" -> Ok(CellKindErased.Progress((fun _ -> 0.0), None))
        | Ok "Custom" -> Ok(CellKindErased.Custom(fun _ -> placeholderClosureNode))
        | Ok s ->
            unknownDuCase
                path
                s
                "Text | Numeric | Date | Editable | Checkbox | Button | ButtonGroup | Link | Pill | Progress | Custom"

let private decodeColumnErased (path: string) (j: Json) : Result<ColumnErased<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        // Phase 460 — `format` / `width` omitted-when-default (`CellFormat.None` /
        // `ColumnWidth.Auto`); restore the identity default on absence (read-compat
        // for explicit values). The `width` seam was masked in the 422 data because
        // decode stops at the first error — same seam, swept here.
        let formatR =
            match tryField fields "format" with
            | None -> Ok CellFormat.None
            | Some v -> decodeCellFormat (path + ".format") v

        let kindR =
            // Field alias: type — the universal JSON prior for a column's kind.
            requireFieldAliased path fields "kind" [ "type" ] "CellKindErased"
            |> Result.bind (decodeCellKindErased (path + ".kind"))

        let labelR =
            // Field aliases: header/title — the react-table / antd prior.
            requireFieldAliased path fields "label" [ "header"; "title" ] "column label string"
            |> Result.bind (requireString (path + ".label"))

        let widthR =
            match tryField fields "width" with
            | None -> Ok ColumnWidth.Auto
            | Some v -> decodeColumnWidth (path + ".width") v

        // Phase 425 — `value` (closure) and `field` (declarative) are sibling optional slots. A present
        // `"value":"<closure>"` decodes to `Some` placeholder (the closure "wins" — renders Empty, same
        // dead behaviour as before); an absent value → `None`, and `field` (if present) drives the
        // row-field projection so the cell renders data with zero host code.
        let value =
            match tryField fields "value" with
            | Some _ -> Some(fun (_: obj) -> CellValue.Empty)
            | None -> None

        let fieldR =
            match tryField fields "field" with
            | None -> Ok None
            | Some fJ -> requireString (path + ".field") fJ |> Result.map Some

        match formatR, kindR, labelR, widthR, fieldR with
        | Ok format, Ok kind, Ok label, Ok width, Ok field ->
            Ok
                { Label = label
                  Value = value
                  Field = field
                  Format = format
                  Kind = kind
                  Width = width }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

/// Phase 393 — decode the `{headers, rows}` static-rows object of a read-only grid
/// (also the shape the legacy `Table` decode-upgrade reads). Cells are `TextSource`.
let private decodeStaticRows (path: string) (j: Json) : Result<TextSource list * TextSource list list, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let headersR =
            match requireField path fields "headers" "header TextSource list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".headers") v with
                | Error e -> Error e
                | Ok xs -> traverseIndexed (fun i item -> decodeTextSource (sprintf "%s.headers[%d]" path i) item) xs

        let rowsR =
            match requireField path fields "rows" "row list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".rows") v with
                | Error e -> Error e
                | Ok rows ->
                    traverseIndexed
                        (fun i row ->
                            let p = sprintf "%s.rows[%d]" path i

                            match requireArray p row with
                            | Error e -> Error e
                            | Ok cells ->
                                traverseIndexed (fun j cell -> decodeTextSource (sprintf "%s[%d]" p j) cell) cells)
                        rows

        match headersR, rowsR with
        | Ok headers, Ok rows -> Ok(headers, rows)
        | Error e, _
        | _, Error e -> Error e

let private decodeGridSpec (path: string) (j: Json) : Result<GridSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let columnsR =
            match requireField path fields "columns" "column list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".columns") v with
                | Error e -> Error e
                | Ok xs -> traverseIndexed (fun i item -> decodeColumnErased (sprintf "%s.columns[%d]" path i) item) xs

        let editableR =
            match tryField fields "editable" with // 0.2.0 omitted-when-false
            | None -> Ok false
            | Some v -> requireBool (path + ".editable") v

        let sourceR =
            requireFieldAliased path fields "source" [ "data"; "rows" ] "Binding<obj seq> Source"
            |> Result.bind (decodeBindingObjSeq (path + ".source"))

        let onRowClickR: Result<(obj -> Action<obj>) option, DecodeError> =
            match tryField fields "onRowClick" with
            | None -> Ok None
            | Some _ -> Ok(Some(fun _ -> Action.Chain []))

        // Phase 425 — `rowKey` (closure) + `rowKeyField` (declarative) are sibling optional slots.
        let rowKey =
            match tryField fields "rowKey" with
            | Some _ -> Some(fun (_: obj) -> closureSentinel)
            | None -> None

        let rowKeyFieldR =
            match tryField fields "rowKeyField" with
            | None -> Ok None
            | Some fJ -> requireString (path + ".rowKeyField") fJ |> Result.map Some

        // Phase 393 — the static read-only mode. `staticRows` (optional, omitted for a
        // data-bound grid so existing fixtures stay byte-identical) carries the retired
        // `Table`'s `TextSource` header/row matrix; when present the renderer emits static
        // `<table>` markup from it. `decodeStaticRows` reads the `{headers, rows}` object.
        let staticRowsR: Result<(TextSource list * TextSource list list) option, DecodeError> =
            match tryField fields "staticRows" with
            | None -> Ok None
            | Some sJ -> decodeStaticRows (path + ".staticRows") sJ |> Result.map Some

        match columnsR, editableR, sourceR, onRowClickR, rowKeyFieldR, staticRowsR with
        | Ok columns, Ok editable, Ok source, Ok onRowClick, Ok rowKeyField, Ok staticRows ->
            Ok
                { Source = source
                  RowKey = rowKey
                  RowKeyField = rowKeyField
                  Columns = columns
                  OnRowClick = onRowClick
                  Editable = editable
                  StaticRows = staticRows }
        | Error e, _, _, _, _, _
        | _, Error e, _, _, _, _
        | _, _, Error e, _, _, _
        | _, _, _, Error e, _, _
        | _, _, _, _, Error e, _
        | _, _, _, _, _, Error e -> Error e

let private decodeChartSpec (path: string) (j: Json) : Result<ChartSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let kindR =
            requireField path fields "kind" "ChartKind"
            |> Result.bind (decodeChartKind (path + ".kind"))

        let sourceR =
            requireFieldAliased path fields "source" [ "data" ] "Binding<obj seq> source"
            |> Result.bind (decodeBindingObjSeq (path + ".source"))

        let xFieldR =
            requireField path fields "xField" "xField string"
            |> Result.bind (requireString (path + ".xField"))

        let yFieldsR =
            match requireField path fields "yFields" "yFields string list" with
            | Error e -> Error e
            | Ok v ->
                match requireArray (path + ".yFields") v with
                | Error e -> Error e
                | Ok xs -> traverseIndexed (fun i item -> requireString (sprintf "%s.yFields[%d]" path i) item) xs

        let titleR =
            match tryField fields "title" with
            | None -> Ok None
            | Some v -> decodeTextSource (path + ".title") v |> Result.map Some

        let onPointClickR: Result<(obj -> Action<obj>) option, DecodeError> =
            match tryField fields "onPointClick" with
            | None -> Ok None
            | Some _ -> Ok(Some(fun _ -> Action.Chain []))

        // `stacked` (Phase 126): now carried on the wire. Absent (legacy wire
        // predating the field) decodes to the default `false`.
        let stackedR: Result<bool, DecodeError> =
            match tryField fields "stacked" with
            | None -> Ok false
            | Some v -> requireBool (path + ".stacked") v

        match kindR, sourceR, xFieldR, yFieldsR, titleR, onPointClickR, stackedR with
        | Ok kind, Ok source, Ok xField, Ok yFields, Ok title, Ok onPointClick, Ok stacked ->
            Ok
                { Source = source
                  Kind = kind
                  XField = xField
                  YFields = yFields
                  Title = title
                  OnPointClick = onPointClick
                  Stacked = stacked }
        | Error e, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _
        | _, _, Error e, _, _, _, _
        | _, _, _, Error e, _, _, _
        | _, _, _, _, Error e, _, _
        | _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, Error e -> Error e

let private decodeMapSpec (path: string) (j: Json) : Result<MapSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let centreLatR =
            requireField path fields "centreLatitude" "centre latitude float"
            |> Result.bind (requireFloat (path + ".centreLatitude"))

        let centreLonR =
            requireField path fields "centreLongitude" "centre longitude float"
            |> Result.bind (requireFloat (path + ".centreLongitude"))

        let sourceR =
            requireFieldAliased path fields "source" [ "data"; "markers" ] "Binding<MapMarker seq> source"
            |> Result.bind (decodeBindingMarkerSeq (path + ".source"))

        let zoomR =
            requireField path fields "zoom" "zoom integer"
            |> Result.bind (requireInt (path + ".zoom"))

        let onMarkerClickR: Result<(MapMarker -> Action<obj>) option, DecodeError> =
            match tryField fields "onMarkerClick" with
            | None -> Ok None
            | Some _ -> Ok(Some(fun _ -> Action.Chain []))

        match centreLatR, centreLonR, sourceR, zoomR, onMarkerClickR with
        | Ok lat, Ok lon, Ok source, Ok zoom, Ok onMarkerClick ->
            Ok
                { Source = source
                  CentreLatitude = lat
                  CentreLongitude = lon
                  Zoom = zoom
                  OnMarkerClick = onMarkerClick }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

let private decodeVisKind (path: string) (j: Json) : Result<VisKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "DataGrid" -> decodeGridSpec path j |> Result.map VisKind.DataGrid
        | Ok "Chart" -> decodeChartSpec path j |> Result.map VisKind.Chart
        | Ok "Map" -> decodeMapSpec path j |> Result.map VisKind.Map
        | Ok s -> unknownDuCase path s "DataGrid | Chart | Table | Map"

// ─── Parameterised-fragment hole / effect / scalar decoders (Phase 180) ─────
//
// Mirror the CanonicalJson encoders. None reference `Node`, so they sit ahead of
// the recursive node group; the slot-argument subtree (the only `Node`-bearing
// arg shape) is decoded inline in the `FragmentRef` arm via `decodeNodeAst`.

let private decodeHoleValueSpace (path: string) (j: Json) : Result<HoleValueSpace, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            match disc with
            | "IntRange" ->
                match
                    requireField path fields "min" "IntRange min"
                    |> Result.bind (requireInt (path + ".min")),
                    requireField path fields "max" "IntRange max"
                    |> Result.bind (requireInt (path + ".max"))
                with
                | Ok lo, Ok hi -> Ok(HoleValueSpace.IntRange(lo, hi))
                | Error e, _
                | _, Error e -> Error e
            | "FloatRange" ->
                match
                    requireField path fields "min" "FloatRange min"
                    |> Result.bind (requireFloat (path + ".min")),
                    requireField path fields "max" "FloatRange max"
                    |> Result.bind (requireFloat (path + ".max"))
                with
                | Ok lo, Ok hi -> Ok(HoleValueSpace.FloatRange(lo, hi))
                | Error e, _
                | _, Error e -> Error e
            | "StringLen" ->
                match
                    requireField path fields "minLen" "StringLen minLen"
                    |> Result.bind (requireInt (path + ".minLen")),
                    requireField path fields "maxLen" "StringLen maxLen"
                    |> Result.bind (requireInt (path + ".maxLen"))
                with
                | Ok lo, Ok hi -> Ok(HoleValueSpace.StringLen(lo, hi))
                | Error e, _
                | _, Error e -> Error e
            | "Enum" ->
                requireField path fields "choices" "Enum choices"
                |> Result.bind (requireArray (path + ".choices"))
                |> Result.bind (traverse (requireString (path + ".choices[]")))
                |> Result.map HoleValueSpace.Enum
            | "AnyString" -> Ok HoleValueSpace.AnyString
            | s -> unknownDuCase path s "IntRange | FloatRange | StringLen | Enum | AnyString"

/// A self-describing boxed scalar (a value default or value argument). The
/// `$type` tag pins the CLR shape so no hole value-space is consulted.
let private decodeScalar (path: string) (j: Json) : Result<obj, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            match disc with
            | "Int" ->
                requireField path fields "value" "Int value"
                |> Result.bind (requireInt (path + ".value"))
                |> Result.map box
            | "Float" ->
                requireField path fields "value" "Float value"
                |> Result.bind (requireFloat (path + ".value"))
                |> Result.map box
            | "Bool" ->
                requireField path fields "value" "Bool value"
                |> Result.bind (requireBool (path + ".value"))
                |> Result.map box
            | "Str" ->
                requireField path fields "value" "Str value"
                |> Result.bind (requireString (path + ".value"))
                |> Result.map box
            | s -> unknownDuCase path s "Int | Float | Bool | Str"

let private decodeHoleDecl (path: string) (j: Json) : Result<HoleDecl, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            match disc with
            | "Value" ->
                let nameR =
                    requireField path fields "name" "Value hole name"
                    |> Result.bind (requireString (path + ".name"))

                let spaceR =
                    requireField path fields "space" "Value hole space"
                    |> Result.bind (decodeHoleValueSpace (path + ".space"))

                let defR =
                    match tryField fields "default" with
                    | None -> Ok None
                    | Some d -> decodeScalar (path + ".default") d |> Result.map Some

                match nameR, spaceR, defR with
                | Ok name, Ok space, Ok def -> Ok(HoleDecl.Value(name, space, def))
                | Error e, _, _
                | _, Error e, _
                | _, _, Error e -> Error e
            | "Slot" ->
                let nameR =
                    requireField path fields "name" "Slot hole name"
                    |> Result.bind (requireString (path + ".name"))

                let kcR =
                    match tryField fields "kindConstraint" with
                    | None -> Ok None
                    | Some k -> requireString (path + ".kindConstraint") k |> Result.map Some

                match nameR, kcR with
                | Ok name, Ok kc -> Ok(HoleDecl.Slot(name, kc))
                | Error e, _
                | _, Error e -> Error e
            | "Repeat" ->
                let nameR =
                    requireField path fields "name" "Repeat hole name"
                    |> Result.bind (requireString (path + ".name"))

                let spaceR =
                    requireField path fields "countSpace" "Repeat hole countSpace"
                    |> Result.bind (decodeHoleValueSpace (path + ".countSpace"))

                match nameR, spaceR with
                | Ok name, Ok space -> Ok(HoleDecl.Repeat(name, space))
                | Error e, _
                | _, Error e -> Error e
            | s -> unknownDuCase path s "Value | Slot | Repeat"

let private decodeEffectClass (path: string) (j: Json) : Result<EffectClass, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let hostR =
            requireField path fields "hostEffect" "EffectClass hostEffect"
            |> Result.bind (requireString (path + ".hostEffect"))
            |> Result.bind (fun s ->
                match s with
                | "Pure" -> Ok HostEffect.Pure
                | "ReadsHost" -> Ok HostEffect.ReadsHost
                | "WritesHost" -> Ok HostEffect.WritesHost
                | other -> unknownDuCase (path + ".hostEffect") other "Pure | ReadsHost | WritesHost")

        let detR =
            requireField path fields "determinism" "EffectClass determinism"
            |> Result.bind (requireString (path + ".determinism"))
            |> Result.bind (fun s ->
                match s with
                | "Deterministic" -> Ok DeterminismSource.Deterministic
                | "Clock" -> Ok DeterminismSource.Clock
                | "Random" -> Ok DeterminismSource.Random
                | "Network" -> Ok DeterminismSource.Network
                | other -> unknownDuCase (path + ".determinism") other "Deterministic | Clock | Random | Network")

        match hostR, detR with
        | Ok host, Ok det -> Ok { HostEffect = host; Determinism = det }
        | Error e, _
        | _, Error e -> Error e

// ─── Layout (mutually recursive with Node) ──────────────────────────────

let rec private decodeChildren (path: string) (fields: Map<string, Json>) : Result<Node<obj> list, DecodeError> =
    match requireField path fields "children" "children Node list" with
    | Error e -> Error e
    | Ok v ->
        match requireArray (path + ".children") v with
        | Error e -> Error e
        | Ok xs -> traverseIndexed (fun i item -> decodeNodeAst (sprintf "%s.children[%d]" path i) item) xs

and private decodeLayoutKind (path: string) (j: Json) : Result<LayoutKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok disc ->
            // Flat wire (WIRE_FORMAT §3.2): the layout spec fields are hoisted
            // directly into the kind object, so we read them from the kind
            // object's own `fields` at `path` (the `$type` key is ignored).
            let specPath = path

            let getSpecFields () = Ok fields

            match disc with
            | "Box" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let headingR =
                        match optFieldAliased specFields "heading" [ "title" ] with
                        | None -> Ok Option.None
                        | Some v -> decodeTextSource (specPath + ".heading") v |> Result.map Some

                    let roleR =
                        requireField specPath specFields "role" "role string"
                        |> Result.bind (requireString (specPath + ".role"))
                        |> Result.bind (fun s ->
                            match s with
                            | "Group" -> Ok BoxRole.Group
                            | "Card" -> Ok BoxRole.Card
                            | "Dashboard" -> Ok BoxRole.Dashboard
                            | "Separator" -> Ok BoxRole.Separator
                            | other -> unknownDuCase (specPath + ".role") other "Group | Card | Dashboard | Separator")

                    let layoutR =
                        requireField specPath specFields "layout" "layout object"
                        |> Result.bind (fun lv -> requireObject (specPath + ".layout") lv)
                        |> Result.bind (fun lfields ->
                            let lpath = specPath + ".layout"

                            requireDiscriminator lpath lfields
                            |> Result.bind (fun ldisc ->
                                match ldisc with
                                | "Flex" ->
                                    let dirR =
                                        requireField lpath lfields "direction" "Orientation"
                                        |> Result.bind (decodeOrientation (lpath + ".direction"))

                                    let wrapR =
                                        requireField lpath lfields "wrap" "wrap bool"
                                        |> Result.bind (requireBool (lpath + ".wrap"))

                                    let gapR =
                                        match tryField lfields "gap" with
                                        | None -> Ok Option.None
                                        | Some v -> requireInt (lpath + ".gap") v |> Result.map Some

                                    match dirR, wrapR, gapR with
                                    | Ok d, Ok w, Ok g -> Ok(BoxLayout.Flex { Direction = d; Wrap = w; Gap = g })
                                    | Error e, _, _
                                    | _, Error e, _
                                    | _, _, Error e -> Error e
                                | "Grid" when
                                    tryField lfields "cols" |> Option.isNone
                                    && tryField lfields "columns" |> Option.isNone
                                    && tryField lfields "templateColumns" |> Option.isNone
                                    ->
                                    // Lenient AI-ingest (WIRE_FORMAT.md §3.6, 2026-07-17): a
                                    // Grid with NO column spec is the CSS auto-grid prior
                                    // (35 launch-eval cells, 8 tasks, every provider) — the
                                    // language already has the concept the author meant:
                                    // `Auto` (responsive auto-tile). Accept-and-canonicalise;
                                    // re-encode emits {"$type":"Auto"}.
                                    Ok BoxLayout.Auto
                                | "Grid" ->
                                    let colsR =
                                        // `TemplateColumns` present ⇒ `Cols` is documented-ignored,
                                        // so an absent cols defaults to 1 rather than MISSING_FIELD
                                        // (the 0.1.6 pilot's residual Grid failure shape).
                                        match tryField lfields "cols", tryField lfields "columns" with
                                        | None, None when (tryField lfields "templateColumns").IsSome -> Ok 1
                                        | _ ->
                                            requireFieldAliased lpath lfields "cols" [ "columns" ] "cols integer"
                                            |> Result.bind (requireInt (lpath + ".cols"))

                                    let tcR =
                                        match tryField lfields "templateColumns" with
                                        | None -> Ok Option.None
                                        | Some v -> requireString (lpath + ".templateColumns") v |> Result.map Some

                                    let gapR =
                                        match tryField lfields "gap" with
                                        | None -> Ok Option.None
                                        | Some v -> requireInt (lpath + ".gap") v |> Result.map Some

                                    match colsR, tcR, gapR with
                                    | Ok c, Ok tc, Ok g ->
                                        Ok(
                                            BoxLayout.Grid
                                                { Cols = c
                                                  TemplateColumns = tc
                                                  Gap = g }
                                        )
                                    | Error e, _, _
                                    | _, Error e, _
                                    | _, _, Error e -> Error e
                                | "Auto" -> Ok BoxLayout.Auto
                                | other -> unknownDuCase lpath other "Flex | Grid | Auto"))

                    match childrenR, headingR, roleR, layoutR with
                    | Ok children, Ok heading, Ok role, Ok layout ->
                        Ok(
                            LayoutKind.Box
                                { Layout = layout
                                  Role = role
                                  Heading = heading
                                  Children = children }
                        )
                    | Error e, _, _, _
                    | _, Error e, _, _
                    | _, _, Error e, _
                    | _, _, _, Error e -> Error e
            | "SplitPanel" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let weightR =
                        requireField specPath specFields "weight" "weight float"
                        |> Result.bind (requireFloat (specPath + ".weight"))

                    match childrenR, weightR with
                    | Ok children, Ok weight -> Ok(LayoutKind.SplitPanel { Weight = weight; Children = children })
                    | Error e, _
                    | _, Error e -> Error e
            | "Tabs" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let orientationR =
                        // 0.2.0 — omitted-when-Horizontal on both boundaries.
                        match tryField specFields "orientation" with
                        | None -> Ok Horizontal
                        | Some v -> decodeOrientation (specPath + ".orientation") v

                    // Additive optional decoders.
                    // `activeIndex` (Phase 126) now round-trips: decode the
                    // integer-indexed binding (defaults to `Binding.Static 0`
                    // for legacy wire predating the field). `onSelect` /
                    // `onSelectTag` (Phase 426): a present `"<closure>"`
                    // sentinel decodes to the inert placeholder `Some`; an
                    // absent key decodes to `None` — the shape that arms the
                    // renderer's ActiveIndex/ActiveTag write-back default.
                    let decodeTabHeaderEntry (path: string) (j: Json) =
                        match requireObject path j with
                        | Error e -> Error e
                        | Ok hFields ->
                            match requireField path hFields "label" "TabHeader.label TextSource" with
                            | Error e -> Error e
                            | Ok labelJ ->
                                match decodeTextSource (path + ".label") labelJ with
                                | Error e -> Error e
                                | Ok label ->
                                    let iconR =
                                        match tryField hFields "icon" with
                                        | None -> Ok Option.None
                                        | Some v -> decodeIconSource (path + ".icon") v |> Result.map Some

                                    let disabledR =
                                        match tryField hFields "disabled" with
                                        | None -> Ok Option.None
                                        | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

                                    match iconR, disabledR with
                                    | Ok icon, Ok disabled ->
                                        Ok
                                            { Label = label
                                              Icon = icon
                                              Disabled = disabled }
                                    | Error e, _
                                    | _, Error e -> Error e

                    let tabHeadersR =
                        match tryField specFields "tabHeaders" with
                        | None -> Ok Option.None
                        | Some v ->
                            requireArray (specPath + ".tabHeaders") v
                            |> Result.bind (fun items ->
                                items
                                |> List.mapi (fun i it -> i, it)
                                |> traverse (fun (i, it) ->
                                    decodeTabHeaderEntry (sprintf "%s.tabHeaders[%d]" specPath i) it))
                            |> Result.map Some

                    let tabTagsR =
                        match tryField specFields "tabTags" with
                        | None -> Ok Option.None
                        | Some v ->
                            requireArray (specPath + ".tabTags") v
                            |> Result.bind (fun items ->
                                items
                                |> List.mapi (fun i it -> i, it)
                                |> traverse (fun (i, it) -> requireString (sprintf "%s.tabTags[%d]" specPath i) it))
                            |> Result.map Some

                    let activeTagR =
                        match tryField specFields "activeTag" with
                        | None -> Ok Option.None
                        | Some v -> decodeBindingString (specPath + ".activeTag") v |> Result.map Some

                    let activeIndexR =
                        match tryField specFields "activeIndex" with
                        | None -> Ok(Binding.Static 0)
                        | Some v -> decodeBindingInt (specPath + ".activeIndex") v

                    match childrenR, orientationR, tabHeadersR, tabTagsR, activeTagR, activeIndexR with
                    | Ok children, Ok orientation, Ok tabHeaders, Ok tabTags, Ok activeTag, Ok activeIndex ->
                        Ok(
                            LayoutKind.Tabs
                                { Orientation = orientation
                                  Children = children
                                  ActiveIndex = activeIndex
                                  OnSelect =
                                    (match tryField specFields "onSelect" with
                                     | Some _ -> Some(fun _ -> Action.Chain [])
                                     | None -> Option.None)
                                  TabHeaders = tabHeaders
                                  TabTags = tabTags
                                  ActiveTag = activeTag
                                  OnSelectTag =
                                    (match tryField specFields "onSelectTag" with
                                     | Some _ -> Some(fun _ -> Action.Chain [])
                                     | None -> Option.None) }
                        )
                    | Error e, _, _, _, _, _
                    | _, Error e, _, _, _, _
                    | _, _, Error e, _, _, _
                    | _, _, _, Error e, _, _
                    | _, _, _, _, Error e, _
                    | _, _, _, _, _, Error e -> Error e
            | "Stepper" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let activeR =
                        requireField specPath specFields "activeStep" "Binding<int> activeStep"
                        |> Result.bind (decodeBindingInt (specPath + ".activeStep"))

                    // `onSelect` is a closure → the sentinel is consumed and
                    // reconstructs a no-op `Action` (re-encodes to the same
                    // sentinel; behaviour can't round-trip) — mirrors Tabs.
                    match childrenR, activeR with
                    | Ok children, Ok active ->
                        Ok(
                            LayoutKind.Stepper
                                { ActiveStep = active
                                  Children = children
                                  OnSelect = (fun _ -> Action.Chain []) }
                        )
                    | Error e, _
                    | _, Error e -> Error e
            | "SummaryList" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let headingR =
                        match optFieldAliased specFields "heading" [ "title" ] with
                        | None -> Ok None
                        | Some v -> decodeTextSource (specPath + ".heading") v |> Result.map Some

                    match childrenR, headingR with
                    | Ok children, Ok heading ->
                        Ok(
                            LayoutKind.SummaryList
                                { Heading = heading
                                  Children = children }
                        )
                    | Error e, _
                    | _, Error e -> Error e
            | "Disclosure" ->
                // Additive typed accordion. `heading`
                // is required (TextSource); `open` is optional (decodes via
                // `decodeBindingBool`, defaulting to `Binding.Static false`
                // when absent — mirrors the `Defaults.disclosure` shape);
                // `defaultOpen` is an optional bool (defaults to `false`);
                // `onToggle` (Phase 426): a present `"<closure>"` sentinel
                // decodes to the inert placeholder `Some`; an absent key
                // decodes to `None`, arming the `Open` write-back default.
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let headingR =
                        requireFieldAliased specPath specFields "heading" [ "title" ] "TextSource heading"
                        |> Result.bind (decodeTextSource (specPath + ".heading"))

                    let openR =
                        match tryField specFields "open" with
                        | None -> Ok(Binding.Static false)
                        | Some v -> decodeBindingBool (specPath + ".open") v

                    let defaultOpenR =
                        match tryField specFields "defaultOpen" with
                        | None -> Ok false
                        | Some v -> requireBool (specPath + ".defaultOpen") v

                    match childrenR, headingR, openR, defaultOpenR with
                    | Ok children, Ok heading, Ok openB, Ok defOpen ->
                        Ok(
                            LayoutKind.Disclosure
                                { Heading = heading
                                  Open = openB
                                  OnToggle =
                                    (match tryField specFields "onToggle" with
                                     | Some _ -> Some(fun _ -> Action.Chain [])
                                     | None -> Option.None)
                                  Children = children
                                  DefaultOpen = defOpen }
                        )
                    | Error e, _, _, _
                    | _, Error e, _, _
                    | _, _, Error e, _
                    | _, _, _, Error e -> Error e
            | "Modal" ->
                // Phase 289 — overlay dialog. `open` is required (visibility
                // binding); `dismissable` required bool; `onDismiss` is a
                // wire-survivable Action (decoded via decodeAction, like
                // Form.OnSubmit) — optional since Phase 426: absent ⇒ `None`,
                // arming the `Open` write-back default; `heading` optional.
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let openR =
                        requireField specPath specFields "open" "Binding<bool> open"
                        |> Result.bind (decodeBindingBool (specPath + ".open"))

                    let dismissableR =
                        requireField specPath specFields "dismissable" "dismissable bool"
                        |> Result.bind (requireBool (specPath + ".dismissable"))

                    let onDismissR =
                        match tryField specFields "onDismiss" with
                        | None -> Ok Option.None
                        | Some v -> decodeAction (specPath + ".onDismiss") v |> Result.map Some

                    let headingR =
                        match optFieldAliased specFields "heading" [ "title" ] with
                        | None -> Ok None
                        | Some v -> decodeTextSource (specPath + ".heading") v |> Result.map Some

                    match childrenR, openR, dismissableR, onDismissR, headingR with
                    | Ok children, Ok openB, Ok dismissable, Ok onDismiss, Ok heading ->
                        Ok(
                            LayoutKind.Modal
                                { Open = openB
                                  Heading = heading
                                  Dismissable = dismissable
                                  Children = children
                                  OnDismiss = onDismiss }
                        )
                    | Error e, _, _, _, _
                    | _, Error e, _, _, _
                    | _, _, Error e, _, _
                    | _, _, _, Error e, _
                    | _, _, _, _, Error e -> Error e
            | "ScrollArea" ->
                // Phase 289 — overflow/scroll container. `orientation` required
                // (scroll axis); optional maxHeight/maxWidth (pixels) decode to
                // None when absent.
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren specPath specFields

                    let orientationR =
                        requireField specPath specFields "orientation" "ScrollOrientation"
                        |> Result.bind (decodeScrollOrientation (specPath + ".orientation"))

                    let maxHeightR =
                        match tryField specFields "maxHeight" with
                        | None -> Ok Option.None
                        | Some v -> requireInt (specPath + ".maxHeight") v |> Result.map Some

                    let maxWidthR =
                        match tryField specFields "maxWidth" with
                        | None -> Ok Option.None
                        | Some v -> requireInt (specPath + ".maxWidth") v |> Result.map Some

                    match childrenR, orientationR, maxHeightR, maxWidthR with
                    | Ok children, Ok orientation, Ok maxHeight, Ok maxWidth ->
                        Ok(
                            LayoutKind.ScrollArea
                                { Orientation = orientation
                                  Children = children
                                  MaxHeight = maxHeight
                                  MaxWidth = maxWidth }
                        )
                    | Error e, _, _, _
                    | _, Error e, _, _
                    | _, _, Error e, _
                    | _, _, _, Error e -> Error e
            | s ->
                unknownDuCase path s "Box | SplitPanel | Tabs | Stepper | SummaryList | Disclosure | Modal | ScrollArea"

and private decodeNodeKind (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        // The four behavioural categories are flat on the wire (WIRE_FORMAT
        // §3.2): the `kind` object carries the primitive discriminator
        // directly, so we route each primitive to its inner decoder and
        // recover the category here. These name-sets MUST stay in sync with
        // the four inner decoders, the encoder, and SchemaGen — the §11
        // forward-coupling surface. An unrecognised discriminator falls
        // through to WRONG_NODE_KIND below.
        | Ok("Box" | "SplitPanel" | "Tabs" | "Stepper" | "SummaryList" | "Disclosure" | "Modal" | "ScrollArea") ->
            decodeLayoutKind path j |> Result.map NodeKind.Layout
        | Ok("Heading" | "Markdown" | "Metric" | "Badge" | "Link" | "Image" | "List" | "Toast" | "CodeBlock" | "Math" | "Drawing" | "Sparkline" | "Callout" | "Progress" | "Skeleton" | "LabelValueRow" | "Fact") ->
            decodeDisplayKind path j |> Result.map NodeKind.Display
        | Ok("Form" | "Filters" | "Button" | "FileUpload" | "Select") ->
            decodeInputKind path j |> Result.map NodeKind.Input
        | Ok("DataGrid" | "Chart" | "Map") -> decodeVisKind path j |> Result.map NodeKind.Visualisation
        | Ok "Custom" ->
            let moduleIdR =
                requireField path fields "moduleId" "Custom moduleId string"
                |> Result.bind (requireString (path + ".moduleId"))

            let componentIdR =
                requireField path fields "componentId" "Custom componentId string"
                |> Result.bind (requireString (path + ".componentId"))

            let propsR =
                requireField path fields "props" "Custom props JSON object"
                |> Result.bind (decodeJValMap (path + ".props"))

            // Optional contentHash + exposedNodeIds
            // — absent = None / [] (preserves the prior round-trip).
            let contentHashR =
                match tryField fields "contentHash" with
                | None -> Ok None
                | Some j ->
                    match requireObject (path + ".contentHash") j with
                    | Error e -> Error e
                    | Ok hashFields ->
                        let algR =
                            requireField (path + ".contentHash") hashFields "algorithm" "ContentHash algorithm string"
                            |> Result.bind (requireString (path + ".contentHash.algorithm"))

                        let hashR =
                            requireField (path + ".contentHash") hashFields "hash" "ContentHash hash hex string"
                            |> Result.bind (requireString (path + ".contentHash.hash"))

                        let strictR =
                            requireField
                                (path + ".contentHash")
                                hashFields
                                "strictness"
                                "ContentHash strictness 'StrictReplay' | 'AdvisoryWarning' | 'Enforced'"
                            |> Result.bind (requireString (path + ".contentHash.strictness"))
                            |> Result.bind (fun s ->
                                match s with
                                | "StrictReplay" -> Ok HashStrictness.StrictReplay
                                | "AdvisoryWarning" -> Ok HashStrictness.AdvisoryWarning
                                | "Enforced" -> Ok HashStrictness.Enforced
                                | other ->
                                    unknownDuCase
                                        (path + ".contentHash.strictness")
                                        other
                                        "StrictReplay | AdvisoryWarning | Enforced")

                        match algR, hashR, strictR with
                        | Ok alg, Ok h, Ok strict ->
                            Ok(
                                Some
                                    { Algorithm = alg
                                      Hash = h
                                      Strictness = strict }
                            )
                        | Error e, _, _
                        | _, Error e, _
                        | _, _, Error e -> Error e

            let exposedNodeIdsR =
                match tryField fields "exposedNodeIds" with
                | None -> Ok []
                | Some j ->
                    requireArray (path + ".exposedNodeIds") j
                    |> Result.bind (fun items ->
                        items
                        |> List.mapi (fun i item ->
                            requireString (sprintf "%s.exposedNodeIds[%d]" path i) item |> Result.map NodeId)
                        |> List.fold
                            (fun acc r ->
                                match acc, r with
                                | Ok xs, Ok v -> Ok(xs @ [ v ])
                                | Error e, _ -> Error e
                                | _, Error e -> Error e)
                            (Ok []))

            match moduleIdR, componentIdR, propsR, contentHashR, exposedNodeIdsR with
            | Ok moduleId, Ok componentId, Ok props, Ok hash, Ok exposedIds ->
                Ok(NodeKind.Custom(moduleId, componentId, props, hash, exposedIds))
            | Error e, _, _, _, _
            | _, Error e, _, _, _
            | _, _, Error e, _, _
            | _, _, _, Error e, _
            | _, _, _, _, Error e -> Error e
        | Ok "ErrorBoundary" ->
            // Mirror the encoder's `child` +
            // `fallback` field pair back into the typed
            // `ErrorBoundarySpec<obj>`. Both fields are required (the
            // boundary is meaningless without either half).
            let childR =
                requireField path fields "child" "ErrorBoundary child Node"
                |> Result.bind (decodeNodeAst (path + ".child"))

            let fallbackR =
                requireField path fields "fallback" "ErrorBoundary fallback Node"
                |> Result.bind (decodeNodeAst (path + ".fallback"))

            match childR, fallbackR with
            | Ok child, Ok fallback -> Ok(NodeKind.ErrorBoundary { Child = child; Fallback = fallback })
            | Error e, _
            | _, Error e -> Error e
        | Ok "Switch" ->
            // Mirror the encoder (Phase 392): `stateKey` (string), `cases`
            // (array of `{child,match}` objects), `default` (Node). All three
            // required. Duplicate `match` values are NOT a decode error —
            // first-match-wins keeps decode structural; the validator
            // (FUARAN082) flags them, mirroring FragmentDecl name-collision
            // handling (§WIRE_FORMAT decoder is structural, six codes only).
            let stateKeyR =
                requireField path fields "stateKey" "Switch stateKey string"
                |> Result.bind (requireString (path + ".stateKey"))

            let casesR =
                match requireField path fields "cases" "Switch cases array" with
                | Error e -> Error e
                | Ok v ->
                    match requireArray (path + ".cases") v with
                    | Error e -> Error e
                    | Ok xs ->
                        xs
                        |> traverseIndexed (fun i item ->
                            let casePath = sprintf "%s.cases[%d]" path i

                            match requireObject casePath item with
                            | Error e -> Error e
                            | Ok caseFields ->
                                let matchR =
                                    requireField casePath caseFields "match" "Switch case match string"
                                    |> Result.bind (requireString (casePath + ".match"))

                                let childR =
                                    requireField casePath caseFields "child" "Switch case child Node"
                                    |> Result.bind (decodeNodeAst (casePath + ".child"))

                                match matchR, childR with
                                | Ok m, Ok child -> Ok(m, child)
                                | Error e, _
                                | _, Error e -> Error e)

            let defaultR =
                requireField path fields "default" "Switch default Node"
                |> Result.bind (decodeNodeAst (path + ".default"))

            match stateKeyR, casesR, defaultR with
            | Ok stateKey, Ok cases, Ok defaultNode ->
                Ok(
                    NodeKind.Switch
                        { StateKey = stateKey
                          Cases = cases
                          Default = defaultNode }
                )
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "FragmentDecl" ->
            // Mirror the encoder's `name` +
            // `body` field pair. Both required — a decl without a name
            // is unresolvable and a decl without a body has nothing to
            // expand. The body decodes via the recursive `decodeNodeAst`
            // so nested fragment declarations / refs round-trip cleanly.
            let nameR =
                requireField path fields "name" "FragmentDecl name string"
                |> Result.bind (requireString (path + ".name"))
                |> Result.map FragmentId

            let bodyR =
                requireField path fields "body" "FragmentDecl body Node"
                |> Result.bind (decodeNodeAst (path + ".body"))

            // Phase 180 — `holes` + `effect` are additive; absent ⇒ degenerate
            // fixed-body (zero holes, pure-deterministic).
            let holesR =
                match tryField fields "holes" with
                | None -> Ok []
                | Some h ->
                    requireArray (path + ".holes") h
                    |> Result.bind (traverse (decodeHoleDecl (path + ".holes[]")))

            let effectR =
                match tryField fields "effect" with
                | None -> Ok EffectClass.pureDeterministic
                | Some e -> decodeEffectClass (path + ".effect") e

            match nameR, bodyR, holesR, effectR with
            | Ok name, Ok body, Ok holes, Ok effect ->
                Ok(
                    NodeKind.FragmentDecl
                        { Name = name
                          Body = body
                          Holes = holes
                          Effect = effect }
                )
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e
        | Ok "FragmentRef" ->
            // Mirror the encoder's `name` + optional `args` shape. The referenced
            // fragment's body lives at its decl site, not duplicated here; `args`
            // (Phase 180) is additive — absent ⇒ degenerate zero-arg ref.
            let nameR =
                requireField path fields "name" "FragmentRef name string"
                |> Result.bind (requireString (path + ".name"))
                |> Result.map FragmentId

            let argR (argPath: string) (j: Json) : Result<FragmentArg<obj>, DecodeError> =
                match requireObject argPath j with
                | Error e -> Error e
                | Ok argFields ->
                    match requireDiscriminator argPath argFields with
                    | Error e -> Error e
                    | Ok "SlotArg" ->
                        requireField argPath argFields "tree" "SlotArg tree Node"
                        |> Result.bind (decodeNodeAst (argPath + ".tree"))
                        |> Result.map FragmentArg.Slot
                    | Ok _ ->
                        // Int | Float | Bool | Str — a value argument.
                        decodeScalar argPath j |> Result.map FragmentArg.Value

            let argsR =
                match tryField fields "args" with
                | None -> Ok Map.empty
                | Some a ->
                    match requireObject (path + ".args") a with
                    | Error e -> Error e
                    | Ok argFields ->
                        argFields
                        |> Map.toList
                        |> traverse (fun (k, v) ->
                            argR (path + ".args." + k) v |> Result.map (fun decoded -> k, decoded))
                        |> Result.map Map.ofList

            match nameR, argsR with
            | Ok name, Ok args -> Ok(NodeKind.FragmentRef { Name = name; Args = args })
            | Error e, _
            | _, Error e -> Error e
        | Ok "Mount" ->
            // Isolation/embedding boundary (Phase 265, §4o). Mirror the encoder:
            // required `scopeId` + `channel` + `capabilities`; optional `inputs`
            // (additive, absent ⇒ empty), reusing the FragmentArg scalar/slot
            // decode. `onBubble` is a closure sentinel on the wire and decodes to
            // the canonical no-op action (`Action.Chain []`). A malformed Mount
            // surfaces a structured DecodeError, never a throw (default-deny).
            let scopeIdR =
                requireField path fields "scopeId" "Mount scopeId string"
                |> Result.bind (requireString (path + ".scopeId"))

            let channelR =
                match requireField path fields "channel" "Mount channel object" with
                | Error e -> Error e
                | Ok chanJson ->
                    match requireObject (path + ".channel") chanJson with
                    | Error e -> Error e
                    | Ok chanFields ->
                        let directionR =
                            requireField (path + ".channel") chanFields "direction" "channel direction string"
                            |> Result.bind (requireString (path + ".channel.direction"))
                            |> Result.bind (fun s ->
                                match s with
                                | "OutOnly" -> Ok ChannelDirection.OutOnly
                                | "TwoWay" -> Ok ChannelDirection.TwoWay
                                | other ->
                                    err
                                        DecodeErrorCode.UNKNOWN_DU_CASE
                                        (path + ".channel.direction")
                                        (sprintf "unknown ChannelDirection '%s'" other)
                                        (Some "OutOnly | TwoWay"))

                        let messageShapeR =
                            match tryField chanFields "messageShape" with
                            | None -> Ok None
                            | Some v -> requireString (path + ".channel.messageShape") v |> Result.map Some

                        match directionR, messageShapeR with
                        | Ok direction, Ok messageShape ->
                            Ok
                                { Direction = direction
                                  MessageShape = messageShape }
                        | Error e, _
                        | _, Error e -> Error e

            let capabilitiesR =
                match requireField path fields "capabilities" "Mount capabilities array" with
                | Error e -> Error e
                | Ok capsJson ->
                    requireArray (path + ".capabilities") capsJson
                    |> Result.bind (
                        traverse (fun j -> requireString (path + ".capabilities[]") j |> Result.map CapabilityTag)
                    )

            let inputsR =
                match tryField fields "inputs" with
                | None -> Ok Map.empty
                | Some a ->
                    match requireObject (path + ".inputs") a with
                    | Error e -> Error e
                    | Ok argFields ->
                        argFields
                        |> Map.toList
                        |> traverse (fun (k, v) ->
                            let argPath = path + ".inputs." + k

                            (match requireObject argPath v with
                             | Error e -> Error e
                             | Ok af ->
                                 match requireDiscriminator argPath af with
                                 | Error e -> Error e
                                 | Ok "SlotArg" ->
                                     requireField argPath af "tree" "SlotArg tree Node"
                                     |> Result.bind (decodeNodeAst (argPath + ".tree"))
                                     |> Result.map FragmentArg.Slot
                                 | Ok _ -> decodeScalar argPath v |> Result.map FragmentArg.Value)
                            |> Result.map (fun decoded -> k, decoded))
                        |> Result.map Map.ofList

            match scopeIdR, channelR, capabilitiesR, inputsR with
            | Ok scopeId, Ok channel, Ok capabilities, Ok inputs ->
                Ok(
                    NodeKind.Mount
                        { ScopeId = scopeId
                          Inputs = inputs
                          Channel = channel
                          OnBubble = (fun _ -> Action.Chain [])
                          Capabilities = capabilities }
                )
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e
        | Ok s ->
            err
                DecodeErrorCode.WRONG_NODE_KIND
                (path + ".$type")
                (sprintf "unknown NodeKind discriminator '%s'" s)
                (Some
                    "a Layout primitive (Box | SplitPanel | Tabs | Stepper | SummaryList | Disclosure | Modal | ScrollArea), a Display primitive (Heading | Markdown | Metric | Badge | Sparkline | Drawing | Callout | Progress | Skeleton | LabelValueRow), an Input primitive (Form | Filters | Button | FileUpload | Select), a Visualisation primitive (DataGrid | Chart | Map), or Custom | ErrorBoundary | FragmentDecl | FragmentRef | Mount")

and private decodeAccessibility (path: string) (j: Json) : Result<Accessibility, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            match tryField fields "label" with
            | None -> Ok None
            | Some v -> decodeBindingString (path + ".label") v |> Result.map Some

        let labelledByR =
            match tryField fields "labelledBy" with
            | None -> Ok None
            | Some v -> requireString (path + ".labelledBy") v |> Result.map (fun s -> Some(NodeId s))

        let describedByR =
            match tryField fields "describedBy" with
            | None -> Ok None
            | Some v -> requireString (path + ".describedBy") v |> Result.map (fun s -> Some(NodeId s))

        let roleR =
            match tryField fields "role" with
            | None -> Ok None
            | Some v -> decodeAriaRole (path + ".role") v |> Result.map Some

        let liveR =
            match tryField fields "liveRegion" with
            | None -> Ok None
            | Some v -> decodeLiveRegion (path + ".liveRegion") v |> Result.map Some

        let hiddenR =
            match tryField fields "hidden" with
            | None -> Ok None
            | Some v -> decodeBindingBool (path + ".hidden") v |> Result.map Some

        match labelR, labelledByR, describedByR, roleR, liveR, hiddenR with
        | Ok label, Ok labelledBy, Ok describedBy, Ok role, Ok liveRegion, Ok hidden ->
            Ok
                { Label = label
                  LabelledBy = labelledBy
                  DescribedBy = describedBy
                  Role = role
                  LiveRegion = liveRegion
                  Hidden = hidden }
        | Error e, _, _, _, _, _
        | _, Error e, _, _, _, _
        | _, _, Error e, _, _, _
        | _, _, _, Error e, _, _
        | _, _, _, _, Error e, _
        | _, _, _, _, _, Error e -> Error e

and private decodeStateBehaviour (path: string) (j: Json) : Result<StateBehaviour<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let onLoadingR =
            match tryField fields "onLoading" with
            | None -> Ok None
            | Some v -> decodeNodeAst (path + ".onLoading") v |> Result.map Some

        let onEmptyR =
            match tryField fields "onEmpty" with
            | None -> Ok None
            | Some v -> decodeNodeAst (path + ".onEmpty") v |> Result.map Some

        // The encoder writes OnError as a `<closure>` sentinel string when
        // present (the rendering closure is opaque). Decode `<closure>`
        // back to Some (fun _ -> placeholderClosureNode); decode missing
        // to None.
        let onErrorR: Result<(ErrorPayload -> Node<obj>) option, DecodeError> =
            match tryField fields "onError" with
            | None -> Ok None
            | Some _ -> Ok(Some(fun _ -> placeholderClosureNode))

        match onLoadingR, onEmptyR, onErrorR with
        | Ok onLoading, Ok onEmpty, Ok onError ->
            Ok
                { OnLoading = onLoading
                  OnEmpty = onEmpty
                  OnError = onError }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

and private decodeSemanticStyle (path: string) (j: Json) : Result<SemanticStyle, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        // Phase 460 — `tone` / `weight` / `emphasis` join `role` / `voice` as
        // omitted-when-default on the wire; restore the identity default on
        // absence (`ToneVariant.Default` / `StyleWeight.Standard` /
        // `Emphasis.Normal`). Explicit values keep decoding (read-compat).
        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let weightR =
            match tryField fields "weight" with
            | None -> Ok StyleWeight.Standard
            | Some v -> decodeWeight (path + ".weight") v

        let emphasisR =
            match tryField fields "emphasis" with
            | None -> Ok Emphasis.Normal
            | Some v -> decodeEmphasis (path + ".emphasis") v

        // `role` / `voice` (Phase 147) are optional on the wire — omitted at
        // their defaults (`StyleRole.None` / `FontVoice.Default`); restore the
        // default on absence.
        let roleR =
            match tryField fields "role" with
            | None -> Ok StyleRole.None
            | Some v -> decodeStyleRole (path + ".role") v

        let voiceR =
            match tryField fields "voice" with
            | None -> Ok FontVoice.Default
            | Some v -> decodeFontVoice (path + ".voice") v

        match toneR, weightR, emphasisR, roleR, voiceR with
        | Ok tone, Ok weight, Ok emphasis, Ok role, Ok voice ->
            Ok
                { Tone = tone
                  Weight = weight
                  Emphasis = emphasis
                  Role = role
                  Voice = voice }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

and private decodeNodeAst (path: string) (j: Json) : Result<Node<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let idR =
            match requireField path fields "id" "Node id string" with
            | Error e -> Error e
            | Ok v ->
                match requireString (path + ".id") v with
                | Error e -> Error e
                | Ok s when s = "" ->
                    err DecodeErrorCode.EMPTY_NODE_ID (path + ".id") "Node id is empty" (Some "non-empty string")
                | Ok s -> Ok(NodeId s)

        let kindR =
            requireField path fields "kind" "NodeKind discriminator object"
            |> Result.bind (decodeNodeKind (path + ".kind"))

        // `state` and `style` are optional on the flat wire (omitted when empty
        // / all-default, WIRE_FORMAT §3.1) — restore the default on absence.
        let stateR =
            match tryField fields "state" with
            | None ->
                Ok(
                    { OnLoading = None
                      OnEmpty = None
                      OnError = None }
                    : StateBehaviour<obj>
                )
            | Some v -> decodeStateBehaviour (path + ".state") v

        let styleR =
            match tryField fields "style" with
            | None ->
                Ok
                    { Emphasis = Emphasis.Normal
                      Tone = ToneVariant.Default
                      Weight = StyleWeight.Standard
                      Role = StyleRole.None
                      Voice = FontVoice.Default }
            | Some v -> decodeSemanticStyle (path + ".style") v

        let accessibilityR =
            match tryField fields "accessibility" with
            | None -> Ok None
            | Some v -> decodeAccessibility (path + ".accessibility") v |> Result.map Some

        match idR, kindR, stateR, styleR, accessibilityR with
        | Ok id, Ok kind, Ok state, Ok style, Ok accessibility ->
            // Motion / ExtraAttributes are not emitted by the encoder
            // (see Types.fs lines 213-218 — ExtraAttributes is "the §4d
            // JSON wire shape omits it on emit"; Motion follows the same
            // convention). Decode to None.
            Ok
                { Id = id
                  Kind = kind
                  State = state
                  Style = style
                  Accessibility = accessibility
                  Motion = None
                  ExtraAttributes = None }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e

// ─── TreeOp decoder ─────────────────────────────────────────────────────

let rec private decodeTreeOpAst (path: string) (j: Json) : Result<TreeOp<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "EditNode" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let newKindR =
                requireField path fields "newKind" "NodeKind object"
                |> Result.bind (decodeNodeKind (path + ".newKind"))

            match targetR, newKindR with
            | Ok target, Ok newKind -> Ok(TreeOp.EditNode(target, newKind))
            | Error e, _
            | _, Error e -> Error e
        | Ok "UpdateProp" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let pathFieldR =
                requireField path fields "path" "dot-separated path string"
                |> Result.bind (requireString (path + ".path"))

            let valueR =
                requireField path fields "value" "JSON value payload"
                |> Result.bind (decodeJVal (path + ".value"))
                // Sentinel gate: a top-level `"<opaque>"` / `"<closure>"` value is
                // the canonical-encoder's marker for an in-process-only `Native`
                // payload that was never wire-representable — replaying it would
                // apply the SENTINEL TEXT as live data (the §16 bare-string rule
                // would happily read it as a `TextSource.Literal`), silently
                // corrupting the tree where the pre-lenient decoder failed loudly.
                // The sentinels are reserved wire vocabulary (WIRE_FORMAT §4);
                // reject by name so a logged Native op is a loud replay error, not
                // a corruption. Only the TOP-LEVEL value is gated — a nested
                // occurrence inside a structured JSON payload is user data.
                |> Result.bind (fun j ->
                    match j with
                    | JStr s when s = opaqueSentinel || s = closureSentinel ->
                        err
                            DecodeErrorCode.WRONG_TYPE
                            (path + ".value")
                            (sprintf
                                "'%s' is the reserved in-process-only sentinel, not a wire value — the op was logged from a Native payload that cannot replay"
                                s)
                            (Some "a wire-representable JSON value (the sentinels are reserved vocabulary)")
                    | _ -> Ok j)
                |> Result.map PropValue.Wire

            match targetR, pathFieldR, valueR with
            | Ok target, Ok pathField, Ok value -> Ok(TreeOp.UpdateProp(target, pathField, value))
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "ReplaceBinding" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let slotR =
                requireField path fields "slot" "slot name string"
                |> Result.bind (requireString (path + ".slot"))

            let bindingR =
                requireField path fields "binding" "Binding<obj> object"
                |> Result.bind (decodeBindingObj (path + ".binding"))

            match targetR, slotR, bindingR with
            | Ok target, Ok slot, Ok binding -> Ok(TreeOp.ReplaceBinding(target, slot, binding))
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "UpdateStyle" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let styleR =
                requireField path fields "style" "SemanticStyle object"
                |> Result.bind (decodeSemanticStyle (path + ".style"))

            match targetR, styleR with
            | Ok target, Ok style -> Ok(TreeOp.UpdateStyle(target, style))
            | Error e, _
            | _, Error e -> Error e
        | Ok "UpdateState" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let stateR =
                requireField path fields "state" "StateBehaviour object"
                |> Result.bind (decodeStateBehaviour (path + ".state"))

            match targetR, stateR with
            | Ok target, Ok state -> Ok(TreeOp.UpdateState(target, state))
            | Error e, _
            | _, Error e -> Error e
        | Ok "InsertChild" ->
            let parentR =
                requireField path fields "parentId" "parent NodeId"
                |> Result.bind (requireString (path + ".parentId"))
                |> Result.map NodeId

            let positionR =
                requireField path fields "position" "position integer"
                |> Result.bind (requireInt (path + ".position"))

            let childR =
                requireField path fields "child" "child Node object"
                |> Result.bind (decodeNodeAst (path + ".child"))

            match parentR, positionR, childR with
            | Ok parent, Ok position, Ok child -> Ok(TreeOp.InsertChild(parent, position, child))
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "RemoveNode" ->
            requireField path fields "target" "target NodeId"
            |> Result.bind (requireString (path + ".target"))
            |> Result.map (fun s -> TreeOp.RemoveNode(NodeId s))
        | Ok "MoveNode" ->
            let targetR =
                requireField path fields "target" "target NodeId"
                |> Result.bind (requireString (path + ".target"))
                |> Result.map NodeId

            let newParentR =
                requireField path fields "newParentId" "new parent NodeId"
                |> Result.bind (requireString (path + ".newParentId"))
                |> Result.map NodeId

            let newPositionR =
                requireField path fields "newPosition" "new position integer"
                |> Result.bind (requireInt (path + ".newPosition"))

            match targetR, newParentR, newPositionR with
            | Ok target, Ok newParent, Ok newPosition -> Ok(TreeOp.MoveNode(target, newParent, newPosition))
            | Error e, _, _
            | _, Error e, _
            | _, _, Error e -> Error e
        | Ok "ReorderChildren" ->
            let parentR =
                requireField path fields "parentId" "parent NodeId"
                |> Result.bind (requireString (path + ".parentId"))
                |> Result.map NodeId

            let newOrderR =
                match requireField path fields "newOrder" "NodeId list" with
                | Error e -> Error e
                | Ok v ->
                    match requireArray (path + ".newOrder") v with
                    | Error e -> Error e
                    | Ok xs ->
                        traverseIndexed
                            (fun i item -> requireString (sprintf "%s.newOrder[%d]" path i) item |> Result.map NodeId)
                            xs

            match parentR, newOrderR with
            | Ok parent, Ok newOrder -> Ok(TreeOp.ReorderChildren(parent, newOrder))
            | Error e, _
            | _, Error e -> Error e
        | Ok "ReplaceRoot" ->
            requireField path fields "node" "root Node object"
            |> Result.bind (decodeNodeAst (path + ".node"))
            |> Result.map TreeOp.ReplaceRoot
        | Ok "Batch" ->
            requireField path fields "ops" "Batch inner-op list"
            |> Result.bind (fun v -> requireArray (path + ".ops") v)
            |> Result.bind (fun xs ->
                traverseIndexed (fun i item -> decodeTreeOpAst (sprintf "%s.ops[%d]" path i) item) xs)
            |> Result.map TreeOp.Batch
        | Ok s ->
            unknownDuCase
                path
                s
                "EditNode | UpdateProp | ReplaceBinding | UpdateStyle | UpdateState | InsertChild | RemoveNode | MoveNode | ReorderChildren | ReplaceRoot | Batch"

// ─── Apply-engine structural coercion bridge ─────────────────────────────
//
// `decodeOp` for `UpdateProp` produces a `PropValue.Wire` (a structured
// `JVal`); the apply engine lowers it at the `applyOne` entry via
// `PropValue.toObj` to the structural obj shapes this bridge parses — F#
// primitives (string / bool / float) boxed natively, JSON *objects*
// (TextSource discriminator records, Binding<T> discriminator records,
// CellFormat records, IconSource single-case DUs, etc.) as recursive
// `Map<string, obj>`. The apply engine's per-spec `dispatchUpdateField`
// wants strongly-typed values it can pour into the record field —
// `TextSource.Literal "..."`, `Binding.Static -125.0`,
// `Some (IconSource "...")`. A direct `unbox<TextSource>` against the
// Map<string,obj> fails with `InvalidCastException`, which the engine
// surfaces as the `KindMismatch` errors the baseline run captured for
// `op-002` / `err-002` / `err-009`.
//
// `Coerce` is the bridge. Each `try*` helper accepts the lowered obj
// shape, rebuilds the matching `Json` AST via the `objToJson` inverse, and
// delegates to the file's existing per-type Json decoders. Apply.fs's
// fast path still `unbox`es a `PropValue.Native`'s typed value directly
// (`Action.CommitLocal` tests, etc.), so this is purely additive on the
// failure path.

module Coerce =
    /// Inverse of the `PropValue.toObj` lowering — convert the lowered obj
    /// shape (`bool` / `string` / `float` / `Map<string,obj>` / `obj list`)
    /// back into the `Json` AST so the existing typed decoders can consume
    /// it. The lowering's emission rules are total over JSON inputs (every
    /// JVal case lowers to one of those CLR shapes), so the fallback
    /// string-of-anything branch is defensive — never expected to fire
    /// under canonical-JSON wire inputs.
#if FABLE_COMPILER
    // Fable refuses *every* generic type-test (`:? Map<string,obj>` and even
    // `:? IDictionary<string,obj>` are hard compile errors:
    // "Cannot type test (evals to false)"). `decodeObj` produces a
    // `Map<string,obj>` for every JSON object and an `obj list` for every JSON
    // array — both surface as `System.Collections.IEnumerable`, so the
    // non-generic enumerable test alone can't tell them apart. Distinguish by
    // comparing the JS *constructor reference* against a known-empty F# map:
    // every `FSharpMap` shares one constructor object, distinct from
    // `FSharpList`'s. Comparing references (not `constructor.name` strings) is
    // robust under production minification and free of any Fable-internal
    // field-name assumption. The `.NET` branch keeps the direct `Map` test —
    // behaviour is byte-identical across pipelines. (Phase 191.)
    [<Fable.Core.Emit("$0 != null && $1 != null && $0.constructor === $1.constructor")>]
    let private sameCtor (_a: obj) (_b: obj) : bool = Fable.Core.Util.jsNative

    let private emptyStringMap: obj = box (Map.empty<string, obj>)

    let rec private objToJson (v: obj) : Json =
        match v with
        | null -> JNull
        | :? bool as b -> JBool b
        | :? string as s -> JString s
        | :? float as f -> JNumber f
        | :? int as i -> JNumber(float i)
        | _ when sameCtor v emptyStringMap ->
            let m = v :?> Map<string, obj>
            JObject(Map.map (fun _ vv -> objToJson vv) m)
        | :? System.Collections.IEnumerable as e -> JArray [ for item in e -> objToJson item ]
        | _ -> JString(string v)
#else
    let rec private objToJson (v: obj) : Json =
        match v with
        | null -> JNull
        | :? bool as b -> JBool b
        | :? string as s -> JString s
        | :? float as f -> JNumber f
        | :? int as i -> JNumber(float i)
        | :? Map<string, obj> as m -> JObject(Map.map (fun _ vv -> objToJson vv) m)
        | :? System.Collections.IEnumerable as e ->
            let items = [ for item in e -> objToJson item ]
            JArray items
        | _ -> JString(string v)
#endif

    /// Wrap a Json-AST typed decoder so it consumes the obj shape
    /// `decodeObj` produces. Path is a fixed sentinel — Apply re-frames
    /// the error message into its own `KindMismatch` shape with the real
    /// field path attached at the call site.
    let private viaJson (decoder: string -> Json -> Result<'T, DecodeError>) (v: obj) : Result<'T, string> =
        match decoder "$value" (objToJson v) with
        | Ok x -> Ok x
        | Error e -> Error e.Message

    /// Optional flavour: a JSON `null` (or CLR `null`) decodes to `None`;
    /// anything else is decoded as `'T` and wrapped in `Some`.
    let private viaJsonOpt (decoder: string -> Json -> Result<'T, DecodeError>) (v: obj) : Result<'T option, string> =
        match v with
        | null -> Ok None
        | _ ->
            match decoder "$value" (objToJson v) with
            | Ok x -> Ok(Some x)
            | Error e -> Error e.Message

    let tryTextSource (v: obj) : Result<TextSource, string> = viaJson decodeTextSource v

    let tryTextSourceOption (v: obj) : Result<TextSource option, string> = viaJsonOpt decodeTextSource v

    let tryBindingFloat (v: obj) : Result<Binding<float>, string> = viaJson decodeBindingFloat v

    let tryBindingFloatOption (v: obj) : Result<Binding<float> option, string> = viaJsonOpt decodeBindingFloat v

    let tryBindingInt (v: obj) : Result<Binding<int>, string> = viaJson decodeBindingInt v
    let tryBindingBool (v: obj) : Result<Binding<bool>, string> = viaJson decodeBindingBool v

    /// `Anchor.Href : Binding<string>` — wire shape is a Binding discriminator
    /// object (`{ "$type": "Static", "value": "…" }` etc.), which `decodeObj`
    /// surfaces as a `Map<string,obj>`. Previously this call site relied on the
    /// .NET-only `unbox` fast path (no Coerce arm); naming the decoder makes the
    /// coercion run under Fable too (Phase 191).
    let tryBindingString (v: obj) : Result<Binding<string>, string> = viaJson decodeBindingString v

    /// Bare-string fields (`FragmentDecl.Name` / `FragmentRef.Name`, the
    /// `GridLayout.TemplateColumns` raw-string sugar). The wire value is a JSON
    /// string, which `decodeObj` boxes as a CLR `string`. `requireString` over
    /// `objToJson v` validates it uniformly with the other coercers (Phase 191).
    let tryString (v: obj) : Result<string, string> = viaJson requireString v

    /// Optional bare-string fields (`Anchor.Rel` / `Anchor.Target` /
    /// `GridLayout.TemplateColumns`). The encoder writes `Some s` as the bare
    /// string and omits / nulls `None`; `viaJsonOpt` maps a CLR `null` to `None`
    /// and decodes anything else as the string (Phase 191).
    let tryStringOption (v: obj) : Result<string option, string> = viaJsonOpt requireString v

    let tryCellFormat (v: obj) : Result<CellFormat, string> = viaJson decodeCellFormat v

    let tryCellFormatOption (v: obj) : Result<CellFormat option, string> = viaJsonOpt decodeCellFormat v

    /// `Column.Width : ColumnWidth` — wire shape is a `$type` object
    /// (`{"$type":"Fixed","pixels":120}` etc.). Added for the Phase 364 nested
    /// UpdateProp surface (`Columns[i].Width`).
    let tryColumnWidth (v: obj) : Result<ColumnWidth, string> = viaJson decodeColumnWidth v

    let tryIconSourceOption (v: obj) : Result<IconSource option, string> = viaJsonOpt decodeIconSource v

    let tryOrientation (v: obj) : Result<Orientation, string> = viaJson decodeOrientation v
    let tryTone (v: obj) : Result<ToneVariant, string> = viaJson decodeTone v
    let tryStyleWeight (v: obj) : Result<StyleWeight, string> = viaJson decodeWeight v
    let tryEmphasis (v: obj) : Result<Emphasis, string> = viaJson decodeEmphasis v

    /// The behavioural `emphasis` BOOL on Fact / LabelValueRow — the
    /// UpdateProp twin of `decodeEmphasisFlag`, so a TreeOp edit gets the
    /// same cross-vocabulary admission as a fresh decode (2026-07-19 sweep).
    let tryEmphasisFlag (v: obj) : Result<bool, string> = viaJson decodeEmphasisFlag v
    let tryHeadingVariant (v: obj) : Result<HeadingVariant, string> = viaJson decodeHeadingVariant v
    let tryBadgeVariant (v: obj) : Result<BadgeVariant, string> = viaJson decodeBadgeVariant v

    /// `Button.Variant : ButtonVariant` — the UpdateProp twin of
    /// `decodeButtonVariant`, added with the Input family's field-level
    /// UpdateProp surface so a `Button` is editable field-by-field rather than
    /// only swappable wholesale via `EditNode`.
    let tryButtonVariant (v: obj) : Result<ButtonVariant, string> = viaJson decodeButtonVariant v

    /// `FileUpload.Accept : string list` / `Chart.YFields : string list` — a
    /// JSON array of strings.
    let tryStringList (v: obj) : Result<string list, string> =
        let decodeStringList (path: string) (j: Json) =
            requireArray path j
            |> Result.bind (traverseIndexed (fun i item -> requireString (sprintf "%s[%d]" path i) item))

        viaJson decodeStringList v

    /// `CodeBlock.HighlightLines : int list`.
    let tryIntList (v: obj) : Result<int list, string> =
        let decodeIntList (path: string) (j: Json) =
            requireArray path j
            |> Result.bind (traverseIndexed (fun i item -> requireInt (sprintf "%s[%d]" path i) item))

        viaJson decodeIntList v

    /// `List.Items : TextSource list`.
    let tryTextSourceList (v: obj) : Result<TextSource list, string> =
        let decodeTextSourceList (path: string) (j: Json) =
            requireArray path j
            |> Result.bind (traverseIndexed (fun i item -> decodeTextSource (sprintf "%s[%d]" path i) item))

        viaJson decodeTextSourceList v

    // The remaining closed-enum twins, added with the Display / Layout /
    // Visualisation field-level UpdateProp surface.
    let tryImageVariant (v: obj) : Result<ImageVariant, string> = viaJson decodeImageVariant v
    let tryMathDisplay (v: obj) : Result<MathDisplay, string> = viaJson decodeMathDisplay v

    let tryScrollOrientation (v: obj) : Result<ScrollOrientation, string> = viaJson decodeScrollOrientation v

    let tryChartKind (v: obj) : Result<ChartKind, string> = viaJson decodeChartKind v

    /// `ScrollArea.MaxHeight` / `MaxWidth : int option`.
    let tryIntOption (v: obj) : Result<int option, string> = viaJsonOpt requireInt v

    /// JSON numbers decode as boxed float — narrow to int for fields like
    /// `Heading.Level` / `Grid.Cols` / `Skeleton.Rows`. Native F# callers
    /// who already box an int still go through `Apply.tryUnbox`'s direct
    /// path; this only fires on the JsonDecode fallback.
    // Diagnostic messages report the offending *value* (`%A`) rather than its
    // CLR type name: `v.GetType().FullName` is not Fable-portable (Fable can
    // only resolve types at compile time), and this coercion path must run on
    // both the .NET host and the Fable browser host (Phase 191).
    let tryInt (v: obj) : Result<int, string> =
        match v with
        | :? int as i -> Ok i
        | :? float as f -> Ok(int f)
        | _ -> Error(sprintf "expected int (or JSON-decoded float); got value %A" v)

    let tryFloat (v: obj) : Result<float, string> =
        match v with
        | :? float as f -> Ok f
        | :? int as i -> Ok(float i)
        | _ -> Error(sprintf "expected float; got value %A" v)

    let tryBool (v: obj) : Result<bool, string> =
        match v with
        | :? bool as b -> Ok b
        | _ -> Error(sprintf "expected bool; got value %A" v)

// ─── Public surface ─────────────────────────────────────────────────────

/// Decode a canonical-JSON encoded `Node<'Msg>` payload into a `WireTree` —
/// the storage-shape `Node<obj>` marked as wire-originated. The wire format is
/// the output of `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`.
/// Closure-bearing slots decode to inert placeholders carrying the `"<closure>"`
/// sentinel, so the result is safe to persist / diff / apply ops to / drive
/// server-side (`BoundedDriver.init` takes a `WireTree` directly) but NOT to
/// render through the live client renderer (its handlers are gone) — see
/// `WireTree`. The orchestrator's typed re-attachment happens downstream
/// (`moduleMsgDecoder: JVal -> 'Msg`). Use `decodeNodeObj` for the raw
/// `Node<obj>` when you are the reattachment / persistence boundary.
let decodeNode (json: string) : Result<WireTree, DecodeError> =
    let decoded =
        match tryParse json with
        | Error parseErr ->
            err
                DecodeErrorCode.INVALID_JSON
                "$"
                (sprintf "input is not valid JSON: %s" parseErr)
                (Some "well-formed JSON object per the canonical-JSON shape")
        | Ok j -> decodeNodeAst "$" j

    decoded |> Result.map WireTree.ofDecoded

/// Raw `Node<obj>` decode — the escape hatch for reattachment / persistence
/// boundaries that need the unmarked tree (equivalent to
/// `decodeNode json |> Result.map WireTree.reify`). Prefer `decodeNode`; this
/// exists so those boundaries don't wrap-then-immediately-reify.
let decodeNodeObj (json: string) : Result<Node<obj>, DecodeError> =
    match tryParse json with
    | Error parseErr ->
        err
            DecodeErrorCode.INVALID_JSON
            "$"
            (sprintf "input is not valid JSON: %s" parseErr)
            (Some "well-formed JSON object per the canonical-JSON shape")
    | Ok j -> decodeNodeAst "$" j

/// Decode a canonical-JSON encoded `TreeOp<'Msg>` payload into the
/// storage-shape `TreeOp<obj>`. Symmetric with
/// `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp`.
let decodeOp (json: string) : Result<TreeOp<obj>, DecodeError> =
    match tryParse json with
    | Error parseErr ->
        err
            DecodeErrorCode.INVALID_JSON
            "$"
            (sprintf "input is not valid JSON: %s" parseErr)
            (Some "well-formed JSON object per the canonical-JSON shape")
    | Ok j -> decodeTreeOpAst "$" j
