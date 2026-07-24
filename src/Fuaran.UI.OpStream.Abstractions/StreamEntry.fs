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
//      now folded in (inside `encode`), so attribution AND outcome are
//      tamper-evident.
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
    /// of `encode`). It makes the chain format **self-describing and tamper-evident**:
    /// a host can read `v` from any record's envelope before verifying and reject an
    /// unrecognised version with a clear error (rather than a cryptic hash break), and
    /// because it is inside the pre-image, a stream cannot be silently relabelled. Bump
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
    let formatVersion (encodedEnvelope: string) : int option =
        let prefix = "{\"v\":"

        if encodedEnvelope.StartsWith prefix then
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

    /// The Core `StreamWitness` over the envelope. `Apply` unwraps to the real
    /// apply engine; `Encode` is the pinned envelope encoding (all a verifier
    /// needs); `Decode` is deliberately unimplemented until the JSONL
    /// consumption lands (the Tidy-Up tail — nothing on the verify/replay path
    /// decodes).
    let coreWitness<'Msg> () : Fuaran.Core.StreamWitness<StreamEntry<'Msg>, Fuaran.UI.Types.Node<'Msg>, ApplyError> =
        { Apply = fun entry tree -> Fuaran.UI.Ops.Apply.apply entry.Op tree
          Encode = encode
          Decode = fun _ -> Error "StreamEntry decode ships with the JSONL consumption (Tidy-Up)" }
