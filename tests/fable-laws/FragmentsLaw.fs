module FableLaws.FragmentsLaw

// ============================================================================
//  Law 15 (Phase 2874) — the certified fragment library builds the same trees,
//  on both pipelines.
//
//  `Fuaran.UI.Fragments` is certified on .NET: the certification suite drives
//  every fragment's holes and the corpus pins each declaration and example as
//  canonical wire bytes. A browser client compiles the same sources through
//  Fable, and nothing in either pipeline's own suite can see whether the tree
//  it builds there is the tree the certification measured. So this law builds
//  them here and prints their canonical wire bytes, and the runner's line
//  comparison of the two outputs is the cross-host claim:
//
//   - every `Stdlib.all` declaration and representative application, and every
//     `boundExamples` application (the corpus's own set);
//   - the parameter panel, the scenario comparison and the refinable table
//     materialised from a binding, and built by their typed builders over a
//     model and a table;
//   - `refinedPipeline` over a sorted, filtered and paged view, as its
//     canonical pipeline and as the rows it evaluates to.
//
//  Wire text is printed with every character outside printable ASCII written
//  as `\uXXXX`, so the comparison measures the bytes the encoder produced and
//  never the console's encoding of them (the harness's output discipline).
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Fragments
open Fuaran.UI.OpStream.Abstractions

/// The law's printed lines and its violation count.
type FragmentsOutcome = { Lines: string list; Violations: int }

/// Printable ASCII kept, everything else as a `\uXXXX` escape over its UTF-16 unit.
let private ascii (s: string) : string =
    let sb = System.Text.StringBuilder()

    for c in s do
        let code = int c

        if code >= 32 && code <= 126 then
            sb.Append c |> ignore
        else
            sb.Append("\\u" + code.ToString("x4")) |> ignore

    sb.ToString()

let private wire (node: Node<obj>) : string = ascii (CanonicalJson.encodeNode node)

let private pipelineWire (pipeline: Fuaran.Compute.Transform list) : string =
    JArr(List.map Fuaran.Compute.DataFrameCodec.encodeTransform pipeline)
    |> Canonical.write Rendering.CrossHost
    |> ascii

// ── The scenario comparison's model (the certification suite's revenue model) ──

let private scenarioRows: DataSource =
    Embedded
        { Schema =
            [ Field.create "item" StringType
              Field.create "units" FloatType
              Field.create "price" FloatType ]
          Columns =
            [ Column.ofStrs "item" (Vector.ofList [ "north"; "south" ]) AllValid
              Column.ofFloats "units" (Vector.ofList [ 100.0; 40.0 ]) AllValid
              Column.ofFloats "price" (Vector.ofList [ 2.0; 5.0 ]) AllValid ] }

/// `units * (1 + growth) * (price + uplift)`.
let private revenueModel: ScenarioModel =
    { Source = scenarioRows
      Key = "item"
      Model =
        { Name = "revenue"
          Params =
            [ { Name = "units"; Default = None }
              { Name = "price"; Default = None }
              { Name = "growth"; Default = None }
              { Name = "uplift"; Default = None } ]
          Body =
            Fuaran.Compute.Binary(
                Fuaran.Compute.Mul,
                Fuaran.Compute.Binary(
                    Fuaran.Compute.Mul,
                    Fuaran.Compute.Arg "units",
                    Fuaran.Compute.Binary(
                        Fuaran.Compute.Add,
                        Fuaran.Compute.ColExpr.Lit(Float 1.0),
                        Fuaran.Compute.Arg "growth"
                    )
                ),
                Fuaran.Compute.Binary(Fuaran.Compute.Add, Fuaran.Compute.Arg "price", Fuaran.Compute.Arg "uplift")
            ) }
      Columns = [ "units", "units"; "price", "price" ] }

let private baseline: ScenarioSide =
    { Label = "Baseline"
      Parameters =
        [ "growth", Binding.Static(Some(JFloat 0.05))
          "uplift", Binding.Static(Some(JFloat 0.0)) ] }

/// The live side: each parameter reads the panel control of the same name.
let private adjusted: ScenarioSide =
    { Label = "Adjusted"
      Parameters =
        [ "growth", Binding.Filter("growth", None)
          "uplift", Binding.Filter("uplift", None) ] }

// ── The refinable table (the certification suite's seven orders) ──

let private column (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failwithf "the law's fixture column %s must build: %A" name e

let private str (v: string option) : Cell =
    match v with
    | Some s -> Str s
    | None -> Cell.Null

let private num (v: float option) : Cell =
    match v with
    | Some f -> Float f
    | None -> Cell.Null

let private orderRows: DataSource =
    Embedded
        { Schema =
            [ Field.create "order" StringType
              Field.create "customer" StringType
              Field.create "region" StringType
              Field.create "total" FloatType ]
          Columns =
            [ column "order" StringType ([ "o1"; "o2"; "o3"; "o4"; "o5"; "o6"; "o7" ] |> List.map (Some >> str))
              column
                  "customer"
                  StringType
                  ([ Some "beta"
                     Some "Acme"
                     None
                     Some "acme"
                     Some "Zed"
                     Some "alpha"
                     Some "Beta" ]
                   |> List.map str)
              column
                  "region"
                  StringType
                  ([ "north"; "south"; "north"; "east"; "south"; "north"; "east" ]
                   |> List.map (Some >> str))
              column
                  "total"
                  FloatType
                  ([ Some 30.0; Some 10.0; Some 30.0; None; Some 5.5; Some 120.0; Some 10.0 ]
                   |> List.map num) ] }

let private regions: SelectOption list =
    [ "north"; "south"; "east" ]
    |> List.map (fun r ->
        ({ Label = TextSource.Literal r
           Value = r }
        : SelectOption))

let private ordersTable: RefinableTable =
    { Source = orderRows
      Key = "order"
      Columns =
        [ { Field = "customer"
            Label = "Customer"
            Kind = RefinableKind.Text
            Sortable = true
            Filter = None }
          { Field = "region"
            Label = "Region"
            Kind = RefinableKind.Text
            Sortable = true
            Filter = Some regions }
          { Field = "total"
            Label = "Total"
            Kind = RefinableKind.Number
            Sortable = true
            Filter = None } ]
      PageSize = 3 }

/// The order keys of the rows a pipeline evaluates to over the orders, and the
/// rows' canonical text — `None` when the source does not resolve.
let private evaluate (pipeline: Fuaran.Compute.Transform list) : (string list * string) option =
    let source = Binding.Transform(TransformSource.Data orderRows, pipeline, None)

    match Fuaran.UI.Renderer.BindingResolver.resolveRowsWithFields Fuaran.UI.Renderer.BindingResolver.empty source with
    | Fuaran.UI.Renderer.BindingResolver.Resolved rows, _ ->
        let rows = Seq.toList rows

        let keys =
            rows
            |> List.map (fun row ->
                match Fuaran.UI.HostPrelude.RowProjection.value row "order" with
                | Fuaran.UI.HostPrelude.CellValue.Text s -> s
                | _ -> "<not-text>")

        Some(keys, ascii (Canonical.write Rendering.CrossHost (Fuaran.UI.RowCodec.encodeRows rows)))
    | _ -> None

/// The views the law refines: a text sort over a membership filter (the case
/// folding and the stable tie both show), and a number sort on a later page.
let private views: (string * RefinableView * string list) list =
    [ "customer-desc-north-east-p1",
      { Sort = Some(0, SortDirection.Desc)
        Page = 1
        Filters = [ "region", [ "north"; "east" ] ] },
      [ "o1"; "o7"; "o6" ]
      "total-asc-p2",
      { Sort = Some(2, SortDirection.Asc)
        Page = 2
        Filters = [ "region", [] ] },
      [ "o1"; "o3"; "o6" ] ]

let fragmentsLaw () : FragmentsOutcome =
    let violations = ref 0

    let built (label: string) (result: Result<Node<obj>, string>) : string =
        match result with
        | Ok tree -> "FRAGMENTS " + label + " " + wire tree
        | Error e ->
            violations.Value <- violations.Value + 1
            "FRAGMENTS " + label + " refused " + ascii e

    let library =
        [ for f in Stdlib.all<obj> do
              yield "FRAGMENTS decl " + f.Name + " " + wire f.Decl
              yield "FRAGMENTS example " + f.Name + " " + wire f.Example
          for (name, what, node) in Stdlib.boundExamples<obj> do
              yield "FRAGMENTS bound " + name + " (" + ascii what + ") " + wire node ]

    let panelArgs: Map<string, obj> =
        Map.ofList [ "count", box 2; "param-1-name", box "growth"; "param-2-name", box "uplift" ]

    let materialised =
        [ "FRAGMENTS materialised parameter-panel "
          + wire (Stdlib.parameterPanel<obj>.Materialize panelArgs)
          "FRAGMENTS materialised scenario-comparison "
          + wire (Stdlib.scenarioComparison<obj>.Materialize Map.empty)
          "FRAGMENTS materialised refinable-table "
          + wire (Stdlib.refinableTable<obj>.Materialize Map.empty) ]

    let builders =
        [ built
              "scenarioComparisonOf"
              (Stdlib.scenarioComparisonOf<obj> "compare" "Revenue" revenueModel baseline adjusted)
          built
              "refinableTableOf"
              (Stdlib.refinableTableOf<obj> "orders" "Orders" Fuaran.Compute.SchemaWalk.noSources ordersTable) ]

    let refined =
        [ for (label, view, expected) in views do
              let pipeline = Stdlib.refinedPipeline "orders" ordersTable view
              yield "FRAGMENTS refinedPipeline " + label + " " + pipelineWire pipeline

              match evaluate pipeline with
              | Some(keys, rows) ->
                  if keys <> expected then
                      violations.Value <- violations.Value + 1

                  yield
                      sprintf
                          "FRAGMENTS refined-rows %s keys=%s expected=%b"
                          label
                          (String.concat "," keys)
                          (keys = expected)

                  yield "FRAGMENTS refined-rows " + label + " " + rows
              | None ->
                  violations.Value <- violations.Value + 1
                  yield "FRAGMENTS refined-rows " + label + " unresolved" ]

    let lines = library @ materialised @ builders @ refined

    { Lines =
        lines
        @ [ sprintf "FRAGMENTS summary lines=%d violations=%d" (List.length lines) violations.Value ]
      Violations = violations.Value }
