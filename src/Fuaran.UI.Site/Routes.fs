module Fuaran.UI.Site.Routes

// ─── Filename → route derivation ─────────────────────────────────────────────
//
// The canonical mapping from a markdown source file to its route, shared by
// every consumer of the page-set layer so no host re-derives it by hand:
//
//   index.md          → /
//   x.md              → /x
//   x/index.md        → /x
//   guide/wire.md     → /guide/wire
//
// Routes carry NO trailing slash. The `.md` agent-route twin (`/x` → `/x.md`,
// `/` → `/index.md`) lives here too, so an SSR host and a static export agree
// on the raw-markdown surface by construction.

open System
open System.IO

/// Map a pages-root-relative path (forward or backward slashes, `.md`
/// extension) to its canonical route — no trailing slash.
let ofRelativePath (relPath: string) : string =
    let rel = relPath.Replace('\\', '/')

    let noExt =
        if rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) then
            rel.Substring(0, rel.Length - ".md".Length)
        else
            rel

    if noExt = "index" then
        "/"
    elif noExt.EndsWith("/index", StringComparison.Ordinal) then
        "/" + noExt.Substring(0, noExt.Length - "/index".Length)
    else
        "/" + noExt

/// Map an absolute page file under `pagesRoot` to its canonical route.
let ofFile (pagesRoot: string) (file: string) : string =
    ofRelativePath (Path.GetRelativePath(pagesRoot, file))

/// The raw-markdown agent route for a page route: `/x` → `/x.md`, `/` →
/// `/index.md` — so an agent can retrieve the page source without HTML.
let mdRouteOf (route: string) : string =
    if route = "/" then "/index.md" else route + ".md"
