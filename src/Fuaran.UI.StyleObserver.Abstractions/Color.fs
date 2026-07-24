namespace Fuaran.UI.StyleObserver

// ─── Colour + font primitives — the raw evidence types ──────────
//
// `StyleObservation` carries resolved colour evidence (the
// foreground the text paints with, the effective background behind
// it) plus a coarse font classification. These are the small,
// Fable-free value types the rest of the package keys off.
//
// **`Rgba` channels are 0–255; alpha is 0–1.** Matches the browser's
// `getComputedStyle` reporting convention (`rgb(r, g, b)` /
// `rgba(r, g, b, a)`), so the browser observer's parse step is a
// direct field fill with no rescaling. Compositing + WCAG luminance
// (in `Flags.fs`) operate on these units directly.
//
// **`FontRole` is deliberately coarse.** The AI consumer reasons
// about "is this monospaced / serif / sans" — not the exact family
// name. Three named roles plus `Unknown` (font-family absent or
// unclassifiable) keep the wire payload a single short token and
// keep the classifier (substring match in `Flags.fontRole`) trivial
// and locale-independent.

/// A resolved colour. Channels `R`/`G`/`B` are 0–255; `A` (alpha) is
/// 0–1. The browser observer fills this from a parsed
/// `getComputedStyle` colour string; the in-memory observer fills it
/// from a fixture. Compositing produces fractional channel values,
/// so the fields are `float` rather than `int`.
type Rgba =
    { R: float
      G: float
      B: float
      A: float }

module Rgba =
    /// Opaque black — the conventional text default.
    let black: Rgba = { R = 0.0; G = 0.0; B = 0.0; A = 1.0 }

    /// Opaque white — the browser's default canvas colour, used as
    /// the implicit opaque base when an element's background stack
    /// never reaches an opaque layer before `<html>`.
    let white: Rgba =
        { R = 255.0
          G = 255.0
          B = 255.0
          A = 1.0 }

    /// Fully-transparent — `background-color: transparent` /
    /// `rgba(_, _, _, 0)`.
    let transparent: Rgba = { R = 0.0; G = 0.0; B = 0.0; A = 0.0 }

    /// Construct an opaque colour from 0–255 channels.
    let rgb (r: float) (g: float) (b: float) : Rgba = { R = r; G = g; B = b; A = 1.0 }

    /// Construct a colour with explicit alpha (0–1).
    let rgba (r: float) (g: float) (b: float) (a: float) : Rgba = { R = r; G = g; B = b; A = a }

    /// `true` when the colour is fully opaque (alpha ≥ 1.0). The
    /// effective-background composite walk stops at the first such
    /// layer.
    let isOpaque (c: Rgba) : bool = c.A >= 1.0

    let private roundCh (v: float) : int = int (System.Math.Round v)

    /// RGB-equal after rounding channels to the nearest integer — the
    /// comparison used to match a resolved colour against a manifest
    /// palette hex (computed `rgb()` integers and a parsed hex differ
    /// only by sub-unit rounding, if at all). Alpha is ignored (palette
    /// colours + composited effective backgrounds are opaque).
    let sameRgb (a: Rgba) (b: Rgba) : bool =
        roundCh a.R = roundCh b.R
        && roundCh a.G = roundCh b.G
        && roundCh a.B = roundCh b.B

    /// Parse a CSS hex colour (`#rgb`, `#rrggbb`, `#rrggbbaa`) to an
    /// `Rgba`. Returns `None` for anything malformed. Used to compare a
    /// manifest palette's declared hex tokens against resolved fills.
    let tryParseHex (raw: string) : Rgba option =
        if isNull (box raw) then
            None
        else
            let s = raw.Trim().TrimStart('#')

            let hex2 (i: int) =
                match
                    System.Int32.TryParse(
                        s.Substring(i, 2),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, v -> Some(float v)
                | _ -> None

            let hex1 (i: int) =
                match
                    System.Int32.TryParse(
                        string s[i],
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, v -> Some(float (v * 16 + v))
                | _ -> None

            match s.Length with
            | 3 ->
                match hex1 0, hex1 1, hex1 2 with
                | Some r, Some g, Some b -> Some(rgb r g b)
                | _ -> None
            | 6 ->
                match hex2 0, hex2 2, hex2 4 with
                | Some r, Some g, Some b -> Some(rgb r g b)
                | _ -> None
            | 8 ->
                match hex2 0, hex2 2, hex2 4, hex2 6 with
                | Some r, Some g, Some b, Some a -> Some(rgba r g b (a / 255.0))
                | _ -> None
            | _ -> None

    /// Encode as compact JSON — `{"r":R,"g":G,"b":B,"a":A}`. Channels
    /// are F2 invariant-culture floats so a German-locale build emits
    /// `255.00`, never `255,00` (the wire format is JSON, period as
    /// decimal separator).
    let encode (c: Rgba) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture
        let fmt (n: float) = n.ToString("F2", inv)
        $"""{{"r":{fmt c.R},"g":{fmt c.G},"b":{fmt c.B},"a":{fmt c.A}}}"""

/// Coarse font-family classification. The AI consumer reasons about
/// the role, not the exact family — "is this monospaced" matters for
/// interpreting a data table or code block; the precise family name
/// does not. `Unknown` covers an absent font-family or one that
/// matches none of the three roles.
[<RequireQualifiedAccess>]
type FontRole =
    | SansSerif
    | Serif
    | Monospace
    | Unknown

module FontRole =
    /// Stable string discriminator for JSON emission. PascalCase-matched
    /// to the DU cases so AI prompt caches pattern-match the F# surface
    /// and the wire identically.
    let kind (role: FontRole) : string =
        match role with
        | FontRole.SansSerif -> "SansSerif"
        | FontRole.Serif -> "Serif"
        | FontRole.Monospace -> "Monospace"
        | FontRole.Unknown -> "Unknown"
