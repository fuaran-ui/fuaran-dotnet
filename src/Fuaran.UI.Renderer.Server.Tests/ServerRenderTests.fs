module Fuaran.UI.Renderer.Server.Tests.ServerRenderTests

// ============================================================================
//  Server renderer (Phase 140) — HTML-string output assertions.
//
//  The server renderer emits an HTML string on plain .NET. Unlike the Feliz
//  client renderer (whose .NET-side ReactElement is opaque), the server output
//  IS a string, so these tests assert directly on the emitted HTML: the shared
//  class vocabulary, real anchors, real Markdig markdown (not the degraded
//  <pre>), the Theme :root block, and the client-library SSR placeholders.
//  Class + ARIA parity with the Feliz renderer is locked separately by the
//  Phase 142 conformance corpus.
// ============================================================================

open Expecto
open Feliz.ViewEngine
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// A representative chrome tree: heading, card (with a link + markdown), a
/// summary-list of label-value rows, and a disclosure.
let private chromeTree: Node<obj> =
    let link: Node<obj> = Fuaran.link "lnk" "/about" "About us"
    let md: Node<obj> = Fuaran.markdown "md" "# Hello\n\nA **bold** world."

    let card: Node<obj> =
        Fuaran.card
            "card"
            { Defaults.card<obj> with
                Heading = Some(TextSource.Literal "Welcome")
                Children = [ link; md ] }

    let row: Node<obj> =
        Fuaran.labelValueRow
            "row1"
            { Defaults.labelValueRow with
                Label = TextSource.Literal "Revenue"
                Value = Binding.Static(Some 1234.0)
                Format = CellFormat.Number(Some 0) }

    let summary: Node<obj> =
        Fuaran.summaryList
            "sum"
            { Defaults.summaryList<obj> with
                Heading = Some(TextSource.Literal "Totals")
                Children = [ row ] }

    let disclosure: Node<obj> =
        Fuaran.disclosure
            "disc"
            { Defaults.disclosure<obj> with
                Heading = TextSource.Literal "Details"
                Open = Binding.Static(Some true)
                DefaultOpen = true
                Children = [ (Fuaran.markdown "dmd" "More info.": Node<obj>) ] }

    let heading: Node<obj> =
        Fuaran.heading
            "h1"
            { Defaults.heading with
                Level = 1
                Text = TextSource.Literal "Scale Mastery" }

    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = [ heading; card; summary; disclosure ] }

let private html = Render.render BindingResolver.empty chromeTree

[<Tests>]
let serverRenderTests =
    testList
        "Fuaran.UI.Renderer.Server"
        [ test "renders valid body-fragment HTML for a representative chrome tree" {
              Expect.isTrue (contains "fuaran-layout-dashboard" html) "dashboard wrapper present"
              Expect.isTrue (contains "data-fuaran-node-id=\"root\"" html) "addressable node-id marker present"
              Expect.isTrue (contains "fuaran-layout-card" html) "card layout present"
              Expect.isTrue (contains "fuaran-card-heading" html) "card heading present"
          }

          test "Link renders a real crawlable <a href>" {
              Expect.isTrue (contains "<a " html) "a real anchor element"
              Expect.isTrue (contains "href=\"/about\"" html) "the href destination"
              Expect.isTrue (contains "fuaran-link" html) "the link class"
              Expect.isTrue (contains "About us" html) "the link label text"
          }

          test "Markdown renders real HTML (Markdig), not the degraded <pre>" {
              Expect.isFalse (contains "fuaran-markdown-fallback" html) "no <pre> fallback class"
              Expect.isTrue (contains "<h1" html && contains "Hello" html) "a real heading from markdown"
              Expect.isTrue (contains "<strong>bold</strong>" html) "real bold emphasis"
          }

          test "Heading renders an <h1> with the heading class" {
              Expect.isTrue (contains "<h1" html) "h1 element"
              Expect.isTrue (contains "fuaran-heading" html) "heading class"
              Expect.isTrue (contains "Scale Mastery" html) "heading text"
          }

          test "SummaryList + LabelValueRow render with resolved, formatted value" {
              Expect.isTrue (contains "fuaran-layout-summary-list" html) "summary-list layout"
              Expect.isTrue (contains "fuaran-label-value-row" html) "label-value row"
              Expect.isTrue (contains "Revenue" html) "row label"
              Expect.isTrue (contains "1234" html) "the resolved + formatted value (Static binding)"
          }

          test "Disclosure renders native <details>/<summary>, open when bound open" {
              Expect.isTrue (contains "<details" html) "details element"
              Expect.isTrue (contains "fuaran-layout-disclosure" html) "disclosure class"
              Expect.isTrue (contains "<summary" html) "summary element"
              Expect.isTrue (contains "open" html) "open attribute when Open binding resolves true"
          }

          test "Theme projects a :root CSS-variable block" {
              let themeHtml =
                  Render.renderWithTheme Defaults.theme BindingResolver.empty chromeTree

              Expect.isTrue (contains "<style>" themeHtml) "style element present"
              Expect.isTrue (contains ":root" themeHtml) ":root selector present"
              Expect.isTrue (contains "--fuaran-tone-brand-bg" themeHtml) "a theme CSS variable present"
          }

          test "renderStatic is identical to render BindingResolver.empty (Phase 434)" {
              Expect.equal
                  (Render.renderStatic chromeTree)
                  (Render.render BindingResolver.empty chromeTree)
                  "renderStatic bakes in BindingResolver.empty — output must match render with the empty sources"
          }

          test "Chart (bar/line) lowers to first-party inline SVG server-side (Phase 526)" {
              // A resolvable Bar/Line chart now renders the lowered Drawing SVG on
              // the server (D3/D4) — no client-hydration placeholder, no blank.
              let row (q: string) (v: float) : Row = Map.ofList [ "x", box q; "y", box v ]

              let chart: Node<obj> =
                  Fuaran.chart
                      "chart"
                      { Defaults.chart<obj> with
                          Kind = ChartKind.Bar
                          Source = Binding.Static(Some(Seq.ofList [ row "Q1" 10.0; row "Q2" 20.0 ]))
                          XField = "x"
                          YFields = [ "y" ] }

              let chartHtml = Render.render BindingResolver.empty chart
              Expect.isTrue (contains "class=\"fuaran-drawing\"" chartHtml) "lowered Drawing svg"
              Expect.isTrue (contains "role=\"img\"" chartHtml) "a11y role"
              Expect.isTrue (contains "fuaran-drawing-rect" chartHtml) "bar rectangles"
              Expect.isFalse (contains "fuaran-chart-ssr-placeholder" chartHtml) "no placeholder for a lowered chart"
          }

          test "Chart (not-yet-lowered kind) renders a deterministic placeholder, not a blank" {
              // Heatmap is the one remaining kind with no lowering rule (the
              // 636/637/638 arms lowered Scatter/Area/Pie), so it keeps the
              // client-hydration placeholder — deterministic, never blank.
              let chart: Node<obj> =
                  Fuaran.chart
                      "chart"
                      { Defaults.chart<obj> with
                          Kind = ChartKind.Heatmap
                          Source =
                              Binding.Static(
                                  Some(
                                      Seq.ofList
                                          [ (Map.ofList [ "x", box 1 ]: Row)
                                            Map.ofList [ "x", box 2 ]
                                            Map.ofList [ "x", box 3 ] ]
                                  )
                              )
                          XField = "x"
                          YFields = [ "y" ] }

              let chartHtml = Render.render BindingResolver.empty chart
              Expect.isTrue (contains "fuaran-chart-ssr-placeholder" chartHtml) "placeholder class"
              Expect.isTrue (contains "data-fuaran-ssr-placeholder=\"Chart\"" chartHtml) "placeholder marker"
              Expect.isTrue (contains "data-fuaran-row-count=\"3\"" chartHtml) "deterministic row count"
          }

          test "Link href is sanitised — javascript: collapses to about:blank" {
              let evil: Node<obj> = Fuaran.link "evil" "javascript:alert(1)" "Click"

              let evilHtml = Render.render BindingResolver.empty evil
              Expect.isFalse (contains "javascript:" evilHtml) "javascript: scheme stripped"
              Expect.isTrue (contains "about:blank" evilHtml) "collapsed to about:blank"
          }

          // ── Phase 141 — server Custom-renderer registry ──────────────────────

          test "unregistered Custom node renders the labelled placeholder" {
              let custom: Node<obj> =
                  Fuaran.custom "cust" "music" "score" Map.empty Option.None []

              let html = Render.render BindingResolver.empty custom
              Expect.isTrue (contains "fuaran-kind-custom-placeholder" html) "placeholder class"
              Expect.isTrue (contains "[fuaran:custom music.score]" html) "labelled placeholder text"
          }

          test "registered server Custom renderer inlines its HTML at the tree position" {
              let custom: Node<obj> =
                  Fuaran.custom "cust" "music" "score" (Map.ofList [ "clef", JStr "treble" ]) Option.None []

              let registry =
                  Registry.empty
                  |> Registry.register "music" "score" (fun props ->
                      let clef =
                          match Map.tryFind "clef" props with
                          | Some(JStr v) -> v
                          | Some other -> Fuaran.Core.Json.render other
                          | None -> "?"

                      Html.div [ prop.className "host-score"; prop.custom ("data-clef", clef) ])

              let html = Render.renderWith registry BindingResolver.empty custom
              Expect.isTrue (contains "host-score" html) "host renderer output inlined"
              Expect.isTrue (contains "data-clef=\"treble\"" html) "props flowed to the host renderer"
              Expect.isFalse (contains "fuaran-kind-custom-placeholder" html) "no placeholder when registered"
          }

          test "StrictReplay content-hash mismatch routes to an error placeholder" {
              let hash: ContentHash =
                  { Algorithm = "SHA256"
                    Hash = "declared-hash"
                    Strictness = HashStrictness.StrictReplay }

              let custom: Node<obj> =
                  Fuaran.custom "cust" "music" "score" Map.empty (Some hash) []

              let registry =
                  Registry.empty
                  |> Registry.registerWithHash "music" "score" "different-hash" (fun _ ->
                      Html.div [ prop.className "host-score" ])

              let html = Render.renderWith registry BindingResolver.empty custom
              Expect.isTrue (contains "fuaran-custom-hash-mismatch" html) "hash-mismatch class"
              Expect.isTrue (contains "data-fuaran-custom-hash-mismatch=\"strict\"" html) "strict marker"
              Expect.isFalse (contains "host-score" html) "the drifted renderer is NOT invoked"
          } ]

// ============================================================================
//  Phase 788 — SSR attribute-NAME injection.
//
//  Feliz.ViewEngine's `ViewBuilder.buildElement` emits `" " + key + "=\"" +
//  value + "\""` and `Interop.mkAttr` escapes the VALUE only: nothing anywhere
//  escapes an attribute NAME. React's own attribute-name validation gives the
//  client renderer an accidental floor the server path has never had, so the
//  key gate (`Sanitize.isAllowedExtraAttributeKey`) plus the emission-site
//  re-check (`Sanitize.isSafeAttributeName`) are the whole defence here.
//
//  These assert on the real emitted HTML string, not on the predicate — the
//  predicate is pinned in `Fuaran.UI.Tests/SanitizeTests.fs`. Each negative
//  assertion is paired with a POSITIVE control proving the ExtraAttributes
//  emission path runs at all for the same tree; without it a renderer that
//  silently stopped emitting ExtraAttributes would pass every "no payload"
//  assertion vacuously.
// ============================================================================

/// The canonical payload: prefix-valid (`data-`), then a space + `=` that
/// terminate the name and open a second, live attribute.
let private injectedKey = "data-x=1 onmouseover=alert(document.domain) z"

[<Tests>]
let extraAttributeNameInjectionTests =
    testList
        "Fuaran.UI.Renderer.Server — ExtraAttributes attribute-name injection (Phase 788)"
        [ test "an injected attribute name never reaches the emitted HTML" {
              // The record-with hatch — the documented bypass of the smart
              // constructor's prefix gate, and the reachable path today (the
              // wire decoder hard-codes `extraAttributes` to None).
              let node: Node<obj> =
                  { Fuaran.heading
                        "h"
                        { Defaults.heading with
                            Text = TextSource.Literal "Title" } with
                      ExtraAttributes = Some(Map.ofList [ injectedKey, "v"; "data-cy", "title" ]) }

              let html = Render.render BindingResolver.empty node

              // POSITIVE CONTROL — the emission path is live for this tree, so
              // the negative assertions below are not vacuous.
              Expect.isTrue (contains "data-cy=\"title\"" html) "the safe ExtraAttributes entry IS emitted"

              Expect.isFalse (contains "onmouseover" html) "no event-handler attribute in the emitted HTML"
              Expect.isFalse (contains "alert(document.domain)" html) "no handler body in the emitted HTML"
              Expect.isFalse (contains "data-x=" html) "the injected key is dropped entirely, not partially emitted"
          }

          test "a valid data-* / aria-* key still round-trips through SSR" {
              let node: Node<obj> =
                  { Fuaran.heading
                        "h"
                        { Defaults.heading with
                            Text = TextSource.Literal "Title" } with
                      ExtraAttributes = Some(Map.ofList [ "data-test-id", "hero"; "aria-describedby", "hint-1" ]) }

              let html = Render.render BindingResolver.empty node
              Expect.isTrue (contains "data-test-id=\"hero\"" html) "data-* key round-trips"
              Expect.isTrue (contains "aria-describedby=\"hint-1\"" html) "aria-* key round-trips"
          }

          test "a whitespace-padded key is emitted TRIMMED, not verbatim" {
              // The gate judges `key.Trim()`; emitting the original would emit a
              // string the gate never inspected.
              let node: Node<obj> =
                  { Fuaran.heading
                        "h"
                        { Defaults.heading with
                            Text = TextSource.Literal "Title" } with
                      ExtraAttributes = Some(Map.ofList [ "  data-cy  ", "padded" ]) }

              let html = Render.render BindingResolver.empty node
              Expect.isTrue (contains "data-cy=\"padded\"" html) "emitted under the trimmed name"
              Expect.isFalse (contains "  data-cy  =" html) "the untrimmed name is not emitted"
          }

          test "go-red self-test: the payload IS an injection if the gate does not fire" {
              // Proves the assertions above measure something. `prop.custom` is
              // the same verbatim-name emission site `extraAttrProps` reaches;
              // handed the payload directly — i.e. with the gate bypassed — it
              // produces exactly the live handler the gate exists to stop. If a
              // future edit made this test pass with the injection absent, the
              // emission site would have gained its own escaping and the
              // negative assertions above would be measuring nothing.
              let unguarded =
                  Feliz.ViewEngine.Render.htmlView (Html.div [ prop.custom (injectedKey, "v") ])

              Expect.isTrue (contains "onmouseover=alert(document.domain)" unguarded) "unguarded emission IS injectable"
          } ]
