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
        Title: string option
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
        XTitle: string option
        YTitle: string option
        Subtitle: string option
        Rows: (string * float list) list
    }

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
      Rows = [] }

/// The case's x cell values, boxed — numeric when `XNums` is set, else the
/// string labels.
let private xCells (case: Case) : obj list =
    match case.XNums with
    | Some ns -> ns |> List.map box
    | None -> case.Rows |> List.map (fst >> box)

let private cases: Case list =
    [ { plain with
          Name = "bar-single"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some "Revenue by quarter"
          Stacked = false
          XNums = None
          Rows = [ "Q1", [ 120.0 ]; "Q2", [ 150.0 ]; "Q3", [ 90.0 ]; "Q4", [ 175.0 ] ] }
      { plain with
          Name = "bar-multi"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales"; "target" ]
          Title = Some "Sales vs target"
          Stacked = false
          XNums = None
          Rows = [ "North", [ 80.0; 100.0 ]; "South", [ 130.0; 110.0 ]; "East", [ 60.0; 90.0 ] ] }
      { plain with
          Name = "line-single"
          Kind = ChartKind.Line
          XField = "month"
          YFields = [ "users" ]
          Title = Some "Active users"
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
          Title = Some "Tickets by status"
          Stacked = true
          XNums = None
          // A zero cell (S2 blocked) pins the zero-height-segment geometry.
          Rows = [ "S1", [ 12.0; 5.0; 3.0 ]; "S2", [ 18.0; 4.0; 0.0 ]; "S3", [ 9.0; 7.0; 2.0 ] ] }
      { plain with
          Name = "area-single"
          Kind = ChartKind.Area
          XField = "week"
          YFields = [ "signups" ]
          Title = Some "Signups"
          Stacked = false
          XNums = None
          Rows = [ "W1", [ 40.0 ]; "W2", [ 55.0 ]; "W3", [ 48.0 ]; "W4", [ 70.0 ] ] }
      { plain with
          Name = "area-multi"
          Kind = ChartKind.Area
          XField = "month"
          YFields = [ "web"; "mobile" ]
          Title = Some "Traffic"
          Stacked = false
          XNums = None
          Rows = [ "Jan", [ 60.0; 35.0 ]; "Feb", [ 72.0; 44.0 ]; "Mar", [ 65.0; 58.0 ] ] }
      { plain with
          Name = "area-stacked"
          Kind = ChartKind.Area
          XField = "month"
          YFields = [ "web"; "mobile" ]
          Title = Some "Traffic (stacked)"
          Stacked = true
          XNums = None
          Rows = [ "Jan", [ 60.0; 35.0 ]; "Feb", [ 72.0; 44.0 ]; "Mar", [ 65.0; 58.0 ] ] }
      // ── Phase 636 — the Scatter arm (numeric x, linear axis) ──
      { plain with
          Name = "scatter-single"
          Kind = ChartKind.Scatter
          XField = "height"
          YFields = [ "weight" ]
          Title = Some "Height vs weight"
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
          Title = Some "Ownership by holder class"
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
          Title = Some "Units shipped"
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      { plain with
          // Step-derived DECIMALS: a 0.1 step gives 1 dp on every tick, so the
          // zero tick reads `0.0` — precision follows the axis, not the datum.
          Name = "line-decimals"
          Kind = ChartKind.Line
          XField = "week"
          YFields = [ "rate" ]
          Title = Some "Conversion rate"
          Rows = [ "W1", [ 0.12 ]; "W2", [ 0.35 ]; "W3", [ 0.28 ]; "W4", [ 0.44 ] ] }
      { plain with
          // Display-unit scaling at the default `Words` mode: short ticks under a
          // single "Millions" label in the axis-unit slot.
          Name = "bar-millions"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some "Revenue by region"
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
          Title = Some "Revenue"
          ValueFormat = Some(Format.Currency "GBP")
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      { plain with
          // `WordsWithSymbol` + a currency: the symbol moves INTO the label
          // ("Millions of £") and leaves the ticks, stated once.
          Name = "bar-currency-millions"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "revenue" ]
          Title = Some "Revenue by region"
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
          Title = Some "Revenue by region"
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
          Title = Some "Conversion rate"
          ValueFormat = Some(Format.Percent None)
          Rows = [ "W1", [ 0.12 ]; "W2", [ 0.35 ]; "W3", [ 0.28 ]; "W4", [ 0.44 ] ] }
      { plain with
          // The opt-in compact mode: the suffix on every tick, no unit label, and
          // it gates at thousands where the label modes gate at millions.
          Name = "bar-compact"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "units" ]
          Title = Some "Units shipped"
          UnitMode = Some "CompactPerTick"
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] }
      // ── Phase 879 — deterministic text metrics ──
      //
      // Six cases, one per decision the pinned advance-width table now makes.
      // The escalation pair is a genuine BOUNDARY: the two inputs differ by a
      // single character, and that character is what moves the labels from the
      // 30° tilt to the 90° vertical arm.
      { plain with
          // Legend pitch from name extents: two 29-character series names. On
          // the retired flat 100 px pitch the second swatch landed on top of
          // the first label; on per-entry pitch each entry occupies its own
          // measured width.
          Name = "bar-legend-long-names"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "monthly_recurring_revenue_gbp"; "annual_contract_value_gbp_est" ]
          Title = Some "Long series names"
          Rows = [ "Q1", [ 40.0; 90.0 ]; "Q2", [ 55.0; 80.0 ] ] }
      { plain with
          // Left-margin autosize: `Off` keeps every magnitude, so the ticks
          // read `2,000,000` — nine characters that the fixed 64 px left margin
          // clipped clean through.
          Name = "bar-wide-ticks"
          Kind = ChartKind.Bar
          XField = "quarter"
          YFields = [ "revenue" ]
          Title = Some "Revenue in full"
          UnitMode = Some "Off"
          Rows =
              [ "Q1", [ 1250000.0 ]
                "Q2", [ 1480000.0 ]
                "Q3", [ 1100000.0 ]
                "Q4", [ 1690000.0 ] ] }
      { plain with
          // Five categories: the labels' along-axis footprint at 30° fits the
          // band pitch, so they stay tilted — and the bottom margin grows to
          // hold the drop.
          Name = "bar-tilt-five"
          Kind = ChartKind.Bar
          XField = "region"
          YFields = [ "sales" ]
          Title = Some "Sales by region"
          Rows =
              [ "North", [ 80.0 ]
                "South", [ 130.0 ]
                "East", [ 60.0 ]
                "West", [ 95.0 ]
                "Central", [ 110.0 ] ] }
      { plain with
          // Twenty categories: the same labels no longer pack at 30°, so the
          // axis escalates to vertical — which packs one label per line height
          // at any count.
          Name = "bar-vertical-twenty"
          Kind = ChartKind.Bar
          XField = "week"
          YFields = [ "signups" ]
          Title = Some "Signups by week"
          Rows = [ for i in 1..20 -> (sprintf "Wk %d" i), [ 40.0 + float ((i * 7) % 23) ] ] }
      { plain with
          // BOUNDARY, under: eight nine-character labels. 9 × 0.55 em × 13 px =
          // 64.35 px; the footprint at 30° is 64.35·cos30 + 15.6·sin30 = 63.53,
          // inside the 68.5 px band pitch — so the labels stay tilted.
          Name = "bar-tilt-boundary"
          Kind = ChartKind.Bar
          XField = "code"
          YFields = [ "count" ]
          Title = Some "At the escalation boundary"
          Rows = [ for i in 0..7 -> (sprintf "10000000%d" i), [ 20.0 + float (i * 9 % 70) ] ] }
      { plain with
          // BOUNDARY, over: the SAME eight labels with one more digit. 10 ×
          // 0.55 em × 13 px = 71.5 px; the footprint is 69.72, past the same
          // 68.5 px pitch — so the identical chart escalates to vertical.
          Name = "bar-vertical-boundary"
          Kind = ChartKind.Bar
          XField = "code"
          YFields = [ "count" ]
          Title = Some "Past the escalation boundary"
          Rows = [ for i in 0..7 -> (sprintf "100000000%d" i), [ 20.0 + float (i * 9 % 70) ] ] }
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
          Title = Some "Sales vs target"
          XTitle = Some "Sales region"
          YTitle = Some "Value (£)"
          Subtitle = Some "Rolling twelve months"
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
          Title = Some "Revenue"
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
          Title = Some "Revenue"
          ValueFormat = Some(Format.Currency "GBP")
          UnitMode = Some "WordsWithSymbol"
          Subtitle = Some "Millions of £"
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
          Title = Some "A very long axis name"
          YTitle = Some "Monthly recurring revenue, net of refunds and credits, in pounds sterling at constant currency"
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

let private specOf (case: Case) : ChartSpec<obj> =
    { Source = Binding.Static(Some(Seq.ofList (buildRows case)))
      Kind = case.Kind
      XField = case.XField
      YFields = case.YFields
      Title = case.Title |> Option.map TextSource.Literal
      ValueFormat = case.ValueFormat
      XTitle = case.XTitle |> Option.map TextSource.Literal
      YTitle = case.YTitle |> Option.map TextSource.Literal
      Subtitle = case.Subtitle |> Option.map TextSource.Literal
      OnPointClick = None
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

    let title =
        match case.Title with
        | Some t -> "\"" + esc t + "\""
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

    // Phase 878 — plain-string keys beside `title`, OMITTED when absent, so
    // every input predating this phase is untouched.
    let optText (key: string) (v: string option) =
        match v with
        | None -> ""
        | Some s -> sprintf ",\"%s\":\"%s\"" key (esc s)

    let titles =
        optText "xTitle" case.XTitle
        + optText "yTitle" case.YTitle
        + optText "subtitle" case.Subtitle

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

let private tryFindFixtures () : string option =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        else
            let candidate = Path.Combine(dir.FullName, "wire-format-fixtures")

            if Directory.Exists candidate then
                Some candidate
            else
                climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

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
                  // LegendPosition / the status triple are declared (Phase 885)
                  // but read by no shipped lowering path — setting them must
                  // change nothing until their phases land. `LabelTiltDegrees`
                  // LEFT this set in Phase 879, which consumes it.
                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  let reserved =
                      { Charts.ChartStyle.defaults with
                          LegendPosition = Charts.ChartLegendPosition.Bottom
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

              test "the legend pitch derives from each entry's own name extent" {
                  let swatchXs (ds: DrawingSpec) : float list =
                      ds.Shapes
                      |> List.choose (fun sh ->
                          match sh with
                          | Shape.Rectangle(x, y, _, _, Some _, _) when y = 34.0 -> Some x
                          | _ -> None)

                  // "monthly_recurring_revenue_gbp" is 14.66 em = 190.58 px, so
                  // entry 0 occupies 15 + 190.58 + 24 = 229.58 px.
                  Expect.equal
                      (swatchXs (loweredCase "bar-legend-long-names"))
                      [ 64.0; r2 (64.0 + 229.58) ]
                      "the second swatch clears the first label"

                  // The short-name case is where the retired flat 100 px pitch
                  // happened to look right; it no longer sits there.
                  Expect.equal
                      (swatchXs (loweredCase "bar-multi"))
                      [ 64.0; r2 (64.0 + 15.0 + 32.24 + 24.0) ]
                      "short names pack tighter than the retired pitch"
              }

              test "category labels tilt by default and escalate to vertical at the boundary" {
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

                  // Tilt is the DEFAULT state, not a crowding fallback — five
                  // roomy categories are still tilted.
                  Expect.equal (rotations (loweredCase "bar-tilt-five")) [ -30.0 ] "the 30° tilt is the default"

                  // Twenty categories no longer pack at 30°.
                  Expect.equal (rotations (loweredCase "bar-vertical-twenty")) [ -90.0 ] "escalated to vertical"

                  // The boundary pair: the SAME chart, one character longer.
                  Expect.equal (rotations (loweredCase "bar-tilt-boundary")) [ -30.0 ] "just inside the band pitch"
                  Expect.equal (rotations (loweredCase "bar-vertical-boundary")) [ -90.0 ] "one character past it"

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
              } ]
