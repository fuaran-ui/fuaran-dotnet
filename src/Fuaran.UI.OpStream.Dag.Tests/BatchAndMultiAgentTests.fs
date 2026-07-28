module Fuaran.UI.OpStream.Dag.Tests.BatchAndMultiAgentTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Batch-aware partial accept (criterion C) + peer-autonomous multi-agent
//  CAS-retry (criterion E, fuaran-side) — Phase 179.
// ============================================================================

let private now = ts 7_000L

[<Tests>]
let tests =
    testList
        "Dag.BatchAndMultiAgent"
        [ test "dependency closure pulls in earlier ops a selected op depends on" {
              // op0 inserts "x" under dash; op1 restyles "x"; op2 restyles "left".
              // Selecting op1 must pull in op0 (op1 targets the node op0 created);
              // op2 is independent.
              let ops =
                  [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "x" "X")
                    TreeOp.UpdateStyle(
                        NodeId "x",
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    )
                    TreeOp.UpdateStyle(
                        leftChildId,
                        { Defaults.style with
                            Tone = ToneVariant.Success }
                    ) ]

              let closure = BatchAccept.dependencyClosure ops (Set.ofList [ 1 ])
              Expect.equal closure (Set.ofList [ 0; 1 ]) "op1 pulls in its dependency op0"

              let closure2 = BatchAccept.dependencyClosure ops (Set.ofList [ 2 ])
              Expect.equal closure2 (Set.ofList [ 2 ]) "op2 is independent"
          }

          test "partial accept keeps a dependency-closed subset; emits Kept/Dropped" {
              let ops =
                  [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "x" "X")
                    TreeOp.UpdateStyle(
                        NodeId "x",
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    )
                    TreeOp.UpdateStyle(
                        leftChildId,
                        { Defaults.style with
                            Tone = ToneVariant.Success }
                    ) ]

              let kept, evt = BatchAccept.partialAccept ops (Set.ofList [ 1 ]) false
              Expect.equal evt.Kept [ 0; 1 ] "kept the closed subset"
              Expect.equal evt.Dropped [ 2 ] "dropped the independent op"
              Expect.equal (List.length kept) 2 "two ops kept, in order"
          }

          test "an indivisible batch withdraws rather than splitting (precedence lattice)" {
              let ops =
                  [ TreeOp.UpdateStyle(
                        leftChildId,
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    )
                    TreeOp.UpdateStyle(
                        rightChildId,
                        { Defaults.style with
                            Tone = ToneVariant.Success }
                    ) ]

              let kept, evt = BatchAccept.partialAccept ops (Set.ofList [ 0 ]) true
              Expect.isEmpty kept "indivisible batch is not split"
              Expect.stringContains evt.Reason "indivisible" "withdraw reason names indivisibility"
          }

          test "peer-autonomous CAS-retry: two agents reconcile with no lost writes" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              // Trunk seeded at base `a`.
              let a = stepRecord "s" None (TreeOp.UpdateStyle(rightChildId, Defaults.style)) 1L
              add sink a
              sink.TryAdvanceHead("s", None, a.Hash) |> Async.RunSynchronously |> ignore

              // Two agents branch off `a` with disjoint edits.
              let agentX =
                  stepRecord
                      "s"
                      (Some a)
                      (TreeOp.UpdateStyle(
                          leftChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Brand }
                      ))
                      2L

              let agentY =
                  stepRecord
                      "s"
                      (Some a)
                      (TreeOp.UpdateStyle(
                          rightChildId,
                          { Defaults.style with
                              Tone = ToneVariant.Success }
                      ))
                      3L

              add sink agentX
              add sink agentY

              // Both reconcile into the trunk; the second rebases onto the first.
              let h1 =
                  DagMerge.mergeIntoTrunk recordAuthor sink "s" initial agentX.Hash now 5
                  |> Async.RunSynchronously

              let h2 =
                  DagMerge.mergeIntoTrunk recordAuthor sink "s" initial agentY.Hash now 5
                  |> Async.RunSynchronously

              Expect.isSome h1 "agent X reconciled"
              Expect.isSome h2 "agent Y reconciled"

              // The final trunk replays to a tree carrying BOTH agents' edits
              // (no lost writes).
              let finalTrunk = sink.Head "s" |> Async.RunSynchronously |> Option.get
              let tree = replaySpine sink "s" initial finalTrunk

              let toneOf (id: NodeId) =
                  match tree.Kind with
                  | NodeKind.Box( spec) ->
                      spec.Children
                      |> List.tryFind (fun c -> c.Id = id)
                      |> Option.map (fun n -> n.Style.Tone)
                  | _ -> None

              Expect.equal (toneOf leftChildId) (Some ToneVariant.Brand) "agent X's edit retained"
              Expect.equal (toneOf rightChildId) (Some ToneVariant.Success) "agent Y's edit retained"
          } ]
