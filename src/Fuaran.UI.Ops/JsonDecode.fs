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
    /// Top-level `"kind"` discriminator not a recognised node kind — i.e. not
    /// one of the flat Layout / Display / Input / Visualisation primitives nor
    /// one of the structural kinds (WIRE_FORMAT §3.2). `knownNodeKinds` below
    /// is the enumeration; this comment deliberately does not restate it.
    | WRONG_NODE_KIND
    /// `"id"` field present but empty — same defect
    /// `PreEmitValidate.EmptyNodeId` catches downstream after apply.
    | EMPTY_NODE_ID
    /// A `Fuaran.UI.WireLimits` resource limit was exceeded (WIRE_FORMAT §21):
    /// node nesting past `MaxDepth`, JSON nesting past `MaxJsonDepth`, a string
    /// past `MaxStringLength`, or an array / object past `MaxArrayLength`.
    ///
    /// Phase 781. Its whole purpose is that this is an ERROR rather than a
    /// `StackOverflowException` — which .NET cannot catch, so past that point
    /// no envelope of any code can be returned at all. The `Message` names the
    /// limit and the observed value so a repairing author knows which bound to
    /// come back under.
    | LIMIT_EXCEEDED

module DecodeErrorCode =
    let toString (code: DecodeErrorCode) : string =
        match code with
        | DecodeErrorCode.INVALID_JSON -> "INVALID_JSON"
        | DecodeErrorCode.MISSING_FIELD -> "MISSING_FIELD"
        | DecodeErrorCode.WRONG_TYPE -> "WRONG_TYPE"
        | DecodeErrorCode.UNKNOWN_DU_CASE -> "UNKNOWN_DU_CASE"
        | DecodeErrorCode.WRONG_NODE_KIND -> "WRONG_NODE_KIND"
        | DecodeErrorCode.EMPTY_NODE_ID -> "EMPTY_NODE_ID"
        | DecodeErrorCode.LIMIT_EXCEEDED -> "LIMIT_EXCEEDED"

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

/// Parser cursor + the Phase 781 resource-limit bookkeeping.
///
/// `Depth` is the current syntactic JSON nesting level (0 outside any composite
/// value), incremented on entry to an object / array and decremented on exit;
/// `parseValue` refuses past `WireLimits.MaxJsonDepth`. Without it,
/// `parseValue` / `parseObjectValue` / `parseArrayValue` are mutually recursive
/// with nothing bounding them, and about 200 KB of `[[[[[…` — two bytes per
/// level — is a `StackOverflowException`: uncatchable in .NET, so the process
/// dies and no `Result` is ever returned.
///
/// `Breach` records that a failure was a LIMIT rather than a syntax error, so
/// `tryParse` can hand the caller `LIMIT_EXCEEDED` instead of the
/// `INVALID_JSON` every other parse failure maps to. It is a field rather than
/// a richer error type because the parser's internals are threaded through
/// `Result<_, string>` in about forty places; carrying one out-of-band flag is
/// a far smaller change than re-typing all of them, and it is written exactly
/// once per parse.
type private ParseState =
    { Text: string
      mutable Pos: int
      mutable Depth: int
      mutable Breach: bool }

/// Record a resource-limit breach and produce the message. The `Breach` flag is
/// what promotes the eventual failure from `INVALID_JSON` to `LIMIT_EXCEEDED`.
let private limitError (s: ParseState) (message: string) : Result<'a, string> =
    s.Breach <- true
    Error message

let private peek (s: ParseState) : char =
    if s.Pos < s.Text.Length then s.Text[s.Pos] else ' '

let private advance (s: ParseState) : unit = s.Pos <- s.Pos + 1

let private skipWs (s: ParseState) : unit =
    while s.Pos < s.Text.Length
          && (let c = s.Text[s.Pos] in c = ' ' || c = '\t' || c = '\n' || c = '\r') do
        advance s

let private parseError (s: ParseState) (msg: string) : Result<'a, string> =
    // Phase 810 — nested parsers each wrapped the propagated message again,
    // so a deep failure read "parse error at offset N:" up to six times and
    // pushed the didactic tail (the load-bearing part) further back each
    // level. The INNERMOST wrap carries the precise offset; propagate it
    // unchanged.
    if msg.StartsWith "parse error at offset" then
        Error msg
    else
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
        | None ->
            // Phase 781 / WIRE_FORMAT §21. Linear rather than recursive work, so
            // it cannot overflow the stack — but an unbounded string is still an
            // unbounded allocation, and closing depth without closing this would
            // leave the cheapest remaining denial-of-service open.
            if sb.Length > Fuaran.UI.WireLimits.MaxStringLength then
                limitError
                    s
                    (sprintf
                        "string of length %d exceeds the wire limit MaxStringLength = %d"
                        sb.Length
                        Fuaran.UI.WireLimits.MaxStringLength)
            else
                Ok(sb.ToString())

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

/// True when entering one more level of syntactic nesting would exceed
/// `MaxJsonDepth`. Callers that pass this increment `Depth` and decrement it on
/// the way out; callers that do not never incremented, so nothing to undo.
let private wouldExceedDepth (s: ParseState) : bool =
    s.Depth >= Fuaran.UI.WireLimits.MaxJsonDepth

let private depthLimitMessage: string =
    sprintf "JSON nesting deeper than the wire limit MaxJsonDepth = %d" Fuaran.UI.WireLimits.MaxJsonDepth

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
        elif wouldExceedDepth s then
            limitError s depthLimitMessage
        else
            s.Depth <- s.Depth + 1
            let mutable acc: (string * Json) list = []
            let mutable count = 0
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
                            count <- count + 1

                            if count > Fuaran.UI.WireLimits.MaxArrayLength then
                                s.Breach <- true

                                error <-
                                    Some(
                                        sprintf
                                            "object with more than the wire limit MaxArrayLength = %d members"
                                            Fuaran.UI.WireLimits.MaxArrayLength
                                    )
                            else
                                skipWs s

                                match peek s with
                                | ',' -> advance s
                                | '}' ->
                                    advance s
                                    finished <- true
                                | other -> error <- Some(sprintf "expected ',' or '}' but found '%c'" other)

            s.Depth <- s.Depth - 1

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
        elif wouldExceedDepth s then
            limitError s depthLimitMessage
        else
            s.Depth <- s.Depth + 1
            let mutable acc: Json list = []
            let mutable count = 0
            let mutable error: string option = None
            let mutable finished = false

            while not finished && error.IsNone do
                match parseValue s with
                | Error e -> error <- Some e
                | Ok v ->
                    acc <- v :: acc
                    count <- count + 1

                    if count > Fuaran.UI.WireLimits.MaxArrayLength then
                        s.Breach <- true

                        error <-
                            Some(
                                sprintf
                                    "array longer than the wire limit MaxArrayLength = %d"
                                    Fuaran.UI.WireLimits.MaxArrayLength
                            )
                    else
                        skipWs s

                        match peek s with
                        | ',' -> advance s
                        | ']' ->
                            advance s
                            finished <- true
                        | other -> error <- Some(sprintf "expected ',' or ']' but found '%c'" other)

            s.Depth <- s.Depth - 1

            match error with
            | Some e -> parseError s e
            | None -> Ok(JArray(List.rev acc))

/// The one didactic-free error class, closed (2026-08-09). A syntax failure
/// carries only an offset, so a model repairing from the envelope has no cause
/// to act on — and the emission class that hides behind it is inline
/// arithmetic (`"value": 178 / 180`): the model computes a fraction and writes
/// the division, which is not JSON. The parser halts exactly on the operator
/// (a complete number was parsed, then `,`/`}`/`]` was expected), so sniff the
/// failure site for `<number> <op> <number>` and, on a hit, name the cause the
/// way every other didactic in this decoder does. Purely additive to the
/// message — never fires on a payload that parses, and a miss changes nothing.
let private sniffArithmeticExpression (text: string) (pos: int) : string option =
    let isDigit c = c >= '0' && c <= '9'

    let isNumChar c =
        isDigit c || c = '.' || c = 'e' || c = 'E' || c = '+' || c = '-'

    let isWs c = c = ' ' || c = '\t'

    if pos < 0 || pos >= text.Length then
        None
    else
        let op = text[pos]

        if op <> '/' && op <> '*' && op <> '+' && op <> '-' then
            None
        else
            // Left operand: skip inline whitespace backwards; a JSON number
            // always ends in a digit, then extend through the number run.
            let mutable i = pos - 1

            while i >= 0 && isWs text[i] do
                i <- i - 1

            let leftEnd = i

            if leftEnd < 0 || not (isDigit text[leftEnd]) then
                None
            else
                while i >= 0 && isNumChar text[i] do
                    i <- i - 1

                let leftStart = i + 1

                // Right operand: skip inline whitespace forwards; accept an
                // optional leading '-' then require a digit.
                let mutable j = pos + 1

                while j < text.Length && isWs text[j] do
                    j <- j + 1

                let rightStart = j

                let rightLooksNumeric =
                    (j < text.Length && isDigit text[j])
                    || (j + 1 < text.Length && text[j] = '-' && isDigit text[j + 1])

                if not rightLooksNumeric then
                    None
                else
                    while j < text.Length && isNumChar text[j] do
                        j <- j + 1

                    Some(text.Substring(leftStart, j - leftStart))

/// Parse, classifying the failure. The code is `LIMIT_EXCEEDED` when a
/// `WireLimits` bound was hit (the `ParseState.Breach` flag) and `INVALID_JSON`
/// for every ordinary syntax failure — the two are different repairs, and only
/// one of them means "the input was structurally hostile rather than malformed".
let private tryParse (input: string) : Result<Json, DecodeErrorCode * string> =
    if isNull input then
        Error(DecodeErrorCode.INVALID_JSON, "input is null")
    else
        let state =
            { Text = input
              Pos = 0
              Depth = 0
              Breach = false }

        skipWs state

        let outcome =
            if state.Pos >= state.Text.Length then
                Error "input is empty"
            else
                parseValue state

        match outcome with
        | Ok j -> Ok j
        | Error message ->
            let code =
                if state.Breach then
                    DecodeErrorCode.LIMIT_EXCEEDED
                else
                    DecodeErrorCode.INVALID_JSON

            let message =
                if state.Breach then
                    message
                else
                    match sniffArithmeticExpression state.Text state.Pos with
                    | Some expr ->
                        message
                        + sprintf
                            " — cause: '%s' is an arithmetic expression in a value position. JSON has no expressions; compute the value yourself and emit only the resulting number"
                            expr
                    | None -> message

            Error(code, message)

// ─── Implied-node-close decode recovery (fuaran#850) ──────────────────────
//
// The one malformed-emission class the wire grammar cannot teach away: a node
// wrapper's closing brace dropped at the end of a `children[]` / `cases[]`
// element (or the root node left open at EOF), typically after a run of ≥2
// closing braces — the model closes the nested spec value and `kind` and stops
// one brace short of the node's own `}`. Measured on stored model emissions
// (2026-08-15): the emission is canonical in intent and correct in vocabulary,
// and fails on a brace count in a run; teaching the brace rule in the prompt
// has zero measured effect (failures re-emit the exact fragment the teaching
// quotes as its own wrong half), so the class is closed at the decode boundary
// instead.
//
// The remedy is measured, not assumed. Auto-close at EOF — the obvious fix —
// recovers NONE of the mid-document cells: the missing brace is owed
// mid-document, so the `]` that follows it is already mis-parsed and appending
// closers at the end fixes nothing (pinned as a test so the wrong fix cannot
// return). What recovers the whole measured set is auto-close on an
// ANCESTOR-LEGAL TOKEN: when `]` (or the array-level `,` that separates two
// element objects) arrives while node wrappers opened inside that array are
// still open, close the owed wrappers implicitly; when EOF arrives with the
// document prefix-valid and every open wrapper closable, close what is owed.
//
// CONTRACT — bounded, profile-gated, fails closed:
//   - Attempted ONLY after `tryParse` fails with `INVALID_JSON` (never on
//     `LIMIT_EXCEEDED`, and never on a document that parses — the happy path
//     does not enter this code at all).
//   - INSERT-ONLY OWED CLOSERS: the recovery inserts `}` for wrappers that are
//     demonstrably open at a boundary where their close is the only insert-only
//     reading (plus the matching `]`/`}` closers at a clean EOF). It never
//     invents content, keys, values, or brackets that open anything.
//   - PROFILE-GATED: mid-document closes fire only into an array keyed
//     `children` / `cases` (the node-wrapper positions of the measured class).
//     The same defect inside any other array does NOT recover — it stays a
//     visible error, so the demand signal for other classes is not eaten.
//   - FAILS CLOSED: any input outside the profile — genuinely-ambiguous
//     nesting (a wrapper that is mid-key, awaiting a value, or after a `,`),
//     an over-closed document, an unterminated string, a truncated tail, or a
//     repaired text that still does not parse — returns the ORIGINAL error
//     unchanged.
//   - COUNTED: every recovery is recorded under the `Reliance` counter id
//     `implied-node-close`, surfacing exactly the way the §16 leniencies are
//     measured. This is error RECOVERY, not §16 shorthand normalisation — a
//     silently-recovered class stops generating demand signal, and the counter
//     is what keeps it measurable while ceasing to be a loss (see
//     `docs/migrations/850-implied-node-close-recovery.md`).

/// Reliance accounting for decode-time recoveries — the read side of the
/// "recover WITH the coercion counted" posture. A recovery that fired is a
/// document that was malformed and was repaired at the boundary; consumers
/// measuring how much a cohort of emissions leans on the lenient decoder read
/// these counters beside the §16 structural-divergence measurement.
///
/// Counts are process-wide and monotonic between `reset` calls. The write side
/// is internal (only the decode entry points record); the read side —
/// `count` / `snapshot` / `reset` — is public surface.
module Reliance =
    /// Counter id for the implied-node-close recovery (fuaran#850): a node
    /// wrapper's dropped closing brace at a `children[]`/`cases[]`/root
    /// boundary, repaired by ancestor-legal-token auto-close.
    [<Literal>]
    let ImpliedNodeClose = "implied-node-close"

    // Module-level mutable, justified: this IS the process-wide accounting
    // surface — the counter must survive across decode calls with no ambient
    // context to thread it through (`decodeNode` is a pure string -> Result
    // function consumed from both .NET and Fable hosts). Updates are a single
    // reference swap; racing writers on .NET may under-count (telemetry
    // best-effort, documented), never corrupt.
    let mutable private counters: Map<string, int> = Map.empty

    /// Record one recovery under `counterId`. Internal — the decode entry
    /// points are the only writers.
    let internal record (counterId: string) : unit =
        let current = counters |> Map.tryFind counterId |> Option.defaultValue 0
        counters <- counters |> Map.add counterId (current + 1)

    /// Recoveries recorded under `counterId` since process start (or the last
    /// `reset`).
    let count (counterId: string) : int =
        counters |> Map.tryFind counterId |> Option.defaultValue 0

    /// Every counter id with its count — the surfacing read side.
    let snapshot () : Map<string, int> = counters

    /// Zero every counter (per-run measurement isolation; tests).
    let reset () : unit = counters <- Map.empty

module private ImpliedNodeClose =
    [<RequireQualifiedAccess>]
    type Kind =
        | Obj
        | Arr

    /// Between-token expectation of the innermost open container. A container
    /// with an open child sits in `Value` (object) / `Value` or `ValueOrClose`
    /// (array) until the child completes — which is what makes the owed-wrapper
    /// chain checkable: a frame BELOW the top is mid-value by construction, and
    /// its pending value IS the frame above it.
    [<RequireQualifiedAccess>]
    type State =
        /// Object, after `{` — a key or `}` may follow.
        | KeyOrClose
        /// Object, after `,` — a key must follow.
        | Key
        /// Object, after a key — `:` must follow.
        | Colon
        /// A value must follow (object: after `:`; array: after `,`).
        | Value
        /// After a complete member / element — `,` or the closer.
        | CommaOrClose
        /// Array, after `[` — a value or `]` may follow.
        | ValueOrClose

    type Frame =
        {
            Kind: Kind
            mutable State: State
            /// Arr frames only: the object key whose value this array is (None
            /// for an array nested directly in an array) — the profile gate.
            ArrKey: string option
            /// Obj frames only: the most recently read member key.
            mutable LastKey: string option
        }

    /// The array keys the mid-document recovery may close owed wrappers into —
    /// the node-list positions of the measured class. Deliberately NOT every
    /// array: a dropped brace in a data array stays a visible error.
    let private recoveryArrayKeys = [ "children"; "cases" ]

    /// Scan `text`, closing owed node wrappers at ancestor-legal tokens.
    /// `Some repaired` when the profile matched and a bounded repair exists;
    /// `None` otherwise (the caller falls back to the original error).
    let tryRecover (text: string) : string option =
        if isNull text then
            None
        else
            let n = text.Length
            let mutable i = 0
            let stack = ResizeArray<Frame>()
            let inserts = ResizeArray<int>()
            let mutable rootDone = false
            let mutable failed = false
            let mutable finished = false

            let isWsChar c =
                c = ' ' || c = '\t' || c = '\n' || c = '\r'

            let skipWsLocal () =
                while i < n && isWsChar text[i] do
                    i <- i + 1

            // Skip a string literal (cursor on the opening quote); false on an
            // unterminated string — the truncation fingerprint, never recovered.
            let skipString () =
                i <- i + 1
                let mutable closed = false

                while not closed && i < n do
                    let c = text[i]

                    if c = '\\' then
                        i <- i + 2
                    elif c = '"' then
                        i <- i + 1
                        closed <- true
                    else
                        i <- i + 1

                closed

            let readKey () : string option =
                let start = i

                if skipString () then
                    Some(text.Substring(start + 1, i - start - 2))
                else
                    None

            let top () = stack[stack.Count - 1]

            // A completed value: advance the enclosing frame (or mark the root
            // value complete).
            let completeValue () =
                if stack.Count = 0 then
                    rootDone <- true
                else
                    (top ()).State <- State.CommaOrClose

            let isScalarStart c =
                c = '-' || (c >= '0' && c <= '9') || c = 't' || c = 'f' || c = 'n'

            let skipScalar () =
                let isScalarChar c =
                    c = '-'
                    || c = '+'
                    || c = '.'
                    || (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')

                while i < n && isScalarChar text[i] do
                    i <- i + 1

            // Consume the opening of one value at the cursor. Malformed scalars
            // are tolerated here — the repaired text re-parses through the real
            // parser, which is the validator of record.
            let openValue () : bool =
                let c = text[i]

                if c = '{' then
                    stack.Add
                        { Kind = Kind.Obj
                          State = State.KeyOrClose
                          ArrKey = None
                          LastKey = None }

                    i <- i + 1
                    true
                elif c = '[' then
                    let key =
                        if stack.Count > 0 && (top ()).Kind = Kind.Obj then
                            (top ()).LastKey
                        else
                            None

                    stack.Add
                        { Kind = Kind.Arr
                          State = State.ValueOrClose
                          ArrKey = key
                          LastKey = None }

                    i <- i + 1
                    true
                elif c = '"' then
                    if skipString () then
                        completeValue ()
                        true
                    else
                        false
                elif isScalarStart c then
                    skipScalar ()
                    completeValue ()
                    true
                else
                    false

            let isClosableObj (f: Frame) =
                f.Kind = Kind.Obj
                && (f.State = State.CommaOrClose || f.State = State.KeyOrClose)

            // Close the owed object-wrapper chain so the current token can be
            // consumed at the nearest enclosing array — the ancestor-legal-token
            // rule. The TOP frame must be a closable object (between members);
            // every frame beneath it in the chain is an object mid-value (its
            // pending value is the frame above, completed by the implied close);
            // the chain ends at the first enclosing array, which must be keyed
            // `children` / `cases`. Anything else fails closed.
            let closeOwedWrappers () : bool =
                if stack.Count = 0 || not (isClosableObj (top ())) then
                    false
                else
                    let mutable k = 1

                    while k < stack.Count
                          && stack[stack.Count - 1 - k].Kind = Kind.Obj
                          && stack[stack.Count - 1 - k].State = State.Value do
                        k <- k + 1

                    if k >= stack.Count then
                        false // the chain ran to the root — no enclosing array
                    else
                        let target = stack[stack.Count - 1 - k]

                        let profileOk =
                            target.Kind = Kind.Arr
                            && (match target.ArrKey with
                                | Some key -> List.contains key recoveryArrayKeys
                                | None -> false)

                        if not profileOk then
                            false
                        else
                            for _ in 1..k do
                                inserts.Add i
                                stack.RemoveAt(stack.Count - 1)

                            // The implied closes complete the array's pending
                            // element.
                            target.State <- State.CommaOrClose
                            true

            skipWsLocal ()

            if i >= n || text[i] <> '{' then
                None
            else
                openValue () |> ignore // pushes the root object frame

                while not failed && not finished do
                    skipWsLocal ()

                    if i >= n then
                        finished <- true
                    elif rootDone then
                        failed <- true // trailing content
                    else
                        let f = top ()
                        let c = text[i]

                        match f.Kind, f.State with
                        | Kind.Obj, State.KeyOrClose ->
                            if c = '}' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            elif c = '"' then
                                match readKey () with
                                | Some key ->
                                    f.LastKey <- Some key
                                    f.State <- State.Colon
                                | None -> failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.Key ->
                            if c = '"' then
                                match readKey () with
                                | Some key ->
                                    f.LastKey <- Some key
                                    f.State <- State.Colon
                                | None -> failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.Colon ->
                            if c = ':' then
                                i <- i + 1
                                f.State <- State.Value
                            else
                                failed <- true
                        | Kind.Obj, State.Value -> failed <- not (openValue ())
                        | Kind.Obj, State.CommaOrClose ->
                            if c = ',' then
                                // Lookahead: an object continuation must be a
                                // key. `,` then `{` is only legal at an ancestor
                                // ARRAY — the second signature of the class (the
                                // wrapper owed its close before the separator).
                                let save = i
                                i <- i + 1
                                skipWsLocal ()

                                if i < n && text[i] = '"' then
                                    f.State <- State.Key
                                elif i < n && text[i] = '{' then
                                    i <- save

                                    if not (closeOwedWrappers ()) then
                                        failed <- true
                                else
                                    failed <- true
                            elif c = '}' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            elif c = ']' then
                                // The first signature of the class: `]` while
                                // node wrappers inside the array are still open.
                                if not (closeOwedWrappers ()) then
                                    failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.ValueOrClose -> failed <- true // unreachable
                        | Kind.Arr, (State.ValueOrClose | State.Value) ->
                            if c = ']' && f.State = State.ValueOrClose then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            else
                                failed <- not (openValue ())
                        | Kind.Arr, State.CommaOrClose ->
                            if c = ',' then
                                i <- i + 1
                                f.State <- State.Value
                            elif c = ']' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            else
                                failed <- true
                        | Kind.Arr, _ -> failed <- true // unreachable

                if failed then
                    None
                else
                    // EOF. The TOP frame's own state gates (between members /
                    // elements only — a mid-key, post-`:`, or post-`,` cut is
                    // genuine truncation, never recovered). Frames below it are
                    // mid-value by construction (their value IS the frame
                    // above), so the implied close of the child completes them.
                    let eofCloses = ResizeArray<char>()
                    let mutable eofOk = true

                    if not rootDone then
                        if stack.Count = 0 then
                            eofOk <- false
                        else
                            let t = top ()

                            let topClosable =
                                match t.Kind, t.State with
                                | Kind.Obj, (State.CommaOrClose | State.KeyOrClose) -> true
                                | Kind.Arr, (State.CommaOrClose | State.ValueOrClose) -> true
                                | _ -> false

                            if not topClosable then
                                eofOk <- false
                            else
                                for idx in stack.Count - 1 .. -1 .. 0 do
                                    eofCloses.Add(if stack[idx].Kind = Kind.Obj then '}' else ']')

                    if not eofOk then
                        None
                    elif inserts.Count = 0 && eofCloses.Count = 0 then
                        // The scanner consumed the document cleanly but the real
                        // parser rejected it — the failure is not this class.
                        None
                    elif inserts.Count + eofCloses.Count > Fuaran.UI.WireLimits.MaxJsonDepth then
                        None
                    else
                        // Insert positions are ascending by construction.
                        let sb = System.Text.StringBuilder(n + inserts.Count + eofCloses.Count)

                        let mutable prev = 0

                        for pos in inserts do
                            sb.Append(text.Substring(prev, pos - prev)).Append '}' |> ignore
                            prev <- pos

                        sb.Append(text.Substring prev) |> ignore

                        for ch in eofCloses do
                            sb.Append ch |> ignore

                        Some(sb.ToString())

/// Parse a NODE payload with the fuaran#850 implied-node-close recovery. A
/// document that parses takes the identical `tryParse` path (the recovery code
/// never runs); an `INVALID_JSON` failure whose profile matches the class is
/// re-parsed from the bounded repair (counted under
/// `Reliance.ImpliedNodeClose`); everything else — profile mismatch, ambiguous
/// nesting, a repair that still fails to parse, `LIMIT_EXCEEDED` — surfaces
/// the ORIGINAL error unchanged.
let private tryParseNodeWithRecovery (json: string) : Result<Json, DecodeErrorCode * string> =
    match tryParse json with
    | Ok j -> Ok j
    | Error(DecodeErrorCode.INVALID_JSON, _) as original ->
        match ImpliedNodeClose.tryRecover json with
        | Some repaired ->
            match tryParse repaired with
            | Ok j ->
                Reliance.record Reliance.ImpliedNodeClose
                Ok j
            | Error _ -> original
        | None -> original
    | Error _ as e -> e

// ─── The recognised NodeKind vocabulary (WIRE_FORMAT.md §3.2) ──────────────
//
// ONE enumeration, grouped by the four behavioural categories plus the
// structural kinds. `decodeNodeKind` dispatches the four family decoders off
// these lists, and `wrongNodeKindHint` is PROJECTED from them — so a kind the
// decoder recognises can never be missing from the hint a repair path forwards
// to the model, which is exactly the drift three hand-maintained hint strings
// had accumulated across the hosts.
//
// Pinned against the generated `wire-format-fixtures` manifest `kinds`
// enumeration by the JsonDecode test suite (beside the Phase 548 cross-host
// kind-set attestation), so the §11 forward-coupling discipline is enforced by
// a failing test rather than by a prose reminder.

/// The flat Layout primitives.
let layoutNodeKinds =
    [ "Box"
      "SplitPanel"
      "Tabs"
      "Stepper"
      "SummaryList"
      "Disclosure"
      "Modal"
      "ScrollArea" ]

/// The flat Display primitives.
let displayNodeKinds =
    [ "Heading"
      "Markdown"
      "Metric"
      "Badge"
      "Sparkline"
      "Callout"
      "Progress"
      "Skeleton"
      "Icon"
      "LabelValueRow"
      "Fact"
      "Link"
      "Image"
      "List"
      "Toast"
      "CodeBlock"
      "Math"
      "Drawing" ]

/// The flat Input primitives.
let inputNodeKinds = [ "Form"; "Filters"; "Button"; "FileUpload"; "Select" ]

/// The flat Visualisation primitives.
let visNodeKinds = [ "DataGrid"; "Chart"; "Map" ]

/// The structural kinds — each carries its own decoder arm in `decodeNodeKind`
/// rather than routing to a family decoder.
let structuralNodeKinds =
    [ "Custom"; "ErrorBoundary"; "Switch"; "FragmentDecl"; "FragmentRef"; "Mount" ]

/// The vocabulary as labelled groups — the single enumeration `knownNodeKinds`
/// flattens and `wrongNodeKindHint` is projected from. `Structural` is the one
/// group the hint lists bare; every other group is a named primitive family.
///
/// Each family decoder's own `unknownDuCase` fallback is projected from its list
/// too, rather than re-typed: the hand-written copies drifted (the Visualisation
/// fallback still advertised the retired `Table` kind long after it was removed),
/// and an error message that names a kind the decoder rejects is worse than none.
let nodeKindGroups =
    [ "Layout", layoutNodeKinds
      "Display", displayNodeKinds
      "Input", inputNodeKinds
      "Visualisation", visNodeKinds
      "Structural", structuralNodeKinds ]

/// Every recognised `kind.$type` discriminator, in wire-documentation order.
/// A discriminator outside this set is `WRONG_NODE_KIND`.
let knownNodeKinds = nodeKindGroups |> List.collect snd

/// The `ExpectedShape` hint carried by every `WRONG_NODE_KIND` error, projected
/// from the vocabulary above. Byte-identical to the sibling hosts' hint (the
/// group order is the shared contract), so a model repairing against one host's
/// error sees the same vocabulary it would from any other. Written as a fold
/// over the groups rather than five hardcoded lookups, so a NEW primitive family
/// also reaches the hint for free.
let wrongNodeKindHint =
    let primitives =
        nodeKindGroups
        |> List.filter (fun (label, _) -> label <> "Structural")
        |> List.map (fun (label, kinds) ->
            let article =
                if List.contains label[0] [ 'A'; 'E'; 'I'; 'O'; 'U' ] then
                    "an"
                else
                    "a"

            sprintf "%s %s primitive (%s)" article label (String.concat " | " kinds))
        |> String.concat ", "

    let structural =
        nodeKindGroups
        |> List.tryFind (fun (label, _) -> label = "Structural")
        |> Option.map snd
        |> Option.defaultValue []

    primitives + ", or " + String.concat " | " structural

/// Every recognised `FormFieldKind` discriminator (WIRE_FORMAT §11) — the ONE
/// control vocabulary shared by a `Form`'s fields and a `Filters` strip's chips,
/// in the same order the sibling hosts advertise it. A discriminator outside this
/// set is `UNKNOWN_DU_CASE`.
///
/// Phase 746 — this list exists so the control vocabulary has a *declaration* to
/// attest against the corpus, exactly as `knownNodeKinds` does for `NodeKind`.
/// Its first act was to catch its own predecessor: the hand-typed
/// `decodeFormFieldKind` fallback below had silently omitted `Range`, so a model
/// that emitted a malformed range chip was told the case did not exist.
let knownFormFieldKinds =
    [ "Text"
      "Number"
      "Checkbox"
      // Phase 766 — the switch affordance. Listing it here is what makes the
      // UNKNOWN_DU_CASE hint name it, which is the model-facing half: the
      // pilot-4 census recorded models reaching for `Toggle` x3 when it did not
      // exist, so the spelling was already the one the intent produces.
      "Toggle"
      "Choice"
      "Range"
      "RangedNumber"
      "SegmentedChoice"
      "TextArea"
      "Date"
      "DateRange" ]

/// The `ExpectedShape` hint carried by every `UNKNOWN_DU_CASE` a control
/// discriminator raises, projected from the vocabulary above rather than
/// re-typed — the same discipline `wrongNodeKindHint` follows, and for the same
/// reason (a hand-written copy drifts, and a hint that names the wrong set is
/// worse than none).
let wrongFormFieldKindHint = String.concat " | " knownFormFieldKinds

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

/// The depth breach shared by both `Json` -> `JVal` bridges. Phase 781: these
/// walk a STRUCTURED PAYLOAD position (`Custom` props, an action payload, a
/// `Transform` pipeline), which nests freely inside a single node and so is not
/// covered by the node-level `MaxDepth` at all — only by the parser's
/// `MaxJsonDepth`. The guard is repeated here rather than relied on from the
/// parser because a `Json` value reaching these is only *currently* guaranteed
/// to have come from `tryParse`; that is an invariant of this file's privacy,
/// not of the functions themselves.
let private jvalTooDeep (path: string) (depth: int) : Result<'a, DecodeError> =
    err
        DecodeErrorCode.LIMIT_EXCEEDED
        path
        (sprintf
            "JSON payload nesting depth %d exceeds the wire limit MaxJsonDepth = %d"
            depth
            Fuaran.UI.WireLimits.MaxJsonDepth)
        (Some(sprintf "a payload nesting no more than %d levels deep" Fuaran.UI.WireLimits.MaxJsonDepth))

/// Bridge the decoder's `Json` AST to `Fuaran.Core.JVal` so a `Binding.Transform`'s columnar
/// source + pipeline decode through the shared `Fuaran.Core` codecs (Phase 282 — the Compute
/// layer). Both ASTs already share the canonical `$type` discipline; the only impedance is Core's
/// `JInt`/`JFloat` split — an integral-valued JSON number bridges to `JInt` (Core's decoders widen
/// `JInt`→`JFloat` where a float is expected, but require `JInt` where an int is). The compute wire
/// carries no `null` (Column uses a validity mask), so `JNull` is unreachable here.
///
/// `depth` is this value's nesting level (the outermost value being 1); past
/// `MaxJsonDepth` the bridge refuses rather than recursing (Phase 781). The
/// return type became a `Result` for exactly that reason — it was total before,
/// which is precisely why it could only fail by killing the process.
/// Phase 815 — organic-demand leniencies for the Transform `source` slot, both
/// observed cross-family (claude, gemini, kimi — the Tier-D pilot, 2026-08-13):
/// models bind a derived value to a Transform whose source is
/// `{"$type":"State","defaultValue":[{row},…]}`. Two universal priors,
/// accommodated as typed data at THIS host bridge, before `Fuaran.Core`'s
/// `ColumnCodec` sees the value (the fuaran#633 `Bound`-unwrap precedent — no
/// Core change, no wire-spec change, no new key):
///   1. a `State`/`Static`/`Bound` binding WRAPPER around the data unwraps to
///      its `defaultValue`/`value` (initial-snapshot semantics — a LIVE
///      state-sourced Transform is the 032/c6 charter, deliberately not this);
///   2. ROW-MAJOR data (an array of row objects) transposes to the canonical
///      columnar `{"columns": …}` shape — first-row key set, absent cells
///      null. Canonical columnar and `ref` sources pass through untouched, so
///      existing fixtures stay byte-identical.
let private normaliseTransformSource (j: Json) : Json =
    let unwrapped =
        match j with
        | JObject fields ->
            match Map.tryFind "$type" fields with
            | Some(JString("State" | "Static" | "Bound")) ->
                match Map.tryFind "defaultValue" fields, Map.tryFind "value" fields with
                | Some inner, _
                | None, Some inner -> inner
                | None, None -> j
            | _ -> j
        | _ -> j

    match unwrapped with
    | JArray(JObject first :: _ as rows) ->
        let keys = first |> Map.toList |> List.map fst

        let cols =
            keys
            |> List.map (fun k ->
                let cells =
                    rows
                    |> List.map (function
                        | JObject rf -> Map.tryFind k rf |> Option.defaultValue JNull
                        | _ -> JNull)

                k, JArray cells)

        JObject(Map.ofList [ "columns", JObject(Map.ofList cols) ])
    | _ -> unwrapped

let rec private jsonToJVal (depth: int) (path: string) (j: Json) : Result<Fuaran.Core.JVal, DecodeError> =
    if depth > Fuaran.UI.WireLimits.MaxJsonDepth then
        jvalTooDeep path depth
    else
        match j with
        | JNull -> Ok(Fuaran.Core.JStr "")
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
                        jsonToJVal (depth + 1) (path + "[" + string i + "]") x
                        |> Result.map (fun v -> v :: items))

            folded |> Result.map (fun items -> Fuaran.Core.JArr(List.rev items))
        | JObject m ->
            let folded =
                (Ok [], m |> Map.toList)
                ||> List.fold (fun acc (k, v) ->
                    match acc with
                    | Error e -> Error e
                    | Ok fields ->
                        jsonToJVal (depth + 1) (path + "." + k) v
                        |> Result.map (fun jv -> (k, jv) :: fields))

            folded |> Result.map (fun fields -> Fuaran.Core.JObj(List.rev fields))

/// The same AST bridge for the JSON-valued PAYLOAD positions (Custom props,
/// Action.Notify / SetState / AiTool payloads, I18n args, a wire-form
/// `UpdateProp` value): identical number policy, but `null` is REJECTED at any
/// depth — the Fuaran wire model has no null (omit the field instead), and
/// `JVal` makes that unrepresentable by construction. The error names the rule
/// so an AI author recovers by omission, not by retrying encodings of null.
///
/// Depth-bounded on the same terms as `jsonToJVal` (Phase 781).
let rec private jsonToJValStrict (depth: int) (path: string) (j: Json) : Result<Fuaran.Core.JVal, DecodeError> =
    if depth > Fuaran.UI.WireLimits.MaxJsonDepth then
        jvalTooDeep path depth
    else
        match j with
        | JNull ->
            Error(
                DecodeError.create
                    DecodeErrorCode.WRONG_TYPE
                    path
                    "null is not representable in the Fuaran wire model — omit the field instead"
                    (Some
                        "any JSON value except null (rule 12: the wire model has no null; omit the field to mean absent)")
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
                        jsonToJValStrict (depth + 1) (path + "[" + string i + "]") x
                        |> Result.map (fun v -> v :: items))

            folded |> Result.map (fun items -> Fuaran.Core.JArr(List.rev items))
        | JObject m ->
            let folded =
                (Ok [], m |> Map.toList)
                ||> List.fold (fun acc (k, v) ->
                    match acc with
                    | Error e -> Error e
                    | Ok fields ->
                        jsonToJValStrict (depth + 1) (path + "." + k) v
                        |> Result.map (fun jv -> (k, jv) :: fields))

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
    { Id = closureSentinel
      Kind = NodeKind.Markdown({ Text = TextSource.Literal closureSentinel })
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

// ─── Variant DU decoders ─────────────────────────────────────────────────

let private decodeOrientation (path: string) (j: Json) : Result<Orientation, DecodeError> =
    match j with
    | JString "Vertical" -> Ok Orientation.Vertical
    | JString "Horizontal" -> Ok Orientation.Horizontal
    // Lenient-ingest aliases (decode-only) — the CSS flex-direction prior:
    // a row lays out horizontally, a column vertically. Same-concept mapping.
    | JString "Row"
    | JString "row" -> Ok Orientation.Horizontal
    | JString "Column"
    | JString "column" -> Ok Orientation.Vertical
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

/// The legal `ToneVariant` names, in one place because two positions now teach them
/// — a `tone` field and (Phase 750) a `TonedPill` tone-map value. A second inline
/// copy is exactly how one of them comes to name six tones.
let private toneVariantNames =
    "Default | Subdued | Brand | Success | Warning | Critical | Info"

let private decodeTone (path: string) (j: Json) : Result<ToneVariant, DecodeError> =
    match j with
    | JString "Default" -> Ok ToneVariant.Default
    | JString "Subdued" -> Ok ToneVariant.Subdued
    | JString "Brand" -> Ok ToneVariant.Brand
    | JString "Success" -> Ok ToneVariant.Success
    | JString "Warning" -> Ok ToneVariant.Warning
    | JString "Critical" -> Ok ToneVariant.Critical
    | JString "Info" -> Ok ToneVariant.Info
    // Phase 460 — lenient-ingest aliases (decode-only; never encoded — canonical
    // re-encode normalises to the DU case names). Faithful semantic mappings only:
    // Positive→Success, Danger/Negative→Critical, Neutral→Default. Documented in
    // WIRE_FORMAT.md's lenient-ingest table; `SchemaGen` stays strict-canonical.
    | JString "Positive" -> Ok ToneVariant.Success
    | JString "Danger"
    | JString "Negative" -> Ok ToneVariant.Critical
    | JString "Neutral" -> Ok ToneVariant.Default
    | JString s -> unknownDuCase path s toneVariantNames
    | _ -> wrongType path "JSON string (ToneVariant)"

let private decodeWeight (path: string) (j: Json) : Result<StyleWeight, DecodeError> =
    match j with
    | JString "Compact" -> Ok StyleWeight.Compact
    | JString "Standard" -> Ok StyleWeight.Standard
    | JString "Spacious" -> Ok StyleWeight.Spacious
    | JString s -> unknownDuCase path s "Compact | Standard | Spacious"
    | _ -> wrongType path "JSON string (StyleWeight)"

let private decodeEmphasis (path: string) (j: Json) : Result<Emphasis, DecodeError> =
    match j with
    | JString "Quiet" -> Ok Emphasis.Quiet
    | JString "Normal" -> Ok Emphasis.Normal
    | JString "Loud" -> Ok Emphasis.Loud
    // Phase 460 — lenient-ingest aliases (decode-only; never encoded). Prominence
    // intent survives: Strong/Bold→Loud, Subtle/Muted→Quiet. `StyleWeight` is
    // deliberately NOT aliased (Bold/Heavy is font-weight intent, but the language
    // means density Compact|Standard|Spacious — a mapping would misread the author).
    | JString "Strong"
    | JString "Bold" -> Ok Emphasis.Loud
    | JString "Subtle"
    | JString "Muted" -> Ok Emphasis.Quiet
    // 2026-07-19 collision sweep — `emphasis` is a same-name cross-vocabulary
    // collision (style ENUM here vs behavioural BOOL on Fact/LabelValueRow);
    // models cross it in both directions. A bool in the enum slot projects
    // one-to-one: true ⇒ Loud, false ⇒ Normal. The bool sites' direction
    // lives in `decodeEmphasisFlag`.
    | JBool true -> Ok Emphasis.Loud
    | JBool false -> Ok Emphasis.Normal
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

// Phase 819 — the Duration / RelativeTime format enums. Defined ahead of
// `decodeCellFormat` (which references them); `decodeFormat` below shares
// them (`decodeRelativeTimeUnit` moved up from the Format section for the
// same reason).
let private decodeDurationUnit (path: string) (j: Json) : Result<DurationUnit, DecodeError> =
    match j with
    | JString "Seconds" -> Ok DurationUnit.Seconds
    | JString "Minutes" -> Ok DurationUnit.Minutes
    | JString "Hours" -> Ok DurationUnit.Hours
    | JString s -> unknownDuCase path s "Seconds | Minutes | Hours"
    | _ -> wrongType path "JSON string (DurationUnit)"

let private decodeDurationStyle (path: string) (j: Json) : Result<DurationStyle, DecodeError> =
    match j with
    | JString "Compact" -> Ok DurationStyle.Compact
    | JString "Clock" -> Ok DurationStyle.Clock
    | JString "Long" -> Ok DurationStyle.Long
    | JString s -> unknownDuCase path s "Compact | Clock | Long"
    | _ -> wrongType path "JSON string (DurationStyle)"

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
        | Ok "Duration" ->
            // Phase 819 — trendable duration cells: raw float counts `unit`s,
            // rendered per `style`.
            match requireField path fields "unit" "DurationUnit string" with
            | Error e -> Error e
            | Ok unitJ ->
                match decodeDurationUnit (path + ".unit") unitJ with
                | Error e -> Error e
                | Ok unit ->
                    match requireField path fields "style" "DurationStyle string" with
                    | Error e -> Error e
                    | Ok styleJ ->
                        decodeDurationStyle (path + ".style") styleJ
                        |> Result.map (fun style -> CellFormat.Duration(unit, style))
        | Ok "RelativeTime" ->
            // Phase 819 — cell-vocabulary parity with `Format.RelativeTime`.
            match requireField path fields "unit" "RelativeTimeUnit string" with
            | Error e -> Error e
            | Ok j -> decodeRelativeTimeUnit (path + ".unit") j |> Result.map CellFormat.RelativeTime
        | Ok "Custom" ->
            // Encoder writes the fn as `<closure>`; decode to a placeholder
            // that returns the sentinel for any CellValue input.
            Ok(CellFormat.Custom(fun _ -> closureSentinel))
        | Ok s ->
            unknownDuCase
                path
                s
                "None | Number | Currency | Percent | SignificantDigits | Date | Duration | RelativeTime | Custom"

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

// (`decodeRelativeTimeUnit` lives beside `decodeCellFormat` above since
// Phase 819 — both format vocabularies reference it.)

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
        | Ok "Duration" ->
            // Phase 819 — locale-independent duration formatting.
            match requireField path fields "unit" "DurationUnit string" with
            | Error e -> Error e
            | Ok unitJ ->
                match decodeDurationUnit (path + ".unit") unitJ with
                | Error e -> Error e
                | Ok unit ->
                    match requireField path fields "style" "DurationStyle string" with
                    | Error e -> Error e
                    | Ok styleJ ->
                        decodeDurationStyle (path + ".style") styleJ
                        |> Result.map (fun style -> Format.Duration(unit, style))
        | Ok s -> unknownDuCase path s "Number | Currency | Percent | Date | RelativeTime | Duration"

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
let private decodeJVal (path: string) (j: Json) : Result<JVal, DecodeError> = jsonToJValStrict 1 path j

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

/// The `Binding<JVal>` flavour (the swap's typed verbatim carrier, D3) —
/// `Binding.I18n` args and `Binding.Transform` param sources since the swap.
and private decodeBindingJVal (path: string) (j: Json) : Result<Binding<JVal>, DecodeError> =
    bindingGeneric<JVal> path (fun p v -> jsonToJVal 1 p v) (JStr closureSentinel) j

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
    | JBool _ -> parseStatic path j |> Result.map (Some >> Binding.Static)
    | _ ->

        match requireObject path j with
        | Error e -> Error e
        | Ok fields ->
            match requireDiscriminator path fields with
            | Error e -> Error e
            | Ok "Static" ->
                // Phase 677 — absence is structural: a MISSING `value` (and the
                // legacy `"value": null` §16 shorthand) routes through the SLOT'S
                // own parser exactly as before the swap — the slot decides what
                // null means (an options slot normalises to `[]`, an option-typed
                // slot to its inner `None`, a scalar slot rejects). The parsed
                // payload wraps in the generated `Some`; a null-ish parsed
                // representation still omits its key at encode (the encoder's
                // inner `isAbsentPayload` check), so the two spellings cannot
                // disagree on the wire.
                let v =
                    match tryField fields "value" with
                    | Some v -> v
                    | None -> JNull

                parseStatic (path + ".value") v |> Result.map (Some >> Binding.Static)
            | Ok "Query" ->
                match requireField path fields "name" "query name string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".name") v
                    |> Result.bind (fun name ->
                        // Phase 421 — optional `dependsOn` string array (the declared filter
                        // edge); absent → `None` (the swap's typed absence; an explicit empty
                        // array normalises to `None` so re-encode stays canonical-minimal).
                        let dependsOnR =
                            // Field aliases: deps/dependencies — the React-hooks prior.
                            match optFieldAliased fields "dependsOn" [ "deps"; "dependencies" ] with
                            | None -> Ok None
                            | Some dJ ->
                                requireArray (path + ".dependsOn") dJ
                                |> Result.bind (traverse (requireString (path + ".dependsOn[]")))
                                |> Result.map (fun l -> if List.isEmpty l then None else Some l)

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

                        Binding.Selection(id, accessor, defaultV, fieldV))
            | Ok "State" ->
                match requireField path fields "key" "state key string" with
                | Error e -> Error e
                | Ok v ->
                    requireString (path + ".key") v
                    |> Result.map (fun key ->
                        // Decode the carried `defaultValue` through the typed
                        // static parser when it parses (Phase 426 — the write-back
                        // default reads a decoded field's own State default, and
                        // the TS decoder already carries it). Since the swap an
                        // absent / null / unparseable default is the OUTER `None`
                        // of the generated `defaultValue: 'T option` — the typed
                        // absence the encoder omits, so the round-trip is exact
                        // without a placeholder standing in.
                        let defaultV =
                            // Field aliases: initialValue/default — the React useState prior.
                            match optFieldAliased fields "defaultValue" [ "initialValue"; "default" ] with
                            | Some JNull
                            | None -> None
                            | Some dv ->
                                match parseStatic (path + ".defaultValue") dv with
                                | Ok parsed -> Some parsed
                                | Error _ -> None

                        Binding.State(key, defaultV))
            | Ok "Computed" ->
                // Encoder writes the fn as `<closure>`; decode to a placeholder.
                Ok(Binding.Computed(fun _ -> placeholder))
            | Ok "Now" ->
                // Phase 765 — the host-furnished current instant. No wire fields:
                // the VALUE is supplied by the runtime at resolve time (the `Query`
                // precedent), never carried on the wire, so a tree stays a pure
                // value and a replayed op-stream re-supplies the recorded instant
                // rather than re-reading a clock. The accessor decodes to a
                // placeholder exactly as `Computed`'s does.
                // Identity, not a placeholder (the 427 Selection fix replayed):
                // the instant is already the wire-shaped string.
                Ok(Binding.Now(fun (raw: obj) -> unbox raw))
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
                            // Positional since the swap (flushOn, format, initialFrom,
                            // onCommit, parse). The decoded stand-ins are unchanged in
                            // meaning: `format` is the renderer's old `None`-default
                            // (`string<'T>`) baked into the required slot, `onCommit` /
                            // `parse` the sentinel placeholders a host re-attaches over.
                            Ok(
                                Binding.Local(
                                    flushOn,
                                    (fun (v: 'T) -> string (box v)),
                                    initialFrom,
                                    Some(fun _ -> box closureSentinel),
                                    (fun _ -> Error closureSentinel)
                                )
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
                        let sourceR: Result<TransformSource, DecodeError> =
                            // Phase 815 — normalise the two observed organic
                            // shapes (State/Static wrapper; row-major rows)
                            // to canonical columnar before Core decodes.
                            // Phase 818 — a binding-shaped source (State /
                            // Selection / Query `$type`) is now PRESERVED as
                            // `TransformSource.Live` so a runtime re-evaluates
                            // the pipeline when the binding's channel changes.
                            // The initial snapshot still derives through the
                            // same 815 normalisation, so SSR output and the
                            // didactics (ragged rows; a State wrapper carrying
                            // NO data) are byte-identical to the snapshot era.
                            let snapshot () =
                                jsonToJVal 1 (path + ".source") (normaliseTransformSource srcJ)
                                |> Result.bind (fun v ->
                                    Fuaran.Core.ColumnCodec.decodeJson v
                                    |> Result.mapError (coreError (path + ".source")))

                            let liveTag =
                                match srcJ with
                                | JObject sf ->
                                    (match Map.tryFind "$type" sf with
                                     | Some(JString(("State" | "Selection" | "Query") as t)) -> Some t
                                     | _ -> None)
                                | _ -> None

                            match liveTag with
                            | None -> snapshot () |> Result.map TransformSource.Data
                            | Some tag ->
                                decodeBindingJVal (path + ".source") srcJ
                                |> Result.bind (fun b ->
                                    let carried =
                                        match b with
                                        | Binding.State(_, dv) -> dv
                                        | Binding.Selection(_, _, dv, _) -> dv
                                        | _ -> None

                                    match carried, tag with
                                    | Some(Fuaran.Core.JArr []), "State" ->
                                        // 0.23.1 — an EMPTY array default is the
                                        // empty table, exactly as Query/Selection
                                        // start: an initially-empty live collection
                                        // ("count the requests in an empty log")
                                        // has zero rows and no columns to infer;
                                        // the codec's refusal had nothing wrong to
                                        // name. Observed organically (terra, the
                                        // Tier-D cohort r0 count badge).
                                        Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource))
                                    | Some _, "State" ->
                                        // The carried data IS the initial snapshot;
                                        // the Json-level 815 path keeps the ragged-
                                        // rows didactic byte-identical.
                                        snapshot () |> Result.map (fun initial -> TransformSource.Live(b, initial))
                                    | Some data, _ ->
                                        // A Selection default may be scalar / row
                                        // shaped; a non-table default starts from
                                        // the empty snapshot (runtime evaluation
                                        // stays loud on a non-tabular value).
                                        (match Fuaran.UI.HostPrelude.TransformLive.initialSource data with
                                         | Ok initial -> Ok(TransformSource.Live(b, initial))
                                         | Error _ ->
                                             Ok(
                                                 TransformSource.Live(
                                                     b,
                                                     Fuaran.UI.HostPrelude.TransformLive.emptySource
                                                 )
                                             ))
                                    | None, "State" ->
                                        // A State wrapper carrying NO data still
                                        // errors didactically (the 815 posture) —
                                        // the columnar codec names the missing
                                        // canonical field.
                                        snapshot () |> Result.map TransformSource.Data
                                    | None, _ ->
                                        Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource)))

                        let pipelineR =
                            jsonToJVal 1 (path + ".pipeline") pipeJ
                            |> Result.bind (fun v ->
                                Fuaran.Core.DataFrameCodec.decodePipelineJson v
                                |> Result.mapError (coreError (path + ".pipeline")))

                        match sourceR with
                        | Error e -> Error e
                        | Ok source ->
                            match pipelineR with
                            | Error e -> Error e
                            | Ok pipeline ->
                                // Phase 424 — optional `params`: [{ "from": <Binding>, "name": <string> }, …]
                                // binding each `ColExpr.Param` name to a scalar `Binding<obj>` source.
                                // Absent → `[]` (byte-identical to the Phase 282 shape).
                                let decodeParam (el: Json) : Result<string * Binding<JVal>, DecodeError> =
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
                                            |> Result.bind (decodeBindingJVal (path + ".params." + name + ".from"))
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
                                                decodeBindingJVal (path + ".params." + name + ".from") v
                                                |> Result.map (fun b -> name, b))
                                    | Some pJ ->
                                        requireArray (path + ".params") pJ |> Result.bind (traverse decodeParam)

                                paramsR
                                |> Result.map (fun parameters ->
                                    // Since the swap: `TransformParam` records, omitted-when-empty
                                    // as the typed outer `None`.
                                    let ps =
                                        parameters
                                        |> List.map (fun (name, fromB) ->
                                            ({ From = fromB; Name = name }: TransformParam))

                                    Binding.Transform(source, pipeline, (if List.isEmpty ps then None else Some ps)))
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
                            |> Result.map (fun args ->
                                Binding.Invoke(
                                    capabilityId,
                                    args |> List.map (fun (addr, v) -> ({ Addr = addr; Value = v }: InvokeArg))
                                ))
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

and private decodeBindingObjArgs (path: string) (j: Json) : Result<Map<string, Binding<JVal>>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let entries = fields |> Map.toList

        let mapped =
            traverse (fun (k, v) -> decodeBindingJVal (path + "." + k) v |> Result.map (fun b -> k, b)) entries

        mapped |> Result.map Map.ofList

let private decodeBindingFloat (path: string) (j: Json) : Result<Binding<float>, DecodeError> =
    bindingGeneric<float> path requireFloat 0.0 j

let private decodeBindingInt (path: string) (j: Json) : Result<Binding<int>, DecodeError> =
    bindingGeneric<int> path requireInt 0 j

let private decodeBindingString (path: string) (j: Json) : Result<Binding<string>, DecodeError> =
    bindingGeneric<string> path requireString "" j

/// A Choice/SegmentedChoice value slot — `Binding<string>` where "no
/// selection" is the ABSENT `Static` payload (`{"$type":"Static"}`, or the
/// legacy `"value": null` §16 shorthand), decoded to `Static None` directly.
/// A scalar string slot rejects that shape (Phase 677 routing); a choice slot
/// is where the generated `Static of 'T option` makes absence first-class.
let private decodeBindingChoiceValue (path: string) (j: Json) : Result<Binding<string>, DecodeError> =
    let isAbsentStatic =
        match j with
        | JObject fields ->
            (match tryField fields "$type" with
             | Some(JString "Static") ->
                 match tryField fields "value" with
                 | None
                 | Some JNull -> true
                 | Some _ -> false
             | _ -> false)
        | _ -> false

    if isAbsentStatic then
        Ok(Binding.Static None)
    else
        decodeBindingString path j

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
    | JString s -> Ok { Value = s; Label = s }
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
                    match requireField path fields "label" "option label string" with
                    | Error e -> Error e
                    | Ok lJ ->
                        // The label is a bare string since the swap (the wire's
                        // literal form). The Literal ENVELOPE (`{"$type":
                        // "Literal","text":…}`) stays decode-accepted through
                        // the TextSource shorthand path and projects to its
                        // text; a Bound/I18n label has no string projection and
                        // is refused (it was never wire-expressible here — the
                        // encoder always emitted the literal form).
                        decodeTextSource (path + ".label") lJ
                        |> Result.bind (fun label ->
                            match label with
                            | TextSource.Literal s -> Ok { Value = value; Label = s }
                            | _ -> wrongType (path + ".label") "literal option label")

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
                    Label = opaqueSentinel } ]
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> traverseIndexed (fun i item -> decodeSelectOption (sprintf "%s[%d]" p i) item) xs

    bindingGeneric<SelectOption list>
        path
        parseStatic
        [ { Value = opaqueSentinel
            Label = opaqueSentinel } ]
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

let private decodeBindingFloatSeq (path: string) (j: Json) : Result<Binding<float list>, DecodeError> =
    let parseStatic (p: string) (v: Json) : Result<float list, DecodeError> =
        match v with
        | JNull -> Ok [] // pre-429 read-compat: an empty-list-backed seq boxed to `null`
        | JString s when s = opaqueSentinel -> Ok []
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> traverseIndexed (fun i item -> requireFloat (sprintf "%s[%d]" p i) item) xs

    bindingGeneric<float list> path parseStatic [] j

let private decodeBindingRangePair (path: string) (j: Json) : Result<Binding<RangePair>, DecodeError> =
    // 0.2.0 — the dual-thumb Range control's (min, max) pair, the `RangePair`
    // record since the swap. Static forms: the object `{min, max}` (canonical)
    // or a two-element array (lenient).
    let parseStatic (p: string) (v: Json) : Result<RangePair, DecodeError> =
        match v with
        | JObject pf ->
            match tryField pf "min", tryField pf "max" with
            | Some mn, Some mx ->
                match requireFloat (p + ".min") mn, requireFloat (p + ".max") mx with
                | Ok a, Ok b -> Ok { Min = a; Max = b }
                | Error e, _
                | _, Error e -> Error e
            | _ -> wrongType p "object with min and max numbers"
        | JArray [ a; b ] ->
            match requireFloat (p + "[0]") a, requireFloat (p + "[1]") b with
            | Ok x, Ok y -> Ok { Min = x; Max = y }
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
        parseStatic path j |> Result.map (Some >> Binding.Static)
    | _ -> bindingGeneric<RangePair> path parseStatic { Min = 0.0; Max = 0.0 } j

let private decodeBindingStringPair (path: string) (j: Json) : Result<Binding<DateRangePair>, DecodeError> =
    // Phase 725 — the DateRange control's (from, to) ISO-8601 pair. Mirrors
    // `decodeBindingFloatPair`: the bare `{from, to}` object is canonical, a
    // two-element `[from, to]` array is the lenient coercion, and the
    // `Static`-enveloped form stays accepted through the generic dispatch.
    let ordered (p: string) (a: string, b: string) : Result<DateRangePair, DecodeError> =
        // Didactic domain rule: a LITERAL pair must be ordered. Same-variant
        // ISO-8601 strings sort lexicographically in chronological order, so an
        // ordinal compare is total here — no date parsing, no locale. Only a
        // literal pair is checked; a bound pair's ordering is a runtime concern.
        if String.CompareOrdinal(a, b) > 0 then
            err
                DecodeErrorCode.WRONG_TYPE
                p
                (sprintf
                    "date-range start '%s' is after end '%s' — a DateRange pair is ordered (from <= to); ISO-8601 strings of one variant compare lexicographically, so swap the two values"
                    a
                    b)
                (Some "ordered ISO-8601 pair ({\"from\": <iso>, \"to\": <iso>} with from <= to)")
        else
            Ok { From = a; To = b }

    let parseStatic (p: string) (v: Json) : Result<DateRangePair, DecodeError> =
        match v with
        | JObject pf ->
            match tryField pf "from", tryField pf "to" with
            | Some f, Some t ->
                match requireString (p + ".from") f, requireString (p + ".to") t with
                | Ok a, Ok b -> ordered p (a, b)
                | Error e, _
                | _, Error e -> Error e
            | _ -> wrongType p "object with from and to ISO-8601 strings"
        | JArray [ a; b ] ->
            match requireString (p + "[0]") a, requireString (p + "[1]") b with
            | Ok x, Ok y -> ordered p (x, y)
            | Error e, _
            | _, Error e -> Error e
        | _ -> wrongType p "date-range pair ({from, to} object or [from, to] array)"

    // The canonical Static pair rides as the BARE `{from, to}` object (the
    // Range precedent, no envelope) — accept it before the generic binding
    // dispatch, which would otherwise demand a `$type`.
    match j with
    | JObject pf when
        (tryField pf "$type").IsNone
        && (tryField pf "from").IsSome
        && (tryField pf "to").IsSome
        ->
        parseStatic path j |> Result.map (Some >> Binding.Static)
    | _ -> bindingGeneric<DateRangePair> path parseStatic { From = ""; To = "" } j

// fuaran#665 — the typed rows decoder: a rows payload is an array of row
// objects (each decoding to a `Row = Map<string, obj>` with `decodeObj` cell
// shapes), with the legacy `"<opaque>"` sentinel accepted indefinitely
// (read-compat → the empty feed, exactly the pre-typed behaviour). A non-object
// row element is a named decode error.
let private decodeRowSeq (path: string) (j: Json) : Result<Binding<Row seq>, DecodeError> =
    let parseRow (p: string) (v: Json) : Result<Row, DecodeError> =
        match v with
        | JObject fields -> Ok(fields |> Map.map (fun _ cell -> decodeObj cell))
        | _ -> wrongType p "row object"

    let parseStatic (p: string) (v: Json) : Result<Row seq, DecodeError> =
        match v with
        | JNull -> Ok Seq.empty // lenient shorthand for absence (rule 4 decode-accept)
        | JString s when s = opaqueSentinel -> Ok Seq.empty
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs ->
                xs
                |> traverseIndexed (fun i item -> parseRow (sprintf "%s[%d]" p i) item)
                |> Result.map Seq.ofList

    bindingGeneric<Row seq> path parseStatic Seq.empty j

let private decodeMapMarker (path: string) (j: Json) : Result<MapMarker, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let labelR =
            // A bare string since the swap; the Literal envelope stays
            // decode-accepted and projects to its text (same rule as the
            // SelectOption label).
            match requireField path fields "label" "marker label string" with
            | Error e -> Error e
            | Ok v ->
                decodeTextSource (path + ".label") v
                |> Result.bind (fun label ->
                    match label with
                    | TextSource.Literal s -> Ok s
                    | _ -> wrongType (path + ".label") "literal marker label")

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

let private decodeBindingMarkerSeq (path: string) (j: Json) : Result<Binding<MapMarker list>, DecodeError> =
    let parseStatic (p: string) (v: Json) : Result<MapMarker list, DecodeError> =
        match v with
        | JNull -> Ok [] // pre-429 read-compat: an empty-list-backed seq boxed to `null`
        | JString s when s = opaqueSentinel -> Ok []
        | _ ->
            match requireArray p v with
            | Error e -> Error e
            | Ok xs -> traverseIndexed (fun i m -> decodeMapMarker (sprintf "%s[%d]" p i) m) xs

    bindingGeneric<MapMarker list> path parseStatic [] j

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
                                        |> Result.map (fun k -> Some(CallResultTarget.State k))
                                    | Ok "Query" ->
                                        requireField (path + ".into") intoFields "name" "query name string"
                                        |> Result.bind (requireString (path + ".into.name"))
                                        |> Result.map (fun n -> Some(CallResultTarget.Query n))
                                    | Ok s -> unknownDuCase (path + ".into") s "State | Query"

                        intoR |> Result.map (fun into -> Action.Call(ep, onResult, into))
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
                // Phase 818 — `value` (a literal JSON value) XOR `valueFrom`
                // (a Binding evaluated at dispatch time inside the existing
                // gate). Exactly one must be present; both / neither error
                // didactically naming both fields.
                match requireField path fields "key" "state key string" with
                | Error e -> Error e
                | Ok kJ ->
                    match requireString (path + ".key") kJ with
                    | Error e -> Error e
                    | Ok key ->
                        match tryField fields "value", tryField fields "valueFrom" with
                        | Some _, Some _ ->
                            err
                                DecodeErrorCode.WRONG_TYPE
                                (path + ".valueFrom")
                                "SetState carries both 'value' and 'valueFrom' — exactly one is allowed"
                                (Some
                                    "either 'value' (a literal JSON value written verbatim) or 'valueFrom' (a Binding — State / Selection / Query / Transform — evaluated at dispatch time); remove one")
                        | None, None ->
                            missingField
                                path
                                "value"
                                "a literal JSON value under 'value', or a Binding under 'valueFrom' (evaluated at dispatch time)"
                        | Some vJ, None ->
                            decodeJVal (path + ".value") vJ
                            |> Result.map (fun v -> Action.SetState(key, Some v, None))
                        | None, Some bJ ->
                            decodeBindingJVal (path + ".valueFrom") bJ
                            |> Result.map (fun b -> Action.SetState(key, None, Some b))
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
                                // Positional since the swap: wire id + host-only
                                // handle (always None on a decoded tree) + the
                                // sentinel-restored onRead.
                                Action.ReadFileBody(fileId, None, encoding, Some(fun _ -> box closureSentinel)))
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
                            |> Result.map (fun args ->
                                Fuaran.UI.Types.Action.Invoke(
                                    capabilityId,
                                    args |> List.map (fun (addr, v) -> ({ Addr = addr; Value = v }: InvokeArg))
                                ))
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
            | Some v -> requireString (path + ".icon") v |> Result.map Some

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
            | Some v -> requireString (path + ".icon") v |> Result.map Some

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

        let protectionR =
            match tryField fields "protection" with
            | None -> Ok None
            | Some v ->
                requireString (path + ".protection") v
                |> Result.bind (function
                    | "email" -> Ok(Some LinkProtection.Email)
                    | other ->
                        Error(
                            DecodeError.create
                                DecodeErrorCode.UNKNOWN_DU_CASE
                                (path + ".protection")
                                ("unknown LinkProtection case: " + other)
                                (Some "\"email\"")
                        ))

        match hrefR, labelR, downloadR, relR, targetR, protectionR with
        | Ok href, Ok label, Ok download, Ok rel, Ok target, Ok protection ->
            Ok
                { Href = href
                  Label = label
                  Rel = rel
                  Target = target
                  Download = download
                  Protection = protection }
        | Error e, _, _, _, _, _
        | _, Error e, _, _, _, _
        | _, _, Error e, _, _, _
        | _, _, _, Error e, _, _
        | _, _, _, _, Error e, _
        | _, _, _, _, _, Error e -> Error e

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

// Phase 821 — the standalone icon-only display kind.
let private decodeIconSize (path: string) (j: Json) : Result<IconSize, DecodeError> =
    match j with
    | JString "Small" -> Ok IconSize.Small
    | JString "Medium" -> Ok IconSize.Medium
    | JString "Large" -> Ok IconSize.Large
    | JString s -> unknownDuCase path s "Small | Medium | Large"
    | _ -> wrongType path "JSON string (IconSize)"

let private decodeIconSpec (path: string) (j: Json) : Result<IconSpec, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let iconR =
            requireField path fields "icon" "icon name string"
            |> Result.bind (requireString (path + ".icon"))

        // `size` omitted-when-`Medium`; `tone` omitted-when-default (the
        // Phase 460 discipline); `label` omitted-when-decorative.
        let sizeR =
            match tryField fields "size" with
            | None -> Ok IconSize.Medium
            | Some v -> decodeIconSize (path + ".size") v

        let toneR =
            match tryField fields "tone" with
            | None -> Ok ToneVariant.Default
            | Some v -> decodeTone (path + ".tone") v

        let labelR =
            match tryField fields "label" with
            | None -> Ok None
            | Some v -> requireString (path + ".label") v |> Result.map Some

        match iconR, sizeR, toneR, labelR with
        | Ok icon, Ok size, Ok tone, Ok label ->
            Ok
                { Icon = icon
                  Size = size
                  Tone = tone
                  Label = label }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

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
            | Some v -> requireString (path + ".icon") v |> Result.map Some

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

let private decodeDisplayKind (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
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
                |> Result.map NodeKind.Heading
            | "Markdown" ->
                getSpec ()
                |> Result.bind (decodeMarkdownSpec specPath)
                |> Result.map NodeKind.Markdown
            | "Metric" ->
                getSpec ()
                |> Result.bind (decodeMetricSpec specPath)
                |> Result.map NodeKind.Metric
            | "Badge" ->
                getSpec ()
                |> Result.bind (decodeBadgeSpec specPath)
                |> Result.map NodeKind.Badge
            | "Sparkline" ->
                getSpec ()
                |> Result.bind (decodeSparklineSpec specPath)
                |> Result.map NodeKind.Sparkline
            | "Callout" ->
                getSpec ()
                |> Result.bind (decodeCalloutSpec specPath)
                |> Result.map NodeKind.Callout
            | "Progress" ->
                getSpec ()
                |> Result.bind (decodeProgressSpec specPath)
                |> Result.map NodeKind.Progress
            | "Skeleton" ->
                getSpec ()
                |> Result.bind (decodeSkeletonSpec specPath)
                |> Result.map NodeKind.Skeleton
            | "Icon" -> getSpec () |> Result.bind (decodeIconSpec specPath) |> Result.map NodeKind.Icon
            | "LabelValueRow" ->
                getSpec ()
                |> Result.bind (decodeLabelValueRowSpec specPath)
                |> Result.map NodeKind.LabelValueRow
            | "Fact" -> getSpec () |> Result.bind (decodeFactSpec specPath) |> Result.map NodeKind.Fact
            | "Link" -> getSpec () |> Result.bind (decodeLinkSpec specPath) |> Result.map NodeKind.Link
            | "Image" ->
                getSpec ()
                |> Result.bind (decodeImageSpec specPath)
                |> Result.map NodeKind.Image
            | "List" -> getSpec () |> Result.bind (decodeListSpec specPath) |> Result.map NodeKind.List
            | "Toast" ->
                getSpec ()
                |> Result.bind (decodeToastSpec specPath)
                |> Result.map NodeKind.Toast
            | "CodeBlock" ->
                getSpec ()
                |> Result.bind (decodeCodeBlockSpec specPath)
                |> Result.map NodeKind.CodeBlock
            | "Math" -> getSpec () |> Result.bind (decodeMathSpec specPath) |> Result.map NodeKind.Math
            | "Drawing" ->
                getSpec ()
                |> Result.bind (decodeDrawingSpec specPath)
                |> Result.map NodeKind.Drawing
            | s -> unknownDuCase path s (String.concat " | " displayNodeKinds)

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

        // Value slot: present ⇒ typed decode; absent ⇒ `None` in an auto-bind
        // context (Phase 596) — else MISSING_FIELD.
        //
        // Phase 694: absence stays STRUCTURAL. Decode no longer synthesises the
        // auto-binding into the tree — `None` IS the canonical decoded shape
        // (matching the generated decoder), and the renderers substitute the
        // exact synthesised binding at render time (the stage-3 mirror). An
        // EXPLICIT value decodes as written — the §16 collapse to the omitted
        // canonical form happens at encode time (`Introspect.canonicalForm`,
        // where the retired hand encoder used to perform it), keeping the hand
        // and generated decoders tree-convergent. `autoBind` still gates
        // required-vs-optional: a NoAutoBind (erased/unknown) context keeps the
        // pre-596 contract.
        let valueOr
            (dec: string -> Json -> Result<Binding<'v>, DecodeError>)
            (_autoDefault: 'v option)
            (expected: string)
            : Result<Binding<'v> option, DecodeError> =
            match tryField fields "value" with
            | Some v -> dec (path + ".value") v |> Result.map Some
            | None ->
                match autoBind with
                | FilterChip _
                | FormFieldId _ -> Ok None
                | NoAutoBind -> missingField path "value" expected

        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Text" ->
            valueOr decodeBindingString (Some Fuaran.UI.Defaults.ControlValueDefaults.text) "Binding<string> value"
            |> Result.map (fun value -> FormFieldKind.Text(value, handlerOpt "onChange"))
        | Ok "Number" ->
            valueOr decodeBindingFloat (Some Fuaran.UI.Defaults.ControlValueDefaults.number) "Binding<float> value"
            |> Result.map (fun value -> FormFieldKind.Number(value, handlerOpt "onChange"))
        | Ok "Checkbox" ->
            valueOr decodeBindingBool (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) "Binding<bool> value"
            |> Result.map (fun value -> FormFieldKind.Checkbox(value, handlerOpt "onToggle"))
        // Phase 766 — same payload as Checkbox; the DIFFERENCE is presentation
        // and the a11y contract (role="switch"), not the data.
        | Ok "Toggle" ->
            valueOr decodeBindingBool (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) "Binding<bool> value"
            |> Result.map (fun value -> FormFieldKind.Toggle(value, handlerOpt "onToggle"))
        | Ok "Choice" ->
            let optionsR =
                requireField path fields "options" "Binding<SelectOption list>"
                |> Result.bind (decodeBindingSelectOptions (path + ".options"))

            let valueR =
                // The choice value is `Binding<string>` since the swap ("no
                // selection" is the absent Static payload); the auto-bind
                // synthesis is a default-less State (choice = None).
                valueOr decodeBindingChoiceValue Fuaran.UI.Defaults.ControlValueDefaults.choice "Binding<string> value"

            match optionsR, valueR with
            | Ok options, Ok value -> Ok(FormFieldKind.Choice(options, value, handlerOpt "onChange"))
            | Error e, _
            | _, Error e -> Error e
        | Ok "Range" ->
            // 0.2.0 — dual-thumb numeric range (absorbed FilterKind.RangeFilter).
            let valueR =
                // Phase 694 — absence stays structural (`None`); see `valueOr`.
                match tryField fields "value" with
                | Some v -> decodeBindingRangePair (path + ".value") v |> Result.map Some
                | None ->
                    match autoBind with
                    | FilterChip _
                    | FormFieldId _ -> Ok None
                    | NoAutoBind -> missingField path "value" "Binding<RangePair> value"

            valueR
            |> Result.map (fun value -> FormFieldKind.Range(value, handlerOpt "onChange", None, None, None))
        | Ok "RangedNumber" ->
            // Parallel-additive Number case carrying optional
            // Min / Max / Step bounds at the field level. Absent keys
            // decode as `None` (mirrors the encoder's omit-when-None
            // discipline). Wire shape:
            //   { "$type": "RangedNumber", "value": <Binding>, "onChange":
            //     "<closure>", "min": <float|absent>, "max": <float|absent>,
            //     "step": <float|absent> }
            let valueR =
                valueOr decodeBindingFloat (Some Fuaran.UI.Defaults.ControlValueDefaults.number) "Binding<float> value"

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
                Ok(FormFieldKind.RangedNumber(value, handlerOpt "onChange", min, max, step))
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e
        | Ok "TextArea" ->
            let valueR =
                valueOr decodeBindingString (Some Fuaran.UI.Defaults.ControlValueDefaults.text) "Binding<string> value"

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
                // The choice value is `Binding<string>` since the swap ("no
                // selection" is the absent Static payload); the auto-bind
                // synthesis is a default-less State (choice = None).
                valueOr decodeBindingChoiceValue Fuaran.UI.Defaults.ControlValueDefaults.choice "Binding<string> value"

            let orientationR =
                // Lenient AI-ingest omitted-when-default (WIRE_FORMAT.md §3.6
                // family): absent `orientation` restores the language default
                // `Horizontal` (Defaults.fs) — the universal segmented-control
                // prior; observed omitted in eval emission data. Decode-only:
                // the encoder still always emits it.
                match tryField fields "orientation" with
                | None -> Ok Orientation.Horizontal
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
                valueOr decodeBindingString (Some Fuaran.UI.Defaults.ControlValueDefaults.date) "Binding<string> value"

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
                Ok(FormFieldKind.Date(value, handlerOpt "onChange", variant, min, max, step))
            | Error e, _, _, _, _
            | _, Error e, _, _, _
            | _, _, Error e, _, _
            | _, _, _, Error e, _
            | _, _, _, _, Error e -> Error e
        | Ok "DateRange" ->
            // Phase 725 — single-control date range. `value` is a
            // `Binding<string * string>` carrying the ordered ISO-8601 pair
            // (canonically the bare `{from, to}` object); `variant` selects the
            // control for both ends; optional min/max (ISO strings) + step
            // (seconds) bound both ends and decode to None when absent.
            // Phase 694 — absence stays structural (`None`); the ONE filter
            // param that carries the whole pair (the case's reason to exist)
            // is synthesised at render time, never here. See `valueOr`.
            let valueR =
                valueOr
                    decodeBindingStringPair
                    (Some Fuaran.UI.Defaults.ControlValueDefaults.dateRange)
                    "Binding<DateRangePair> value"

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
                Ok(FormFieldKind.DateRange(value, handlerOpt "onChange", variant, min, max, step))
            | Error e, _, _, _, _
            | _, Error e, _, _, _
            | _, _, Error e, _, _
            | _, _, _, Error e, _
            | _, _, _, _, Error e -> Error e
        | Ok s -> unknownDuCase path s wrongFormFieldKindHint

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
                  Kind = field }
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
            | Some v -> requireString (path + ".icon") v |> Result.map Some

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
            requireField path fields "value" "Binding<string>"
            |> Result.bind (decodeBindingChoiceValue (path + ".value"))

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
            | None -> Ok Option.None
            | Some v -> requireBool (path + ".multiple") v |> Result.map Some

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
                  OnSelect =
                    (match tryField fields "onSelect" with
                     | Some _ -> Some(fun _ -> Action.Chain [])
                     | None -> Option.None)
                  Disabled = disabled }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

let private decodeInputKind (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Form" -> decodeFormSpec path j |> Result.map NodeKind.Form
        | Ok "Filters" ->
            requireField path fields "items" "FilterSpec list"
            |> Result.bind (fun v -> requireArray (path + ".items") v)
            |> Result.bind (fun xs ->
                traverseIndexed (fun i item -> decodeFilterSpec (sprintf "%s.items[%d]" path i) item) xs)
            |> Result.map (fun items -> NodeKind.Filters { Items = items })
        | Ok "Button" -> decodeButtonSpec path j |> Result.map NodeKind.Button
        | Ok "FileUpload" -> decodeFileUploadSpec path j |> Result.map NodeKind.FileUpload
        | Ok "Select" -> decodeSelectSpec path j |> Result.map NodeKind.Select
        | Ok s -> unknownDuCase path s (String.concat " | " inputNodeKinds)

// ─── Visualisation ──────────────────────────────────────────────────────

/// Phase 750 — a `TonedPill`'s `map`: a string-keyed object whose VALUES are
/// `ToneVariant`s. Routed through `decodeTone` per entry, which buys two things
/// deliberately rather than by accident: the Phase 460 tone aliases work inside the
/// map exactly as they do in a `tone` field, and an unrecognised value's message
/// enumerates the seven legal tone names. The path names the offending KEY
/// (`…map.Delayed`), because "one of your tones is wrong" is not an actionable
/// report when the map has nine entries.
let private decodeToneMap (path: string) (j: Json) : Result<Map<string, ToneVariant>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        // `decodeTone` is reused for the VALUE vocabulary + the Phase 460 aliases,
        // but its refusal is re-issued here rather than passed through: it reports at
        // `<path>.$type` with "unknown discriminator", and a map value is neither a
        // discriminator nor at a `$type` key, so the raw error points at a path the
        // document does not contain. The re-issue keeps the code and the legal-name
        // list, and names the offending key and value in the terms the author wrote
        // them — which is the whole reason this fixture is in the corpus.
        let entryTone (key: string) (v: Json) : Result<string * ToneVariant, DecodeError> =
            let entryPath = path + "." + key

            match decodeTone entryPath v with
            | Ok t -> Ok(key, t)
            | Error e when e.Code = DecodeErrorCode.toString DecodeErrorCode.UNKNOWN_DU_CASE ->
                let got =
                    match v with
                    | JString s -> s
                    | _ -> ""

                err
                    DecodeErrorCode.UNKNOWN_DU_CASE
                    entryPath
                    (sprintf "tone-map value '%s' for '%s' is not a ToneVariant" got key)
                    (Some toneVariantNames)
            // A non-string value (a number, an object) is a WRONG_TYPE from
            // `decodeTone` and already reports at the right path.
            | Error e -> Error e

        fields
        |> Map.toList
        |> traverse (fun (k, v) -> entryTone k v)
        |> Result.map Map.ofList

/// The shared body of the canonical `TonedPill` case and the `Pill`-tagged §16
/// shorthand below — one reader, so the two spellings cannot drift apart in what
/// they accept.
let private decodeTonedPill (path: string) (fields: Map<string, Json>) : Result<CellKindErased<obj>, DecodeError> =
    let fieldR =
        requireField path fields "field" "TonedPill row-field name (drives the label and the map key)"
        |> Result.bind (requireString (path + ".field"))

    let mapR =
        // Field aliases: `toneMap` / `tones` — `map` is the shortest honest name for
        // a value→tone dictionary and the least descriptive one.
        requireFieldAliased path fields "map" [ "toneMap"; "tones" ] "TonedPill value→ToneVariant map"
        |> Result.bind (decodeToneMap (path + ".map"))

    // `default` is omitted-when-`ToneVariant.Default` (Phase 460); restore the
    // identity on absence.
    let defaultR =
        match tryField fields "default" with
        | None -> Ok ToneVariant.Default
        | Some v -> decodeTone (path + ".default") v

    match fieldR, mapR, defaultR with
    | Ok field, Ok toneMap, Ok defaultTone -> Ok(CellKindErased.TonedPill(field, toneMap, defaultTone))
    | Error e, _, _
    | _, Error e, _
    | _, _, Error e -> Error e

let private decodeCellKindErased
    (columnField: string option)
    (path: string)
    (j: Json)
    : Result<CellKindErased<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Text" -> Ok CellKindErased.Text
        | Ok "Numeric" -> Ok CellKindErased.Numeric
        | Ok "Date" -> Ok CellKindErased.Date
        // The interactive closure slots are OPTIONS since the swap — decode
        // maps wire presence (the `"<closure>"` sentinel) to `Some` no-op so
        // re-encode stays byte-identical, and absence to `None`.
        | Ok "Editable" ->
            Ok(CellKindErased.Editable(tryField fields "onEdit" |> Option.map (fun _ -> fun _ -> Action.Chain [])))
        | Ok "Checkbox" ->
            Ok(
                CellKindErased.Checkbox(
                    (fun _ -> false),
                    tryField fields "onToggle" |> Option.map (fun _ -> fun _ -> Action.Chain [])
                )
            )
        | Ok "Button" ->
            requireField path fields "label" "button TextSource label"
            |> Result.bind (decodeTextSource (path + ".label"))
            |> Result.map (fun label ->
                CellKindErased.Button(
                    label,
                    tryField fields "onClick" |> Option.map (fun _ -> fun _ -> Action.Chain [])
                ))
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
                            |> Result.map (fun label ->
                                { Label = label
                                  OnClick =
                                    tryField bf "onClick" |> Option.map (fun _ -> fun (_: Row) -> Action.Chain []) }
                                : ButtonGroupItem<obj>))
                    xs)
            |> Result.map CellKindErased.ButtonGroup
        | Ok "Link" ->
            Ok(CellKindErased.Link((fun _ -> closureSentinel), (fun _ -> TextSource.Literal closureSentinel)))
        // Lenient-ingest (WIRE_FORMAT.md §16, Phase 750): `Pill` is the WORD for the
        // thing, so a declarative tone rule arrives tagged `Pill` more often than
        // tagged `TonedPill`. Before this phase that document decoded as a closure
        // pill and threw `field`/`map` on the floor — the author's whole intent gone,
        // silently, with no error to notice. Presence of a tone map is the
        // unambiguous tell (a closure `Pill` has no such key), so route it.
        | Ok "Pill" when
            [ "map"; "toneMap"; "tones" ]
            |> List.exists (fun k -> (tryField fields k).IsSome)
            ->
            decodeTonedPill path fields
        | Ok "Pill" ->
            Ok(CellKindErased.Pill((fun _ -> TextSource.Literal closureSentinel), (fun _ -> ToneVariant.Default)))
        | Ok "TonedPill" -> decodeTonedPill path fields
        | Ok "Progress" ->
            // Phase 425 / cat:Fuaran.UI.ProgressFieldCell (2026-08-10) — the
            // field-driven fraction. A decoded `Progress` cell in a column that
            // carries `field` derives its per-row fill from that row property
            // (clamped to 0..1; missing / non-numeric → 0, never a throw),
            // ending the silent zero-fill class: before this, EVERY wire
            // `Progress` cell rendered an empty bar regardless of the data —
            // rubric-demanded + repair-proof evidence in the eval suite's
            // 627-repair-escalation-20260809.md. No new wire key: the driver
            // is the column-level `field` the Phase 425 core shipped. A
            // columnless (node-context) or fieldless `Progress` keeps the
            // inert placeholder; native-host closures never pass through
            // decode, so closure-authored grids are untouched.
            let fraction: Row -> float =
                match columnField with
                | Some field ->
                    fun row ->
                        match Map.tryFind field row with
                        | Some v ->
                            let f =
                                match v with
                                | :? float as x -> x
                                | :? int as i -> float i
                                | _ -> 0.0

                            max 0.0 (min 1.0 f)
                        | None -> 0.0
                | None -> fun _ -> 0.0

            Ok(
                CellKindErased.Progress(
                    fraction,
                    tryField fields "labelFn"
                    |> Option.map (fun _ -> fun _ -> TextSource.Literal closureSentinel)
                )
            )
        | Ok "Custom" -> Ok(CellKindErased.Custom(fun _ -> placeholderClosureNode))
        | Ok s ->
            unknownDuCase
                path
                s
                "Text | Numeric | Date | Editable | Checkbox | Button | ButtonGroup | Link | Pill | TonedPill | Progress | Custom"

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

        let fieldR =
            match tryField fields "field" with
            | None -> Ok None
            | Some fJ -> requireString (path + ".field") fJ |> Result.map Some

        let kindR =
            // Field alias: type — the universal JSON prior for a column's kind.
            // The column-level `field` rides into the kind decode so field-driven
            // cell kinds (Phase 425 / cat:Fuaran.UI.ProgressFieldCell — `Progress`
            // first) can synthesize their row projection at decode time.
            requireFieldAliased path fields "kind" [ "type" ] "CellKindErased"
            |> Result.bind (fun kJ ->
                let columnField =
                    match fieldR with
                    | Ok f -> f
                    | Error _ -> None

                decodeCellKindErased columnField (path + ".kind") kJ)

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
            | Some _ -> Some(fun (_: Row) -> CellValue.Empty)
            | None -> None

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

/// Phase 801 — `"asc"` / `"desc"`, closed. A value outside the pair is an
/// `UNKNOWN_DU_CASE` (the bare-string-enum convention `decodeLiveRegion` sets),
/// never a silent fallback to ascending: an unrecognised direction is an emitter
/// defect the author can fix, and quietly picking one hides it.
let private decodeSortDirection (path: string) (j: Json) : Result<SortDirection, DecodeError> =
    match j with
    | JString "asc" -> Ok SortDirection.Asc
    | JString "desc" -> Ok SortDirection.Desc
    | JString s -> unknownDuCase path s "asc | desc"
    | _ -> wrongType path "JSON string (SortDirection)"

/// Phase 801 — the `{column, direction}` initial-order declaration. `column` is a
/// NON-NEGATIVE index into `headers`; a negative (or non-numeric) index is a
/// `WRONG_TYPE`, which is also what the published schema's `minimum: 0` says, so
/// the two expressions of the contract agree.
///
/// An index PAST the end of `headers` is deliberately NOT rejected here: the
/// decoder validates one object at a time and the header list is a sibling field,
/// so the cross-field check belongs to the pre-emit validator, not the codec. A
/// host that cannot resolve the column renders the authored order.
let private decodeDefaultSort (path: string) (j: Json) : Result<DefaultSort, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let columnR =
            match requireField path fields "column" "non-negative header index" with
            | Error e -> Error e
            | Ok v ->
                match v with
                | JNumber n when n >= 0.0 && n = floor n -> Ok(int n)
                | _ -> wrongType (path + ".column") "JSON number (non-negative integer header index)"

        let directionR =
            match requireField path fields "direction" "asc | desc" with
            | Error e -> Error e
            | Ok v -> decodeSortDirection (path + ".direction") v

        match columnR, directionR with
        | Ok column, Ok direction ->
            Ok
                { Column = column
                  Direction = direction }
        | Error e, _
        | _, Error e -> Error e

/// Phase 393 — decode the `{headers, rows}` static-rows object of a read-only grid
/// (also the shape the legacy `Table` decode-upgrade reads). Cells are `TextSource`.
///
/// Phase 801 — plus the two optional sort-intent slots. Both absent ⇒ the returned
/// record is the pre-801 shape and re-encodes byte-identically.
let private decodeStaticRows (path: string) (j: Json) : Result<StaticRows, DecodeError> =
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

        let sortableR =
            match tryField fields "sortable" with
            | None -> Ok None
            | Some v -> requireBool (path + ".sortable") v |> Result.map Some

        let defaultSortR =
            match tryField fields "defaultSort" with
            | None -> Ok None
            | Some v -> decodeDefaultSort (path + ".defaultSort") v |> Result.map Some

        match headersR, rowsR, sortableR, defaultSortR with
        | Ok headers, Ok rows, Ok sortable, Ok defaultSort ->
            Ok
                { DefaultSort = defaultSort
                  Headers = headers
                  Rows = rows
                  Sortable = sortable }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e

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
            requireFieldAliased path fields "source" [ "data"; "rows" ] "Binding<Row seq> Source"
            |> Result.bind (decodeRowSeq (path + ".source"))

        let onRowClickR: Result<(Row -> Action<obj>) option, DecodeError> =
            match tryField fields "onRowClick" with
            | None -> Ok None
            | Some _ -> Ok(Some(fun _ -> Action.Chain []))

        // Phase 425 — `rowKey` (closure) + `rowKeyField` (declarative) are sibling optional slots.
        let rowKey =
            match tryField fields "rowKey" with
            | Some _ -> Some(fun (_: Row) -> closureSentinel)
            | None -> None

        let rowKeyFieldR =
            match tryField fields "rowKeyField" with
            | None -> Ok None
            | Some fJ -> requireString (path + ".rowKeyField") fJ |> Result.map Some

        // Phase 818 — the grid-sort header affordance: `sortStateKey` names the
        // State key carrying the sort descriptor `{column, direction}` a
        // data-bound grid's runtime sorts by (and whose headers write it).
        let sortStateKeyR =
            match tryField fields "sortStateKey" with
            | None -> Ok None
            | Some fJ -> requireString (path + ".sortStateKey") fJ |> Result.map Some

        // Phase 393 — the static read-only mode. `staticRows` (optional, omitted for a
        // data-bound grid so existing fixtures stay byte-identical) carries the retired
        // `Table`'s `TextSource` header/row matrix; when present the renderer emits static
        // `<table>` markup from it. `decodeStaticRows` reads the `{headers, rows}` object.
        let staticRowsR: Result<StaticRows option, DecodeError> =
            match tryField fields "staticRows" with
            | None -> Ok None
            | Some sJ -> decodeStaticRows (path + ".staticRows") sJ |> Result.map Some

        match columnsR, editableR, sourceR, onRowClickR, rowKeyFieldR, sortStateKeyR, staticRowsR with
        | Ok columns, Ok editable, Ok source, Ok onRowClick, Ok rowKeyField, Ok sortStateKey, Ok staticRows ->
            Ok
                { Source = source
                  RowKey = rowKey
                  RowKeyField = rowKeyField
                  SortStateKey = sortStateKey
                  Columns = columns
                  OnRowClick = onRowClick
                  Editable = editable
                  StaticRows = staticRows }
        | Error e, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _
        | _, _, Error e, _, _, _, _
        | _, _, _, Error e, _, _, _
        | _, _, _, _, Error e, _, _
        | _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, Error e -> Error e

let private decodeChartSpec (path: string) (j: Json) : Result<ChartSpec<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let kindR =
            requireField path fields "kind" "ChartKind"
            |> Result.bind (decodeChartKind (path + ".kind"))

        let sourceR =
            requireFieldAliased path fields "source" [ "data" ] "Binding<Row seq> source"
            |> Result.bind (decodeRowSeq (path + ".source"))

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

        let onPointClickR: Result<(Row -> Action<obj>) option, DecodeError> =
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

let private decodeVisKind (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "DataGrid" -> decodeGridSpec path j |> Result.map NodeKind.DataGrid
        | Ok "Chart" -> decodeChartSpec path j |> Result.map NodeKind.Chart
        | Ok "Map" -> decodeMapSpec path j |> Result.map NodeKind.Map
        | Ok s -> unknownDuCase path s (String.concat " | " visNodeKinds)

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
/// The typed `Scalar` twin (a `HoleDecl.Value` default since the swap).
let private decodeScalarTyped (path: string) (j: Json) : Result<Scalar, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        | Ok "Int" ->
            requireField path fields "value" "Int value"
            |> Result.bind (requireInt (path + ".value"))
            |> Result.map Scalar.Int
        | Ok "Float" ->
            requireField path fields "value" "Float value"
            |> Result.bind (requireFloat (path + ".value"))
            |> Result.map Scalar.Float
        | Ok "Bool" ->
            requireField path fields "value" "Bool value"
            |> Result.bind (requireBool (path + ".value"))
            |> Result.map Scalar.Bool
        | Ok "Str" ->
            requireField path fields "value" "Str value"
            |> Result.bind (requireString (path + ".value"))
            |> Result.map Scalar.Str
        | Ok s -> unknownDuCase path s "Int | Float | Bool | Str"

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
                    | Some d -> decodeScalarTyped (path + ".default") d |> Result.map Some

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

/// The structural walk's budget state (Phase 781): this node's own nesting
/// level, plus the number of nodes decoded so far across the WHOLE document.
///
/// One value rather than two parameters because the structural decode threads it
/// through some twenty-five recursion sites, and `Depth` is per-position while
/// `Nodes` is per-document — `descend` copies the record (advancing the level)
/// but the `ref` cell inside is shared by reference, so every position sees the
/// same running total. A plain mutable field would NOT work here: `{ w with … }`
/// copies it, and each branch would then count only its own subtree.
type private Walk = { Depth: int; Nodes: int ref }

/// A fresh budget for one document. Depth 1 is the root node.
let private walkRoot () : Walk = { Depth = 1; Nodes = ref 0 }

/// One level further down, same document-wide node budget.
let private descend (w: Walk) : Walk = { w with Depth = w.Depth + 1 }

let rec private decodeChildren
    (w: Walk)
    (path: string)
    (fields: Map<string, Json>)
    : Result<Node<obj> list, DecodeError> =
    match requireField path fields "children" "children Node list" with
    | Error e -> Error e
    | Ok v ->
        match requireArray (path + ".children") v with
        | Error e -> Error e
        | Ok xs -> traverseIndexed (fun i item -> decodeNodeAst (descend w) (sprintf "%s.children[%d]" path i) item) xs

and private decodeLayoutKind (w: Walk) (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
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
                    let childrenR = decodeChildren w specPath specFields

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
                                    | Ok d, Ok w, Ok g -> Ok(LayoutMode.Flex(d, w, g))
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
                                    | Ok c, Ok tc, Ok g -> Ok(LayoutMode.Grid(c, tc, g))
                                    | Error e, _, _
                                    | _, Error e, _
                                    | _, _, Error e -> Error e
                                | "Auto" -> Ok BoxLayout.Auto
                                | other -> unknownDuCase lpath other "Flex | Grid | Auto"))

                    match childrenR, headingR, roleR, layoutR with
                    | Ok children, Ok heading, Ok role, Ok layout ->
                        Ok(
                            NodeKind.Box
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
                    let childrenR = decodeChildren w specPath specFields

                    let weightR =
                        requireField specPath specFields "weight" "weight float"
                        |> Result.bind (requireFloat (specPath + ".weight"))

                    match childrenR, weightR with
                    | Ok children, Ok weight -> Ok(NodeKind.SplitPanel { Weight = weight; Children = children })
                    | Error e, _
                    | _, Error e -> Error e
            | "Tabs" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren w specPath specFields

                    let orientationR =
                        // 0.2.0 — omitted-when-Horizontal on both boundaries.
                        match tryField specFields "orientation" with
                        | None -> Ok Orientation.Horizontal
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
                                        // Bare string since the swap (the IconSource wrapper
                                        // unwraps at this boundary).
                                        match tryField hFields "icon" with
                                        | None -> Ok Option.None
                                        | Some v ->
                                            decodeIconSource (path + ".icon") v
                                            |> Result.map (fun (IconSource s) -> Some s)

                                    let disabledR =
                                        match tryField hFields "disabled" with
                                        | None -> Ok Option.None
                                        | Some v -> decodeBindingBool (path + ".disabled") v |> Result.map Some

                                    match iconR, disabledR with
                                    | Ok icon, Ok disabled ->
                                        Ok(
                                            { Label = label
                                              Icon = icon
                                              Disabled = disabled }
                                            : TabHeader
                                        )
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
                        | None -> Ok(Binding.Static(Some 0))
                        | Some v -> decodeBindingInt (specPath + ".activeIndex") v

                    match childrenR, orientationR, tabHeadersR, tabTagsR, activeTagR, activeIndexR with
                    | Ok children, Ok orientation, Ok tabHeaders, Ok tabTags, Ok activeTag, Ok activeIndex ->
                        Ok(
                            NodeKind.Tabs
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
                    let childrenR = decodeChildren w specPath specFields

                    let activeR =
                        requireField specPath specFields "activeStep" "Binding<int> activeStep"
                        |> Result.bind (decodeBindingInt (specPath + ".activeStep"))

                    // `onSelect` is a closure → a present sentinel reconstructs
                    // a no-op `Some` (re-encodes to the same sentinel; behaviour
                    // can't round-trip); an absent key decodes `None` — mirrors
                    // Tabs. The slot is an option since the swap.
                    match childrenR, activeR with
                    | Ok children, Ok active ->
                        Ok(
                            NodeKind.Stepper
                                { ActiveStep = active
                                  Children = children
                                  OnSelect =
                                    (match tryField specFields "onSelect" with
                                     | Some _ -> Some(fun _ -> Action.Chain [])
                                     | None -> Option.None) }
                        )
                    | Error e, _
                    | _, Error e -> Error e
            | "SummaryList" ->
                match getSpecFields () with
                | Error e -> Error e
                | Ok specFields ->
                    let childrenR = decodeChildren w specPath specFields

                    let headingR =
                        match optFieldAliased specFields "heading" [ "title" ] with
                        | None -> Ok None
                        | Some v -> decodeTextSource (specPath + ".heading") v |> Result.map Some

                    match childrenR, headingR with
                    | Ok children, Ok heading ->
                        Ok(
                            NodeKind.SummaryList
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
                    let childrenR = decodeChildren w specPath specFields

                    let headingR =
                        requireFieldAliased specPath specFields "heading" [ "title" ] "TextSource heading"
                        |> Result.bind (decodeTextSource (specPath + ".heading"))

                    let openR =
                        match tryField specFields "open" with
                        | None -> Ok(Binding.Static(Some false))
                        | Some v -> decodeBindingBool (specPath + ".open") v

                    let defaultOpenR =
                        match tryField specFields "defaultOpen" with
                        | None -> Ok false
                        | Some v -> requireBool (specPath + ".defaultOpen") v

                    match childrenR, headingR, openR, defaultOpenR with
                    | Ok children, Ok heading, Ok openB, Ok defOpen ->
                        Ok(
                            NodeKind.Disclosure
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
                    let childrenR = decodeChildren w specPath specFields

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
                            NodeKind.Modal
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
                    let childrenR = decodeChildren w specPath specFields

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
                            NodeKind.ScrollArea
                                { Orientation = orientation
                                  Children = children
                                  MaxHeight = maxHeight
                                  MaxWidth = maxWidth }
                        )
                    | Error e, _, _, _
                    | _, Error e, _, _
                    | _, _, Error e, _
                    | _, _, _, Error e -> Error e
            | s -> unknownDuCase path s (String.concat " | " layoutNodeKinds)

and private decodeNodeKind (w: Walk) (path: string) (j: Json) : Result<NodeKind<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        match requireDiscriminator path fields with
        | Error e -> Error e
        // The four behavioural categories are flat on the wire (WIRE_FORMAT
        // §3.2): the `kind` object carries the primitive discriminator
        // directly, so we route each primitive to its inner decoder and
        // recover the category here. The name-sets are the module-level
        // `layoutNodeKinds` / `displayNodeKinds` / `inputNodeKinds` /
        // `visNodeKinds` — the SAME lists `wrongNodeKindHint` is projected
        // from, so dispatch and hint cannot drift apart. They MUST stay in
        // sync with the four inner decoders, the encoder, and SchemaGen — the
        // §11 forward-coupling surface. An unrecognised discriminator falls
        // through to WRONG_NODE_KIND below.
        | Ok s when List.contains s layoutNodeKinds ->
            // Phase 692 — the family decoders now build flat NodeKind cases
            // directly; the category wrappers they used to re-wrap into are gone.
            decodeLayoutKind w path j
        | Ok s when List.contains s displayNodeKinds -> decodeDisplayKind path j
        | Ok s when List.contains s inputNodeKinds -> decodeInputKind path j
        | Ok s when List.contains s visNodeKinds -> decodeVisKind path j
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
                                Some(
                                    { Algorithm = alg
                                      Hash = h
                                      Strictness = strict }
                                    : ContentHash
                                )
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
                        |> List.mapi (fun i item -> requireString (sprintf "%s.exposedNodeIds[%d]" path i) item)
                        |> List.fold
                            (fun acc r ->
                                match acc, r with
                                | Ok xs, Ok v -> Ok(xs @ [ v ])
                                | Error e, _ -> Error e
                                | _, Error e -> Error e)
                            (Ok []))

            match moduleIdR, componentIdR, propsR, contentHashR, exposedNodeIdsR with
            | Ok moduleId, Ok componentId, Ok props, Ok hash, Ok exposedIds ->
                Ok(
                    NodeKind.Custom
                        { ModuleId = moduleId
                          ComponentId = componentId
                          Props = props
                          ContentHash = hash
                          // An absent/empty list stays `None` (omitted on the wire).
                          ExposedNodeIds = (if List.isEmpty exposedIds then None else Some exposedIds) }
                )
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
                |> Result.bind (decodeNodeAst (descend w) (path + ".child"))

            let fallbackR =
                requireField path fields "fallback" "ErrorBoundary fallback Node"
                |> Result.bind (decodeNodeAst (descend w) (path + ".fallback"))

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
            // Phase 768 — the selector is `on` (any Binding) or the compact
            // `stateKey` (the State form's canonical spelling). Both absent
            // keeps the stateKey MISSING_FIELD, so the existing reject
            // fixture's error is byte-identical.
            let stateKeyR =
                match tryField fields "on" with
                | Some onJ -> decodeBindingString (path + ".on") onJ
                | None ->
                    requireField path fields "stateKey" "Switch stateKey string"
                    |> Result.bind (requireString (path + ".stateKey"))
                    |> Result.map (fun key -> Binding.State(key, None))

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
                                    |> Result.bind (decodeNodeAst (descend w) (casePath + ".child"))

                                match matchR, childR with
                                | Ok m, Ok child -> Ok({ Match = m; Child = child }: SwitchCase<obj>)
                                | Error e, _
                                | _, Error e -> Error e)

            let defaultR =
                requireField path fields "default" "Switch default Node"
                |> Result.bind (decodeNodeAst (descend w) (path + ".default"))

            match stateKeyR, casesR, defaultR with
            | Ok on, Ok cases, Ok defaultNode ->
                Ok(
                    NodeKind.Switch
                        { On = on
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

            let bodyR =
                requireField path fields "body" "FragmentDecl body Node"
                |> Result.bind (decodeNodeAst (descend w) (path + ".body"))

            // Phase 180 — `holes` + `effect` are additive; absent ⇒ degenerate
            // fixed-body (zero holes, pure-deterministic — `None` since the
            // swap, so the omitted wire form round-trips as omission).
            let holesR =
                match tryField fields "holes" with
                | None -> Ok None
                | Some h ->
                    requireArray (path + ".holes") h
                    |> Result.bind (traverse (decodeHoleDecl (path + ".holes[]")))
                    |> Result.map Some

            let effectR =
                match tryField fields "effect" with
                | None -> Ok None
                | Some e -> decodeEffectClass (path + ".effect") e |> Result.map Some

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

            let argR (argPath: string) (j: Json) : Result<FragmentArg<obj>, DecodeError> =
                match requireObject argPath j with
                | Error e -> Error e
                | Ok argFields ->
                    match requireDiscriminator argPath argFields with
                    | Error e -> Error e
                    | Ok "SlotArg" ->
                        requireField argPath argFields "tree" "SlotArg tree Node"
                        |> Result.bind (decodeNodeAst (descend w) (argPath + ".tree"))
                        |> Result.map FragmentArg.SlotArg
                    | Ok _ ->
                        // Int | Float | Bool | Str — a value argument (the
                        // FragmentArg cases are typed since the swap).
                        decodeScalarTyped argPath j
                        |> Result.map (fun s ->
                            match s with
                            | Scalar.Int v -> FragmentArg.Int v
                            | Scalar.Float v -> FragmentArg.Float v
                            | Scalar.Bool v -> FragmentArg.Bool v
                            | Scalar.Str v -> FragmentArg.Str v)

            let argsR =
                match tryField fields "args" with
                | None -> Ok None
                | Some a ->
                    match requireObject (path + ".args") a with
                    | Error e -> Error e
                    | Ok argFields ->
                        argFields
                        |> Map.toList
                        |> traverse (fun (k, v) ->
                            argR (path + ".args." + k) v |> Result.map (fun decoded -> k, decoded))
                        |> Result.map (Map.ofList >> Some)

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
                            Ok(
                                { Direction = direction
                                  MessageShape = messageShape }
                                : GuestChannel
                            )
                        | Error e, _
                        | _, Error e -> Error e

            let capabilitiesR =
                match requireField path fields "capabilities" "Mount capabilities array" with
                | Error e -> Error e
                | Ok capsJson ->
                    requireArray (path + ".capabilities") capsJson
                    |> Result.bind (traverse (fun j -> requireString (path + ".capabilities[]") j))

            let inputsR =
                match tryField fields "inputs" with
                | None -> Ok None
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
                                     |> Result.bind (decodeNodeAst (descend w) (argPath + ".tree"))
                                     |> Result.map FragmentArg.SlotArg
                                 | Ok _ ->
                                     decodeScalarTyped argPath v
                                     |> Result.map (fun s ->
                                         match s with
                                         | Scalar.Int sv -> FragmentArg.Int sv
                                         | Scalar.Float sv -> FragmentArg.Float sv
                                         | Scalar.Bool sv -> FragmentArg.Bool sv
                                         | Scalar.Str sv -> FragmentArg.Str sv))
                            |> Result.map (fun decoded -> k, decoded))
                        |> Result.map (Map.ofList >> Some)

            match scopeIdR, channelR, capabilitiesR, inputsR with
            | Ok scopeId, Ok channel, Ok capabilities, Ok inputs ->
                Ok(
                    NodeKind.Mount
                        { ScopeId = scopeId
                          Inputs = inputs
                          Channel = channel
                          // Wire `onBubble` is a sentinel; presence decodes to a
                          // no-op `Some` so re-encode stays byte-identical.
                          OnBubble =
                            (match tryField fields "onBubble" with
                             | Some _ -> Some(fun _ -> Action.Chain [])
                             | None -> Option.None)
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
                (Some wrongNodeKindHint)

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
            | Some v -> requireString (path + ".labelledBy") v |> Result.map Some

        let describedByR =
            match tryField fields "describedBy" with
            | None -> Ok None
            | Some v -> requireString (path + ".describedBy") v |> Result.map Some

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

and private decodeStateBehaviour (w: Walk) (path: string) (j: Json) : Result<StateBehaviour<obj>, DecodeError> =
    match requireObject path j with
    | Error e -> Error e
    | Ok fields ->
        let onLoadingR =
            match tryField fields "onLoading" with
            | None -> Ok None
            | Some v -> decodeNodeAst (descend w) (path + ".onLoading") v |> Result.map Some

        let onEmptyR =
            match tryField fields "onEmpty" with
            | None -> Ok None
            | Some v -> decodeNodeAst (descend w) (path + ".onEmpty") v |> Result.map Some

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

/// The single gate every node in the document passes through — both structural
/// bounds are enforced here, on the way DOWN (Phase 781; WIRE_FORMAT §21).
///
/// `w.Depth` is this node's own nesting level, the root being 1. The DEPTH guard
/// is here and not only in the parser because the two bounds are different
/// quantities. The parser bounds SYNTACTIC nesting (`MaxJsonDepth`), which a
/// tree consumes several levels of per node and which a structured payload
/// position consumes freely within a single node; this bounds NODE nesting
/// (`MaxDepth`). The structural decoder's frames are also far heavier than the
/// parser's — measured, it overflows roughly an order of magnitude shallower —
/// so the parser's bound could never have stood in for this one.
///
/// The NODE-COUNT guard closes the remaining cost channel once depth is bounded.
/// Depth, string length and array length together still admit a tree that is
/// merely WIDE — 24 levels of 100 000 siblings — whose cost is linear in the
/// input but whose constant is not: the decoded tree is far larger in memory
/// than the bytes that produced it. Counting on the way down means an oversized
/// document stops being decoded at the ceiling rather than after the whole tree
/// has been allocated.
///
/// It does NOT bound the parse that precedes it — `tryParse` has already built a
/// `Json` AST for the entire input by the time the first node is decoded — and
/// this comment says so rather than implying a stronger guarantee. Bounding
/// total payload SIZE is a transport concern the host owns (a request body
/// limit); what these limits bound is structure, which is the part a size limit
/// cannot express.
and private decodeNodeAst (w: Walk) (path: string) (j: Json) : Result<Node<obj>, DecodeError> =
    if w.Depth > Fuaran.UI.WireLimits.MaxDepth then
        err
            DecodeErrorCode.LIMIT_EXCEEDED
            path
            (sprintf "node nesting depth %d exceeds the wire limit MaxDepth = %d" w.Depth Fuaran.UI.WireLimits.MaxDepth)
            (Some(sprintf "a tree nesting nodes no more than %d levels deep" Fuaran.UI.WireLimits.MaxDepth))
    else
        w.Nodes.Value <- w.Nodes.Value + 1

        if w.Nodes.Value > Fuaran.UI.WireLimits.MaxNodes then
            err
                DecodeErrorCode.LIMIT_EXCEEDED
                path
                (sprintf "the document holds more than the wire limit MaxNodes = %d nodes" Fuaran.UI.WireLimits.MaxNodes)
                (Some(sprintf "a tree of no more than %d nodes in total" Fuaran.UI.WireLimits.MaxNodes))
        else
            decodeNodeAstCore w path j

/// The node decode proper. Reached only through `decodeNodeAst`, which is what
/// enforces `MaxDepth` — split out purely so the guard is a two-line function
/// rather than a wrapper around sixty indented lines.
and private decodeNodeAstCore (w: Walk) (path: string) (j: Json) : Result<Node<obj>, DecodeError> =
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
                | Ok s -> Ok s

        let kindR =
            requireField path fields "kind" "NodeKind discriminator object"
            |> Result.bind (decodeNodeKind w (path + ".kind"))

        // `state` and `style` are optional on the flat wire (omitted when empty
        // / all-default, WIRE_FORMAT §3.1). The envelope slots are OPTIONS since
        // the swap — absence decodes to `None` (the canonical default form),
        // presence to `Some`, so re-encode reproduces the incoming shape.
        let stateR: Result<StateBehaviour<obj> option, DecodeError> =
            match tryField fields "state" with
            | None -> Ok None
            | Some v -> decodeStateBehaviour w (path + ".state") v |> Result.map Some

        let styleR: Result<SemanticStyle option, DecodeError> =
            match tryField fields "style" with
            | None -> Ok None
            | Some v -> decodeSemanticStyle (path + ".style") v |> Result.map Some

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

/// The op decoder is bounded on the SAME two axes as the node decoder, and for a
/// reason that was measured rather than assumed (Phase 781).
///
/// `TreeOp.Batch` carries a list of ops, so `decodeTreeOpAst` is recursive in
/// itself, and it was left unguarded on the first pass at this phase because the
/// phase's own defect list did not name it. The parser's `MaxJsonDepth` looked
/// like sufficient cover — a Batch level costs two JSON levels, so 256 admits
/// only about 127 of them. It is not sufficient: a 2.6 KB payload of 100 nested
/// Batches kills the process outright, because these frames are as heavy as the
/// node decoder's. That is the original defect exactly, reached through the other
/// public entry point.
///
/// `w.Depth` therefore bounds BATCH nesting here. The node-bearing arms restart
/// the depth count with `atRoot` rather than continuing it: op nesting and node
/// nesting are different axes, and charging a node tree for the depth of the
/// Batch that carries it would refuse legitimate ops. The node COUNT is
/// deliberately not restarted — `atRoot` keeps the same `Nodes` cell — because a
/// Batch is one wire artefact, and a per-op budget would let a thousand-op batch
/// smuggle a thousand times the ceiling.
let private atRoot (w: Walk) : Walk = { w with Depth = 1 }

let rec private decodeTreeOpAst (w: Walk) (path: string) (j: Json) : Result<TreeOp<obj>, DecodeError> =
    if w.Depth > Fuaran.UI.WireLimits.MaxDepth then
        err
            DecodeErrorCode.LIMIT_EXCEEDED
            path
            (sprintf "op nesting depth %d exceeds the wire limit MaxDepth = %d" w.Depth Fuaran.UI.WireLimits.MaxDepth)
            (Some(sprintf "a Batch nesting ops no more than %d levels deep" Fuaran.UI.WireLimits.MaxDepth))
    else
        decodeTreeOpAstCore w path j

/// The op decode proper. Reached only through `decodeTreeOpAst`, which is what
/// enforces the bound — split out so the guard is a few lines rather than a
/// wrapper around two hundred indented ones.
and private decodeTreeOpAstCore (w: Walk) (path: string) (j: Json) : Result<TreeOp<obj>, DecodeError> =
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
                |> Result.bind (decodeNodeKind (atRoot w) (path + ".newKind"))

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
                |> Result.bind (decodeStateBehaviour (atRoot w) (path + ".state"))

            match targetR, stateR with
            | Ok target, Ok state -> Ok(TreeOp.UpdateState(target, state))
            | Error e, _
            | _, Error e -> Error e
        | Ok "InsertChild" ->
            let parentR =
                requireField path fields "parentId" "parent NodeId"
                |> Result.bind (requireString (path + ".parentId"))
                |> Result.map NodeId

            let childR =
                requireField path fields "child" "child Node object"
                |> Result.bind (decodeNodeAst (atRoot w) (path + ".child"))

            // A legacy `position` is ACCEPTED AND IGNORED for the migration window
            // (phase 681): five hosts adopt independently, and a stored v1 emission
            // must still apply — as an append, since order is now ReorderChildren's.
            // The tolerance is a migration mechanism, not a choice offered to an
            // author: nothing that teaches the wire mentions the field. Phase 687
            // closes the window and makes it a decode error.
            match parentR, childR with
            | Ok parent, Ok child -> Ok(TreeOp.InsertChild(parent, child))
            | Error e, _
            | _, Error e -> Error e
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

            // Legacy `newPosition` accepted and ignored — see the InsertChild note.
            match targetR, newParentR with
            | Ok target, Ok newParent -> Ok(TreeOp.MoveNode(target, newParent))
            | Error e, _
            | _, Error e -> Error e
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
            |> Result.bind (decodeNodeAst (atRoot w) (path + ".node"))
            |> Result.map TreeOp.ReplaceRoot
        | Ok "Batch" ->
            requireField path fields "ops" "Batch inner-op list"
            |> Result.bind (fun v -> requireArray (path + ".ops") v)
            |> Result.bind (fun xs ->
                traverseIndexed (fun i item -> decodeTreeOpAst (descend w) (sprintf "%s.ops[%d]" path i) item) xs)
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

    /// `Icon.Size : IconSize` (Phase 821) — the UpdateProp twin of
    /// `decodeIconSize`, added with the standalone Icon display kind.
    let tryIconSize (v: obj) : Result<IconSize, string> = viaJson decodeIconSize v
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

/// Map a `tryParse` failure onto the `DecodeError` envelope. `LIMIT_EXCEEDED`
/// keeps the parser's own message — it already names the limit and the observed
/// value — because "input is not valid JSON" would be an actively wrong
/// diagnosis: the input was perfectly well-formed and simply too big to walk.
let private parseFailure (code: DecodeErrorCode, message: string) : Result<'a, DecodeError> =
    match code with
    | DecodeErrorCode.LIMIT_EXCEEDED ->
        err
            code
            "$"
            message
            (Some
                "a payload within the WIRE_FORMAT §21 resource limits (node depth, JSON depth, string length, array length)")
    | _ ->
        err
            code
            "$"
            (sprintf "input is not valid JSON: %s" message)
            (Some "well-formed JSON object per the canonical-JSON shape")


/// Decode a canonical-JSON encoded `Node<'Msg>` payload into a `WireTree` —
/// the storage-shape `Node<obj>` marked as wire-originated. The wire format is
/// the output of `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`.
/// Closure-bearing slots decode to inert placeholders carrying the `"<closure>"`
/// sentinel, so the result is safe to persist / diff / apply ops to / drive
/// through a bounded program loop (`Fuaran.Program.Bounded`'s driver takes a
/// `WireTree` directly) but NOT to
/// render through the live client renderer (its handlers are gone) — see
/// `WireTree`. The orchestrator's typed re-attachment happens downstream
/// (`moduleMsgDecoder: JVal -> 'Msg`). Use `decodeNodeObj` for the raw
/// `Node<obj>` when you are the reattachment / persistence boundary.
let decodeNode (json: string) : Result<WireTree, DecodeError> =
    let decoded =
        match tryParseNodeWithRecovery json with
        | Error failure -> parseFailure failure
        | Ok j -> decodeNodeAst (walkRoot ()) "$" j

    decoded |> Result.map WireTree.ofDecoded

/// Raw `Node<obj>` decode — the escape hatch for reattachment / persistence
/// boundaries that need the unmarked tree (equivalent to
/// `decodeNode json |> Result.map WireTree.reify`). Prefer `decodeNode`; this
/// exists so those boundaries don't wrap-then-immediately-reify.
let decodeNodeObj (json: string) : Result<Node<obj>, DecodeError> =
    match tryParseNodeWithRecovery json with
    | Error failure -> parseFailure failure
    | Ok j -> decodeNodeAst (walkRoot ()) "$" j

/// Decode a canonical-JSON encoded `TreeOp<'Msg>` payload into the
/// storage-shape `TreeOp<obj>`. Symmetric with
/// `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp`.
let decodeOp (json: string) : Result<TreeOp<obj>, DecodeError> =
    match tryParse json with
    | Error failure -> parseFailure failure
    | Ok j -> decodeTreeOpAst (walkRoot ()) "$" j
