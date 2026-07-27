module Fuaran.UI.Telemetry.Tests.ApplyTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  applyWithTelemetry — the opt-in wrapper that meets the
//  "Op-apply emits OpApplyTelemetry to the configured sink for every
//  OpOutcome branch" acceptance criterion.
// ============================================================================

type private Msg = | Tick

// F# 10 nullness types `box _` as `obj | null`; wrap via `Unchecked.nonNull`
// for PropValue.Native payloads (mirrors Fuaran.UI.Ops.Tests/OpsApplyTests.fs::nn).
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private revenueMetric: Node<Msg> =
    Fuaran.metric
        "revenue-metric"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = Binding.Static 1.0 }

let private dashboard: Node<Msg> =
    Fuaran.dashboard
        "channel-analysis"
        { Defaults.dashboard<Msg> with
            Children = [ revenueMetric ] }

let private ctx: Apply.ApplyContext =
    { StreamId = "stream-A"
      Sequence = 1
      UserId = "user-1"
      PromptId = Some "prompt-A" }

[<Tests>]
let tests =
    testList
        "applyWithTelemetry"
        [ test "OpOutcome.Applied for a successful EditNode" {
              let sink = InMemorySink()

              let newKind =
                  NodeKind.Display(
                      DisplayKind.Heading
                          { Defaults.heading with
                              Text = TextSource.Literal "Renamed" }
                  )

              let op = TreeOp.EditNode(NodeId "revenue-metric", newKind)

              let result =
                  Apply.applyWithTelemetry (sink :> IFuaranTelemetrySink) ctx op dashboard

              Expect.isOk result "apply must succeed"

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 1 "one telemetry record emitted"
              Expect.equal recorded[0].Outcome OpOutcome.Applied "outcome is Applied"
              Expect.equal recorded[0].OpKind OpKind.EditNode "OpKind is EditNode"
              Expect.equal recorded[0].NodeId (Some "revenue-metric") "NodeId is the target id"
              Expect.equal recorded[0].StreamId "stream-A" "StreamId from context"
              Expect.equal recorded[0].PromptId (Some "prompt-A") "PromptId from context"
              Expect.equal recorded[0].UserId "user-1" "UserId from context"
              Expect.isGreaterThanOrEqual recorded[0].TimeToApplyMs 0.0 "TimeToApplyMs >= 0"
          }

          test "OpOutcome.NodeNotFound carries the offending NodeId" {
              let sink = InMemorySink()
              let op = TreeOp.RemoveNode(NodeId "ghost")

              let result =
                  Apply.applyWithTelemetry (sink :> IFuaranTelemetrySink) ctx op dashboard

              Expect.isError result "apply must fail"

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 1 "one telemetry record emitted"

              match recorded[0].Outcome with
              | OpOutcome.NodeNotFound nodeId -> Expect.equal nodeId "ghost" "outcome carries the missing NodeId"
              | other -> failtestf "Expected NodeNotFound, got %A" other
          }

          test "OpOutcome.ApplyEngineError for a FieldNotFound branch" {
              let sink = InMemorySink()
              // Address a field that doesn't exist on Metric — Metric.NonExistentField.
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "NonExistentField", PropValue.Native(nn "x"))

              let result =
                  Apply.applyWithTelemetry (sink :> IFuaranTelemetrySink) ctx op dashboard

              Expect.isError result "apply must fail"

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 1 "one telemetry record emitted"

              match recorded[0].Outcome with
              | OpOutcome.ApplyEngineError detail ->
                  Expect.stringContains detail "FieldNotFound" "detail names the error code"
                  Expect.stringContains detail "NonExistentField" "detail mentions the offending field"
              | other -> failtestf "Expected ApplyEngineError, got %A" other
          }

          test "OpKind.Batch fires a single telemetry record (the batch is one event)" {
              let sink = InMemorySink()
              let inner = [ TreeOp.RemoveNode(NodeId "revenue-metric") ]
              let op = TreeOp.Batch inner

              let result =
                  Apply.applyWithTelemetry (sink :> IFuaranTelemetrySink) ctx op dashboard

              Expect.isOk result "batch apply succeeds"

              let recorded = sink.OpApplyRecords
              Expect.equal recorded.Length 1 "single record per applyWithTelemetry call"
              Expect.equal recorded[0].OpKind OpKind.Batch "outer op kind is Batch"
              Expect.equal recorded[0].NodeId None "Batch has no top-level NodeId"
          }

          test "Throwing sink does not poison the apply result" {
              let throwingSink =
                  { new IFuaranTelemetrySink with
                      member _.RecordOpApply _ = failwith "deliberately throwing sink"
                      member _.RecordDeny _ = ()
                      member _.RecordRenderFailure _ = ()
                      member _.RecordProviderCall _ = ()
                      member _.RecordCacheStat _ = ()
                      member _.RecordValidateOutcome _ = () }

              let op = TreeOp.RemoveNode(NodeId "revenue-metric")
              let result = Apply.applyWithTelemetry throwingSink ctx op dashboard
              // The apply result must still propagate; sink failures are swallowed.
              Expect.isOk result "apply succeeded despite throwing sink"
          } ]
