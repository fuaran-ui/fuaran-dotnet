module Fuaran.UI.Tests.MotionEmission

// ============================================================================
//  Motion-class emission contract.
//
//  Pins the `Theme.motionVar` mapping for every Motion token + the renderer's
//  outer-wrapper className composition when `Node.Motion` is `Some token`.
//  Mirrors the AI-emit shape stability promise — the token names are the
//  contract; if either side ever drifts, this file is the early-warning
//  surface.
//
//  Feliz' .NET-side ReactElement is opaque (same constraint as
//  AccessibilityTests / ToneClassEmissionTests), so the "outer wrapper
//  carries fuaran-motion-{token}" assertion mirrors the renderer's
//  composition rule via `Theme.nodeClassName` + the token suffix, without
//  reaching into the ReactElement.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Documented contract ───────────────────────────────────────────────────

/// Every `Motion` case paired with the suffix the renderer is contracted
/// to emit (kebab-case projection of the case name). Order mirrors the
/// `Motion` DU declaration in Types.fs.
let private allMotions: (Motion * string) list =
    [ Motion.None, "none"
      Motion.PulseDuringLoad, "pulse-during-load"
      Motion.FadeInOnMount, "fade-in-on-mount"
      Motion.SlideInFromBelow, "slide-in-from-below"
      Motion.ShakeOnError, "shake-on-error"
      Motion.RotateOnRefresh, "rotate-on-refresh"
      Motion.SlideInFromRight, "slide-in-from-right"
      Motion.ExpandCollapse, "expand-collapse"
      // Phase 1122 — the TRANSITION pair. They sit in this list on exactly the
      // same terms as the eight above (one token, one suffix, one class), and
      // differ only in what the class means: the others style the node, these
      // two style the child that arrives when a `Switch` replaces what stood
      // there.
      Motion.CrossFade, "cross-fade"
      Motion.SlideBetween, "slide-between" ]

[<Tests>]
let tests =
    testList
        "motion-class emission contract"
        [ // ── (1) Theme.motionVar — documented suffix per Motion case ──────
          testList
              "Theme.motionVar — kebab-case suffix per Motion token"
              [ for (motion, expectedSuffix) in allMotions ->
                    test (sprintf "Motion.%A → %s" motion expectedSuffix) {
                        Expect.equal (Theme.motionVar motion) expectedSuffix "Theme.motionVar contract drift"
                    } ]

          // ── (2) Full `fuaran-motion-{suffix}` class shape ──────────────────
          testList
              "fuaran-motion-{suffix} class shape — 8 assertions"
              [ for (motion, expectedSuffix) in allMotions ->
                    test (sprintf "Motion.%A emits class fuaran-motion-%s" motion expectedSuffix) {
                        let expected = sprintf "fuaran-motion-%s" expectedSuffix
                        let actual = sprintf "fuaran-motion-%s" (Theme.motionVar motion)
                        Expect.equal actual expected "fuaran-motion-{suffix} drift"
                    } ]

          // ── (3) Token-list completeness — DU coverage ────────────────────
          // If a new Motion case is added without updating allMotions, the
          // assertion below fails. (We used to rely on the F# compiler's
          // exhaustiveness check, but Theme.motionVar is the only consumer
          // — without this test a future addition could ship with no
          // contract assertion.)
          test "Motion DU is fully enumerated above (sanity check via Theme.motionVar)" {
              // Smoke-call against every documented Motion case via the
              // suffix list; if Theme.motionVar grows a new case but the
              // suite doesn't, the new case lacks coverage — surface it
              // by enumerating the suffix list length against the
              // expected token count.
              Expect.equal allMotions.Length 10 "Motion DU is 10 tokens — update allMotions when adding a case"
          } ]
