# Migration — Phase 1026: the ambient destination policy

**Breaking.** `RenderContext`, `ServerRenderContext`, `DriverServices`, `EmailOptions` and
`FuaranGiraffeOptions` each gain one required field. If you construct any of those records
literally, you must add it; if you build them through the shipped constructors, you do not.

The behavioural change is larger than the type change, and it is the point: **a rendered tree can
no longer reach a destination the host has not declared.**

---

## What widened

[Phase 897](../../../roadmap/phases/897-destination-policy.md) shipped the typed origin allowlist —
`Sanitize.EgressPolicy`, `EgressClass`, `checkDestination`, `sanitizeUrlForEgress` — and left every
renderer emission site calling `sanitizeUrlOrBlank`. The policy was therefore **available, not
ambient**: it decided nothing unless a caller went out of its way to ask.

1026 moves it onto the record every render already threads:

| Type | New field | Default |
|---|---|---|
| `Render.RenderContext<'Msg>` (client) | `EgressPolicy` | `Sanitize.denyNonLocalEgress` |
| `Render.ServerRenderContext` (SSR) | `EgressPolicy` | `Sanitize.denyNonLocalEgress` |
| `Driver.DriverServices<'Msg>` | `EgressPolicy` | `denyNonLocalEgress`; `permissiveEgress` in `createPermissive` |
| `Email.EmailOptions` | `EgressPolicy` | `Sanitize.denyNonLocalEgress` |
| `FuaranGiraffeOptions` | `EgressPolicy` | `Sanitize.denyNonLocalEgress` |

Every `href`, `src` and tree-declared route the renderer emits now routes through
`Sanitize.sanitizeUrlForEgress` (or `checkDestination`) with that policy.

## The default, and why it is that one

`denyNonLocalEgress` is:

```fsharp
{ Rules = []; AllowAnyOrigin = false; AllowLocal = true; AllowNonNetwork = false }
```

- **Same-origin is allowed.** Relative paths, fragments and empty URLs render exactly as before. The
  default denies *leaving*, not *linking* — ordinary in-app navigation is untouched.
- **Every absolute network destination is refused** unless a rule names its host.
- **`mailto:` and `tel:` are refused.** See the surprise below.

A decoded tree cannot declare its own egress, so absent a host's declaration it gets none. This is
the same inversion the dispatch gate took in Phase 782 and the effect registry in 897.

## What a refusal looks like

Not a silent neuter. The emitted `href` / `src` becomes:

```
about:blank#fuaran-egress-refused
```

and the element carries `data-fuaran-egress-refused="<class>:<host>"` — for example
`hyperlink:collector.example` or `media:cdn.example`. "Nothing happened" and "this was refused" are
different facts and only one of them is debuggable.

**The marker carries the class and the host, never the path or the query.** The query string of a
refused exfiltration attempt is the payload itself, so a refusal record that quoted it would become
the disclosure it exists to prevent.

A refused **route** (`Action.Navigate`) emits nothing at all rather than navigating to `about:blank`:
an anchor has to stay structurally valid, a navigation does not, and a redirect the author never
asked for is not an improvement on a refused one.

---

## Adopting

### 1. If you build a context or options record literally

Add the field. For a **hand-authored** tree, where the author is the trust boundary,
`Sanitize.permissiveEgress` is the correct and honest value:

```fsharp
{ Sources = sources
  // …
  EgressPolicy = Sanitize.permissiveEgress }
```

For anything that renders a **decoded** tree, do not do that. Declare instead.

### 2. Declaring your egress — the shape to prefer

```fsharp
let policy =
    Sanitize.denyNonLocalEgress
    |> Sanitize.allowOrigin (Sanitize.HostSuffix "cdn.example")   [ Sanitize.EgressClass.Media ]
    |> Sanitize.allowOrigin (Sanitize.ExactHost  "docs.example")  [ Sanitize.EgressClass.Hyperlink ]
```

- A rule names a **host** — never a scheme, never a path. Scheme is already reduced to an allowlist
  upstream, and a path is not a security boundary.
- `HostSuffix "example.com"` matches `example.com` and `a.b.example.com`, and never
  `notexample.com` — it matches at a label boundary, so it is a suffix rather than a substring.
- Rules are **class-scoped**. Declaring a host for `Media` does not admit a `Hyperlink` to it. An
  empty class list in `allowOrigin` is the ergonomic "every class"; an `EgressRule` whose `Classes`
  is empty permits nothing.

### 3. Reaching a policy from the shipped entry points

| Tier | Named opt-out / declaration point |
|---|---|
| Client renderer | `Render.renderWithSourcesAndEgress sources policy dispatch node` |
| SSR | `Render.renderWithEgress policy customs sources node`, `Render.mkContextWithEgress` |
| SSR islands | `Hydration.renderWithIslandsAndEgress policy sources node` |
| Giraffe | `{ FuaranGiraffeOptions.create with EgressPolicy = policy }` |
| Email | `{ Email.defaults with EgressPolicy = policy }` |
| Server-driven | `{ DriverServices.create render with EgressPolicy = policy }` |

A host needing a policy **and** a telemetry sink / session context / action sink constructs the
`RenderContext` record directly — it is public, and minting an entry-point permutation per field is
combinatorial growth that family is already close to.

Reaching the permissive posture is deliberately **by name**, so `grep permissiveEgress` finds every
host that has opted back out. The opt-out is visible in the host's own source rather than inherited
silently.

---

## The one that will surprise you: `mailto:`

`AllowNonNetwork = false` in the default, so **a `mailto:` or `tel:` link is refused unless you say
otherwise.** If you ship a contact link, a `Fuaran.emailLink`, or an email digest, this is the change
you will meet first.

It is deliberate rather than an oversight. `mailto:` *is* an egress channel — a `body` parameter
carries arbitrary text off the machine — and it has no host for a rule to name, so it cannot be
allowlisted, only permitted wholesale. Permitting it *by omission* is precisely the failure the
default exists to prevent.

The narrow remedy, which is a one-field widening rather than a jump to permissive:

```fsharp
let policy = { Sanitize.denyNonLocalEgress with AllowNonNetwork = true }
```

---

## Cache keys

`FuaranGiraffeOptions.EgressPolicy` is folded into the ETag via `Sanitize.encodeEgressPolicy` (which
is canonical and sorted, so it is stable across runs). **If you compute your own render cache key,
add the policy to it.** Two policies render the same tree to different documents; a cache that cannot
tell them apart will serve one host's egress decision to another host's request, and the bug presents
as an egress-policy failure — the worst way to meet one.

---

## What this does NOT cover

The inventory's value is that it is complete, so:

- ~~**Markdown link and image destinations are not policy-checked.**~~ **CLOSED by
  [Phase 1032](1032-markdown-egress.md) in 0.35.0.** It was a live gap at 0.33.0, for the reason
  recorded here: `Markdown.toHtml` is a pure `string -> string` function pinned by a canonical
  cross-host corpus, so threading a policy through it was a wire-adjacent forward-coupling event
  rather than a call-site adoption, and doing it quietly inside 1026 would have been the wrong act.
  It was done as its own act instead — the renderer tiers pass `ctx.EgressPolicy` into markdown, a
  refused markdown destination renders the same refusal shape as every other, and the cross-host
  corpus pins it in every conformant host. **If you are pinned to 0.33.x this bullet still describes
  your version**: treat a decoded markdown body as an egress surface and sanitise upstream until you
  adopt 0.35.0.
- **`Fuaran.UI.Giraffe.DocumentShell`** (canonical link, stylesheet hrefs, script srcs) is
  host-authored document furniture, not tree-authored, and is unchanged.
- **`EmailOptions.LiveUrl`** is supplied by the host in that same record, so it is not checked
  against the host's own allowlist.
- **The email projection drops the refusal ATTRIBUTE** (the refused URL itself still applies):
  `data-*` attributes do not survive the sanitisers most mail clients run, so emitting a marker there
  would put a signal in the document that cannot be relied on to arrive.
- **A registered performer is not governed.** The policy decides where a destination *named by the
  tree* may be reached. It says nothing about where a host's own `Custom` renderer, `Mount` guest
  loader, or effect performer goes — those are ordinary host code with the process's ambient
  authority. See [`ESCAPE-HATCHES.md`](../../../docs/security/ESCAPE-HATCHES.md) Hatch 1 and Hatch 2.

## See also

- [`ESCAPE-HATCHES.md`](../../../docs/security/ESCAPE-HATCHES.md) — Hatch 1, amended by this phase.
- [`SANITIZATION.md`](../../SANITIZATION.md) — the render-time sanitization contract.
- `Fuaran.UI.Renderer.Server.Tests/EgressRenderTests.fs` — the executable form of everything above.
