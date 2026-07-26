module Fuaran.UI.OpStream.Tests.ApplyPersistTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  applyAndPersist — the apply-engine integration wrapper.
//
//  Tests cover:
//   - Successful apply persists a genesis record (sequence=1, PreviousHash=
//     genesisPreviousHash, Hash matches HashChain.computeHash).
//   - Apply failure short-circuits without touching the sink.
//   - Sequential applies build a hash chain that `Verify.chain` accepts.
//   - Sink.Append throws → apply still returns Ok and OnSinkError fires.
//   - Sink.Append throws → apply still returns Ok and no hook installed →
//     exception is swallowed silently.
//   - PromptId on the context propagates into the persisted record.
// ============================================================================

let private childIds (root: Node<TestMsg>) : string list =
    match root.Kind with
    | NodeKind.Layout(LayoutKind.Box spec) ->
        spec.Children
        |> List.map (fun n ->
            match n.Id with
            | NodeId raw -> raw)
    | _ -> failwithf "Expected dashboard, got %A" root.Kind

/// IOpStreamSink<'Msg> that throws on every Append. Used to verify the
/// best-effort durability contract — the apply path must still return Ok
/// and (when wired) the OnSinkError hook must fire.
type private ThrowingSink<'Msg>() =
    interface IOpStreamSink<'Msg> with
        member _.Append _ =
            async { invalidOp "ThrowingSink: simulated sink failure on Append." }

        member _.Replay(_, _, _) = async { return [] }
        member _.LatestSequence _ = async { return 0 }
        member _.Streams() = async { return [] }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — applyAndPersist (apply-engine integration)"
        [ test "Genesis apply persists a sequence=1 record with genesis previous-hash" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-A" "alice"
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Right child removed by apply"
              | Error e -> failtestf "Expected Ok, got Error %A" e

              let records = sink.Replay("stream-A", 1, 10) |> Async.RunSynchronously

              Expect.equal records.Length 1 "Exactly one record persisted"
              let record = List.head records
              Expect.equal record.Sequence 1 "Genesis sequence is 1"
              Expect.equal record.PreviousHash HashChain.genesisPreviousHash "Genesis PreviousHash"
              Expect.equal record.Actor (Actor.Human "alice") "Actor threaded from ctx (lifted from the user id)"
              Expect.equal record.PromptId None "PromptId defaults to None"
              Expect.equal record.ResultEnvelope OpResultEnvelope.Success "Success envelope on Ok apply"

              let recomputed =
                  HashChain.computeHash
                      record.PreviousHash
                      record.Op
                      record.Sequence
                      record.Timestamp
                      record.Actor
                      record.PromptId
                      record.ResultEnvelope

              Expect.equal record.Hash recomputed "Hash matches HashChain.computeHash"
          }

          test "Apply failure short-circuits — no record persisted" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-B" "alice"
              let tree = buildDashboard ()
              // RemoveNode against an id that doesn't exist in the tree.
              let op = TreeOp.RemoveNode(NodeId "ghost"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Error err -> Expect.equal err.Code ApplyErrorCode.NodeNotFound "Surface ApplyError unchanged"
              | Ok _ -> failtest "Expected apply to fail on missing node"

              let latest = sink.LatestSequence "stream-B" |> Async.RunSynchronously
              Expect.equal latest 0 "Sink untouched — no records persisted on apply failure"
          }

          test "Sequential applies build a valid hash chain" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-C" "alice"
              let tree = buildDashboard ()

              let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let op2 = TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "footer" "Footer")

              let r1 = ApplyPersist.applyAndPersist sink ctx op1 tree |> Async.RunSynchronously

              let tree1 =
                  match r1 with
                  | Ok t -> t
                  | Error e -> failtestf "First apply failed: %A" e

              let r2 = ApplyPersist.applyAndPersist sink ctx op2 tree1 |> Async.RunSynchronously

              match r2 with
              | Ok _ -> ()
              | Error e -> failtestf "Second apply failed: %A" e

              let records = sink.Replay("stream-C", 1, 10) |> Async.RunSynchronously

              Expect.equal records.Length 2 "Both records persisted"
              Expect.equal (records[0].Sequence, records[1].Sequence) (1, 2) "Sequences are 1, 2"
              Expect.equal records[1].PreviousHash records[0].Hash "Hash chain links second to first"

              match Verify.chain records with
              | Ok() -> ()
              | Error e -> failtestf "Hash chain verification failed: %A" e
          }

          test "Sink.Append throws — apply returns Ok and OnSinkError fires" {
              let sink: IOpStreamSink<TestMsg> = ThrowingSink<TestMsg>() :> _
              let captured = ResizeArray<exn>()

              let ctx =
                  PersistContext.create "stream-D" "alice"
                  |> PersistContext.withSinkErrorHook captured.Add

              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Apply succeeded despite sink failure"
              | Error e -> failtestf "Expected Ok, got Error %A" e

              Expect.equal captured.Count 1 "OnSinkError fired exactly once"
              Expect.stringContains (captured[0]).Message "ThrowingSink" "Captured exception came from sink"
          }

          test "Sink.Append throws and no hook installed — exception swallowed silently" {
              let sink: IOpStreamSink<TestMsg> = ThrowingSink<TestMsg>() :> _
              let ctx = PersistContext.create "stream-E" "alice"
              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Apply succeeds even with no hook"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          }

          test "PromptId on context threads into the persisted record" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()

              let ctx =
                  PersistContext.create "stream-F" "alice"
                  |> PersistContext.withPromptId "prompt-42"

              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Ok _ -> ()
              | Error e -> failtestf "Expected Ok, got Error %A" e

              let records = sink.Replay("stream-F", 1, 10) |> Async.RunSynchronously

              let record = List.head records
              Expect.equal record.PromptId (Some "prompt-42") "PromptId propagates from ctx to record"
          }

          test "Hook exception is swallowed — apply still returns Ok" {
              let sink: IOpStreamSink<TestMsg> = ThrowingSink<TestMsg>() :> _

              let ctx =
                  PersistContext.create "stream-G" "alice"
                  |> PersistContext.withSinkErrorHook (fun _ -> failwith "buggy hook")

              let tree = buildDashboard ()
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Ok updated -> Expect.equal (childIds updated) [ "left" ] "Buggy hook does not break apply"
              | Error e -> failtestf "Expected Ok, got Error %A" e
          } ]
