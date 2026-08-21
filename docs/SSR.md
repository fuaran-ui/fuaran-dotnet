# Server-side rendering (`Fuaran.UI.Renderer.Server`)

`Fuaran.UI.Renderer.Server` (Phase 140) turns a `Node<'Msg>` tree into an HTML
**string** on plain .NET – no React, no Fable – via `Feliz.ViewEngine`. A
Giraffe / ASP.NET host server-renders Fuaran chrome for SEO surfaces with no
client-runtime requirement and no visual degradation; the same tree later
hydrates client-side (Phase 143).

The renderer reuses the [`Fuaran.UI.Renderer.Core`](../src/Fuaran.UI.Renderer.Core/)
spine (class-name vocabulary, accessibility projection, sanitize, binding
resolution, locale formatting), so the **class names + ARIA attributes** it
emits match the Feliz client renderer for the same tree – the load-bearing
parity property, locked by the Phase 142
conformance corpus.

## Entry points

```fsharp
open Fuaran.UI.Renderer            // BindingResolver, Theme, Defaults
open Fuaran.UI.Renderer.Server

// Body-fragment HTML string (the host owns <html>/<head>/meta/CSS):
let html : string = Render.render BindingResolver.empty tree

// The common no-dynamic-bindings static page — one call, no BindingSources:
let staticHtml : string = Render.renderStatic tree   // = render BindingResolver.empty tree

// With the Theme :root variable block prepended:
let withTheme : string = Render.renderWithTheme Defaults.theme BindingResolver.empty tree

// As a ViewEngine element, to compose into a host's own document layout:
let element = Render.renderToElement BindingResolver.empty tree
```

`Render.render` returns **body-fragment HTML only**. The document shell
(`<html>` / `<head>` / `<meta>` / JSON-LD structured data) and serving the
reference CSS (`Fuaran.UI.Renderer/content/fuaran-reference.css`) stay
host-owned.

## The document-shell boundary

| Concern | Owner |
|---|---|
| `<html>` / `<head>` / `<meta>` / `<title>` / canonical / Open Graph / JSON-LD | **Host** |
| Serving / linking the reference CSS | **Host** |
| The `:root { --fuaran-* }` theme variable block | Fuaran (`renderWithTheme` / `themeStyleElement`) – host mounts it, typically in `<head>` |
| The body-fragment HTML (the Fuaran tree) | Fuaran (`render`) |
| Domain SVG / widgets behind `NodeKind.Custom` | **Host** (Phase 141 server Custom registry) |

## Server binding-resolution table

The server has read-only `BindingSources` and no runtime. Binding resolution
follows the shared `Fuaran.UI.Renderer.BindingResolver`:

| Binding | Server behaviour |
|---|---|
| `Static v` | resolves to `v` |
| `Query name` | resolves against the server-supplied `BindingSources.QueryResults` (the host pre-populates query data before rendering); unresolved ⇒ `NotResolved` |
| `State(key, default)` | resolves to the supplied state or the binding's declared default |
| `Local` | resolves to its `InitialFrom` source (the per-field buffer is a client concern) |
| `Selection` / `Filter` | resolves against the supplied sources, else `NotResolved` |
| `Computed f` | best-effort: runs the closure against `ComputedContext` |
| `Format` / `I18n` | resolves via the shared `Formatting` / i18n resolver (locale formatting is identical to the client) |

### StateBehaviour slots, server-side

The **resolved / loaded branch renders by default**. The server only falls back
to `OnLoading` when a primary binding genuinely does not resolve server-side
(e.g. a `Query` with no server data). `OnError` / `OnEmpty` are not synthesised
server-side (there is no failing async to surface); a data-bound component whose
binding resolves renders its body, and one that does not renders `OnLoading`
when present.

## Per-kind server behaviour

| Kind | Server output |
|---|---|
| **Layout** – Dashboard / Stack / Card / GridLayout / SplitPanel / SummaryList / Stepper | Full structural HTML, identical classes to the client. |
| **Layout.Tabs** | Static `role="tablist"` + the active panel, ARIA-complete. Keyboard nav + tab-switch are client-only (inert server-side). |
| **Layout.Disclosure** | Native `<details>`/`<summary>`; the `open` attribute reflects the resolved `Open` binding (falling back to `DefaultOpen`). |
| **Display** – Heading / Markdown / Metric / Badge / Callout / Progress / Spacer / Skeleton / LabelValueRow | Full structural HTML. `Metric` / `Progress` / `LabelValueRow` render their resolved + formatted value. |
| **Display.Image** (Phase 287) | Real `<img>`; `src` sanitised (`javascript:`/`vbscript:`/`file:`/unknown → `about:blank`); `alt` always emitted; `fuaran-image-avatar`/`-rounded` per variant. |
| **Display.List** (Phase 287) | `<ol>`/`<ul>` (`fuaran-list-ordered`/`-unordered`) of `<li class="fuaran-list-item">`. |
| **Display.Divider** (Phase 287) | `<hr class="fuaran-divider-horizontal">`, a labelled `role="separator"` rule, or a vertical `role="separator" aria-orientation="vertical"` rule. |
| **Display.Toast** (Phase 289) | Overlay contract (below): always emitted, `role="status"` + `aria-live="polite"`, `[hidden]` when `open` is false. |
| **Layout.Modal** (Phase 289) | Overlay contract (below): `fuaran-modal-overlay` → `role="dialog"` + `aria-modal="true"` dialog, `[hidden]` when closed, dismiss/heading inert server-side. |
| **Layout.ScrollArea** (Phase 289) | `fuaran-scrollarea-{axis}` overflow container + `tabindex="0"`; pixel bounds as an inline `max-height`/`max-width` style. |
| **Display.CodeBlock** (Phase 290) | A **deterministic `<pre><code>`** (HTML-escaped, no markdown library) – byte-identical to the client; `data-language` + `language-{x}` class + optional `data-highlight-lines` + an inert copy button. Syntax highlighting is a **client-only** post-hydration enhancement (targets `language-{x}`), outside the parity output. |
| **Display.Math** (Phase 293 / 658) | **Deterministic native MathML** for the closed LaTeX subset (real superscripts/subscripts/fractions with **no JavaScript**), or the **escaped-source fallback** (`fuaran-math-source`) for out-of-subset input – both in a `fuaran-math-block`/`-inline` container carrying `data-math-display` + `data-fuaran-math-src`. Byte-identical across the four renderers, locked by the fixture table in [`MATH-DEGRADATION.md`](MATH-DEGRADATION.md). **KaTeX** upgrades either shape **client-only** post-hydration (targets the `.fuaran-math` container), outside the parity output. |
| **Input.Select (multi)** (Phase 291) | `<select multiple>` (the `multiple` attribute; no scalar `value`), inert server-side; single-select renders unchanged. |
| **Display.Markdown** | **Real HTML via the deterministic GFM renderer** (`.Core` `Markdown.toHtml`, Phase 292) — the *same* module the client renderer runs, so SSR↔CSR parity here is by construction rather than by two engines agreeing. Phase 292 retired the old split (npm `marked` client-side, Markdig server-side); raw HTML is escaped by the renderer and the output still routes through the `.Core` `Sanitize.sanitizeMarkdownHtml` seam as defence-in-depth. See [`MARKDOWN.md`](MARKDOWN.md) for the in/out/deferred buckets. |
| **Display.Sparkline** | The `fuaran-sparkline` hook + an em-dash placeholder (the polyline draws on hydration). |
| **Display.Link** | A **real crawlable `<a href>`** – the no-JS navigation path. `href` is sanitised (`javascript:` / `vbscript:` / raw `data:` collapse to `about:blank`); `rel` / `target` / `download` emit when set. |
| **Input** – Button / Form / Select / FileUpload / Filters | Rendered **inert**: the controls render with their classes + resolved values + `disabled` state, but carry no event handlers. They are dead until client hydration (143). `Button` + `Action.Navigate` renders the button; reach for `Display.Link` for a crawlable destination instead. |
| **Visualisation.Table** | Full HTML `<table>`. |
| **Visualisation** – Chart / Map / DataGrid | A **deterministic placeholder** carrying `data-fuaran-ssr-placeholder` + a row/marker count (never a blank); the client library renders into it on hydration. |
| **ErrorBoundary** | Renders the protected child subtree (the `Fallback` is a client-runtime degradation path; the server has no throws to catch). |
| **FragmentDecl** | Zero-paint (the decl is a template). |
| **FragmentRef** | Expanded against the one-shot fragment registry collected from the tree; an unresolved ref renders a labelled placeholder. |
| **Custom** | Consults the host-supplied server Custom-renderer registry (Phase 141); an unregistered node renders the same labelled placeholder as the client. |

## Custom-renderer registry (Phase 141)

`NodeKind.Custom` is the language's bounded escape hatch. The server renderer
gives it the HTML-string twin of the client's `CustomRendererRegistry`: a
registry mapping `(moduleId, componentId)` to a `Map<string, JVal> ->
Feliz.ViewEngine element`. **Fuaran ships the seam; the domain renderers live in
the consumer app, never in the language tier.**

```fsharp
open Fuaran.UI.Renderer.Server

// Host registers a server SVG renderer for its own domain component:
let registry =
    Registry.empty
    |> Registry.register "music" "score" (fun props ->
        // The host owns + escapes its own output (trust boundary).
        Html.svg [ prop.className "host-score"; (* … *) ])

let html = Render.renderWith registry BindingResolver.empty tree
```

- **Unregistered** `(moduleId, componentId)` → the same labelled
  `fuaran-kind-custom-placeholder` the client emits (parity).
- **Content hash (Phase 70):** register the renderer's hash with
  `Registry.registerWithHash`. When a `Custom` node declares a `ContentHash`,
  the server compares it per `HashStrictness`: `StrictReplay` / `Enforced` route
  a mismatch to a labelled `fuaran-custom-hash-mismatch` placeholder (the
  drifted renderer is **not** invoked); `AdvisoryWarning` renders the body with
  a `data-fuaran-custom-hash-mismatch="advisory"` marker.
- **Exposed node ids:** a Custom node's declared `exposedNodeIds` are emitted as
  a `data-fuaran-exposed-node-ids` attribute on a wrapper so the layout observer
  / hydration can locate the interior nodes.
- **Trust boundary:** the registered closure is a **host trust boundary** – it
  escapes its own output; the server does **not** sanitize it (identical posture
  to the client `RegisterCustomRenderer`). See
  [`SANITIZATION.md`](../SANITIZATION.md) "Custom-renderer trust boundary".

### Typed contracts (Phase 164)

`Registry.registerContract` is the typed twin of `register` – it takes a
`CustomContract<'Props>` (defined once in `Fuaran.UI`) plus a render fn over the
decoded `'Props`, and wires the decode, the render, and the contract's
**derived** content hash in one call. The same contract value drives the client
`CustomRendererRegistry.RegisterContract`, so the four things that must agree
(the tree's prop bag, the client decode, the server decode, the Phase-70 hash)
all flow from a single definition instead of being hand-maintained at four
sites:

```fsharp
open Fuaran.UI
open Fuaran.UI.Renderer.Server

let registry =
    Registry.empty
    |> Registry.registerContract sparklineContract (fun (p: Sparkline) ->
        // The render fn receives typed, decoded props; it still owns + escapes
        // its own output (trust boundary unchanged).
        Html.div [ prop.className "host-spark"; prop.dangerouslySetInnerHTML (svgOf p) ])

let html = Render.renderWith registry BindingResolver.empty tree
```

- The contract's **derived** hash is recorded automatically (no
  `registerWithHash` hand-set string), so the bounded-escape verification above
  is satisfied by construction for nodes built with `Custom.node`.
- A **decode failure** renders a labelled `fuaran-custom-decode-error`
  placeholder naming the failing key (`data-fuaran-custom-decode-error="<key>"`)
  + emits a diagnostic – a malformed payload is debuggable, not a blank box.
- Worked end-to-end in [`samples/giraffe-ssr/App.fs`](../samples/giraffe-ssr/App.fs)
  (the sparkline Custom renderer, registered as a contract on the SSR path).

## SSR parity contract (Phase 142)

The class-name + ARIA parity between the Feliz client renderer and the
Feliz.ViewEngine server renderer is **executable** – the same discipline
`wire-format-fixtures/` applies to the codec. The corpus lives in
[`Fuaran.UI.Renderer.Server.Tests/SsrParityTests.fs`](../src/Fuaran.UI.Renderer.Server.Tests/SsrParityTests.fs)
and runs as the `Build.fs` **`SsrParity`** target (CI runs it alongside `Test`).

How parity is locked on .NET:

1. **The shared spine (provably identical).** Both renderers wrap every node in
   `class="<Theme.nodeClassName node.Kind node.Style>"` and project
   `Accessibility` via the same `Fuaran.UI.Renderer.Accessibility` `.Core`
   function. The corpus asserts the server output's outer wrapper equals
   `Theme.nodeClassName` for the node – so any change to the shared class
   vocabulary (consumed by **both** renderers) is caught, and the ARIA
   projection is the same function on both sides by construction.
2. **The per-kind body (golden corpus).** Body class names are literals
   re-emitted in each renderer's per-kind arm – the genuine drift surface. The
   corpus pins the canonical `fuaran-*` body classes + `role` / `aria-*`
   attributes the server MUST emit per kind. A deliberate class-name change in
   the server renderer fails the matching assertion; the client tier is held
   against the same golden by the catalog's Feliz-parity tests + code review.

> The F# Feliz client renderer cannot render to an HTML string on .NET (its
> `ReactElement` is opaque – which is exactly why `Feliz.ViewEngine` exists as a
> separate backend), so a byte-level client-vs-server diff isn't expressible in
> the corpus; it is the executable contract both tiers conform to.

**Forward-coupling (server-renderer extension of WIRE_FORMAT.md §11):** adding a
`NodeKind` or changing a `fuaran-*` class name extends the parity corpus in the
same change-set – add a fixture with the node's canonical class+ARIA tokens.

**Where the ARIA lands (Phase 951 / `docs/DECISIONS.md` D4).** The projection is
one function on both tiers, but its TARGET is per-kind: a kind whose body is the
node's semantic element – `Link` (`<a>`), `Button` (`<button>`), `Image`
(`<img>`) – carries `role` / `aria-*` on that element, and the `aria-*` half of
`ExtraAttributes` follows it there; the `data-*` half stays on the wrapper with
`data-fuaran-node-id`. Every other kind keeps the whole projection on the
wrapper. `Accessibility.forwardsToSemanticElement` is the predicate, shared by
both tiers, so the placement cannot fork between them – and a fixture that pins
an `aria-*` token for one of those three kinds is pinning it on the body element,
not on the wrapper.

### The uniform icon hook (icon-contract)

Every icon-bearing spec (tab header / Fact / Metric / Callout / Button) renders
its `IconSource` as ONE empty placement element, identically in the client and
server renderers (and the TS tier):

```html
<span class="fuaran-icon fuaran-{kind}-icon" data-icon="{name}" aria-hidden="true"></span>
```

The icon NAME rides the `data-icon` attribute, never the text content – the
reference CSS ships no glyphs, so a host with no icon system sees nothing (not
the raw name), and a host maps `data-icon` to glyphs via its own mechanism
(CSS `::before` content, an icon-font class, or hydration-time SVG injection).
`aria-hidden` because every icon-bearing spec pairs the icon with a visible
text label. The corpus pins the hook's classes + `data-icon` per kind, and a
dedicated lock asserts the name never leaks as text content. _(Supersedes the
pre-0.3.0 behaviour, where Tabs/Fact emitted the raw name as text and
Metric/Callout/Button dropped the icon.)_

## Overlay + overflow render-fidelity contract (Phase 289)

Overlays are the #1 render-fidelity hazard class: a server that renders inline
while the client renders into a React **portal** moves the node in the DOM, and
hydration mismatches. `Modal` / `Toast` / `ScrollArea` therefore ship with an
explicit, executable SSR↔CSR contract – pinned in the parity corpus
([`SsrParityTests.fs`](../src/Fuaran.UI.Renderer.Server.Tests/SsrParityTests.fs))
across all three hosts:

1. **No portal – render inline.** Both renderers emit the overlay **in document
   flow at its tree position**. Position, centring, stacking, and backdrop are
   owned entirely by CSS (`position: fixed`, `z-index: var(--fuaran-z-modal/…)`),
   not by relocating the node. The SSR HTML and the CSR DOM are the **same tree
   shape** → `hydrateRoot` finds the DOM it expects.
2. **Closed = `[hidden]`, never absent.** A closed `Modal`/`Toast` stays in the
   DOM behind the native `[hidden]` attribute (`display:none` via the reference
   CSS). Conditionally *omitting* the node would make the server tree differ from
   the client's first render – the classic hydration mismatch. The `open`
   `Binding<bool>` toggles the attribute, not the node's presence.
3. **ARIA is structural and identical.** `Modal` → `role="dialog"` +
   `aria-modal="true"`; `Toast` → `role="status"` + `aria-live="polite"`;
   `ScrollArea` → the Node-level `role="region"` + a `tabindex="0"` scroll
   target. These are literals re-emitted by both renderers and asserted by the
   corpus. `ScrollArea`'s `tabindex` is emitted **lowercase** server-side
   (`prop.custom ("tabindex", …)`) to match what React normalises the client's
   `prop.tabIndex` to – otherwise Feliz.ViewEngine's camelCase `tabIndex` would
   diverge.
4. **Focus management is additive + client-only.** Focus-trap, restore-focus,
   and Esc-to-dismiss are attached on hydration and **do not alter the hydrated
   DOM** – they are behaviour, not structure, so they cannot cause a mismatch.
5. **Overflow is CSS-owned.** `ScrollArea`'s `overflow` clip + scrollbar come
   from the `fuaran-scrollarea-{vertical,horizontal,both}` classes; the optional
   pixel bounds render as an identical inline `max-height`/`max-width` style on
   both sides.

`Toast` is the **declarative, hydration-stable** notification surface; the
imperative `Action.Notify` (no rendered node) is the host-chrome path – see
WIRE_FORMAT.md §3.2 "Toast vs Action.Notify".

### Protected email links (Phase 812)

A `Link` with `protection = email` over a `mailto:` href renders as the same
tree shape on both sides – a `fuaran-link-protected-wrap` span wrapping a
`fuaran-link fuaran-link-protected` anchor – but the **SSR side emits every
character of the sanitised href AND the label as a decimal HTML entity**
(no plaintext address anywhere in the document source, a working `mailto:`
anchor with no JavaScript), while the **CSR side sets the decoded href
directly** (a hydrated DOM reveals nothing the document didn't). The parity
contract is therefore *post-entity-decode*: the two DOMs are identical after
the browser decodes the SSR entities, which is exactly the comparison the
`Display/LinkProtectedEmail` fixture in the SSR-parity corpus locks (plus a
plaintext-absence assertion on the raw SSR output). Cross-host, the wire field
is certified by `nodes/link-protected-1.json` in the conformance corpus –
see WIRE_FORMAT.md §3.2 "Link protection".

## Deterministic-render + client-only-enhancement contract (Phases 290, 293)

`CodeBlock` (syntax highlighting) and `Math` (KaTeX) carry rich rendering that is
**non-deterministic and library-driven** – exactly the kind of thing that breaks
cross-host + SSR↔CSR parity. The contract splits the render in two:

1. **The deterministic floor (parity-checked).** Both renderers emit the same
   bare, escaped structure: `CodeBlock` → `<pre><code class="language-{x}">`
   (HTML-escaped, **no markdown library**); `Math` → **native MathML** for the
   closed LaTeX subset, or the raw escaped `source` span for out-of-subset input,
   in a `fuaran-math-{block,inline}` container (Phase 658 – see
   [`MATH-DEGRADATION.md`](MATH-DEGRADATION.md), the normative subset + byte-exact
   fixture table). This is the only output the SSR-parity corpus + the cross-host
   byte-diff compare. The no-JS / SSR / crawler reader gets a correct, readable
   result – now with real superscripts on the MathML tier.
2. **The rich enhancement (client-only, OUTSIDE every parity comparison).** A
   post-hydration pass upgrades the floor in place: a highlighter targets
   `.language-{x}`; KaTeX targets the `.fuaran-math` container (reading the LaTeX
   from `data-fuaran-math-src`) and replaces it wholesale (a separate pass
   KaTeX-renders inline `$…$` / `$$…$$` spans in rendered markdown). Because it
   runs *after* hydration and is never emitted server-side, it can never cause a
   hydration mismatch or a cross-host divergence – highlighting/KaTeX are
   client dependencies, not parity-path ones.

**The KaTeX pass ships (Phase 293).** `@fuaran-ui/renderer` exports
`enhanceMath(root)` (also at the React-free subpath `@fuaran-ui/renderer/enhance-math`)
 – the canonical, host-agnostic, idempotent client pass that KaTeX-renders the
`Math` nodes + inline `$…$`/`$$…$$` markdown in `root`. A host calls it once after
each render/hydration and loads `katex/dist/katex.min.css`. The **F# (Fable)
client reuses the same enhancer via interop** (`[<Import("enhanceMath",
"@fuaran-ui/renderer/enhance-math")>]`) rather than re-implementing it – one
implementation, zero divergence, since both renderers emit identical
`.fuaran-math` container / `.fuaran-markdown` markup. A native pure-F#
`Fuaran.UI.Renderer.MathEnhance` (`enhance` / `enhanceDocument`) also ships, for
Fable apps that don't carry the `@fuaran-ui/*` npm packages – it mirrors the TS
logic (its pure `parseSegments` is .NET-unit-tested against the same cases; the
DOM/KaTeX half is `#if FABLE_COMPILER`-only, verified by transpilation). The
syntax-highlighting pass remains a host integration seam (target `.language-{x}`
with any highlighter).

This is the same shape as the overlay contract (deterministic structure pinned,
behaviour layered on after) – see `SsrParityTests.fs` (the `CodeBlock` / `Math`
fixtures assert only the bare floor).

## Sortable rendered tables

A `staticRows` `DataGrid` and a markdown-node table both server-render as the
same semantic markup – `<table class="fuaran-table">` with
`.fuaran-table-header` / `.fuaran-table-row` / `.fuaran-table-cell` – so both are
static HTML: complete, readable, and in their authored order, with no data
binding and no client grid library involved. Column sorting over that output is a
**host affordance**, the same posture as the icon hook and the highlight pass:
the language emits the semantics, the host owns the presentation-interaction.

The reference implementation of that affordance ships with the renderer package
as `content/fuaran-reference-tables.js`, beside `content/fuaran-reference.css`.
Serve it as a file – nothing to build, nothing to import, no dependencies:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
<script src="/fuaran-reference-tables.js" defer></script>
```

Every table on the page with a `thead` and at least two body rows then gains
sortable headers:

- **Cycling.** Click a header, or focus it and press Enter or Space, to cycle
  ascending → descending → **the authored order**. The third activation restores
  rather than adding a third sort: an emitted row order is a deliberate default –
  a grouping, a ranking, a chronology – so leaving a stuck sort would discard
  information the tree carried.
- **`aria-sort`** on the active header mirrors the live state (`ascending` /
  `descending`, removed on restore), so assistive technology reads what the arrow
  shows. The direction glyphs in the reference stylesheet key off that same
  attribute, which is what keeps the two from drifting apart.
- **Numeric parsing through display annotations.** Currency symbols, thousands
  separators, percent signs and `±` markers are stripped before parsing; an
  `a / b` fraction compares by ratio; unparseable text compares case-insensitively.
- **Unmeasured is not zero.** An en-dash placeholder (`–`, or `—` / `-` / empty)
  means *no measurement*, and sorts **last in both directions**. Sorting a column
  to find its worst value must never surface the rows that were never measured.
- **Ties keep their authored order** – the sort is stable, so a partial ordering
  never scrambles the rest of the table.

Two properties matter for the contracts above. The enhancement is **client-only**
and lives outside every parity comparison: it re-orders existing DOM rows and
sets attributes (`data-sortable`, `tabindex`, `aria-sort`), never touching cell
content, so the server-rendered bytes stay byte-identical for every visitor and
the deterministic-render gate is unaffected. And the **no-JS fallback is the
static table itself** – nothing degrades, because nothing was replaced. Since
every attribute the indicator CSS matches on is set by the script, a page that
serves the stylesheet without the script shows no sort affordance at all: a table
never advertises an interaction it cannot perform.

**Author cells as raw values, not pre-formatted strings.** The parser reaches
through the common display annotations, but it is reading rendered text – so a
`CellKind.Numeric` column sorts numerically and reliably, while the same figures
pre-baked into a `CellKind.Text` column as decorated strings sort only as well as
the annotation-stripping happens to manage. Let the cell kind carry the type and
the renderer carry the formatting; sorting then follows for free.

## Email-safe render projection — the Display subset (Phase 441)

`Fuaran.UI.Renderer.Server.Email` is a **second projection of the same tree**,
aimed at the most hostile render target in computing: HTML email. No
JavaScript, no external stylesheet, no flexbox or grid worth relying on, and a
rendering engine per client — Outlook on Windows still lays out through Word.
A scheduled digest is therefore not a fork of the application; it is another
emission of it.

```fsharp
open Fuaran.UI.Renderer.Server

// The content column, for a host's own <body> or a mock-inbox frame:
let fragment = Email.renderStatic tree

// A complete, sendable document (doctype, meta, <title>):
let opts = { Email.defaults with LiveUrl = Some "https://acme.example/report" }
let message = Email.renderDocument opts "Monday briefing" BindingResolver.empty tree

// The structural gate, over any emitted HTML:
let findings : Email.LintFinding list = Email.lint message
```

### The scope line

**This is the feature, not a limitation of it.** The projection is bounded hard
to the **Display subset** — the kinds that carry information rather than
interaction. Everything interactive projects to a **labelled "open live" link**.
A `<button>` in an inbox is a control that looks live and cannot be; a `<form>`
that posts nowhere is worse than an absent one. **The projection never emits a
half-working control.**

| Disposition | Kinds |
|---|---|
| **Rendered** (the Display subset) | Heading · Metric · Fact · LabelValueRow · Badge · Callout · List · Link · Image · Markdown · Progress · CodeBlock · Math · Toast (open) · DataGrid (`staticRows`) |
| **Structural** (children render; the node carries layout only) | Box (all roles) · SplitPanel · SummaryList · Disclosure · ScrollArea · ErrorBoundary · Switch · FragmentRef |
| **Open-live link** (never a control) | Button · Form · Select · FileUpload · Filters · Tabs · Stepper · Modal · Chart · Map · Sparkline · Drawing · Custom · Mount · DataGrid (client-library form) |
| **Omitted** (nothing a static digest can convey) | Icon · Skeleton · FragmentDecl · Toast (closed) |

`Email.scope` is that table **in code**, one row per canonical wire kind with the
reason attached, and it is what the renderer and the tests agree on — this
rendering is for a reader. Six declarations in it are narrower than the SSR
answer, deliberately:

- **Math** renders its **escaped LaTeX source**, not the MathML floor. MathML is
  correct in a browser and blank in Outlook; readable source beats invisible
  mathematics.
- **Chart** and **Drawing** link out rather than emitting SVG, which Word's
  engine does not draw. A broken picture is worse than a link.
- **Disclosure** renders **expanded**. `<details>` is inert in Outlook, so a
  collapsed section is content the reader never learns exists.
- **ScrollArea** renders in full — an email has no clipping, and hidden content
  is lost content.
- **Toast**, when closed, is **omitted rather than `[hidden]`**. `[hidden]` is
  not honoured everywhere, and a notification leaking into a digest it was
  closed in is a disclosure bug, not a cosmetic one.
- **Tabs** links out rather than rendering the active panel. A digest that
  silently drops the other panels misrepresents how much it contains.

**The interactive set is derived, not restated.** `Email.interactiveWireKinds`
reads the Phase 442 render-fidelity manifest (`Fuaran.UI.RenderFidelity`) and
takes every kind whose `RichTier` is `Behavioural` — which means precisely
"renders inert server-side, gains its behaviour at hydration". The conformance
corpus asserts each of those has an open-live row in `Email.scope`, and that
`scope` covers every kind in the manifest. **A new interactive `NodeKind`
therefore fails the build rather than silently shipping a dead button to an
inbox.**

### What is guaranteed

- **Table-based layout, inline styles only.** No flex, no grid, no positioning,
  no classes keyed to a stylesheet that will not arrive. A `Box` with a
  `Grid(cols)` layout becomes an *N*-across table row — the KPI row a digest
  opens with, which is exactly the shape flex and grid cannot express here.
- **Determinism.** Same tree + same options ⇒ same bytes. No clock, no id
  minting, no iteration over an unordered collection. Text resolves through the
  SSR renderer's own `renderText` and figures through its own `formatNumber`, so
  a digest and the page it links to cannot disagree about what a number says.
- **Byte-pinned fixtures.** Goldens live in
  [`Fuaran.UI.Renderer.Server.Tests/email-corpus/`](../src/Fuaran.UI.Renderer.Server.Tests/email-corpus/)
  and are compared byte-for-byte, with determinism asserted separately (a golden
  that matches proves equality with the file, not with the next render).
  Regenerate deliberately with `FUARAN_APPROVE_EMAIL_CORPUS=1`, and read the
  diff — a changed golden is a changed email.

> **These fixtures are IN-REPO, and deliberately not in the shared wire-format
> corpus.** That corpus is the cross-host *wire* oracle; the email projection is
> a .NET-side render target no other host implements, so putting fixtures there
> would assert a conformance obligation on hosts that have no such projection.
> Same discipline (Phase 142), different scope.

### Client-matrix validation — findings recorded, not hidden

**Status: the structural half ships; real-client validation is outstanding.**

What **is** automated is the falsifiable half. `Email.lint` scans emitted HTML
for constructs the client matrix is already known to break on, and the corpus
runs it over every fixture:

| Code | Catches |
|---|---|
| `EMAIL-FLEX` / `EMAIL-GRID` | `display:flex`, `display:grid`, `flex-direction`, `grid-template`, `gap:` |
| `EMAIL-POSITION` | `position: fixed / absolute / sticky` |
| `EMAIL-EXTERNAL-CSS` | `<link>`, `<style>`, `@import`, `var(--…)` |
| `EMAIL-SCRIPT` | `<script>`, `javascript:`, inline `on*=` handlers |
| `EMAIL-CONTROL` | `<form>`, `<button>`, `<input>`, `<select>`, `<textarea>` |
| `EMAIL-EMBED` | `<iframe>`, `<svg>`, `<canvas>`, `<video>`, `<audio>`, `<object>`, `<embed>` |
| `EMAIL-DIV-LAYOUT` | any `<div>` — this projection lays out entirely in tables, so one is evidence of an unaudited emission path |
| `EMAIL-ENTITY-QUOTE` | `&apos;` / `&#39;` inside a style attribute, which HTML4-era mail parsers print literally instead of decoding |

**A clean lint is not a claim of email-safety; a dirty one is proof of the
opposite.** The asymmetry is the point, and it is why the corpus also plants a
hostile construct and asserts the lint goes red on it — a scanner that has never
failed is not evidence.

The intended matrix, and its honest status:

| Client | Layout engine | Status |
|---|---|---|
| Outlook 2016 / 2019 / 2021 / Microsoft 365, Windows | Word | **Pending** — the bar; tables + inline styles are chosen for it |
| Outlook (new) / Outlook.com | Chromium-derived web | **Pending** |
| Outlook for Mac | WebKit | **Pending** |
| Gmail web | Gmail sanitiser | **Pending** — strips `<style>`; the inline-only rule is aimed here |
| Gmail Android / iOS | Gmail sanitiser | **Pending** |
| Apple Mail, macOS / iOS | WebKit | **Pending** |
| Yahoo / AOL web | Yahoo sanitiser | **Pending** |
| Thunderbird | Gecko | **Pending** |

**Pending means pending.** Reaching a real-client test service (Litmus, Email on
Acid, or an equivalent) is a network-dependent, credentialed step that has not
been run, so no row above may be reported as passing. The rows are the declared
target list; the lint is what is currently enforced. When the matrix is run,
record the findings **in this table** — including the failures. A degradation
that is written down is a known limit; one that is quietly fixed in a fixture is
a surprise waiting for the next reader.

Until then, **the mock inbox is the guaranteed-good rendering**, which is what
the demo shows.

## Isomorphic hydration (Phase 143)

"Server render for first paint + SEO, hydrate for interactivity, one canonical
tree across both." The server emits the canonical wire-format tree embedded as a
`<script type="application/json">` payload alongside the HTML; a client mount
decodes it and attaches via React `hydrateRoot` (instead of `createRoot`
clobbering the server DOM). The Phase 142 parity contract is what makes this
mismatch-free – server and client emit the same class+ARIA markup, so React's
hydration reconciler finds the DOM it expects.

### Server side – hydration-ready emission

```fsharp
open Fuaran.UI.Renderer.Server

// Body HTML + an embedded <script type="application/json" id="fuaran-hydrate-<rootId>">
// carrying the canonical wire tree (script-injection-escaped):
let html : string = Hydration.renderHydratable BindingResolver.empty tree
```

The embedded JSON is `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`
output with `<` / `>` / `&` `\uXXXX`-escaped so a `</script>` substring in node
data can't break out of the element; a JSON parser reads the escapes back, so
the decoded tree is byte-identical to what the server encoded (round-trips
through `Fuaran.UI.Ops.JsonDecode.decodeNode`).

### Client side – the `hydrate` mount (browser, shipped)

The F# client mount is **`Fuaran.UI.Renderer.Hydration`** (Fable + `react-dom/client`
`hydrateRoot`). It uses **model-b**: the client reconstructs the *same* tree in
F# (the same authoring code the server used) and hydrates it – **no in-browser
wire-decode** (the client owns the tree). A minimal Elmish loop drives
interactivity via `Hydration.render` (React reuses the hydrated root):

```fsharp
open Fuaran.UI.Renderer

// view () : ReactElement — reconstruct the tree against the current model.
let rec dispatch msg = (* update model *); Option.iter (fun r -> Hydration.render r (view ())) root
and view () = Render.renderWithSources sources dispatch (Tree.build model)

// Initial hydrate — attach React to the server-rendered DOM in place
// (hydrateRoot, NOT createRoot):
root <- Hydration.hydrateById "hydration-root" (view ())
```

Interactive `Action`s / `Local` state become live **only after hydration**;
`Custom` nodes hydrate via the client registry over the server-rendered shell
("server-render-once, client-attach"). Because the server and client markup are
parity-locked (Phase 142), `hydrateRoot` attaches without a re-render or a React
hydration-mismatch warning. The runnable worked example is
[`samples/hydration/`](../samples/hydration/).

> **Status (shipped + browser-verified 2026-06-07).** Server embed + wire-tree
> round-trip (tested on .NET); the F# **model-b** client mount + the
> `samples/hydration/` worked example are browser-verified (Vite + Fable 5 +
> Claude Preview): the page is fully visible pre-JS, React hydrates with **zero
> hydration-mismatch warnings**, and the Details-tab click switches the panel via
> the hydrated root. (Getting here required trimming the renderer's Fable graph:
> it had transitively pulled the whole of `Fuaran.UI.Ops` via
> `Telemetry.Abstractions → Ops`; fixed by splitting the `TreeOp` type contract
> into `Fuaran.UI.Ops.Abstractions`, so the graph carries the contract rather
> than the apply engine.)
>
> **The in-browser-decode path is TS-tier – and shipped.** Decoding the
> *embedded JSON* in the browser (rather than reconstructing the tree in F#)
> needs a decoder in the browser. `Fuaran.UI.Ops` has been Fable-portable since
> Phase 191 (`docs/migrations/191-fable-portable-ops.md`), so this is a
> **layering** choice rather than a portability constraint — the path lives in
> the (browser-native) TS tier:
> **`@fuaran-ui/renderer`'s `hydrateEmbedded`** reads the server-emitted
> `<script type="application/json">` (id `fuaran-hydrate-<rootId>`, the shared
> contract with the F# `Renderer.Server` `scriptId`), **decodes it via
> `@fuaran-ui/ops`**, and attaches React with `hydrateRoot`. So the F#
> `Renderer.Server` emits the embed and a TS client decodes + hydrates it – the
> cross-tier isomorphic loop. Verified by `@fuaran-ui/renderer`'s
> `hydrate.test.tsx` (real `renderToStaticMarkup` → embed → `decodeNode` →
> `hydrateRoot`, asserting zero React hydration-mismatch – the same React 19
> reconciler a browser runs, under jsdom) – **and live-browser-verified** in the
> TS tier's own `samples/hydration` worked example (`ssr.mjs` server-renders +
> embeds the tree; `main.tsx` decodes via `hydrateEmbedded` + `hydrateRoot`s):
> served on Vite, the page is visible pre-JS and hydrates with zero mismatch
> warnings.

### Interactivity over a decoded tree – ship a verb, not a function

A decoded tree can be made **interactive client-side** without a server round-trip
and without a wire-format change: wire a `runtime` into `hydrateEmbedded` and a
button's action fires through it after hydration. The constraint is what survives
the wire. You cannot serialise a closure, so the `Action` cases split in two:

| Survives the wire (data – dispatchable after decode) | Dies to `<closure>` (callback – inert after decode) |
| --- | --- |
| `SetState`, `Notify`, `Navigate`, `AiTool`, `Chain`, `CommitLocal`, `WriteToClipboard` | `Dispatch` (app message), `Call` (`onResult`), `ReadFileBody` (`onRead`) |

So the rule is **"ship a verb, not a function":** a server-emitted tree expresses
intent as named, data-carrying actions; the client holds the behaviour (a `runtime`
+ an `update`). For closure-backed interactivity, reconstruct the tree in code (the
F# model-b path) or keep the model server-side (the server-driven tier).

**Default-deny dispatch gate.** Because a decoded action came from *outside*, the TS
`@fuaran-ui/renderer` consults an optional `runtime.canDispatch(descriptor)` gate
**before** the host-effecting cases (`Call` / `Navigate` / `AiTool` / `ReadFileBody`)
fire – the mirror of the F# `IFuaranRuntime.CanDispatch` seam. An absent gate allows
(existing hosts unchanged); a gate returning `false` denies, emitting a diagnostic
and skipping the effect. A standalone host hydrating a tree it does not fully trust
supplies a gate (typically an allowlist) so a decoded `Navigate` / `AiTool` cannot
fire unapproved. This is the client-side counterpart of the server-driven tier's
inbound trust boundary.

### The interactivity axes over one tree

| Axis | Where the model + closures live | Runs which actions | Trade-off |
| --- | --- | --- | --- |
| Fable / model-b hydration | client, in compiled F# | the full space (incl. `Dispatch` / `Call` / `Computed`) | needs a per-app compile/bundle |
| **TS client-decode (this)** | client; decoded structure + a wired `runtime` | the wire-survivable set, gated by `canDispatch` | no per-app bundle, offline-capable; no closure actions |
| Server-driven | the *server* (one channel, a generic shim) | the full space, server-side | needs a live connection per interaction |

## Islands – partial hydration (Phase 163)

Whole-tree hydration (143) pulls the React runtime + a full-tree reconstruction
onto the page – fine for an app surface, wasteful for an SEO page that wants
static HTML with one or two small interactive regions (an audio control, a unit
toggle). **Islands** are the middle tier: mark a subtree as an island and only
*that* subtree hydrates.

```fsharp
// Author: mark the interactive subtrees (render-time-only — rides on the
// wire-omitted ExtraAttributes, so no wire-format change).
let page =
    Fuaran.dashboard "page" { Defaults.dashboard with Children =
        [ staticArticle                                  // inert SSR HTML
          playbackControl |> Node.asIsland "playback"    // hydrates
          unitToggle      |> Node.asIsland "units" ] }    // hydrates

// Server: static page + per-island boundary + per-island hydrate <script>.
let html = Hydration.renderWithIslands sources page
//   (or, with a document shell: Fuaran.UI.Giraffe.fuaranIslandsPage)

// Client (Fable): mount each island independently.
Hydration.hydrateIslands (fun islandId ->
    match islandId with
    | "playback" -> Some (renderClient playbackControl)
    | "units"    -> Some (renderClient unitToggle)
    | _          -> None)
```

The server lifts each island marker onto a `<div data-fuaran-island="<id>">`
**boundary wrapper** (the hydration container) and emits a scoped
`<script type="application/json" id="fuaran-hydrate-island-<id>">` with that
subtree's wire tree. The client locates each boundary, reconstructs the subtree
(model-b, same authoring code), and `hydrateRoot`s it in place – **independent
React roots per island**; an island whose mount throws degrades to its static
HTML without breaking the others. A page with **zero islands emits zero hydrate
script** and is byte-identical to a plain `render`. Mismatch-freedom is
structural: the boundary wrapper's children are exactly the island node's plain
static render (marker stripped), which is what the client renders into it.

### The five-tier client spectrum – one canonical tree

Pick the lightest tier that delivers the interactivity the page actually needs:

| Tier | Ships to the browser | Interactivity | Pick when |
| --- | --- | --- | --- |
| **Static SSR** (`render`) | HTML only | none (real `<a href>` nav) | pure content / SEO; no client behaviour |
| **Islands** (163, this) | HTML + per-island roots | only the marked subtrees | mostly-static page with a few interactive regions; page-weight-sensitive |
| **Whole-tree hydration** (143) | HTML + one root over the whole tree | the full tree | an app surface that benefits from SSR first paint + SEO |
| **SPA** (Phase 12, Fable) | the app bundle | the full tree, no SSR | an app behind auth where first-paint SEO doesn't matter |
| **Server-driven** (152) | a generic ~few-KB shim | the full space, server-side | round-trip-tolerant interactivity with no per-app bundle; a live connection is acceptable |

All five render the **same** canonical `Node` tree; the tier is a deployment
choice, not a re-authoring.

## No dispatch server-side (FGP 3)

`Action`-bearing nodes render inert. There is simply **no host to dispatch to**
server-side – no `IFuaranRuntime`, no `dispatch` sink – so no dispatch path is
silently bypassed; the interactivity arrives with client hydration. The
crawlable, no-JS navigation path is `Display.Link` (a real `<a href>`), which is
exactly why Phase 139
shipped the typed link node ahead of this renderer.

## Giraffe host integration (`Fuaran.UI.Giraffe`, Phase 162)

`Renderer.Server` emits the *body fragment*; every Giraffe / ASP.NET consumer
then hand-rolled the same document shell, `HttpHandler` plumbing, and response
caching. `Fuaran.UI.Giraffe` is the last mile – the document shell becomes
library-*shaped* while the head content stays host-*authored*.

```fsharp
open Fuaran.UI.Giraffe

let opts =
    { FuaranGiraffeOptions.create with
        Theme = Some Fuaran.UI.Defaults.theme
        Customs = serverCustomRegistry          // Phase 141 domain renderers
        Cache = RenderCache.inMemory () }        // or any IFuaranRenderCache

let shell =
    { DocumentShell.create "Pricing — Acme" with
        MetaDescription = Some "Simple, transparent pricing."
        Canonical = Some "https://acme.example/pricing"
        OpenGraph = [ "og:title", "Pricing — Acme"; "og:type", "website" ]
        JsonLd = [ productJsonLd ]
        Stylesheets = [ "/fuaran-reference.css" ] }

let webApp =
    choose
        [ route "/pricing" >=> fuaranPage opts shell pricingTree           // static SSR document
          route "/pricing/fragment" >=> fuaranFragment opts pricingTree    // body fragment (HTMX)
          route "/app" >=> fuaranHydratablePage opts shell appTree ]       // document + hydrate payload
```

| Handler | Emits |
|---|---|
| `fuaranPage` | A full `<!DOCTYPE html>` crawlable document, static SSR. |
| `fuaranHydratablePage` | The document + the Phase 143 hydrate `<script>` payload. |
| `fuaranFragment` | The body fragment only (no shell). |

**Deterministic ETag + 304 + render cache.** Every response carries a strong
ETag = SHA-256 over the canonical tree wire-form + theme CSS + shell signature;
`If-None-Match` serves `304 Not Modified` with no body and no re-render. A
host-supplied `IFuaranRenderCache` is consulted before render and populated
after – the default `RenderCache.none` is a zero-cost pass-through, and
`RenderCache.inMemory ()` is a **bounded** in-process store
(`RenderCache.defaultCapacity` documents, LRU eviction; `RenderCache.bounded n`
sizes it yourself). The bound is not incidental: the key is a content hash, so a
high-fan-out surface mints a fresh key per distinct tree and a never-evicting
store grows for the process lifetime. The render
mode (static vs hydratable vs fragment) folds into the ETag, so the three
emissions of one tree get distinct cache keys.

**Injection safety** follows the document-shell boundary above: text fields
HTML-escape via `Feliz.ViewEngine`; URL fields (`Canonical`, stylesheet hrefs,
script srcs) route through `Renderer.Sanitize.sanitizeUrlOrBlank`; **JSON-LD is
the one sanctioned raw-JSON injection point** (host-trusted) and is script-escaped
exactly like the Phase 143 hydrate payload so it cannot break out of `<script>`.

**Giraffe isolation.** Giraffe is a dependency of `Fuaran.UI.Giraffe` only – the
language tier and `Renderer.Server` stay Giraffe-free. The adapter composes with
(does not depend on) `Fuaran.UI.ServerDriven.AspNetCore`'s live endpoints. Worked
example: [`samples/giraffe-ssr/`](../samples/giraffe-ssr/).
