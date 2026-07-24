namespace Fuaran.UI.LayoutObserver

// ─── Layout flags — the AI-facing geometric interpretations ─────
//
// The orchestrator's semantic-state channel
// (`AIStateSnapshot` = fields / buttons / selections) is blind to
// layout failures — a Stack squeezed flat by an oversized sibling,
// a child clipped by a parent's `overflow: hidden`, a flex item
// collapsed to zero height. This DU is the small fixed vocabulary
// the layout observer derives from raw geometry and surfaces to
// the AI via `AIStateSnapshot.Layout` + `_platform.ui.inspect_layout`.
//
// **Additive-only post-ship.** Each flag is a load-bearing AI input
// — adding a new case is fine (existing consumers compile with a
// FS0025 exhaustiveness warning), but redefining an existing case
// breaks every prompt cache that pattern-matched against it. The v1
// flag set is fixed: any new flag is a follow-on phase.
//
// **Axis-tagged where it matters.** `ZeroDimension` and
// `SqueezedToMin` both carry an axis string ("width" / "height") so
// the AI can reason about which dimension collapsed; the flag fires
// when EITHER axis collapses (separate cases per axis if both).
// `AspectRatioWildlyOff` carries the observed / expected ratio
// factor (e.g. 3.2 = 3.2x off intended) so the AI can grade severity.

/// The fixed set of layout-interpretation flags the observer
/// derives from raw geometry. Additive-only post-ship.
type LayoutFlag =
    /// The element's `scrollWidth > clientWidth` AND its computed
    /// `overflow-x` is not `visible` — content is clipped or
    /// horizontally scrolling.
    | OverflowHorizontal
    /// The element's `scrollHeight > clientHeight` AND its computed
    /// `overflow-y` is not `visible` — content is clipped or
    /// vertically scrolling.
    | OverflowVertical
    /// One of the element's dimensions resolved to ≤ 0.5px.
    /// `axis` is `"width"` or `"height"`. A separate case fires
    /// per collapsed axis (both axes can be flagged simultaneously).
    | ZeroDimension of axis: string
    /// The element's resolved dimension equals its computed `min-width`
    /// (or `min-height`) — it's been squeezed against the floor of
    /// its sizing contract. `axis` is `"width"` or `"height"`.
    | SqueezedToMin of axis: string
    /// The element's bounding rect extends beyond an ancestor whose
    /// computed `overflow` is `hidden` / `clip` / `scroll` / `auto` —
    /// the visible portion is being clipped by a containing block.
    | ChildClippedByAncestor
    /// The observed `width / height` ratio diverges from the
    /// expected ratio by more than the configured factor (default 3x).
    /// `factor` is the observed-over-expected magnitude (e.g. 3.2
    /// means 3.2x off the expected aspect). Useful for catching
    /// images / charts / cards that have collapsed against their
    /// intended layout shape.
    | AspectRatioWildlyOff of factor: float

module LayoutFlag =
    /// Stable string discriminator for JSON / log emission. The
    /// names are deliberately PascalCase-matched to the DU cases so
    /// AI prompt caches that pattern-match on either side stay
    /// aligned with the F# surface.
    let kind (flag: LayoutFlag) : string =
        match flag with
        | OverflowHorizontal -> "OverflowHorizontal"
        | OverflowVertical -> "OverflowVertical"
        | ZeroDimension _ -> "ZeroDimension"
        | SqueezedToMin _ -> "SqueezedToMin"
        | ChildClippedByAncestor -> "ChildClippedByAncestor"
        | AspectRatioWildlyOff _ -> "AspectRatioWildlyOff"

    /// Encode a single flag as the AI-friendly tagged-object JSON
    /// shape — `{"kind":"X"}` for nullary cases, `{"kind":"X","axis":"width"}`
    /// for axis-tagged cases, `{"kind":"X","factor":3.2}` for the
    /// aspect-ratio case. Mirrors the `_platform.ui.inspect_active_module`
    /// payload style so the model pattern-matches both surfaces
    /// the same way.
    let encode (flag: LayoutFlag) : string =
        let escape (s: string) =
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")

        match flag with
        | OverflowHorizontal
        | OverflowVertical
        | ChildClippedByAncestor -> $"{{\"kind\":\"{kind flag}\"}}"
        | ZeroDimension axis
        | SqueezedToMin axis -> $"{{\"kind\":\"{kind flag}\",\"axis\":\"{escape axis}\"}}"
        | AspectRatioWildlyOff factor ->
            // Invariant-culture float so a German-locale build doesn't
            // emit "3,2" — the wire format is JSON, period as decimal
            // separator.
            let f = factor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)

            $"{{\"kind\":\"{kind flag}\",\"factor\":{f}}}"
