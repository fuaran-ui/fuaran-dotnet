# CodeBlock — the deterministic highlighting tier

**Status:** normative (Phase 1854). This document is the oracle for the
deterministic highlighting tier of `NodeKind.CodeBlock`. The fixture table in §5
is pinned byte for byte by the F# reference implementation
(`src/Fuaran.UI.Renderer.Core/CodeHighlight.fs`, tests in
`src/Fuaran.UI.Tests/CodeHighlightTests.fs`) and by the TypeScript port
(`@fuaran-ui/renderer` `codeHighlight`, tests in
`packages/renderer/test/codeHighlight.test.ts`). A change to either
implementation that moves a byte of §5 is a change to this document first.

It is built the way the `Math` degradation tier was built (see
[MATH-DEGRADATION.md](MATH-DEGRADATION.md)): a small, closed, lexical rule set
that every reference renderer implements identically, so highlighting reaches
server-rendered pages, readers with JavaScript off, and crawled documents, and
stays inside the SSR/CSR parity output.

## 1. What the tier emits

For a `CodeBlock` whose `language` selects a grammar (§3), the renderer emits

```html
<code class="fuaran-codeblock-code language-{x}" data-highlighted="deterministic">…tokens…</code>
```

where `…tokens…` is the source text, HTML-escaped, with the recognised tokens
wrapped in class-only spans:

| Class | Token |
|---|---|
| `tok-kw` | a word in the grammar's keyword table |
| `tok-ty` | a word in the grammar's type table (built-in types, F* effects) |
| `tok-com` | a block or line comment |
| `tok-str` | a string or character literal |
| `tok-num` | a numeric literal |
| `tok-op` | a run of operator characters, or `(*)` |

Everything else (identifiers, whitespace, brackets, `.`, `,`, `;`) is plain
escaped text. The spans carry **no** inline style: colour is the stylesheet's
job (§4). Every other attribute of the CodeBlock (container classes,
`data-language`, `data-highlight-lines`, the copy button) is unchanged from
Phase 290.

A `language` with **no** grammar produces exactly the Phase 290 output: the
escaped text as the `<code>`'s only content and no `data-highlighted`
attribute. Its bytes do not move.

**Escaping.** Text is escaped with the same floor both renderers apply to text
content: `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, and nothing else. A plain run
is therefore byte-identical to what the unhighlighted path would emit for it.

**The client-only seam survives.** A host may still run a richer highlighter
after hydration against `.language-{x}`; `data-highlighted="deterministic"`
tells it the deterministic tier is present, so it can skip the element or
replace its content wholesale.

## 2. The shared lexical core

Both grammars are ML-family and share one lexical core; a grammar contributes
only its tables (§3). The tokeniser walks the source by UTF-16 code unit. It uses
no regular expressions and no platform case tables, so .NET and JavaScript agree
by construction.

### 2.1 Precedence

At each position the first matching rule wins, in this order:

1. `(*)` → one `tok-op` token (the multiplication operator, not a comment).
2. `(*` → a block comment (§2.2).
3. `//` → a line comment (§2.2). This covers `///` doc comments.
4. `"""` → a triple-quoted string, **in grammars that have them** (§2.3).
5. `@"` → a verbatim string, **in grammars that have them** (§2.3).
6. `"` → a string (§2.3).
7. `'` → a character literal if one matches (§2.4), otherwise one plain `'`.
8. an ASCII digit → a number (§2.5).
9. `A-Z`, `a-z` or `_` → a word: letters, digits, `_` and `'` continue it. It is
   `tok-kw` if the keyword table holds it, else `tok-ty` if the type table holds
   it, else plain.
10. an operator character → an operator run (§2.6).
11. anything else → one plain code unit.

Adjacent tokens of the same class are coalesced into one run before emission, so
two touching comments become one span. Emission then applies the per-line rule
(§2.7).

### 2.2 Comments

- **Block comments** open with `(*` and **nest**: inside a comment, `(*` raises
  the depth and `*)` lowers it; the comment ends when the depth returns to zero.
  Inside a comment `(*)` is skipped as a unit and changes no depth. Strings are
  **not** recognised inside comments (a lexical simplification: `(* "*)" *)`
  ends at the first `*)`).
- **Line comments** open with `//` and run to, but not including, the next `\n`.
- An **unterminated** block comment runs to the end of the block.

### 2.3 Strings

- `"…"`: a backslash escapes the next code unit (whatever it is, including `"`
  and `\n`); the string ends at the next unescaped `"`.
- `"""…"""` (F# only): no escapes; ends at the first `"""`.
- `@"…"` (F# only): no backslash escapes; `""` is an escaped quote; ends at the
  next single `"`.
- An **unterminated** string runs to the end of the block. It is still
  tokenised: its text is split per line like any other token (§2.7).

### 2.4 Character literals

At a `'`, a character literal matches if either

- the next unit is neither `\` nor `\n` and the one after it is `'` (`'x'`), or
- the next unit is `\`, and a `'` closes it at offset 3 to 11 from the opening
  quote with no `\n` before it (`'\n'`, `'\u0041'`, `'\U00000041'`).

Otherwise the `'` is one plain code unit — this is what keeps a type variable
(`'a`) and a primed name (`x'`, which the word rule already consumed) out of
`tok-str`.

### 2.5 Numbers

A number starts at an ASCII digit. It continues through letters, digits and `_`
(so suffixes, `0x` hex digits and `0b` binary digits belong to it); through a
`.` only when a digit follows (so `1..10` is a number, a plain `..`, and a
number); and through a `-` or `+` only when it directly follows `e` or `E`, a
digit follows it, and the number does not start `0x` / `0X` (so `1.5e-3` is one
number).

### 2.6 Operators

The operator-character table is

```
!  $  %  &  *  +  -  /  :  <  =  >  ?  @  ^  |  ~  \
```

An operator run is a maximal sequence of these, which is what makes every
multi-character operator a single token — `==>`, `<==>`, `/\`, `\/`, `==`,
`=!=`, `|>`, `->`, `<-`, `::`, `>>=` and any other combination. The run stops
before a `//` (a comment opener) and, in a grammar with verbatim strings, before
an `@"`. `.` is deliberately **not** an operator character (member access would
otherwise dominate the output), and neither are brackets, `,`, `;` or `#`.

### 2.7 The per-line rule

**No span ever contains a line break.** Each token's text is split at `\n`; each
non-empty segment gets its own span, and the `\n` itself is emitted between
them, outside any span. A multi-line comment or string is therefore closed and
reopened at every line boundary, deterministically. This is what lets
line-oriented markup (the `lineNumbers` / `highlightLines` hooks, or a future
per-line wrapper) sit around the tokens without ever cutting through one. `\r`
is an ordinary code unit and stays inside its segment.

### 2.8 Totality

The tokeniser is total: every input, including empty input, unterminated
constructs and lone surrogates, produces output and nothing throws. Removing the
spans and reversing the three escapes always yields the source exactly (the
tests assert this over thousands of generated inputs).

## 3. The grammars

The `language` tag selects a grammar **ASCII-case-insensitively** (only `A`–`Z`
are folded, so no platform case table is consulted):

| Grammar | Tags |
|---|---|
| F* | `fstar`, `fst` |
| F# | `fsharp`, `fs`, `f#` |

Every other tag, including the empty string, selects no grammar.

### 3.1 F*

- **Keywords (`tok-kw`):** `abstract and as assert assert_norm assume attributes
  begin by calc class decreases effect else end ensures exception exists false
  forall friend fun function if in include inline inline_for_extraction instance
  introduce irreducible layered_effect let logic match module new new_effect noeq
  noextract of open opaque private rec reflectable reifiable reify requires
  returns sub_effect then total true try type unfold unopteq val when with`
- **Types (`tok-ty`):** `Div Dv GTot Ghost Lemma ML Pure ST Stack Tot Type Type0
  Type1 bool int list nat option pos prop squash string unit` — the built-in
  types and the effect names, which is where F*'s lexical "type" signal lives.
  `Lemma` is the effect and is here; lower-case `lemma` is not an F* keyword and
  is an ordinary identifier (it commonly prefixes lemma names).
- **Strings:** `"…"` with backslash escapes only.

### 3.2 F#

- **Keywords (`tok-kw`):** `abstract and as assert base begin class const default
  delegate do done downcast downto elif else end exception extern false finally
  fixed for fun function global if in inherit inline interface internal lazy let
  match member module mutable namespace new null of open or override private
  public rec return static struct then to true try type upcast use val when while
  with yield`
- **Types (`tok-ty`):** `array bool byte char decimal double exn float float32 int
  int16 int32 int64 int8 list nativeint obj option sbyte seq single string uint
  uint16 uint32 uint64 uint8 unativeint unit voption`
- **Strings:** `"…"`, `"""…"""` and `@"…"`.

F# is the second grammar because it shares most of F*'s lexical structure; it is
what shows the tokeniser is table-driven rather than hard-wired to F*. A third
grammar is a new pair of tables plus the two string switches, and a new block of
rows in §5.

## 4. Theme

The reference stylesheet colours the six classes with **existing** theme tokens,
read on the code surface (`--fuaran-code-bg`, fallback `#1e1e2e`). No new colour
value is introduced: each rule's fallback is the token's own `:root` value.

| Class | Token | Fallback | Contrast on `#1e1e2e` |
|---|---|---|---|
| `tok-kw` | `--fuaran-tone-brand-border` | `#93c5fd` | 9.10 : 1 |
| `tok-ty` | `--fuaran-tone-success-border` | `#6ee7b7` | 10.76 : 1 |
| `tok-str` | `--fuaran-tone-warning-border` | `#fcd34d` | 11.37 : 1 |
| `tok-num` | `--fuaran-tone-critical-border` | `#fca5a5` | 8.64 : 1 |
| `tok-op` | `--fuaran-tone-brand-hover-border` | `#60a5fa` | 6.45 : 1 |
| `tok-com` | `--fuaran-tone-subdued-hover-border` (+ italic) | `#9ca3af` | 6.46 : 1 |

Every pair clears the 4.5 : 1 text-contrast floor (WCAG 2.x AA, normal text).
The border-family tokens are used because they are the light members of the
palette, which is what reads on the dark code surface; the fg-family tokens are
tuned for light surfaces and would not. A test pins the rules, that each
fallback equals the token's `:root` value, and each ratio. A host that re-binds
`--fuaran-code-bg` to a light surface should override the six rules alongside it.

## 5. Fixture table (normative)

Each row is `language`, the source (as an escaped string literal: `\n` is a line
feed, `\"` a quote, `\\` a backslash), and the exact inner bytes of the `<code>`
element. "No grammar" means the renderer emits the Phase 290 output: the escaped
text, no `data-highlighted`. All samples are synthetic, written for this
document.

| # | `language` | Source | Exact inner bytes |
|---|---|---|---|
| 1 | `fstar` | `val f : x:nat -> Tot nat` | `<span class="tok-kw">val</span> f <span class="tok-op">:</span> x<span class="tok-op">:</span><span class="tok-ty">nat</span> <span class="tok-op">-&gt;</span> <span class="tok-ty">Tot</span> <span class="tok-ty">nat</span>` |
| 2 | `fstar` | `assume val lemma_pos : n:nat -> Lemma (requires n > 0) (ensures n >= 1)` | `<span class="tok-kw">assume</span> <span class="tok-kw">val</span> lemma_pos <span class="tok-op">:</span> n<span class="tok-op">:</span><span class="tok-ty">nat</span> <span class="tok-op">-&gt;</span> <span class="tok-ty">Lemma</span> (<span class="tok-kw">requires</span> n <span class="tok-op">&gt;</span> <span class="tok-num">0</span>) (<span class="tok-kw">ensures</span> n <span class="tok-op">&gt;=</span> <span class="tok-num">1</span>)` |
| 3 | `fstar` | `p ==> q <==> r /\\ s \\/ t =!= u` | `p <span class="tok-op">==&gt;</span> q <span class="tok-op">&lt;==&gt;</span> r <span class="tok-op">/\</span> s <span class="tok-op">\/</span> t <span class="tok-op">=!=</span> u` |
| 4 | `fstar` | `(* outer (* inner *) still outer *) let` — nested comment | `<span class="tok-com">(* outer (* inner *) still outer *)</span> <span class="tok-kw">let</span>` |
| 5 | `fstar` | `(* one\n   two *)\nval x : int` — per-line split | `<span class="tok-com">(* one</span>\n<span class="tok-com">   two *)</span>\n<span class="tok-kw">val</span> x <span class="tok-op">:</span> <span class="tok-ty">int</span>` |
| 6 | `fsharp` | `let s = \"open\nlet t = 1` — unterminated string | `<span class="tok-kw">let</span> s <span class="tok-op">=</span> <span class="tok-str">"open</span>\n<span class="tok-str">let t = 1</span>` |
| 7 | `python` | `if a < b: pass` — unknown language | no grammar: `if a &lt; b: pass`, unchanged |
| 8 | `fsharp` | `/// doc\nlet x = 0x1Fu + 1.5e-3 // tail` | `<span class="tok-com">/// doc</span>\n<span class="tok-kw">let</span> x <span class="tok-op">=</span> <span class="tok-num">0x1Fu</span> <span class="tok-op">+</span> <span class="tok-num">1.5e-3</span> <span class="tok-com">// tail</span>` |
| 9 | `fsharp` | `@\"C:\\dir\" + \"\"\"say \"hi\" \"\"\" + \"a\\\"b\"` | `<span class="tok-str">@"C:\dir"</span> <span class="tok-op">+</span> <span class="tok-str">"""say "hi" """</span> <span class="tok-op">+</span> <span class="tok-str">"a\"b"</span>` |
| 10 | `fsharp` | `let c = 'x' in List.fold (*) 1 [ 'a'; '\\n' ]` | `<span class="tok-kw">let</span> c <span class="tok-op">=</span> <span class="tok-str">'x'</span> <span class="tok-kw">in</span> List.fold <span class="tok-op">(*)</span> <span class="tok-num">1</span> [ <span class="tok-str">'a'</span>; <span class="tok-str">'\n'</span> ]` |
| 11 | `fsharp` | `let id<'a> (x: 'a) = x <> \"<b>\" && true` — escaping, type variables | `<span class="tok-kw">let</span> id<span class="tok-op">&lt;</span>'a<span class="tok-op">&gt;</span> (x<span class="tok-op">:</span> 'a) <span class="tok-op">=</span> x <span class="tok-op">&lt;&gt;</span> <span class="tok-str">"&lt;b&gt;"</span> <span class="tok-op">&amp;&amp;</span> <span class="tok-kw">true</span>` |
| 12 | `fstar` | (empty) | (empty), with `data-highlighted="deterministic"` |
| 13 | `FST` | `open FStar.Mul` — alias, case-folded | `<span class="tok-kw">open</span> FStar.Mul` |
| 14 | `F#` | `module M` | `<span class="tok-kw">module</span> M` |
| 15 | `fstar` | `(* a (* b *)\nlet` — unterminated nested comment | `<span class="tok-com">(* a (* b *)</span>\n<span class="tok-com">let</span>` |
| 16 | `fsharp` | `x+// c` — an operator run stops at a comment | `x<span class="tok-op">+</span><span class="tok-com">// c</span>` |
| 17 | `fsharp` | `[1..10]` — a range is not a float | `[<span class="tok-num">1</span>..<span class="tok-num">10</span>]` |
| 18 | `fsharp` | `let π = \"∀\"` — non-ASCII is plain | `<span class="tok-kw">let</span> π <span class="tok-op">=</span> <span class="tok-str">"∀"</span>` |

In the inner-bytes column `\n` is a literal line feed, and every other character
is literal (the backslashes in rows 3, 9 and 10 are single characters of the
output).

## 6. Which hosts owe the tier

The four **reference renderers** — the F# client (`Fuaran.UI.Renderer`), the F#
server (`Fuaran.UI.Renderer.Server`), the TypeScript client
(`@fuaran-ui/renderer`) and the TypeScript server
(`@fuaran-ui/renderer-server`) — emit it, byte-identically, locked by §5 and by
the SSR parity fixtures on each side.

For **every other conformant host** (the Python, Go and Rust hosts among them)
the tier is **optional**. A host that implements it must reproduce §5 byte for
byte, grammar by grammar; a host that does not keeps the Phase 290 plain escaped
`<code>` for every language, and remains conformant. The CodeBlock row of
`render-fidelity.json` says so explicitly, and adds no render obligation, so no
host's obligation checks go red for lacking the tier. It is optional rather than
obliged because it is a fidelity improvement, not a correctness property: the
plain output carries the same text, and making it mandatory would turn every
grammar added here into a breaking change for every host at once.

## 7. What is deliberately out of scope

- Markdown fenced code blocks keep their Phase 292 output; this tier is the
  `CodeBlock` node's only.
- No semantic highlighting (no name resolution, no distinguishing a type from a
  value by position), and no grammar beyond F* and F#.
- No per-line wrapper elements are introduced: `lineNumbers` and
  `highlightLines` keep their Phase 290 container class and data hooks. The
  per-line rule (§2.7) is what keeps a future wrapper from ever splitting a
  token.
