module Fuaran.UI.OpStream.Tests.JournalAppliedTests

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

// ============================================================================
//  journalApplied — the append-only leg behind the Phase 193 in-page apply seam.
//
//  `window.__fuaran.apply` is a third dispatch path, and for the op stream to
//  stay the source of truth (FGP 5) a console-driven mutation must journal like
//  any other. But by the time the debug global hands the op over, the host's
//  ApplyHandler has ALREADY applied it — so the journal must append WITHOUT
//  re-applying, or the tree is mutated twice.
//
//  These tests pin that the append-only path produces the same valid hash chain
//  `applyAndPersist` does, and that it never touches the tree.
// ============================================================================

[<Tests>]
let journalAppliedTests =
    testList
        "ApplyPersist.journalApplied — append-only journalling (Phase 193)"
        [ test "journals a genesis record with a valid chain hash" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "console-A" "operator"
              let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<TestMsg>

              ApplyPersist.journalApplied sink ctx op |> Async.RunSynchronously

              let records = sink.Replay("console-A", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 1 "one record journalled"

              let record = List.head records
              Expect.equal record.Sequence 1 "genesis sequence"
              Expect.equal record.PreviousHash HashChain.genesisPreviousHash "genesis previous hash"
              Expect.equal record.Actor (Actor.Human "operator") "actor threaded from the context"

              let recomputed =
                  HashChain.computeHash
                      record.PreviousHash
                      record.Op
                      record.Sequence
                      record.Timestamp
                      record.Actor
                      record.PromptId
                      record.ResultEnvelope

              Expect.equal record.Hash recomputed "hash matches the canonical chain computation"
          }

          test "successive console applies extend one chain" {
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "console-B" "operator"

              ApplyPersist.journalApplied sink ctx (TreeOp.RemoveNode(NodeId "left"))
              |> Async.RunSynchronously

              ApplyPersist.journalApplied sink ctx (TreeOp.RemoveNode(NodeId "right"))
              |> Async.RunSynchronously

              let records = sink.Replay("console-B", 1, 10) |> Async.RunSynchronously
              Expect.equal records.Length 2 "two records"

              let first = records[0]
              let second = records[1]
              Expect.equal second.Sequence 2 "sequence advances"
              Expect.equal second.PreviousHash first.Hash "the second record chains onto the first"
          }

          test "journalling does NOT re-apply — the tree is untouched" {
              // The distinction from applyAndPersist that the seam depends on:
              // the host already applied, so a second apply would double-mutate.
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()
              let ctx = PersistContext.create "console-C" "operator"
              let tree = buildDashboard ()

              ApplyPersist.journalApplied sink ctx (TreeOp.RemoveNode(NodeId "right"))
              |> Async.RunSynchronously

              // `journalApplied` never receives the tree, so it cannot mutate it —
              // pinned here as the behavioural contract the seam relies on.
              match tree.Kind with
              | NodeKind.Box( spec) ->
                  Expect.equal spec.Children.Length 2 "the tree still has both children"
              | other -> failtestf "expected a dashboard, got %A" other
          }

          test "a console stream is independent of the app's own stream" {
              // A host may journal console-driven ops to their own stream id so
              // "what did the console session do?" is answerable in isolation.
              let sink: IOpStreamSink<TestMsg> = InMemorySink.create ()

              ApplyPersist.journalApplied sink (PersistContext.create "app" "alice") (TreeOp.RemoveNode(NodeId "a"))
              |> Async.RunSynchronously

              ApplyPersist.journalApplied
                  sink
                  (PersistContext.create "console" "operator")
                  (TreeOp.RemoveNode(NodeId "b"))
              |> Async.RunSynchronously

              let appRecords = sink.Replay("app", 1, 10) |> Async.RunSynchronously
              let consoleRecords = sink.Replay("console", 1, 10) |> Async.RunSynchronously

              Expect.equal appRecords.Length 1 "app stream has its own record"
              Expect.equal consoleRecords.Length 1 "console stream has its own record"
              Expect.equal consoleRecords[0].Actor (Actor.Human "operator") "attributed to the operator"
          } ]
