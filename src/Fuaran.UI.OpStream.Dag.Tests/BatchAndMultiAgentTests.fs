module Fuaran.UI.OpStream.Dag.Tests.BatchAndMultiAgentTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Batch-aware partial accept (criterion C) + peer-autonomous multi-agent
//  CAS-retry (criterion E, fuaran-side) — Phase 179.
// ============================================================================

let private now = timestamp 7_000L

[<Tests>]
let tests =
    testList
        "Dag.BatchAndMultiAgent"
        [ test "dependency closure pulls in earlier ops a selected op depends on" {
              // op0 inserts "x" under dash; op1 restyles "x"; op2 restyles "left".
              // Selecting op1 must pull in op0 (op1 targets the node op0 created).
              // Selecting op2 pulls in BOTH (Phase 2292): a restyle carries an
              // unknown-parent write (Phase 2004), which Core's `Ops.interference`
              // orders against every op that writes structure or a parent it
              // cannot name — every non-empty tier op.
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
              Expect.equal closure2 (Set.ofList [ 0; 1; 2 ]) "a vertical op is ordered after every earlier op"
          }

          test "an op on a node INSIDE an inserted subtree pulls the insert in (Phase 2044)" {
              // op0 inserts Stack "a" holding Markdown "b"; op1 restyles "b".
              // "b" is not the inserted child's own id, so a closure keyed on the
              // addressed ids alone drops op0 — and the kept batch then fails to
              // apply, because "b" does not exist without it.
              let ops =
                  [ TreeOp.InsertChild(
                        dashboardId,
                        Fuaran.stack
                            "a"
                            { Defaults.stack<TestMsg> with
                                Children = [ Fuaran.markdown "b" "B" ] }
                    )
                    TreeOp.UpdateStyle(
                        NodeId "b",
                        { Defaults.style with
                            Tone = ToneVariant.Brand }
                    ) ]

              let kept, evt = BatchAccept.partialAccept ops (Set.ofList [ 1 ]) false
              Expect.equal evt.Kept [ 0; 1 ] "the insert that introduces b is kept with the op on b"
              Expect.equal evt.Dropped [] "nothing is dropped"

              let applied = kept |> List.fold (fun tree op -> applyOk op tree) (buildDashboard ())
              Expect.isSome (Fuaran.UI.Ops.Introspect.findNode (NodeId "b") applied) "the kept batch applies"
          }

          test "the closure is transitive across a chain, in one reverse pass (Phase 2044)" {
              // op2 needs op1 (both address "x"); op1 needs op0 (op0 inserts "x").
              // op3, a restyle elsewhere, closes over all three (Phase 2292): it
              // carries an unknown-parent write, as the move does.
              let ops =
                  [ TreeOp.InsertChild(dashboardId, Fuaran.markdown "x" "X")
                    TreeOp.UpdateStyle(NodeId "x", Defaults.style)
                    TreeOp.MoveNode(NodeId "x", leftChildId)
                    TreeOp.UpdateStyle(rightChildId, Defaults.style) ]

              Expect.equal
                  (BatchAccept.dependencyClosure ops (Set.ofList [ 2 ]))
                  (Set.ofList [ 0; 1; 2 ])
                  "the chain closes"

              Expect.equal
                  (BatchAccept.dependencyClosure ops (Set.ofList [ 3 ]))
                  (Set.ofList [ 0; 1; 2; 3 ])
                  "a vertical op closes over every earlier op"
          }

          test "an op independent under Core stays alone (Phase 2292)" {
              // Two inserts under different NAMED parents: neither reads what the other
              // writes, and neither writes a parent it cannot name.
              let ops =
                  [ TreeOp.InsertChild(NodeId "p", Fuaran.markdown "x" "X")
                    TreeOp.InsertChild(NodeId "q", Fuaran.markdown "y" "Y") ]

              Expect.equal (BatchAccept.dependencyClosure ops (Set.ofList [ 1 ])) (Set.ofList [ 1 ]) "alone"
          }

          // Phase 2292 — the closure is Core's `Ops.interference`, so the unknown-parent
          // write of a move or a remove orders it before a later reorder of its source
          // parent. Over the flat id sets the closure used to read, `[a]` and `[P; b; c]`
          // are disjoint, the move was dropped, and the kept reorder no longer named P's
          // children.
          let reorderBase () =
              container
                  "root"
                  [ container "P" [ Fuaran.markdown "a" "A"; Fuaran.markdown "b" "B"; Fuaran.markdown "c" "C" ]
                    container "Q" [] ]

          let reorder = TreeOp.ReorderChildren(NodeId "P", [ NodeId "c"; NodeId "b" ])

          for name, first in
              [ "move", TreeOp.MoveNode(NodeId "a", NodeId "Q")
                "remove", TreeOp.RemoveNode(NodeId "a") ] do
              test $"a partial accept of a reorder keeps the earlier {name} it needs" {
                  let ops = [ first; reorder ]

                  // Probed both ways first: the batch applies, and the reorder alone is refused.
                  Expect.isOk (Fuaran.UI.Ops.Apply.apply (TreeOp.Batch ops) (reorderBase ())) "the whole batch applies"

                  Expect.isError
                      (Fuaran.UI.Ops.Apply.apply reorder (reorderBase ()))
                      $"the reorder alone is refused — P still holds a without the {name}"

                  let kept, evt = BatchAccept.partialAccept ops (Set.ofList [ 1 ]) false
                  Expect.equal evt.Kept [ 0; 1 ] $"the {name} is kept with the reorder"
                  Expect.equal evt.Dropped [] "nothing is dropped"
                  Expect.isOk (Fuaran.UI.Ops.Apply.apply (TreeOp.Batch kept) (reorderBase ())) "the kept batch applies"
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
                  | NodeKind.Box(spec) ->
                      spec.Children
                      |> List.tryFind (fun c -> NodeId c.Id = id)
                      |> Option.map (fun n -> (n.Style |> Option.defaultValue Defaults.style).Tone)
                  | _ -> None

              Expect.equal (toneOf leftChildId) (Some ToneVariant.Brand) "agent X's edit retained"
              Expect.equal (toneOf rightChildId) (Some ToneVariant.Success) "agent Y's edit retained"
          } ]
