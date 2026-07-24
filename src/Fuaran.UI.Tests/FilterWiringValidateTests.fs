module Fuaran.UI.Tests.FilterWiringValidate

// ============================================================================
//  The consolidated 421/424/425 validator follow-up: the cross-tree filter
//  consumption union + grid display-floor checks, over `BindingWalk`.
//
//    FUARAN074 — decorative filter (declared chip nothing consumes; a chip's
//                own Binding.Filter(self, None)-read does NOT count)
//    FUARAN075 — dangling filter reference (a Query.dependsOn / Transform
//                param Filter source naming an undeclared filter — error)
//    FUARAN076 — unreferenced Transform param (not in Transform.paramsOf)
//    FUARAN077 — blank grid column (neither Value nor Field)
//    FUARAN078 — unstable row identity (neither RowKey nor RowKeyField)
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

type private Msg = NoOp

let private dashboard id children : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

/// A declarative chip (423 shape): named filter, value self-reading its own
/// `$filters.<name>`, no handler.
let private declarativeChip (name: string) : Node<Msg> =
    Fuaran.filters
        "chips"
        [ { Name = name
            Label = TextSource.Literal name
            Field = FormFieldKind.Text(Binding.Filter(name, None), None) } ]

let private embeddedSource =
    Fuaran.Core.Embedded
        { Schema = [ "dept", Fuaran.Core.StringType ]
          Columns = [ Fuaran.Core.Column.create "dept" Fuaran.Core.StringType [ Fuaran.Core.Str "eng" ] ] }

let private paramPipeline: Fuaran.Core.Transform list =
    [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "dept")) ]

let private gridWith (source: Binding<obj seq>) : Node<Msg> =
    { Id = NodeId "grid"
      Kind =
        NodeKind.Visualisation(
            VisKind.DataGrid
                { Source = source
                  RowKey = None
                  RowKeyField = Some "dept"
                  Columns =
                    [ { Label = "Dept"
                        Value = None
                        Field = Some "dept"
                        Format = CellFormat.None
                        Kind = CellKindErased.Text
                        Width = ColumnWidth.Auto } ]
                  OnRowClick = None
                  Editable = false
                  StaticRows = None }
        )
      State = Defaults.stateBehaviour<Msg>
      Style = Defaults.style
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

[<Tests>]
let tests =
    testList
        "421/424/425 follow-up — filter wiring + grid display checks"
        [ test "FUARAN074: a declared chip nothing consumes is decorative (self-read exempt)" {
              let tree = dashboard "root" [ declarativeChip "q" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DecorativeFilter("chips", "q"))
                      "DecorativeFilter surfaced despite the chip's own self-read"
              | Ok() -> failtest "Expected FUARAN074 defect, got Ok"
          }

          test "FUARAN074 does not fire when a Transform param consumes the filter" {
              let grid =
                  gridWith (Binding.Transform(embeddedSource, paramPipeline, [ "dept", Binding.Filter("dept", None) ]))

              let tree = dashboard "root" [ declarativeChip "dept"; grid ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a consumed filter, got: %A" defects
          }

          test "FUARAN074 does not fire when a Query.dependsOn consumes the filter" {
              let reader =
                  Fuaran.metric
                      "orders-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Orders"
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), [ "status" ]) }

              let tree = dashboard "root" [ declarativeChip "status"; reader ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a dependsOn-consumed filter, got: %A" defects
          }

          test "FUARAN075: a Query.dependsOn naming an undeclared filter is dangling (error)" {
              let reader =
                  Fuaran.metric
                      "orders-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Orders"
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), [ "no-such-filter" ]) }

              let tree = dashboard "root" [ reader ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingFilterReference("orders-metric", "no-such-filter"))
                      "DanglingFilterReference surfaced for the undeclared dependsOn name"
              | Ok() -> failtest "Expected FUARAN075 defect, got Ok"
          }

          test "FUARAN075: a Transform param Filter source naming an undeclared filter is dangling" {
              let grid =
                  gridWith (Binding.Transform(embeddedSource, paramPipeline, [ "dept", Binding.Filter("dept", None) ]))

              let tree = dashboard "root" [ grid ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingFilterReference("grid", "dept"))
                      "DanglingFilterReference surfaced for the undeclared param source"
              | Ok() -> failtest "Expected FUARAN075 defect, got Ok"
          }

          test "FUARAN076: a params entry the pipeline never references is unreferenced (warn)" {
              let grid =
                  gridWith (
                      Binding.Transform(
                          embeddedSource,
                          paramPipeline,
                          [ "dept", Binding.Filter("dept", None)
                            "orphan", Binding.State("x", (box "" |> Unchecked.nonNull)) ]
                      )
                  )

              let tree = dashboard "root" [ declarativeChip "dept"; grid ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UnreferencedTransformParam("grid", "orphan"))
                      "UnreferencedTransformParam surfaced for the dead params entry"
              | Ok() -> failtest "Expected FUARAN076 defect, got Ok"
          }

          test "FUARAN077 / FUARAN078: a blank column and a keyless grid are flagged" {
              let bareGrid: Node<Msg> =
                  { Id = NodeId "bare-grid"
                    Kind =
                      NodeKind.Visualisation(
                          VisKind.DataGrid
                              { Source = Binding.Static Seq.empty
                                RowKey = None
                                RowKeyField = None
                                Columns =
                                  [ { Label = "Blank"
                                      Value = None
                                      Field = None
                                      Format = CellFormat.None
                                      Kind = CellKindErased.Text
                                      Width = ColumnWidth.Auto } ]
                                OnRowClick = None
                                Editable = false
                                StaticRows = None }
                      )
                    State = Defaults.stateBehaviour<Msg>
                    Style = Defaults.style
                    Accessibility = None
                    Motion = Defaults.Motion.none
                    ExtraAttributes = None }

              let tree = dashboard "root" [ bareGrid ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.BlankGridColumn("bare-grid", "Blank"))
                      "BlankGridColumn surfaced"

                  Expect.contains defects (PreEmitDefect.UnstableRowIdentity "bare-grid") "UnstableRowIdentity surfaced"
              | Ok() -> failtest "Expected FUARAN077 + FUARAN078 defects, got Ok"
          } ]
