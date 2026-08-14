module Fuaran.UI.Site.Tests.SiteCheckTests

open Expecto
open Fuaran.UI.Site

let private page route layout title (extraFm: (string * string) list) : SitePage =
    { Route = route
      SourcePath = "mem:" + route
      Title = title
      Description = None
      Layout = layout
      Frontmatter = Map.ofList extraFm
      Body = "body" }

let private layouts = set [ "page"; "landing" ]

[<Tests>]
let tests =
    testList
        "SiteCheck"
        [ test "a clean set reports nothing" {
              let pages = [ page "/" "page" "Home" []; page "/a" "landing" "A" [] ]
              Expect.isEmpty (SiteCheck.run layouts pages) "no findings"
          }

          test "duplicate routes are an error" {
              let pages = [ page "/a" "page" "A" []; page "/a" "page" "A again" [] ]
              let issues = SiteCheck.run layouts pages
              Expect.isTrue (SiteCheck.hasErrors issues) "errors present"
              Expect.exists issues (fun i -> i.Where = "/a" && i.Severity = SiteSeverity.Error) "names the route"
          }

          test "an unknown layout is an error, never a silent fall-through" {
              let pages = [ page "/a" "prose" "A" [] ]
              let issues = SiteCheck.run layouts pages
              Expect.isTrue (SiteCheck.hasErrors issues) "errors present"
              Expect.exists issues (fun i -> i.Detail.Contains "unknown layout 'prose'") "names the layout"
          }

          test "an empty title is an error" {
              let pages = [ page "/a" "page" "  " [] ]
              Expect.isTrue (SiteCheck.run layouts pages |> SiteCheck.hasErrors) "errors present"
          }

          test "errors filters to error severity only" {
              let pages = [ page "/a" "prose" "A" [] ]
              let issues = SiteCheck.run layouts pages
              Expect.isNonEmpty (SiteCheck.errors issues) "has errors"

              Expect.all
                  (SiteCheck.errors issues)
                  (fun i -> i.Severity = SiteSeverity.Error)
                  "only errors after the filter"
          }

          test "describe emits one line per finding with a severity tag" {
              let pages = [ page "/a" "prose" "A" [] ]
              let report = SiteCheck.run layouts pages |> SiteCheck.describe
              Expect.stringStarts report "error: " "severity tag leads the line"
          } ]
