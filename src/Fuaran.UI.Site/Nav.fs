module Fuaran.UI.Site.Nav

// ─── Auto-nav projection from the page set ───────────────────────────────────
//
// Navigation derived from the pages themselves, so nav chrome is never
// hand-maintained beside the page set (the double-maintenance defect every
// hand-rolled site host eventually grows). The frontmatter contract:
//
//   nav-order: 10          # int; PRESENT ⇒ the page appears in the nav
//   nav-title: Pricing     # optional; defaults to the page title
//
// A page without `nav-order` is not in the nav. `project` emits a plain Fuaran
// composition — ordered `Link` nodes in a `Group`-role container with the
// navigation ARIA role — no new node kind, and every link is a real crawlable
// `<a href>`. The current page is marked semantically with `aria-current="page"`
// (via the node contract's `aria-*` extra-attribute hatch), not a style hack.
//
// SiteCheck gates the contract: a non-integer `nav-order` is an error, a
// duplicate `nav-order` a warning (the tie is still deterministic — see
// `entries`).

open System
open System.Globalization
open Fuaran.UI
open Fuaran.UI.Types

/// The frontmatter key that opts a page into the nav and orders it.
[<Literal>]
let OrderKey = "nav-order"

/// The frontmatter key that overrides the nav label (defaults to the title).
[<Literal>]
let TitleKey = "nav-title"

/// One page's presence in the nav.
type NavEntry =
    { Route: string
      Title: string
      Order: int }

/// Read a page's nav declaration: `Ok None` = not in the nav (no `nav-order`),
/// `Ok (Some entry)` = in the nav, `Error` = a malformed (non-integer)
/// `nav-order` — surfaced as a SiteCheck error, never silently dropped.
let entryOf (page: SitePage) : Result<NavEntry option, string> =
    match Map.tryFind OrderKey page.Frontmatter with
    | None -> Ok None
    | Some raw ->
        match Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture) with
        | true, order ->
            let title = Map.tryFind TitleKey page.Frontmatter |> Option.defaultValue page.Title

            Ok(
                Some
                    { Route = page.Route
                      Title = title
                      Order = order }
            )
        | false, _ -> Error(sprintf "%s '%s' is not an integer" OrderKey raw)

/// The nav entries of a page set, ordered by (`nav-order`, route) — the route
/// tie-break keeps the projection deterministic under equal orders. Malformed
/// declarations are excluded here (SiteCheck reports them as errors).
let entries (pages: SitePage list) : NavEntry list =
    pages
    |> List.choose (fun p ->
        match entryOf p with
        | Ok(Some e) -> Some e
        | Ok None
        | Error _ -> None)
    |> List.sortBy (fun e -> e.Order, e.Route)

/// Deterministic node id for a nav link ("/" → "site-nav-index").
let private linkId (route: string) : string =
    if route = "/" then
        "site-nav-index"
    else
        "site-nav-" + route.TrimStart('/').Replace('/', '-')

/// Project the page set's nav as a plain Fuaran composition: ordered `Link`
/// nodes in a horizontal `Group` container carrying the navigation ARIA role.
/// The entry whose route equals `currentRoute` is marked `aria-current="page"`.
/// Hosts place the returned node in their document shell once — pages opt in
/// by frontmatter alone, with zero per-page wiring.
let project (pages: SitePage list) (currentRoute: string) : Node<'Msg> =
    let links =
        entries pages
        |> List.map (fun e ->
            let link = Fuaran.link (linkId e.Route) e.Route e.Title

            if e.Route = currentRoute then
                link |> Node.withExtraAttribute "aria-current" "page"
            else
                link)

    Fuaran.stack
        "site-nav"
        { Orientation = Orientation.Horizontal
          Wrap = true
          Children = links }
    |> Node.withAccessibility (
        Some
            { Defaults.Accessibility.empty with
                Role = Some AriaRole.Navigation }
    )
