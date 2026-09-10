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
    | NodeKind.Box(spec) -> spec.Children |> List.map _.Id
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

// ============================================================================
//  Phase 1666 — the FGP 5 obligations for non-structural addressing.
//
//  `applyStructural` can now DESCEND through a keyed position (a `Switch`
//  case's child, an `ErrorBoundary` arm, a `state.onLoading` alternative) to
//  reach a node below it, where it previously answered `NodeNotFound` for a
//  node `findNode` reaches perfectly well. FGP 5 asks for two things of any
//  newly-reachable apply path, and neither is safe to assert by inspection:
//
//   1. **Every applied op is hash-chained and persisted.** The claim is that
//      the descent introduces no second path — it returns an ordinary
//      `Ok tree` from `applyOne`, so it reaches `OpOutcome.ofApplyResult`, the
//      telemetry sink and this persist wrapper by exactly the route every other
//      apply takes. A test is what tells that apart from a path that quietly
//      bypasses the wrapper.
//   2. **Addressing must not break replay.** The op recorded for a descended
//      apply has to reproduce the same tree when re-applied from the stream
//      against the same initial tree — otherwise a store whose records verify
//      folds to a state that never existed, which is the failure mode
//      `Replay.applyTo` refuses a broken chain to avoid.
//
//  And the refusal half, which is the same obligation seen from the other end:
//  a `PositionNotStructural` refusal must persist NOTHING, exactly as
//  `NodeNotFound` does. A refused op in the stream would replay as a state
//  change that never happened.
// ============================================================================

/// A dashboard whose second child is a `Switch` holding a container at
/// `cases[0].child` — so `keyed-inner` is a node the structural ops could not
/// address before this phase, and `keyed-panel` IS the position.
let private buildKeyedDashboard () : Node<TestMsg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<TestMsg> with
            Children =
                [ Fuaran.markdown "left" "Left pane"
                  Fuaran.switch
                      "mode"
                      { Defaults.switch<TestMsg> with
                          On = Binding.State("mode", None)
                          Cases =
                              [ { Match = Some "compact"
                                  When = None
                                  Child =
                                    Fuaran.dashboard
                                        "keyed-panel"
                                        { Defaults.dashboard<TestMsg> with
                                            Children =
                                                [ Fuaran.markdown "keyed-inner" "Inner"
                                                  Fuaran.markdown "keyed-kept" "Kept" ] } } ]
                          Default = Fuaran.markdown "fallback" "Fallback" } ] }

[<Tests>]
let nonStructuralPersistTests =
    testList
        "Fuaran.UI.OpStream — applyAndPersist through a non-structural position (Phase 1666)"
        [ test "a descended apply is persisted and hash-chained by the same wrapper" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-ns-1" "alice"
              let tree = buildKeyedDashboard ()
              let op = TreeOp.RemoveNode(NodeId "keyed-inner"): TreeOp<TestMsg>

              let result = ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously

              match result with
              | Error e -> failtestf "a node below a keyed position was unreachable: %A" e
              | Ok updated ->
                  Expect.isNone
                      (Fuaran.UI.Ops.Introspect.findNode (NodeId "keyed-inner") updated)
                      "the addressed node below the position was removed"

                  Expect.isSome
                      (Fuaran.UI.Ops.Introspect.findNode (NodeId "keyed-panel") updated)
                      "the position itself still holds its node — arity untouched"

              let records = sink.Replay("stream-ns-1", 1, 10) |> Async.RunSynchronously

              Expect.equal records.Length 1 "the descended apply persisted exactly one record"
              let record = List.head records
              Expect.equal record.Sequence 1 "genesis sequence"
              Expect.equal record.PreviousHash HashChain.genesisPreviousHash "genesis previous-hash"
              Expect.equal record.ResultEnvelope OpResultEnvelope.Success "Success envelope on an Ok apply"

              let recomputed =
                  HashChain.computeHash
                      record.PreviousHash
                      record.Op
                      record.Sequence
                      record.Timestamp
                      record.Actor
                      record.PromptId
                      record.ResultEnvelope

              Expect.equal
                  record.Hash
                  recomputed
                  "the record's hash recomputes — the chain covers this op like any other"
          }

          test "replay of the recorded stream reproduces the descended tree" {
              // The obligation that matters most: addressing must not break
              // replay. The records are replayed against a FRESH initial tree,
              // so nothing from the applying session carries over.
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-ns-2" "alice"

              let op1 = TreeOp.RemoveNode(NodeId "keyed-inner"): TreeOp<TestMsg>

              let op2 =
                  TreeOp.InsertChild(NodeId "keyed-panel", Fuaran.markdown "keyed-added" "Added")

              let applied =
                  ApplyPersist.applyAndPersist sink ctx op1 (buildKeyedDashboard ())
                  |> Async.RunSynchronously
                  |> Result.bind (fun t ->
                      ApplyPersist.applyAndPersist sink ctx op2 t
                      |> Async.RunSynchronously
                      |> Result.mapError id)

              let appliedTree =
                  match applied with
                  | Ok t -> t
                  | Error e -> failtestf "the two descended applies did not both succeed: %A" e

              let records = sink.Replay("stream-ns-2", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 2 "both descended applies were recorded"

              // `applyTo` verifies the chain first, so this also asserts the two
              // records chain to each other.
              match Replay.applyTo (buildKeyedDashboard ()) records with
              | Error e -> failtestf "replaying descended ops failed: %A" e
              | Ok replayed ->
                  // `Node` carries closures and has no structural equality —
                  // compare the ids the ops moved, in order, at the position.
                  let idsAt (root: Node<TestMsg>) =
                      match Fuaran.UI.Ops.Introspect.findNode (NodeId "keyed-panel") root with
                      | None -> failtest "the keyed position lost its node"
                      | Some panel ->
                          Fuaran.UI.Ops.Introspect.getChildren panel.Kind
                          |> Option.defaultValue []
                          |> List.map (fun (c: Node<TestMsg>) -> c.Id)

                  Expect.equal
                      (idsAt replayed)
                      (idsAt appliedTree)
                      "the replayed tree matches the applied one below the keyed position"

                  Expect.equal
                      (idsAt replayed)
                      [ "keyed-kept"; "keyed-added" ]
                      "and it is the state the two ops describe"
          }

          test "a PositionNotStructural refusal persists nothing" {
              // Same contract as the `NodeNotFound` short-circuit above, and the
              // same reason: a refused op in the stream would replay as a state
              // change that never happened.
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "stream-ns-3" "alice"
              let tree = buildKeyedDashboard ()
              // `keyed-panel` IS the position — removing it would leave the
              // Switch case with no child, which a case cannot express.
              let op = TreeOp.RemoveNode(NodeId "keyed-panel"): TreeOp<TestMsg>

              match ApplyPersist.applyAndPersist sink ctx op tree |> Async.RunSynchronously with
              | Ok _ -> failtest "removing the node AT a keyed position was accepted"
              | Error err ->
                  match err.Code with
                  | ApplyErrorCode.PositionNotStructural slot ->
                      Expect.equal slot "Switch.cases[0].child" "the refusal names the position"
                  | other -> failtestf "expected PositionNotStructural, got %A" other

              let latest = sink.LatestSequence "stream-ns-3" |> Async.RunSynchronously
              Expect.equal latest 0 "sink untouched — a refusal is not history"
          } ]
