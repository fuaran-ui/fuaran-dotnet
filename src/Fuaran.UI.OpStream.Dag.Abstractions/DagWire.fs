namespace Fuaran.UI.OpStream.Dag.Abstractions

open System.Globalization
open System.Text
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DagWire — the canonical wire form of a `DagOpRecord`.
//
//  The DagOpRecord wire shape is an ADDITIVE wire artifact: the *linear*
//  `OpRecord` + the Node/TreeOp corpus are untouched. A DAG record serialises
//  as a canonical-JSON object (Ordinal-sorted keys, no whitespace — the same
//  discipline as `CanonicalJson`) carrying the multi-parent links + the nested
//  canonical `TreeOp`:
//
//      {"actor":{…canonical Actor…},"hash":"…","op":{…canonical TreeOp…},
//       "outcomeHash":"…"?,"parents":["…","…"],"promptId":"…"?,
//       "resultEnvelope":{…},"streamId":"…","timestamp":<unixSeconds>,
//       "tombstoned":false}
//
//  `outcomeHash` / `promptId` are omitted when `None` (algorithm rule 4).
//  `op` nests the existing `CanonicalJson.encodeOp` output verbatim, and `actor`
//  nests `Actor.encode` verbatim, so the DAG envelope reuses the TreeOp and
//  actor wire contracts rather than re-specifying either.
//
//  Phase 1144 replaced the trailing `"userId":"…"` member with the leading
//  `"actor":{…}` — the typed `Human | Agent` the linear chain has carried since
//  Phase 320. Top-level keys stay Ordinal-sorted, which is why `actor` moves to
//  the FRONT of the envelope. It is a MAJOR wire event on the same axis as the
//  content-address change it accompanies (`DagOpRecord`): the actor is inside
//  the address, so a record cannot be re-encoded under the new key while
//  keeping its old hash. `decodeRecord` therefore REFUSES a `userId` envelope by
//  name rather than lifting it — see `docs/migrations/1144-typed-actor-dag-fold.md`.
//
//  Two conformant hosts (F# + the TS reference implementation) encode the same
//  record to byte-identical JSON, and the `parents` array + nested `op` are the
//  pre-image the content hash folds over — so the wire form, the hash, and the
//  corpus all move together (the additive forward-coupling for this artifact).
// ============================================================================

module DagWire =

    let private escapeInto (sb: StringBuilder) (s: string) : unit =
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore

    let private encodeEnvelope (sb: StringBuilder) (envelope: OpResultEnvelope) : unit =
        match envelope with
        | OpResultEnvelope.Success -> sb.Append "{\"$type\":\"Success\"}" |> ignore
        | OpResultEnvelope.Failure(code, message) ->
            sb.Append "{\"$type\":\"Failure\",\"code\":" |> ignore
            escapeInto sb code
            sb.Append ",\"message\":" |> ignore
            escapeInto sb message
            sb.Append '}' |> ignore

    /// Encode a `DagOpRecord` to its canonical JSON wire form. Keys are emitted
    /// in Ordinal-sorted order; `outcomeHash` / `promptId` are omitted when
    /// `None`. `op` nests `CanonicalJson.encodeOp` verbatim and `actor` nests
    /// `Actor.encode` verbatim (both pinned encodings, embedded as-is).
    let encodeRecord<'Msg> (record: DagOpRecord<'Msg>) : string =
        let sb = StringBuilder()
        sb.Append '{' |> ignore

        // Ordinal key order: actor < hash < op < outcomeHash < parents <
        // promptId < resultEnvelope < streamId < timestamp < tombstoned.
        sb.Append "\"actor\":" |> ignore
        sb.Append(Actor.encode record.Actor) |> ignore

        sb.Append ",\"hash\":" |> ignore
        escapeInto sb record.Hash

        sb.Append ",\"op\":" |> ignore
        sb.Append(CanonicalJson.encodeOp record.Op) |> ignore

        match record.OutcomeHash with
        | Some o ->
            sb.Append ",\"outcomeHash\":" |> ignore
            escapeInto sb o
        | None -> ()

        sb.Append ",\"parents\":[" |> ignore

        record.Parents
        |> List.iteri (fun i p ->
            if i > 0 then
                sb.Append ',' |> ignore

            escapeInto sb p)

        sb.Append ']' |> ignore

        match record.PromptId with
        | Some p ->
            sb.Append ",\"promptId\":" |> ignore
            escapeInto sb p
        | None -> ()

        sb.Append ",\"resultEnvelope\":" |> ignore
        encodeEnvelope sb record.ResultEnvelope

        sb.Append ",\"streamId\":" |> ignore
        escapeInto sb record.StreamId

        sb.Append ",\"timestamp\":" |> ignore

        sb.Append(record.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        |> ignore

        sb.Append ",\"tombstoned\":" |> ignore
        sb.Append(if record.Tombstoned then "true" else "false") |> ignore

        sb.Append '}' |> ignore
        sb.ToString()

    // ── Content identity for the sinks' write path (Phase 1525) ───────────

    /// The CONTENT IDENTITY of a DAG record: the SHA-256 of its canonical wire
    /// form, with the retention-mutable `Tombstoned` flag normalised to `false`.
    ///
    /// This is what a sink's `Add` compares when a record arrives at an address
    /// it already holds. It has to be a DIGEST of the canonical bytes rather
    /// than the bytes themselves for one reason: retention drops a record's op
    /// payload (`Tombstone`), so a sink that kept the bytes in order to compare
    /// them would either have to retain the payload it was pruning — defeating
    /// retention — or lose the ability to recognise an identical re-add after a
    /// compaction. A fixed-size fingerprint survives the pruning that the
    /// content it summarises does not.
    ///
    /// `Tombstoned` is normalised because it is retention STATE, not content: a
    /// live record and its own tombstone are the same node at the same address,
    /// and a re-add of the original after a sweep must resolve as the idempotent
    /// duplicate it is, not as a collision.
    ///
    /// Everything else the record carries is inside these bytes — parents, op,
    /// outcome hash, prompt id, actor, timestamp, result envelope, stream and
    /// stored address — so two records that fingerprint alike differ in nothing
    /// a host can observe, and two that differ anywhere at all fingerprint
    /// apart. That is the property the collision refusal needs and that a
    /// hand-picked field-by-field comparison cannot promise: the previous check
    /// compared `Parents` and `OutcomeHash` only, so a record with a DIFFERENT
    /// OP at the same address was admitted as a duplicate and silently dropped.
    let contentFingerprint<'Msg> (record: DagOpRecord<'Msg>) : string =
        HashChain.sha256Hex (encodeRecord { record with Tombstoned = false })

    // ── Decode ────────────────────────────────────────────────────────────
    //
    // A focused scanner over the canonical envelope. It walks only TOP-LEVEL
    // keys (skipping nested values wholesale via string-aware brace matching),
    // so a `"hash"` / `"op"` key appearing INSIDE the nested op is never
    // mistaken for a top-level field. The nested `op` raw substring is handed
    // to the host `IOpJsonCodec<'Msg>` (the `'Msg` shape is host-owned, exactly
    // as for the linear sink).

    /// Scan one JSON value starting at `s[i]`; return the index just past it.
    let rec private scanValue (s: string) (i: int) : int =
        match s[i] with
        | '"' -> scanString s i
        | '{' -> scanBracketed s i '{' '}'
        | '[' -> scanBracketed s i '[' ']'
        | _ ->
            // number / true / false / null — read until a structural char.
            let mutable j = i

            while j < s.Length && s[j] <> ',' && s[j] <> '}' && s[j] <> ']' do
                j <- j + 1

            j

    /// Index just past a string starting at the opening quote `s[i]`.
    and private scanString (s: string) (i: int) : int =
        let mutable j = i + 1
        let mutable fin = false

        while not fin && j < s.Length do
            match s[j] with
            | '\\' -> j <- j + 2
            | '"' ->
                fin <- true
                j <- j + 1
            | _ -> j <- j + 1

        j

    /// Index just past a `{…}` / `[…]` starting at `s[i] = open`, respecting
    /// strings (braces inside strings don't count).
    and private scanBracketed (s: string) (i: int) (openCh: char) (closeCh: char) : int =
        let mutable j = i + 1
        let mutable depth = 1

        while depth > 0 && j < s.Length do
            match s[j] with
            | '"' -> j <- scanString s j
            | c when c = openCh ->
                depth <- depth + 1
                j <- j + 1
            | c when c = closeCh ->
                depth <- depth - 1
                j <- j + 1
            | _ -> j <- j + 1

        j

    /// Unescape a raw JSON string substring (including its surrounding quotes).
    let private unquote (raw: string) : string =
        let inner = raw.Substring(1, raw.Length - 2)
        let sb = StringBuilder()
        let mutable i = 0

        while i < inner.Length do
            if inner[i] = '\\' && i + 1 < inner.Length then
                match inner[i + 1] with
                | '"' ->
                    sb.Append '"' |> ignore
                    i <- i + 2
                | '\\' ->
                    sb.Append '\\' |> ignore
                    i <- i + 2
                | 'n' ->
                    sb.Append '\n' |> ignore
                    i <- i + 2
                | 't' ->
                    sb.Append '\t' |> ignore
                    i <- i + 2
                | 'u' when i + 5 < inner.Length ->
                    let code = System.Convert.ToInt32(inner.Substring(i + 2, 4), 16)
                    sb.Append(char code) |> ignore
                    i <- i + 6
                | other ->
                    sb.Append other |> ignore
                    i <- i + 2
            else
                sb.Append inner[i] |> ignore
                i <- i + 1

        sb.ToString()

    /// Map of top-level key → raw value substring for a canonical envelope.
    let private topLevelFields (json: string) : Map<string, string> =
        let s = json.Trim()
        let mutable i = 1 // past '{'
        let mutable fields = Map.empty

        while i < s.Length && s[i] <> '}' do
            // key (a string)
            let keyEnd = scanString s i
            let key = unquote (s.Substring(i, keyEnd - i))
            // ':' then value
            let valStart = keyEnd + 1
            let valEnd = scanValue s valStart
            fields <- Map.add key (s.Substring(valStart, valEnd - valStart)) fields
            // skip a trailing comma
            i <-
                if valEnd < s.Length && s[valEnd] = ',' then
                    valEnd + 1
                else
                    valEnd

        fields

    let private parseStringArray (raw: string) : string list =
        let inner = raw.Trim().Trim('[', ']')

        if inner = "" then
            []
        else
            // Each element is a quoted string; reuse the scanner.
            let result = ResizeArray<string>()
            let mutable i = 0

            while i < inner.Length do
                if inner[i] = '"' then
                    let e = scanString inner i
                    result.Add(unquote (inner.Substring(i, e - i)))
                    i <- e
                else
                    i <- i + 1

            List.ofSeq result

    /// Decode a `resultEnvelope` value BY FIELD (Phase 1525).
    ///
    /// It used to decide the case by asking whether the raw text CONTAINED the
    /// substring `"Success"` — a property of the whole envelope rather than of
    /// its discriminator, so
    /// `{"$type":"Failure","code":"E","message":"the \"Success\" branch was not
    /// taken"}` decoded as a SUCCESS. A failure that merely mentions the word is
    /// not an exotic payload: an apply error's message routinely quotes the op,
    /// the field or the variant it refused. The discriminator is now read from
    /// the `$type` member alone, through the same top-level scanner the record
    /// envelope uses, so a nested value can never be mistaken for it.
    ///
    /// An unrecognised `$type` — or a value that is not an object at all — is a
    /// typed `Error` rather than a silent coercion to `Success`. That is the
    /// whole reason the defect was worth fixing: the wrong answer here is
    /// indistinguishable from the right one everywhere downstream.
    let private parseEnvelope (raw: string) : Result<OpResultEnvelope, string> =
        let trimmed = raw.Trim()

        if trimmed.Length = 0 || trimmed[0] <> '{' then
            Error(sprintf "'resultEnvelope' is not an object: %s" trimmed)
        else
            let f = topLevelFields trimmed

            let get k =
                f |> Map.tryFind k |> Option.map unquote |> Option.defaultValue ""

            match f |> Map.tryFind "$type" |> Option.map unquote with
            | Some "Success" -> Ok OpResultEnvelope.Success
            | Some "Failure" -> Ok(OpResultEnvelope.Failure(get "code", get "message"))
            | Some other -> Error(sprintf "'resultEnvelope' has an unrecognised $type '%s'" other)
            | None -> Error(sprintf "'resultEnvelope' carries no '$type' discriminator: %s" trimmed)

    /// Decode a canonical DAG-record envelope. `decodeOp` is the host's op
    /// decoder for the nested `op` object (the `'Msg` shape is host-owned).
    /// Returns the reconstructed `DagOpRecord<'Msg>` or a parse error string.
    let decodeRecord<'Msg>
        (decodeOp: string -> Result<TreeOp<'Msg>, string>)
        (json: string)
        : Result<DagOpRecord<'Msg>, string> =
        try
            let f = topLevelFields json
            let req k = Map.tryFind k f

            match req "hash", req "op", req "parents", req "streamId", req "timestamp", req "actor" with
            | Some hashRaw, Some opRaw, Some parentsRaw, Some streamRaw, Some tsRaw, Some actorRaw ->
                match Actor.tryDecode actorRaw, decodeOp opRaw with
                | None, _ ->
                    Error(
                        sprintf
                            "DagWire.decodeRecord: malformed 'actor' — not a canonical Actor.encode object: %s"
                            actorRaw
                    )
                | _, Error e -> Error(sprintf "DagWire.decodeRecord: op decode failed: %s" e)
                | Some actor, Ok op ->
                    // An ABSENT `resultEnvelope` still defaults to `Success` — the
                    // member is optional in the sense that a hand-written envelope
                    // may omit it, and "not stated" has always meant success here.
                    // A PRESENT but unrecognised one is refused: that is a shape
                    // the writer meant something by, and guessing at it is exactly
                    // the substring-sniffing this replaced.
                    let envelopeResult =
                        match Map.tryFind "resultEnvelope" f with
                        | None -> Ok OpResultEnvelope.Success
                        | Some raw -> parseEnvelope raw

                    match envelopeResult with
                    | Error e -> Error("DagWire.decodeRecord: " + e)
                    | Ok envelope ->
                        Ok
                            { StreamId = unquote streamRaw
                              Hash = unquote hashRaw
                              Parents = parseStringArray parentsRaw
                              Op = op
                              OutcomeHash = f |> Map.tryFind "outcomeHash" |> Option.map unquote
                              PromptId = f |> Map.tryFind "promptId" |> Option.map unquote
                              Actor = actor
                              // `int64 (s: string)` is FSharp.Core's invariant-culture
                              // parse and is Fable-supported; the explicit
                              // `Int64.Parse(s, provider)` overload is not (Fable
                              // errors "provider argument is ignored"), and this file
                              // ships in the Fable-packed abstractions.
                              Timestamp = System.DateTimeOffset.FromUnixTimeSeconds(int64 tsRaw)
                              ResultEnvelope = envelope
                              Tombstoned = (f |> Map.tryFind "tombstoned") = Some "true" }
            | _ when (Map.containsKey "userId" f) && not (Map.containsKey "actor" f) ->
                // A pre-1144 envelope. Refused BY NAME rather than lifted: the actor
                // is inside the content address, so a lifted record would carry a
                // stored `hash` that `DagOpRecord.recomputeHash` cannot reproduce —
                // a silent verification failure downstream instead of a clear one here.
                Error
                    "DagWire.decodeRecord: pre-1144 envelope — 'userId' was replaced by the typed 'actor', and DAG content addresses do not carry forward (docs/migrations/1144-typed-actor-dag-fold.md)"
            | _ ->
                Error
                    "DagWire.decodeRecord: missing one of the required fields (hash/op/parents/streamId/timestamp/actor)"
        with ex ->
            Error(sprintf "DagWire.decodeRecord: %s" ex.Message)
