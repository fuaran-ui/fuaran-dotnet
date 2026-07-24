module Fuaran.UI.Renderer.Server.Tests.ScalarSsrParityTests

// ============================================================================
//  SSR scalar-resolution parity (Phases 629/632 on the server path).
//
//  The client renderer routes every SCALAR slot — `TextSource.Bound`, the
//  Metric value/trend, the LabelValueRow value — through the shared
//  `BindingResolver.resolveScalar*` path, so a `Binding.Transform` yields its
//  1×1 result cell (Phase 632) and a `Binding.Selection` yields its declared
//  `defaultValue` until the first real selection (Phase 629). The server
//  renderer dispatches through the SAME shared functions; these tests make
//  that an executable contract over the wire-format corpus fixtures that
//  exercise it.
//
//  Parity shape (the TS tier's corpus locks are the model): the F# Feliz
//  client renderer cannot render to an HTML string on .NET, so each assertion
//  computes the CLIENT's resolved value by calling the exact shared-Core
//  function the client's arm dispatches through
//  (`tryResolveScalarText` — Render.fs `renderText`'s Bound arm), pins it to
//  the canonical corpus value, and then asserts the SERVER HTML carries that
//  same value as element text. A divergence on either side fails loudly.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// Walk up from the test assembly until the workspace `wire-format-fixtures/`
/// corpus is found (a sibling of the `fuaran/` repo).
let private corpusRoot () : string =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            failwith "wire-format-fixtures/ not found walking up — the Fuaran workspace checkout is required."
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Path.Combine(dir.FullName, "wire-format-fixtures")
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

let private root = corpusRoot ()

let private decodeFixture (name: string) : Node<obj> =
    let json = File.ReadAllText(Path.Combine(root, "nodes", name + ".json"))

    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "decode failed for %s: %A" name e

/// Find a node by id. The corpus fixtures under test are Box-rooted with
/// Box-nested children, so the Box arm is the only recursion these walks need.
let rec private tryFindNode (id: string) (node: Node<obj>) : Node<obj> option =
    if node.Id = NodeId id then
        Some node
    else
        let children =
            match node.Kind with
            | NodeKind.Layout(LayoutKind.Box s) -> s.Children
            | _ -> []

        children |> List.tryPick (tryFindNode id)

let private findNode (id: string) (tree: Node<obj>) : Node<obj> =
    match tryFindNode id tree with
    | Some n -> n
    | None -> failwithf "node '%s' not found in fixture" id

/// The client renderer's `renderText` Bound-arm dispatch, verbatim (Phase 632):
/// this IS what the CSR side puts in the DOM for a bound text slot.
let private clientTextOf (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound binding ->
        BindingResolver.tryResolveScalarText BindingResolver.empty binding
        |> Option.defaultValue ""
    | TextSource.I18n(key, _) -> failwithf "unexpected I18n text slot in fixture: %s" key

[<Tests>]
let scalarSsrParityTests =
    testList
        "SSR scalar-resolution parity (629/632)"
        [ test "scalar-transform-composition — Badge count + param-defaulted Callout body match CSR" {
              let tree = decodeFixture "scalar-transform-composition"
              let html = Render.render BindingResolver.empty tree

              // Badge label: a Transform ending in a global single-count agg —
              // the 1×1 scalar law resolves the lone cell (Phase 632).
              let badgeText =
                  match (findNode "critical-count-badge" tree).Kind with
                  | NodeKind.Display(DisplayKind.Badge spec) -> clientTextOf spec.Label
                  | k -> failwithf "unexpected kind for critical-count-badge: %A" k

              Expect.equal badgeText "2" "the client-dispatch scalar resolution of the Badge count"

              Expect.isTrue
                  (contains (sprintf "fuaran-badge-critical\">%s<" badgeText) html)
                  "SSR Badge text equals CSR"

              // Callout body: a Transform whose param defaults through
              // `Selection.defaultValue` (Phase 629) then projects one row's
              // alert column (project + limit 1 — the row-field-lookup terminal).
              let calloutText =
                  match (findNode "sla-warning" tree).Kind with
                  | NodeKind.Display(DisplayKind.Callout spec) -> clientTextOf spec.Body
                  | k -> failwithf "unexpected kind for sla-warning: %A" k

              Expect.equal
                  calloutText
                  "TCK-2041 breaches SLA in 2 hours"
                  "the client-dispatch scalar resolution of the Callout body"

              Expect.isTrue
                  (contains (sprintf "fuaran-callout-body\">%s<" calloutText) html)
                  "SSR Callout body equals CSR"
          }

          test "master-detail-preselected — the detail Fact shows the Selection defaultValue on SSR" {
              let tree = decodeFixture "master-detail-preselected"
              let html = Render.render BindingResolver.empty tree

              // The detail Fact binds `Selection` with `defaultValue` + `field`
              // (Phase 629): with no selection written server-side, the shared
              // resolver yields the declared default — no store seeding exists
              // on either host; resolution-time defaulting IS the mechanism.
              let factText =
                  match (findNode "detail-ticket" tree).Kind with
                  | NodeKind.Display(DisplayKind.Fact spec) -> clientTextOf spec.Value
                  | k -> failwithf "unexpected kind for detail-ticket: %A" k

              Expect.equal factText "TCK-2041" "the client-dispatch resolution of the preselected detail"
              Expect.isTrue (contains (sprintf ">%s<" factText) html) "SSR Fact value equals CSR"
          }

          test "filterable-static-dashboard — filter-driven composition SSRs stably under the scalar dispatch" {
              // No scalar slot resolves here (the Transform params come from
              // Filters with no defaults), so the lock is stability: the new
              // dispatch must not change what this composition renders. Unset
              // filter params PRUNE their dependent pipeline stages (Phase 424)
              // — both hosts show the unfiltered frame — so the Line chart
              // lowers to the inline Drawing SVG over all rows and the grid
              // placeholder reports the full row count.
              let tree = decodeFixture "filterable-static-dashboard"
              let html = Render.render BindingResolver.empty tree

              Expect.isTrue (contains "fuaran-filters" html) "the Filters block renders"

              for optionLabel in [ "EMEA"; "Americas"; "Drama"; "Documentary" ] do
                  Expect.isTrue (contains optionLabel html) (sprintf "filter option '%s' renders" optionLabel)

              Expect.isTrue
                  (contains "fuaran-drawing" html && contains "<svg" html)
                  "the Line chart lowers to the inline Drawing SVG over the prune-unfiltered rows"

              Expect.isTrue
                  (contains "data-fuaran-ssr-placeholder=\"DataGrid\"" html
                   && contains "data-fuaran-row-count=\"2\"" html)
                  "the grid keeps its hydration placeholder over the prune-unfiltered row count"
          } ]
