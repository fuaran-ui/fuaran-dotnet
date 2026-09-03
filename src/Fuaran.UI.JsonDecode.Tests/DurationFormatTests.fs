module Fuaran.UI.JsonDecode.Tests.DurationFormat

// `box` of a literal is nullable-typed under the F# 10 nullness rules; the
// Row cells below are known non-null (same waiver Fixtures.fs carries).
#nowarn "3261"

// ============================================================================
//  Phase 819 (`CellFormat.Duration` + `CellFormat.RelativeTime` +
//  `Format.Duration`) and Phase 821 (the standalone `Icon` display kind).
//
//  Three layers:
//   1. Wire round-trips — encode → decode → re-encode byte-equality over the
//      new vocabulary (the same assertion the corpus suite runs, exercised
//      here directly against the F# fixture values so the typed decode is
//      also inspectable).
//   2. Duration decomposition — the ONE shared hand-rolled implementation in
//      `Fuaran.UI.Renderer.Formatting` (Compact / Clock / Long, <1min, >=1h,
//      zero, negative). Locale-independent by design, so the expectations
//      are exact strings.
//   3. Didactic rejects — an unknown `DurationStyle` / `IconSize` names the
//      allowed cases (the decoder's teaching contract).
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

module Formatting = Fuaran.UI.Renderer.Formatting

// ─── Helpers ─────────────────────────────────────────────────────────────

let private roundTripped (n: Node<obj>) : string * Node<obj> =
    let wire = CanonicalJson.encodeNode n

    match JsonDecode.decodeNodeObj wire with
    | Ok decoded ->
        let reencoded = CanonicalJson.encodeNode decoded
        Expect.equal reencoded wire "round-trip preserves canonical-JSON byte form"
        wire, decoded
    | Error e -> failtestf "decode failed: %s at %s — %s" e.Code e.Path e.Message

// ─── 1. Wire round-trips ─────────────────────────────────────────────────

[<Tests>]
let wireRoundTrips =
    testList
        "Phase 819/821 — wire round-trips"
        [ testCase "Metric with CellFormat.Duration value + cell RelativeTime trend round-trips" (fun () ->
              let wire, decoded = roundTripped Fixtures.metricDuration

              Expect.stringContains
                  wire
                  "\"format\":{\"$type\":\"Duration\",\"style\":\"Compact\",\"unit\":\"Minutes\"}"
                  "Duration discriminator with alphabetical field order (style before unit) on the wire"

              Expect.stringContains
                  wire
                  "\"trendFormat\":{\"$type\":\"RelativeTime\",\"unit\":\"Minute\"}"
                  "RelativeTime discriminator on the wire"

              // `CellFormat` carries a closure arm (`Custom`), so the DU has
              // no structural equality — assert the typed shape by pattern.
              match decoded.Kind with
              | NodeKind.Metric spec ->
                  match spec.Format with
                  | CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact) -> ()
                  | other -> failtestf "typed Duration format did not survive decode: %A" other

                  match spec.TrendFormat with
                  | Some(CellFormat.RelativeTime RelativeTimeUnit.Minute) -> ()
                  | other -> failtestf "typed cell RelativeTime trend format did not survive decode: %A" other
              | other -> failtestf "expected a Metric, got %A" other)

          testCase "grid column with CellFormat.RelativeTime round-trips" (fun () ->
              let col: ColumnErased<obj> =
                  { Label = "Last seen"
                    Value = None
                    Field = Some "lastSeen"
                    Sortable = None
                    Editable = None
                    Format = CellFormat.RelativeTime RelativeTimeUnit.Hour
                    Kind = CellKindErased.Numeric
                    Width = ColumnWidth.Auto }

              let grid: Node<obj> =
                  { Id = "grid-relative-1"
                    Kind =
                      NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source =
                              Binding.Static(Some(Seq.ofList [ (Map.ofList [ "lastSeen", box -3 ]: Fuaran.Core.Row) ]))
                            RowKey = None
                            RowKeyField = Some "lastSeen"
                            Columns = [ col ]
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
                    Motion = None
                    ExtraAttributes = None
                    Tooltip = None }

              let _, decoded = roundTripped grid

              match decoded.Kind with
              | NodeKind.DataGrid spec ->
                  match spec.Columns |> List.map _.Format with
                  | [ CellFormat.RelativeTime RelativeTimeUnit.Hour ] -> ()
                  | other -> failtestf "typed column RelativeTime format did not survive decode: %A" other
              | other -> failtestf "expected a DataGrid, got %A" other)

          testCase "Binding.Format with Format.Duration round-trips" (fun () ->
              let n: Node<obj> =
                  { Id = "fmt-duration-1"
                    Kind =
                      NodeKind.Markdown(
                          { Text =
                              TextSource.Bound(
                                  Binding.Format(
                                      Binding.Static(Some 4830.0),
                                      Format.Duration(DurationUnit.Seconds, DurationStyle.Clock),
                                      LocaleSource.Ambient
                                  )
                              ) }
                      )
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = None
                    ExtraAttributes = None
                    Tooltip = None }

              let wire, decoded = roundTripped n

              Expect.stringContains
                  wire
                  "{\"$type\":\"Duration\",\"style\":\"Clock\",\"unit\":\"Seconds\"}"
                  "alphabetical field order (style before unit) on the wire"

              match decoded.Kind with
              | NodeKind.Markdown { Text = TextSource.Bound(Binding.Format(_, fmt, _)) } ->
                  Expect.equal
                      fmt
                      (Format.Duration(DurationUnit.Seconds, DurationStyle.Clock))
                      "typed Format.Duration survives decode"
              | other -> failtestf "expected a Markdown with a bound Format, got %A" other)

          testCase "unlabelled (decorative) Icon round-trips" (fun () ->
              let wire, decoded = roundTripped Fixtures.iconDecorative

              Expect.stringContains wire "\"$type\":\"Icon\"" "Icon discriminator on the wire"
              Expect.stringContains wire "\"size\":\"Large\"" "non-default size emitted"
              Expect.isFalse (wire.Contains "\"tone\"") "default tone omitted"
              Expect.isFalse (wire.Contains "\"label\"") "absent label omitted (decorative)"

              match decoded.Kind with
              | NodeKind.Icon spec ->
                  Expect.equal spec.Icon "sparkles" "icon name survives decode"
                  Expect.equal spec.Size IconSize.Large "size survives decode"
                  Expect.equal spec.Tone ToneVariant.Default "omitted tone decodes to Default"
                  Expect.equal spec.Label None "no label — decorative"
              | other -> failtestf "expected an Icon, got %A" other)

          testCase "labelled Icon round-trips (default size omitted)" (fun () ->
              let labelled: Node<obj> =
                  { Id = "icon-labelled-1"
                    Kind =
                      NodeKind.Icon(
                          { Icon = "check-circle"
                            Size = IconSize.Medium
                            Tone = ToneVariant.Success
                            Label = Some "Payment received" }
                      )
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = None
                    ExtraAttributes = None
                    Tooltip = None }

              let wire, decoded = roundTripped labelled

              Expect.isFalse (wire.Contains "\"size\"") "default Medium size omitted"
              Expect.stringContains wire "\"label\":\"Payment received\"" "label emitted"
              Expect.stringContains wire "\"tone\":\"Success\"" "non-default tone emitted"

              match decoded.Kind with
              | NodeKind.Icon spec ->
                  Expect.equal spec.Size IconSize.Medium "omitted size decodes to Medium"
                  Expect.equal spec.Label (Some "Payment received") "label survives decode"
                  Expect.equal spec.Tone ToneVariant.Success "tone survives decode"
              | other -> failtestf "expected an Icon, got %A" other) ]

// ─── 2. Duration decomposition (shared Compact / Clock / Long impl) ──────

[<Tests>]
let durationDecomposition =
    let compact = Formatting.formatDuration DurationUnit.Seconds DurationStyle.Compact
    let clock = Formatting.formatDuration DurationUnit.Seconds DurationStyle.Clock
    let long = Formatting.formatDuration DurationUnit.Seconds DurationStyle.Long

    testList
        "Phase 819 — duration decomposition"
        [ testCase "Compact: >=1h renders hours + minutes" (fun () ->
              Expect.equal (compact 4800.0) "1h 20m" "80 minutes"
              Expect.equal (compact 7500.0) "2h 5m" "125 minutes"
              Expect.equal (compact 7200.0) "2h" "whole hours drop the zero minutes")

          testCase "Compact: <1h renders minutes + seconds" (fun () ->
              Expect.equal (compact 330.0) "5m 30s" "five and a half minutes"
              Expect.equal (compact 300.0) "5m" "whole minutes drop the zero seconds"
              Expect.equal (compact 42.0) "42s" "under a minute"
              Expect.equal (compact 0.0) "0s" "zero")

          testCase "Compact: unit scaling (Minutes / Hours sources)" (fun () ->
              Expect.equal
                  (Formatting.formatDuration DurationUnit.Minutes DurationStyle.Compact 80.0)
                  "1h 20m"
                  "80 minutes"

              Expect.equal
                  (Formatting.formatDuration DurationUnit.Hours DurationStyle.Compact 1.5)
                  "1h 30m"
                  "1.5 hours")

          testCase "Clock: h:mm:ss from one hour, m:ss below" (fun () ->
              Expect.equal (clock 4800.0) "1:20:00" ">=1h pads minutes + seconds"
              Expect.equal (clock 3661.0) "1:01:01" "single-digit components pad"
              Expect.equal (clock 330.0) "5:30" "<1h renders m:ss"
              Expect.equal (clock 42.0) "0:42" "under a minute keeps the minute slot"
              Expect.equal (clock 0.0) "0:00" "zero")

          testCase "Long: English words, singular/plural, zero components omitted" (fun () ->
              Expect.equal (long 4800.0) "1 hour 20 minutes" "singular hour, plural minutes"
              Expect.equal (long 7200.0) "2 hours" "zero minutes omitted"
              Expect.equal (long 90.0) "1 minute 30 seconds" "minutes + seconds"
              Expect.equal (long 3661.0) "1 hour 1 minute 1 second" "all singular"
              Expect.equal (long 0.0) "0 minutes" "zero")

          testCase "negative durations prefix a minus sign" (fun () ->
              Expect.equal (compact -4800.0) "-1h 20m" "compact"
              Expect.equal (clock -330.0) "-5:30" "clock"
              Expect.equal (long -90.0) "-1 minute 30 seconds" "long") ]

// ─── 3. Didactic rejects ─────────────────────────────────────────────────

[<Tests>]
let didacticRejects =
    testList
        "Phase 819/821 — didactic rejects"
        [ testCase "unknown DurationStyle names the allowed cases" (fun () ->
              let wire =
                  """{"id":"m1","kind":{"$type":"Metric","format":{"$type":"Duration","style":"Fast","unit":"Minutes"},"label":"x","value":{"$type":"Static","value":5}}}"""

              match JsonDecode.decodeNodeObj wire with
              | Ok _ -> failtest "an unknown DurationStyle must be rejected"
              | Error e ->
                  Expect.equal e.Code "UNKNOWN_DU_CASE" "the unknown-enum class"
                  Expect.stringContains e.Path ".format.style" "the error path names the slot"

                  Expect.equal
                      e.ExpectedShape
                      (Some "Compact | Clock | Long")
                      "the didactic hint lists the allowed styles")

          testCase "unknown DurationUnit names the allowed cases" (fun () ->
              let wire =
                  """{"id":"m1","kind":{"$type":"Metric","format":{"$type":"Duration","style":"Compact","unit":"Days"},"label":"x","value":{"$type":"Static","value":5}}}"""

              match JsonDecode.decodeNodeObj wire with
              | Ok _ -> failtest "an unknown DurationUnit must be rejected"
              | Error e ->
                  Expect.equal e.Code "UNKNOWN_DU_CASE" "the unknown-enum class"
                  Expect.equal e.ExpectedShape (Some "Seconds | Minutes | Hours") "the didactic hint lists the units")

          testCase "unknown IconSize names the allowed cases" (fun () ->
              let wire = """{"id":"i1","kind":{"$type":"Icon","icon":"sparkles","size":"Huge"}}"""

              match JsonDecode.decodeNodeObj wire with
              | Ok _ -> failtest "an unknown IconSize must be rejected"
              | Error e ->
                  Expect.equal e.Code "UNKNOWN_DU_CASE" "the unknown-enum class"
                  Expect.stringContains e.Path ".size" "the error path names the slot"
                  Expect.equal e.ExpectedShape (Some "Small | Medium | Large") "the didactic hint lists the sizes") ]
