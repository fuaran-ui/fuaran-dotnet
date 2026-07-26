# Fuaran.UI.Renderer.Server

The **server-side renderer** for the Fuaran UI language. Turns a `Node<'Msg>`
tree into an HTML **string** on plain .NET — no React, no Fable — via
`Feliz.ViewEngine`, so a Giraffe / ASP.NET host can server-render Fuaran chrome
for SEO surfaces with no client-runtime requirement and no visual degradation.

The emitted **class names + ARIA attributes** match the Feliz client renderer
(`Fuaran.UI.Renderer`) for the same tree — the load-bearing parity property,
locked by the conformance corpus. The class-name vocabulary, accessibility
projection, sanitize, binding resolution, and locale formatting all come from
the shared `Fuaran.UI.Renderer.Core` spine, so the two renderers cannot drift on
those axes.

## Usage

```fsharp
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

// Body-fragment HTML (the host owns <html>/<head>/meta/CSS):
let html = Render.render BindingResolver.empty tree

// With the Theme :root variable block prepended:
let withTheme = Render.renderWithTheme Defaults.theme BindingResolver.empty tree

// Or compose into a host's own ViewEngine document:
let element = Render.renderToElement BindingResolver.empty tree
```

## Server semantics

See [`fuaran-dotnet/docs/SSR.md`](../../docs/SSR.md) for the full per-kind behaviour
table. In brief: no runtime / no dispatch (Action-bearing nodes render inert; a
`Link` is the crawlable no-JS path); the resolved/loaded `StateBehaviour` branch
renders by default; client-library visualisations (`Chart` / `Map` / `DataGrid`)
render deterministic placeholders for later hydration; `Markdown` renders real
HTML via Markdig. The host owns the document shell + serving the reference CSS.
