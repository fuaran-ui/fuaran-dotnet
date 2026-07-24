module Fuaran.UI.StyleObserver.Tests.ManifestFlagTests

// ─── Manifest-aware flag derivation (Phase 146) ─────────────────
//
// Acceptance criteria covered:
//   - UsageBudgetExceeded: a fixture tree whose brand-toned nodes
//     occupy 28% of total area against a 9% ± 3% budget fires
//     UsageBudgetExceeded("color.brand.base", 9.0, 28.0); a tree within
//     tolerance fires nothing.
//   - ContrastBelowDeclaredFloor fires against a per-role floor the
//     manifest-free AA default would pass.
//   - TokenResolutionFailed fires when a tone resolves to no host token.
//   - OffPaletteColour honours the Custom-subtree exemption (untoned
//     nodes — incl. domain SVG — are never palette-checked).
//   - With no manifest, only the Phase 144 flags fire (graceful
//     degradation); the budget math is deterministic.

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.StyleObserver
open Fuaran.UI.ThemeManifest

// ─── Fixtures ───────────────────────────────────────────────────

let private brand = Rgba.rgb 59.0 91.0 219.0 // #3b5bdb
let private white = Rgba.white

/// A manifest binding Tone.Brand → the brand token, with a brand usage
/// budget (9% ± 3%), a brand contrast floor (7.0 — stricter than AA),
/// and a surface budget (60% ± 10%).
let private manifest: ThemeManifest =
    { Meta = ManifestMeta.anonymous
      Tokens =
        [ { Name = "color.brand.base"
            Type = "color"
            Value = "#3b5bdb"
            Description = None
            Role = None }
          { Name = "color.surface"
            Type = "color"
            Value = "#ffffff"
            Description = None
            Role = None } ]
      Roles =
        [ { Role = ManifestRole.Tone ToneVariant.Brand
            TokenName = "color.brand.base" }
          { Role = ManifestRole.Tone ToneVariant.Default
            TokenName = "color.surface" } ]
      Invariants =
        [ Invariant.create (InvariantKind.UsageBudget("color.brand.base", 9.0, 3.0))
          Invariant.create (InvariantKind.UsageBudget("color.surface", 60.0, 10.0))
          Invariant.create (InvariantKind.ContrastFloor("Brand", 7.0)) ] }

/// A toned observation with a given effective background + contrast.
let private tonedObs (nodeId: string) (tone: string) (bg: Rgba) (contrast: float) : StyleObservation =
    { NodeId = nodeId
      Foreground = Rgba.black
      EffectiveBackground = bg
      FontRole = FontRole.SansSerif
      EmittedTone = Some tone
      ContrastRatio = contrast
      Flags = [] }

[<Tests>]
let tests =
    testList
        "ManifestFlags"
        [

          // ─── UsageBudget (tree-level area weighting) ─────────────

          test "UsageBudgetExceeded fires when brand occupies 28% against a 9% ± 3% budget" {
              // brand nodes: 280 of 1000 total area = 28%; surface: 720 = 72%.
              let nodes =
                  [ tonedObs "b1" "Brand" brand 21.0, 180.0
                    tonedObs "b2" "Brand" brand 21.0, 100.0
                    tonedObs "s1" "Default" white 21.0, 720.0 ]

              let flags = ManifestFlags.verifyUsageBudgets manifest nodes

              Expect.contains
                  flags
                  (StyleFlag.UsageBudgetExceeded("color.brand.base", 9.0, 28.0))
                  "brand at 28% breaches the 9% ± 3% budget"

              // surface at 72% breaches the 60% ± 10% budget (>70).
              Expect.contains
                  flags
                  (StyleFlag.UsageBudgetExceeded("color.surface", 60.0, 72.0))
                  "surface at 72% breaches the 60% ± 10% budget"
          }

          test "UsageBudget within tolerance fires nothing" {
              // brand: 90 of 1000 = 9% (exactly on target); surface 610 = 61% (within 60±10).
              let nodes =
                  [ tonedObs "b1" "Brand" brand 21.0, 90.0
                    tonedObs "s1" "Default" white 21.0, 610.0
                    // a third, off-palette filler to make the total 1000 without a budgeted token
                    { tonedObs "x" "Default" (Rgba.rgb 1.0 2.0 3.0) 21.0 with
                        EmittedTone = None },
                    300.0 ]

              let budgetFlags =
                  ManifestFlags.verifyUsageBudgets manifest nodes
                  |> List.filter (fun f ->
                      match f with
                      | StyleFlag.UsageBudgetExceeded _ -> true
                      | _ -> false)

              Expect.isEmpty budgetFlags "9% brand + 61% surface are both within tolerance"
          }

          test "UsageBudget is deterministic (identical inputs → identical flags)" {
              let nodes =
                  [ tonedObs "b1" "Brand" brand 21.0, 280.0
                    tonedObs "s1" "Default" white 21.0, 720.0 ]

              Expect.equal
                  (ManifestFlags.verifyUsageBudgets manifest nodes)
                  (ManifestFlags.verifyUsageBudgets manifest nodes)
                  "same tree + manifest → same flags"
          }

          test "UsageBudget degrades gracefully with no area (empty → no budget flags)" {
              Expect.isEmpty (ManifestFlags.verifyUsageBudgets manifest []) "no areas → no budget flags"
          }

          // ─── ContrastBelowDeclaredFloor ──────────────────────────

          test "ContrastBelowDeclaredFloor fires against a per-role floor AA would pass" {
              // Contrast 5.0 passes the manifest-free AA default (4.5) but
              // fails the manifest's declared Brand floor (7.0).
              let obs = tonedObs "b1" "Brand" brand 5.0
              let flags = ManifestFlags.perNodeFlags manifest obs

              Expect.contains
                  flags
                  (StyleFlag.ContrastBelowDeclaredFloor("Brand", 5.0, 7.0))
                  "declared floor stricter than AA fires"
          }

          test "ContrastBelowDeclaredFloor does NOT fire when the declared floor is met" {
              let obs = tonedObs "b1" "Brand" brand 8.0
              let flags = ManifestFlags.perNodeFlags manifest obs

              Expect.isFalse
                  (flags
                   |> List.exists (fun f ->
                       match f with
                       | StyleFlag.ContrastBelowDeclaredFloor _ -> true
                       | _ -> false))
                  "8.0 ≥ 7.0 floor → no flag"
          }

          // ─── TokenResolutionFailed ───────────────────────────────

          test "TokenResolutionFailed fires when a tone resolves to no host token" {
              // Critical has no role binding in this manifest.
              let obs = tonedObs "c1" "Critical" (Rgba.rgb 200.0 0.0 0.0) 21.0
              let flags = ManifestFlags.perNodeFlags manifest obs
              Expect.contains flags (StyleFlag.TokenResolutionFailed "Critical") "unbound tone → resolution failure"
          }

          test "a resolved tone does not raise TokenResolutionFailed" {
              let obs = tonedObs "b1" "Brand" brand 21.0
              let flags = ManifestFlags.perNodeFlags manifest obs

              Expect.isFalse
                  (flags
                   |> List.exists (fun f ->
                       match f with
                       | StyleFlag.TokenResolutionFailed _ -> true
                       | _ -> false))
                  "Brand resolves → no resolution failure"
          }

          // ─── OffPaletteColour + Custom exemption ─────────────────

          test "OffPaletteColour fires when a resolved toned fill is off-palette" {
              // Brand resolves (so not a resolution failure) but the
              // rendered surface is an off-palette teal.
              let obs = tonedObs "b1" "Brand" (Rgba.rgb 12.0 200.0 180.0) 21.0
              let flags = ManifestFlags.perNodeFlags manifest obs

              Expect.contains flags (StyleFlag.OffPaletteColour "rgb(12, 200, 180)") "off-palette toned fill flagged"
          }

          test "OffPaletteColour does NOT fire for an on-palette fill" {
              let obs = tonedObs "b1" "Brand" brand 21.0
              let flags = ManifestFlags.perNodeFlags manifest obs

              Expect.isFalse
                  (flags
                   |> List.exists (fun f ->
                       match f with
                       | StyleFlag.OffPaletteColour _ -> true
                       | _ -> false))
                  "brand fill is on palette"
          }

          test "Custom-subtree exemption: an untoned node is never manifest-checked (domain SVG safe)" {
              // An untoned node with a wildly off-palette fill (a chart
              // series / logo gradient) must NOT raise OffPaletteColour.
              let svg =
                  { tonedObs "chart-series" "" (Rgba.rgb 255.0 0.0 128.0) 21.0 with
                      EmittedTone = None }

              Expect.isEmpty (ManifestFlags.perNodeFlags manifest svg) "untoned (Custom/SVG) nodes are exempt"
          }

          // ─── Graceful degradation (no manifest) ──────────────────

          test "no manifest wired ⇒ only Phase 144 flags fire" {
              // The in-memory observer with no manifest must produce no
              // manifest-aware flags even for an off-palette, unbound tone.
              let observer = InMemoryStyleObserver.create ()

              observer.RegisterFixture(
                  "b1",
                  { Input =
                      { Flags.StyleInput.baseline with
                          Foreground = Rgba.black
                          BackgroundLayers = [ Rgba.rgb 12.0 200.0 180.0 ]
                          EmittedTone = Some "Critical" }
                    Parent = None }
              )

              let obs = ((observer :> IStyleObserver).Observe("b1")).Value

              Expect.isFalse
                  (obs.Flags
                   |> List.exists (fun f ->
                       match f with
                       | StyleFlag.TokenResolutionFailed _
                       | StyleFlag.OffPaletteColour _
                       | StyleFlag.UsageBudgetExceeded _
                       | StyleFlag.ContrastBelowDeclaredFloor _ -> true
                       | _ -> false))
                  "no manifest → no manifest-aware flags"
          }

          test "manifest wired into the observer ⇒ per-node manifest flags flow through Observe" {
              let observer =
                  InMemoryStyleObserver.createWithManifest StyleObserverOptions.defaults manifest

              observer.RegisterFixture(
                  "c1",
                  { Input =
                      { Flags.StyleInput.baseline with
                          Foreground = Rgba.black
                          BackgroundLayers = [ Rgba.rgb 200.0 0.0 0.0 ]
                          EmittedTone = Some "Critical" }
                    Parent = None }
              )

              let obs = ((observer :> IStyleObserver).Observe("c1")).Value

              Expect.contains
                  obs.Flags
                  (StyleFlag.TokenResolutionFailed "Critical")
                  "unbound Critical flagged via Observe"
          }

          // ─── encode round-trips for the new cases ────────────────

          test "the four manifest-aware flags encode to tagged-object JSON" {
              Expect.equal
                  (StyleFlag.encode (StyleFlag.TokenResolutionFailed "Brand"))
                  "{\"kind\":\"TokenResolutionFailed\",\"slot\":\"Brand\"}"
                  "TokenResolutionFailed"

              Expect.equal
                  (StyleFlag.encode (StyleFlag.OffPaletteColour "rgb(1, 2, 3)"))
                  "{\"kind\":\"OffPaletteColour\",\"value\":\"rgb(1, 2, 3)\"}"
                  "OffPaletteColour"

              Expect.equal
                  (StyleFlag.encode (StyleFlag.UsageBudgetExceeded("color.brand.base", 9.0, 28.0)))
                  "{\"kind\":\"UsageBudgetExceeded\",\"token\":\"color.brand.base\",\"declaredPct\":9.00,\"observedPct\":28.00}"
                  "UsageBudgetExceeded"

              Expect.equal
                  (StyleFlag.encode (StyleFlag.ContrastBelowDeclaredFloor("Brand", 5.0, 7.0)))
                  "{\"kind\":\"ContrastBelowDeclaredFloor\",\"role\":\"Brand\",\"ratio\":5.00,\"floor\":7.00}"
                  "ContrastBelowDeclaredFloor"
          } ]
