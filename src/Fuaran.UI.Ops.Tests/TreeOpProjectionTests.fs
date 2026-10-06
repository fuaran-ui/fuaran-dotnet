module Fuaran.UI.Tests.TreeOpProjection

// ============================================================================
//  Phase 2044 — `TreeOp.kindName`, `TreeOp.targets` and `TreeOp.footprint`.
//
//  The three projections every module that needs only an op's name, its
//  addressed ids or the ids it touches reads instead of carrying its own match.
//  The footprint is the one whose divergence was a defect: it must reach every
//  id an op puts into the tree, through every keyed position, not only the id
//  of the node it names.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

type Msg = Noop

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private leaf (id: string) : Node<Msg> = Fuaran.markdown id $"body of {id}"

let private ids (raw: string list) : Set<NodeId> = raw |> List.map NodeId |> Set.ofList

/// A subtree holding a node in an ordered list, a `Switch` case, a `Switch`
/// default, an `ErrorBoundary` fallback and a `state.onEmpty` alternative.
let private deepSubtree: Node<Msg> =
    let withState =
        { leaf "s" with
            State =
                Some
                    { OnLoading = None
                      OnEmpty = Some(leaf "s-empty")
                      OnError = None } }

    Fuaran.stack
        "top"
        { Defaults.stack<Msg> with
            Children =
                [ leaf "ordered"
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          On = Binding.State("mode", None)
                          Cases =
                              [ { Match = Some "a"
                                  When = None
                                  Child = leaf "sw-case" } ]
                          Default = leaf "sw-default" }
                  Fuaran.errorBoundary
                      "eb"
                      { Child = leaf "eb-child"
                        Fallback = leaf "eb-fallback" }
                  withState ] }

let private deepIds =
    [ "top"
      "ordered"
      "sw"
      "sw-case"
      "sw-default"
      "eb"
      "eb-child"
      "eb-fallback"
      "s"
      "s-empty" ]

/// One op of every case, with its name and its addressed ids.
let private everyCase: (TreeOp<Msg> * string * NodeId list) list =
    [ TreeOp.EditNode(NodeId "k", (leaf "x").Kind), "EditNode", [ NodeId "k" ]
      TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y")), "UpdateProp", [ NodeId "k" ]
      TreeOp.ReplaceBinding(NodeId "k", "Source", Binding.Static(Some(nn 2.0))), "ReplaceBinding", [ NodeId "k" ]
      TreeOp.UpdateStyle(NodeId "k", Defaults.style), "UpdateStyle", [ NodeId "k" ]
      TreeOp.UpdateState(NodeId "k", Defaults.stateBehaviour<Msg>), "UpdateState", [ NodeId "k" ]
      TreeOp.InsertChild(NodeId "p", leaf "c"), "InsertChild", [ NodeId "p" ]
      TreeOp.RemoveNode(NodeId "k"), "RemoveNode", [ NodeId "k" ]
      TreeOp.MoveNode(NodeId "k", NodeId "p"), "MoveNode", [ NodeId "k"; NodeId "p" ]
      TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ]), "ReorderChildren", [ NodeId "p" ]
      TreeOp.ReplaceRoot(leaf "r"), "ReplaceRoot", [ NodeId "r" ]
      TreeOp.Batch [ TreeOp.RemoveNode(NodeId "k"); TreeOp.MoveNode(NodeId "k", NodeId "p") ],
      "Batch",
      [ NodeId "k"; NodeId "p" ] ]

[<Tests>]
let tests =
    testList
        "TreeOp projections (Phase 2044)"
        [ test "kindName names every case by its wire spelling" {
              for (op, name, _) in everyCase do
                  Expect.equal (TreeOp.kindName op) name (sprintf "kindName %A" op)
          }

          test "targets lists the addressed ids, in order, each once" {
              for (op, _, addressed) in everyCase do
                  Expect.equal (TreeOp.targets op) addressed (sprintf "targets %A" op)
          }

          test "the footprint contains the targets for every case" {
              for (op, _, addressed) in everyCase do
                  Expect.isTrue
                      (Set.isSubset (Set.ofList addressed) (TreeOp.footprint op))
                      (sprintf "footprint ⊇ targets for %A" op)
          }

          test "an in-place op's footprint is exactly its targets" {
              let ops: TreeOp<Msg> list =
                  [ TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y"))
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style)
                    TreeOp.RemoveNode(NodeId "k")
                    TreeOp.MoveNode(NodeId "k", NodeId "p") ]

              for op in ops do
                  Expect.equal (TreeOp.footprint op) (Set.ofList (TreeOp.targets op)) (sprintf "%A" op)
          }

          test "an insert's footprint reaches every id of its subtree, through every keyed position" {
              Expect.equal
                  (TreeOp.footprint (TreeOp.InsertChild(NodeId "p", deepSubtree)))
                  (ids ("p" :: deepIds))
                  "parent plus the whole inserted subtree"
          }

          test "a replacement root's footprint is its whole tree" {
              Expect.equal (TreeOp.footprint (TreeOp.ReplaceRoot deepSubtree)) (ids deepIds) "the whole new tree"
          }

          test "an EditNode's footprint reaches the nodes its new kind holds" {
              Expect.equal
                  (TreeOp.footprint (TreeOp.EditNode(NodeId "k", deepSubtree.Kind)))
                  (ids ("k" :: List.tail deepIds))
                  "the edited id plus everything the new kind holds"
          }

          test "an UpdateState's footprint reaches the alternatives it installs" {
              let state: StateBehaviour<Msg> =
                  { OnLoading = Some(leaf "loading")
                    OnEmpty = Some deepSubtree
                    OnError = None }

              Expect.equal
                  (TreeOp.footprint (TreeOp.UpdateState(NodeId "k", state)))
                  (ids ("k" :: "loading" :: deepIds))
                  "the addressed id plus both alternatives' subtrees"
          }

          test "a reorder's footprint includes its newOrder" {
              Expect.equal
                  (TreeOp.footprint (TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ])))
                  (ids [ "p"; "a"; "b" ])
                  "parent plus the permuted children"
          }

          test "a batch's footprint is the union of its members'" {
              Expect.equal
                  (TreeOp.footprint (
                      TreeOp.Batch [ TreeOp.InsertChild(NodeId "p", leaf "c"); TreeOp.RemoveNode(NodeId "k") ]
                  ))
                  (ids [ "p"; "c"; "k" ])
                  "members unioned"
          } ]
