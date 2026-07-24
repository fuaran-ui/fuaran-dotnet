namespace Fuaran.UI.LayoutObserver

// ─── LayoutObserverOptions — host-tunable defaults ──────────────
//
// The three policy knobs hosts can adjust without
// rewriting the observer. v1 defaults satisfy the acceptance
// criterion ("1000-event resize burst → ≤ 10 emissions") and are
// designed to keep the AI-facing wire payload sparse + the
// per-frame cost bounded.
//
//   - `DebounceMs`: minimum interval between emissions, in
//     milliseconds. Browser implementation coalesces via
//     `requestAnimationFrame` AND a wall-clock floor; a viewport
//     drag that fires 1000 ResizeObserver callbacks across 1 second
//     produces at most 10 emissions at the default 100ms floor.
//   - `AspectRatioWildlyOffFactor`: the magnitude threshold at
//     which the aspect-ratio flag fires. Default 3.0 — observed
//     ratio diverging from expected ratio by 3x or more. Lower
//     values catch milder distortions; higher values reduce noise.
//   - `EmitOnFlagChangeOnly`: when true (the v1 default), emit a
//     subscriber callback ONLY when the observed node's flag set
//     differs from the previous emission. When false, emit on
//     every debounced tick — useful for hosts that want a raw
//     metric stream for telemetry / visualisation. Initial
//     emission (first time a node enters the observer) always
//     fires regardless of this setting.

/// Host-tunable policy for the layout observer. Constructed via
/// `LayoutObserverOptions.defaults` and overridden field-by-field
/// at the `withLayoutObservation` boundary.
type LayoutObserverOptions =
    { DebounceMs: int
      AspectRatioWildlyOffFactor: float
      EmitOnFlagChangeOnly: bool }

module LayoutObserverOptions =
    /// v1 defaults — 100ms rAF-coalesced debounce, 3x aspect-ratio
    /// threshold, change-only emission. Matches the
    /// cost-model target.
    let defaults: LayoutObserverOptions =
        { DebounceMs = 100
          AspectRatioWildlyOffFactor = 3.0
          EmitOnFlagChangeOnly = true }
