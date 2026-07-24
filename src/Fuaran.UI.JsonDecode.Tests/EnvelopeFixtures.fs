module Fuaran.UI.JsonDecode.Tests.EnvelopeFixtures

// ============================================================================
//  Wire-versioning envelope + tolerance fixture corpus (WIRE_FORMAT §15).
//
//  The §15 version/profile negotiation layer (the `$profile` / `$payload`
//  envelope, `negotiate`, transport-only `Unknown` + must-ignore-but-preserve,
//  `requiredProfile` degrade) is host-neutral substrate in
//  `Fuaran.Core.Wire.Versioning`. This family exercises it as a CROSS-HOST
//  contract: each fixture is an enveloped artifact whose behaviour the F# host
//  proves at emit time (`negotiateReencode` below) and that the TS host
//  re-proves via the conformance kit — so §15.5's multi-host claim is
//  corpus-certified, not asserted.
//
//  The host operation is `negotiateReencode`: read the envelope, negotiate the
//  authored profile against the consumer's own (`core@1.0`), and either
//    - refuse a `Foreign` profile (`FOREIGN_PROFILE` at `$.$profile`), or
//    - tolerantly decode the payload (unknown kinds → transport-only `Unknown`,
//      preserved byte-for-byte) and re-render the whole envelope.
//  It bridges `Fuaran.Core.Wire.Versioning` (the substrate) to the UI host's
//  own `JsonDecode` / `CanonicalJson` codec — the same composition every §15
//  host performs, here demonstrated by the reference F# host.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

// ─── Bridge: Fuaran.Core.Wire.Versioning ↔ the UI node codec ─────────────────

/// Whether the UI decoder knows how to route a node kind — probed against
/// `decodeNodeObj` itself, so it is zero-drift with the decoder's kind set: an
/// unknown top-level kind fails with `WRONG_NODE_KIND`; a known kind (even one
/// missing required fields) fails with some other code, or succeeds.
let private isKnownKind (t: string) : bool =
    let probe = "{\"id\":\"_probe_kind\",\"kind\":{\"$type\":\"" + t + "\"}}"

    match decodeNodeObj probe with
    | Ok _ -> true
    | Error e -> e.Code <> "WRONG_NODE_KIND"

/// Read a node payload's `kind.$type` discriminator off the parsed Core JVal.
let private nodeTagOf (jv: Fuaran.Core.JVal) : Result<string, string> =
    Fuaran.Core.Decode.getProp "kind" jv
    |> Result.bind (Fuaran.Core.Decode.getProp "$type")
    |> Result.bind Fuaran.Core.Decode.asString

/// Decode a known payload JVal through the UI decoder (over its canonical bytes).
let private decodeKnownNode (jv: Fuaran.Core.JVal) : Result<Node<obj>, string> =
    match decodeNodeObj (Fuaran.Core.Canon.render jv) with
    | Ok n -> Ok n
    | Error e -> Error(e.Code + " at " + e.Path)

/// Re-encode a known UI node back to a Core JVal (via its canonical bytes).
let private encodeKnownNode (n: Node<obj>) : Fuaran.Core.JVal =
    match Fuaran.Core.Decode.parse (CanonicalJson.encodeNode n) with
    | Ok jv -> jv
    | Error m -> failwithf "EnvelopeFixtures.encodeKnownNode: %s" m

/// The consumer entry point: read a possibly-versioned envelope, negotiate
/// against the host's own `core@1.0`, and either refuse a `Foreign` profile
/// (`Error (code, path)`) or tolerantly decode + re-render (`Ok bytes`).
/// Shared by the emitter (law proof) and the corpus round-trip suite.
let negotiateReencode (input: string) : Result<string, string * string> =
    match Fuaran.Core.Versioning.parse input with
    | Error _ -> Error("INVALID_JSON", "$")
    | Ok env ->
        match Fuaran.Core.Versioning.negotiate Fuaran.Core.Versioning.Profile.coreV1 env.Profile with
        | Fuaran.Core.Versioning.Foreign _ -> Error("FOREIGN_PROFILE", "$.$profile")
        | _ ->
            match Fuaran.Core.Versioning.decodeTolerant nodeTagOf isKnownKind decodeKnownNode env.Payload with
            | Error _ -> Error("WRONG_TYPE", "$.$payload")
            | Ok decoded ->
                let reenc = Fuaran.Core.Versioning.reencode encodeKnownNode decoded
                Ok(Fuaran.Core.Versioning.render { env with Payload = reenc })

// ─── Fixture data ────────────────────────────────────────────────────────────

/// A canonical known-node wire form, derived through the real codec so it is
/// exactly the bytes the encoder emits (independent of field ordering).
let private knownNode: string =
    let raw =
        """{"id":"m1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"hi"}}}"""

    match decodeNodeObj raw with
    | Ok n -> CanonicalJson.encodeNode n
    | Error e -> failwithf "EnvelopeFixtures: known node failed to decode (%s)" e.Code

// Unknown-kind payloads a newer producer authored — hand-authored in canonical
// (Ordinal-sorted keys) form so a re-render reproduces the input bytes. The UI
// host cannot AUTHOR these (the closed surface); only a Behind reader tolerates
// them.
let private unknownNode =
    """{"id":"h1","kind":{"$type":"hologram"},"shimmer":true}"""

let private unknownNodeReq =
    """{"id":"h1","kind":{"$type":"hologram"},"requiredProfile":"core@1.4","shimmer":true}"""

let private unknownNodeBadReq =
    """{"id":"h1","kind":{"$type":"hologram"},"requiredProfile":"not-a-profile","shimmer":true}"""

/// The `$payload` / `$profile` envelope (payload first — `$payload` sorts before
/// `$profile` under the canonical Ordinal key order).
let private enveloped (payload: string) (profile: string) : string =
    "{\"$payload\":" + payload + ",\"$profile\":\"" + profile + "\"}"

/// The two envelope fixture dispositions: an accept (negotiate → tolerate →
/// re-render byte-identical) or a Foreign refuse.
type EnvelopeExpect =
    | RoundTrip
    | ForeignRefuse

type EnvelopeFixture =
    {
        /// Corpus filename stem (`envelope/<id>.json`) + manifest id.
        Id: string
        /// The enveloped input wire form.
        Enveloped: string
        Expect: EnvelopeExpect
        Description: string
    }

let all: EnvelopeFixture list =
    [ { Id = "envelope-known-current"
        Enveloped = enveloped knownNode "core@1.0"
        Expect = RoundTrip
        Description =
          "enveloped Current (core@1.0) known payload — decode + re-render byte-identical (WIRE_FORMAT 15.1)" }
      { Id = "envelope-unknown-behind"
        Enveloped = enveloped unknownNode "core@1.1"
        Expect = RoundTrip
        Description =
          "Behind (core@1.1) unknown-kind payload — tolerant decode preserves bytes verbatim (WIRE_FORMAT 15.3)" }
      { Id = "envelope-unknown-required-profile"
        Enveloped = enveloped unknownNodeReq "core@1.4"
        Expect = RoundTrip
        Description =
          "Behind unknown kind declaring requiredProfile core@1.4 — preserved + degrade label surfaced (WIRE_FORMAT 15.3)" }
      { Id = "envelope-unknown-malformed-required-profile"
        Enveloped = enveloped unknownNodeBadReq "core@1.2"
        Expect = RoundTrip
        Description =
          "Behind unknown kind with a malformed requiredProfile — no label, bytes still preserved (WIRE_FORMAT 15.3)" }
      { Id = "envelope-foreign-major"
        Enveloped = enveloped knownNode "core@2.0"
        Expect = ForeignRefuse
        Description = "Foreign (major-ahead core@2.0) — hard-refuse, never silently mis-decode (WIRE_FORMAT 15.2)" }
      { Id = "envelope-foreign-namespace"
        Enveloped = enveloped knownNode "music@1.0"
        Expect = ForeignRefuse
        Description = "Foreign (different namespace music@1.0) — hard-refuse (WIRE_FORMAT 15.2)" } ]
