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
