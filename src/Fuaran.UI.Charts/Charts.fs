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
//  Phase 878 — AXIS TITLES + SUBTITLE. `ChartSpec` gained `XTitle` / `YTitle` /
//              `Subtitle`, all optional and all default-ON in the sense that
//              matters: an absent axis title falls back to the capitalised
//              field name, so an axis is never nameless — which is what finally
//              retired the hardcoded `"Value"` y-hint. The y title renders
//              ROTATED in the left margin (whose autosize now reserves its
//              line); the subtitle renders muted under the title (whose line
//              the top margin reserves only when one is present). See "Axis
//              titles + the display-unit slot" at the emission site for the
//              three composition rules and the date-axis note.
//  Phase 880 — LEGEND PLACEMENT, and the default moved to the RIGHT. `ChartSpec`
//              gained an optional `LegendPosition` (`Top | Right | Bottom |
//              None`); `ChartStyle.LegendPosition` carries the default, and an
//              explicit spec value beats it. The default `Right` arm is a
//              VERTICAL COLUMN — one row per entry, width from Phase 879's
//              metrics, the plot shrinking by it — which is what structurally
//              retires the top band's overflow past ~6 entries rather than
//              patching it. The pie arm's own right-hand legend WAS this shape
//              already, so the two converged: one `legendEntries` list, one
//              emitter, one set of constants, and the pie honours the position
//              like every other arm. A single-SERIES cartesian chart still
//              draws no legend (the title names the series); a single-series
//              PIE still does, because its legend is over categories.
//  Phase 881 — SELECTIVE DATA LABELS. `ChartSpec` gained an optional
//              `DataLabels` (`Off | Ends`), absent = `Off` = the default, so
//              every pre-881 golden is byte-unchanged. `Ends` writes the value
//              at bar CAPS (a stacked bar's TOTAL only) and at LINE/AREA
//              ENDPOINTS — and nowhere else: there is deliberately no
//              all-points case, so the API cannot express a number on every
//              interior point. Values run through Phase 876's formatter, so a
//              label and a tick always agree; placement is gated by Phase
//              879's `TextMetrics.fitsBox` and SUPPRESSED on no-fit, never
//              clipped, overlapped, or moved inside a bar. No label reserves
//              space, so nothing about the layout moved.
//  Phase 903 — TWO CORRECTIONS to shipped band-axis behaviour (operator,
//              2026-08-17). (1) The category-label tilt becomes the MIDDLE RUNG
//              of a fit-driven ladder rather than the resting state: labels are
//              HORIZONTAL while every one fits its band, all rotate to
//              `LabelTiltDegrees` when any does not, and all escalate to
//              `VerticalTiltDegrees` when that no longer packs — uniform per
//              axis, never mixed. (2) A BAND axis's outside tick marks move to
//              the `n+1` band BOUNDARIES (the category-axis convention),
//              delimiting the groups; labels stay centred in their bands. Ticks
//              stay AT the value only where the axis is continuous — the y axis
//              and Scatter's numeric x. No wire change; band-arm goldens moved
//              once, across every conformant host, in one change-set.
//  Phase 882 — THE TEMPORAL X-AXIS, the second non-band x-scale after the
//              Phase-636 Scatter arm. `ChartSpec` gained an optional `XScale`
//              (`Category | Temporal`); absent is `Category`, so every pre-882
//              chart is byte-unchanged. `Temporal` is DECLARED, never inferred
//              — the language grounds the declaration against the column type
//              (FUARAN097) instead of sniffing the cell strings, because an
//              inferred axis would make the same wire tree draw differently
//              depending on where its rows came from. Dates map LINEARLY over
//              days since 1970-01-01 (proleptic Gregorian, UTC, integer
//              arithmetic this module owns — no host date type anywhere in the
//              layout path); ticks land on calendar-nice boundaries drawn from
//              a fixed ladder; the tick FORMAT follows the chosen rung's
//              nominal length at the operator's thresholds (`> 365` days ⇒
//              `yyyy`, `> 27` ⇒ `mmm yy`, else `dd mmm yy`). It takes the
//              CONTINUOUS side of Phase 903's tick split — marks at the value,
//              labels centred on them, vertical gridlines — and wires Phase
//              878's date-axis rule: a temporal axis suppresses its DEFAULT
//              x-title, never an explicit one. See "The temporal x-axis" below,
//              the normative cross-host spec.
//  2026-08-18 — THE BAND OVERFLOW RULE (operator decision). An explicit `Top` or
//              `Bottom` legend whose entries do not pack into one band row FALLS
//              BACK TO THE RIGHT-HAND COLUMN. `LegendPosition.Top`/`Bottom` now
//              read "band if it fits, column if it cannot" — the author's intent
//              (a VISIBLE legend) is honoured either way, and the wire is
//              unchanged. The column never loses information, never grows the
//              band unboundedly, and reuses shipped layout; a second row and a
//              refusal were both considered and declined. See "Legend placement"
//              at the emission site for the predicate and the alternatives.
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

/// Which edge the series legend occupies — or `None`, which suppresses it.
///
/// **Consumed since Phase 880**, and now a WIRE vocabulary: the DU lives in the
/// wire layer (`Fuaran.UI.Generated`) and is re-exported here so a style can
/// name a default without the styling module owning the vocabulary. This
/// abbreviation is the same type, so `ChartLegendPosition.Right` reads the same
/// as it did when Phase 885 declared the field reserved.
///
/// Phase 885's `Left` case is retired: the left edge is the y axis's — the tick
/// column and the rotated axis title are already there — and no lowering path
/// ever consumed it, so nothing rendered changes.
type ChartLegendPosition = Fuaran.UI.Types.ChartLegendPosition

/// Whether the chart writes its values onto the picture, and where (Phase 881).
///
/// A WIRE vocabulary (`ChartSpec.DataLabels`), re-exported here so this module
/// can name it without owning it — the same shape `ChartLegendPosition` takes.
/// `Off` is the default AND what an absent field means, so a pre-881 spec draws
/// a pre-881 picture byte-for-byte.
type ChartDataLabels = Fuaran.UI.Types.ChartDataLabels

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
        /// Font size of the subtitle (Phase 878). Deliberately BELOW
        /// `TitleFontSize` — the subtitle is a qualifier on the title, and a
        /// qualifier set at the same size competes with what it qualifies.
        SubtitleFontSize: float
        /// Baseline y of the subtitle — directly under the title, and sharing
        /// its `TitleAlignment`, so the two read as one block.
        SubtitleBaselineY: float

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
        /// — since Phase 903 — one per BAND BOUNDARY on a category axis (`n+1`
        /// for `n` bands, delimiting the groups rather than pointing at their
        /// centres), or one per x tick on the Scatter arm, whose x is a
        /// continuous value axis. `0.0` suppresses them.
        TickMarkLength: float
        /// Baseline nudge that optically centres a tick label on its gridline.
        TickLabelBaselineDy: float
        /// Drop from the x-axis spine to the category / x-tick label baseline.
        CategoryLabelOffsetY: float
        /// The MAGNITUDE of the MIDDLE RUNG of the band-arm category-label
        /// angle ladder (Phase 879; the ladder itself Phase 903), in degrees.
        /// The ladder is fit-driven and UNIFORM per axis: labels are horizontal
        /// while every one of them fits its band, all rotate to this angle when
        /// any does not, and all escalate to `VerticalTiltDegrees` when this
        /// angle no longer packs into the band pitch either. (Phase 879 read the
        /// tilt as the resting state; the operator's 2026-08-17 correction makes
        /// it the middle rung.) The lowering emits `DrawStyle.Rotation = -tilt`:
        /// `Rotation` is clockwise (SVG's convention), and the tilt has to be
        /// COUNTER-clockwise so the text falls AWAY from the axis rather than
        /// climbing into the plot. `0.0` opts out of rotation entirely — labels
        /// stay horizontal and `Middle`-anchored at every label length, and no
        /// escalation is considered.
        LabelTiltDegrees: float
        /// The terminal rung of the ladder (Phase 879). At 90° a label
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
        /// x of the DISPLAY-UNIT slot (left-anchored, above the plot). Since
        /// Phase 878 this slot carries the Phase-876 unit label and nothing
        /// else — the y axis's NAME moved to the rotated left-margin title.
        AxisTitleLeftX: float
        /// Rise from the plot's top edge to the display-unit slot's baseline.
        AxisTitleTopOffset: float
        /// x of the ROTATED y-axis title's baseline, measured from the canvas
        /// LEFT EDGE (Phase 878) — not from the autosized margin, so the title
        /// does not slide about as tick widths change. A rotated-by
        /// `-YAxisTitleDegrees` label's ascenders extend LEFT of its baseline,
        /// which is why this sits near the outer edge of the reserved band
        /// rather than at it.
        YAxisTitleOffsetX: float
        /// The MAGNITUDE of the y-axis title's rotation, in degrees
        /// (Phase 878). Emitted as `DrawStyle.Rotation = -YAxisTitleDegrees`:
        /// `Rotation` is clockwise (SVG's convention), so the negative angle
        /// reads BOTTOM-UP — the conventional treatment, and the same sign
        /// convention `VerticalTiltDegrees` already uses. `0.0` leaves the
        /// title horizontal.
        YAxisTitleDegrees: float

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

        // ── Legend geometry (Phase 880 — ONE legend, four placements) ──
        //
        // Both shapes are here because both are reachable from any arm: a
        // horizontal BAND (the `Top` / `Bottom` arms — Phase 879's per-entry
        // pitch) and a vertical COLUMN (`Right`, the default — one row per
        // entry, the plot shrinking by the column's width). The pie arm draws
        // through exactly these fields too since Phase 880; its own
        // `PieLegend*` constants are retired.
        /// Which edge the legend occupies when the `ChartSpec` does not say —
        /// the DEFAULT, not the answer. An explicit `ChartSpec.LegendPosition`
        /// beats it (Phase 880), because WHERE the legend goes is the author's
        /// meaning where the geometry below is the host's.
        LegendPosition: ChartLegendPosition
        /// BAND arms only. Horizontal padding after a legend entry's label,
        /// before the next entry's swatch (Phase 879). The pitch itself is no
        /// constant: an entry occupies `LegendLabelOffsetX + the estimated
        /// width of its own name + LegendEntryGap`, so a 30-character name
        /// pushes its neighbour along instead of being written over by it.
        /// (The retired `LegendPitchX` was a flat 100 px, which collided on any
        /// name past ~12 characters.)
        LegendEntryGap: float
        /// BAND arms only. Top y of a legend swatch in the TOP band, measured
        /// from the canvas top. The `Bottom` band mirrors it from the canvas
        /// bottom via `LegendLabelBaselineDy`, so it needs no second constant.
        LegendSwatchY: float
        /// Side length of a (square) legend swatch.
        LegendSwatchSize: float
        /// Corner radius of a legend swatch.
        LegendSwatchCornerRadius: float
        /// Gap from a swatch's left edge to its label's left edge.
        LegendLabelOffsetX: float
        /// BAND arms only. Baseline y of a legend label in the TOP band.
        LegendLabelBaselineY: float
        /// COLUMN arms only. Vertical pitch between legend rows.
        LegendRowPitchY: float
        /// COLUMN arms only (and the `Bottom` band). Baseline nudge from a
        /// legend row's TOP to its label's baseline — the relation that lets a
        /// row be placed by its top edge and still read as one line.
        LegendLabelBaselineDy: float
        /// COLUMN arms only. Gap between the plot's edge and the legend
        /// column's swatches. The column's own trailing clearance to the canvas
        /// edge is `MarginRight`, which is what it always was.
        LegendColumnGap: float
        /// Ceiling on the legend column's width, as a share of `Width`. Same
        /// posture as the margin autosizes: a pathological series name is
        /// truncated with the deterministic ellipsis rather than allowed to eat
        /// the plot. The column is otherwise sized from the widest name.
        LegendColumnMaxShare: float

        // ── Data-label geometry (Phase 881 — the `Ends` placements) ──
        //
        // The wire says WHETHER values are written onto the picture; these four
        // say what that looks like and, through the fit gate, whether a given
        // label survives. NONE of them feed a margin: a data label never makes
        // the plot smaller, it either fits the room the picture already has or
        // it is suppressed. That is what keeps `Off` byte-identical to the
        // pre-881 layout rather than merely visually similar.
        /// Font size of a data label. Deliberately a field of its own rather
        /// than `TickFontSize`: a tick sits OUTSIDE the plot in a column of its
        /// own, where a data label sits INSIDE it competing with the mark it
        /// describes, so it is set one step smaller — subordinate to the shape,
        /// which is what the reader is looking at first.
        DataLabelFontSize: float
        /// Clearance between a bar's cap and the nearest ink of its label, in
        /// BOTH directions — above a positive cap, below a negative one. One
        /// constant, so the two placements are exact mirrors.
        DataLabelOffsetY: float
        /// Clearance a label must keep from the plot edge, and half the
        /// clearance it keeps from its neighbour's label. Feeds the fit gate
        /// only; a label that cannot hold it is suppressed rather than moved.
        DataLabelPadding: float
        /// Gap from a line/area endpoint to the left edge of its label.
        DataLabelEndOffsetX: float
        /// Rise from a line/area endpoint to its label's baseline — the nudge
        /// that takes the text off the line it belongs to.
        DataLabelEndNudgeY: float

        // ── Pie geometry (the polar arm) ──
        /// Wedge radius.
        PieRadius: float

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
          SubtitleFontSize = 13.0
          SubtitleBaselineY = 38.0
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
          YAxisTitleOffsetX = 18.0
          YAxisTitleDegrees = 90.0
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
          // The column arm's row geometry (Phase 880) — the retired
          // `PieLegendPitchY` / `PieLegendLabelBaselineDy` values, kept
          // unchanged and promoted to serve every arm: the pie legend was
          // already the vertical right-hand shape this phase generalises, so
          // adopting its numbers is what keeps the pie goldens honest rather
          // than restyled by a layout change.
          LegendRowPitchY = 20.0
          LegendLabelBaselineDy = 9.0
          LegendColumnGap = 16.0
          LegendColumnMaxShare = 0.3
          // Phase 881 — one point below `TickFontSize`, and four small
          // clearances. None of them is corpus-visible until a spec asks for
          // `Ends`, because `Off` emits no label at all.
          DataLabelFontSize = 12.0
          DataLabelOffsetY = 5.0
          DataLabelPadding = 2.0
          DataLabelEndOffsetX = 6.0
          DataLabelEndNudgeY = 5.0
          PieRadius = 130.0
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
//   5. THE INTEGER PART IS RENDERED IN POSITIONAL NOTATION AT EVERY MAGNITUDE,
//      by an expansion this module owns — never by inheriting a host's default
//      double→string switch. Grouping walks decimal digits, so handing it an
//      exponent form corrupts it silently: `groupThousands "1E+17"` is
//      `"1E,+17"`. And the hosts do not agree on WHEN that form appears — the
//      .NET `"R"` layout (which the Fable, Python and Rust hosts mirror, and
//      which `WIRE_FORMAT.md` §5 pins) goes scientific once the leading-digit
//      exponent passes 16, i.e. at 1e17, while JavaScript's
//      `Number.prototype.toString` stays positional until 1e21. So above 1e17
//      four hosts drew a grouped exponent and one drew correctly-grouped
//      digits: the same chart, different bytes. `expandToFixed` therefore
//      re-lays any `d[.ddd]E±NN` mantissa/exponent pair (JavaScript's
//      lower-case `e+NN` included) as its digits zero-padded to `exp + 1`
//      places, and leaves an already-positional form untouched — so every host
//      groups the same digit string and no output below 1e17 moves.
//
//      NOTE the threshold: it is 1e17, not the 1e15 that appears in
//      `DrawingSvg.formatNum`. That constant bounds the exact `int64`
//      fast path, not the notation switch, and the window between them —
//      1e15 ≤ |v| < 1e17 — was always rendered positionally and correctly by
//      all four hosts.
//
//      The expansion is over the SHORTEST-ROUND-TRIP digits, which is the
//      canonical decimal identity of the double (the same one rule 5 of the
//      wire format pins), NOT the double's exact binary value. So 1e21 reads
//      `1,000,000,000,000,000,000,000` rather than its exact expansion
//      `999,999,999,999,999,916,000`. That is deliberate on both counts: the
//      shortest round-trip is the number the wire already says this value is,
//      and it is computable on every host without arbitrary-precision
//      arithmetic, which an exact expansion is not.
//
//      Only the INTEGER part needs this. The fraction part is bounded by
//      `10^d ≤ 10^6` by rule 1's cap, so it can never reach an exponent form.

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

/// Expand a canonical round-trip number form into POSITIONAL notation (rule 5).
/// `s` is whatever the host's shortest-round-trip formatter produced for a
/// non-negative INTEGER-valued double: positional at small magnitudes, and
/// `d[.ddd]E±NN` — or JavaScript's lower-case `e+NN` — above whichever
/// magnitude that host switches at. Total by construction: a form carrying no
/// exponent is returned unchanged, as is the negative-exponent form an integer
/// part cannot produce.
let private expandToFixed (s: string) : string =
    let eIdx =
        let upper = s.IndexOf 'E'
        if upper >= 0 then upper else s.IndexOf 'e'

    if eIdx < 0 then
        s
    else
        let mant = s.Substring(0, eIdx)
        let exp = int (s.Substring(eIdx + 1))

        if exp < 0 then
            s
        else
            let dot = mant.IndexOf '.'

            let digits =
                if dot < 0 then
                    mant
                else
                    mant.Substring(0, dot) + mant.Substring(dot + 1)

            // An integer-valued double's shortest round-trip always has at
            // least as many places as digits; the guard keeps the function
            // total rather than describing a reachable case.
            if digits.Length >= exp + 1 then
                digits
            else
                digits + String.replicate (exp + 1 - digits.Length) "0"

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
        // Rule 5 — expand before grouping. `formatNum` alone would hand the
        // grouper an exponent form above the host's own switch magnitude.
        let intStr = groupThousands (expandToFixed (DrawingSvg.formatNum intPart))

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

// ─── The temporal x-axis (Phase 882) ─────────────────────────────────────────
//
// NORMATIVE CROSS-HOST SPEC (R2), the same standing as the text metrics and the
// number formatter above: every conformant host reproduces this module exactly,
// and `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md` §4h carries it as the
// language-neutral statement. The `chart-lowering/*` goldens pin it.
//
// FIVE RULES, and each one exists to remove a way two hosts could disagree.
//
//   1. THE UNIT IS THE DAY, and a date is an INTEGER: days since 1970-01-01 in
//      the PROLEPTIC GREGORIAN calendar. Nothing here reads a host date type, a
//      locale, a time zone, or a clock — the conversions are the fixed integer
//      algorithms below (Howard Hinnant's `days_from_civil` /
//      `civil_from_days`, public domain), which are exact for every date they
//      admit and need no leap-year table. A `Timestamp` cell's TIME-OF-DAY IS
//      DISCARDED: the value is its UTC date. That is the whole of the axis's
//      time-zone policy, and it is stated rather than inherited, because
//      inheriting it from a host would make the picture depend on where it was
//      drawn.
//
//      Integer division must TRUNCATE TOWARD ZERO (F#, Rust, Go and C all do;
//      JavaScript needs `Math.trunc(a / b)`, Python needs a truncating helper
//      rather than `//`, which floors). The two algorithms bias their operands
//      into the non-negative range precisely so that truncation is the only
//      convention they need.
//
//   2. THE DOMAIN IS THE DATA'S OWN EXTENT, UNEXPANDED — `[min, max]`, so the
//      first and last points sit on the plot's edges. It is NOT snapped outward
//      to a tick boundary (the value axis's `niceDomain` posture), because a
//      calendar boundary is a coarse thing to round to: nicing a 30-day domain
//      to whole months would add a month of empty plot at each end to make room
//      for ticks nobody asked for. The ticks come to the domain instead. A
//      degenerate domain (every row the same date, or no rows) becomes
//      `[lo, lo+1]`, the same guard `niceDomain` applies for the same reason.
//
//   3. THE TICKS ARE CALENDAR-ALIGNED INSTANTS INSIDE THE DOMAIN, at a step
//      drawn from a FIXED LADDER — the `{1,2,5}·10ⁿ` rule's analogue for units
//      that are not decimal:
//
//        1, 2, 5, 10 DAYS · 1, 2, 3, 6 MONTHS · {1,2,5}·10ⁿ YEARS (n ≤ 6)
//
//      The chosen rung is the FIRST whose in-domain tick count fits the
//      ceiling; the coarsest rung is the fallback nothing else fits. Day rungs
//      step from the DOMAIN'S OWN START (a "nice" 2-day or 5-day boundary does
//      not exist — days are uniform, so the honest anchor is the first datum);
//      month rungs land on month starts where `(month-1) mod k = 0`, which
//      makes `k = 3` the calendar quarters and `k = 6` January and July; year
//      rungs land on the January 1 of years where `year mod k = 0`.
//
//      The ceiling is `TargetTickCount + 1` (6 at the shipped default) rather
//      than `TargetTickCount` itself. The value axis's step is CONTINUOUS and
//      can be tuned to hit a target; a calendar rung jumps by 2–3× and cannot,
//      so rounding down a rung loses roughly half the ticks. Admitting the
//      densest rung that still reads keeps the actual count in the 3–6 band.
//      Counts are computed WITHOUT generating the ticks, so the ladder can be
//      walked from its densest rung on a millennium-wide domain without
//      unbounded work.
//
//   4. THE FORMAT FOLLOWS THE STEP'S NOMINAL LENGTH, at the operator's
//      thresholds: `> 365` days ⇒ `yyyy`, `> 27` ⇒ `mmm yy`, else `dd mmm yy`.
//      Nominal, not measured: a month is `365.2425 / 12 = 30.436875` days and a
//      year `365.2425`, so the rung decides the format and the DATA cannot.
//      Measuring the actual tick gaps instead would put the year rung's average
//      at exactly 365.0 across a run of non-leap years (1900–1903, say) and
//      flip a decade chart from `yyyy` to `mmm yy` on a property of the
//      calendar nobody was asking about. The thresholds are calibrated for
//      this: the 1-month rung clears 27 and the 6-month rung does not clear
//      365, so each threshold separates two ADJACENT rungs.
//
//   5. THE MONTH NAMES ARE PART OF THE SPEC. English three-letter
//      abbreviations, invariant, never a locale lookup — an i18n date axis is a
//      different feature with its own vocabulary, and a chart whose golden bytes
//      changed with the host's culture would not be certifiable at all.

/// The calendar the temporal x-axis runs on, and the tick rule over it. Pure
/// integer arithmetic over days since 1970-01-01 (proleptic Gregorian): no host
/// date type, no locale, no time zone, no time-of-day.
[<RequireQualifiedAccess>]
module Temporal =

    /// The English three-letter month abbreviations, in calendar order.
    /// INVARIANT — part of the wire-visible spec (rule 5), never a locale lookup.
    let monthNames =
        [| "Jan"
           "Feb"
           "Mar"
           "Apr"
           "May"
           "Jun"
           "Jul"
           "Aug"
           "Sep"
           "Oct"
           "Nov"
           "Dec" |]

    /// The calendar unit a tick step counts in.
    [<RequireQualifiedAccess>]
    type Unit =
        | Days
        | Months
        | Years

    /// One rung of the ladder: `Count` of `Unit`.
    type Step = { Unit: Unit; Count: int }

    /// Gregorian leap year (proleptic — the rule applies to every year the
    /// parser admits, with no historical exception).
    let isLeapYear (y: int) : bool =
        (y % 4 = 0 && y % 100 <> 0) || y % 400 = 0

    /// Days in a month — the one place the calendar's irregularity is written
    /// down, used by the PARSER only (the conversions below need no table).
    let daysInMonth (y: int) (m: int) : int =
        if m = 2 then (if isLeapYear y then 29 else 28)
        elif m = 4 || m = 6 || m = 9 || m = 11 then 30
        else 31

    /// `(y, m, d)` → days since 1970-01-01. Hinnant's `days_from_civil`: exact
    /// for every proleptic-Gregorian date, no leap table, integer-only.
    /// Division truncates toward zero — the operands are biased so that is the
    /// only convention needed (rule 1).
    let daysFromCivil (year: int) (month: int) (day: int) : int =
        let y = if month <= 2 then year - 1 else year
        let era = (if y >= 0 then y else y - 399) / 400
        let yoe = y - era * 400 // [0, 399]
        let mp = if month > 2 then month - 3 else month + 9 // March-based month
        let doy = (153 * mp + 2) / 5 + day - 1 // [0, 365]
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy // [0, 146096]
        era * 146097 + doe - 719468

    /// Days since 1970-01-01 → `(y, m, d)`. Hinnant's `civil_from_days`, the
    /// exact inverse of `daysFromCivil`.
    let civilFromDays (days: int) : int * int * int =
        let z = days + 719468
        let era = (if z >= 0 then z else z - 146096) / 146097
        let doe = z - era * 146097 // [0, 146096]
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365 // [0, 399]
        let y = yoe + era * 400
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100) // [0, 365]
        let mp = (5 * doy + 2) / 153 // [0, 11], March-based
        let d = doy - (153 * mp + 2) / 5 + 1 // [1, 31]
        let m = if mp < 10 then mp + 3 else mp - 9 // [1, 12]
        ((if m <= 2 then y + 1 else y), m, d)

    /// Parse a canonical ISO-8601 date to days since epoch — `YYYY-MM-DD`,
    /// optionally followed by `T…`, whose time-of-day is DISCARDED (rule 1).
    /// STRICT by shape and by calendar: four digits, two, two, both hyphens, a
    /// month in 1–12 and a day the month actually has. `None` for everything
    /// else, including a locale spelling ("15/01/2026") and a bare year —
    /// admitting either would be the string-sniffing this axis exists to avoid.
    let tryParseDay (text: string) : int option =
        let digits (start: int) (len: int) : int option =
            let mutable ok = start + len <= text.Length
            let mutable acc = 0

            if ok then
                for k in start .. start + len - 1 do
                    let c = text.[k]

                    if c >= '0' && c <= '9' then
                        acc <- acc * 10 + (int c - int '0')
                    else
                        ok <- false

            if ok then Some acc else None

        if text.Length < 10 then
            None
        elif text.[4] <> '-' || text.[7] <> '-' then
            None
        elif text.Length > 10 && text.[10] <> 'T' then
            None
        else
            match digits 0 4, digits 5 2, digits 8 2 with
            | Some y, Some m, Some d when m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth y m ->
                Some(daysFromCivil y m d)
            | _ -> None

    /// The day number a row's x cell carries, with an UNPARSEABLE cell reading
    /// as the epoch. That mirrors `numericOf`'s posture for a non-numeric value
    /// axis cell — the lowering stays total and the grounding rule (FUARAN097)
    /// is what makes a non-date column loud, upstream, before any picture is
    /// drawn. Silence here is not the design; refusing here would be.
    let dayOf (text: string) : int =
        match tryParseDay text with
        | Some d -> d
        | None -> 0

    /// The step's NOMINAL length in days (rule 4) — a mean Gregorian month and
    /// year, so the FORMAT is a property of the rung rather than of the data.
    let nominalDays (step: Step) : float =
        match step.Unit with
        | Unit.Days -> float step.Count
        | Unit.Months -> float step.Count * 30.436875 // 365.2425 / 12
        | Unit.Years -> float step.Count * 365.2425

    /// The ladder, ascending (rule 3). Written out rather than generated: it is
    /// a pinned vocabulary five hosts mirror, and an explicit list cannot drift
    /// on a difference of opinion about `pown`.
    let ladder: Step list =
        [ { Unit = Unit.Days; Count = 1 }
          { Unit = Unit.Days; Count = 2 }
          { Unit = Unit.Days; Count = 5 }
          { Unit = Unit.Days; Count = 10 }
          { Unit = Unit.Months; Count = 1 }
          { Unit = Unit.Months; Count = 2 }
          { Unit = Unit.Months; Count = 3 }
          { Unit = Unit.Months; Count = 6 }
          { Unit = Unit.Years; Count = 1 }
          { Unit = Unit.Years; Count = 2 }
          { Unit = Unit.Years; Count = 5 }
          { Unit = Unit.Years; Count = 10 }
          { Unit = Unit.Years; Count = 20 }
          { Unit = Unit.Years; Count = 50 }
          { Unit = Unit.Years; Count = 100 }
          { Unit = Unit.Years; Count = 200 }
          { Unit = Unit.Years; Count = 500 }
          { Unit = Unit.Years; Count = 1000 }
          { Unit = Unit.Years; Count = 2000 }
          { Unit = Unit.Years; Count = 5000 }
          { Unit = Unit.Years; Count = 10000 }
          { Unit = Unit.Years; Count = 20000 }
          { Unit = Unit.Years; Count = 50000 }
          { Unit = Unit.Years; Count = 100000 }
          { Unit = Unit.Years; Count = 200000 }
          { Unit = Unit.Years; Count = 500000 }
          { Unit = Unit.Years; Count = 1000000 }
          { Unit = Unit.Years; Count = 2000000 }
          { Unit = Unit.Years; Count = 5000000 } ]

    /// Round an index UP to the next multiple of `k` (both non-negative).
    let private ceilTo (k: int) (i: int) : int = (i + k - 1) / k * k

    /// The aligned window a month rung covers: `(first aligned month index,
    /// count)` over `[lo, hi]`, in month-index space (`year·12 + month - 1`).
    /// Closed-form, so a count never generates a tick.
    let private monthWindow (k: int) (lo: int) (hi: int) : int * int =
        let y0, m0, d0 = civilFromDays lo
        // A `lo` past the 1st means `lo`'s own month start is outside the domain.
        let firstIdx = (y0 * 12 + m0 - 1) + (if d0 > 1 then 1 else 0)
        let first = ceilTo k firstIdx
        let y1, m1, _ = civilFromDays hi
        // `hi`'s own month start is always inside the domain (its day ≥ 1).
        let last = (y1 * 12 + m1 - 1) / k * k

        if last < first then
            first, 0
        else
            first, (last - first) / k + 1

    /// The year rung's twin of `monthWindow`, in year space.
    let private yearWindow (k: int) (lo: int) (hi: int) : int * int =
        let y0, m0, d0 = civilFromDays lo
        let firstYear = y0 + (if m0 = 1 && d0 = 1 then 0 else 1)
        let first = ceilTo k firstYear
        let y1, _, _ = civilFromDays hi
        let last = y1 / k * k

        if last < first then
            first, 0
        else
            first, (last - first) / k + 1

    /// How many `step`-aligned ticks fall in `[lo, hi]` — closed-form, never by
    /// generation (rule 3), so walking the ladder is O(rungs) whatever the span.
    let tickCount (step: Step) (lo: int) (hi: int) : int =
        if hi < lo then
            0
        else
            match step.Unit with
            | Unit.Days -> (hi - lo) / step.Count + 1
            | Unit.Months -> snd (monthWindow step.Count lo hi)
            | Unit.Years -> snd (yearWindow step.Count lo hi)

    /// The `step`-aligned ticks in `[lo, hi]`, ascending.
    let ticks (step: Step) (lo: int) (hi: int) : int list =
        if hi < lo then
            []
        else
            match step.Unit with
            | Unit.Days -> [ for i in 0 .. (hi - lo) / step.Count -> lo + i * step.Count ]
            | Unit.Months ->
                let first, count = monthWindow step.Count lo hi

                [ for i in 0 .. count - 1 do
                      let idx = first + i * step.Count
                      daysFromCivil (idx / 12) (idx % 12 + 1) 1 ]
            | Unit.Years ->
                let first, count = yearWindow step.Count lo hi

                [ for i in 0 .. count - 1 -> daysFromCivil (first + i * step.Count) 1 1 ]

    /// The chosen rung: the FIRST whose in-domain tick count fits `maxTicks`,
    /// else the coarsest (rule 3). Total — the ladder is never empty.
    let chooseStep (maxTicks: int) (lo: int) (hi: int) : Step =
        match ladder |> List.tryFind (fun s -> tickCount s lo hi <= maxTicks) with
        | Some s -> s
        | None -> List.last ladder

    /// The domain: the data's own extent, unexpanded, with the degenerate guard
    /// (rule 2). No rows ⇒ `[0, 1]` — the epoch day and the one after it, which
    /// draws an axis rather than dividing by zero.
    let domain (days: int[]) : int * int =
        if days.Length = 0 then
            0, 1
        else
            let lo = Array.min days
            let hi = Array.max days
            if hi = lo then lo, lo + 1 else lo, hi

    let private pad (width: int) (v: int) : string =
        let s = string v

        if s.Length >= width then
            s
        else
            String.replicate (width - s.Length) "0" + s

    /// The tick label for `day` under `step` — the granularity-adaptive format
    /// (rule 4). `yyyy` past a year, `mmm yy` past 27 days, else `dd mmm yy`.
    let label (step: Step) (day: int) : string =
        let y, m, d = civilFromDays day
        let nominal = nominalDays step
        let yy = pad 2 (y % 100)
        let mmm = monthNames.[m - 1]

        if nominal > 365.0 then pad 4 y
        elif nominal > 27.0 then mmm + " " + yy
        else pad 2 d + " " + mmm + " " + yy

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
      Rotation = None
      Tip = None }

/// Phase 883 — the separator between the three parts of a hover readout. A
/// middle dot with hair spaces of its own: it is not a character any series or
/// category name is likely to contain (a hyphen, a slash and a comma all are),
/// and it reads as a separator rather than as punctuation belonging to either
/// side. Screen readers announce it as a pause, not as a word.
[<Literal>]
let private tipSeparator = " · "

/// Phase 883 — stamp the hover readout onto a data-bearing shape's style. An
/// EMPTY readout is dropped rather than encoded: an empty SVG `<title>`
/// suppresses the native tooltip AND overrides the element's accessible name
/// with nothing, which is worse than no title at all.
let private withTip (text: string) (style: DrawStyle) : DrawStyle =
    if text = "" then
        style
    else
        { style with
            Tip = Some(TextSource.Literal text) }

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

// ─── The accessible summary (Phase 921) ──────────────────────────────────────
//
// NORMATIVE CROSS-HOST SPEC, the same standing as the text metrics, the number
// formatter and the temporal calendar above: every conformant host reproduces
// this grammar exactly, the `chart-lowering/*` goldens pin it, and
// `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md` §4i carries the language-neutral
// statement.
//
// WHY IT EXISTS. Phase 532's R3 gave the drawing root `role="img"` — the right
// answer for a chart read as one graphic, because it stops a reader announcing
// several hundred meaningless `<rect>`s. Phase 883's per-mark `<title>` is that
// element's accessible name, but an assistive technology presents a `role="img"`
// element as a SINGLE graphic and does not traverse into its children, so those
// names are never announced. Operator decision 2026-08-18 (Option 2): the root
// keeps `role="img"`, and the lowering generates a deterministic SUMMARY as the
// drawing's `Description` — announced today, on every host, with no structural
// change. The per-mark `<title>`s remain what they actually are: the sighted
// pointer's affordance.
//
// IT IS A LOWERING RULE LIKE EVERY OTHER ONE HERE. Generated from the resolved
// `ChartSpec` + rows, canonical strings only, the pinned formatter, no host
// locale, no clock — so the same wire tree yields byte-identical summaries
// everywhere and the corpus can certify it. That is also why the TITLE is not
// part of it: `ChartSpec.Title` is a `TextSource`, which a bound or i18n arm
// resolves only at RENDER time, and a summary that carried the title only for
// the `Literal` arm would announce a different thing depending on how the title
// was authored. The title is composed in front of the summary by the renderer's
// root wiring instead (`DrawingSvg`, Phase 921), where every `TextSource` arm
// resolves — so the announced string is "<title>. <summary>" for every arm, and
// the two artefacts stay what SVG says they are: `<title>` names, `<desc>`
// describes.

/// The clause separator + terminator. Periods, not commas: a screen reader
/// pauses at a sentence boundary, and four comma-spliced clauses read as one
/// long run-on.
[<Literal>]
let private summaryClauseSeparator = ". "

/// At most this many series are NAMED before the summary folds the rest into a
/// count. Four is the legibility bound, not a technical one: a name list is
/// announced serially, and past four the reader has lost the first one before
/// the last arrives — the count is then the more useful statement.
[<Literal>]
let private summaryMaxSeriesNamed = 4

/// The per-NAME character cap (a series field, a category label). Untrusted
/// strings straight off the data feed; a single 4 000-character category would
/// otherwise be the whole summary.
[<Literal>]
let private summaryMaxNameChars = 32

/// The whole summary's character cap — the outer bound the per-name caps and
/// the series folding already keep it well inside. It exists so the bound is a
/// PROPERTY of the grammar rather than a consequence of its parts.
[<Literal>]
let private summaryMaxChars = 320

/// Truncate to at most `maxChars`, marking the cut with the ellipsis.
///
/// The cut NEVER splits a UTF-16 surrogate pair: a boundary landing between a
/// high and a low surrogate moves one unit earlier. Without that rule an
/// emoji-bearing category name would produce a lone surrogate — which is not a
/// valid string on any host, and which the three hosts counting differently
/// (UTF-16 units in F#/TS, scalar values in Python/Rust) would cut in three
/// different places.
let private clampText (maxChars: int) (s: string) : string =
    if s.Length <= maxChars then
        s
    else
        let cut = maxChars - 1

        let cut =
            if cut > 0 && System.Char.IsHighSurrogate s.[cut - 1] then
                cut - 1
            else
                cut

        s.Substring(0, cut) + TextMetrics.Ellipsis

/// The chart's kind in words — the summary's opening clause. `Stacked` earns a
/// word only on the two arms where it changes the geometry (the same rule the
/// lowering itself applies), so a `Stacked = true` Line does not announce a
/// stacking that was ignored.
let private summaryKindWords (kind: ChartKind) (stacked: bool) : string =
    match kind with
    | ChartKind.Bar -> if stacked then "Stacked bar chart" else "Bar chart"
    | ChartKind.Line -> "Line chart"
    | ChartKind.Area -> if stacked then "Stacked area chart" else "Area chart"
    | ChartKind.Scatter -> "Scatter chart"
    | ChartKind.Pie -> "Pie chart"
    | ChartKind.Heatmap -> "Heatmap chart"

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


// ─── Phase 933 — mark identity, read backwards ────────────────────────────────
//
// The renderer's first-party chart path emits the lowered `DrawingSpec` as a
// RAW SVG STRING (`dangerouslySetInnerHTML`), so there is no per-point React
// element to hang a click handler on. A point click is therefore a single
// DELEGATED handler on the container that maps the clicked element back to a
// datum — and the only per-datum identity in the emitted markup is Phase 642's
// `data-fuaran-mark`, which `withMark` writes and `DrawingSvg` renders.
//
// The inverse of that write lives HERE, beside the write, because the mark-id
// FORMAT is this module's own. A copy of the parse in the renderer would be a
// second definition of the format, free to drift from the one that produces it.
//
// NOTE: nothing about the emitted SVG changes. This reads an attribute that has
// shipped since Phase 642; no golden moves, and no wire field is involved.

/// Phase 933 — the x-key each row contributes to a data-bearing mark id, in the
/// SAME form the lowering stamps, index-aligned with `rows`.
///
/// This MIRRORS four bindings inside `lowerRows` (`categories`, `isTemporal`,
/// `dayValues`, `xValues`) which are local to that function and cannot be
/// called from outside it. The mirror is not held by this comment: the test
/// `every per-datum mark id the lowering emits is reproduced by markCategoryKeys`
/// lowers a real chart per arm and pins the two against each other, so a change
/// to either derivation goes red rather than silently un-selecting the chart.
let markCategoryKeys<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) : string[] =
    let rowList = List.ofSeq rows

    // The band arms' key: the x cell AS PROJECTED, one per ROW and NOT deduped,
    // so equality against it recovers exactly one row (the first, where a
    // category repeats — a repeat is the author's ambiguity, not ours).
    let categories =
        rowList
        |> List.map (fun r -> BindingResolver.projectRowFieldString r spec.XField)
        |> List.toArray

    let isTemporal =
        (match spec.XScale with
         | Some ChartXScale.Temporal -> true
         | _ -> false)
        && (match spec.Kind with
            | ChartKind.Pie -> false
            | _ -> true)

    match spec.Kind with
    // Scatter alone keys its marks by the CANONICAL NUMERIC x rather than the
    // projection (`withMark yf (DrawingSvg.formatNum xValues.[i])`) — and under
    // a temporal x-scale that numeric is the DAY NUMBER, not the cell. Matching
    // a scatter mark as though it were a projection finds nothing at all, which
    // presents as "charts do not select" rather than as a defect, so the arm is
    // reproduced here in its own form.
    | ChartKind.Scatter ->
        let xValues =
            if isTemporal then
                categories |> Array.map (fun c -> float (Temporal.dayOf c))
            else
                rowList |> List.map (fun r -> numericOf r spec.XField) |> List.toArray

        xValues |> Array.map DrawingSvg.formatNum
    | _ -> categories

/// Phase 933 — the INVERSE of `withMark`: given the `data-fuaran-mark` id read
/// off a clicked SVG element, recover the row the mark was derived from.
///
/// Three shapes reach here, matching the three the lowering emits:
///
///   * `"<seriesField>|<categoryKey>"` — a PER-DATUM mark (the Bar arms, Pie,
///     and Scatter). Resolvable, and the case this exists for.
///   * `"<seriesField>"` — a SERIES-level mark (`withSeriesMark`: Line and Area
///     draw one polyline that IS the whole series). It carries no per-datum
///     identity, so there is no datum to publish and this returns `None`. That
///     is a real limit of the shipped mark vocabulary rather than an omission
///     here — recovering a point from a polyline click needs geometric
///     hit-testing, which no attribute in the markup can supply.
///   * anything else (chrome: axes, gridlines, labels, legend) never carries a
///     mark at all, so it never reaches this function.
///
/// The split is at the FIRST `|`: the series part is a FIELD NAME, while the
/// category part is whatever the x cell projected to and may itself contain a
/// separator.
let rowOfMarkId<'Msg> (spec: ChartSpec<'Msg>) (rows: Row seq) (markId: string) : Row option =
    let cut = markId.IndexOf "|"

    if cut < 0 then
        None
    else
        let categoryKey = markId.Substring(cut + 1)
        let rowList = List.ofSeq rows

        markCategoryKeys spec rowList
        |> Array.tryFindIndex (fun k -> k = categoryKey)
        |> Option.bind (fun i -> List.tryItem i rowList)
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

    // ── Hover readout (Phase 883) ────────────────────────────────────────────
    //
    // THE TIP IS WHERE FULL PRECISION LIVES. Phase 881 deferred label precision
    // to here for a reason it wrote down: "a label more precise than its own
    // axis is a chart disagreeing with itself". A printed data label therefore
    // goes through `yTickText` — the axis's own formatter, the axis's step
    // precision, the axis's display unit — and reads *roughly where*. The tip
    // answers the different question, *what exactly is this*, so it takes the
    // opposite three decisions, deliberately:
    //
    //   * UNSCALED by the display unit. An axis in millions scales once and
    //     says so in its unit slot; a tooltip has no unit slot beside it, and
    //     "1.2" with the "millions" three inches away is a number a reader can
    //     misread. The tip prints the raw magnitude.
    //   * THE DATUM'S OWN PRECISION, not the tick step's — the smallest number
    //     of decimals that reproduces the value exactly (capped at 6, and
    //     tolerant of float noise, so 0.1 + 0.2 reads "0.3" and not
    //     "0.30000000000000004"). An author's EXPLICIT `Format.Number d` /
    //     `Format.Percent d` still wins: a declared precision is a statement
    //     about the data, not about the axis, so it holds in both places.
    //   * THE CURRENCY SYMBOL IS KEPT. The ticks drop it because the axis-unit
    //     label states it once; a tip stands alone and must say what it is.
    //
    // Everything else is the shared formatter (`formatValue`), so thousands
    // grouping, the round-half-up rule, the `%` suffix and the sign placement
    // are one implementation across ticks, labels and tips.
    let tipValueText (v: float) : string = formatValue valueFormat 1.0 false v v

    /// The readout for a PER-DATUM mark (a bar, a stack segment, a wedge, a
    /// scatter point): "Series · Category · value". Both leading parts are
    /// untrusted strings straight off the data feed — the renderer's XML escape
    /// is what makes that safe, and it is audited (SANITIZATION.md).
    ///
    /// The series name is the FIELD name, matching the legend and `MarkId`
    /// rather than the capitalised axis title: the legend is what a reader
    /// cross-references a colour against, so the tip agrees with the legend.
    let datumTip (seriesField: string) (categoryKey: string) (v: float) (style: DrawStyle) : DrawStyle =
        withTip (seriesField + tipSeparator + categoryKey + tipSeparator + tipValueText v) style

    /// The readout for a SERIES-LEVEL mark (a line, an area band or its edge).
    /// THE TIP'S GRANULARITY FOLLOWS THE MARK'S IDENTITY GRANULARITY — Phase
    /// 642 gives one `MarkId` to the whole polyline because one element IS the
    /// whole series, and a single `<title>` on it cannot honestly name one
    /// point's value: SVG resolves the tooltip per ELEMENT, so whichever value
    /// was chosen would be reported for a hover anywhere along the line. The
    /// series name is what that element actually is, so that is what it says.
    /// Per-point readouts on a line need per-point elements, which is geometry
    /// this phase does not add.
    let seriesTip (seriesField: string) (style: DrawStyle) : DrawStyle = withTip seriesField style

    // ── Linear x-scale (Phase 636 — the Scatter arm's numeric x axis) ──
    // Scatter reads the x-field NUMERICALLY and plots on a linear x-domain (the
    // first non-band x-scale arm). The domain is NOT zero-anchored — a scatter's
    // x range carries no baseline semantics (the y domain stays zero-anchored
    // with the other arms, deliberately: one shared y-domain rule).
    let isScatter =
        match spec.Kind with
        | ChartKind.Scatter -> true
        | _ -> false

    // ── Temporal x-scale (Phase 882 — the SECOND non-band x-scale) ──
    //
    // DECLARED, never inferred. `ChartSpec.XScale = Temporal` is the author
    // saying "this column is dates"; the language then GROUNDS that claim
    // against the statically-known column type (FUARAN097) wherever it can.
    // Inference was the alternative and is wrong twice over: the schema is
    // statically known only for an embedded table with an EMPTY pipeline
    // (FUARAN086's window), so an inferred axis would make the same tree draw a
    // band axis or a temporal one depending on where its rows came from — a
    // picture that depends on data PROVENANCE — and sniffing the cell strings
    // for an ISO-8601 shape is the guess-dressed-as-a-rule §4e refused. Absent
    // is `Category`, which is every pre-882 chart, byte-for-byte.
    //
    // Pie is excluded because it HAS no x axis: a temporal declaration there is
    // dead intent the polar arm cannot honour, and neutralising it here keeps
    // the pie geometry free of a scale it never reads.
    let isTemporal =
        (match spec.XScale with
         | Some ChartXScale.Temporal -> true
         | _ -> false)
        && (match spec.Kind with
            | ChartKind.Pie -> false
            | _ -> true)

    /// Each row's x as a DAY NUMBER, read off the same string projection the
    /// band arms label with — which is exactly the canonical ISO-8601 form a
    /// `Cell.Date` / `Cell.Timestamp` carries through the row bridge. So the
    /// mark identity below keeps the ISO string while the geometry uses the
    /// integer, and neither has to be derived from the other.
    let dayValues: int[] =
        if isTemporal then
            categories |> Array.map Temporal.dayOf
        else
            [||]

    /// The x axis is CONTINUOUS (Phase 903's split) on exactly two arms: the
    /// Scatter arm's numeric x and a temporal x. Everything keyed off this —
    /// tick marks AT the value, vertical gridlines, marks placed by value rather
    /// than by band index — follows from that one property rather than from a
    /// list of kinds.
    let isContinuousX = isScatter || isTemporal

    let xValues =
        if isTemporal then
            dayValues |> Array.map float
        elif isScatter then
            rows |> List.map (fun r -> numericOf r spec.XField) |> List.toArray
        else
            [||]

    /// The chosen calendar rung, on a temporal axis only. ONE value decides both
    /// the tick positions and the label format, so the two cannot disagree about
    /// the axis's granularity.
    let temporalStep: Temporal.Step option =
        if isTemporal then
            let lo, hi = Temporal.domain dayValues
            Some(Temporal.chooseStep (int style.TargetTickCount + 1) lo hi)
        else
            None

    let xNiceLo, xNiceHi, xStep, xTicks =
        match temporalStep with
        | Some step ->
            // The domain is the data's own extent (rule 2) — deliberately NOT
            // nice-d outward — and the ticks are the calendar-aligned instants
            // inside it. `xStep` carries the rung's NOMINAL length, which is
            // what the label format reads.
            let lo, hi = Temporal.domain dayValues

            float lo, float hi, Temporal.nominalDays step, (Temporal.ticks step lo hi |> List.map float)
        | None ->
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
    //
    // A TEMPORAL tick takes the calendar label instead (Phase 882) — the same
    // one-formatter-per-axis discipline over a different vocabulary: the number
    // formatter has nothing true to say about a date.
    let xTickText (v: float) : string =
        match temporalStep with
        | Some step -> Temporal.label step (int v)
        | None -> formatValue None 1.0 false xStep v

    // ── Text-metric layout (Phase 879) ───────────────────────────────────────
    //
    // Everything below reads `TextMetrics` and nothing reads a font. The four
    // decisions, in dependency order: the LEFT margin (from the widest formatted
    // y tick), the band pitch that follows from it, the category-label TILT
    // (from the widest category name against that pitch), and the BOTTOM margin
    // the chosen tilt needs to fall into.

    let tickSize = style.TickFontSize
    let titleSize = style.TitleFontSize
    let subtitleSize = style.SubtitleFontSize
    let lineHeight = TextMetrics.lineHeight tickSize style.TextLineHeightFactor

    let widestOf (texts: string seq) : float =
        texts |> Seq.fold (fun acc t -> max acc (TextMetrics.width tickSize t)) 0.0

    // ── Axis names + subtitle (Phase 878) ────────────────────────────────────
    //
    // Resolved HERE, before any margin, because both margins have to reserve a
    // line for text whose presence is decided by these three fields — the left
    // margin for the rotated y-axis title, the top margin for the subtitle. The
    // same dependency Phase 879 established when the bottom margin started
    // reserving the x-axis title's line.
    let capitalise (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpper s.[0]) + s.Substring 1

    /// An axis title: the author's own `TextSource` when declared, else the
    /// capitalised field name — which is exactly what the x axis has always
    /// drawn, now stated once and applied to both axes. `None` only where there
    /// is no honest fallback: an empty field name, or a y axis carrying no
    /// series at all.
    let axisTitleOf (declared: TextSource option) (fallbackField: string) : TextSource option =
        match declared with
        | Some t -> Some t
        | None when fallbackField = "" -> None
        | None -> Some(TextSource.Literal(capitalise fallbackField))

    /// Phase 882 wires §4e's date-axis rule: a SELF-EVIDENT DATE AXIS SUPPRESSES
    /// ITS DEFAULT TITLE — an axis reading "Jan Feb Mar" does not need the word
    /// "Date" beneath it. Two boundaries, both stated when the rule was written
    /// down and both kept: it applies to the FALLBACK only (an explicit `XTitle`
    /// is the author overriding the default and always draws), and it suppresses
    /// the TITLE, never the axis. The declaration is what made it wirable —
    /// nothing before 882 could tell a date column from a string one, which is
    /// why 878 recorded the rule instead of shipping it.
    let xTitle =
        if isTemporal && Option.isNone spec.XTitle then
            None
        else
            axisTitleOf spec.XTitle spec.XField

    // The y fallback is the capitalised FIRST y-field. It is the honest answer
    // to "what is on this axis", where the retired `"Value"` literal named
    // neither the measure nor its unit — and it makes ONE rule cover both axes
    // rather than a rule for x and a constant for y. The multi-series chart is
    // the case it serves least well; there the legend already names every
    // series, and an author plotting genuinely different measures should
    // declare `YTitle`, which is precisely why the field exists.
    let yTitle =
        axisTitleOf
            spec.YTitle
            (match spec.YFields with
             | f :: _ -> f
             | [] -> "")

    // ── Top margin ──
    // A subtitle takes one line under the visible title, and EVERYTHING below
    // it in the top band moves down by exactly that line: the legend row, the
    // display-unit slot, and the plot itself (so on the Pie arm the wedge
    // centre moves too). Reserved only when a subtitle is present, so a chart
    // without one keeps the pre-878 layout byte-for-byte.
    let subtitleBand =
        match spec.Subtitle with
        | Some _ -> TextMetrics.lineHeight subtitleSize style.TextLineHeightFactor
        | None -> 0.0

    let marginTop = r2 (style.MarginTop + subtitleBand)

    // ── Left margin ──
    // The tick column must clear `TickLabelGap` from the spine and
    // `AxisLabelPadding` from the canvas edge. The ceiling is a share of the
    // canvas; a tick column that would breach it is TRUNCATED (never allowed to
    // eat the plot), which is why the truncation budget is derived from the
    // ceiling — a constant — and not from the margin it is about to decide.
    let leftCeiling = style.MarginLeftMaxShare * style.Width

    // Phase 878 — the rotated y-axis title occupies one LINE of the left
    // margin, outboard of the tick column. Only its line height (plus the
    // padding beside it) is reserved here: the title is rotated, so its LENGTH
    // runs vertically and is bounded against the plot height further down. That
    // is what keeps this acyclic — exactly the shape Phase 879 gave the x-axis
    // title's line in the bottom margin.
    let yTitleBand =
        match yTitle with
        | Some _ -> lineHeight + style.AxisLabelPadding
        | None -> 0.0

    let tickTextBudget =
        max 0.0 (leftCeiling - style.TickLabelGap - style.AxisLabelPadding - yTitleBand)

    let yTickLabelText (v: float) : string =
        TextMetrics.truncateToWidth tickSize tickTextBudget (yTickText v)

    let requiredLeft =
        style.TickLabelGap
        + widestOf (ticks |> List.map yTickLabelText)
        + style.AxisLabelPadding
        + yTitleBand

    let marginLeft = r2 (max style.MarginLeft (min leftCeiling requiredLeft))

    let plotX0 = marginLeft

    // ── Legend placement (Phase 880; BAND overflow fallback 2026-08-18) ──────
    //
    // ONE legend with four placements, resolved HERE — AFTER the left margin,
    // whose `plotX0` is where a band packs FROM, and before the plot's right
    // edge, because a `Right` legend's column width is an INPUT to the plot
    // rectangle and a `Bottom` legend's band is an input to the bottom margin.
    // Same acyclicity discipline the text metrics established: everything the
    // layout reads is computed before the layout that reads it. Phase 880
    // resolved this block above ALL the margins; the overflow rule moved it
    // below the LEFT one, because that is where the band's available width
    // comes from. Nothing between the two reads the legend, so the block moved
    // whole and not one term of it changed.
    //
    // The pie arm's shares are resolved here for the same reason: its legend
    // labels carry them ("name (NN%)"), so they are layout input, not output.
    let isPie =
        match spec.Kind with
        | ChartKind.Pie -> true
        | _ -> false

    let pieValues = if isPie && m = 1 then series.[0] else [||]

    let pieTotal = Array.sum pieValues

    // The Phase-638 bounded-v1 guard, unchanged and merely lifted: exactly one
    // series, no negative value, a positive total. A refused pie draws no
    // geometry AND no legend — a legend for a picture that was refused would be
    // a claim about data the drawing declined to show.
    let pieRefused =
        isPie
        && (m <> 1 || pieValues |> Array.exists (fun v -> v < 0.0) || pieTotal <= 0.0)

    let pieFractions =
        if isPie && not pieRefused then
            pieValues |> Array.map (fun v -> v / pieTotal)
        else
            [||]

    /// The legend's rows in draw order — `(colour, label)`. TWO sources, ONE
    /// shape, which is what Phase 880 unified: the cartesian arms legend their
    /// SERIES and only when there is more than one (with a single series the
    /// title already names it — the pre-880 rule, preserved exactly), while the
    /// pie arm legends its CATEGORIES, which is why a single-series pie legends
    /// and a single-series bar does not. Before this phase these were two
    /// separate emitters with two separate constant sets, and only one of them
    /// could honour a position.
    let legendEntries: (string * string)[] =
        if isPie then
            if pieRefused then
                [||]
            else
                pieFractions
                |> Array.mapi (fun i f ->
                    // Routed through the canonical formatter (Phase 876) — one
                    // rounding + rendering rule for every number this module
                    // prints. A share is a whole percent, so the shipped `NN%`
                    // shape is unchanged.
                    let pct = formatValue None 1.0 false 1.0 (f * 100.0)
                    colourFor style i, sprintf "%s (%s%%)" categories.[i] pct)
        elif m > 1 then
            Array.init m (fun j -> colourFor style j, yFields.[j])
        else
            [||]

    /// The placement the author ASKED FOR: their explicit `ChartSpec` value
    /// where there is one, else the host style's default. With no entries at
    /// all the answer is `None` whatever either of them said — so an explicit
    /// position on a single-series chart still draws nothing, and, more to the
    /// point, reserves no space.
    let requestedPos =
        if Array.isEmpty legendEntries then
            ChartLegendPosition.None
        else
            spec.LegendPosition |> Option.defaultValue style.LegendPosition

    /// A BAND entry's PITCH: the swatch's label offset, the label's own natural
    /// width, and the gap before the next entry. Read by the overflow predicate
    /// AND by the band emitter far below — one expression, deliberately, so the
    /// rule can never decide against geometry the drawing does not use. The name
    /// is the untruncated one, because a band never truncates.
    let bandEntryWidth (t: string) : float =
        style.LegendLabelOffsetX + TextMetrics.width tickSize t + style.LegendEntryGap

    /// The width a BAND has to pack into: from the plot's left edge, where the
    /// band starts, to the plot's right edge — which on a band arm is the canvas
    /// less the right margin, since a band reserves no column and
    /// `legendColumnW` is 0 there by construction. So the term is not circular,
    /// and it is the PLOT's width rather than the canvas-minus-declared-margins
    /// width: the band packs from `plotX0`, the autosized left margin, not from
    /// `style.MarginLeft`.
    let bandAvailableW = style.Width - style.MarginRight - plotX0

    /// **The BAND overflow rule (operator decision, 2026-08-18).** An explicit
    /// `Top` or `Bottom` legend whose entries do not pack into one band row
    /// FALLS BACK TO THE RIGHT-HAND COLUMN. A band's width is the SUM of its
    /// entries, so it runs off the canvas once the names are long enough or
    /// numerous enough — and truncating any one name cannot fix a sum, which is
    /// why Phase 879's per-entry natural pitch and Phase 880's repositioning
    /// both left it standing.
    ///
    /// The column never loses information, never grows the band unboundedly, and
    /// reuses layout that already shipped. The two alternatives were considered
    /// and DECLINED: a second row grows the reserved band and moves the plot
    /// rectangle with the entry COUNT (chrome sliding under a data refresh, the
    /// thing this module's mark-identity rule exists to avoid); a refusal past a
    /// computed entry count loses the legend entirely, when the author's intent —
    /// a visible legend — is honourable at a different edge. So `Top`/`Bottom`
    /// mean "band if it fits, column if it cannot"; the wire is unchanged.
    ///
    /// The comparison is against the packed sum INCLUDING the last entry's
    /// trailing `LegendEntryGap`, exactly as the emitter computes it — that gap
    /// is the clearance to the right margin, so a legend whose ink would fit but
    /// whose clearance would not is treated as overflowing. Strict `>`, so an
    /// exact fit stays a band. And the fallback is UNIFORM: the whole legend
    /// moves, never a split across two edges.
    let bandOverflows =
        match requestedPos with
        | ChartLegendPosition.Top
        | ChartLegendPosition.Bottom -> (legendEntries |> Array.sumBy (fun (_, t) -> bandEntryWidth t)) > bandAvailableW
        | _ -> false

    /// The placement actually used.
    let legendPos =
        if bandOverflows then
            ChartLegendPosition.Right
        else
            requestedPos

    /// COLUMN arms: the widest label decides the column, bounded by
    /// `LegendColumnMaxShare` of the canvas and truncated beyond it — the
    /// margin autosizes' posture, adopted for the same reason. A name with no
    /// bound is a data problem the layout should report by truncating, not
    /// absorb by shrinking the picture.
    let legendNameBudget =
        max
            0.0
            (style.LegendColumnMaxShare * style.Width
             - style.LegendLabelOffsetX
             - style.LegendColumnGap)

    let legendTexts =
        legendEntries
        |> Array.map (fun (_, t) ->
            match legendPos with
            | ChartLegendPosition.Right -> TextMetrics.truncateToWidth tickSize legendNameBudget t
            // A BAND arm packs at each entry's NATURAL width and never
            // truncates: the overflow it can suffer is in the SUM, not in one
            // name, so truncating would cost information without fixing
            // anything. A band that cannot pack falls back to the column above
            // — and then this arm is `Right`, so the budget applies after all.
            | _ -> t)

    let legendColumnW =
        match legendPos with
        | ChartLegendPosition.Right -> r2 (style.LegendColumnGap + style.LegendLabelOffsetX + widestOf legendTexts)
        | _ -> 0.0

    /// The `Bottom` band's height — one line plus its padding, reserved BELOW
    /// everything the bottom margin's autosize already accounts for (the x-axis
    /// title's line included), so the two computations never contend for the
    /// same pixels. The exact mirror of `subtitleBand` at the top: one term
    /// that shifts the whole band, present only when the arm is.
    let legendBandH =
        match legendPos with
        | ChartLegendPosition.Bottom -> r2 (lineHeight + style.AxisLabelPadding)
        | _ -> 0.0

    // Phase 880 — a `Right` legend takes its column off the plot, not off the
    // right margin: the margin stays the clearance between the legend's widest
    // label and the canvas edge, exactly as it was the clearance to the plot
    // before. Every other placement leaves `legendColumnW = 0`, so the pre-880
    // rectangle is recovered term-for-term.
    let plotX1 = style.Width - style.MarginRight - legendColumnW
    let plotW = plotX1 - plotX0

    let bandW = if n > 0 then plotW / float n else plotW
    let centreX (i: int) : float = r2 (plotX0 + bandW * (float i + 0.5))

    /// The `i`th BAND BOUNDARY — `n` bands have `n+1` of them, boundary `0` at
    /// the y-axis spine and boundary `n` at the plot's right edge. Phase 903's
    /// category tick marks land here, where a label lands at `centreX`.
    let boundaryX (i: int) : float = r2 (plotX0 + bandW * float i)

    // ── The x-axis-label ANGLE LADDER (Phase 903, correcting Phase 879) ──
    // The BAND arms label categories; Pie has no x axis at all and Scatter
    // labels numeric x ticks (short by construction, left horizontal). Both of
    // those must contribute NO drop, or their bottom margin — and with it the
    // pie's centre — would move for a decision they never take.
    let drawsCategoryLabels =
        not isScatter
        && not isTemporal
        && (match spec.Kind with
            | ChartKind.Pie -> false
            | _ -> true)

    // Phase 882 — a TEMPORAL axis labels its TICKS, and the ladder applies to
    // them: same three rungs, same footprint formula, measured against the TICK
    // PITCH instead of the band pitch. A date label is not short by
    // construction the way a numeric tick is (`15 Jan 26` against `150`), so
    // leaving it always-flat would recreate exactly the overlap the ladder
    // exists to resolve — and reusing the ladder rather than adding a second
    // rule is what keeps one angle policy for the whole x axis.
    let temporalTickTexts =
        if isTemporal then
            xTicks |> List.map xTickText |> List.toArray
        else
            [||]

    /// Whether the x axis draws labels the ladder governs at all — the band
    /// arms' categories or a temporal axis's ticks. Scatter and Pie: no.
    let drawsXAxisLabels = drawsCategoryLabels || isTemporal

    /// The pitch the ladder measures a label against: a band's width, or — on a
    /// temporal axis — the SMALLEST pixel gap between consecutive ticks, since
    /// calendar gaps are not uniform (28 to 31 days a month) and the tightest
    /// pair is the one that has to fit. Computable here because it needs `plotW`
    /// only, which the left margin has already fixed: the acyclicity Phase 879
    /// established survives intact, with nothing reading the bottom margin the
    /// ladder is about to decide.
    let xLabelPitch =
        if not isTemporal then
            bandW
        else
            let span = xNiceHi - xNiceLo

            match xTicks with
            | []
            | [ _ ] -> plotW
            | ts ->
                let minGap =
                    List.zip (List.truncate (ts.Length - 1) ts) (List.tail ts)
                    |> List.fold (fun acc (a, b) -> min acc (b - a)) span

                plotW * minGap / span

    /// The labels the ladder decides on, AS AUTHORED (see below).
    let xLabelsAsAuthored = if isTemporal then temporalTickTexts else categories

    // A rotated label's footprint ALONG the axis is its width's horizontal
    // projection plus the line height's: `w·cos θ + h·sin θ`. At 0° that is the
    // bare width (`cos 0 = 1`, `sin 0 = 0`, both exact on every IEEE-754 host,
    // so the flat rung needs no special case); at 90° the width term vanishes,
    // so the vertical arm packs one label per line height at any category count
    // — which is why it is the terminal rung and there is nothing beyond it.
    let radians (deg: float) : float = deg * System.Math.PI / 180.0

    let alongAxisFootprint (deg: float) (w: float) : float =
        w * cos (radians deg) + lineHeight * sin (radians deg)

    // THREE RUNGS, ONE PREDICATE, applied to the WIDEST label and therefore
    // UNIFORMLY to the axis: flat while every label fits its band, 30° when it
    // does not, vertical when 30° no longer packs either. Phase 879 read the
    // tilt as the resting state and started at rung two; the operator's
    // correction (2026-08-17) is that the tilt is the MIDDLE rung of a
    // fit-driven ladder — "North South East West" is legible flat and should
    // read flat. Deciding on the widest label rather than per-label is what
    // keeps an axis from mixing angles, which reads as a defect however
    // individually-correct each label's own angle would be.
    //
    // The decision is taken on the labels AS AUTHORED (Phase 879's rule, kept):
    // `widestOf xLabelsAsAuthored`, not the truncated `xLabelTexts` — the
    // truncation budget below is a function of the angle, so reading the
    // truncated text here would be circular as well as wrong.
    let widestXLabel = widestOf xLabelsAsAuthored

    let packsAt (deg: float) : bool =
        alongAxisFootprint deg widestXLabel <= xLabelPitch

    let tiltDegrees =
        if not drawsXAxisLabels || n = 0 || style.LabelTiltDegrees <= 0.0 then
            // `LabelTiltDegrees = 0` is FLAT-ALWAYS, not "the ladder with a
            // flat rung": a host that zeroed the tilt angle named the one
            // rotation the ladder is allowed to use, and escalating past that
            // to vertical would override an explicit choice with a computed
            // one. An author who wants the ladder leaves the default in place —
            // which since Phase 903 already starts flat, so the opt-out costs
            // nothing but the escalation it deliberately declines.
            0.0
        elif packsAt 0.0 then
            0.0
        elif packsAt style.LabelTiltDegrees then
            style.LabelTiltDegrees
        else
            style.VerticalTiltDegrees

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

    /// The x labels as DRAWN — the ladder's own labels, bounded by the drop
    /// ceiling. Empty on the arms that draw none, so their bottom margin is
    /// unmoved (Scatter's short numeric ticks are emitted separately, flat).
    let xLabelTexts =
        if drawsXAxisLabels then
            xLabelsAsAuthored |> Array.map categoryLabelText
        else
            [||]

    let requiredBottom =
        style.CategoryLabelOffsetY
        + sinTilt * widestOf xLabelTexts
        + style.AxisLabelPadding
        + lineHeight
        + style.AxisTitleBottomOffset

    // The `Bottom` legend's band is ADDED to the autosized margin rather than
    // competing inside its ceiling: the ceiling exists to stop LABELS eating
    // the plot, and the legend is not a label. So the picture shrinks by the
    // band, and the tilt escalation still sees the budget it had.
    let marginBottom =
        r2 (legendBandH + max style.MarginBottom (min bottomCeiling requiredBottom))

    let plotY0 = marginTop
    let plotY1 = style.Height - marginBottom
    let plotH = plotY1 - plotY0

    let yScale (v: float) : float =
        r2 (plotY1 - (v - niceLo) / (niceHi - niceLo) * plotH)

    /// The x-scale before rounding. Split out by Phase 882 so the bar arms can
    /// derive an UNROUNDED slot origin from it: rounding a centre and then
    /// subtracting half a width would round twice, and the band arms' goldens
    /// pin the single-rounding form.
    let xScaleRaw (v: float) : float =
        plotX0 + (v - xNiceLo) / (xNiceHi - xNiceLo) * plotW

    let xScale (v: float) : float = r2 (xScaleRaw v)

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

    // Vertical gridlines — wherever the x axis is CONTINUOUS (Phase 875 for
    // Scatter, extended to the temporal axis by Phase 882). A continuous scale
    // has readable x positions, so a reader traces a point back to an x value
    // the same way the horizontal grid lets them trace a y value. A BAND x-axis
    // has no such positions to trace (a category is a label, not a magnitude),
    // so a vertical rule there would be decoration. Stating it as "continuous"
    // rather than "Scatter" is what let the temporal axis inherit the behaviour
    // instead of re-deciding it — including on a temporal BAR chart, where the
    // rules read as date guides through the bars rather than as chrome.
    let xGridlines =
        if isContinuousX then
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
    //
    // BAND vs CONTINUOUS (Phase 903). Where the axis is CONTINUOUS a tick marks
    // a VALUE and sits at it: the y axis, and Scatter's numeric x. Where it is a
    // BAND axis a tick DELIMITS a group, so the `n+1` marks land on the band
    // BOUNDARIES and the label stays centred in the band between two of them —
    // the category-axis convention every spreadsheet draws, and the honest one:
    // a category has an extent, not a position, and a mark under its centre
    // claims a coordinate the axis does not have. Phase 882's temporal axis
    // TAKES the continuous side of this split: a date IS a position, so its
    // marks sit at their dates and its labels are centred ON them — there are no
    // boundaries to delimit, because there are no bands.
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
                if isContinuousX then xTicks |> List.map (xScale >> xAt)
                elif n = 0 then []
                else [ for i in 0..n -> xAt (boundaryX i) ]

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
    // Every category label sits at its band CENTRE — including since Phase 903,
    // when the tick marks moved to the boundaries: the label names the band, the
    // marks delimit it, and that is the whole point of the split.
    //
    // The ANCHOR follows the ladder's rung. At the FLAT rung a label is
    // `Middle`-anchored on the band centre — the ordinary reading of a centred
    // caption, and the pre-879 convention this restores. At either ROTATED rung
    // it is `End`-anchored at the same point and rotated NEGATIVELY
    // (counter-clockwise, against `DrawStyle.Rotation`'s clockwise convention):
    // the anchor is the pivot, so the text ENDS under the band centre and runs
    // back down-and-left, reading up-to-the-right into it. The opposite sign
    // would swing the same text up into the plot area. At the vertical rung this
    // degenerates to reading bottom-up — the y-axis title's convention. Scatter's numeric ticks stay horizontal + `Middle`: they are
    // short by construction, and centring them on their tick is the correct
    // reading of a value axis.
    //
    // Phase 882 — a TEMPORAL axis's labels sit at their TICKS (not at a band
    // centre, because there are no bands) and take the ladder's rung and anchor
    // exactly as the band arms do. So one expression covers "centred at the
    // position the label names" on both, and the only thing that differs is
    // which positions those are.
    let tiltedLabelStyle =
        let s =
            textStyle style (Some style.LabelOpacity) TextAnchor.End tickSize Emphasis.Normal

        { s with
            Rotation = Some(r2 -tiltDegrees) }

    let xLabelStyle =
        if tiltDegrees > 0.0 then
            tiltedLabelStyle
        else
            textStyle style (Some style.LabelOpacity) TextAnchor.Middle tickSize Emphasis.Normal

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
        elif isTemporal then
            List.zip xTicks (List.ofArray xLabelTexts)
            |> List.map (fun (t, text) ->
                Shape.Label(xScale t, r2 (plotY1 + style.CategoryLabelOffsetY), TextSource.Literal text, xLabelStyle))
        else
            xLabelTexts
            |> Array.mapi (fun i c ->
                Shape.Label(centreX i, r2 (plotY1 + style.CategoryLabelOffsetY), TextSource.Literal c, xLabelStyle))
            |> Array.toList

    // ── Axis titles + the display-unit slot (Phase 878) ──
    //
    // Three rules, and together they retire the hardcoded `"Value"`:
    //
    //   1. NAMES. The x title stays centred under the tick band (where it has
    //      always been); the y title is ROTATED by `-YAxisTitleDegrees` in the
    //      left margin, centred on the plot, reading BOTTOM-UP — the
    //      conventional treatment, and the same sign convention Phase 879's
    //      vertical category labels already use. Each falls back to its
    //      capitalised field name, so an axis is never nameless.
    //
    //   2. UNITS KEEP THEIR OWN SLOT. The top-left label states the Phase-876
    //      display unit and NOTHING else: with no scaling in play it is not
    //      drawn at all, where it previously fell back to the literal `"Value"`
    //      — a word naming neither the measure nor its unit, printed on every
    //      chart in the corpus. Composing the unit INTO the rotated title
    //      ("Revenue (Millions of £)") was the alternative and was rejected:
    //      that concatenation is only expressible when the title is a
    //      `Literal`, so a bound or i18n title would silently fall back to a
    //      different layout — and a layout rule with a shape that depends on
    //      which `TextSource` arm an author reached for is not a rule. Two
    //      slots, always the same two, is what stays total.
    //
    //   3. DEDUPE. An explicit `Subtitle` SUPPRESSES the unit slot. The
    //      subtitle is the author's own place to say "£m", and the machine
    //      restating it two lines away is exactly the clutter this rule exists
    //      to prevent — so the author's sentence wins. PRESENCE is the whole
    //      test: no string comparison, which is what keeps the rule total over
    //      every `TextSource` arm and identical on every host.
    //
    // A SELF-EVIDENT DATE AXIS SUPPRESSES ITS DEFAULT TITLE — an axis reading
    // "Jan Feb Mar" does not need the word "Month" beneath it. The rule is
    // recorded here and in `docs/CHARTS-DRAWING-PRIMITIVE-DESIGN.md` §4e, and
    // is WIRED when Phase 882's temporal axis lands: nothing in the lowering
    // can currently tell a date column from a string one, and inferring it from
    // the label text would be a guess dressed as a rule. It will apply to the
    // FALLBACK only — an explicit `XTitle` is the author overriding the
    // default, and always draws.

    /// Bound a title to the extent it runs along. Only a `Literal` can be
    /// truncated — the text behind a `Bound` or `I18n` arm is not known here —
    /// and that is the honest boundary: those pass through and may overrun,
    /// which is a visible fact rather than a silently wrong measurement.
    let boundText (fontSize: float) (extent: float) (t: TextSource) : TextSource =
        match t with
        | TextSource.Literal s -> TextSource.Literal(TextMetrics.truncateToWidth fontSize extent s)
        | other -> other

    let axisTitleStyle (anchor: TextAnchor) : DrawStyle =
        textStyle style None anchor tickSize Emphasis.Normal

    let xTitleShapes =
        match xTitle with
        | Some t ->
            [ Shape.Label(
                  r2 ((plotX0 + plotX1) / 2.0),
                  // Phase 880 — the x title rides ABOVE a `Bottom` legend band,
                  // keeping its own inset from whatever is beneath it.
                  // `legendBandH` is 0 on every other arm, so the pre-880
                  // baseline is unchanged.
                  r2 (style.Height - legendBandH - style.AxisTitleBottomOffset),
                  boundText tickSize plotW t,
                  axisTitleStyle TextAnchor.Middle
              ) ]
        | None -> []

    let yTitleShapes =
        match yTitle with
        | Some t ->
            let rotated =
                { axisTitleStyle TextAnchor.Middle with
                    Rotation = Some(r2 -style.YAxisTitleDegrees) }

            // `Middle`-anchored at the plot's vertical centre: the anchor is
            // the pivot, so the rotated text stays centred on the axis it
            // names, whatever its length. The x is measured from the CANVAS
            // edge, not the autosized margin, so the title does not slide as
            // tick widths change.
            [ Shape.Label(r2 style.YAxisTitleOffsetX, r2 ((plotY0 + plotY1) / 2.0), boundText tickSize plotH t, rotated) ]
        | None -> []

    let unitSlotShapes =
        if yDisplayUnit.Label = "" || Option.isSome spec.Subtitle then
            []
        else
            [ Shape.Label(
                  r2 style.AxisTitleLeftX,
                  r2 (plotY0 - style.AxisTitleTopOffset),
                  TextSource.Literal yDisplayUnit.Label,
                  axisTitleStyle TextAnchor.Start
              ) ]

    let axisTitles = xTitleShapes @ yTitleShapes @ unitSlotShapes

    // ── Where a datum sits along x (Phase 882) ───────────────────────────────
    //
    // ONE pair of expressions the series geometry reads, and the band-vs-value
    // difference lives here and nowhere else. On a band axis a datum sits at its
    // band's INDEX; on a temporal axis it sits at its DATE — the same datum, a
    // different question asked of the axis.
    //
    // The temporal slot keeps `bandW` as its PITCH — `plotW / n`, the average
    // spacing — so a bar's thickness is decided by the same expression on both
    // axes and a monthly bar chart looks like a bar chart rather than like a
    // sequence of hairlines. With irregular dates two slots can overlap; that is
    // honest, because the bars are at their true positions and the overlap is
    // the data's, not the layout's. `BarMaxThickness` already bounds the other
    // direction.
    //
    // A CONSEQUENCE, RECORDED RATHER THAN PATCHED: because the domain is the
    // data's own extent, the first and last bars are centred ON the plot's
    // edges, so each overhangs it by half its thickness (~7 px at the shipped
    // constants — inside the canvas, never clipped; `bar-temporal-monthly` pins
    // it). Padding the domain by half a pitch for the bar arms was the
    // alternative and is worse: it would make the DOMAIN kind-dependent, so a
    // Line and a Bar over identical rows would disagree about where a given date
    // sits — two pictures of one dataset that cannot be read against each other.
    // A mark's position is the datum; a bar's width is a legibility affordance,
    // and the affordance is what yields at the edge.

    /// The x a datum's mark centres on.
    let xCentre (i: int) : float =
        if isTemporal then xScale xValues.[i] else centreX i

    /// The UNROUNDED left edge of the slot a datum's bar geometry lays out in.
    /// Unrounded because the bar arms round once, at the end — the band form is
    /// `plotX0 + bandW·i` character-for-character, so every band golden is
    /// unmoved.
    let slotOriginX (i: int) : float =
        if isTemporal then
            xScaleRaw xValues.[i] - bandW / 2.0
        else
            plotX0 + bandW * float i

    // ── Bar geometry ──
    //
    // Hoisted out of the two Bar arms (Phase 881) because the cap labels have to
    // land on the SAME caps the rectangles draw: one expression per quantity, so
    // a label and its bar cannot disagree about where the bar is. The arithmetic
    // is character-for-character what the arms computed inline before, which is
    // why every golden is unmoved.
    let barGroupW = bandW * style.BarGroupWidthFraction

    /// The single capped bar of a STACKED category.
    let stackedBarW =
        r2 (min (barGroupW * style.BarWidthFraction) style.BarMaxThickness)

    let stackedBarX (i: int) : float =
        r2 (slotOriginX i + (bandW - stackedBarW) / 2.0)

    /// A grouped bar's own sub-slot within the band, and its capped thickness.
    let groupedSubW = if m > 0 then barGroupW / float m else barGroupW

    let groupedBarW =
        r2 (min (groupedSubW * style.BarWidthFraction) style.BarMaxThickness)

    let groupedBarX (i: int) (j: int) : float =
        // Centre the (possibly capped) bar in its own sub-slot, so a cap takes
        // air off BOTH sides and the group stays symmetric about the band centre.
        let slotX = slotOriginX i + (bandW - barGroupW) / 2.0 + float j * groupedSubW

        r2 (slotX + (groupedSubW - groupedBarW) / 2.0)

    // ── Series geometry ──
    let seriesShapes =
        match spec.Kind with
        | ChartKind.Bar when stacked ->
            // One capped bar per category, centred in its band; series stack as
            // segments between consecutive cumulative sums (Phase 637), each
            // shortened by `StackSegmentGap` on the side facing the next segment
            // so the boundaries read as gaps rather than colour changes
            // (Phase 875).
            let bw = stackedBarW

            [ for i in 0 .. n - 1 do
                  let bx = stackedBarX i
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
                          // Phase 883 — a stack SEGMENT's tip carries its OWN
                          // series value, never the running total. This is
                          // where an interior segment finally gets a readout:
                          // Phase 881 prints the stack TOTAL at the cap and
                          // nothing else (an interior number is unreadable
                          // against the segment above it) and pointed here for
                          // the rest.
                          styleFill (colourFor style j)
                          |> withMark yFields.[j] categories.[i]
                          |> datumTip yFields.[j] categories.[i] series.[j].[i]
                      ) ]
        | ChartKind.Bar ->
            let bw = groupedBarW
            let baseY = yScale 0.0

            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]

                  for i in 0 .. n - 1 do
                      let v = values.[i]
                      let bx = groupedBarX i j
                      let vy = yScale v
                      let top = min vy baseY
                      let hgt = r2 (abs (vy - baseY))

                      Shape.Rectangle(
                          bx,
                          top,
                          bw,
                          hgt,
                          None,
                          styleFill colour
                          |> withMark yFields.[j] categories.[i]
                          |> datumTip yFields.[j] categories.[i] v
                      ) ]
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

                      let upper = [ for i in 0 .. n - 1 -> dp (xCentre i) (yScale cums.[i].[j + 1]) ]

                      let lower = [ for i in n - 1 .. -1 .. 0 -> dp (xCentre i) (yScale cums.[i].[j]) ]

                      yield
                          Shape.Polygon(
                              upper @ lower,
                              styleFillOpacity colour style.AreaFillOpacity
                              |> withSeriesMark yf
                              |> seriesTip yf
                          )

                      yield
                          Shape.Polyline(
                              upper,
                              styleStroke colour style.SeriesStrokeWidth |> withSeriesMark yf |> seriesTip yf
                          ) ]
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

                      let points = [ for i in 0 .. n - 1 -> dp (xCentre i) (yScale values.[i]) ]

                      let band = (dp (xCentre 0) baseY :: points) @ [ dp (xCentre (n - 1)) baseY ]

                      yield
                          Shape.Polygon(
                              band,
                              styleFillOpacity colour style.AreaFillOpacity
                              |> withSeriesMark yf
                              |> seriesTip yf
                          )

                      yield
                          Shape.Polyline(
                              points,
                              styleStroke colour style.SeriesStrokeWidth |> withSeriesMark yf |> seriesTip yf
                          ) ]
        | ChartKind.Line ->
            [ for j in 0 .. m - 1 do
                  let colour = colourFor style j
                  let values = series.[j]

                  let points = [ for i in 0 .. n - 1 -> dp (xCentre i) (yScale values.[i]) ]

                  Shape.Polyline(
                      points,
                      styleStroke colour style.SeriesStrokeWidth
                      |> withSeriesMark yFields.[j]
                      |> seriesTip yFields.[j]
                  ) ]
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
                          styleFill colour
                          |> withMark yf (DrawingSvg.formatNum xValues.[i])
                          // The tip's middle part is the x cell as PROJECTED
                          // (`categories.[i]`), not the mark id's canonical
                          // numeric form: the id is for object constancy, the
                          // tip is for a human, and on a temporal axis the
                          // projection is the ISO date rather than a day count.
                          |> datumTip yf categories.[i] values.[i]
                      ) ]
        | _ -> []

    // ── Data labels (Phase 881) — the values, written selectively ────────────
    //
    // `ChartSpec.DataLabels` has two states and no third: `Off` (the default,
    // and what an absent field means) and `Ends`. There is deliberately NO
    // all-points mode — a number on every interior point is the clutter this
    // vocabulary exists to prevent, so the API cannot express it. `Ends` names
    // the placements that read on their own:
    //
    //   * BARS label the CAP — above a positive cap, below a negative one, the
    //     two placements exact mirrors of each other about the cap.
    //   * A GROUPED bar labels every bar. A STACKED bar labels the TOTAL at the
    //     stack cap and nothing else: an interior segment's own value is
    //     unreadable against the segment above it, and the legend plus the
    //     Phase-883 hover readout already serve it.
    //   * LINES and AREA EDGES label the LAST point of each series, right of
    //     the endpoint and nudged up off the line — the end of a series is the
    //     value a reader is looking for, and it is the one point with clear air
    //     beside it.
    //   * SCATTER gets nothing in v1 (RECORDED DECISION, not an omission): a
    //     scatter's "ends" are not privileged points. Its x axis is a value
    //     axis, so the last row carries no meaning the first does not, and
    //     labelling by row order would present an accident of the feed as a
    //     reading of the chart. Whatever a scatter's labels turn out to be —
    //     extremes, outliers, a named subset — it is a different rule, and one
    //     that needs its own evidence.
    //   * PIE is unchanged: its legend already carries `name (NN%)`.
    //
    // EVERY value goes through the axis's own formatter (`yTickText`), so a
    // label and a tick agree by construction — same precision from the same
    // tick step, and the same display-unit scaling, so a chart in millions
    // labels in millions rather than restating the magnitude the axis just
    // scaled away.
    //
    // NO LABEL EVER MOVES A MARGIN. The plot rectangle is decided long before
    // this point and is not revisited: a label either fits the room the picture
    // already has, or it is SUPPRESSED. That is what keeps `Off` byte-identical
    // to the pre-881 layout, and it is also the honest posture — a value that
    // cannot be written legibly is still on the axis and still under the
    // pointer, where a clipped or overlapping one is a wrong picture.
    let dataLabelsOn =
        match spec.DataLabels with
        | Some ChartDataLabels.Ends -> true
        | _ -> false

    let dataLabelSize = style.DataLabelFontSize

    let dataLabelLine = TextMetrics.lineHeight dataLabelSize style.TextLineHeightFactor

    let dataLabelStyle (anchor: TextAnchor) : DrawStyle =
        // Label-role ink at the chrome opacity — NEVER the series colour. A
        // value is a reading of the mark, not a second copy of its identity,
        // and a coloured number would compete with the mark it describes (and
        // would have to clear the surface contrast gate on its own, which the
        // palette is not chosen for).
        textStyle style (Some style.LabelOpacity) anchor dataLabelSize Emphasis.Normal

    /// The single fit gate: `TextMetrics.fitsBox` (Phase 879) against the room
    /// the placement actually has. No fit, no label — never a clip, never an
    /// overlap, and never a fallback placement inside the bar (a label inside a
    /// bar reads as part of the bar's colour, and the whole point of the cap
    /// placement is that the number sits on the surface, not on the ink).
    let dataLabel
        (anchor: TextAnchor)
        (x: float)
        (baseline: float)
        (maxWidth: float)
        (maxHeight: float)
        (text: string)
        : Shape list =
        if TextMetrics.fitsBox dataLabelSize style.TextLineHeightFactor maxWidth maxHeight text then
            [ Shape.Label(r2 x, r2 baseline, TextSource.Literal text, dataLabelStyle anchor) ]
        else
            []

    /// A value at a bar's cap, centred on `cx`. `pitch` is the distance to the
    /// NEXT label's centre — the neighbouring bar's slot — so the width budget
    /// is what separates two labels rather than what fits one bar: a label may
    /// legitimately be wider than the bar it caps, and may not be wider than
    /// the room between it and its neighbour.
    let capLabel (cx: float) (pitch: float) (v: float) : Shape list =
        let capY = yScale v
        let maxWidth = max 0.0 (pitch - 2.0 * style.DataLabelPadding)

        // The room in the direction the label goes, less the cap clearance and
        // the plot-edge padding. Above for a positive cap, below for a negative
        // one; `DataLabelOffsetY` is one constant used twice, so the two
        // placements are mirrors and neither can drift from the other.
        if v < 0.0 then
            dataLabel
                TextAnchor.Middle
                cx
                (capY + style.DataLabelOffsetY + dataLabelSize)
                maxWidth
                (plotY1 - capY - style.DataLabelOffsetY - style.DataLabelPadding)
                (yTickText v)
        else
            dataLabel
                TextAnchor.Middle
                cx
                (capY - style.DataLabelOffsetY)
                maxWidth
                (capY - plotY0 - style.DataLabelOffsetY - style.DataLabelPadding)
                (yTickText v)

    /// The series-endpoint labels, in series order. Two gates, and the second
    /// is the vertical analogue of the cap labels' pitch: every endpoint label
    /// shares one x, so the thing they can collide with is each other. A label
    /// is admitted only when its line clears every ALREADY-ADMITTED label's by
    /// the padding — series order decides who yields, which makes the outcome
    /// deterministic and the earlier series the one that keeps its number.
    let endpointLabels (valueAt: int -> float) : Shape list =
        if n = 0 then
            []
        else
            let px = xCentre (n - 1)
            let labelX = px + style.DataLabelEndOffsetX
            // The width budget runs to the PLOT's right edge, not the canvas's:
            // beyond it lies the legend column (or the right margin), and a
            // label that ran into the legend would be exactly the collision the
            // gate exists to refuse.
            let maxWidth = max 0.0 (plotX1 - labelX - style.DataLabelPadding)

            let mutable admitted: float list = []
            let mutable shapes: Shape list = []

            for j in 0 .. m - 1 do
                let v = valueAt j
                let baseline = yScale v - style.DataLabelEndNudgeY

                let separated =
                    admitted
                    |> List.forall (fun b -> abs (b - baseline) >= dataLabelLine + style.DataLabelPadding)

                if separated then
                    let emitted =
                        dataLabel
                            TextAnchor.Start
                            labelX
                            baseline
                            maxWidth
                            (baseline - plotY0 - style.DataLabelPadding)
                            (yTickText v)

                    if not (List.isEmpty emitted) then
                        admitted <- baseline :: admitted
                        shapes <- shapes @ emitted

            shapes

    let dataLabelShapes =
        if not dataLabelsOn then
            []
        else
            match spec.Kind with
            | ChartKind.Bar when stacked ->
                // The TOTAL at the stack cap, once per category — `cumsFor i`'s
                // last entry is the stack's own top, which is the value the
                // whole bar's height means.
                [ for i in 0 .. n - 1 do
                      let cums = cumsFor i
                      yield! capLabel (stackedBarX i + stackedBarW / 2.0) bandW cums.[m] ]
            | ChartKind.Bar ->
                [ for j in 0 .. m - 1 do
                      for i in 0 .. n - 1 do
                          yield! capLabel (groupedBarX i j + groupedBarW / 2.0) groupedSubW series.[j].[i] ]
            | ChartKind.Area when stacked ->
                // The band's own UPPER boundary is the edge that was drawn, so
                // it is the cumulative value there — not the series' own datum,
                // which is nowhere on the picture.
                endpointLabels (fun j -> (cumsFor (n - 1)).[j + 1])
            | ChartKind.Line
            | ChartKind.Area -> endpointLabels (fun j -> series.[j].[n - 1])
            | _ -> []

    // ── Legend (Phase 880) — one entry list, four placements ──
    //
    // COLUMN (`Right`, the shipped default): one row per entry, each a swatch
    // and its label, the plot already shrunk by the column above. Rows are
    // TOP-ALIGNED with the plot rather than vertically centred, deliberately:
    // centring makes row j's y a function of the entry COUNT, so adding a
    // series moves every row that was already there — chrome sliding under a
    // data refresh is precisely what this module's mark-identity rule exists to
    // avoid, and there is no reason to reintroduce it for the legend. Reading
    // order is also series order, which is the order the rows are in.
    //
    // This is what structurally retires the overflow. A BAND's width is the SUM
    // of its entries, so it runs off the canvas once the names are long enough
    // or numerous enough, silently and with no ellipsis. A COLUMN's width is
    // the MAX of its entries — bounded by `LegendColumnMaxShare` and truncated
    // at it — and its height is one pitch per entry into 400 px of canvas.
    // Neither term grows without limit, so the eight-slot palette's eight-series
    // chart legends itself by construction rather than by luck of naming.
    //
    // BAND (`Top` / `Bottom`): Phase 879's horizontal row, entries laid out
    // cumulatively from the plot's left edge at each entry's own natural width
    // — unchanged for `Top`, which is the pre-880 shape every pre-880 golden
    // pins. A band that cannot PACK into the plot's width no longer runs off the
    // edge: `bandOverflows` above sends the whole legend to the column instead
    // (operator decision, 2026-08-18), so by the time this arm is reached the
    // entries are known to fit.
    //
    // The label styling is one expression for all four: chrome ink at
    // `LabelOpacity`, `Start`-anchored, tick-sized.
    let legendLabelStyle =
        textStyle style (Some style.LabelOpacity) TextAnchor.Start tickSize Emphasis.Normal

    let legendRow (swatchX: float) (rowTop: float) (j: int) : Shape list =
        [ Shape.Rectangle(
              r2 swatchX,
              r2 rowTop,
              style.LegendSwatchSize,
              style.LegendSwatchSize,
              Some style.LegendSwatchCornerRadius,
              styleFill (fst legendEntries.[j])
          )
          Shape.Label(
              r2 (swatchX + style.LegendLabelOffsetX),
              r2 (rowTop + style.LegendLabelBaselineDy),
              TextSource.Literal legendTexts.[j],
              legendLabelStyle
          ) ]

    let legend =
        match legendPos with
        | ChartLegendPosition.None -> []
        | ChartLegendPosition.Right ->
            let swatchX = plotX1 + style.LegendColumnGap

            [ for j in 0 .. legendEntries.Length - 1 do
                  yield! legendRow swatchX (plotY0 + style.LegendRowPitchY * float j) j ]
        | ChartLegendPosition.Top
        | ChartLegendPosition.Bottom ->
            // Phase 878 — the TOP band sits BELOW the subtitle, so it moves
            // down by the line the subtitle took; `subtitleBand` is 0 without
            // one, leaving the pre-878 constants exactly where they were. The
            // BOTTOM band mirrors from the canvas bottom off the band the
            // margin already reserved, so it needs no constants of its own.
            let swatchY, baselineY =
                match legendPos with
                | ChartLegendPosition.Bottom ->
                    let rowTop = style.Height - legendBandH
                    rowTop, rowTop + style.LegendLabelBaselineDy
                | _ -> style.LegendSwatchY + subtitleBand, style.LegendLabelBaselineY + subtitleBand

            // Prefix sums — entry j starts where every earlier entry ended, at
            // the same `bandEntryWidth` the overflow rule measured against.
            let xs =
                Array.init legendEntries.Length id
                |> Array.scan (fun acc j -> acc + bandEntryWidth legendTexts.[j]) plotX0

            [ for j in 0 .. legendEntries.Length - 1 do
                  let lx = r2 xs.[j]

                  yield
                      Shape.Rectangle(
                          lx,
                          r2 swatchY,
                          style.LegendSwatchSize,
                          style.LegendSwatchSize,
                          Some style.LegendSwatchCornerRadius,
                          styleFill (fst legendEntries.[j])
                      )

                  yield
                      Shape.Label(
                          r2 (lx + style.LegendLabelOffsetX),
                          r2 baselineY,
                          TextSource.Literal legendTexts.[j],
                          legendLabelStyle
                      ) ]

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

    // ── Subtitle (Phase 878) — the muted line under the title ──
    //
    // MUTED (label-role opacity, not full-strength ink) and SMALLER than the
    // title, sharing its x and its anchor, so the pair reads as one block and
    // the subtitle is unmistakably subordinate. It draws independently of the
    // title: an author who sets one and not the other gets what they asked
    // for, and the top margin has already reserved the line either way.
    let subtitleShapes =
        match spec.Subtitle with
        | Some s ->
            [ Shape.Label(
                  titleX,
                  style.SubtitleBaselineY,
                  boundText subtitleSize plotW s,
                  textStyle style (Some style.LabelOpacity) titleAnchor subtitleSize Emphasis.Normal
              ) ]
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
    //
    // Phase 880 — this emits WEDGES ONLY. The pie's legend was the vertical
    // right-hand column the cartesian arms have now converged on, so it is
    // emitted by the shared `legend` above (from the shared `legendEntries`,
    // which carry the shares) and honours `LegendPosition` like any other. The
    // guard + the shares themselves were lifted above the margins, because the
    // legend's width is layout input.
    let pieShapes () : Shape list =
        if pieRefused then
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

            let starts = pieFractions |> Array.scan (+) 0.0
            let top = -System.Math.PI / 2.0

            // Half the angular padding comes off each end of every wedge
            // (Phase 875), so the separation is a sliver of absent ink — no
            // surface colour is needed and the result is theme-invariant, which
            // a stroked wedge border could not be.
            let halfGap = style.WedgeGapDegrees * System.Math.PI / 360.0

            let segs =
                let yf = yFields.[0]

                [ for i in 0 .. n - 1 do
                      let f = pieFractions.[i]

                      if f > 0.0 then
                          let colour = colourFor style i

                          let markStyle =
                              styleFill colour
                              |> withMark yf categories.[i]
                              // The wedge's own VALUE, not its share. The share
                              // is already stated, once, in the legend entry
                              // (`name (NN%)`); restating it here would make the
                              // one number the pie does not otherwise print —
                              // the magnitude behind the slice — the one number
                              // still unreachable.
                              |> datumTip yf categories.[i] pieValues.[i]

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

            segs

    // Pie is polar — no axes/gridlines/tick chrome; every other arm assembles
    // the shared cartesian chrome in painter's order: gridlines (h then v), the
    // zero baseline, axes, tick marks, y-tick + x labels, axis titles, series,
    // legend, chart title. Since Phase 880 BOTH arms take the same `legend` in
    // the same slot — geometry, then legend, then titles.
    let shapes =
        match spec.Kind with
        | ChartKind.Pie -> pieShapes () @ legend @ titleShapes @ subtitleShapes
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
            // Phase 881 — the values sit ON the series, so they are painted
            // straight after it and before the legend: over their own marks,
            // under nothing that would obscure them.
            @ dataLabelShapes
            @ legend
            @ titleShapes
            @ subtitleShapes

    // ── The accessible summary (Phase 921) ───────────────────────────────────
    //
    // The grammar is stated once, at the section head above and normatively in
    // §4i; what follows is its four clauses in order. Every string it reads is
    // either a canonical constant, a field/category name off the data feed (each
    // clamped), or a number through the axis's own Phase-876 formatter — so it
    // is deterministic, host-invariant, and bounded.
    let accessibleSummary: string option =
        // A REFUSED PIE announces nothing, for exactly the reason Phase 880 gave
        // when it stopped emitting the refused pie's legend: "a legend for a
        // picture that was refused would be a claim about data the drawing
        // declined to show". A summary is the same claim, in words.
        if pieRefused then
            None
        else
            let namedSeries =
                yFields
                |> Array.truncate summaryMaxSeriesNamed
                |> Array.map (clampText summaryMaxNameChars)
                |> String.concat ", "

            let seriesClause =
                if m = 0 then
                    "no series"
                elif m > summaryMaxSeriesNamed then
                    string m
                    + " series: "
                    + namedSeries
                    + ", and "
                    + string (m - summaryMaxSeriesNamed)
                    + " more"
                else
                    string m + " series: " + namedSeries

            // The extent clause follows the X AXIS's own kind, not the chart's:
            // a band axis has categories and states its first and last, while a
            // continuous axis (Phase 903's split — the Scatter arm's numeric x
            // and Phase 882's temporal x) has a DOMAIN and states its endpoints
            // through that axis's own tick formatter. So a temporal chart reads
            // "Jan 26 to Dec 26" in the format its ticks are already drawn in,
            // and the summary can never disagree with the picture about how a
            // date is written.
            let extentClause =
                if isContinuousX then
                    if n = 0 then
                        "no points"
                    else
                        (if n = 1 then "1 point: " else string n + " points: ")
                        + xTickText xNiceLo
                        + " to "
                        + xTickText xNiceHi
                elif n = 0 then
                    "no categories"
                elif n = 1 then
                    "1 category: " + clampText summaryMaxNameChars categories.[0]
                else
                    string n
                    + " categories: "
                    + clampText summaryMaxNameChars categories.[0]
                    + " to "
                    + clampText summaryMaxNameChars categories.[n - 1]

            // The peak is the largest SINGLE DATUM — never a stacked total,
            // because the clause names one series at one category and a total
            // belongs to neither. Ties resolve to the earliest category, then
            // the earliest series (a strict `>` scanned category-major), which
            // is the axis's own reading order.
            //
            // Its NUMBER takes the value axis's rendering: the Phase-876
            // formatter at the axis's step precision, plus the axis's display
            // unit stated in the axis's own words. The chart says one thing in
            // one vocabulary. (Phase 883's tip takes the opposite three
            // decisions — unscaled, the datum's own precision, currency symbol
            // kept — because a tooltip stands alone with no unit slot beside it.
            // A summary is not alone: it names the unit itself.)
            //
            // Its CATEGORY is the datum's own label, verbatim — not the axis
            // format, even on a temporal axis. The extent clause has already
            // stated how the axis writes a date; this clause has to identify one
            // point, and "Mar 26" identifies a month where "2026-03-15"
            // identifies the datum.
            let peakClause =
                if n = 0 || m = 0 then
                    []
                else
                    let mutable bi = 0
                    let mutable bj = 0
                    let mutable bv = series.[0].[0]

                    for i in 0 .. n - 1 do
                        for j in 0 .. m - 1 do
                            if series.[j].[i] > bv then
                                bv <- series.[j].[i]
                                bi <- i
                                bj <- j

                    let unitSuffix =
                        if yDisplayUnit.Label = "" then
                            ""
                        else
                            " " + yDisplayUnit.Label

                    [ "Peak "
                      + clampText summaryMaxNameChars yFields.[bj]
                      + " at "
                      + clampText summaryMaxNameChars categories.[bi]
                      + ", "
                      + yTickText bv
                      + unitSuffix ]

            let clauses =
                [ summaryKindWords spec.Kind stacked; seriesClause; extentClause ] @ peakClause

            Some(clampText summaryMaxChars (String.concat summaryClauseSeparator clauses + "."))

    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = style.Width
          Height = style.Height }
      Shapes = shapes
      Style = emptyStyle
      Title = spec.Title
      Description = accessibleSummary |> Option.map TextSource.Literal }

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
