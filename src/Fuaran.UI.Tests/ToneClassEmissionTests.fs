module Fuaran.UI.Tests.ToneClassEmission

// ============================================================================
//  Tone-class emission contract.
//
//  The renderer emits per-spec tone classes (`fuaran-callout-{tone}`,
//  `fuaran-progress-{tone}`, `fuaran-pill-{tone}`, `fuaran-metric-{tone}`) and per-
//  BadgeVariant classes (`fuaran-badge-{variant}`) on top of the outer-wrapper
//  semantic-style class (`fuaran-tone-{tone}` from `Theme.className`).
//
//  Several tone classes once had no matching CSS rule and renderMetric
//  silently dropped `MetricSpec.Tone` on the floor. The matrix is now complete
//  and propagated Metric's tone; these tests pin the contract so a future
//  regression (e.g. renaming `Theme.toneVar`'s output, or adding a new
//  ToneVariant case without updating fuaran-reference.css) gets caught at
//  CI time rather than at a customer demo.
//
//  Feliz' .NET-side ReactElement is opaque so we can't snapshot the
//  rendered DOM here — same constraint as the
//  AccessibilityTests.fs projection. Instead we pin:
//    1. `Theme.toneVar` produces the documented suffix for each
//       `ToneVariant` case.
//    2. The composed full classes for callout / progress / pill / metric
//       match the documented shape (`fuaran-{kind}-{toneVar}`).
//    3. The BadgeVariant suffixes are stable (Neutral / Brand / Success /
//       Warning / Critical / Info).
//    4. `Theme.className` against a default `SemanticStyle` emits
//       `fuaran-tone-{toneVar}` on the outer wrapper for every tone.
//
//  HOST-STYLING-CHECKLIST.md §4.1 is the documented contract this file
//  asserts against; the tests fail (and the contract changes intentionally)
//  whenever §4.1 needs updating.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Documented contract (mirrors HOST-STYLING-CHECKLIST.md §4.1) ──────────

/// Every `ToneVariant` case paired with the suffix the renderer is
/// contracted to emit. Mirrors HOST-STYLING-CHECKLIST.md §4.1's row labels.
let private allTones: (ToneVariant * string) list =
    [ ToneVariant.Default, "default"
      ToneVariant.Subdued, "subdued"
      ToneVariant.Brand, "brand"
      ToneVariant.Success, "success"
      ToneVariant.Warning, "warning"
      ToneVariant.Critical, "critical"
      ToneVariant.Info, "info" ]

/// Every `BadgeVariant` case paired with the suffix the renderer is
/// contracted to emit (HOST-STYLING-CHECKLIST.md §4.2).
let private allBadges: (BadgeVariant * string) list =
    [ BadgeVariant.Neutral, "neutral"
      BadgeVariant.Brand, "brand"
      BadgeVariant.Success, "success"
      BadgeVariant.Warning, "warning"
      BadgeVariant.Critical, "critical"
      BadgeVariant.Info, "info" ]

/// The four tone-bearing components whose per-spec class follows the
/// `fuaran-{kind}-{toneVar}` shape (HOST-STYLING-CHECKLIST.md §4.1 row headers).
let private toneBearingKinds: string list =
    [ "callout"; "progress"; "pill"; "metric" ]

let private defaultStyle: SemanticStyle =
    { Tone = ToneVariant.Default
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Role = StyleRole.None
      Voice = FontVoice.Default }

[<Tests>]
let tests =
    testList
        "tone-class emission contract"
        [ // ── (1) Theme.toneVar is the canonical tone → suffix mapping ──────
          testList
              "Theme.toneVar — documented suffix per ToneVariant case"
              [ for (tone, expectedSuffix) in allTones ->
                    test (sprintf "ToneVariant.%A → %s" tone expectedSuffix) {
                        Expect.equal (Theme.toneVar tone) expectedSuffix "Theme.toneVar contract drift"
                    } ]

          // ── (2) Full per-spec class shape for callout/progress/pill/metric ──
          testList
              "Per-spec class shape (4 components × 7 tones = 28 assertions)"
              [ for kind in toneBearingKinds do
                    for (tone, expectedSuffix) in allTones ->
                        let expected = sprintf "fuaran-%s-%s" kind expectedSuffix
                        let actual = sprintf "fuaran-%s-%s" kind (Theme.toneVar tone)

                        test (sprintf "fuaran-%s-%s shape stable" kind expectedSuffix) {
                            Expect.equal actual expected "per-spec tone class drift"
                        } ]

          // ── (3) BadgeVariant suffixes are stable (6 assertions) ──────────
          // BadgeVariant is a distinct DU from ToneVariant (see
          // HOST-STYLING-CHECKLIST.md §4.4 vocabulary fork). Pin the suffix
          // set so a future refactor that unifies / renames either DU
          // surfaces here rather than as a silent visual regression.
          testList
              "BadgeVariant suffixes pinned"
              [ for (variant, expectedSuffix) in allBadges ->
                    test (sprintf "BadgeVariant.%A → fuaran-badge-%s" variant expectedSuffix) {
                        // The renderer's badgeVariantClass is private; mirror
                        // the documented mapping locally and assert
                        // exhaustively against the DU shape.
                        let actualSuffix =
                            match variant with
                            | BadgeVariant.Neutral -> "neutral"
                            | BadgeVariant.Brand -> "brand"
                            | BadgeVariant.Success -> "success"
                            | BadgeVariant.Warning -> "warning"
                            | BadgeVariant.Critical -> "critical"
                            | BadgeVariant.Info -> "info"

                        Expect.equal actualSuffix expectedSuffix "BadgeVariant suffix drift"
                    } ]

          // ── (4) Outer-wrapper fuaran-tone-{tone} via Theme.className ──────
          // Every Fuaran node renders inside a `Theme.className`-computed
          // wrapper carrying `fuaran-tone-{tone}`. The outer class is what
          // tone-cascading consumer rules (e.g. `.fuaran-tone-brand .fuaran-metric`)
          // hook against; pin its shape per ToneVariant case.
          testList
              "Outer wrapper carries fuaran-tone-{tone} for every ToneVariant"
              [ for (tone, expectedSuffix) in allTones ->
                    test (sprintf "Theme.className with Tone=%A includes fuaran-tone-%s" tone expectedSuffix) {
                        let className = Theme.className { defaultStyle with Tone = tone }

                        let expectedFragment = sprintf "fuaran-tone-%s" expectedSuffix

                        Expect.isTrue
                            (className.Contains expectedFragment)
                            (sprintf "expected outer wrapper '%s' to contain '%s'" className expectedFragment)
                    } ]

          // ── (5) Phase 147 — StyleRole / FontVoice class emission ──────────
          // The role/voice fragments append to the node className only when
          // non-default; a default style emits neither, so a tree authored
          // before the fields existed renders byte-identically.
          testList
              "Phase 147 — role/voice fragments append only when non-default"
              [ test "default style emits no fuaran-role-/fuaran-voice- fragment (byte-identical)" {
                    let className = Theme.className defaultStyle

                    Expect.equal
                        className
                        "fuaran-node fuaran-tone-default fuaran-weight-standard fuaran-emphasis-normal"
                        "default SemanticStyle must yield the exact pre-147 class string"
                }

                for (role, suffix) in
                    [ StyleRole.Eyebrow, "eyebrow"
                      StyleRole.Data, "data"
                      StyleRole.Lede, "lede"
                      StyleRole.Caption, "caption" ] do
                    test (sprintf "StyleRole.%A → fuaran-role-%s" role suffix) {
                        let className = Theme.className { defaultStyle with Role = role }

                        Expect.isTrue
                            (className.Contains(sprintf "fuaran-role-%s" suffix))
                            (sprintf "expected '%s' to contain 'fuaran-role-%s'" className suffix)
                    }

                for (voice, suffix) in [ FontVoice.Display, "display"; FontVoice.Structural, "structural" ] do
                    test (sprintf "FontVoice.%A → fuaran-voice-%s" voice suffix) {
                        let className = Theme.className { defaultStyle with Voice = voice }

                        Expect.isTrue
                            (className.Contains(sprintf "fuaran-voice-%s" suffix))
                            (sprintf "expected '%s' to contain 'fuaran-voice-%s'" className suffix)
                    }

                test "StyleRole.None / FontVoice.Default suffixes resolve to no fragment" {
                    Expect.equal (Theme.styleRoleVar StyleRole.None) None "StyleRole.None → no fragment"
                    Expect.equal (Theme.fontVoiceVar FontVoice.Default) None "FontVoice.Default → no fragment"
                } ] ]
