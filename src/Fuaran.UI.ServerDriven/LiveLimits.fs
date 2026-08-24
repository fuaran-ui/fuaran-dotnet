module Fuaran.UI.ServerDriven.LiveLimits

// ============================================================================
//  Shared inbound budgets for the live transports (Phase 787).
//
//  Phase 211 capped the SSE+POST body at 1 MB by writing `1L * 1024L * 1024L`
//  into the POST handler. The WebSocket backend, written to the same design,
//  had no cap at all — its fragment accumulator flushed only at `EndOfMessage`,
//  so a client streaming endless fragments grew server memory without bound.
//
//  A number that lives inside one handler cannot be compared with the number
//  inside another, so "the two transports agree" was a claim no test could
//  make and no reader could check. The budget lives here instead: both
//  transports default to it, and the transport-parity test asserts the two
//  defaults are the SAME VALUE rather than merely both plausible.
//
//  Fable-clean (an integer and nothing else) — the client shim's transpile
//  passes straight over it.
// ============================================================================

/// The default cap on ONE inbound live event, in bytes (1 MB).
///
/// A live event is a small JSON envelope — a node id, an event name, a flat
/// payload map — so a megabyte is already generous by three orders of
/// magnitude. It is a denial-of-service floor, not a sizing knob: the point is
/// that an unbounded read is refused, and a host whose events legitimately
/// approach this has a design question rather than a configuration one.
///
/// Each transport expresses exceeding it in its own vocabulary: SSE+POST
/// answers `413 Payload Too Large`; WebSocket closes with `MessageTooBig`
/// (1009). Same budget, same refusal, two protocols.
let defaultMaxInboundBytes: int64 = 1L * 1024L * 1024L
