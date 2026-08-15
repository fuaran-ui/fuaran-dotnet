# Fuaran language-tier sanitization contract

This document declares the render-time injection-safety contract for `Fuaran.UI.Renderer`. Every string the renderer pours into a DOM attribute, URL prop, or raw-HTML sink has a declared sanitization posture, documented here so downstream auditors can verify that Fuaran-emitted trees cannot carry an XSS payload through into the host application.

Companion to [`STABILITY.md`](STABILITY.md) — that document declares which surfaces are stable; this one declares which surfaces are safe.

## Scope and threat model

The Fuaran value proposition is that an AI emits the UI. Once a `Node<'Msg>` reaches `Fuaran.UI.Renderer`, AI-emitted text content, AI-emitted markdown, and AI-emitted attribute values must be treated as untrusted input. The renderer is the last line of defence before bytes hit the browser's HTML parser.

Threats in scope:

- **Script injection** via text fields, markdown bodies, or attribute values.
- **Event-handler injection** via `on*=` attributes (`onclick`, `onerror`, …).
- **URL-scheme injection** via `javascript:` / `vbscript:` / unguarded `data:` schemes in `href` / `src` props.
- **Off-origin redirection** via a PROTOCOL-RELATIVE `href` / `src` — a URL with no scheme but a leading pair of slash-ish characters. All four spellings (`//host`, `/\host`, `\\host`, `\/host`) resolve off-origin, because WHATWG URL parsing treats `\` as `/` for a special scheme and reads what follows the pair as an AUTHORITY. On an `href` that is a live off-origin link; on an `Image.src` it is an off-origin request that leaks the Referer. `sanitizeUrl` rejects all four (`//` and `/\` from Phase 298, the backslash-leading pair from Phase 784). A SINGLE leading backslash is NOT in scope: WHATWG reads `\path` as `/path`, an ordinary same-origin path.
- **HTML smuggling** through markdown source containing literal `<script>` / `<iframe>` / `<object>` blocks. Since Phase 292 markdown is rendered by Fuaran's own deterministic GFM renderer (`Markdown.toHtml`), which **escapes raw HTML by construction** (raw HTML is the OUT bucket — see [`docs/MARKDOWN.md`](docs/MARKDOWN.md)); the `sanitizeMarkdownHtml` sweep below is therefore now defence-in-depth over a much smaller surface (it was the primary gate when markdown came from the npm `marked` library, which rendered embedded HTML by default).

Threats out of scope (consumer responsibility):

- **Content Security Policy** — Fuaran's renderer does not set CSP headers; the host application does.
- **DOM-clobbering** via author-supplied IDs that match global JavaScript names — author trust boundary.
- **Trusted Types** — opt-in browser surface; consumers wire it via React's runtime if required.

## Posture per seam

Every place a string makes it to the DOM through `Fuaran.UI.Renderer`:

| Seam | Source of string | Posture | Implementation |
|---|---|---|---|
| `TextSource.Literal` / `Bound` / `I18n` → `prop.text` | AI emission, query resolution, i18n catalog | **Escaped by React** (textContent assignment) | `Render.renderText` |
| `Markdown.toHtml` → `prop.dangerouslySetInnerHTML` | AI-emitted markdown source | **Escaped by construction** (deterministic GFM renderer escapes every text run, escapes raw HTML, routes URLs through `sanitizeUrlOrBlank`) **+ sanitized at render-time** as defence-in-depth (strip `<script>`/`<iframe>`/`<object>`/`<embed>`/`<form>`/`<link>`/`<meta>` blocks + `on*=` attributes + `javascript:`/`vbscript:` URLs) | `Renderer.Core` `Markdown.toHtml` → `Sanitize.sanitizeMarkdownHtml` |
| `Theme.toCss` → `prop.dangerouslySetInnerHTML` (via `themeStyleElement`) | Host-supplied `Theme` record | **Host-trusted** — Theme is a consumer-authored F# record; the wire-decode path strips it (Theme is not part of the §4d JSON contract) | `themeStyleElement` |
| Protected email `Link` (`protection: "email"`) → `prop.dangerouslySetInnerHTML` (SSR only) | Resolved link href (post-`sanitizeUrlOrBlank`) + rendered label | **Escaped by construction** — every character of the sanitised `mailto:` href and the label is emitted as a decimal HTML entity (`&#N;`), so no character can open a tag, attribute, or entity of its own; the href has additionally passed `sanitizeUrlOrBlank` before encoding. The CSR arm uses only typed props (no raw HTML) | `Renderer.Server` `Render.fs` `NodeKind.Link` protected arm (Phase 812) |
| `Node.ExtraAttributes` → `prop.custom(k, v)` | Smart-ctor `Node.withExtraAttribute` (prefix-gated) OR `{ node with ExtraAttributes = ... }` (bypass) | **Sanitized at render-time** — data-* / aria-* prefix rule **plus a positive `[A-Za-z0-9-]` character allowlist over the whole key** + value safety check; non-conforming entries dropped, survivors emitted **trimmed**. The server renderer re-checks the key at its emission site (see "Attribute-name injection" below) | `Render.render` → `Sanitize.sanitizeExtraAttributes`; SSR emission site → `Sanitize.isSafeAttributeName` |
| `Render.accessibilityAttributes` → `prop.custom` | Renderer-controlled keys (`aria-label`, `role`, …); values resolved through `BindingResolver` | **Renderer-controlled keys + React-escaped values** | `Render.accessibilityAttributes` |
| `IconSource s` → `prop.custom("data-icon", s)` | Smart-ctor IconSource | **Escaped by React** (attribute encoder). The icon-contract hook is an EMPTY element — the name rides the `data-icon` attribute only, never text content, and no SVG/glyph path exists in the renderer (a future bundled-glyph path needs its own sanitization arm) | `Render.iconHook` (Tabs / Fact / Metric / Callout / Button) |
| `CellKindErased.Link href` → `prop.href` | AI-emitted grid row + accessor closure | **Sanitized at render-time** — http/https/mailto/tel allowlist; `javascript:`/`vbscript:`/unknown schemes replaced with `about:blank`; relative paths pass through | `Render.renderCell` → `Sanitize.sanitizeUrlOrBlank` |
| `prop.value`, `prop.placeholder` on form inputs | Resolved binding text | **Escaped by React** (attribute encoder) | various `Render.renderForm*` arms |
| `prop.id` (NodeId) | Smart-ctor first positional arg | **Author-trusted** — NodeId is consumer-emitted (in the AI scenario, the AI; in the wire-decode scenario, the JSON decoder); React's attribute encoder applies | `Render.render` |
| `NodeKind.Custom` via `IFuaranRuntime.TryRenderCustom` | Host-registered closure | **Host trust boundary** — see below | `Render.renderKind` |

## `Action.Navigate` and the State-key namespace (Phase 782)

The table above covers strings the renderer pours into the **DOM**. Two seams carry a string out of a
decoded tree without touching the DOM at all, and both were unguarded until Phase 782.

**`Action.Navigate` → `IFuaranRuntime.Navigate` / `ClientEffect.Navigate`.** A route is a URL a host
performs, not a URL the renderer emits, so `sanitizeUrlOrBlank` at the `href`/`src` render sites did
not cover it. The shipped `BrowserRuntime` assigns `window.location.hash`, which cannot execute
script — but `IFuaranRuntime.Navigate` is documented as the seam a host wires to its SPA router, and
`location.href` / `router.push` turn a `javascript:` route into script execution and any absolute URL
into an open redirect. Since Phase 782 the route passes `Sanitize.sanitizeUrl` on the action path in
all three interpreters (`Render.treeNavigate`, `Driver.interpret`, and
`BoundedActions.runBoundedAction` — which ships in `Fuaran.Program.Bounded` from 0.25.0)
**before** the dispatch gate sees it and before any host code is reached. A refusal performs nothing
and emits no effect — `about:blank` would be a navigation the author did not ask for.

The check sits on the **canonical decoded field**. The wire accepts `route` / `href` / `url` / `to`
as spellings of the same thing, so guarding the decoded field covers all four by construction; a
wire-key match would have covered whichever ones someone remembered.

**`Action.SetState` → the State channel.** The State channel is one flat key namespace shared by the
host and every tree it renders, persisting (on the browser path) into one flat `localStorage`
namespace. A decoded tree could therefore address any key the host owned. `StateKeys.HostReservedPrefix`
(`"host."`) splits it: every tree-originated write — `Action.SetState`, a covered control's write-back
default, a `Call … into State` target, the bounded server-driven interpreter — refuses a key under
that prefix and records the refusal. **This is not gate policy**: it holds when `CanDispatch` allows
everything, because which of a host's own key names are sensitive is not something a shipped default
can know.

Stated plainly so it is not over-read: tree writes are **not** re-namespaced into a sandbox. The
declarative write-back loop needs tree writes and tree reads to name the same key, and hosts seed
`BindingSources.State` under those same names. A host slot that matters takes the `host.` prefix; one
that does not is reachable by any tree that renders, exactly as before.

## React's escaping floor

React's `prop.text` (text content), `prop.value`, `prop.placeholder`, and the standard typed attribute setters (`prop.className`, `prop.id`, …) HTML-escape their string arguments before insertion. The Fuaran renderer leans on this floor for the bulk of its text-rendering path. Where the renderer reaches past React's typed surface — `prop.custom`, `prop.href`, `prop.dangerouslySetInnerHTML` — sanitization is the renderer's explicit responsibility, not React's.

**The floor covers attribute VALUES, not attribute NAMES** — see the next section.

## Attribute-name injection (Phase 788)

Every escaping surface in this document escapes attribute *values*. Nothing escapes an attribute *name*: React's attribute encoder escapes the value it is given, and on the server `Feliz.ViewEngine`'s `Interop.mkAttr` does the same while `ViewBuilder.buildElement` writes `" " + key + "=\"" + value + "\""` with the key verbatim.

That asymmetry is not an oversight in either library — **HTML has no escape for an illegal character in an attribute name.** A space inside a name simply starts a *new* attribute and an `=` starts its value, so a key of `data-x=1 onmouseover=alert(document.domain) z` is not a mangled attribute name; it is three attributes, one of them a live event handler. The only sound response is to drop the entry.

So the key gate is a **positive character allowlist** (`[A-Za-z0-9-]`, applied to the whole trimmed key) layered on the `data-` / `aria-` prefix rule and the explicit `on*` / `style` rejects — `Sanitize.isAllowedExtraAttributeKey`. `Sanitize.sanitizeExtraAttributes` emits the **trimmed** key, since the trimmed form is what the gate inspected. The server renderer's emission site re-checks each surviving name with `Sanitize.isSafeAttributeName` as defence in depth, so it does not rest solely on upstream validation remaining correct.

Two scope notes, so the posture is neither overstated nor understated:

- **The reachable path is host-side, not wire-side.** The wire decoder hard-codes `extraAttributes` to `None` (`Fuaran.UI/Generated.fs`, `Ops/JsonDecode.fs`), so a decoded AI emission cannot carry an `ExtraAttributes` entry at all today. The reachable surface is a host or adapter mapping its own untrusted data through `{ node with ExtraAttributes = ... }` — the record-with bypass documented above. If a future phase teaches the decoder to carry `extraAttributes`, this seam becomes wire-reachable with no further change, which is why the gate lives at render time rather than at the decoder.
- **Before Phase 788 the client renderer was saved only by React**, which validates attribute names and refuses to set an invalid one. That is an accidental floor, not a declared posture; the server renderer had no equivalent, and this document previously described the seam as covered by a "key allowlist" when only the prefix was checked.

## Custom-renderer trust boundary

`NodeKind.Custom(moduleId, componentId, props)` dispatches through `IFuaranRuntime.TryRenderCustom`. When a host has called `MutableRuntime.RegisterCustomRenderer(moduleId, componentId, closure)`, the registered closure is invoked with the props map and returns a `ReactElement` verbatim. The renderer does NOT police the closure's output.

**This is by design.** The custom-renderer registry is a host extension surface for renderers the language tier doesn't ship (a Fuaran-native Mermaid renderer, a Lottie wrapper, a host-specific business widget). The closure is consumer-authored F# code, not AI-emitted markup. Wire-decode (`Fuaran.UI.Ops.JsonDecode`) cannot construct a closure — `NodeKind.Custom` carries only `moduleId` / `componentId` / `props`, and the closure is looked up at render time from the host-controlled registry.

**The same boundary applies to the server renderer (Phase 141).** `Fuaran.UI.Renderer.Server`'s `Registry.register (moduleId) (componentId) (closure)` registers a `Map<string, JsonValue> -> Feliz.ViewEngine` element closure; the server `Custom` arm invokes it verbatim and does NOT police its output — identical posture to the client `RegisterCustomRenderer`. The host owns + escapes its own server HTML. The bounded-escape `ContentHash` (Phase 70) is verified on the server path per its `HashStrictness` (a `StrictReplay` / `Enforced` mismatch routes to a labelled error placeholder and does NOT invoke the drifted renderer). See `docs/SSR.md` "Custom-renderer registry".

### What the registry scoping and `ContentHash` do and do NOT protect (Phase 783)

Two clarifications the earlier text left implicit, both of which had been read the strong way.

**The registry is scope-constrained, and it was not before.** Until Phase 783 the registry was one
process-wide dictionary keyed on `(moduleId, componentId)` **taken straight off the wire**, so any
decoded tree could invoke any renderer registered anywhere in the process, with attacker-chosen
props. A renderer registered for a privileged admin surface was reachable from a tree rendered on a
public one — a confused deputy, and the "the closure is consumer-authored, not AI-emitted" argument
above did not address it: the CLOSURE was trusted, but WHICH closure ran was chosen by the tree.

The key now carries the render scope on both renderers. `None` is the root scope, where the unscoped
`Register` / `register` land; a host separates surfaces by rendering under distinct scopes
(`Render.renderWithSourcesInScope` client-side, `ServerRenderContext.Scope` /
`Render.renderWithInScope` server-side) and registering with `RegisterInScope` /
`Registry.registerInScope`. Lookup does **not** fall back across scopes — a fallback would make the
scoping advisory, which is indistinguishable from not having it. A mounted guest already renders
under its own scope, so a guest reaches only what was registered for it.

**`ContentHash` is drift detection, not authentication — and cannot be authentication.** The tree
supplies its own hash record, so a match proves only that whoever wrote the tree knew the registered
renderer's hash. Two bypasses followed from reading it as more than that, and both are closed:

- **Omitting the hash** classified as `NoTreeHash`, which shared a render branch with `Match` and
  rendered **silently** — the cheapest route past verification was to skip it. Under an enforcing
  host floor, an unverifiable hash is now a refusal.
- **Declaring a lenient strictness** worked because strictness was read from the *tree's own* record,
  so an attacker who did declare a hash chose `AdvisoryWarning` and got warn-then-render. Strictness
  is now a **host floor** (`CustomHash.installCustomHashFloor`) that a tree may only tighten.

The floor defaults to `AdvisoryWarning` — today's behaviour — because a tree with no hash is the
common legitimate case. Enforcement is a host act; what changed is that a host *can* enforce, and
that a tree cannot talk its way underneath the host's choice.

**The closure's OUTPUT remains unpoliced, deliberately.** That is the host trust boundary and it has
not moved. What Phase 783 changed is who may cause a given closure to run.

The contract for hosts implementing custom renderers:

1. Treat `props : Map<string, JsonValue>` as untrusted AI-emitted data.
2. Escape any prop value before inserting it into HTML attributes, text, or URLs.
3. Use React's typed prop setters (`prop.text`, `prop.value`, …) where possible.
4. If the renderer reaches past React's typed surface (`dangerouslySetInnerHTML`, `prop.custom`, raw `href`), apply `Fuaran.UI.Renderer.Sanitize.*` or an equivalent.

Custom renderers that ship with Fuaran (`Fuaran.UI.Renderer.AgAdapter`, `AgChartAdapter`, `AgGridAdapter`, `VisAdapter`) follow this contract; third-party host renderers MUST follow it too.

### Typed Custom-payload contracts (Phase 164)

`CustomContract<'Props>` (in `Fuaran.UI`) is a host-side façade over the same escape hatch — it does **not** move the trust boundary. The boundary is unchanged: the host's render fn still owns and escapes its own output, exactly as above. What the contract changes is the *input* seam. Instead of hand-maintaining four things that must agree — the prop bag the tree encodes, the client decode, the server decode, and the Phase-70 content hash — a contract is defined once and yields all four:

```fsharp
open Fuaran.UI

type Sparkline = { Points: string }

let sparklineContract : CustomContract<Sparkline> =
    CustomContract.create
        "sample" "sparkline-svg"
        (fun p -> Map.ofList [ "points", JsonValue(box p.Points) ])   // encode
        (fun bag ->                                                    // decode
            match Map.tryFind "points" bag with
            | Some (JsonValue v) -> Ok { Points = string v }
            | None -> Error (CustomDecodeError.forKey "points" "missing"))
        { Points = "" }                                                // schema sample (key-set only)
        []                                                             // exposed node ids
        HashStrictness.StrictReplay

// Typed node — the prop bag can never be malformed; the derived hash is stamped:
let node = Custom.node "spark-1" sparklineContract { Points = "0,1 2,3" }

// One value drives BOTH registries (client + server):
clientRegistry.RegisterContract(sparklineContract, fun (p: Sparkline) -> (* ReactElement *) )
let serverReg = Registry.registerContract sparklineContract (fun (p: Sparkline) -> (* element *)) Registry.empty
```

Security-relevant properties:

- **The render fn receives typed, decoded `'Props`, not the raw `Map<string, JsonValue>`.** It still treats those values as untrusted AI-emitted data and escapes them on the way to HTML (rules 2–4 above are unchanged).
- **A malformed payload is debuggable, not a blank box.** A decode failure (`CustomDecodeError`) routes to a labelled `fuaran-custom-decode-error` placeholder that names the failing key (`data-fuaran-custom-decode-error="<key>"`) and emits a diagnostic on both pipelines — it never silently invokes the render fn with bad data.
- **The content hash is derived from the declared shape, not hand-typed**, so the Phase-70 bounded-escape verification (`StrictReplay` / `AdvisoryWarning` / `Enforced`) is honoured with no opportunity for a stale hand-set hash to drift from the registered renderer.

### The `Mount` boundary (Phase 783)

`NodeKind.Mount` is described as an isolation boundary. For an **authored** tree it was; for a
**decoded** one it was not, in two ways, both now closed:

- **The guest received the host's runtime unwrapped** when no `GuestSeam` was installed. A guest is
  foreign content and `MountSpec.Capabilities` is documented as "a request, not a grant", so the
  no-policy default granting everything was the exact inverse of the declared posture. With no seam
  the guest now receives a `Runtime.UnprivilegedGuestRuntime`: every capability refused, every
  refusal recorded through the host's `Warn` channel, `CanDispatch` false, no custom renderers in any
  scope, and no nested guest loading (so a guest cannot mount its own guests to climb back out).
- **`ChannelDirection` is a REQUIRED wire field**, so a hostile tree simply wrote `TwoWay`. `OutOnly`
  was only the default of the *authoring* smart constructor, and no host-side clamp existed — which
  made `Types.fs`'s "OutOnly, the default, safe for untrusted guests" true of authored trees and
  false of wire ones. The renderer now clamps every mount to `OutOnly` and records the downgrade;
  `TwoWay` is a host grant (`GuestSeam.GrantTwoWay`), never a wire-declared property.

The clamp is at the RENDERER, not the decoder, deliberately: the decoder preserves what the wire
said, so canonical round-trip and the shared conformance corpus are untouched, and the host's own
policy decides what is honoured.

## `sanitizeMarkdownHtml` is a floor over escaped-by-construction input, not a general sanitizer (Phase 303)

`Sanitize.sanitizeMarkdownHtml` is a **public** binding, but its correctness rests on a precondition: its
only intended input is the Fuaran GFM renderer's own already-escaped output (`Markdown.toHtml`). It is an
approximate substring sweep — it anchors `on*=` stripping on leading whitespace and splits attributes on
the first `=`/quote — which is sound for the renderer's narrow output shape but **bypassable on arbitrary
untrusted HTML**. Do not call it as the sole sanitizer for HTML from an untrusted source: route such input
through a real sanitizer (DOMPurify-class) first, then (optionally) this floor as defence-in-depth. The
precondition is now stated loudly at the binding's doc-comment.

### Adversarial floor corpus (Phase 214)

The substring sweep's *intended floor* is now pinned behind an adversarial corpus
(`Fuaran.UI.Tests/SanitizeTests.fs`, "Phase 214 — adversarial floor corpus") so a future edit cannot
silently weaken it. The corpus is scoped to the sweep's assumed input shape — the Fuaran GFM
renderer's already-escaped-by-construction output (`Markdown.toHtml`). (Historically the assumed shape
was the npm `marked` library's output; since Phase 292 it is the deterministic in-repo GFM renderer.
The floor's job is unchanged — the framing is what moved.)

**The floor (what the sweep guarantees against the assumed shape).** No live *open* dangerous element
(`<script>` / `<iframe>` / `<object>` / `<embed>` / `<form>` / `<link>` / `<meta>`), no inline `on*=`
event handler, and no `javascript:` / `vbscript:` URL survives — even under the evasion classes a
substring stripper is weakest against:

- **Nested / double / split tags** (`<<script>`, `<scr<script>ipt>`, `<script><script>…`,
  `</scr<script>ipt>`). The sweep loops, so no live *open* tag is ever reconstructed. Substring
  recombination can leave a stray *close* token (`</script>`) or residual plain text — both inert
  (browsers ignore orphan close tags and never execute text nodes); the floor is "no live open tag",
  not "byte-clean output".
- **Case + whitespace variants** on the open tag (`<ScRiPt>`, `<script >…</script >`, tabs/newlines
  inside the tag) — stripped case-insensitively; a spaced close falls back to open-tag removal.
- **Malformed nesting** (unbalanced open with no close, open tag with no `>`) — the open tag or the
  tail is removed.
- **`on*=` handlers** — unquoted, attr-joined, newline-separated, and upper-case handlers are stripped;
  a legitimate `data-on*` key is *not* a false positive (the tag-interior + leading-whitespace anchor).
- **`javascript:` / `vbscript:`** — case variants neutralised to `about:blank`; the substitution
  inserts `about:blank` so interleaved schemes cannot recombine.

**The non-goals (deliberately out of scope — the sweep is not a parser).** These evasions are only
reachable on *arbitrary untrusted HTML* outside the assumed shape, where the GFM renderer's own
escaping and `sanitizeUrl` already neutralise them upstream. They are pinned in the corpus at their
*actual* (uncaught) behaviour so the boundary is honest, **not** fixed by growing the substring
stripper into an HTML parser (Phase 214 task 3):

- **Intra-scheme whitespace / control chars** (`java\tscript:`) — the markdown sweep does not normalise
  them, but `sanitizeUrl` (which the renderer applies to every href) does, returning `None`.
- **Entity- or numeric-encoded schemes** (`&#106;avascript:`) — the sweep does not HTML-decode before
  matching.
- **Slash-separated attributes** (`<a/onclick=…>`) — a valid HTML5 shape the GFM renderer never emits;
  the handler scan anchors on leading whitespace and intentionally does not chase it.

**The host-owned ceiling.** For HTML from an *untrusted* source outside the assumed shape, the host
MUST layer a real DOMPurify-class sanitizer first; this floor is defence-in-depth on top of that, not a
substitute for it. The floor is what Fuaran's renderer guarantees; the ceiling is the host's to raise.

## Opt-in raw-HTML seam

There is no `Trust.raw` opt-in in the current renderer surface. The renderer's posture is **default-deny**: every string-to-DOM seam is sanitized or React-escaped. Hosts that need raw HTML in a specific renderer slot do so through the `NodeKind.Custom` registry — registering a custom renderer is the documented opt-in.

If a future phase introduces a typed `Trust.raw : string -> RawHtml` constructor for in-tree raw-HTML emission (e.g. for app-authored prose chunks the AI never touches), that constructor MUST live in `Fuaran.UI.Defaults` or `Fuaran.UI` (NOT in the renderer), MUST be excluded from the §4d JSON wire decode, and MUST be flagged by the validator as a consumer-only authoring shape with the same posture as `Node.withExtraAttribute`'s "AI-opaque" note.

## Validator coverage

The `Fuaran.UI.Validator` build-time AST walker emits `FUARAN060` (Warning) when it sees a `Node.withExtraAttribute` call whose key argument is a string literal that violates the data-* / aria-* prefix rule OR matches a known dangerous prefix (`on*` event handlers, `style`). Build-time signal complements the render-time floor: authors catch the mistake at compile time instead of relying on the renderer to silently drop the attribute.

The validator does NOT walk the typed tree's record-with bypass (`{ node with ExtraAttributes = ... }`) — that surface is the AI-opaque hatch by design, and the render-time `Sanitize.sanitizeExtraAttributes` filter is the floor that catches it.

## Test coverage

`Fuaran.UI.Tests/SanitizeTests.fs` (Phase 56) pins:

- Script-tag injection through `Markdown.toHtml` is neutralised.
- `onclick=`, `onerror=`, `onload=` attributes are stripped from markdown HTML.
- `javascript:` / `vbscript:` URLs in markdown anchors are replaced with `about:blank`.
- `Node.ExtraAttributes` keys that don't match data-* / aria-* are dropped at render time even when the smart-ctor gate is bypassed.
- `on*=` keys are rejected even if hand-built into the map.
- `style` is rejected.
- **Attribute-name injection (Phase 788)** — a prefix-valid key that terminates its own name
  (`data-x=1 onmouseover=alert(document.domain) z`) is rejected, as are keys carrying quote
  characters, angle brackets, `/`, whitespace or control bytes; a surviving key is emitted trimmed.
  `Fuaran.UI.Renderer.Server.Tests/ServerRenderTests.fs` pins the same payload on the **real emitted
  HTML string** for the SSR path, each negative assertion paired with a positive control proving the
  `ExtraAttributes` emission path is live, plus a go-red self-test showing that the same payload
  handed straight to `prop.custom` *does* produce the live handler — so the gate is demonstrably
  what neutralises it.
- `CellKindErased.Link` with a `javascript:` href resolves to `about:blank`.
- Legitimate `data-cy` / `aria-describedby` keys pass through unchanged (Phase 12.F + Phase 12.I regression floor).
- Legitimate http / https / mailto / tel hrefs pass through unchanged.
- **The adversarial floor corpus (Phase 214)** pins the markdown-HTML sweep against nested / double /
  split-tag, case + whitespace, and malformed-nesting evasions (the floor holds: no live open
  dangerous tag survives), and pins the documented non-goals (intra-scheme whitespace, entity-encoded
  schemes, slash-separated attributes) at their actual uncaught behaviour — see "Adversarial floor
  corpus (Phase 214)" above.

## Reference

- [`src/Fuaran.UI.Renderer.Core/Sanitize.fs`](src/Fuaran.UI.Renderer.Core/Sanitize.fs) — implementation (shared by the client and server renderers).
- [`src/Fuaran.UI.Tests/SanitizeTests.fs`](src/Fuaran.UI.Tests/SanitizeTests.fs) — XSS-payload corpus.
- [`src/Fuaran.UI.Renderer.Server.Tests/ServerRenderTests.fs`](src/Fuaran.UI.Renderer.Server.Tests/ServerRenderTests.fs) — SSR attribute-name-injection assertions on the emitted HTML string.
- [`STABILITY.md`](STABILITY.md) — language-tier stability policy (which surfaces are stable).
- [`docs/VALIDATOR-MANIFEST.md`](docs/VALIDATOR-MANIFEST.md) — validator codes including FUARAN060.
- [`CLAUDE.md`](CLAUDE.md) — repo conventions.
