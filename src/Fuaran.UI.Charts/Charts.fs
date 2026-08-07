module Fuaran.UI.Charts

// ============================================================================
//  Phase 526 — render-time Chart → Drawing lowering (bar / line, S3).
//  Phase 637 — stacked series (bar / area) + the Area arm.
//  Phase 636 — the Scatter arm (linear numeric x-scale, point marks).
//  Phase 638 — the Pie arm (polar, cubic-approximated wedges; donut variant
//              deferred to the next wire event — see the phase file).
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

// ─── Layout constants (the fixed canonical drawing space) ────────────────────

[<Literal>]
let private W = 640.0

[<Literal>]
let private H = 400.0

[<Literal>]
let private marginTop = 64.0 // title + legend band

[<Literal>]
let private marginRight = 28.0

[<Literal>]
let private marginBottom = 56.0 // x-axis category labels + x-axis title

[<Literal>]
let private marginLeft = 64.0 // right-aligned y-axis tick labels

let private plotX0 = marginLeft
let private plotX1 = W - marginRight
let private plotY0 = marginTop
let private plotY1 = H - marginBottom
let private plotW = plotX1 - plotX0
let private plotH = plotY1 - plotY0

/// A fixed, deterministic categorical palette (series index → colour).
let private palette =
    [| "#3366cc"; "#dc3912"; "#ff9900"; "#109618"; "#990099"; "#0099c6" |]

let private colourFor (i: int) : string = palette.[i % palette.Length]

// ─── Surface-relative ink (Phase 536 — theme-aware chart lowering, S4) ───────
//
// Structural + text ink is `currentColor` at a per-role opacity, so a lowered
// chart inks from the surface's own text colour and is legible on a light OR a
// dark surface without a CSS override (the rest of the renderer already themes
// colour via inherited CSS — this lowering was the lone place that baked literal
// hex). On a white surface with near-black text the chosen opacities reproduce
// the prior palette within rounding: 0.12 ≈ `#e0e0e0` (grid), 0.66 ≈ `#555`
// (labels), 0.8 ≈ `#333` (axis); titles ink full-strength (no opacity). Series
// (categorical data) colours stay hex — they must stay distinct + read on both
// surfaces. Theme is a lowering / render-time concern, never a `ChartSpec` wire
// field. See `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md` (S4).
let private ink = "currentColor"
let private axisOpacity = 0.8
let private gridOpacity = 0.12
let private labelOpacity = 0.66

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

/// A nice value domain + its tick values for `[lo, hi]`, targeting ~5 ticks.
let private niceDomain (lo: float) (hi: float) : float * float * float list =
    let hi = if hi = lo then lo + 1.0 else hi
    let targetTicks = 5.0
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

/// The chart's own font stack — carried in the wire (Phase 528.1), so a lowered
/// chart is self-contained + legible on every host without host CSS.
let private chartFont = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"

let private baseStyle: DrawStyle =
    { Fill = None
      Stroke = None
      StrokeWidth = None
      Opacity = None
      TextAnchor = None
      FontSize = None
      Emphasis = None
      FontFamily = None
      MarkId = None }

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

/// A translucent categorical fill (Phase 637 — area bands). The gridlines stay
/// legible through the band; the series' full-strength Polyline edge on top
/// carries the categorical colour at full contrast.
[<Literal>]
let private areaFillOpacity = 0.35

let private styleFillOpacity (fill: string) (opacity: float) : DrawStyle =
    { baseStyle with
        Fill = Some(Binding.Static(Some fill))
        Opacity = Some(Binding.Static(Some opacity)) }

/// A surface-relative structural stroke (Phase 536): `currentColor` at a per-role
/// opacity, so axis + gridlines ink from the surface's own text colour. Used for
/// the chrome (axes, gridlines) — series lines keep their categorical hex.
let private styleStrokeInk (opacity: float) (width: float) : DrawStyle =
    { baseStyle with
        Stroke = Some(Binding.Static(Some ink))
        StrokeWidth = Some(Binding.Static(Some width))
        Opacity = Some(Binding.Static(Some opacity)) }

let private emptyStyle: DrawStyle = baseStyle

/// A text-label style (Phase 536): surface-relative ink (`currentColor`) + an
/// optional per-role opacity (`None` = full-strength, e.g. titles) + alignment +
/// size + weight + the chart font.
let private textStyle (opacity: float option) (anchor: TextAnchor) (size: float) (emphasis: Emphasis) : DrawStyle =
    { baseStyle with
        Fill = Some(Binding.Static(Some ink))
        Opacity = opacity |> Option.map (Some >> Binding.Static)
        TextAnchor = Some anchor
        FontSize = Some size
        Emphasis = Some emphasis
        FontFamily = Some chartFont }

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
    /// (the palette itself has 6 colours) and 10 000 points is well past the
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

/// The core lowering: an ALREADY-CAPPED row list to a canonical `DrawingSpec`.
/// The public entry points (`lower` / `lowerWith` / `tryLower*`) apply the
/// Phase-790 cost caps before this runs, so it never sees an over-budget input.
/// Lowered arms: `Bar` (grouped + stacked), `Line`, `Area` (overlaid +
/// stacked), `Scatter` (linear numeric x), `Pie` (polar, single-series) —
/// Phases 533 + 637 + 636 + 638. `Heatmap` produces an empty drawing (its
/// lowering rule lands with its own phase). `Stacked = true` on a kind where
/// stacking is meaningless (`Line`, `Scatter`, `Pie`) is ignored — the flag
/// only changes `Bar` / `Area` geometry.
let private lowerRows<'Msg> (spec: ChartSpec<'Msg>) (rows: Row list) : DrawingSpec =
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
    let niceLo, niceHi, ticks = niceDomain (min 0.0 dataMin) (max 0.0 dataMax)

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
                niceDomain 0.0 1.0
            else
                niceDomain (Array.min xValues) (Array.max xValues)
        else
            0.0, 1.0, []

    let xScale (v: float) : float =
        r2 (plotX0 + (v - xNiceLo) / (xNiceHi - xNiceLo) * plotW)

    // ── Axes + gridlines ──
    let axes =
        [ Shape.Line(r2 plotX0, r2 plotY0, r2 plotX0, r2 plotY1, styleStrokeInk axisOpacity 1.0)
          Shape.Line(r2 plotX0, r2 plotY1, r2 plotX1, r2 plotY1, styleStrokeInk axisOpacity 1.0) ]

    let gridlines =
        ticks
        |> List.map (fun t ->
            let y = yScale t
            Shape.Line(r2 plotX0, y, r2 plotX1, y, styleStrokeInk gridOpacity 1.0))

    let tickSize = 13.0
    let titleSize = 16.0

    // y-axis tick labels — right-anchored (End) so the number column sits cleanly
    // in the left margin, ending just before the axis.
    let yTickLabels =
        ticks
        |> List.map (fun t ->
            Shape.Label(
                r2 (plotX0 - 8.0),
                r2 (yScale t + 4.0),
                TextSource.Literal(tickLabel t),
                textStyle (Some labelOpacity) TextAnchor.End tickSize Emphasis.Normal
            ))

    // x-axis labels — band arms label each category under its band centre;
    // Scatter labels its numeric x-ticks along the linear axis (Phase 636).
    let xLabels =
        if isScatter then
            xTicks
            |> List.map (fun t ->
                Shape.Label(
                    xScale t,
                    r2 (plotY1 + 20.0),
                    TextSource.Literal(tickLabel t),
                    textStyle (Some labelOpacity) TextAnchor.Middle tickSize Emphasis.Normal
                ))
        else
            categories
            |> Array.mapi (fun i c ->
                Shape.Label(
                    centreX i,
                    r2 (plotY1 + 20.0),
                    TextSource.Literal c,
                    textStyle (Some labelOpacity) TextAnchor.Middle tickSize Emphasis.Normal
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
              r2 (H - 12.0),
              TextSource.Literal(capitalise spec.XField),
              textStyle None TextAnchor.Middle tickSize Emphasis.Normal
          )
          Shape.Label(
              r2 8.0,
              r2 (plotY0 - 12.0),
              TextSource.Literal "Value",
              textStyle None TextAnchor.Start tickSize Emphasis.Normal
          ) ]

    // ── Series geometry ──
    let seriesShapes =
        match spec.Kind with
        | ChartKind.Bar when stacked ->
            // One full group-width bar per category; series stack as segments
            // between consecutive cumulative sums (Phase 637).
            let groupW = bandW * 0.7

            [ for i in 0 .. n - 1 do
                  let bx = r2 (plotX0 + bandW * float i + (bandW - groupW) / 2.0)
                  let bw = r2 (groupW * 0.9)
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
                          styleFill (colourFor j) |> withMark yFields.[j] categories.[i]
                      ) ]
        | ChartKind.Bar ->
            let groupW = bandW * 0.7
            let subW = if m > 0 then groupW / float m else groupW
            let baseY = yScale 0.0

            [ for j in 0 .. m - 1 do
                  let colour = colourFor j
                  let values = series.[j]

                  for i in 0 .. n - 1 do
                      let v = values.[i]
                      let bx = r2 (plotX0 + bandW * float i + (bandW - groupW) / 2.0 + float j * subW)
                      let bw = r2 (subW * 0.9)
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
                      let colour = colourFor j
                      let yf = yFields.[j]

                      let upper = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale cums.[i].[j + 1]) ]

                      let lower = [ for i in n - 1 .. -1 .. 0 -> dp (centreX i) (yScale cums.[i].[j]) ]

                      yield Shape.Polygon(upper @ lower, styleFillOpacity colour areaFillOpacity |> withSeriesMark yf)
                      yield Shape.Polyline(upper, styleStroke colour 2.0 |> withSeriesMark yf) ]
        | ChartKind.Area ->
            // Overlaid baseline-closed bands in palette order (painter's order:
            // later series draw over earlier); the translucent fill keeps the
            // overlap legible, the Polyline edge keeps each series distinct.
            if n = 0 then
                []
            else
                let baseY = yScale 0.0

                [ for j in 0 .. m - 1 do
                      let colour = colourFor j
                      let values = series.[j]
                      let yf = yFields.[j]

                      let points = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale values.[i]) ]

                      let band = (dp (centreX 0) baseY :: points) @ [ dp (centreX (n - 1)) baseY ]

                      yield Shape.Polygon(band, styleFillOpacity colour areaFillOpacity |> withSeriesMark yf)
                      yield Shape.Polyline(points, styleStroke colour 2.0 |> withSeriesMark yf) ]
        | ChartKind.Line ->
            [ for j in 0 .. m - 1 do
                  let colour = colourFor j
                  let values = series.[j]

                  let points = [ for i in 0 .. n - 1 -> dp (centreX i) (yScale values.[i]) ]

                  Shape.Polyline(points, styleStroke colour 2.0 |> withSeriesMark yFields.[j]) ]
        | ChartKind.Scatter ->
            // Fixed-radius point marks per datum (Phase 636). A non-numeric
            // x/y cell reads 0.0 (`numericOf`'s posture, shared with the other
            // arms) — grounded validation makes that loud upstream, not here.
            [ for j in 0 .. m - 1 do
                  let colour = colourFor j
                  let values = series.[j]
                  let yf = yFields.[j]

                  for i in 0 .. n - 1 do
                      Shape.Circle(
                          xScale xValues.[i],
                          yScale values.[i],
                          4.0,
                          styleFill colour |> withMark yf (DrawingSvg.formatNum xValues.[i])
                      ) ]
        | _ -> []

    // ── Legend (only when >1 series) — a swatch + series name per series ──
    let legend =
        if m > 1 then
            [ for j in 0 .. m - 1 do
                  let colour = colourFor j
                  let lx = r2 (plotX0 + float j * 100.0)
                  yield Shape.Rectangle(lx, 34.0, 10.0, 10.0, Some 2.0, styleFill colour)

                  yield
                      Shape.Label(
                          r2 (lx + 15.0),
                          43.0,
                          TextSource.Literal yFields.[j],
                          textStyle (Some labelOpacity) TextAnchor.Start tickSize Emphasis.Normal
                      ) ]
        else
            []

    // ── Visible title (a Label — bigger + emphasised) + the a11y Title ──
    let titleShapes =
        match spec.Title with
        | Some t -> [ Shape.Label(r2 plotX0, 22.0, t, textStyle None TextAnchor.Start titleSize Emphasis.Loud) ]
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
            let radius = 130.0

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
                          let colour = colourFor i
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
                [ for i in 0 .. n - 1 do
                      let ly = 70.0 + 20.0 * float i

                      yield Shape.Rectangle(r2 (W - 168.0), r2 ly, 10.0, 10.0, Some 2.0, styleFill (colourFor i))

                      let pct = DrawingSvg.formatNum (floor (fractions.[i] * 100.0 + 0.5))

                      yield
                          Shape.Label(
                              r2 (W - 153.0),
                              r2 (ly + 9.0),
                              TextSource.Literal(sprintf "%s (%s%%)" categories.[i] pct),
                              textStyle (Some labelOpacity) TextAnchor.Start tickSize Emphasis.Normal
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
          Width = W
          Height = H }
      Shapes = shapes
      Style = emptyStyle
      Title = spec.Title
      Description = None }

/// The drawing a refused lowering produces (Phase 790): the same canvas, no
/// shapes, and the refusal as the a11y `<desc>` — bounded output that says why
/// it is empty rather than a blank picture that does not.
let refusalDrawing<'Msg> (spec: ChartSpec<'Msg>) (refusal: ChartRefusal) : DrawingSpec =
    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = W
          Height = H }
      Shapes = []
      Style = emptyStyle
      Title = spec.Title
      Description = Some(TextSource.Literal(describeRefusal refusal)) }

/// Lower under explicit cost caps, refusing rather than doing unbounded work
/// (Phase 790). The row source is read at most `MaxPointsPerSeries + 1` deep, so
/// an over-budget — or unbounded — sequence is never materialised.
let tryLowerWith<'Msg>
    (limits: ChartLimits)
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
            Ok(lowerRows spec capped)

/// Lower under the shipped default caps, surfacing a refusal typed.
let tryLower<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) : Result<DrawingSpec, ChartRefusal> =
    tryLowerWith ChartLimits.defaults spec rows

/// Lower under explicit caps; a refusal renders as the bounded refusal drawing.
let lowerWith<'Msg> (limits: ChartLimits) (spec: ChartSpec<'Msg>) (rows: Row seq) : DrawingSpec =
    match tryLowerWith limits spec rows with
    | Ok drawing -> drawing
    | Error refusal -> refusalDrawing spec refusal

/// Lower a resolved `ChartSpec` + data rows to a canonical `DrawingSpec` under
/// the shipped default cost caps (`ChartLimits.defaults`). An over-budget chart
/// yields the refusal drawing; `tryLower` is the typed form for a caller that
/// wants to handle the refusal itself.
let lower<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) : DrawingSpec =
    lowerWith ChartLimits.defaults spec rows
