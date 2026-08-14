module Fuaran.UI.Site.Tests.RenderPlanTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Site

// RenderPlan.compute through fake seams — the plan layer is renderer-neutral,
// so a sprintf-shaped body renderer and shell exercise it completely.

let private page route layout title (extraFm: (string * string) list) : SitePage =
    { Route = route
      SourcePath = "mem:" + route
      Title = title
      Description = None
      Layout = layout
      Frontmatter = Map.ofList extraFm
      Body = "body of " + route }

/// A minimal deterministic "renderer": literal text of the tree's markdown
/// node (the fake layouts emit one), so the output is assertable.
let private fakeRenderBody (_: SitePage) (node: Node<obj>) : string =
    match node.Kind with
    | NodeKind.Markdown spec ->
        match spec.Text with
        | TextSource.Literal t -> "<body>" + t + "</body>"
        | _ -> "<body>?</body>"
    | _ -> "<tree>" + node.Id + "</tree>"

/// The nav rendered as a pipe-list of hrefs, current one starred — proving the
/// shell seam receives a per-page nav with zero per-page wiring.
let private navSummary (nav: Node<obj>) : string =
    match nav.Kind with
    | NodeKind.Box spec ->
        spec.Children
        |> List.map (fun child ->
            match child.Kind with
            | NodeKind.Link link ->
                let href =
                    match link.Href with
                    | Binding.Static(Some h) -> h
                    | _ -> "?"

                let current =
                    match child.ExtraAttributes with
                    | Some attrs when Map.tryFind "aria-current" attrs = Some "page" -> "*"
                    | _ -> ""

                href + current
            | _ -> "?")
        |> String.concat "|"
    | _ -> "?"

let private seams: SiteSeams<obj> =
    { Layouts =
        Map.ofList
            [ "page", (fun (p: SitePage) -> Fuaran.markdown ("md-" + p.Title) p.Body)
              "landing", (fun (p: SitePage) -> Fuaran.markdown ("landing-" + p.Title) p.Body) ]
      RenderBody = fakeRenderBody
      Shell =
        fun ctx body -> sprintf "<html><title>%s</title><nav>%s</nav>%s</html>" ctx.Page.Title (navSummary ctx.Nav) body }

let private pages =
    [ page "/" "page" "Home" [ "nav-order", "0" ]
      page "/about" "landing" "About" [ "nav-order", "10" ]
      page "/legal" "page" "Legal" [] ]

[<Tests>]
let tests =
    testList
        "RenderPlan"
        [ test "computes one rendered document per page, in page-set order" {
              match RenderPlan.compute seams pages with
              | Error issues -> failtest (SiteCheck.describe issues)
              | Ok plan ->
                  Expect.equal (plan.Pages |> List.map (fst >> _.Route)) [ "/"; "/about"; "/legal" ] "order kept"

                  Expect.equal
                      (RenderPlan.tryFind "/about" plan)
                      (Some "<html><title>About</title><nav>/|/about*</nav><body>body of /about</body></html>")
                      "layout dispatched, body rendered, shell wrapped, nav current-marked"
          }

          test "every page's shell receives the nav with THAT page marked current" {
              match RenderPlan.compute seams pages with
              | Error issues -> failtest (SiteCheck.describe issues)
              | Ok plan ->
                  match RenderPlan.tryFind "/" plan with
                  | Some html -> Expect.stringContains html "<nav>/*|/about</nav>" "home marked on /"
                  | None -> failtest "no document for /"

                  match RenderPlan.tryFind "/legal" plan with
                  | Some html -> Expect.stringContains html "<nav>/|/about</nav>" "nothing marked on a non-nav page"
                  | None -> failtest "no document for /legal"
          }

          test "an unknown layout refuses the plan with the gate's findings" {
              let bad = pages @ [ page "/x" "prose" "X" [] ]

              match RenderPlan.compute seams bad with
              | Ok _ -> failtest "an unknown layout must refuse the plan"
              | Error issues ->
                  Expect.isTrue (SiteCheck.hasErrors issues) "error severity"
                  Expect.exists issues (fun i -> i.Detail.Contains "unknown layout 'prose'") "names the layout"
          }

          test "warnings ride the plan without refusing it" {
              let noisy =
                  [ page "/" "page" "Home" [ "nav-order", "0" ]
                    page "/a" "page" "A" [ "nav-order", "0" ] ]

              match RenderPlan.compute seams noisy with
              | Error issues -> failtest (SiteCheck.describe issues)
              | Ok plan ->
                  Expect.hasLength plan.Warnings 1 "the duplicate nav-order warning"
                  Expect.equal plan.Warnings.Head.Severity SiteSeverity.Warning "warning severity"
          }

          test "the plan is deterministic — two computations are equal" {
              let a = RenderPlan.compute seams pages
              let b = RenderPlan.compute seams pages
              Expect.equal a b "identical plans"
          }

          test "knownLayoutsOf is the dispatch key set" {
              Expect.equal (RenderPlan.knownLayoutsOf seams) (set [ "landing"; "page" ]) "keys"
          } ]
