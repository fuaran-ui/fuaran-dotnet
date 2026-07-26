module Fuaran.UI.OpStream.Dag.Tests.DegenerateTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Degenerate-chain equivalence (Phase 178 acceptance #1).
//
//  A linear history is a valid degenerate DAG: replaying it through the DAG
//  sink yields the SAME tree as the linear chain, with a verifiable DAG chain
//  and no data migration. The DAG hashes are freshly content-addressed (they
//  do not equal the linear OpRecord.Hash values — the linear hash folds in
//  Sequence, the DAG omits it) — equivalence is "same resulting tree +
//  verifiable chain", not identical hash bytes.
// ============================================================================

/// A three-op linear history applied to the genesis dashboard:
///   1. restyle the left pane,
///   2. remove the right pane,
///   3. insert a fresh pane.
let private linearOps: TreeOp<TestMsg> list =
    let restyle =
        { Defaults.style with
            Tone = ToneVariant.Brand }

    [ TreeOp.UpdateStyle(leftChildId, restyle)
      TreeOp.RemoveNode rightChildId
      TreeOp.InsertChild(dashboardId, Fuaran.markdown "fresh" "Fresh pane") ]

/// Build the linear OpRecord history (hash-chained) for `linearOps`.
let private buildLinear () : OpRecord<TestMsg> list =
    linearOps
    |> List.mapi (fun i op -> (i + 1, op))
    |> List.fold
        (fun (acc: OpRecord<TestMsg> list) (seq, op) ->
            let previousHash =
                match acc with
                | [] -> HashChain.genesisPreviousHash
                | prev :: _ -> prev.Hash

            let timestamp = ts (int64 (1_000 + seq))
            let actor = Actor.Human "tester"

            let hash =
                HashChain.computeHash previousHash op seq timestamp actor None OpResultEnvelope.Success

            { StreamId = "s"
              Sequence = seq
              PreviousHash = previousHash
              Hash = hash
              Op = op
              PromptId = None
              Actor = actor
              Timestamp = timestamp
              ResultEnvelope = OpResultEnvelope.Success }
            :: acc)
        []
    |> List.rev

[<Tests>]
let tests =
    testList
        "Dag.Degenerate"
        [ test "linear history replays to the same tree through the DAG sink" {
              let initial = buildDashboard ()
              let linear = buildLinear ()

              // Linear reference tree.
              let linearTree =
                  match Replay.applyTo initial linear with
                  | Ok t -> t
                  | Error e -> failwithf "linear replay failed: %A" e

              // Embed as a single-parent DAG, add to the sink, advance the head.
              let dag = DagOpRecord.ofLinear linear
              let sink = InMemoryDagSink.create<TestMsg> ()

              let mutable expected = None

              for r in dag do
                  add sink r
                  let ok = sink.TryAdvanceHead("s", expected, r.Hash) |> Async.RunSynchronously
                  Expect.isTrue ok "uncontended head advance must succeed"
                  expected <- Some r.Hash

              let head = sink.Head "s" |> Async.RunSynchronously |> Option.get
              let dagTree = replaySpine sink "s" initial head

              Expect.equal (canonical dagTree) (canonical linearTree) "DAG replay must match the linear tree"
          }

          test "the embedded DAG chain verifies" {
              let dag = DagOpRecord.ofLinear (buildLinear ())

              match DagVerify.records dag with
              | Ok() -> ()
              | Error e -> failtestf "degenerate DAG chain must verify: %A" e
          }

          test "each degenerate node has exactly one parent except the genesis" {
              let dag = DagOpRecord.ofLinear (buildLinear ())

              match dag with
              | genesis :: rest ->
                  Expect.equal genesis.Parents [] "genesis node has no parent"

                  for r in rest do
                      Expect.equal (List.length r.Parents) 1 "each non-genesis degenerate node has one parent"
              | [] -> failtest "expected a non-empty DAG"
          } ]
