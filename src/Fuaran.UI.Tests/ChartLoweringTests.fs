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
        Rows: (string * float list) list
    }

/// The default-shaped case — the four Phase-876 fields absent — so the twelve
/// pre-876 cases stay readable.
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
          Rows = [ "Q1", [ 4200.0 ]; "Q2", [ 9500.0 ]; "Q3", [ 6800.0 ]; "Q4", [ 7300.0 ] ] } ]

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
// The y tick labels are the only End-anchored labels the cartesian arms emit;
// the axis-unit slot is the only Start-anchored, full-strength, Normal-weight
// one (the visible title is Loud, the legend labels carry an opacity).

let private yTickTexts (ds: DrawingSpec) : string list =
    ds.Shapes
    |> List.choose (fun sh ->
        match sh with
        | Shape.Label(_, _, TextSource.Literal t, s) when s.TextAnchor = Some TextAnchor.End -> Some t
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

    sprintf
        "{\"kind\":\"%s\",\"xField\":\"%s\",\"yFields\":[%s],\"title\":%s,\"stacked\":%s%s%s,\"data\":[%s]}"
        kind
        (esc case.XField)
        yFields
        title
        (if case.Stacked then "true" else "false")
        valueFormat
        unitMode
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
                  // LegendPosition / LabelTiltDegrees / the status triple are
                  // declared (Phase 885) but read by no shipped lowering path —
                  // setting them must change nothing until their phases land.
                  let enc (ds: DrawingSpec) : string =
                      CanonicalJson.encodeNode (Fuaran.drawingSpec "c" ds: Node<obj>)

                  let reserved =
                      { Charts.ChartStyle.defaults with
                          LegendPosition = Charts.ChartLegendPosition.Bottom
                          LabelTiltDegrees = 90.0
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
                  Expect.equal (axisUnitLabelOf "bar-thousands") "Value" "no unit label below the gate"
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

                  Expect.equal (axisUnitLabelOf "bar-compact") "Value" "no unit label in compact mode"
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

                  Expect.equal (axisUnitLabel ds) "Value" "no unit label when nothing was scaled"
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
              } ]
