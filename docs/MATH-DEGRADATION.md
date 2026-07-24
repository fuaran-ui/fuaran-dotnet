# MATH-DEGRADATION.md – the no-JavaScript MathML tier for the `Math` primitive

**Status:** normative. This document is the single source of truth for the `Math`
primitive's deterministic, no-JavaScript rendering tier. Every reference renderer
(F# client + server, TypeScript client + server) implements *exactly* the finite
function specified here, and the [fixture table](#fixture-table) is the shared
byte-for-byte oracle pinned in tests on both language tiers.

## Why this tier exists

`Math` carries a LaTeX `Source` string on the wire. Its rendering has three layers:

1. **Rich (client JavaScript):** the KaTeX post-hydration enhancement – full LaTeX,
   the highest fidelity. Opt-in, never part of the parity output.
2. **Deterministic MathML (no JavaScript, *this document*):** for the closed
   expression subset below, the renderers emit native **MathML**, which every modern
   browser lays out with real superscripts, subscripts, and fractions **without
   JavaScript**. This is the tier a script-less surface sees – the Degradation
   Ladder's sandboxed iframe, a crawled document, a reader with JS disabled.
3. **Deterministic source fallback (no JavaScript, unchanged):** for any input
   *outside* the subset, the renderers emit the raw escaped LaTeX source in a
   `.fuaran-math-source` span – exactly as before this tier existed.

The **never-crash rule** is absolute: unparseable or out-of-subset input renders as
the source span, never an error. The translator is a total function.

## The load-bearing constraint – a SUBSET, not a LaTeX engine

A full LaTeX→MathML translator in four renderers is a non-starter (size, divergence
risk). Instead a small, closed, spec'd expression subset has a deterministic
translation; anything the subset cannot express falls back to source. The subset is
deliberately minimal but genuinely useful – it covers the overwhelmingly common
"an equation in running prose" case (`a^2 + b^2 = c^2`, `E = mc^2`, `\frac{a}{b}`).

## The subset grammar

Whitespace (space, tab, newline) is **insignificant** and skipped between tokens – 
matching LaTeX math mode. The following, and *only* the following, are in-subset:

| Construct | LaTeX | MathML |
|---|---|---|
| identifier (one ASCII letter) | `a` … `z`, `A` … `Z` | `<mi>x</mi>` |
| number (digits, optional single `.`) | `42`, `3.14` | `<mn>3.14</mn>` |
| superscript | `x^2`, `x^{n+1}` | `<msup>…</msup>` |
| subscript | `x_i`, `x_{i}` | `<msub>…</msub>` |
| sub + super on one base | `x_i^2` | `<msubsup>…</msubsup>` |
| addition | `+` | `<mo>+</mo>` |
| subtraction | `-` | `<mo>−</mo>` (U+2212 MINUS SIGN) |
| multiplication | `*` | `<mo>⋅</mo>` (U+22C5 DOT OPERATOR) |
| division | `/` | `<mo>/</mo>` |
| equals | `=` | `<mo>=</mo>` |
| parenthesised group | `(a+b)` | `<mrow><mo>(</mo>…<mo>)</mo></mrow>` |
| brace group (LaTeX grouping) | `{…}` | invisible; groups the script/frac argument |
| fraction | `\frac{a}{b}` | `<mfrac>…</mfrac>` |
| Greek letter | `\alpha` … (table below) | `<mi>α</mi>` |

**Decisions recorded here (the phase mandates a final call, made at design time):**

- **Fractions (`\frac`) – INCLUDED.** `\frac{num}{den}` → `<mfrac>{num}{den}</mfrac>`,
  where each argument is a single atom (`{…}` group, a letter, a number, a `\frac`,
  a `(…)` group, or a Greek letter). It is the single highest-value MathML construct
  a browser renders and JavaScript cannot fake without layout, and its translation is
  a two-atom rule with no ambiguity.
- **Greek letters – INCLUDED, as a fixed enumerated table.** A closed lookup, so the
  divergence risk is a static map both tiers pin identically. The set:

  `\alpha`→α `\beta`→β `\gamma`→γ `\delta`→δ `\epsilon`→ε `\zeta`→ζ `\eta`→η
  `\theta`→θ `\iota`→ι `\kappa`→κ `\lambda`→λ `\mu`→μ `\nu`→ν `\xi`→ξ `\pi`→π
  `\rho`→ρ `\sigma`→σ `\tau`→τ `\phi`→φ `\chi`→χ `\psi`→ψ `\omega`→ω
  `\Gamma`→Γ `\Delta`→Δ `\Theta`→Θ `\Lambda`→Λ `\Xi`→Ξ `\Pi`→Π `\Sigma`→Σ
  `\Phi`→Φ `\Psi`→Ψ `\Omega`→Ω

Any other `\command` (`\sqrt`, `\int`, `\sin`, `\,`, …) is **out of subset** → source
fallback. Any character not named above (`<`, `>`, `&`, `,`, `.` not in a number, `!`,
`|`, `[`, `]`, …) is **out of subset** → source fallback. A dangling script (`a^`),
an unbalanced group (`{a+b`, `(a`), an empty/whitespace-only source, and any script
whose argument is missing are all out of subset → source fallback.

Because the in-subset alphabet contains no `<`, `>`, or `&`, the emitted MathML never
needs HTML-escaping – the translation is closed under the escaping floor by
construction. (The raw source, which *may* contain those characters, is only ever
placed in the `.fuaran-math-source` span text and the `data-fuaran-math-src`
attribute, both of which the renderer's own escaping floor handles.)

## The deterministic translation (the finite function)

A recursive-descent parse over the source, index `i`, over these routines. On any
failure the whole function returns "out of subset" (the renderer then emits the source
span). It never throws.

- **`parseAtom`** – skip whitespace, then:
  - a digit → consume a run of digits and at most one `.` followed by a digit →
    `<mn>{text}</mn>`.
  - a letter → `<mi>{letter}</mi>` (one letter per `<mi>`; adjacent letters are
    separate identifiers, i.e. implicit multiplication, as in LaTeX).
  - `{` → parse a sequence until `}`; if the sequence is a single element return it
    bare, else wrap it `<mrow>…</mrow>`; the braces are not emitted.
  - `(` → parse a sequence until `)` → `<mrow><mo>(</mo>{sequence}<mo>)</mo></mrow>`.
  - `\frac` → `<mfrac>{parseAtom}{parseAtom}</mfrac>`.
  - `\`+letters naming a Greek entry → `<mi>{unicode}</mi>`.
  - anything else → failure.
- **`parseScripted`** – `base = parseAtom`; then, skipping whitespace, consume an
  optional `^`-script and an optional `_`-script (each argument is a bare `parseAtom`,
  in either order, at most one of each): both → `<msubsup>{base}{sub}{sup}</msubsup>`;
  super only → `<msup>{base}{sup}</msup>`; sub only → `<msub>{base}{sub}</msub>`; none
  → `base`.
- **`parseSequence(stop)`** – until end-of-input or an unconsumed `stop` char: skip
  whitespace; an operator (`+ - * / =`) → its `<mo>`; otherwise `parseScripted`. A
  stray unmatched `)`/`}` or a `stop` that is never reached is a failure.
- **top level** – `body = parseSequence(none)`. Success iff parsing did not fail, the
  whole source was consumed, and `body` is non-empty. Then:

  ```
  <math xmlns="http://www.w3.org/1998/Math/MathML" display="{block|inline}">{body}</math>
  ```

  `display` is `block` for `MathDisplay.Block`, `inline` for `MathDisplay.Inline`.
  The `<math>` element's children form an inferred `<mrow>`; no extra top-level
  wrapper is emitted (minimal, deterministic bytes).

## The container shape (what each renderer emits)

Both layers share one container, unchanged in class vocabulary from the pre-existing
shape (parity-locked), plus one new deterministic attribute:

- **Block:** `<div class="fuaran-math fuaran-math-block" data-math-display="block"
  data-fuaran-math-src="{source}">…</div>`
- **Inline:** `<span class="fuaran-math fuaran-math-inline" data-math-display="inline"
  data-fuaran-math-src="{source}">…</span>`

The inner content is:

- **in-subset** → the `<math>…</math>` MathML fragment (emitted raw – it rides
  `dangerouslySetInnerHTML` on the client and raw on the server, exactly like the
  `Drawing` inline-SVG builder);
- **out-of-subset** → `<span class="fuaran-math-source">{source}</span>` (today's
  fallback, unchanged).

`data-fuaran-math-src` carries the original LaTeX source so the KaTeX enhancement can
upgrade the MathML variant (whose text content is *not* valid LaTeX). It is emitted in
both variants for one uniform enhancement path.

## KaTeX enhancement (the rich tier) – retargeted

The client-only KaTeX pass (`MathEnhance` in F#, `enhanceMath` in TS) now targets the
**container** (`.fuaran-math:not([data-fuaran-math-done])`) rather than the inner
source span, reads the LaTeX from `data-fuaran-math-src` (falling back to
`textContent`), determines display mode from the `fuaran-math-block` class, and
**replaces the container's content wholesale** with KaTeX output – upgrading *both* the
MathML and the source-fallback variants identically. It marks the container
`data-fuaran-math-done` (attribute unchanged), so it stays idempotent, and it remains
outside every parity comparison (it runs after hydration and is never part of any
renderer's output). The rendered-markdown inline-`$…$` path is unchanged.

## Fixture table

The byte-for-byte oracle. `X` abbreviates
`<math xmlns="http://www.w3.org/1998/Math/MathML" display="…">`. Each in-subset row is
pinned exactly (`translate source display = Some "<math …>…</math>"`) in the F# tests
(`MathMlTests.fs`) and the TS tests (`mathMl.test.ts`); each out-of-subset row is
pinned as the source fallback (`translate = None`).

### In-subset → exact MathML

| # | Source | Display | MathML body (inside `<math …>` … `</math>`) |
|---|---|---|---|
| 1 | `x^2` | inline | `<msup><mi>x</mi><mn>2</mn></msup>` |
| 2 | `a^2 + b^2 = c^2` | block | `<msup><mi>a</mi><mn>2</mn></msup><mo>+</mo><msup><mi>b</mi><mn>2</mn></msup><mo>=</mo><msup><mi>c</mi><mn>2</mn></msup>` |
| 3 | `x^2 + y^2 = z^2` | block | `<msup><mi>x</mi><mn>2</mn></msup><mo>+</mo><msup><mi>y</mi><mn>2</mn></msup><mo>=</mo><msup><mi>z</mi><mn>2</mn></msup>` |
| 4 | `x_i` | inline | `<msub><mi>x</mi><mi>i</mi></msub>` |
| 5 | `x_i^2` | inline | `<msubsup><mi>x</mi><mi>i</mi><mn>2</mn></msubsup>` |
| 6 | `\frac{a}{b}` | block | `<mfrac><mi>a</mi><mi>b</mi></mfrac>` |
| 7 | `\alpha + \beta` | inline | `<mi>α</mi><mo>+</mo><mi>β</mi>` |
| 8 | `(a + b)^2` | block | `<msup><mrow><mo>(</mo><mi>a</mi><mo>+</mo><mi>b</mi><mo>)</mo></mrow><mn>2</mn></msup>` |
| 9 | `3.14` | inline | `<mn>3.14</mn>` |
| 10 | `E = mc^2` | block | `<mi>E</mi><mo>=</mo><mi>m</mi><msup><mi>c</mi><mn>2</mn></msup>` |
| 11 | `a / b` | inline | `<mi>a</mi><mo>/</mo><mi>b</mi>` |
| 12 | `2 * x` | inline | `<mn>2</mn><mo>⋅</mo><mi>x</mi>` |
| 13 | `n - 1` | inline | `<mi>n</mi><mo>−</mo><mn>1</mn>` |

Row 3 is the shared `wire-format-fixtures/nodes/math-1.json` corpus node – from this
phase it renders as MathML across all four renderers. Full row-1 example, in full:
`<math xmlns="http://www.w3.org/1998/Math/MathML" display="inline"><msup><mi>x</mi><mn>2</mn></msup></math>`.

### Out-of-subset → source fallback (`translate = None`)

| # | Source | Why out of subset |
|---|---|---|
| 14 | `\sqrt{2}` | `\sqrt` is not in the command set |
| 15 | `x < y` | `<` is not in the alphabet |
| 16 | `\int_0^1 x \, dx` | `\int` / `\,` not in the command set |
| 17 | `` (empty / whitespace) | empty body |
| 18 | `f(x) = \sin(x)` | `\sin` is not in the command set |
| 19 | `a^` | dangling superscript – missing script atom |
| 20 | `{a + b` | unbalanced brace group |

## Accessibility posture

Native MathML is **more accessible** than the source-span fallback, not less. A
`<math>` element carries an implicit `math` role and structured semantics, so a
screen reader with MathML support (or an assistive layer such as MathJax/AT) announces
`a² + b² = c²` as a spoken equation with correct super/subscript prosody. No extra
ARIA is added to the `<math>` element – the native semantics are the contract, and
bolting on `aria-*` would fight the built-in accessibility tree.

The source-span fallback (out-of-subset input) is read as its literal LaTeX text, the
same posture as before this tier: honest, legible, and never a blank. Neither variant
regresses the pre-existing behaviour; the MathML variant strictly improves it.

`docs/SSR.md`'s `Display.Math` row records the two-tier deterministic behaviour; this
document is the normative expansion it points to.

## Email projection – DECISION: source-only

The `fuaran-live` "Send Me That App" email projection (`app/showcase/Send.fs`) does
**not** adopt MathML; it stays source-only. HTML email is the most hostile render
target in computing, and **MathML support across mail clients is poor and
inconsistent** – Gmail strips unknown elements including `<math>`, and most desktop and
webmail clients render it as either nothing or unstyled fallback text. A blank or
mangled equation is strictly worse than the readable raw LaTeX source. The email walk
therefore treats `Math` like every other rich kind: it degrades to the deterministic
source text. (The current Send-page artefact contains no `Math` node, so this is a
documented no-change; were a `Math` node added, the source-text degradation is the
intended behaviour, and the page's honesty copy already frames the email tier as the
deliberately most-degraded projection.)

## Other hosts – py / go / rs / swift / kt (follow-up, not this phase)

The Python, Go, and Rust reference render tiers, and the Swift/Kotlin render surfaces
over the Rust core, **keep their current `Math` handling this phase** – the raw escaped
source in the `.fuaran-math-{block,inline}` container. Adopting this translator in
those hosts is a deliberate follow-up phase decision (each is a separate conformant
implementation of the same finite function specified here, so the fixture table above
is the ready-made oracle when they do), not silent scope growth. Recording it here so
the divergence is intentional and traceable: F#/TS lead; the others follow when
scheduled.
