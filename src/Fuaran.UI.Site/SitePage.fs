namespace Fuaran.UI.Site

// ─── The page model + frontmatter parser + content loader ───────────────────
//
// A site's content directory is a set of markdown files with a leading
// `---`-fenced frontmatter block. `SitePage` is the parsed page model the rest
// of the page-set layer (SiteCheck / Nav / RenderPlan / Export) operates on.
//
// Everything except the two loader functions at the bottom is pure — the
// frontmatter split and the page construction take strings, so the model is
// testable without touching a filesystem. The host owns the CONTENT (what the
// pages say, which layouts exist, what the chrome looks like); this layer owns
// the MECHANISM (discovery, parsing, routes, gates, export planning).

open System
open System.IO

/// A site page parsed from a markdown source: canonical route, parsed
/// frontmatter, and the raw markdown body with the frontmatter block stripped.
type SitePage =
    {
        /// Canonical route, no trailing slash: "/", "/pricing", "/guide/wire".
        Route: string
        /// Where the page came from (an absolute file path, or a synthetic
        /// name for in-memory pages) — used in gate reports.
        SourcePath: string
        /// Page title (frontmatter `title`; falls back to the route's last
        /// segment, "Home" for "/").
        Title: string
        /// Meta description (frontmatter `description`), when present.
        Description: string option
        /// Layout name (frontmatter `layout`; defaults to "page"). Must name
        /// a layout the host registers — SiteCheck errors on an unknown one.
        Layout: string
        /// The full parsed frontmatter map (later duplicate keys win), so a
        /// host or an extension (e.g. the nav contract) can read its own keys.
        Frontmatter: Map<string, string>
        /// Raw markdown body with the frontmatter block stripped.
        Body: string
    }

[<RequireQualifiedAccess>]
module SitePage =

    /// The layout name a page gets when its frontmatter declares none.
    [<Literal>]
    let DefaultLayout = "page"

    // ─── Frontmatter ─────────────────────────────────────────────────────────

    /// Split a leading `---`-fenced frontmatter block from the body. Returns
    /// the (key, value) pairs and the remaining markdown body. A file with no
    /// leading `---` has empty frontmatter and the whole text as body; an
    /// unterminated block is treated as no frontmatter. Values may be
    /// double-quoted; `#` comment lines and blanks are ignored (a strict,
    /// hand-parsed subset — no YAML dependency).
    let splitFrontmatter (text: string) : (string * string) list * string =
        let norm = text.Replace("\r\n", "\n")

        if not (norm.StartsWith("---\n", StringComparison.Ordinal)) then
            [], norm
        else
            let afterOpen = norm.Substring 4

            match afterOpen.IndexOf("\n---", StringComparison.Ordinal) with
            | -1 -> [], norm // unterminated — treat as no frontmatter
            | close ->
                let fm = afterOpen.Substring(0, close)
                let rest = afterOpen.Substring(close + 4) // skip "\n---"
                let body = rest.TrimStart('\n')

                let pairs =
                    fm.Split('\n')
                    |> Array.choose (fun line ->
                        let t = line.Trim()

                        if t = "" || t.StartsWith "#" then
                            None
                        else
                            match t.IndexOf ':' with
                            | -1 -> None
                            | i ->
                                let k = t.Substring(0, i).Trim()
                                let v = t.Substring(i + 1).Trim().Trim('"')
                                Some(k, v))
                    |> Array.toList

                pairs, body

    /// A human title derived from a route when frontmatter carries no `title`.
    let private titleFromRoute (route: string) : string =
        if route = "/" then
            "Home"
        else
            route.TrimStart('/').Split('/') |> Array.last

    // ─── Construction (pure) ─────────────────────────────────────────────────

    /// Build a page from its source text — the pure core `loadFile` wraps.
    let ofText (sourcePath: string) (route: string) (text: string) : SitePage =
        let pairs, body = splitFrontmatter text
        let fm = Map.ofList pairs

        { Route = route
          SourcePath = sourcePath
          Title = Map.tryFind "title" fm |> Option.defaultValue (titleFromRoute route)
          Description = Map.tryFind "description" fm
          Layout = Map.tryFind "layout" fm |> Option.defaultValue DefaultLayout
          Frontmatter = fm
          Body = body }

    /// The raw markdown for a page's agent route — the frontmatter-stripped
    /// body, with the page title prepended as an `# H1` when the body has none
    /// (bodies often drop their H1 for the layout to inject). Self-describing
    /// for an agent that fetched only this file.
    let agentMarkdown (page: SitePage) : string =
        if page.Body.TrimStart().StartsWith("# ", StringComparison.Ordinal) then
            page.Body
        else
            sprintf "# %s\n\n%s" page.Title page.Body

    // ─── File I/O surface (the only functions here that touch a filesystem) ──

    /// Load one page file under `pagesRoot` into a `SitePage`.
    let loadFile (pagesRoot: string) (file: string) : SitePage =
        ofText file (Routes.ofFile pagesRoot file) (File.ReadAllText file)

    /// Discover + load every `*.md` page under `pagesRoot` (recursive),
    /// ordered by route for a deterministic page set.
    let loadAll (pagesRoot: string) : SitePage list =
        Directory.EnumerateFiles(pagesRoot, "*.md", SearchOption.AllDirectories)
        |> Seq.map (loadFile pagesRoot)
        |> Seq.sortBy (fun p -> p.Route)
        |> List.ofSeq
