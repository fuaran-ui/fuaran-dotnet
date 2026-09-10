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
open System.Text.Json
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
let private tryCorpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind () // Phase 1647 — the ONE resolver

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

/// One fixture's expectation, DERIVED from the corpus's own a11y contract
/// (Phase 1665).
///
/// This table used to be hand-written here — and the same table was hand-written
/// again in four other hosts. Five copies of one cross-host claim is exactly the
/// arrangement that let `Accessibility.label` resolve five different ways with
/// every conformance gate green: each host measured itself against its own idea
/// of the trait, and no copy could contradict another. The claim now lives once,
/// in `a11y-contract.json`'s `behaviour` section, and every host reads it.
///
/// What stays host-local is the one thing the contract deliberately does not
/// state: which ELEMENT this host renders for a forwarding kind. The contract
/// says the projection FORWARDS (the D4 predicate, host-neutral); the tag is
/// this renderer's own answer, and `forwardingTag` below is where it is given.
type private A11yCase =
    {
        Fixture: string
        /// The contract's `forwards` flag: false = the wrapper carries the
        /// projection, true = the kind's own semantic element does.
        Forwards: bool
        /// The exact `(attr, value)` pairs the SHARED projection must produce —
        /// in the wire's slot order, so a dropped slot fails as loudly as a
        /// wrong value.
        Expected: (string * string) list
        /// Attributes that must NOT appear on the carrying element. DERIVED: the
        /// contract declares its attribute list exhaustive for the projection,
        /// so every projection attribute the vector omits is one the carrier
        /// must not emit.
        AbsentFromCarrier: string list
    }

/// The six attribute names the accessibility projection can emit, in the wire's
/// slot order. The complement of a vector's own list is what that vector forbids.
let private projectionAttributes =
    [ "aria-label"
      "aria-labelledby"
      "aria-describedby"
      "role"
      "aria-live"
      "aria-hidden" ]

/// The element THIS host's body renders for each forwarding fixture's kind — the
/// host-local half of a contract vector (see `A11yCase` above). A forwarding
/// vector with no entry here fails loudly rather than falling back to the
/// wrapper: a silent fallback would assert the projection landed where the
/// contract says it must not.
let private forwardingTag =
    Map.ofList
        [ "a11y-link-labelled", "a"
          "a11y-button-named", "button"
          "a11y-image-decorative", "img" ]

/// Read the contract's behaviour vectors. `[]` in a checkout with no corpus —
/// the leg below turns that into a skip rather than a vacuous pass.
let private readVectors (corpusRoot: string) : A11yCase list =
    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot, "a11y-contract.json")))

    [ for v in doc.RootElement.GetProperty("behaviour").GetProperty("vectors").EnumerateArray() do
          let expected =
              [ for pair in v.GetProperty("attributes").EnumerateArray() -> pair[0].GetString(), pair[1].GetString() ]

          { Fixture = v.GetProperty("fixture").GetString()
            Forwards = v.GetProperty("forwards").GetBoolean()
            Expected = expected
            AbsentFromCarrier =
              projectionAttributes
              |> List.filter (fun a -> expected |> List.forall (fun (k, _) -> k <> a)) } ]

let private cases =
    match root with
    | Some r -> readVectors r
    | None -> []

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
                      if not case.Forwards then
                          wrapper
                      else
                          match Map.tryFind case.Fixture forwardingTag with
                          | Some tag -> openTagOf tag html
                          | None ->
                              failwithf
                                  "%s: the contract says the projection forwards, and this host has not said which element it renders for that kind - add it to `forwardingTag`"
                                  case.Fixture

                  for (attr, value) in case.Expected do
                      Expect.isTrue
                          (contains $"{attr}=\"{value}\"" carrier)
                          $"{case.Fixture}: {attr} must land on the carrying element — got: {carrier}"

                  for attr in case.AbsentFromCarrier do
                      Expect.isFalse
                          (contains attr carrier)
                          $"{case.Fixture}: {attr} must not be emitted — got: {carrier}"

                  // A forwarding kind must not leave the projection behind.
                  if case.Forwards then
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
          // that checked nothing — and since Phase 1665 the table is READ rather
          // than written here, so an empty one is also what a mis-shaped contract
          // looks like. Both are refused. The count is not restated: the contract
          // is the enumeration, exactly as `manifest.json` is for the fixtures.
          test "the a11y contract's behaviour vectors are present, and cover the Transform-bound name" {
              if root.IsNone then
                  skiptest
                      "wire-format-fixtures/ not found walking up from the test assembly — skipped in a bare single-repo clone"

              Expect.isNonEmpty cases "a11y-contract.json's `behaviour.vectors` must enumerate the a11y fixture family"

              Expect.isTrue
                  (cases |> List.exists (fun c -> c.Fixture = "a11y-wrapper-transform-label"))
                  "the contract must carry the Phase 1665 vector — the Transform-bound accessible name is the one every host resolved through its row-shaped generic path"
          } ]
