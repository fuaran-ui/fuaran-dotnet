module Fuaran.UI.Ops.CleanRoom.Tests.AuditTests

// ============================================================================
//  Acceptance: every skeleton issuance + every gate decision is audit-emitted
//  (FGP 5), and the audit records are content-free (a node count, ids, an
//  op-kind + outcome — never prose).
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.CleanRoom
open Fuaran.UI.Ops.CleanRoom.Audit
open Fuaran.UI.Ops.CleanRoom.Tests.Fixtures

[<Tests>]
let tests =
    testList
        "structure-only clean room — audit emission"
        [ test "issuing a skeleton emits one content-free issuance record" {
              let sink = InMemoryCleanRoomAuditSink()
              let sk = Audit.issue sink realTree

              Expect.equal sink.Issued.Length 1 "exactly one issuance recorded"
              let issued = sink.Issued.Head
              Expect.equal issued.RootId (NodeId "doc-root") "issuance records the root id"
              Expect.equal issued.NodeCount (Skeleton.nodeCount sk) "issuance records the node count"
              Expect.equal issued.NodeCount 7 "the document has 7 nodes"

              // The issuance record is content-free: searching its rendering for
              // any content sentinel finds nothing.
              let rendered = sprintf "%A" issued

              for secret in [ secretHeadingA; secretHeadingB; secretBody; secretMetric ] do
                  Expect.isFalse (rendered.Contains secret) (sprintf "issuance record must not carry '%s'" secret)
          }

          test "each gate decision emits one content-free decision record" {
              let sink = InMemoryCleanRoomAuditSink()
              let broker = Broker.StructuralOpBroker.create ()
              let sk = Audit.issue sink realTree

              let released =
                  Audit.enforceAudited sink broker sk (TreeOp.RemoveNode(NodeId "recital"))

              let withheld =
                  Audit.enforceAudited
                      sink
                      broker
                      sk
                      (TreeOp.EditNode(NodeId "clause-1-title", (Fuaran.UI.Fuaran.markdown "d" "x").Kind))

              // The decisions land in order alongside the one issuance.
              Expect.equal sink.Decisions.Length 2 "two decisions recorded"

              let d0 = sink.Decisions[0]
              Expect.equal d0.OpKind "RemoveNode" "first decision is the RemoveNode"
              Expect.equal d0.Outcome GateOutcome.Released "RemoveNode released"
              Expect.equal d0.TargetIds [ NodeId "recital" ] "decision records the structural target id"

              let d1 = sink.Decisions[1]
              Expect.equal d1.OpKind "EditNode" "second decision is the EditNode"

              match d1.Outcome with
              | GateOutcome.Withheld _ -> ()
              | GateOutcome.Released -> failtest "EditNode must be withheld"

              // The returned decisions match the recorded outcomes.
              match released, withheld with
              | Broker.StructuralGateDecision.Released _, Broker.StructuralGateDecision.Withheld _ -> ()
              | _ -> failtest "enforceAudited must return the same decision it records"

              // The decision records are content-free.
              let rendered = sprintf "%A" sink.Decisions

              for secret in [ secretHeadingA; secretBody ] do
                  Expect.isFalse (rendered.Contains secret) (sprintf "decision record must not carry '%s'" secret)
          }

          test "the NoOp sink swallows emissions" {
              let sink = NoOpCleanRoomAuditSink() :> ICleanRoomAuditSink
              // Must not throw; nothing to observe.
              let sk = Audit.issue sink realTree

              Audit.enforceAudited sink (Broker.StructuralOpBroker.create ()) sk (TreeOp.RemoveNode(NodeId "recital"))
              |> ignore
          }

          // Phase 2044 — `opKindName` and `referencedIds` project from
          // `TreeOp.kindName` / `TreeOp.targets`. The expected columns are what
          // their own per-case matches answered before, so the audit record is
          // pinned as unchanged for every case.
          test "opKindName and referencedIds are unchanged by projecting, for every case" {
              let nn (v: 'T) : obj = box v |> Unchecked.nonNull
              let child = realTree

              let cases: (TreeOp<Msg> * string * NodeId list) list =
                  [ TreeOp.EditNode(NodeId "k", realTree.Kind), "EditNode", [ NodeId "k" ]
                    TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y")), "UpdateProp", [ NodeId "k" ]
                    TreeOp.ReplaceBinding(NodeId "k", "Source", Binding.Static(Some(nn 2.0))),
                    "ReplaceBinding",
                    [ NodeId "k" ]
                    TreeOp.UpdateStyle(NodeId "k", Fuaran.UI.Defaults.style), "UpdateStyle", [ NodeId "k" ]
                    TreeOp.UpdateState(NodeId "k", Fuaran.UI.Defaults.stateBehaviour<Msg>),
                    "UpdateState",
                    [ NodeId "k" ]
                    TreeOp.InsertChild(NodeId "p", child), "InsertChild", [ NodeId "p" ]
                    TreeOp.RemoveNode(NodeId "k"), "RemoveNode", [ NodeId "k" ]
                    TreeOp.MoveNode(NodeId "k", NodeId "p"), "MoveNode", [ NodeId "k"; NodeId "p" ]
                    TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ]),
                    "ReorderChildren",
                    [ NodeId "p"; NodeId "b"; NodeId "a" ]
                    TreeOp.ReplaceRoot child, "ReplaceRoot", [ NodeId child.Id ]
                    TreeOp.Batch
                        [ TreeOp.RemoveNode(NodeId "k")
                          TreeOp.MoveNode(NodeId "k", NodeId "p")
                          TreeOp.ReorderChildren(NodeId "p", [ NodeId "k" ]) ],
                    "Batch",
                    [ NodeId "k"; NodeId "k"; NodeId "p"; NodeId "p"; NodeId "k" ] ]

              for (op, kind, ids) in cases do
                  Expect.equal (Audit.opKindName op) kind (sprintf "opKindName %A" op)
                  Expect.equal (Audit.referencedIds op) ids (sprintf "referencedIds %A" op)
          } ]
