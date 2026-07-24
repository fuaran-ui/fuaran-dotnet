module Fuaran.UI.ServerDriven.Inbound

#if !FABLE_COMPILER
open System.Text.Json
open Fuaran.UI.ServerDriven.Validation

// ============================================================================
//  Inbound parse — the ONE shared client→server event parser (Phase 152 core;
//  hardened Phase 211).
//
//  Both transport backends (SSE+POST `Fuaran.UI.ServerDriven.AspNetCore`, and
//  the WebSocket backend) receive an untrusted client event JSON body
//  (`{ "nodeId", "event", "payload", "lastSeq" }`) and must turn it into a typed
//  `LiveEvent`. Before Phase 211 each backend carried its own near-identical
//  copy of this parse — so a hardening on one (the WS recv loop already guarded
//  its call site) could silently drift from the other (the SSE POST handler
//  called an *unguarded* `JsonDocument.Parse`, so a non-JSON body threw an
//  unhandled 500). This module is the single seam both backends now share, so
//  they **cannot diverge again** (G1 default-deny by shape, not by discipline).
//
//  The `connId` comes from the server-assigned correlation token (the SSE cookie
//  / the WS accept), NOT the body — the body is untrusted. Server-only .NET, so
//  `System.Text.Json` is fine (never crosses to the browser); the whole module
//  sits under `#if !FABLE_COMPILER`, the same posture as `SessionStore`. Pure +
//  headlessly testable; the ASP.NET / WS handlers are glue around it.
// ============================================================================

/// Map one JSON value to the closure-free `LiveValue` payload subset.
let parseLiveValue (el: JsonElement) : LiveValue =
    match el.ValueKind with
    | JsonValueKind.String -> LiveValue.Str(el.GetString() |> Unchecked.nonNull)
    | JsonValueKind.Number -> LiveValue.Num(el.GetDouble())
    | JsonValueKind.True -> LiveValue.Bool true
    | JsonValueKind.False -> LiveValue.Bool false
    | _ -> LiveValue.Null

/// Read a whole-document root into a `LiveEvent`, stamping the server-assigned
/// `connId`. Missing / mistyped fields degrade to safe empties — the G1 trust
/// boundary (which runs next) rejects an empty/unknown `nodeId` or illegitimate
/// `event`, so a junk body cannot drive the session. `lastSeq` reads through
/// `TryGetInt32` (Phase 211, M1): a JSON number outside the `Int32` range
/// degrades to `0` rather than throwing `System.OverflowException`.
let private fromRoot (connId: string) (root: JsonElement) : LiveEvent =
    let str (name: string) : string =
        match root.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString() |> Unchecked.nonNull
        | _ -> ""

    let lastSeq =
        match root.TryGetProperty "lastSeq" with
        | true, v when v.ValueKind = JsonValueKind.Number ->
            match v.TryGetInt32() with
            | true, n -> n
            | _ -> 0 // out-of-Int32 range → 0 (M1: never throw)
        | _ -> 0

    let payload =
        match root.TryGetProperty "payload" with
        | true, p when p.ValueKind = JsonValueKind.Object ->
            p.EnumerateObject()
            |> Seq.map (fun prop -> prop.Name, parseLiveValue prop.Value)
            |> Map.ofSeq
        | _ -> Map.empty

    { ConnId = connId
      NodeId = str "nodeId"
      Event = str "event"
      Payload = payload
      LastSeq = lastSeq }

/// Parse a client event body into a `LiveEvent`, or `None` when the body is not
/// well-formed JSON (Phase 211, C2 — the default-deny guard). This is the seam
/// **both** transports call: a malformed POST / WS message yields `None` (the
/// handler answers a clean 400 / skips the message) instead of an unhandled
/// exception. A structurally-valid-but-semantically-junk body (missing fields,
/// out-of-range `lastSeq`) is NOT malformed — it parses to a safe-empty
/// `LiveEvent` the G1 boundary then rejects.
let tryParseLiveEvent (connId: string) (json: string) : LiveEvent option =
    try
        use doc = JsonDocument.Parse json
        Some(fromRoot connId doc.RootElement)
    with :? JsonException ->
        None

/// Parse a client event body into a `LiveEvent`, throwing `JsonException` on a
/// malformed body. Retained for callers that have already validated the body is
/// JSON (and for the headless round-trip tests); transport handlers use the
/// guarded `tryParseLiveEvent` so a malformed body cannot throw unhandled.
let parseLiveEvent (connId: string) (json: string) : LiveEvent =
    use doc = JsonDocument.Parse json
    fromRoot connId doc.RootElement
#endif
