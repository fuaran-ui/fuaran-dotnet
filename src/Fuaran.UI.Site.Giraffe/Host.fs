namespace Fuaran.UI.Site.Giraffe

// ─── The Giraffe host adapter for a page-set site ────────────────────────────
//
// Serve a computed `RenderPlan` through Giraffe: one exact-match GET route per
// rendered page, an optional `.md` agent route per page (raw markdown, so an
// agent retrieves the page source without HTML), and the SEO pair
// (sitemap.xml + robots.txt) derived from the same page set. Pages were
// rendered once at plan time — every handler serves a string.
//
// Giraffe is isolated to this package, matching the split between the
// language tier and its SSR host adapter: `Fuaran.UI.Site` stays web-
// framework-free.

open Giraffe
open Fuaran.UI.Site

[<AutoOpen>]
module Host =

    /// One GET route per rendered page in the plan, serving its pre-rendered
    /// document. Unmatched paths fall through to the host's own handlers.
    let sitePages (plan: RenderPlan) : HttpHandler =
        plan.Pages
        |> List.map (fun (page, html) -> route page.Route >=> htmlString html)
        |> choose

    /// The optional raw-markdown agent route per page: `/x` → `/x.md`
    /// (`/` → `/index.md`), served as `text/markdown` from
    /// `SitePage.agentMarkdown`.
    let siteMarkdown (plan: RenderPlan) : HttpHandler =
        plan.Pages
        |> List.map (fun (page, _) ->
            route (Routes.mdRouteOf page.Route)
            >=> setHttpHeader "Content-Type" "text/markdown; charset=utf-8"
            >=> setBodyFromString (SitePage.agentMarkdown page))
        |> choose

    /// sitemap.xml + robots.txt from the plan's page set. `baseUrl` is the
    /// site origin, e.g. "https://example.org".
    let siteSeo (baseUrl: string) (plan: RenderPlan) : HttpHandler =
        let pages = plan.Pages |> List.map fst

        choose
            [ route "/sitemap.xml"
              >=> setHttpHeader "Content-Type" "application/xml"
              >=> setBodyFromString (Export.sitemapXml baseUrl pages)
              route "/robots.txt"
              >=> setHttpHeader "Content-Type" "text/plain; charset=utf-8"
              >=> setBodyFromString (Export.robotsTxt baseUrl) ]

    /// The whole site in one handler: pages + markdown agent routes + SEO.
    /// Compose the pieces yourself when you want a different set.
    let site (baseUrl: string) (plan: RenderPlan) : HttpHandler =
        choose [ sitePages plan; siteMarkdown plan; siteSeo baseUrl plan ]
