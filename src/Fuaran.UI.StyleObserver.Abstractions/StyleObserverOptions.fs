namespace Fuaran.UI.StyleObserver

// ─── StyleObserverOptions — host-tunable defaults ───────────────
//
// The policy knobs hosts can adjust without rewriting the observer
// — the resolved-style twin of `LayoutObserverOptions`. v1 defaults
// keep the AI-facing wire payload sparse + the per-frame cost
// bounded, and pin the WCAG thresholds at their standard values.
//
//   - `DebounceMs`: minimum interval between emissions, in
//     milliseconds. Browser implementation coalesces via
//     `requestAnimationFrame` AND a wall-clock floor.
//   - `ContrastAAThreshold`: the WCAG AA normal-text contrast floor.
//     Default 4.5 — `ContrastBelowAA` fires when the contrast ratio
//     is below this (and at/above the invisible-text threshold).
//   - `InvisibleTextThreshold`: the contrast at/below which text is
//     treated as effectively invisible. Default 1.1 — a ratio this
//     close to 1.0 means foreground ≈ background. `InvisibleText`
//     fires below it (instead of `ContrastBelowAA`).
//   - `AccentIndistinctThreshold`: the WCAG UI-component contrast
//     floor (3.0:1). `AccentIndistinct` fires when a toned element's
//     background tint contrasts with the surface behind it below this.
//   - `EmitOnFlagChangeOnly`: when true (the v1 default), emit a
//     subscriber callback ONLY when the observed node's flag set
//     differs from the previous emission. When false, emit on every
//     debounced tick. Initial emission (first time a node enters the
//     observer) always fires regardless of this setting.

/// Host-tunable policy for the style observer. Constructed via
/// `StyleObserverOptions.defaults` and overridden field-by-field at
/// the observer-construction boundary.
type StyleObserverOptions =
    { DebounceMs: int
      ContrastAAThreshold: float
      InvisibleTextThreshold: float
      AccentIndistinctThreshold: float
      EmitOnFlagChangeOnly: bool }

module StyleObserverOptions =
    /// v1 defaults — 100ms rAF-coalesced debounce, the standard WCAG
    /// AA (4.5) / UI-component (3.0) contrast floors, a 1.1
    /// invisible-text threshold, change-only emission.
    let defaults: StyleObserverOptions =
        { DebounceMs = 100
          ContrastAAThreshold = 4.5
          InvisibleTextThreshold = 1.1
          AccentIndistinctThreshold = 3.0
          EmitOnFlagChangeOnly = true }
