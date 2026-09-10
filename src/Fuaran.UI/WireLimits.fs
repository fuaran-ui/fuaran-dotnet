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

/// Maximum length of a single decoded JSON string, in **Unicode code points**
/// (WIRE_FORMAT §21.6). A surrogate pair counts as ONE, so the bound is a
/// property of the text rather than of .NET's UTF-16 representation: the same
/// document must sit inside or outside this limit on every host, and the two
/// obvious alternatives — UTF-16 code units, UTF-8 bytes — each make it depend
/// on something the author did not choose (the host's string type, or the
/// alphabet the author writes in).
[<Literal>]
let MaxStringLength = 1048576

/// Maximum size of a whole input document, in **UTF-8 bytes**
/// (WIRE_FORMAT §21.7). The five structural limits compose multiplicatively —
/// 100 000 array elements each carrying a maximal string satisfies every one of
/// them and is a hundred gigabytes — so nothing bounded the total until this
/// one. Checked BEFORE the parse: it is one comparison on the input length, so
/// deferring it buys nothing and pays the allocation it exists to refuse.
///
/// UTF-8 bytes rather than code points or .NET string length, deliberately, and
/// it is the one limit here whose unit differs from `MaxStringLength`'s: this
/// bounds the CARRIAGE — what an attacker sends and what the host allocates —
/// and carriage is bytes. Measuring it in UTF-16 units would under-count a CJK
/// document threefold, which is the direction that admits rather than refuses.
///
/// The FIGURE is constrained from below by `MaxNodes`, and the first candidate
/// (8 MiB) was refuted by this repo's own max-nodes test: a document at exactly
/// 100 000 nodes is about 8 MB of small nodes, so an 8 MiB ceiling would have
/// refused a document §21.2 rule 1 requires every host to ACCEPT — quietly
/// lowering `MaxNodes` while leaving its stated value in the table. 32 MiB
/// leaves ~335 bytes per node at the node ceiling, and still refuses the
/// multiplicative blow-up this limit exists for by three orders of magnitude.
[<Literal>]
let MaxDocumentBytes = 33554432

/// Maximum number of elements in a single decoded JSON array, and of members in
/// a single decoded JSON object.
[<Literal>]
let MaxArrayLength = 100000

/// Maximum total node count of a wire tree. The server-driven driver's
/// `InteractionBudget.MaxNodes` is the per-host, per-tenant tightening of this
/// ceiling and defaults far below it; this is the absolute protocol bound.
[<Literal>]
let MaxNodes = 100000

/// Maximum number of `ColExpr` nodes in ONE expression (Fuaran-UI Phase 1534;
/// WIRE_FORMAT §21.8). Counted per expression, not per document: a tree may
/// carry many bounded expressions, with the document as a whole still bounded
/// by `MaxDocumentBytes`.
///
/// Its SCOPE is EVERY expression a decoded document can name (Fuaran-UI Phase
/// 1662): a `Binding.Expr`'s expression, and the `ColExpr` a `Binding.Transform`
/// pipeline embeds — a `derive`'s expression, a `filter`'s predicate. Those two
/// are the whole surface: `Filter` and `Derive` are the only `Fuaran.Core`
/// `Transform` cases carrying a `ColExpr`, and the operand of a `join` / `union`
/// / `intersect` / `except` is a `DataSource`, which is an embedded table or a
/// named ref and never another pipeline.
///
/// Until 1662 the pipeline surface was deliberately UNBOUNDED, and saying so
/// made `MaxExprNodes` bypassable by wrapping the expression in a Transform —
/// the one shape from which a decoded document could still name an unbounded
/// evaluation. Closing it changes what an already-shipped decoder accepts, so
/// the refusal is stated in the specification (§21.8) rather than left to be
/// read off the code: a document past the bound is refused OUTRIGHT on every
/// host, with no profile boundary and no grandfathering, because §21.2 rules 1
/// and 2 admit no second acceptance class.
///
/// ONE budget for both surfaces, not two. The thing bounded is identical — the
/// evaluation named by one `ColExpr` — so a second constant would be one more
/// figure to keep in step across five hosts and would refuse nothing this one
/// does not. And PER EXPRESSION rather than per pipeline: twenty `derive` steps
/// of ten nodes each are twenty cheap evaluations, not one expensive one, so a
/// whole-pipeline sum would refuse that legitimate shape while catching no
/// blow-up this bound misses.
///
/// ONE count, not a count and a depth, because depth ≤ node count for every
/// expression: an expression 600 deep is at least 600 nodes and is already
/// refused, so a second limit would add a number to keep in step across five
/// hosts and refuse nothing the first does not.
///
/// The figure is a protocol bound rather than a budget the evaluator discovers.
/// An expression a person writes is single digits of nodes; the widest thing in
/// the corpus is an `InList` over a literal set, and 512 admits a membership
/// test over ~500 values. What it refuses is the blow-up: a nested `Case` chain
/// deep enough to make evaluation the attack. Changing it is a protocol change
/// — it moves in `WIRE_FORMAT.md` §21 and across the conformant hosts, not here
/// alone.
[<Literal>]
let MaxExprNodes = 512
