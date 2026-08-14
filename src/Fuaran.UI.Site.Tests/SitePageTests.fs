module Fuaran.UI.Site.Tests.SitePageTests

open Expecto
open Fuaran.UI.Site

// Frontmatter-parser + route-derivation coverage, including the edge cases the
// donor sites proved in in-code self-tests.

[<Tests>]
let frontmatterTests =
    testList
        "SitePage.splitFrontmatter"
        [ test "parses pairs, unquotes values, skips comments, strips the block" {
              let fm, body =
                  SitePage.splitFrontmatter
                      "---\ntitle: Hello\ndescription: \"a, b\"\nlayout: doc\n# note\n---\n\n# H1\nprose"

              Expect.contains fm ("title", "Hello") "title pair"
              Expect.contains fm ("description", "a, b") "quoted value unquoted"
              Expect.contains fm ("layout", "doc") "layout pair"
              Expect.isFalse (fm |> List.exists (fun (k, _) -> k.StartsWith "#")) "comment line skipped"
              Expect.equal body "# H1\nprose" "body stripped of the frontmatter block"
          }

          test "no frontmatter: empty pairs, whole text as body" {
              let fm, body = SitePage.splitFrontmatter "no frontmatter here"
              Expect.isEmpty fm "no pairs"
              Expect.equal body "no frontmatter here" "whole text is the body"
          }

          test "unterminated block is treated as no frontmatter" {
              let text = "---\ntitle: Dangling\nno close fence"
              let fm, body = SitePage.splitFrontmatter text
              Expect.isEmpty fm "no pairs"
              Expect.equal body text "whole text is the body"
          }

          test "CRLF input normalises" {
              let fm, body = SitePage.splitFrontmatter "---\r\ntitle: X\r\n---\r\nbody"
              Expect.contains fm ("title", "X") "pair parsed across CRLF"
              Expect.equal body "body" "body stripped"
          }

          test "a line without a colon is ignored" {
              let fm, _ = SitePage.splitFrontmatter "---\nnot a pair\ntitle: Y\n---\nbody"
              Expect.equal fm [ "title", "Y" ] "only the well-formed pair survives"
          } ]

[<Tests>]
let routeTests =
    testList
        "Routes"
        [ test "index.md maps to /" { Expect.equal (Routes.ofRelativePath "index.md") "/" "root route" }

          test "top-level page maps to /name" {
              Expect.equal (Routes.ofRelativePath "components.md") "/components" "top route"
          }

          test "nested page maps to /dir/name" {
              Expect.equal (Routes.ofRelativePath "guide/wire-format.md") "/guide/wire-format" "nested route"
          }

          test "nested index maps to /dir" {
              Expect.equal (Routes.ofRelativePath "guide/index.md") "/guide" "nested index route"
          }

          test "backslash separators normalise" {
              Expect.equal (Routes.ofRelativePath "guide\\wire.md") "/guide/wire" "windows-style path"
          }

          test "mdRouteOf twins each route" {
              Expect.equal (Routes.mdRouteOf "/") "/index.md" "root agent route"
              Expect.equal (Routes.mdRouteOf "/pricing") "/pricing.md" "page agent route"
          } ]

[<Tests>]
let ofTextTests =
    testList
        "SitePage.ofText"
        [ test "fields populate from frontmatter" {
              let page =
                  SitePage.ofText
                      "mem:pricing.md"
                      "/pricing"
                      "---\ntitle: Pricing\ndescription: Plans\nlayout: landing\nnav-order: 20\n---\nbody"

              Expect.equal page.Route "/pricing" "route"
              Expect.equal page.Title "Pricing" "title"
              Expect.equal page.Description (Some "Plans") "description"
              Expect.equal page.Layout "landing" "layout"
              Expect.equal (Map.tryFind "nav-order" page.Frontmatter) (Some "20") "frontmatter map keeps every key"
              Expect.equal page.Body "body" "body"
          }

          test "defaults: title from route, layout 'page', no description" {
              let page = SitePage.ofText "mem:wire.md" "/guide/wire" "body only"
              Expect.equal page.Title "wire" "title falls back to the route's last segment"
              Expect.equal page.Layout SitePage.DefaultLayout "default layout"
              Expect.equal page.Description None "no description"
          }

          test "root title default is Home" {
              let page = SitePage.ofText "mem:index.md" "/" "body"
              Expect.equal page.Title "Home" "root fallback title"
          } ]

[<Tests>]
let agentMarkdownTests =
    testList
        "SitePage.agentMarkdown"
        [ test "prepends the title as an H1 when the body has none" {
              let page = SitePage.ofText "mem:a.md" "/a" "---\ntitle: About\n---\nprose"
              Expect.equal (SitePage.agentMarkdown page) "# About\n\nprose" "H1 prepended"
          }

          test "leaves a body that already opens with an H1 alone" {
              let page = SitePage.ofText "mem:a.md" "/a" "---\ntitle: About\n---\n# About\nprose"
              Expect.equal (SitePage.agentMarkdown page) "# About\nprose" "body unchanged"
          } ]
