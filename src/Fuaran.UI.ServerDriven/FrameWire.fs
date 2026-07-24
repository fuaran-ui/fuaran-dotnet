module Fuaran.UI.ServerDriven.FrameWire

// ============================================================================
//  Frame wire encoding (Phase 152, Track D).
//
//  The pure, transport-shaped half of the SSE+POST backend: a `Frame` →
//  (a) its JSON body and (b) its full Server-Sent-Events wire form. Pure string
//  work over the existing `DomPatch` / `ClientEffect` encoders — no ASP.NET, no
//  new dependency, Fable-clean, headlessly testable. The ASP.NET streaming
//  handler (the long-lived response, keepalive, flush, the POST receiver, the
//  connection registry) is thin glue around this.
//
//  The shape is the exact contract the generic shim's `sseAdapter` consumes:
//  it listens for the `patch` event and reads `data` as JSON with `seq` /
//  `patches` / `effects` (see `content/fuaran-live-patch.js`). The `id:` line
//  carries the op sequence so `EventSource` replays `Last-Event-ID` on
//  reconnect (→ `LiveConnection.Resync`).
// ============================================================================

/// The frame's JSON body: `{ "seq": N, "patches": [...], "effects": [...] }`,
/// reusing the `DomPatch` / `ClientEffect` list encoders (closure-free,
/// camelCase tagged objects).
let encodeJson (frame: Frame) : string =
    $"""{{"seq":{frame.Seq},"patches":{DomPatch.encodeList frame.Patches},"effects":{ClientEffect.encodeList frame.Effects}}}"""

/// The full SSE wire frame: an `id:` line (the op sequence — the reconnect
/// `Last-Event-ID` key), the `patch` event type, the JSON `data:` line, and the
/// blank line that terminates the event. Spec-correct single-line `data:` (the
/// JSON body has no embedded newlines — the encoders emit compact JSON).
let encodeSse (frame: Frame) : string =
    $"id: {frame.Seq}\nevent: patch\ndata: {encodeJson frame}\n\n"
