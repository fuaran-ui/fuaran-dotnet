module Fuaran.UI.Site.Tests.NavTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Site

// Snapshot-style structural assertions over the nav projection: deterministic
// output, stable ordering under equal nav-order, current-page marking per
// route, and the container's semantic shape.

let private page route title (extraFm: (string * string) list) : SitePage =
    { Route = route
      SourcePath = "mem:" + route
      Title = title
      Description = None
      Layout = "page"
      Frontmatter = Map.ofList extraFm
      Body = "body" }

let private pages =
    [ page "/" "Home" [ "nav-order", "0" ]
      page "/pricing" "Pricing" [ "nav-order", "20" ]
      page "/about" "About" [ "nav-order", "10"; "nav-title", "Who we are" ]
      page "/hidden" "Hidden" [] ]

/// Flatten the projected nav to (id, href, label, isCurrent) rows.
let private rows (node: Node<obj>) : (string * string * string * bool) list =
    match node.Kind with
    | NodeKind.Box spec ->
        spec.Children
        |> List.map (fun child ->
            match child.Kind with
            | NodeKind.Link link ->
                let href =
                    match link.Href with
                    | Binding.Static(Some h) -> h
                    | _ -> failtest "nav link href is not a static binding"

                let label =
                    match link.Label with
                    | TextSource.Literal t -> t
                    | _ -> failtest "nav link label is not a literal"

                let isCurrent =
                    match child.ExtraAttributes with
                    | Some attrs -> Map.tryFind "aria-current" attrs = Some "page"
                    | None -> false

                child.Id, href, label, isCurrent
            | _ -> failtest "nav child is not a Link node")
    | _ -> failtest "nav root is not a Box node"

[<Tests>]
let entryTests =
    testList
        "Nav.entryOf / Nav.entries"
        [ test "no nav-order means not in the nav" {
              Expect.equal (Nav.entryOf (page "/x" "X" [])) (Ok None) "absent key"
          }

          test "nav-title defaults to the page title" {
              match Nav.entryOf (page "/x" "X" [ "nav-order", "5" ]) with
              | Ok(Some e) -> Expect.equal e.Title "X" "title fallback"
              | other -> failtest (sprintf "unexpected: %A" other)
          }

          test "nav-title overrides the page title" {
              match Nav.entryOf (page "/x" "X" [ "nav-order", "5"; "nav-title", "Y" ]) with
              | Ok(Some e) -> Expect.equal e.Title "Y" "override"
              | other -> failtest (sprintf "unexpected: %A" other)
          }

          test "a non-integer nav-order is an Error" {
              match Nav.entryOf (page "/x" "X" [ "nav-order", "first" ]) with
              | Error detail -> Expect.stringContains detail "nav-order" "names the key"
              | other -> failtest (sprintf "unexpected: %A" other)
          }

          test "entries order by nav-order" {
              let routes = Nav.entries pages |> List.map (fun e -> e.Route)
              Expect.equal routes [ "/"; "/about"; "/pricing" ] "ordered, hidden page absent"
          }

          test "equal nav-order ties break by route — stable and deterministic" {
              let tied =
                  [ page "/zeta" "Z" [ "nav-order", "10" ]
                    page "/alpha" "A" [ "nav-order", "10" ] ]

              let routes = Nav.entries tied |> List.map (fun e -> e.Route)
              Expect.equal routes [ "/alpha"; "/zeta" ] "route tie-break"
          } ]

[<Tests>]
let projectTests =
    testList
        "Nav.project"
        [ test "projects ordered crawlable links with deterministic ids" {
              let nav: Node<obj> = Nav.project pages "/pricing"

              Expect.equal
                  (rows nav)
                  [ "site-nav-index", "/", "Home", false
                    "site-nav-about", "/about", "Who we are", false
                    "site-nav-pricing", "/pricing", "Pricing", true ]
                  "the full projection snapshot"
          }

          test "the container is a Group box with the navigation ARIA role" {
              let nav: Node<obj> = Nav.project pages "/"
              Expect.equal nav.Id "site-nav" "container id"

              match nav.Kind with
              | NodeKind.Box spec -> Expect.equal spec.Role BoxRole.Group "group role"
              | _ -> failtest "nav root is not a Box node"

              match nav.Accessibility with
              | Some a11y -> Expect.equal a11y.Role (Some AriaRole.Navigation) "navigation role"
              | None -> failtest "nav carries no accessibility record"
          }

          test "current-page marking follows the route" {
              for route in [ "/"; "/about"; "/pricing" ] do
                  let nav: Node<obj> = Nav.project pages route
                  let current = rows nav |> List.filter (fun (_, href, _, _) -> href = route)

                  Expect.equal
                      (current |> List.map (fun (_, _, _, c) -> c))
                      [ true ]
                      (sprintf "exactly the %s link is current" route)

                  let others = rows nav |> List.filter (fun (_, href, _, _) -> href <> route)
                  Expect.all others (fun (_, _, _, c) -> not c) "no other link is current"
          }

          test "a route outside the nav marks nothing current" {
              let nav: Node<obj> = Nav.project pages "/hidden"
              Expect.all (rows nav) (fun (_, _, _, c) -> not c) "no current marker"
          }

          test "projection is deterministic — two runs are structurally equal" {
              // `Node` carries function-typed slots, so the type itself is not
              // equatable; compare a total rendering of the projection instead.
              let a: Node<obj> = Nav.project pages "/about"
              let b: Node<obj> = Nav.project pages "/about"
              Expect.equal (rows a) (rows b) "identical link rows"
              Expect.equal (sprintf "%A" a) (sprintf "%A" b) "identical printed structure"
          }

          test "an empty page set projects an empty nav container" {
              let nav: Node<obj> = Nav.project [] "/"
              Expect.isEmpty (rows nav) "no links"
          } ]
