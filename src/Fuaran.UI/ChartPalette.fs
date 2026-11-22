module Fuaran.UI.ChartPalette

// ============================================================================
//  Phase 2482 — the chart series palette gate.
//
//  A chart's series colours must stay distinct for every reader on a light OR a
//  dark surface, and that is a property of the palette as a SET, measured over
//  ADJACENT pairs (neighbours in the legend and on the canvas). This module is
//  the one implementation of the measurement, so the two places that ask it — the
//  install path in `Fuaran.UI.Charts` (a theme's palette is checked before it is
//  drawn) and the theme manifest's invariant (a designer sees the refusal where
//  the other theme invariants report) — cannot disagree.
//
//  The gate set and its thresholds are the ones the Phase 875 default was
//  validated against (the data-visualisation palette validator):
//
//    * lightness band — OKLCH L inside the surface's band (light 0.43–0.77,
//      dark 0.48–0.67); measured per slot, per surface;
//    * chroma floor   — OKLCH C >= 0.10 (below it a hue reads as grey); per slot;
//    * CVD separation — OKLab ΔE ×100 between ADJACENT slots under simulated
//      protanopia and deuteranopia (Machado, Oliveira & Fernandes 2009 at
//      severity 1.0, applied in linear RGB) must be >= 6.0. The 6–8 band is
//      the validator's WARN band (legal with secondary encoding — every lowered
//      series and pie category carries a visible legend label), so only < 6 is
//      refused;
//    * normal-vision floor — OKLab ΔE ×100 between ADJACENT slots, unsimulated,
//      must be >= 15.0. A hard gate: secondary encoding does not excuse it.
//
//  The CVD and normal-vision gates compare marks to each other, so they do not
//  depend on the surface; the lightness band does, and is measured on both.
//  Contrast against the surface is NOT a hard gate (it is relief-eligible, and
//  the lowering always carries the relief), so it is not refused here.
//
//  The thresholds are calibrated to the Machado simulation: swapping the
//  simulation model would move borderline pairs and require recalibrating them.
//
//  Phase 2655 — the heatmap RAMPS ride the same gate, measured the way a ramp is
//  read rather than the way a palette is. A ramp's stops are ORDERED steps of
//  one quantity, so the categorical lightness band and chroma floor do not apply
//  (a sequential ramp is light-to-dark by design, and its light end reads as
//  near-grey on purpose). What a reader needs from a ramp is:
//
//    * step separation — OKLab ΔE ×100 between ADJACENT stops >= 6.0 under full
//      colour vision (`RampStepFloor`) AND under each simulated deficiency
//      (`CvdFloor`, the categorical calibration reused unchanged), so two
//      neighbouring bins never merge for any reader;
//    * label contrast — the WCAG 2.x contrast ratio of each stop against the
//      ramp's LABEL TONE (the literal ink a cell's value label is drawn in) is
//      >= 4.5, the small-text AA floor, so every cell's value is legible
//      whichever bin it fell in;
//    * shape — at least two stops, at most `MaxRampStops`, and a DIVERGING ramp
//      has an odd count, so its centre stop is the midpoint's own colour.
//
//  A ramp that fails is refused by name — the ramp, the stop pair or the stop —
//  never degraded: the install path keeps the shipped default instead.
//
//  `FSharp.Core`-only and Fable-portable: the colour maths is written out rather
//  than taken from a platform API, so a browser host and a server host measure
//  the same palette to the same bits.
// ============================================================================

open Fuaran.UI.Types

/// A surface a palette is measured against, with its OKLCH lightness band.
type Surface =
    { Name: string
      Hex: string
      LightnessMin: float
      LightnessMax: float }

/// The light chart surface (`#fcfcfb`) and its band.
let light: Surface =
    { Name = "light"
      Hex = "#fcfcfb"
      LightnessMin = 0.43
      LightnessMax = 0.77 }

/// The dark chart surface (`#1a1a19`) and its band.
let dark: Surface =
    { Name = "dark"
      Hex = "#1a1a19"
      LightnessMin = 0.48
      LightnessMax = 0.67 }

/// Both surfaces: a theme-invariant palette must clear the band on each.
let surfaces: Surface list = [ light; dark ]

/// OKLCH chroma below which a hue reads as grey.
[<Literal>]
let ChromaFloor = 0.10

/// Adjacent-pair OKLab ΔE ×100 under protan / deutan simulation below which a
/// pair is refused.
[<Literal>]
let CvdFloor = 6.0

/// Adjacent-pair OKLab ΔE ×100 under unsimulated vision below which a pair is
/// refused.
[<Literal>]
let NormalVisionFloor = 15.0

/// The most series slots a categorical palette may carry. A ninth series is
/// never a generated hue: it folds into "Other", small multiples or faceting.
[<Literal>]
let MaxSlots = 8

/// Adjacent-stop OKLab ΔE ×100 under full colour vision below which a ramp step
/// is refused (Phase 2655). The simulated floor is `CvdFloor`.
[<Literal>]
let RampStepFloor = 6.0

/// WCAG 2.x contrast ratio of a ramp stop against the ramp's label tone below
/// which the stop is refused (Phase 2655) — the small-text AA floor.
[<Literal>]
let RampLabelContrastFloor = 4.5

/// The most stops a ramp may carry (Phase 2655). A bin is a stop, so this bounds
/// the classes a reader is asked to tell apart.
[<Literal>]
let MaxRampStops = 9

/// The colour-vision deficiency a pair was simulated under.
[<RequireQualifiedAccess>]
type CvdKind =
    | Protan
    | Deutan

/// Why a palette was refused. Every case names the slot or the pair it is about,
/// so the refusal says what to change, not merely that something is wrong.
/// Series slots are 1-based, matching the `--fuaran-chart-series-N` names.
[<RequireQualifiedAccess>]
type PaletteFinding =
    /// The palette has no series slot at all.
    | Empty
    /// The palette carries more slots than a categorical palette may.
    | TooManySlots of count: int * max: int
    /// A slot is not a `#rrggbb` literal, so it cannot be measured (and the
    /// lowering, which bakes literal colour into the drawing, cannot draw it).
    | NotLiteral of slot: string * value: string
    /// A series slot sits outside a surface's lightness band.
    | OutOfLightnessBand of slot: int * hex: string * surface: string * lightness: float * min: float * max: float
    /// A series slot's chroma is below the floor — it reads as grey.
    | BelowChromaFloor of slot: int * hex: string * chroma: float
    /// Two adjacent series slots are too close under a simulated deficiency.
    | CvdTooClose of slotA: int * hexA: string * slotB: int * hexB: string * kind: CvdKind * deltaE: float
    /// Two adjacent series slots are too close under unsimulated vision.
    | NormalVisionTooClose of slotA: int * hexA: string * slotB: int * hexB: string * deltaE: float
    /// Phase 2655 — a ramp has the wrong number of stops: fewer than two, more
    /// than `MaxRampStops`, or an even count on a diverging ramp (no centre stop).
    | RampShape of ramp: string * stops: int * requirement: string
    /// Phase 2655 — two adjacent ramp stops (1-based) are too close, under full
    /// colour vision (`kind = None`) or a simulated deficiency.
    | RampStepTooClose of
        ramp: string *
        stopA: int *
        hexA: string *
        stopB: int *
        hexB: string *
        kind: CvdKind option *
        deltaE: float
    /// Phase 2655 — a ramp stop (1-based) does not clear the contrast floor
    /// against the ramp's label tone.
    | RampLabelContrast of ramp: string * stop: int * hex: string * label: string * ratio: float

// ─── Colour maths ──────────────────────────────────────────────────────────

let private nibble (c: char) : int option =
    if c >= '0' && c <= '9' then Some(int c - int '0')
    elif c >= 'a' && c <= 'f' then Some(int c - int 'a' + 10)
    elif c >= 'A' && c <= 'F' then Some(int c - int 'A' + 10)
    else None

/// Parse a `#rrggbb` literal (the `#` is required, case-insensitive) to sRGB
/// channels in [0, 1]. `None` for anything else, including `#rgb` and alpha
/// forms: the gate measures exactly the literal the lowering will draw.
let tryParseHex (value: string) : (float * float * float) option =
    if value.Length <> 7 || value[0] <> '#' then
        None
    else
        let digits = [ for i in 1..6 -> nibble value[i] ]

        if digits |> List.forall Option.isSome then
            let d = digits |> List.map Option.get |> List.toArray
            let ch i = float (d[i] * 16 + d[i + 1]) / 255.0
            Some(ch 0, ch 2, ch 4)
        else
            None

let private toLinear (c: float) : float =
    if c <= 0.04045 then
        c / 12.92
    else
        ((c + 0.055) / 1.055) ** 2.4

let private cbrt (x: float) : float =
    if x >= 0.0 then
        x ** (1.0 / 3.0)
    else
        -((-x) ** (1.0 / 3.0))

let private clamp01 (x: float) : float = max 0.0 (min 1.0 x)

/// OKLab (L, a, b) of a linear-RGB triple.
let private oklabOfLinear (r: float, g: float, b: float) : float * float * float =
    let l = cbrt (0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
    let m = cbrt (0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
    let s = cbrt (0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)

    (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
     1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
     0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)

let private linearOf (r: float, g: float, b: float) = (toLinear r, toLinear g, toLinear b)

/// Machado, Oliveira & Fernandes (2009) at severity 1.0, in linear RGB.
let private simulate (kind: CvdKind) (r: float, g: float, b: float) : float * float * float =
    let row (m0: float, m1: float, m2: float) = clamp01 (m0 * r + m1 * g + m2 * b)

    match kind with
    | CvdKind.Protan ->
        (row (0.152286, 1.052583, -0.204868), row (0.114503, 0.786281, 0.099216), row (-0.003882, -0.048116, 1.051998))
    | CvdKind.Deutan ->
        (row (0.367322, 0.860646, -0.227968), row (0.280085, 0.672501, 0.047413), row (-0.011820, 0.042940, 0.968881))

/// OKLCH lightness and chroma of an sRGB triple.
let lightnessAndChroma (rgb: float * float * float) : float * float =
    let lightness, a, b = oklabOfLinear (linearOf rgb)
    lightness, sqrt (a * a + b * b)

/// OKLab ΔE ×100 between two sRGB triples, optionally under a simulated
/// deficiency.
let deltaE (kind: CvdKind option) (x: float * float * float) (y: float * float * float) : float =
    let lab rgb =
        let lin = linearOf rgb

        oklabOfLinear (
            match kind with
            | Some k -> simulate k lin
            | None -> lin
        )

    let l1, a1, b1 = lab x
    let l2, a2, b2 = lab y
    100.0 * sqrt ((l1 - l2) ** 2.0 + (a1 - a2) ** 2.0 + (b1 - b2) ** 2.0)

// ─── The gate ──────────────────────────────────────────────────────────────

/// The WCAG 2.x relative luminance of an sRGB triple.
let relativeLuminance (rgb: float * float * float) : float =
    let r, g, b = linearOf rgb
    0.2126 * r + 0.7152 * g + 0.0722 * b

/// The WCAG 2.x contrast ratio between two sRGB triples, >= 1.
let contrastRatio (x: float * float * float) (y: float * float * float) : float =
    let lx = relativeLuminance x
    let ly = relativeLuminance y
    (max lx ly + 0.05) / (min lx ly + 0.05)

/// The literal a `ColorVar` carries when it is a `#rrggbb` hex, else `None`.
let hexOf (color: ColorVar) : string option =
    match color with
    | ColorVar.Hex h when (tryParseHex h).IsSome -> Some h
    | _ -> None

let private describeColorVar (color: ColorVar) : string =
    match color with
    | ColorVar.Hex h -> h
    | ColorVar.OKLCH(l, c, h, a) -> sprintf "oklch(%g %g %g / %g)" l c h a
    | ColorVar.CssRaw raw -> raw

/// Measure a series palette against every hard gate on both surfaces. `[]` is
/// a pass. A slot that is not a literal is refused by name and left out of the
/// measured gates (it cannot be measured), so every finding is about a real pair.
let validateSeries (series: ColorVar list) : PaletteFinding list =
    let count = List.length series

    if count = 0 then
        [ PaletteFinding.Empty ]
    elif count > MaxSlots then
        [ PaletteFinding.TooManySlots(count, MaxSlots) ]
    else
        let indexed = series |> List.mapi (fun i c -> i + 1, c)

        let notLiteral =
            indexed
            |> List.choose (fun (slot, c) ->
                match hexOf c with
                | Some _ -> None
                | None -> Some(PaletteFinding.NotLiteral($"series-{slot}", describeColorVar c)))

        if not notLiteral.IsEmpty then
            notLiteral
        else
            let slots =
                indexed
                |> List.map (fun (slot, c) ->
                    let hex = (hexOf c).Value
                    slot, hex, (tryParseHex hex).Value)

            let band =
                [ for surface in surfaces do
                      for slot, hex, rgb in slots do
                          let lightness, _ = lightnessAndChroma rgb

                          if lightness < surface.LightnessMin || lightness > surface.LightnessMax then
                              PaletteFinding.OutOfLightnessBand(
                                  slot,
                                  hex,
                                  surface.Name,
                                  lightness,
                                  surface.LightnessMin,
                                  surface.LightnessMax
                              ) ]

            let chroma =
                [ for slot, hex, rgb in slots do
                      let _, chroma = lightnessAndChroma rgb

                      if chroma < ChromaFloor then
                          PaletteFinding.BelowChromaFloor(slot, hex, chroma) ]

            let pairs = List.pairwise slots

            let cvd =
                [ for (sa, ha, ra), (sb, hb, rb) in pairs do
                      // The worse of the two simulations is the one a reader meets.
                      let worst =
                          [ CvdKind.Protan; CvdKind.Deutan ]
                          |> List.map (fun k -> k, deltaE (Some k) ra rb)
                          |> List.minBy snd

                      match worst with
                      | kind, d when d < CvdFloor -> PaletteFinding.CvdTooClose(sa, ha, sb, hb, kind, d)
                      | _ -> () ]

            let normal =
                [ for (sa, ha, ra), (sb, hb, rb) in pairs do
                      let d = deltaE None ra rb

                      if d < NormalVisionFloor then
                          PaletteFinding.NormalVisionTooClose(sa, ha, sb, hb, d) ]

            band @ chroma @ cvd @ normal

/// Measure a whole chart token group: the series palette through every gate,
/// and the reserved status triple for literal-ness only. The triple is semantic
/// (gain / loss / baseline) rather than categorical — a neutral is grey by
/// design — so the categorical gates do not apply to it; but the lowering draws
/// it as literal colour, so a non-literal is refused like a series slot.
let validate (tokens: ChartTokens) : PaletteFinding list =
    let status =
        [ "positive", tokens.Positive
          "negative", tokens.Negative
          "neutral", tokens.Neutral ]
        |> List.choose (fun (slot, c) ->
            match hexOf c with
            | Some _ -> None
            | None -> Some(PaletteFinding.NotLiteral(slot, describeColorVar c)))

    validateSeries tokens.Series @ status

/// Phase 2655 — measure one heatmap ramp: its shape, every adjacent stop pair
/// under full and simulated colour vision, and every stop against the label tone
/// its cells' value labels are drawn in. `ramp` names it in the findings
/// (`"sequential"` / `"diverging"`); `diverging` demands an odd stop count. `[]`
/// is a pass. A non-literal stop (or label tone) is refused by name and the
/// measured gates are skipped, as `validateSeries` does.
let validateRamp (ramp: string) (diverging: bool) (stops: string list) (labelTone: string) : PaletteFinding list =
    let count = List.length stops

    if count < 2 then
        [ PaletteFinding.RampShape(ramp, count, "at least 2 stops") ]
    elif count > MaxRampStops then
        [ PaletteFinding.RampShape(ramp, count, $"at most {MaxRampStops} stops") ]
    elif diverging && count % 2 = 0 then
        [ PaletteFinding.RampShape(ramp, count, "an odd number of stops, so the midpoint has a centre stop") ]
    else
        let indexed = stops |> List.mapi (fun i hex -> i + 1, hex)

        let notLiteral =
            [ for stop, hex in indexed do
                  if (tryParseHex hex).IsNone then
                      PaletteFinding.NotLiteral($"{ramp}-ramp-{stop}", hex)
              if (tryParseHex labelTone).IsNone then
                  PaletteFinding.NotLiteral($"{ramp}-ramp-label", labelTone) ]

        if not notLiteral.IsEmpty then
            notLiteral
        else
            let measured =
                indexed |> List.map (fun (stop, hex) -> stop, hex, (tryParseHex hex).Value)

            let label = (tryParseHex labelTone).Value

            let steps =
                [ for (sa, ha, ra), (sb, hb, rb) in List.pairwise measured do
                      let normal = deltaE None ra rb

                      if normal < RampStepFloor then
                          PaletteFinding.RampStepTooClose(ramp, sa, ha, sb, hb, None, normal)

                      // The worse of the two simulations is the one a reader meets.
                      let kind, worst =
                          [ CvdKind.Protan; CvdKind.Deutan ]
                          |> List.map (fun k -> k, deltaE (Some k) ra rb)
                          |> List.minBy snd

                      if worst < CvdFloor then
                          PaletteFinding.RampStepTooClose(ramp, sa, ha, sb, hb, Some kind, worst) ]

            let contrast =
                [ for stop, hex, rgb in measured do
                      let ratio = contrastRatio rgb label

                      if ratio < RampLabelContrastFloor then
                          PaletteFinding.RampLabelContrast(ramp, stop, hex, labelTone, ratio) ]

            steps @ contrast

let private cvdName (kind: CvdKind) : string =
    match kind with
    | CvdKind.Protan -> "protanopia"
    | CvdKind.Deutan -> "deuteranopia"

/// A one-sentence, slot- or pair-naming account of a finding.
let describe (finding: PaletteFinding) : string =
    match finding with
    | PaletteFinding.Empty -> "the chart series palette has no slots"
    | PaletteFinding.TooManySlots(count, max) ->
        $"the chart series palette has {count} slots; a categorical palette carries at most {max} (a further series folds into Other or small multiples, never a generated hue)"
    | PaletteFinding.NotLiteral(slot, value) ->
        $"chart {slot} is '{value}', not a #rrggbb literal: the palette gates measure, and the lowering draws, literal colour"
    | PaletteFinding.OutOfLightnessBand(slot, hex, surface, lightness, lo, hi) ->
        sprintf
            "chart series-%d %s on %s is outside the lightness band: OKLCH L %.3f, band %.2f-%.2f"
            slot
            hex
            surface
            lightness
            lo
            hi
    | PaletteFinding.BelowChromaFloor(slot, hex, chroma) ->
        sprintf "chart series-%d %s reads as grey: OKLCH C %.3f, floor %.2f" slot hex chroma ChromaFloor
    | PaletteFinding.CvdTooClose(sa, ha, sb, hb, kind, d) ->
        sprintf
            "adjacent chart series-%d %s and series-%d %s are indistinguishable under %s: OKLab dE %.1f, floor %.0f"
            sa
            ha
            sb
            hb
            (cvdName kind)
            d
            CvdFloor
    | PaletteFinding.NormalVisionTooClose(sa, ha, sb, hb, d) ->
        sprintf
            "adjacent chart series-%d %s and series-%d %s are too close under full colour vision: OKLab dE %.1f, floor %.0f"
            sa
            ha
            sb
            hb
            d
            NormalVisionFloor
    | PaletteFinding.RampShape(ramp, stops, requirement) ->
        $"the {ramp} chart ramp has {stops} stops; a ramp needs {requirement}"
    | PaletteFinding.RampStepTooClose(ramp, sa, ha, sb, hb, kind, d) ->
        let vision, floor =
            match kind with
            | Some k -> "under " + cvdName k, CvdFloor
            | None -> "under full colour vision", RampStepFloor

        sprintf
            "adjacent %s ramp stops %d %s and %d %s are too close %s: OKLab dE %.1f, floor %.0f"
            ramp
            sa
            ha
            sb
            hb
            vision
            d
            floor
    | PaletteFinding.RampLabelContrast(ramp, stop, hex, label, ratio) ->
        sprintf
            "%s ramp stop %d %s does not carry a %s value label: contrast %.2f:1, floor %.1f:1"
            ramp
            stop
            hex
            label
            ratio
            RampLabelContrastFloor
