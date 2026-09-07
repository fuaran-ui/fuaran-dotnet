# Fuaran.UI.Giraffe

Thin SSR host-integration adapter for **Giraffe / ASP.NET** (Phase 162). The
Wave-18 server stack (`Fuaran.UI.Renderer.Server`) emits the *body fragment* for
a `Node` tree; this package supplies the last mile — the document shell, the
page `HttpHandler` plumbing, and a render-cache seam — so a consumer stops
hand-rolling the same boilerplate.

```fsharp
open Fuaran.UI.Giraffe

// Register once (binding sources, server Custom registry, optional theme, cache).
let opts =
    { FuaranGiraffeOptions.create with
        Theme = Some Fuaran.UI.Defaults.theme
        Cache = RenderCache.inMemory () }

// A crawlable page in one HttpHandler call.
// `withLocale` drives BOTH `lang` and `dir` on `<html>` — one declaration, so
// the two cannot disagree. A shell that declares no locale emits neither.
let shell =
    { (DocumentShell.create "Pricing — Acme" |> DocumentShell.withLocale "en") with
        MetaDescription = Some "Simple, transparent pricing."
        Canonical = Some "https://acme.example/pricing"
        OpenGraph = [ "og:title", "Pricing — Acme"; "og:type", "website" ]
        JsonLd = [ """{"@context":"https://schema.org","@type":"Product","name":"Acme"}""" ]
        Stylesheets = [ "/fuaran-reference.css" ] }

let webApp =
    choose
        [ route "/pricing" >=> fuaranPage opts shell pricingTree
          route "/pricing/fragment" >=> fuaranFragment opts pricingTree
          route "/app" >=> fuaranHydratablePage opts shell appTree ]
```

## What you get

| Handler | Emits |
|---|---|
| `fuaranPage opts shell node` | A full `<!DOCTYPE html>` crawlable document, static SSR, no client runtime. |
| `fuaranHydratablePage opts shell node` | The document + the Phase 143 hydrate `<script>` payload, so the client `hydrateRoot` mount attaches. |
| `fuaranIslandsPage opts shell node` | Static document with **islands** (Phase 163) — per-`Node.asIsland` subtree, a `data-fuaran-island` boundary + an embedded hydrate `<script>`; everything else inert static HTML. |
| `fuaranFragment opts node` | The body fragment only (no shell) — for HTMX-style swaps / host-composed responses. |

Every response whose inputs have an identity carries a **deterministic strong
ETag** (SHA-256 over the canonical tree wire-form + the options identity + the
shell signature) and honours `If-None-Match` → `304 Not Modified` without
re-rendering. A host-supplied
`IFuaranRenderCache` is consulted before render and populated after — the
default `RenderCache.none` is a zero-cost pass-through. `RenderCache.inMemory ()`
is a **bounded** in-process store (`RenderCache.defaultCapacity` documents, LRU
eviction); `RenderCache.bounded n` sizes it to your own working set. The bound
matters because the key is a content hash: a high-fan-out surface mints a fresh
key per distinct tree, so a store that never evicts grows for the process
lifetime.

### Declare `SourcesKey` when your sources vary per request

The options identity covers the theme, the egress policy, the server `Custom`
registry, the ambient locale, the host-furnished instant and the i18n catalog.
It cannot cover `Sources.QueryResults` / `State` / `Filters` / `Selections` /
`ComputedContext`, nor a replaced i18n resolver or capability invoker: those are
`obj` values and host closures, and there is no total projection of them into
bytes. Only the host knows whether two source bags render the same document.

```fsharp
{ opts with
    Sources = sourcesFor user
    SourcesKey = Some (sprintf "u:%s|rev:%d" user.Id user.DataRevision) }
```

The key is a **cache key, not a secret** — it is hashed into a public ETag, so
give it a discriminator rather than a tenant's data.

Leave it `None` and the adapter will not guess. A request whose sources carry
host data and whose variant is unnamed is served **with no `ETag` and
`Cache-Control: no-store`**: a strong validator is a promise a browser keeps, and
one that cannot separate two users' documents is worse than none. Configuring a
render `Cache` in that state is refused where the handler is built, naming the
field that fixes it — silently dropping the cache would leave a host believing it
had one.

## Document shell

`DocumentShell` is host-*authored* but library-*shaped*: you own the head
*content* (title, meta description, canonical, Open Graph / Twitter cards,
JSON-LD, stylesheet + script refs); the wrapper plumbing is the library's.

Injection safety follows `SANITIZATION.md`: text fields HTML-escape via
`Feliz.ViewEngine`; URL fields route through `Renderer.Sanitize.sanitizeUrlOrBlank`
(a `javascript:` href becomes `about:blank`); **JSON-LD is the one sanctioned
raw-JSON injection point** (host-trusted) and is `<`/`>`/`&`-escaped for
`<script>` embedding so it cannot break out of the element.

## Giraffe isolation

Giraffe is a dependency of **this package only**. The language tier and
`Renderer.Server` stay Giraffe-free; this adapter composes with (does not depend
on) `Fuaran.UI.ServerDriven.AspNetCore`'s live endpoints.
