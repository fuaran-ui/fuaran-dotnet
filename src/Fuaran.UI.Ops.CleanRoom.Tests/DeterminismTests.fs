module Fuaran.UI.Ops.CleanRoom.Tests.DeterminismTests

// ============================================================================
//  Acceptance: brokered ops applied to the real tree re-derive the same
//  canonical-JSON document as the content-reattach round-trip — the proof
//  that the ops are content-position-independent. Routes through
//  CanonicalJson (the determinism oracle both sides re-derive).
//
//  The shape: apply the SAME structural op to (a) the real tree and (b) a
//  content-free stand-in (same ids/structure, placeholder content); then
//  re-attach the real content onto the rearranged stand-in by NodeId. If the
//  op is content-position-independent, the re-attached tree is byte-identical
//  (canonical JSON) to the directly-applied real tree.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Ops.CleanRoom
open Fuaran.UI.Ops.CleanRoom.Broker
open Fuaran.UI.Ops.CleanRoom.Tests.Fixtures

let private sk = Skeleton.project realTree

/// Assert the op is released by the broker, then prove content-position
/// independence via the re-attach round-trip through CanonicalJson.
let private proveDeterministic (label: string) (op: TreeOp<Msg>) =
    match Broker.enforce sk op with
    | StructuralGateDecision.Withheld r ->
        failtestf "%s: expected the broker to release the op, got Withheld %s" label r
    | StructuralGateDecision.Released _ ->
        let realAfter = applyOk op realTree
        let standInAfter = applyOk op standInTree
        let reattached = reattach (nodesById realTree) standInAfter

        Expect.equal
            (CanonicalJson.encodeNode realAfter)
            (CanonicalJson.encodeNode reattached)
            (sprintf "%s: brokered op re-derives the same canonical-JSON document" label)

        // Sanity: the stand-in genuinely carried different content, so the
        // re-attach actually did work (the test isn't vacuously true).
        Expect.notEqual
            (CanonicalJson.encodeNode realAfter)
            (CanonicalJson.encodeNode standInAfter)
            (sprintf "%s: the stand-in's placeholder content differs from the real document" label)

[<Tests>]
let tests =
    testList
        "structure-only clean room — determinism proof"
        [ test "ReorderChildren re-derives the real document" {
              proveDeterministic
                  "reorder"
                  (TreeOp.ReorderChildren(
                      NodeId "doc-root",
                      [ NodeId "clause-2"
                        NodeId "recital"
                        NodeId "clause-1"
                        NodeId "headline-metric" ]
                  ))
          }

          test "MoveNode re-derives the real document" {
              proveDeterministic "move" (TreeOp.MoveNode(NodeId "clause-1", NodeId "clause-2", 0))
          }

          test "RemoveNode re-derives the real document" {
              proveDeterministic "remove" (TreeOp.RemoveNode(NodeId "recital"))
          }

          test "a Batch of structural ops re-derives the real document" {
              proveDeterministic
                  "batch"
                  (TreeOp.Batch
                      [ TreeOp.RemoveNode(NodeId "recital")
                        TreeOp.MoveNode(NodeId "clause-1", NodeId "clause-2", 0) ])
          } ]
