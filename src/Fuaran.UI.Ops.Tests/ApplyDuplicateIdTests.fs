module Fuaran.UI.Tests.ApplyDuplicateIds

// ============================================================================
//  Apply refuses a tree that would hold duplicate node ids (WIRE_FORMAT §8.1).
//
//  Every op addresses its target by NodeId alone, so a tree that repeats one
//  makes every later id-addressed op ambiguous. The decoder judges shape and
//  accepts a repeated id; the pre-emit validator sees one only on a finished
//  tree. Before Phase 2172 an apply could BUILD such a tree from parts that each
//  decoded cleanly: a `ReplaceRoot` whose payload repeats an id, an `EditNode`
//  or `UpdateState` whose new nodes collide with the rest of the tree.
//
//  The check reads the op's RESULT and charges the op only for ids it
//  installed: an id the op put in that the result holds more than once is
//  refused with `DuplicateNodeId`. A duplicate already present before the op
//  is not that op's, exactly as a tree already over a limit is not refused for
//  an op that did not grow it.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

let private box (nodeId: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.dashboard
        nodeId
        { Defaults.dashboard<unit> with
            Children = children }

let private leaf (nodeId: string) : Node<unit> = box nodeId []

let private boxKindHolding (children: Node<unit> list) : NodeKind<unit> = (box "carrier" children).Kind

let private loadingWith (node: Node<unit>) : StateBehaviour<unit> =
    { Defaults.stateBehaviour<unit> with
        OnLoading = Some node }

/// r [ a; b ]
let private baseTree () : Node<unit> = box "r" [ leaf "a"; leaf "b" ]

let private expectDuplicate (what: string) (result: Result<Node<unit>, ApplyError>) =
    match result with
    | Error err -> Expect.equal err.Code ApplyErrorCode.DuplicateNodeId what
    | Ok _ -> failtestf "%s: the op applied and left a duplicate id in the tree" what

let private expectApplies (what: string) (result: Result<Node<unit>, ApplyError>) =
    match result with
    | Ok _ -> ()
    | Error err -> failtestf "%s: refused with %A (%s)" what err.Code err.Message

[<Tests>]
let applyDuplicateIdTests =
    testList
        "Fuaran.UI.Ops apply refuses duplicate node ids"
        [ test "ReplaceRoot whose payload repeats an id is refused" {
              Apply.apply (TreeOp.ReplaceRoot(box "r2" [ leaf "x"; leaf "x" ])) (baseTree ())
              |> expectDuplicate "a repeated id in the replacement tree"
          }

          test "ReplaceRoot whose root id reappears below is refused" {
              Apply.apply (TreeOp.ReplaceRoot(box "r" [ leaf "r" ])) (baseTree ())
              |> expectDuplicate "the root id repeated on a descendant"
          }

          test "ReplaceRoot reusing the replaced tree's ids, each once, applies" {
              Apply.apply (TreeOp.ReplaceRoot(box "r" [ leaf "a"; leaf "b" ])) (baseTree ())
              |> expectApplies "the old tree is gone, so nothing collides"
          }

          test "EditNode whose new kind holds an id the tree already holds is refused" {
              Apply.apply (TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "b" ])) (baseTree ())
              |> expectDuplicate "a collision with a pre-existing node"
          }

          test "EditNode whose new kind repeats an id within itself is refused" {
              Apply.apply (TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "x"; leaf "x" ])) (baseTree ())
              |> expectDuplicate "a repeat within the new kind"
          }

          test "EditNode whose new kind repeats the edited node's own id is refused" {
              Apply.apply (TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "a" ])) (baseTree ())
              |> expectDuplicate "the edited node's own id below it"
          }

          test "EditNode restating the children it replaces applies" {
              let tree = box "r" [ box "a" [ leaf "c" ] ]

              Apply.apply (TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "c"; leaf "d" ])) tree
              |> expectApplies "the old child leaves as the restated one arrives"
          }

          test "UpdateState attaching an alternative whose id the tree holds is refused" {
              Apply.apply (TreeOp.UpdateState(NodeId "a", loadingWith (leaf "b"))) (baseTree ())
              |> expectDuplicate "an onLoading colliding with a pre-existing node"
          }

          test "UpdateState replacing an alternative with one of the same id applies" {
              let tree =
                  box
                      "r"
                      [ { leaf "a" with
                            State = Some(loadingWith (leaf "l")) } ]

              Apply.apply (TreeOp.UpdateState(NodeId "a", loadingWith (box "l" [ leaf "l2" ]))) tree
              |> expectApplies "the old alternative leaves as the new one arrives"
          }

          test "InsertChild whose subtree repeats an id within itself is refused" {
              Apply.apply (TreeOp.InsertChild(NodeId "r", box "c" [ leaf "x"; leaf "x" ])) (baseTree ())
              |> expectDuplicate "a repeat within the inserted subtree"
          }

          test "InsertChild whose child collides with the tree is refused" {
              Apply.apply (TreeOp.InsertChild(NodeId "r", leaf "b")) (baseTree ())
              |> expectDuplicate "a collision with a pre-existing node"
          }

          test "A duplicate the op did not install is not charged to it" {
              // The tree already holds x twice; the edit installs only w.
              let tree = box "r" [ leaf "x"; box "y" [ leaf "x" ]; leaf "z" ]

              Apply.apply (TreeOp.EditNode(NodeId "z", boxKindHolding [ leaf "w" ])) tree
              |> expectApplies "a pre-existing duplicate is not this op's to refuse"
          }

          test "A Batch whose result holds an installed id twice is refused" {
              let tree = box "r" [ leaf "a"; box "s" [ leaf "b" ] ]

              Apply.apply (TreeOp.Batch [ TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "b" ]) ]) tree
              |> expectDuplicate "the batch's result repeats b"
          }

          test "A Batch whose intermediate state duplicates but whose result does not applies" {
              let tree = box "r" [ leaf "a"; box "s" [ leaf "b" ] ]

              let batch =
                  TreeOp.Batch
                      [ TreeOp.EditNode(NodeId "a", boxKindHolding [ leaf "b" ])
                        TreeOp.EditNode(NodeId "s", boxKindHolding []) ]

              Apply.apply batch tree |> expectApplies "the check reads the batch's result"
          }

          test "An op that is over a limit AND installs a duplicate reports LimitExceeded" {
              // limitsApply's `editnode-repeated-id-past-maxdepth` pins this
              // precedence: the limit is checked first.
              let rec chain depth =
                  if depth <= 1 then
                      leaf "n1"
                  else
                      box (sprintf "n%d" depth) [ chain (depth - 1) ]

              match
                  Apply.apply (TreeOp.EditNode(NodeId "n1", boxKindHolding [ leaf "n1" ])) (chain WireLimits.MaxDepth)
              with
              | Error err -> Expect.equal err.Code ApplyErrorCode.LimitExceeded "LimitExceeded takes precedence"
              | Ok _ -> failtest "the op applied"
          }

          test "The refusal renders as the shared DuplicateNodeId token" {
              let op = TreeOp.ReplaceRoot(box "r2" [ leaf "x"; leaf "x" ])

              match Apply.apply op (baseTree ()) with
              | Error err -> Expect.stringContains (ErrorRender.render op err) "DuplicateNodeId" "the shared token"
              | Ok _ -> failtest "Expected a refusal"
          } ]
