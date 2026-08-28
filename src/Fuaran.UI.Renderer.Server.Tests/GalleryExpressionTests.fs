module Fuaran.UI.Renderer.Server.Tests.GalleryExpressionTests

// ============================================================================
//  Gallery expression probe — what the SHIPPED vocabulary can and cannot say
//  about an artwork-gallery page.
//
//  This module is a GATE, not a regression corpus. It was written to answer one
//  question with emitted bytes rather than with an opinion: given `Box`'s
//  `Grid` layout mode and the image presentation slots (`fit` / `aspectRatio` /
//  `loading` / `srcSet` / `expandable` / `caption`), is a real artwork-gallery
//  page expressible without new vocabulary — and, specifically, is the
//  MASONRY (column-fill) layout expressible?
//
//  The answers it records:
//
//   1. **Uniform-thumbnail grid — EXPRESSIBLE, completely.** `expressibleUniform`
//      below is the whole page: a captioned, aspect-reserved, cover-cropped,
//      lazily-loaded, expandable, srcSet-bearing responsive grid. Every
//      requirement lands on a closed token; nothing reaches a style attribute
//      except the column count, which is the renderer's own emission.
//
//   2. **Masonry (column-fill) — WAS inexpressible; ADMITTED by Phase 1082 as
//      `LayoutMode.Masonry`.** See "The flip" below.
//
//  The near-miss is recorded too (`nearMissNaturalAspect`): a grid of
//  `AspectRatio.Natural` images is a real, shippable gallery — it is simply a
//  ROW-aligned one, where each row is as tall as its tallest picture. That is a
//  legitimate look, and for a gallery of similarly-proportioned works it is
//  arguably the better one. It is not masonry, and this module does not pretend
//  it is.
//
//  ── The flip (Phase 1082) — and the finding it produced ────────────────────
//
//  The gate above was written so that a phase making masonry reachable would
//  turn it RED, retiring the finding rather than leaving it standing as stale
//  prose. Phase 1082 made masonry reachable. **The gate stayed GREEN — all
//  seven tests passed, unchanged, against a tree where `LayoutMode.Masonry`
//  renders.** That is worth more than the red would have been, so it is
//  recorded rather than quietly fixed.
//
//  The reason is precise, and it is not that the probes were weak. Each probe
//  is sound for the route it names: probe A asserts the complete inline style
//  set of a `Grid` container, probe B asserts which CSS PROPERTY the template
//  escape reaches, probe C asserts that no per-item style channel exists. All
//  three remain TRUE after the admission, because masonry did not arrive
//  through any of them — it arrived as a fourth route the gate did not model,
//  a new case on the layout DU, which touches no `Grid` emission at all.
//
//  So the fault was in the SCOPE of the claim, not in the detectors: three
//  route-specific falsifiers were read as supporting a route-INDEPENDENT
//  conclusion ("masonry is inexpressible"). An enumeration of closed doors is
//  only a proof that a room is sealed if the enumeration is known to be
//  complete, and nothing here established that. This is the estate's
//  "verify the probe, not just the verdict" discipline seen from an angle it
//  had not been seen from before: the probes were verified, individually, and
//  the verdict still over-reached them.
//
//  The module is therefore rewritten rather than deleted. The three probes are
//  KEPT — they are now separation assertions, and they say something sharper
//  than they did: a `Grid` still emits no column-fill mechanism, so the two
//  modes did not blur into one another when the second arrived. Beside them sit
//  the POSITIVE assertions of the admitted case, which are what a future
//  removal or regression of masonry would go red against. The negative claim
//  that outran its evidence is gone.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// Rendered under the permissive egress policy for the same reason
/// `SsrParityTests` does: this module asks a question about the LAYOUT and
/// PRESENTATION vocabulary, and a failure here should never be ambiguous
/// between "the vocabulary changed" and "the egress policy changed".
let private renderHtml (node: Node<obj>) : string =
    Render.renderWithEgress Sanitize.permissiveEgress Registry.empty BindingResolver.empty node

// ─── The gallery page, built from the shipped vocabulary alone ──────────────

/// One artwork, with everything an artwork page actually needs said about it:
/// a described alternative text, a reserved box so the page does not reflow as
/// the bytes land, a crop rule for that box, deferred loading, the resolution
/// ladder, the full-size expansion, and the caption a gallery visitor reads.
let private artwork (id: string) (slug: string) (title: string) (aspect: ImageAspect) : Node<obj> =
    Fuaran.imageSpec
        id
        { Defaults.image with
            Src = Binding.Static(Some(sprintf "/art/%s-1200.jpg" slug))
            Alt = TextSource.Literal title
            AspectRatio = aspect
            Fit = ImageFit.Cover
            Loading = ImageLoading.Lazy
            Expandable = true
            Caption = Some(TextSource.Literal title)
            SrcSet =
                [ { Src = Binding.Static(Some(sprintf "/art/%s-400.jpg" slug))
                    Width = 400 }
                  { Src = Binding.Static(Some(sprintf "/art/%s-800.jpg" slug))
                    Width = 800 }
                  { Src = Binding.Static(Some(sprintf "/art/%s-1200.jpg" slug))
                    Width = 1200 } ] }

/// Target 1 — the uniform-thumbnail gallery. Three columns of square,
/// cover-cropped thumbnails. This is the shape the phase predicted was already
/// covered, and it is: `Grid.Cols` supplies the columns, `AspectRatio.Square`
/// supplies the uniformity, `Fit.Cover` supplies the crop.
let private expressibleUniform: Node<obj> =
    Fuaran.gridLayout
        "gallery"
        { Defaults.gridLayout<obj> with
            Cols = 3
            Children =
                [ artwork "a1" "harbour" "Harbour at dawn" ImageAspect.Square
                  artwork "a2" "quarry" "The quarry road" ImageAspect.Square
                  artwork "a3" "gannets" "Gannets over Boreray" ImageAspect.Square ] }

/// Target 2 — the masonry hang, expressible since Phase 1082. The same three
/// works at their natural proportions, packed by COLUMN instead of by row.
/// Deliberately built from `Defaults.masonryLayout` with only `Cols` and
/// `Children` set, because that is the whole authoring surface: there is no
/// `TemplateColumns` twin to reach for, which is what keeps the case bounded.
let private expressibleMasonry: Node<obj> =
    Fuaran.masonryLayout
        "gallery-masonry"
        { Defaults.masonryLayout<obj> with
            Cols = 3
            Children =
                [ artwork "m1" "harbour" "Harbour at dawn" ImageAspect.Natural
                  artwork "m2" "quarry" "The quarry road" ImageAspect.Natural
                  artwork "m3" "gannets" "Gannets over Boreray" ImageAspect.Natural ] }

/// The near-miss — the same grid with each work at its NATURAL proportions.
/// Emitted deliberately so the difference from the uniform page is a byte
/// difference (no `fuaran-image-aspect-*` class) rather than a described one.
let private nearMissNaturalAspect: Node<obj> =
    Fuaran.gridLayout
        "gallery-natural"
        { Defaults.gridLayout<obj> with
            Cols = 3
            Children =
                [ artwork "n1" "harbour" "Harbour at dawn" ImageAspect.Natural
                  artwork "n2" "quarry" "The quarry road" ImageAspect.Natural
                  artwork "n3" "gannets" "Gannets over Boreray" ImageAspect.Natural ] }

// ─── Tests ─────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "GalleryExpression (Phase 1082 gate)"
        [
          // ── 1. The uniform grid: EXPRESSIBLE ────────────────────────────
          //
          // Asserted as one page rather than as six per-slot fixtures on
          // purpose. The per-slot vocabulary is already pinned by
          // `SsrParityTests`; what was unproven — and what the gate asked — is
          // that the slots COMPOSE on one node without any of them displacing
          // another. A caption wrapper that swallowed the aspect class, or an
          // expansion anchor that dropped the srcset, would pass every existing
          // fixture and fail here.
          test "uniform-thumbnail gallery is fully expressible" {
              let html = renderHtml expressibleUniform

              // The container: a real CSS grid at the declared column count.
              Expect.isTrue (contains "fuaran-layout-grid" html) "grid container class"

              Expect.isTrue
                  (contains "grid-template-columns:repeat(3, 1fr)" html)
                  "column count rides the renderer's own inline emission"

              // The uniformity: every cell is a reserved square box, cropped.
              Expect.equal
                  (System.Text.RegularExpressions.Regex.Matches(html, "fuaran-image-aspect-square").Count)
                  3
                  "every work reserves a square box before its bytes arrive"

              Expect.equal
                  (System.Text.RegularExpressions.Regex.Matches(html, "fuaran-image-fit-cover").Count)
                  3
                  "every work is cropped to fill that box"

              // The page-weight discipline: deferred loading + a resolution
              // ladder, both on every work.
              Expect.equal
                  (System.Text.RegularExpressions.Regex.Matches(html, "loading=\"lazy\"").Count)
                  3
                  "every work below the fold defers"

              Expect.isTrue
                  (contains
                      "srcset=\"/art/harbour-400.jpg 400w, /art/harbour-800.jpg 800w, /art/harbour-1200.jpg 1200w\""
                      html)
                  "the resolution ladder is emitted ascending"

              // The gallery affordances: a real caption, and a real link to the
              // full-size asset that works without script.
              Expect.isTrue
                  (contains "<figcaption class=\"fuaran-image-figure-caption\">Harbour at dawn</figcaption>" html)
                  "the work is captioned"

              Expect.isTrue
                  (contains "<a class=\"fuaran-image-expand\" href=\"/art/harbour-1200.jpg\"" html)
                  "the work expands to its full-size asset via a plain anchor"
          }

          // The composition claim above needs its own falsifier: assert the
          // nesting, so a renderer that emitted the anchor and the figure as
          // siblings — carrying every class this module checks — is caught.
          test "the gallery cell's structure composes rather than collides" {
              let html = renderHtml expressibleUniform

              Expect.isTrue
                  (contains
                      "<figure class=\"fuaran-image-figure\"><a class=\"fuaran-image-expand\" href=\"/art/harbour-1200.jpg\""
                      html)
                  "figure wraps anchor wraps img, with the caption outside the link"
          }

          // ── 2. Masonry: EXPRESSIBLE (Phase 1082) ────────────────────────
          //
          // The positive record of the admitted case. These are the assertions
          // a removal or a regression of masonry now goes red against — the
          // role the three probes below were mistakenly read as filling.
          test "masonry gallery is expressible, and emits the column-fill mechanism" {
              let html = renderHtml expressibleMasonry

              Expect.isTrue (contains "fuaran-layout-masonry" html) "masonry container class"

              // The mechanism itself, asserted as the emitted DECLARATION
              // rather than as the class alone: the class could be styled by
              // any host stylesheet, whereas `column-count` is the renderer's
              // own instruction and is what WIRE_FORMAT §3.6.7 makes normative.
              Expect.isTrue (contains "column-count:3" html) "the declared column count rides the inline emission"

              // The complete inline style set, on the same pattern probe A uses
              // for the grid: one layout instruction and nothing else, so this
              // goes red the moment the container learns to say anything more.
              let styles =
                  System.Text.RegularExpressions.Regex.Matches(html, "style=\"([^\"]*)\"")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> List.ofSeq

              Expect.equal styles [ "column-count:3" ] "one layout instruction, and it is the column count"

              // It is a MASONRY, not a grid wearing a new name: the row-fill
              // track property must be absent, or the two modes have blurred.
              Expect.isFalse (contains "grid-template-columns" html) "no row-fill track template"

              // The works themselves still compose exactly as they do in the
              // grid — the layout mode changed, the cell vocabulary did not.
              Expect.equal
                  (System.Text.RegularExpressions.Regex.Matches(html, "fuaran-image-fit-cover").Count)
                  3
                  "every work is still cropped to its box"

              Expect.isTrue
                  (contains "<figcaption class=\"fuaran-image-figure-caption\">Harbour at dawn</figcaption>" html)
                  "…and still captioned"
          }

          // The gap slot, which is the case's only other field. Asserted
          // separately because it is omitted-when-None: a container with no gap
          // must emit the column count ALONE, so the two shapes are different
          // byte strings and a renderer that always emitted a gap would be
          // caught here rather than in the corpus.
          test "masonry gap is emitted only when declared" {
              // Built by rewriting the layout on an authored node rather than
              // through the smart constructor, because `gap` is deliberately
              // NOT on the authoring surface — `Fuaran.masonryLayout` passes
              // `None` exactly as `Fuaran.gridLayout` does. It is wire
              // vocabulary a decoded tree can carry and a hand-authored F# tree
              // reaches only this way, and that asymmetry is inherited from
              // `Grid` on purpose rather than fixed here.
              let withGap =
                  let authored =
                      Fuaran.masonryLayout
                          "m-gap"
                          { Defaults.masonryLayout<obj> with
                              Cols = 4
                              Children = [ artwork "g1" "harbour" "Harbour at dawn" ImageAspect.Natural ] }

                  match authored.Kind with
                  | NodeKind.Box spec ->
                      { authored with
                          Kind =
                              NodeKind.Box
                                  { spec with
                                      Layout = BoxLayout.Masonry(4, Some 16) } }
                  | _ -> failtest "masonryLayout must build a Box"

              let html = renderHtml withGap

              Expect.isTrue (contains "column-count:4" html) "the declared column count"
              Expect.isTrue (contains "gap:16px" html) "…and the declared gap"

              // The no-gap shape, for contrast — the assertion that makes the
              // one above about `gap` rather than about rendering at all.
              Expect.isFalse (contains "gap:" (renderHtml expressibleMasonry)) "a gap-free masonry emits no gap"
          }

          // ── 3. The three original probes, KEPT as separation assertions ──
          //
          // Each remains TRUE after the admission, and that is now the point:
          // `Grid` did not acquire a column-fill mechanism when `Masonry`
          // arrived, so the two modes stayed distinct. Read them as "the grid
          // is still the grid", never again as "masonry is unreachable" — the
          // module header records why that second reading was never sound.
          //
          // Probe A — the typed route. `LayoutMode.Grid` carries a column COUNT
          // and nothing about fill direction, so the emitted page instructs the
          // browser in `grid-template-columns` alone. Column-fill needs one of
          // three CSS mechanisms the tree cannot reach: multi-column
          // (`columns` / `column-count`), a per-item row span computed from the
          // work's natural height (`grid-row: span N`), or `grid-template-rows:
          // masonry`. This asserts all three are absent — and, since the only
          // layout instruction emitted is the column template, that their
          // absence is structural rather than incidental.
          test "masonry probe A — the typed grid emits no column-fill mechanism" {
              let html = renderHtml expressibleUniform

              Expect.isFalse (contains "column-count" html) "no multi-column count"

              // Anchored, because the naive substring `columns:` is a SUBSTRING
              // of `grid-template-columns:` — the first draft of this probe
              // reported a masonry mechanism that was the ordinary column
              // template. A declaration starts at a `"`, a `;` or a space.
              Expect.isFalse
                  (System.Text.RegularExpressions.Regex.IsMatch(html, "(^|[\";\\s])columns\\s*:"))
                  "no multi-column shorthand"

              Expect.isFalse (contains "grid-row" html) "no per-item row span"
              Expect.isFalse (contains "masonry" html) "no masonry track keyword"

              // The positive half of the same claim: the ONLY layout property
              // the container emits is the column template. A probe that merely
              // listed absent strings would stay green if a fourth mechanism
              // were invented; this one goes red the moment the container
              // learns to say anything else.
              let styles =
                  System.Text.RegularExpressions.Regex.Matches(html, "style=\"([^\"]*)\"")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> List.ofSeq

              Expect.equal
                  styles
                  [ "grid-template-columns:repeat(3, 1fr)" ]
                  "one layout instruction, and it is the column template"
          }

          // Probe B — the escape route. `GridLayoutSpec.TemplateColumns` is a
          // verbatim CSS string and is the vocabulary's one layout escape
          // hatch, so the honest question is whether masonry is reachable
          // THROUGH it. It is not, and not for want of trying: the string is
          // emitted as the VALUE of `grid-template-columns`, so every masonry
          // mechanism is a different PROPERTY and unreachable by construction.
          // Asserting the property name is what makes that a fact about the
          // seam rather than about the string chosen here.
          test "masonry probe B — the template escape reaches one property only" {
              let templated =
                  Fuaran.gridLayoutTemplated
                      "gallery-templated"
                      "repeat(auto-fill, minmax(14rem, 1fr))"
                      { Defaults.gridLayout<obj> with
                          Children = [ artwork "t1" "harbour" "Harbour at dawn" ImageAspect.Natural ] }

              let html = renderHtml templated

              // The escape does buy something real — content-driven responsive
              // COLUMNS, which `Cols: int` cannot express.
              Expect.isTrue
                  (contains "grid-template-columns:repeat(auto-fill, minmax(14rem, 1fr))" html)
                  "the verbatim sizing function is emitted"

              // But it buys only that. The value cannot become a second
              // declaration: the renderer writes `grid-template-columns:` and
              // the string follows it.
              Expect.isFalse (contains "column-count" html) "the escape cannot reach column-count"
              Expect.isFalse (contains "grid-template-rows" html) "the escape cannot reach the row track"
          }

          // Probe C — the per-item route. Column-fill can also be faked by
          // giving each cell a computed row span, which needs a per-ITEM style.
          // `ExtraAttributes` is the only per-node attribute channel, and it
          // refuses `style` outright — so this is the third and last route,
          // closed. (It is also not on the wire at all, so a decoded tree could
          // not carry it even if the renderer accepted it.)
          test "masonry probe C — no per-item style channel exists" {
              let styled =
                  { artwork "s1" "harbour" "Harbour at dawn" ImageAspect.Natural with
                      ExtraAttributes = Some(Map.ofList [ "style", "grid-row: span 2" ]) }

              let html = renderHtml styled

              Expect.isFalse (contains "grid-row" html) "a hand-authored per-item style is dropped, not emitted"
              Expect.isFalse (contains "span 2" html) "…and nothing of it survives"
          }

          // ── 4. The near-miss, recorded rather than argued ────────────────
          //
          // `AspectRatio.Natural` gives each work its own proportions inside a
          // row-fill grid. That is the closest the shipped vocabulary gets to a
          // masonry look, and the byte difference from the uniform page is
          // exactly one thing: no reserved-box class. Worth pinning, because
          // the temptation when reading the finding later will be to remember
          // this as "nearly masonry" — it is not; the rows still align.
          test "near-miss — natural proportions in a row-fill grid" {
              let html = renderHtml nearMissNaturalAspect

              Expect.isFalse
                  (contains "fuaran-image-aspect-" html)
                  "no box is reserved — each work keeps its own proportions"

              Expect.isTrue (contains "grid-template-columns:repeat(3, 1fr)" html) "…inside the same row-fill grid"

              // The structural fact that makes it NOT masonry: the works are
              // emitted in authored order into a row-fill container, so a tall
              // work pushes its whole ROW down. Masonry's defining behaviour is
              // that it does not — and nothing in this emission distinguishes
              // one cell's height from another's.
              let firstAlt =
                  html.IndexOf("alt=\"Harbour at dawn\"", System.StringComparison.Ordinal)

              let secondAlt =
                  html.IndexOf("alt=\"The quarry road\"", System.StringComparison.Ordinal)

              let thirdAlt =
                  html.IndexOf("alt=\"Gannets over Boreray\"", System.StringComparison.Ordinal)

              Expect.isTrue
                  (firstAlt < secondAlt && secondAlt < thirdAlt)
                  "document order is authored order — the grid fills by row"
          }

          // ── 5. The probes' own go-red self-test ─────────────────────────
          //
          // Every masonry probe above is an ABSENCE assertion, and an absence
          // assertion is worth exactly what its detector is worth: one that
          // cannot match anything passes on every input, forever, and reads as
          // proof. The first draft of probe A had the opposite defect — its
          // `columns:` substring matched `grid-template-columns:` and reported a
          // masonry mechanism that was not there — which is the same class of
          // fault seen from the other side.
          //
          // So each detector is run here against a planted positive. This is
          // the estate's standing "verify the probe, not just the verdict"
          // discipline made executable: if a detector is ever weakened to the
          // point where it cannot see the construct it names, this test fails
          // and the probe's silence stops being evidence.
          test "the masonry detectors can go red" {
              let plantedShorthand = "<div style=\"columns: 3 14rem\">"
              let plantedCount = "<div style=\"column-count:3\">"
              let plantedSpan = "<img style=\"grid-row:span 2\">"
              let plantedTrack = "<div style=\"grid-template-rows:masonry\">"

              Expect.isTrue
                  (System.Text.RegularExpressions.Regex.IsMatch(plantedShorthand, "(^|[\";\\s])columns\\s*:"))
                  "the anchored shorthand detector sees a real multi-column declaration"

              Expect.isFalse
                  (System.Text.RegularExpressions.Regex.IsMatch(
                      "<div style=\"grid-template-columns:repeat(3, 1fr)\">",
                      "(^|[\";\\s])columns\\s*:"
                  ))
                  "…and does not fire on the ordinary column template"

              Expect.isTrue (contains "column-count" plantedCount) "the count detector sees a planted column-count"
              Expect.isTrue (contains "grid-row" plantedSpan) "the row-span detector sees a planted span"
              Expect.isTrue (contains "masonry" plantedTrack) "the track detector sees a planted masonry keyword"
          } ]
