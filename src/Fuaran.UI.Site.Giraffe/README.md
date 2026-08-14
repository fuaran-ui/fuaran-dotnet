# Fuaran.UI.Site.Giraffe

Giraffe host adapter for the [`Fuaran.UI.Site`](https://www.nuget.org/packages/Fuaran.UI.Site)
page-set layer — the last mile between a computed `RenderPlan` and a running
SSR site.

```fsharp
open Fuaran.UI.Site
open Fuaran.UI.Site.Giraffe

let plan =
    match RenderPlan.compute seams (SitePage.loadAll pagesRoot) with
    | Ok plan -> plan
    | Error issues -> failwith (SiteCheck.describe issues)

// Pages + `.md` agent routes + sitemap.xml + robots.txt in one handler…
let webApp = site "https://example.org" plan

// …or compose exactly the pieces you want:
let webApp' =
    choose
        [ sitePages plan // one GET route per pre-rendered page
          siteMarkdown plan // `/x` → `/x.md`, text/markdown (agent surface)
          siteSeo "https://example.org" plan
          setStatusCode 404 >=> text "Not found" ]
```

Every page was rendered **once** at plan time — each handler serves a string.
The `.md` agent routes serve `SitePage.agentMarkdown` (the frontmatter-stripped
body, title prepended as an `# H1` when the body has none), so an agent can
retrieve page source without HTML.

## The boundary

The host owns content, layout trees, and chrome (all upstream, in the seams
`RenderPlan.compute` takes). `Fuaran.UI.Site` owns discovery, routes, gates,
and export. This package owns only the Giraffe wiring — and Giraffe is
isolated here: the page-set layer itself stays web-framework-free.

Apache-2.0.
