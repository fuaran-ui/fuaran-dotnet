module Fuaran.UI.Renderer.Server.Tests.SsrParityTests

// ============================================================================
//  SSR class+ARIA parity conformance corpus (Phase 142).
//
//  Makes the class-name + ARIA parity between the renderers executable, so the
//  two-renderer invariant can't silently drift — the same discipline
//  `wire-format-fixtures/` applies to the codec.
//
//  How parity is locked on .NET:
//
//   1. **The shared spine (provably identical).** Both the Feliz client renderer
//      (`Render.fs`) and the Feliz.ViewEngine server renderer wrap every node in
//      `class="<Theme.nodeClassName node.Kind node.Style>"` and project
//      `Accessibility` via the same `Fuaran.UI.Renderer.Accessibility`
//      `.Core` function. This corpus asserts the SERVER output's outer wrapper
//      equals `Theme.nodeClassName` for the node — so any change to the shared
//      class vocabulary (which BOTH renderers consume) is caught here, and the
//      ARIA projection is the same function on both sides by construction.
//
//   2. **The per-kind body (golden corpus).** The body class names are literals
//      re-emitted in each renderer's per-kind arm — the genuine drift surface.
//      This corpus pins the canonical `fuaran-*` body classes + `role` / `aria-*`
//      attributes the server MUST emit per kind. A deliberate class-name change
//      in the server renderer fails the matching assertion (the lock works on
//      the server tier); the client tier is held against the same golden by the
//      catalog's Phase 12.S Feliz-parity tests + code review.
//
//  The F# Feliz client renderer cannot render to an HTML string on .NET (its
//  `ReactElement` is opaque — which is exactly why `Feliz.ViewEngine` exists as
//  a separate backend), so a byte-level client-vs-server diff isn't expressible
//  here; this corpus is the executable contract both tiers conform to.
//
//  Forward-coupling: adding a `NodeKind` / changing a `fuaran-*` class name
//  extends this corpus in the same change-set (the server-renderer analog of the
//  WIRE_FORMAT.md §11 rule). See `docs/SSR.md` "SSR parity contract".
// ============================================================================

open Expecto
open Feliz.ViewEngine
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// A parity fixture: a canonical node + the `fuaran-*` classes / ARIA tokens the
/// server HTML must carry for it (the golden body vocabulary).
type private Fixture =
    { Name: string
      Node: Node<obj>
      Expected: string list }

// ─── Canonical node builders ────────────────────────────────────────────────

let private leaf id : Node<obj> = Fuaran.markdown id "x"

let private fixtures: Fixture list =
    [ { Name = "Layout/Stack"
        Node =
          Fuaran.stack
              "stk"
              { Defaults.stack<obj> with
                  Children = [ leaf "a"; leaf "b" ] }
        Expected = [ "fuaran-layout-stack"; "fuaran-stack-vertical" ] }

      { Name = "Layout/Card"
        Node =
          Fuaran.card
              "crd"
              { Defaults.card<obj> with
                  Heading = Some(TextSource.Literal "H")
                  Children = [ leaf "a" ] }
        Expected = [ "fuaran-layout-card"; "fuaran-card-heading"; "fuaran-card-body" ] }

      { Name = "Layout/GridLayout"
        Node =
          Fuaran.gridLayout
              "grd"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children = [ leaf "a" ] }
        Expected = [ "fuaran-layout-grid"; "gridTemplateColumns" ] }

      { Name = "Layout/Disclosure"
        Node =
          Fuaran.disclosure
              "dsc"
              { Defaults.disclosure<obj> with
                  Heading = TextSource.Literal "More"
                  Children = [ leaf "a" ] }
        Expected =
          [ "fuaran-layout-disclosure"
            "fuaran-disclosure-summary"
            "fuaran-disclosure-body"
            "<details"
            "<summary" ] }

      { Name = "Layout/Tabs"
        Node =
          Fuaran.tabs
              "tbs"
              { Defaults.tabs<obj> with
                  Children = [ leaf "a"; leaf "b" ]
                  TabHeaders =
                      Some
                          [ { Label = TextSource.Literal "One"
                              Icon = Some "one-glyph"
                              Disabled = Option.None }
                            { Label = TextSource.Literal "Two"
                              Icon = Option.None
                              Disabled = Option.None } ] }
        Expected =
          [ "fuaran-layout-tabs"
            "fuaran-tabs-bar"
            "role=\"tablist\""
            "role=\"tab\""
            "aria-selected"
            "role=\"tabpanel\""
            // Icon-contract: the uniform empty hook, name on `data-icon`.
            "fuaran-icon fuaran-tab-icon"
            "data-icon=\"one-glyph\"" ] }

      // Switch (Phase 392): SSR renders the initial match. With empty sources
      // the state key does not resolve, so the Default child renders — proving
      // the server emits the initial (default) branch inline under the
      // `fuaran-kind-switch` wrapper, byte-comparably to the client's first
      // render (the hydration-parity property, docs/SSR.md).
      { Name = "Switch (state-bound conditional — default branch)"
        Node =
          Fuaran.switch
              "sw"
              { Defaults.switch<obj> with
                  StateKey = "view"
                  Cases = [ { Match = "details"; Child = leaf "d" } ]
                  Default =
                      Fuaran.heading
                          "sw-def"
                          { Defaults.heading with
                              Level = 2
                              Text = TextSource.Literal "Pick one" } }
        Expected = [ "fuaran-kind-switch"; "fuaran-heading"; "<h2" ] }

      { Name = "Display/Heading"
        Node =
          Fuaran.heading
              "hd"
              { Defaults.heading with
                  Level = 2
                  Text = TextSource.Literal "T" }
        Expected = [ "fuaran-heading"; "<h2" ] }

      { Name = "Display/Metric"
        Node =
          Fuaran.metric
              "mt"
              { Defaults.metric with
                  Label = TextSource.Literal "Rev"
                  Value = Binding.Static(Some 9.0)
                  Icon = Some "trend-glyph" }
        Expected =
          [ "fuaran-metric"
            "fuaran-metric-label"
            "fuaran-metric-value"
            "fuaran-icon fuaran-metric-icon"
            "data-icon=\"trend-glyph\"" ] }

      { Name = "Display/Fact (icon-contract hook)"
        Node =
          Fuaran.factSpec
              "fct"
              { Defaults.fact with
                  Label = TextSource.Literal "Patient"
                  Value = TextSource.Literal "Alice"
                  Icon = Some "user-glyph" }
        Expected =
          [ "fuaran-fact"
            "fuaran-fact-label"
            "fuaran-fact-value"
            "fuaran-icon fuaran-fact-icon"
            "data-icon=\"user-glyph\""
            "aria-hidden=\"true\"" ] }

      { Name = "Display/Link"
        Node = Fuaran.link "lk" "/x" "X"
        Expected = [ "fuaran-link"; "<a "; "href=\"/x\"" ] }

      { Name = "Display/Callout"
        Node =
          Fuaran.callout
              "cl"
              { Defaults.callout with
                  Body = TextSource.Literal "B"
                  Icon = Some "info-glyph" }
        Expected =
          [ "fuaran-callout"
            "fuaran-callout-body"
            "fuaran-icon fuaran-callout-icon"
            "data-icon=\"info-glyph\"" ] }

      { Name = "Input/Button"
        Node =
          Fuaran.button
              "btn"
              { Defaults.button<obj> with
                  Label = TextSource.Literal "Go"
                  Variant = ButtonVariant.Primary
                  Icon = Some "go-glyph" }
        Expected =
          [ "fuaran-button"
            "fuaran-button-primary"
            "<button"
            "fuaran-icon fuaran-button-icon"
            "data-icon=\"go-glyph\"" ] }

      { Name = "Visualisation/Grid (static-table mode — staticRows via Fuaran.table)"
        Node =
          Fuaran.table
              "tbl"
              { Defaults.table<obj> with
                  Headers = [ TextSource.Literal "H" ]
                  Rows = [ [ TextSource.Literal "v" ] ] }
        Expected =
          [ "fuaran-table"
            "fuaran-table-header"
            "fuaran-table-row"
            "fuaran-table-cell" ] }

      // Phase 526 — a Bar/Line chart lowers to a first-party Drawing (D3/D4); the
      // server emits the inline SVG, not the hydration placeholder.
      { Name = "Visualisation/Chart (bar → lowered inline SVG)"
        Node =
          Fuaran.chart
              "cht"
              { Defaults.chart<obj> with
                  Kind = ChartKind.Bar
                  Source = Binding.Static(Some(Seq.ofList [ box (Map.ofList [ "x", box "Q1"; "y", box 10.0 ]) ]))
                  XField = "x"
                  YFields = [ "y" ] }
        Expected = [ "fuaran-drawing"; "fuaran-drawing-rect"; "role=\"img\""; "<svg" ] }

      // The one not-yet-lowered kind (Heatmap — Scatter/Area/Pie gained arms in
      // Phases 636/637/638) keeps the deterministic client-hydration placeholder.
      { Name = "Visualisation/Chart (Heatmap — SSR placeholder)"
        Node =
          Fuaran.chart
              "cht2"
              { Defaults.chart<obj> with
                  Kind = ChartKind.Heatmap
                  Source = Binding.Static(Some(Seq.ofList [ box 1 ]))
                  XField = "x"
                  YFields = [ "y" ] }
        Expected = [ "fuaran-chart-ssr-placeholder"; "data-fuaran-ssr-placeholder=\"Chart\"" ] }

      { Name = "Custom (unregistered placeholder)"
        Node = Fuaran.custom "cu" "m" "c" Map.empty Option.None []
        Expected = [ "fuaran-kind-custom-placeholder" ] }

      // ── Phase 287/289 vocabulary-completion primitives ─────────────────────
      { Name = "Display/Image"
        Node =
          Fuaran.imageSpec
              "img"
              { Defaults.image with
                  Src = Binding.Static(Some "/a.png")
                  Alt = TextSource.Literal "Alt"
                  Variant = ImageVariant.Avatar }
        Expected =
          [ "fuaran-image"
            "fuaran-image-avatar"
            "<img"
            "src=\"/a.png\""
            "alt=\"Alt\"" ] }

      { Name = "Display/List"
        Node =
          Fuaran.listSpec
              "lst"
              { Defaults.list with
                  Items = [ TextSource.Literal "a" ]
                  Ordered = true }
        Expected = [ "fuaran-list"; "fuaran-list-ordered"; "fuaran-list-item"; "<ol"; "<li" ] }

      { Name = "Layout/Box separator (Phase 459 — retired Divider)"
        Node = Fuaran.divider "dv"
        Expected = [ "fuaran-layout-separator"; "<hr" ] }

      // The overlay render-fidelity contract (Phase 289): the overlay is ALWAYS
      // in the SSR HTML (no portal); role/aria + body classes are pinned so the
      // client hydration finds the identical structure (no mismatch).
      { Name = "Display/Toast (overlay contract)"
        Node =
          Fuaran.toast
              "ts"
              { Defaults.toast with
                  Message = TextSource.Literal "Saved"
                  Open = Binding.Static(Some true) }
        Expected =
          [ "fuaran-toast"
            "fuaran-toast-info"
            "role=\"status\""
            "aria-live=\"polite\""
            "fuaran-toast-message"
            "fuaran-toast-dismiss" ] }

      { Name = "Layout/Modal (overlay contract)"
        Node =
          Fuaran.modal
              "md"
              { Defaults.modal<obj> with
                  Heading = Some(TextSource.Literal "Confirm")
                  Open = Binding.Static(Some true)
                  Children = [ leaf "a" ] }
        Expected =
          [ "fuaran-modal-overlay"
            "fuaran-modal-dialog"
            "role=\"dialog\""
            "aria-modal=\"true\""
            "fuaran-modal-heading"
            "fuaran-modal-dismiss"
            "fuaran-modal-body" ] }

      { Name = "Layout/ScrollArea (overflow contract)"
        Node =
          Fuaran.scrollArea
              "sc"
              { Defaults.scrollArea<obj> with
                  Children = [ leaf "a" ]
                  MaxHeight = Some 200 }
        Expected =
          [ "fuaran-scrollarea"
            "fuaran-scrollarea-vertical"
            "tabindex=\"0\""
            "max-height" ] }

      // Phase 290/293 — deterministic-render contract: the parity output is the
      // bare escaped <pre><code> / source fallback; highlighting + KaTeX are
      // client-only enhancements outside this comparison.
      { Name = "Display/CodeBlock (deterministic <pre><code>)"
        Node = Fuaran.codeBlock "cb" "fsharp" "let x = 1"
        Expected =
          [ "fuaran-codeblock"
            "data-language=\"fsharp\""
            "fuaran-codeblock-pre"
            "fuaran-codeblock-code"
            "language-fsharp"
            "<pre"
            "<code"
            "fuaran-codeblock-copy" ] }

      // Phase 658 — the deterministic Math output is now native MathML for the
      // closed subset (real superscripts, no JS), and the raw-source span only
      // for out-of-subset input. Both variants carry `data-fuaran-math-src` (the
      // KaTeX enhancement's retargeted source). See docs/MATH-DEGRADATION.md.
      { Name = "Display/Math in-subset (deterministic native MathML)"
        Node = Fuaran.math "mth" "x^2 + y^2"
        Expected =
          [ "fuaran-math"
            "fuaran-math-block"
            "data-math-display=\"block\""
            "data-fuaran-math-src=\"x^2 + y^2\""
            "<math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\"block\">"
            "<msup><mi>x</mi><mn>2</mn></msup>"
            "<mo>+</mo>" ] }

      { Name = "Display/Math out-of-subset (deterministic source fallback)"
        Node = Fuaran.math "mth2" "\\sqrt{2}"
        Expected =
          [ "fuaran-math"
            "fuaran-math-block"
            "fuaran-math-source"
            "data-math-display=\"block\""
            "data-fuaran-math-src=" ] }

      // Phase 525 — the Drawing primitive renders first-party inline SVG,
      // static-geometry so SSR emits the full drawing (not a placeholder), with
      // `role="img"` + `<title>` (R3 a11y) and the parity-locked `fuaran-drawing*`
      // classes.
      { Name = "Display/Drawing (inline SVG + a11y)"
        Node =
          Fuaran.drawingSpec
              "dr"
              { Defaults.drawing with
                  ViewBox =
                      { MinX = 0.0
                        MinY = 0.0
                        Width = 100.0
                        Height = 50.0 }
                  Shapes =
                      [ Shape.Rectangle(10.0, 10.0, 30.0, 20.0, Option.None, Defaults.drawStyle)
                        Shape.Label(50.0, 40.0, TextSource.Literal "Q1", Defaults.drawStyle) ]
                  Title = Some(TextSource.Literal "Bars")
                  Description = Some(TextSource.Literal "A tiny bar drawing") }
        Expected =
          [ "fuaran-drawing"
            "fuaran-drawing-rect"
            "fuaran-drawing-label"
            "role=\"img\""
            "<svg"
            "<rect"
            "<text"
            "<title"
            "<desc" ] } ]

[<Tests>]
let ssrParityTests =
    testList
        "SSR parity corpus"
        [ // The shared-spine lock: the server's outer wrapper class equals the
          // shared Core `Theme.nodeClassName` the client renderer also uses, so a
          // change to the shared class vocabulary is caught for BOTH tiers.
          test "every fixture's outer wrapper uses the shared Theme.nodeClassName" {
              for f in fixtures do
                  let html = Render.render BindingResolver.empty f.Node

                  let expectedOuter =
                      Theme.nodeClassName f.Node.Kind (f.Node.Style |> Option.defaultValue Fuaran.UI.Defaults.style)

                  Expect.isTrue
                      (contains (sprintf "class=\"%s\"" expectedOuter) html)
                      (sprintf "%s: outer wrapper must be the shared Theme.nodeClassName" f.Name)
          }

          // The golden body-vocabulary lock: the server emits the canonical
          // per-kind classes + ARIA. A class-name change in the server renderer
          // fails here.
          test "every fixture's server HTML carries its golden class+ARIA tokens" {
              for f in fixtures do
                  let html = Render.render BindingResolver.empty f.Node

                  for token in f.Expected do
                      Expect.isTrue
                          (contains token html)
                          (sprintf "%s: expected token '%s' in server HTML" f.Name token)
          }

          // Guard that the lock actually bites: a token NOT in the vocabulary
          // must be absent (proves contains-assertions aren't vacuously true).
          test "the parity lock is not vacuous — a bogus class is absent" {
              let html = Render.render BindingResolver.empty (Fuaran.link "lk" "/x" "X")
              Expect.isFalse (contains "fuaran-not-a-real-class" html) "a non-existent class must not appear"
          }

          // The icon-contract lock: the icon NAME rides the `data-icon`
          // attribute of an EMPTY hook element and must never appear as
          // visible text content anywhere in the HTML.
          test "icon names ride data-icon and never leak as text content" {
              for f in fixtures do
                  let html = Render.render BindingResolver.empty f.Node

                  for token in f.Expected do
                      if token.StartsWith("data-icon=\"", System.StringComparison.Ordinal) then
                          let name = token.Substring("data-icon=\"".Length).TrimEnd('"')

                          Expect.isFalse
                              (contains (sprintf ">%s<" name) html)
                              (sprintf "%s: icon name '%s' must not appear as text content" f.Name name)
          } ]
