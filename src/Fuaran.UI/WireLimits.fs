// ============================================================================
//  Decode-side resource limits for untrusted wire input (Phase 781).
//
//  WHY THIS EXISTS. The published safety claim is that decoding is *total* — a
//  malformed or hostile input yields a structured, typed error, never an
//  exception or a hang. That claim held on SEMANTICS (every wrong-shaped field
//  is a `DecodeError`) and was false on SHAPE: every tree-walking function in
//  the stack — the hand-rolled JSON parser, the structural decoder, the JVal
//  bridges, `PreEmitValidate`, the server-side renderer — was plainly recursive
//  with no depth counter, so a sufficiently nested payload produced a
//  `StackOverflowException`. In .NET that exception is **uncatchable and
//  unrecoverable**: the process dies, no `Result` is ever returned, and the
//  error envelope the claim promises cannot be produced by construction. These
//  limits are what make the claim true.
//
//  These are the F# host's expression of the normative limits in
//  `WIRE_FORMAT.md` §21. They are protocol limits, not implementation details:
//  a conformant host MUST refuse a payload beyond them with a typed error
//  rather than a throw, and MUST accept one within them.
//
//  ── How MaxDepth was chosen (measured, not guessed) ──────────────────────
//
//  Every walk was bisected for its true overflow depth by running it, with the
//  guards below raised out of the way, on a thread with an explicitly-sized
//  1 MB stack — the .NET default main-thread / thread-pool stack on Windows —
//  in both build configurations. A stack overflow terminates the process, so
//  each probe is its own process and "survived" means exit 0. The figures are
//  the DEEPEST SURVIVING level; the walk dies one level further down.
//
//    walk                                     Release   Debug   unit
//    ---------------------------------------  -------   -----   -----------
//    JsonDecode.tryParse (array nesting)          2095     805   JSON nesting
//    JsonDecode.decodeNodeAst                      186      31   tree nesting
//    JsonDecode.decodeTreeOpAst (nested Batch)       –      ~60  op nesting
//    OpStream CanonicalJson.encodeNode             348       –   tree nesting
//    PreEmitValidate.validate                      294     151   tree nesting
//    Renderer.Server.Render.renderNode              67      30   tree nesting
//
//  Depth scales linearly with stack size — 512K / 1M / 4M gave 31 / 67 / 285
//  for the server renderer — so these are genuine per-frame costs and not an
//  artefact of one stack size.
//
//  The binding constraint is the **server-side renderer** at roughly 15 KB of
//  stack per node level in Release and 34 KB in Debug — `renderKind` is one
//  large function whose frame carries every branch's locals. It survives 67
//  (Release) / 30 (Debug) levels on the default stack.
//
//  `MaxDepth = 24` is the largest round figure that keeps a real safety margin
//  on the tightest walk in BOTH configurations (24 levels against the 30 the
//  Debug renderer survives; a 2.8x margin in Release). A larger figure was
//  rejected deliberately: 32 fits Release comfortably but is *past* the Debug
//  renderer's and Debug structural decoder's budget, which would leave the
//  guard working only in the configuration we ship and not in the one we
//  develop and debug in — a guard with a hole is worse than a smaller limit,
//  because it reads as covered. The Fable/JS pipeline was not directly
//  measured; JS engines give a comparable or smaller budget for frames this
//  size, which argues the same way.
//
//  The Debug margin is the thinnest part of this and is stated rather than
//  smoothed over: 6 frames. It is acceptable because the guard makes 24 the
//  hard ceiling on every walk — a decoded tree cannot be deeper, so nothing
//  downstream ever recurses further — and because production runs Release,
//  where the margin is 43 frames.
//
//  For scale: the deepest tree in the whole shared conformance corpus is 3
//  levels, and a deliberately deep application tree (dashboard > grid > card >
//  stack > tabs > panel > split > disclosure > form > field) reaches about 16.
//
//  ── The other three limits ──────────────────────────────────────────────
//
//  `MaxJsonDepth` bounds SYNTACTIC nesting, which is what the parser sees. It
//  is separate from `MaxDepth` because one tree level costs several JSON levels
//  (a `Box` costs 3: the node object, its `children` array, the child object;
//  worst-case kinds cost about 5), and because a structured JSON payload
//  position (`Custom` props, an action payload) nests freely *within* one node.
//  256 covers a `MaxDepth`-deep tree of any kind shape with payload room to
//  spare, and sits 9x (Release) / 3.4x (Debug) under the parser's own budget.
//
//  `MaxStringLength` and `MaxArrayLength` bound work that is linear rather than
//  recursive — they exist so a single string or array cannot make decode
//  arbitrarily expensive in time and memory once depth is closed off.
//
//  Fable-compatible: literals only, no `System.*`, no reflection.
// ============================================================================
module Fuaran.UI.WireLimits

/// Maximum NODE nesting depth of a wire tree (the root is depth 1). A payload
/// nesting nodes deeper than this is refused with a typed depth error by the
/// decoder, the pre-emit validator, and the server-side renderer. The same
/// figure bounds `TreeOp.Batch` nesting in the op decoder — a different axis
/// counted separately, but held to the same ceiling.
///
/// See the module header for the measurement table this figure is derived from.
/// Changing it is a protocol change: it must move in `WIRE_FORMAT.md` §21 and
/// across the conformant hosts, not here alone.
[<Literal>]
let MaxDepth = 24

/// Maximum SYNTACTIC JSON nesting depth accepted by the parser (the outermost
/// value is depth 1). Bounds the hand-rolled parser and the `Json`->`JVal`
/// bridges, which see JSON structure rather than tree structure.
[<Literal>]
let MaxJsonDepth = 256

/// Maximum length in characters of a single decoded JSON string.
[<Literal>]
let MaxStringLength = 1048576

/// Maximum number of elements in a single decoded JSON array, and of members in
/// a single decoded JSON object.
[<Literal>]
let MaxArrayLength = 100000

/// Maximum total node count of a wire tree. The server-driven driver's
/// `InteractionBudget.MaxNodes` is the per-host, per-tenant tightening of this
/// ceiling and defaults far below it; this is the absolute protocol bound.
[<Literal>]
let MaxNodes = 100000
