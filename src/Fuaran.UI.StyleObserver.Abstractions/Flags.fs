module Fuaran.UI.StyleObserver.Flags

// ─── Shared flag-derivation logic ───────────────────────────────
//
// Pure functions over `StyleInput` that produce the derived
// `StyleFlag list` + the resolved colour evidence. Both
// `BrowserStyleObserver` (Fable) and `InMemoryStyleObserver` (.NET)
// feed their captured colours through the same derivation surface —
// by construction they produce identical flags from identical
// inputs, satisfying FGP 4 (diagnostics under both pipelines).
//
// **`StyleInput` is the abstract evidence envelope.** The browser
// side fills it from `getComputedStyle` (the element's `color`, its
// own + ancestor `background-color` stack, `font-family`, and the
// `data-fuaran-tone` attribute); the in-memory side fills it from a
// hand-authored fixture. Keeping the derivation logic ignorant of
// the source means the same Expecto cases that verify the browser
// observer's behaviour drive the in-memory observer without browser
// substrate.
//
// **The effective-background composite walk is the load-bearing
// algorithm.** `BackgroundLayers` is the element's own background
// colour followed by each ancestor's, element-first. Each may be
// translucent. `effectiveBackground` composites them top-over-bottom
// (source-over) down to the first opaque layer, falling back to an
// opaque-white base (the browser's default canvas) when no ancestor
// is opaque before `<html>`. The composited result is the colour the
// text actually sits on — the contrast denominator.

open Fuaran.UI.StyleObserver

/// The abstract evidence envelope the derivation logic operates on.
/// Pre-populated by either the browser observer (live computed style)
/// or the in-memory observer (test fixtures).
type StyleInput =
    {
        /// The element's declared `color` (foreground). May be
        /// translucent; the derivation composites it over the
        /// effective background before measuring contrast.
        Foreground: Rgba
        /// The element's own `background-color` followed by each
        /// ancestor's, element-first (top-to-bottom paint order).
        /// Each may be translucent. The composite walk folds these to
        /// the first opaque layer. An empty list means the element +
        /// every ancestor were transparent — the implicit opaque-white
        /// canvas is the effective background.
        BackgroundLayers: Rgba list
        /// The computed `font-family` string, classified to a
        /// `FontRole`. `None` skips classification (`FontRole.Unknown`).
        FontFamily: string option
        /// The `--fuaran-tone-*` token the element declared (read from
        /// the renderer's `data-fuaran-tone` attribute browser-side).
        /// `None` when the element opted into no tone — the
        /// `AccentIndistinct` flag only fires for toned elements.
        EmittedTone: string option
    }

module StyleInput =
    /// Baseline input — opaque-black text on the implicit white
    /// canvas, no font, no tone. Useful for tests/fixtures that only
    /// need to assert one specific flag.
    let baseline: StyleInput =
        { Foreground = Rgba.black
          BackgroundLayers = []
          FontFamily = None
          EmittedTone = None }

// ─── Compositing + WCAG contrast ────────────────────────────────

/// Source-over composite of `top` (with its alpha) over `bottom`.
/// Standard premultiplied-then-normalised alpha blend. When the
/// result is fully transparent the channels are meaningless, so we
/// return transparent.
let composite (top: Rgba) (bottom: Rgba) : Rgba =
    let a = top.A + bottom.A * (1.0 - top.A)

    if a <= 0.0 then
        Rgba.transparent
    else
        let blend tc bc =
            (tc * top.A + bc * bottom.A * (1.0 - top.A)) / a

        { R = blend top.R bottom.R
          G = blend top.G bottom.G
          B = blend top.B bottom.B
          A = a }

/// Composite a background layer stack (element-first) down to the
/// first opaque layer, returning the opaque colour the text sits on.
/// Layers below the first opaque one are invisible and discarded;
/// when no layer is opaque, an opaque-white base (the browser's
/// default canvas) is appended.
let effectiveBackground (layers: Rgba list) : Rgba =
    // Truncate at (and including) the first opaque layer.
    let rec takeToOpaque acc remaining =
        match remaining with
        | [] -> List.rev acc, false
        | layer :: rest ->
            if Rgba.isOpaque layer then
                List.rev (layer :: acc), true
            else
                takeToOpaque (layer :: acc) rest

    let truncated, foundOpaque = takeToOpaque [] layers

    let stack =
        if foundOpaque then
            truncated
        else
            truncated @ [ Rgba.white ]

    // Composite from the opaque base upward to the element layer.
    match List.rev stack with
    | [] -> Rgba.white
    | baseLayer :: aboveBottomUp -> List.fold (fun acc top -> composite top acc) baseLayer aboveBottomUp

/// WCAG relative luminance of an (assumed opaque) colour. Channels
/// are linearised per the sRGB transfer function then weighted.
let relativeLuminance (c: Rgba) : float =
    let channel (v: float) =
        let s = v / 255.0

        if s <= 0.03928 then
            s / 12.92
        else
            ((s + 0.055) / 1.055) ** 2.4

    0.2126 * channel c.R + 0.7152 * channel c.G + 0.0722 * channel c.B

/// WCAG contrast ratio between two opaque colours — `(L_lighter +
/// 0.05) / (L_darker + 0.05)`, in the range 1.0 (identical) … 21.0
/// (black-on-white).
let contrastRatio (a: Rgba) (b: Rgba) : float =
    let la = relativeLuminance a
    let lb = relativeLuminance b
    let lighter = max la lb
    let darker = min la lb
    (lighter + 0.05) / (darker + 0.05)

// ─── Derived evidence (shared by both observers) ────────────────

/// The opaque background the text sits on, after the composite walk.
let resolvedBackground (input: StyleInput) : Rgba =
    effectiveBackground input.BackgroundLayers

/// The colour the text actually paints with — the declared
/// foreground composited over the effective background, so a
/// translucent foreground reports its rendered value.
let resolvedForeground (input: StyleInput) : Rgba =
    composite input.Foreground (resolvedBackground input)

/// The WCAG contrast ratio between the resolved foreground and the
/// effective background — the value `ContrastBelowAA` / `InvisibleText`
/// key off.
let contrast (input: StyleInput) : float =
    contrastRatio (resolvedForeground input) (resolvedBackground input)

/// Classify the computed font-family string into a `FontRole`.
/// Substring match, case-insensitive, locale-independent: "mono" →
/// Monospace, "serif" → Serif (unless it's a "sans serif"/"sans-serif"
/// family, which is SansSerif), "sans" → SansSerif. `None` /
/// unclassifiable → `Unknown`.
let fontRole (input: StyleInput) : FontRole =
    match input.FontFamily with
    | None -> FontRole.Unknown
    | Some raw ->
        let f = raw.ToLowerInvariant()

        if f.Contains("mono") then FontRole.Monospace
        elif f.Contains("sans") then FontRole.SansSerif
        elif f.Contains("serif") then FontRole.Serif
        else FontRole.Unknown

// ─── Per-flag predicates ────────────────────────────────────────
//
// Each predicate is a pure function over `StyleInput` returning
// `StyleFlag option`. Exposed individually for direct testing.
//
// `InvisibleText` and `ContrastBelowAA` partition the contrast axis:
// invisible fires strictly below the invisible threshold; below-AA
// fires in the band `[invisibleThreshold, aaThreshold)`. By
// construction they never both fire (no double-flag for one
// underlying legibility failure), so `derive` simply concatenates the
// predicate outputs — same shape as `LayoutObserver.Flags.derive`.

/// `InvisibleText` — contrast at/below the invisible threshold (text
/// ≈ background colour). The severe subset.
let invisibleText (invisibleThreshold: float) (input: StyleInput) : StyleFlag option =
    let c = contrast input

    if c < invisibleThreshold then
        Some(StyleFlag.InvisibleText c)
    else
        None

/// `ContrastBelowAA` — contrast in the band `[invisibleThreshold,
/// aaThreshold)`: below the WCAG AA normal-text floor but still
/// faintly visible.
let contrastBelowAA (invisibleThreshold: float) (aaThreshold: float) (input: StyleInput) : StyleFlag option =
    let c = contrast input

    if c >= invisibleThreshold && c < aaThreshold then
        Some(StyleFlag.ContrastBelowAA c)
    else
        None

/// `AccentIndistinct` — fires when the element declared a tone AND
/// has a distinct (non-transparent) own background tint, but that
/// tint's contrast against the surface behind it is below the
/// UI-component floor. The toned accent surface is indistinguishable
/// from its container.
let accentIndistinct (accentThreshold: float) (input: StyleInput) : StyleFlag option =
    match input.EmittedTone, input.BackgroundLayers with
    | None, _ -> None
    | Some _, [] -> None
    | Some _, ownLayer :: _ when ownLayer.A <= 0.0 -> None
    | Some _, _ :: ancestorLayers ->
        // The accent surface = the full composite (element tint over
        // ancestors); the surface behind it = the ancestor composite.
        let accentSurface = resolvedBackground input
        let ancestorSurface = effectiveBackground ancestorLayers
        let c = contrastRatio accentSurface ancestorSurface

        if c < accentThreshold then
            Some(StyleFlag.AccentIndistinct c)
        else
            None

// ─── Combined derivation ────────────────────────────────────────
//
// `derive` is the entry point both observers call. Order of the
// resulting list is deterministic so `EmitOnFlagChangeOnly`
// comparisons stay stable across emissions.

/// Derive the full flag list for one `StyleInput`. Used by both
/// `BrowserStyleObserver` and `InMemoryStyleObserver` — same input
/// produces same output.
let derive (options: StyleObserverOptions) (input: StyleInput) : StyleFlag list =
    [ yield! Option.toList (invisibleText options.InvisibleTextThreshold input)
      yield! Option.toList (contrastBelowAA options.InvisibleTextThreshold options.ContrastAAThreshold input)
      yield! Option.toList (accentIndistinct options.AccentIndistinctThreshold input) ]

/// Build a fully-populated `StyleObservation` from a `StyleInput` —
/// the shared shape both observers use so the browser + in-memory
/// payloads are byte-identical for identical evidence.
let toObservation (options: StyleObserverOptions) (nodeId: string) (input: StyleInput) : StyleObservation =
    { NodeId = nodeId
      Foreground = resolvedForeground input
      EffectiveBackground = resolvedBackground input
      FontRole = fontRole input
      EmittedTone = input.EmittedTone
      ContrastRatio = contrast input
      Flags = derive options input }
