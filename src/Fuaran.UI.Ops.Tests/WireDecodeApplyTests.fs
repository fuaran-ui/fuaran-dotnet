module Fuaran.UI.Ops.Tests.WireDecodeApply

// ============================================================================
//  Regression coverage for the wire-decode → apply round-trip on the
//  `UpdateProp` value path. A committed baseline run (2026-05-28)
//  captured `KindMismatch` failures for three prompts that
//  share the same shape:
//
//    op-002 / err-002 — `path:"Label", value:{ $type:"Literal", text:... }`
//    err-009          — `path:"Trend", value:{ $type:"Static", value:... }`
//
//  Both decode through `Fuaran.UI.Ops.JsonDecode.decodeOp` into
//  `TreeOp.UpdateProp(_, _, PropValue.Wire (JObj ...))` because the
//  wire DU payload is a JSON object and the decoder boxes objects as
//  `Map<string, obj>`. The apply engine's `tryUnbox<TextSource>` /
//  `tryUnbox<Binding<float> option>` then InvalidCast'd because nothing
//  bridged the structural shape into the typed F# value.
//
//  The fix is `JsonDecode.Coerce` + `Apply.tryUnbox` fallback. These
//  tests exercise the integration end-to-end: parse the eval prompt's
//  wire JSON, apply against a fixture Metric keyed on the same node id the
//  eval prompts use (`"revenue"`), and assert the typed field landed.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

/// Fixture: a Metric node whose id is `"revenue"` so the eval prompts'
/// `target` resolves without rewriting the wire JSON. Constructed as a
/// record literal (rather than via the `Fuaran.metric` smart-ctor) so the
/// `'Msg` parameter explicitly resolves to `obj` — matching the
/// `Node<obj>` shape `JsonDecode.decodeOp` produces.
let private revenueMetric: Node<obj> =
    { Id = "revenue"
      Kind =
        NodeKind.Metric(
            { Label = TextSource.Literal "Revenue"
              Value = Binding.Static(Some 0.0)
              Format = CellFormat.Currency "USD"
              Tone = ToneVariant.Brand
              Weight = StyleWeight.Standard
              Emphasis = Emphasis.Normal
              Trend = None
              TrendFormat = None
              Icon = None
              Subtext = None }
        )
      // `None` is the canonical empty-state / default-style shape since the swap.
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

let private metricOf (node: Node<obj>) : MetricSpec =
    match node.Kind with
    | NodeKind.Metric(spec) -> spec
    | other -> failtestf "Expected Metric, got %A" other

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops UpdateProp wire-decode coercion"
        [

          test "op-002 / err-002: UpdateProp { path=\"Label\", value=TextSource.Literal } applies via wire decode" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Label","value":{"$type":"Literal","text":"Net revenue (USD)"}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated

                      match spec.Label with
                      | TextSource.Literal "Net revenue (USD)" -> ()
                      | other -> failtestf "Expected Literal 'Net revenue (USD)', got %A" other
          }

          test "err-009: UpdateProp { path=\"Trend\", value=Binding.Static } applies into Option<Binding<float>> field" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Trend","value":{"$type":"Static","value":-125.0}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated

                      match spec.Trend with
                      | Some(Binding.Static(Some v)) -> Expect.equal v -125.0 "Trend Static value"
                      | other -> failtestf "Expected Some (Binding.Static -125.0), got %A" other
          }

          // ─── Parallel coverage for the same path on adjacent typed fields ──
          //
          // These don't appear in the 2026-05-28 baseline but exercise the
          // same coercion machinery for the other types the dispatcher hands
          // to `tryUnbox` — so a future Coerce regression on Binding<float>
          // (Source), CellFormat (Format), or TextSource option (Subtext)
          // surfaces in this suite rather than the next eval-baseline run.

          test "UpdateProp { path=\"Source\", value=Binding.Static } applies via wire decode" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Value","value":{"$type":"Static","value":42000.0}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated

                      match spec.Value with
                      | Binding.Static(Some v) -> Expect.equal v 42000.0 "Source Static value"
                      | other -> failtestf "Expected Binding.Static 42000.0, got %A" other
          }

          test "UpdateProp { path=\"Subtext\", value=TextSource.Literal } applies into Option<TextSource> field" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Subtext","value":{"$type":"Literal","text":"vs last quarter"}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated

                      match spec.Subtext with
                      | Some(TextSource.Literal "vs last quarter") -> ()
                      | other -> failtestf "Expected Some Literal, got %A" other
          }

          test "UpdateProp { path=\"Format\", value=CellFormat.Currency } applies via wire decode" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Format","value":{"$type":"Currency","code":"GBP"}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated

                      match spec.Format with
                      | CellFormat.Currency "GBP" -> ()
                      | other -> failtestf "Expected Currency 'GBP', got %A" other
          }

          test "UpdateProp { path=\"Tone\", value=\"Warning\" } applies the string-enum DU via wire decode" {
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Tone","value":"Warning"}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      let spec = metricOf updated
                      Expect.equal spec.Tone ToneVariant.Warning "Tone updated to Warning"
          }

          // ─── Nested paths via wire decode (Phase 364) ──────────────────────

          test "UpdateProp { path=\"Columns[0].Format\", value=CellFormat.Percent } applies via wire decode" {
              // A nested path whose value is a `$type` object — exercises the
              // Map<string,obj> → typed-value coercion on the nested leg.
              let grid: Node<obj> =
                  { revenueMetric with
                      Id = "channel-grid"
                      Kind =
                          NodeKind.DataGrid(
                              { Source = Binding.Static(Some Seq.empty)
                                RowKey = Some(fun _ -> "")
                                RowKeyField = None
                                Columns =
                                  [ { Label = "Channel"
                                      Value = Some(fun _ -> CellValue.Text "")
                                      Field = None
                                      Format = CellFormat.None
                                      Kind = CellKindErased.Text
                                      Width = ColumnWidth.Auto } ]
                                OnRowClick = None
                                Editable = false
                                StaticRows = None }
                          ) }

              let wire =
                  """{"$type":"UpdateProp","target":"channel-grid","path":"Columns[0].Format","value":{"$type":"Percent","decimals":1}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op grid with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      match updated.Kind with
                      | NodeKind.DataGrid(spec) ->
                          match spec.Columns[0].Format with
                          | CellFormat.Percent(Some 1) -> ()
                          | other -> failtestf "Expected Percent (Some 1), got %A" other
                      | other -> failtestf "Expected DataGrid, got %A" other
          }

          test "UpdateProp { path=\"Columns[0].Width\", value=ColumnWidth.Fixed } applies via wire decode" {
              let grid: Node<obj> =
                  { revenueMetric with
                      Id = "channel-grid"
                      Kind =
                          NodeKind.DataGrid(
                              { Source = Binding.Static(Some Seq.empty)
                                RowKey = Some(fun _ -> "")
                                RowKeyField = None
                                Columns =
                                  [ { Label = "Channel"
                                      Value = Some(fun _ -> CellValue.Text "")
                                      Field = None
                                      Format = CellFormat.None
                                      Kind = CellKindErased.Text
                                      Width = ColumnWidth.Auto } ]
                                OnRowClick = None
                                Editable = false
                                StaticRows = None }
                          ) }

              let wire =
                  """{"$type":"UpdateProp","target":"channel-grid","path":"Columns[0].Width","value":{"$type":"Fixed","pixels":120}}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op grid with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      match updated.Kind with
                      | NodeKind.DataGrid(spec) ->
                          Expect.equal spec.Columns[0].Width (ColumnWidth.Fixed 120) "Width applied from wire"
                      | other -> failtestf "Expected DataGrid, got %A" other
          } ]
