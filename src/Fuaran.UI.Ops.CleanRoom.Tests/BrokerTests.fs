module Fuaran.UI.Ops.CleanRoom.Tests.BrokerTests

// ============================================================================
//  Acceptance: StructuralOpBroker.Enforce withholds any inbound op that
//  targets an unknown NodeId, authors / carries content, or falls off the
//  structural allowlist; permits id-referenced move / reorder / reparent /
//  delete — the withhold / release matrix.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.CleanRoom
open Fuaran.UI.Ops.CleanRoom.Broker
open Fuaran.UI.Ops.CleanRoom.Tests.Fixtures

// F# 10 nullness: `box _` types as `obj | null`; our payloads are non-null.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private sk = Skeleton.project realTree

let private decide (op: TreeOp<Msg>) = Broker.enforce sk op

let private isReleased (op: TreeOp<Msg>) =
    match decide op with
    | StructuralGateDecision.Released _ -> true
    | StructuralGateDecision.Withheld _ -> false

let private isWithheld (op: TreeOp<Msg>) = not (isReleased op)

// A throwaway node used to source a Kind / Style / State payload for the
// content-authoring ops without hand-constructing the records.
let private donor: Node<Msg> = Fuaran.markdown "donor" "donor-body"

[<Tests>]
let tests =
    testList
        "structure-only clean room — structural-op gate"
        [
          // ── Released: id-referenced rearrangements (content-free) ──
          test "RemoveNode of a known id is released" {
              Expect.isTrue (isReleased (TreeOp.RemoveNode(NodeId "recital"))) "delete by known id"
          }
          test "MoveNode between known ids is released" {
              Expect.isTrue
                  (isReleased (TreeOp.MoveNode(NodeId "clause-1", NodeId "clause-2", 0)))
                  "reparent by known ids"
          }
          test "ReorderChildren over known ids is released" {
              let op =
                  TreeOp.ReorderChildren(
                      NodeId "doc-root",
                      [ NodeId "clause-2"
                        NodeId "recital"
                        NodeId "clause-1"
                        NodeId "headline-metric" ]
                  )

              Expect.isTrue (isReleased op) "reorder a permutation of known ids"
          }
          test "Batch of allowed ops is released" {
              let op =
                  TreeOp.Batch
                      [ TreeOp.RemoveNode(NodeId "recital")
                        TreeOp.MoveNode(NodeId "clause-1", NodeId "clause-2", 0) ]

              Expect.isTrue (isReleased op) "batch of move/delete"
          }

          // ── Withheld: unknown NodeId targets ──
          test "RemoveNode of an unknown id is withheld" {
              Expect.isTrue (isWithheld (TreeOp.RemoveNode(NodeId "ghost"))) "unknown delete target"
          }
          test "MoveNode to an unknown parent is withheld" {
              Expect.isTrue (isWithheld (TreeOp.MoveNode(NodeId "clause-1", NodeId "ghost", 0))) "unknown move parent"
          }
          test "MoveNode of an unknown node is withheld" {
              Expect.isTrue (isWithheld (TreeOp.MoveNode(NodeId "ghost", NodeId "clause-2", 0))) "unknown move source"
          }
          test "ReorderChildren naming an unknown id is withheld" {
              let op = TreeOp.ReorderChildren(NodeId "doc-root", [ NodeId "ghost" ])
              Expect.isTrue (isWithheld op) "unknown id smuggled into a reorder"
          }

          // ── Withheld: content-authoring ops ──
          test "EditNode is withheld (authors a new kind/content)" {
              Expect.isTrue (isWithheld (TreeOp.EditNode(NodeId "recital", donor.Kind))) "EditNode"
          }
          test "UpdateProp is withheld (sets a field value)" {
              Expect.isTrue
                  (isWithheld (TreeOp.UpdateProp(NodeId "clause-1-title", "Text", PropValue.Native(nn "new text"))))
                  "UpdateProp"
          }
          test "ReplaceBinding is withheld (carries a value)" {
              Expect.isTrue
                  (isWithheld (TreeOp.ReplaceBinding(NodeId "headline-metric", "Source", Binding.Static(nn 1.0))))
                  "ReplaceBinding"
          }
          test "InsertChild is withheld (inserts a new content-bearing subtree)" {
              Expect.isTrue
                  (isWithheld (TreeOp.InsertChild(NodeId "doc-root", 0, Fuaran.markdown "smuggled" "secret")))
                  "InsertChild"
          }
          test "UpdateState is withheld (carries content subtrees)" {
              Expect.isTrue (isWithheld (TreeOp.UpdateState(NodeId "recital", donor.State))) "UpdateState"
          }
          test "ReplaceRoot is withheld (replaces the whole tree)" {
              Expect.isTrue (isWithheld (TreeOp.ReplaceRoot donor)) "ReplaceRoot"
          }

          // ── Withheld: off the structural allowlist (default-deny) ──
          test "UpdateStyle is withheld (not a move/reorder/reparent/delete)" {
              Expect.isTrue (isWithheld (TreeOp.UpdateStyle(NodeId "recital", donor.Style))) "UpdateStyle"
          }

          // ── Batch withholds if ANY inner op is withheld ──
          test "Batch with one content op withholds the whole batch" {
              let op =
                  TreeOp.Batch
                      [ TreeOp.RemoveNode(NodeId "recital")
                        TreeOp.EditNode(NodeId "clause-1-title", donor.Kind) ]

              Expect.isTrue (isWithheld op) "one bad inner op poisons the batch"
          }
          test "Batch with one unknown-id op withholds the whole batch" {
              let op =
                  TreeOp.Batch [ TreeOp.RemoveNode(NodeId "recital"); TreeOp.RemoveNode(NodeId "ghost") ]

              Expect.isTrue (isWithheld op) "one unknown-id inner op poisons the batch"
          }

          // ── The substitutable seam returns the same decision ──
          test "IStructuralOpBroker.Enforce mirrors the pure function" {
              let broker = StructuralOpBroker.create ()

              match broker.Enforce(sk, TreeOp.RemoveNode(NodeId "recital")) with
              | StructuralGateDecision.Released _ -> ()
              | StructuralGateDecision.Withheld r -> failtestf "expected Released via the seam, got Withheld %s" r

              match broker.Enforce(sk, TreeOp.RemoveNode(NodeId "ghost")) with
              | StructuralGateDecision.Withheld _ -> ()
              | StructuralGateDecision.Released _ -> failtest "expected Withheld via the seam"
          } ]
