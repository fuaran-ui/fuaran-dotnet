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

open System.IO
open System.Text

/// Route → export-relative file path, forward slashes: "/" → "index.html",
/// "/x" → "x/index.html", "/guide/wire" → "guide/wire/index.html".
let relativePathOf (route: string) : string =
    if route = "/" then
        "index.html"
    else
        route.TrimStart('/') + "/index.html"

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
        sb.Append(sprintf "  <url><loc>%s%s</loc></url>\n" origin path) |> ignore

    sb.Append("</urlset>\n") |> ignore
    sb.ToString()

/// robots.txt content: allow everything, point at the sitemap.
let robotsTxt (baseUrl: string) : string =
    let origin = baseUrl.TrimEnd('/')
    sprintf "User-agent: *\nAllow: /\n\nSitemap: %s/sitemap.xml\n" origin

// ─── File I/O surface (everything above is pure) ─────────────────────────────

/// Write one rendered page under `outDir` at its `relativePathOf` location.
let writePage (outDir: string) (route: string) (html: string) : unit =
    let target =
        if route = "/" then
            Path.Combine(outDir, "index.html")
        else
            let dir =
                Path.Combine(outDir, route.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))

            Directory.CreateDirectory dir |> ignore
            Path.Combine(dir, "index.html")

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
