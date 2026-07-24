module Fuaran.UI.StyleObserver.Tests.FlagDerivationTests

// ─── Per-flag derivation rules + the composite walk + WCAG ──────
//
// Acceptance criteria covered:
//   - The effective-background composite walk produces correct
//     contrast against a translucent stack (fixture-pinned).
//   - WCAG contrast derivation against known pairs.
//   - InvisibleText / ContrastBelowAA / AccentIndistinct boundaries.
//   - encode round-trips mirroring LayoutFlag / LayoutObservation.
//
// These exercise the shared `Flags` derivation seam directly — no
// observer, no debounce, no subscribers. Just "given this evidence,
// does the right flag come out".

open Expecto
open Fuaran.UI.StyleObserver

let private opts = StyleObserverOptions.defaults

[<Tests>]
let tests =
    testList
        "StyleFlagDerivation"
        [

          // ─── Compositing ────────────────────────────────────────

          test "effectiveBackground composites a translucent layer over the implicit white canvas" {
              // 50%-black over transparent over (implicit) white →
              // mid-grey 127.5. The real `rgba(--ink) over transparent
              // card` case from the design bundles.
              let layers =
                  [ Rgba.rgba 0.0 0.0 0.0 0.5 // element's own 50% black tint
                    Rgba.transparent ] // transparent card

              let bg = Flags.effectiveBackground layers
              Expect.floatClose Accuracy.medium bg.R 127.5 "R composites to mid-grey"
              Expect.floatClose Accuracy.medium bg.G 127.5 "G composites to mid-grey"
              Expect.floatClose Accuracy.medium bg.B 127.5 "B composites to mid-grey"
              Expect.floatClose Accuracy.medium bg.A 1.0 "result is opaque"
          }

          test "effectiveBackground stops at the first opaque ancestor (deeper layers discarded)" {
              // Opaque red sits below a 50% white; layers below the red
              // are invisible and must not affect the result.
              let layers =
                  [ Rgba.rgba 255.0 255.0 255.0 0.5
                    Rgba.rgb 200.0 0.0 0.0 // opaque — base
                    Rgba.rgb 0.0 0.0 255.0 ] // below opaque — discarded

              let bg = Flags.effectiveBackground layers
              // 50% white over opaque (200,0,0): R = 255*.5 + 200*.5 = 227.5
              Expect.floatClose Accuracy.medium bg.R 227.5 "R = white-over-red"
              Expect.floatClose Accuracy.medium bg.G 127.5 "G = white-over-red"
              Expect.floatClose Accuracy.medium bg.B 127.5 "B = white-over-red"
          }

          test "empty background-layer stack resolves to the white canvas" {
              let bg = Flags.effectiveBackground []
              Expect.equal bg Rgba.white "no layers → implicit white"
          }

          // ─── WCAG contrast ──────────────────────────────────────

          test "contrastRatio black-on-white is 21:1" {
              Expect.floatClose Accuracy.medium (Flags.contrastRatio Rgba.black Rgba.white) 21.0 "max contrast"
          }

          test "contrastRatio identical colours is 1:1" {
              Expect.floatClose Accuracy.medium (Flags.contrastRatio Rgba.white Rgba.white) 1.0 "min contrast"
          }

          // ─── InvisibleText ──────────────────────────────────────

          test "InvisibleText fires when foreground equals background" {
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.white
                      BackgroundLayers = [ Rgba.white ] }

              match Flags.invisibleText opts.InvisibleTextThreshold input with
              | Some(StyleFlag.InvisibleText r) -> Expect.floatClose Accuracy.medium r 1.0 "ratio ≈ 1.0"
              | other -> failwithf "expected InvisibleText, got %A" other
          }

          test "InvisibleText does NOT fire for legible text" {
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.black
                      BackgroundLayers = [ Rgba.white ] }

              Expect.equal (Flags.invisibleText opts.InvisibleTextThreshold input) None "21:1 is not invisible"
          }

          // ─── ContrastBelowAA ────────────────────────────────────

          test "ContrastBelowAA fires in the band [invisible, AA)" {
              // mid-grey (150) on white ≈ 2.96:1 — below the 4.5 AA
              // floor but well above the invisible threshold.
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.rgb 150.0 150.0 150.0
                      BackgroundLayers = [ Rgba.white ] }

              match Flags.contrastBelowAA opts.InvisibleTextThreshold opts.ContrastAAThreshold input with
              | Some(StyleFlag.ContrastBelowAA r) ->
                  Expect.isLessThan r 4.5 "below AA"
                  Expect.isGreaterThan r 1.1 "above invisible"
              | other -> failwithf "expected ContrastBelowAA, got %A" other
          }

          test "ContrastBelowAA does NOT fire when text passes AA" {
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.black
                      BackgroundLayers = [ Rgba.white ] }

              Expect.equal
                  (Flags.contrastBelowAA opts.InvisibleTextThreshold opts.ContrastAAThreshold input)
                  None
                  "21:1 passes AA"
          }

          test "InvisibleText and ContrastBelowAA partition the contrast axis (never both)" {
              // An invisible case must produce InvisibleText only.
              let invisible =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.white
                      BackgroundLayers = [ Rgba.white ] }

              let flags = Flags.derive opts invisible
              Expect.equal (List.length flags) 1 "exactly one legibility flag"

              match flags with
              | [ StyleFlag.InvisibleText _ ] -> ()
              | other -> failwithf "expected [InvisibleText], got %A" other
          }

          // ─── AccentIndistinct ───────────────────────────────────

          test "AccentIndistinct fires when a toned tint is indistinct from its surface" {
              // Toned element with a faint near-white tint over white →
              // the accent surface is indistinguishable from its container.
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.black
                      EmittedTone = Some "brand"
                      BackgroundLayers = [ Rgba.rgb 240.0 240.0 240.0; Rgba.white ] }

              match Flags.accentIndistinct opts.AccentIndistinctThreshold input with
              | Some(StyleFlag.AccentIndistinct r) -> Expect.isLessThan r 3.0 "accent vs surface below UI floor"
              | other -> failwithf "expected AccentIndistinct, got %A" other
          }

          test "AccentIndistinct does NOT fire for a clearly distinct toned tint" {
              let input =
                  { Flags.StyleInput.baseline with
                      EmittedTone = Some "brand"
                      BackgroundLayers = [ Rgba.rgb 0.0 0.0 200.0; Rgba.white ] }

              Expect.equal
                  (Flags.accentIndistinct opts.AccentIndistinctThreshold input)
                  None
                  "blue-on-white accent is distinct"
          }

          test "AccentIndistinct does NOT fire when no tone was emitted" {
              let input =
                  { Flags.StyleInput.baseline with
                      EmittedTone = None
                      BackgroundLayers = [ Rgba.rgb 240.0 240.0 240.0; Rgba.white ] }

              Expect.equal
                  (Flags.accentIndistinct opts.AccentIndistinctThreshold input)
                  None
                  "untoned elements have no accent contract"
          }

          test "AccentIndistinct does NOT fire when the element has no own background tint" {
              let input =
                  { Flags.StyleInput.baseline with
                      EmittedTone = Some "brand"
                      BackgroundLayers = [ Rgba.transparent; Rgba.white ] }

              Expect.equal
                  (Flags.accentIndistinct opts.AccentIndistinctThreshold input)
                  None
                  "transparent own layer = no accent surface"
          }

          // ─── derive ordering ────────────────────────────────────

          test "derive produces deterministic order (legibility then accent)" {
              // Indistinct toned tint that is ALSO low-contrast for its
              // text → both a legibility flag and AccentIndistinct, in
              // order.
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.rgb 250.0 250.0 250.0 // near-white text
                      EmittedTone = Some "brand"
                      BackgroundLayers = [ Rgba.rgb 240.0 240.0 240.0; Rgba.white ] }

              let flags = Flags.derive opts input

              match flags with
              | [ StyleFlag.ContrastBelowAA _; StyleFlag.AccentIndistinct _ ]
              | [ StyleFlag.InvisibleText _; StyleFlag.AccentIndistinct _ ] -> ()
              | other -> failwithf "expected legibility-then-accent order, got %A" other
          }

          // ─── font-role classification ───────────────────────────

          test "fontRole classifies by family substring" {
              let role family =
                  Flags.fontRole
                      { Flags.StyleInput.baseline with
                          FontFamily = family }

              Expect.equal (role (Some "Courier New, monospace")) FontRole.Monospace "mono"
              Expect.equal (role (Some "Arial, sans-serif")) FontRole.SansSerif "sans"
              Expect.equal (role (Some "Georgia, serif")) FontRole.Serif "serif"
              Expect.equal (role (Some "Comic Whatever")) FontRole.Unknown "unclassifiable"
              Expect.equal (role None) FontRole.Unknown "absent"
          }

          // ─── encode round-trips ─────────────────────────────────

          test "StyleFlag.encode produces tagged-object JSON with invariant-culture ratio" {
              let original = System.Globalization.CultureInfo.CurrentCulture

              try
                  System.Globalization.CultureInfo.CurrentCulture <-
                      System.Globalization.CultureInfo.GetCultureInfo("de-DE")

                  Expect.equal
                      (StyleFlag.encode (StyleFlag.ContrastBelowAA 3.25))
                      "{\"kind\":\"ContrastBelowAA\",\"ratio\":3.25}"
                      "invariant decimal point + tagged shape"
              finally
                  System.Globalization.CultureInfo.CurrentCulture <- original
          }

          test "Rgba.encode produces compact camelCase JSON" {
              Expect.equal
                  (Rgba.encode (Rgba.rgba 127.5 0.0 255.0 0.5))
                  "{\"r\":127.50,\"g\":0.00,\"b\":255.00,\"a\":0.50}"
                  "compact colour shape"
          }

          test "StyleObservation.encode produces stable camelCase JSON" {
              let obs: StyleObservation =
                  { NodeId = "metric-1"
                    Foreground = Rgba.black
                    EffectiveBackground = Rgba.white
                    FontRole = FontRole.SansSerif
                    EmittedTone = Some "brand"
                    ContrastRatio = 21.0
                    Flags = [ StyleFlag.ContrastBelowAA 3.25 ] }

              Expect.equal
                  (StyleObservation.encode obs)
                  "{\"nodeId\":\"metric-1\",\"foreground\":{\"r\":0.00,\"g\":0.00,\"b\":0.00,\"a\":1.00},\"effectiveBackground\":{\"r\":255.00,\"g\":255.00,\"b\":255.00,\"a\":1.00},\"fontRole\":\"SansSerif\",\"emittedTone\":\"brand\",\"contrastRatio\":21.00,\"flags\":[{\"kind\":\"ContrastBelowAA\",\"ratio\":3.25}]}"
                  "full camelCase envelope"
          }

          test "StyleObservation.encode emits null for an absent tone" {
              let obs: StyleObservation =
                  { NodeId = "n"
                    Foreground = Rgba.black
                    EffectiveBackground = Rgba.white
                    FontRole = FontRole.Unknown
                    EmittedTone = None
                    ContrastRatio = 21.0
                    Flags = [] }

              Expect.stringContains (StyleObservation.encode obs) "\"emittedTone\":null" "None → null"
          } ]
