namespace Fuaran.UI.Site

// ─── RenderPlan — the startup pre-render ─────────────────────────────────────
//
// A pure-SSR site's pages are static, so they render ONCE — at startup, or at
// export time — and are served as strings thereafter. `RenderPlan.compute`
// takes the page set plus the three host-owned seams (layout dispatch, body
// renderer, document shell) and produces the per-route rendered-page list,
// gated by SiteCheck so an invalid page set refuses to become a plan.
//
// The layout dispatch is a `Map` keyed by layout name: its key set IS the
// known-layout set the gate checks, so an unknown layout cannot silently fall
// through to a default — the same declaration feeds both the gate and the
// dispatch. Each page's shell also receives the auto-nav projected from the
// page set with that page marked current, so hosts place primary navigation in
// their chrome with zero per-page wiring.

open Fuaran.UI.Types

/// Per-page context handed to the host's document shell alongside the rendered
/// body: the page itself plus the nav projected for it (current page marked).
type PageContext<'Msg> =
    {
        Page: SitePage
        /// `Nav.project pages page.Route` — ordered `Link` nodes with this page
        /// marked `aria-current="page"`. Render it wherever the chrome puts
        /// primary navigation (or ignore it for a nav-less site).
        Nav: Node<'Msg>
    }

/// The three host-owned seams a render plan is computed through. The host owns
/// content, layout trees, and chrome; this layer owns discovery, routes,
/// gates, and export.
type SiteSeams<'Msg> =
    {
        /// Layout dispatch: layout name → page → body tree. The key set is
        /// the known-layout set SiteCheck gates on — one declaration, no
        /// silent fall-through.
        Layouts: Map<string, SitePage -> Node<'Msg>>
        /// Render one body tree to its HTML fragment (typically the server
        /// renderer's `render`, partially applied with the host's resolver).
        RenderBody: SitePage -> Node<'Msg> -> string
        /// Wrap a rendered body fragment in the host's document shell —
        /// `<head>`, chrome, stylesheets. The context carries the page and
        /// its projected nav.
        Shell: PageContext<'Msg> -> string -> string
    }

/// The computed plan: every page beside its fully rendered document, in page-set
/// order, plus any warning-severity findings from the gate.
type RenderPlan =
    { Pages: (SitePage * string) list
      Warnings: SiteIssue list }

[<RequireQualifiedAccess>]
module RenderPlan =

    /// The known-layout set a seam declaration implies (its dispatch keys).
    let knownLayoutsOf (seams: SiteSeams<'Msg>) : Set<string> =
        seams.Layouts |> Map.toSeq |> Seq.map fst |> Set.ofSeq

    /// Gate the page set, then render every page once: dispatch its layout,
    /// render the body, wrap it in the shell (with the page's projected nav).
    /// Any error-severity finding refuses the plan with the full issue list.
    let compute (seams: SiteSeams<'Msg>) (pages: SitePage list) : Result<RenderPlan, SiteIssue list> =
        let issues = SiteCheck.run (knownLayoutsOf seams) pages

        if SiteCheck.hasErrors issues then
            Error issues
        else
            let rendered =
                pages
                |> List.map (fun page ->
                    // Total by the gate: every page.Layout is a dispatch key.
                    let toTree = Map.find page.Layout seams.Layouts
                    let body = seams.RenderBody page (toTree page)

                    let context =
                        { Page = page
                          Nav = Nav.project pages page.Route }

                    page, seams.Shell context body)

            Ok
                { Pages = rendered
                  Warnings = issues |> List.filter (fun i -> i.Severity = SiteSeverity.Warning) }

    /// The rendered document for a route, when the plan has one.
    let tryFind (route: string) (plan: RenderPlan) : string option =
        plan.Pages
        |> List.tryPick (fun (page, html) -> if page.Route = route then Some html else None)
