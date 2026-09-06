namespace Fuaran.UI.OpStream.Abstractions

open System
open Fuaran.UI.Ops.Types

// ============================================================================
//  StreamEntry — the domain provenance envelope (Phase 406).
//
//  The op-stream chain is re-expressed over `Fuaran.Core.OpStream`'s canonical
//  format WITHOUT a new Core seam (finding F13): the rich per-record provenance
//  a `Fuaran.UI` record carries — the op, its timestamp, the prompt that
//  produced it, and its apply outcome — is bundled into ONE opaque payload that
//  becomes Core's `'Op`. The chain pre-image is then Core's canonical *delimited*
//  `{"seq":…,"actor":…,"op":…}` envelope with `op = StreamEntry.encode`, hashed
//  by the Phase-405 host-side SHA-256 supplied through Core's certified `HashFn`
//  seam (GP3).
//
//  Two defects the pre-406 chain carried, both closed here:
//   1. Provenance hole — `PromptId` and `ResultEnvelope` were OUTSIDE the hash
//      pre-image, so re-attributing an op to a different prompt or flipping a
//      recorded `Failure` to `Success` did not break `Verify.chain`. They are
//      now folded in (inside `encode`), so attribution AND outcome are covered
//      by the digest — an edit to either is detected on verification, provided
//      the chain was not recomputed with it (the chain is UNKEYED: it detects
//      corruption, not a writer who re-chains — see CRYPTO.md).
//   2. Undelimited pre-image — the old formula concatenated `sequence` and the
//      unix timestamp with no separator, so distinct `(seq, ts)` pairs could be
//      byte-identical. Core's canonical payload is a delimited JSON object.
//
//  `encode` is the cross-host contract (the TS host mirrors it, Phase 407);
//  field order is pinned and MUST stay byte-for-byte aligned across hosts.
// ============================================================================

/// One op's worth of durable provenance — the opaque `'Op` Core's chain carries.
/// Assembled from an `OpRecord`'s fields at hash time; not itself a stored type.
type StreamEntry<'Msg> =
    { Op: TreeOp<'Msg>
      Timestamp: DateTimeOffset
      PromptId: string option
      ResultEnvelope: OpResultEnvelope }

module StreamEntry =

    /// The chain FORMAT version, folded into the hash pre-image (the first field
    /// of `encode`). It makes the chain format **self-describing, and covered by the
    /// digest**: a host can read `v` from any record's envelope before verifying and
    /// reject an unrecognised version with a clear error (rather than a cryptic hash
    /// break), and because it is inside the pre-image, relabelling a stream's format
    /// breaks verification unless the chain is recomputed with it. Bump
    /// this — in lock-step across every host (F#/TS/Python) and the `chain-corpus.json`
    /// golden — whenever the pre-image formula, the envelope shape, or the `HashFn`
    /// changes. History:
    ///   v2 — Phase 406/411: the provenance envelope + Core-canonical delimited payload
    ///        + host-side SHA-256 (this file). (v1 = the pre-406 FNV-1a raw-concat chain,
    ///        migrated by `ChainMigration`; it carried no version tag, so a tagless
    ///        record is treated as v1 by a reader that finds no `v`.)
    [<Literal>]
    let chainFormatVersion = 2

    /// Canonical JSON string escaping — mirrors `CanonicalJson.appendRawString`
    /// (only `"` / `\` / control chars, control as `\u00xx`) so the bytes are
    /// identical to the encoder the rest of the wire format uses.
    let private jstr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    /// Canonical encoding of the apply outcome. A `Success` is a bare tag; a
    /// `Failure` carries its code + message, so flipping outcome breaks the hash.
    let encodeResult (r: OpResultEnvelope) : string =
        match r with
        | OpResultEnvelope.Success -> "{\"kind\":\"success\"}"
        | OpResultEnvelope.Failure(code, message) ->
            "{\"kind\":\"failure\",\"code\":"
            + jstr code
            + ",\"message\":"
            + jstr message
            + "}"

    /// The pinned cross-host encoding of the provenance envelope. Field order —
    /// **v** / op / ts / promptId / result — is pinned; `v` is the chain format
    /// version (`chainFormatVersion`) and sorts first so a reader can lift it
    /// with a minimal parse. `ts` is unix SECONDS (matching the pre-406
    /// pre-image's timestamp resolution); `promptId` is `null` when absent.
    let encode (entry: StreamEntry<'Msg>) : string =
        "{\"v\":"
        + string chainFormatVersion
        + ",\"op\":"
        + CanonicalJson.encodeOp entry.Op
        + ",\"ts\":"
        + string (entry.Timestamp.ToUnixTimeSeconds())
        + ",\"promptId\":"
        + (match entry.PromptId with
           | Some p -> jstr p
           | None -> "null")
        + ",\"result\":"
        + encodeResult entry.ResultEnvelope
        + "}"

    /// Read the chain format version from a persisted / encoded envelope without
    /// verifying it — for a host that wants to reject an unrecognised format with
    /// a clear error *before* attempting hash verification (which would otherwise
    /// surface as a cryptic chain break). Returns `None` when no `v` field leads
    /// the envelope, which a reader treats as **v1** (the pre-406 tagless format).
    ///
    /// A minimal prefix scan for the pinned leading `{"v":<int>` — deliberately
    /// NOT a full `Json.parse`: the envelope legitimately carries `promptId:null`,
    /// which the Fuaran wire JVal model rejects, and a version reader must work on
    /// an envelope of an *unknown future* shape it cannot fully parse anyway.
    ///
    /// The prefix test is ORDINAL (Phase 1525). It is a WIRE-BYTE comparison,
    /// not a human-text one: the envelope's leading five characters are what the
    /// encoder emitted, and the culture-sensitive default overload can answer
    /// differently under a culture whose collation treats them differently — so
    /// the same persisted record would report its format version on one machine
    /// and read as tagless (v1) on another, which is exactly the "cryptic chain
    /// break" this reader exists to prevent. The `Substring` below slices at the
    /// same fixed count, so test and slice must agree by construction.
    let formatVersion (encodedEnvelope: string) : int option =
        let prefix = "{\"v\":"

        if encodedEnvelope.StartsWith(prefix, System.StringComparison.Ordinal) then
            let digits =
                encodedEnvelope.Substring prefix.Length
                |> Seq.takeWhile System.Char.IsDigit
                |> Seq.toArray
                |> System.String

            match System.Int32.TryParse digits with
            | true, n -> Some n
            | _ -> None
        else
            None

    // ── decode — the inverse of `encode` ──────────────────────────────────
    //
    // Hand-rolled and Fable-clean, the `Actor.tryDecode` discipline: no
    // `System.Text.Json`, no culture-provider `Parse` overload (Fable rejects
    // them), pure char arithmetic and `StringBuilder`.
    //
    // It is a TOP-LEVEL field scanner rather than the flat `IndexOf` search
    // `Actor.tryDecode` uses, and that difference is load-bearing: the envelope's
    // `op` value is arbitrary canonical JSON that legitimately contains its own
    // `"ts"` / `"result"` / `"promptId"` keys (a `Batch` of ops, a spec carrying a
    // field of that name), so a whole-string search would happily read an
    // envelope field out of the nested payload. The scanner captures each
    // top-level value's RAW span, so `op` reaches the host decoder byte-for-byte
    // — the same round-trip guarantee `Core.OpStream.fromJsonl` gives its `op`.
    //
    // TOTAL ON TRUNCATED INPUT (Phase 1525). Every scanner below answers `-1`
    // for "this span does not close", and `tryTopLevelFields` turns that into a
    // named `Error`. Before, each scanner ASSUMED its span was well-formed:
    // `scanString` stepped two characters past a trailing backslash and could
    // return an index beyond the string; `scanValue` indexed `s[i]` without
    // checking `i` was in range; the field walk read `s[keyEnd]` as a `:` it had
    // never confirmed and sliced a value span that might not exist. A stream
    // truncated mid-record — the ordinary outcome of a partial write, a clipped
    // copy-paste, or a bounded transport — therefore reached a decoder that
    // either threw an index exception past the `Result` boundary the signature
    // promises, or silently produced a field map missing (or mis-spanning) the
    // very fields the chain pre-image is built from. Both are worse than a
    // refusal: the caller is holding a `Result` and has no reason to expect
    // either. Malformed input is now always `Error`, never a throw and never a
    // plausible-looking record.

    /// Index just past the string starting at the opening quote `s[i]`, or `-1`
    /// when the string never closes (truncated input) or an escape's payload
    /// runs off the end.
    let rec private scanString (s: string) (i: int) : int =
        let mutable j = i + 1
        let mutable fin = false
        let mutable truncated = false

        while not fin && not truncated && j < s.Length do
            match s[j] with
            | '\\' ->
                // A trailing backslash has no escaped character to consume; the
                // pre-1525 `j <- j + 2` walked past the end of the string here.
                if j + 1 >= s.Length then truncated <- true else j <- j + 2
            | '"' ->
                fin <- true
                j <- j + 1
            | _ -> j <- j + 1

        if fin then j else -1

    /// Index just past a `{…}` / `[…]` starting at `s[i]`, respecting strings,
    /// or `-1` when the bracket never closes or a nested string is truncated.
    and private scanBracketed (s: string) (i: int) (openCh: char) (closeCh: char) : int =
        let mutable j = i + 1
        let mutable depth = 1
        let mutable truncated = false

        while not truncated && depth > 0 && j < s.Length do
            match s[j] with
            | '"' ->
                let k = scanString s j

                if k < 0 then truncated <- true else j <- k
            | c when c = openCh ->
                depth <- depth + 1
                j <- j + 1
            | c when c = closeCh ->
                depth <- depth - 1
                j <- j + 1
            | _ -> j <- j + 1

        if truncated || depth > 0 then -1 else j

    /// Index just past one JSON value starting at `s[i]`, or `-1` when there is
    /// no value there at all (the input ended) or the value does not close.
    and private scanValue (s: string) (i: int) : int =
        if i >= s.Length then
            -1
        else
            match s[i] with
            | '"' -> scanString s i
            | '{' -> scanBracketed s i '{' '}'
            | '[' -> scanBracketed s i '[' ']'
            | _ ->
                // number / true / false / null — read to the next structural char.
                let mutable j = i

                while j < s.Length && s[j] <> ',' && s[j] <> '}' && s[j] <> ']' do
                    j <- j + 1

                // A bare value is delimited by a structural character. Running to
                // the end of the input means the object was cut mid-value, and an
                // empty span means there was no value where one was promised.
                if j >= s.Length || j = i then -1 else j

    /// Unescape a raw JSON string span (quotes included). The inverse of `jstr`
    /// -- which writes control characters as \uXXXX -- plus the shorthand
    /// escapes (\n, \t, \r, \b, \f, \/) a conformant peer is entitled to emit
    /// for the same characters. Accepting the wider set costs nothing and keeps
    /// this a decoder of the FORMAT rather than of one encoder's habits.
    ///
    /// The length guard is defence in depth (Phase 1525), not the check that
    /// matters: `scanString` already refuses a span that does not open and close
    /// with a quote, and `tryUnquote` refuses one that is not a string at all. It
    /// is here because the pre-1525 `Substring(1, raw.Length - 2)` THREW on a
    /// shorter span, and a throw is precisely what a caller holding a `Result`
    /// has no way to handle.
    let private unquote (raw: string) : string =
        let inner =
            if raw.Length < 2 then
                raw
            else
                raw.Substring(1, raw.Length - 2)

        let sb = System.Text.StringBuilder()
        let mutable i = 0

        let hexDigit (c: char) : int =
            if c >= '0' && c <= '9' then int c - int '0'
            elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
            elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
            else -1

        while i < inner.Length do
            if inner[i] = '\\' && i + 1 < inner.Length then
                match inner[i + 1] with
                | '"' ->
                    sb.Append '"' |> ignore
                    i <- i + 2
                | '\\' ->
                    sb.Append '\\' |> ignore
                    i <- i + 2
                | '/' ->
                    sb.Append '/' |> ignore
                    i <- i + 2
                | 'n' ->
                    sb.Append '\n' |> ignore
                    i <- i + 2
                | 'r' ->
                    sb.Append '\r' |> ignore
                    i <- i + 2
                | 't' ->
                    sb.Append '\t' |> ignore
                    i <- i + 2
                | 'b' ->
                    sb.Append '\b' |> ignore
                    i <- i + 2
                | 'f' ->
                    sb.Append '\f' |> ignore
                    i <- i + 2
                | 'u' when i + 5 < inner.Length ->
                    // Manual nibble parse — Fable rejects the
                    // NumberStyles.HexNumber + provider overload.
                    let d0 = hexDigit inner[i + 2]
                    let d1 = hexDigit inner[i + 3]
                    let d2 = hexDigit inner[i + 4]
                    let d3 = hexDigit inner[i + 5]

                    if d0 >= 0 && d1 >= 0 && d2 >= 0 && d3 >= 0 then
                        sb.Append(char ((d0 <<< 12) ||| (d1 <<< 8) ||| (d2 <<< 4) ||| d3)) |> ignore
                    else
                        sb.Append(inner.Substring(i, 6)) |> ignore

                    i <- i + 6
                | other ->
                    sb.Append other |> ignore
                    i <- i + 2
            else
                sb.Append inner[i] |> ignore
                i <- i + 1

        sb.ToString()

    /// A raw value span unescaped as a JSON string, or `None` when the span is
    /// not a string at all. The `None` case is the load-bearing one (Phase
    /// 1525): the pre-1525 decoder handed every span straight to `unquote`,
    /// which strips a leading and a trailing character unconditionally — so a
    /// numeric `promptId` of `123` decoded to the string `"2"` and a truncated
    /// span decoded to whatever was left. A field that is not a string is a
    /// malformed envelope, and the caller says so rather than reading it.
    let private tryUnquote (raw: string) : string option =
        let t = raw.Trim()

        if t.Length >= 2 && t[0] = '"' && t[t.Length - 1] = '"' then
            Some(unquote t)
        else
            None

    /// Map of top-level key → raw value span for a canonical envelope object,
    /// or a named `Error` when the input is not one (Phase 1525). Every scanner
    /// result is checked before it indexes or slices, so a truncated envelope
    /// refuses by name instead of throwing or yielding a partial field map that
    /// the decoder above would read as a plausible record.
    let private tryTopLevelFields (json: string) : Result<Map<string, string>, string> =
        let s = json.Trim()

        if s.Length < 2 || s[0] <> '{' || s[s.Length - 1] <> '}' then
            Error "StreamEntry.decode: the envelope is not a complete JSON object (truncated or malformed input)"
        else
            let mutable i = 1
            let mutable fields = Map.empty
            let mutable failure = None

            while failure.IsNone && i < s.Length && s[i] <> '}' do
                if s[i] = '"' then
                    let keyEnd = scanString s i

                    if keyEnd < 0 then
                        failure <- Some "StreamEntry.decode: a field name is not a terminated string (truncated input)"
                    elif keyEnd >= s.Length || s[keyEnd] <> ':' then
                        failure <-
                            Some
                                "StreamEntry.decode: a field name is not followed by ':' (truncated or malformed input)"
                    else
                        let valStart = keyEnd + 1 // past ':'
                        let valEnd = scanValue s valStart

                        if valEnd < 0 then
                            failure <-
                                Some "StreamEntry.decode: a field value is truncated or malformed (it does not close)"
                        else
                            let key = unquote (s.Substring(i, keyEnd - i))
                            fields <- Map.add key (s.Substring(valStart, valEnd - valStart)) fields

                            i <-
                                if valEnd < s.Length && s[valEnd] = ',' then
                                    valEnd + 1
                                else
                                    valEnd
                else
                    i <- i + 1

            match failure with
            | Some e -> Error e
            | None when i = s.Length - 1 && s[i] = '}' -> Ok fields
            | None ->
                // The walk ran off the end without reaching the object's OWN
                // closing brace. This is the truncation the shape check above
                // structurally cannot see: a prefix cut immediately after a
                // nested object still ENDS in `}` and can carry every required
                // field, so it would otherwise decode to a perfectly plausible
                // record that nobody ever wrote.
                Error "StreamEntry.decode: the envelope object does not close (truncated input)"

    let private decodeResult (raw: string) : Result<OpResultEnvelope, string> =
        match tryTopLevelFields raw with
        | Error e -> Error e
        | Ok f ->
            match Map.tryFind "kind" f with
            | None -> Error "StreamEntry.decode: the result envelope has no 'kind'"
            | Some kindRaw ->
                match tryUnquote kindRaw with
                | None -> Error "StreamEntry.decode: the result envelope's 'kind' is not a string"
                | Some "success" -> Ok OpResultEnvelope.Success
                | Some "failure" ->
                    match
                        Map.tryFind "code" f |> Option.bind tryUnquote,
                        Map.tryFind "message" f |> Option.bind tryUnquote
                    with
                    | Some c, Some m -> Ok(OpResultEnvelope.Failure(c, m))
                    | _ -> Error "StreamEntry.decode: a failure result must carry both 'code' and 'message' as strings"
                | Some k -> Error(sprintf "StreamEntry.decode: unrecognised result kind %s" k)

    /// Parse an envelope produced by `encode` back to a `StreamEntry`.
    /// `decodeOp` is the host's op decoder for the nested `op` payload — the
    /// `'Msg` shape is host-owned, exactly as for the sinks' `IOpJsonCodec`.
    ///
    /// The format version is CHECKED, not merely readable: an envelope tagged
    /// with a version this host does not implement is refused by name rather
    /// than half-parsed into a plausible-looking record. That is the whole point
    /// of `chainFormatVersion` leading the pre-image (see `formatVersion`) — a
    /// reader that silently accepted an unknown `v` would give the self-describing
    /// format nothing to describe.
    ///
    /// Round-trips `encode` exactly: `decode d (encode e) = Ok e` for any entry
    /// whose `Timestamp` is on a whole second, `d` inverting
    /// `CanonicalJson.encodeOp`. The second granularity is the ENCODER's (`ts` is
    /// unix seconds, matching the pre-406 pre-image resolution), not a decode
    /// loss — sub-second precision never reaches the wire and so is never in the
    /// hash either.
    ///
    /// TOTAL (Phase 1525): truncated or malformed input is always a named
    /// `Error`. Not a throw — the signature promises a `Result`, and a caller
    /// reading a partial record off disk or a bounded transport has no reason to
    /// wrap the call — and not a mis-read record either, which is the worse of
    /// the two failures: a `StreamEntry` is the chain PRE-IMAGE, so a record
    /// assembled from a mis-spanned field would re-hash to something no other
    /// host can reproduce, and the break would surface later as a chain error
    /// nowhere near the truncation that caused it.
    let decode<'Msg>
        (decodeOp: string -> Result<TreeOp<'Msg>, string>)
        (encoded: string)
        : Result<StreamEntry<'Msg>, string> =
        let decodeFields (fields: Map<string, string>) : Result<StreamEntry<'Msg>, string> =
            let version =
                match Map.tryFind "v" fields with
                | Some raw ->
                    match System.Int32.TryParse(raw.Trim()) with
                    | true, n -> Some n
                    | _ -> None
                // A tagless envelope is the pre-406 v1 format (see `formatVersion`).
                | None -> Some 1

            match version with
            | None -> Error "StreamEntry.decode: the 'v' field is not an integer"
            | Some v when v <> chainFormatVersion ->
                Error(
                    sprintf
                        "StreamEntry.decode: unsupported chain format version %d (this host implements %d)"
                        v
                        chainFormatVersion
                )
            | Some _ ->
                match Map.tryFind "op" fields, Map.tryFind "ts" fields, Map.tryFind "result" fields with
                | Some opRaw, Some tsRaw, Some resultRaw ->
                    match decodeOp opRaw with
                    | Error e -> Error(sprintf "StreamEntry.decode: op decode failed: %s" e)
                    | Ok op ->
                        match System.Int64.TryParse(tsRaw.Trim()) with
                        | false, _ -> Error(sprintf "StreamEntry.decode: 'ts' is not an integer: %s" (tsRaw.Trim()))
                        | true, unixSeconds ->
                            match decodeResult resultRaw with
                            | Error e -> Error e
                            | Ok envelope ->
                                // `promptId` is `null` or a STRING. Anything else is a
                                // malformed envelope, refused rather than stripped of
                                // its first and last character (Phase 1525).
                                let promptId =
                                    match Map.tryFind "promptId" fields with
                                    | Some raw when raw.Trim() = "null" -> Ok None
                                    | Some raw ->
                                        match tryUnquote raw with
                                        | Some p -> Ok(Some p)
                                        | None -> Error "StreamEntry.decode: 'promptId' is neither null nor a string"
                                    | None -> Ok None

                                match promptId with
                                | Error e -> Error e
                                | Ok promptId ->
                                    Ok
                                        { Op = op
                                          Timestamp = DateTimeOffset.FromUnixTimeSeconds unixSeconds
                                          PromptId = promptId
                                          ResultEnvelope = envelope }
                | _ -> Error "StreamEntry.decode: missing one of the required fields (op/ts/result)"

        tryTopLevelFields encoded |> Result.bind decodeFields

    /// The certified host-side SHA-256 `HashFn` (Phase 405) supplied to Core's
    /// chain — `sha256(prev | payload)`, mirroring Core's `defaultHash` shape but
    /// cryptographic. Calls `Fuaran.UI.Hashing` directly (not `HashChain`, which
    /// compiles after this file) to keep the module order acyclic.
    let hashFn: Fuaran.Core.HashFn =
        fun prev payload -> Fuaran.UI.Hashing.sha256Hex (prev + "|" + payload)

    let private coreActor (a: Actor) : Fuaran.Core.Actor =
        match a with
        | Human id -> Fuaran.Core.Actor.Human id
        | Agent(model, version, id) -> Fuaran.Core.Actor.Agent(model, version, id)

    /// The single chain-format authority (Phase 406, seq basis aligned Phase 411):
    /// the hash for one record, over Core's canonical `{seq,actor,op}` payload
    /// (`op = encode entry`) with the SHA-256 `HashFn`. `seq0` is **Core's 0-based
    /// record index** — the domain's public 1-based `Sequence` minus one; the
    /// mapping lives in `HashChain.computeHash` (the UI-facing authority), so a
    /// UI record maps to a `Core.OpRecord` with `Seq = Sequence - 1` and
    /// `Core.OpStream.firstChainBreakWith` verifies the chain directly (F14
    /// resolved: the pre-image speaks Core's basis; the API keeps the domain's).
    let chainHash (previousHash: string) (seq0: int) (actor: Actor) (entry: StreamEntry<'Msg>) : string =
        let payload =
            Fuaran.Core.OpStream.canonicalConfig.Payload seq0 (coreActor actor) (encode entry)

        hashFn previousHash payload

    /// The provenance envelope of a persisted record — the exact `'Op` its chain
    /// hash was computed over.
    let ofRecord (r: OpRecord<'Msg>) : StreamEntry<'Msg> =
        { Op = r.Op
          Timestamp = r.Timestamp
          PromptId = r.PromptId
          ResultEnvelope = r.ResultEnvelope }

    /// Project a domain record onto Core's record shape (Phase 411): the envelope
    /// as the opaque op, the actor mapped, and `Seq = Sequence - 1` (Core's
    /// 0-based index vs the domain's 1-based presentation). `StreamId` is the
    /// sink partition key and does not ride the Core record.
    let toCoreRecord (r: OpRecord<'Msg>) : Fuaran.Core.OpRecord<StreamEntry<'Msg>> =
        { Seq = r.Sequence - 1
          Actor = coreActor r.Actor
          Op = ofRecord r
          PrevHash = r.PreviousHash
          Hash = r.Hash }

    let private uiActor (a: Fuaran.Core.Actor) : Actor =
        match a with
        | Fuaran.Core.Actor.Human id -> Human id
        | Fuaran.Core.Actor.Agent(model, version, id) -> Agent(model, version, id)

    /// The inverse of `toCoreRecord`: rehydrate a domain `OpRecord` from Core's
    /// record shape.
    ///
    /// `streamId` is a **sidecar**, not a decode loss. Core's record deliberately
    /// does not carry one, because the domain's `StreamId` is the sink's
    /// PARTITION KEY — host naming rather than provenance — and is outside the
    /// chain pre-image for exactly that reason (see `OpRecord`'s hash-coverage
    /// note: it is what lets a guest rebase or a rename keep a verifiable chain).
    /// A reader supplies it from wherever it knew to look for the stream at all.
    ///
    /// `Sequence = Seq + 1` re-bases Core's 0-based record index onto the
    /// domain's 1-based presentation, the mirror of `toCoreRecord`.
    let ofCoreRecord<'Msg> (streamId: string) (r: Fuaran.Core.OpRecord<StreamEntry<'Msg>>) : OpRecord<'Msg> =
        { StreamId = streamId
          Sequence = r.Seq + 1
          PreviousHash = r.PrevHash
          Hash = r.Hash
          Op = r.Op.Op
          PromptId = r.Op.PromptId
          Actor = uiActor r.Actor
          Timestamp = r.Op.Timestamp
          ResultEnvelope = r.Op.ResultEnvelope }

    /// The Core `StreamWitness` over the envelope, parameterised by the host's
    /// op decoder. `Apply` unwraps to the real apply engine; `Encode` is the
    /// pinned envelope encoding; `Decode` is `decode decodeOp`, so the witness
    /// now satisfies the whole `StreamWitness` contract and `Core.OpStream`'s
    /// `toJsonl` / `fromJsonl` are usable over it, not just the verify path.
    ///
    /// The op decoder must be supplied by the caller because `'Msg` is
    /// host-owned — the same reason the sinks take an `IOpJsonCodec`. Callers
    /// that only verify (the chain walk never decodes) use `coreWitness`.
    let coreWitnessWith<'Msg>
        (decodeOp: string -> Result<TreeOp<'Msg>, string>)
        : Fuaran.Core.StreamWitness<StreamEntry<'Msg>, Fuaran.UI.Types.Node<'Msg>, ApplyError> =
        { Apply = fun entry tree -> Fuaran.UI.Ops.Apply.apply entry.Op tree
          Encode = encode
          Decode = decode decodeOp }

    /// The verify-path witness: `Decode` refuses, because nothing on the
    /// verify/replay path decodes (the chain walk re-encodes each record's
    /// already-typed op and compares hashes). A caller that needs decoding —
    /// JSONL consumption — takes `coreWitnessWith` and supplies its op decoder.
    let coreWitness<'Msg> () : Fuaran.Core.StreamWitness<StreamEntry<'Msg>, Fuaran.UI.Types.Node<'Msg>, ApplyError> =
        coreWitnessWith (fun _ ->
            Error "StreamEntry.coreWitness: this witness does not decode — use coreWitnessWith <your op decoder>")
