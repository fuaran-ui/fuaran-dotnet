module Fuaran.UI.Renderer.Server.Tests.A11yCorpusParityTests

// ============================================================================
//  The a11y projection, driven by the SHARED CORPUS (Phase 956).
//
//  `SsrParityTests`' Phase-951 block already asserts WHERE the projection
//  lands, but every node in it is hand-built in this repo — so it measures the
//  reference host against the reference host's own idea of the trait. The
//  Phase-955 fixture family is the oracle every host answers to: all six slots,
//  both role classes (a named lower-case `region` and a deliberately-cased
//  custom `doc-pageFooter`), both binding forms (Static and State), all three
//  `liveRegion` tokens, and both placement shapes.
//
//  Parity shape, taken from `ScalarSsrParityTests`: the Feliz CLIENT renderer
//  cannot render to an HTML string on .NET, so each case computes the client's
//  projection by calling the exact shared function the client's wrapper
//  dispatches through — `Accessibility.accessibilityAttributes`, which
//  `Render.fs` feeds into `prop.custom` — pins it to the expected pairs, and
//  then asserts the SERVER HTML carries the same pairs ON THE SAME ELEMENT. A
//  divergence on either side fails loudly.
//
//  The HTML assertions split at an element's OWN open tag (the 951 pattern).
//  A substring check over the whole markup cannot tell a `role="link"` on the
//  wrapper from one on the anchor, and that difference is the entire point:
//  assistive technology does not associate a role on a non-interactive
//  container with the interactive element inside it.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.Ordinal)

/// Walk up from the test assembly to the workspace corpus. `None` in a bare
/// single-repo clone or a worktree checked out elsewhere — the same
/// degrade-to-skip posture `ScalarSsrParityTests` records, and for the same
/// reason: a missing input is a statement about the checkout, not the code.
let private tryCorpusRoot () : string option =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            None
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Some(Path.Combine(dir.FullName, "wire-format-fixtures"))
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

let private root = tryCorpusRoot ()

let private corpusDir () : string =
    match root with
    | Some r -> r
    | None ->
        skiptest
            "wire-format-fixtures/ not found walking up from the test assembly — the Phase 955 a11y family needs the workspace checkout (skipped in a bare single-repo clone or a worktree elsewhere)"

let private decodeFixture (name: string) : Node<obj> =
    let json = File.ReadAllText(Path.Combine(corpusDir (), "nodes", name + ".json"))

    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "decode failed for %s: %A" name e

/// An element's own open tag — everything from `<tag` up to its first `>`.
let private openTagOf (tag: string) (html: string) =
    let from = html.Substring(html.IndexOf("<" + tag, StringComparison.Ordinal))
    from.Substring(0, from.IndexOf('>') + 1)

/// The wrapper's own open tag.
let private wrapperTag (html: string) =
    html.Substring(0, html.IndexOf('>') + 1)

/// One fixture's expectation.
///
/// `Element = None` means the projection stays on the wrapper `<div>`; `Some
/// tag` names the semantic element the kind body renders, which carries it
/// under D4.
type private A11yCase =
    {
        Fixture: string
        Element: string option
        /// The exact `(attr, value)` pairs the SHARED projection must produce —
        /// pinned in the wire's slot order, so a dropped slot fails as loudly as
        /// a wrong value.
        Expected: (string * string) list
        /// Attributes that must NOT appear on the carrying element.
        AbsentFromCarrier: string list
    }

let private cases =
    [
      // All six slots at once on an ordinary wrapper kind. `hidden` is an
      // explicit Static FALSE — distinct on the wire from omitted, and it must
      // emit nothing.
      { Fixture = "a11y-wrapper-all-slots"
        Element = None
        Expected =
          [ "aria-label", "Channel performance summary"
            "aria-labelledby", "a11y-wrapper-heading"
            "aria-describedby", "a11y-wrapper-note"
            "role", "region"
            "aria-live", "polite" ]
        AbsentFromCarrier = [ "aria-hidden" ] }

      // The State forms. `label` resolves through its declared `defaultValue`
      // with no host state (the Phase-629 default law); `hidden`'s default is
      // FALSE, so nothing is emitted. The custom role's CASE is carried
      // verbatim — the exact spelling a fold bug once rewrote — and `off` is a
      // real `liveRegion` token, not an absence.
      { Fixture = "a11y-wrapper-state-bound"
        Element = None
        Expected = [ "aria-label", "Site footer"; "role", "doc-pageFooter"; "aria-live", "off" ]
        AbsentFromCarrier = [ "aria-hidden" ] }

      { Fixture = "a11y-alert-assertive"
        Element = None
        Expected = [ "role", "alert"; "aria-live", "assertive" ]
        AbsentFromCarrier = [] }

      // D4 forwarding: the body IS the semantic element. The accessible name
      // OVERRIDES the visible "Read more".
      { Fixture = "a11y-link-labelled"
        Element = Some "a"
        Expected = [ "aria-label", "Read the 2026 annual report (PDF)" ]
        AbsentFromCarrier = [] }

      { Fixture = "a11y-button-named"
        Element = Some "button"
        Expected = [ "aria-label", "Refresh revenue figures"; "role", "button" ]
        AbsentFromCarrier = [] }

      // The decorative shape: empty alt + `hidden` Static TRUE — the slot two
      // hosts dropped entirely before the Phase 951 port.
      { Fixture = "a11y-image-decorative"
        Element = Some "img"
        Expected = [ "aria-hidden", "true" ]
        AbsentFromCarrier = [] } ]

[<Tests>]
let a11yCorpusParityTests =
    testList
        "a11y corpus projection parity (955/956)"
        [ for case in cases do
              test $"{case.Fixture} — the shared projection and the server HTML agree, on the right element" {
                  let node = decodeFixture case.Fixture

                  // ── The CLIENT side: the shared projection the Feliz wrapper
                  // feeds into `prop.custom`, asserted as an exact list so a
                  // dropped slot cannot pass.
                  let projected =
                      Accessibility.accessibilityAttributes BindingResolver.empty node.Accessibility

                  Expect.equal
                      projected
                      case.Expected
                      $"{case.Fixture}: the shared a11y projection must match the corpus fixture's slots exactly"

                  // ── The SERVER side: the same pairs, on the same element.
                  let html = Render.render BindingResolver.empty node
                  let wrapper = wrapperTag html

                  let carrier =
                      match case.Element with
                      | None -> wrapper
                      | Some tag -> openTagOf tag html

                  for (attr, value) in case.Expected do
                      Expect.isTrue
                          (contains $"{attr}=\"{value}\"" carrier)
                          $"{case.Fixture}: {attr} must land on the carrying element — got: {carrier}"

                  for attr in case.AbsentFromCarrier do
                      Expect.isFalse
                          (contains attr carrier)
                          $"{case.Fixture}: {attr} must not be emitted — got: {carrier}"

                  // A forwarding kind must not leave the projection behind.
                  match case.Element with
                  | None -> ()
                  | Some _ ->
                      for (attr, _) in case.Expected do
                          Expect.isFalse
                              (contains attr wrapper)
                              $"{case.Fixture}: {attr} leaked onto the wrapper — got: {wrapper}"

                  // The wrapper keeps the node's ADDRESS whichever element
                  // carries the projection.
                  Expect.isTrue
                      (contains $"data-fuaran-node-id=\"{node.Id}\"" wrapper)
                      $"{case.Fixture}: the wrapper must keep the node address — got: {wrapper}"
              }

          // A table-driven leg that silently enumerated nothing would be a gate
          // that checked nothing.
          test "the a11y corpus family is the full Phase 955 set" {
              Expect.equal (List.length cases) 6 "the Phase 955 node family is six fixtures"
          } ]
