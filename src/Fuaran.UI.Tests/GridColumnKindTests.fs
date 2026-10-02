module Fuaran.UI.Tests.GridColumnKind

// ============================================================================
//  Phase 1909 — grid column rules follow the cell kind.
//
//  An ACTION column (cell kind `Button` / `ButtonGroup`) draws its own label and
//  hands the whole row to its handler, so it never displays its column's
//  `field`. The rules used to ignore that: FUARAN077 warned that a field-less
//  Button column "renders blank" (false), emitters invented a field to satisfy
//  it, and FUARAN114 refused the invented name with a message that was also
//  false. The operator ruling (2026-09-28) is that an action column carries no
//  `field` at all, and a declared one is a warning (FUARAN163), not a sort key.
//
//  The opposite gap: a `TonedPill` cell's OWN field IS a column reference (the
//  pill's label and tone key), and no rule grounded it. It is now FUARAN114's
//  sub-case, over the same `SchemaWalk` window: a closed walk refuses, an open
//  one stands down and the reader grades unchecked.
//
//  Three layers are pinned here: the validator (constructed trees), the
//  renderer's shared sort / sortable decisions (`GridColumn.dataField`,
//  `BindingResolver.sortRowsByDescriptor`, `AgGridPlan.columnSortable`, and a
//  source read of the first-party leg's header and both export legs, which
//  only run under Fable), and the three corpus fixtures every host certifies.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

// ── Construction ────────────────────────────────────────────────────────────

let private source (dataSource: Fuaran.Core.DataSource) : Binding<Row seq> =
    Binding.Transform(
        TransformSource.Data dataSource,
        [ Fuaran.Compute.GroupBy(
              [ "dept" ],
              [ ({ Name = "total"
                   Fn = Fuaran.Core.Sum
                   Of = "amount" }
                : Fuaran.Compute.Agg) ]
          ) ],
        None
    )

/// `groupBy dept, sum(amount) as total` over an embedded table — a CLOSED walk
/// producing exactly `dept, total`.
let private closedSource: Binding<Row seq> =
    source (
        Fuaran.Core.Embedded
            { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "dept"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
                  Fuaran.Core.Column.create "amount" Fuaran.Core.IntType [ Fuaran.Core.Int 1; Fuaran.Core.Int 2 ] ] }
    )

/// The same pipeline over a host-resolved `Ref` with no declared schema — an
/// OPEN walk, over which no negative verdict is sound.
let private openSource: Binding<Row seq> =
    Binding.Transform(
        TransformSource.Data(Fuaran.Core.Ref "spend"),
        [ Fuaran.Compute.Filter(
              Fuaran.Compute.Binary(
                  Fuaran.Compute.Gt,
                  Fuaran.Compute.Col "amount",
                  Fuaran.Compute.ColExpr.Lit(Fuaran.Core.Int 0)
              )
          ) ],
        None
    )

let private column (label: string) (field: string option) (kind: CellKindErased<obj>) : ColumnErased<obj> =
    { Label = label
      Value = None
      Field = field
      Sortable = None
      Editable = None
      Format = CellFormat.None
      Kind = kind
      Width = ColumnWidth.Auto }

let private button: CellKindErased<obj> =
    CellKindErased.Button(TextSource.Literal "Open", None)

let private buttonGroup: CellKindErased<obj> =
    CellKindErased.ButtonGroup
        [ { Label = TextSource.Literal "Approve"
            OnClick = None } ]

let private pill (field: string) : CellKindErased<obj> =
    CellKindErased.TonedPill(field, Map [ "eng", ToneVariant.Info ], ToneVariant.Default)

let private gridSpec (src: Binding<Row seq>) (columns: ColumnErased<obj> list) : GridSpec<obj> =
    { SortStateKey = Some "sort"
      PageSize = None
      PageStateKey = None
      EditStateKey = None
      DefaultSort = None
      Source = src
      RowKey = None
      RowKeyField = Some "dept"
      Columns = columns
      OnRowClick = None
      Editable = false
      Reorderable = false
      TransferInKey = None
      TransferOutKey = None
      StaticRows = None
      KeepRowsTogether = false
      RepeatHeader = false
      Exportable = false
      WindowStateKey = None
      RowTotal = None }

let private grid (src: Binding<Row seq>) (columns: ColumnErased<obj> list) : Node<obj> =
    { Id = "g"
      Kind = NodeKind.DataGrid(gridSpec src columns)
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      Fallback = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

let private defects (tree: Node<obj>) : PreEmitDefect list =
    match validate tree with
    | Ok() -> []
    | Error ds -> ds

let private codes (tree: Node<obj>) : string list =
    defects tree |> List.map (fun d -> let c, _, _ = describe d in c)

// ── The validator ───────────────────────────────────────────────────────────

let private validatorTests =
    testList
        "validator"
        [ test "a field-less Button column raises nothing" {
              let tree =
                  grid closedSource [ column "Dept" (Some "dept") CellKindErased.Text; column "Open" None button ]

              Expect.isEmpty (codes tree) "an action column is not blank for having no field"
          }

          test "a field-less ButtonGroup column raises nothing" {
              let tree =
                  grid
                      closedSource
                      [ column "Dept" (Some "dept") CellKindErased.Text
                        column "Review" None buttonGroup ]

              Expect.isEmpty (codes tree) "the rule reads both action kinds"
          }

          test "a field-less Text column still raises FUARAN077" {
              let tree =
                  grid
                      closedSource
                      [ column "Dept" (Some "dept") CellKindErased.Text
                        column "Blank" None CellKindErased.Text ]

              Expect.equal (codes tree) [ "FUARAN077" ] "the exemption is for action columns only"
          }

          test "a Button column WITH a field raises only the FUARAN163 'drop it' warning — not FUARAN114" {
              // `action` is absent from the closed walk: before this phase that
              // was a FUARAN114 Error. It is now read by nothing, so the one
              // finding is the warning, and it names the column and the field.
              let tree =
                  grid
                      closedSource
                      [ column "Dept" (Some "dept") CellKindErased.Text
                        column "Actions" (Some "action") button ]

              Expect.equal
                  (defects tree)
                  [ PreEmitDefect.ActionColumnField("g", "Actions", "action") ]
                  "the only finding"

              let code, severity, message =
                  describe (PreEmitDefect.ActionColumnField("g", "Actions", "action"))

              Expect.equal code "FUARAN163" "the minted code"
              Expect.equal severity DefectSeverity.Warning "a warning: the field does nothing, it breaks nothing"
              Expect.stringContains message "drop the field" "the repair is stated"
          }

          test "a ButtonGroup column with a GROUNDED field still raises FUARAN163" {
              // Grounded or not, the field does nothing on an action column.
              let tree = grid closedSource [ column "Review" (Some "dept") buttonGroup ]

              Expect.equal
                  (codes tree)
                  [ "FUARAN163" ]
                  "the warning is about the field's uselessness, not its grounding"
          }

          test "an action column's field is not grounded under an open walk either (no FUARAN114, only FUARAN163)" {
              let tree = grid openSource [ column "Actions" (Some "action") button ]
              Expect.equal (codes tree) [ "FUARAN163" ] "the warning does not depend on the source"
          }

          test
              "a Text column naming an unproduced field is still FUARAN114, and the message no longer promises a blank cell unconditionally" {
              let tree = grid closedSource [ column "Amount" (Some "amount") CellKindErased.Text ]
              Expect.equal (codes tree) [ "FUARAN114" ] "the grounding rule is unchanged for a data column"

              let _, _, message =
                  describe (PreEmitDefect.GridFieldUngrounded("g", "amount", [ "dept"; "total" ]))

              Expect.isFalse (message.Contains "so the cell renders blank") "the unconditional claim is gone"
              Expect.stringContains message "sort and export" "it names every reader of the field"
          }

          test
              "a TonedPill naming an absent column under a CLOSED walk is refused — FUARAN114's sub-case, naming the cell" {
              let tree = grid closedSource [ column "Status" (Some "dept") (pill "status") ]

              Expect.equal
                  (defects tree)
                  [ PreEmitDefect.PillFieldUngrounded("g", "Status", "status", [ "dept"; "total" ]) ]
                  "the pill's own field is grounded, and the column is named"

              let code, severity, message =
                  describe (PreEmitDefect.PillFieldUngrounded("g", "Status", "status", [ "dept"; "total" ]))

              Expect.equal code "FUARAN114" "a sub-case: the repair is FUARAN114's"
              Expect.equal severity DefectSeverity.Error "an error, like the rule it belongs to"
              Expect.stringContains message "TonedPill" "the message names the cell"
              Expect.stringContains message "default tone" "and says what the reader actually sees"
          }

          test "a TonedPill naming a PRODUCED column is clean (the probe in the other direction)" {
              let tree = grid closedSource [ column "Status" (Some "dept") (pill "dept") ]
              Expect.isEmpty (codes tree) "the rename clears it"
          }

          test "a TonedPill under an OPEN walk stands down, and the reader grades unchecked" {
              let tree = grid openSource [ column "Status" (Some "status") (pill "status") ]
              Expect.isEmpty (codes tree) "an open walk supports no negative verdict"

              match bindingChecks tree with
              | [ check ] ->
                  match check.Grade with
                  | BindingGrade.Unchecked(UncheckedReason.OpenSchema _) -> ()
                  | other -> failtestf "expected an OpenSchema grade, got %A" other
              | other -> failtestf "expected one reader, got %A" other
          }

          test "the binding-check report locates the pill finding at the cell's own field" {
              let tree =
                  grid
                      closedSource
                      [ column "Actions" (Some "status") button
                        column "Status" (Some "dept") (pill "status") ]

              let check = bindingChecks tree |> List.exactlyOne
              Expect.equal check.Grade BindingGrade.Checked "judged"

              Expect.equal
                  (check.Diagnostics |> List.map (fun d -> d.Code, d.Path))
                  [ "FUARAN114", "$.kind.columns[1].kind.field" ]
                  "at the pill's field — the action column's same-named field is no slot, and FUARAN163 is not a grounding finding"
          }

          test "a Text column's finding is not located at an earlier action column naming the same field" {
              let tree =
                  grid
                      closedSource
                      [ column "Actions" (Some "amount") button
                        column "Amount" (Some "amount") CellKindErased.Text ]

              let check = bindingChecks tree |> List.exactlyOne

              Expect.equal
                  (check.Diagnostics |> List.map (fun d -> d.Code, d.Path))
                  [ "FUARAN114", "$.kind.columns[1].field" ]
                  "the slot is the data column's"
          } ]

// ── The renderer's shared decisions ─────────────────────────────────────────

let private rowOf (pairs: (string * obj) list) : Row = Map.ofList pairs

let private rendererTests =
    testList
        "renderer"
        [ test "GridColumn.dataField is None on both action kinds, whatever they declare" {
              Expect.isNone (GridColumn.dataField (column "A" (Some "x") button)) "Button"
              Expect.isNone (GridColumn.dataField (column "A" (Some "x") buttonGroup)) "ButtonGroup"
              Expect.equal (GridColumn.dataField (column "A" (Some "x") CellKindErased.Text)) (Some "x") "Text keeps it"
              Expect.equal (GridColumn.dataField (column "A" (Some "x") (pill "y"))) (Some "x") "TonedPill keeps it"
          }

          test "AgGridPlan.columnSortable offers no sort on an action column that declares a field" {
              let spec =
                  gridSpec
                      closedSource
                      [ column "Dept" (Some "dept") CellKindErased.Text
                        column "Actions" (Some "dept") button
                        column "Review" (Some "dept") buttonGroup ]

              Expect.equal
                  (Fuaran.UI.Renderer.AgGridPlan.columnSortable spec)
                  [ true; false; false ]
                  "the data column sorts; neither action column does"
          }

          test "a sort descriptor naming an action column leaves the authored order standing" {
              let rows = [ rowOf [ "dept", ("sales" :> obj) ]; rowOf [ "dept", ("eng" :> obj) ] ]

              let columns =
                  [ column "Dept" (Some "dept") CellKindErased.Text
                    column "Actions" (Some "dept") button ]

              let byAction =
                  Fuaran.UI.Renderer.BindingResolver.sortRowsByDescriptor columns (Some(1, SortDirection.Asc)) rows

              Expect.equal byAction rows "sort ignores an action column's field"

              let byData =
                  Fuaran.UI.Renderer.BindingResolver.sortRowsByDescriptor columns (Some(0, SortDirection.Asc)) rows

              Expect.equal byData (List.rev rows) "and the probe runs the other way: the data column does sort"
          }

          test "the first-party header and both export legs read the column through GridColumn (source read)" {
              // The React legs run only under Fable, so the decision they make is
              // pinned on the source: the sortable header matches on
              // `GridColumn.dataField`, and each export cuts action columns.
              let path =
                  Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "Render.fs")

              let src = File.ReadAllText path

              Expect.stringContains
                  src
                  "match spec.SortStateKey, Fuaran.UI.GridColumn.dataField col, col.Sortable with"
                  "the header's sortable decision"

              let cut =
                  "let exported = spec.Columns |> List.filter (Fuaran.UI.GridColumn.isAction >> not)"

              let occurrences = src.Split(cut).Length - 1
              Expect.equal occurrences 2 "the first-party AND the AG-Grid export legs"
          } ]

// ── The corpus ──────────────────────────────────────────────────────────────

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

let private decode (dir: string) (stem: string) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText(Path.Combine(dir, stem + ".json"))) with
    | Ok node -> node
    | Error e -> failtestf "%s failed to decode: %s at %s" stem e.Code e.Path

let private withCorpus (f: string -> unit) =
    match nodesDir () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some d -> f d

let private corpusTests =
    testList
        "corpus"
        [ test "grid-action-column-clean: field-less action columns and a grounded pill, clean and checked" {
              withCorpus (fun d ->
                  let tree = decode d "grid-action-column-clean"
                  Expect.isEmpty (codes tree) "no FUARAN077 on a field-less action column"

                  let check = bindingChecks tree |> List.exactlyOne
                  Expect.equal check.Grade BindingGrade.Checked "the reader is judged"
                  Expect.isEmpty check.Diagnostics "and nothing is found")
          }

          test "grid-action-column-field: FUARAN163 is the only finding" {
              withCorpus (fun d ->
                  let tree = decode d "grid-action-column-field"

                  Expect.equal
                      (defects tree)
                      [ PreEmitDefect.ActionColumnField("grid-action-column-field", "Actions", "action") ]
                      "the invented field earns the warning, and no FUARAN114"

                  Expect.isEmpty
                      (bindingChecks tree |> List.collect _.Diagnostics)
                      "the report carries no grounding finding for it")
          }

          test "grid-toned-pill-ungrounded: FUARAN114's pill sub-case, at the cell's field" {
              withCorpus (fun d ->
                  let tree = decode d "grid-toned-pill-ungrounded"
                  Expect.equal (codes tree) [ "FUARAN114" ] "exactly one finding"

                  let check = bindingChecks tree |> List.exactlyOne

                  match check.Diagnostics with
                  | [ diagnostic ] ->
                      Expect.equal diagnostic.Path "$.kind.columns[1].kind.field" "located at the pill's own field"

                      match diagnostic.Defect with
                      | PreEmitDefect.PillFieldUngrounded(_, label, field, cols) ->
                          Expect.equal
                              (label, field, cols)
                              ("Status", "status", [ "dept"; "total" ])
                              "the cell, the name, the produced set"
                      | other -> failtestf "expected PillFieldUngrounded, got %A" other
                  | other -> failtestf "expected one diagnostic, got %A" other)
          }

          test "the probe runs the other way: repairing either negative clears it" {
              withCorpus (fun d ->
                  let repaired (stem: string) (from: string) (into: string) =
                      match
                          Fuaran.UI.Ops.JsonDecode.decodeNodeObj (
                              File.ReadAllText(Path.Combine(d, stem + ".json")).Replace(from, into)
                          )
                      with
                      | Ok tree -> tree
                      | Error e -> failtestf "%s repaired failed to decode: %s" stem e.Code

                  Expect.isEmpty
                      (codes (repaired "grid-action-column-field" "\"field\":\"action\"," ""))
                      "dropping the field clears FUARAN163"

                  Expect.isEmpty
                      (codes (repaired "grid-toned-pill-ungrounded" "\"field\":\"status\"" "\"field\":\"dept\""))
                      "naming a produced column clears the pill finding")
          } ]

[<Tests>]
let tests =
    testList "Phase 1909 — grid column rules follow the cell kind" [ validatorTests; rendererTests; corpusTests ]
