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
//  culture-sensitive formatting — and the multiply goes through `mul32`,
//  because a naive `uint32 *` does NOT survive Fable's float-backed numerics
//  (see the comment on `mul32`; parity is measured by
//  `tests/ids-parity-probe/`, not asserted).
//
//  `randomCorrelationId` is the documented escape hatch for the rare call
//  site that genuinely wants a fresh non-deterministic id (e.g. a host that
//  correlates individual render *instances* of an otherwise-identical tree).
//  The default render path uses the deterministic form; determinism is the
//  default, not a dead end.
// ============================================================================

/// 32-bit wrapping multiply that stays exact under Fable's float-backed
/// numerics. Fable emits `uint32 *` as a plain JS multiply on doubles, and
/// `hash * 16777619u` reaches ~3.6e16 — past the 2^53 exact-integer ceiling —
/// so precision is lost INSIDE the operation and a trailing
/// `&&& 0xFFFFFFFFu` cannot recover it (by then the low bits are gone).
/// Measured: FNV-1a of "a" is e40c292c on .NET and e40c2930 under a
/// naive-multiply Fable lowering.
///
/// The fix is to never form a product above 2^32. Split both operands into
/// 16-bit halves: the `aHi*bHi` term is a multiple of 2^32 and vanishes mod
/// 2^32, the cross terms contribute only their low 16 bits (masked BEFORE
/// recombining — the sum wraps at 2^32 on .NET and does not in JS, and the
/// surviving low 16 bits agree either way), and recombination is `* 65536u`,
/// never `<<<`, so no signed-shift semantics enter. On .NET every step is
/// ordinary `uint32` arithmetic and both masks are no-ops, so .NET values
/// are unchanged by this transform. Pattern from the public `Fuaran.Core`
/// `Hash.mul32` (Apache-2.0). **Re-run `tests/ids-parity-probe/` if you
/// touch this** — a compile gate cannot disagree about a number, so only the
/// value probe catches this class.
let inline private mul32 (a: uint32) (b: uint32) : uint32 =
    let aLo = a &&& 0xFFFFu
    let aHi = a >>> 16
    let bLo = b &&& 0xFFFFu
    let bHi = b >>> 16
    let cross = ((aLo * bHi) + (aHi * bLo)) &&& 0xFFFFu
    ((aLo * bLo) + (cross * 65536u)) &&& 0xFFFFFFFFu

/// Deterministic short correlation id derived from `seed` (FNV-1a 32-bit →
/// 8 lowercase hex chars). The same seed always produces the same id, so a
/// tree that fails the same way at the same node renders byte-identical
/// output — cache-stable + hydration-parity-safe. Fable-portable — measured,
/// not asserted: the multiply goes through `mul32` (read its comment before
/// simplifying the loop), and `tests/ids-parity-probe/` byte-compares the
/// two pipelines over a corpus.
let deterministicCorrelationId (seed: string) : string =
    // FNV-1a 32-bit. Offset basis 2166136261, prime 16777619. On .NET the
    // multiply is ordinary wrapping `uint32` arithmetic; under Fable it is
    // not — hence `mul32`.
    let mutable hash = 2166136261u

    for ch in seed do
        hash <- hash ^^^ uint32 (int ch)
        hash <- mul32 hash 16777619u

    sprintf "%08x" hash

/// Escape hatch: a fresh, non-deterministic 8-char correlation id. The
/// renderer's default path uses `deterministicCorrelationId`; this exists so
/// hosts that want per-render-instance correlation across identical trees have
/// a sanctioned seam rather than re-introducing `Guid.NewGuid()` ad hoc.
let randomCorrelationId () : string =
    System.Guid.NewGuid().ToString("N").Substring(0, 8)

// ============================================================================
//  Per-node ARIA-id vocabulary (Phase 207).
//
//  The composite ids the renderers mint per node per frame so `aria-controls` /
//  `aria-labelledby` / `id` pairs address each other. Historically each renderer
//  spelled them with `sprintf "%s-tab-%d"` at the call site — a runtime
//  format-string parse per tab per frame under Fable, and two spellings that
//  could drift apart across the client / SSR pair the parity corpus locks.
//
//  Each builder is one `inline` concatenation. **Perf primitives: do NOT
//  "simplify" them back to `sprintf`** — the output is byte-identical, so no
//  behavioural test can see the regression; the named builder is the guard
//  (`Fuaran.UI.Tests/HotPathVocabularyTests.fs` asserts the call sites use it).
//
//  `string index` is culture-invariant for `int` on both pipelines (Fable emits
//  a plain JS number-to-string), so these are byte-identical to the `%d` forms
//  they replace.
// ============================================================================

/// `<parentNodeId>-tab-<index>` — a Tabs header's element id, referenced by the
/// matching panel's `aria-labelledby`.
let inline tab (parentNodeId: string) (index: int) : string = parentNodeId + "-tab-" + string index

/// `<parentNodeId>-panel-<index>` — a Tabs panel's element id, referenced by the
/// matching header's `aria-controls`.
let inline panel (parentNodeId: string) (index: int) : string = parentNodeId + "-panel-" + string index

/// `<idNamespace>-opt-<index>` — one option's element id inside a segmented
/// choice / filter group.
let inline optionId (idNamespace: string) (index: int) : string = idNamespace + "-opt-" + string index
