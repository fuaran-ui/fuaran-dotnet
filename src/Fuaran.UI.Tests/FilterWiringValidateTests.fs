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
//    FUARAN090 — inert editable grid (editable: true without a direct
//                Binding.State source — Phase 663 write-back floor)
//    FUARAN114 — a column `field` / `rowKeyField` naming a column absent from
//                the source's statically-known schema (Phase 1149 — error)
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
            Kind = FormFieldKind.Text(Some(Binding.Filter(name, None)), None) } ]

let private embeddedSource =
    Fuaran.Core.Embedded
        { Schema = [ "dept", Fuaran.Core.StringType ]
          Columns = [ Fuaran.Core.Column.create "dept" Fuaran.Core.StringType [ Fuaran.Core.Str "eng" ] ] }

let private paramPipeline: Fuaran.Core.Transform list =
    [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "dept")) ]

let private gridWithEditable (editable: bool) (source: Binding<Row seq>) : Node<Msg> =
    { Id = "grid"
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = source
              RowKey = None
              RowKeyField = Some "dept"
              Columns =
                [ { Label = "Dept"
                    Value = None
                    Field = Some "dept"
                    Sortable = None
                    Editable = None
                    Format = CellFormat.None
                    Kind = CellKindErased.Text
                    Width = ColumnWidth.Auto } ]
              OnRowClick = None
              Editable = editable
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false
              Exportable = false }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None
      Tooltip = None }

let private gridWith (source: Binding<Row seq>) : Node<Msg> = gridWithEditable false source

/// A read-only grid naming the fields it projects — the shape FUARAN114 judges.
/// `columnFields` are the column `field`s in order; `rowKeyField` is the grid's
/// own row-identity name.
let private gridNamingFields
    (columnFields: string list)
    (rowKeyField: string option)
    (source: Binding<Row seq>)
    : Node<Msg> =
    { Id = "grid"
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = source
              RowKey = None
              RowKeyField = rowKeyField
              Columns =
                columnFields
                |> List.map (fun f ->
                    { Label = f
                      Value = None
                      Field = Some f
                      Sortable = None
                      Editable = None
                      Format = CellFormat.None
                      Kind = CellKindErased.Text
                      Width = ColumnWidth.Auto })
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false
              Exportable = false }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None
      Tooltip = None }

/// `embeddedSource` carries exactly one column, `dept`.
let private deptSchema = [ "dept" ]

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
                  gridWith (
                      Binding.Transform(
                          TransformSource.Data(embeddedSource),
                          paramPipeline,
                          Some
                              [ { From = Binding.Filter("dept", None)
                                  Name = "dept" } ]
                      )
                  )

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
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), Some [ "status" ]) }

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
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), Some [ "no-such-filter" ]) }

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
                  gridWith (
                      Binding.Transform(
                          TransformSource.Data(embeddedSource),
                          paramPipeline,
                          Some
                              [ { From = Binding.Filter("dept", None)
                                  Name = "dept" } ]
                      )
                  )

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
                          TransformSource.Data(embeddedSource),
                          paramPipeline,
                          Some
                              [ { From = Binding.Filter("dept", None)
                                  Name = "dept" }
                                { From = Binding.State("x", Some(Fuaran.Core.JStr ""))
                                  Name = "orphan" } ]
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
                  { Id = "bare-grid"
                    Kind =
                      NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Static(Some Seq.empty)
                            RowKey = None
                            RowKeyField = None
                            Columns =
                              [ { Label = "Blank"
                                  Value = None
                                  Field = None
                                  Sortable = None
                                  Editable = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            TransferInKey = None
                            TransferOutKey = None
                            StaticRows = None
                            KeepRowsTogether = false
                            RepeatHeader = false
                            Exportable = false }
                      )
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = Defaults.Motion.none
                    ExtraAttributes = None
                    Tooltip = None }

              let tree = dashboard "root" [ bareGrid ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.BlankGridColumn("bare-grid", "Blank"))
                      "BlankGridColumn surfaced"

                  Expect.contains defects (PreEmitDefect.UnstableRowIdentity "bare-grid") "UnstableRowIdentity surfaced"
              | Ok() -> failtest "Expected FUARAN077 + FUARAN078 defects, got Ok"
          }

          // Phase 812 — FUARAN092: email protection is only meaningful over a
          // mailto: href; a statically non-mailto href is dead intent.
          test "FUARAN092: email protection over a non-mailto static href is flagged" {
              let link =
                  Fuaran.linkSpec
                      "lk"
                      { Defaults.link with
                          Href = Binding.Static(Some "/contact")
                          Label = TextSource.Literal "Email"
                          Protection = Some LinkProtection.Email }

              let tree = dashboard "root" [ link ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.ProtectedNonMailtoLink "lk") "ProtectedNonMailtoLink surfaced"
              | Ok() -> failtest "Expected FUARAN092 defect, got Ok"
          }

          test "FUARAN092 does not fire for a protected mailto nor an unprotected link" {
              let tree =
                  dashboard
                      "root"
                      [ Fuaran.emailLink "lk1" "user@example.com" "user@example.com"
                        Fuaran.link "lk2" "/contact" "Contact" ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok, got %A" defects
          }

          test "FUARAN090: editable over a non-State source is inert and flagged" {
              let grid =
                  gridWithEditable true (Binding.Transform(TransformSource.Data(embeddedSource), [], None))

              let tree = dashboard "root" [ grid ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.InertEditableGrid "grid") "InertEditableGrid surfaced"
              | Ok() -> failtest "Expected FUARAN090 defect, got Ok"
          }

          test "FUARAN090 does not fire for an editable State-sourced grid, nor for editable=false" {
              let stateRows: Row seq =
                  Seq.singleton (Map.ofList [ "dept", (box "eng" |> Unchecked.nonNull) ])

              let editableStateGrid =
                  gridWithEditable true (Binding.State("grid-rows", Some stateRows))

              match PreEmitValidate.validate (dashboard "root" [ editableStateGrid ]) with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for an editable State-sourced grid, got: %A" defects

              let readOnlyTransformGrid =
                  gridWithEditable false (Binding.Transform(TransformSource.Data(embeddedSource), [], None))

              match PreEmitValidate.validate (dashboard "root" [ readOnlyTransformGrid ]) with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a read-only Transform grid, got: %A" defects
          }

          // Phase 1149 — FUARAN114: the grid's read-side grounding, the twin of
          // FUARAN086's chart rule. Positive, negative, and the two shapes where
          // the schema is not derivable and the rule must stay silent.
          test "FUARAN114: a column field absent from the source's schema is an error" {
              let grid =
                  gridNamingFields
                      [ "dept"; "headcount" ]
                      (Some "dept")
                      (Binding.Transform(TransformSource.Data(embeddedSource), [], None))

              match PreEmitValidate.validate (dashboard "root" [ grid ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.GridFieldUngrounded("grid", "headcount", deptSchema))
                      "GridFieldUngrounded surfaced for the absent column field, carrying the schema's column set"

                  // The column that IS in the schema raises nothing — the rule
                  // reports per offending name, not per grid.
                  Expect.isFalse
                      (defects
                       |> List.exists (fun d -> d = PreEmitDefect.GridFieldUngrounded("grid", "dept", deptSchema)))
                      "the grounded column must not be reported"

                  let code, severity, _ =
                      PreEmitValidate.describe (PreEmitDefect.GridFieldUngrounded("grid", "headcount", deptSchema))

                  Expect.equal code "FUARAN114" "the stable code"
                  Expect.equal severity DefectSeverity.Error "FUARAN114 is an error, not an advisory"
              | Ok() -> failtest "Expected FUARAN114 defect, got Ok"
          }

          test "FUARAN114: a rowKeyField absent from the source's schema is an error" {
              let grid =
                  gridNamingFields
                      [ "dept" ]
                      (Some "id")
                      (Binding.Transform(TransformSource.Data(embeddedSource), [], None))

              match PreEmitValidate.validate (dashboard "root" [ grid ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.GridFieldUngrounded("grid", "id", deptSchema))
                      "GridFieldUngrounded surfaced for the absent rowKeyField"
              | Ok() -> failtest "Expected FUARAN114 defect for the rowKeyField, got Ok"
          }

          test "FUARAN114 does not fire when every named field is in the schema" {
              let grid =
                  gridNamingFields
                      [ "dept" ]
                      (Some "dept")
                      (Binding.Transform(TransformSource.Data(embeddedSource), [], None))

              match PreEmitValidate.validate (dashboard "root" [ grid ]) with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a fully grounded grid, got: %A" defects
          }

          test "FUARAN114 stands down where the output schema is not derivable" {
              // A non-empty pipeline changes the column set — derive adds,
              // project/groupBy remove — so a name absent from the SOURCE schema
              // may be perfectly correct against the pipeline's output. Refusing
              // it here would be a guess, and an error that is occasionally wrong
              // gets suppressed.
              let pipelined =
                  gridNamingFields
                      [ "headcount" ]
                      (Some "id")
                      (Binding.Transform(
                          TransformSource.Data(embeddedSource),
                          paramPipeline,
                          Some
                              [ { From = Binding.Filter("dept", None)
                                  Name = "dept" } ]
                      ))

              match PreEmitValidate.validate (dashboard "root" [ declarativeChip "dept"; pipelined ]) with
              | Ok() -> ()
              | Error defects ->
                  Expect.isFalse
                      (defects
                       |> List.exists (fun d ->
                           match d with
                           | PreEmitDefect.GridFieldUngrounded _ -> true
                           | _ -> false))
                      (sprintf "FUARAN114 must not fire over a non-empty pipeline, got: %A" defects)

              // Every other source shape is unknowable before the tree runs.
              for source in
                  [ Binding.Static(Some Seq.empty)
                    Binding.State("grid-rows", None)
                    Binding.Query("rows", (fun (raw: obj) -> unbox raw), None) ] do
                  let grid = gridNamingFields [ "headcount" ] (Some "id") source

                  match PreEmitValidate.validate (dashboard "root" [ grid ]) with
                  | Ok() -> ()
                  | Error defects ->
                      Expect.isFalse
                          (defects
                           |> List.exists (fun d ->
                               match d with
                               | PreEmitDefect.GridFieldUngrounded _ -> true
                               | _ -> false))
                          (sprintf "FUARAN114 must not fire over %A, got: %A" source defects)
          } ]
