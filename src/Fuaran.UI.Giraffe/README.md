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
let shell =
    { DocumentShell.create "Pricing — Acme" with
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

Every response carries a **deterministic strong ETag** (SHA-256 over the
canonical tree wire-form + theme CSS + shell signature) and honours
`If-None-Match` → `304 Not Modified` without re-rendering. A host-supplied
`IFuaranRenderCache` is consulted before render and populated after — the
default `RenderCache.none` is a zero-cost pass-through. `RenderCache.inMemory ()`
is a **bounded** in-process store (`RenderCache.defaultCapacity` documents, LRU
eviction); `RenderCache.bounded n` sizes it to your own working set. The bound
matters because the key is a content hash: a high-fan-out surface mints a fresh
key per distinct tree, so a store that never evicts grows for the process
lifetime.

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
