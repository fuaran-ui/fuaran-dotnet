module Fuaran.UI.Site.Tests.ExportTests

open System.IO
open Expecto
open Fuaran.UI.Site

let private page route : SitePage =
    { Route = route
      SourcePath = "mem:" + route
      Title = "T"
      Description = None
      Layout = "page"
      Frontmatter = Map.empty
      Body = "body" }

[<Tests>]
let planningTests =
    testList
        "Export planning (pure)"
        [ test "route → relative file path mapping" {
              Expect.equal (Export.relativePathOf "/") "index.html" "root"
              Expect.equal (Export.relativePathOf "/x") "x/index.html" "top-level"
              Expect.equal (Export.relativePathOf "/guide/wire") "guide/wire/index.html" "nested"
          }

          test "sitemap.xml lists every page against the origin" {
              let xml = Export.sitemapXml "https://example.org/" [ page "/"; page "/pricing" ]

              Expect.stringStarts xml "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" "xml declaration"
              Expect.stringContains xml "<loc>https://example.org</loc>" "root loc, trailing slash trimmed"
              Expect.stringContains xml "<loc>https://example.org/pricing</loc>" "page loc"
          }

          test "robots.txt allows all and points at the sitemap" {
              let txt = Export.robotsTxt "https://example.org"
              Expect.stringContains txt "User-agent: *\nAllow: /" "allow all"
              Expect.stringContains txt "Sitemap: https://example.org/sitemap.xml" "sitemap pointer"
          }

          // ── Phase 1532 ──────────────────────────────────────────────────────
          test "sitemap.xml XML-escapes the route" {
              // A route is DISCOVERED from a filename and its frontmatter, so the
              // export does not control these characters. `&` is the commonest and
              // is a legal path character; unescaped it produced a document no
              // conforming sitemap reader accepts.
              let xml =
                  Export.sitemapXml "https://example.org" [ page "/r&d"; page "/a<b>c"; page "/q\"'" ]

              Expect.stringContains xml "<loc>https://example.org/r&amp;d</loc>" "ampersand escaped"
              Expect.stringContains xml "<loc>https://example.org/a&lt;b&gt;c</loc>" "angle brackets escaped"
              Expect.stringContains xml "<loc>https://example.org/q&quot;&apos;</loc>" "quotes escaped"

              // The escaping is real, not a coincidence of the assertions above:
              // no raw metacharacter survives inside a `<loc>`.
              Expect.isFalse (xml.Contains "/r&d") "no raw ampersand"
              Expect.isFalse (xml.Contains "/a<b>c") "no raw angle brackets"
          }

          test "sitemap.xml is parseable XML for a route full of metacharacters" {
              // The property the escaping exists for, asserted by a parser rather
              // than by string matching — a `Replace` chain in the wrong order
              // (escaping `&` last, so `&lt;` becomes `&amp;lt;`) passes a
              // Contains check on one entity and still produces a wrong document.
              let xml = Export.sitemapXml "https://example.org" [ page "/a&b<c>\"d'e" ]
              let doc = System.Xml.Linq.XDocument.Parse xml

              let locs =
                  doc.Descendants(System.Xml.Linq.XName.Get("loc", "http://www.sitemaps.org/schemas/sitemap/0.9"))
                  |> Seq.map _.Value
                  |> List.ofSeq

              Expect.equal locs [ "https://example.org/a&b<c>\"d'e" ] "the parsed text is the original route"
          } ]

[<Tests>]
let containmentTests =
    // Phase 1532 — `writePage` trusted the route as a path fragment, so a route
    // carrying `..` resolved outside the export directory and the write landed
    // there. Pure: the decision is about two strings.
    testList
        "Export.tryResolveTarget (route containment)"
        [ test "an ordinary route resolves inside the export directory" {
              let outDir = Path.Combine(Path.GetTempPath(), "fuaran-site-contain")

              for route in [ "/"; "/x"; "/guide/wire"; "/a.b/c-d" ] do
                  match Export.tryResolveTarget outDir route with
                  | Ok target ->
                      Expect.stringStarts target (Path.GetFullPath outDir) (route + " stays under outDir")
                      Expect.stringEnds target "index.html" (route + " writes an index.html")
                  | Error m -> failtestf "route '%s' was refused: %s" route m
          }

          test "a route that climbs out of the export directory is REFUSED" {
              let outDir = Path.Combine(Path.GetTempPath(), "fuaran-site-contain")

              for route in [ "/../secrets"; "/a/../../secrets"; "/../"; "/x/../../../etc" ] do
                  match Export.tryResolveTarget outDir route with
                  | Error m -> Expect.stringContains m "outside the export directory" (route + " names the reason")
                  | Ok target -> failtestf "route '%s' resolved to '%s' instead of being refused" route target
          }

          test "a route that names an absolute root is REFUSED" {
              // `Path.Combine` DISCARDS its first argument when the second is
              // rooted, so this escapes with no `..` in it at all — a shape a
              // `..`-scanning check would miss entirely, and the reason the
              // containment test compares resolved paths rather than scanning the
              // route for segments.
              //
              // Note what is NOT here: a leading `//host/share` is trimmed to a
              // relative `host/share` and stays inside, which is correct — the
              // route's leading slashes are a site-path convention, not a UNC one.
              let outDir = Path.Combine(Path.GetTempPath(), "fuaran-site-contain")

              let route =
                  if Path.DirectorySeparatorChar = '\\' then
                      "/C:/windows/system32"
                  else
                      "/./../etc"

              match Export.tryResolveTarget outDir route with
              | Error _ -> ()
              | Ok target -> failtestf "rooted route '%s' resolved to '%s'" route target
          }

          test "a sibling directory sharing the prefix is REFUSED" {
              // The bare-`StartsWith` bug: `fuaran-site-out-2` starts with
              // `fuaran-site-out`, so a containment test without the trailing
              // separator admits it.
              let root = Path.Combine(Path.GetTempPath(), "fuaran-site-out")

              match Export.tryResolveTarget root "/../fuaran-site-out-2/page" with
              | Error _ -> ()
              | Ok target -> failtestf "a prefix-sharing sibling resolved to '%s'" target
          }

          test "writePage raises rather than writing outside outDir" {
              let root =
                  Path.Combine(Path.GetTempPath(), "fuaran-site-escape-" + string (System.Guid.NewGuid()))

              let outDir = Path.Combine(root, "out")

              try
                  Directory.CreateDirectory outDir |> ignore

                  Expect.throwsT<System.ArgumentException>
                      (fun () -> Export.writePage outDir "/../escaped" "<html>bad</html>")
                      "an escaping route is refused, not written"

                  Expect.isFalse
                      (File.Exists(Path.Combine(root, "escaped", "index.html")))
                      "no file was written outside the export directory"

                  // The negative control: an ordinary route through the same
                  // function still writes, so the refusal is not blanket.
                  Export.writePage outDir "/ok" "<html>good</html>"

                  Expect.equal
                      (File.ReadAllText(Path.Combine(outDir, "ok", "index.html")))
                      "<html>good</html>"
                      "an ordinary route still writes"
              finally
                  if Directory.Exists root then
                      Directory.Delete(root, true)
          } ]

[<Tests>]
let writeTests =
    testList
        "Export.writeAll (file I/O)"
        [ test "writes pages, sitemap, robots, and copies public assets" {
              let root =
                  Path.Combine(Path.GetTempPath(), "fuaran-site-export-" + string (System.Guid.NewGuid()))

              let publicRoot = Path.Combine(root, "public")
              let outDir = Path.Combine(root, "out")

              try
                  Directory.CreateDirectory(Path.Combine(publicRoot, "css")) |> ignore
                  File.WriteAllText(Path.Combine(publicRoot, "css", "site.css"), "body{}")

                  let plan: RenderPlan =
                      { Pages = [ page "/", "<html>home</html>"; page "/pricing", "<html>pricing</html>" ]
                        Warnings = [] }

                  let count = Export.writeAll "https://example.org" (Some publicRoot) outDir plan

                  Expect.equal count 2 "two pages written"
                  Expect.equal (File.ReadAllText(Path.Combine(outDir, "index.html"))) "<html>home</html>" "root page"

                  Expect.equal
                      (File.ReadAllText(Path.Combine(outDir, "pricing", "index.html")))
                      "<html>pricing</html>"
                      "nested page"

                  Expect.isTrue (File.Exists(Path.Combine(outDir, "sitemap.xml"))) "sitemap written"
                  Expect.isTrue (File.Exists(Path.Combine(outDir, "robots.txt"))) "robots written"

                  Expect.equal
                      (File.ReadAllText(Path.Combine(outDir, "css", "site.css")))
                      "body{}"
                      "public asset copied recursively"
              finally
                  if Directory.Exists root then
                      Directory.Delete(root, true)
          }

          test "export is byte-deterministic for the same plan" {
              let root =
                  Path.Combine(Path.GetTempPath(), "fuaran-site-export-" + string (System.Guid.NewGuid()))

              try
                  let plan: RenderPlan =
                      { Pages = [ page "/", "<html>home</html>" ]
                        Warnings = [] }

                  let outA = Path.Combine(root, "a")
                  let outB = Path.Combine(root, "b")
                  Export.writeAll "https://example.org" None outA plan |> ignore
                  Export.writeAll "https://example.org" None outB plan |> ignore

                  for rel in [ "index.html"; "sitemap.xml"; "robots.txt" ] do
                      Expect.equal
                          (File.ReadAllBytes(Path.Combine(outA, rel)))
                          (File.ReadAllBytes(Path.Combine(outB, rel)))
                          (rel + " identical across exports")
              finally
                  if Directory.Exists root then
                      Directory.Delete(root, true)
          } ]
