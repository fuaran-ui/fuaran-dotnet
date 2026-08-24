# Markdown rendering – the deterministic GFM contract (Phase 292)

Fuaran renders `DisplayKind.Markdown` through **one deterministic GFM → HTML renderer**,
`Fuaran.UI.Renderer.Markdown.toHtml`, which lives in the FSharp.Core-only spine
[`Fuaran.UI.Renderer.Core`](../src/Fuaran.UI.Renderer.Core/Markdown.fs). Because the Fable
**client** renderer (`Fuaran.UI.Renderer`) and the .NET **server** renderer
(`Fuaran.UI.Renderer.Server`) call the *same* function, their markdown output is byte-identical **by
construction** – there is no SSR↔CSR hydration-mismatch surface. The TypeScript
(`@fuaran-ui/renderer`) and Python (`fuaran_py.renderer`) hosts implement the same renderer and are
held byte-identical by a shared conformance corpus (below).

> **Behaviour change (Phase 292) – read this if you relied on the old renderer.** Before Phase 292
> the client used the npm **`marked`** library and the server used **Markdig** – two different
> renders of the same node. Both are now removed. The supported surface is the **documented GFM
> subset** below; markdown features outside it (raw HTML, math, Mermaid, emoji, footnotes, …) no
> longer render as rich HTML – they degrade to escaped-literal text (nothing renders *wrong*, just
> plainly). This is a deliberate, bounded contract, not a regression. There is **no wire-format
> change**: markdown text still rides the wire as a raw `TextSource` and round-trips identically;
> only the *render* changed.

## Target

The renderer targets the **GitHub Flavored Markdown spec** (<https://github.github.com/gfm>) – a
named standard with an official ~600-example test suite. The pragmatic bar is **the GFM feature set,
common-case correct**: implement the features below so the everyday cases match, and let pathological
CommonMark edge cases fall to the escaped-literal fallback. This is a real CommonMark+GFM parser, not
a one-line fallback shim.

## The three buckets

### IN – the GFM spec (rendered)

CommonMark core:

- ATX headings (`#`–`######`) and setext headings (`===` / `---` underlines)
- Paragraphs; thematic breaks (`---`, `***`, `___`)
- Emphasis / strong (`*`, `_`, `**`, `__`) with the flanking + rule-of-three delimiter algorithm
- Inline code spans; fenced code (` ``` ` / `~~~`, with an info-string language); indented code (4 spaces)
- Blockquotes; ordered and unordered lists (with nesting and tight/loose detection)
- Inline links `[text](url "title")` **and reference links** `[text][ref]` / `[ref]` with definitions
- Images `![alt](url)`; `<url>` angle autolinks; hard breaks (two trailing spaces or `\` at EOL) and soft breaks
- Backslash escapes; HTML entities (common named + numeric `&#NN;` / `&#xHH;`)

GFM extensions:

- **Tables** (header row + alignment delimiter `---` / `:--` / `:-:` / `--:`, `|` cell delimiter, `\|` literal-pipe escape, inline markdown in cells)
- **Strikethrough** `~~text~~`
- **Task-list items** `- [ ]` / `- [x]`
- **Bare-URL autolinks** (`http://…`, `https://…`, `www.…` at a word boundary)

### OUT – by design, never in the subset (not deferred)

- **Raw / inline HTML → escaped.** Consistent with GFM's tagfilter, and a security win: no raw-HTML
  passthrough means a far smaller injection surface. `<div>x</div>` renders as the literal text
  `&lt;div&gt;x&lt;/div&gt;`.
- **Engine-dependent extensions – math (`$…$` / KaTeX), Mermaid diagrams.** These need host-specific
  rendering engines, which would produce *different DOM per host* and break the byte-parity this
  renderer exists to guarantee. They live as a `Custom` node or a client-only post-hydration
  enhancement (math: the `Math` node, Phase 293),
  **outside the byte-diff, permanently**. Not a TODO.

### DEFERRED – demand-gated, cheap + safe to add later (escaped-literal until then)

GitHub's beyond-spec extras – emoji shortcodes, footnotes, heading auto-anchors – plus
sub/superscript, definition lists, and the full ~2000-name HTML5 named-entity table (only a common
subset decodes today). Adding any later is a graceful upgrade, not a breaking change.

## Reuse of the Wave 43 render paths (consistency)

- A **fenced code block** emits the same deterministic `<pre><code[ class="language-LANG"]>…escaped…</code></pre>`
  shape as the `CodeBlock` node (Phase 290). Client-only
  syntax highlighting stays a post-hydration enhancement, outside the byte-diff.
- A **GFM table** routes to the same `fuaran-table` / `fuaran-table-header` / `fuaran-table-row` /
  `fuaran-table-cell` class vocabulary as the `Table` `VisKind`, so a markdown table and a `Table`
  node look identical and share its parity coverage. Column alignment adds an `align="left|center|right"`
  attribute (absent when unspecified, so unaligned tables match the `Table` node exactly).

## Canonical output format (the cross-host byte-parity contract)

The HTML string `toHtml` returns is the contract every host reproduces exactly:

- **Text escaping:** `&` `<` `>` `"` only (cmark's set); `'` is left literal.
- Every **top-level block** emits its element followed by a single `\n`. Container blocks
  (blockquote, list, list item) wrap their rendered children, each already carrying its trailing `\n`.
- **No regex anywhere** – regex-engine semantics differ across F#/JS/Python and would be a parity
  hazard; the renderer uses manual scanning only.

## Architecture

- **Home:** `Fuaran.UI.Renderer.Core` (Phase 138 spine; FSharp.Core only, Fable-portable). The F#
  client and server renderers consume it directly → F#-side parity by construction.
- **TS / Python:** `@fuaran-ui/renderer` and `fuaran_py.renderer` implement the same renderer,
  verified byte-identical against the corpus.
- **Sanitization:** the renderer **escapes by construction** – every text run is HTML-escaped, raw
  HTML never passes through, and every link/image URL goes through the scheme floor
  (so `javascript:` collapses to `about:blank`). The `Sanitize.sanitizeMarkdownHtml` contract
  ([SANITIZATION.md](../SANITIZATION.md)) is still applied as defence-in-depth, but its surface is
  now far smaller (no third-party library output to police). See [SANITIZATION.md](../SANITIZATION.md).
- **Destination policy (0.35.0, Phase 1032):** the scheme floor answers *is this URL safe to have*;
  it does not answer *is this destination one the composition declared*. `Markdown.toHtmlWithEgress`
  consults a `Sanitize.EgressPolicy` for every link (`Hyperlink`) and image (`Media`) destination,
  and a refused one renders `about:blank#fuaran-egress-refused` plus a `data-fuaran-egress-refused`
  marker naming the class and the host — never the path or the query. The renderer tiers pass their
  context's policy, whose default denies, so a **decoded** body is covered without a caller opt-in.
  **`Markdown.toHtml` is unchanged** and is the permissive case by definition, for a hand-authored
  body where the author is the trust boundary. The floor's own answer is likewise unchanged: a URL
  it rejects is still the bare `about:blank`, with no marker. Specified language-neutrally in the
  wire format's §14.1; adoption guide in
  [`migrations/1032-markdown-egress.md`](migrations/1032-markdown-egress.md).

## Conformance corpus + the cross-host gate

The corpus lives at [`wire-format-fixtures/markdown/corpus.json`](../../wire-format-fixtures/markdown/corpus.json)
 – a list of `{ id, source, html }` fixtures covering the GFM feature set at the common-case bar. The
F# renderer is the reference:

- **Leg A (F#):** `MarkdownCorpusTests.fs` asserts `Markdown.toHtml source == html` for every
  fixture ⇒ `F# == corpus`.
- **Leg B (cross-host):** the TS and Python renderer test suites run the same corpus ⇒ `TS == corpus`,
  `Py == corpus`.

`F# == corpus` and `TS == corpus` and `Py == corpus` together prove `F# == TS == Py`, byte-for-byte – 
the §11.1-style mechanical enforcement applied to markdown rendering. A one-byte divergence in any
host turns its leg red. Inline `MarkdownTests.fs` additionally pins the contract for standalone
checkouts where the workspace corpus is absent.

## See also

- [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §14 – markdown is render-only; the wire carries the raw `TextSource`.
- [`SSR.md`](SSR.md) – the SSR↔CSR parity posture this renderer slots into.
- [`SANITIZATION.md`](../SANITIZATION.md) – the render-time injection-safety floor.
- Phase 292 – the originating roadmap phase.
