module Fuaran.UI.Tests.OpsApply

// ============================================================================
//  Tree-op apply engine acceptance set.
//
//  Each test names the op shape it exercises (happy-path + a negative form
//  that proves the §4d error envelope is correctly populated). The fixture
//  is a small dashboard with two children — a Metric and a Grid — so structural
//  ops have somewhere to insert / remove / reorder, and FieldNotFound has a
//  second node whose kind carries the wrongly-addressed field for the
//  `nodes_with_<field>_field` hint.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

type Msg =
    | SelectRow of int
    | DismissBanner

type Row = { RowId: int; Channel: string }

let private idOf (NodeId rawId) = rawId

// F# 10 nullness types `box _` as `obj | null`. `PropValue.Native` and `Binding<obj>`
// payloads here are always non-null (we control the test input), so wrap the
// boxing in a small helper that asserts non-null via `Unchecked.nonNull`.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private revenueMetric: Node<Msg> =
    Fuaran.metric
        "revenue-metric"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = binding.query "totalRevenue" (fun (r: {| amount: float |}) -> r.amount)
            Format = format.currency "GBP"
            Tone = ToneVariant.Brand }

let private channelGrid: Node<Msg> =
    Fuaran.grid
        "channel-grid"
        { Defaults.grid<Row, Msg> with
            Source = binding.query "channelRows" id
            RowKey = (fun r -> string r.RowId)
            Columns = [ Column.text "Channel" (fun (r: Row) -> r.Channel) ]
            OnRowClick = Some(fun r -> Action.dispatch (SelectRow r.RowId)) }

let private dashboard: Node<Msg> =
    Fuaran.dashboard
        "channel-analysis"
        { Defaults.dashboard<Msg> with
            Children = [ revenueMetric; channelGrid ] }

// ─── Nested-path fixtures (Phase 364) ──────────────────────────────────────

let private analysisTabs: Node<Msg> =
    Fuaran.tabs
        "analysis-tabs"
        { Defaults.tabs with
            Children =
                [ Fuaran.markdown "tab-a" "Overview body"
                  Fuaran.markdown "tab-b" "Detail body" ]
            TabHeaders =
                Some
                    [ { Defaults.tabHeader with
                          Label = TextSource.Literal "Overview" }
                      { Defaults.tabHeader with
                          Label = TextSource.Literal "Detail" } ] }

let private signupForm: Node<Msg> =
    Fuaran.form
        "signup-form"
        { Defaults.form with
            Fields =
                [ { Defaults.formField with
                      Id = "name"
                      Label = TextSource.Literal "Name" }
                  { Defaults.formField with
                      Id = "age"
                      Label = TextSource.Literal "Age" } ] }

let private mixChart: Node<Msg> =
    Fuaran.chart
        "mix-chart"
        { Defaults.chart with
            XField = "month"
            YFields = [ "revenue"; "cost" ] }

let private childIdOf (root: Node<Msg>) (index: int) : string =
    match root.Kind with
    | NodeKind.Box(spec) -> idOf spec.Children[index].Id
    | other -> failtestf "Expected Box root, got %A" other

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops apply engine"
        [
          // ─── Happy-path structural ops ─────────────────────────────────────

          test "InsertChild appends a Markdown placeholder at the end of the dashboard" {
              let newNode = Fuaran.markdown "footnote" "Updated hourly."

              match Apply.apply (TreeOp.InsertChild(NodeId "channel-analysis", newNode)) dashboard with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Box(spec) ->
                      Expect.equal spec.Children.Length 3 "Three children after insert"
                      Expect.equal (idOf spec.Children[2].Id) "footnote" "Footnote landed at position 2"
                  | other -> failtestf "Expected Dashboard, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "RemoveNode drops the addressed node from its parent's children" {
              match Apply.apply (TreeOp.RemoveNode(NodeId "channel-grid")) dashboard with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Box(spec) ->
                      Expect.equal spec.Children.Length 1 "One child after remove"
                      Expect.equal (idOf spec.Children[0].Id) "revenue-metric" "Survivor is the Metric"
                  | other -> failtestf "Expected Dashboard, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "ReorderChildren permutes the dashboard's child list" {
              let newOrder = [ NodeId "channel-grid"; NodeId "revenue-metric" ]

              match Apply.apply (TreeOp.ReorderChildren(NodeId "channel-analysis", newOrder)) dashboard with
              | Ok updated ->
                  Expect.equal (childIdOf updated 0) "channel-grid" "Grid first"
                  Expect.equal (childIdOf updated 1) "revenue-metric" "Metric second"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "MoveNode appends — membership only" {
              // A same-parent move re-appends the target; it no longer carries a
              // position, so "move to head" is the two-op form below.
              match Apply.apply (TreeOp.MoveNode(NodeId "revenue-metric", NodeId "channel-analysis")) dashboard with
              | Ok updated ->
                  Expect.equal (childIdOf updated 0) "channel-grid" "the other child shifts up"
                  Expect.equal (childIdOf updated 1) "revenue-metric" "the moved node is appended"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "repositioning is the move plus a reorder — the two-op form" {
              let op =
                  TreeOp.Batch
                      [ TreeOp.MoveNode(NodeId "channel-grid", NodeId "channel-analysis")
                        TreeOp.ReorderChildren(
                            NodeId "channel-analysis",
                            [ NodeId "channel-grid"; NodeId "revenue-metric" ]
                        ) ]

              match Apply.apply op dashboard with
              | Ok updated ->
                  Expect.equal (childIdOf updated 0) "channel-grid" "Grid moved to head by naming the order"
                  Expect.equal (childIdOf updated 1) "revenue-metric" "Metric demoted"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "ReplaceRoot swaps the entire tree — the only op that changes the root" {
              let replacement = Fuaran.markdown "fresh-root" "A wholly new screen."

              match Apply.apply (TreeOp.ReplaceRoot replacement) dashboard with
              | Ok updated ->
                  Expect.equal (idOf updated.Id) "fresh-root" "the root node id is now the replacement's"

                  match updated.Kind with
                  | NodeKind.Markdown(_) -> ()
                  | other -> failtestf "Expected the replacement Markdown root, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          // ─── Style / State / Edit ──────────────────────────────────────────

          test "UpdateStyle replaces the addressed node's Style block" {
              let newStyle: SemanticStyle =
                  { Tone = ToneVariant.Warning
                    Weight = StyleWeight.Spacious
                    Emphasis = Emphasis.Loud
                    Role = StyleRole.None
                    Voice = FontVoice.Default }

              match Apply.apply (TreeOp.UpdateStyle(NodeId "revenue-metric", newStyle)) dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some node ->
                      Expect.equal node.Style.Tone ToneVariant.Warning "Tone updated"
                      Expect.equal node.Style.Emphasis Emphasis.Loud "Emphasis updated"
                  | None -> failtest "Metric vanished"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateState wires an OnEmpty placeholder onto the addressed node" {
              let placeholder = Fuaran.markdown "metric-empty" "Loading revenue..."

              let newState: StateBehaviour<Msg> =
                  { OnLoading = None
                    OnEmpty = Some placeholder
                    OnError = None }

              match Apply.apply (TreeOp.UpdateState(NodeId "revenue-metric", newState)) dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some node ->
                      Expect.isSome node.State.OnEmpty "OnEmpty wired"
                      Expect.isNone node.State.OnLoading "OnLoading still none"
                  | None -> failtest "Metric vanished"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "EditNode swaps the addressed node's Kind wholesale, preserving Id" {
              let newKind = NodeKind.Markdown({ Text = TextSource.Literal "Replaced" })

              match Apply.apply (TreeOp.EditNode(NodeId "revenue-metric", newKind)) dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some node ->
                      Expect.equal (idOf node.Id) "revenue-metric" "Id preserved"

                      match node.Kind with
                      | NodeKind.Markdown(spec) ->
                          match spec.Text with
                          | TextSource.Literal "Replaced" -> ()
                          | other -> failtestf "Expected 'Replaced' literal, got %A" other
                      | other -> failtestf "Expected Markdown, got %A" other
                  | None -> failtest "Node vanished"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          // ─── UpdateProp ────────────────────────────────────────────────────

          test "UpdateProp 'Tone' on the Metric writes the new tone via the typed dispatch" {
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "Tone", PropValue.Native(nn ToneVariant.Critical))

              match Apply.apply op dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some { Kind = NodeKind.Metric(spec) } -> Expect.equal spec.Tone ToneVariant.Critical "Tone updated"
                  | _ -> failtest "Metric not found / not Metric"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Columns' on the Metric returns FieldNotFound with nodes_with_columns_field hint" {
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "Columns", PropValue.Native(nn (List.empty<int>)))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected FieldNotFound error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.FieldNotFound "Code = FieldNotFound"
                  Expect.equal err.Hint.NodeKind (Some "Metric") "Hint.NodeKind = Metric"

                  Expect.contains err.Hint.AvailableFields "Value" "Source is one of Metric's available fields"

                  match err.Hint.NodesWithField with
                  | Some("Columns", ids) ->
                      Expect.contains ids (NodeId "channel-grid") "Grid offered as alternative target"
                  | other -> failtestf "Expected nodes_with_Columns hint, got %A" other
          }

          // ─── UpdateProp — nested paths (Phase 364) ─────────────────────────

          test "UpdateProp 'Columns[0].Label' renames a grid column as one surgical op" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns[0].Label", PropValue.Native(nn "Sales channel"))

              match Apply.apply op dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "channel-grid") updated with
                  | Some { Kind = NodeKind.DataGrid(spec) } ->
                      Expect.equal spec.Columns[0].Label "Sales channel" "Column label renamed"
                  | _ -> failtest "Grid not found / not DataGrid"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Columns[0].Width' writes a typed ColumnWidth" {
              let op =
                  TreeOp.UpdateProp(
                      NodeId "channel-grid",
                      "Columns[0].Width",
                      PropValue.Native(nn (ColumnWidth.Fixed 120))
                  )

              match Apply.apply op dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "channel-grid") updated with
                  | Some { Kind = NodeKind.DataGrid(spec) } ->
                      Expect.equal spec.Columns[0].Width (ColumnWidth.Fixed 120) "Column width updated"
                  | _ -> failtest "Grid not found / not DataGrid"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Columns.0.Label' (dot-numeric segment) returns PathInvalid with the nested patterns" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns.0.Label", PropValue.Native(nn "Renamed"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PathInvalid error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.PathInvalid "Code = PathInvalid"

                  Expect.contains err.Hint.AvailableFields "Columns[i].Label" "hint enumerates the nested patterns"
          }

          test "UpdateProp 'Columns.Label' (list segment without index) returns PathInvalid naming the indexed form" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns.Label", PropValue.Native(nn "Renamed"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PathInvalid error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.PathInvalid "Code = PathInvalid"
                  Expect.contains err.Hint.AvailableFields "Columns[i].Label" "hint enumerates the nested patterns"

                  match err.Hint.Suggestion with
                  | Some s -> Expect.stringContains s "Columns[i]" "suggestion names the indexed form"
                  | None -> failtest "Expected a suggestion"
          }

          test "UpdateProp 'Columns[#col-1].Label' (reserved id-keyed syntax) returns PathInvalid" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns[#col-1].Label", PropValue.Native(nn "Renamed"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PathInvalid error"
              | Error err -> Expect.equal err.Code ApplyErrorCode.PathInvalid "Code = PathInvalid"
          }

          test "UpdateProp 'Columns[5].Label' returns PositionOutOfRange with the valid range" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns[5].Label", PropValue.Native(nn "Renamed"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PositionOutOfRange error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.PositionOutOfRange "Code = PositionOutOfRange"

                  match err.Hint.Suggestion with
                  | Some s -> Expect.stringContains s "0 and 0" "suggestion names the valid range"
                  | None -> failtest "Expected a suggestion"
          }

          test "UpdateProp 'Columns[0].Nope' returns FieldNotFound enumerating the sub-paths at the failing segment" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns[0].Nope", PropValue.Native(nn "x"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected FieldNotFound error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.FieldNotFound "Code = FieldNotFound"

                  Expect.equal
                      err.Hint.AvailableFields
                      [ "Label"; "Format"; "Width" ]
                      "sub-paths at the failing segment"
          }

          test "UpdateProp 'Columns[0].Kind' (closure-bearing leaf) returns PathNotSupportedYet" {
              let op =
                  TreeOp.UpdateProp(NodeId "channel-grid", "Columns[0].Kind", PropValue.Native(nn "Text"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PathNotSupportedYet error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.PathNotSupportedYet "Code = PathNotSupportedYet"

                  Expect.contains err.Hint.AvailableFields "Columns[i].Label" "hint carries the nested patterns"
          }

          test "UpdateProp 'TabHeaders[1].Label' renames the second tab header" {
              let op =
                  TreeOp.UpdateProp(
                      NodeId "analysis-tabs",
                      "TabHeaders[1].Label",
                      PropValue.Native(nn (TextSource.Literal "Breakdown"))
                  )

              match Apply.apply op analysisTabs with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Tabs(spec) ->
                      match spec.TabHeaders with
                      | Some headers ->
                          match headers[1].Label with
                          | TextSource.Literal "Breakdown" -> ()
                          | other -> failtestf "Expected Literal 'Breakdown', got %A" other
                      | None -> failtest "TabHeaders vanished"
                  | other -> failtestf "Expected Tabs, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'TabHeaders[0].Disabled' installs the optional bound disabled-state" {
              let op =
                  TreeOp.UpdateProp(
                      NodeId "analysis-tabs",
                      "TabHeaders[0].Disabled",
                      PropValue.Native(nn (Binding.Static(Some true): Binding<bool>))
                  )

              match Apply.apply op analysisTabs with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Tabs(spec) ->
                      match spec.TabHeaders with
                      | Some [ { Disabled = Some(Binding.Static(Some true)) }; _ ] -> ()
                      | other -> failtestf "Expected first header disabled, got %A" other
                  | other -> failtestf "Expected Tabs, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'TabHeaders[0].Label' on a Tabs without headers returns PositionOutOfRange (empty list)" {
              let bareTabs =
                  Fuaran.tabs
                      "bare-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ Fuaran.markdown "only-tab" "body" ] }

              let op =
                  TreeOp.UpdateProp(
                      NodeId "bare-tabs",
                      "TabHeaders[0].Label",
                      PropValue.Native(nn (TextSource.Literal "X"))
                  )

              match Apply.apply op bareTabs with
              | Ok _ -> failtest "Expected PositionOutOfRange error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.PositionOutOfRange "Code = PositionOutOfRange"
                  Expect.stringContains err.Message "empty" "message names the empty list"
          }

          test "UpdateProp 'Fields[0].Required' flips a form field's required flag" {
              let op =
                  TreeOp.UpdateProp(NodeId "signup-form", "Fields[0].Required", PropValue.Native(nn true))

              match Apply.apply op signupForm with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Form(spec) ->
                      Expect.isTrue spec.Fields[0].Required "First field now required"
                      Expect.isFalse spec.Fields[1].Required "Second field untouched"
                  | other -> failtestf "Expected Form, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Fields[0].Kind' (closure-bearing leaf) returns PathNotSupportedYet" {
              let op =
                  TreeOp.UpdateProp(NodeId "signup-form", "Fields[0].Kind", PropValue.Native(nn "Text"))

              match Apply.apply op signupForm with
              | Ok _ -> failtest "Expected PathNotSupportedYet error"
              | Error err -> Expect.equal err.Code ApplyErrorCode.PathNotSupportedYet "Code = PathNotSupportedYet"
          }

          test "UpdateProp 'YFields[1]' rewrites an indexed scalar leaf" {
              let op =
                  TreeOp.UpdateProp(NodeId "mix-chart", "YFields[1]", PropValue.Native(nn "profit"))

              match Apply.apply op mixChart with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Chart(spec) -> Expect.equal spec.YFields [ "revenue"; "profit" ] "Second y-field rewritten"
                  | other -> failtestf "Expected Chart, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp nested path on a kind without a nested surface returns PathNotSupportedYet" {
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "Label[0].Text", PropValue.Native(nn "x"))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected PathNotSupportedYet error"
              | Error err -> Expect.equal err.Code ApplyErrorCode.PathNotSupportedYet "Code = PathNotSupportedYet"
          }

          // ─── ReplaceBinding ────────────────────────────────────────────────

          test "ReplaceBinding 'Source' on the Metric swaps in the new typed binding" {
              let newBinding: Binding<obj> = Binding.Static(Some(nn 12345.0))
              let op = TreeOp.ReplaceBinding(NodeId "revenue-metric", "Value", newBinding)

              match Apply.apply op dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some { Kind = NodeKind.Metric(spec) } ->
                      match spec.Value with
                      | Binding.Static(Some v) -> Expect.equal v 12345.0 "Static value updated"
                      | other -> failtestf "Expected Binding.Static, got %A" other
                  | _ -> failtest "Metric not found"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "ReplaceBinding against an unknown slot returns SlotNotFound with available slots hint" {
              let op =
                  TreeOp.ReplaceBinding(NodeId "revenue-metric", "Bogus", Binding.Static(Some(nn 0.0)))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected SlotNotFound error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.SlotNotFound "Code = SlotNotFound"
                  Expect.equal err.Hint.NodeKind (Some "Metric") "Hint.NodeKind = Metric"
                  Expect.contains err.Hint.AvailableFields "Value" "Source listed as available slot"
                  Expect.contains err.Hint.AvailableFields "Trend" "Trend listed as available slot"
          }

          // ─── Negative addressing ───────────────────────────────────────────

          test "Op against a missing NodeId returns NodeNotFound with empty hint" {
              let op = TreeOp.UpdateStyle(NodeId "nonexistent", Defaults.style)

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected NodeNotFound error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.NodeNotFound "Code = NodeNotFound"
                  Expect.isNone err.Hint.NodeKind "Hint.NodeKind unset"
                  Expect.isEmpty err.Hint.AvailableFields "No available_fields for unresolved node"
          }

          test "InsertChild under a Display kind returns ChildlessKind" {
              let op =
                  TreeOp.InsertChild(NodeId "revenue-metric", Fuaran.markdown "noop" "ignored")

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected ChildlessKind error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.ChildlessKind "Code = ChildlessKind"
                  Expect.equal err.Hint.NodeKind (Some "Metric") "Hint.NodeKind = Metric"
          }

          test "InsertChild cannot be out of range — it appends" {
              // `PositionOutOfRange` still exists and is still raised, but only by the
              // nested DATA paths (`Columns[i]`, `Fields[i]`): contained collections
              // whose items have no identity keep their ordinals. A structural insert
              // has no position to be wrong about.
              let op =
                  TreeOp.InsertChild(NodeId "channel-analysis", Fuaran.markdown "appended" "lands last")

              match Apply.apply op dashboard with
              | Ok updated -> Expect.equal (childIdOf updated 2) "appended" "appended after the existing two"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "InsertChild with a duplicate NodeId surfaces DuplicateNodeId" {
              // Re-insert the existing Metric under the dashboard.
              let duplicate = revenueMetric
              let op = TreeOp.InsertChild(NodeId "channel-analysis", duplicate)

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected DuplicateNodeId"
              | Error err -> Expect.equal err.Code ApplyErrorCode.DuplicateNodeId "Code = DuplicateNodeId"
          }

          test "InsertChild into an empty-Children Layout at position 0 returns Ok (not List.head exception)" {
              // Regression for an AI-emission crash: the engine threw
              // ArgumentException("The input list was empty") from `List.head`
              // when InsertChild targeted a Layout with zero children. Contract
              // is Result<Node, ApplyError>; exceptions are off-contract.
              let emptyDashboard: Node<Msg> =
                  Fuaran.dashboard
                      "empty-insert-root"
                      { Defaults.dashboard<Msg> with
                          Children = [] }

              let newChild = Fuaran.markdown "first-child" "hello"
              let op = TreeOp.InsertChild(NodeId "empty-insert-root", newChild)

              match Apply.apply op emptyDashboard with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Box(spec) ->
                      Expect.equal spec.Children.Length 1 "One child after insert"
                      Expect.equal (idOf spec.Children[0].Id) "first-child" "Inserted child landed at position 0"
                  | other -> failtestf "Expected Dashboard, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "InsertChild into empty-Children Layout with id colliding with parent surfaces DuplicateNodeId" {
              // The exact shape from eval prompt `err-001-nodenotfound-typo`:
              // parent_id = child.id = "root", position 0, parent has empty Children.
              // Must produce a structured DuplicateNodeId — never an exception.
              let emptyRoot: Node<Msg> =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard<Msg> with
                          Children = [] }

              let colliding = Fuaran.markdown "root" "collides with parent id"
              let op = TreeOp.InsertChild(NodeId "root", colliding)

              match Apply.apply op emptyRoot with
              | Ok _ -> failtest "Expected DuplicateNodeId"
              | Error err -> Expect.equal err.Code ApplyErrorCode.DuplicateNodeId "Code = DuplicateNodeId"
          }

          test "ReorderChildren of an empty-Children Layout with empty newOrder returns Ok (not List.head exception)" {
              // Regression for the same unsafe-`List.head` pattern that
              // bit InsertChild: when `children` is empty AND `newOrder` is empty,
              // the sort-equality and byId lookups both pass, then `List.head children`
              // throws ArgumentException. Contract is Result<Node, ApplyError>; the
              // no-op reorder of an empty list must be Ok.
              let emptyDashboard: Node<Msg> =
                  Fuaran.dashboard
                      "empty-reorder-root"
                      { Defaults.dashboard<Msg> with
                          Children = [] }

              let op = TreeOp.ReorderChildren(NodeId "empty-reorder-root", [])

              match Apply.apply op emptyDashboard with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Box(spec) -> Expect.equal spec.Children.Length 0 "Still zero children after no-op reorder"
                  | other -> failtestf "Expected Dashboard, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "RemoveNode targeting the root surfaces KindMismatch with a clarifying suggestion" {
              let op = TreeOp.RemoveNode(NodeId "channel-analysis")

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected KindMismatch"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.KindMismatch "Code = KindMismatch"
                  Expect.isSome err.Hint.Suggestion "Suggestion populated"
          }

          // ─── Batch atomicity ───────────────────────────────────────────────

          test "Batch happy-path applies every inner op in sequence" {
              let ops =
                  [ TreeOp.UpdateStyle(
                        NodeId "revenue-metric",
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    )
                    TreeOp.UpdateStyle(
                        NodeId "channel-grid",
                        { Defaults.style with
                            Tone = ToneVariant.Subdued }
                    ) ]

              match Apply.apply (TreeOp.Batch ops) dashboard with
              | Ok updated ->
                  match Introspect.findNode (NodeId "revenue-metric") updated with
                  | Some n -> Expect.equal n.Style.Tone ToneVariant.Brand "Metric tone updated"
                  | None -> failtest "Metric vanished"

                  match Introspect.findNode (NodeId "channel-grid") updated with
                  | Some n -> Expect.equal n.Style.Tone ToneVariant.Subdued "Grid tone updated"
                  | None -> failtest "Grid vanished"
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "Batch failure aborts at the inner op index without partial mutation" {
              let ops =
                  [ TreeOp.UpdateStyle(
                        NodeId "revenue-metric",
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    )
                    TreeOp.UpdateStyle(NodeId "nonexistent-id", Defaults.style) // <- aborts here
                    TreeOp.UpdateStyle(
                        NodeId "channel-grid",
                        { Defaults.style with
                            Tone = ToneVariant.Critical }
                    ) ]

              match Apply.apply (TreeOp.Batch ops) dashboard with
              | Ok _ -> failtest "Expected BatchAborted"
              | Error err ->
                  match err.Code with
                  | ApplyErrorCode.BatchAborted 1 -> ()
                  | other -> failtestf "Expected BatchAborted at index 1, got %A" other

                  // The caller still holds the original `dashboard` reference;
                  // verify it was not mutated through any side channel.
                  match Introspect.findNode (NodeId "revenue-metric") dashboard with
                  | Some n -> Expect.equal n.Style.Tone ToneVariant.Default "Metric tone unchanged after batch failure"
                  | None -> failtest "Metric vanished from original"
          }

          // ─── Feliz-parity additive shapes ───────────────────────

          test "UpdateProp 'Wrap' on a Stack flips the additive boolean field" {
              let stackNode: Node<Msg> =
                  Fuaran.stack
                      "chip-strip"
                      { Defaults.stack<Msg> with
                          Orientation = Orientation.Horizontal
                          Children = [] }

              let op = TreeOp.UpdateProp(NodeId "chip-strip", "Wrap", PropValue.Native(nn true))

              match Apply.apply op stackNode with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Box(spec) ->
                      match spec.Layout with
                      | BoxLayout.Flex f -> Expect.equal f.Wrap true "Wrap flipped to true"
                      | other -> failtestf "Expected BoxLayout.Flex, got %A" other
                  | other -> failtestf "Expected Box, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Variant' on a Heading swaps in the HeadingVariant DU value" {
              let headingNode: Node<Msg> =
                  Fuaran.heading
                      "tax-year-banner"
                      { Defaults.heading with
                          Text = TextSource.Literal "Tax year 2025/26" }

              let op =
                  TreeOp.UpdateProp(NodeId "tax-year-banner", "Variant", PropValue.Native(nn HeadingVariant.Eyebrow))

              match Apply.apply op headingNode with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.Heading(spec) ->
                      Expect.equal spec.Variant HeadingVariant.Eyebrow "Variant updated to Eyebrow"
                  | other -> failtestf "Expected Heading, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "Apply engine reports LabelValueRow's available fields when an unknown field is addressed" {
              let row: Node<Msg> =
                  Fuaran.labelValueRow
                      "row-take-home"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Take-home"
                          Value = binding.``static`` 32500.0
                          Format = format.currency "GBP"
                          Emphasis = true }

              let op =
                  TreeOp.UpdateProp(NodeId "row-take-home", "BogusField", PropValue.Native(nn 0))

              match Apply.apply op row with
              | Ok _ -> failtest "Expected FieldNotFound error"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.FieldNotFound "Code = FieldNotFound"
                  Expect.equal err.Hint.NodeKind (Some "LabelValueRow") "Hint.NodeKind = LabelValueRow"
                  Expect.contains err.Hint.AvailableFields "Label" "Label listed"
                  Expect.contains err.Hint.AvailableFields "Value" "Source listed"
                  Expect.contains err.Hint.AvailableFields "Emphasis" "Emphasis listed"
                  Expect.contains err.Hint.AvailableFields "Help" "Help listed"
          }

          test "ReplaceBinding 'Source' on a LabelValueRow swaps in a typed Binding<float>" {
              let row: Node<Msg> =
                  Fuaran.labelValueRow
                      "row-income"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "Income"
                          Value = binding.``static`` 50000.0 }

              let newBinding: Binding<obj> = Binding.Static(Some(nn 62500.0))
              let op = TreeOp.ReplaceBinding(NodeId "row-income", "Value", newBinding)

              match Apply.apply op row with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.LabelValueRow(spec) ->
                      match spec.Value with
                      | Binding.Static(Some v) -> Expect.equal v 62500.0 "Static binding updated"
                      | other -> failtestf "Expected Binding.Static, got %A" other
                  | other -> failtestf "Expected LabelValueRow, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "InsertChild under a SummaryList appends a LabelValueRow at the addressed position" {
              let summaryList: Node<Msg> =
                  Fuaran.summaryList
                      "summary-list"
                      { Defaults.summaryList<Msg> with
                          Heading = Some(TextSource.Literal "Breakdown")
                          Children =
                              [ Fuaran.labelValueRow
                                    "row-a"
                                    { Defaults.labelValueRow with
                                        Label = TextSource.Literal "A"
                                        Value = binding.``static`` 1.0 } ] }

              let extraRow =
                  Fuaran.labelValueRow
                      "row-b"
                      { Defaults.labelValueRow with
                          Label = TextSource.Literal "B"
                          Value = binding.``static`` 2.0 }

              match Apply.apply (TreeOp.InsertChild(NodeId "summary-list", extraRow)) summaryList with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.SummaryList(spec) ->
                      Expect.equal spec.Children.Length 2 "Two rows after insert"
                      Expect.equal (idOf spec.Children[1].Id) "row-b" "Row B appended"
                  | other -> failtestf "Expected SummaryList, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          test "UpdateProp 'Heading' on a SummaryList swaps the optional section heading" {
              let summaryList: Node<Msg> =
                  Fuaran.summaryList
                      "summary-list"
                      { Defaults.summaryList<Msg> with
                          Heading = None }

              let op =
                  TreeOp.UpdateProp(
                      NodeId "summary-list",
                      "Heading",
                      PropValue.Native(nn (Some(TextSource.Literal "Income")))
                  )

              match Apply.apply op summaryList with
              | Ok updated ->
                  match updated.Kind with
                  | NodeKind.SummaryList(spec) ->
                      match spec.Heading with
                      | Some(TextSource.Literal "Income") -> ()
                      | other -> failtestf "Expected Some Literal 'Income', got %A" other
                  | other -> failtestf "Expected SummaryList, got %A" other
              | Error err -> failtestf "Expected Ok, got Error %A" err
          }

          // ─── ErrorRender JSON envelope ─────────────────────────────────────

          test "ErrorRender.render produces a §4d-shaped JSON envelope with op + error + hint" {
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "Columns", PropValue.Native(nn (List.empty<int>)))

              match Apply.apply op dashboard with
              | Ok _ -> failtest "Expected error, got Ok"
              | Error err ->
                  let json = ErrorRender.render op err
                  Expect.stringContains json "\"op\"" "Has op key"
                  Expect.stringContains json "\"kind\":\"UpdateProp\"" "Op kind echoed"
                  Expect.stringContains json "\"path\":\"Columns\"" "Op path echoed"
                  Expect.stringContains json "\"code\":\"FieldNotFound\"" "Error code present"
                  Expect.stringContains json "\"node_kind\":\"Metric\"" "Hint node_kind"
                  Expect.stringContains json "\"available_fields\"" "Available fields enumerated"
                  Expect.stringContains json "\"nodes_with_columns_field\"" "Dynamic field-search key present"
          } ]
