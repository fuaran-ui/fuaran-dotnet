module Fuaran.UI.Tests.ChartLoweringTests

#nowarn "3261" // DirectoryInfo.Parent + box are legitimately nullable here.

// ============================================================================
//  Phase 526 — Chart → Drawing lowering certification + determinism.
//
//  The reference lowering (`Fuaran.UI.Charts.lower`) is pinned by the
//  language-neutral `wire-format-fixtures/chart-lowering/*` fixture family: each
//  case ships an `<name>.input.json` (the ChartSpec + data rows, the neutral
//  cross-host contract for Phase 527) and an `<name>.expected.json` (the
//  canonical Drawing wire JSON the lowering must produce). This suite asserts
//  the F# lowering reproduces each golden byte-for-byte, and that the lowering
//  is deterministic + independent of row-collection enumeration order.
//
//  Regenerate the goldens with `FUARAN_EMIT_CHART_LOWERING=1` (writes the
//  input + expected files), then commit them.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

/// The lowering's own round-half-up-to-2dp rule, so an expectation below can be
/// written the way the lowering computes it rather than as a magic literal.
let private r2 (x: float) : float = floor (x * 100.0 + 0.5) / 100.0

/// One lowering case: a chart kind + fields + title + rows (x label, one y per
/// series). `XNums = Some ns` replaces the string x labels with numeric x
/// values (the Scatter arm's linear axis, Phase 636) — `Rows`' fst is then
/// unused; `ns` must be the same length.
type private Case =
    {
        Name: string
        Kind: ChartKind
        XField: string
        YFields: string list
        /// Phase 1143 — a `TextSource`, not a string: every arm crosses the
        /// lowering unresolved, so the harness has to be able to spell one.
        /// `lit` builds the `Literal` arm the pre-1143 cases all carry, and
        /// `inputJson` emits it as the bare string that is its canonical form,
        /// so not one of their `.input.json` files moved a byte.
        Title: TextSource option
        Stacked: bool
        XNums: float list option
        /// Phase 876 — the value axis's declared number format (a WIRE field:
        /// `ChartSpec.ValueFormat`), carried in the neutral input contract as
        /// `valueFormat` in canonical `Format` wire JSON.
        ValueFormat: Format option
        /// Phase 876 — the axis-unit mode. NOT a wire field (`ChartStyle` is a
        /// lowering parameter, D8): the neutral input contract carries it as
        /// `axisUnitMode` purely so the corpus can pin every mode, and a host
        /// reads it into the style it lowers with, never into a spec.
        UnitMode: string option
        /// Phase 878 — the axis NAMES and the muted subtitle (all three WIRE
        /// fields: `ChartSpec.XTitle` / `.YTitle` / `.Subtitle`), carried in the
        /// neutral input contract as plain strings beside `title`. Absent is
        /// the ordinary shape — each axis falls back to its capitalised field
        /// name — so the keys are OMITTED when `None` and the twenty pre-878
        /// inputs stay byte-identical.
        XTitle: TextSource option
        YTitle: TextSource option
        Subtitle: TextSource option
        /// Phase 880 — the legend's declared edge (a WIRE field:
        /// `ChartSpec.LegendPosition`), carried in the neutral input contract
        /// as the canonical enum string beside `title`. Absent means "the host
        /// style's default" — which is now `Right` — so the key is OMITTED when
        /// `None` and the pre-880 inputs stay byte-identical even though the
        /// PICTURE they lower to has moved.
        LegendPosition: string option
        /// Phase 881 — whether the chart writes its values onto the picture (a
        /// WIRE field: `ChartSpec.DataLabels`), carried in the neutral input
        /// contract as the canonical enum string beside `title`. Absent means
        /// `Off`, which is also the default, so the key is OMITTED when `None`
        /// and every pre-881 input AND golden is byte-identical.
        DataLabels: string option
        /// Phase 882 — what the x column MEANS (a WIRE field:
        /// `ChartSpec.XScale`), carried in the neutral input contract as the
        /// canonical enum string beside `title`. Absent means `Category`, which
        /// is also the default, so the key is OMITTED when `None` and every
        /// pre-882 input AND golden is byte-identical. `Temporal` cases carry
        /// their x cells as canonical ISO-8601 date STRINGS in `Rows`' fst —
        /// the same slot a category label uses, because that is exactly what a
        /// `Cell.Date` presents to the lowering.
        XScale: string option
        /// Phase 1490 (§4l) — the chart's data-addressed annotations (a WIRE
        /// field: `ChartSpec.Annotations`), carried in the neutral input
        /// contract as canonical `ChartAnnotation` wire JSON beside `title`.
        /// Absent OMITS the key, so every pre-1490 input AND golden is
        /// byte-identical. Typed as the real union rather than as a string
        /// because an annotation carries a payload — `valueFormat`'s treatment,
        /// not `xScale`'s.
        Annotations: ChartAnnotation list option
        Rows: (string * float list) list
    }

/// The `Literal` arm, which is what a case means when it writes a bare string.
let private lit (s: string) : TextSource = TextSource.Literal s

/// The default-shaped case — the Phase-876 and Phase-878 fields absent — so the
/// twelve pre-876 cases stay readable.
let private plain: Case =
    { Name = ""
      Kind = ChartKind.Bar
      XField = ""
      YFields = []
      Title = None
      Stacked = false
      XNums = None
      ValueFormat = None
      UnitMode = None
      XTitle = None
      YTitle = None
      Subtitle = None
      LegendPosition = None
      DataLabels = None
      XScale = None
      Annotations = None
      Rows = [] }

/// The case's x cell values, boxed — numeric when `XNums` is set, else the
/// string labels.
let private xCells (case: Case) : obj list =
    match case.XNums with
    | Some ns -> ns |> List.map box
    | None -> case.Rows |> List.map (fst >> box)

// ── Phase 882 — date runs for the temporal cases ─────────────────────────────
//
// A temporal case's x cells are canonical ISO-8601 date STRINGS in the same slot
// a category label occupies, because that is exactly what a `Cell.Date` presents
// to the lowering. They are GENERATED rather than hand-typed: thirty literal
// dates would be thirty chances to typo a run that is supposed to be regular,
// and the generation goes through `Charts.Temporal`'s own calendar arithmetic —
// the same functions the lowering uses, so a case cannot describe a date the
// lowering would read differently. The emitted `.input.json` pins the result, so
// the other hosts still read literal dates.

let private isoOf (day: int) : string =
    let y, m, d = Charts.Temporal.civilFromDays day
    sprintf "%04d-%02d-%02d" y m d

/// `count` dates from `start` (ISO), stepping `stepDays` days.
let private isoRun (start: string) (stepDays: int) (count: int) : string list =
    let d0 = Charts.Temporal.tryParseDay start |> Option.defaultValue 0
    [ for i in 0 .. count - 1 -> isoOf (d0 + i * stepDays) ]

/// `count` MONTH STARTS from `(year, month)`.
let private isoMonths (year: int) (month: int) (count: int) : string list =
    let start = year * 12 + (month - 1)

    [ for i in 0 .. count - 1 ->
          let idx = start + i
          isoOf (Charts.Temporal.daysFromCivil (idx / 12) (idx % 12 + 1) 1) ]

/// `count` YEAR STARTS from `year`.
let private isoYears (year: int) (count: int) : string list =
    [ for i in 0 .. count - 1 -> isoOf (Charts.Temporal.daysFromCivil (year + i) 1 1) ]

/// Pair each x with a value series. One rising-with-a-wobble series keeps every
/// temporal case's DATA uninteresting on purpose — what these fixtures pin is the
/// axis, and a distinctive series would only make the goldens harder to read.
let private seriesOver (xs: string list) : (string * float list) list =
    xs |> List.mapi (fun i x -> x, [ float (1000 + 25 * i + 60 * ((i * 3) % 4)) ])

let private cases: Case list =
    [ { plain with
          Name = "bar-single"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by quarter")
          Stacked = false
          XNums = None
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      { plain with
          Name = "bar-multi"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          Stacked = false
          XNums = None
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          Name = "line-single"
          Kind = ChartKind.Line
          XField = "month"
          YFields = [ "users" ]
          Title = Some(lit "Active users")
          Stacked = false
          XNums = None
          Rows =
              [ "Jan", [ 30.0 ]
                "Feb", [ 45.0 ]
                "Mar", [ 42.0 ]
                "Apr", [ 60.0 ]
                "May", [ 85.0 ] ] }
      { plain with
          Name = "line-multi"
          Kind = ChartKind.Line
          XField = "day"
          YFields = [ "cpu"; "mem" ]
          Title = None
          Stacked = false
          XNums = None
          Rows = [ "Mon", [ 20.0; 55.0 ]; "Tue", [ 35.0; 60.0 ]; "Wed", [ 28.0; 52.0 ] ] }
      // ── Phase 637 — stacked series + the Area arm ──
      { plain with
          Name = "bar-stacked"
          Kind = ChartKind.Bar
          XField = "sprint"
          YFields = [ "done"; "doing"; "blocked" ]
          Title = Some(lit "Tickets by status")
          Stacked = true
          XNums = None
          // A zero cell (S2 blocked) pins the zero-height-segment geometry.
          Rows = [ "S1", [ 12.0; 5.0; 3.0 ]; "S2", [ 18.0; 4.0; 0.0 ]; "S3", [ 9.0; 7.0; 2.0 ] ] }
      { plain with
          Name = "area-single"
          Kind = ChartKind.Area
          XField = "week"
          YFields = [ "signups" ]
          Title = Some(lit "Signups")
          Stacked = false
          XNums = None
          Rows = [ "W1", [ 40.0 ]; "W2", [ 55.0 ]; "W3", [ 48.0 ]; "W4", [ 70.0 ] ] }
      { plain with
          Name = "area-multi"
          Kind = ChartKind.Area
          XField = "month"
          YFields = [ "web"; "mobile" ]
          Title = Some(lit "Traffic")
          Stacked = false
          XNums = None
          Rows = [ "Jan", [ 60.0; 35.0 ]; "Feb", [ 72.0; 44.0 ]; "Mar", [ 65.0; 58.0 ] ] }
      { plain with
          Name = "area-stacked"
          Kind = ChartKind.Area
          XField = "month"
          YFields = [ "web"; "mobile" ]
          Title = Some(lit "Traffic (stacked)")
          Stacked = true
          XNums = None
          Rows = [ "Jan", [ 60.0; 35.0 ]; "Feb", [ 72.0; 44.0 ]; "Mar", [ 65.0; 58.0 ] ] }
      // ── Phase 636 — the Scatter arm (numeric x, linear axis) ──
      { plain with
          Name = "scatter-single"
          Kind = ChartKind.Scatter
          XField = "height"
          YFields = [ "weight" ]
          Title = Some(lit "Height vs weight")
          Stacked = false
          XNums = Some [ 150.0; 162.0; 171.0; 180.0; 195.0 ]
          Rows = [ "", [ 52.0 ]; "", [ 61.0 ]; "", [ 68.5 ]; "", [ 74.0 ]; "", [ 88.0 ] ] }
      { plain with
          Name = "scatter-multi"
          Kind = ChartKind.Scatter
          XField = "delta"
          YFields = [ "gain"; "loss" ]
          Title = None
          Stacked = false
          // Negative + zero-crossing on BOTH axes pins the non-zero-anchored
          // linear x-domain and the shared zero-anchored y-domain.
          XNums = Some [ -10.0; -2.5; 0.0; 4.0; 12.0 ]
          Rows =
              [ "", [ 5.0; -3.0 ]
                "", [ -1.5; 2.0 ]
                "", [ 0.0; 6.0 ]
                "", [ 7.25; -4.5 ]
                "", [ 11.0; 1.0 ] ] }
      // ── Phase 638 — the Pie arm (polar wedges) ──
      { plain with
          Name = "pie-single"
          Kind = ChartKind.Pie
          XField = "holder"
          YFields = [ "share" ]
          Title = Some(lit "Ownership by holder class")
          Stacked = false
          XNums = None
          // Uneven shares (incl. a zero-share category, legended but wedge-less).
          Rows =
              [ "Founders", [ 40.0 ]
                "Series A", [ 30.0 ]
                "Employees", [ 20.0 ]
                "Advisors", [ 10.0 ]
                "Treasury", [ 0.0 ] ] }
      { plain with
          Name = "pie-quarters"
          Kind = ChartKind.Pie
          XField = "region"
          YFields = [ "sales" ]
          Title = None
          Stacked = false
          XNums = None
          // Four equal shares pin the exact quarter-boundary arc geometry.
          Rows = [ "N", [ 25.0 ]; "E", [ 25.0 ]; "S", [ 25.0 ]; "W", [ 25.0 ] ] }
      // ── Phase 876 — axis number formatting ──
      //
      // The twelve cases above all peak below 1 000, so the canonical formatter
      // reproduces their goldens byte-for-byte: no separator is reachable and
      // every step is a whole number. That is the point of these eight — the
      // new behaviour is unobservable in the old corpus, so it needs its own.
      { plain with
          // Thousands separators at the DEFAULT: a step of 2 000 gives 0 dp and
          // the grouping does the rest.
          Name = "bar-thousands"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "units" ]
          Title = Some(lit "Units shipped")
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      { plain with
          // Step-derived DECIMALS: a 0.1 step gives 1 dp on every tick, so the
          // zero tick reads `0.0` — precision follows the axis, not the datum.
          Name = "line-decimals"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "rate" ]
          Title = Some(lit "Conversion rate")
          Rows = [ "W1", [ 0.12 ]; "W2", [ 0.35 ]; "W3", [ 0.28 ]; "W4", [ 0.44 ] ] }
      { plain with
          // Display-unit scaling at the default `Words` mode: short ticks under a
          // single "Millions" label in the axis-unit slot.
          Name = "bar-millions"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by region")
          Rows =
              [ "North", [ 12500000.0 ]
                "South", [ 9800000.0 ]
                "East", [ 15200000.0 ]
                "West", [ 11100000.0 ] ] }
      { plain with
          // `Format.Currency` under the default mode: the symbol stays on every
          // tick (the label says only the magnitude).
          Name = "bar-currency"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue")
          ValueFormat = Some(Format.Currency "GBP")
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      { plain with
          // `WordsWithSymbol` + a currency: the symbol moves INTO the label
          // ("Millions of £") and leaves the ticks, stated once.
          Name = "bar-currency-millions"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by region")
          ValueFormat = Some(Format.Currency "GBP")
          UnitMode = Some "WordsWithSymbol"
          Rows =
              [ "North", [ 12500000.0 ]
                "South", [ 9800000.0 ]
                "East", [ 15200000.0 ]
                "West", [ 11100000.0 ] ] }
      { plain with
          // The SI form of the same axis — "M£".
          Name = "bar-currency-si"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by region")
          ValueFormat = Some(Format.Currency "GBP")
          UnitMode = Some "SIAbbreviation"
          Rows =
              [ "North", [ 12500000.0 ]
                "South", [ 9800000.0 ]
                "East", [ 15200000.0 ]
                "West", [ 11100000.0 ] ] }
      { plain with
          // `Format.Percent` reads a RATIO (the same reading `Binding.Format`
          // gives it) and derives its precision from the ×100 step.
          Name = "line-percent"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "rate" ]
          Title = Some(lit "Conversion rate")
          ValueFormat = Some(Format.Percent None)
          Rows = [ "W1", [ 0.12 ]; "W2", [ 0.35 ]; "W3", [ 0.28 ]; "W4", [ 0.44 ] ] }
      { plain with
          // The opt-in compact mode: the suffix on every tick, no unit label, and
          // it gates at thousands where the label modes gate at millions.
          Name = "bar-compact"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "units" ]
          Title = Some(lit "Units shipped")
          UnitMode = Some "CompactPerTick"
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      // ── Phase 879 — deterministic text metrics ──
      //
      // Six cases, one per decision the pinned advance-width table now makes.
      //
      // Phase 903 RE-GROUNDED the rotation cases, because the ladder replaced
      // the tilt-as-default reading and the old inputs no longer demonstrate
      // what their names claimed. `bar-tilt-five`'s roomy compass labels fit
      // their bands, so they now read FLAT — which is the correction's whole
      // point, so the case was renamed `bar-flat-five` rather than retuned. The
      // boundary pair kept its names and moved its DATA to four categories,
      // where the band pitch is wide enough for all THREE rungs to be one
      // character apart; `bar-flat-boundary` is the new lower step. Every rung
      // is therefore pinned by a single-character difference from its
      // neighbour, which is the property that makes these cases worth having.
      { plain with
          // Legend pitch from name extents: two 29-character series names. On
          // the retired flat 100 px pitch the second swatch landed on top of
          // the first label; on per-entry pitch each entry occupies its own
          // measured width.
          Name = "bar-legend-long-names"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "monthly_recurring_revenue_gbp"; "annual_contract_value_gbp_est" ]
          Title = Some(lit "Long series names")
          Rows = [ "Q1", [ 40.0; 90.0 ]; "Q2", [ 55.0; 80.0 ] ] }
      { plain with
          // Left-margin autosize: `Off` keeps every magnitude, so the ticks
          // read `2,000,000` — nine characters that the fixed 64 px left margin
          // clipped clean through.
          Name = "bar-wide-ticks"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue in full")
          UnitMode = Some "Off"
          Rows =
              [ "Q1", [ 1250000.0 ]
                "Q2", [ 1480000.0 ]
                "Q3", [ 1100000.0 ]
                "Q4", [ 1690000.0 ] ] }
      { plain with
          // FIVE ROOMY CATEGORIES — the operator's own example. The widest
          // label ("Central", 42.77 px) fits comfortably inside the ~109 px band
          // pitch, so since Phase 903 the axis reads FLAT: this is the case that
          // proves a compass axis is not tilted for the sake of tilting. It was
          // `bar-tilt-five` and asserted `-30°` until the correction.
          Name = "bar-flat-five"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales" ]
          Title = Some(lit "Sales by region")
          Rows =
              [ "North", [ 80.0 ]
                "South", [ 130.0 ]
                "East", [ 60.0 ]
                "West", [ 95.0 ]
                "Central", [ 110.0 ] ] }
      { plain with
          // Twenty categories: at a 27 px band pitch nothing fits flat and
          // nothing packs at 30°, so the axis escalates to the terminal rung —
          // which takes one line height per label at any count. Unchanged by
          // Phase 903, and the check that the ladder's top is where it was.
          Name = "bar-vertical-twenty"
          Kind = ChartKind.Bar
          XField = "week"
          YFields = [ "signups" ]
          Title = Some(lit "Signups by week")
          Rows = [ for i in 1..20 -> (sprintf "Wk %d" i), [ 40.0 + float ((i * 7) % 23) ] ] }
      { plain with
          // LADDER STEP 1 — FLAT. Four nineteen-character labels: 19 × 0.55 em ×
          // 13 px = 135.85 px, inside the ~137 px band pitch, so every label
          // fits its own band and the axis stays horizontal. One character
          // shorter than `bar-tilt-boundary`.
          Name = "bar-flat-boundary"
          Kind = ChartKind.Bar
          XField = "code"
          YFields = [ "count" ]
          Title = Some(lit "At the flat-to-tilt boundary")
          Rows = [ for i in 0..3 -> (String.replicate 18 "1" + string i), [ 20.0 + float (i * 9 % 70) ] ] }
      { plain with
          // LADDER STEP 2 — TILT. The SAME chart, one character longer: 20 ×
          // 0.55 em × 13 px = 143.00 px, past the band pitch, so the flat rung
          // fails — and the footprint at 30° is 143.00·cos30 + 15.6·sin30 =
          // 131.64, inside it, so the whole axis rotates to 30°.
          Name = "bar-tilt-boundary"
          Kind = ChartKind.Bar
          XField = "code"
          YFields = [ "count" ]
          Title = Some(lit "At the tilt-to-vertical boundary")
          Rows = [ for i in 0..3 -> (String.replicate 19 "1" + string i), [ 20.0 + float (i * 9 % 70) ] ] }
      { plain with
          // LADDER STEP 3 — VERTICAL. One character longer again: 21 × 0.55 em ×
          // 13 px = 150.15 px, whose 30° footprint is 137.83 — past the same
          // pitch — so the identical chart escalates to the terminal rung.
          Name = "bar-vertical-boundary"
          Kind = ChartKind.Bar
          XField = "code"
          YFields = [ "count" ]
          Title = Some(lit "Past the tilt-to-vertical boundary")
          Rows = [ for i in 0..3 -> (String.replicate 20 "1" + string i), [ 20.0 + float (i * 9 % 70) ] ] }
      // ── Phase 878 — axis titles + subtitle ──
      { plain with
          // ALL THREE SET, on a multi-series chart. Pins the whole feature at
          // once: the explicit x title overriding the field-name fallback, the
          // ROTATED y title in the (now wider) left margin, the muted subtitle
          // under the 18 px title, the top margin growing by exactly that line,
          // and the legend row moving down with it.
          Name = "bar-axis-titles"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          XTitle = Some(lit "Sales region")
          YTitle = Some(lit "Value (£)")
          Subtitle = Some(lit "Rolling twelve months")
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          // NONE SET, with a display unit in play. The complement of the case
          // above: both axes take their capitalised field names (`Quarter` /
          // `Revenue` — never the retired `"Value"` literal), and with no
          // subtitle to defer to, the display-unit slot draws as Phase 876
          // shipped it.
          Name = "bar-axis-titles-default"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue")
          ValueFormat = Some(Format.Currency "GBP")
          UnitMode = Some "WordsWithSymbol"
          Rows =
              [ "Q1", [ 12500000.0 ]
                "Q2", [ 15200000.0 ]
                "Q3", [ 11800000.0 ]
                "Q4", [ 17400000.0 ] ] }
      { plain with
          // THE DEDUPE RULE. Byte-for-byte the case above plus a subtitle that
          // states the unit — so the display-unit slot is SUPPRESSED. The two
          // goldens differ in exactly that label plus the subtitle's own line,
          // which is what makes the rule readable from the corpus alone.
          Name = "bar-subtitle-units"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue")
          ValueFormat = Some(Format.Currency "GBP")
          UnitMode = Some "WordsWithSymbol"
          Subtitle = Some(lit "Millions of £")
          Rows =
              [ "Q1", [ 12500000.0 ]
                "Q2", [ 15200000.0 ]
                "Q3", [ 11800000.0 ]
                "Q4", [ 17400000.0 ] ] }
      { plain with
          // LONG Y TITLE. The rotated title runs along the PLOT HEIGHT, so that
          // — not the left margin's width — is the extent it truncates to. This
          // one comfortably exceeds it and comes back ellipsised, pinning that
          // the bound is the plot height rather than the canvas or the margin.
          Name = "bar-long-y-title"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "A very long axis name")
          YTitle =
              Some(lit "Monthly recurring revenue, net of refunds and credits, in pounds sterling at constant currency")
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      // ── Phase 880 — legend placement ──
      //
      // The DEFAULT arm needs no case of its own: every pre-880 multi-series
      // golden IS the right-hand column now, which is what a default flip
      // means. These pin the arms an author has to ask for, plus the two cases
      // the column arm introduces (the >6-entry chart that the top band could
      // not hold, and the truncation bound).
      { plain with
          // EIGHT SERIES — the case the top band silently drew off-canvas past
          // the sixth entry, and the reason the default moved. The column takes
          // eight rows in 400 px of canvas without touching the plot's height,
          // so nothing is clipped and nothing needs an overflow rule.
          Name = "bar-legend-eight-series"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "alpha"; "beta"; "gamma"; "delta"; "epsilon"; "zeta"; "eta"; "theta" ]
          Title = Some(lit "Eight series, one legend")
          Rows =
              [ "North", [ 80.0; 100.0; 60.0; 45.0; 92.0; 30.0; 71.0; 55.0 ]
                "South", [ 130.0; 110.0; 75.0; 50.0; 64.0; 41.0; 88.0; 62.0 ] ] }
      { plain with
          // THE COLUMN'S BOUND. Two names far past `LegendColumnMaxShare` of
          // the canvas: both come back ellipsised at the same budget, so the
          // column is capped and the plot survives. The contrast is
          // `bar-legend-band-packs` / `-band-overflow-column` below: a BAND
          // never truncates, because its overflow is in the SUM and not in any
          // one name — it falls back to this column instead.
          Name = "bar-legend-column-truncation"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields =
              [ "monthly_recurring_revenue_gbp_constant_currency"
                "monthly_recurring_revenue_gbp_reported_currency" ]
          Title = Some(lit "A column that cannot hold its names")
          Rows = [ "Q1", [ 120.0; 118.0 ]; "Q2", [ 150.0; 147.0 ] ] }
      { plain with
          // TOP — the pre-880 band, now something an author asks for. Byte-for
          // byte `bar-multi` but for the declared position, so the two goldens
          // read as the before-and-after of the default flip.
          Name = "bar-legend-top"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          LegendPosition = Some "Top"
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          // BOTTOM — the band mirrored below the x-axis title, which is what
          // the reserved band pushes UP. Declares an x title so the golden pins
          // that the title rides above the legend rather than under it.
          Name = "bar-legend-bottom"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          XTitle = Some(lit "Sales region")
          LegendPosition = Some "Bottom"
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          // NONE — no legend box, and no space reserved for one either: the
          // plot must be the full width, identical to a single-series chart's.
          Name = "bar-legend-none"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          LegendPosition = Some "None"
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      // ── The BAND overflow rule (operator decision, 2026-08-18) ──
      //
      // A boundary PAIR, minimally different: the same four series, the same
      // rows, the same declared `Top` — and ONE character of difference in the
      // last field name. Below the boundary the band packs and draws as Phase
      // 879 shipped it; one character past it the WHOLE legend falls back to
      // the Phase-880 right-hand column.
      //
      // The arithmetic, so a host porting this can check its own numbers rather
      // than trusting the golden: an entry's band pitch is `LegendLabelOffsetX
      // (15) + width(name) + LegendEntryGap (24)` at the 13 px tick size, and
      // the band's available width is `Width (640) - MarginRight (28) - plotX0
      // (64, unautosized here — the ticks are three characters)` = 548. The four
      // names below pack to 542.23; pluralising the last one adds one
      // DefaultEm character (7.15 px) for 549.38, which is past it. So the pair
      // straddles the predicate by 5.77 px on one side and 1.38 px on the
      // other, and nothing else about the two cases differs.
      { plain with
          // PACKS — 542.23 <= 548. The band, unchanged.
          Name = "bar-legend-band-packs"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "support_revenue"; "training_revenue"; "product_revenue"; "usage_revenue" ]
          Title = Some(lit "Revenue by line")
          LegendPosition = Some "Top"
          Rows = [ "Q1", [ 80.0; 45.0; 130.0; 22.0 ]; "Q2", [ 92.0; 51.0; 118.0; 30.0 ] ] }
      { plain with
          // OVERFLOWS — 549.38 > 548, from the single trailing `s`. The legend
          // is the right-hand COLUMN: the plot shrinks by `legendColumnW`, the
          // top band reserves nothing, and no entry is lost. Names are well
          // inside `LegendColumnMaxShare`, so nothing is ellipsised either —
          // this pins the FALLBACK, not the column's own bound (that is
          // `bar-legend-column-truncation`).
          Name = "bar-legend-band-overflow-column"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "support_revenue"; "training_revenue"; "product_revenue"; "usage_revenues" ]
          Title = Some(lit "Revenue by line")
          LegendPosition = Some "Top"
          Rows = [ "Q1", [ 80.0; 45.0; 130.0; 22.0 ]; "Q2", [ 92.0; 51.0; 118.0; 30.0 ] ] }
      { plain with
          // THE BOTTOM ARM'S FALLBACK, which is NOT redundant with the one
          // above: `Bottom` is the only placement that reserves a band in the
          // MARGIN, pushing the x-axis title up by one line plus its padding
          // (`bar-legend-bottom` pins that), and the fallback must give that
          // back. This case is `bar-legend-band-overflow-column` with ONE key
          // changed — `Bottom` for `Top` — so its golden must come out
          // BYTE-IDENTICAL to that one: a fallen-back band reserves nothing at
          // either edge, and the fallback is uniform across both.
          Name = "bar-legend-band-bottom-overflow-column"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "support_revenue"; "training_revenue"; "product_revenue"; "usage_revenues" ]
          Title = Some(lit "Revenue by line")
          LegendPosition = Some "Bottom"
          Rows = [ "Q1", [ 80.0; 45.0; 130.0; 22.0 ]; "Q2", [ 92.0; 51.0; 118.0; 30.0 ] ] }
      { plain with
          // PIE UNDER AN EXPLICIT TOP. The pie legend was the right-hand column
          // this phase generalised; this is the proof the generalisation went
          // both ways — the polar arm now takes a band like any other, `NN%`
          // labels and all.
          Name = "pie-legend-top"
          Kind = ChartKind.Pie
          XField = "department"
          YFields = [ "share" ]
          Title = Some(lit "Budget share")
          LegendPosition = Some "Top"
          Rows = [ "Ops", [ 40.0 ]; "R&D", [ 35.0 ]; "Sales", [ 25.0 ] ] }
      // ── Phase 881 — selective data labels ──
      //
      // `Off` needs no case: every one of the fifty above IS the `Off` picture,
      // and every one of their goldens is byte-unchanged by this phase, which
      // is the regression guard the whole design rests on. These pin the arms
      // `Ends` reaches, and the pair at the end pins the suppression boundary.
      { plain with
          // GROUPED BARS — every bar's own cap, each label centred on its bar
          // and bounded by the sub-slot pitch that separates it from the next
          // series' label. Byte-for-byte `bar-multi` but for the declared
          // labels, so the two goldens read as the before-and-after.
          Name = "bar-labels-grouped"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some(lit "Sales vs target")
          DataLabels = Some "Ends"
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          // STACKED BARS — the TOTAL at the stack cap and NOTHING else. Three
          // series and three categories, so a per-segment rule would emit nine
          // labels where this emits three; the golden's label count is what
          // makes "interior segments carry no label" checkable from the corpus
          // alone. Byte-for-byte `bar-stacked` but for the declared labels.
          Name = "bar-labels-stacked"
          Kind = ChartKind.Bar
          XField = "sprint"
          YFields = [ "done"; "doing"; "blocked" ]
          Title = Some(lit "Tickets by status")
          Stacked = true
          DataLabels = Some "Ends"
          Rows = [ "S1", [ 12.0; 5.0; 3.0 ]; "S2", [ 18.0; 4.0; 0.0 ]; "S3", [ 9.0; 7.0; 2.0 ] ] }
      { plain with
          // LINE ENDPOINTS — one label per series at the LAST point, right of
          // the endpoint and nudged up off the line. Two series whose final
          // values are far apart in y, so both are admitted and the golden pins
          // the ordinary case rather than the separation rule's.
          Name = "line-labels-ends"
          Kind = ChartKind.Line
          XField = "day"
          YFields = [ "cpu"; "mem" ]
          Title = Some(lit "Load by day")
          DataLabels = Some "Ends"
          Rows = [ "Mon", [ 20.0; 55.0 ]; "Tue", [ 35.0; 60.0 ]; "Wed", [ 28.0; 52.0 ] ] }
      { plain with
          // NEGATIVE CAPS — a zero-crossing domain, so two bars cap upward and
          // two downward, and the golden pins that the below-cap placement is
          // the exact mirror of the above-cap one rather than a second rule.
          Name = "bar-labels-negative"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "variance" ]
          Title = Some(lit "Variance to plan")
          DataLabels = Some "Ends"
          Rows = [ "Q1", [ 12.0 ]; "Q2", [ -8.0 ]; "Q3", [ 5.0 ]; "Q4", [ -14.0 ] ] }
      { plain with
          // DISPLAY-UNIT AGREEMENT — the axis scales to millions, so the labels
          // do too, at the axis's own step-derived precision. The value a label
          // prints is the value the tick column would print at that height; the
          // rounding that follows (12.5 M reading `13`) is the axis's rule, not
          // a second one, and the alternative — a label more precise than its
          // own axis — is what makes a chart disagree with itself.
          Name = "bar-labels-millions"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by region")
          DataLabels = Some "Ends"
          Rows =
              [ "North", [ 12500000.0 ]
                "South", [ 9800000.0 ]
                "East", [ 15200000.0 ]
                "West", [ 11100000.0 ] ] }
      { plain with
          // BOUNDARY, UNDER. `Off` scaling keeps the full magnitudes, so every
          // label is the nine-character `1,250,000` — 4.41 em, 52.92 px at the
          // 12 px label size. Six categories give a 85.85 px band and therefore
          // a 56.09 px width budget, so every label is admitted.
          Name = "bar-labels-fit-boundary"
          Kind = ChartKind.Bar
          XField = "month"
          YFields = [ "revenue" ]
          Title = Some(lit "At the label-fit boundary")
          UnitMode = Some "Off"
          DataLabels = Some "Ends"
          Rows =
              [ "M1", [ 1250000.0 ]
                "M2", [ 1480000.0 ]
                "M3", [ 1100000.0 ]
                "M4", [ 1690000.0 ]
                "M5", [ 1320000.0 ]
                "M6", [ 1550000.0 ] ] }
      { plain with
          // BOUNDARY, OVER. The SAME chart with ONE more category — a value
          // inside the existing domain, so the ticks, the margins and the label
          // texts are all unchanged and the band pitch is the only thing that
          // moves. It moves to 73.58 px, the budget falls under the labels'
          // 52.92 px + padding, and every label is SUPPRESSED. The golden
          // therefore carries no labels at all: suppression is total per
          // placement, deterministic, and visible as an absence rather than as
          // a clipped or overlapped string.
          Name = "bar-labels-suppress-boundary"
          Kind = ChartKind.Bar
          XField = "month"
          YFields = [ "revenue" ]
          Title = Some(lit "Past the label-fit boundary")
          UnitMode = Some "Off"
          DataLabels = Some "Ends"
          Rows =
              [ "M1", [ 1250000.0 ]
                "M2", [ 1480000.0 ]
                "M3", [ 1100000.0 ]
                "M4", [ 1690000.0 ]
                "M5", [ 1320000.0 ]
                "M6", [ 1550000.0 ]
                "M7", [ 1400000.0 ] ] }

      // ── Phase 882 — the temporal x-axis ──
      //
      // THREE GRANULARITY REGIMES first, one per label format, because that is
      // the feature a reader checks by eye: a daily series reads
      // `dd mmm yy`, a monthly one `mmm yy`, a decade `yyyy`. Then the FORMAT
      // BOUNDARIES — four cases pinning the two rungs either side of each
      // threshold, which is what "just above/below 27 and 365 days" means under
      // a nominal-step rule: the ladder's rungs jump, so the boundary is not a
      // span you can approach continuously but the PAIR OF ADJACENT RUNGS the
      // threshold separates. Then the title override, the polar-free arms
      // (scatter, stacked bar) that exercise the value-positioned geometry.
      { plain with
          // DAILY. 30 consecutive days: the day rungs 1 / 2 / 5 give 30 / 15 / 6
          // ticks, so 5 DAYS is the first rung inside the 6-tick ceiling — the
          // nominal 5 is under 27, so the labels are `dd mmm yy`.
          Name = "line-temporal-daily"
          Kind = ChartKind.Line
          XField = "day"
          YFields = [ "sessions" ]
          Title = Some(lit "Sessions by day")
          XScale = Some "Temporal"
          Rows = seriesOver (isoRun "2026-01-05" 1 30) }
      { plain with
          // MONTHLY, on the BAR arm — the arm whose geometry moves furthest,
          // since each bar is centred on its own date rather than in a band. 24
          // month starts span 699 days; the month rungs 1 / 2 / 3 give 24 / 12 /
          // 8, so 6 MONTHS is the first inside the ceiling and the nominal 182.6
          // reads `mmm yy`.
          Name = "bar-temporal-monthly"
          Kind = ChartKind.Bar
          XField = "month"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by month")
          XScale = Some "Temporal"
          Rows = seriesOver (isoMonths 2026 1 24) }
      { plain with
          // YEARLY. Ten year starts span 3287 days; 1 YEAR gives 10 ticks and 2
          // YEARS gives 5, so the nominal 730.5 reads `yyyy` — four characters,
          // which is why a decade axis needs no tilt however many years it runs.
          Name = "line-temporal-yearly"
          Kind = ChartKind.Line
          XField = "year"
          YFields = [ "headcount" ]
          Title = Some(lit "Headcount by year")
          XScale = Some "Temporal"
          Rows = seriesOver (isoYears 2017 10) }
      { plain with
          // 27-DAY THRESHOLD, UNDER. Seven weekly points span 42 days: the 5-day
          // rung gives 9 ticks and the 10-DAY rung gives 5, so the coarsest DAY
          // rung is selected and its nominal 10 stays under 27 — `dd mmm yy`.
          Name = "line-temporal-format-day-boundary"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "orders" ]
          Title = Some(lit "At the day-format boundary")
          XScale = Some "Temporal"
          Rows = seriesOver (isoRun "2026-03-02" 7 7) }
      { plain with
          // 27-DAY THRESHOLD, OVER. Six month starts span 151 days, which puts
          // the 10-day rung at 16 ticks and the 1-MONTH rung at 6 — the first
          // rung past the threshold, nominal 30.436875, so the SAME kind of
          // chart one rung coarser drops the day and reads `mmm yy`.
          Name = "line-temporal-format-month-boundary"
          Kind = ChartKind.Line
          XField = "month"
          YFields = [ "orders" ]
          Title = Some(lit "Past the day-format boundary")
          XScale = Some "Temporal"
          Rows = seriesOver (isoMonths 2026 1 6) }
      { plain with
          // 365-DAY THRESHOLD, UNDER. 30 month starts span 882 days, which the
          // 3-month rung overshoots and the 6-MONTH rung covers in 5 ticks —
          // 182.6, the LAST nominal under 365, so an axis nearly three years
          // wide still reads `mmm yy`.
          Name = "line-temporal-format-halfyear-boundary"
          Kind = ChartKind.Line
          XField = "month"
          YFields = [ "orders" ]
          Title = Some(lit "At the year-format boundary")
          XScale = Some "Temporal"
          Rows = seriesOver (isoMonths 2024 1 30) }
      { plain with
          // 365-DAY THRESHOLD, OVER. Six year starts span 1826 days, which puts
          // the 6-month rung at 11 ticks and the 1-YEAR rung at 6 — the first
          // nominal past 365 (365.2425, a mean Gregorian year), so the month
          // disappears and only `yyyy` remains.
          Name = "line-temporal-format-year-boundary"
          Kind = ChartKind.Line
          XField = "year"
          YFields = [ "orders" ]
          Title = Some(lit "Past the year-format boundary")
          XScale = Some "Temporal"
          Rows = seriesOver (isoYears 2021 6) }
      { plain with
          // TITLE OVERRIDE (§4e, wired by 882). Every other temporal case omits
          // `xTitle` and therefore draws NO x title — the date-axis suppression.
          // This one declares it, and it draws: the rule suppresses the machine's
          // FALLBACK, never the author's own words.
          Name = "bar-temporal-x-title"
          Kind = ChartKind.Bar
          XField = "month"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by month")
          XTitle = Some(lit "Reporting month")
          XScale = Some "Temporal"
          Rows = seriesOver (isoMonths 2026 1 12) }
      { plain with
          // TEMPORAL + SCATTER — both axes continuous, which is the case that
          // proves `isContinuousX` is a property and not an alias for Scatter:
          // the x is read as DATES (not numerically), the marks are points, and
          // the ticks are calendar-aligned.
          Name = "scatter-temporal"
          Kind = ChartKind.Scatter
          XField = "day"
          YFields = [ "latency" ]
          Title = Some(lit "Latency over time")
          XScale = Some "Temporal"
          Rows = seriesOver (isoRun "2026-02-01" 3 12) }
      { plain with
          // TEMPORAL + STACKED BAR — the stacked arm's own slot arithmetic over
          // value-positioned bars, so `slotOriginX` is pinned on both bar arms
          // rather than only the grouped one.
          Name = "bar-temporal-stacked"
          Kind = ChartKind.Bar
          XField = "month"
          YFields = [ "direct"; "partner" ]
          Title = Some(lit "Revenue by channel")
          Stacked = true
          XScale = Some "Temporal"
          Rows =
              isoMonths 2026 1 8
              |> List.mapi (fun i x -> x, [ float (600 + 20 * i); float (300 + 35 * ((i * 3) % 5)) ]) }
      { plain with
          // THE LADDER, ON A TEMPORAL AXIS. Everything above rests FLAT, and that
          // is not an accident of the data: the 6-tick ceiling plus a 9-character
          // `dd mmm yy` label means the pitch is comfortable at the shipped
          // canvas, so the ladder's escalation is REACHABLE only when something
          // else has taken the width — here a wide unscaled y-tick column
          // (`Off` display units on millions) plus a `Right` legend for two
          // long-named series. §4g's arithmetic then applies unchanged: below a
          // ~58 px pitch the 30° window is empty, so the axis steps straight
          // from flat to VERTICAL. Pinning it is what makes "the ladder governs
          // the tick labels too" a checked claim rather than a stated one.
          Name = "line-temporal-vertical-labels"
          Kind = ChartKind.Line
          XField = "day"
          YFields = [ "metropolitan northern region"; "outlying southern region" ]
          Title = Some(lit "A crowded date axis")
          UnitMode = Some "Off"
          XScale = Some "Temporal"
          Rows =
              isoRun "2026-01-05" 1 30
              |> List.mapi (fun i x -> x, [ float (1250000 + 25000 * i); float (980000 + 31000 * i) ]) }
      // ── The formatter's rule-5 regimes — the two cases that reach them ──
      //
      // No other `chart-lowering/*` fixture has a magnitude anywhere near the
      // notation switch, which is exactly why the Phase 876 wave shipped a
      // formatter that grouped an EXPONENT form and the byte-parity corpus
      // certified it green: a corpus can only pin the values it exercises.
      //
      // TWO cases, because the two regimes cannot share an axis. Above ~1e18 a
      // grouped tick is wider than the 30 %-of-canvas left-margin ceiling and
      // TRUNCATES to `…`, and the `r2` geometry rounding (`⌊x·100+½⌋/100`)
      // stops being exact once `x·100` passes 2^53, so a 1e21 y-domain would
      // pin a truncated `999,999,999,999,999,90…` — deterministic and
      // cross-host identical, but pinning the margin rule rather than the
      // formatter. So the BAR case takes the axis regime it can print in full,
      // and the ≥1e21 regime rides a PIE, which has no value axis to truncate
      // and whose wedge tips carry the raw datum untouched by `r2`.
      { plain with
          // `axisUnitMode = Off` is load-bearing. Every other mode scales the
          // axis into its display unit first (`Quadrillions` is the table's top
          // rung, so even a 1e21 axis reads in the low thousands), and a scaled
          // axis can never print a raw magnitude — so `Off` is the ONLY axis
          // regime that reaches rule 5. The tips reach it regardless: Phase
          // 883's decision 1 prints them unscaled, which is what made this
          // wrong-in-practice rather than wrong-in-theory.
          //
          //   1.5e15 — above `formatNum`'s int64 fast-path bound but BELOW the
          //            notation switch. Every host was already correct here and
          //            its bytes must not move; this datum is the control that
          //            says so, and the reason the bundle's "at or above 1e15"
          //            framing is a magnitude too low.
          //   9e16   — the last regime still positional on every host.
          //   1.8e17
          //   2.5e17 — past the .NET `"R"` switch (the Fable, Python and Rust
          //            hosts mirror it). Pre-fix these read `1.8E,+17` /
          //            `2.5E,+17` on four hosts while the TypeScript host —
          //            positional to 1e21 — read them correctly. Divergence,
          //            not merely ugliness.
          Name = "bar-huge-magnitudes"
          Kind = ChartKind.Bar
          XField = "instrument"
          YFields = [ "notional" ]
          Title = Some(lit "Notional outstanding")
          UnitMode = Some "Off"
          Rows =
              [ "Swaps", [ 1.5e15 ]
                "Options", [ 9.0e16 ]
                "Futures", [ 1.8e17 ]
                "Forwards", [ 2.5e17 ] ] }
      { plain with
          // The regime past JavaScript's own switch, where pre-fix ALL FOUR
          // hosts were wrong AND still disagreed with each other — `1E,+21`
          // against `1e,+21`, the exponent letter's CASE being the whole of the
          // difference. A pie has no value axis, so nothing here is truncated
          // and nothing goes through `r2`: the wedge tip prints `pieValues.[i]`
          // exactly as the row carried it.
          //
          //   5e20    — still positional in JavaScript, already scientific in
          //             the .NET layout: the divergence, isolated.
          //   1e21    — a single mantissa digit, zero-padded to 22 places.
          //   2.25e21
          //   3.75e21 — a MULTI-digit mantissa, so the padding is pinned as
          //             `exp + 1 − digits` and not as a fixed count.
          Name = "pie-huge-magnitudes"
          Kind = ChartKind.Pie
          XField = "instrument"
          YFields = [ "notional" ]
          Title = Some(lit "Notional by instrument")
          UnitMode = Some "Off"
          Rows =
              [ "Swaps", [ 5.0e20 ]
                "Options", [ 1.0e21 ]
                "Futures", [ 2.25e21 ]
                "Forwards", [ 3.75e21 ] ] }
      // ── Phase 921 — the accessible summary's two variable-length arms ──
      //
      // Every other grammar arm is already covered by the cases above: the kind
      // words by one fixture per kind, the temporal extent by the four
      // `*-temporal-*` cases (which state their domain in the 882 label format),
      // the display-unit suffix by `bar-millions` / `bar-currency-millions` /
      // `bar-compact`, the un-folded series list by the three four-series legend
      // cases and the folded one by `bar-legend-eight-series`. These two pin
      // what nothing else does.
      { plain with
          // THE FOLDING BOUNDARY. Four series are named in full (the legend
          // cases above); FIVE is the first count that folds, and it folds to
          // exactly "and 1 more" — the singular the count arithmetic produces
          // rather than a special case, which is worth pinning precisely
          // because a one-off `n − 4` bug reads perfectly at every other count.
          Name = "bar-summary-fold-boundary"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "alpha"; "beta"; "gamma"; "delta"; "epsilon" ]
          Title = Some(lit "Five series, one folded")
          Rows =
              [ "North", [ 80.0; 100.0; 60.0; 45.0; 92.0 ]
                "South", [ 130.0; 110.0; 75.0; 50.0; 64.0 ] ] }
      { plain with
          // HOSTILE + OVERLONG TEXT. The summary is built from series-field and
          // category strings taken straight off the data feed, so it carries
          // exactly the untrusted-string exposure Phase 883's `<title>` does.
          // Two properties in one case: markup metacharacters survive into the
          // `Description` as DATA (the renderer's XML escape is what makes them
          // inert — pinned on the render side), and a name past the 32-character
          // cap comes back clamped with the ellipsis, in both the series list
          // and the peak clause.
          Name = "bar-summary-hostile-text"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "monthly_recurring_revenue_in_pounds_sterling" ]
          Title = Some(lit "Hostile labels")
          Rows =
              [ "<script>alert('x')</script>", [ 80.0 ]
                "North & \"South\"", [ 130.0 ]
                "a region name comfortably past the clamp", [ 60.0 ] ] }
      { plain with
          // Phase 1143 — THE NON-LITERAL TEXT ARMS, and the fixture whose
          // absence let two hosts drop them for three phases while the family
          // was otherwise at byte parity. All four `TextSource`-typed fields
          // carry a non-`Literal` arm at once, so the golden pins four separate
          // things a host can get wrong:
          //
          //   * the visible title and the drawing's a11y `Title` carry the
          //     `Bound` arm THROUGH — a host that resolves here bakes live
          //     binding state into a shared golden, and one that drops it
          //     leaves the chart untitled;
          //   * the axis titles carry their arms through and the capitalised
          //     field-name FALLBACK does not stand in — the failure that reads
          //     as an ordinary chart, because "Region" and "Sales" look like
          //     titles somebody chose;
          //   * the y title is long enough that a `Literal` of it would be
          //     truncated against the plot height (compare `bar-long-y-title`),
          //     so the golden shows it whole and clause 4 is pinned by a
          //     measurable difference rather than by assertion;
          //   * the subtitle is present, so the display-unit slot is suppressed
          //     and the top margin reserves its line — the PRESENCE rules,
          //     holding on an arm whose text nothing here can read.
          //
          // The generated summary carries no title on any arm, which this case
          // pins for the arms that would otherwise be most tempting to fold in.
          Name = "bar-bound-i18n-titles"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales" ]
          Title = Some(TextSource.Bound(Binding.Static(Some "Revenue, from the live binding")))
          XTitle = Some(TextSource.I18n("chart.axis.region", Map.empty))
          YTitle =
              Some(
                  TextSource.Bound(
                      Binding.Static(Some "Monthly recurring revenue, net of refunds and credits, in pounds sterling")
                  )
              )
          Subtitle = Some(TextSource.I18n("chart.subtitle.rolling_twelve_months", Map.empty))
          Rows = [ "North", [ 80.0 ]; "South", [ 130.0 ]; "East", [ 60.0 ] ] }
      // ── Phase 1490 (§4l) — the data-addressed annotations, first member ──
      //
      // Four cases, each pinning ONE fact the others cannot, which is why the
      // family needs four fixtures rather than one chart carrying everything.
      { plain with
          // The BARE line, and the DOMAIN-NEUTRAL case: a zero line over
          // mixed-sign data. The value 0.0 is already inside every chart's
          // zero-anchored domain, so this golden shows the line, the mark id and
          // the draw order with the domain held still — the control the two
          // below are read against.
          //
          // No label, so it also pins that an annotation with no label emits ONE
          // shape and no empty `Shape.Label` — the absence is a shape that is
          // not there, which is the only form of it a byte comparison can see.
          Name = "line-reference-zero"
          Kind = ChartKind.Line
          XField = "month"
          YFields = [ "netFlow" ]
          Title = Some(lit "Net flow")
          Annotations = Some [ ChartAnnotation.ReferenceLine(0.0, None) ]
          Rows =
              [ "Jan", [ 40.0 ]
                "Feb", [ -25.0 ]
                "Mar", [ 15.0 ]
                "Apr", [ -60.0 ]
                "May", [ 35.0 ] ] }
      { plain with
          // DOMAIN WIDENING, and the reason §4l rule 3 is a rule. Every bar is
          // below 200 and the target is 260, so a lowering that clamped the line
          // to the data's own domain would draw it ON the top gridline — a
          // picture that says the target has been met. The golden's y ticks run
          // past the tallest bar, which is exactly the difference: the axis says
          // so, and the reader can see the gap.
          //
          // Unlabelled, deliberately: this fixture's job is the domain, and a
          // label would put the fit gate's outcome into the same bytes.
          Name = "bar-reference-target-above-data"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue against target")
          Annotations = Some [ ChartAnnotation.ReferenceLine(260.0, None) ]
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      { plain with
          // THE LABEL, on the `Literal` arm — the one arm whose glyphs the
          // lowering can measure, so the one arm the fit gate can refuse. Two
          // annotations, so the golden also pins the per-case ordinal in the
          // mark ids (`annotation|reference|0` and `|1`) and the rule that every
          // label is painted LAST, after both lines rather than each after its
          // own.
          Name = "line-reference-labelled"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "latencyMs" ]
          Title = Some(lit "p95 latency")
          Annotations =
              Some
                  [ ChartAnnotation.ReferenceLine(250.0, Some(lit "SLO"))
                    ChartAnnotation.ReferenceLine(350.0, Some(lit "Breach")) ]
          Rows = [ "W1", [ 180.0 ]; "W2", [ 240.0 ]; "W3", [ 210.0 ]; "W4", [ 300.0 ] ] }
      { plain with
          // THE NON-LITERAL ARM, and the boundary the text contract draws. The
          // label is `Bound`, so its glyphs are unknowable at lowering time: it
          // is carried through UNRESOLVED (contract clause 1) and admitted on
          // PRESENCE (clause 3) rather than fit-gated, because measuring text
          // that is not the text drawn is silently wrong — the same honest
          // boundary clause 4 draws for truncation.
          //
          // It is the twin of `bar-bound-i18n-titles` at a new slot, and it
          // exists for that fixture's recorded reason: the failure mode is a
          // host resolving or dropping the arm, and both look like an ordinary
          // chart until two hosts compare bytes.
          Name = "line-reference-labelled-bound-label"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "latencyMs" ]
          Title = Some(lit "p95 latency")
          Annotations =
              Some
                  [ ChartAnnotation.ReferenceLine(
                        250.0,
                        Some(TextSource.Bound(Binding.Static(Some "the SLO, from the live binding")))
                    ) ]
          Rows = [ "W1", [ 180.0 ]; "W2", [ 240.0 ]; "W3", [ 210.0 ]; "W4", [ 300.0 ] ] }
      // ── Phase 1491 (§4l) — the event marker, the family's second member ──
      //
      // Three cases, one per fact the others cannot carry: the BAND address, the
      // TEMPORAL address at the shape the member was demanded for, and the
      // COLLISION rule that is why this member earned its own phase.
      { plain with
          // THE BAND ADDRESS, and Phase 903's split applied to an address rather
          // than to a datum. The marker sits at Q3's band CENTRE — the same place
          // Q3's own label sits — because a band has an extent and not a
          // position, so its centre is the only x that means "Q3" rather than
          // "the edge between Q2 and Q3".
          //
          // One marker, labelled, on the plainest chart in the corpus: this
          // golden's job is the placement, the full-height line, the mark id and
          // the draw order, and a second marker would put the collision gate's
          // outcome into the same bytes.
          Name = "bar-event-single"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by quarter")
          Annotations = Some [ ChartAnnotation.EventMarker(ChartAnnotationX.Category "Q3", Some(lit "Repricing")) ]
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      { plain with
          // THE SHAPE THE MEMBER WAS DEMANDED FOR — five shocks over a
          // thirty-year series, which is the surveyed article's chart and the
          // recorded demand behind this phase. It is also where §4l rule 3 shows
          // on the X axis: every marker's date joins the extent BEFORE the
          // calendar rung is chosen, so the axis is the axis of the picture that
          // is drawn rather than of the data alone.
          //
          // THREE OF THE FIVE LABELS SURVIVE, and that was measured rather than
          // intended: the first three events are years apart and their labels
          // fit, while 2020 and 2022 are thirty months apart on a thirty-year
          // axis — about sixty pixels — and "Pandemic" and "Mini-budget" do not.
          // Left as it fell, because it is the more useful fixture: the gate
          // bites at DECADE scale on real spacing, not only at the day scale the
          // case below exercises deliberately, and all five markers still draw.
          // `bar-event-single` above is the control where a label fits.
          Name = "line-temporal-events-five"
          Kind = ChartKind.Line
          XField = "year"
          YFields = [ "output" ]
          Title = Some(lit "Output, 1996–2025")
          XScale = Some "Temporal"
          Annotations =
              Some
                  [ ChartAnnotation.EventMarker(ChartAnnotationX.Date "2000-03-10", Some(lit "Dot-com"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2008-09-15", Some(lit "GFC"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2016-06-23", Some(lit "Brexit"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2020-03-11", Some(lit "Pandemic"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2022-09-23", Some(lit "Mini-budget")) ]
          Rows = seriesOver (isoYears 1996 30) }
      { plain with
          // THE COLLISION RULE, which is why the event marker is its own phase
          // rather than a second case on 1490's. Three markers within four days
          // of a thirty-day span: each label's width budget runs to its
          // NEIGHBOUR's line, so the first two have a few pixels and are
          // SUPPRESSED, and the last runs to the plot edge and is drawn.
          //
          // What that pins is the half of Phase 881's rule the reference line
          // could not exercise: never clipped, never overlapped, never nudged
          // across another marker — and a suppressed label does not suppress its
          // marker, so all three lines are in the bytes with one label among
          // them.
          Name = "line-temporal-events-collide"
          Kind = ChartKind.Line
          XField = "day"
          YFields = [ "sessions" ]
          Title = Some(lit "Sessions by day")
          XScale = Some "Temporal"
          Annotations =
              Some
                  [ ChartAnnotation.EventMarker(ChartAnnotationX.Date "2026-01-10", Some(lit "First review window"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2026-01-12", Some(lit "Second review window"))
                    ChartAnnotation.EventMarker(ChartAnnotationX.Date "2026-01-14", Some(lit "Sign-off")) ]
          Rows = seriesOver (isoRun "2026-01-05" 1 30) }
      // ── Phase 1492 (§4l) — the range band, the family's third member ──
      //
      // Three cases, one per fact the others cannot carry: the VALUE-axis pair
      // with its domain widening, the TEMPORAL x pair at the shape the member
      // was demanded for, and the CATEGORY x pair, which is the only place
      // Phase 903's BOUNDARIES (rather than its centres) are exercised.
      //
      // All three pin the half of §4l's draw order no earlier member could: the
      // band's rectangle is the FIRST shape in the golden, before the gridlines.
      // A host that emitted it after its series would produce a valid document
      // showing a different picture, and only these bytes can see that.
      { plain with
          // THE VALUE PAIR, and §4l rule 3 at BOTH ends. The tolerance runs
          // 200–260 over latencies that peak at 300 and bottom at 180, so the
          // band is interior at the top and the axis is unmoved — the control
          // for the widening the `to` end would force if it ran past the data.
          //
          // Labelled, and the label fits: a value band spans the plot's whole
          // width, so its horizontal budget is the widest any annotation label
          // gets. That is the control for the two below, where it does not.
          Name = "line-band-y-tolerance"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "latencyMs" ]
          Title = Some(lit "p95 latency")
          Annotations =
              Some [ ChartAnnotation.RangeBand(ChartAnnotationRange.ValueRange(200.0, 260.0), Some(lit "Tolerance")) ]
          Rows = [ "W1", [ 180.0 ]; "W2", [ 240.0 ]; "W3", [ 210.0 ]; "W4", [ 300.0 ] ] }
      { plain with
          // THE TEMPORAL PAIR — a recession over a decade series, which is the
          // recorded demand (kc-009's `range_band` sighting). Both dates enter
          // the extent before the calendar rung is chosen, exactly as an event
          // marker's one date does; here it matters more, because a band
          // truncated at the plot edge reads as a recession ENDING there.
          //
          // The label is deliberately longer than the band is wide — eighteen
          // months on a fifteen-year axis — so the golden pins the SUPPRESSION
          // this member's gate performs where the reference line's never could:
          // the budget is the band's own width, not the plot's, and the band
          // still draws with no label at all.
          Name = "line-temporal-band-x-period"
          Kind = ChartKind.Line
          XField = "year"
          YFields = [ "output" ]
          Title = Some(lit "Output, 2004–2018")
          XScale = Some "Temporal"
          Annotations =
              Some
                  [ ChartAnnotation.RangeBand(
                        ChartAnnotationRange.XRange(
                            ChartAnnotationX.Date "2008-04-01",
                            ChartAnnotationX.Date "2009-06-30"
                        ),
                        Some(lit "Recession")
                    ) ]
          Rows = seriesOver (isoYears 2004 15) }
      { plain with
          // THE CATEGORY PAIR, and the one case that exercises Phase 903's
          // BOUNDARIES. The band runs Q2–Q3, so its left edge is the boundary
          // BEFORE Q2 and its right edge the boundary AFTER Q3 — a whole two
          // quarters of the axis. An event marker at Q3 sits at that band's
          // CENTRE, and the difference is the whole reason both members exist:
          // a marker is a position, a band is an extent.
          //
          // Two annotations of DIFFERENT cases, so the golden also pins that the
          // per-case ordinals are independent (`annotation|band|0` beside
          // `annotation|reference|0`) and that the band is painted FIRST while
          // the reference line is painted in front of the series — one document,
          // both halves of the draw order.
          Name = "bar-band-x-categories"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue by quarter")
          Annotations =
              Some
                  [ ChartAnnotation.RangeBand(
                        ChartAnnotationRange.XRange(ChartAnnotationX.Category "Q2", ChartAnnotationX.Category "Q3"),
                        Some(lit "Freeze")
                    )
                    ChartAnnotation.ReferenceLine(160.0, None) ]
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      // ── Phase 1494 (§4i) — an annotation LABEL as untrusted text ──
      { plain with
          // The `bar-summary-hostile-text` shape at the slot 1494 opened. That
          // fixture pins the exposure the summary carries through the SERIES
          // and CATEGORY strings; this one pins it through an annotation
          // LABEL, which is a different path into the same string: the label
          // is authored rather than fed, but it is authored by whoever authors
          // the tree, so the summary's guarantee has to be the same one.
          //
          // Three properties in one case, and each needs the other two to be
          // legible:
          //
          //   * markup metacharacters survive into `Description` as DATA —
          //     the renderer's XML escape is what makes them inert, and doing
          //     it here would double-escape;
          //   * the 32-character clamp bites on the LABEL as it does on a
          //     series name, so an overlong label cannot be the whole summary;
          //   * the label carries the `", "` the item list is joined with,
          //     which is why the summary brackets a label rather than
          //     appending it — the brackets are the delimiter, not decoration.
          //
          // The label FITS, deliberately: the drawn label and the announced one
          // are then the same authored string at two different lengths, so the
          // golden shows the clamp as a difference between two bytes it holds
          // rather than as an assertion about one. `line-temporal-events-collide`
          // is where the other half — a SUPPRESSED label still announced — is
          // pinned, because that is the fixture whose gate actually bites.
          Name = "bar-annotation-hostile-text"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some(lit "Revenue against target")
          Annotations =
              Some
                  [ ChartAnnotation.ReferenceLine(
                        160.0,
                        Some(lit "<b>target</b> & \"stretch\", a label comfortably past the clamp")
                    ) ]
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] } ]

/// Build the typed `Row` rows (the canonical embedded-data shape; fuaran#665
/// named the slot — the representation is the same `Map<string,obj>`).
let private buildRows (case: Case) : Row list =
    List.zip (xCells case) (case.Rows |> List.map snd)
    |> List.map (fun (x, ys) ->
        let fields =
            (case.XField, x)
            :: (List.zip case.YFields ys |> List.map (fun (f, v) -> f, box v))

        Map.ofList fields)

/// The neutral input contract's `legendPosition` string → the wire value. The
/// one place the mapping is written down on this host; the other hosts mirror
/// it. Unlike `axisUnitMode` this IS a wire field, so the strings are the
/// canonical enum names, not a harness convention.
let private legendPositionOf (name: string) : ChartLegendPosition =
    match name with
    | "Top" -> ChartLegendPosition.Top
    | "Bottom" -> ChartLegendPosition.Bottom
    | "None" -> ChartLegendPosition.None
    | _ -> ChartLegendPosition.Right

/// The neutral input contract's `dataLabels` string → the wire value. Two
/// values, and the harness cannot spell a third because the vocabulary has
/// none.
let private dataLabelsOf (name: string) : ChartDataLabels =
    match name with
    | "Ends" -> ChartDataLabels.Ends
    | _ -> ChartDataLabels.Off

/// The neutral input contract's `xScale` string → the wire value (Phase 882).
/// Two values, and an unrecognised string reads as `Category` — the default —
/// so a harness typo degrades to today's behaviour rather than to a wrong axis.
let private xScaleOf (name: string) : ChartXScale =
    match name with
    | "Temporal" -> ChartXScale.Temporal
    | _ -> ChartXScale.Category

let private specOf (case: Case) : ChartSpec<obj> =
    { Defaults.chart with
        Source = Binding.Static(Some(Seq.ofList (buildRows case)))
        Kind = case.Kind
        XField = case.XField
        YFields = case.YFields
        Title = case.Title
        ValueFormat = case.ValueFormat
        XTitle = case.XTitle
        YTitle = case.YTitle
        Subtitle = case.Subtitle
        LegendPosition = case.LegendPosition |> Option.map legendPositionOf
        DataLabels = case.DataLabels |> Option.map dataLabelsOf
        XScale = case.XScale |> Option.map xScaleOf
        Annotations = case.Annotations
        Stacked = case.Stacked }

/// The neutral input contract's `axisUnitMode` string → the mode. The one place
/// the mapping is written down on this host; the other hosts mirror it.
let private unitModeOf (name: string) : Charts.ChartAxisUnitMode =
    match name with
    | "WordsWithSymbol" -> Charts.ChartAxisUnitMode.WordsWithSymbol
    | "SIAbbreviation" -> Charts.ChartAxisUnitMode.SIAbbreviation
    | "CompactPerTick" -> Charts.ChartAxisUnitMode.CompactPerTick
    | "Off" -> Charts.ChartAxisUnitMode.Off
    | _ -> Charts.ChartAxisUnitMode.Words

/// The style a case lowers under — `ChartStyle.defaults` unless the case names
/// an axis-unit mode (which is a STYLE choice, never a wire field).
let private styleOf (case: Case) : Charts.ChartStyle =
    match case.UnitMode with
    | None -> Charts.ChartStyle.defaults
    | Some m ->
        { Charts.ChartStyle.defaults with
            AxisUnitMode = unitModeOf m }

/// Lower + wrap in a Drawing node + encode to canonical wire JSON.
let private loweredJson (case: Case) : string =
    let ds =
        Charts.lowerWithStyle Charts.ChartLimits.defaults (styleOf case) (specOf case) (Seq.ofList (buildRows case))

    let node: Node<obj> = Fuaran.drawingSpec (sprintf "chart-%s" case.Name) ds
    CanonicalJson.encodeNode node

/// Lower a NAMED corpus case under its own style — the shape the Phase-876
/// rule tests read.
let private loweredCase (name: string) : DrawingSpec =
    let case = cases |> List.find (fun c -> c.Name = name)
    Charts.lowerWithStyle Charts.ChartLimits.defaults (styleOf case) (specOf case) (Seq.ofList (buildRows case))

// ── Phase 876 readers — pull the axis strings back out of a lowered drawing ──
//
// The y tick labels are the only UNROTATED End-anchored labels the cartesian
// arms emit — Phase 879's tilted category labels are End-anchored too, and
// carry a `Rotation`, which is what separates them here. The axis-unit slot is
// the only Start-anchored, full-strength, Normal-weight one (the visible title
// is Loud, the legend labels carry an opacity).

let private yTickTexts (ds: DrawingSpec) : string list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, s) when
            s.TextAnchor = Some TextAnchor.End && Option.isNone s.Rotation
            ->
            Some t
        | _ -> None)

let private axisUnitLabel (ds: DrawingSpec) : string =
    ds.Shapes
    |> List.tryPick (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, s) when
            s.TextAnchor = Some TextAnchor.Start
            && Option.isNone s.Opacity
            && s.Emphasis = Some Emphasis.Normal
            ->
            Some t
        | _ -> None)
    |> Option.defaultValue ""

// ── Phase 878 readers — pull the axis titles + subtitle back out ─────────────
//
// Each keys off the discriminator the LOWERING used to place the shape, so a
// reader cannot drift into finding the wrong label. Axis titles are the only
// FULL-STRENGTH (no opacity) `Normal`-weight labels: the visible chart title is
// `Loud`, and every chrome label — ticks, categories, legend, subtitle —
// carries `LabelOpacity`. Within that set, the y title is the rotated one.

let private literalTexts (ds: DrawingSpec) : string list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, _) -> Some t
        | _ -> None)

let private fullStrengthTitle (rotated: bool) (ds: DrawingSpec) : string =
    ds.Shapes
    |> List.tryPick (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, s) when
            Option.isNone s.Opacity
            && s.Emphasis = Some Emphasis.Normal
            && s.TextAnchor = Some TextAnchor.Middle
            && Option.isSome s.Rotation = rotated
            ->
            Some t
        | _ -> None)
    |> Option.defaultValue ""

let private xAxisTitleOf (ds: DrawingSpec) : string = fullStrengthTitle false ds

let private yAxisTitleOf (ds: DrawingSpec) : string = fullStrengthTitle true ds

let private yAxisTitleRotation (ds: DrawingSpec) : float option =
    ds.Shapes
    |> List.tryPick (fun sh ->
        match sh with
        | Shape.Label(_, _, _, s) when Option.isNone s.Opacity && Option.isSome s.Rotation -> s.Rotation
        | _ -> None)

// ── Phase 880 readers — pull the legend + the plot rectangle back out ────────
//
// The SWATCH is the discriminator: a rounded-corner `Rectangle` is the legend's
// and nothing else's on either arm (bars and stack segments carry no corner
// radius), so these readers cannot drift onto series geometry. Order is emission
// order, which is entry order.

/// ── Phase 881 reader — pull the data labels back out ──────────────────────
///
/// The FONT SIZE is the discriminator: `DataLabelFontSize` (12) is used by no
/// other shape the lowering emits — ticks, categories, legend rows and axis
/// titles are all `TickFontSize` (13), the subtitle 13, the visible title 18 —
/// so this reader cannot drift onto chrome. Order is emission order.
/// Phase 1490 narrowed this reader. `DataLabelFontSize` was the whole
/// discriminator while it was the only 12px label in a lowered chart; an
/// annotation label is 12px too — deliberately, since both sit INSIDE the plot
/// and are the same size of thing — so size alone would now find annotation
/// labels as well. `Emphasis` separates them: a data label is `Normal` and an
/// annotation label is `Quiet`, which is the emitted difference the lowering
/// makes for exactly this reason.
let private dataLabelsOfDrawing (ds: DrawingSpec) : (float * float * string) list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Label(x, y, TextSource.Literal t, s) when s.FontSize = Some 12.0 && s.Emphasis = Some Emphasis.Normal ->
            Some(x, y, t)
        | _ -> None)

let private dataLabelTextsOf (name: string) : string list =
    dataLabelsOfDrawing (loweredCase name) |> List.map (fun (_, _, t) -> t)

let private legendSwatches (ds: DrawingSpec) : (float * float) list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Rectangle(x, y, _, _, Some _, _) -> Some(x, y)
        | _ -> None)

/// The legend's label texts. Every legend label is `Start`-anchored, muted, and
/// unrotated — which separates them from tick labels (`End`), category labels
/// (rotated), and the axis titles (full strength). The display-unit slot is the
/// one other `Start`-anchored label and carries NO opacity, so it is excluded.
///
/// Phase 1490 adds the `Emphasis` clause, which excludes an annotation label
/// (`Quiet`). It does NOT make the reader exact: a Phase-881 endpoint data label
/// is `Start`-anchored, muted, unrotated and `Normal` too, so a case declaring
/// `Ends` would still be over-collected here. No caller does, and closing that
/// is Phase 881's gap rather than this one's — recorded so the next reader knows
/// the boundary rather than rediscovering it.
let private legendTextsOf (ds: DrawingSpec) : string list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, s) when
            s.TextAnchor = Some TextAnchor.Start
            && Option.isSome s.Opacity
            && Option.isNone s.Rotation
            && s.Emphasis = Some Emphasis.Normal
            ->
            Some t
        | _ -> None)

/// The plot rectangle's right / bottom edges, read off the axis + gridline
/// geometry: the horizontal rules span the plot's width and sit at its ticks.
let private plotRight (ds: DrawingSpec) : float =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Line(_, y1, x2, y2, _) when y1 = y2 -> Some x2
        | _ -> None)
    |> List.max

let private plotBottom (ds: DrawingSpec) : float =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Line(_, y1, _, y2, _) when y1 = y2 -> Some y1
        | _ -> None)
    |> List.max

/// The x-axis title's baseline — the only full-strength, unrotated,
/// `Middle`-anchored label (the visible chart title is `Loud`).
let private xAxisTitleY (ds: DrawingSpec) : float =
    ds.Shapes
    |> List.pick (fun sh ->
        match sh with
        | Shape.Label(_, y, _, s) when
            Option.isNone s.Opacity
            && s.Emphasis = Some Emphasis.Normal
            && s.TextAnchor = Some TextAnchor.Middle
            && Option.isNone s.Rotation
            ->
            Some y
        | _ -> None)

/// The neutral input contract (for the Phase 527 cross-host hosts).
let private inputJson (case: Case) : string =
    let esc (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let num (v: float) = DrawingSvg.formatNum v // canonical number form (whole → no decimal)

    let yFields =
        case.YFields |> List.map (fun f -> "\"" + esc f + "\"") |> String.concat ","

    let rowsJson =
        case.Rows
        |> List.mapi (fun i (x, ys) ->
            let xCell =
                match case.XNums with
                | Some ns -> "\"" + esc case.XField + "\":" + num (List.item i ns)
                | None -> "\"" + esc case.XField + "\":\"" + esc x + "\""

            let cells =
                xCell
                :: (List.zip case.YFields ys
                    |> List.map (fun (f, v) -> "\"" + esc f + "\":" + num v))

            "{" + String.concat "," cells + "}")
        |> String.concat ","

    let kind =
        match case.Kind with
        | ChartKind.Bar -> "Bar"
        | ChartKind.Line -> "Line"
        | ChartKind.Area -> "Area"
        | ChartKind.Pie -> "Pie"
        | ChartKind.Scatter -> "Scatter"
        | ChartKind.Heatmap -> "Heatmap"

    // Phase 1143 — a `TextSource` in canonical wire JSON. The `Literal` arm is
    // the BARE STRING (0.2.0 canonical form, WIRE_FORMAT 16), so every pre-1143
    // input is byte-identical; the other two arms are `$type`-tagged with their
    // members in canonical order. The harness spells only the arms the corpus
    // uses and fails loudly on the rest, exactly as `valueFormat` does — a
    // fixture reaching for a `Query` binding would need an accessor no wire
    // document carries, so it must be a deliberate act rather than a default.
    let textSourceJson (t: TextSource) : string =
        match t with
        | TextSource.Literal text -> "\"" + esc text + "\""
        | TextSource.Bound(Binding.Static(Some v)) ->
            "{\"$type\":\"Bound\",\"binding\":{\"$type\":\"Static\",\"value\":\""
            + esc v
            + "\"}}"
        | TextSource.I18n(key, args) when Map.isEmpty args ->
            "{\"$type\":\"I18n\",\"args\":{},\"key\":\"" + esc key + "\"}"
        | _ -> failwith "chart-lowering inputs carry only Literal, Bound-of-Static and empty-args I18n text sources"

    let title =
        match case.Title with
        | Some t -> textSourceJson t
        | None -> "null"

    // Phase 876 — both keys are OMITTED when absent, so the twelve pre-876
    // inputs stay byte-identical. `valueFormat` is canonical `Format` wire JSON
    // (a real wire field); `axisUnitMode` is a harness-only style selector.
    let valueFormat =
        match case.ValueFormat with
        | None -> ""
        | Some(Format.Number None) -> ",\"valueFormat\":{\"$type\":\"Number\"}"
        | Some(Format.Number(Some d)) -> sprintf ",\"valueFormat\":{\"$type\":\"Number\",\"decimals\":%d}" d
        | Some(Format.Percent None) -> ",\"valueFormat\":{\"$type\":\"Percent\"}"
        | Some(Format.Percent(Some d)) -> sprintf ",\"valueFormat\":{\"$type\":\"Percent\",\"decimals\":%d}" d
        | Some(Format.Currency iso) -> sprintf ",\"valueFormat\":{\"$type\":\"Currency\",\"isoCode\":\"%s\"}" (esc iso)
        | Some _ -> failwith "chart-lowering inputs carry only the numeric Format arms"

    let unitMode =
        match case.UnitMode with
        | None -> ""
        | Some m -> sprintf ",\"axisUnitMode\":\"%s\"" (esc m)

    // Phase 878 — keys beside `title`, OMITTED when absent, so every input
    // predating this phase is untouched. `optText` carries the bare enum
    // strings; `optTextSource` carries the three `TextSource`-typed ones, which
    // spell every arm since Phase 1143.
    let optText (key: string) (v: string option) =
        match v with
        | None -> ""
        | Some s -> sprintf ",\"%s\":\"%s\"" key (esc s)

    let optTextSource (key: string) (v: TextSource option) =
        match v with
        | None -> ""
        | Some t -> sprintf ",\"%s\":%s" key (textSourceJson t)

    let titles =
        optTextSource "xTitle" case.XTitle
        + optTextSource "yTitle" case.YTitle
        + optTextSource "subtitle" case.Subtitle
        // Phase 880 — same OMITTED-when-absent posture; the value is the
        // canonical `ChartLegendPosition` enum string.
        + optText "legendPosition" case.LegendPosition
        // Phase 881 — likewise; the canonical `ChartDataLabels` enum string.
        + optText "dataLabels" case.DataLabels
        // Phase 882 — likewise; the canonical `ChartXScale` enum string.
        + optText "xScale" case.XScale
        // Phase 1490 — likewise, and the value is canonical `ChartAnnotation`
        // wire JSON: `$type` first (0x24 sorts before every lower-case key) and
        // the payload members Ordinal-ordered after it, which is what
        // `Canon.typed` + `Canon.render` produce. Spelled by hand here for the
        // reason `valueFormat` is: the neutral input contract is the OTHER
        // hosts' input, so it has to be canonical bytes rather than whatever an
        // encoder happened to emit.
        + (match case.Annotations with
           | None -> ""
           | Some anns ->
               let labelPart (label: TextSource option) =
                   match label with
                   | None -> ""
                   | Some t -> ",\"label\":" + textSourceJson t

               // Phase 1491 — the x address is its own union, so it is its own
               // canonical object: `$type` then the one payload member.
               let addressJson (at: ChartAnnotationX) =
                   match at with
                   | ChartAnnotationX.Category key -> "{\"$type\":\"Category\",\"key\":\"" + esc key + "\"}"
                   | ChartAnnotationX.Date iso -> "{\"$type\":\"Date\",\"iso\":\"" + esc iso + "\"}"

               let one (a: ChartAnnotation) =
                   match a with
                   | ChartAnnotation.ReferenceLine(v, label) ->
                       let l =
                           match label with
                           | None -> ""
                           | Some t -> "\"label\":" + textSourceJson t + ","

                       "{\"$type\":\"ReferenceLine\"," + l + "\"value\":" + num v + "}"
                   | ChartAnnotation.EventMarker(at, label) ->
                       // `at` sorts before `label` (Ordinal), so the label rides
                       // AFTER the address here where a reference line's rides
                       // before its value — the canonical order is the members'
                       // own, not a per-case convention.
                       "{\"$type\":\"EventMarker\",\"at\":" + addressJson at + labelPart label + "}"
                   | ChartAnnotation.RangeBand(range, label) ->
                       // Phase 1492 — `label` sorts BEFORE `range` (Ordinal), so
                       // the label leads here where an event marker's trails its
                       // address. Third spelling, third position, same rule: the
                       // canonical order is the members' own.
                       let rangeJson =
                           match range with
                           | ChartAnnotationRange.ValueRange(f, t) ->
                               "{\"$type\":\"ValueRange\",\"from\":" + num f + ",\"to\":" + num t + "}"
                           | ChartAnnotationRange.XRange(f, t) ->
                               "{\"$type\":\"XRange\",\"from\":"
                               + addressJson f
                               + ",\"to\":"
                               + addressJson t
                               + "}"

                       let l =
                           match label with
                           | None -> ""
                           | Some t -> "\"label\":" + textSourceJson t + ","

                       "{\"$type\":\"RangeBand\"," + l + "\"range\":" + rangeJson + "}"

               ",\"annotations\":[" + (anns |> List.map one |> String.concat ",") + "]")

    sprintf
        "{\"kind\":\"%s\",\"xField\":\"%s\",\"yFields\":[%s],\"title\":%s,\"stacked\":%s%s%s%s,\"data\":[%s]}"
        kind
        (esc case.XField)
        yFields
        title
        (if case.Stacked then "true" else "false")
        valueFormat
        unitMode
        titles
        rowsJson

let private yTicksOf (name: string) : string list = yTickTexts (loweredCase name)

let private axisUnitLabelOf (name: string) : string = axisUnitLabel (loweredCase name)

/// Every Phase-883 hover readout in the lowered Drawing, in emission order.
/// `Tip` rides `DrawStyle`, so this walks the shape tree rather than filtering
/// one arm — a tip on a group, a bar or a wedge all count.
let private tipsOf (name: string) : string list =
    let rec walk (sh: Shape) : string list =
        let styleOf =
            match sh with
            | Shape.Group(_, s)
            | Shape.Rectangle(_, _, _, _, _, s)
            | Shape.Line(_, _, _, _, s)
            | Shape.Polyline(_, s)
            | Shape.Polygon(_, s)
            | Shape.Curve(_, s)
            | Shape.Circle(_, _, _, s)
            | Shape.Ellipse(_, _, _, _, s)
            | Shape.Label(_, _, _, s) -> s

        let here =
            match styleOf.Tip with
            | Some(TextSource.Literal t) -> [ t ]
            | _ -> []

        match sh with
        | Shape.Group(children, _) -> here @ (children |> List.collect walk)
        | _ -> here

    (loweredCase name).Shapes |> List.collect walk

/// Phase 921 — the lowered Drawing's generated accessible summary (its
/// `Description`). `""` when the lowering generated none, which is the refused
/// pie and nothing else.
let private summaryOf (name: string) : string =
    match (loweredCase name).Description with
    | Some(TextSource.Literal t) -> t
    | _ -> ""

let private tryFindFixtures () : string option = Fuaran.Tests.CorpusRoot.tryFind () // Phase 1647 — the ONE corpus-root resolver (tests/corpus-root/CorpusRoot.fs)

[<Tests>]
let chartLoweringTests =
    match tryFindFixtures () with
    | None ->
        testList
            "Chart lowering (cross-host contract)"
            [ test "fixtures absent — skipped (standalone checkout)" {
                  Expect.isTrue true "wire-format-fixtures/ not found"
              } ]
    | Some fixturesRoot ->
        let dir = Path.Combine(fixturesRoot, "chart-lowering")

        // Emit mode — (re)generate the input + expected goldens.
        if Environment.GetEnvironmentVariable "FUARAN_EMIT_CHART_LOWERING" = "1" then
            Directory.CreateDirectory dir |> ignore

            for case in cases do
                File.WriteAllText(Path.Combine(dir, case.Name + ".input.json"), inputJson case)
                File.WriteAllText(Path.Combine(dir, case.Name + ".expected.json"), loweredJson case)

        testList
            "Chart lowering (cross-host contract)"
            [ test "every case lowers byte-identically to its committed golden" {
                  for case in cases do
                      let expectedFile = Path.Combine(dir, case.Name + ".expected.json")

                      Expect.isTrue
                          (File.Exists expectedFile)
                          (sprintf "%s: golden missing (regenerate with FUARAN_EMIT_CHART_LOWERING=1)" case.Name)

                      let expected = File.ReadAllText expectedFile
                      Expect.equal (loweredJson case) expected (sprintf "%s: lowering drifted from golden" case.Name)
              }

              test "lowering is deterministic — two independent runs are byte-identical" {
                  for case in cases do
                      Expect.equal (loweredJson case) (loweredJson case) (sprintf "%s: non-deterministic" case.Name)
              }

              test "lowering is order-independent — row Map construction order does not matter" {
                  // Build the same rows with a reversed field-insertion order; the
                  // lowering reads fields by name, so the Drawing must be identical.
                  for case in cases do
                      let reversedRows: Row list =
                          List.zip (xCells case) (case.Rows |> List.map snd)
                          |> List.map (fun (x, ys) ->
                              let fields =
                                  (List.zip case.YFields ys |> List.map (fun (f, v) -> f, box v))
                                  @ [ case.XField, x ]

                              Map.ofList (List.rev fields))

                      let nodeA: Node<obj> =
                          Fuaran.drawingSpec "c" (Charts.lower (specOf case) (Seq.ofList (buildRows case)))

                      let nodeB: Node<obj> =
                          Fuaran.drawingSpec "c" (Charts.lower (specOf case) (Seq.ofList reversedRows))

                      Expect.equal
                          (CanonicalJson.encodeNode nodeA)
                          (CanonicalJson.encodeNode nodeB)
                          (sprintf "%s: field-order-dependent" case.Name)
              }

              // ── Phase 1143 — the text contract ──
              //
              // The golden above already pins these bytes; this states what the
              // bytes MEAN, so a future regeneration that quietly resolved or
              // dropped an arm would rewrite the golden and still fail here.
              // `docs/CHART-LOWERING-TEXT-CONTRACT.md` is the recorded contract.
              test "every TextSource arm carries into the drawing, unresolved" {
                  // `TextSource` carries no structural equality (a `Binding` arm
                  // holds an accessor function), so the arms are compared by a
                  // projection rather than directly — which also keeps the
                  // failure message readable.
                  let describe (t: TextSource) : string =
                      match t with
                      | TextSource.Literal s -> "Literal:" + s
                      | TextSource.Bound(Binding.Static(Some v)) -> "Bound-Static:" + v
                      | TextSource.Bound _ -> "Bound:<other>"
                      | TextSource.I18n(key, _) -> "I18n:" + key

                  let ds = loweredCase "bar-bound-i18n-titles"

                  let labelTexts =
                      ds.Shapes
                      |> List.choose (function
                          | Shape.Label(_, _, t, _) -> Some(describe t)
                          | _ -> None)

                  // Clause 1 — carried, not resolved. The y title is the load-
                  // bearing one: a `Literal` of that length is truncated against
                  // the plot height (`bar-long-y-title`), so its surviving whole
                  // is clause 4 measured rather than asserted.
                  let expectedArms =
                      [ "Bound-Static:Revenue, from the live binding"
                        "Bound-Static:Monthly recurring revenue, net of refunds and credits, in pounds sterling"
                        "I18n:chart.axis.region"
                        "I18n:chart.subtitle.rolling_twelve_months" ]

                  for expected in expectedArms do
                      Expect.contains labelTexts expected "a declared TextSource arm did not reach a label"

                  // Clause 1, the a11y half: the drawing's own `Title` is the
                  // spec's, whichever arm it carries.
                  Expect.equal
                      (ds.Title |> Option.map describe)
                      (Some "Bound-Static:Revenue, from the live binding")
                      "the drawing's a11y Title dropped the Bound arm"

                  // Clause 5 — the capitalised-field-name fallback answers
                  // ABSENCE only. THIS is the assertion that catches a host
                  // dropping an axis arm, because the fallback draws a plausible
                  // axis name and the chart looks entirely ordinary.
                  Expect.isFalse
                      (labelTexts |> List.contains "Literal:Region")
                      "the x-axis fallback stood in for a declared I18n title"

                  Expect.isFalse
                      (labelTexts |> List.contains "Literal:Sales")
                      "the y-axis fallback stood in for a declared Bound title"

                  // Clause 6 — the generated summary names no title on any arm.
                  Expect.isFalse
                      ((summaryOf "bar-bound-i18n-titles").Contains "Revenue, from the live binding")
                      "the accessible summary folded in the title"
              }

              // ── Phase 637 — stacked-series semantics ──
              test "Stacked=true is ignored on kinds where stacking is meaningless (Line)" {
                  let case = cases |> List.find (fun c -> c.Name = "line-multi")
                  let rows () = Seq.ofList (buildRows case)

                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  let flat = Charts.lower (specOf case) (rows ())

                  let stacked = Charts.lower { specOf case with Stacked = true } (rows ())

                  Expect.equal (enc stacked) (enc flat) "Line must ignore the Stacked flag"
              }

              // ── Phase 638 — Pie-arm semantics ──
              test "a lone 100% pie category degenerates to a Circle, not a self-closing arc" {
                  let case =
                      { (cases |> List.find (fun c -> c.Name = "pie-single")) with
                          Rows = [ "Only", [ 10.0 ]; "None", [ 0.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let circles =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Circle _ -> true
                          | _ -> false)

                  let curves =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Curve _ -> true
                          | _ -> false)

                  Expect.equal (List.length circles) 1 "one full-circle wedge"
                  Expect.equal (List.length curves) 0 "no arc wedges"
              }

              test "a negative pie value refuses the geometry (title-only drawing, never a lie)" {
                  let case =
                      { (cases |> List.find (fun c -> c.Name = "pie-single")) with
                          Rows = [ "A", [ 40.0 ]; "B", [ -10.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let nonLabel =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Label _ -> false
                          | _ -> true)

                  Expect.equal (List.length nonLabel) 0 "no geometry for a mixed-sign pie"
              }

              test "a multi-series pie refuses the geometry rather than silently truncating" {
                  let case =
                      { (cases |> List.find (fun c -> c.Name = "pie-single")) with
                          YFields = [ "share"; "votes" ]
                          Rows = [ "A", [ 40.0; 1.0 ]; "B", [ 10.0; 2.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let nonLabel =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Label _ -> false
                          | _ -> true)

                  Expect.equal (List.length nonLabel) 0 "no first-series truncation"
              }

              test "a zero-share pie category keeps its legend row but draws no wedge" {
                  let case = cases |> List.find (fun c -> c.Name = "pie-single")
                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let wedges =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Curve _
                          | Shape.Circle _ -> true
                          | _ -> false)

                  let swatches =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Rectangle _ -> true
                          | _ -> false)

                  Expect.equal (List.length wedges) 4 "four wedges (Treasury share is 0)"
                  Expect.equal (List.length swatches) 5 "five legend rows (Treasury retained)"
              }

              // ── Phase 642 — keyed mark identity (object constancy) ──
              test "mark ids are row-order-invariant and survive row insertion" {
                  let styleOf (sh: Shape) : DrawStyle =
                      match sh with
                      | Shape.Group(_, s) -> s
                      | Shape.Rectangle(_, _, _, _, _, s) -> s
                      | Shape.Line(_, _, _, _, s) -> s
                      | Shape.Polyline(_, s) -> s
                      | Shape.Polygon(_, s) -> s
                      | Shape.Curve(_, s) -> s
                      | Shape.Circle(_, _, _, s) -> s
                      | Shape.Ellipse(_, _, _, _, s) -> s
                      | Shape.Label(_, _, _, s) -> s

                  let markIds (ds: DrawingSpec) : Set<string> =
                      ds.Shapes |> List.choose (fun sh -> (styleOf sh).MarkId) |> Set.ofList

                  let case = cases |> List.find (fun c -> c.Name = "bar-multi")
                  let baseIds = markIds (Charts.lower (specOf case) (Seq.ofList (buildRows case)))

                  Expect.isNonEmpty (Set.toList baseIds) "data marks carry ids"

                  // Row order shuffled: the id SET is identical (identity is
                  // derived from series-field|category, never the row index).
                  let shuffled = { case with Rows = List.rev case.Rows }

                  let shuffledIds =
                      markIds (Charts.lower (specOf shuffled) (Seq.ofList (buildRows shuffled)))

                  Expect.equal shuffledIds baseIds "row order must not change mark identities"

                  // A row insertion adds ids and invalidates none.
                  let grown =
                      { case with
                          Rows = case.Rows @ [ "West", [ 70.0; 95.0 ] ] }

                  let grownIds = markIds (Charts.lower (specOf grown) (Seq.ofList (buildRows grown)))

                  Expect.isTrue (Set.isSubset baseIds grownIds) "insertion preserves every pre-existing id"
              }

              test "stacked bar geometry differs from grouped bar geometry over the same data" {
                  let case = cases |> List.find (fun c -> c.Name = "bar-stacked")
                  let rows () = Seq.ofList (buildRows case)

                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  let stacked = Charts.lower (specOf case) (rows ())

                  let grouped = Charts.lower { specOf case with Stacked = false } (rows ())

                  Expect.notEqual (enc stacked) (enc grouped) "Stacked must change Bar geometry"
              }

              // ── Phase 885 — ChartStyle as a lowering parameter ──
              test "ChartStyle.defaults IS the corpus-pinned form — lowerWithStyle defaults ≡ lower" {
                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  for case in cases do
                      let viaLower = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                      let viaStyle =
                          Charts.lowerWithStyle
                              Charts.ChartLimits.defaults
                              Charts.ChartStyle.defaults
                              (specOf case)
                              (Seq.ofList (buildRows case))

                      Expect.equal
                          (enc viaStyle)
                          (enc viaLower)
                          (sprintf "%s: the default style must reproduce the pinned lowering" case.Name)
              }

              test "a custom ChartStyle restyles the lowering — palette + title size flow through" {
                  let case = cases |> List.find (fun c -> c.Name = "bar-multi")

                  let custom =
                      { Charts.ChartStyle.defaults with
                          Palette = [| "#112233"; "#445566" |]
                          TitleFontSize = 42.0
                          Width = 800.0 }

                  let styled =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          custom
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  let fills =
                      styled.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Rectangle(_, _, _, _, _, s) ->
                              match s.Fill with
                              | Some(Binding.Static(Some c)) -> Some c
                              | _ -> None
                          | _ -> None)
                      |> Set.ofList

                  Expect.equal fills (Set.ofList [ "#112233"; "#445566" ]) "bars + swatches take the custom palette"

                  let titleSizes =
                      styled.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, _, s) when s.Emphasis = Some Emphasis.Loud -> s.FontSize
                          | _ -> None)

                  Expect.equal titleSizes [ 42.0 ] "the title carries the custom size"
                  Expect.equal styled.ViewBox.Width 800.0 "the canvas takes the custom width"

                  // …and the default style is untouched by the custom one.
                  let byDefault = Charts.lower (specOf case) (Seq.ofList (buildRows case))
                  Expect.equal byDefault.ViewBox.Width 640.0 "defaults unaffected by a host's style"
              }

              test "the reserved ChartStyle fields are genuinely not consumed" {
                  // The status triple is declared (Phase 885) but read by no
                  // shipped lowering path — setting it must change nothing
                  // until the variance/waterfall arms land. `LabelTiltDegrees`
                  // LEFT this set in Phase 879 and `LegendPosition` in Phase
                  // 880, both of which consume theirs.
                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  let reserved =
                      { Charts.ChartStyle.defaults with
                          PositiveColour = "#000001"
                          NegativeColour = "#000002"
                          NeutralColour = "#000003" }

                  for case in cases do
                      let plain = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                      let withReserved =
                          Charts.lowerWithStyle
                              Charts.ChartLimits.defaults
                              reserved
                              (specOf case)
                              (Seq.ofList (buildRows case))

                      Expect.equal
                          (enc withReserved)
                          (enc plain)
                          (sprintf "%s: a reserved field must not affect the lowering" case.Name)
              }

              // ── Phase 876 — axis number formatting ──
              //
              // The goldens pin the whole picture; these pin the RULES, in the
              // one form a reader can check by eye. Each reads the y-tick
              // strings straight out of the lowered drawing.
              test "the default renders thousands separators and step-derived decimals" {
                  Expect.equal
                      (yTicksOf "bar-thousands")
                      [ "0"; "2,000"; "4,000"; "6,000"; "8,000"; "10,000" ]
                      "a 2 000 step gives 0 dp + comma grouping"

                  Expect.equal
                      (yTicksOf "line-decimals")
                      [ "0.0"; "0.1"; "0.2"; "0.3"; "0.4"; "0.5" ]
                      "a 0.1 step gives 1 dp on EVERY tick, the zero included"
              }

              test "a millions-range axis reads short ticks under one unit label" {
                  Expect.equal (yTicksOf "bar-millions") [ "0"; "5"; "10"; "15"; "20" ] "the ticks are scaled by 10^6"

                  Expect.equal (axisUnitLabelOf "bar-millions") "Millions" "the unit is stated once"

                  // …and a THOUSANDS-range axis is left alone at the default
                  // gate: the operator's `unit > 3` rule, so `12,500` survives.
                  // Since Phase 878 the slot is then EMPTY rather than falling
                  // back to the literal `"Value"` — the axis's name lives in
                  // the rotated y title, and the slot carries units or nothing.
                  Expect.equal (axisUnitLabelOf "bar-thousands") "" "no unit label below the gate"
              }

              test "a host can lower the display-unit gate to reach Thousands" {
                  let style =
                      { Charts.ChartStyle.defaults with
                          DisplayUnitMinExponent = 3 }

                  let case = cases |> List.find (fun c -> c.Name = "bar-thousands")

                  let ds =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          style
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  Expect.equal (yTickTexts ds) [ "0"; "2"; "4"; "6"; "8"; "10" ] "scaled by 10^3"
                  Expect.equal (axisUnitLabel ds) "Thousands" "the words form of 10^3"
              }

              test "Format.Currency prefixes the symbol inside the sign; Percent reads a ratio" {
                  Expect.equal
                      (yTicksOf "bar-currency")
                      [ "£0"; "£2,000"; "£4,000"; "£6,000"; "£8,000"; "£10,000" ]
                      "the symbol sits on every tick when the label states only the magnitude"

                  Expect.equal
                      (yTicksOf "line-percent")
                      [ "0%"; "10%"; "20%"; "30%"; "40%"; "50%" ]
                      "a ratio source renders ×100 with a % suffix"

                  // The sign rule, on a domain that crosses zero.
                  let case =
                      { plain with
                          Name = "negatives"
                          Kind = ChartKind.Bar
                          XField = "quarter"
                          YFields = [ "delta" ]
                          ValueFormat = Some(Format.Currency "GBP")
                          Rows = [ "Q1", [ -1200.0 ]; "Q2", [ 800.0 ] ] }

                  let ticks = yTickTexts (Charts.lower (specOf case) (Seq.ofList (buildRows case)))

                  Expect.isTrue
                      (ticks |> List.forall (fun t -> not (t.Contains "£-")))
                      "the sign leads, never the symbol"

                  Expect.isTrue (List.contains "-£1,500" ticks) "a negative currency tick reads -£1,500"
                  Expect.isTrue (List.contains "£0" ticks) "the zero tick never reads -£0"
              }

              test "the symbol moves into the label under WordsWithSymbol / SIAbbreviation" {
                  Expect.equal
                      (yTicksOf "bar-currency-millions")
                      [ "0"; "5"; "10"; "15"; "20" ]
                      "the ticks drop the symbol — it is stated once"

                  Expect.equal (axisUnitLabelOf "bar-currency-millions") "Millions of £" "words + symbol"
                  Expect.equal (yTicksOf "bar-currency-si") [ "0"; "5"; "10"; "15"; "20" ] "same ticks, SI label"
                  Expect.equal (axisUnitLabelOf "bar-currency-si") "M£" "the SI prefix + the symbol"
              }

              test "CompactPerTick suffixes every tick and states no unit label" {
                  Expect.equal
                      (yTicksOf "bar-compact")
                      [ "0K"; "2K"; "4K"; "6K"; "8K"; "10K" ]
                      "the suffix repeats — the deliberate opt-out from the doctrine"

                  Expect.equal (axisUnitLabelOf "bar-compact") "" "no unit label in compact mode"
              }

              test "rule 5 — the integer part is positional at every magnitude, on every host" {
                  // The regime BELOW the notation switch is the control: these
                  // bytes were already right on all four hosts and must not
                  // move. `1e15` is `formatNum`'s int64 fast-path bound, not the
                  // notation switch — the window between them was never broken.
                  Expect.equal
                      (tipsOf "bar-huge-magnitudes" |> List.item 0)
                      "notional · Swaps · 1,500,000,000,000,000"
                      "1.5e15 is above the int64 fast path and below the notation switch — unchanged"

                  // Past the .NET `"R"` switch (1e17), where the F#, Python and
                  // Rust hosts used to hand `groupThousands` a `2.5E+17` and get
                  // `2.5E,+17` back, while the TypeScript host — positional to
                  // 1e21 — got this. The fix makes every host agree HERE.
                  Expect.equal
                      (tipsOf "bar-huge-magnitudes" |> List.item 3)
                      "notional · Forwards · 250,000,000,000,000,000"
                      "2.5e17 expands rather than grouping an exponent"

                  // Past JavaScript's switch too, so pre-fix all four hosts were
                  // wrong AND disagreed (`1E,+21` against `1e,+21`). No axis
                  // here — a pie's wedge tip prints the raw datum.
                  Expect.equal
                      (tipsOf "pie-huge-magnitudes")
                      [ "notional · Swaps · 500,000,000,000,000,000,000"
                        "notional · Options · 1,000,000,000,000,000,000,000"
                        "notional · Futures · 2,250,000,000,000,000,000,000"
                        "notional · Forwards · 3,750,000,000,000,000,000,000" ]
                      "a single mantissa digit zero-pads to exp+1 places, and so does a multi-digit one"

                  // The AXIS reaches the switch too, because `Off` is the only
                  // mode that does not scale a large magnitude away first.
                  Expect.equal
                      (yTicksOf "bar-huge-magnitudes")
                      [ "0"
                        "100,000,000,000,000,000"
                        "200,000,000,000,000,000"
                        "300,000,000,000,000,000" ]
                      "the ticks are grouped digits, never a grouped exponent"

                  // The SHAPE of the defect, stated so a future regression fails
                  // on the shape and not only on these literals: no rendered
                  // MAGNITUDE may carry an exponent marker at all. Only the
                  // number is checked — a tip's leading series and category are
                  // untrusted feed strings and `Futures` legitimately has an `e`
                  // in it.
                  let magnitudes =
                      (tipsOf "bar-huge-magnitudes" @ tipsOf "pie-huge-magnitudes"
                       |> List.map (fun t -> t.Substring(t.LastIndexOf " · " + 3)))
                      @ yTicksOf "bar-huge-magnitudes"

                  Expect.isTrue
                      (magnitudes |> List.forall (fun t -> not (t.Contains "E" || t.Contains "e")))
                      "no rendered magnitude carries an exponent marker"
              }

              test "AxisUnitMode.Off never scales, however large the axis" {
                  let style =
                      { Charts.ChartStyle.defaults with
                          AxisUnitMode = Charts.ChartAxisUnitMode.Off }

                  let case = cases |> List.find (fun c -> c.Name = "bar-millions")

                  let ds =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          style
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  Expect.equal
                      (yTickTexts ds)
                      [ "0"; "5,000,000"; "10,000,000"; "15,000,000"; "20,000,000" ]
                      "full magnitudes, grouped"

                  Expect.equal (axisUnitLabel ds) "" "no unit label when nothing was scaled"
              }

              test "the Scatter x axis takes the same canonical formatter" {
                  let case =
                      { plain with
                          Name = "scatter-thousands"
                          Kind = ChartKind.Scatter
                          XField = "population"
                          YFields = [ "score" ]
                          XNums = Some [ 12000.0; 45000.0; 78000.0 ]
                          Rows = [ "", [ 1.0 ]; "", [ 2.0 ]; "", [ 3.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  // The x labels sit under the plot; the y labels are End-anchored.
                  let xLabels =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, TextSource.Literal t, s) when s.TextAnchor = Some TextAnchor.Middle ->
                              Some t
                          | _ -> None)

                  Expect.isTrue (xLabels |> List.exists (fun t -> t.Contains ",")) "numeric x ticks are grouped too"
              }

              test "the pie legend percentages are unchanged by the formatter routing" {
                  let case = cases |> List.find (fun c -> c.Name = "pie-single")
                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let legend =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, TextSource.Literal t, _) when t.Contains "%" -> Some t
                          | _ -> None)

                  Expect.equal
                      legend
                      [ "Founders (40%)"
                        "Series A (30%)"
                        "Employees (20%)"
                        "Advisors (10%)"
                        "Treasury (0%)" ]
                      "the NN% shape survives"
              }

              // ── Phase 879 — deterministic text metrics ──
              //
              // The goldens pin the whole picture; these pin the RULES in the
              // one form a reader can check by hand against the table.
              test "the advance-width table is the five pinned classes" {
                  // The em sum accumulates left-to-right in floats, so compare
                  // at the table's own precision rather than bit-for-bit.
                  let em (s: string) =
                      System.Math.Round(Charts.TextMetrics.advanceEmOf s, 6)

                  let cls (count: float) (factor: float) = System.Math.Round(count * factor, 6)

                  Expect.equal (em "iljI.,:;!| '") (cls 12.0 0.28) "the thin class"
                  Expect.equal (em "\"()*-/\\[]{}frt") (cls 14.0 0.33) "the narrow class"
                  Expect.equal (em "mMW%@") (cls 5.0 0.9) "the extra-wide class"
                  Expect.equal (em "ABCDEFGHKNOPQRSTUVXYZw") (cls 22.0 0.7) "the wide class"
                  // Digits, the remaining lowercase, `J`/`L`, and — the rule
                  // that makes the table TOTAL — every unlisted character,
                  // including non-ASCII.
                  Expect.equal (em "0123456789") (cls 10.0 0.55) "digits default"
                  Expect.equal (em "JL") (cls 2.0 0.55) "J and L are default-width, not wide"
                  Expect.equal (em "£€漢…") (cls 4.0 0.55) "an unlisted character takes the default"

                  // Width is the em sum times the size, rounded once.
                  Expect.equal (Charts.TextMetrics.width 13.0 "0000") (r2 (13.0 * 2.2)) "width = size × em sum"
                  Expect.equal (Charts.TextMetrics.width 13.0 "") 0.0 "an empty string is zero-wide"
              }

              test "truncation is deterministic and never yields an empty label" {
                  let t = Charts.TextMetrics.truncateToWidth 13.0

                  Expect.equal (t 1000.0 "already fits") "already fits" "a string within budget is untouched"

                  // 10 digits = 5.5 em = 71.5 px. A 40 px budget leaves
                  // 40 - 7.15 (the ellipsis) = 32.85 px ⇒ 4 digits (28.6 px);
                  // a fifth would be 35.75.
                  Expect.equal (t 40.0 "0123456789") "0123…" "the longest prefix that fits, plus the ellipsis"

                  // Nothing fits ⇒ the bare ellipsis, never "".
                  Expect.equal (t 1.0 "0123456789") "…" "a hopeless budget still yields a mark"
                  Expect.equal (t 0.0 "x") "…" "a zero budget still yields a mark"
              }

              test "the fit predicate answers both axes (the Phase 881 gate)" {
                  let fits = Charts.TextMetrics.fitsBox 13.0 1.2
                  Expect.isTrue (fits 100.0 20.0 "short") "fits both ways"
                  Expect.isFalse (fits 10.0 20.0 "short") "too wide"
                  Expect.isFalse (fits 100.0 10.0 "short") "too short a box"
              }

              test "the left margin autosizes to the widest FORMATTED tick" {
                  // `Off` prints full magnitudes, so the widest tick is
                  // `1,000,000` = 4.41 em = 57.33 px; + the 12 px gap + 6 px
                  // padding = 75.33 — and, since Phase 878, + the rotated
                  // y-title's band (one 15.6 px line + 6 px padding = 21.6) =
                  // 96.93, past the 64 px floor.
                  let leftmost (ds: DrawingSpec) : float =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(x1, _, _, _, _) -> Some x1
                          | _ -> None)
                      |> List.min

                  let wide = loweredCase "bar-wide-ticks"
                  // The y-tick marks start `TickMarkLength` left of the spine.
                  Expect.equal (leftmost wide) (r2 (96.93 - 5.0)) "the spine moved right to clear the tick column"

                  // …and a short tick column leaves the floor alone.
                  let narrow = loweredCase "bar-single"
                  Expect.equal (leftmost narrow) (64.0 - 5.0) "a short column keeps the 64 px floor"
              }

              test "the BAND legend's pitch derives from each entry's own name extent" {
                  // Since Phase 880 the band is the `Top` / `Bottom` arms, not
                  // the default — so the two cases are lowered under an
                  // explicit `Top` to ask this question of the arm that still
                  // answers it. The 879 rule and its numbers are unchanged.
                  let swatchXs (name: string) : float list =
                      let case =
                          { (cases |> List.find (fun c -> c.Name = name)) with
                              LegendPosition = Some "Top" }

                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          (styleOf case)
                          (specOf case)
                          (Seq.ofList (buildRows case))
                      |> fun ds ->
                          ds.Shapes
                          |> List.choose (fun sh ->
                              match sh with
                              | Shape.Rectangle(x, y, _, _, Some _, _) when y = 34.0 -> Some x
                              | _ -> None)

                  // "monthly_recurring_revenue_gbp" is 14.66 em = 190.58 px, so
                  // entry 0 occupies 15 + 190.58 + 24 = 229.58 px.
                  Expect.equal
                      (swatchXs "bar-legend-long-names")
                      [ 64.0; r2 (64.0 + 229.58) ]
                      "the second swatch clears the first label"

                  // The short-name case is where the retired flat 100 px pitch
                  // happened to look right; it no longer sits there.
                  Expect.equal
                      (swatchXs "bar-multi")
                      [ 64.0; r2 (64.0 + 15.0 + 32.24 + 24.0) ]
                      "short names pack tighter than the retired pitch"
              }

              test "the category-label angle ladder is fit-driven and uniform per axis" {
                  // CHROME labels only. Since Phase 878 the y-axis TITLE is
                  // rotated too, and it is full-strength ink where every chrome
                  // label carries `LabelOpacity` — so the opacity is what
                  // separates "a label the tilt rule decided" from "the axis's
                  // name". Without this filter the scatter assertion below
                  // would read the title's -90 and call the x ticks rotated.
                  let rotations (ds: DrawingSpec) : float list =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, _, s) when Option.isSome s.Opacity -> s.Rotation
                          | _ -> None)
                      |> List.distinct

                  // Phase 903 — the FLAT rung is the resting state. Five roomy
                  // compass labels fit their bands, so nothing rotates. An empty
                  // rotation list is the assertion: a flat label carries no
                  // `Rotation` at all, not `Some 0.0`.
                  Expect.equal (rotations (loweredCase "bar-flat-five")) [] "roomy categories read flat"

                  // Twenty categories fit at neither of the lower rungs.
                  Expect.equal (rotations (loweredCase "bar-vertical-twenty")) [ -90.0 ] "escalated to vertical"

                  // THE LADDER, one character per step, on otherwise identical
                  // charts. Each list has exactly ONE element, which is the
                  // uniformity claim: an axis never mixes angles, so however many
                  // categories it carries there is a single rotation to read.
                  Expect.equal (rotations (loweredCase "bar-flat-boundary")) [] "19 chars fit the band"
                  Expect.equal (rotations (loweredCase "bar-tilt-boundary")) [ -30.0 ] "20 chars need the tilt"
                  Expect.equal (rotations (loweredCase "bar-vertical-boundary")) [ -90.0 ] "21 chars need vertical"

                  // A numeric scatter x axis stays horizontal — its ticks are
                  // short by construction and belong centred on their value.
                  Expect.equal (rotations (loweredCase "scatter-single")) [] "scatter x ticks are never rotated"
              }

              test "a host can opt out of the tilt entirely, and is not escalated instead" {
                  let style =
                      { Charts.ChartStyle.defaults with
                          LabelTiltDegrees = 0.0 }

                  let case = cases |> List.find (fun c -> c.Name = "bar-vertical-twenty")

                  let ds =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          style
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  // Chrome labels only — the Phase-878 y-axis title is rotated
                  // by its own rule and is not what this opt-out governs.
                  let rotated =
                      ds.Shapes
                      |> List.filter (function
                          | Shape.Label(_, _, _, s) -> Option.isSome s.Rotation && Option.isSome s.Opacity
                          | _ -> false)

                  Expect.isEmpty rotated "0° means horizontal, not 'escalate me'"
              }

              test "the ladder's rung decides the ANCHOR, not just the angle" {
                  // A flat category label is `Middle`-anchored ON the band
                  // centre; a rotated one is `End`-anchored at the same point,
                  // because the anchor is the pivot. Reading the anchor rather
                  // than only the rotation is what catches a host that rotated
                  // the text but left it centred — which draws the label
                  // straddling its own band instead of falling away from it.
                  let categoryAnchors (name: string) : (TextAnchor option * float option) list =
                      // The category row is the LOWEST row of chrome labels: the
                      // y ticks sit beside the plot and the axis titles carry no
                      // opacity, so taking the maximum-y group is total without
                      // depending on where any margin happened to land — which a
                      // fixed y threshold is not, since the tilt moves the plot's
                      // bottom edge.
                      let chrome =
                          loweredCase name
                          |> fun ds -> ds.Shapes
                          |> List.choose (fun sh ->
                              match sh with
                              | Shape.Label(_, y, _, s) when Option.isSome s.Opacity ->
                                  Some(y, (s.TextAnchor, s.Rotation))
                              | _ -> None)

                      let lowest = chrome |> List.map fst |> List.max

                      chrome |> List.filter (fst >> (=) lowest) |> List.map snd |> List.distinct

                  Expect.equal
                      (categoryAnchors "bar-flat-five")
                      [ Some TextAnchor.Middle, None ]
                      "the flat rung centres its labels"

                  Expect.equal
                      (categoryAnchors "bar-tilt-boundary")
                      [ Some TextAnchor.End, Some -30.0 ]
                      "the tilt rung pivots on the band centre"

                  Expect.equal
                      (categoryAnchors "bar-vertical-boundary")
                      [ Some TextAnchor.End, Some -90.0 ]
                      "so does the vertical rung"
              }

              // ── Phase 903 — band-boundary tick marks ──

              test "a BAND axis ticks its n+1 boundaries; a continuous axis ticks its values" {
                  // The x tick marks are the short vertical segments hanging
                  // BELOW the x-axis spine — the only lines whose y-extent lies
                  // entirely under `plotY1`, which is what separates them from
                  // the gridlines, the spines and the bar geometry.
                  let xMarkXs (name: string) : float list =
                      let ds = loweredCase name

                      let spineY =
                          ds.Shapes
                          |> List.choose (fun sh ->
                              match sh with
                              | Shape.Line(x1, y1, x2, y2, _) when y1 = y2 && x1 < x2 -> Some y1
                              | _ -> None)
                          |> List.max

                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(x1, y1, x2, y2, _) when x1 = x2 && y1 = spineY && y2 > spineY -> Some x1
                          | _ -> None)
                      |> List.sort

                  // FOUR bands ⇒ FIVE marks, at the boundaries: the first on the
                  // y-axis spine, the last on the plot's right edge, and the band
                  // pitch between each pair. The labels sit at the CENTRES, so no
                  // mark shares an x with one — which is the whole change.
                  let quarters = xMarkXs "bar-single"
                  Expect.equal (List.length quarters) 5 "n+1 marks delimit n bands"

                  let pitches =
                      quarters
                      |> List.pairwise
                      |> List.map (fun (a, b) -> r2 (b - a))
                      |> List.distinct

                  Expect.equal (List.length pitches) 1 "the boundaries are evenly pitched"

                  // The first boundary IS the y-axis spine, and the last IS the
                  // plot's right edge — so the marks bracket the plot rather than
                  // sitting inside it.
                  let spineX =
                      loweredCase "bar-single"
                      |> fun ds -> ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(x1, _, x2, _, _) when x1 = x2 -> Some x1
                          | _ -> None)
                      |> List.min

                  Expect.equal (List.head quarters) spineX "boundary 0 lands on the y-axis spine"

                  // Twenty bands ⇒ twenty-one marks. The count follows the
                  // categories, not the tick rule.
                  Expect.equal (List.length (xMarkXs "bar-vertical-twenty")) 21 "the rule is n+1, at any n"

                  // SCATTER is continuous: its x marks sit at the nice-tick
                  // VALUES, so their count follows the tick rule and not the row
                  // count. Six rows, and not six marks.
                  let scatter = xMarkXs "scatter-single"
                  Expect.isTrue (List.length scatter > 0) "the continuous axis still ticks"

                  let scatterTickXs =
                      loweredCase "scatter-single"
                      |> fun ds -> ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          // Its x tick LABELS are the unrotated Middle-anchored
                          // chrome labels below the plot.
                          | Shape.Label(x, y, _, s) when
                              Option.isSome s.Opacity
                              && s.TextAnchor = Some TextAnchor.Middle
                              && Option.isNone s.Rotation
                              && y > 300.0
                              ->
                              Some x
                          | _ -> None)
                      |> List.sort

                  Expect.equal scatter scatterTickXs "a continuous mark sits AT its label, not beside it"
              }

              // ── Phase 878 — axis titles + subtitle ──
              //
              // The readers below key off the same discriminators the lowering
              // uses to place these shapes, so a test cannot pass by finding
              // the wrong label: the y title is the ONLY rotated full-strength
              // label, the x title the only unrotated full-strength `Middle`
              // one on a band arm, and the subtitle the only muted label that
              // is neither End-anchored nor sitting in the legend row.

              test "an absent axis title falls back to the capitalised field name" {
                  let ds = loweredCase "bar-axis-titles-default"

                  Expect.equal (xAxisTitleOf ds) "Quarter" "the x title capitalises XField, as it always has"

                  Expect.equal
                      (yAxisTitleOf ds)
                      "Revenue"
                      "the y title capitalises the FIRST y-field — never the retired \"Value\" literal"

                  // The retired literal is gone from the whole drawing, not
                  // merely from the slot the readers look at.
                  Expect.isFalse
                      (literalTexts ds |> List.contains "Value")
                      "no shape anywhere still prints the hardcoded hint"
              }

              test "an explicit axis title overrides the fallback, and the y title is rotated bottom-up" {
                  let ds = loweredCase "bar-axis-titles"

                  Expect.equal (xAxisTitleOf ds) "Sales region" "the declared x title wins over \"Region\""
                  Expect.equal (yAxisTitleOf ds) "Value (£)" "the declared y title wins over \"Sales\""

                  Expect.equal
                      (yAxisTitleRotation ds)
                      (Some -90.0)
                      "negative = counter-clockwise = reads bottom-up, the y-axis convention"
              }

              test "a multi-series chart takes its y-title fallback from the FIRST y-field" {
                  // `bar-multi` plots sales + target and declares no titles.
                  Expect.equal (yAxisTitleOf (loweredCase "bar-multi")) "Sales" "the first series names the axis"
              }

              test "the subtitle is muted, smaller than the title, and directly under it" {
                  let ds = loweredCase "bar-axis-titles"

                  let subtitle =
                      ds.Shapes
                      |> List.tryPick (fun sh ->
                          match sh with
                          | Shape.Label(x, y, TextSource.Literal "Rolling twelve months", s) -> Some(x, y, s)
                          | _ -> None)

                  match subtitle with
                  | None -> failtest "the subtitle did not render"
                  | Some(x, y, s) ->
                      Expect.equal s.FontSize (Some 13.0) "smaller than the 18 px title"

                      // `Binding<float>` carries no equality constraint, so the
                      // opacity is read out rather than compared wholesale.
                      match s.Opacity with
                      | Some(Binding.Static(Some o)) ->
                          Expect.equal
                              o
                              Charts.ChartStyle.defaults.LabelOpacity
                              "muted — label-role ink, not the title's full strength"
                      | _ -> failtest "the subtitle carries no static opacity, so it is not muted at all"

                      Expect.equal s.Emphasis (Some Emphasis.Normal) "the title carries the Loud weight, not this"
                      Expect.equal y 38.0 "one line under the 22 px title baseline"

                      // Left-aligned WITH the title: same x, same anchor.
                      let titleX =
                          ds.Shapes
                          |> List.pick (fun sh ->
                              match sh with
                              | Shape.Label(tx, _, _, ts) when ts.Emphasis = Some Emphasis.Loud -> Some tx
                              | _ -> None)

                      Expect.equal x titleX "shares the title's x, so the pair reads as one block"
                      Expect.equal s.TextAnchor (Some TextAnchor.Start) "and its alignment"
              }

              test "the top margin reserves the subtitle's line ONLY when one is present" {
                  // `bar-subtitle-units` and `bar-axis-titles-default` are the
                  // same chart but for the subtitle, so the plot's top edge is
                  // the one thing that moves — by exactly one subtitle line
                  // (13 px × 1.2 = 15.6), taking the legend row with it.
                  let topOf (ds: DrawingSpec) : float =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, y1, _, _, _) -> Some y1
                          | _ -> None)
                      |> List.min

                  let without = topOf (loweredCase "bar-axis-titles-default")
                  let bare = topOf (loweredCase "bar-single")

                  Expect.equal bare 64.0 "a chart with no subtitle keeps the pre-878 top margin exactly"
                  Expect.equal without 64.0 "…and so does one that declares the other two fields"
                  Expect.equal (topOf (loweredCase "bar-subtitle-units")) (r2 (64.0 + 15.6)) "one subtitle line lower"
              }

              test "an explicit subtitle suppresses the display-unit slot (the dedupe rule)" {
                  // Both cases scale to millions under WordsWithSymbol, so both
                  // HAVE a unit to state; only the one without a subtitle states
                  // it. The ticks are identical, which is the point — dedupe
                  // removes the repetition, never the scaling.
                  Expect.equal
                      (axisUnitLabelOf "bar-axis-titles-default")
                      "Millions of £"
                      "no subtitle — the lowering states the unit itself"

                  Expect.equal
                      (axisUnitLabelOf "bar-subtitle-units")
                      ""
                      "the author's subtitle said it; the machine does not repeat it"

                  Expect.equal
                      (yTicksOf "bar-subtitle-units")
                      (yTicksOf "bar-axis-titles-default")
                      "suppressing the label does not un-scale the axis"
              }

              test "a long y title truncates to the PLOT HEIGHT, the extent it runs along" {
                  let ds = loweredCase "bar-long-y-title"
                  let title = yAxisTitleOf ds

                  Expect.stringEnds title "…" "it did not fit and came back ellipsised"

                  // The bound is the plot height (400 − 64 top − the autosized
                  // bottom margin), NOT the left margin's width — a rotated
                  // title's length runs vertically.
                  let plotH =
                      let ys =
                          ds.Shapes
                          |> List.choose (fun sh ->
                              match sh with
                              | Shape.Line(_, y1, _, y2, _) when y1 = y2 -> Some y1
                              | _ -> None)

                      List.max ys - List.min ys

                  Expect.isTrue
                      (Charts.TextMetrics.width 13.0 title <= plotH)
                      "the truncated title fits the plot height"

                  Expect.isTrue
                      (Charts.TextMetrics.width 13.0 (title.Substring(0, title.Length - 1) + "x…") > plotH)
                      "…and is the LONGEST prefix that does — one character more overruns"
              }

              test "a pathological label is truncated rather than allowed to eat the plot" {
                  // One 400-character category: the bottom margin cannot grow
                  // past its ceiling, so the label truncates to what the ceiling
                  // affords and the plot keeps its share of the canvas.
                  let case =
                      { plain with
                          Name = "pathological"
                          Kind = ChartKind.Bar
                          XField = "name"
                          YFields = [ "v" ]
                          Rows = [ String.replicate 400 "x", [ 10.0 ]; "b", [ 20.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let categoryText =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Label(_, _, TextSource.Literal t, s) when Option.isSome s.Rotation -> Some t
                          | _ -> None)

                  Expect.isTrue (categoryText.EndsWith "…") "the label carries the truncation mark"
                  Expect.isTrue (categoryText.Length < 400) "and is shorter than the input"

                  // The bottom margin is capped at 35 % of the canvas height.
                  let plotBottom =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, y1, _, y2, _) when y1 = y2 -> Some y1
                          | _ -> None)
                      |> List.max

                  Expect.isTrue (plotBottom >= 400.0 - 0.35 * 400.0) "the plot keeps at least 65 % of the height"
              }

              // ── Phase 880 — legend placement ──
              //
              // The goldens pin the pictures; these pin the RULES. The readers
              // key off the swatch — a rounded-corner Rectangle is the legend's
              // and nothing else's, on either arm — so a test cannot pass by
              // finding a bar.

              test "the DEFAULT legend is a vertical right-hand column, and it shrinks the plot" {
                  let ds = loweredCase "bar-multi"

                  Expect.equal
                      (legendSwatches ds)
                      [ 562.68, 64.0; 562.68, 84.0 ]
                      "one row per series, top-aligned with the plot and pitched by LegendRowPitchY"

                  // The column is taken off the PLOT, not off the right margin:
                  // the single-series chart (which draws no legend) keeps the
                  // full 640 − 28 width, and this one is short by the column.
                  Expect.equal (plotRight (loweredCase "bar-single")) 612.0 "no legend, no column, full width"
                  Expect.equal (plotRight ds) 546.68 "the plot ends where the column's gap begins"

                  // …and the column's far edge lands ON the right margin, so
                  // the margin is still exactly the clearance to the canvas.
                  // "target" is the wider of the two names (2.64 em = 34.32 px).
                  Expect.equal
                      (r2 (546.68 + 16.0 + 15.0 + 34.32))
                      (640.0 - 28.0)
                      "column + gap + widest label = the margin"
              }

              test "rows are TOP-aligned, so adding a series never moves an existing row" {
                  // Centring would make row j's y a function of the entry
                  // count. Eight series must therefore start exactly where two
                  // do — this is the object-constancy argument, in the one form
                  // that can fail.
                  let two = legendSwatches (loweredCase "bar-multi") |> List.map snd
                  let eight = legendSwatches (loweredCase "bar-legend-eight-series") |> List.map snd

                  Expect.equal (List.head two) (List.head eight) "the first row is where it always is"
                  Expect.equal (List.truncate 2 eight) two "…and so is the second"
              }

              test "eight series legend themselves — the case the band could not hold" {
                  let rows = legendSwatches (loweredCase "bar-legend-eight-series")

                  Expect.equal (List.length rows) 8 "one row per palette slot"

                  Expect.isTrue
                      (rows |> List.forall (fun (x, y) -> x + 10.0 <= 640.0 && y + 10.0 <= 400.0))
                      "every swatch is inside the canvas"

                  // The contrast, in the one form that can fail. A band's width
                  // is the SUM of its entries, so it cannot hold them once the
                  // names are long enough — which is what the Tidy-Up bundle
                  // recorded. A column's width is the MAX of its entries
                  // (bounded by the ceiling) and its height is one pitch per
                  // entry, so neither term grows without limit. Realistic series
                  // names are what make the difference visible: eight
                  // five-letter greek letters happen to fit the band, and
                  // reading that as "the band is fine" is exactly the mistake.
                  //
                  // Since the 2026-08-18 overflow rule the band no longer draws
                  // off-canvas here — it FALLS BACK to the column — so what this
                  // asserts is that the two routes reach the same picture: an
                  // explicit `Top` these names cannot pack into, and the plain
                  // default, lower identically. That is the strongest available
                  // statement of "the fallback IS the column, not a copy of it".
                  let realistic =
                      { plain with
                          Name = "eight-realistic"
                          Kind = ChartKind.Bar
                          XField = "region"
                          YFields =
                              [ for greek in [ "alpha"; "beta"; "gamma"; "delta"; "epsilon"; "zeta"; "eta"; "theta" ] ->
                                    "monthly_" + greek ]
                          Rows = [ "North", [ for i in 1..8 -> float (i * 10) ] ] }

                  let banded =
                      Charts.lower
                          { specOf realistic with
                              LegendPosition = Some Charts.ChartLegendPosition.Top }
                          (Seq.ofList (buildRows realistic))
                      |> legendSwatches

                  Expect.isTrue
                      (banded |> List.forall (fun (x, y) -> x + 10.0 <= 640.0 && y + 10.0 <= 400.0))
                      "an explicit Top these names cannot pack falls back inside the canvas"

                  let columned =
                      Charts.lower (specOf realistic) (Seq.ofList (buildRows realistic))
                      |> legendSwatches

                  Expect.isTrue
                      (columned |> List.forall (fun (x, y) -> x + 10.0 <= 640.0 && y + 10.0 <= 400.0))
                      "…and the same eight names sit inside the canvas as a column"

                  Expect.equal banded columned "the fallback IS the column — same code path, same geometry"
              }

              test "the BAND overflow rule — one character decides the edge (operator, 2026-08-18)" {
                  // The boundary pair, in the one form that can fail. The two
                  // cases differ by a single trailing `s` in the last field
                  // name: 542.23 px of packed band on one side of 548, 549.38
                  // on the other.
                  let packs = loweredCase "bar-legend-band-packs"
                  let falls = loweredCase "bar-legend-band-overflow-column"

                  Expect.equal
                      (legendSwatches packs |> List.map snd)
                      [ 34.0; 34.0; 34.0; 34.0 ]
                      "below the boundary all four swatches share the one band row"

                  Expect.equal
                      (legendSwatches falls |> List.map snd)
                      [ 64.0; 84.0; 104.0; 124.0 ]
                      "one character past it they are a column, one LegendRowPitchY apart"

                  // Four entries, still four — the column loses nothing, which
                  // is the whole argument against refusing.
                  Expect.equal (List.length (legendSwatches falls)) 4 "no entry is dropped by the fallback"

                  // And the fallback's structural consequences: the column
                  // comes off the PLOT (the band took none), and every swatch
                  // is inside the canvas where the band's fourth would not have
                  // been.
                  Expect.equal (plotRight packs) 612.0 "a band takes no width"
                  Expect.isTrue (plotRight falls < 612.0) "…and the column it falls back to does"

                  Expect.isTrue
                      (legendSwatches falls
                       |> List.forall (fun (x, y) -> x + 10.0 <= 640.0 && y + 10.0 <= 400.0))
                      "every swatch is on the canvas"

                  // The `Bottom` arm's fallback gives back the band it reserved
                  // in the MARGIN — the one thing the `Top` fallback cannot
                  // show. Its case differs from the `Top` one by that single
                  // key, so the two must lower to the SAME picture: the plot
                  // keeps its full height and the x title returns to its
                  // unreserved baseline (`bar-legend-bottom` pins it 21.6 px
                  // higher when the band IS drawn).
                  let bottom = loweredCase "bar-legend-band-bottom-overflow-column"

                  Expect.equal (plotBottom bottom) (plotBottom falls) "a fallen-back Bottom reserves no band"
                  Expect.equal (xAxisTitleY bottom) 388.0 "…so the x title sits at its unreserved baseline"

                  Expect.equal
                      (legendSwatches bottom)
                      (legendSwatches falls)
                      "both band arms fall back to the same column — the rule is uniform"
              }

              test "an explicit position beats the style default, in both directions" {
                  // The wire field wins over the style: `Top` on a chart the
                  // style would have put on the right…
                  Expect.equal
                      (legendSwatches (loweredCase "bar-legend-top") |> List.map snd)
                      [ 34.0; 34.0 ]
                      "the declared Top puts both swatches in the one band row"

                  // …and the style wins where the spec says nothing, which is
                  // what makes the first assertion mean anything.
                  let styled =
                      { Charts.ChartStyle.defaults with
                          LegendPosition = Charts.ChartLegendPosition.Top }

                  let case = cases |> List.find (fun c -> c.Name = "bar-multi")

                  let viaStyle =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          styled
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  Expect.equal
                      (legendSwatches viaStyle |> List.map snd)
                      [ 34.0; 34.0 ]
                      "an absent spec value takes the style's"

                  // …and an explicit `Right` on that same style overrides it
                  // back, so the precedence is a rule rather than a coincidence
                  // of which value happens to be the default.
                  let overridden =
                      Charts.lowerWithStyle
                          Charts.ChartLimits.defaults
                          styled
                          { specOf case with
                              LegendPosition = Some Charts.ChartLegendPosition.Right }
                          (Seq.ofList (buildRows case))

                  Expect.equal
                      (legendSwatches overridden |> List.map snd)
                      [ 64.0; 84.0 ]
                      "the spec beats the style either way"
              }

              test "Bottom mirrors the band below the x-axis title, which moves up to make room" {
                  let ds = loweredCase "bar-legend-bottom"
                  let plain = loweredCase "bar-legend-top"

                  Expect.equal (legendSwatches ds |> List.map snd) [ 378.4; 378.4 ] "the band is the canvas's last line"

                  // The x title rides ABOVE the band — 15.6 px line + 6 px
                  // padding = the 21.6 px it moved up by.
                  Expect.equal (xAxisTitleY plain) 388.0 "the pre-880 baseline, 12 px off the canvas bottom"
                  Expect.equal (xAxisTitleY ds) (r2 (388.0 - 21.6)) "…and one legend band higher when there is one"

                  // The plot lost exactly the band and nothing else.
                  Expect.equal (plotBottom ds) (r2 (plotBottom plain - 21.6)) "the band comes off the plot, once"
                  Expect.equal (plotRight ds) (plotRight plain) "and takes no width — a band is not a column"
              }

              test "None draws no legend AND reserves no space for one" {
                  let ds = loweredCase "bar-legend-none"

                  Expect.isEmpty (legendSwatches ds) "no swatch anywhere"

                  // The second half is the one worth pinning: a suppressed
                  // legend that still shrank the plot would be the worst of
                  // both. The multi-series chart must lay out exactly as the
                  // single-series one does.
                  Expect.equal (plotRight ds) 612.0 "the full width, as if there were one series"
                  Expect.equal (plotBottom ds) (plotBottom (loweredCase "bar-legend-top")) "and the full height"
              }

              test "a single-series cartesian chart still draws no legend, whatever the position says" {
                  // The pre-880 rule, preserved: the title names the series, so
                  // a legend would repeat it. An explicit position does not
                  // conjure one — and, since the position resolves to `None`
                  // when there are no entries, it reserves nothing either.
                  for pos in [ "Top"; "Right"; "Bottom"; "None" ] do
                      let case =
                          { (cases |> List.find (fun c -> c.Name = "bar-single")) with
                              LegendPosition = Some pos }

                      let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                      Expect.isEmpty (legendSwatches ds) (sprintf "%s: one series needs no legend" pos)
                      Expect.equal (plotRight ds) 612.0 (sprintf "%s: …and reserves no room for one" pos)
              }

              test "the pie legends its CATEGORIES — one series, and still a legend" {
                  // The asymmetry is deliberate and is the reason the two
                  // emitters could not simply be merged on the series count: a
                  // pie's palette roles are its categories, so its legend is
                  // over four things where its series count is one.
                  Expect.equal
                      (List.length (legendSwatches (loweredCase "pie-quarters")))
                      4
                      "four categories, four rows"

                  Expect.equal
                      (legendTextsOf (loweredCase "pie-quarters"))
                      [ "N (25%)"; "E (25%)"; "S (25%)"; "W (25%)" ]
                      "the shares survived the unification unchanged"
              }

              test "the pie honours an explicit position like every other arm" {
                  let ds = loweredCase "pie-legend-top"

                  Expect.equal (legendSwatches ds |> List.map snd) [ 34.0; 34.0; 34.0 ] "a band, on the polar arm"

                  Expect.equal
                      (legendTextsOf ds)
                      [ "Ops (40%)"; "R&D (35%)"; "Sales (25%)" ]
                      "…carrying the same NN% labels the column does"
              }

              test "a refused pie draws no legend either" {
                  // A legend for a picture the lowering declined to draw would
                  // be a claim about data it refused to show.
                  let case =
                      { (cases |> List.find (fun c -> c.Name = "pie-single")) with
                          Rows = [ "A", [ 40.0 ]; "B", [ -10.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))
                  Expect.isEmpty (legendSwatches ds) "no geometry, no legend"
              }

              test "the column truncates a pathological name rather than eating the plot" {
                  let ds = loweredCase "bar-legend-column-truncation"
                  let texts = legendTextsOf ds

                  Expect.isTrue (texts |> List.forall (fun t -> t.EndsWith "…")) "both names came back ellipsised"

                  // The bound is a share of the CANVAS (30 %), not of whatever
                  // the plot has left — a budget derived from the thing it is
                  // about to decide is how a layout loops.
                  let columnW = 640.0 - 28.0 - plotRight ds
                  Expect.isTrue (columnW <= 0.3 * 640.0) "the column is inside its ceiling"

                  Expect.isTrue
                      (plotRight ds - 64.0 > 0.5 * 640.0)
                      "…so the plot keeps the clear majority of the canvas"
              }

              // ── Phase 881 — selective data labels ──
              //
              // The goldens pin the pictures; these pin the RULES. The reader
              // keys off `DataLabelFontSize`, which no other shape uses, so a
              // test cannot pass by finding a tick.

              test "OFF is the default AND what an absent field means — the pre-881 picture, byte-for-byte" {
                  // THE REGRESSION GUARD, stated as an assertion rather than
                  // left to the corpus. Every case that declares no labels must
                  // draw none, and must lower to the same bytes with the field
                  // absent as with it explicitly `Off`.
                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  for case in cases do
                      if Option.isNone case.DataLabels then
                          Expect.isEmpty
                              (dataLabelsOfDrawing (loweredCase case.Name))
                              (sprintf "%s: an absent dataLabels drew a label" case.Name)

                          let explicitlyOff =
                              Charts.lowerWithStyle
                                  Charts.ChartLimits.defaults
                                  (styleOf case)
                                  { specOf case with
                                      DataLabels = Some Charts.ChartDataLabels.Off }
                                  (Seq.ofList (buildRows case))

                          Expect.equal
                              (enc explicitlyOff)
                              (enc (loweredCase case.Name))
                              (sprintf "%s: explicit Off differs from absent" case.Name)
              }

              test "grouped bars label every cap; stacked bars label only the TOTAL" {
                  // Three categories × two series = six caps…
                  Expect.equal
                      (dataLabelTextsOf "bar-labels-grouped")
                      [ "80"; "130"; "60"; "100"; "110"; "90" ]
                      "one label per bar, in series-then-category emission order"

                  // …and three categories × three series = three labels, not
                  // nine. This is what makes "interior segments carry no label"
                  // a rule rather than a coincidence of the data.
                  Expect.equal
                      (dataLabelTextsOf "bar-labels-stacked")
                      [ "20"; "22"; "18" ]
                      "the stack total, once per category"
              }

              test "a label sits ABOVE a positive cap and BELOW a negative one" {
                  let labels = dataLabelsOfDrawing (loweredCase "bar-labels-negative")

                  Expect.equal
                      (labels |> List.map (fun (_, _, t) -> t))
                      [ "12"; "-8"; "5"; "-14" ]
                      "every cap labelled, in category order"

                  let yOf (t: string) =
                      labels |> List.pick (fun (_, y, s) -> if s = t then Some y else None)

                  // Canvas y grows downward, so "above the cap" is the smaller
                  // number. The positive caps are higher than the negative ones
                  // in the picture AND their labels are higher still, which is
                  // the readable form of "the two placements are mirrors".
                  Expect.isTrue (yOf "12" < yOf "-8") "the positive label is above the negative one"
                  Expect.isTrue (yOf "5" < yOf "-14") "…and so is the smaller pair's"
              }

              test "lines label the LAST point of each series, right of the endpoint" {
                  let ds = loweredCase "line-labels-ends"
                  let labels = dataLabelsOfDrawing ds

                  Expect.equal (labels |> List.map (fun (_, _, t) -> t)) [ "28"; "52" ] "the final cpu and mem values"

                  // Both share the endpoint's x + the offset, and both sit
                  // inside the plot's right edge — the legend column starts
                  // there, and running into it is the collision the width
                  // budget refuses.
                  let xs = labels |> List.map (fun (x, _, _) -> x) |> List.distinct
                  Expect.equal (List.length xs) 1 "one x for every endpoint label"

                  Expect.isTrue
                      (xs |> List.forall (fun x -> x < plotRight ds))
                      "…and it is inside the plot, clear of the legend column"
              }

              test "labels agree with the AXIS — same display unit, same step-derived precision" {
                  // The chart scales to millions, so the ticks read 0/5/10/…
                  // and the labels read the same magnitudes at the same
                  // precision. A label more precise than its own axis would be
                  // a chart disagreeing with itself.
                  Expect.equal (yTicksOf "bar-labels-millions") [ "0"; "5"; "10"; "15"; "20" ] "the axis is in millions"

                  Expect.equal
                      (dataLabelTextsOf "bar-labels-millions")
                      [ "13"; "10"; "15"; "11" ]
                      "…and so are the labels, at the axis's own precision"

                  Expect.equal (axisUnitLabelOf "bar-labels-millions") "Millions" "stated once, in the unit slot"
              }

              test "the fit gate SUPPRESSES rather than clipping — the boundary, both sides" {
                  // The pair differs by ONE category. Everything the labels
                  // depend on but the band pitch is held constant: the seventh
                  // value sits inside the existing domain, so the ticks, the
                  // margins and the label texts are identical.
                  Expect.equal
                      (yTicksOf "bar-labels-fit-boundary")
                      (yTicksOf "bar-labels-suppress-boundary")
                      "same axis on both sides of the boundary"

                  Expect.equal
                      (dataLabelTextsOf "bar-labels-fit-boundary")
                      [ "1,250,000"; "1,480,000"; "1,100,000"; "1,690,000"; "1,320,000"; "1,550,000" ]
                      "six categories: every label is admitted"

                  // …and one more category takes the pitch under the budget, so
                  // the labels are ABSENT. Not truncated, not overlapped, not
                  // relocated inside the bar — absent, with the values still on
                  // the axis and still under the pointer.
                  Expect.isEmpty
                      (dataLabelsOfDrawing (loweredCase "bar-labels-suppress-boundary"))
                      "seven categories: every label is suppressed"
              }

              test "endpoint labels that would overlap yield in series order" {
                  // Two series ending at the SAME value share one baseline, so
                  // the second cannot be drawn without writing over the first.
                  // The earlier series keeps its number — deterministic, and
                  // the same answer on every host.
                  let case =
                      { (cases |> List.find (fun c -> c.Name = "line-labels-ends")) with
                          Rows = [ "Mon", [ 20.0; 55.0 ]; "Tue", [ 35.0; 60.0 ]; "Wed", [ 40.0; 40.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  Expect.equal
                      (dataLabelsOfDrawing ds |> List.map (fun (_, _, t) -> t))
                      [ "40" ]
                      "one label where two would have collided"
              }

              test "scatter and pie carry no data labels, by decision" {
                  // Recorded rather than merely absent: a scatter's ends are not
                  // privileged points (its x is a value axis, so the last ROW
                  // means nothing), and a pie's legend already carries the
                  // shares.
                  for name in [ "scatter-single"; "scatter-multi"; "pie-single"; "pie-quarters" ] do
                      let case =
                          { (cases |> List.find (fun c -> c.Name = name)) with
                              DataLabels = Some "Ends" }

                      let ds =
                          Charts.lowerWithStyle
                              Charts.ChartLimits.defaults
                              (styleOf case)
                              (specOf case)
                              (Seq.ofList (buildRows case))

                      Expect.isEmpty (dataLabelsOfDrawing ds) (sprintf "%s: Ends must draw nothing" name)
              }

              test "a data label wears LABEL-ROLE ink, never the series colour" {
                  let ds = loweredCase "bar-labels-grouped"

                  for sh in ds.Shapes do
                      match sh with
                      | Shape.Label(_, _, _, s) when s.FontSize = Some 12.0 ->
                          let fill =
                              match s.Fill with
                              | Some(Binding.Static(Some c)) -> c
                              | _ -> "<not a static colour>"

                          let opacity =
                              match s.Opacity with
                              | Some(Binding.Static(Some o)) -> o
                              | _ -> nan

                          Expect.equal fill "currentColor" "inked from the surface, not the palette"
                          Expect.equal opacity 0.66 "at the chrome label opacity"
                      | _ -> ()
              }

              test "no data label ever moved a margin — the plot is where Off left it" {
                  // The layout is decided before any label is placed, so a
                  // labelled chart and its unlabelled twin must have the SAME
                  // plot rectangle. This is why `Off` can be byte-identical
                  // rather than merely similar.
                  let labelled = loweredCase "bar-labels-grouped"
                  let plainer = loweredCase "bar-multi"

                  Expect.equal (plotRight labelled) (plotRight plainer) "same right edge"
                  Expect.equal (plotBottom labelled) (plotBottom plainer) "same bottom edge"
              }

              // ── Phase 882 — the temporal x-axis ──
              //
              // The calendar arithmetic is tested DIRECTLY (it is a normative
              // spec five hosts mirror, so its properties are worth asserting
              // rather than inferring from pixel positions), and the lowering
              // behaviour is read back off the drawing through the same
              // discriminators the emitter used.

              test "the calendar conversions are exact inverses across four centuries" {
                  // The property that matters: `civilFromDays` and
                  // `daysFromCivil` round-trip for EVERY day, including the
                  // negative side of the epoch and the century leap rules. A
                  // coprime stride samples all residues rather than a lattice.
                  let mutable failures = 0

                  for d in -100000..977..100000 do
                      let y, m, dd = Charts.Temporal.civilFromDays d

                      if Charts.Temporal.daysFromCivil y m dd <> d then
                          failures <- failures + 1

                  Expect.equal failures 0 "every sampled day round-trips"

                  // The anchors, stated so a port has fixed points to check.
                  Expect.equal (Charts.Temporal.civilFromDays 0) (1970, 1, 1) "day 0 is the epoch"
                  Expect.equal (Charts.Temporal.daysFromCivil 1970 1 1) 0 "and back"
                  Expect.equal (Charts.Temporal.daysFromCivil 1969 12 31) -1 "the day before is -1"

                  // 2000 is a leap year (÷400), 1900 is not (÷100, not ÷400) —
                  // the pair that a naive four-year rule gets wrong.
                  Expect.isTrue (Charts.Temporal.isLeapYear 2000) "2000 is leap"
                  Expect.isFalse (Charts.Temporal.isLeapYear 1900) "1900 is not"

                  Expect.equal
                      (Charts.Temporal.daysFromCivil 2000 3 1 - Charts.Temporal.daysFromCivil 2000 2 1)
                      29
                      "February 2000 has 29 days"

                  Expect.equal
                      (Charts.Temporal.daysFromCivil 1900 3 1 - Charts.Temporal.daysFromCivil 1900 2 1)
                      28
                      "February 1900 has 28"
              }

              test "the ISO date parser is strict, and a timestamp keeps only its date" {
                  let ok (s: string) =
                      Option.isSome (Charts.Temporal.tryParseDay s)

                  Expect.isTrue (ok "2026-01-15") "the canonical form"
                  Expect.isTrue (ok "2000-02-29") "a real leap day"

                  // A timestamp's TIME-OF-DAY is discarded — the axis's unit is
                  // the day, so 00:01 and 23:59 are the same value. That is the
                  // whole of the time-zone policy, and it is why no host needs
                  // one.
                  Expect.equal
                      (Charts.Temporal.tryParseDay "2026-01-15T10:30:00Z")
                      (Charts.Temporal.tryParseDay "2026-01-15")
                      "a timestamp reads as its UTC date"

                  // Refused: an impossible calendar date, a locale spelling, a
                  // bare year, and a plausible-looking near-miss. Admitting any
                  // of them would be the string-sniffing this axis exists to
                  // avoid.
                  for bad in
                      [ "1900-02-29"
                        "2026-13-01"
                        "2026-00-10"
                        "2026-01-32"
                        "15/01/2026"
                        "2026"
                        "" ] do
                      Expect.isFalse (ok bad) (sprintf "'%s' is not a canonical ISO date" bad)

                  // And an unparseable cell reads as the EPOCH rather than
                  // throwing — the lowering stays total; FUARAN097 is the loud
                  // part, upstream.
                  Expect.equal (Charts.Temporal.dayOf "not a date") 0 "unparseable reads as day 0"
              }

              test "the tick ladder picks a calendar-nice step and formats to the granularity" {
                  // The three regimes, read off the CHOSEN RUNG rather than off
                  // the picture: one rung decides both the positions and the
                  // format, so this is the single decision the fixtures then pin
                  // in pixels.
                  let stepOf (case: Case) =
                      let days = case.Rows |> List.map (fst >> Charts.Temporal.dayOf) |> List.toArray

                      let lo, hi = Charts.Temporal.domain days
                      Charts.Temporal.chooseStep 6 lo hi

                  let named (name: string) =
                      stepOf (cases |> List.find (fun c -> c.Name = name))

                  let expect (name: string) (unit: Charts.Temporal.Unit) (count: int) (sample: string) =
                      let step = named name
                      Expect.equal step.Unit unit (sprintf "%s: unit" name)
                      Expect.equal step.Count count (sprintf "%s: count" name)

                      let case = cases |> List.find (fun c -> c.Name = name)
                      let first = Charts.Temporal.dayOf (fst (List.head case.Rows))

                      Expect.equal (Charts.Temporal.label step first) sample (sprintf "%s: label shape" name)

                  expect "line-temporal-daily" Charts.Temporal.Unit.Days 5 "05 Jan 26"
                  expect "bar-temporal-monthly" Charts.Temporal.Unit.Months 6 "Jan 26"
                  expect "line-temporal-yearly" Charts.Temporal.Unit.Years 2 "2017"

                  // The FORMAT BOUNDARIES: the adjacent rungs the two thresholds
                  // separate. 10 days is the last nominal under 27; one month
                  // (30.436875) the first over. Six months (182.6) is the last
                  // under 365; one year (365.2425) the first over. Under a
                  // nominal-step rule these pairs ARE the boundary — a span
                  // cannot approach a threshold continuously, because the rungs
                  // jump.
                  expect "line-temporal-format-day-boundary" Charts.Temporal.Unit.Days 10 "02 Mar 26"
                  expect "line-temporal-format-month-boundary" Charts.Temporal.Unit.Months 1 "Jan 26"
                  expect "line-temporal-format-halfyear-boundary" Charts.Temporal.Unit.Months 6 "Jan 24"
                  expect "line-temporal-format-year-boundary" Charts.Temporal.Unit.Years 1 "2021"

                  // The thresholds themselves, stated on the nominals so a port
                  // can check the arithmetic without a fixture.
                  let nominal u c =
                      Charts.Temporal.nominalDays { Unit = u; Count = c }

                  Expect.isTrue (nominal Charts.Temporal.Unit.Days 10 <= 27.0) "10 days is under the day threshold"

                  Expect.isTrue
                      (nominal Charts.Temporal.Unit.Months 1 > 27.0)
                      "one month clears it (30.436875, the mean Gregorian month)"

                  Expect.isTrue (nominal Charts.Temporal.Unit.Months 6 <= 365.0) "six months stays under the year one"

                  Expect.isTrue
                      (nominal Charts.Temporal.Unit.Years 1 > 365.0)
                      "one year clears it (365.2425, the mean Gregorian year)"

                  // Every rung's tick count fits the ceiling, and the ladder is
                  // total: a millennium-wide domain still resolves, and it does
                  // so without generating a tick per day on the way.
                  let wide =
                      Charts.Temporal.chooseStep
                          6
                          (Charts.Temporal.daysFromCivil 1000 1 1)
                          (Charts.Temporal.daysFromCivil 2000 1 1)

                  Expect.equal wide.Unit Charts.Temporal.Unit.Years "a millennium ticks in years"
                  Expect.isTrue (wide.Count >= 200) "and in a coarse multiple of them"
              }

              test "the month and year rungs land on calendar boundaries, never on data offsets" {
                  // The quarters fall out of the alignment rule rather than being
                  // a case of their own: `(month-1) mod 3 = 0` IS Jan/Apr/Jul/Oct.
                  let lo = Charts.Temporal.daysFromCivil 2026 1 15
                  let hi = Charts.Temporal.daysFromCivil 2027 12 20

                  let quarters =
                      Charts.Temporal.ticks
                          { Unit = Charts.Temporal.Unit.Months
                            Count = 3 }
                          lo
                          hi
                      |> List.map (fun d ->
                          let y, m, dd = Charts.Temporal.civilFromDays d
                          y, m, dd)

                  Expect.isTrue
                      (quarters |> List.forall (fun (_, m, d) -> d = 1 && (m - 1) % 3 = 0))
                      "every quarter tick is a quarter-month's 1st"

                  Expect.equal (List.head quarters) (2026, 4, 1) "the first is INSIDE the domain, not at its start"

                  // A year rung anchors on the January 1 of years divisible by
                  // the step — so a decade chart ticks 2020, 2030, not
                  // 2021, 2031.
                  let decades =
                      Charts.Temporal.ticks
                          { Unit = Charts.Temporal.Unit.Years
                            Count = 10 }
                          (Charts.Temporal.daysFromCivil 2013 6 1)
                          (Charts.Temporal.daysFromCivil 2044 6 1)
                      |> List.map (fun d ->
                          let y, _, _ = Charts.Temporal.civilFromDays d
                          y)

                  Expect.equal decades [ 2020; 2030; 2040 ] "decades land on the decade"

                  // A DAY rung steps from the domain's own start, because a
                  // "nice" 5-day boundary does not exist.
                  let fives =
                      Charts.Temporal.ticks
                          { Unit = Charts.Temporal.Unit.Days
                            Count = 5 }
                          100
                          118

                  Expect.equal fives [ 100; 105; 110; 115 ] "day ticks step from the first datum"
              }

              test "a temporal axis is CONTINUOUS — marks at the dates, labels centred on them" {
                  // The same reader Phase 903's band/continuous test uses: the x
                  // marks are the short segments hanging below the spine.
                  let ds = loweredCase "line-temporal-daily"

                  let spineY =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(x1, y1, x2, y2, _) when y1 = y2 && x1 < x2 -> Some y1
                          | _ -> None)
                      |> List.max

                  let markXs =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(x1, y1, x2, y2, _) when x1 = x2 && y1 = spineY && y2 > spineY -> Some x1
                          | _ -> None)
                      |> List.sort

                  // SIX ticks from thirty rows: the count follows the tick rule,
                  // not the row count — which is the whole difference from a band
                  // axis, where it would be thirty-one boundaries.
                  Expect.equal (List.length markXs) 6 "the ladder's ticks, not the rows"

                  let labelled =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(x, y, TextSource.Literal t, s) when
                              Option.isSome s.Opacity
                              && s.TextAnchor = Some TextAnchor.Middle
                              && Option.isNone s.Rotation
                              && y > spineY
                              ->
                              Some(x, t)
                          | _ -> None)
                      |> List.sortBy fst

                  Expect.equal (List.map fst labelled) markXs "a continuous label sits AT its mark, not beside it"

                  Expect.equal
                      (List.map snd labelled)
                      [ "05 Jan 26"; "10 Jan 26"; "15 Jan 26"; "20 Jan 26"; "25 Jan 26"; "30 Jan 26" ]
                      "and reads at the data's own granularity"

                  // Vertical gridlines follow from the axis being continuous, so
                  // a temporal BAR chart has them too — the rule is a property,
                  // not a kind list.
                  let verticalRules (name: string) =
                      let d = loweredCase name

                      d.Shapes
                      |> List.filter (fun sh ->
                          match sh with
                          | Shape.Line(x1, y1, x2, y2, s) ->
                              // The GRID opacity is the discriminator (0.12,
                              // used by no other stroke); the axis spines and
                              // the tick marks carry `AxisOpacity`.
                              x1 = x2
                              && y2 > y1
                              && (match s.Opacity with
                                  | Some(Binding.Static(Some o)) -> o = Charts.ChartStyle.defaults.GridOpacity
                                  | _ -> false)
                          | _ -> false)
                      |> List.length

                  Expect.equal (verticalRules "bar-temporal-monthly") 4 "a temporal bar axis rules its dates"
                  Expect.equal (verticalRules "bar-single") 0 "a band axis has no positions to rule"
              }

              test "a date's position is its VALUE, so an irregular run is not evenly spaced" {
                  // The point of a temporal axis over a band one: 1 Jan, 2 Jan
                  // and 1 Feb are not three equal steps. A band axis would draw
                  // them evenly and silently misstate the data.
                  let case =
                      { plain with
                          Name = "probe"
                          Kind = ChartKind.Line
                          XField = "day"
                          YFields = [ "v" ]
                          XScale = Some "Temporal"
                          Rows = [ "2026-01-01", [ 1.0 ]; "2026-01-02", [ 2.0 ]; "2026-02-01", [ 3.0 ] ] }

                  let ds = Charts.lower (specOf case) (Seq.ofList (buildRows case))

                  let pts =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Polyline(ps, _) -> Some ps
                          | _ -> None)
                      |> List.map (fun p -> p.X)

                  match pts with
                  | [ a; b; c ] ->
                      // One day out of thirty-one: the second point sits hard
                      // against the first, and the third at the far edge.
                      Expect.isTrue (b - a < (c - b) / 10.0) "one day is a thirtieth of the span, and is drawn so"

                      // The same rows as a CATEGORY axis space evenly — the
                      // contrast that makes the feature worth having.
                      let bandDs =
                          Charts.lower (specOf { case with XScale = None }) (Seq.ofList (buildRows case))

                      let bandPts =
                          bandDs.Shapes
                          |> List.pick (fun sh ->
                              match sh with
                              | Shape.Polyline(ps, _) -> Some ps
                              | _ -> None)
                          |> List.map (fun p -> p.X)

                      match bandPts with
                      | [ p; q; r ] -> Expect.equal (r2 (q - p)) (r2 (r - q)) "a band axis spaces them evenly"
                      | _ -> failtest "expected three band points"
                  | _ -> failtest "expected three temporal points"
              }

              test "a temporal axis suppresses its DEFAULT x-title, never an explicit one" {
                  // §4e's rule, stated by Phase 878 and wired here. It
                  // suppresses the machine's fallback: an axis reading "Jan Feb
                  // Mar" does not need the word "Month" beneath it.
                  Expect.equal (xAxisTitleOf (loweredCase "line-temporal-daily")) "" "no fallback title on a date axis"

                  // The band twin of the same chart DOES title itself, so the
                  // suppression is attributable to the scale and to nothing else.
                  let case = cases |> List.find (fun c -> c.Name = "line-temporal-daily")

                  let banded =
                      Charts.lower (specOf { case with XScale = None }) (Seq.ofList (buildRows case))

                  Expect.equal (xAxisTitleOf banded) "Day" "a category axis still falls back to the field name"

                  // And an explicit title always draws — the author overriding
                  // the default, which the rule never touches.
                  Expect.equal
                      (xAxisTitleOf (loweredCase "bar-temporal-x-title"))
                      "Reporting month"
                      "the author's own words survive the suppression"

                  // The y axis is untouched: the rule is about the x axis's
                  // self-evidence, not about titles in general.
                  Expect.equal (yAxisTitleOf (loweredCase "line-temporal-daily")) "Sessions" "the y title is unaffected"
              }

              test "the label ladder governs a temporal axis's tick labels too" {
                  // Every ordinary temporal fixture rests FLAT — six short date
                  // labels in a comfortable pitch — and the crowded one
                  // escalates. §4g's arithmetic is unchanged: below a ~58 px
                  // pitch the 30° window is empty, so the step is flat →
                  // vertical.
                  // Keyed on the x-label BASELINE exactly — `plotY1 +
                  // CategoryLabelOffsetY` — because the y axis's lowest tick
                  // label also sits below the plot bottom, and a looser reader
                  // picks it up and reports a flat axis whatever the x labels do.
                  let rotations (name: string) =
                      let ds = loweredCase name
                      let baseline = r2 (plotBottom ds + Charts.ChartStyle.defaults.CategoryLabelOffsetY)

                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, y, _, s) when Option.isSome s.Opacity && y = baseline -> Some s.Rotation
                          | _ -> None)
                      |> List.distinct

                  Expect.equal (rotations "line-temporal-daily") [ None ] "a roomy date axis reads flat"

                  Expect.equal
                      (rotations "line-temporal-vertical-labels")
                      [ Some -90.0 ]
                      "a crowded one goes vertical — uniformly, never a mix"
              }

              test "an absent xScale is byte-identical to an explicit Category" {
                  // The stronger form the corpus cannot state: the default is not
                  // merely similar to absence, it is the same bytes. Which is why
                  // every pre-882 golden is unmoved.
                  let enc (spec: ChartSpec<obj>) (rows: Row list) =
                      let ds = Charts.lower spec (Seq.ofList rows)
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  for case in cases |> List.filter (fun c -> Option.isNone c.XScale) do
                      let rows = buildRows case
                      let baseSpec = specOf case

                      Expect.equal
                          (enc
                              { baseSpec with
                                  XScale = Some ChartXScale.Category }
                              rows)
                          (enc baseSpec rows)
                          (sprintf "%s: Category must be indistinguishable from absent" case.Name)
              }

              test "a temporal declaration on a Pie is inert — the polar arm has no x axis" {
                  // Dead intent the lowering cannot honour, neutralised rather
                  // than half-applied: a pie's picture must not depend on a scale
                  // it never reads.
                  let case = cases |> List.find (fun c -> c.Name = "pie-quarters")
                  let rows = buildRows case

                  let enc (spec: ChartSpec<obj>) =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" (Charts.lower spec (Seq.ofList rows)): Node<obj>)

                  Expect.equal
                      (enc
                          { specOf case with
                              XScale = Some ChartXScale.Temporal })
                      (enc (specOf case))
                      "a pie is unchanged by an x-scale declaration"
              }

              // ── Phase 921 — the accessible summary ──
              //
              // The goldens above already pin the summary byte-for-byte on every
              // fixture (it is a field of the Drawing they encode). These pin
              // the GRAMMAR's arms by name, so a rewrite that keeps the goldens
              // passing by regenerating them still has to answer for each rule.

              test "every lowered chart carries a summary, and it opens with the kind" {
                  let expectations =
                      [ "bar-single", "Bar chart"
                        "bar-stacked", "Stacked bar chart"
                        "line-single", "Line chart"
                        "area-single", "Area chart"
                        "area-stacked", "Stacked area chart"
                        "scatter-single", "Scatter chart"
                        "pie-single", "Pie chart" ]

                  for name, kindWords in expectations do
                      let summary = summaryOf name

                      Expect.stringStarts summary (kindWords + ".") (name + ": the opening clause is the kind")
                      Expect.stringEnds summary "." (name + ": the summary is terminated")
              }

              test "the series clause names up to four, then folds to a count" {
                  Expect.stringContains (summaryOf "bar-multi") "2 series: sales, target" "two series are both named"

                  Expect.stringContains
                      (summaryOf "bar-legend-band-packs")
                      "4 series: support_revenue, training_revenue, product_revenue, usage_revenue."
                      "four is the last count named in full — no fold, and the clause simply ends"

                  Expect.stringContains
                      (summaryOf "bar-summary-fold-boundary")
                      "5 series: alpha, beta, gamma, delta, and 1 more"
                      "five is the first count that folds — and the remainder is singular"

                  Expect.stringContains
                      (summaryOf "bar-legend-eight-series")
                      "8 series: alpha, beta, gamma, delta, and 4 more"
                      "eight folds to the same four names plus a count"

                  Expect.stringContains (summaryOf "bar-single") "1 series: revenue" "a single series is named"
              }

              test "the extent clause follows the X AXIS's kind, not the chart's" {
                  // A band axis states its first and last CATEGORY…
                  Expect.stringContains
                      (summaryOf "bar-multi")
                      "3 categories: North to East"
                      "a band axis states the category extent"

                  // …a continuous axis states its DOMAIN, in that axis's own
                  // tick format — the 882 calendar label on a temporal axis, the
                  // 876 number formatter on the Scatter arm's numeric x.
                  let temporal = summaryOf "line-temporal-yearly"

                  Expect.stringContains temporal " points: " temporal

                  Expect.isTrue
                      (temporal.Contains "2010 to " || temporal.Contains " to 20")
                      ("dates in the axis's own format: " + temporal)

                  Expect.stringContains (summaryOf "scatter-single") " points: " "a numeric x axis states a point count"
              }

              test "the peak names ONE datum, through the value axis's own rendering" {
                  // `bar-multi` — target 110 at South is the largest single
                  // value (sales peaks at 130… which is larger, so THAT is the
                  // peak). The clause names the series, the category and the
                  // number in the axis's format.
                  Expect.stringContains (summaryOf "bar-multi") "Peak sales at South, 130" "the largest single datum"

                  // A STACKED chart's peak is still a datum, never the stack
                  // total — the clause names one series at one category, and a
                  // total belongs to neither.
                  let stacked = summaryOf "bar-stacked"
                  Expect.stringContains stacked "Peak " "a stacked chart still names its peak datum"

                  Expect.isFalse
                      (stacked.Contains "Peak sales at North, 3")
                      "the peak is not a cumulative total (a 3-digit North total would be)"

                  // The DISPLAY UNIT is stated in the axis's own words, so the
                  // summary and the axis agree about the magnitude.
                  Expect.stringContains
                      (summaryOf "bar-millions")
                      " Millions"
                      "the axis's display unit is stated in the summary"
              }

              test "names are clamped, and hostile text survives as DATA" {
                  let summary = summaryOf "bar-summary-hostile-text"

                  // The 32-character clamp bites in both the series list and the
                  // peak clause, and marks the cut.
                  Expect.stringContains
                      summary
                      "1 series: monthly_recurring_revenue_in_po…"
                      "an overlong series name is clamped with the ellipsis"

                  Expect.stringContains
                      summary
                      "a region name comfortably past …"
                      "an overlong category name is clamped too"

                  // The clamp is exactly 32 units INCLUDING the ellipsis — the
                  // one property a "roughly this long" cap would not have.
                  Expect.equal "monthly_recurring_revenue_in_po…".Length 32 "the clamped name is exactly at the cap"

                  // The markup metacharacters are carried verbatim — escaping is
                  // the RENDERER's job (`DrawingSvg.escape`), and doing it here
                  // would double-escape.
                  Expect.stringContains summary "<script>" "hostile text is carried as data, unescaped"

                  Expect.isTrue (summary.Length <= 320) "the whole summary is inside its cap"
              }

              // ── Phase 1494 — the annotation clauses ──
              //
              // The goldens pin these byte-for-byte like every other summary
              // arm; these pin the GRAMMAR by name, so a rewrite that kept the
              // goldens green by regenerating them still answers for each rule.

              test "each annotation member gets its own clause, after the data clauses" {
                  let reference = summaryOf "line-reference-zero"

                  Expect.stringContains reference "1 reference line: 0" "one reference line, named by its value"

                  Expect.isTrue
                      (reference.IndexOf "Peak " < reference.IndexOf "1 reference line")
                      ("the annotation clause follows the data clauses: " + reference)

                  Expect.stringContains
                      (summaryOf "line-reference-labelled")
                      "2 reference lines: 250 (SLO), 350 (Breach)"
                      "a Literal label is bracketed after its address, in document order"

                  Expect.stringContains
                      (summaryOf "bar-event-single")
                      "1 event: Q3 (Repricing)"
                      "a band-axis event is named by its category key"

                  Expect.stringContains
                      (summaryOf "line-band-y-tolerance")
                      "1 band: 200 to 260 (Tolerance)"
                      "a value band states its pair in the value axis's own rendering"

                  Expect.stringContains
                      (summaryOf "bar-band-x-categories")
                      "1 band: Q2 to Q3 (Freeze)"
                      "a category band states its pair in the band axis's keys"

                  // Both cases present, each in its own clause, in the
                  // `ChartAnnotation` declaration order.
                  let both = summaryOf "bar-band-x-categories"

                  Expect.isTrue
                      (both.IndexOf "1 reference line" < both.IndexOf "1 band")
                      ("reference lines are announced before bands: " + both)
              }

              test "a temporal address is announced in the axis's own tick format" {
                  // Never the authored ISO string: clause 3 has already stated
                  // how this axis writes a date, and two spellings in one
                  // summary would disagree with the picture about one of them.
                  let five = summaryOf "line-temporal-events-five"

                  Expect.stringContains five "(Dot-com)" "the marker's Literal label is announced"
                  Expect.isFalse (five.Contains "2000-03-10") ("the authored ISO form is not announced: " + five)

                  let period = summaryOf "line-temporal-band-x-period"

                  Expect.stringContains period "1 band: " "a temporal band is announced"
                  Expect.stringContains period "(Recession)" "…with its label, though the drawn one is suppressed"
                  Expect.isFalse (period.Contains "2008-04-01") ("the authored ISO form is not announced: " + period)
              }

              test "an annotation clause folds at four, exactly as the series clause does" {
                  Expect.stringContains
                      (summaryOf "line-temporal-events-five")
                      ", and 1 more"
                      "five markers fold to four named plus a singular count"
              }

              test "a SUPPRESSED label is still announced — suppression is about ink, not meaning" {
                  // `line-temporal-events-collide` draws three markers and one
                  // label: the first two labels have a few pixels of budget and
                  // the fit gate refuses them. All three are named here.
                  let collide = summaryOf "line-temporal-events-collide"

                  Expect.stringContains collide "3 events: " "all three markers are announced"
                  Expect.stringContains collide "(First review window)" "the first suppressed label is announced"
                  Expect.stringContains collide "(Second review window)" "the second suppressed label is announced"
                  Expect.stringContains collide "(Sign-off)" "the drawn label is announced too"

                  // The drawing itself carries only the third label, which is
                  // what makes the assertions above a statement about the
                  // summary rather than about the fit gate.
                  let drawn = literalTexts (loweredCase "line-temporal-events-collide")

                  Expect.isTrue (List.contains "Sign-off" drawn) "the third label is drawn"

                  Expect.isFalse
                      (List.contains "First review window" drawn)
                      "the first label was suppressed by the fit gate"

                  Expect.isFalse
                      (List.contains "Second review window" drawn)
                      "the second label was suppressed by the fit gate"
              }

              test "a NON-Literal label leaves the annotation named by its address alone" {
                  // The same honest boundary the fit gate draws: the text
                  // behind a `Bound` arm is not known at lowering, and
                  // announcing something that is not the text drawn is silently
                  // wrong. The address is always stated, so the annotation is
                  // never unannounced.
                  let bound = summaryOf "line-reference-labelled-bound-label"

                  Expect.stringContains bound "1 reference line: 250" "the address is announced"
                  Expect.isFalse (bound.Contains "(") ("no label is bracketed for a Bound arm: " + bound)
                  Expect.isFalse (bound.Contains "live binding") ("the binding's text is not announced: " + bound)
              }

              test "an annotation LABEL is untrusted text, clamped and carried as data" {
                  let summary = summaryOf "bar-annotation-hostile-text"

                  Expect.stringContains
                      summary
                      "1 reference line: 160 (<b>target</b> & \"stretch\", a la…)"
                      "the label is bracketed, clamped at 32 with the ellipsis, and carried verbatim"

                  Expect.isTrue (summary.Length <= 320) "the whole summary is inside its cap"

                  // The DRAWN label carries the same authored string WHOLE —
                  // the clamp is the summary's own bound, not the label's, and
                  // the two bytes side by side are what say so.
                  let drawn = literalTexts (loweredCase "bar-annotation-hostile-text")

                  Expect.isTrue
                      (List.contains "<b>target</b> & \"stretch\", a label comfortably past the clamp" drawn)
                      "the drawn label is the authored string, unclamped"
              }

              test "a chart with no annotations gains no clause" {
                  // The stability claim the phase makes: every pre-1494 golden
                  // that carries no annotation is byte-unchanged, which shows
                  // here as a summary that ends at its peak clause.
                  let unannotated = summaryOf "bar-multi"

                  Expect.isFalse (unannotated.Contains "reference line") "no reference-line clause"
                  Expect.isFalse (unannotated.Contains " event") "no event clause"
                  Expect.isFalse (unannotated.Contains " band") "no band clause"
              }

              test "a REFUSED pie announces nothing — the Phase 880 legend rule, in words" {
                  // A refused pie draws no geometry and no legend, because
                  // either would be a claim about data the drawing declined to
                  // show. A summary is the same claim.
                  let case = cases |> List.find (fun c -> c.Name = "pie-single")

                  let refused =
                      Charts.lower
                          { specOf case with
                              YFields = [ "value"; "other" ] }
                          (Seq.ofList (
                              buildRows
                                  { case with
                                      YFields = [ "value"; "other" ]
                                      Rows = [ "A", [ 1.0; 2.0 ]; "B", [ 3.0; 4.0 ] ] }
                          ))

                  Expect.isNone refused.Description "a refused pie carries no summary"

                  // …matching the geometry it already declined to draw: no
                  // wedges (a `Curve` or a lone-100% `Circle`) and no legend
                  // swatch (a rounded `Rectangle`). The chart title still draws
                  // — a refusal is not an untitled picture.
                  let claimShapes =
                      refused.Shapes
                      |> List.filter (fun sh ->
                          match sh with
                          | Shape.Curve _
                          | Shape.Circle _
                          | Shape.Rectangle(_, _, _, _, Some _, _) -> true
                          | _ -> false)

                  Expect.isEmpty claimShapes "…and no wedge or legend row, as before"
              }

              test "a REFUSED lowering keeps its refusal description — the summary never overwrites it" {
                  // Phase 790's refusal drawing says why it is empty. That text
                  // is the one thing the reader needs, so the summary must not
                  // displace it — and it cannot, because the summary is
                  // generated inside `lowerRows`, which a refusal never reaches.
                  let case = cases |> List.find (fun c -> c.Name = "bar-multi")

                  let refusal =
                      Charts.tryLowerWith
                          { Charts.ChartLimits.defaults with
                              MaxSeries = 1 }
                          (specOf case)
                          (Seq.ofList (buildRows case))

                  match refusal with
                  | Error r ->
                      match (Charts.refusalDrawing (specOf case) r).Description with
                      | Some(TextSource.Literal t) ->
                          Expect.equal
                              t
                              (Charts.describeRefusal r)
                              "the refusal drawing announces the refusal, not a summary"
                      | other -> failtestf "expected the refusal text, got %A" other
                  | Ok _ -> failtest "expected a series-cap refusal"
              }

              test "the summary is a LITERAL — never a binding the reader cannot resolve" {
                  // The whole point of generating it in the lowering is that it
                  // is canonical, host-invariant text. A bound description would
                  // reintroduce exactly the render-time variability the title
                  // has (and is why the title is composed by the renderer's root
                  // wiring instead of being baked in here).
                  for case in cases do
                      match (loweredCase case.Name).Description with
                      | None
                      | Some(TextSource.Literal _) -> ()
                      | Some other -> failtestf "%s: the summary is not a literal (%A)" case.Name other
              }

              // ── Phase 1490 (§4l) — the annotation rules the goldens carry but
              //    cannot STATE. Each of these is a property a byte comparison
              //    would satisfy for the wrong reason: the goldens pin the bytes
              //    that hold today, these say why those bytes are the right ones.

              test "an ABSENT annotations slot is byte-identical to the pre-1490 lowering" {
                  // The claim the corpus diff makes on its own — not one
                  // pre-1490 `.expected.json` was rewritten — stated in the
                  // stronger form the corpus cannot: for EVERY case declaring no
                  // annotation, clearing the (already absent) slot changes
                  // nothing. It is what makes this phase additive rather than a
                  // restyle, and it is the assertion that would fail first if a
                  // later member started reserving space by presence.
                  for case in cases |> List.filter (fun c -> Option.isNone c.Annotations) do
                      let cleared =
                          Fuaran.drawingSpec
                              (sprintf "chart-%s" case.Name)
                              (Charts.lowerWithStyle
                                  Charts.ChartLimits.defaults
                                  (styleOf case)
                                  { specOf case with Annotations = None }
                                  (Seq.ofList (buildRows case)))

                      Expect.equal
                          (CanonicalJson.encodeNode cleared)
                          (loweredJson case)
                          (sprintf "%s: an absent annotations slot moved the picture" case.Name)
              }

              test "an annotation WIDENS the value domain — a target above every bar is drawn, and the axis says so" {
                  // §4l rule 3, and the reason it is a rule rather than a
                  // preference. Clamping the line to the data's own domain would
                  // draw the 260 target ON the top gridline of a chart whose
                  // tallest bar is 175 — a picture asserting the target had been
                  // met. The axis must move instead.
                  let withTarget = loweredCase "bar-reference-target-above-data"
                  let case = cases |> List.find (fun c -> c.Name = "bar-reference-target-above-data")

                  let without =
                      Charts.lower { specOf case with Annotations = None } (Seq.ofList (buildRows case))

                  let topTick (ds: DrawingSpec) =
                      yTickTexts ds |> List.map float |> List.max

                  Expect.isTrue
                      (topTick withTarget > topTick without)
                      "the annotated chart's axis reaches further than the unannotated one's"

                  Expect.isTrue (topTick withTarget >= 260.0) "…far enough to contain the target it draws"

                  // And the line is INSIDE the plot rather than on its edge:
                  // a target the axis merely touches is the clamped picture by
                  // another route.
                  let lineY =
                      withTarget.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Line(_, y, _, _, s) when s.MarkId = Some "annotation|reference|0" -> Some y
                          | _ -> None)

                  let plotTop =
                      withTarget.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, y1, _, y2, _) when y1 = y2 -> Some y1
                          | _ -> None)
                      |> List.min

                  Expect.isTrue (lineY > plotTop) "the reference line sits below the topmost gridline"
              }

              test "mark identity is per-CASE document order, and no data change moves it" {
                  // Phase 642's stability property at the new slot. The ordinal
                  // indexes the DOCUMENT, so growing the data — the change that
                  // moves every datum's own mark id — leaves an annotation's
                  // alone. §4l's answer to the "an ordinal in a mark id is an
                  // ordinal address" objection is exactly this test.
                  let ids (ds: DrawingSpec) =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, _, _, _, s) -> s.MarkId
                          | _ -> None)
                      |> List.filter (fun id -> id.StartsWith "annotation|")

                  let case = cases |> List.find (fun c -> c.Name = "line-reference-labelled")

                  Expect.equal
                      (ids (loweredCase case.Name))
                      [ "annotation|reference|0"; "annotation|reference|1" ]
                      "document order within the case, counted from zero"

                  let grown =
                      { case with
                          Rows = case.Rows @ [ "W5", [ 275.0 ] ] }

                  Expect.equal
                      (ids (Charts.lower (specOf grown) (Seq.ofList (buildRows grown))))
                      (ids (loweredCase case.Name))
                      "a data change leaves an annotation's identity where it was"
              }

              test "draw order — lines in FRONT of the series, every label LAST" {
                  // §4l's z-order rung 3 and rung 4. In inline SVG z-order IS
                  // emission order, so a host that painted these in a different
                  // sequence would emit a valid document showing a different
                  // picture, and neither a schema nor a validator could see it.
                  // That is the class this test and the goldens exist to catch.
                  let ds = loweredCase "line-reference-labelled"

                  let isAnnotationLine (sh: Shape) =
                      match sh with
                      | Shape.Line(_, _, _, _, s) ->
                          s.MarkId
                          |> Option.map (fun id -> id.StartsWith "annotation|")
                          |> Option.defaultValue false
                      | _ -> false

                  let isSeries (sh: Shape) =
                      match sh with
                      | Shape.Polyline _ -> true
                      | _ -> false

                  let indexed = ds.Shapes |> List.indexed

                  let lastSeries =
                      indexed |> List.filter (snd >> isSeries) |> List.map fst |> List.max

                  let firstAnnotationLine =
                      indexed |> List.filter (snd >> isAnnotationLine) |> List.map fst |> List.min

                  Expect.isTrue (firstAnnotationLine > lastSeries) "the reference lines are painted after the series"

                  // Rung 4 — LAST, literally. Both labels are the final two
                  // shapes in the drawing, after the legend and the title.
                  let tail = ds.Shapes |> List.rev |> List.truncate 2

                  let labelTexts =
                      tail
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, TextSource.Literal t, _) -> Some t
                          | _ -> None)

                  Expect.equal (List.length labelTexts) 2 "the last two shapes are the annotation labels"
                  Expect.isTrue (List.contains "SLO" labelTexts) "…the first annotation's"
                  Expect.isTrue (List.contains "Breach" labelTexts) "…and the second's"
              }

              test "a label with no room is SUPPRESSED, and its line still draws" {
                  // Phase 881's rule, unchanged at a new slot: never clipped,
                  // never overlapped, never moved onto a mark. The half that
                  // matters is the second clause — a suppressed label must not
                  // take its annotation with it, or a layout decision becomes a
                  // silent loss of the reader's threshold.
                  let case = cases |> List.find (fun c -> c.Name = "line-reference-labelled")

                  let overlong =
                      { specOf case with
                          Annotations =
                              Some
                                  [ ChartAnnotation.ReferenceLine(
                                        250.0,
                                        Some(lit (String.replicate 40 "an unreasonably long threshold name "))
                                    ) ] }

                  let ds = Charts.lower overlong (Seq.ofList (buildRows case))

                  let annotationLines =
                      ds.Shapes
                      |> List.filter (fun sh ->
                          match sh with
                          | Shape.Line(_, _, _, _, s) -> s.MarkId = Some "annotation|reference|0"
                          | _ -> false)

                  Expect.equal (List.length annotationLines) 1 "the line draws"

                  Expect.isFalse
                      (literalTexts ds |> List.exists (fun t -> t.Contains "unreasonably long"))
                      "…and the label that could not fit is not written anywhere"
              }

              test "a NON-LITERAL label is admitted on PRESENCE — carried, never measured, never dropped" {
                  // The text contract's clauses 1 and 3 at this slot. A `Bound`
                  // arm's glyphs are unknowable here, so it cannot be fit-gated:
                  // it is carried through unresolved and may overrun, which is
                  // the same honest boundary clause 4 draws for truncation. The
                  // failure this pins is a host that resolves it (baking live
                  // state into a shared golden) or drops it (losing authored
                  // content silently).
                  let ds = loweredCase "line-reference-labelled-bound-label"

                  let bound =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Label(_, _, TextSource.Bound b, _) -> Some b
                          | _ -> None)

                  Expect.equal (List.length bound) 1 "the bound label reaches the drawing as a Bound arm"

                  // A `Literal` of the SAME text is refused, which is what makes
                  // "admitted on presence" a measurable difference rather than
                  // an assertion about a case that happened to fit.
                  let case =
                      cases |> List.find (fun c -> c.Name = "line-reference-labelled-bound-label")

                  let asLiteral =
                      { specOf case with
                          Annotations =
                              Some
                                  [ ChartAnnotation.ReferenceLine(
                                        250.0,
                                        Some(lit (String.replicate 40 "the SLO, from the live binding "))
                                    ) ] }

                  let literalDs = Charts.lower asLiteral (Seq.ofList (buildRows case))

                  Expect.isEmpty
                      (literalDs.Shapes
                       |> List.filter (fun sh ->
                           match sh with
                           | Shape.Label(_, _, TextSource.Literal t, _) -> t.Contains "live binding"
                           | _ -> false))
                      "a literal too long for the plot is suppressed at the same slot"
              }

              test "the polar arm is NEUTRALISED, not half-applied — a pie is byte-unchanged by an annotation" {
                  // Phase 882's treatment of `xScale`, and for the same reason:
                  // a pie has no value axis, so a value-axis address names
                  // nothing there. Drawing a horizontal rule across a pie would
                  // be an assertion about a space the picture does not have.
                  let case = cases |> List.find (fun c -> c.Name = "pie-single")

                  let annotated =
                      { specOf case with
                          Annotations = Some [ ChartAnnotation.ReferenceLine(25.0, Some(lit "Quarter")) ] }

                  let node (spec: ChartSpec<obj>) : Node<obj> =
                      Fuaran.drawingSpec "c" (Charts.lower spec (Seq.ofList (buildRows case)))

                  Expect.equal
                      (CanonicalJson.encodeNode (node annotated))
                      (CanonicalJson.encodeNode (node (specOf case)))
                      "the pie lowers identically with and without an annotation"
              }

              test "a NON-FINITE value cannot reach the geometry — the lowering stays total" {
                  // The third gate, and the only one that is not a check: the
                  // decoder refuses a non-finite value at the wire boundary and
                  // FUARAN137 refuses it on the authoring path, so this filter
                  // exists for the construction site neither of them sits on.
                  // What it buys is that a lowering handed one anyway draws the
                  // chart it can, rather than taking the domain, every gridline
                  // and every mark's coordinate to NaN.
                  let case = cases |> List.find (fun c -> c.Name = "line-reference-zero")

                  for bad in [ nan; infinity; -infinity ] do
                      let poisoned =
                          { specOf case with
                              Annotations = Some [ ChartAnnotation.ReferenceLine(bad, None) ] }

                      let ds = Charts.lower poisoned (Seq.ofList (buildRows case))

                      let clean =
                          Charts.lower { specOf case with Annotations = None } (Seq.ofList (buildRows case))

                      Expect.equal
                          (CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds))
                          (CanonicalJson.encodeNode (Fuaran.drawingSpec "c" clean))
                          (sprintf "a %f annotation is dropped and the rest of the chart is unmoved" bad)
              }

              // ── Phase 1491 (§4l) — the event marker's own rules. Same posture
              //    as the 1490 block above: the goldens pin the bytes, these say
              //    why those bytes are the right ones.

              test "a category address sits at the BAND CENTRE — the same x its own label does" {
                  // Phase 903's split applied to an ADDRESS. A band has an extent
                  // and not a position, so the only x that means "Q3" rather than
                  // "the edge between Q2 and Q3" is the band's centre — which is
                  // where the band's own label already sits, and comparing the two
                  // is what makes this a statement about the axis rather than
                  // about an arithmetic the test would be repeating.
                  let ds = loweredCase "bar-event-single"

                  let markerX =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Line(x1, _, _, _, s) when s.MarkId = Some "annotation|event|0" -> Some x1
                          | _ -> None)

                  let labelX =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Label(x, _, TextSource.Literal "Q3", _) -> Some x
                          | _ -> None)

                  Expect.equal markerX labelX "the marker stands where the band it names is labelled"

                  // Full height, not a stub: the marker crosses the whole plot,
                  // which is what lets a reader line every series up against it.
                  let y1, y2 =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Line(_, a, _, b, s) when s.MarkId = Some "annotation|event|0" -> Some(a, b)
                          | _ -> None)

                  let plotTop =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, a, _, b, _) when a = b -> Some a
                          | _ -> None)
                      |> List.min

                  Expect.equal y1 plotTop "the marker starts at the top of the plot"
                  Expect.isTrue (y2 > y1) "…and runs down it"
              }

              test "a temporal address WIDENS the extent — a marker past the last datum is drawn, not clamped" {
                  // §4l rule 3 on the X axis, and the mirror of the value-axis
                  // test above. Without the widening a later date maps past the
                  // plot's right edge and is drawn off the picture; clamping it
                  // instead would draw the event at a date that is not the date
                  // declared, which is the same defect the 260 target measures on
                  // the other axis.
                  let case = cases |> List.find (fun c -> c.Name = "line-temporal-events-collide")

                  let lastPointX (ds: DrawingSpec) =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Polyline(points, _) -> Some (points |> List.last).X
                          | _ -> None)

                  let plotRight (ds: DrawingSpec) =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, a, x2, b, _) when a = b -> Some x2
                          | _ -> None)
                      |> List.max

                  let bare =
                      Charts.lower { specOf case with Annotations = None } (Seq.ofList (buildRows case))

                  // The control: with no annotation the domain IS the data's own
                  // extent (§4h rule 2, unexpanded), so the last datum sits ON the
                  // right edge — which is exactly why a LATER marker has nowhere
                  // to go unless the extent moves.
                  Expect.equal (lastPointX bare) (plotRight bare) "the unannotated chart ends at its last datum"

                  let beyond =
                      { specOf case with
                          Annotations =
                              Some [ ChartAnnotation.EventMarker(ChartAnnotationX.Date "2026-06-01", Option.None) ] }

                  let widened = Charts.lower beyond (Seq.ofList (buildRows case))

                  let markerX =
                      widened.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Line(x1, _, _, _, s) when s.MarkId = Some "annotation|event|0" -> Some x1
                          | _ -> None)

                  Expect.isTrue
                      (lastPointX widened < markerX)
                      "the marker sits after the last datum, which has moved off the edge to make room"

                  Expect.isTrue (markerX <= plotRight widened + 0.01) "…and is still on the picture rather than past it"
              }

              test "mark identity is PER CASE — a reference line and an event marker never renumber each other" {
                  // §4l's "per-case, not whole-list" clause, which is the property
                  // that lets a later member be added without moving an existing
                  // annotation's identity. Interleaved deliberately: a whole-list
                  // ordinal would number these 0, 1, 2 in document order and the
                  // event ids would depend on where the reference line was written.
                  let case = cases |> List.find (fun c -> c.Name = "bar-event-single")

                  let mixed =
                      { specOf case with
                          Annotations =
                              Some
                                  [ ChartAnnotation.EventMarker(ChartAnnotationX.Category "Q2", Option.None)
                                    ChartAnnotation.ReferenceLine(140.0, Option.None)
                                    ChartAnnotation.EventMarker(ChartAnnotationX.Category "Q4", Option.None) ] }

                  let ids =
                      (Charts.lower mixed (Seq.ofList (buildRows case))).Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, _, _, _, s) -> s.MarkId
                          | _ -> None)
                      |> List.filter (fun id -> id.StartsWith "annotation|")

                  Expect.equal
                      (List.sort ids)
                      [ "annotation|event|0"; "annotation|event|1"; "annotation|reference|0" ]
                      "each case counts its own subsequence from zero"
              }

              test "colliding labels are SUPPRESSED one by one, and every marker still draws its line" {
                  // Phase 881's rule along X — the half a horizontal reference
                  // line structurally cannot exercise, and the reason this member
                  // earned its own phase. Each label's budget runs to its
                  // NEIGHBOUR's line, so the two crowded ones go and the last,
                  // whose budget runs to the plot edge, stays. Suppression is
                  // per-label rather than all-or-nothing, and it never takes the
                  // marker with it.
                  let ds = loweredCase "line-temporal-events-collide"

                  let markerIds =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Line(_, _, _, _, s) -> s.MarkId
                          | _ -> None)
                      |> List.filter (fun id -> id.StartsWith "annotation|event|")

                  Expect.equal
                      markerIds
                      [ "annotation|event|0"; "annotation|event|1"; "annotation|event|2" ]
                      "all three markers draw, however little room their labels have"

                  let texts = literalTexts ds

                  Expect.isFalse (List.contains "First review window" texts) "the first label had no room and is gone"
                  Expect.isFalse (List.contains "Second review window" texts) "…and so had the second"
                  Expect.isTrue (List.contains "Sign-off" texts) "the last one runs to the plot edge and is drawn"

                  // The same rule at DECADE scale, which is not a repetition: the
                  // day-scale case above is contrived to collide, and this one is
                  // the surveyed article's own spacing. Thirty years across the
                  // plot leaves the first three events room and the last two —
                  // thirty months apart — without it, so the gate is shown biting
                  // on real data rather than only on a case built to break it.
                  let decade = literalTexts (loweredCase "line-temporal-events-five")

                  Expect.equal
                      (decade
                       |> List.filter (fun t -> t = "Dot-com" || t = "GFC" || t = "Brexit")
                       |> List.length)
                      3
                      "the three well-spaced events keep their labels"

                  Expect.isFalse
                      (List.contains "Pandemic" decade || List.contains "Mini-budget" decade)
                      "…and the two crowded ones do not"
              }

              test "an address in the WRONG FORM reaches no geometry — the lowering stays total" {
                  // The third gate, exactly as the non-finite filter is. §4l rule
                  // 1's mismatch is refused pre-emit (FUARAN139) and a category
                  // address names no band on a temporal axis anyway; what this
                  // pins is that a lowering handed one through a construction site
                  // neither gate sits on draws the chart it can, rather than
                  // placing a marker at a coordinate the axis does not have.
                  let band = cases |> List.find (fun c -> c.Name = "bar-event-single")
                  let temporal = cases |> List.find (fun c -> c.Name = "line-temporal-events-five")

                  let unchangedBy (case: Case) (bad: ChartAnnotation) (why: string) =
                      let node (spec: ChartSpec<obj>) : Node<obj> =
                          Fuaran.drawingSpec "c" (Charts.lower spec (Seq.ofList (buildRows case)))

                      Expect.equal
                          (CanonicalJson.encodeNode (
                              node
                                  { specOf case with
                                      Annotations = Some [ bad ] }
                          ))
                          (CanonicalJson.encodeNode (
                              node
                                  { specOf case with
                                      Annotations = Option.None }
                          ))
                          why

                  unchangedBy
                      band
                      (ChartAnnotation.EventMarker(ChartAnnotationX.Date "2026-02-14", Some(lit "Launch")))
                      "a DATE address on a band axis draws nothing"

                  unchangedBy
                      temporal
                      (ChartAnnotation.EventMarker(ChartAnnotationX.Category "Q3", Some(lit "Repricing")))
                      "a CATEGORY address on a temporal axis draws nothing"

                  unchangedBy
                      band
                      (ChartAnnotation.EventMarker(ChartAnnotationX.Category "Q9", Some(lit "Nowhere")))
                      "a category key no row carries draws nothing"

                  // And the polar arm is NEUTRALISED for this member exactly as it
                  // is for the reference line: a pie has no x axis, so an address
                  // on it names nothing at all.
                  let pie = cases |> List.find (fun c -> c.Name = "pie-single")

                  unchangedBy
                      pie
                      (ChartAnnotation.EventMarker(ChartAnnotationX.Category "North", Some(lit "Anything")))
                      "a pie lowers identically with and without an event marker"
              }

              // ── Phase 1492 (§4l) — the range band's own rules. Same posture
              //    as the two blocks above: the goldens pin the bytes, these say
              //    why those bytes are the right ones.

              test "a band is emitted FIRST — behind the series, and behind the grid with it" {
                  // §4l rung 1, and the half of the draw order neither earlier
                  // member could exercise. The assertion is POSITIONAL because
                  // the property is: in inline SVG z-order IS emission order, so
                  // a host that painted the band later would emit a valid
                  // document showing a tinted rectangle OVER its own data, and
                  // no schema and no validator could see it.
                  let ds = loweredCase "bar-band-x-categories"

                  let indexOfMark (markId: string) : int =
                      ds.Shapes
                      |> List.findIndex (fun sh ->
                          match sh with
                          | Shape.Rectangle(_, _, _, _, _, s)
                          | Shape.Line(_, _, _, _, s) -> s.MarkId = Some markId
                          | _ -> false)

                  Expect.equal (indexOfMark "annotation|band|0") 0 "the band is the very first shape in the drawing"

                  Expect.isTrue
                      (indexOfMark "annotation|band|0" < indexOfMark "revenue|Q1")
                      "…so every series mark is painted over it"

                  // And the OTHER rung is unmoved by the band's arrival: a
                  // reference line still sits in FRONT of the series (rung 3).
                  // One document, both halves of the order.
                  Expect.isTrue
                      (indexOfMark "revenue|Q4" < indexOfMark "annotation|reference|0")
                      "the reference line still paints in front of the series"

                  // The per-case ordinals are independent: a band and a
                  // reference line in one document are both `|0`.
                  Expect.isTrue
                      (indexOfMark "annotation|reference|0" > 0)
                      "the reference line keeps its own case's ordinal 0 beside the band's"
              }

              test "a CATEGORY band takes Phase 903's BOUNDARIES where a marker takes its centres" {
                  // The difference between the two members drawn from the same
                  // address, and the reason both exist: a marker is a POSITION
                  // and a band is an EXTENT. "Q2 to Q3" therefore runs from the
                  // boundary BEFORE Q2 to the boundary AFTER Q3 — shading
                  // centre-to-centre would leave half of each named quarter
                  // outside the region that names it.
                  //
                  // Derived from the axis's own labels rather than from a
                  // recomputed band pitch, so this states something about the
                  // picture instead of repeating the lowering's arithmetic.
                  let ds = loweredCase "bar-band-x-categories"

                  let labelX (text: string) : float =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Label(x, _, TextSource.Literal t, _) when t = text -> Some x
                          | _ -> None)

                  let x, width =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Rectangle(x, _, w, _, _, s) when s.MarkId = Some "annotation|band|0" -> Some(x, w)
                          | _ -> None)

                  let pitch = labelX "Q3" - labelX "Q2"

                  Expect.equal (r2 x) (r2 (labelX "Q2" - pitch / 2.0)) "the left edge is the boundary before Q2"

                  Expect.equal
                      (r2 (x + width))
                      (r2 (labelX "Q3" + pitch / 2.0))
                      "the right edge is the boundary after Q3"

                  // FULL HEIGHT: the band's claim is about x, so it says nothing
                  // about y by spanning all of it. A band that stopped short
                  // would be asserting a value range it was never given.
                  let y, height =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Rectangle(_, y, _, h, _, s) when s.MarkId = Some "annotation|band|0" -> Some(y, h)
                          | _ -> None)

                  let axisTop, axisBottom =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          // The y spine — the only vertical full-height line the
                          // cartesian chrome draws.
                          | Shape.Line(x1, y1, x2, y2, _) when x1 = x2 && y2 - y1 > 100.0 -> Some(y1, y2)
                          | _ -> None)

                  Expect.equal y axisTop "the band starts at the plot's top"
                  Expect.equal (y + height) axisBottom "…and ends at its bottom"
              }

              test "a VALUE band widens the domain at BOTH ends, and spans the plot's whole width" {
                  // §4l rule 3, at two addresses instead of one. A tolerance band
                  // whose upper edge sits above every datum is drawn WHOLE and
                  // the axis says so; clipping it at the data's own maximum would
                  // end the band where the author did not, which is the
                  // misreading `bar-reference-target-above-data` already pins for
                  // the single-address case.
                  let case = cases |> List.find (fun c -> c.Name = "line-band-y-tolerance")

                  let ticksWith (ann: ChartAnnotation option) : string list =
                      yTickTexts (
                          Charts.lower
                              { specOf case with
                                  Annotations = ann |> Option.map List.singleton }
                              (Seq.ofList (buildRows case))
                      )

                  let plain = ticksWith Option.None

                  let widened =
                      ticksWith (
                          Some(ChartAnnotation.RangeBand(ChartAnnotationRange.ValueRange(400.0, 500.0), Option.None))
                      )

                  Expect.notEqual
                      widened
                      plain
                      "a band above every datum lifts the axis rather than being clipped to it"

                  // The interior band in the golden does NOT move the axis, which
                  // is the control: the widening is the pair's, not the member's.
                  Expect.equal
                      (ticksWith (Some(List.head (Option.defaultValue [] case.Annotations))))
                      plain
                      "an interior band leaves the axis alone"

                  // FULL WIDTH, the mirror of the x band's full height.
                  let ds = loweredCase "line-band-y-tolerance"

                  let x, width =
                      ds.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Rectangle(x, _, w, _, _, s) when s.MarkId = Some "annotation|band|0" -> Some(x, w)
                          | _ -> None)

                  Expect.equal (r2 (x + width)) (r2 (plotRight ds)) "the band runs to the plot's right edge"
              }

              test "a band's label is budgeted by the BAND, not the plot — and suppressed on no fit" {
                  // Where this member's gate bites and the other two's do not. A
                  // narrow x band is the ordinary case — eighteen months on a
                  // fifteen-year axis — and its name will not fit inside it.
                  // Phase 881's rule: suppressed, never clipped, never spilled
                  // across the boundary of the region it names, and the band
                  // still draws.
                  let narrow = loweredCase "line-temporal-band-x-period"

                  Expect.isFalse
                      (List.contains "Recession" (literalTexts narrow))
                      "the label does not fit inside an 18-month band and is suppressed"

                  Expect.isTrue
                      (narrow.Shapes
                       |> List.exists (fun sh ->
                           match sh with
                           | Shape.Rectangle(_, _, _, _, _, s) -> s.MarkId = Some "annotation|band|0"
                           | _ -> false))
                      "…and a suppressed label never suppresses its band"

                  // The control, on the same case: widen the band and the same
                  // label fits. Without this the suppression above would be
                  // consistent with the label never being emitted at all.
                  let case = cases |> List.find (fun c -> c.Name = "line-temporal-band-x-period")

                  let wide =
                      Charts.lower
                          { specOf case with
                              Annotations =
                                  Some
                                      [ ChartAnnotation.RangeBand(
                                            ChartAnnotationRange.XRange(
                                                ChartAnnotationX.Date "2006-01-01",
                                                ChartAnnotationX.Date "2014-01-01"
                                            ),
                                            Some(lit "Recession")
                                        ) ] }
                          (Seq.ofList (buildRows case))

                  Expect.isTrue
                      (List.contains "Recession" (literalTexts wide))
                      "the same label fits inside an eight-year band"

                  // The label sits INSIDE the band's top edge — not above it,
                  // which for a value band would put it over the series and for
                  // an x band outside the plot entirely.
                  let bandTop, bandLeft =
                      wide.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Rectangle(x, y, _, _, _, s) when s.MarkId = Some "annotation|band|0" -> Some(y, x)
                          | _ -> None)

                  let labelX, labelY =
                      wide.Shapes
                      |> List.pick (fun sh ->
                          match sh with
                          | Shape.Label(x, y, TextSource.Literal "Recession", _) -> Some(x, y)
                          | _ -> None)

                  Expect.isTrue (labelY > bandTop) "the baseline is below the band's top edge"
                  Expect.isTrue (labelX > bandLeft) "…and inset from its left one"
              }

              test "an unusable band reaches no geometry — the lowering stays total" {
                  // The third gate again, and the same division of labour the
                  // other two members draw: what the validator REFUSES, the
                  // lowering merely declines to draw, so a spec that arrived
                  // through a construction site neither gate sits on still
                  // produces the chart it can.
                  let band = cases |> List.find (fun c -> c.Name = "bar-band-x-categories")
                  let temporal = cases |> List.find (fun c -> c.Name = "line-temporal-band-x-period")

                  let drawsNothing (case: Case) (bad: ChartAnnotation) (why: string) =
                      let node (spec: ChartSpec<obj>) : Node<obj> =
                          Fuaran.drawingSpec "c" (Charts.lower spec (Seq.ofList (buildRows case)))

                      Expect.equal
                          (CanonicalJson.encodeNode (
                              node
                                  { specOf case with
                                      Annotations = Some [ bad ] }
                          ))
                          (CanonicalJson.encodeNode (
                              node
                                  { specOf case with
                                      Annotations = Option.None }
                          ))
                          why

                  drawsNothing
                      band
                      (ChartAnnotation.RangeBand(
                          ChartAnnotationRange.XRange(
                              ChartAnnotationX.Category "Q2",
                              ChartAnnotationX.Date "2026-01-01"
                          ),
                          Option.None
                      ))
                      "a pair mixing the two address forms draws nothing — half a pair addresses no interval"

                  drawsNothing
                      band
                      (ChartAnnotation.RangeBand(
                          ChartAnnotationRange.XRange(ChartAnnotationX.Category "Q2", ChartAnnotationX.Category "Q9"),
                          Option.None
                      ))
                      "an end no row carries draws nothing"

                  drawsNothing
                      temporal
                      (ChartAnnotation.RangeBand(
                          ChartAnnotationRange.XRange(
                              ChartAnnotationX.Date "2008-04-01",
                              ChartAnnotationX.Date "2009-13-40"
                          ),
                          Option.None
                      ))
                      "an end naming no calendar day draws nothing"

                  drawsNothing
                      band
                      (ChartAnnotation.RangeBand(ChartAnnotationRange.ValueRange(100.0, System.Double.NaN), Option.None))
                      "a non-finite end drops the WHOLE band — half a band is a different claim, not a smaller one"

                  // The polar arm is neutralised for this member too: a pie has
                  // neither axis for a band to span.
                  let pie = cases |> List.find (fun c -> c.Name = "pie-single")

                  drawsNothing
                      pie
                      (ChartAnnotation.RangeBand(ChartAnnotationRange.ValueRange(1.0, 2.0), Some(lit "Anything")))
                      "a pie lowers identically with and without a range band"
              }

              test "a BACKWARDS band still draws the region it names — refusal is the validator's job" {
                  // The division of labour stated as bytes. FUARAN141 refuses the
                  // pair and the wire decoder refuses it too; a lowering handed
                  // one anyway must not emit an inverted rectangle (a negative
                  // width is not a picture at all), so it draws the interval the
                  // ends bound. Both are right: the check refuses, the drawing
                  // copes.
                  let case = cases |> List.find (fun c -> c.Name = "line-band-y-tolerance")

                  let lowerWith (f: float) (t: float) =
                      Charts.lower
                          { specOf case with
                              Annotations =
                                  Some [ ChartAnnotation.RangeBand(ChartAnnotationRange.ValueRange(f, t), Option.None) ] }
                          (Seq.ofList (buildRows case))

                  let forwards = Fuaran.drawingSpec "c" (lowerWith 200.0 260.0)
                  let backwards = Fuaran.drawingSpec "c" (lowerWith 260.0 200.0)

                  Expect.equal
                      (CanonicalJson.encodeNode backwards)
                      (CanonicalJson.encodeNode forwards)
                      "a backwards pair lowers to the same band as its ordered twin"
              } ]
