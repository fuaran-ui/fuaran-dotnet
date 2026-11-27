module Fuaran.UI.Tests.ChartTableTests

#nowarn "3261" // `box` and `JsonElement.GetString` are legitimately nullable here.

// ============================================================================
//  Phase 2776 — a chart's data-table alternative: the lowering's table model.
//
//  `Charts.tableModel` yields, beside the drawing, the values a chart's marks
//  were drawn from. It is pinned for every host by the
//  `wire-format-fixtures/chart-lowering/table-model/` subfamily: each case is
//  a BYTE COPY of an existing chart-lowering input (`<name>.input.json`) and a
//  sidecar golden (`<name>.table.json`) holding the table model in canonical
//  JSON, or `null` where the lowering's cost caps refuse the chart. The
//  existing `*.expected.json` goldens are not touched: the hosts that read the
//  top-level family compare those bytes, and they do not read a subdirectory.
//
//  The inputs are DECODED here from the neutral input contract, as a second
//  host will decode them, rather than rebuilt from the lowering suite's own
//  case records: the copy and the golden are then a pair any host can certify
//  against with nothing but the corpus.
//
//  Regenerate with `FUARAN_EMIT_CHART_LOWERING=1` (copies the inputs, writes the
//  goldens), then commit them.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// The cases: the family directory under `chart-lowering/` ("" for the
/// top level) and the input's stem, which is also the case's name.
let private tableCases: (string * string) list =
    [ "", "bar-single"
      "", "bar-multi"
      "", "bar-stacked"
      "", "bar-currency"
      "", "bar-currency-millions"
      "", "bar-summary-hostile-text"
      "", "line-multi"
      "", "line-percent"
      "", "line-decimals"
      "", "line-temporal-daily"
      "", "area-stacked"
      "", "pie-quarters"
      "", "scatter-single"
      "derived-series", "derived-mixed-columns"
      "derived-series", "derived-over-series-cap"
      "heatmap", "heatmap-missing-cells"
      "heatmap", "heatmap-refusal-cell-cap"
      "withheld", "bar-withheld"
      "withheld", "line-missing"
      "withheld", "pie-withheld"
      "withheld", "scatter-withheld"
      "channels", "bar-sort-desc"
      "channels", "bar-interval"
      "channels", "scatter-all-channels"
      "channels", "line-sort-temporal-neutralised" ]

// ── The neutral input contract, decoded ──────────────────────────────────────
//
// Only the members a table reads: what is plotted and how a value reads. The
// members that change the PICTURE but not the values (legend, labels, axis
// titles, annotations, the heatmap's scale, the axis-unit mode) are read past.

let private str (e: JsonElement) (name: string) : string option =
    match e.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private kindOf (name: string) : ChartKind =
    match name with
    | "Bar" -> ChartKind.Bar
    | "Line" -> ChartKind.Line
    | "Area" -> ChartKind.Area
    | "Pie" -> ChartKind.Pie
    | "Scatter" -> ChartKind.Scatter
    | "Heatmap" -> ChartKind.Heatmap
    | other -> failwithf "unknown chart kind %s" other

let private decimalsOf (e: JsonElement) : int option =
    match e.TryGetProperty "decimals" with
    | true, d -> Some(d.GetInt32())
    | _ -> None

let private formatOf (e: JsonElement) : Format =
    match str e "$type" with
    | Some "Number" -> Format.Number(decimalsOf e)
    | Some "Percent" -> Format.Percent(decimalsOf e)
    | Some "Currency" -> Format.Currency((str e "isoCode").Value, decimalsOf e)
    | other -> failwithf "valueFormat %A is not read by this harness" other

let private cellOf (v: JsonElement) : obj option =
    match v.ValueKind with
    | JsonValueKind.String -> Some(box (v.GetString()))
    | JsonValueKind.Number -> Some(box (v.GetDouble()))
    | JsonValueKind.True -> Some(box true)
    | JsonValueKind.False -> Some(box false)
    | _ -> None

/// A neutral input → the spec and rows it lowers.
let private decodeInput (json: string) : ChartSpec<obj> * Map<string, obj> list =
    use doc = JsonDocument.Parse json
    let e = doc.RootElement

    let rows =
        [ for r in e.GetProperty("data").EnumerateArray() ->
              r.EnumerateObject()
              |> Seq.choose (fun p -> cellOf p.Value |> Option.map (fun v -> p.Name, v))
              |> Map.ofSeq ]

    let spec =
        { Defaults.chart<obj> with
            Kind = kindOf (str e "kind").Value
            XField = (str e "xField").Value
            YFields = [ for f in e.GetProperty("yFields").EnumerateArray() -> f.GetString() ]
            Stacked = e.GetProperty("stacked").GetBoolean()
            Title =
                match e.TryGetProperty "title" with
                | true, t when t.ValueKind = JsonValueKind.String -> Some(TextSource.Literal(t.GetString()))
                | true, t -> failwithf "a %A title is not read by this harness" t.ValueKind
                | _ -> None
            ValueFormat =
                match e.TryGetProperty "valueFormat" with
                | true, f -> Some(formatOf f)
                | _ -> None
            XScale =
                match str e "xScale" with
                | Some "Temporal" -> Some ChartXScale.Temporal
                | Some _ -> Some ChartXScale.Category
                | None -> None
            WithheldField = str e "withheldField"
            Interval =
                match e.TryGetProperty "interval" with
                | true, iv ->
                    Some
                        { Low = (str iv "low").Value
                          High = (str iv "high").Value }
                | _ -> None
            SizeField = str e "sizeField"
            PathField = str e "pathField"
            SortBy =
                match e.TryGetProperty "sortBy" with
                | true, s ->
                    Some
                        { Field = (str s "field").Value
                          Direction =
                            match str s "direction" with
                            | Some "desc" -> SortDirection.Desc
                            | _ -> SortDirection.Asc }
                | _ -> None
            Source = Binding.Static None }

    spec, rows

// ── The table model, encoded ─────────────────────────────────────────────────

/// A table model in canonical JSON (`caption` as a `TextSource`'s canonical
/// wire form, the bare string for a `Literal`), or `null` for no table.
let tableJson (table: Accessibility.ChartTable option) : string =
    match table with
    | None -> "null"
    | Some t ->
        Fuaran.Core.Canonical.write
            Fuaran.Core.Rendering.CrossHost
            (Fuaran.Core.JObj
                [ "caption", Generated.encodeTextSourceJson t.Caption
                  "columns", Fuaran.Core.JArr(t.Columns |> List.map Fuaran.Core.JStr)
                  "rowCount", Fuaran.Core.JInt t.RowCount
                  "rows", Fuaran.Core.JArr(t.Rows |> List.map (List.map Fuaran.Core.JStr >> Fuaran.Core.JArr)) ])

let private tableOfInput (json: string) : string =
    let spec, rows = decodeInput json
    tableJson (Charts.tableModel spec rows)

// ── Readers of a drawing, for "the values equal the values plotted" ──────────

/// Every tip in a drawing ("Series · Category · value …" on a per-datum mark).
let rec private tipsOf (shape: Shape) : string list =
    let style: DrawStyle =
        match shape with
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
        match style.Tip with
        | Some(TextSource.Literal t) -> [ t ]
        | _ -> []

    match shape with
    | Shape.Group(children, _) -> here @ List.collect tipsOf children
    | _ -> here

let private tryFindFixtures () : string option = Fuaran.Tests.CorpusRoot.tryFind ()

[<Tests>]
let chartTableTests =
    let fixtureTests =
        match tryFindFixtures () with
        | None ->
            [ test "table-model fixtures absent — skipped (standalone checkout)" {
                  Expect.isTrue true "wire-format-fixtures/ not found"
              } ]
        | Some fixturesRoot ->
            let lowering = Path.Combine(fixturesRoot, "chart-lowering")
            let dir = Path.Combine(lowering, "table-model")

            let sourceOf (family: string, name: string) =
                Path.Combine(lowering, family, name + ".input.json")

            if Environment.GetEnvironmentVariable "FUARAN_EMIT_CHART_LOWERING" = "1" then
                Directory.CreateDirectory dir |> ignore

                for case in tableCases do
                    let name = snd case
                    File.Copy(sourceOf case, Path.Combine(dir, name + ".input.json"), true)

                    File.WriteAllText(
                        Path.Combine(dir, name + ".table.json"),
                        tableOfInput (File.ReadAllText(sourceOf case))
                    )

            [ test "every table-model input is a byte copy of the chart-lowering input it names" {
                  for case in tableCases do
                      let name = snd case

                      Expect.equal
                          (File.ReadAllBytes(Path.Combine(dir, name + ".input.json")))
                          (File.ReadAllBytes(sourceOf case))
                          (name
                           + ": the copy drifted from its source (regenerate with FUARAN_EMIT_CHART_LOWERING=1)")
              }

              test "every table-model case yields its committed golden, byte for byte" {
                  for _, name in tableCases do
                      let golden = Path.Combine(dir, name + ".table.json")
                      Expect.isTrue (File.Exists golden) (name + ": golden missing")

                      Expect.equal
                          (tableOfInput (File.ReadAllText(Path.Combine(dir, name + ".input.json"))))
                          (File.ReadAllText golden)
                          (name + ": the table model drifted from its golden")
              }

              test "the subfamily is exactly the declared cases, and holds nothing else" {
                  let expected =
                      [ for _, name in tableCases do
                            yield name + ".input.json"
                            yield name + ".table.json" ]
                      |> Set.ofList

                  let actual = Directory.GetFiles dir |> Array.map Path.GetFileName |> Set.ofArray

                  Expect.equal actual expected "table-model/ holds the declared pairs"
              }

              test "a refused chart offers no table, and every other case one row per plotted row" {
                  for case in tableCases do
                      let spec, rows = decodeInput (File.ReadAllText(sourceOf case))

                      match Charts.tryLower spec rows, Charts.tableModel spec rows with
                      | Error _, None -> ()
                      | Ok _, Some table when spec.Kind <> ChartKind.Heatmap ->
                          Expect.equal table.RowCount rows.Length (snd case + ": one row per row")
                          Expect.equal table.Rows.Length rows.Length (snd case + ": every row shown")
                      | Ok _, Some _ -> ()
                      | drawn, table -> failtestf "%s: drawing %A but table %A" (snd case) drawn table
              }

              test "every per-datum mark's readout is the table's cell for that row and series" {
                  for case in tableCases do
                      let spec, rows = decodeInput (File.ReadAllText(sourceOf case))

                      match Charts.tryLower spec rows, Charts.tableModel spec rows with
                      | Ok drawing, Some table when spec.Kind <> ChartKind.Heatmap ->
                          let cells =
                              [ for row in table.Rows do
                                    for j in 1 .. table.Columns.Length - 1 -> table.Columns[j], row[0], row[j] ]

                          for tip in drawing.Shapes |> List.collect tipsOf do
                              match tip.Split " · " |> List.ofArray with
                              // An interval's readout is its two ends ("low – high"),
                              // which are two cells, and a withheld mark reads no value.
                              | series :: category :: value :: _ when value <> "withheld" && not (value.Contains " – ") ->
                                  Expect.contains
                                      cells
                                      (series, category, value)
                                      (sprintf "%s: the mark %s reads a value the table does not hold" (snd case) tip)
                              | _ -> ()
                      | _ -> ()
              } ]

    testList
        "Chart lowering — the data-table alternative (Phase 2776)"
        (fixtureTests
         @ [ test "the caption is the title, carried unresolved, or minted from the kind" {
                 let spec =
                     { Defaults.chart<obj> with
                         Kind = ChartKind.Area
                         Stacked = true
                         XField = "x"
                         YFields = [ "a"; "b" ] }

                 let rows = [ Map.ofList [ "x", box "p"; "a", box 1.0; "b", box 2.0 ] ]
                 let bound = TextSource.Bound(Binding.State("title", None))

                 match Charts.tableModel spec rows |> Option.map _.Caption with
                 | Some(TextSource.Literal minted) ->
                     Expect.equal minted "Stacked area chart data" "minted from the kind"
                 | other -> failtestf "the minted caption is %A" other

                 match Charts.tableModel { spec with Title = Some bound } rows |> Option.map _.Caption with
                 | Some(TextSource.Bound(Binding.State("title", None))) -> ()
                 | other -> failtestf "a bound title crosses unresolved, not as %A" other
             }

             test "a missing value is an empty cell and a withheld one says so, never a zero" {
                 let spec =
                     { Defaults.chart<obj> with
                         Kind = ChartKind.Bar
                         XField = "x"
                         YFields = [ "v" ]
                         WithheldField = Some "w" }

                 let rows =
                     [ Map.ofList [ "x", box "a"; "v", box 1.5; "w", box false ]
                       Map.ofList [ "x", box "b"; "w", box false ]
                       Map.ofList [ "x", box "c"; "v", box 9.0; "w", box true ] ]

                 Expect.equal
                     (Charts.tableModel spec rows |> Option.map _.Rows)
                     (Some [ [ "a"; "1.5" ]; [ "b"; "" ]; [ "c"; "withheld" ] ])
                     "the three cells"
             }

             test "a chart with no rows, or over the caps, offers no table" {
                 let spec =
                     { Defaults.chart<obj> with
                         Kind = ChartKind.Line
                         XField = "x"
                         YFields = [ "v" ] }

                 Expect.isNone (Charts.tableModel spec []) "no rows"

                 let many =
                     [ for i in 0..10 -> Map.ofList [ "x", box (string i); "v", box (float i) ] ]

                 let limits =
                     { Charts.ChartLimits.defaults with
                         MaxPointsPerSeries = 10 }

                 Expect.isNone (Charts.tableModelWith limits spec many) "over the point cap"
                 Expect.isSome (Charts.tableModelWith limits spec (List.truncate 10 many)) "at the point cap"
             }

             test "past the row limit the table holds the limit and counts every row" {
                 let spec =
                     { Defaults.chart<obj> with
                         Kind = ChartKind.Line
                         XField = "x"
                         YFields = [ "v" ] }

                 let rows =
                     [ for i in 1 .. Charts.tableRowLimit * 2 -> Map.ofList [ "x", box (string i); "v", box (float i) ] ]

                 let table = (Charts.tableModel spec rows).Value
                 Expect.equal table.Rows.Length Charts.tableRowLimit "the limit"
                 Expect.equal table.RowCount (Charts.tableRowLimit * 2) "the count"
                 Expect.equal (List.head table.Rows) [ "1"; "1" ] "the first rows, in order"
             } ])
