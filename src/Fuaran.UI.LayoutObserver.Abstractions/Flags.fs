module Fuaran.UI.LayoutObserver.Flags

// ─── Shared flag-derivation logic ───────────────────────────────
//
// Pure functions over `LayoutInput` that produce the
// derived `LayoutFlag list`. Both `BrowserLayoutObserver` (Fable)
// and `InMemoryLayoutObserver` (.NET) feed their captured metrics
// through the same derivation surface — by construction they
// produce identical flags from identical inputs, satisfying FGP 4
// (diagnostics under both pipelines).
//
// **`LayoutInput` is the abstract metric envelope.** The browser
// side fills it from `getBoundingClientRect` + `getComputedStyle`
// + a parent-rect snapshot; the in-memory side fills it from a
// hand-authored fixture. Keeping the derivation logic ignorant of
// the metric source means the same Expecto cases that verify the
// browser observer's behaviour can drive the in-memory observer
// without browser substrate.
//
// **One flag per axis where it matters.** `ZeroDimension` and
// `SqueezedToMin` fire per axis (width / height) independently;
// the derivation produces separate flag-list entries for each
// collapsed axis. Other flags are nullary.

/// The abstract metric envelope the derivation logic operates on.
/// Pre-populated by either the browser observer (live geometry) or
/// the in-memory observer (test fixtures). Optional fields stay
/// `None` when the source can't provide them — derivation degrades
/// gracefully (the dependent flag simply doesn't fire).
type LayoutInput =
    {
        /// Bounding-rect width in CSS pixels.
        Width: float
        /// Bounding-rect height in CSS pixels.
        Height: float
        /// `element.scrollWidth` — content-region width. `None`
        /// when unavailable (e.g. test fixture didn't specify);
        /// derivation skips the overflow-horizontal flag.
        ScrollWidth: float option
        /// `element.scrollHeight` — content-region height.
        ScrollHeight: float option
        /// `element.clientWidth` — paint-region width. Used to
        /// compare against `scrollWidth` for overflow detection.
        ClientWidth: float option
        /// `element.clientHeight` — paint-region height.
        ClientHeight: float option
        /// Computed `overflow-x` style ("visible" / "hidden" /
        /// "scroll" / "auto" / "clip"). `None` skips the
        /// overflow-horizontal flag — visible-overflow elements
        /// don't clip, so flagging them as overflowed is a false
        /// positive.
        OverflowX: string option
        /// Computed `overflow-y` style.
        OverflowY: string option
        /// Computed `min-width` in CSS pixels — used to detect
        /// SqueezedToMin on the width axis.
        MinWidth: float option
        /// Computed `min-height` in CSS pixels.
        MinHeight: float option
        /// The nearest clipping ancestor's bounding rect, if any
        /// (ancestor whose computed `overflow` is hidden / clip /
        /// scroll / auto). `None` means no clipping ancestor was
        /// found by the walker — `ChildClippedByAncestor` does not
        /// fire.
        ClippingAncestorRect: (float * float * float * float) option
        /// The element's bounding rect in viewport coordinates
        /// (left, top, right, bottom). Used together with
        /// `ClippingAncestorRect` to detect clipping.
        ElementRect: float * float * float * float
        /// The author-intended aspect ratio (width / height), if
        /// declared. Sources: CSS `aspect-ratio`, the node's
        /// `Style.AspectRatio` slot (when one lands; out of v1
        /// scope), or `None` (skip the AspectRatioWildlyOff flag).
        ExpectedAspectRatio: float option
    }

module LayoutInput =
    /// Empty input — useful as a baseline for tests / fixtures
    /// that only need to assert one specific flag.
    let empty (width: float) (height: float) : LayoutInput =
        { Width = width
          Height = height
          ScrollWidth = None
          ScrollHeight = None
          ClientWidth = None
          ClientHeight = None
          OverflowX = None
          OverflowY = None
          MinWidth = None
          MinHeight = None
          ClippingAncestorRect = None
          ElementRect = 0.0, 0.0, width, height
          ExpectedAspectRatio = None }

// ─── Per-flag predicates ────────────────────────────────────────
//
// Each predicate is a pure function over `LayoutInput` returning
// `LayoutFlag option`. Exposed individually for direct testing.

/// `OverflowHorizontal` — content extends beyond paint region AND
/// the element clips (computed overflow-x is not `visible`).
/// Returns None when scrollWidth / clientWidth / overflowX is not
/// available (visible-overflow elements should never be flagged as
/// overflowed; without overflow style we can't tell).
let overflowHorizontal (input: LayoutInput) : LayoutFlag option =
    match input.ScrollWidth, input.ClientWidth, input.OverflowX with
    | Some sw, Some cw, Some ox when sw > cw && ox <> "visible" -> Some LayoutFlag.OverflowHorizontal
    | _ -> None

/// `OverflowVertical` — vertical counterpart of overflowHorizontal.
let overflowVertical (input: LayoutInput) : LayoutFlag option =
    match input.ScrollHeight, input.ClientHeight, input.OverflowY with
    | Some sh, Some ch, Some oy when sh > ch && oy <> "visible" -> Some LayoutFlag.OverflowVertical
    | _ -> None

/// `ZeroDimension` — fires per axis whose rendered dimension
/// resolved to ≤ 0.5px. Browser hosts return widths in CSS px;
/// half-a-pixel is conservatively beneath "visually zero". Returns
/// a list (zero, one, or two entries — width and height can both
/// collapse).
let zeroDimension (input: LayoutInput) : LayoutFlag list =
    let widthFlag =
        if input.Width <= 0.5 then
            [ LayoutFlag.ZeroDimension "width" ]
        else
            []

    let heightFlag =
        if input.Height <= 0.5 then
            [ LayoutFlag.ZeroDimension "height" ]
        else
            []

    widthFlag @ heightFlag

/// `SqueezedToMin` — fires per axis whose rendered dimension is
/// within 0.5px of the computed min-dimension. Tolerance handles
/// sub-pixel rounding between the layout engine's rect and the
/// computed-style number. Returns a list of zero, one, or two
/// entries.
let squeezedToMin (input: LayoutInput) : LayoutFlag list =
    let widthFlag =
        match input.MinWidth with
        | Some mw when mw > 0.0 && abs (input.Width - mw) <= 0.5 -> [ LayoutFlag.SqueezedToMin "width" ]
        | _ -> []

    let heightFlag =
        match input.MinHeight with
        | Some mh when mh > 0.0 && abs (input.Height - mh) <= 0.5 -> [ LayoutFlag.SqueezedToMin "height" ]
        | _ -> []

    widthFlag @ heightFlag

/// `ChildClippedByAncestor` — fires when the element's bounding
/// rect extends beyond a clipping ancestor's bounding rect. Uses
/// the ancestor-rect snapshot the browser observer captured (the
/// nearest ancestor whose computed overflow is hidden / clip /
/// scroll / auto). No-op when no clipping ancestor was found.
let childClippedByAncestor (input: LayoutInput) : LayoutFlag option =
    match input.ClippingAncestorRect with
    | None -> None
    | Some(ancL, ancT, ancR, ancB) ->
        let elL, elT, elR, elB = input.ElementRect
        // Tolerance of 0.5px to absorb sub-pixel rounding noise.
        if elL < ancL - 0.5 || elT < ancT - 0.5 || elR > ancR + 0.5 || elB > ancB + 0.5 then
            Some LayoutFlag.ChildClippedByAncestor
        else
            None

/// `AspectRatioWildlyOff` — fires when the observed width/height
/// ratio diverges from the expected ratio by `factorThreshold` or
/// more. `factor` carried by the flag is the observed-over-expected
/// magnitude (always ≥ 1.0 — direction-agnostic). Returns None
/// when expected ratio is unavailable or either dimension is
/// non-positive.
let aspectRatioWildlyOff (factorThreshold: float) (input: LayoutInput) : LayoutFlag option =
    match input.ExpectedAspectRatio with
    | None -> None
    | Some expected when expected <= 0.0 -> None
    | Some expected ->
        if input.Height <= 0.0 || input.Width <= 0.0 then
            None
        else
            let observed = input.Width / input.Height
            let ratio = observed / expected
            // Direction-agnostic magnitude — 0.3x off is just as
            // "wildly off" as 3.0x off, so normalise to ≥ 1.0.
            let magnitude = if ratio >= 1.0 then ratio else 1.0 / ratio

            if magnitude >= factorThreshold then
                Some(LayoutFlag.AspectRatioWildlyOff magnitude)
            else
                None

// ─── Combined derivation ────────────────────────────────────────
//
// `derive` is the entry point both observers call. Order of the
// resulting list is deterministic so `EmitOnFlagChangeOnly`
// comparisons stay stable across emissions.

/// Derive the full flag list for one `LayoutInput`. Used by both
/// `BrowserLayoutObserver` and `InMemoryLayoutObserver` — same
/// input produces same output.
let derive (options: LayoutObserverOptions) (input: LayoutInput) : LayoutFlag list =
    [ yield! Option.toList (overflowHorizontal input)
      yield! Option.toList (overflowVertical input)
      yield! zeroDimension input
      yield! squeezedToMin input
      yield! Option.toList (childClippedByAncestor input)
      yield! Option.toList (aspectRatioWildlyOff options.AspectRatioWildlyOffFactor input) ]
