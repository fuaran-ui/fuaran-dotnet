module Fuaran.UI.Site.Export

// ─── Static export ───────────────────────────────────────────────────────────
//
// Turn a `RenderPlan` into a static mirror a file host can serve directly:
// `/` → `index.html`, `/x` → `x/index.html`, plus `sitemap.xml` from the page
// set, `robots.txt`, and a recursive public-asset copy. The path mapping and
// the sitemap/robots CONTENT are pure functions (testable, reusable by an SSR
// host serving the same documents); only the `write*`/`copy*` functions at the
// bottom touch a filesystem. Determinism is inherited: the plan renders once,
// and the export writes exactly those bytes.

open System
open System.IO
open System.Text

/// Route → export-relative file path, forward slashes: "/" → "index.html",
/// "/x" → "x/index.html", "/guide/wire" → "guide/wire/index.html".
let relativePathOf (route: string) : string =
    if route = "/" then
        "index.html"
    else
        route.TrimStart('/') + "/index.html"

/// XML-escape a string bound for element content.
///
/// The sitemap's `<loc>` is built by concatenation, and a route is DISCOVERED —
/// from a filename and its frontmatter — not authored here. An `&` in one (a
/// perfectly legal path character, and the commonest of these by far) produced a
/// document no conforming sitemap reader accepts; a `<` closed the element.
///
/// The full five-entity set rather than the three element content strictly
/// needs: the sitemap protocol's own escaping table lists all five, and this
/// string is one concatenation away from being an attribute value.
let private xmlEscape (s: string) : string =
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;")

/// sitemap.xml content for a page set. `baseUrl` is the site origin without a
/// trailing slash (one is trimmed if present), e.g. "https://example.org".
let sitemapXml (baseUrl: string) (pages: SitePage list) : string =
    let origin = baseUrl.TrimEnd('/')
    let sb = StringBuilder()

    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n") |> ignore

    sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n")
    |> ignore

    for page in pages do
        let path = if page.Route = "/" then "" else page.Route

        sb.Append(sprintf "  <url><loc>%s</loc></url>\n" (xmlEscape (origin + path)))
        |> ignore

    sb.Append("</urlset>\n") |> ignore
    sb.ToString()

/// robots.txt content: allow everything, point at the sitemap.
let robotsTxt (baseUrl: string) : string =
    let origin = baseUrl.TrimEnd('/')
    sprintf "User-agent: *\nAllow: /\n\nSitemap: %s/sitemap.xml\n" origin

// ─── File I/O surface (everything above is pure) ─────────────────────────────

/// Resolve `route` to the absolute file the export would write, or refuse it.
///
/// The refusal is the point. `writePage` used to trust the route as a path
/// fragment, so a route of `/../secrets` resolved OUTSIDE `outDir` and the
/// export wrote a page over whatever was there — and a route is DISCOVERED from
/// a filename and its frontmatter, which is the trust boundary. Every route this
/// module was designed for resolves unchanged; what is refused is a route that
/// leaves the directory the caller named.
///
/// Pure: `Path.GetFullPath` normalises `..` segments textually, so this is a
/// decision about two strings and is testable without a filesystem. The
/// containment test compares the normalised target against the normalised
/// directory with a trailing separator, so a SIBLING directory sharing a prefix
/// (`out-2/` beside `out/`) is refused rather than admitted by a bare
/// `StartsWith`.
let tryResolveTarget (outDir: string) (route: string) : Result<string, string> =
    let root = Path.GetFullPath outDir

    let rooted =
        root
        + (if root.EndsWith(string Path.DirectorySeparatorChar) then
               ""
           else
               string Path.DirectorySeparatorChar)

    let target =
        if route = "/" then
            Path.Combine(root, "index.html")
        else
            Path.Combine(root, route.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "index.html")

    // `Path.Combine` DISCARDS the first argument when the second is rooted, so a
    // route of `/C:/x` or `//host/share` escapes without a single `..` in it. The
    // full-path comparison below catches that too, and catches it for the same
    // reason: the answer is not under the root.
    let full = Path.GetFullPath target

    if full.StartsWith(rooted, StringComparison.Ordinal) then
        Ok full
    else
        Error(
            sprintf
                "Route '%s' resolves to '%s', which is outside the export directory '%s'. A route is a site path, not a filesystem path: it may not contain '..' segments, a drive or UNC root, or anything else that leaves the directory being exported."
                route
                full
                root
        )

/// Write one rendered page under `outDir` at its `relativePathOf` location.
///
/// REFUSES a route that resolves outside `outDir` (see `tryResolveTarget`),
/// raising rather than returning: this function's contract is "the page is
/// written", a partially-exported site is not a site, and a silent skip would
/// leave `writeAll`'s count claiming a page that is not there.
let writePage (outDir: string) (route: string) (html: string) : unit =
    match tryResolveTarget outDir route with
    | Error message -> invalidArg (nameof route) message
    | Ok target ->
        match Path.GetDirectoryName target with
        | null
        | "" -> ()
        | dir -> Directory.CreateDirectory dir |> ignore

        File.WriteAllText(target, html)

/// Recursively copy every file under `publicRoot` into `outDir`, preserving
/// relative paths. Returns the number of files copied.
let copyPublicAssets (publicRoot: string) (outDir: string) : int =
    let mutable copied = 0

    for file in Directory.EnumerateFiles(publicRoot, "*", SearchOption.AllDirectories) do
        let rel = Path.GetRelativePath(publicRoot, file)
        let target = Path.Combine(outDir, rel)

        match Path.GetDirectoryName target with
        | null
        | "" -> ()
        | dir -> Directory.CreateDirectory dir |> ignore

        File.Copy(file, target, overwrite = true)
        copied <- copied + 1

    copied

/// Export a whole plan: every rendered page, sitemap.xml, robots.txt, and (when
/// `publicRoot` is given and exists) the recursive public-asset copy. Returns
/// the number of pages written.
let writeAll (baseUrl: string) (publicRoot: string option) (outDir: string) (plan: RenderPlan) : int =
    Directory.CreateDirectory outDir |> ignore

    for page, html in plan.Pages do
        writePage outDir page.Route html

    let pages = plan.Pages |> List.map fst
    File.WriteAllText(Path.Combine(outDir, "sitemap.xml"), sitemapXml baseUrl pages)
    File.WriteAllText(Path.Combine(outDir, "robots.txt"), robotsTxt baseUrl)

    match publicRoot with
    | Some root when Directory.Exists root -> copyPublicAssets root outDir |> ignore
    | Some _
    | None -> ()

    List.length plan.Pages
