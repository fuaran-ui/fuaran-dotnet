namespace Fuaran.UI.Site

// ─── SiteCheck — the typed content gate ──────────────────────────────────────
//
// Validate a loaded page set before anything renders, so a defect is a build
// failure rather than a published broken page. Errors: duplicate routes, an
// unknown layout (never a silent fall-through to some default), an empty
// title, a non-integer `nav-order`. Warnings: a duplicated `nav-order` (the
// projection is still deterministic — the route tie-break — but the ordering
// intent is probably unfinished).

open System

/// Severity of a page-set finding: `Error` should fail the build; `Warning`
/// should be surfaced but does not block rendering.
[<RequireQualifiedAccess>]
type SiteSeverity =
    | Error
    | Warning

/// One page-set finding: severity + the offending route/path + a message.
type SiteIssue =
    { Severity: SiteSeverity
      Where: string
      Detail: string }

[<RequireQualifiedAccess>]
module SiteCheck =

    /// Validate a page set against the layouts the host registers: route
    /// uniqueness, known-layout membership, non-empty titles, and the nav
    /// frontmatter contract (`nav-order` integer; duplicate orders warned).
    let run (knownLayouts: Set<string>) (pages: SitePage list) : SiteIssue list =
        let duplicateRoutes =
            pages
            |> List.groupBy (fun p -> p.Route)
            |> List.choose (fun (r, ps) ->
                if List.length ps > 1 then
                    Some
                        { Severity = SiteSeverity.Error
                          Where = r
                          Detail = sprintf "%d pages map to the same route %s" (List.length ps) r }
                else
                    None)

        let unknownLayouts =
            pages
            |> List.choose (fun p ->
                if Set.contains p.Layout knownLayouts then
                    None
                else
                    Some
                        { Severity = SiteSeverity.Error
                          Where = p.SourcePath
                          Detail =
                            sprintf
                                "unknown layout '%s' (known: %s)"
                                p.Layout
                                (String.concat ", " (Set.toList knownLayouts)) })

        let emptyTitles =
            pages
            |> List.choose (fun p ->
                if String.IsNullOrWhiteSpace p.Title then
                    Some
                        { Severity = SiteSeverity.Error
                          Where = p.SourcePath
                          Detail = "empty title" }
                else
                    None)

        let malformedNavOrders =
            pages
            |> List.choose (fun p ->
                match Nav.entryOf p with
                | Error detail ->
                    Some
                        { Severity = SiteSeverity.Error
                          Where = p.SourcePath
                          Detail = detail }
                | Ok _ -> None)

        let duplicateNavOrders =
            Nav.entries pages
            |> List.groupBy (fun e -> e.Order)
            |> List.choose (fun (order, es) ->
                if List.length es > 1 then
                    Some
                        { Severity = SiteSeverity.Warning
                          Where = es |> List.map (fun e -> e.Route) |> String.concat ", "
                          Detail = sprintf "%d pages share %s %d" (List.length es) Nav.OrderKey order }
                else
                    None)

        duplicateRoutes
        @ unknownLayouts
        @ emptyTitles
        @ malformedNavOrders
        @ duplicateNavOrders

    /// The error-severity findings only.
    let errors (issues: SiteIssue list) : SiteIssue list =
        issues |> List.filter (fun i -> i.Severity = SiteSeverity.Error)

    /// Whether any finding is an error (the "fail the build" verdict).
    let hasErrors (issues: SiteIssue list) : bool =
        issues |> List.exists (fun i -> i.Severity = SiteSeverity.Error)

    /// A one-line-per-finding report suitable for build output.
    let describe (issues: SiteIssue list) : string =
        issues
        |> List.map (fun i ->
            let tag =
                match i.Severity with
                | SiteSeverity.Error -> "error"
                | SiteSeverity.Warning -> "warning"

            sprintf "%s: %s: %s" tag i.Where i.Detail)
        |> String.concat "\n"
