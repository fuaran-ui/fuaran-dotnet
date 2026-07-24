module Fuaran.UI.Telemetry.Tests.StyleDriftTests

open Expecto
open Fuaran.UI.StyleObserver
open Fuaran.UI.ThemeManifest
open Fuaran.UI.Telemetry.Drift
open Fuaran.UI.Telemetry.Drift.StyleDrift

// ============================================================================
//  Style-flag drift detector (Phase 151) — newly-introduced vs cleared
//  violations across two windows of StyleObservation, severity-weighted by
//  the manifest's declared invariant weights. Deterministic flag-set diff,
//  no pixels.
// ============================================================================

/// Build an observation with a given flag set — the colour evidence is
/// irrelevant to the drift diff (it keys on NodeId + flag kind), so we use a
/// neutral fixture and override `Flags`.
let private obs (nodeId: string) (flags: StyleFlag list) : StyleObservation =
    { StyleObservation.withoutFlags nodeId Rgba.black Rgba.white FontRole.Unknown None 21.0 with
        Flags = flags }

/// A manifest whose `brand` usage-budget invariant carries weight 3.0 — so a
/// `UsageBudgetExceeded brand` violation weights 3× a default violation.
let private weightedManifest: ThemeManifest =
    { ThemeManifest.empty with
        Invariants =
            [ Invariant.createWeighted 3.0 (InvariantKind.UsageBudget("brand", 10.0, 5.0))
              Invariant.create (InvariantKind.ContrastFloor("body-text", 4.5)) ] }

[<Tests>]
let tests =
    testList
        "style drift detector"
        [ test "a render that introduces a violation absent from the prior is flagged" {
              let baseline = [ obs "n1" []; obs "n2" [] ]

              let current =
                  [ obs "n1" [ StyleFlag.UsageBudgetExceeded("brand", 10.0, 22.0) ]; obs "n2" [] ]

              let report = StyleDrift.detect (Some weightedManifest) baseline current

              Expect.isTrue (StyleDrift.regressionDetected report) "regression detected"
              Expect.equal report.Introduced.Length 1 "one introduced violation"
              Expect.equal report.AffectedNodeCount 1 "one affected node"
              Expect.equal report.Introduced[0].NodeId "n1" "introduced on n1"
              Expect.equal report.Introduced[0].FlagKind "UsageBudgetExceeded" "the budget flag"
              // Weighted by the manifest's brand-budget invariant weight (3.0).
              Expect.floatClose Accuracy.medium report.WeightedSeverity 3.0 "severity = invariant weight 3.0"
              Expect.isEmpty report.Cleared "nothing cleared"
          }

          test "severity sums the violated invariants' weights, default 1.0 when unweighted" {
              // n1 gains a weight-3 budget violation; n2 gains a manifest-free
              // contrast violation (no declared invariant → default weight 1.0).
              let baseline = [ obs "n1" []; obs "n2" [] ]

              let current =
                  [ obs "n1" [ StyleFlag.UsageBudgetExceeded("brand", 10.0, 22.0) ]
                    obs "n2" [ StyleFlag.ContrastBelowAA 3.1 ] ]

              let report = StyleDrift.detect (Some weightedManifest) baseline current

              Expect.equal report.Introduced.Length 2 "two introduced"
              Expect.equal report.AffectedNodeCount 2 "two affected nodes"
              Expect.floatClose Accuracy.medium report.WeightedSeverity 4.0 "3.0 (budget) + 1.0 (contrast)"
          }

          test "a render that clears a prior violation is a fix, not a regression" {
              let baseline = [ obs "n1" [ StyleFlag.UsageBudgetExceeded("brand", 10.0, 22.0) ] ]
              let current = [ obs "n1" [] ]

              let report = StyleDrift.detect (Some weightedManifest) baseline current

              Expect.isFalse (StyleDrift.regressionDetected report) "no regression on a fix-only render"
              Expect.isEmpty report.Introduced "nothing introduced"
              Expect.equal report.Cleared.Length 1 "one cleared violation"
              Expect.equal report.Cleared[0].NodeId "n1" "cleared on n1"
          }

          test "a no-change render produces no regression and no fix" {
              let window = [ obs "n1" [ StyleFlag.ContrastBelowAA 3.1 ]; obs "n2" [] ]
              let report = StyleDrift.detect None window window

              Expect.isFalse (StyleDrift.regressionDetected report) "no regression"
              Expect.isEmpty report.Introduced "nothing introduced"
              Expect.isEmpty report.Cleared "nothing cleared"
              Expect.floatClose Accuracy.medium report.WeightedSeverity 0.0 "zero severity"
          }

          test "the diff is deterministic — identical render pairs give identical reports" {
              let baseline =
                  [ obs "b" [ StyleFlag.ContrastBelowAA 3.1 ]
                    obs "a" [ StyleFlag.InvisibleText 1.0 ] ]

              let current =
                  [ obs "a" []
                    obs "b" [ StyleFlag.ContrastBelowAA 3.1; StyleFlag.OffPaletteColour "#abc" ] ]

              let report1 = StyleDrift.detect None baseline current
              let report2 = StyleDrift.detect None baseline current

              Expect.equal report1 report2 "identical inputs → identical report"
              // n a cleared its InvisibleText; n b gained OffPaletteColour.
              Expect.equal report1.Introduced.Length 1 "one introduced (OffPaletteColour on b)"
              Expect.equal report1.Cleared.Length 1 "one cleared (InvisibleText on a)"
              Expect.equal report1.Introduced[0].FlagKind "OffPaletteColour" "the off-palette flag"
          }

          test "the latest observation per node is authoritative within a window" {
              // Two emissions for n1 in the current window — the later one
              // (no flags) wins, so the prior-window violation is cleared, not
              // re-introduced.
              let baseline = [ obs "n1" [ StyleFlag.ContrastBelowAA 3.0 ] ]

              let current = [ obs "n1" [ StyleFlag.ContrastBelowAA 3.0 ]; obs "n1" [] ]

              let report = StyleDrift.detect None baseline current
              Expect.isFalse (StyleDrift.regressionDetected report) "latest emission cleared it"
              Expect.equal report.Cleared.Length 1 "reported as a fix"
          }

          test "formatReport phrases the worst violation in the manifest's vocabulary" {
              let baseline = [ obs "n1" []; obs "n2" [] ]

              let current =
                  [ obs "n1" [ StyleFlag.UsageBudgetExceeded("brand", 10.0, 22.0) ]
                    obs "n2" [ StyleFlag.ContrastBelowAA 3.1 ] ]

              let report = StyleDrift.detect (Some weightedManifest) baseline current
              let line = StyleDrift.formatReport report

              Expect.stringContains line "introduced 2 style violation(s) across 2 node(s)" "count + node line"
              // The weight-3 budget violation is the worst; reported in budget vocabulary.
              Expect.stringContains line "UsageBudgetExceeded brand" "worst violation named"
              Expect.stringContains line "observed 22.0%" "observed share in the manifest's vocabulary"
          } ]
