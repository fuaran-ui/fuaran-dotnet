module Fuaran.UI.Tests.PreEmitValidate

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

// ============================================================================
//  Tests for the pre-emit tree-invariant walker.
//
//  Per the §4g op vocabulary's NodeId-uniqueness contract + the wire
//  shape's empty-string-rejection, `Fuaran.UI.PreEmitValidate.validate`
//  is the canonical pre-submission gate. These tests exercise the
//  defect classes the walker can report.
//
//  Every tree below is a NEGATIVE fixture: the malformed tabs shape IS the
//  test input, constructed so `validate` can be asserted to report it. The
//  build-time validator sees the same defect at the same source and is
//  correct to — so the two tab-shape codes are suppressed for this file.
//  Narrowing this to the individual call sites would need a pragma per
//  fixture and would rot as tests are added; the whole file is fixtures.
// fuaran-validator: disable FUARAN047, FUARAN048 — negative-test fixtures
// ============================================================================

type private Msg = NoOp

let private dashboard id children : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

let private markdown id text : Node<Msg> = Fuaran.markdown id text

[<Tests>]
let tests =
    testList
        "Fuaran.UI.PreEmitValidate"
        [ test "validate passes a clean tree with unique non-empty NodeIds" {
              let tree =
                  dashboard "root" [ markdown "summary" "Hello"; markdown "details" "Body" ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok, got defects: %A" defects
          }

          test "validate flags duplicate NodeIds with the offending id + count" {
              let tree =
                  dashboard
                      "root"
                      [ markdown "duplicate" "one"
                        markdown "duplicate" "two"
                        markdown "duplicate" "three" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let dup =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.DuplicateNodeId(id, count) -> Some(id, count)
                          | _ -> None)

                  Expect.equal dup (Some("duplicate", 3)) "DuplicateNodeId reports id and count"
              | Ok() -> failtest "Expected Error, got Ok"
          }

          test "validate flags empty NodeId strings" {
              let tree = dashboard "" [ markdown "child" "ok" ]

              match PreEmitValidate.validate tree with
              | Error defects -> Expect.contains defects PreEmitDefect.EmptyNodeId "EmptyNodeId reported"
              | Ok() -> failtest "Expected Error, got Ok"
          }

          test "validate collects all defects in one pass (no short-circuit)" {
              let tree =
                  dashboard "" [ markdown "dup" "First duplicate"; markdown "dup" "Second duplicate" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains defects PreEmitDefect.EmptyNodeId "EmptyNodeId surfaced"

                  Expect.isTrue
                      (defects
                       |> List.exists (function
                           | PreEmitDefect.DuplicateNodeId("dup", _) -> true
                           | _ -> false))
                      "DuplicateNodeId surfaced"
              | Ok() -> failtest "Expected Error with multiple defects, got Ok"
          }

          // ──────────────────────────────────────────────────────────────
          // Tab-shape invariants: header / tag length match children;
          // ActiveTag presence requires TabTags presence.

          test "validate flags FUARAN047 when TabHeaders.Length ≠ Children.Length" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabHeaders =
                              Some
                                  [ { Defaults.tabHeader with
                                        Label = TextSource.Literal "Overview" } ] }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let header =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.TabHeaderCountMismatch(id, hc, cc) -> Some(id, hc, cc)
                          | _ -> None)

                  Expect.equal
                      header
                      (Some("results-tabs", 1, 2))
                      "TabHeaderCountMismatch reports nodeId + 1 header + 2 children"
              | Ok() -> failtest "Expected FUARAN047 defect, got Ok"
          }

          test "validate flags FUARAN048 when TabTags.Length ≠ Children.Length" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabTags = Some [ "overview" ] }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let tag =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.TabTagCountMismatch(id, tc, cc) -> Some(id, tc, cc)
                          | _ -> None)

                  Expect.equal
                      tag
                      (Some("results-tabs", 1, 2))
                      "TabTagCountMismatch reports nodeId + 1 tag + 2 children"
              | Ok() -> failtest "Expected FUARAN048 defect, got Ok"
          }

          test "validate flags FUARAN049 when ActiveTag is Some but TabTags is None" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview" ]
                          ActiveTag = Some(Binding.Static(Some "overview")) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.TabActiveTagWithoutTags "results-tabs")
                      "TabActiveTagWithoutTags surfaced for results-tabs"
              | Ok() -> failtest "Expected FUARAN049 defect, got Ok"
          }

          test "validate passes a TabsSpec where TabHeaders + TabTags + ActiveTag align" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabHeaders =
                              Some
                                  [ { Defaults.tabHeader with
                                        Label = TextSource.Literal "Overview" }
                                    { Defaults.tabHeader with
                                        Label = TextSource.Literal "Detail" } ]
                          TabTags = Some [ "overview"; "detail" ]
                          // State-bound so the tag write-back default keeps the
                          // handler-free tabs live (FUARAN069 stays silent).
                          ActiveTag = Some(Binding.State("activeTab", Some "overview")) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for aligned Tabs spec, got: %A" defects
          }

          // ── FUARAN069 — inert-control check (Phase 426 write-back default) ──

          test "validate flags FUARAN069 for a handler-free form field over a non-writable binding" {
              let field: FormField<Msg> =
                  { Defaults.formField<Msg> with
                      Id = "inert-name"
                      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), None) }

              let formNode =
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          Fields = [ field ] }

              let tree = dashboard "root" [ formNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.InertControl("frm", "FormField(inert-name)"))
                      "InertControl surfaced for the static-bound handler-free field"
              | Ok() -> failtest "Expected FUARAN069 defect, got Ok"
          }

          test "validate passes a handler-free form field whose value is State-bound (write-back target)" {
              let field: FormField<Msg> =
                  { Defaults.formField<Msg> with
                      Id = "profile-name"
                      Kind = FormFieldKind.Text(Some(Binding.State("profileName", Some "")), None) }

              let formNode =
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          Fields = [ field ] }

              let tree = dashboard "root" [ formNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a State-bound handler-free field, got: %A" defects
          }

          test "validate flags FUARAN069 for a dismissable modal with no OnDismiss and a static Open" {
              let modalNode =
                  Fuaran.modal
                      "confirm"
                      { Defaults.modal<Msg> with
                          Children = [ markdown "body" "Sure?" ] }

              let tree = dashboard "root" [ modalNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.InertControl("confirm", "Modal"))
                      "InertControl surfaced for the undismissable-in-practice modal"
              | Ok() -> failtest "Expected FUARAN069 defect, got Ok"
          }

          test "validate flags FUARAN082 for a Switch with duplicate match values" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          StateKey = "view"
                          Cases = [ "a", markdown "c1" "one"; "a", markdown "c2" "two" ]
                          Default = markdown "def" "none" }

              let tree = dashboard "root" [ switchNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DuplicateSwitchMatch("sw", "a"))
                      "DuplicateSwitchMatch surfaced for the repeated match value"
              | Ok() -> failtest "Expected FUARAN082 defect, got Ok"
          }

          test "validate flags FUARAN083 for a Switch with an empty stateKey" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          Cases = [ "details", markdown "c1" "one" ]
                          Default = markdown "def" "none" }

              let tree = dashboard "root" [ switchNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UngroundedSwitchStateKey "sw")
                      "UngroundedSwitchStateKey surfaced for the empty stateKey"
              | Ok() -> failtest "Expected FUARAN083 defect, got Ok"
          }

          test "validate passes a Switch with distinct match values" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          StateKey = "view"
                          Cases = [ "details", markdown "c1" "one"; "summary", markdown "c2" "two" ]
                          Default = markdown "def" "none" }

              let tree = dashboard "root" [ switchNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for distinct-match Switch, got: %A" defects
          }

          test "validate passes handler-free tabs whose ActiveIndex is State-bound" {
              let tabsNode =
                  Fuaran.tabs
                      "panes"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview" ]
                          ActiveIndex = Binding.State("activePane", Some 0) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for State-bound handler-free tabs, got: %A" defects
          }

          // ── FUARAN070 / FUARAN071 — Selection edge checks (Phase 427) ──

          test "validate flags FUARAN070 for a Binding.Selection naming a NodeId absent from the tree" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ detail ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("detail", "no-such-grid"))
                      "DanglingSelection surfaced with reader + missing target"
              | Ok() -> failtest "Expected FUARAN070 defect, got Ok"
          }

          test "validate flags FUARAN071 for a Binding.Selection over a non-producer node" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("summary", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ markdown "summary" "Not a grid"; detail ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.SelectionOverNonProducer("detail", "summary"))
                      "SelectionOverNonProducer surfaced with reader + non-producer target"
              | Ok() -> failtest "Expected FUARAN071 defect, got Ok"
          }

          // ── FUARAN072 / FUARAN073 — Call result-target checks (Phase 428) ──

          test "validate flags FUARAN072 for a Call into a Query no reader binds (orphan fetch)" {
              let fetchButton =
                  Fuaran.button
                      "fetch"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Fetch"
                          OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders")) }

              let tree = dashboard "root" [ fetchButton ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.OrphanQueryFetch("fetch", "orders"))
                      "OrphanQueryFetch surfaced for the readerless query target"
              | Ok() -> failtest "Expected FUARAN072 defect, got Ok"
          }

          test "validate passes a Call into a Query a reader binds" {
              let fetchButton =
                  Fuaran.button
                      "fetch"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Fetch"
                          OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders")) }

              let reader =
                  Fuaran.metric
                      "orders-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Orders"
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), None) }

              let tree = dashboard "root" [ fetchButton; reader ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a read query target, got: %A" defects
          }

          test "validate flags FUARAN073 for a Call with neither onResult nor into (dropped result)" {
              let fireButton =
                  Fuaran.button
                      "fire"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Ping"
                          OnClick = Action.Call("/api/ping", None, None) }

              let tree = dashboard "root" [ fireButton ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.CallResultDropped("fire", "/api/ping"))
                      "CallResultDropped surfaced for the fire-and-forget call"
              | Ok() -> failtest "Expected FUARAN073 defect, got Ok"
          }

          test "validate passes a master-detail pair (Selection over a DataGrid node)" {
              let grid: Node<Msg> =
                  { Id = NodeId "orders-grid"
                    Kind =
                      NodeKind.DataGrid(
                          { Source = Binding.Static(Some Seq.empty)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = []
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
                      )
                    State = Defaults.stateBehaviour<Msg>
                    Style = Defaults.style
                    Accessibility = None
                    Motion = Defaults.Motion.none
                    ExtraAttributes = None }

              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("orders-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ grid; detail ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a valid master-detail pair, got: %A" defects
          }

          // ── Phase 640 — schema-grounded chart validation (FUARAN086–089) ──

          test "a chart field absent from an embedded table's schema fires ChartFieldUngrounded" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "quarter", Fuaran.Core.ColumnType.StringType
                        "revenue", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let chart: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source = Binding.Transform(Fuaran.Core.DataSource.Embedded table, [], None)
                          XField = "quarter"
                          YFields = [ "revenu" ] } // typo — absent from the schema

              match PreEmitValidate.validate chart with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldUngrounded("cht", "revenu"))
                      "the typo'd y-field is ungrounded"
              | Ok() -> failtest "Expected ChartFieldUngrounded"
          }

          test "a grounded chart over an embedded table passes; a non-empty pipeline is unknowable and passes" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "quarter", Fuaran.Core.ColumnType.StringType
                        "revenue", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let grounded: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source = Binding.Transform(Fuaran.Core.DataSource.Embedded table, [], None)
                          XField = "quarter"
                          YFields = [ "revenue" ] }

              match PreEmitValidate.validate grounded with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a grounded chart, got: %A" defects

              // The same typo'd field behind a Derive pipeline: the output
              // schema is not statically derivable, so grounding must NOT fire.
              let piped: Node<Msg> =
                  Fuaran.chart
                      "cht2"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source =
                              Binding.Transform(
                                  Fuaran.Core.DataSource.Embedded table,
                                  [ Fuaran.Core.Transform.Derive("variance", Fuaran.Core.ColExpr.Col "revenue") ],
                                  None
                              )
                          XField = "quarter"
                          YFields = [ "variance" ] }

              match PreEmitValidate.validate piped with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a piped chart (unknowable schema), got: %A" defects
          }

          test "a string-typed y-field and a string-typed scatter x fire ChartFieldTypeMismatch" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "name", Fuaran.Core.ColumnType.StringType
                        "score", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let badY: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source = Binding.Transform(Fuaran.Core.DataSource.Embedded table, [], None)
                          XField = "name"
                          YFields = [ "name" ] } // string column as a value series

              match PreEmitValidate.validate badY with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldTypeMismatch("cht", "name", "string"))
                      "string y-field flagged"
              | Ok() -> failtest "Expected ChartFieldTypeMismatch for the y-field"

              let badScatterX: Node<Msg> =
                  Fuaran.chart
                      "cht2"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Scatter
                          Source = Binding.Transform(Fuaran.Core.DataSource.Embedded table, [], None)
                          XField = "name" // scatter x must be numeric
                          YFields = [ "score" ] }

              match PreEmitValidate.validate badScatterX with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldTypeMismatch("cht2", "name", "string"))
                      "string scatter x flagged"
              | Ok() -> failtest "Expected ChartFieldTypeMismatch for the scatter x"
          }

          test "a multi-series pie fires ChartPieSeriesShape; stacked Line fires ChartStackedMeaningless" {
              let pie: Node<Msg> =
                  Fuaran.chart
                      "pie"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Pie
                          YFields = [ "a"; "b" ] }

              match PreEmitValidate.validate pie with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.ChartPieSeriesShape("pie", 2)) "two-series pie flagged"
              | Ok() -> failtest "Expected ChartPieSeriesShape"

              let stackedLine: Node<Msg> =
                  Fuaran.chart
                      "ln"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          YFields = [ "a" ]
                          Stacked = true }

              match PreEmitValidate.validate stackedLine with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartStackedMeaningless("ln", "Line"))
                      "stacked Line flagged as dead intent"
              | Ok() -> failtest "Expected ChartStackedMeaningless"
          } ]
