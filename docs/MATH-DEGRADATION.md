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
"an equation in running prose" case (`a^2 + b^2 = c^2`, `E = mc^2`, `\frac{a}{b}`)
and, since Phase 1853, the notation of logic and specification
(`\forall x.\ x \in S \Rightarrow f(x) \le c`). Every Phase 1853 addition is a fixed
lookup-table entry or a one-token rule; none adds a LaTeX engine or a parser change
beyond new atom kinds.

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
| brace group (LaTeX grouping) | `{…}` | invisible; one element stands bare, several are one `<mrow>` |
| fraction | `\frac{a}{b}` | `<mfrac>…</mfrac>` |
| Greek letter | `\alpha` … (table below) | `<mi>α</mi>` |
| logic / relation / set operator (Phase 1853) | `\forall`, `\le`, `\in`, … (table below) | `<mo>∀</mo>` |
| pre-escaped comparison (Phase 1853) | `\lt`, `\gt` | `<mo>&lt;</mo>`, `<mo>&gt;</mo>` |
| symbol constant (Phase 1853) | `\top`, `\bot`, `\emptyset` | `<mi>⊤</mi>`, `<mi>⊥</mi>`, `<mi>∅</mi>` |
| italic named term (Phase 1853) | `\mathit{name}` | `<mi mathvariant="italic">name</mi>` |
| upright named term (Phase 1853) | `\mathrm{Name}` | `<mi mathvariant="normal">Name</mi>` |
| text term (Phase 1853) | `\text{word}` | `<mtext>word</mtext>` |
| separator comma (Phase 1853) | `,` | `<mo separator="true">,</mo>` |
| quantifier dot (Phase 1853) | `.` not in a number | `<mo>.</mo>` |
| bar (Phase 1853) | `\|` | `<mo>\|</mo>` |
| bracket group (Phase 1853) | `[a, b]` | `<mrow><mo>[</mo>…<mo>]</mo></mrow>` |
| set-brace group (Phase 1853) | `\{a, b\}` | `<mrow><mo>{</mo>…<mo>}</mo></mrow>` |
| fixed spacing (Phase 1853) | `\,` `\:` `\;` `\ ` `\quad` | `<mspace width="…"></mspace>` |

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

- **Logic, relation and set operators (Phase 1853) – INCLUDED, as a fixed enumerated
  table → `<mo>`.** Aliases map to the same code point. They are sequence items like
  `+`, not atoms, so they take no scripts (`\le^2` is out of subset). The set:

  Logic: `\forall`→∀ (U+2200) `\exists`→∃ (U+2203) `\neg` `\lnot`→¬ (U+00AC)
  `\land` `\wedge`→∧ (U+2227) `\lor` `\vee`→∨ (U+2228) `\Rightarrow` `\implies`→⇒
  (U+21D2) `\Leftrightarrow` `\iff`→⇔ (U+21D4) `\to` `\rightarrow`→→ (U+2192)
  `\mapsto`→↦ (U+21A6) `\vdash`→⊢ (U+22A2).

  Relations and sets: `\le` `\leq`→≤ (U+2264) `\ge` `\geq`→≥ (U+2265) `\ne` `\neq`→≠
  (U+2260) `\equiv`→≡ (U+2261) `\in`→∈ (U+2208) `\notin`→∉ (U+2209) `\subseteq`→⊆
  (U+2286) `\subset`→⊂ (U+2282) `\cup`→∪ (U+222A) `\cap`→∩ (U+2229) `\times`→×
  (U+00D7) `\cdot`→⋅ (U+22C5) `\lt`→`&lt;` `\gt`→`&gt;`.

- **`\top`, `\bot`, `\emptyset` are `<mi>`, not `<mo>` (Phase 1853).** They are
  constants – ordinary symbols, not operators – so they are atoms (and may carry
  scripts), matching how MathML and KaTeX classify them. The candidate list put them
  among the operators; this is the design-time correction.
- **Bare `<` and `>` stay OUT of subset; `\lt` / `\gt` are the in-subset spelling
  (Phase 1853).** The escaping-floor closure below depends on the in-subset alphabet
  containing no `<`, `>` or `&`. `\lt` and `\gt` map to the fixed, pre-escaped table
  entries `<mo>&lt;</mo>` and `<mo>&gt;</mo>` – the ONLY table values that are
  markup-significant.
- **Named terms (Phase 1853) – INCLUDED, one-token rule.** `\mathit{…}`, `\mathrm{…}`
  and `\text{…}` take one brace argument of one or more ASCII letters, digits and
  escaped underscores `\_` (each emitted as `_`), with no whitespace inside – so a
  lemma name is written `\mathit{unregistered\_refused}` and translates to
  `unregistered_refused`. A **bare** `_` in the argument is out of subset: in LaTeX
  (and so in the KaTeX rich tier, which reads the same source) it is a subscript, and
  a MathML tier that read it as a literal would typeset a different expression from
  the tier above it. The candidate list admitted bare `_`; that premise does not hold
  against LaTeX, and this is the design-time correction. Anything else in the
  argument – a space, `-`, a nested command, an empty argument – is out of subset.
  They are atoms, so they may carry scripts.
  `\mathit` emits `mathvariant="italic"` rather than a bare `<mi>`: a multi-character
  `<mi>` renders upright by MathML's own rule, so the bare form would silently lose
  the italic the author asked for. A MathML-Core-only engine that ignores the
  attribute still renders the name legibly, upright.
- **Separator comma and quantifier dot (Phase 1853).** `,` → `<mo separator="true">,</mo>`.
  `.` → `<mo>.</mo>` when it is not part of a number; the written rule separating it
  from the number rule is: the number rule consumes a `.` only when a digit run
  precedes it AND a digit follows it (`3.14`); a `.` the number rule did not consume
  is the quantifier dot, UNLESS a digit directly follows it (`.5`, `x.5`, `1.2.3`),
  which is out of subset rather than silently read as a dot then a number. So `3.` is
  `<mn>3</mn><mo>.</mo>`.
- **Delimiters (Phase 1853).** `[ ]` and `\{ \}` **group like `( )`**: each is an
  atom (so `[a, b]^2` scripts the whole group), its closer must be the matching one,
  and mismatched fences (`[a, b)`, a half-open interval) are out of subset. `|` is a
  **bare operator** `<mo>|</mo>`: an opening and a closing bar are the same character,
  so pairing them would need lookahead the one-token rule forbids, and a script on it
  (`|x|^2`) is out of subset. Inside a group, `<mo>` fences stretch to their content by
  MathML's operator dictionary, with no attribute needed.
- **Fixed spacing (Phase 1853).** `\,` → `0.1667em`, `\:` → `0.2222em`, `\;` →
  `0.2778em` (LaTeX's 3/18, 4/18 and 5/18 em), `\ ` (backslash-space) → `0.3333em`
  (the interword space), `\quad` → `1em`, each emitted as
  `<mspace width="…"></mspace>`. The explicit end tag, not `<mspace …/>`, is the form a
  DOM serialiser writes back, so the bytes survive an HTML parse-and-serialise round
  trip unchanged. `\qquad`, `\!` and any other spacing command are out of subset.
- **Brace groups wrap several elements in one `<mrow>`.** This was the rule as
  written in Phase 658; both implementations emitted a multi-element `{…}` group
  unwrapped, so `x^{n+1}` produced an `<msup>` with four children, which is invalid
  MathML. Phase 1853 brings both tiers to the written rule (fixture rows 35–36). An
  EMPTY brace group (`x^{}`, `a{}b`) is out of subset: it is a missing argument.

Any other `\command` (`\sqrt`, `\int`, `\sin`, `\qquad`, `\mid`, …) is **out of
subset** → source fallback. A command name is the whole run of letters after `\`, so
`\forallx` is the unknown command `forallx`, not `\forall` then `x`. Any character not
named above (`<`, `>`, `&`, `!`, `;`, `:`, a `.` directly followed by a digit outside a
number, …) is **out of subset** → source fallback. A dangling script (`a^`), an
unbalanced or mismatched group (`{a+b`, `(a`, `[a)`, `\{a}`), an empty/whitespace-only
source, and any script whose argument is missing are all out of subset → source
fallback.

**The escaping-floor closure.** The in-subset alphabet contains no `<`, `>`, or `&`,
and the only source characters ever copied into the output are ASCII letters, digits,
`.` (inside a number) and `_` (the unescaped `\_` of a named-term argument) –
everything else the
translator emits comes from its own fixed tables. The emitted MathML therefore never
needs HTML-escaping – the translation is closed under the escaping floor by
construction. The two `\lt` / `\gt` entries do not break the argument: their values
`&lt;` and `&gt;` are fixed, complete character references written by the table, not
by the source, so they decode to text and can never open markup, and no source
character can combine with them (the next emitted byte is always the `<` of `</mo>`).
The renderer's sanitising floor leaves every in-subset payload byte-identical, which
the F# and TS translator tests pin. (The raw source, which *may* contain those
characters, is only ever placed in the `.fuaran-math-source` span text and the
`data-fuaran-math-src` attribute, both of which the renderer's own escaping floor
handles.)

## The deterministic translation (the finite function)

A recursive-descent parse over the source, index `i`, over these routines. On any
failure the whole function returns "out of subset" (the renderer then emits the source
span). It never throws.

- **`command`** – at a `\`: the name is the run of ASCII letters that follows, or, if
  the next character is not a letter, exactly that one character (a control symbol:
  `\,` `\:` `\;` `\ ` `\{` `\}`). A bare trailing `\` has the empty name, which no
  table holds.
- **`parseAtom`** – skip whitespace, then:
  - a digit → consume a run of digits and at most one `.` followed by a digit →
    `<mn>{text}</mn>`.
  - a letter → `<mi>{letter}</mi>` (one letter per `<mi>`; adjacent letters are
    separate identifiers, i.e. implicit multiplication, as in LaTeX).
  - `{` → parse a sequence until `}`; an empty sequence is a failure; a single
    element is returned bare, several are wrapped `<mrow>…</mrow>`; the braces are not
    emitted.
  - `(` → parse a sequence until `)` → `<mrow><mo>(</mo>{sequence}<mo>)</mo></mrow>`.
  - `[` → parse a sequence until `]` → `<mrow><mo>[</mo>{sequence}<mo>]</mo></mrow>`.
  - `\{` → parse a sequence until `\}` → `<mrow><mo>{</mo>{sequence}<mo>}</mo></mrow>`.
  - `\frac` → `<mfrac>{parseAtom}{parseAtom}</mfrac>`.
  - `\mathit` / `\mathrm` / `\text` → skip whitespace, then `{` + one or more of
    `[A-Za-z0-9]` or `\_` (emitted as `_`) + `}` → `<mi mathvariant="italic">…</mi>` /
    `<mi mathvariant="normal">…</mi>` / `<mtext>…</mtext>`.
  - a command naming a Greek entry → `<mi>{unicode}</mi>`; a command naming a symbol
    constant → `<mi>{unicode}</mi>`.
  - anything else → failure.
- **`parseScripted`** – `base = parseAtom`; then, skipping whitespace, consume an
  optional `^`-script and an optional `_`-script (each argument is a bare `parseAtom`,
  in either order, at most one of each): both → `<msubsup>{base}{sub}{sup}</msubsup>`;
  super only → `<msup>{base}{sup}</msup>`; sub only → `<msub>{base}{sub}</msub>`; none
  → `base`.
- **`parseSequence(stop)`** – until end-of-input or an unconsumed `stop` (`}`, `)`,
  `]` or `\}`): skip whitespace; an operator (`+ - * / =`) → its `<mo>`; `,` → the
  separator `<mo>`; `.` → the quantifier dot, or a failure when a digit follows it;
  `|` → `<mo>|</mo>`; a command naming an operator entry → its `<mo>`; a command naming
  a spacing entry → its `<mspace>`; otherwise `parseScripted`. A stray unmatched
  `)`/`}`/`]`/`\}` or a `stop` that is never reached is a failure.
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

Phase 1853 did not move the container shape – only the MathML inside it – so the
enhancement needed no change: it still reads the LaTeX from `data-fuaran-math-src`, not
from the MathML. The TS `enhanceMath` tests pin that a container holding widened-subset
MathML (including the pre-escaped `&lt;`) is replaced wholesale by KaTeX output and that
a second pass leaves it byte-identical.

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
| 16 | `\int_0^1 x \, dx` | `\int` is not in the command set (`\,` is in subset since Phase 1853) |
| 17 | `` (empty / whitespace) | empty body |
| 18 | `f(x) = \sin(x)` | `\sin` is not in the command set |
| 19 | `a^` | dangling superscript – missing script atom |
| 20 | `{a + b` | unbalanced brace group |

### Phase 1853 – in-subset → exact MathML

| # | Source | Display | MathML body (inside `<math …>` … `</math>`) |
|---|---|---|---|
| 21 | `\forall x.\ x \in S \Rightarrow f(x) \le c` | block | `<mo>∀</mo><mi>x</mi><mo>.</mo><mspace width="0.3333em"></mspace><mi>x</mi><mo>∈</mo><mi>S</mi><mo>⇒</mo><mi>f</mi><mrow><mo>(</mo><mi>x</mi><mo>)</mo></mrow><mo>≤</mo><mi>c</mi>` |
| 22 | `\exists n, n \ge 0` | inline | `<mo>∃</mo><mi>n</mi><mo separator="true">,</mo><mi>n</mi><mo>≥</mo><mn>0</mn>` |
| 23 | `p \land q \lor \neg r \iff \top` | inline | `<mi>p</mi><mo>∧</mo><mi>q</mi><mo>∨</mo><mo>¬</mo><mi>r</mi><mo>⇔</mo><mi>⊤</mi>` |
| 24 | `\Gamma \vdash e \mapsto v` | inline | `<mi>Γ</mi><mo>⊢</mo><mi>e</mi><mo>↦</mo><mi>v</mi>` |
| 25 | `A \subseteq B \cup C \cap D` | inline | `<mi>A</mi><mo>⊆</mo><mi>B</mi><mo>∪</mo><mi>C</mi><mo>∩</mo><mi>D</mi>` |
| 26 | `x \notin \emptyset, a \ne b, a \equiv b` | inline | `<mi>x</mi><mo>∉</mo><mi>∅</mi><mo separator="true">,</mo><mi>a</mi><mo>≠</mo><mi>b</mi><mo separator="true">,</mo><mi>a</mi><mo>≡</mo><mi>b</mi>` |
| 27 | `a \lt b \gt c` | inline | `<mi>a</mi><mo>&lt;</mo><mi>b</mi><mo>&gt;</mo><mi>c</mi>` |
| 28 | `2 \times 3 \cdot 4` | inline | `<mn>2</mn><mo>×</mo><mn>3</mn><mo>⋅</mo><mn>4</mn>` |
| 29 | `\mathit{unregistered\_refused}(k) \to \bot` | inline | `<mi mathvariant="italic">unregistered_refused</mi><mrow><mo>(</mo><mi>k</mi><mo>)</mo></mrow><mo>→</mo><mi>⊥</mi>` |
| 30 | `\mathrm{Dom}(f) \subset \text{Keys}` | inline | `<mi mathvariant="normal">Dom</mi><mrow><mo>(</mo><mi>f</mi><mo>)</mo></mrow><mo>⊂</mo><mtext>Keys</mtext>` |
| 31 | `a\,b\:c\;d\quad e` | inline | `<mi>a</mi><mspace width="0.1667em"></mspace><mi>b</mi><mspace width="0.2222em"></mspace><mi>c</mi><mspace width="0.2778em"></mspace><mi>d</mi><mspace width="1em"></mspace><mi>e</mi>` |
| 32 | `[a, b]^2` | inline | `<msup><mrow><mo>[</mo><mi>a</mi><mo separator="true">,</mo><mi>b</mi><mo>]</mo></mrow><mn>2</mn></msup>` |
| 33 | `\{a, b\}` | inline | `<mrow><mo>{</mo><mi>a</mi><mo separator="true">,</mo><mi>b</mi><mo>}</mo></mrow>` |
| 34 | `\|x\| \le 1` | inline | `<mo>\|</mo><mi>x</mi><mo>\|</mo><mo>≤</mo><mn>1</mn>` |
| 35 | `x^{n+1}` | inline | `<msup><mi>x</mi><mrow><mi>n</mi><mo>+</mo><mn>1</mn></mrow></msup>` |
| 36 | `\frac{a+b}{2}` | block | `<mfrac><mrow><mi>a</mi><mo>+</mo><mi>b</mi></mrow><mn>2</mn></mfrac>` |

Every alias of the operator table (`\lnot`, `\wedge`, `\vee`, `\implies`,
`\Leftrightarrow`, `\rightarrow`, `\leq`, `\geq`, `\neq`) is pinned as translating
identically to its partner. Rows 21 and 27/29/30 (a `\lt` beside the named terms) are
also pinned through each tier's server renderer – the F# `SsrParityTests` corpus and a
TS server-against-client check – so the container carries the exact bytes on both
render paths.

### Phase 1853 – out-of-subset neighbours → source fallback (`translate = None`)

| # | Source | Why out of subset |
|---|---|---|
| 37 | `x > y` | bare `>` is not in the alphabet (write `\gt`) |
| 38 | `a & b` | bare `&` is not in the alphabet |
| 39 | `\mathit{a b}` | a named-term argument containing a space |
| 40 | `\mathit{}` | an empty named-term argument |
| 41 | `\text{a-b}` | a named-term argument outside `[A-Za-z0-9]` and `\_` |
| 42 | `\forallx` | the unknown command `forallx` (a command name is the whole letter run) |
| 43 | `.5` | a `.` directly followed by a digit outside a number |
| 44 | `[a, b)` | mismatched fences |
| 45 | `\{a}` | an unclosed set brace |
| 46 | `x^{}` | an empty brace group – a missing script argument |
| 47 | `\|x\|^2` | a script on the bare `\|` operator |
| 48 | `\qquad` | not in the spacing table |
| 49 | `\mathit{a_b}` | a bare `_` in a named-term argument (a subscript in LaTeX; write `\_`) |

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

**Phase 1853 posture – DECISION: unchanged.** The widened subset does not change any
of those hosts: none of them implements the Phase 658 translator yet (each still emits
the raw escaped source for every `Math` node, confirmed against their render tiers when
this phase landed), so there is no narrower subset for them to be out of step with. When
a host adopts the translator it adopts the subset as specified at that time – rows 1–49
of the fixture table, not rows 1–20 – because the subset is one finite function, and a
host implementing an older revision of it would diverge byte-for-byte from F#/TS on
exactly the notation this phase added. That adoption stays a scheduled follow-up per
host, not scope growth here.
