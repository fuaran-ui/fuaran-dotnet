# Fuaran.UI.Site

The **page-set layer** for pure-SSR Fuaran sites: stand up a multi-page site
from a `content/pages/*.md` directory with only a layout-dispatch map and a
document shell.

## The boundary

**The host owns content, layout trees, and chrome. This package owns
discovery, routes, gates, and export.** Nothing here renders HTML or serves a
request — the body renderer and the document shell are host-supplied seams, so
the layer stays host-neutral (any renderer, any web framework, or none at all
for a pure static export).

| This package | The host |
|---|---|
| Page discovery + frontmatter parsing (`SitePage`) | The markdown content itself |
| Filename → route derivation (`Routes`) | — |
| The typed content gate (`SiteCheck`) | Failing its build on the gate |
| The once-computed render plan (`RenderPlan`) | Layout trees, the body renderer, the document shell |
| Static-export planning + writing (`Export`) | The deploy target |

## Pages

A page is a markdown file with a leading `---`-fenced frontmatter block
(`key: value` lines; values may be double-quoted; `#` comments ignored — a
strict hand-parsed subset, no YAML dependency):

```markdown
---
title: Pricing
description: Simple, transparent pricing.
layout: page
---

Body markdown…
```

Routes derive from filenames: `index.md` → `/`, `pricing.md` → `/pricing`,
`guide/index.md` → `/guide`, `guide/wire.md` → `/guide/wire`. No trailing
slashes. `Routes.mdRouteOf` gives each route its raw-markdown agent twin
(`/pricing` → `/pricing.md`); `SitePage.agentMarkdown` gives the content
(body with the title prepended as an `# H1` when the body has none).

## The gate

`SiteCheck.run knownLayouts pages` returns typed findings — duplicate routes,
an **unknown layout** (never a silent fall-through to a default), an empty
title — each an error a host should fail its build on.

## The render plan

```fsharp
open Fuaran.UI.Site

let seams: SiteSeams<obj> =
    { Layouts =
        Map.ofList
            [ "page", fun page -> Layouts.prose page
              "landing", fun page -> Layouts.landing page ]
      RenderBody = fun _ tree -> MyRenderer.render tree
      Shell = fun ctx body -> MyShell.document ctx.Page body }

match RenderPlan.compute seams (SitePage.loadAll pagesRoot) with
| Ok plan -> plan // (SitePage * renderedDocument) list, computed once
| Error issues -> failwith (SiteCheck.describe issues) // make it a build failure
```

The dispatch map's key set **is** the known-layout set the gate checks — one
declaration feeds both, so an unadmitted layout cannot reach the dispatch.

## Static export

`Export.writeAll baseUrl publicRoot outDir plan` writes the static mirror:
`/` → `index.html`, `/x` → `x/index.html`, `sitemap.xml` from the page set,
`robots.txt`, and a recursive public-asset copy. The path mapping and the
sitemap/robots content are pure functions an SSR host can reuse for the same
documents.

## Scope

Depends only on `Fuaran.UI` (the `Node` tree the dispatch produces). No
Giraffe / ASP.NET dependency — the Giraffe adapter is the separate
`Fuaran.UI.Site.Giraffe` package. No markdown renderer — the body is kept raw
for the host's layout trees to render as they choose.

Apache-2.0.
