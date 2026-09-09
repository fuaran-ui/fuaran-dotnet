module Fuaran.UI.Renderer.Server.Tests.StrictCspTests

// ============================================================================
//  The strict-CSP render mode (Phase 1545), asserted in EMITTED BYTES.
//
//  The claim the mode makes is a claim about a document: served under
//  `style-src 'self' 'nonce-…'` with no `'unsafe-inline'`, nothing the renderer
//  emitted is blocked. Two things have to be true of the bytes for that to
//  hold, and both are checkable here:
//
//   1. no `style` ATTRIBUTE anywhere in the markup, and
//   2. no `<style>` ELEMENT without the render's nonce on it.
//
//  So the assertions are over the HTML string, not over the render context —
//  a context field says what was intended, and the browser reads the bytes.
//
//  ── The detectors are proven to go red ────────────────────────────────────
//  Both assertions are ABSENCE assertions, and an absence assertion is worth
//  exactly what its detector is worth: one that cannot match anything passes on
//  every input, forever, and reads as proof. The estate's standing
//  "verify the probe, not just the verdict" discipline is made executable here
//  by the go-red twin, which reintroduces ONE style-bearing site into an
//  otherwise-strict document and requires the detector to see it.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

// ─── The detectors ─────────────────────────────────────────────────────────

/// Every `style="…"` attribute in the markup. Anchored on the preceding
/// whitespace so it cannot match a substring of another attribute name
/// (`data-my-style="…"`), which is the failure mode that would make this
/// detector quietly under-count.
let private styleAttributes (html: string) : string list =
    Regex.Matches(html, "(?<=\\s)style=\"[^\"]*\"")
    |> Seq.cast<Match>
    |> Seq.map _.Value
    |> List.ofSeq

/// Every `<style …>` open tag that does NOT carry a `nonce` attribute.
///
/// It matches the open tag only, so a `</style>` close and the CSS text between
/// them are never mistaken for an element; and the nonce test is over the tag's
/// own attribute region rather than over the whole document, so a nonce on a
/// script elsewhere cannot make an un-nonced style element read as nonced.
let private unNoncedStyleElements (html: string) : string list =
    Regex.Matches(html, "<style\\b[^>]*>")
    |> Seq.cast<Match>
    |> Seq.map _.Value
    |> Seq.filter (fun tag -> not (tag.Contains("nonce=", StringComparison.Ordinal)))
    |> List.ofSeq

let private nonce = "r4nd0m-per-response"

let private strict = Csp.Strict nonce

// ─── The style-bearing corpus ──────────────────────────────────────────────
//
//  One tree per site that can emit a `style` attribute on this tier. It is
//  written out rather than derived, because the point is to name the seven
//  sites: a site added later without a `cspStyle` call is invisible to a
//  derived corpus and visible to a reader of this list.

let private leaf (id: string) : Node<obj> = Fuaran.markdown id "x"

let private box (id: string) (layout: BoxLayout) (children: Node<obj> list) : Node<obj> =
    Fuaran.box
        id
        { Children = children
          Heading = Option.None
          Layout = layout
          Role = BoxRole.Group
          KeepTogether = false
          BreakBefore = false }

let private styleBearingCorpus: (string * Node<obj>) list =
    [ "Box/Grid (track list + gap)",
      box "g1" (BoxLayout.Grid(3, Option.Some "1fr 2fr 1fr", Option.Some 12)) [ leaf "a" ]

      "Box/Grid (derived track list, no gap)", box "g2" (BoxLayout.Grid(4, Option.None, Option.None)) [ leaf "b" ]

      "Box/Masonry (column count + gap)", box "m1" (BoxLayout.Masonry(3, Option.Some 8)) [ leaf "c" ]

      "Box/Flex (gap)", box "f1" (BoxLayout.Flex(Orientation.Horizontal, false, Option.Some 16)) [ leaf "d" ]

      "SplitPanel (two pane weights)",
      Fuaran.splitPanel
          "s1"
          { Defaults.splitPanel<obj> with
              Weight = 0.3
              Children = [ leaf "e"; leaf "f" ] }

      "ScrollArea (both ceilings)",
      Fuaran.scrollArea
          "sc1"
          { Defaults.scrollArea<obj> with
              Children = [ leaf "g" ]
              MaxHeight = Option.Some 200
              MaxWidth = Option.Some 320 }

      "Progress (fill width)",
      Fuaran.progress
          "p1"
          { Defaults.progress with
              Fraction = Binding.Static(Some 0.42) } ]

/// The whole corpus as one document, which is also the case that exercises the
/// collector's deduplication and its walk order.
let private wholeCorpus: Node<obj> =
    box "root" BoxLayout.Auto (styleBearingCorpus |> List.map snd)

// ─── The wire-format reference corpus ──────────────────────────────────────

/// Walk up from the test assembly to the workspace `wire-format-fixtures/`
/// corpus. `None` in a bare single-repo clone — absence of the workspace
/// checkout is a statement about the checkout, not about the code, so it
/// degrades to a skip (the `ScalarSsrParityTests` posture, and for the reason
/// recorded there: a module-level throw takes the whole assembly down).
let private tryCorpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind () // Phase 1647 — the ONE resolver

let private corpusRoot = tryCorpusRoot ()

/// Every `nodes/` fixture that decodes, as `(fixture name, tree)`. A fixture
/// that does not decode is SKIPPED rather than failed: this module is not the
/// decoder's corpus lock (`Fuaran.UI.Tests` owns that), and a decode failure
/// here would report a CSP defect where there is a decoder one.
let private decodedFixtures () : (string * Node<obj>) list =
    match corpusRoot with
    | None -> []
    | Some root ->
        let dir = Path.Combine(root, "nodes")

        if not (Directory.Exists dir) then
            []
        else
            Directory.EnumerateFiles(dir, "*.json")
            |> Seq.sort
            |> Seq.choose (fun path ->
                match JsonDecode.decodeNodeObj (File.ReadAllText path) with
                | Ok node -> Some(Path.GetFileNameWithoutExtension path, node)
                | Error _ -> None)
            |> List.ofSeq

[<Tests>]
let tests =
    testList
        "StrictCsp"
        [

          // ── 1. The default mode does not move ─────────────────────────────
          //
          // The first thing to assert, because it is what every other consumer
          // of this renderer depends on: adding the mode changed no byte on the
          // path nobody opted into. Asserted against the SAME entry point pair
          // rather than against a golden file, so it stays true as the
          // renderer's output legitimately evolves.
          test "Permissive is byte-identical to the unmoded entry point" {
              for name, node in styleBearingCorpus do
                  let before = Render.renderWith Registry.empty BindingResolver.empty node

                  let after =
                      Render.renderWithCsp Csp.Permissive Registry.empty BindingResolver.empty node

                  Expect.equal after before (sprintf "%s renders identically under the Permissive posture" name)
          }

          test "Permissive still emits the style attribute it always did" {
              for name, node in styleBearingCorpus do
                  let html = Render.renderWith Registry.empty BindingResolver.empty node

                  Expect.isNonEmpty
                      (styleAttributes html)
                      (sprintf
                          "%s carries an inline style attribute under Permissive — if this fails the corpus has stopped naming style-bearing sites, and the strict assertions below are vacuous"
                          name)
          }

          // ── 2. The claim: zero style attributes, every <style> nonced ─────

          test "no style attribute survives strict mode, per site" {
              for name, node in styleBearingCorpus do
                  let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node

                  Expect.equal (styleAttributes html) [] (sprintf "%s emits no style attribute under Csp.Strict" name)
          }

          test "no style attribute survives strict mode over the whole corpus" {
              let html =
                  Render.renderWithCsp strict Registry.empty BindingResolver.empty wholeCorpus

              Expect.equal (styleAttributes html) [] "the composed document emits no style attribute"
              Expect.equal (unNoncedStyleElements html) [] "every style element in the document carries the nonce"

              Expect.stringContains html ("nonce=\"" + nonce + "\"") "the collected stylesheet carries the host's nonce"
          }

          test "the theme element carries the nonce under strict mode" {
              let themed =
                  Render.renderWithThemeAndCsp strict Defaults.theme BindingResolver.empty wholeCorpus

              Expect.equal (styleAttributes themed) [] "a themed strict document emits no style attribute"

              Expect.equal
                  (unNoncedStyleElements themed)
                  []
                  "both the theme block and the collected stylesheet carry the nonce"

              // Two elements, one nonce — the shape a host needs for a single
              // `style-src` directive.
              Expect.equal
                  (Regex.Matches(themed, "<style\\b").Count)
                  2
                  "a themed strict render emits exactly the theme block and the collected stylesheet"
          }

          test "the un-nonced theme element is what Permissive still emits" {
              let themed = Render.renderWithTheme Defaults.theme BindingResolver.empty wholeCorpus

              Expect.isNonEmpty
                  (unNoncedStyleElements themed)
                  "Permissive emits the theme block without a nonce — if this fails, the nonce detector above is asserting nothing"
          }

          // ── 3. The go-red twin ────────────────────────────────────────────
          //
          // The task's own words: "a go-red twin that reintroduces one site".
          // The document is otherwise a real strict render; ONE node is
          // rendered through the permissive path and spliced in, which is
          // exactly what a site that forgot to route through `cspStyle` would
          // produce. Both detectors must see their construct.
          test "the detectors go red when one site is reintroduced" {
              let strictHtml =
                  Render.renderWithCsp strict Registry.empty BindingResolver.empty wholeCorpus

              let reintroducedSite =
                  Render.renderWith
                      Registry.empty
                      BindingResolver.empty
                      (box "leak" (BoxLayout.Grid(2, Option.None, Option.Some 4)) [ leaf "z" ])

              let leaked = strictHtml + reintroducedSite

              Expect.isNonEmpty
                  (styleAttributes leaked)
                  "the style-attribute detector sees a single reintroduced inline style"

              let unNoncedElement = strictHtml + "<style>.x{color:red}</style>"

              Expect.isNonEmpty
                  (unNoncedStyleElements unNoncedElement)
                  "the un-nonced-element detector sees a single injected style element"

              // And the same detectors say nothing about the clean document —
              // otherwise the two assertions above would pass on any input.
              Expect.equal (styleAttributes strictHtml) [] "the clean strict document is clean"
              Expect.equal (unNoncedStyleElements strictHtml) [] "the clean strict document is clean"
          }

          // ── 4. Determinism ────────────────────────────────────────────────

          test "two strict renders of one tree are byte-identical" {
              let first =
                  Render.renderWithCsp strict Registry.empty BindingResolver.empty wholeCorpus

              let second =
                  Render.renderWithCsp strict Registry.empty BindingResolver.empty wholeCorpus

              Expect.equal second first "the generated class names are derived, never allocated"
          }

          test "a class is generated once however many nodes carry the rule" {
              // Two boxes with identical declarations and identical ids: the
              // class is a function of both, so this is the collector's
              // deduplication and nothing else.
              let duplicated =
                  box
                      "root"
                      BoxLayout.Auto
                      [ box "same" (BoxLayout.Flex(Orientation.Vertical, false, Option.Some 4)) [ leaf "a" ]
                        box "same" (BoxLayout.Flex(Orientation.Vertical, false, Option.Some 4)) [ leaf "b" ] ]

              let html =
                  Render.renderWithCsp strict Registry.empty BindingResolver.empty duplicated

              let expected = Csp.generatedClass "same" "flex" (Csp.Declarations.flex (Some 4))

              Expect.equal
                  (Regex.Matches(html, "\\." + Regex.Escape expected + "\\{").Count)
                  1
                  "the rule is written once"

              Expect.equal
                  (Regex.Matches(html, Regex.Escape expected).Count)
                  3
                  "…and referenced by both elements that carry it, plus the rule itself"
          }

          // ── 5. The generated class is the SHARED derivation ───────────────
          //
          // The client renderer computes its class from `Csp.Declarations` and
          // `Csp.generatedClass` — the same two functions this assertion calls.
          // So pinning the server's emitted class to that derivation is what
          // makes the hydration claim checkable from a tier that cannot run the
          // React renderer: if either tier stopped routing through the shared
          // builders, the class it emits stops matching this value.
          test "the emitted class is the shared derivation, per slot" {
              let cases =
                  [ "g1",
                    "grid",
                    Csp.Declarations.grid "1fr 2fr 1fr" (Some 12),
                    box "g1" (BoxLayout.Grid(3, Option.Some "1fr 2fr 1fr", Option.Some 12)) [ leaf "a" ]

                    "m1",
                    "masonry",
                    Csp.Declarations.masonry 3 (Some 8),
                    box "m1" (BoxLayout.Masonry(3, Option.Some 8)) [ leaf "c" ]

                    "f1",
                    "flex",
                    Csp.Declarations.flex (Some 16),
                    box "f1" (BoxLayout.Flex(Orientation.Horizontal, false, Option.Some 16)) [ leaf "d" ]

                    "sc1",
                    "scroll",
                    Csp.Declarations.scrollArea (Some 200) (Some 320),
                    Fuaran.scrollArea
                        "sc1"
                        { Defaults.scrollArea<obj> with
                            Children = [ leaf "g" ]
                            MaxHeight = Option.Some 200
                            MaxWidth = Option.Some 320 } ]

              for nodeId, slot, declarations, node in cases do
                  let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node
                  let expected = Csp.generatedClass nodeId slot declarations

                  Expect.stringContains html expected (sprintf "%s/%s carries the derived class" nodeId slot)

                  Expect.stringContains
                      html
                      ("." + expected + "{" + Csp.declarationText declarations + "}")
                      (sprintf "%s/%s writes the rule the class names" nodeId slot)
          }

          // ── 5b. The CROSS-HOST pins ──────────────────────────────────────
          //
          // A document is supposed to render the same on every conformant host,
          // and a generated class name is part of the document. These exact
          // strings are pinned by the TypeScript server renderer's own suite
          // (`packages/renderer-server/test/strictCsp.test.ts`) for the same
          // trees. They are written out rather than computed so that a drift in
          // either host's declaration spelling or hash reddens exactly ONE suite
          // and names the class it now produces — a computed expectation on both
          // sides would move together and prove nothing.
          test "the class names are the ones the other host derives" {
              let pins =
                  [ "fuaran-csp-c5f9115c",
                    box "g1" (BoxLayout.Grid(3, Option.Some "1fr 2fr 1fr", Option.Some 12)) [ leaf "a" ]

                    "fuaran-csp-6d6f75ea", box "m1" (BoxLayout.Masonry(3, Option.Some 8)) [ leaf "c" ]

                    "fuaran-csp-06499874",
                    box "f1" (BoxLayout.Flex(Orientation.Horizontal, false, Option.Some 16)) [ leaf "d" ]

                    "fuaran-csp-48acacb7",
                    Fuaran.splitPanel
                        "sp"
                        { Defaults.splitPanel<obj> with
                            Weight = 0.5
                            Children = [ leaf "e"; leaf "f" ] }

                    "fuaran-csp-5082b942",
                    Fuaran.scrollArea
                        "sc1"
                        { Defaults.scrollArea<obj> with
                            Children = [ leaf "g" ]
                            MaxHeight = Option.Some 200
                            MaxWidth = Option.Some 320 }

                    "fuaran-csp-4140a29a",
                    Fuaran.progress
                        "p1"
                        { Defaults.progress with
                            Fraction = Binding.Static(Some 0.42) } ]

              for expected, node in pins do
                  let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node

                  Expect.stringContains
                      html
                      expected
                      (sprintf "the cross-host pinned class %s is what this host derives" expected)
          }

          test "the split panel's two panes take different classes" {
              // Equal weights, one node id: only the slot discriminator keeps
              // them apart, and this is the case that proves it does.
              let node =
                  Fuaran.splitPanel
                      "sp"
                      { Defaults.splitPanel<obj> with
                          Weight = 0.5
                          Children = [ leaf "a"; leaf "b" ] }

              let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node

              let left = Csp.generatedClass "sp" "split-left" (Csp.Declarations.splitPane 0.5)
              let right = Csp.generatedClass "sp" "split-right" (Csp.Declarations.splitPane 0.5)

              Expect.notEqual left right "the two panes derive different class names"
              Expect.stringContains html left "the left pane carries its class"
              Expect.stringContains html right "the right pane carries its class"
          }

          // ── 6. The new raw-CSS sink refuses what it must ──────────────────
          //
          // A collected declaration lands in raw `<style>` CONTENT, which is
          // not escaped and which the HTML parser scans for `</style` before
          // any CSS parser sees it. The shared emission grammar denies `;`, `{`,
          // `}` and `\` — it does NOT deny `<`, and correctly so for an
          // attribute value. This sink adds that denial, and this is where the
          // addition is checked rather than asserted in prose.
          test "the collector refuses a value that could close the style element" {
              Expect.isFalse (Csp.isCollectableValue "1fr</style><script>alert(1)</script>") "a closing tag is refused"
              Expect.isFalse (Csp.isCollectableValue "red;background:url(x)") "a declaration break is refused"
              Expect.isFalse (Csp.isCollectableValue "red}") "a rule break is refused"
              Expect.isTrue (Csp.isCollectableValue "repeat(3, 1fr)") "an ordinary track list is collectable"

              Expect.isTrue
                  (Csp.isCollectableValue "clamp(1rem, 2vw, 3rem)")
                  "an unfamiliar CSS function is collectable"
          }

          test "a hostile track list reaches neither the attribute nor the stylesheet" {
              // The emission site already refuses this value (Phase 1523) and
              // marks the refusal; the assertion here is that strict mode adds
              // no new route for it.
              let node =
                  box
                      "hostile"
                      (BoxLayout.Grid(2, Option.Some "1fr;background:url(https://collector/?d=1)", Option.None))
                      [ leaf "a" ]

              let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node

              Expect.isFalse
                  (html.Contains("collector", StringComparison.Ordinal))
                  "the refused value is nowhere in the document"

              Expect.stringContains
                  html
                  Sanitize.cssRefusalAttribute
                  "and the refusal is marked on the element, exactly as it is under Permissive"
          }

          // ── 7. The wire-format reference corpus ──────────────────────────

          testList
              "reference corpus"
              [ test "every node fixture renders strict-clean" {
                    match corpusRoot with
                    | None ->
                        skiptest
                            "wire-format-fixtures/ not found walking up from the test assembly — this corpus sweep needs the workspace checkout (skipped in a bare single-repo clone)"
                    | Some _ ->
                        let fixtures = decodedFixtures ()

                        Expect.isNonEmpty
                            fixtures
                            "the corpus decoded at least one fixture — an empty sweep reports everything as clean"

                        // A fixture this tier cannot render AT ALL against empty
                        // binding sources is excluded — but only after the
                        // PERMISSIVE path has been shown to throw on it too, so
                        // the exclusion is provably about the fixture and never
                        // about the mode. They are collected and printed by
                        // name: not checked is not passed.
                        let unrenderable = ResizeArray<string * string>()
                        let mutable checked' = 0

                        for name, node in fixtures do
                            let permissive =
                                try
                                    Some(Render.render BindingResolver.empty node)
                                with ex ->
                                    unrenderable.Add(name, ex.GetType().Name)
                                    None

                            match permissive with
                            | None -> ()
                            | Some _ ->
                                checked' <- checked' + 1
                                let html = Render.renderWithCsp strict Registry.empty BindingResolver.empty node

                                Expect.equal
                                    (styleAttributes html)
                                    []
                                    (sprintf "fixture %s emits no style attribute under Csp.Strict" name)

                                Expect.equal
                                    (unNoncedStyleElements html)
                                    []
                                    (sprintf "fixture %s emits no un-nonced style element" name)

                        if unrenderable.Count > 0 then
                            printfn
                                "StrictCsp corpus sweep: %d fixture(s) NOT CHECKED because this tier throws on them under the ordinary permissive path too — %s"
                                unrenderable.Count
                                (String.Join(", ", unrenderable |> Seq.map (fun (n, e) -> n + " (" + e + ")")))

                        Expect.isGreaterThan checked' 0 "at least one corpus fixture was actually rendered and checked"
                } ] ]
