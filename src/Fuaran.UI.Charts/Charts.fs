module Fuaran.UI.Charts

// ============================================================================
//  Phase 526 — render-time Chart → Drawing lowering (bar / line, S3).
//  Phase 637 — stacked series (bar / area) + the Area arm.
//  Phase 636 — the Scatter arm (linear numeric x-scale, point marks).
//  Phase 638 — the Pie arm (polar, cubic-approximated wedges; donut variant
//              deferred to the next wire event — see the phase file).
//  Phase 885 — `ChartStyle`: the styling surface as a LOWERING PARAMETER.
//              Every styling constant this module used to bake inline now reads
//              from a `ChartStyle` record threaded through the lowering.
//              Style is NEVER a `ChartSpec` wire field (D8).
//  Phase 875 — default chart style v2: the visible restyle. `ChartStyle.defaults`
//              MOVED (palette, axis chrome, mark geometry, title size) and the
//              `chart-lowering/*` goldens were regenerated once for the whole
//              change-set. `defaults` remains the corpus-pinned form — it is no
//              longer the pre-885 form.
//  Phase 876 — AXIS NUMBER FORMATTING: the canonical invariant formatter +
//              display-unit scaling. Ticks no longer print raw floats; see
//              "The canonical invariant number formatter" below, which is the
//              normative cross-host spec every conformant host reproduces.
//  Phase 879 — DETERMINISTIC TEXT METRICS: a pinned per-character advance-width
//              table (`TextMetrics`) that no host queries a font for. It drives
//              the four layout decisions that were previously blind — legend
//              pitch, left-margin autosize, the 30°-tilt→90°-vertical
//              escalation for category labels, and the bottom margin the tilt
//              needs — and exposes the fit predicate Phase 881's data labels
//              will gate on. See "Deterministic text metrics" below, the
//              normative cross-host spec.
//
//  `Chart` stays a SEMANTIC wire kind (D2). This module is the bounded layout
//  engine that turns a resolved `ChartSpec` + data rows into a canonical
//  `DrawingSpec` subtree (scales, ticks, axes, gridlines, legend, series
//  geometry) — so a chart renders as first-party inline SVG (Phase 525) on
//  every host, headless included, and a new chart type is a lowering rule +
//  fixtures rather than bespoke per-host drawing (D3).
//
//  Deterministic (R2): a fixed pixel viewBox, a `{1,2,5}·10ⁿ` nice-tick rule,
//  and round-half-up coordinate rounding to 2 dp, so the output depends only on
//  the ChartSpec + data (never on enumeration order or platform float print).
//  The F# output IS the golden the other hosts conform to (Phase 527); the
//  `chart-lowering/*` corpus fixtures pin it.
//
//  Fable-portable (FSharp.Core + Renderer.Core only; no reflection).
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Renderer

// Record labels do not flow through the Types.fs abbreviations (stage 3 of the
// 692-694 swap), so DrawPoint literals build through this annotated helper.
let inline private dp (x: float) (y: float) : DrawPoint = { X = x; Y = y }

// ─── The styling surface (Phase 885) ─────────────────────────────────────────
//
// Every styling constant the lowering used to bake inline lives here, in ONE
// typed record threaded through the lowering — the `ChartLimits` precedent
// (Phase 790) applied to appearance rather than cost. Two postures follow from
// D8 and are load-bearing:
//
//   * Style is a LOWERING PARAMETER, never a `ChartSpec` wire field. A theme
//     flip, a brand palette, or a house typography choice is the host's, made
//     at render time; it must not rewrite a semantic node (D2/D6).
//   * `ChartStyle.defaults` is CORPUS-PINNED — the `chart-lowering/*` goldens,
//     and the cross-host parity they certify (R2), are its projection. Changing
//     a default value is therefore a corpus event: regenerate the goldens and
//     move every conformant host in the same change-set (Phase 875 did exactly
//     that). A host passing its own style is a deliberate act off the
//     conformance path, and its output is its own.

/// Where the chart title sits along the plot's top edge.
[<RequireQualifiedAccess>]
type ChartTitleAlignment =
    /// Flush with the plot's left edge (the shipped default).
    | Left
    /// Centred over the plot area.
    | Centre
    /// Flush with the plot's right edge.
    | Right

/// Which edge the series legend occupies.
///
/// **Reserved — not yet consumed.** The shipped lowering draws the legend as a
/// single horizontal row in the top margin regardless of this field. Phase 875
/// (which this comment previously named as the phase that would wire it) did
/// the palette / chrome / geometry restyle and deliberately left legend LAYOUT
/// alone — so the row still overflows the canvas past six entries, which the
/// 8-slot palette v2 now makes reachable. Positioning and overflow are one
/// problem and land together in a later phase; the default records the
/// 2026-08-16 operator decision so the field is already right when it does.
[<RequireQualifiedAccess>]
type ChartLegendPosition =
    | Top
    | Right
    | Bottom
    | Left

/// How a value axis states its DISPLAY UNIT once a large magnitude has been
/// scaled by a power of ten (Phase 876).
///
/// The operator's doctrine: ticks stay short and comparable, and the unit is
/// stated ONCE — so the axis reads `5` `10` `15` under a single "Millions"
/// label rather than `5,000,000` on every gridline. Compact-per-tick (`12K` on
/// each tick) is the deliberate opt-out from that doctrine, never the default.
[<RequireQualifiedAccess>]
type ChartAxisUnitMode =
    /// One word in the axis-unit slot — "Thousands" / "Millions" / … (the
    /// shipped default).
    | Words
    /// The word plus the value format's unit symbol — "Millions of £". Falls
    /// back to `Words` when the spec declares no `Format.Currency`. The ticks
    /// then DROP the currency symbol: the unit is stated once, in the label.
    | WordsWithSymbol
    /// The SI prefix plus the unit symbol — "M£" (or bare "M"). Same
    /// symbol-drops-from-the-ticks rule as `WordsWithSymbol`.
    | SIAbbreviation
    /// No axis-unit label at all: every tick carries its own compact suffix
    /// (`12K`, `4M`). The opt-in mode — short ticks at the cost of repeating
    /// the unit `TargetTickCount` times.
    | CompactPerTick
    /// Never scale; every tick prints its full magnitude.
    | Off

/// The complete styling surface of a lowering: canvas, palette, ink, typography,
/// tick + axis-label geometry, series geometry, legend geometry, and the pie
/// arm's polar geometry — plus the reserved slots named in their doc comments.
type ChartStyle =
    {
        // ── Canvas (the fixed canonical drawing space) ──
        /// Drawing-space width (the viewBox width).
        Width: float
        /// Drawing-space height (the viewBox height).
        Height: float
        /// Top margin — the title + legend band.
        MarginTop: float
        /// Right margin.
        MarginRight: float
        /// Bottom margin — x-axis category labels + the x-axis title. Since
        /// Phase 879 this is the **floor**, not the value: a tilted or vertical
        /// category label needs room to fall into, so the lowering autosizes
        /// between this floor and `MarginBottomMaxShare · Height`.
        MarginBottom: float
        /// Left margin — right-aligned y-axis tick labels. Since Phase 879 this
        /// is the **floor**, not the value: the lowering autosizes between it
        /// and `MarginLeftMaxShare · Width` from the widest FORMATTED tick
        /// label, so a seven-digit tick is no longer clipped by a fixed 64 px.
        MarginLeft: float
        /// Ceiling on the autosized left margin, as a share of `Width`. A
        /// pathological tick column is truncated with a deterministic ellipsis
        /// rather than eating the plot.
        MarginLeftMaxShare: float
        /// Ceiling on the autosized bottom margin, as a share of `Height`.
        /// Same posture: category labels truncate rather than eat the plot.
        MarginBottomMaxShare: float
        /// Breathing room between an autosized margin's content and the canvas
        /// edge (or the axis title beyond it). Also absorbs the few percent by
        /// which a real font differs from the `TextMetrics` table.
        AxisLabelPadding: float

        // ── Series palette ──
        /// The categorical palette, indexed by series (or, on the Pie arm, by
        /// category) modulo its length. Series colours stay literal hex: they
        /// must stay distinct AND read on a light or a dark surface, so they
        /// cannot ink from `currentColor` the way the chrome does (D8).
        ///
        /// The shipped default is the Phase 875 validated 8-slot set — ONE hex
        /// set that clears every hard gate on BOTH surfaces, which is what D8's
        /// theme-invariance demands (a per-theme palette would make a series
        /// colour a host-theme function, and the goldens carry one hex). The
        /// ASSIGNMENT ORDER is load-bearing: the CVD and normal-vision gates are
        /// measured over ADJACENT pairs, so re-ordering the array can drop a
        /// passing set below the floor. Do not cycle or sort it.
        Palette: string[]

        // ── Surface-relative ink (Phase 536 — theme-aware chart lowering, S4) ──
        //
        // Structural + text ink is `currentColor` at a per-role opacity, so a
        // lowered chart inks from the surface's own text colour and is legible
        // on a light OR a dark surface without a CSS override (the rest of the
        // renderer already themes colour via inherited CSS — this lowering was
        // the lone place that baked literal hex). On a white surface with
        // near-black text the default opacities reproduce the pre-536 palette
        // within rounding: 0.12 ≈ `#e0e0e0` (grid), 0.66 ≈ `#555` (labels),
        // 0.8 ≈ `#333` (axis); titles ink full-strength (no opacity).
        /// The chrome's ink source — `currentColor` by default, so axes,
        /// gridlines and labels inherit the surface's own text colour.
        Ink: string
        /// Per-role opacity for the axis spines.
        AxisOpacity: float
        /// Per-role opacity for the gridlines.
        GridOpacity: float
        /// Per-role opacity for tick / category / legend text.
        LabelOpacity: float
        /// Stroke width of the axis spines.
        AxisStrokeWidth: float
        /// Stroke width of the gridlines.
        GridStrokeWidth: float
        /// Stroke width of a series line (Line, and an Area band's edge).
        SeriesStrokeWidth: float

        // ── Typography ──
        /// The chart's own font stack — carried in the wire (Phase 528.1), so a
        /// lowered chart is self-contained + legible on every host without
        /// host CSS.
        FontFamily: string
        /// Font size of tick labels, category labels, axis titles and legend text.
        TickFontSize: float
        /// Font size of the visible chart title.
        TitleFontSize: float
        /// A line's height as a multiple of its font size (Phase 879) — the
        /// vertical half of the `TextMetrics` estimate, used for the tilt
        /// escalation's along-axis footprint and for `TextMetrics.fitsBox`.
        TextLineHeightFactor: float
        /// Where the chart title sits along the plot's top edge.
        TitleAlignment: ChartTitleAlignment
        /// Baseline y of the visible chart title.
        TitleBaselineY: float

        // ── Ticks + axis labels ──
        /// Target number of y-axis ticks the `{1,2,5}·10ⁿ` nice-tick rule aims
        /// for (the gridline count follows).
        TargetTickCount: float
        /// Gap between the y-axis spine and the right edge of a tick label.
        /// Clears `TickMarkLength` — the tick mark occupies the first stretch of
        /// that gap — so widening the mark without widening this crowds the
        /// number column into it.
        TickLabelGap: float
        /// Length of the small OUTSIDE tick marks on both axes (Phase 875):
        /// y-axis marks run left from the spine, x-axis marks run down from it,
        /// so neither eats plot area. Inked at axis strength, one per y tick and
        /// one per category band centre (or per x tick on the Scatter arm).
        /// `0.0` suppresses them.
        TickMarkLength: float
        /// Baseline nudge that optically centres a tick label on its gridline.
        TickLabelBaselineDy: float
        /// Drop from the x-axis spine to the category / x-tick label baseline.
        CategoryLabelOffsetY: float
        /// The MAGNITUDE of the tilt applied to band-arm category labels
        /// (Phase 879), in degrees. Tilt is the DEFAULT state — operator
        /// doctrine: it is for LEGIBILITY, not a crowding fallback — so every
        /// category label is tilted, and the lowering escalates to
        /// `VerticalTiltDegrees` when the tilted labels no longer pack into the
        /// band pitch. The lowering emits `DrawStyle.Rotation = -tilt`:
        /// `Rotation` is clockwise (SVG's convention), and the tilt has to be
        /// COUNTER-clockwise so the text falls AWAY from the axis rather than
        /// climbing into the plot. `0.0` opts out entirely — labels stay
        /// horizontal and `Middle`-anchored, and no escalation is considered.
        LabelTiltDegrees: float
        /// The vertical arm of the escalation (Phase 879). At 90° a label
        /// occupies one line-height along the axis whatever its length, so a
        /// vertical axis packs at any category count. Emitted with the same
        /// negative sign as the tilt, so the text reads bottom-up — the
        /// convention the y-axis title already uses.
        VerticalTiltDegrees: float
        // ── Display units (Phase 876) ──
        /// How the value axis states its display unit once scaling applies.
        AxisUnitMode: ChartAxisUnitMode
        /// The smallest unit exponent that TRIGGERS display-unit scaling — the
        /// operator's `unit > 3` gate, expressed as the first admissible unit.
        /// At the shipped default (`6`) scaling begins at MILLIONS, so a
        /// thousands-range axis still reads `12,500` in full; a host that wants
        /// thousands scaling sets `3` and gets `12.5` under a "Thousands"
        /// label. `CompactPerTick` is exempt — repeating a suffix per tick is
        /// only worth doing from thousands up, so it gates at `3` regardless.
        /// The admissible exponents are the prefix table's own: 3, 6, 9, 12, 15.
        DisplayUnitMinExponent: int

        /// Distance from the canvas bottom to the x-axis title's baseline.
        AxisTitleBottomOffset: float
        /// x of the y-axis title (left-anchored, above the plot).
        AxisTitleLeftX: float
        /// Rise from the plot's top edge to the y-axis title's baseline.
        AxisTitleTopOffset: float

        // ── Series geometry ──
        /// Share of a category band the bar group occupies (the rest is air).
        BarGroupWidthFraction: float
        /// Share of its own slot a single bar occupies (the rest separates
        /// neighbouring bars).
        BarWidthFraction: float
        /// Hard pixel ceiling on a single bar's thickness (Phase 875). The bar
        /// takes the MIN of its band share and this cap, and is then centred in
        /// its slot — so a chart with three categories gets three bars with air
        /// around them rather than three slabs. Uncapped band-share alone made
        /// bar thickness a function of category COUNT, which carries no meaning.
        /// `infinity` restores the pre-875 uncapped behaviour.
        BarMaxThickness: float
        /// GEOMETRIC gap between consecutive segments of a stacked bar
        /// (Phase 875) — the segment is shortened on the side facing the next
        /// segment, so the separation is absence of ink, not a surface-coloured
        /// stroke. A stroke would need to know the surface colour and would
        /// therefore stop being theme-invariant; a gap never does. The topmost
        /// segment keeps its full height, so the stack total stays honest.
        StackSegmentGap: float
        /// GEOMETRIC angular padding between pie wedges, in DEGREES
        /// (Phase 875) — half is taken from each end of every wedge's sweep, for
        /// the same reason `StackSegmentGap` is a gap rather than a stroke. A
        /// wedge whose sweep is narrower than the padding is dropped rather than
        /// inverted. A lone full-circle category is unaffected.
        WedgeGapDegrees: float
        /// Opacity of an area band's translucent fill. The gridlines stay
        /// legible through the band; the full-strength Polyline edge on top
        /// carries the categorical colour at full contrast. Phase 875 dropped
        /// this to a wash: at 0.35 two overlaid bands read as a third colour and
        /// the chrome beneath them disappears.
        AreaFillOpacity: float
        /// Radius of a Scatter point mark.
        ScatterPointRadius: float

        // ── Legend geometry (the cartesian arms' horizontal top-margin row) ──
        /// Which edge the legend occupies. **Reserved — not yet consumed** (see
        /// `ChartLegendPosition`).
        LegendPosition: ChartLegendPosition
        /// Horizontal padding after a legend entry's label, before the next
        /// entry's swatch (Phase 879). The pitch itself is no longer a
        /// constant: an entry occupies `LegendLabelOffsetX + the estimated
        /// width of its series name + LegendEntryGap`, so a 30-character name
        /// pushes its neighbour along instead of being written over by it.
        /// (The retired `LegendPitchX` was a flat 100 px, which collided on any
        /// name past ~12 characters.)
        LegendEntryGap: float
        /// Top y of a legend swatch.
        LegendSwatchY: float
        /// Side length of a (square) legend swatch.
        LegendSwatchSize: float
        /// Corner radius of a legend swatch.
        LegendSwatchCornerRadius: float
        /// Gap from a swatch's left edge to its label's left edge.
        LegendLabelOffsetX: float
        /// Baseline y of a legend label.
        LegendLabelBaselineY: float

        // ── Pie geometry (the polar arm) ──
        /// Wedge radius.
        PieRadius: float
        /// Inset from the canvas right edge to the pie legend's swatch column.
        PieLegendOffsetX: float
        /// Top y of the pie legend's first row.
        PieLegendTopY: float
        /// Vertical pitch between pie legend rows.
        PieLegendPitchY: float
        /// Baseline nudge from a pie legend row's top to its label baseline.
        PieLegendLabelBaselineDy: float

        // ── Status triple (reserved) ──
        //
        // A semantic triple DISTINCT from the categorical series palette: it
        // encodes meaning (good / bad / neutral), not identity, so it must not
        // be drawn from the palette's rotation. Reserved here for the variance
        // and waterfall arms; **no shipped lowering path reads these three
        // fields**, and a host setting them changes nothing today.
        /// Reserved — favourable variance / gain.
        PositiveColour: string
        /// Reserved — adverse variance / loss.
        NegativeColour: string
        /// Reserved — no-change / baseline.
        NeutralColour: string
    }

module ChartStyle =
    /// The shipped default style — **corpus-pinned**. The `chart-lowering/*`
    /// goldens are this record's projection, and every conformant host
    /// reproduces it, so changing a value here is a corpus event: regenerate the
    /// fixtures and move the other hosts in the same change-set. Phase 875 is
    /// the restyle that did so; Phase 885 is where the fields came from.
    let defaults: ChartStyle =
        { Width = 640.0
          Height = 400.0
          MarginTop = 64.0
          MarginRight = 28.0
          MarginBottom = 56.0
          MarginLeft = 64.0
          // The autosize ceilings (Phase 879). 30 % / 35 % leave the plot the
          // clear majority of the canvas in the worst case, and a chart whose
          // labels want more than that has a data problem the layout should
          // report by truncating, not absorb by shrinking the picture.
          MarginLeftMaxShare = 0.3
          MarginBottomMaxShare = 0.35
          AxisLabelPadding = 6.0
          // Phase 875 palette v2 — 8 slots, fixed assignment order. Validated on
          // BOTH surfaces (light #fcfcfb, dark #1a1a19) against the OKLab gate
          // set: lightness band, chroma floor, adjacent-pair CVD ΔE (protan +
          // deutan, Machado 2009 at severity 1.0), adjacent-pair normal-vision
          // ΔE. Every slot sits in the INTERSECTION of the two lightness bands
          // (OKLCH L 0.48–0.67), which is what lets one hex set serve both
          // themes. Slot 1 is the brand loch hue (OKLCH h ≈ 228) with its chroma
          // lifted to clear the 0.10 floor — the brand hex itself reads as grey
          // to the gate. Predecessor (the 2008 Google Charts default) failed
          // contrast in both themes and CVD-separated poorly.
          Palette =
            [| "#1a86ac" // loch blue
               "#bf831c" // ochre
               "#a51574" // magenta
               "#21a766" // green
               "#6454e5" // violet
               "#af153d" // crimson
               "#21a2b2" // teal
               "#d3241b" |] // vermilion
          Ink = "currentColor"
          AxisOpacity = 0.8
          GridOpacity = 0.12
          LabelOpacity = 0.66
          AxisStrokeWidth = 1.0
          GridStrokeWidth = 1.0
          SeriesStrokeWidth = 2.0
          FontFamily = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
          TickFontSize = 13.0
          TitleFontSize = 18.0
          TextLineHeightFactor = 1.2
          TitleAlignment = ChartTitleAlignment.Left
          TitleBaselineY = 22.0
          TargetTickCount = 5.0
          TickLabelGap = 12.0
          TickMarkLength = 5.0
          TickLabelBaselineDy = 4.0
          CategoryLabelOffsetY = 20.0
          LabelTiltDegrees = 30.0
          VerticalTiltDegrees = 90.0
          AxisUnitMode = ChartAxisUnitMode.Words
          DisplayUnitMinExponent = 6
          AxisTitleBottomOffset = 12.0
          AxisTitleLeftX = 8.0
          AxisTitleTopOffset = 12.0
          BarGroupWidthFraction = 0.7
          BarWidthFraction = 0.9
          BarMaxThickness = 28.0
          StackSegmentGap = 2.0
          WedgeGapDegrees = 0.75
          AreaFillOpacity = 0.12
          ScatterPointRadius = 4.0
          LegendPosition = ChartLegendPosition.Right
          LegendEntryGap = 24.0
          LegendSwatchY = 34.0
          LegendSwatchSize = 10.0
          LegendSwatchCornerRadius = 2.0
          LegendLabelOffsetX = 15.0
          LegendLabelBaselineY = 43.0
          PieRadius = 130.0
          PieLegendOffsetX = 168.0
          PieLegendTopY = 70.0
          PieLegendPitchY = 20.0
          PieLegendLabelBaselineDy = 9.0
          // Refreshed by Phase 875 alongside the palette: these three were the
          // only survivors of the retired 2008 set, and leaving unvalidated
          // hexes here would hand the variance/waterfall arm a palette that
          // already failed its gates. They are drawn from the SAME validated
          // set the categorical slots come from, but they are not slots — the
          // rotation must never reach them (they encode meaning, not identity).
          PositiveColour = "#21a766"
          NegativeColour = "#d3241b"
          NeutralColour = "#999999" }

/// Series index (or, on the Pie arm, category index) → colour. An empty palette
/// inks series from the surface colour rather than failing — a style is host
/// input, and a degenerate one must still lower.
let private colourFor (style: ChartStyle) (i: int) : string =
    if style.Palette.Length = 0 then
        style.Ink
    else
        style.Palette.[i % style.Palette.Length]

// ─── Deterministic numeric helpers ───────────────────────────────────────────

/// Round-half-up to 2 dp — a single deterministic rule every host reproduces
/// (avoids banker's-rounding / platform float-print divergence).
let private r2 (x: float) : float = floor (x * 100.0 + 0.5) / 100.0

/// A "nice" number ≥ (or ≤, when not rounding) the magnitude of `x` — the
/// classic {1,2,5}·10ⁿ selection used for axis ticks.
let private niceNum (x: float) (roundIt: bool) : float =
    if x <= 0.0 then
        0.0
    else
        let exp = floor (log10 x)
        let f = x / (10.0 ** exp)

        let nf =
            if roundIt then
                if f < 1.5 then 1.0
                elif f < 3.0 then 2.0
                elif f < 7.0 then 5.0
                else 10.0
            else if f <= 1.0 then
                1.0
            elif f <= 2.0 then
                2.0
            elif f <= 5.0 then
                5.0
            else
                10.0

        nf * (10.0 ** exp)

/// A nice value domain + its TICK STEP + its tick values for `[lo, hi]`,
/// targeting `targetTicks` ticks (`ChartStyle.TargetTickCount`). The step is
/// returned because the axis's decimal precision is derived from it
/// (Phase 876) — precision follows the axis, not the data.
let private niceDomain (targetTicks: float) (lo: float) (hi: float) : float * float * float * float list =
    let hi = if hi = lo then lo + 1.0 else hi
    let range = niceNum (hi - lo) false
    let step = niceNum (range / (targetTicks - 1.0)) true
    let niceLo = floor (lo / step) * step
    let niceHi = ceil (hi / step) * step
    // Enumerate ticks by integer count (float accumulation would drift).
    let count = int (System.Math.Round((niceHi - niceLo) / step))

    let ticks = [ for i in 0..count -> r2 (niceLo + float i * step) ]

    niceLo, niceHi, step, ticks

// ─── Deterministic text metrics (Phase 879) ──────────────────────────────────
//
// NORMATIVE CROSS-HOST SPEC (R2). Every conformant host reproduces this table
// and these functions EXACTLY, byte-for-byte; the `chart-lowering/*` goldens pin
// them through the margins, the legend pitch and the label rotations they
// decide. `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md` §4d carries the same table
// as the language-neutral statement of it.
//
// THE APPROXIMATION IS THE SPEC. No host measures text: a headless Python or Go
// emitter has no font engine, a browser's measurement depends on which of the
// `FontFamily` stack's fonts actually resolved, and a lowering whose margins
// depended on either would stop being deterministic — the one property (R2) the
// whole corpus rests on. So the widths come from a FIXED table of per-character
// advance widths expressed as a fraction of the font size (em factors), chosen
// to approximate a typical sans-serif (the shipped `FontFamily`'s members all
// sit near Helvetica's metrics). A real font will differ by a few percent; that
// is accepted, and the padding constants absorb it.
//
//   1. FIVE WIDTH CLASSES, listed below. A character not named in a class takes
//      the DEFAULT — which is also what every non-ASCII character takes, so the
//      table is total over every string without enumerating Unicode.
//   2. A STRING'S WIDTH is `fontSize · Σ advanceEm(ch)`, summed LEFT TO RIGHT
//      (the order is part of the spec: float addition is not associative, and
//      two hosts summing differently could round to different pixels), then
//      `r2`-rounded exactly once at the end.
//   3. A LINE'S HEIGHT is `fontSize · ChartStyle.TextLineHeightFactor`.
//   4. TRUNCATION is deterministic: the longest character prefix whose width
//      plus the ellipsis's still fits, then `…`. When not even one character
//      fits, the result is the bare `…` (never the empty string — an empty
//      label is indistinguishable from a missing one).

[<RequireQualifiedAccess>]
module TextMetrics =

    /// `' . , : ; ! i j l I |` and the space — the stems and the punctuation.
    [<Literal>]
    let ThinEm = 0.28

    /// `" ( ) * - / \ [ ] { } f r t` — narrow but not a bare stem.
    [<Literal>]
    let NarrowEm = 0.33

    /// Digits, most lowercase, `J`, `L`, everything unlisted, and every
    /// non-ASCII character.
    [<Literal>]
    let DefaultEm = 0.55

    /// Uppercase (bar `I J L M W`) and lowercase `w`.
    [<Literal>]
    let WideEm = 0.70

    /// `m M W % @` — the widest glyphs in a sans-serif.
    [<Literal>]
    let ExtraWideEm = 0.90

    /// The truncation marker. One character, `DefaultEm` wide by the table's
    /// own non-ASCII rule.
    [<Literal>]
    let Ellipsis = "…"

    /// One character's advance width as a fraction of the font size. Total: an
    /// unlisted character — punctuation, an accented letter, a CJK ideograph —
    /// takes `DefaultEm`, so the table never has to enumerate Unicode.
    let advanceEm (ch: char) : float =
        match ch with
        | ' '
        | '!'
        | '\''
        | ','
        | '.'
        | ':'
        | ';'
        | 'I'
        | 'i'
        | 'j'
        | 'l'
        | '|' -> ThinEm
        | '"'
        | '('
        | ')'
        | '*'
        | '-'
        | '/'
        | '\\'
        | '['
        | ']'
        | '{'
        | '}'
        | 'f'
        | 'r'
        | 't' -> NarrowEm
        | '%'
        | '@'
        | 'M'
        | 'W'
        | 'm' -> ExtraWideEm
        | 'J'
        | 'L' -> DefaultEm
        | c when c >= 'A' && c <= 'Z' -> WideEm
        | 'w' -> WideEm
        | _ -> DefaultEm

    /// A string's advance width in em — summed LEFT TO RIGHT (rule 2).
    let advanceEmOf (text: string) : float =
        let mutable acc = 0.0

        for ch in text do
            acc <- acc + advanceEm ch

        acc

    /// The estimated rendered width of `text` at `fontSize`, in drawing-space
    /// px. `r2`-rounded once, at the end (rule 2).
    let width (fontSize: float) (text: string) : float = r2 (fontSize * advanceEmOf text)

    /// The estimated line height at `fontSize` (rule 3).
    let lineHeight (fontSize: float) (lineHeightFactor: float) : float = r2 (fontSize * lineHeightFactor)

    /// Does `text` fit a box `maxWidth` × `maxHeight` at `fontSize`? The
    /// predicate Phase 881's data labels gate inside/outside/suppress on — a
    /// single place the fit question is answered, so a label never disagrees
    /// with the margin that made room for it.
    let fitsBox (fontSize: float) (lineHeightFactor: float) (maxWidth: float) (maxHeight: float) (text: string) : bool =
        width fontSize text <= maxWidth
        && lineHeight fontSize lineHeightFactor <= maxHeight

    /// The longest character prefix of `text` whose width stays within
    /// `budget` at `fontSize` (rule 4's inner loop).
    let rec private prefixWithin (fontSize: float) (budget: float) (text: string) (i: int) (accEm: float) : int =
        if i >= text.Length then
            i
        else
            let next = accEm + advanceEm text.[i]

            if r2 (fontSize * next) <= budget then
                prefixWithin fontSize budget text (i + 1) next
            else
                i

    /// Deterministic ellipsis truncation to `maxWidth` (rule 4). A string that
    /// already fits is returned unchanged — so a host that never hits a bound
    /// never sees a `…`, and the goldens for such a chart are untouched.
    let truncateToWidth (fontSize: float) (maxWidth: float) (text: string) : string =
        if width fontSize text <= maxWidth then
            text
        else
            let budget = maxWidth - width fontSize Ellipsis

            if budget < 0.0 then
                Ellipsis
            else
                let take = prefixWithin fontSize budget text 0 0.0
                text.Substring(0, take) + Ellipsis

// ─── The canonical invariant number formatter (Phase 876) ────────────────────
//
// NORMATIVE CROSS-HOST SPEC (R2). Every conformant host reproduces these rules
// exactly; the `chart-lowering/*` goldens pin them. The chart lowering does NOT
// inherit the locale-aware rendering other surfaces give `Format` (that is
// `Binding.Format`'s job, via `Intl` / `CultureInfo`): a chart's ticks are part
// of a drawing whose bytes must be identical on every host, so the rendering
// here is locale-INVARIANT by definition — period decimal separator, comma
// thousands separator, no CLDR anywhere.
//
//   1. DECIMALS COME FROM THE TICK STEP, never from the data. `dpsOfStep`
//      returns the smallest d ≤ 6 for which `step · 10^d` is an integer, so a
//      step of 500 gives 0 dp (`12,500`) and a step of 0.25 gives 2 dp
//      (`0.25`). Every tick on one axis therefore carries the same precision,
//      which is what makes a column of numbers comparable.
//   2. The BASE RENDER is round-half-up on the magnitude at that precision,
//      the integer part grouped in threes with `,`, the fraction zero-padded
//      to exactly d places, a leading `-` only when the rounded magnitude is
//      non-zero (so a tick that rounds to zero never reads `-0`).
//   3. The `Format` ARMS layer meaning over that base: `Percent` renders the
//      ratio ×100 with a `%` suffix (the same ratio reading `Binding.Format`
//      gives it) and derives its precision from the ×100 step; `Currency`
//      prefixes the ISO code's symbol INSIDE the sign (`-£1,200`); `Number`
//      may pin the precision explicitly. `Date` / `RelativeTime` / `Duration`
//      are not value-axis formats — they fall through to the base render
//      rather than inventing an axis semantics for them.
//   4. DISPLAY-UNIT SCALING divides both the tick value and the step by 10ⁿ,
//      so rule 1 keeps holding on the scaled numbers.

/// Decimal places implied by a tick step: the smallest `d ≤ 6` for which
/// `step · 10^d` is (within float tolerance) an integer. `0` for a degenerate
/// step. The tolerance is relative, so it holds at any magnitude.
let private dpsOfStep (step: float) : int =
    let s = abs step

    if s <= 0.0 || System.Double.IsNaN s || System.Double.IsInfinity s then
        0
    else
        let rec probe (d: int) (scaled: float) =
            if d >= 6 then
                6
            elif abs (scaled - floor (scaled + 0.5)) <= 1e-9 * (max 1.0 scaled) then
                d
            else
                probe (d + 1) (scaled * 10.0)

        probe 0 s

/// Group an integral digit string in threes from the right with `,`.
let private groupThousands (digits: string) : string =
    let n = digits.Length

    if n <= 3 then
        digits
    else
        let head = n % 3
        let groups = [ for i in head..3 .. n - 3 -> digits.Substring(i, 3) ]
        let leading = if head > 0 then [ digits.Substring(0, head) ] else []
        String.concat "," (leading @ groups)

/// Render `v` with EXACTLY `dps` decimals — round-half-up on the magnitude,
/// comma thousands separators, period decimal point, locale-invariant.
let private renderFixed (dps: int) (v: float) : string =
    if System.Double.IsNaN v || System.Double.IsInfinity v then
        "0"
    else
        let d =
            if dps < 0 then 0
            elif dps > 6 then 6
            else dps

        let scale = 10.0 ** float d
        // Round-half-up on the MAGNITUDE (not banker's rounding, and not
        // half-away-from-zero on the signed value) — one rule, reproducible on
        // every host's IEEE doubles.
        let units = floor (abs v * scale + 0.5)
        let intPart = floor (units / scale)
        let fracPart = units - intPart * scale
        let intStr = groupThousands (DrawingSvg.formatNum intPart)

        let body =
            if d = 0 then
                intStr
            else
                let raw = DrawingSvg.formatNum fracPart
                let pad = String.replicate (max 0 (d - raw.Length)) "0"
                intStr + "." + pad + raw

        if v < 0.0 && units > 0.0 then "-" + body else body

/// ISO-4217 code → symbol, the invariant table (a superset of the codes the
/// locale-aware `Formatting` module curates). An unlisted code renders as the
/// code itself — deterministic, and never a wrong symbol.
let private currencySymbols: Map<string, string> =
    Map
        [ "EUR", "€"
          "USD", "$"
          "GBP", "£"
          "JPY", "¥"
          "CNY", "¥"
          "CHF", "CHF"
          "AUD", "$"
          "CAD", "$"
          "NZD", "$"
          "HKD", "$"
          "SGD", "$"
          "INR", "₹"
          "KRW", "₩"
          "BRL", "R$"
          "RUB", "₽"
          "ZAR", "R"
          "SEK", "kr"
          "NOK", "kr"
          "DKK", "kr"
          "PLN", "zł"
          "CZK", "Kč"
          "HUF", "Ft"
          "TRY", "₺"
          "MXN", "$"
          "THB", "฿"
          "ILS", "₪" ]

let private currencySymbol (iso: string) : string =
    match Map.tryFind iso currencySymbols with
    | Some s -> s
    | None -> iso

/// The unit symbol a `Format` contributes to an axis-unit label — the currency
/// symbol, or `""` for every other arm (a percentage's unit is already the `%`
/// on each tick).
let private formatUnitSymbol (fmt: Format option) : string =
    match fmt with
    | Some(Format.Currency iso) -> currencySymbol iso
    | _ -> ""

/// The ×100 a `Format.Percent` applies to BOTH the value and the step (so the
/// step-derived precision is computed on what is actually printed).
let private formatValueScale (fmt: Format option) : float =
    match fmt with
    | Some(Format.Percent _) -> 100.0
    | _ -> 1.0

/// Render one value-axis number. `divisor` is the display unit (`1.0` when no
/// scaling applies); `dropSymbol` suppresses a currency symbol on the ticks
/// because the axis-unit label already states it once; `step` is the axis's
/// tick step, from which the precision derives.
let private formatValue (fmt: Format option) (divisor: float) (dropSymbol: bool) (step: float) (v: float) : string =
    let pct = formatValueScale fmt
    let dv = v * pct / divisor
    let ds = step * pct / divisor

    let dps =
        match fmt with
        | Some(Format.Number(Some d))
        | Some(Format.Percent(Some d)) -> d
        | _ -> dpsOfStep ds

    let body = renderFixed dps dv

    match fmt with
    | Some(Format.Percent _) -> body + "%"
    | Some(Format.Currency iso) when not dropSymbol ->
        let sym = currencySymbol iso

        if body.StartsWith "-" then
            "-" + sym + body.Substring 1
        else
            sym + body
    | _ -> body

// ─── Display units (Phase 876) ───────────────────────────────────────────────
//
// The operator's prefix table, transcribed: thresholds sit at 1 + 3k and the
// selected threshold `t` for a magnitude of exponent `e` satisfies
// `e - 1 ≤ t < e + 2`, giving the unit exponent `n = t - 1`. Each unit
// therefore covers three exponents — Thousands for e ∈ {3,4,5}, Millions for
// {6,7,8}, Billions for {9,10,11} — which is why a 12-million axis and a
// 900-million axis both read in millions rather than flipping mid-range.

/// The display-unit exponent for a magnitude: `n = 3·⌈(e-2)/3⌉` where
/// `e = ⌊log₁₀|max| + ½⌋`. Clamped to the table's span.
let private unitExponentOf (maxAbs: float) : int =
    if maxAbs <= 0.0 || System.Double.IsNaN maxAbs || System.Double.IsInfinity maxAbs then
        0
    else
        let e = int (floor (log10 maxAbs + 0.5))
        let n = 3 * int (ceil (float (e - 2) / 3.0))

        if n < -15 then -15
        elif n > 15 then 15
        else n

/// The words form of a unit exponent (`""` outside the table's positive span).
let private unitWords (n: int) : string =
    match n with
    | 3 -> "Thousands"
    | 6 -> "Millions"
    | 9 -> "Billions"
    | 12 -> "Trillions"
    | 15 -> "Quadrillions"
    | _ -> ""

/// The SI-prefix form of a unit exponent.
let private unitSi (n: int) : string =
    match n with
    | 3 -> "k"
    | 6 -> "M"
    | 9 -> "G"
    | 12 -> "T"
    | 15 -> "P"
    | _ -> ""

/// The compact per-tick suffix of a unit exponent (the finance convention —
/// `B` for billions, not SI's `G`).
let private unitCompact (n: int) : string =
    match n with
    | 3 -> "K"
    | 6 -> "M"
    | 9 -> "B"
    | 12 -> "T"
    | 15 -> "Q"
    | _ -> ""

/// A resolved display unit for one value axis: the exponent, the divisor, the
/// per-tick suffix, whether the ticks drop the currency symbol, and the axis
/// unit label (`""` when the axis states no unit).
type private DisplayUnit =
    { Exponent: int
      Divisor: float
      TickSuffix: string
      DropSymbol: bool
      Label: string }

let private noDisplayUnit: DisplayUnit =
    { Exponent = 0
      Divisor = 1.0
      TickSuffix = ""
      DropSymbol = false
      Label = "" }

/// Resolve the display unit for a value axis whose printed magnitudes peak at
/// `maxAbs` (already through any `Format.Percent` ×100 — the unit follows what
/// is PRINTED, not the raw datum).
let private resolveDisplayUnit (style: ChartStyle) (fmt: Format option) (maxAbs: float) : DisplayUnit =
    let n = unitExponentOf maxAbs

    let threshold =
        match style.AxisUnitMode with
        | ChartAxisUnitMode.CompactPerTick -> 3
        | _ -> style.DisplayUnitMinExponent

    let admissible = n >= 3 && n >= threshold && unitWords n <> ""

    if not admissible || style.AxisUnitMode = ChartAxisUnitMode.Off then
        noDisplayUnit
    else
        let symbol = formatUnitSymbol fmt
        let divisor = 10.0 ** float n

        match style.AxisUnitMode with
        | ChartAxisUnitMode.Words ->
            { Exponent = n
              Divisor = divisor
              TickSuffix = ""
              DropSymbol = false
              Label = unitWords n }
        | ChartAxisUnitMode.WordsWithSymbol ->
            { Exponent = n
              Divisor = divisor
              TickSuffix = ""
              DropSymbol = symbol <> ""
              Label =
                (if symbol = "" then
                     unitWords n
                 else
                     unitWords n + " of " + symbol) }
        | ChartAxisUnitMode.SIAbbreviation ->
            { Exponent = n
              Divisor = divisor
              TickSuffix = ""
              DropSymbol = symbol <> ""
              Label = unitSi n + symbol }
        | ChartAxisUnitMode.CompactPerTick ->
            { Exponent = n
              Divisor = divisor
              TickSuffix = unitCompact n
              DropSymbol = false
              Label = "" }
        | ChartAxisUnitMode.Off -> noDisplayUnit

// ─── DrawStyle builders ──────────────────────────────────────────────────────
//
// Every builder that emits a colour, an opacity, a width or a font takes the
// `ChartStyle` — there is no ambient styling constant left in this module.

let private baseStyle: DrawStyle =
    { Fill = None
      Stroke = None
      StrokeWidth = None
      Opacity = None
      TextAnchor = None
      FontSize = None
      Emphasis = None
      FontFamily = None
      MarkId = None
      Rotation = None }

/// Phase 642 — stamp a derivation-based mark identity onto a data-bearing
/// shape's style: `series-field|category-key`, stable under row reorder and
/// data refresh (object constancy). Chrome (axes, gridlines, labels, legend)
/// deliberately stays unstamped — its identity is structural, not data-borne.
let private withMark (seriesField: string) (categoryKey: string) (style: DrawStyle) : DrawStyle =
    { style with
        MarkId = Some(seriesField + "|" + categoryKey) }

/// A series-level mark (one shape carries the whole series — Line/Area): the
/// identity is the series field alone.
let private withSeriesMark (seriesField: string) (style: DrawStyle) : DrawStyle =
    { style with MarkId = Some seriesField }

let private styleFill (fill: string) : DrawStyle =
    { baseStyle with
        Fill = Some(Binding.Static(Some fill)) }

let private styleStroke (stroke: string) (width: float) : DrawStyle =
    { baseStyle with
        Stroke = Some(Binding.Static(Some stroke))
        StrokeWidth = Some(Binding.Static(Some width)) }

/// A translucent categorical fill (Phase 637 — area bands); the opacity comes
/// from `ChartStyle.AreaFillOpacity`.
let private styleFillOpacity (fill: string) (opacity: float) : DrawStyle =
    { baseStyle with
        Fill = Some(Binding.Static(Some fill))
        Opacity = Some(Binding.Static(Some opacity)) }

/// A surface-relative structural stroke (Phase 536): `ChartStyle.Ink` at a
/// per-role opacity, so axis + gridlines ink from the surface's own text colour.
/// Used for the chrome (axes, gridlines) — series lines keep their categorical hex.
let private styleStrokeInk (style: ChartStyle) (opacity: float) (width: float) : DrawStyle =
    { baseStyle with
        Stroke = Some(Binding.Static(Some style.Ink))
        StrokeWidth = Some(Binding.Static(Some width))
        Opacity = Some(Binding.Static(Some opacity)) }

let private emptyStyle: DrawStyle = baseStyle

/// A text-label style (Phase 536): surface-relative ink (`ChartStyle.Ink`) + an
/// optional per-role opacity (`None` = full-strength, e.g. titles) + alignment +
/// size + weight + the style's font stack.
let private textStyle
    (style: ChartStyle)
    (opacity: float option)
    (anchor: TextAnchor)
    (size: float)
    (emphasis: Emphasis)
    : DrawStyle =
    { baseStyle with
        Fill = Some(Binding.Static(Some style.Ink))
        Opacity = opacity |> Option.map (Some >> Binding.Static)
        TextAnchor = Some anchor
        FontSize = Some size
        Emphasis = Some emphasis
        FontFamily = Some style.FontFamily }

// ─── Data extraction (over the resolved rows) ────────────────────────────────

/// The `ChartKind`s this module lowers to a real `Drawing`. The render
/// dispatch (client + server) consults THIS — so the first-party render branch
/// and the lowering's arm set can never drift apart (the Phase 636/637 arms
/// would otherwise still hit the placeholder branch).
let isLowered (kind: ChartKind) : bool =
    match kind with
    | ChartKind.Bar
    | ChartKind.Line
    | ChartKind.Area
    | ChartKind.Scatter
    | ChartKind.Pie -> true
    | ChartKind.Heatmap -> false

// ─── Cost bounds (Phase 790) ─────────────────────────────────────────────────
//
// A `Chart` is ONE node, so the tree-size budget a host applies to an untrusted
// emission never sees the lowering's cost: a single node carrying a large enough
// series is unbounded work behind a bounded-looking tree. These caps close that
// — the lowering refuses rather than truncating, per the estate's default-deny
// posture (a silently truncated chart is a wrong chart that looks right).
//
// The point cap is enforced by TAKING `MaxPointsPerSeries + 1` from the row
// sequence, so an over-budget (or infinite) source is never materialised: the
// refusal costs one row more than the cap, not the whole feed.

/// Per-lowering cost caps.
type ChartLimits =
    {
        /// Maximum number of series (`YFields`) one chart may carry.
        MaxSeries: int
        /// Maximum number of data rows (points per series) one chart may carry.
        MaxPointsPerSeries: int
    }

/// Why a lowering refused. Carries the observed magnitude and the breached
/// limit, so a host can report the breach rather than an empty picture.
type ChartRefusal =
    /// More series than `MaxSeries`.
    | TooManySeries of series: int * limit: int
    /// At least `atLeast` rows against `MaxPointsPerSeries` (the count is a lower
    /// bound: the source is only read to the cap + 1).
    | TooManyPoints of atLeast: int * limit: int

module ChartLimits =
    /// The shipped defaults. 32 series exceeds any legible categorical palette
    /// (the default palette has 8 colours) and 10 000 points is well past the
    /// 640×400 canvas's ability to distinguish marks — so a chart at these caps
    /// is already unreadable, and anything beyond them is cost, not information.
    let defaults: ChartLimits =
        { MaxSeries = 32
          MaxPointsPerSeries = 10_000 }

    /// No caps — the trusted-author case (the chart is your own data).
    let unlimited: ChartLimits =
        { MaxSeries = System.Int32.MaxValue
          MaxPointsPerSeries = System.Int32.MaxValue }

/// A human-readable one-line description of a refusal — the text a refusal
/// drawing carries as its `<desc>`.
let describeRefusal (r: ChartRefusal) : string =
    match r with
    | TooManySeries(series, limit) -> sprintf "Chart not rendered: %d series exceeds the limit of %d." series limit
    | TooManyPoints(atLeast, limit) ->
        sprintf "Chart not rendered: at least %d data points exceeds the limit of %d per series." atLeast limit

let private numericOf (row: Row) (field: string) : float =
    match BindingResolver.projectRowFieldValue row field with
    // Non-finite guard (Phase 640): NaN/Infinity would poison every domain
    // computation and emit NaN geometry into the SVG. Wire-carried data can
    // never be non-finite (the canonical-float codec rejects it), so this
    // covers only host-side obj-seq rows — coerced to the same 0.0 the
    // non-numeric posture uses, deterministically.
    | CellValue.Numeric n when System.Double.IsNaN n || System.Double.IsInfinity n -> 0.0
    | CellValue.Numeric n -> n
    | CellValue.Bool b -> if b then 1.0 else 0.0
    | _ -> 0.0

// ─── The lowering ─────────────────────────────────────────────────────────────

/// The core lowering: an ALREADY-CAPPED row list to a canonical `DrawingSpec`,
/// under an explicit `ChartStyle` (Phase 885 — every colour, size, weight and
/// offset below reads from it). The public entry points (`lower` / `lowerWith` /
/// `lowerWithStyle` / `tryLower*`) apply the Phase-790 cost caps before this
/// runs, so it never sees an over-budget input.
/// Lowered arms: `Bar` (grouped + stacked), `Line`, `Area` (overlaid +
/// stacked), `Scatter` (linear numeric x), `Pie` (polar, single-series) —
/// Phases 533 + 637 + 636 + 638. `Heatmap` produces an empty drawing (its
/// lowering rule lands with its own phase). `Stacked = true` on a kind where
/// stacking is meaningless (`Line`, `Scatter`, `Pie`) is ignored — the flag
/// only changes `Bar` / `Area` geometry.
let private lowerRows<'Msg> (style: ChartStyle) (spec: ChartSpec<'Msg>) (rows: Row list) : DrawingSpec =
    // ORDER IS LOAD-BEARING SINCE PHASE 879. The plot rectangle used to be the
    // first thing computed, from four constant margins. It is now DERIVED from
    // the text the chart is going to print — the widest formatted y tick decides
    // the left margin, and the category labels' tilt decides the bottom one — so
    // everything text-metric-shaped is computed first, the margins next, and the
    // plot rectangle (and every scale over it) only then.

    // ARRAYS, not lists, for everything the nested series-by-point loops index
    // (Phase 790). F# list indexing is O(index), so `series.[j].[i]` inside a
    // per-category × per-series loop made Pie roughly O(n²) and stacked bar
    // roughly O(n²m + nm²). Array access is O(1), so the lowering is linear in
    // points. The emitted geometry is byte-identical — this is an access-cost
    // fix, not a layout change.
    let categories =
        rows
        |> List.map (fun r -> BindingResolver.projectRowFieldString r spec.XField)
        |> List.toArray

    let n = List.length rows

    let yFields = List.toArray spec.YFields

    let series =
        yFields
        |> Array.map (fun yf -> rows |> List.map (fun r -> numericOf r yf) |> List.toArray)

    let m = Array.length series

    // Stacking applies to Bar + Area only (Phase 637). Values stack as-is by
    // plain cumulative sum per category — deterministic and total; a negative
    // value simply lowers the running sum (mixed-sign stacks are a validation
    // concern, not a lowering one).
    let stacked =
        spec.Stacked
        && (match spec.Kind with
            | ChartKind.Bar
            | ChartKind.Area -> true
            | _ -> false)

    /// Per-category running sums across the series, INCLUDING the leading 0
    /// baseline: `cumsFor i` has length m+1.
    let cumsFor (i: int) : float[] =
        Array.init m (fun j -> series.[j].[i]) |> Array.scan (+) 0.0

    let allValues =
        let vs =
            if stacked then
                [ for i in 0 .. n - 1 do
                      yield! cumsFor i ]
            else
                [ for s in series do
                      yield! s ]

        match vs with
        | [] -> [ 0.0 ]
        | vs -> vs

    let dataMin = List.min allValues
    let dataMax = List.max allValues
    // Bars + lines share a zero-anchored domain — deterministic + honest for
    // bars. Stacked domains come from the cumulative partial sums, so the axis
    // covers the stack totals, never a single series' range.
    let niceLo, niceHi, yStep, ticks =
        niceDomain style.TargetTickCount (min 0.0 dataMin) (max 0.0 dataMax)

    // ── Value-axis number formatting (Phase 876) ──
    // The declared meaning (`ChartSpec.ValueFormat`) chooses the arms; the
    // style chooses whether a large magnitude is stated once as a display unit;
    // the tick STEP chooses the precision. The unit is resolved from the
    // PRINTED magnitude, so a `Percent` axis is measured after its ×100.
    let valueFormat = spec.ValueFormat

    let yDisplayUnit =
        resolveDisplayUnit style valueFormat (max (abs niceLo) (abs niceHi) * formatValueScale valueFormat)

    let yTickText (v: float) : string =
        formatValue valueFormat yDisplayUnit.Divisor yDisplayUnit.DropSymbol yStep v
        + yDisplayUnit.TickSuffix

    // ── Linear x-scale (Phase 636 — the Scatter arm's numeric x axis) ──
    // Scatter reads the x-field NUMERICALLY and plots on a linear x-domain (the
    // first non-band x-scale arm). The domain is NOT zero-anchored — a scatter's
    // x range carries no baseline semantics (the y domain stays zero-anchored
    // with the other arms, deliberately: one shared y-domain rule).
    let isScatter =
        match spec.Kind with
        | ChartKind.Scatter -> true
        | _ -> false

    let xValues =
        if isScatter then
            rows |> List.map (fun r -> numericOf r spec.XField) |> List.toArray
        else
            [||]

    let xNiceLo, xNiceHi, xStep, xTicks =
        if isScatter then
            if Array.isEmpty xValues then
                niceDomain style.TargetTickCount 0.0 1.0
            else
                niceDomain style.TargetTickCount (Array.min xValues) (Array.max xValues)
        else
            0.0, 1.0, 1.0, []

    // The Scatter arm's x IS a value axis, so its ticks take the same canonical
    // formatter (Phase 876) — thousands separators + step-derived decimals.
    // `ValueFormat` is deliberately NOT applied to it: one declared meaning
    // cannot be true of two different measures (a "height vs weight" scatter
    // does not have pounds on both axes), and there is no second axis-unit slot
    // to state an x display unit in until the axis-title phase lands.
    let xTickText (v: float) : string = formatValue None 1.0 false xStep v

    // ── Text-metric layout (Phase 879) ───────────────────────────────────────
    //
    // Everything below reads `TextMetrics` and nothing reads a font. The four
    // decisions, in dependency order: the LEFT margin (from the widest formatted
    // y tick), the band pitch that follows from it, the category-label TILT
    // (from the widest category name against that pitch), and the BOTTOM margin
    // the chosen tilt needs to fall into.

    let tickSize = style.TickFontSize
    let titleSize = style.TitleFontSize
    let lineHeight = TextMetrics.lineHeight tickSize style.TextLineHeightFactor

    let widestOf (texts: string seq) : float =
        texts |> Seq.fold (fun acc t -> max acc (TextMetrics.width tickSize t)) 0.0

    // ── Left margin ──
    // The tick column must clear `TickLabelGap` from the spine and
    // `AxisLabelPadding` from the canvas edge. The ceiling is a share of the
    // canvas; a tick column that would breach it is TRUNCATED (never allowed to
    // eat the plot), which is why the truncation budget is derived from the
    // ceiling — a constant — and not from the margin it is about to decide.
    let leftCeiling = style.MarginLeftMaxShare * style.Width

    let tickTextBudget =
        max 0.0 (leftCeiling - style.TickLabelGap - style.AxisLabelPadding)

    let yTickLabelText (v: float) : string =
        TextMetrics.truncateToWidth tickSize tickTextBudget (yTickText v)

    let requiredLeft =
        style.TickLabelGap
        + widestOf (ticks |> List.map yTickLabelText)
        + style.AxisLabelPadding

    let marginLeft = r2 (max style.MarginLeft (min leftCeiling requiredLeft))

    let plotX0 = marginLeft
    let plotX1 = style.Width - style.MarginRight
    let plotW = plotX1 - plotX0

    let bandW = if n > 0 then plotW / float n else plotW
    let centreX (i: int) : float = r2 (plotX0 + bandW * (float i + 0.5))

    // ── Category-label tilt + its vertical escalation ──
    // Only the BAND arms label categories: Scatter labels numeric x ticks (short
    // by construction, left horizontal) and Pie has no x axis at all. Both must
    // therefore contribute NO drop, or their bottom margin — and with it the
    // pie's centre — would move for a decision they never take.
    let drawsCategoryLabels =
        not isScatter
        && (match spec.Kind with
            | ChartKind.Pie -> false
            | _ -> true)

    // A rotated label's footprint ALONG the axis is its width's horizontal
    // projection plus the line height's: `w·cos θ + h·sin θ`. Escalate when the
    // widest label's footprint at the tilt no longer fits the band pitch. At 90°
    // the width term vanishes, so the vertical arm packs one label per line
    // height at any category count — which is why it is the terminal arm and
    // there is nothing to escalate to beyond it.
    let radians (deg: float) : float = deg * System.Math.PI / 180.0

    let alongAxisFootprint (deg: float) (w: float) : float =
        w * cos (radians deg) + lineHeight * sin (radians deg)

    let tiltDegrees =
        if not drawsCategoryLabels || n = 0 || style.LabelTiltDegrees <= 0.0 then
            // `LabelTiltDegrees = 0` is a host opting out of tilt entirely;
            // honour it literally rather than escalating it to vertical.
            0.0
        elif alongAxisFootprint style.LabelTiltDegrees (widestOf categories) > bandW then
            style.VerticalTiltDegrees
        else
            style.LabelTiltDegrees

    // ── Bottom margin ──
    // A tilted label falls `w·sin θ` below its anchor. Below the plot, top to
    // bottom: the label offset, that drop, the padding, the x-axis title's own
    // LINE (`AxisTitleBottomOffset` measures to its BASELINE, so the glyphs
    // above that baseline need reserving separately — omitting this term let a
    // long tilted label run into the title), and the title's inset from the
    // canvas bottom. Same ceiling-then-truncate posture as the left margin —
    // the budget comes from the ceiling, so the truncation that feeds the
    // margin does not depend on the margin.
    let sinTilt = sin (radians tiltDegrees)
    let bottomCeiling = style.MarginBottomMaxShare * style.Height

    let dropCeiling =
        max
            0.0
            (bottomCeiling
             - style.CategoryLabelOffsetY
             - style.AxisLabelPadding
             - lineHeight
             - style.AxisTitleBottomOffset)

    let categoryTextBudget = if sinTilt > 0.0 then dropCeiling / sinTilt else infinity

    let categoryLabelText (c: string) : string =
        TextMetrics.truncateToWidth tickSize categoryTextBudget c

    let categoryTexts =
        if drawsCategoryLabels then
            categories |> Array.map categoryLabelText
        else
            [||]

    let requiredBottom =
        style.CategoryLabelOffsetY
        + sinTilt * widestOf categoryTexts
        + style.AxisLabelPadding
        + lineHeight
        + style.AxisTitleBottomOffset

    let marginBottom = r2 (max style.MarginBottom (min bottomCeiling requiredBottom))

    let plotY0 = style.MarginTop
    let plotY1 = style.Height - marginBottom
    let plotH = plotY1 - plotY0

    let yScale (v: float) : float =
        r2 (plotY1 - (v - niceLo) / (niceHi - niceLo) * plotH)

    let xScale (v: float) : float =
        r2 (plotX0 + (v - xNiceLo) / (xNiceHi - xNiceLo) * plotW)

    // ── Axes + gridlines ──
    let axisStyle = styleStrokeInk style style.AxisOpacity style.AxisStrokeWidth

    let axes =
        [ Shape.Line(r2 plotX0, r2 plotY0, r2 plotX0, r2 plotY1, axisStyle)
          Shape.Line(r2 plotX0, r2 plotY1, r2 plotX1, r2 plotY1, axisStyle) ]

    let gridStyle = styleStrokeInk style style.GridOpacity style.GridStrokeWidth

    let gridlines =
        ticks
        |> List.map (fun t ->
            let y = yScale t
            Shape.Line(r2 plotX0, y, r2 plotX1, y, gridStyle))

    // Vertical gridlines — the Scatter arm only (Phase 875). A linear x-scale
    // has readable x positions, so a reader traces a point back to an x value
    // the same way the horizontal grid lets them trace a y value. A BAND x-axis
    // has no such positions to trace (a category is a label, not a magnitude),
    // so a vertical rule there would be decoration.
    let xGridlines =
        if isScatter then
            xTicks
            |> List.map (fun t -> Shape.Line(xScale t, r2 plotY0, xScale t, r2 plotY1, gridStyle))
        else
            []

    // Zero baseline (Phase 875) — only when the domain CROSSES zero, where the
    // sign of a value is a reading of the chart and the zero line is what the
    // reader measures against. Drawn at axis strength, over the ordinary
    // gridline it shares a y with, so it separates from the grid; when the
    // domain does not cross zero the axis spine already IS the baseline and a
    // second rule at the same strength would be noise.
    let zeroLine =
        if niceLo < 0.0 && niceHi > 0.0 then
            let y = yScale 0.0
            [ Shape.Line(r2 plotX0, y, r2 plotX1, y, axisStyle) ]
        else
            []

    // Outside tick marks (Phase 875) — outside the plot on both axes, so the
    // plot area stays ink-free and the marks tie each label to its position.
    let tickMarks =
        if style.TickMarkLength <= 0.0 then
            []
        else
            let yMarks =
                ticks
                |> List.map (fun t ->
                    let y = yScale t
                    Shape.Line(r2 (plotX0 - style.TickMarkLength), y, r2 plotX0, y, axisStyle))

            let xAt (x: float) =
                Shape.Line(x, r2 plotY1, x, r2 (plotY1 + style.TickMarkLength), axisStyle)

            let xMarks =
                if isScatter then
                    xTicks |> List.map (xScale >> xAt)
                else
                    [ for i in 0 .. n - 1 -> xAt (centreX i) ]

            yMarks @ xMarks

    // y-axis tick labels — right-anchored (End) so the number column sits cleanly
    // in the left margin, ending just before the axis. The text is the
    // margin-bounded one (Phase 879): whatever the margin was sized for is
    // exactly what gets drawn, so a truncation can never disagree with the room
    // made for it.
    let yTickLabels =
        ticks
        |> List.map (fun t ->
            Shape.Label(
                r2 (plotX0 - style.TickLabelGap),
                r2 (yScale t + style.TickLabelBaselineDy),
                TextSource.Literal(yTickLabelText t),
                textStyle style (Some style.LabelOpacity) TextAnchor.End tickSize Emphasis.Normal
            ))

    // x-axis labels — band arms label each category under its band centre;
    // Scatter labels its numeric x-ticks along the linear axis (Phase 636).
    //
    // A tilted category label is `End`-anchored at the band centre and rotated
    // NEGATIVELY (counter-clockwise, against `DrawStyle.Rotation`'s clockwise
    // convention): the anchor is the pivot, so the text ENDS under the band's
    // tick and runs back down-and-left, reading up-to-the-right into it. The
    // opposite sign would swing the same text up into the plot area. At the
    // vertical arm this degenerates to reading bottom-up — the y-axis title's
    // convention. Scatter's numeric ticks stay horizontal + `Middle`: they are
    // short by construction, and centring them on their tick is the correct
    // reading of a value axis.
    let tiltedLabelStyle =
        let s =
            textStyle style (Some style.LabelOpacity) TextAnchor.End tickSize Emphasis.Normal

        { s with
            Rotation = Some(r2 -tiltDegrees) }

    let xLabels =
        if isScatter then
            xTicks
            |> List.map (fun t ->
                Shape.Label(
                    xScale t,
                    r2 (plotY1 + style.CategoryLabelOffsetY),
                    TextSource.Literal(xTickText t),
                    textStyle style (Some style.LabelOpacity) TextAnchor.Middle tickSize Emphasis.Normal
                ))
        else
            categoryTexts
            |> Array.mapi (fun i c ->
                Shape.Label(
                    centreX i,
                    r2 (plotY1 + style.CategoryLabelOffsetY),
                    TextSource.Literal c,
                    (if tiltDegrees > 0.0 then
                         tiltedLabelStyle
                     else
                         textStyle style (Some style.LabelOpacity) TextAnchor.Middle tickSize Emphasis.Normal)
                ))
            |> Array.toList

    // ── Axis titles (a name on both axes) ──
    let capitalise (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpper s.[0]) + s.Substring 1

    // The top-left slot states the value axis's DISPLAY UNIT once ("Millions",
    // "Millions of £", "M£") when scaling applies, and otherwise keeps the
    // horizontal "Value" hint it has always carried. Placement is deliberately
    // unchanged here — axis titles are the next phase's, and moving the slot
    // and changing its text in one change-set would make either one
    // unattributable in the goldens.
    let axisTitles =
        [ Shape.Label(
              r2 ((plotX0 + plotX1) / 2.0),
              r2 (style.Height - style.AxisTitleBottomOffset),
              TextSource.Literal(capitalise spec.XField),
              textStyle style None TextAnchor.Middle tickSize Emphasis.Normal
          )
          Shape.Label(
              r2 style.AxisTitleLeftX,
              r2 (plotY0 - style.AxisTitleTopOffset),
              TextSource.Literal(
                  if yDisplayUnit.Label = "" then
                      "Value"
                  else
                      yDisplayUnit.Label
              ),
              textStyle style None TextAnchor.Start tickSize Emphasis.Normal
          ) ]

    // ── Series geometry ──
    let seriesShapes =
        match spec.Kind with
        | ChartKind.Bar when stacked ->
            // One capped bar per category, centred in its band; series stack as
            // segments between consecutive cumulative sums (Phase 637), each
            // shortened by `StackSegmentGap` on the side facing the next segment
            // so the boundaries read as gaps rather than colour changes
            // (Phase 875).
            let groupW = bandW * style.BarGroupWidthFraction
            let bw = r2 (min (groupW * style.BarWidthFraction) style.BarMaxThickness)

            [ for i in 0 .. n - 1 do
                  let bx = r2 (plotX0 + bandW * float i + (bandW - bw) / 2.0)
                  let cums = cumsFor i

                  for j in 0 .. m - 1 do
                      let y0 = yScale cums.[j]
                      let y1 = yScale cums.[j + 1]
                      // The gap comes off the far side from the baseline, and
                      // only where another segment follows — so the stack's
                      // outer tip keeps its full height and the total stays
                      // honest. `max 0.0` covers a segment thinner than the gap.
                      let gap = if j < m - 1 then style.StackSegmentGap else 0.0
                      let top = r2 (min y0 y1 + (if y1 < y0 then gap else 0.0))
                      let hgt = r2 (max 0.0 (abs (y1 - y0) - gap))

                      Shape.Rectangle(
                          bx,
                          top,
                          bw,
                          hgt,
                          None,
                          styleFill (colourFor style j) |> withMark yFields.[j] categories.[i]
                      ) ]
        | ChartKind.Bar ->
            let groupW = bandW * style.BarGroupWidthFraction
            let subW = if m > 0 then groupW / float m else groupW
            let bw = r2 (min (subW * style.BarWidthFraction) style.BarMaxThickness)
            let baseY = yScale 0.0

            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]

                  for i in 0 .. n - 1 do
                      let v = values.[i]
                      // Centre the (possibly capped) bar in its own sub-slot, so
                      // a cap takes air off BOTH sides and the group stays
                      // symmetric about the band centre.
                      let slotX = plotX0 + bandW * float i + (bandW - groupW) / 2.0 + float j * subW
                      let bx = r2 (slotX + (subW - bw) / 2.0)
                      let vy = yScale v
                      let top = min vy baseY
                      let hgt = r2 (abs (vy - baseY))

                      Shape.Rectangle(bx, top, bw, hgt, None, styleFill colour |> withMark yFields.[j] categories.[i]) ]
        | ChartKind.Area when stacked ->
            // Cumulative bands, bottom band first (painter's order): band j fills
            // between boundary j (below) and boundary j+1 (above); its upper
            // boundary carries the full-strength series edge (Phase 637).
            if n = 0 then
                []
            else
                let cums = Array.init n cumsFor

                [ for j in 0 .. m - 1 do
                      let colour = colourFor style j
                      let yf = yFields.[j]

                      let upper = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale cums.[i].[j + 1]) ]

                      let lower = [ for i in n - 1 .. -1 .. 0 -> dp (centreX i) (yScale cums.[i].[j]) ]

                      yield
                          Shape.Polygon(
                              upper @ lower,
                              styleFillOpacity colour style.AreaFillOpacity |> withSeriesMark yf
                          )

                      yield Shape.Polyline(upper, styleStroke colour style.SeriesStrokeWidth |> withSeriesMark yf) ]
        | ChartKind.Area ->
            // Overlaid baseline-closed bands in palette order (painter's order:
            // later series draw over earlier); the translucent fill keeps the
            // overlap legible, the Polyline edge keeps each series distinct.
            if n = 0 then
                []
            else
                let baseY = yScale 0.0

                [ for j in 0 .. m - 1 do
                      let colour = colourFor style j
                      let values = series.[j]
                      let yf = yFields.[j]

                      let points = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale values.[i]) ]

                      let band = (dp (centreX 0) baseY :: points) @ [ dp (centreX (n - 1)) baseY ]

                      yield Shape.Polygon(band, styleFillOpacity colour style.AreaFillOpacity |> withSeriesMark yf)
                      yield Shape.Polyline(points, styleStroke colour style.SeriesStrokeWidth |> withSeriesMark yf) ]
        | ChartKind.Line ->
            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]

                  let points = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale values.[i]) ]

                  Shape.Polyline(points, styleStroke colour style.SeriesStrokeWidth |> withSeriesMark yFields.[j]) ]
        | ChartKind.Scatter ->
            // Fixed-radius point marks per datum (Phase 636). A non-numeric
            // x/y cell reads 0.0 (`numericOf`'s posture, shared with the other
            // arms) — grounded validation makes that loud upstream, not here.
            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]
                  let yf = yFields.[j]

                  for i in 0 .. n - 1 do
                      Shape.Circle(
                          xScale xValues.[i],
                          yScale values.[i],
                          style.ScatterPointRadius,
                          styleFill colour |> withMark yf (DrawingSvg.formatNum xValues.[i])
                      ) ]
        | _ -> []

    // ── Legend (only when >1 series) — a swatch + series name per series ──
    //
    // The pitch is PER ENTRY since Phase 879: an entry occupies its swatch-to-
    // label offset, the estimated width of its own name, and the inter-entry
    // gap, so entries are laid out cumulatively rather than on a fixed stride.
    // A 30-character series name now pushes its neighbour along instead of
    // being overwritten by it — which the retired flat 100 px pitch could not
    // do past about twelve characters.
    //
    // `ChartStyle.LegendPosition` is still NOT consumed, and a long enough row
    // still runs off the right edge: WHERE the legend sits and what happens
    // when it overflows are one problem, and they land together in a later
    // phase. Pitch alone is what deterministic metrics can fix, and it is
    // fixed; the overflow is unchanged, deliberately.
    let legendEntryWidth (j: int) : float =
        style.LegendLabelOffsetX
        + TextMetrics.width tickSize yFields.[j]
        + style.LegendEntryGap

    let legendX =
        // Prefix sums — entry j starts where every earlier entry ended.
        Array.init m id |> Array.scan (fun acc j -> acc + legendEntryWidth j) plotX0

    let legend =
        if m > 1 then
            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let lx = r2 legendX.[j]

                  yield
                      Shape.Rectangle(
                          lx,
                          style.LegendSwatchY,
                          style.LegendSwatchSize,
                          style.LegendSwatchSize,
                          Some style.LegendSwatchCornerRadius,
                          styleFill colour
                      )

                  yield
                      Shape.Label(
                          r2 (lx + style.LegendLabelOffsetX),
                          style.LegendLabelBaselineY,
                          TextSource.Literal yFields.[j],
                          textStyle style (Some style.LabelOpacity) TextAnchor.Start tickSize Emphasis.Normal
                      ) ]
        else
            []

    // ── Visible title (a Label — bigger + emphasised) + the a11y Title ──
    let titleX, titleAnchor =
        match style.TitleAlignment with
        | ChartTitleAlignment.Left -> r2 plotX0, TextAnchor.Start
        | ChartTitleAlignment.Centre -> r2 ((plotX0 + plotX1) / 2.0), TextAnchor.Middle
        | ChartTitleAlignment.Right -> r2 plotX1, TextAnchor.End

    let titleShapes =
        match spec.Title with
        | Some t ->
            [ Shape.Label(titleX, style.TitleBaselineY, t, textStyle style None titleAnchor titleSize Emphasis.Loud) ]
        | None -> []

    // ── Pie (Phase 638) — the polar arm: no cartesian chrome ──
    //
    // Bounded v1: exactly ONE series (multi-series pie is a grounded-validation
    // refusal upstream, never a silent first-series truncation) and non-negative
    // values (any negative refuses the geometry — a mixed-sign pie has no
    // honest reading). Zero-value categories draw no wedge but keep their
    // legend row. Wedges start at 12 o'clock and sweep clockwise; arcs are the
    // standard <=90-degree-segment cubic-Bezier approximation (the closed
    // `CurveCommand` vocabulary has no arc case, deliberately). A lone 100%
    // category degenerates to a `Circle`. Category share reads in the legend
    // ("name (NN%)") — outside labels with leader lines are a later variant.
    // Trig note: cos/sin are IEEE-double library calls on every host; a
    // last-ulp divergence cannot flip the 2 dp `r2` rounding except at exact
    // .005 boundaries, the same exposure `niceNum`'s `10.0 ** exp` already
    // carries.
    let pieShapes () : Shape list =
        let values = if m = 1 then series.[0] else [||]

        let refused = m <> 1 || values |> Array.exists (fun v -> v < 0.0)

        let total = Array.sum values

        if refused || total <= 0.0 then
            []
        else
            let cx = r2 ((plotX0 + plotX1) / 2.0)
            let cy = r2 ((plotY0 + plotY1) / 2.0)
            let radius = style.PieRadius

            let pt (a: float) : DrawPoint =
                dp (r2 (cx + radius * cos a)) (r2 (cy + radius * sin a))

            let arcCubics (a0: float) (a1: float) : CurveCommand list =
                let segments = max 1 (int (ceil ((a1 - a0) / (System.Math.PI / 2.0) - 1e-9)))

                [ for s in 0 .. segments - 1 do
                      let t0 = a0 + (a1 - a0) * float s / float segments
                      let t1 = a0 + (a1 - a0) * float (s + 1) / float segments
                      let k = 4.0 / 3.0 * tan ((t1 - t0) / 4.0)

                      let c1 =
                          dp (r2 (cx + radius * (cos t0 - k * sin t0))) (r2 (cy + radius * (sin t0 + k * cos t0)))

                      let c2 =
                          dp (r2 (cx + radius * (cos t1 + k * sin t1))) (r2 (cy + radius * (sin t1 - k * cos t1)))

                      CurveCommand.CubicTo(c1, c2, pt t1) ]

            let fractions = values |> Array.map (fun v -> v / total)
            let starts = fractions |> Array.scan (+) 0.0
            let top = -System.Math.PI / 2.0

            // Half the angular padding comes off each end of every wedge
            // (Phase 875), so the separation is a sliver of absent ink — no
            // surface colour is needed and the result is theme-invariant, which
            // a stroked wedge border could not be.
            let halfGap = style.WedgeGapDegrees * System.Math.PI / 360.0

            let segs =
                let yf = yFields.[0]

                [ for i in 0 .. n - 1 do
                      let f = fractions.[i]

                      if f > 0.0 then
                          let colour = colourFor style i
                          let markStyle = styleFill colour |> withMark yf categories.[i]

                          if f >= 1.0 - 1e-9 then
                              // A lone 100% category is a circle — there is no
                              // neighbour to separate from, so no padding.
                              yield Shape.Circle(cx, cy, radius, markStyle)
                          else
                              let a0 = top + 2.0 * System.Math.PI * starts.[i] + halfGap
                              let a1 = top + 2.0 * System.Math.PI * starts.[i + 1] - halfGap

                              // A wedge narrower than the padding is DROPPED
                              // rather than drawn inverted — the alternative is
                              // a sliver sweeping the wrong way round the
                              // circle, which is a wrong picture, not a small one.
                              if a1 > a0 then
                                  let cmds =
                                      [ CurveCommand.MoveTo(dp cx cy); CurveCommand.LineTo(pt a0) ]
                                      @ arcCubics a0 a1
                                      @ [ CurveCommand.Close ]

                                  yield Shape.Curve(cmds, markStyle) ]

            // Vertical category legend on the right — categories take the
            // palette roles a cartesian chart gives its series.
            let pieLegend =
                let swatchX = style.Width - style.PieLegendOffsetX

                [ for i in 0 .. n - 1 do
                      let ly = style.PieLegendTopY + style.PieLegendPitchY * float i

                      yield
                          Shape.Rectangle(
                              r2 swatchX,
                              r2 ly,
                              style.LegendSwatchSize,
                              style.LegendSwatchSize,
                              Some style.LegendSwatchCornerRadius,
                              styleFill (colourFor style i)
                          )

                      // Routed through the canonical formatter (Phase 876) —
                      // one rounding + rendering rule for every number this
                      // module prints. A share is a whole percent here, so the
                      // shipped `NN%` shape is unchanged.
                      let pct = formatValue None 1.0 false 1.0 (fractions.[i] * 100.0)

                      yield
                          Shape.Label(
                              r2 (swatchX + style.LegendLabelOffsetX),
                              r2 (ly + style.PieLegendLabelBaselineDy),
                              TextSource.Literal(sprintf "%s (%s%%)" categories.[i] pct),
                              textStyle style (Some style.LabelOpacity) TextAnchor.Start tickSize Emphasis.Normal
                          ) ]

            segs @ pieLegend

    // Pie is polar — no axes/gridlines/tick chrome; every other arm assembles
    // the shared cartesian chrome in painter's order: gridlines (h then v), the
    // zero baseline, axes, tick marks, y-tick + x labels, axis titles, series,
    // legend, chart title.
    let shapes =
        match spec.Kind with
        | ChartKind.Pie -> pieShapes () @ titleShapes
        | _ ->
            gridlines
            @ xGridlines
            @ zeroLine
            @ axes
            @ tickMarks
            @ yTickLabels
            @ xLabels
            @ axisTitles
            @ seriesShapes
            @ legend
            @ titleShapes

    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = style.Width
          Height = style.Height }
      Shapes = shapes
      Style = emptyStyle
      Title = spec.Title
      Description = None }

/// The drawing a refused lowering produces (Phase 790) under an explicit style:
/// the style's canvas, no shapes, and the refusal as the a11y `<desc>` — bounded
/// output that says why it is empty rather than a blank picture that does not.
let refusalDrawingWithStyle<'Msg> (style: ChartStyle) (spec: ChartSpec<'Msg>) (refusal: ChartRefusal) : DrawingSpec =
    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = style.Width
          Height = style.Height }
      Shapes = []
      Style = emptyStyle
      Title = spec.Title
      Description = Some(TextSource.Literal(describeRefusal refusal)) }

/// The refusal drawing under the shipped default style.
let refusalDrawing<'Msg> (spec: ChartSpec<'Msg>) (refusal: ChartRefusal) : DrawingSpec =
    refusalDrawingWithStyle ChartStyle.defaults spec refusal

/// Lower under explicit cost caps AND an explicit style (Phase 885), refusing
/// rather than doing unbounded work (Phase 790). The two parameters compose:
/// `ChartLimits` bounds the WORK, `ChartStyle` chooses the APPEARANCE, and
/// neither is a `ChartSpec` wire field. The row source is read at most
/// `MaxPointsPerSeries + 1` deep, so an over-budget — or unbounded — sequence is
/// never materialised.
let tryLowerWithStyle<'Msg>
    (limits: ChartLimits)
    (style: ChartStyle)
    (spec: ChartSpec<'Msg>)
    (rows: Row seq)
    : Result<DrawingSpec, ChartRefusal> =
    let seriesCount = List.length spec.YFields

    if seriesCount > limits.MaxSeries then
        Error(TooManySeries(seriesCount, limits.MaxSeries))
    else
        let capped =
            if limits.MaxPointsPerSeries = System.Int32.MaxValue then
                rows |> Seq.toList
            else
                rows |> Seq.truncate (limits.MaxPointsPerSeries + 1) |> Seq.toList

        let observed = List.length capped

        if observed > limits.MaxPointsPerSeries then
            Error(TooManyPoints(observed, limits.MaxPointsPerSeries))
        else
            Ok(lowerRows style spec capped)

/// Lower under explicit cost caps and the shipped default style, refusing rather
/// than doing unbounded work (Phase 790).
let tryLowerWith<'Msg>
    (limits: ChartLimits)
    (spec: ChartSpec<'Msg>)
    (rows: Row seq)
    : Result<DrawingSpec, ChartRefusal> =
    tryLowerWithStyle limits ChartStyle.defaults spec rows

/// Lower under the shipped default caps, surfacing a refusal typed.
let tryLower<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) : Result<DrawingSpec, ChartRefusal> =
    tryLowerWith ChartLimits.defaults spec rows

/// Lower under explicit caps AND an explicit style (Phase 885); a refusal renders
/// as the bounded refusal drawing, on the style's own canvas.
let lowerWithStyle<'Msg>
    (limits: ChartLimits)
    (style: ChartStyle)
    (spec: ChartSpec<'Msg>)
    (rows: Row seq)
    : DrawingSpec =
    match tryLowerWithStyle limits style spec rows with
    | Ok drawing -> drawing
    | Error refusal -> refusalDrawingWithStyle style spec refusal

/// Lower under explicit caps and the shipped default style; a refusal renders as
/// the bounded refusal drawing.
let lowerWith<'Msg> (limits: ChartLimits) (spec: ChartSpec<'Msg>) (rows: Row seq) : DrawingSpec =
    lowerWithStyle limits ChartStyle.defaults spec rows

/// Lower a resolved `ChartSpec` + data rows to a canonical `DrawingSpec` under
/// the shipped default cost caps (`ChartLimits.defaults`) and the shipped default
/// style (`ChartStyle.defaults`) — the corpus-pinned form every conformant host
/// reproduces. An over-budget chart yields the refusal drawing; `tryLower` is the
/// typed form for a caller that wants to handle the refusal itself.
let lower<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) : DrawingSpec =
    lowerWith ChartLimits.defaults spec rows
