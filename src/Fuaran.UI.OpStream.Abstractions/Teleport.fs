namespace Fuaran.UI.OpStream.Abstractions

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ============================================================================
//  Teleport state bundle — the whole live application as a string (Phase 437).
//
//  A teleport bundle serialises a running Fuaran app — the tree, its
//  `Binding.State` values, an optional bounded op-history window, and the
//  op-chain head hash — into one canonically-encoded, deflate-compressed,
//  base64url string small enough to ride a URL fragment or a QR code, and
//  resume exactly where it was on any device. It extends the Phase 109
//  tree-only permalink (untouched — this is a new, additive artefact) with
//  state + integrity, on the Phase 177 posture that Fuaran interactivity is
//  serialisable data, not closures.
//
//  Format (`FT1.` + base64url(deflate(canonical JSON envelope))):
//
//    { "bundle":   "teleport@1",
//      "chainHead": "<64-hex op-chain head>",     // optional
//      "digest":    "<64-hex integrity digest>",  // required
//      "history":  [ <TreeOp>, … ],               // optional bounded window
//      "state":    { "<key>": <JVal>, … },        // optional
//      "tree":     <Node> }
//
//  The inner form follows WIRE_FORMAT.md §2 (Ordinal-sorted keys, canonical
//  floats, omit-when-empty); `tree` / `history` items are the standard
//  canonical Node / TreeOp wire forms. `digest` is SHA-256 over the canonical
//  envelope *without* the digest field, so any tamper — including a rewritten
//  `chainHead` — fails verification as a typed error. Both encode and decode
//  assemble the digest preimage through the same `Fuaran.Core.Canon.render`
//  path, so verification cannot drift from production.
//
//  FGP 3 — closures cannot exist on the wire. The bundle rides the standard
//  codec: closure-bearing slots encode as the `"<closure>"` sentinel and
//  decode to inert placeholders; only wire-survivable actions (`SetState` /
//  `Notify` / `Navigate` / `AiTool` / `Chain` / `CommitLocal` /
//  `WriteToClipboard` / `Invoke`, + declarative `Call … into`) are live after
//  resume, and every dispatch runs through the host's standard `CanDispatch`
//  gate (Phase 119 / 159) exactly as any decoded tree does. Decode also runs
//  `PreEmitValidate` and refuses a tree whose node identity is broken
//  (duplicate / empty NodeId) — state re-seat is keyed on stable identity.
//
//  Cross-pipeline determinism (FGP 4 discipline): every stage — canonical
//  JSON, SHA-256 (`Fuaran.UI.Hashing`), deflate + base64url
//  (`Fuaran.UI.Compression`) — is the same pure F# on the .NET and Fable
//  pipelines, so the same bundle produces the same string everywhere.
// ============================================================================

/// A teleportable application snapshot: the live tree, the `Binding.State`
/// value map (wire-representable `JVal` values), an optional bounded
/// op-history window, and the op-chain head hash at bundle time.
type TeleportBundle<'Msg> =
    { Tree: Node<'Msg>
      State: Map<string, JVal>
      History: TreeOp<'Msg> list
      ChainHead: string option }

module TeleportBundle =
    /// A bundle carrying only the tree (no state, history, or chain binding).
    let ofTree (tree: Node<'Msg>) : TeleportBundle<'Msg> =
        { Tree = tree
          State = Map.empty
          History = []
          ChainHead = None }

/// Size ceilings the teleport surfaces are budgeted against, in encoded
/// characters (the encoded string is pure ASCII, so chars = bytes).
module TeleportBudget =

    /// QR byte-mode capacity at version 40, error-correction level L — the
    /// hard ceiling for a single QR code.
    [<Literal>]
    let QrMaxBytes = 2953

    /// Comfortable scan density (≈ version 25-L). Above this a QR code
    /// scans poorly on mid-range cameras; prefer truncating history first.
    [<Literal>]
    let QrComfortableBytes = 1273

    /// Practical URL-fragment budget. Browsers accept far more, but shared-
    /// link surfaces (chat apps, mail clients, server logs) get hostile to
    /// URLs beyond a few KB.
    [<Literal>]
    let UrlFragmentBytes = 8000

/// Decode-side resource limits for untrusted input.
type TeleportLimits =
    {
        /// Reject the encoded string before any decompression work.
        MaxEncodedChars: int
        /// Cap the inflated envelope size (the deflate-bomb guard).
        MaxDecodedBytes: int
    }

module TeleportLimits =
    /// Defaults sized generously above every legitimate budget: 64 K encoded
    /// chars (8× the URL budget), 1 MB decoded envelope.
    let defaults: TeleportLimits =
        { MaxEncodedChars = 65536
          MaxDecodedBytes = 1048576 }

/// A typed teleport decode/encode failure — the standard recoverable-envelope
/// discipline (mirrors `DecodeError` / the dispatch-gate refusal shape;
/// never a throw).
[<RequireQualifiedAccess>]
type TeleportError =
    /// The input (or requested output) exceeds a size limit/budget.
    | Oversize of limit: int * message: string
    /// Not a teleport bundle: bad prefix, bad base64url, malformed deflate
    /// stream, or invalid UTF-8.
    | InvalidFormat of message: string
    /// The inflated payload is not valid JSON.
    | InvalidJson of message: string
    /// The envelope object is structurally wrong (missing/mistyped field).
    | InvalidEnvelope of path: string * message: string
    /// The envelope's `bundle` version is not one this decoder supports.
    | UnsupportedVersion of found: string
    /// The carried digest does not match the recomputed one — the bundle
    /// (chain head included) was tampered with or corrupted in transit.
    | DigestMismatch of recomputed: string * carried: string
    /// The `tree` payload failed the standard wire decode.
    | TreeDecode of Fuaran.UI.Ops.JsonDecode.DecodeError
    /// A `history` op failed the standard wire decode.
    | HistoryDecode of index: int * Fuaran.UI.Ops.JsonDecode.DecodeError
    /// The decoded tree fails pre-emit validation on node identity
    /// (duplicate / empty NodeId) — state re-seat would be ambiguous.
    | TreeInvalid of defects: PreEmitValidate.PreEmitDefect list

/// The decoded, validated bundle. `Tree` / `History` are the storage-erased
/// wire shapes (`Node<obj>` / `TreeOp<obj>`, closures inert by design);
/// `Defects` carries any non-fatal `PreEmitValidate` findings.
type DecodedTeleport =
    { Tree: Node<obj>
      State: Map<string, JVal>
      History: TreeOp<obj> list
      ChainHead: string option
      Digest: string
      Defects: PreEmitValidate.PreEmitDefect list }

module Teleport =

    module JsonDecode = Fuaran.UI.Ops.JsonDecode

    /// The self-identifying string-format tag (Fuaran Teleport, format 1:
    /// deflate + base64url). A future compression or framing change mints
    /// `FT2.`; the envelope's own `bundle` field versions the JSON shape.
    [<Literal>]
    let FormatPrefix = "FT1."

    /// The envelope version this codec produces and accepts.
    [<Literal>]
    let Version = "teleport@1"

    [<Literal>]
    let private DigestPreimageTag = "fuaran-teleport:v1|"

    // ── State conversion helpers ────────────────────────────────────────────

    /// Best-effort `StateStore` snapshot → wire state map. Follows the
    /// WIRE_FORMAT §2 rule-11 posture: the recognised primitives (string /
    /// bool / int / float, plus values already `JVal`-shaped — the decoded-
    /// path `SetState` payload) capture; anything else is host-typed content
    /// the wire cannot decompose and is dropped from the bundle.
    let captureState (snapshot: Map<string, obj>) : Map<string, JVal> =
        snapshot
        |> Map.toList
        |> List.choose (fun (k, v) ->
            match v with
            | :? JVal as jv -> Some(k, jv)
            | :? string as s -> Some(k, JStr s)
            | :? bool as b -> Some(k, JBool b)
            | :? int as i -> Some(k, JInt i)
            | :? float as f -> Some(k, JFloat f)
            | _ -> None)
        |> Map.ofList

    /// Decoded state map → the `obj` values a host seats back into its state
    /// store, via the substrate's standard structural lowering
    /// (`JValObj.toObj`: numbers as `float`, arrays as `obj list`, objects as
    /// `Map<string, obj>` — the same shapes the wire decoder's obj path and
    /// the server-driven bounded state store consume).
    let stateValues (state: Map<string, JVal>) : Map<string, obj> =
        state |> Map.map (fun _ v -> JValObj.toObj v)

    // ── Encode ──────────────────────────────────────────────────────────────

    let private reparse (context: string) (canonicalJson: string) : Result<JVal, TeleportError> =
        match Json.parse canonicalJson with
        | Ok v -> Ok v
        | Error e -> Error(TeleportError.InvalidFormat(context + " is not wire-representable: " + e))

    let private historyJVals (ops: TreeOp<'Msg> list) : Result<JVal list, TeleportError> =
        let rec go acc i rest =
            match rest with
            | [] -> Ok(List.rev acc)
            | op :: tail ->
                match reparse (sprintf "history[%d]" i) (CanonicalJson.encodeOp op) with
                | Ok jv -> go (jv :: acc) (i + 1) tail
                | Error e -> Error e

        go [] 0 ops

    let private digestOf (coreFields: (string * JVal) list) : string =
        Hashing.sha256Hex (DigestPreimageTag + Canon.render (JObj coreFields))

    /// Encode `bundle` as a teleport string: canonical envelope → deflate →
    /// base64url, prefixed `FT1.`. Deterministic — the same bundle produces
    /// the same string on every host and pipeline. Fails only when a payload
    /// is not wire-representable (e.g. an integer beyond the wire's int53
    /// range — the same boundary the canonical parser enforces).
    let encode (bundle: TeleportBundle<'Msg>) : Result<string, TeleportError> =
        reparse "tree" (CanonicalJson.encodeNode bundle.Tree)
        |> Result.bind (fun treeJv ->
            historyJVals bundle.History
            |> Result.map (fun historyJv ->
                let coreFields =
                    [ yield "bundle", JStr Version

                      match bundle.ChainHead with
                      | Some head -> yield "chainHead", JStr head
                      | None -> ()

                      if not historyJv.IsEmpty then
                          yield "history", JArr historyJv

                      if not (Map.isEmpty bundle.State) then
                          yield "state", JObj(Map.toList bundle.State)

                      yield "tree", treeJv ]

                let envelope =
                    Canon.render (JObj(("digest", JStr(digestOf coreFields)) :: coreFields))

                FormatPrefix + Base64Url.encode (Deflate.compress (Utf8.encode envelope))))

    /// `encode` under a size budget (encoded characters — see
    /// `TeleportBudget`). Over budget is a typed `Oversize` naming the
    /// overshoot, with the standard remedy: shrink or drop the `history`
    /// window first, then prune `state` keys the tree no longer reads.
    let encodeWithin (budgetChars: int) (bundle: TeleportBundle<'Msg>) : Result<string, TeleportError> =
        encode bundle
        |> Result.bind (fun s ->
            if s.Length > budgetChars then
                Error(
                    TeleportError.Oversize(
                        budgetChars,
                        sprintf
                            "encoded bundle is %d chars, %d over the %d budget — truncate the history window first, then prune unread state keys"
                            s.Length
                            (s.Length - budgetChars)
                            budgetChars
                    )
                )
            else
                Ok s)

    // ── Decode ──────────────────────────────────────────────────────────────

    let private fieldOpt (name: string) (fields: (string * JVal) list) : JVal option =
        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    let private requireStr (name: string) (fields: (string * JVal) list) : Result<string, TeleportError> =
        match fieldOpt name fields with
        | Some(JStr s) -> Ok s
        | Some _ -> Error(TeleportError.InvalidEnvelope("$." + name, "expected a string"))
        | None -> Error(TeleportError.InvalidEnvelope("$." + name, "missing required field"))

    let private decodeHistory (items: JVal list) : Result<TreeOp<obj> list, TeleportError> =
        let rec go acc i rest =
            match rest with
            | [] -> Ok(List.rev acc)
            | jv :: tail ->
                match JsonDecode.decodeOp (Canon.render jv) with
                | Ok op -> go (op :: acc) (i + 1) tail
                | Error e -> Error(TeleportError.HistoryDecode(i, e))

        go [] 0 items

    let private identityBroken (defect: PreEmitValidate.PreEmitDefect) : bool =
        match defect with
        | PreEmitValidate.PreEmitDefect.DuplicateNodeId _
        | PreEmitValidate.PreEmitDefect.EmptyNodeId -> true
        | _ -> false

    /// Decode, integrity-verify, and validate a teleport string produced by
    /// `encode`. The pipeline: size gate → base64url → bounded inflate →
    /// UTF-8 → JSON parse → envelope shape + version → digest verification
    /// (any tamper, chain head included, is `DigestMismatch`) → standard wire
    /// decode of tree + history → `PreEmitValidate` (identity defects refuse;
    /// the rest surface as `Defects`). The decoded tree's closures are inert
    /// placeholders by construction (FGP 3); dispatch of the surviving
    /// wire-survivable actions goes through the host's standard
    /// `CanDispatch` gate exactly as for any decoded tree.
    let decodeWith (limits: TeleportLimits) (encoded: string) : Result<DecodedTeleport, TeleportError> =
        if encoded.Length > limits.MaxEncodedChars then
            Error(
                TeleportError.Oversize(
                    limits.MaxEncodedChars,
                    sprintf "encoded input is %d chars (limit %d)" encoded.Length limits.MaxEncodedChars
                )
            )
        elif not (encoded.StartsWith FormatPrefix) then
            Error(TeleportError.InvalidFormat "not a Fuaran teleport bundle (missing 'FT1.' prefix)")
        else
            let payload = encoded.Substring FormatPrefix.Length

            Base64Url.decode payload
            |> Result.mapError TeleportError.InvalidFormat
            |> Result.bind (fun compressed ->
                Deflate.inflate limits.MaxDecodedBytes compressed
                |> Result.mapError (function
                    | Deflate.InflateError.OutputLimit limit ->
                        TeleportError.Oversize(limit, sprintf "decoded envelope exceeds %d bytes" limit)
                    | Deflate.InflateError.Malformed m -> TeleportError.InvalidFormat("malformed deflate stream: " + m)))
            |> Result.bind (Utf8.decode >> Result.mapError TeleportError.InvalidFormat)
            |> Result.bind (Json.parse >> Result.mapError TeleportError.InvalidJson)
            |> Result.bind (fun envelope ->
                match envelope with
                | JObj fields ->
                    requireStr "bundle" fields
                    |> Result.bind (fun version ->
                        if version <> Version then
                            Error(TeleportError.UnsupportedVersion version)
                        else
                            requireStr "digest" fields)
                    |> Result.bind (fun carried ->
                        let coreFields = fields |> List.filter (fun (k, _) -> k <> "digest")
                        let recomputed = digestOf coreFields

                        if recomputed <> carried then
                            Error(TeleportError.DigestMismatch(recomputed, carried))
                        else
                            let chainHead =
                                match fieldOpt "chainHead" fields with
                                | Some(JStr h) -> Some h
                                | _ -> None

                            let state =
                                match fieldOpt "state" fields with
                                | Some(JObj kvs) -> Map.ofList kvs
                                | _ -> Map.empty

                            let historyItems =
                                match fieldOpt "history" fields with
                                | Some(JArr items) -> items
                                | _ -> []

                            match fieldOpt "tree" fields with
                            | None -> Error(TeleportError.InvalidEnvelope("$.tree", "missing required field"))
                            | Some treeJv ->
                                match JsonDecode.decodeNode (Canon.render treeJv) with
                                | Error e -> Error(TeleportError.TreeDecode e)
                                | Ok wire ->
                                    decodeHistory historyItems
                                    |> Result.bind (fun history ->
                                        let tree = WireTree.reify wire

                                        let defects =
                                            match PreEmitValidate.validate tree with
                                            | Ok() -> []
                                            | Error ds -> ds

                                        if defects |> List.exists identityBroken then
                                            Error(TeleportError.TreeInvalid defects)
                                        else
                                            Ok
                                                { Tree = tree
                                                  State = state
                                                  History = history
                                                  ChainHead = chainHead
                                                  Digest = carried
                                                  Defects = defects }))
                | _ -> Error(TeleportError.InvalidEnvelope("$", "expected a JSON object envelope")))

    /// `decodeWith` under `TeleportLimits.defaults`.
    let decode (encoded: string) : Result<DecodedTeleport, TeleportError> =
        decodeWith TeleportLimits.defaults encoded
