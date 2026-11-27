module Fuaran.UI.Renderer.Server.Tests.ChartTableCases

// ============================================================================
//  Phase 2776 — the charts the data-table suite renders.
//
//  Kept apart from the suite, and written against nothing newer than the
//  chart vocabulary the phase found, because the "policy off renders
//  byte-identically to before" claim is pinned against digests taken by
//  rendering EXACTLY these nodes on the tree before the phase. A case added
//  here needs its pre-phase digest taken the same way, or it belongs in the
//  suite's on-by-default tests only.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

let private row (cells: (string * obj) list) : Map<string, obj> = Map.ofList cells

let private chartOf (id: string) (spec: ChartSpec<obj>) (rows: Map<string, obj> list) : Node<obj> =
    Fuaran.chart
        id
        { spec with
            Source = Binding.Static(Some(Seq.ofList rows)) }

/// The cases, by name. Each node's id is `chart-<name>`.
let cases: (string * Node<obj>) list =
    [ "bar-single",
      chartOf
          "chart-bar-single"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              XField = "quarter"
              YFields = [ "revenue" ]
              Title = Some(TextSource.Literal "Revenue by quarter") }
          [ row [ "quarter", box "Q1"; "revenue", box 120.0 ]
            row [ "quarter", box "Q2"; "revenue", box 150.0 ]
            row [ "quarter", box "Q3"; "revenue", box 90.5 ]
            row [ "quarter", box "Q4"; "revenue", box 175.0 ] ]

      "bar-stacked-currency",
      chartOf
          "chart-bar-stacked-currency"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              Stacked = true
              XField = "region"
              YFields = [ "online"; "store" ]
              ValueFormat = Some(Format.Currency("GBP", None)) }
          [ row [ "region", box "North"; "online", box 1200.0; "store", box 3400.25 ]
            row [ "region", box "South <&>"; "online", box 900.0; "store", box 2100.0 ] ]

      "line-temporal",
      chartOf
          "chart-line-temporal"
          { Defaults.chart<obj> with
              Kind = ChartKind.Line
              XField = "day"
              YFields = [ "users" ]
              XScale = Some ChartXScale.Temporal
              Title = Some(TextSource.Literal "Active users") }
          [ row [ "day", box "2026-01-01"; "users", box 30.0 ]
            row [ "day", box "2026-01-02"; "users", box 45.0 ]
            row [ "day", box "2026-01-03" ]
            row [ "day", box "2026-01-04"; "users", box 60.0 ] ]

      "pie-percent",
      chartOf
          "chart-pie-percent"
          { Defaults.chart<obj> with
              Kind = ChartKind.Pie
              XField = "channel"
              YFields = [ "share" ]
              ValueFormat = Some(Format.Percent None) }
          [ row [ "channel", box "Search"; "share", box 0.45 ]
            row [ "channel", box "Social"; "share", box 0.3 ]
            row [ "channel", box "Direct"; "share", box 0.25 ] ]

      "scatter-channels",
      chartOf
          "chart-scatter-channels"
          { Defaults.chart<obj> with
              Kind = ChartKind.Scatter
              XField = "x"
              YFields = [ "y" ]
              Interval = Some { Low = "lo"; High = "hi" }
              SizeField = Some "n"
              PathField = Some "t" }
          [ row
                [ "x", box 1.0
                  "y", box 2.0
                  "lo", box 1.5
                  "hi", box 2.5
                  "n", box 10.0
                  "t", box 1.0 ]
            row
                [ "x", box 2.0
                  "y", box 3.5
                  "lo", box 4.0
                  "hi", box 3.0
                  "n", box 20.0
                  "t", box 2.0 ]
            row [ "x", box 3.0; "y", box 3.0; "n", box 5.0; "t", box 3.0 ] ]

      "bar-withheld-sorted",
      chartOf
          "chart-bar-withheld-sorted"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              XField = "arm"
              YFields = [ "effect" ]
              WithheldField = Some "suppressed"
              SortBy =
                  Some
                      { Field = "effect"
                        Direction = SortDirection.Desc } }
          [ row [ "arm", box "A"; "effect", box 12.0; "suppressed", box false ]
            row [ "arm", box "B"; "effect", box 18.0; "suppressed", box true ]
            row [ "arm", box "C"; "effect", box 7.0; "suppressed", box false ]
            row [ "arm", box "D"; "effect", box 15.0; "suppressed", box false ] ]

      "heatmap-withheld",
      chartOf
          "chart-heatmap-withheld"
          { Defaults.chart<obj> with
              Kind = ChartKind.Heatmap
              XField = "month"
              YFields = [ "team"; "score" ]
              WithheldField = Some "suppressed"
              Title = Some(TextSource.Literal "Score") }
          [ row
                [ "month", box "Jan"
                  "team", box "Alpha"
                  "score", box 38.0
                  "suppressed", box false ]
            row
                [ "month", box "Feb"
                  "team", box "Alpha"
                  "score", box 45.0
                  "suppressed", box true ]
            row
                [ "month", box "Jan"
                  "team", box "Beta"
                  "score", box 51.0
                  "suppressed", box false ]
            row
                [ "month", box "Jan"
                  "team", box "Alpha"
                  "score", box 99.0
                  "suppressed", box false ] ]

      "bar-derived-series",
      chartOf
          "chart-bar-derived-series"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              XField = "q"
              YFields = [] }
          [ row [ "q", box "Q1"; "a", box 1.0; "b", box 2.0; "note", box "x" ]
            row [ "q", box "Q2"; "a", box 3.0; "b", box 4.0; "note", box "y" ] ]

      "bar-no-rows",
      chartOf
          "chart-bar-no-rows"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              XField = "q"
              YFields = [ "v" ] }
          []

      "bar-refused-series",
      chartOf
          "chart-bar-refused-series"
          { Defaults.chart<obj> with
              Kind = ChartKind.Bar
              XField = "q"
              YFields = [ for i in 1..33 -> sprintf "s%d" i ] }
          [ row (("q", box "Q1") :: [ for i in 1..33 -> sprintf "s%d" i, box (float i) ]) ] ]
