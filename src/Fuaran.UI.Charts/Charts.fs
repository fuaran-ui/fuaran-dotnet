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
//              `ChartStyle.defaults` reproduces the pre-phase output
//              byte-identically, so it stays the corpus-pinned form; a host
//              passing its own style is a deliberate act off the conformance
//              path. Style is NEVER a `ChartSpec` wire field (D8).
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
//   * `ChartStyle.defaults` is CORPUS-PINNED. It reproduces the pre-885 output
//     byte-identically, so the `chart-lowering/*` goldens — and the cross-host
//     parity they certify (R2) — are untouched. A host passing its own style is
//     a deliberate act off the conformance path, and its output is its own.

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
/// horizontal row in the top margin regardless of this field; the positioning
/// mechanics land with the default-style restyle (Phase 875). The default
/// records the 2026-08-16 operator decision so the field is already right when
/// that phase wires it.
[<RequireQualifiedAccess>]
type ChartLegendPosition =
    | Top
    | Right
    | Bottom
    | Left

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
        /// Bottom margin — x-axis category labels + the x-axis title.
        MarginBottom: float
        /// Left margin — right-aligned y-axis tick labels.
        MarginLeft: float

        // ── Series palette ──
        /// The categorical palette, indexed by series (or, on the Pie arm, by
        /// category) modulo its length. Series colours stay literal hex: they
        /// must stay distinct AND read on a light or a dark surface, so they
        /// cannot ink from `currentColor` the way the chrome does (D8).
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
        /// Where the chart title sits along the plot's top edge.
        TitleAlignment: ChartTitleAlignment
        /// Baseline y of the visible chart title.
        TitleBaselineY: float

        // ── Ticks + axis labels ──
        /// Target number of y-axis ticks the `{1,2,5}·10ⁿ` nice-tick rule aims
        /// for (the gridline count follows).
        TargetTickCount: float
        /// Gap between the y-axis spine and the right edge of a tick label.
        TickLabelGap: float
        /// Baseline nudge that optically centres a tick label on its gridline.
        TickLabelBaselineDy: float
        /// Drop from the x-axis spine to the category / x-tick label baseline.
        CategoryLabelOffsetY: float
        /// Rotation applied to crowded category labels.
        ///
        /// **Reserved — not yet consumed.** The shipped lowering draws category
        /// labels horizontally; the tilt mechanics land with a later phase. The
        /// default records the 2026-08-16 operator decision (30°).
        LabelTiltDegrees: float
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
        /// Opacity of an area band's translucent fill. The gridlines stay
        /// legible through the band; the full-strength Polyline edge on top
        /// carries the categorical colour at full contrast.
        AreaFillOpacity: float
        /// Radius of a Scatter point mark.
        ScatterPointRadius: float

        // ── Legend geometry (the cartesian arms' horizontal top-margin row) ──
        /// Which edge the legend occupies. **Reserved — not yet consumed** (see
        /// `ChartLegendPosition`).
        LegendPosition: ChartLegendPosition
        /// Horizontal pitch between consecutive legend entries.
        LegendPitchX: float
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
    /// The shipped default style — **corpus-pinned**. These values reproduce the
    /// pre-885 lowering byte-identically, so the `chart-lowering/*` goldens and
    /// the cross-host parity they certify are unaffected by this phase. Change
    /// them only in a phase that regenerates the corpus (Phase 875).
    let defaults: ChartStyle =
        { Width = 640.0
          Height = 400.0
          MarginTop = 64.0
          MarginRight = 28.0
          MarginBottom = 56.0
          MarginLeft = 64.0
          Palette = [| "#3366cc"; "#dc3912"; "#ff9900"; "#109618"; "#990099"; "#0099c6" |]
          Ink = "currentColor"
          AxisOpacity = 0.8
          GridOpacity = 0.12
          LabelOpacity = 0.66
          AxisStrokeWidth = 1.0
          GridStrokeWidth = 1.0
          SeriesStrokeWidth = 2.0
          FontFamily = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
          TickFontSize = 13.0
          TitleFontSize = 16.0
          TitleAlignment = ChartTitleAlignment.Left
          TitleBaselineY = 22.0
          TargetTickCount = 5.0
          TickLabelGap = 8.0
          TickLabelBaselineDy = 4.0
          CategoryLabelOffsetY = 20.0
          LabelTiltDegrees = 30.0
          AxisTitleBottomOffset = 12.0
          AxisTitleLeftX = 8.0
          AxisTitleTopOffset = 12.0
          BarGroupWidthFraction = 0.7
          BarWidthFraction = 0.9
          AreaFillOpacity = 0.35
          ScatterPointRadius = 4.0
          LegendPosition = ChartLegendPosition.Right
          LegendPitchX = 100.0
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
          PositiveColour = "#109618"
          NegativeColour = "#dc3912"
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

/// A nice value domain + its tick values for `[lo, hi]`, targeting
/// `targetTicks` ticks (`ChartStyle.TargetTickCount`).
let private niceDomain (targetTicks: float) (lo: float) (hi: float) : float * float * float list =
    let hi = if hi = lo then lo + 1.0 else hi
    let range = niceNum (hi - lo) false
    let step = niceNum (range / (targetTicks - 1.0)) true
    let niceLo = floor (lo / step) * step
    let niceHi = ceil (hi / step) * step
    // Enumerate ticks by integer count (float accumulation would drift).
    let count = int (System.Math.Round((niceHi - niceLo) / step))

    let ticks = [ for i in 0..count -> r2 (niceLo + float i * step) ]

    niceLo, niceHi, ticks

/// Format a tick value: whole → integer, else 2-dp trimmed. Matches the SVG
/// coordinate form (`DrawingSvg.formatNum`) so ticks read consistently.
let private tickLabel (v: float) : string = DrawingSvg.formatNum (r2 v)

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
    /// (the default palette has 6 colours) and 10 000 points is well past the
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
    // The plot rectangle, derived from the style's canvas + margins.
    let plotX0 = style.MarginLeft
    let plotX1 = style.Width - style.MarginRight
    let plotY0 = style.MarginTop
    let plotY1 = style.Height - style.MarginBottom
    let plotW = plotX1 - plotX0
    let plotH = plotY1 - plotY0

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
    let niceLo, niceHi, ticks =
        niceDomain style.TargetTickCount (min 0.0 dataMin) (max 0.0 dataMax)

    let yScale (v: float) : float =
        r2 (plotY1 - (v - niceLo) / (niceHi - niceLo) * plotH)

    let bandW = if n > 0 then plotW / float n else plotW
    let centreX (i: int) : float = r2 (plotX0 + bandW * (float i + 0.5))

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

    let xNiceLo, xNiceHi, xTicks =
        if isScatter then
            if Array.isEmpty xValues then
                niceDomain style.TargetTickCount 0.0 1.0
            else
                niceDomain style.TargetTickCount (Array.min xValues) (Array.max xValues)
        else
            0.0, 1.0, []

    let xScale (v: float) : float =
        r2 (plotX0 + (v - xNiceLo) / (xNiceHi - xNiceLo) * plotW)

    // ── Axes + gridlines ──
    let axisStyle = styleStrokeInk style style.AxisOpacity style.AxisStrokeWidth

    let axes =
        [ Shape.Line(r2 plotX0, r2 plotY0, r2 plotX0, r2 plotY1, axisStyle)
          Shape.Line(r2 plotX0, r2 plotY1, r2 plotX1, r2 plotY1, axisStyle) ]

    let gridlines =
        ticks
        |> List.map (fun t ->
            let y = yScale t
            Shape.Line(r2 plotX0, y, r2 plotX1, y, styleStrokeInk style style.GridOpacity style.GridStrokeWidth))

    let tickSize = style.TickFontSize
    let titleSize = style.TitleFontSize

    // y-axis tick labels — right-anchored (End) so the number column sits cleanly
    // in the left margin, ending just before the axis.
    let yTickLabels =
        ticks
        |> List.map (fun t ->
            Shape.Label(
                r2 (plotX0 - style.TickLabelGap),
                r2 (yScale t + style.TickLabelBaselineDy),
                TextSource.Literal(tickLabel t),
                textStyle style (Some style.LabelOpacity) TextAnchor.End tickSize Emphasis.Normal
            ))

    // x-axis labels — band arms label each category under its band centre;
    // Scatter labels its numeric x-ticks along the linear axis (Phase 636).
    let xLabels =
        if isScatter then
            xTicks
            |> List.map (fun t ->
                Shape.Label(
                    xScale t,
                    r2 (plotY1 + style.CategoryLabelOffsetY),
                    TextSource.Literal(tickLabel t),
                    textStyle style (Some style.LabelOpacity) TextAnchor.Middle tickSize Emphasis.Normal
                ))
        else
            categories
            |> Array.mapi (fun i c ->
                Shape.Label(
                    centreX i,
                    r2 (plotY1 + style.CategoryLabelOffsetY),
                    TextSource.Literal c,
                    textStyle style (Some style.LabelOpacity) TextAnchor.Middle tickSize Emphasis.Normal
                ))
            |> Array.toList

    // ── Axis titles (a name on both axes) ──
    let capitalise (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpper s.[0]) + s.Substring 1

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
              TextSource.Literal "Value",
              textStyle style None TextAnchor.Start tickSize Emphasis.Normal
          ) ]

    // ── Series geometry ──
    let seriesShapes =
        match spec.Kind with
        | ChartKind.Bar when stacked ->
            // One full group-width bar per category; series stack as segments
            // between consecutive cumulative sums (Phase 637).
            let groupW = bandW * style.BarGroupWidthFraction

            [ for i in 0 .. n - 1 do
                  let bx = r2 (plotX0 + bandW * float i + (bandW - groupW) / 2.0)
                  let bw = r2 (groupW * style.BarWidthFraction)
                  let cums = cumsFor i

                  for j in 0 .. m - 1 do
                      let y0 = yScale cums.[j]
                      let y1 = yScale cums.[j + 1]
                      let top = min y0 y1
                      let hgt = r2 (abs (y1 - y0))

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
            let baseY = yScale 0.0

            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]

                  for i in 0 .. n - 1 do
                      let v = values.[i]
                      let bx = r2 (plotX0 + bandW * float i + (bandW - groupW) / 2.0 + float j * subW)
                      let bw = r2 (subW * style.BarWidthFraction)
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
    // `ChartStyle.LegendPosition` is declared but NOT yet consumed: the legend
    // is a horizontal row in the top margin whatever it says (Phase 875 wires
    // the positioning).
    let legend =
        if m > 1 then
            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let lx = r2 (plotX0 + float j * style.LegendPitchX)

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

            let segs =
                let yf = yFields.[0]

                [ for i in 0 .. n - 1 do
                      let f = fractions.[i]

                      if f > 0.0 then
                          let colour = colourFor style i
                          let markStyle = styleFill colour |> withMark yf categories.[i]

                          if f >= 1.0 - 1e-9 then
                              yield Shape.Circle(cx, cy, radius, markStyle)
                          else
                              let a0 = top + 2.0 * System.Math.PI * starts.[i]
                              let a1 = top + 2.0 * System.Math.PI * starts.[i + 1]

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

                      let pct = DrawingSvg.formatNum (floor (fractions.[i] * 100.0 + 0.5))

                      yield
                          Shape.Label(
                              r2 (swatchX + style.LegendLabelOffsetX),
                              r2 (ly + style.PieLegendLabelBaselineDy),
                              TextSource.Literal(sprintf "%s (%s%%)" categories.[i] pct),
                              textStyle style (Some style.LabelOpacity) TextAnchor.Start tickSize Emphasis.Normal
                          ) ]

            segs @ pieLegend

    // Pie is polar — no axes/gridlines/tick chrome; every other arm assembles
    // the shared cartesian chrome in painter's order: gridlines, axes, y-tick +
    // x labels, axis titles, series, legend, chart title.
    let shapes =
        match spec.Kind with
        | ChartKind.Pie -> pieShapes () @ titleShapes
        | _ ->
            gridlines
            @ axes
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
