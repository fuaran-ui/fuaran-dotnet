module Fuaran.UI.OpStream.Tests.ApplyWithSinksTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  applyWithSinks — the both-sinks fan-out (Phase 124).
//
//  Pins the acceptance criteria: one apply emits exactly one op-stream record
//  AND one telemetry record, with no double-apply; the (StreamId, Sequence)
//  join key matches across the two sinks; apply failure still emits telemetry
//  (outcome-classified) but persists nothing; and a telemetry-sink throw is
//  swallowed without breaking the apply + persist path.
// ============================================================================

/// Recording IFuaranTelemetrySink — captures every OpApplyTelemetry. The deny
/// + render-failure members are unused here (no-ops).
type private RecordingTelemetrySink() =
    let opApplies = ResizeArray<OpApplyTelemetry>()
    member _.OpApplies = opApplies

    interface IFuaranTelemetrySink with
        member _.RecordOpApply t = opApplies.Add t
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// IFuaranTelemetrySink whose RecordOpApply throws — to verify the best-effort
/// contract (a misbehaving telemetry sink must not break apply + persist).
type private ThrowingTelemetrySink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ =
            invalidOp "ThrowingTelemetrySink: simulated failure on RecordOpApply."

        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

let private childIds (root: Node<TestMsg>) : string list =
    match root.Kind with
    | NodeKind.Box(spec) ->
        spec.Children
        |> List.map (fun n ->
            match n.Id with
            | NodeId raw -> raw)
    | _ -> failwithf "Expected dashboard, got %A" root.Kind

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — applyWithSinks (both-sinks fan-out)"
        [ test "One apply emits exactly one op-stream record AND one telemetry record (no double-apply)" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let telemetry = RecordingTelemetrySink()
              let ctx = PersistContext.create "stream-A" "alice"
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result =
                  ApplyPersist.applyWithSinks sink (telemetry :> IFuaranTelemetrySink) ctx op tree
                  |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Apply ran exactly once (single child removed)"
              | Error e -> failtestf "Expected Ok, got Error %A" e

              let records = sink.Replay("stream-A", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 1 "Exactly one op-stream record persisted"
              Expect.equal telemetry.OpApplies.Count 1 "Exactly one telemetry record emitted"

              let record = List.head records
              let t = telemetry.OpApplies[0]
              Expect.equal t.Outcome OpOutcome.Applied "Telemetry outcome is Applied"
              Expect.equal t.OpKind OpKind.RemoveNode "Telemetry op-kind matches the op"
              Expect.equal t.NodeId (Some "right") "Telemetry NodeId is the targeted node"
              Expect.equal (record.StreamId, record.Sequence) (t.StreamId, t.Sequence) "join key matches across sinks"
          }

          test "Apply failure emits classified telemetry but persists no record" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let telemetry = RecordingTelemetrySink()
              let ctx = PersistContext.create "stream-B" "alice"
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "ghost"): TreeOp<TestMsg>

              let result =
                  ApplyPersist.applyWithSinks sink (telemetry :> IFuaranTelemetrySink) ctx op tree
                  |> Async.RunSynchronously

              match result with
              | Error err -> Expect.equal err.Code ApplyErrorCode.NodeNotFound "Surface ApplyError unchanged"
              | Ok _ -> failtest "Expected apply to fail on missing node"

              let latest = sink.LatestSequence "stream-B" |> Async.RunSynchronously
              Expect.equal latest 0 "No op-stream record persisted on apply failure"
              Expect.equal telemetry.OpApplies.Count 1 "Telemetry still emitted on failure"
              Expect.equal telemetry.OpApplies[0].Outcome (OpOutcome.NodeNotFound "ghost") "Failure is classified"
          }

          test "Telemetry-sink throw is swallowed — apply + persist still succeed" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let telemetry = ThrowingTelemetrySink()
              let ctx = PersistContext.create "stream-C" "alice"
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result =
                  ApplyPersist.applyWithSinks sink (telemetry :> IFuaranTelemetrySink) ctx op tree
                  |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Apply succeeds despite telemetry throw"
              | Error e -> failtestf "Expected Ok, got Error %A" e

              let records = sink.Replay("stream-C", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 1 "Op-stream record still persisted despite telemetry throw"
          }

          test "Sequential applies through applyWithSinks chain + correlate" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let telemetry = RecordingTelemetrySink()
              let ctx = PersistContext.create "stream-D" "alice"
              let tree = buildDashboard ()

              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>
              let op2 = TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "footer" "Footer")

              let tree1 =
                  match
                      ApplyPersist.applyWithSinks sink (telemetry :> IFuaranTelemetrySink) ctx op1 tree
                      |> Async.RunSynchronously
                  with
                  | Ok t -> t
                  | Error e -> failtestf "First apply failed: %A" e

              match
                  ApplyPersist.applyWithSinks sink (telemetry :> IFuaranTelemetrySink) ctx op2 tree1
                  |> Async.RunSynchronously
              with
              | Ok _ -> ()
              | Error e -> failtestf "Second apply failed: %A" e

              let records = sink.Replay("stream-D", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 2 "Both op-stream records persisted"
              Expect.equal telemetry.OpApplies.Count 2 "Both telemetry records emitted"

              match Verify.chain records with
              | Ok() -> ()
              | Error e -> failtestf "Hash chain verification failed: %A" e

              Expect.equal
                  (telemetry.OpApplies[0].Sequence, telemetry.OpApplies[1].Sequence)
                  (1, 2)
                  "Telemetry sequences track the op-stream sequences"
          } ]
