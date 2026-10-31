module Fuaran.UI.Renderer.Benchmarks.Surfaces

// ============================================================================
//  The measured surfaces (Phase 2529). Pipeline-neutral: every surface is
//  plain authoring code, so the solution build compiles it on .NET and the
//  browser run compiles the same source under Fable.
//
//   - sites24: the generated 24-site tree of Phase 1761's cost reading: one
//     2,000-row table, 24 Transform-fed charts, each reading exactly one chip
//     and declaring all four, so one chip write reaches a quarter of the charts.
//   - catalog: every kind `samples/catalog/Matrix.fs` declares, once per tone.
//   - shell: a many-module shell, every module mounted at once: the worst case
//     of a host whose modules are all live.
//   - table200 (Phase 2069): 200 rows of two text cells and a button, the shape a
//     host rebuilds on every dispatch, declaring no seed and no fragment.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Samples.Catalog

/// Which store a surface's write lands in.
type WriteChannel =
    | StateWrite
    | FilterWrite

type Surface =
    {
        Name: string
        Tree: Node<unit>
        /// `Tree` plus one more child at the end of the root: the structural change.
        Grown: Node<unit>
        Channel: WriteChannel
        Key: string
        /// The two values a write alternates between.
        Values: obj * obj
        /// The subtrees whose output actually depends on `Key`.
        Readers: Node<unit> list
        /// `Tree` with its charts replaced by a placeholder, on the surface that
        /// calibrates the chart stage.
        WithoutCharts: Node<unit> option
    }

let private nonNull (x: obj) : obj = Unchecked.nonNull x

let private dashboard (id: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<unit> with
            Children = children }

let private card (id: string) (heading: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.card
        id
        { Defaults.card<unit> with
            Heading = Some(TextSource.Literal heading)
            Children = children }

let private metric (id: string) (label: string) (value: Binding<float>) : Node<unit> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal label
            Value = value }

let private grownChild: Node<unit> =
    metric "grown-child" "Added" (Binding.Static(Some 1.0))

let private fragmentDecl (name: string) (body: Node<unit>) : Node<unit> =
    Fuaran.fragmentDecl
        ("decl-" + name)
        { Defaults.fragmentDecl<unit> with
            Name = name
            Body = body }

// ─── The 24-site tree ──────────────────────────────────────────────────────

/// The Phase 1761 generator: a deterministic 64-bit LCG, so a reading replays.
type private Rng = { mutable State: uint64 }

let private rngOf (seed: int) : Rng =
    { State = uint64 seed * 6364136223846793005UL + 1442695040888963407UL }

let private intBelow (bound: int) (r: Rng) : int =
    r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
    int ((r.State >>> 33) % uint64 bound)

let private stringChips =
    [ "region", [ "north"; "south"; "east"; "west" ]
      "genre", [ "drama"; "docs"; "comedy" ]
      "month", [ "jan"; "feb"; "mar"; "apr" ] ]

let private floorChip = "floor"
let private allChips = (stringChips |> List.map fst) @ [ floorChip ]

let private tableOf (rows: int) (r: Rng) : Table =
    let pick (domain: string list) = domain[intBelow domain.Length r]

    let cells =
        [ for _ in 1..rows ->
              (stringChips |> List.map (fun (_, domain) -> Str(pick domain)))
              @ [ Float(float (intBelow 50 r)) ] ]

    let names = (stringChips |> List.map fst) @ [ "n" ]
    let types = (stringChips |> List.map (fun _ -> StringType)) @ [ FloatType ]
    let schema = List.map2 Field.create names types

    { Schema = schema
      Columns =
        schema
        |> List.mapi (fun i f ->
            match Column.ofCells f.Name f.Type (cells |> List.map (fun row -> row[i])) with
            | Ok c -> c
            | Error e -> failwithf "column %s holds a cell its type cannot: %A" f.Name e) }

let private readStep (chip: string) : Transform =
    if chip = floorChip then
        Filter(Binary(Ge, Col "n", Param floorChip))
    else
        Filter(Binary(Eq, Col chip, Param chip))

/// Every site DECLARES every chip, as the generator does: the env binds more
/// than a pipeline reads.
let private declaredParams: TransformParam list =
    allChips
    |> List.map (fun c ->
        { Name = c
          From = Binding.Filter(c, None) })

let private siteChart (table: Table) (i: int) : Node<unit> =
    let chip = allChips[i % allChips.Length]

    // The generator's two chart-worthy tails, alternated: a grouped count and a
    // sorted top-N. Its third shape, no tail, feeds a chart every filtered row.
    let tail, yField =
        if i % 2 = 0 then
            [ GroupBy([ "region" ], [ { Name = "count"; Fn = Count; Of = "n" } ]) ], "count"
        else
            [ Transform.sortBy [ "n", Desc ]; Transform.limit 5 0 ], "n"

    Fuaran.chart
        $"site-{i}-chart"
        { Defaults.chart<unit> with
            Source = Binding.Transform(TransformSource.Data(Embedded table), readStep chip :: tail, Some declaredParams)
            Kind = ChartKind.Bar
            XField = "region"
            YFields = [ yField ]
            Title = Some(TextSource.Literal $"Site {i}") }

let private site (chart: Node<unit>) (i: int) : Node<unit> =
    card $"site-{i}" $"Site {i}" [ Fuaran.fragmentRef $"site-{i}-legend" "site-legend"; chart ]

let private sites24: Surface =
    let table = tableOf 2_000 (rngOf (20260927 + 24))

    let legend =
        fragmentDecl
            "site-legend"
            (Fuaran.stack
                "site-legend-body"
                { Defaults.stack<unit> with
                    Children =
                        [ metric "site-legend-a" "Rows" (Binding.Static(Some 2000.0))
                          metric "site-legend-b" "Chips" (Binding.Static(Some 4.0)) ] })

    let root (chartOf: int -> Node<unit>) (extra: Node<unit> list) =
        dashboard "sites" ([ legend ] @ [ for i in 0..23 -> site (chartOf i) i ] @ extra)

    let chart = siteChart table

    let readers =
        [ for i in 0..23 do
              if i % allChips.Length = 0 then
                  site (chart i) i ]

    { Name = "sites-24"
      Tree = root chart []
      Grown = root chart [ grownChild ]
      Channel = FilterWrite
      Key = "region"
      Values = nonNull (box "north"), nonNull (box "south")
      Readers = readers
      WithoutCharts = Some(root (fun i -> metric $"site-{i}-chart" "Placeholder" (Binding.Static(Some 0.0))) []) }

// ─── The catalog surface ───────────────────────────────────────────────────

let private catalog: Surface =
    let key = "bench.catalog"

    let reader = metric "catalog-reader" "State reader" (Binding.State(key, None))

    let cards =
        [ for entry in Matrix.entries do
              for tone in Matrix.allTones do
                  card
                      $"catalog-{entry.Id}-{Matrix.toneLabel tone}"
                      entry.Label
                      [ entry.Build(tone, StyleWeight.Standard, Emphasis.Normal) ] ]

    let root (extra: Node<unit> list) =
        dashboard "catalog" ([ reader ] @ cards @ extra)

    { Name = "catalog"
      Tree = root []
      Grown = root [ grownChild ]
      Channel = StateWrite
      Key = key
      Values = nonNull (box 1.0), nonNull (box 2.0)
      Readers = [ reader ]
      WithoutCharts = None }

// ─── The many-module shell ─────────────────────────────────────────────────

let private moduleCount = 40

let private moduleKey (m: int) (k: int) : string = $"module.{m}.metric.{k}"

let private shellModule (m: int) : Node<unit> =
    let metrics =
        [ for k in 0..5 -> metric $"m{m}-metric-{k}" $"Metric {k}" (Binding.State(moduleKey m k, None)) ]

    let table =
        Fuaran.table
            $"m{m}-table"
            { Defaults.table<unit> with
                Headers = [ TextSource.Literal "Item"; TextSource.Literal "State" ]
                Rows =
                    [ for r in 0..19 ->
                          [ TextSource.Literal $"Item {r}"
                            TextSource.Literal(if r % 2 = 0 then "Open" else "Closed") ] ] }

    let form =
        Fuaran.form
            $"m{m}-form"
            { Defaults.form<unit> with
                SubmitLabel = TextSource.Literal "Save"
                Fields =
                    [ for f in 0..2 ->
                          { Defaults.formField<unit> with
                              Id = $"m{m}-field-{f}"
                              Label = TextSource.Literal $"Field {f}"
                              Kind =
                                  FormFieldKind.Text(
                                      Some(FieldValue.ofText (Binding.Static(Some ""))),
                                      Some(FieldChange.ofText (fun _ -> Action.Chain []))
                                  ) } ] }

    let rows: Row seq =
        seq {
            for p in 0..11 -> Map.ofList [ "x", box $"p{p}" |> nonNull; "y", box (float ((p * 7 + m) % 13)) |> nonNull ]
        }

    let chart =
        Fuaran.chart
            $"m{m}-chart"
            { Defaults.chart<unit> with
                Source = Binding.Static(Some rows)
                Kind = ChartKind.Line
                XField = "x"
                YFields = [ "y" ]
                Title = Some(TextSource.Literal $"Module {m}") }

    card
        $"module-{m}"
        $"Module {m}"
        (Fuaran.fragmentRef $"m{m}-toolbar" "module-toolbar" :: metrics
         @ [ table; form; chart ])

let private shell: Surface =
    let toolbar =
        fragmentDecl
            "module-toolbar"
            (Fuaran.stack
                "module-toolbar-body"
                { Defaults.stack<unit> with
                    Orientation = Orientation.Horizontal
                    Children =
                        [ for b in 0..3 ->
                              Fuaran.button
                                  $"module-toolbar-{b}"
                                  { Defaults.button<unit> with
                                      Label = TextSource.Literal $"Action {b}" } ] })

    let modules = [ for m in 0 .. moduleCount - 1 -> shellModule m ]

    let root (extra: Node<unit> list) =
        dashboard "shell" ([ toolbar ] @ modules @ extra)

    { Name = "shell-40"
      Tree = root []
      Grown = root [ grownChild ]
      Channel = StateWrite
      Key = moduleKey 0 0
      Values = nonNull (box 1.0), nonNull (box 2.0)
      Readers = [ modules.Head ]
      WithoutCharts = None }

// ─── The 200-row table ─────────────────────────────────────────────────────

let private tableRow (i: int) : Node<unit> =
    let id = "r" + string i

    Fuaran.card
        id
        { Defaults.card<unit> with
            Children =
                [ Fuaran.markdown (id + "-label") ("Row " + string i)
                  Fuaran.markdown (id + "-value") (string (i * 7))
                  Fuaran.button
                      (id + "-bump")
                      { Defaults.button with
                          Label = TextSource.Literal "+"
                          OnClick = Action.Print } ] }

let private table200: Surface =
    let key = "table-tick"
    let tick = metric "tick" "Tick" (Binding.State(key, Some 0.0))
    let rows = [ for i in 0..199 -> tableRow i ]

    let root (extra: Node<unit> list) =
        dashboard "table" ([ tick ] @ rows @ extra)

    { Name = "table-200"
      Tree = root []
      Grown = root [ grownChild ]
      Channel = StateWrite
      Key = key
      Values = nonNull (box 1.0), nonNull (box 2.0)
      Readers = [ tick ]
      WithoutCharts = None }

let all: Surface list = [ sites24; catalog; shell; table200 ]
