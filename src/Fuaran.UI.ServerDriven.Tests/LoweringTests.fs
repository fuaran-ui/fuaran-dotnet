module Fuaran.UI.ServerDriven.Tests.LoweringTests

// ─── Phase 152 Track B: TreeOp → DomPatch lowering ──────────────
//
// The lowering joins the Track-A diff to the Track-B wire. A stub
// `renderFragment` (id → "<f id='…'/>") stands in for the host's
// Renderer.Server-backed renderer, so these assert the op→patch mapping
// without a real HTML renderer; the diff→lower pipeline test ties the
// two tracks end to end.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven

/// Stub renderer — emits a marker carrying the node id so tests can assert
/// "the right node was rendered" without a real HTML renderer.
let private stubRender (n: Node<obj>) : string =
    let (NodeId s) = n.Id
    $"<f id='{s}'/>"

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<obj> with
            Children = children }

let private tree = dash [ Fuaran.markdown "m" "hello"; Fuaran.markdown "n" "world" ]

[<Tests>]
let tests =
    testList
        "Lowering (TreeOp → DomPatch)"
        [ test "structural ops lower directly (no render)" {
              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.RemoveNode(NodeId "m") ])
                  [ DomPatch.RemoveNode "m" ]
                  "RemoveNode"

              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "n"; NodeId "m" ]) ])
                  [ DomPatch.ReorderChildren("dash", [ "n"; "m" ]) ]
                  "ReorderChildren maps node ids to raw strings"

              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.MoveNode(NodeId "m", NodeId "other", 2) ])
                  [ DomPatch.MoveNode("m", "other", 2) ]
                  "MoveNode → identity-preserving DomPatch.MoveNode"
          }

          test "InsertChild renders the inserted subtree into an InsertFragment" {
              let child = Fuaran.markdown "fresh" "new"

              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.InsertChild(NodeId "dash", 1, child) ])
                  [ DomPatch.InsertFragment("dash", 1, "<f id='fresh'/>") ]
                  "InsertChild → InsertFragment with the rendered child"
          }

          test "content ops re-render the changed node into a ReplaceFragment" {
              // EditNode / UpdateProp / UpdateStyle / UpdateState / ReplaceBinding
              // all look the node up in the new tree and re-render it.
              let updated = dash [ Fuaran.markdown "m" "CHANGED"; Fuaran.markdown "n" "world" ]

              Expect.equal
                  (Lowering.lower
                      stubRender
                      updated
                      [ TreeOp.UpdateProp(
                            NodeId "m",
                            "Text",
                            PropValue.Native(Unchecked.nonNull (box (TextSource.Literal "CHANGED")))
                        ) ])
                  [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ]
                  "UpdateProp → ReplaceFragment of the changed node (rendered from the new tree)"
          }

          test "a content op whose node is absent from the new tree yields nothing" {
              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.UpdateStyle(NodeId "ghost", Defaults.style) ])
                  []
                  "no node, no patch"
          }

          test "Batch flattens to the concatenated lowering of its inner ops" {
              let ops =
                  [ TreeOp.Batch [ TreeOp.RemoveNode(NodeId "m"); TreeOp.MoveNode(NodeId "n", NodeId "dash", 0) ] ]

              Expect.equal
                  (Lowering.lower stubRender tree ops)
                  [ DomPatch.RemoveNode "m"; DomPatch.MoveNode("n", "dash", 0) ]
                  "Batch is flattened in order"
          }

          test "end-to-end: diff → lower produces a targeted patch for a leaf text change" {
              let a = dash [ Fuaran.markdown "m" "before" ]
              let b = dash [ Fuaran.markdown "m" "after" ]

              let patches = TreeOpDiff.diff a b |> Lowering.lower stubRender b

              // The diff localises to the changed leaf (UpdateProp Text); the
              // lowering re-renders just that node.
              Expect.equal
                  patches
                  [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ]
                  "one targeted ReplaceFragment for the changed leaf"
          } ]
