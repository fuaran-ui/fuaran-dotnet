module Fuaran.UI.Telemetry.Tests.OpKindTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  OpKind projection — case-name mapping from TreeOp to flat discriminator.
//
//  `OpKind.ofTreeOp` is exhaustive across the ten §4g cases; the build
//  catches an unhandled case via `TreatWarningsAsErrors`. These tests
//  pin the explicit mapping so a future case-rename or case-reorder is
//  caught here too, not just at the apply engine.
// ============================================================================

type private Msg = | Tick

// F# 10 nullness types `box _` as `obj | null`. The boxed payloads handed to
// PropValue.Native / Binding<obj> payloads are always non-null in these tests; wrap via
// `Unchecked.nonNull` to satisfy the non-null obj signature. Same shape as
// Fuaran.UI.Ops.Tests/OpsApplyTests.fs::nn.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private metric =
    Fuaran.metric
        "k"
        { Defaults.metric with
            Label = TextSource.Literal "x"
            Value = Binding.Static 1.0 }

let private leafChild = metric

[<Tests>]
let tests =
    testList
        "OpKind projection"
        [ test "OpKind.ofTreeOp covers every TreeOp case" {
              let cases: (TreeOp<Msg> * OpKind) list =
                  [ TreeOp.EditNode(NodeId "k", metric.Kind), OpKind.EditNode
                    TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y")), OpKind.UpdateProp
                    TreeOp.ReplaceBinding(NodeId "k", "Source", Binding.Static(nn 2.0)), OpKind.ReplaceBinding
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style), OpKind.UpdateStyle
                    TreeOp.UpdateState(NodeId "k", Defaults.stateBehaviour<Msg>), OpKind.UpdateState
                    TreeOp.InsertChild(NodeId "p", leafChild), OpKind.InsertChild
                    TreeOp.RemoveNode(NodeId "k"), OpKind.RemoveNode
                    TreeOp.MoveNode(NodeId "k", NodeId "p"), OpKind.MoveNode
                    TreeOp.ReorderChildren(NodeId "p", [ NodeId "k" ]), OpKind.ReorderChildren
                    TreeOp.Batch [], OpKind.Batch ]

              for (op, expected) in cases do
                  Expect.equal
                      (OpKind.ofTreeOp op)
                      expected
                      (sprintf "OpKind.ofTreeOp must map %A to %s" op (OpKind.name expected))
          }

          test "OpKind.name renders the canonical discriminator for every case" {
              let pairs =
                  [ OpKind.EditNode, "EditNode"
                    OpKind.UpdateProp, "UpdateProp"
                    OpKind.ReplaceBinding, "ReplaceBinding"
                    OpKind.UpdateStyle, "UpdateStyle"
                    OpKind.UpdateState, "UpdateState"
                    OpKind.InsertChild, "InsertChild"
                    OpKind.RemoveNode, "RemoveNode"
                    OpKind.MoveNode, "MoveNode"
                    OpKind.ReorderChildren, "ReorderChildren"
                    OpKind.Batch, "Batch" ]

              for (kind, expected) in pairs do
                  Expect.equal (OpKind.name kind) expected (sprintf "OpKind.name %A" kind)
          } ]
