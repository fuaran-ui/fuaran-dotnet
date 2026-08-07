namespace Fuaran.UI.OpStream.Abstractions

open System
open Fuaran.UI.Ops.Types

// ============================================================================
//  OpRecord — durable record of one applied TreeOp against one stream.
//
//  The shape is parameterised on 'Msg to match the typed apply engine:
//  apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>.
//  IOpStreamSink<'Msg> binds a sink instance to one 'Msg type per app, the
//  way Elmish dispatch is. Hosts that bridge multiple apps use one sink
//  per app or erase to `obj` at the registration seam.
//
//  Hash chain rule (Phase 320 — typed attested provenance folds the actor in):
//      Hash[n] = SHA-256(PreviousHash[n]
//                        ++ CanonicalJson.encodeOp(Op[n])
//                        ++ Sequence[n].ToString()
//                        ++ Timestamp[n].ToUnixTimeSeconds().ToString()
//                        ++ Actor.encode(Actor[n]))
//      Hash[0]'s PreviousHash is HashChain.genesisPreviousHash (sixty-four '0' chars).
//  The actor is now INSIDE the hash, so re-attributing an op breaks the chain
//  unless the chain is recomputed — attribution is covered by the digest, not
//  merely stored beside it. (Pre-320 the actor lived outside the hash; see
//  docs/migrations/12-Z-op-stream.md for the prior rule.) The chain is an
//  UNKEYED digest, so this detects corruption and accidental re-attribution,
//  NOT an editor who rewrites the records and re-chains them — see CRYPTO.md.
// ============================================================================

/// Who authored an op. The Human/Agent distinction is the load-bearing
/// AI-accountability fact; `model`/`version` on the Agent case doubles as
/// corpus-quality metadata. Mirrors the `Fuaran.Core.OpStream.Actor` contract
/// (fuaran-core a15ad23) so the cross-host hashed pre-image is byte-identical —
/// the canonical encoding is pinned by `Actor.encode`.
type Actor =
    | Human of id: string
    | Agent of model: string * version: string * id: string

module Actor =

    /// Canonical JSON encoding of a string — mirrors `CanonicalJson.appendRawString`
    /// (quote / backslash escaped, control chars as `\u00xx`) so the actor's bytes
    /// are identical across the F# and TypeScript hosts.
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

    /// The canonical wire/hash encoding. Field order is pinned (kind first, then
    /// case fields) and MUST match the TS + Core encoders byte-for-byte.
    let encode (a: Actor) : string =
        match a with
        | Human id -> "{\"kind\":\"human\",\"id\":" + jstr id + "}"
        | Agent(model, version, id) ->
            "{\"kind\":\"agent\",\"model\":"
            + jstr model
            + ",\"version\":"
            + jstr version
            + ",\"id\":"
            + jstr id
            + "}"

    /// The stable attribution id — the user id (Human) or the agent id (Agent).
    let id (a: Actor) : string =
        match a with
        | Human id -> id
        | Agent(_, _, id) -> id

    /// Lift a pre-320 bare-string actor to the typed `Human` case — the migration
    /// path for legacy records / host APIs that still pass a user-id string.
    let ofLegacyString (s: string) : Actor = Human s

    // --- Decoder (for sink persistence round-trips) -------------------------

    /// Read a canonical JSON string starting at `i` (`s[i]` must be '"').
    /// Returns the unescaped value + the index just past the closing quote.
    let private readString (s: string) (i: int) : (string * int) option =
        if i >= s.Length || s[i] <> '"' then
            None
        else
            let sb = System.Text.StringBuilder()
            let mutable j = i + 1
            let mutable finished = false
            let mutable ok = true

            while ok && not finished && j < s.Length do
                let c = s[j]

                if c = '"' then
                    finished <- true
                    j <- j + 1
                elif c = '\\' then
                    if j + 1 >= s.Length then
                        ok <- false
                    else
                        match s[j + 1] with
                        | '"' ->
                            sb.Append '"' |> ignore
                            j <- j + 2
                        | '\\' ->
                            sb.Append '\\' |> ignore
                            j <- j + 2
                        | 'u' when j + 5 < s.Length ->
                            // Manual 4-digit hex parse for the \uXXXX escape.
                            // Fable rejects the NumberStyles.HexNumber + culture
                            // TryParse overload ("provider argument is ignored"),
                            // and the single-arg overload can't read hex — so we
                            // parse the four nibbles directly. Pure char
                            // arithmetic, Fable-safe AND .NET-identical, and it
                            // preserves the graceful-failure (ok <- false) path.
                            let hexDigit (c: char) : int =
                                if c >= '0' && c <= '9' then int c - int '0'
                                elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
                                elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
                                else -1

                            let d0 = hexDigit s[j + 2]
                            let d1 = hexDigit s[j + 3]
                            let d2 = hexDigit s[j + 4]
                            let d3 = hexDigit s[j + 5]

                            if d0 >= 0 && d1 >= 0 && d2 >= 0 && d3 >= 0 then
                                let code = (d0 <<< 12) ||| (d1 <<< 8) ||| (d2 <<< 4) ||| d3
                                sb.Append(char code) |> ignore
                                j <- j + 6
                            else
                                ok <- false
                        | other ->
                            sb.Append other |> ignore
                            j <- j + 2
                else
                    sb.Append c |> ignore
                    j <- j + 1

            if ok && finished then Some(sb.ToString(), j) else None

    /// Extract the value of the first `"<key>":"<string>"` occurrence.
    let private field (key: string) (s: string) : string option =
        let marker = "\"" + key + "\":"
        let idx = s.IndexOf(marker, StringComparison.Ordinal)

        if idx < 0 then
            None
        else
            readString s (idx + marker.Length) |> Option.map fst

    /// Parse a canonical `Actor.encode` string back to a typed `Actor`.
    /// Returns `None` on a malformed payload (the caller decides the fallback).
    let tryDecode (s: string) : Actor option =
        if s.Contains "\"kind\":\"agent\"" then
            match field "model" s, field "version" s, field "id" s with
            | Some m, Some v, Some i -> Some(Agent(m, v, i))
            | _ -> None
        elif s.Contains "\"kind\":\"human\"" then
            field "id" s |> Option.map Human
        else
            None

/// Outcome envelope captured at sink.Append time. v1 only records successful
/// applies — the apply-engine integration fires sink.Append AFTER the
/// `Result.Ok` branch. `Failure` is reserved for the future
/// apply-failure-also-recorded variant so callers don't grow it in a later
/// breaking change. The closed shape keeps `OpResultEnvelope` encodable
/// without a host codec.
[<RequireQualifiedAccess>]
type OpResultEnvelope =
    | Success
    | Failure of code: string * message: string

/// One stream-position's worth of apply trace. Append-only — sinks reject
/// duplicate (StreamId, Sequence) pairs as structural defects.
///
/// `Actor` (Phase 320) replaces the pre-320 bare `UserId: string` and is folded
/// into the hash — see the module header. Hosts that still thread a user-id
/// string lift it with `Actor.ofLegacyString`.
///
/// **Hash coverage (Phase 412 / A4).** Since Phase 406/411 the chain pre-image
/// folds every field of a record EXCEPT `StreamId` — `Op`, `Sequence`, `Actor`,
/// `Timestamp`, `PromptId`, `ResultEnvelope` are all covered by the digest, so a
/// change to any of them is detected on verification (the chain is unkeyed, so
/// that is corruption detection, not evidence against a writer who re-chains —
/// see `CRYPTO.md`). `StreamId`
/// is **deliberately outside the hash**: it is the sink's *partition key* — host
/// naming, not provenance — so the same op history relocated under a new stream
/// id (a guest rebase, a rename, a copy) stays a verifiable chain. This mirrors
/// the DAG's stream-id-independent content address, which is load-bearing for
/// guest convergence. Re-partitioning a stream is a host operation, not a chain
/// edit; keeping `StreamId` out of the hash is a decision, not an omission.
type OpRecord<'Msg> =
    { StreamId: string
      Sequence: int
      PreviousHash: string
      Hash: string
      Op: TreeOp<'Msg>
      PromptId: string option
      Actor: Actor
      Timestamp: DateTimeOffset
      ResultEnvelope: OpResultEnvelope }
