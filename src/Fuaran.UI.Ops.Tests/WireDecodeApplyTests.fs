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
          }

          // ─── The lenient value-coercion sugar, on the UpdateProp leg ───────
          //
          //  Every test above sends the VERBOSE `$type` form. These pin the
          //  SHORTHAND, because the coercion posture is a cross-host contract
          //  and it was previously believed to diverge (the claim: F#/TS demand
          //  the envelope while Python accepts raw primitives as sugar).
          //
          //  It does not diverge, and the spec settles which way: WIRE_FORMAT
          //  §16 is NORMATIVE and says a conformant decoder **MUST** accept the
          //  shorthands and **MUST NOT** invent private ones — so the posture is
          //  accept-everywhere, and a reject-everywhere host would be the
          //  non-conformant one. For a `TextSource` the bare string is not even
          //  shorthand: since the 0.2.0 direction-flip it IS canonical, and the
          //  `{"$type":"Literal"}` envelope is the lenient side of the pair.
          //
          //  `Coerce.*` reaches this for free — each helper is `viaJson` over
          //  the SAME per-type decoder a fresh decode uses, so the UpdateProp
          //  leg inherits §16 rather than re-implementing it. That is exactly
          //  what makes it worth pinning: the sugar here is a consequence of a
          //  shared code path, so a future refactor that gave UpdateProp its own
          //  narrower coercer would regress it silently and only the corpus
          //  fixture `ops/op-updateprop.json` would notice.

          test "§16 sugar: UpdateProp { path=\"Label\", value=<bare string> } coerces to TextSource.Literal" {
              // The corpus fixture `ops/op-updateprop.json` verbatim.
              let wire =
                  """{"$type":"UpdateProp","path":"Label","target":"revenue","value":"Updated revenue"}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      match (metricOf updated).Label with
                      | TextSource.Literal "Updated revenue" -> ()
                      | other -> failtestf "Expected Literal 'Updated revenue', got %A" other
          }

          test "§16 sugar: UpdateProp { path=\"Subtext\", value=<bare string> } coerces into the OPTION slot" {
              // `viaJsonOpt` — the optional flavour must admit the shorthand on
              // the same terms, or the sugar would depend on slot optionality.
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Subtext","value":"vs last quarter"}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      match (metricOf updated).Subtext with
                      | Some(TextSource.Literal "vs last quarter") -> ()
                      | other -> failtestf "Expected Some (Literal 'vs last quarter'), got %A" other
          }

          test "§3.6 sugar: UpdateProp { path=\"Value\", value=<bare number> } coerces to Binding.Static" {
              // The bare-SCALAR shape coercion (§3.6, 2026-07-17 second wave) on
              // a `Binding<float>` slot — `bindingGeneric`'s JNumber arm.
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Value","value":42000.0}"""

              match JsonDecode.decodeOp wire with
              | Error e -> failtestf "decodeOp failed: %A" e
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error err -> failtestf "Apply.apply failed: %A" err
                  | Ok updated ->
                      match (metricOf updated).Value with
                      | Binding.Static(Some 42000.0) -> ()
                      | other -> failtestf "Expected Static (Some 42000.0), got %A" other
          }

          test "§3.6 sugar refusal: a Binding slot still REJECTS a bare object without $type" {
              // The negative half — the profile is a closed list, and an object
              // without a discriminator is "more plausibly a mistyped binding
              // than a Static value" (WIRE_FORMAT §3.6, Refused). Without this
              // the three tests above would also pass under a decoder that had
              // simply gone permissive, which is the failure §16's MUST-NOT
              // extend clause guards against.
              let wire =
                  """{"$type":"UpdateProp","target":"revenue","path":"Value","value":{"amount":42000.0}}"""

              match JsonDecode.decodeOp wire with
              | Error _ -> () // rejected at decode — equally conformant
              | Ok op ->
                  match Apply.apply op revenueMetric with
                  | Error _ -> () // rejected at apply — the Coerce leg refused it
                  | Ok updated ->
                      failtestf
                          "Expected a bare object in a Binding slot to be refused, got %A"
                          (metricOf updated).Value
          } ]
