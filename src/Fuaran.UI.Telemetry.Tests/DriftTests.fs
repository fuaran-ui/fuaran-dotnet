module Fuaran.UI.Telemetry.Tests.DriftTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Drift
open Fuaran.UI.Telemetry.Drift.Detect

// ============================================================================
//  Drift detector — aggregate metrics + window-over-window regression.
// ============================================================================

let private record (seq': int) (kind: OpKind) (outcome: OpOutcome) (latencyMs: float) : OpApplyTelemetry =
    { StreamId = "drift-test"
      Sequence = seq'
      OpKind = kind
      NodeId = None
      Outcome = outcome
      TimeToApplyMs = latencyMs
      PromptId = None
      UserId = "test"
      Timestamp = DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero) }

[<Tests>]
let tests =
    testList
        "drift detector"
        [ test "Aggregates.compute reports per-OpKind success rate" {
              let records =
                  [ for i in 1..10 -> record i OpKind.UpdateProp OpOutcome.Applied 1.0 ]
                  @ [ record 11 OpKind.UpdateProp (OpOutcome.ApplyEngineError "x") 1.0 ]

              let aggregates = Aggregates.compute 3 records
              Expect.equal aggregates.Length 1 "single OpKind in input → single aggregate"

              let agg = aggregates[0]
              Expect.equal agg.OpKind OpKind.UpdateProp "aggregate carries kind"
              Expect.equal agg.SampleCount 11 "sample count matches input"
              Expect.equal agg.AppliedCount 10 "applied count matches input"

              Expect.floatClose Accuracy.medium agg.SuccessRate (10.0 / 11.0) "success rate is applied / total"
          }

          test "Aggregates.compute surfaces top decoder-rejection reasons" {
              let records =
                  [ record 1 OpKind.UpdateProp (OpOutcome.DecoderRejected "missing-field") 0.5
                    record 2 OpKind.UpdateProp (OpOutcome.DecoderRejected "missing-field") 0.5
                    record 3 OpKind.UpdateProp (OpOutcome.DecoderRejected "type-mismatch") 0.5
                    record 4 OpKind.UpdateProp OpOutcome.Applied 0.5 ]

              let aggregates = Aggregates.compute 3 records
              let agg = aggregates[0]
              Expect.equal agg.TopRejectionReasons.Length 2 "two distinct reasons"
              Expect.equal (fst agg.TopRejectionReasons[0]) "missing-field" "most frequent reason is first"
              Expect.equal (snd agg.TopRejectionReasons[0]) 2 "missing-field count = 2"
          }

          test "Aggregates.compute surfaces top NodeNotFound NodeIds" {
              let records =
                  [ record 1 OpKind.RemoveNode (OpOutcome.NodeNotFound "ghost") 0.1
                    record 2 OpKind.RemoveNode (OpOutcome.NodeNotFound "ghost") 0.1
                    record 3 OpKind.RemoveNode (OpOutcome.NodeNotFound "phantom") 0.1 ]

              let agg = (Aggregates.compute 3 records)[0]
              Expect.equal agg.TopMissingNodeIds.Length 2 "two distinct missing ids"
              Expect.equal (fst agg.TopMissingNodeIds[0]) "ghost" "most frequent id"
              Expect.equal (snd agg.TopMissingNodeIds[0]) 2 "ghost count = 2"
          }

          test "Detect.run flags a regression that exceeds the threshold" {
              // 90 / 100 = 90 % baseline; 70 / 100 = 70 % current → 20pp drop.
              // Default threshold is 10pp, so this fires a Regression.
              let baseline =
                  [ for i in 1..90 -> record i OpKind.UpdateProp OpOutcome.Applied 1.0 ]
                  @ [ for i in 91..100 -> record i OpKind.UpdateProp (OpOutcome.ApplyEngineError "x") 1.0 ]

              let current =
                  [ for i in 101..170 -> record i OpKind.UpdateProp OpOutcome.Applied 1.0 ]
                  @ [ for i in 171..200 -> record i OpKind.UpdateProp (OpOutcome.ApplyEngineError "x") 1.0 ]

              let findings = Detect.run Defaults.thresholds baseline current
              Expect.equal findings.Length 1 "one finding per kind"

              match findings[0] with
              | Regression(kind, baselineRate, currentRate, drop) ->
                  Expect.equal kind OpKind.UpdateProp "kind"
                  Expect.floatClose Accuracy.medium baselineRate 0.90 "baseline rate"
                  Expect.floatClose Accuracy.medium currentRate 0.70 "current rate"
                  Expect.floatClose Accuracy.medium drop 0.20 "drop = 20pp"
              | other -> failtestf "Expected Regression, got %A" other
          }

          test "Detect.run reports NoRegression when drop is below threshold" {
              // 5pp drop, below default 10pp threshold.
              let baseline =
                  [ for i in 1..95 -> record i OpKind.UpdateStyle OpOutcome.Applied 1.0 ]
                  @ [ for i in 96..100 -> record i OpKind.UpdateStyle (OpOutcome.ApplyEngineError "x") 1.0 ]

              let current =
                  [ for i in 101..190 -> record i OpKind.UpdateStyle OpOutcome.Applied 1.0 ]
                  @ [ for i in 191..200 -> record i OpKind.UpdateStyle (OpOutcome.ApplyEngineError "x") 1.0 ]

              let findings = Detect.run Defaults.thresholds baseline current

              match findings[0] with
              | NoRegression(_, baselineRate, currentRate) ->
                  Expect.floatClose Accuracy.medium baselineRate 0.95 "baseline rate"
                  Expect.floatClose Accuracy.medium currentRate 0.90 "current rate"
              | other -> failtestf "Expected NoRegression, got %A" other
          }

          test "Detect.run emits InsufficientData when baseline is too small" {
              let baseline = [ for i in 1..10 -> record i OpKind.EditNode OpOutcome.Applied 1.0 ]
              // Default min-baseline-samples is 50; 10 records is below.
              let current = [ for i in 11..30 -> record i OpKind.EditNode OpOutcome.Applied 1.0 ]

              let findings = Detect.run Defaults.thresholds baseline current

              match findings[0] with
              | InsufficientData(kind, count) ->
                  Expect.equal kind OpKind.EditNode "kind"
                  Expect.equal count 10 "baseline sample count reported"
              | other -> failtestf "Expected InsufficientData, got %A" other
          } ]
