module Fuaran.UI.Telemetry.Drift.Program

open System
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Console entry — `dotnet run --project src/Fuaran.UI.Telemetry.Drift`.
//
//  The v1 console runner ships with a deliberately small
//  feature set: load no real backend, emit the "wired-up" message and
//  describe how to feed it real telemetry. The drift-against-cron
//  scenario (loading from Fuaran.UI.OpStream.Sqlite or a host's
//  telemetry backend) is the operator-configurable follow-up — the
//  algorithm is here (Aggregates.compute, Detect.run); the operator
//  decides what to feed it.
//
//  The console output documents the loading shape so an operator
//  reading `dotnet run --project src/Fuaran.UI.Telemetry.Drift` once
//  can wire the input themselves without reading source.
// ============================================================================

let private welcome () =
    printfn ""
    printfn "Fuaran op-apply telemetry — drift detector"
    printfn "====================================================="
    printfn ""
    printfn "Aggregate metrics + window-over-window regression detection"
    printfn "for OpApplyTelemetry records. The algorithm lives in"
    printfn "  Fuaran.UI.Telemetry.Drift.Aggregates.compute"
    printfn "  Fuaran.UI.Telemetry.Drift.Detect.run"
    printfn ""
    printfn "To feed real telemetry from a Sqlite OpStream sink,"
    printfn "load OpApplyTelemetry records from your host's telemetry backend"
    printfn "and call:"
    printfn ""
    printfn "  let baseline : OpApplyTelemetry seq = ... // prior week"
    printfn "  let current  : OpApplyTelemetry seq = ... // latest week"
    printfn "  let findings = Detect.run Defaults.thresholds baseline current"
    printfn "  for f in findings do printfn \"%%s\" (Detect.formatFinding f)"
    printfn ""
    printfn "Default thresholds:"

    printfn
        "  SuccessRateRegressionPct = %.2f (=%.0f%% week-on-week drop fires a warning)"
        Defaults.successRateRegressionPct
        (Defaults.successRateRegressionPct * 100.0)

    printfn "  MinBaselineSamples       = %d records" Defaults.minBaselineSamples
    printfn ""

let private syntheticDemo () =
    printfn "Synthetic demonstration — 100-record baseline + 100-record current:"
    printfn ""

    let now = DateTimeOffset.UtcNow

    let baseline: OpApplyTelemetry list =
        [ for i in 1..100 ->
              { StreamId = "demo"
                Sequence = i
                OpKind = OpKind.UpdateProp
                NodeId = Some "k"
                Outcome =
                  if i <= 90 then
                      OpOutcome.Applied
                  else
                      OpOutcome.ApplyEngineError "synthetic"
                TimeToApplyMs = 0.5
                PromptId = None
                UserId = "demo"
                Timestamp = now } ]

    let current: OpApplyTelemetry list =
        [ for i in 101..200 ->
              { StreamId = "demo"
                Sequence = i
                OpKind = OpKind.UpdateProp
                NodeId = Some "k"
                Outcome =
                  if i <= 175 then
                      OpOutcome.Applied
                  else
                      OpOutcome.ApplyEngineError "synthetic"
                TimeToApplyMs = 0.5
                PromptId = None
                UserId = "demo"
                Timestamp = now } ]

    let findings = Detect.run Defaults.thresholds baseline current

    for f in findings do
        printfn "%s" (Detect.formatFinding f)

    printfn ""

[<EntryPoint>]
let main _ =
    welcome ()
    syntheticDemo ()
    0
