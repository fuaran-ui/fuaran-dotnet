namespace Fuaran.UI.Telemetry.Drift

// ============================================================================
//  Operator-tunable defaults for the drift detector.
//
//  Per the anti-pattern note: "Don't ship drift-detector cron
//  without an operator-decided threshold." The default lives here so
//  operators can override per-deployment by passing a configured
//  `DriftThresholds` record to `Detect.run` instead of editing source.
// ============================================================================

/// Tunable thresholds the drift detector reports against. All percentages
/// are in 0.0..1.0 scale (0.10 = 10 %).
type DriftThresholds =
    {
        /// Minimum drop in success rate (current vs baseline window) that
        /// surfaces a regression warning. The opener-defined default is
        /// 10 % week-on-week.
        SuccessRateRegressionPct: float
        /// Minimum baseline-window sample size required to produce a
        /// regression finding. Below this, a window-over-window comparison
        /// is too noisy to act on; the detector reports "insufficient data"
        /// rather than firing a false-positive warning.
        MinBaselineSamples: int
    }

[<RequireQualifiedAccess>]
module Defaults =
    /// Default — 10 % week-on-week success-rate drop.
    let successRateRegressionPct = 0.10

    /// Default — require at least 50 baseline-window records before
    /// firing a regression warning. The number is conservative; operators
    /// running high-traffic deployments raise it; operators running test
    /// harnesses lower it.
    let minBaselineSamples = 50

    let thresholds: DriftThresholds =
        { SuccessRateRegressionPct = successRateRegressionPct
          MinBaselineSamples = minBaselineSamples }
