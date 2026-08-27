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

/// Render for THIS corpus (Phase 1026).
///
/// The corpus asks a question about the class + ARIA vocabulary, so it renders
/// under a policy that admits every destination. That is not a weakening: the
/// ambient default-deny has its own dedicated corpus in `EgressRenderTests`,
/// and leaving it switched on here would mean a class-name regression and an
/// egress-policy change produced the same failure output — a corpus that cannot
/// say which of two things broke is worth much less than two that can.
///
/// Several fixtures below are `mailto:` links, which the DEFAULT policy refuses
/// (`AllowNonNetwork = false`); rendering them under `permissiveEgress` is what
/// keeps them testing the anchor shape rather than the policy.
let private renderHtml (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.renderWithEgress Sanitize.permissiveEgress Registry.empty sources node

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

      // The inline style must use the CSS property name, never the client
      // arm's camelCase `gridTemplateColumns` — Feliz.ViewEngine emits the
      // key verbatim, and CSS ignores camelCase names, so a camelCase
      // emission silently renders every SSR grid single-column.
      { Name = "Layout/GridLayout"
        Node =
          Fuaran.gridLayout
              "grd"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children = [ leaf "a" ] }
        Expected = [ "fuaran-layout-grid"; "grid-template-columns:repeat(3, 1fr)" ] }

      { Name = "Layout/GridLayoutTemplated"
        Node =
          Fuaran.gridLayoutTemplated
              "grdt"
              "repeat(auto-fit, minmax(15rem, 1fr))"
              { Defaults.gridLayout<obj> with
                  Children = [ leaf "a" ] }
        Expected =
          [ "fuaran-layout-grid"
            "grid-template-columns:repeat(auto-fit, minmax(15rem, 1fr))" ] }

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
                  On = Binding.State("view", None)
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
                  Source = Binding.Static(Some(Seq.ofList [ (Map.ofList [ "x", box "Q1"; "y", box 10.0 ]: Row) ]))
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
                  Source = Binding.Static(Some(Seq.ofList [ (Map.ofList [ "x", box 1 ]: Row) ]))
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

      // Phase 1077 — the presentation tokens' class + attribute vocabulary.
      // A separate fixture rather than an extension of the one above, so a
      // regression names which half broke: the variant mapping or the
      // presentation mapping.
      { Name = "Display/Image (Phase 1077 presentation tokens)"
        Node =
          Fuaran.imageSpec
              "imgp"
              { Defaults.image with
                  Src = Binding.Static(Some "/hero.jpg")
                  Alt = TextSource.Literal "Hero"
                  Fit = ImageFit.Cover
                  AspectRatio = ImageAspect.SixteenNine
                  Loading = ImageLoading.Lazy }
        Expected =
          [ "fuaran-image"
            "fuaran-image-fit-cover"
            "fuaran-image-aspect-sixteen-nine"
            "loading=\"lazy\"" ] }

      // Phase 1078 — the caption's structural vocabulary. The `<figure>` and
      // `<figcaption>` element names are pinned alongside the classes because
      // the semantics live in the ELEMENTS: an assistive technology binds the
      // caption to the image on the `figure`/`figcaption` pair, and a wrapper
      // that carried the same classes on two `<div>`s would look identical in
      // a class-only assertion and be worthless to a screen reader.
      { Name = "Display/Image (Phase 1078 caption — literal)"
        Node =
          Fuaran.imageSpec
              "imgc"
              { Defaults.image with
                  Src = Binding.Static(Some "/harbour.jpg")
                  Alt = TextSource.Literal "Fishing boats moored at first light"
                  Caption = Some(TextSource.Literal "The harbour at dawn") }
        Expected =
          [ "<figure class=\"fuaran-image-figure\">"
            "<figcaption class=\"fuaran-image-figure-caption\">The harbour at dawn</figcaption>"
            "</figure>" ] }

      // Phase 1080 — the srcSet emission vocabulary. The candidates are
      // authored DESCENDING and the expectation is the ASCENDING string, so
      // this fixture pins the renderer's sort as well as its spelling: a
      // renderer that emitted authored order would produce a `srcset`
      // containing all the same URLs and fail here.
      { Name = "Display/Image (Phase 1080 srcSet — ascending, with sizes)"
        Node =
          Fuaran.imageSpec
              "imgs"
              { Defaults.image with
                  Src = Binding.Static(Some "/harbour.jpg")
                  Alt = TextSource.Literal "Harbour"
                  SrcSet =
                      [ { Src = Binding.Static(Some "/harbour-1600.jpg")
                          Width = 1600 }
                        { Src = Binding.Static(Some "/harbour-800.jpg")
                          Width = 800 }
                        { Src = Binding.Static(Some "/harbour-400.jpg")
                          Width = 400 } ] }
        Expected =
          [ "srcset=\"/harbour-400.jpg 400w, /harbour-800.jpg 800w, /harbour-1600.jpg 1600w\""
            "sizes=\"100vw\"" ] }

      // Phase 1079 — the expansion affordance's structural vocabulary. Like the
      // caption fixture above, the ELEMENT name is pinned and not only the
      // class: the whole no-JS claim is that this is an `<a href>`, and a
      // `<span class="fuaran-image-expand">` carrying a data attribute would
      // pass a class-only assertion while giving a scriptless reader nothing.
      // The `href` is pinned to the same value as the `src` because that is the
      // contract — the expansion goes to the asset the image already names.
      { Name = "Display/Image (Phase 1079 expandable — a real anchor to the asset)"
        Node =
          Fuaran.imageSpec
              "imge"
              { Defaults.image with
                  Src = Binding.Static(Some "/harbour.jpg")
                  Alt = TextSource.Literal "Harbour"
                  Expandable = true }
        Expected =
          [ "<a class=\"fuaran-image-expand\" href=\"/harbour.jpg\" data-fuaran-expandable=\"\">"
            "<img class=\"fuaran-image\" src=\"/harbour.jpg\" alt=\"Harbour\">"
            "</a>" ] }

      // Phase 1079 — the NESTING, pinned as a fixture rather than left to the
      // prose. `<figure>` wraps `<a>` wraps `<img>`, and the `<figcaption>` is
      // the anchor's SIBLING: the caption is outside the link target because it
      // is prose a reader selects and quotes, not a second click surface.
      // Asserting the two opening tags in order is what catches the inversion
      // (anchor outside figure), which would carry every one of these classes.
      { Name = "Display/Image (Phase 1079 expandable + caption — figure wraps anchor wraps img)"
        Node =
          Fuaran.imageSpec
              "imgef"
              { Defaults.image with
                  Src = Binding.Static(Some "/harbour.jpg")
                  Alt = TextSource.Literal "Harbour"
                  Expandable = true
                  Caption = Some(TextSource.Literal "The harbour at dawn") }
        Expected =
          [ "<figure class=\"fuaran-image-figure\"><a class=\"fuaran-image-expand\" href=\"/harbour.jpg\" data-fuaran-expandable=\"\">"
            "</a><figcaption class=\"fuaran-image-figure-caption\">The harbour at dawn</figcaption></figure>" ] }

      // ── Phase 1076: the media transport ───────────────────────────────────
      //
      // Four fixtures, and each pins something the WIRE corpus structurally
      // cannot: the corpus pins bytes, and every claim below is about markup.
      { Name = "Display/Media (video — element, class, label, transport)"
        Node =
          Fuaran.mediaSpec
              "mv"
              { Defaults.media with
                  Src = Binding.Static(Some "/walkthrough.mp4")
                  Label = TextSource.Literal "Studio walkthrough" }
        Expected =
          [ "<video"
            "class=\"fuaran-media fuaran-media-video\""
            "src=\"/walkthrough.mp4\""
            "aria-label=\"Studio walkthrough\""
            "controls=\"true\"" ] }

      // THE PROBE. The pairing is the one rule on this kind whose violation is
      // silent: an unmuted `autoplay` is blocked by every browser, so the video
      // simply never starts and the document's declaration quietly means
      // nothing. Both attributes are asserted, so removing either fails here —
      // which is how this assertion was verified, by observing it red against a
      // renderer that emitted `autoplay` alone.
      { Name = "Display/Media (video autoplay NEVER renders without muted)"
        Node =
          Fuaran.mediaSpec
              "mva"
              { Defaults.media with
                  Src = Binding.Static(Some "/ambient.mp4")
                  Label = TextSource.Literal "Ambient loop"
                  Controls = false
                  Loop = true
                  Kind = MediaKind.Video(true, None) }
        Expected = [ "autoplay=\"true\""; "muted=\"true\""; "loop=\"true\"" ] }

      { Name = "Display/Media (video poster — the second URL through the same floor)"
        Node =
          Fuaran.mediaSpec
              "mvp"
              { Defaults.media with
                  Src = Binding.Static(Some "/walkthrough.mp4")
                  Label = TextSource.Literal "Studio walkthrough"
                  Kind = MediaKind.Video(false, Some(Binding.Static(Some "/walkthrough-poster.jpg"))) }
        Expected = [ "poster=\"/walkthrough-poster.jpg\"" ] }

      // The Audio arm has NO autoplay pathway in the type, so the only way this
      // fixture could grow one is a renderer inventing it. Asserting the
      // element and the class is what pins that `<audio>` is emitted rather
      // than a `<video>` with a different class — a mistake a class-only
      // assertion would pass.
      { Name = "Display/Media (audio — its own element, and no autoplay anywhere)"
        Node =
          Fuaran.mediaSpec
              "ma"
              { Defaults.media with
                  Src = Binding.Static(Some "/commentary.mp3")
                  Label = TextSource.Literal "Curator's commentary"
                  Kind = MediaKind.Audio }
        Expected =
          [ "<audio"
            "class=\"fuaran-media fuaran-media-audio\""
            // The apostrophe is deliberate: the label reaches an ATTRIBUTE, so
            // the entity-encoded form is what pins that it goes through the
            // engine's escape rather than being written raw.
            "aria-label=\"Curator&apos;s commentary\""
            "controls=\"true\"" ] }

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
            "<desc" ] }

      // Phase 812 — protected email link: wrapper + protected classes present,
      // and the href begins with the entity-encoded "mailto:" prefix
      // (&#109;… = 'm'); the plaintext-absence lock is the dedicated test
      // below (the fixture vocabulary only asserts presence).
      { Name = "Display/LinkProtectedEmail"
        Node = Fuaran.emailLink "plk" "user@example.com" "user@example.com"
        Expected =
          [ "fuaran-link-protected-wrap"
            "fuaran-link fuaran-link-protected"
            "&#109;&#97;&#105;&#108;&#116;&#111;&#58;" ] } ]

[<Tests>]
let ssrParityTests =
    testList
        "SSR parity corpus"
        [ // The shared-spine lock: the server's outer wrapper class equals the
          // shared Core `Theme.nodeClassName` the client renderer also uses, so a
          // change to the shared class vocabulary is caught for BOTH tiers.
          test "every fixture's outer wrapper uses the shared Theme.nodeClassName" {
              for f in fixtures do
                  let html = renderHtml BindingResolver.empty f.Node

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
                  let html = renderHtml BindingResolver.empty f.Node

                  for token in f.Expected do
                      Expect.isTrue
                          (contains token html)
                          (sprintf "%s: expected token '%s' in server HTML" f.Name token)
          }

          // ── Phase 1076 — the two NEGATIVE media claims ────────────────────
          //
          // The fixture vocabulary above asserts presence only, so it cannot
          // state either of this kind's actual safety properties. Both are
          // absences, and an absence is exactly what a corpus of substring
          // expectations is blind to.
          test "an Audio node emits no autoplay pathway of any kind" {
              let html =
                  renderHtml
                      BindingResolver.empty
                      (Fuaran.mediaSpec
                          "ma-neg"
                          { Defaults.media with
                              Src = Binding.Static(Some "/commentary.mp3")
                              Label = TextSource.Literal "Commentary"
                              Kind = MediaKind.Audio })

              Expect.isFalse (contains "autoplay" html) "an <audio> must never carry an autoplay attribute"
              Expect.isFalse (contains "muted" html) "an <audio> has no autoplay, so it has nothing to mute"
          }

          // A video that does NOT declare autoplay must not carry `muted`
          // either. The pairing runs one way: muted is what makes autoplay
          // honest, and emitting it unasked would silence a video the reader
          // pressed play on.
          test "a video without autoplay carries neither attribute" {
              let html =
                  renderHtml
                      BindingResolver.empty
                      (Fuaran.mediaSpec
                          "mv-neg"
                          { Defaults.media with
                              Src = Binding.Static(Some "/walkthrough.mp4")
                              Label = TextSource.Literal "Walkthrough" })

              Expect.isFalse (contains "autoplay" html) "autoplay is not declared, so it must not be emitted"

              Expect.isFalse
                  (contains "muted" html)
                  "muted rides autoplay; unasked, it silences a video the reader started"
          }

          // ── Phase 951 — WHERE the projection lands ────────────────────────
          //
          // The fixture vocabulary above asserts PRESENCE, so it cannot tell a
          // `role="link"` on the wrapper from one on the anchor — which is
          // exactly the defect D4 fixes. These tests split the emitted HTML at
          // the wrapper's own `>` and assert against each element separately,
          // so the lock is placement-sensitive rather than presence-sensitive.
          let a11yNode (node: Node<obj>) =
              node
              |> Node.withAccessibility (
                  Some
                      { Defaults.Accessibility.empty with
                          Role = Some AriaRole.Link
                          Label = Some(Binding.Static(Some "Home")) }
              )
              |> Node.withExtraAttribute "aria-current" "page"
              |> Node.withExtraAttribute "data-test-hook" "nav"

          /// The wrapper's own open tag — everything up to its first `>`.
          let wrapperTag (html: string) =
              html.Substring(0, html.IndexOf('>') + 1)

          test "Link — the a11y projection and aria-* extras land on the <a>, not the wrapper (Phase 951)" {
              let html =
                  renderHtml BindingResolver.empty (a11yNode (Fuaran.link "lk" "/home" "Home"))

              let wrapper = wrapperTag html

              Expect.isFalse (contains "role=" wrapper) "role must not sit on the wrapper div"
              Expect.isFalse (contains "aria-label" wrapper) "aria-label must not sit on the wrapper div"
              Expect.isFalse (contains "aria-current" wrapper) "an aria-* extra must not sit on the wrapper div"

              // The data-* half is ADDRESSING and stays with data-fuaran-node-id.
              Expect.isTrue (contains "data-test-hook=\"nav\"" wrapper) "a data-* extra stays on the wrapper div"
              Expect.isTrue (contains "data-fuaran-node-id=\"lk\"" wrapper) "the node address stays on the wrapper div"

              let anchor = html.Substring(html.IndexOf("<a "))
              let anchorTag = anchor.Substring(0, anchor.IndexOf('>') + 1)

              Expect.isTrue (contains "role=\"link\"" anchorTag) "role lands on the anchor"
              Expect.isTrue (contains "aria-label=\"Home\"" anchorTag) "aria-label lands on the anchor"
              Expect.isTrue (contains "aria-current=\"page\"" anchorTag) "the aria-* extra follows the projection"
              Expect.isFalse (contains "data-test-hook" anchorTag) "a data-* extra does not follow the projection"
          }

          test "Button — the a11y projection lands on the <button> (Phase 951)" {
              let node =
                  a11yNode (
                      Fuaran.button
                          "btn"
                          { Defaults.button<obj> with
                              Label = TextSource.Literal "Go" }
                  )

              let html = renderHtml BindingResolver.empty node
              Expect.isFalse (contains "aria-label" (wrapperTag html)) "aria-label must not sit on the wrapper div"

              let btn = html.Substring(html.IndexOf("<button"))
              let btnTag = btn.Substring(0, btn.IndexOf('>') + 1)
              Expect.isTrue (contains "aria-label=\"Home\"" btnTag) "aria-label lands on the button"
              Expect.isTrue (contains "aria-current=\"page\"" btnTag) "the aria-* extra follows the projection"
          }

          test "Image — the a11y projection lands on the <img> (Phase 951)" {
              let node =
                  a11yNode (
                      Fuaran.imageSpec
                          "img"
                          { Defaults.image with
                              Src = Binding.Static(Some "/a.png")
                              Alt = TextSource.Literal "Alt" }
                  )

              let html = renderHtml BindingResolver.empty node
              Expect.isFalse (contains "aria-label" (wrapperTag html)) "aria-label must not sit on the wrapper div"

              let img = html.Substring(html.IndexOf("<img"))
              Expect.isTrue (contains "aria-label=\"Home\"" img) "aria-label lands on the img"
          }

          // The other half of the rule: a kind whose body is NOT the semantic
          // element keeps the whole projection — a11y AND both halves of the
          // extras — on the wrapper, in the pre-951 order.
          test "a non-forwarding kind keeps the whole projection on the wrapper (Phase 951)" {
              let html = renderHtml BindingResolver.empty (a11yNode (Fuaran.markdown "md" "x"))
              let wrapper = wrapperTag html

              Expect.isTrue (contains "role=\"link\"" wrapper) "role stays on the wrapper for a non-forwarding kind"
              Expect.isTrue (contains "aria-label=\"Home\"" wrapper) "aria-label stays on the wrapper"
              Expect.isTrue (contains "aria-current=\"page\"" wrapper) "an aria-* extra stays on the wrapper"
              Expect.isTrue (contains "data-test-hook=\"nav\"" wrapper) "a data-* extra stays on the wrapper"
          }

          // The protected-email Link variant: an entity-encoded opaque anchor
          // string, so the projection lands on the wrap <span> — the only
          // element that arm owns in BOTH tiers (D4's stated limit). Pinned so
          // the limit is a recorded behaviour rather than an accident.
          test "protected-email Link — the projection lands on the wrap span (Phase 951)" {
              let html =
                  renderHtml BindingResolver.empty (a11yNode (Fuaran.emailLink "plk" "u@e.com" "u@e.com"))

              Expect.isFalse (contains "aria-label" (wrapperTag html)) "aria-label must not sit on the wrapper div"

              let span = html.Substring(html.IndexOf("<span"))
              let spanTag = span.Substring(0, span.IndexOf('>') + 1)
              Expect.isTrue (contains "aria-label=\"Home\"" spanTag) "aria-label lands on the wrap span"
          }

          // Guard that the lock actually bites: a token NOT in the vocabulary
          // must be absent (proves contains-assertions aren't vacuously true).
          test "the parity lock is not vacuous — a bogus class is absent" {
              let html = renderHtml BindingResolver.empty (Fuaran.link "lk" "/x" "X")
              Expect.isFalse (contains "fuaran-not-a-real-class" html) "a non-existent class must not appear"
          }

          // Phase 812 — the protected-email absence lock: the address must not
          // appear in plaintext ANYWHERE in the emitted document (href or
          // text). The entity-encoded anchor still decodes to a working
          // mailto: with JavaScript disabled.
          test "protected email link emits no plaintext address" {
              let html =
                  renderHtml BindingResolver.empty (Fuaran.emailLink "plk" "user@example.com" "user@example.com")

              Expect.isFalse (contains "user@example.com" html) "plaintext address must be absent"
              Expect.isFalse (contains "mailto:" html) "plaintext mailto: prefix must be absent"
              Expect.isTrue (contains "&#117;&#115;&#101;&#114;&#64;" html) "entity-encoded address must be present"
          }

          test "an unprotected mailto link still emits the plain anchor" {
              let html =
                  renderHtml BindingResolver.empty (Fuaran.link "lk" "mailto:user@example.com" "Email us")

              Expect.isTrue (contains "href=\"mailto:user@example.com\"" html) "plain mailto anchor unchanged"
              Expect.isFalse (contains "fuaran-link-protected" html) "protected classes absent"
          }

          // Phase 877 — CSR/SSR parity for a rotated `Label`.
          //
          // Both renderers reach the drawing through the SAME shared emitter:
          // `Fuaran.UI.Renderer/Render.fs` and `Fuaran.UI.Renderer.Server/Render.fs`
          // each wrap `DrawingSvg.render ctx.Sources (renderText ctx) spec` in a
          // `dangerouslySetInnerHTML` div, so the SVG payload is identical by
          // construction rather than by two literals kept in step. What is NOT
          // free — and is what this pins — is that the server tier splices that
          // payload through VERBATIM: an escape, a re-serialisation, or an
          // attribute-reordering pass anywhere in the SSR path would silently
          // desynchronise the two tiers, and `transform="rotate(…)"` is exactly
          // the kind of attribute such a pass mangles (parentheses + spaces).
          //
          // This is the strongest parity assertion expressible on .NET — the
          // Feliz client renderer produces an opaque `ReactElement` with no
          // string projection (see this file's header), so a byte-level
          // client-vs-server diff cannot be written here at all.
          test "rotated Label — the SSR payload is the shared emitter's bytes verbatim (Phase 877)" {
              let rotatedSpec =
                  { Defaults.drawing with
                      ViewBox =
                          { MinX = 0.0
                            MinY = 0.0
                            Width = 100.0
                            Height = 50.0 }
                      Shapes =
                          [ Shape.Label(
                                50.0,
                                40.0,
                                TextSource.Literal "Q1",
                                { Defaults.drawStyle with
                                    TextAnchor = Some TextAnchor.Middle
                                    Rotation = Some -30.0 }
                            ) ]
                      Title = Some(TextSource.Literal "Tilted") }

              let node = Fuaran.drawingSpec "dr-rot" rotatedSpec

              // The shared emitter's own output — the exact string the CLIENT
              // renderer hands to `dangerouslySetInnerHTML`.
              let shared =
                  DrawingSvg.render
                      BindingResolver.empty
                      (fun t ->
                          match t with
                          | TextSource.Literal s -> s
                          | _ -> "?")
                      rotatedSpec

              let html = renderHtml BindingResolver.empty node

              Expect.isTrue
                  (contains "transform=\"rotate(-30 50 40)\"" shared)
                  "the shared emitter rotates about the label anchor"

              Expect.isTrue (contains shared html) "server HTML embeds the shared SVG payload byte-for-byte"
          }

          // Phase 883 — the same parity argument as the rotation test above, on
          // the seam most likely to break it. A tip is XML TEXT CONTENT inside a
          // `<title>` CHILD, so the SSR path has two extra chances to interfere
          // that an attribute did not: a re-escaping pass would double-encode
          // `&amp;` into `&amp;amp;` (silently — the page still renders, the tooltip
          // just reads wrong), and a well-meaning "tidy the markup" pass would
          // re-close the now-open `<rect>` differently from the client.
          test "tipped marks — the SSR payload is the shared emitter's bytes verbatim (Phase 883)" {
              let tippedSpec =
                  { Defaults.drawing with
                      ViewBox =
                          { MinX = 0.0
                            MinY = 0.0
                            Width = 100.0
                            Height = 50.0 }
                      Shapes =
                          [ Shape.Rectangle(
                                10.0,
                                10.0,
                                20.0,
                                30.0,
                                Option.None,
                                { Defaults.drawStyle with
                                    Tip = Some(TextSource.Literal "revenue · R&D · 1,234") }
                            ) ]
                      Title = Some(TextSource.Literal "Tipped") }

              let node = Fuaran.drawingSpec "dr-tip" tippedSpec

              let shared =
                  DrawingSvg.render
                      BindingResolver.empty
                      (fun t ->
                          match t with
                          | TextSource.Literal s -> s
                          | _ -> "?")
                      tippedSpec

              let html = renderHtml BindingResolver.empty node

              Expect.isTrue
                  (contains "><title>revenue · R&amp;D · 1,234</title></rect>" shared)
                  "the shared emitter opens the rect and closes it around the title"

              Expect.isTrue (contains shared html) "server HTML embeds the shared SVG payload byte-for-byte"

              Expect.isFalse (contains "&amp;amp;" html) "the SSR path does not re-escape the already-escaped tip"
          }

          // ── Phase 1078 — the caption ──────────────────────────────────────
          //
          // The acceptance criterion for this phase is a NEGATIVE: an
          // uncaptioned image must emit exactly what it emitted before the
          // field existed. Asserting merely that `<figure` is absent would
          // pass on a renderer that wrapped in a `<span>` instead, so this
          // pins the WHOLE emission byte-for-byte. The literal below is the
          // pre-phase output — the `None` branch returns the `<img>`
          // expression untouched, so there is nothing between this string and
          // the shape that shipped.
          //
          // The assertion is on the TAIL rather than the whole string on
          // purpose: the wrapper's own class list belongs to the shared
          // `Theme.nodeClassName` vocabulary, which other phases legitimately
          // move and which the first test in this list already locks. Pinning
          // it twice would make this test fail for reasons that have nothing
          // to do with captions. What the tail pins is exactly this arm's
          // claim — the `<img>`'s own bytes, its position as the wrapper's
          // immediate child, and that nothing at all follows it.
          test "an uncaptioned image emits the bare <img> — this arm's emission, byte for byte" {
              let node =
                  Fuaran.imageSpec
                      "imgb"
                      { Defaults.image with
                          Src = Binding.Static(Some "/a.png")
                          Alt = TextSource.Literal "Alt" }

              let html = renderHtml BindingResolver.empty node

              Expect.isTrue
                  (html.EndsWith(
                      "><img class=\"fuaran-image\" src=\"/a.png\" alt=\"Alt\"></div>",
                      System.StringComparison.Ordinal
                  ))
                  (sprintf "an uncaptioned image emits no wrapper element of any kind — got %s" html)
          }

          // The caption is a `TextSource`, not a string, and this is the test
          // that makes that load-bearing rather than incidental: the `I18n`
          // case must reach the `<figcaption>` through the SAME `renderText`
          // every other text slot uses — placeholder substitution included.
          // A host that special-cased captions would pass the literal fixture
          // above and fail here.
          test "a caption resolves through TextSource — the I18n case, args and all" {
              let sources =
                  { BindingResolver.empty with
                      I18n = Map.ofList [ "gallery.caption.harbour", "Le port au lever du jour ({year})" ] }

              let node =
                  Fuaran.imageSpec
                      "imgi"
                      { Defaults.image with
                          Src = Binding.Static(Some "/harbour.jpg")
                          Alt = TextSource.Literal "Fishing boats moored at first light"
                          Caption =
                              Some(
                                  TextSource.I18n(
                                      "gallery.caption.harbour",
                                      Map.ofList [ "year", Fuaran.Core.JInt 1908 ]
                                  )
                              ) }

              let html = renderHtml sources node

              Expect.isTrue
                  (contains
                      "<figcaption class=\"fuaran-image-figure-caption\">Le port au lever du jour (1908)</figcaption>"
                      html)
                  "the I18n caption resolves through the catalog with its args substituted"
          }

          // ── Phase 1080 — the srcSet ───────────────────────────────────────
          //
          // The sanitisation proof, and the one test this phase most needed to
          // exist. `srcset` is a list of URLs the browser fetches with NO user
          // act, which is the exact class the render-time URL floor exists for
          // — so a slot that routed only the primary `src` through it would be
          // a documented way around the one rule this node has.
          //
          // The assertion is deliberately in TWO parts, because either alone
          // is passable by a broken renderer. That the dangerous URL is absent
          // would pass on a renderer that neutered it to the refusal URL and
          // served it anyway; that the refusal URL is absent would pass on one
          // that emitted the `javascript:` URL raw. Together they say the only
          // thing worth saying: the candidate is GONE, and what remains is the
          // safe one at its own descriptor.
          test "every srcSet candidate passes the URL floor — a javascript: entry is dropped, not neutered" {
              let node =
                  Fuaran.imageSpec
                      "imgx"
                      { Defaults.image with
                          Src = Binding.Static(Some "/harbour.jpg")
                          Alt = TextSource.Literal "Harbour"
                          SrcSet =
                              [ { Src = Binding.Static(Some "/harbour-400.jpg")
                                  Width = 400 }
                                { Src = Binding.Static(Some "javascript:alert(1)")
                                  Width = 800 } ] }

              let html = renderHtml BindingResolver.empty node

              Expect.isFalse (contains "javascript:" html) "no srcSet candidate carries a javascript: URL"

              Expect.isFalse
                  (contains "fuaran-egress-refused 800w" html)
                  "a refused candidate is dropped from the list, not neutered into it"

              Expect.isTrue
                  (contains "srcset=\"/harbour-400.jpg 400w\"" html)
                  (sprintf "the surviving candidate is emitted alone, at its own descriptor — got %s" html)
          }

          // An empty `srcSet` emits NEITHER attribute. The byte pin above
          // ("an uncaptioned image emits the bare <img>") already forbids any
          // addition to that emission, so this test is the readable statement
          // of the same fact rather than a second guard — and it names the two
          // attributes, so a failure says which one leaked.
          test "an image with no srcSet candidates emits neither srcset nor sizes" {
              let node =
                  Fuaran.imageSpec
                      "imgn"
                      { Defaults.image with
                          Src = Binding.Static(Some "/a.png")
                          Alt = TextSource.Literal "Alt" }

              let html = renderHtml BindingResolver.empty node

              Expect.isFalse (contains "srcset" html) "no srcset attribute on an image with no candidates"

              Expect.isFalse
                  (contains "sizes=" html)
                  "no sizes attribute either — it describes a list that is not there"
          }

          // ── Phase 1079 — the expansion affordance ─────────────────────────
          //
          // The negative half, and the one this phase most needed. An
          // `expandable` image whose `src` the egress floor REFUSED must emit
          // no anchor: the `<img>`'s `src` collapses to the refusal URL because
          // an `<img>` must have one, but an anchor has no such obligation, and
          // `<a href="about:blank">` is precisely the dead control the design
          // exists to avoid. The image itself still renders, carrying its
          // refusal marker — the reader is simply not offered an expansion that
          // could not work.
          //
          // The assertion is in three parts, because each alone is passable by
          // a different broken renderer: that no anchor was emitted (a renderer
          // wrapping the refusal URL would fail it), that the marker attribute
          // is absent too (one that dropped the `<a>` but left the data
          // attribute on the `<img>` would leave an enhancement tier a target
          // with no href), and that the image and its refusal marker are still
          // there (one that dropped the whole node would pass the first two and
          // silently lose the picture).
          test "an expandable image whose src is refused emits no anchor at all" {
              let node =
                  Fuaran.imageSpec
                      "imgxr"
                      { Defaults.image with
                          Src = Binding.Static(Some "javascript:alert(1)")
                          Alt = TextSource.Literal "Harbour"
                          Expandable = true }

              let html = renderHtml BindingResolver.empty node

              Expect.isFalse (contains "<a " html) "no anchor is emitted around a refused source"

              Expect.isFalse
                  (contains "data-fuaran-expandable" html)
                  "and no expansion marker either — an enhancement must not find a target with no link"

              Expect.isTrue
                  (contains "fuaran-egress-refused" html)
                  (sprintf "the image itself still renders, carrying its refusal marker — got %s" html)
          }

          // The composition claim, stated as one byte pin because the ORDER is
          // the claim. `srcSet` candidates are renditions of the THUMBNAIL and
          // ride the `<img>`; the anchor's `href` is the primary `src`, the
          // FULL asset. A renderer that put the smallest candidate behind the
          // link would satisfy every class assertion in this file and defeat
          // the entire feature — the reader would click a thumbnail and be
          // shown a thumbnail.
          test "expandable + srcSet — the candidates ride the img, the full asset rides the href" {
              let node =
                  Fuaran.imageSpec
                      "imges"
                      { Defaults.image with
                          Src = Binding.Static(Some "/harbour.jpg")
                          Alt = TextSource.Literal "Harbour"
                          Expandable = true
                          SrcSet =
                              [ { Src = Binding.Static(Some "/harbour-400.jpg")
                                  Width = 400 } ] }

              let html = renderHtml BindingResolver.empty node

              Expect.isTrue
                  (contains "href=\"/harbour.jpg\"" html)
                  "the anchor points at the primary src — the full asset, never a candidate"

              Expect.isFalse
                  (contains "href=\"/harbour-400.jpg\"" html)
                  "a srcSet candidate never becomes the expansion target"

              Expect.isTrue
                  (contains "srcset=\"/harbour-400.jpg 400w\"" html)
                  (sprintf "the candidate list stays on the <img> inside the anchor — got %s" html)
          }

          // The icon-contract lock: the icon NAME rides the `data-icon`
          // attribute of an EMPTY hook element and must never appear as
          // visible text content anywhere in the HTML.
          test "icon names ride data-icon and never leak as text content" {
              for f in fixtures do
                  let html = renderHtml BindingResolver.empty f.Node

                  for token in f.Expected do
                      if token.StartsWith("data-icon=\"", System.StringComparison.Ordinal) then
                          let name = token.Substring("data-icon=\"".Length).TrimEnd('"')

                          Expect.isFalse
                              (contains (sprintf ">%s<" name) html)
                              (sprintf "%s: icon name '%s' must not appear as text content" f.Name name)
          } ]
