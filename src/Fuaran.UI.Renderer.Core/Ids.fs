module Fuaran.UI.Renderer.Ids

// ============================================================================
//  Fuaran — deterministic renderer ids (Phase 138).
//
//  The renderer emits short correlation ids on the `ErrorPayload` records it
//  synthesises when a data-bound component's binding fails to resolve (Metric /
//  Progress / LabelValueRow / Grid / Chart / Map) and on the per-node render
//  guard's fallback marker. Historically these were minted with
//  `Guid.NewGuid().ToString("N").Substring(0, 8)` — fresh every render.
//
//  That nondeterminism defeats two Wave-18 SSR goals: server output must be
//  *cache-stable* (the same tree byte-identical across renders) and
//  *hydration-parity-safe* (the server-emitted markup must match what the
//  client renderer produces so React's `hydrateRoot` doesn't discard it). A
//  Guid-driven `data-fuaran-render-correlation` attribute differs on every
//  render, so identical trees would never produce identical HTML.
//
//  `deterministicCorrelationId` derives the id from a seed string — typically
//  the failing node's id plus a slot discriminator — so the same failure at
//  the same position always yields the same id. FNV-1a (32-bit) over the
//  seed's chars, formatted as 8 lowercase hex digits (same width as the old
//  Guid prefix). Fable-portable: no `System.Security.Cryptography`, no
//  culture-sensitive formatting, only `uint32` arithmetic + `%08x`.
//
//  `randomCorrelationId` is the documented escape hatch for the rare call
//  site that genuinely wants a fresh non-deterministic id (e.g. a host that
//  correlates individual render *instances* of an otherwise-identical tree).
//  The default render path uses the deterministic form; determinism is the
//  default, not a dead end.
// ============================================================================

/// Deterministic short correlation id derived from `seed` (FNV-1a 32-bit →
/// 8 lowercase hex chars). The same seed always produces the same id, so a
/// tree that fails the same way at the same node renders byte-identical
/// output — cache-stable + hydration-parity-safe. Fable-portable.
let deterministicCorrelationId (seed: string) : string =
    // FNV-1a 32-bit. Offset basis 2166136261, prime 16777619. `uint32`
    // arithmetic wraps (F# operators are unchecked by default), matching the
    // reference algorithm; Fable lowers the ops to the equivalent JS 32-bit
    // unsigned semantics.
    let mutable hash = 2166136261u

    for ch in seed do
        hash <- hash ^^^ uint32 (int ch)
        hash <- hash * 16777619u

    sprintf "%08x" hash

/// Escape hatch: a fresh, non-deterministic 8-char correlation id. The
/// renderer's default path uses `deterministicCorrelationId`; this exists so
/// hosts that want per-render-instance correlation across identical trees have
/// a sanctioned seam rather than re-introducing `Guid.NewGuid()` ad hoc.
let randomCorrelationId () : string =
    System.Guid.NewGuid().ToString("N").Substring(0, 8)
