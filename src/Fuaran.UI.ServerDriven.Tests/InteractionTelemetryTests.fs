module Fuaran.UI.ServerDriven.Tests.InteractionTelemetryTests

// ─── Phase 158 QW7: per-interaction telemetry ─────────────────────────────────
//
// LiveConnection.EnableTelemetry records driver-side metrics per handled
// interaction (op / patch / effect counts, patch bytes, rejected) — the signal
// for which surfaces want WebSocket vs SSE. Off until enabled (non-breaking).

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Msg = Inc

type Model = int

let private update Inc (m: Model) : Model = m + 1

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "inc"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Inc }
                  Fuaran.markdown "count" (string m) ] }

let private stubRender (n: Node<Msg>) : string =
    let (NodeId s) = n.Id
    $"<f id='{s}'/>"

let private ev nodeId : LiveEvent =
    { ConnId = "c"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

[<Tests>]
let tests =
    testList
        "Interaction telemetry (Phase 158 QW7)"
        [ test "records a successful interaction's driver-side metrics" {
              let collector = InteractionTelemetry.Collector()
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c", init (DriverServices.create stubRender) update view 0, channel)

              conn.EnableTelemetry collector

              channel.Send(ev "inc")

              match collector.Recorded with
              | [ t ] ->
                  Expect.equal t.NodeId "inc" "node id captured"
                  Expect.equal t.Event "click" "event captured"
                  Expect.equal t.OpCount 1 "one TreeOp (the count text change)"
                  Expect.equal t.PatchCount 1 "one DomPatch"
                  Expect.isGreaterThan t.PatchBytes 0 "patch payload size measured"
                  Expect.equal t.EffectCount 0 "no client effects"
                  Expect.isFalse t.Rejected "a successful interaction is not rejected"
              | other -> failtestf "expected one telemetry record, got %A" other
          }

          test "records a rejected interaction with zero ops/patches" {
              let collector = InteractionTelemetry.Collector()
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c", init (DriverServices.create stubRender) update view 0, channel)

              conn.EnableTelemetry collector

              channel.Send(ev "ghost") // unknown node → G1 reject

              match collector.Recorded with
              | [ t ] ->
                  Expect.isTrue t.Rejected "the G1 reject is recorded as rejected"
                  Expect.equal t.OpCount 0 "no ops on a rejected interaction"
                  Expect.equal t.PatchCount 0 "no patches on a rejected interaction"
              | other -> failtestf "expected one telemetry record, got %A" other
          }

          test "op count resets per interaction (a reject after a success records 0)" {
              let collector = InteractionTelemetry.Collector()
              let channel = InMemoryChannel()

              let conn =
                  LiveConnection("c", init (DriverServices.create stubRender) update view 0, channel)

              conn.EnableTelemetry collector

              channel.Send(ev "inc") // success: 1 op
              channel.Send(ev "ghost") // reject: must record 0, not the stale 1

              match collector.Recorded with
              | [ ok; rejected ] ->
                  Expect.equal ok.OpCount 1 "first interaction recorded its op"
                  Expect.equal rejected.OpCount 0 "the reject recorded 0 ops (not the stale count)"
              | other -> failtestf "expected two telemetry records, got %A" other
          }

          test "telemetry is off until enabled" {
              let collector = InteractionTelemetry.Collector()
              let channel = InMemoryChannel()
              // No EnableTelemetry call.
              let _ =
                  LiveConnection("c", init (DriverServices.create stubRender) update view 0, channel)

              channel.Send(ev "inc")
              Expect.isEmpty collector.Recorded "no telemetry recorded when the sink was never wired"
          } ]
