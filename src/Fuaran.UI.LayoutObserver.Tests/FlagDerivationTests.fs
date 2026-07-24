module Fuaran.UI.LayoutObserver.Tests.FlagDerivationTests

// ─── Per-flag derivation rules ──────────────────────────────────
//
// Acceptance criterion: each flag's predicate is
// individually testable against `Flags.LayoutInput`. These tests
// exercise the flag-derivation seam directly — no observer
// involved, no debounce timing, no subscriber callbacks. Just
// "given this input, does the right flag come out".
//
// Tests are deliberately small + named after the flag they exercise
// so a failure points straight at the derivation rule that
// regressed.

open Expecto
open Fuaran.UI.LayoutObserver

let private defaultOptions = LayoutObserverOptions.defaults

[<Tests>]
let tests =
    testList
        "FlagDerivation"
        [ test "overflowHorizontal fires when scrollWidth > clientWidth AND overflow-x clips" {
              let input =
                  { Flags.LayoutInput.empty 100.0 50.0 with
                      ScrollWidth = Some 250.0
                      ClientWidth = Some 100.0
                      OverflowX = Some "hidden" }

              Expect.equal (Flags.overflowHorizontal input) (Some LayoutFlag.OverflowHorizontal) "should flag"
          }

          test "overflowHorizontal does NOT fire when overflow-x is visible (no clip)" {
              let input =
                  { Flags.LayoutInput.empty 100.0 50.0 with
                      ScrollWidth = Some 250.0
                      ClientWidth = Some 100.0
                      OverflowX = Some "visible" }

              Expect.equal (Flags.overflowHorizontal input) None "visible-overflow elements don't clip"
          }

          test "overflowVertical fires when scrollHeight > clientHeight AND overflow-y clips" {
              let input =
                  { Flags.LayoutInput.empty 100.0 50.0 with
                      ScrollHeight = Some 500.0
                      ClientHeight = Some 50.0
                      OverflowY = Some "scroll" }

              Expect.equal (Flags.overflowVertical input) (Some LayoutFlag.OverflowVertical) "should flag"
          }

          test "zeroDimension fires per collapsed axis" {
              let input = Flags.LayoutInput.empty 0.0 50.0
              let flags = Flags.zeroDimension input
              Expect.equal flags [ LayoutFlag.ZeroDimension "width" ] "only width should fire"

              let bothZero = Flags.LayoutInput.empty 0.0 0.0

              Expect.equal
                  (Flags.zeroDimension bothZero)
                  [ LayoutFlag.ZeroDimension "width"; LayoutFlag.ZeroDimension "height" ]
                  "both axes should fire"
          }

          test "squeezedToMin fires when rendered dimension hits min-width / min-height" {
              let input =
                  { Flags.LayoutInput.empty 120.0 80.0 with
                      MinWidth = Some 120.0
                      MinHeight = Some 200.0 }

              let flags = Flags.squeezedToMin input
              Expect.equal flags [ LayoutFlag.SqueezedToMin "width" ] "only width hits its min"
          }

          test "childClippedByAncestor fires when bounding rect overflows clipping ancestor" {
              let input =
                  { Flags.LayoutInput.empty 200.0 100.0 with
                      ElementRect = (50.0, 50.0, 250.0, 150.0)
                      // Ancestor clips at x=200 so element's right edge (250) overflows.
                      ClippingAncestorRect = Some(0.0, 0.0, 200.0, 200.0) }

              Expect.equal (Flags.childClippedByAncestor input) (Some LayoutFlag.ChildClippedByAncestor) "should flag"
          }

          test "childClippedByAncestor does NOT fire when element fits inside ancestor" {
              let input =
                  { Flags.LayoutInput.empty 100.0 50.0 with
                      ElementRect = (10.0, 10.0, 110.0, 60.0)
                      ClippingAncestorRect = Some(0.0, 0.0, 200.0, 200.0) }

              Expect.equal (Flags.childClippedByAncestor input) None "element fits, no flag"
          }

          test "aspectRatioWildlyOff fires when observed/expected magnitude ≥ factorThreshold" {
              // Expected aspect 16:9 (~1.778). Observed 400/50 = 8.0 → magnitude 4.5x.
              let input =
                  { Flags.LayoutInput.empty 400.0 50.0 with
                      ExpectedAspectRatio = Some(16.0 / 9.0) }

              match Flags.aspectRatioWildlyOff defaultOptions.AspectRatioWildlyOffFactor input with
              | Some(LayoutFlag.AspectRatioWildlyOff f) ->
                  // 4.5x off — the carried factor should be ≥ 3.0 (the threshold).
                  Expect.isGreaterThanOrEqual f 3.0 "carried factor should reflect magnitude"
              | other -> failwithf "expected AspectRatioWildlyOff, got %A" other
          }

          test "aspectRatioWildlyOff does NOT fire below threshold" {
              // 320x200 = 1.6 ratio; expected 16:9 = 1.778; magnitude ~1.11.
              let input =
                  { Flags.LayoutInput.empty 320.0 200.0 with
                      ExpectedAspectRatio = Some(16.0 / 9.0) }

              Expect.equal
                  (Flags.aspectRatioWildlyOff defaultOptions.AspectRatioWildlyOffFactor input)
                  None
                  "1.11x is well within tolerance"
          }

          test "derive combines all rules and produces deterministic order" {
              // Construct an input that triggers OverflowHorizontal + ZeroDimension(height).
              let input =
                  { Flags.LayoutInput.empty 100.0 0.0 with
                      ScrollWidth = Some 250.0
                      ClientWidth = Some 100.0
                      OverflowX = Some "hidden" }

              let flags = Flags.derive defaultOptions input

              Expect.equal
                  flags
                  [ LayoutFlag.OverflowHorizontal; LayoutFlag.ZeroDimension "height" ]
                  "overflow first, then zero-dimension per the derive order"
          }

          test "LayoutFlag.encode produces tagged-object JSON for nullary cases" {
              Expect.equal
                  (LayoutFlag.encode LayoutFlag.OverflowHorizontal)
                  "{\"kind\":\"OverflowHorizontal\"}"
                  "nullary shape"
          }

          test "LayoutFlag.encode carries axis for ZeroDimension / SqueezedToMin" {
              Expect.equal
                  (LayoutFlag.encode (LayoutFlag.ZeroDimension "width"))
                  "{\"kind\":\"ZeroDimension\",\"axis\":\"width\"}"
                  "axis-tagged shape"
          }

          test "LayoutFlag.encode emits invariant-culture factor for aspect-ratio" {
              // Even on a German-locale build, the wire format must use
              // period as decimal separator (it's JSON, not csv).
              let originalCulture = System.Globalization.CultureInfo.CurrentCulture

              try
                  System.Globalization.CultureInfo.CurrentCulture <-
                      System.Globalization.CultureInfo.GetCultureInfo("de-DE")

                  Expect.equal
                      (LayoutFlag.encode (LayoutFlag.AspectRatioWildlyOff 3.25))
                      "{\"kind\":\"AspectRatioWildlyOff\",\"factor\":3.25}"
                      "invariant-culture decimal point"
              finally
                  System.Globalization.CultureInfo.CurrentCulture <- originalCulture
          }

          test "LayoutObservation.encode produces stable camelCase JSON" {
              let obs: LayoutObservation =
                  { NodeId = "panel-1"
                    Width = 120.5
                    Height = 80.0
                    ViewportX = 10.0
                    ViewportY = 20.0
                    Flags = [ LayoutFlag.OverflowHorizontal ] }

              let json = LayoutObservation.encode obs

              Expect.equal
                  json
                  "{\"nodeId\":\"panel-1\",\"width\":120.50,\"height\":80.00,\"viewportX\":10.00,\"viewportY\":20.00,\"flags\":[{\"kind\":\"OverflowHorizontal\"}]}"
                  "camelCase + invariant-culture floats + tagged-object flags"
          } ]
