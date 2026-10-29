module Fuaran.UI.Tests.TreeOpProjection

// ============================================================================
//  Phase 2044 — `TreeOp.kindName`, `TreeOp.targets`, `TreeOp.inserted`, and
//  the footprint, which is `TreeOpFootprint.ofOp` since Phase 2292.
//
//  The projections every module that needs only an op's name, its addressed
//  ids or the ids it touches reads instead of carrying its own match. The
//  footprint is the one whose divergence was a defect: it must reach every id
//  an op puts into the tree, through every keyed position, not only the id of
//  the node it names. `touched` reads the shipped Core footprint as the id set
//  it addresses, so these facts are asserted about the projection a consumer
//  gets.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

type Msg = Noop

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private leaf (id: string) : Node<Msg> = Fuaran.markdown id $"body of {id}"

let private rootIds (nodes: Node<Msg> list) : string list = nodes |> List.map _.Id

let private ids (raw: string list) : Set<NodeId> = raw |> List.map NodeId |> Set.ofList

/// Every id the op's shipped footprint names, in any of its four node sets.
let private touched (op: TreeOp<Msg>) : Set<NodeId> =
    let f = TreeOpFootprint.ofOp op

    Set.unionMany [ f.Reads; f.ContentWrites; f.StructureWrites; f.UnknownParentWrites ]
    |> Set.map NodeId

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
      TreeOp.PatchStyle(
          NodeId "k",
          { StyleDelta.empty with
              Tone = Some ToneVariant.Brand }
      ),
      "PatchStyle",
      [ NodeId "k" ]
      TreeOp.UpdateState(NodeId "k", Defaults.stateBehaviour<Msg>), "UpdateState", [ NodeId "k" ]
      TreeOp.InsertChild(NodeId "p", leaf "c"), "InsertChild", [ NodeId "p" ]
      TreeOp.RemoveNode(NodeId "k"), "RemoveNode", [ NodeId "k" ]
      TreeOp.MoveNode(NodeId "k", NodeId "p"), "MoveNode", [ NodeId "k"; NodeId "p" ]
      TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ]), "ReorderChildren", [ NodeId "p" ]
      TreeOp.ReplaceRoot(leaf "r"), "ReplaceRoot", [ NodeId "r" ]
      TreeOp.Batch [ TreeOp.RemoveNode(NodeId "k"); TreeOp.MoveNode(NodeId "k", NodeId "p") ],
      "Batch",
      [ NodeId "k"; NodeId "p" ] ]

// ---- Phase 2327: the base-aware form ----

/// root [ p1 [ k ]; p2 [ q ] ] — two in-place targets under different parents.
let private inPlaceBase: Node<Msg> =
    Fuaran.box
        "root"
        { Defaults.box<Msg> with
            Children =
                [ Fuaran.box
                      "p1"
                      { Defaults.box<Msg> with
                          Children = [ leaf "k" ] }
                  Fuaran.box
                      "p2"
                      { Defaults.box<Msg> with
                          Children = [ leaf "q" ] } ] }

let private tone (id: string) : TreeOp<Msg> =
    TreeOp.PatchStyle(
        NodeId id,
        { StyleDelta.empty with
            Tone = Some ToneVariant.Brand }
    )

let private voice (id: string) : TreeOp<Msg> =
    TreeOp.PatchStyle(
        NodeId id,
        { StyleDelta.empty with
            Voice = Some FontVoice.Display }
    )

/// Whether the two ops, each read at `state`, are independent under Core.
let private independentAt (state: Node<Msg>) (a: TreeOp<Msg>) (b: TreeOp<Msg>) : bool =
    Fuaran.Core.Ops.independent (TreeOpFootprint.ofOpIn state a) (TreeOpFootprint.ofOpIn state b)

let private bothOrdersApply (state: Node<Msg>) (a: TreeOp<Msg>) (b: TreeOp<Msg>) : bool =
    let run x y =
        Apply.apply x state |> Result.bind (Apply.apply y) |> Result.isOk

    run a b && run b a

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
                      (Set.isSubset (Set.ofList addressed) (touched op))
                      (sprintf "footprint ⊇ targets for %A" op)
          }

          test "an in-place op's footprint is exactly its targets" {
              let ops: TreeOp<Msg> list =
                  [ TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y"))
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style)
                    TreeOp.PatchStyle(
                        NodeId "k",
                        { StyleDelta.empty with
                            Tone = Some ToneVariant.Brand }
                    )
                    TreeOp.RemoveNode(NodeId "k")
                    TreeOp.MoveNode(NodeId "k", NodeId "p") ]

              for op in ops do
                  Expect.equal (touched op) (Set.ofList (TreeOp.targets op)) (sprintf "%A" op)
          }

          test "an insert's footprint reaches every id of its subtree, through every keyed position" {
              Expect.equal
                  (touched (TreeOp.InsertChild(NodeId "p", deepSubtree)))
                  (ids ("p" :: deepIds))
                  "parent plus the whole inserted subtree"
          }

          test "a replacement root's footprint is its whole tree" {
              Expect.equal (touched (TreeOp.ReplaceRoot deepSubtree)) (ids deepIds) "the whole new tree"
          }

          test "an EditNode's footprint reaches the nodes its new kind holds" {
              Expect.equal
                  (touched (TreeOp.EditNode(NodeId "k", deepSubtree.Kind)))
                  (ids ("k" :: List.tail deepIds))
                  "the edited id plus everything the new kind holds"
          }

          test "an UpdateState's footprint reaches the alternatives it installs" {
              let state: StateBehaviour<Msg> =
                  { OnLoading = Some(leaf "loading")
                    OnEmpty = Some deepSubtree
                    OnError = None }

              Expect.equal
                  (touched (TreeOp.UpdateState(NodeId "k", state)))
                  (ids ("k" :: "loading" :: deepIds))
                  "the addressed id plus both alternatives' subtrees"
          }

          test "a reorder's footprint includes its newOrder" {
              Expect.equal
                  (touched (TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ])))
                  (ids [ "p"; "a"; "b" ])
                  "parent plus the permuted children"
          }

          test "a batch's footprint is the union of its members'" {
              Expect.equal
                  (touched (TreeOp.Batch [ TreeOp.InsertChild(NodeId "p", leaf "c"); TreeOp.RemoveNode(NodeId "k") ]))
                  (ids [ "p"; "c"; "k" ])
                  "members unioned"
          }

          // Phase 2292 — the shipped projection's verdicts.
          test "a vertical op is Core's UpdateNode footprint of its node, with no slot" {
              let vertical: TreeOp<Msg> list =
                  [ TreeOp.EditNode(NodeId "k", (leaf "x").Kind)
                    TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y"))
                    TreeOp.ReplaceBinding(NodeId "k", "Source", Binding.Static(Some(nn 2.0)))
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style)
                    TreeOp.PatchStyle(
                        NodeId "k",
                        { StyleDelta.empty with
                            Tone = Some ToneVariant.Brand }
                    )
                    TreeOp.UpdateState(NodeId "k", Defaults.stateBehaviour<Msg>) ]

              let expected =
                  { Fuaran.Core.Footprint.contentEdit "k" with
                      UnknownParentWrites = Set.singleton "k" }

              for op in vertical do
                  Expect.equal (TreeOpFootprint.ofOp op) expected (sprintf "%A" op)
          }

          test "a vertical op is independent of no non-empty tier op, and of the empty batch" {
              // Phase 2004's unknown-parent write collides with every structural or unknown-parent
              // write, and every non-empty TreeOp makes one. So a field-level restyle of `q` is
              // ordered against every case — a kind edit or another restyle of `q` included — and
              // only the empty batch commutes with it. Red the day the pinned Core frees a pair.
              let restyle =
                  TreeOpFootprint.ofOp (
                      TreeOp.PatchStyle(
                          NodeId "q",
                          { StyleDelta.empty with
                              Voice = Some FontVoice.Display }
                      )
                  )

              let sameNode: TreeOp<Msg> list =
                  [ TreeOp.EditNode(NodeId "q", (leaf "x").Kind)
                    TreeOp.PatchStyle(
                        NodeId "q",
                        { StyleDelta.empty with
                            Tone = Some ToneVariant.Brand }
                    ) ]

              for op in (everyCase |> List.map (fun (op, _, _) -> op)) @ sameNode do
                  Expect.isFalse
                      (Fuaran.Core.Ops.independent restyle (TreeOpFootprint.ofOp op))
                      (sprintf "a restyle of q is ordered against %A" op)

              Expect.isTrue
                  (Fuaran.Core.Ops.independent restyle (TreeOpFootprint.ofOp (TreeOp.Batch [])))
                  "the empty batch writes nothing"
          }

          test "ReplaceRoot has a footprint, ordered against every non-empty tier op" {
              let replace = TreeOpFootprint.ofOp (TreeOp.ReplaceRoot deepSubtree)

              for op, _, _ in everyCase do
                  Expect.isFalse
                      (Fuaran.Core.Ops.independent replace (TreeOpFootprint.ofOp op))
                      (sprintf "ReplaceRoot is ordered against %A" op)

              Expect.isTrue
                  (Fuaran.Core.Ops.independent replace (TreeOpFootprint.ofOp (TreeOp.Batch [])))
                  "the empty batch writes nothing"
          }

          test "two inserts under different named parents are independent" {
              // The fold class the tier keeps: neither writes a parent it cannot name.
              Expect.isTrue
                  (Fuaran.Core.Ops.independent
                      (TreeOpFootprint.ofOp (TreeOp.InsertChild(NodeId "p", leaf "c")))
                      (TreeOpFootprint.ofOp (TreeOp.InsertChild(NodeId "r", leaf "d"))))
                  "disjoint parents, disjoint ids"
          }

          // Phase 2141 — `inserted`, the nodes an op puts in.
          test "inserted names the subtree roots each op puts into the tree" {
              let state: StateBehaviour<Msg> =
                  { OnLoading = Some(leaf "loading")
                    OnEmpty = Some(leaf "empty")
                    OnError = None }

              Expect.equal (rootIds (TreeOp.inserted (TreeOp.InsertChild(NodeId "p", leaf "c")))) [ "c" ] "the child"
              Expect.equal (rootIds (TreeOp.inserted (TreeOp.ReplaceRoot(leaf "r")))) [ "r" ] "the new root"

              Expect.equal
                  (rootIds (TreeOp.inserted (TreeOp.UpdateState(NodeId "k", state))))
                  [ "loading"; "empty" ]
                  "both alternatives, in order"

              Expect.equal
                  (rootIds (TreeOp.inserted (TreeOp.EditNode(NodeId "k", deepSubtree.Kind))))
                  (rootIds (NodeChildren.children NodeChildren.Reach.keyed deepSubtree))
                  "the nodes the new kind holds"

              let batch =
                  TreeOp.Batch [ TreeOp.InsertChild(NodeId "p", leaf "a"); TreeOp.ReplaceRoot(leaf "b") ]

              Expect.equal (rootIds (TreeOp.inserted batch)) [ "a"; "b" ] "members, in order"
          }

          test "inserted is empty for every op that carries no node" {
              let ops: TreeOp<Msg> list =
                  [ TreeOp.EditNode(NodeId "k", (leaf "x").Kind)
                    TreeOp.UpdateProp(NodeId "k", "Label", PropValue.Native(nn "y"))
                    TreeOp.ReplaceBinding(NodeId "k", "Source", Binding.Static(Some(nn 2.0)))
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style)
                    TreeOp.PatchStyle(
                        NodeId "k",
                        { StyleDelta.empty with
                            Tone = Some ToneVariant.Brand }
                    )
                    TreeOp.UpdateState(NodeId "k", Defaults.stateBehaviour<Msg>)
                    TreeOp.RemoveNode(NodeId "k")
                    TreeOp.MoveNode(NodeId "k", NodeId "p")
                    TreeOp.ReorderChildren(NodeId "p", [ NodeId "b"; NodeId "a" ]) ]

              for op in ops do
                  Expect.isEmpty (TreeOp.inserted op) (sprintf "%A" op)
          }

          test "inserted counts nodes where repeated ids would collapse the footprint" {
              let twice =
                  Fuaran.stack
                      "x"
                      { Defaults.stack<Msg> with
                          Children = [ leaf "x" ] }

              let op = TreeOp.ReplaceRoot twice
              Expect.equal (touched op) (ids [ "x" ]) "the id set collapses to the target"
              Expect.equal (rootIds (TreeOp.inserted op)) [ "x" ] "the node list keeps its one root"

              Expect.equal
                  (List.length (NodeChildren.children NodeChildren.Reach.keyed twice))
                  1
                  "and that root holds a second node"
          }

          // Phase 2327 — the base-aware form, at the tree the op applies to.
          test "base-aware: a kind edit and a restyle of one node are independent" {
              let edit = TreeOp.EditNode(NodeId "k", (leaf "k").Kind)

              Expect.isTrue (independentAt inPlaceBase edit (tone "k")) "kind beside style"
              Expect.isTrue (bothOrdersApply inPlaceBase edit (tone "k")) "and both orders apply"
          }

          test "base-aware: a state edit and a prop edit of one node are independent" {
              let state =
                  TreeOp.UpdateState(
                      NodeId "k",
                      { Defaults.stateBehaviour<Msg> with
                          OnEmpty = Some(leaf "k-empty") }
                  )

              let prop = TreeOp.UpdateProp(NodeId "k", "Text", PropValue.Native(nn "edited"))

              Expect.isTrue (independentAt inPlaceBase state prop) "state beside prop"
              Expect.isTrue (bothOrdersApply inPlaceBase state prop) "and both orders apply"
          }

          test "base-aware: two in-place edits under different parents are independent" {
              let edit = TreeOp.EditNode(NodeId "q", (leaf "q").Kind)

              Expect.isTrue (independentAt inPlaceBase (tone "k") edit) "k under p1, q under p2"
              Expect.isTrue (independentAt inPlaceBase (tone "k") (voice "k")) "two fields of one style"
          }

          test "base-aware: an in-place edit beside the removal of its ancestor still interferes" {
              for edit in
                  [ tone "k"
                    TreeOp.EditNode(NodeId "k", (leaf "k").Kind)
                    TreeOp.UpdateProp(NodeId "k", "Text", PropValue.Native(nn "edited")) ] do
                  Expect.isFalse
                      (independentAt inPlaceBase (TreeOp.RemoveNode(NodeId "p1")) edit)
                      (sprintf "removing p1 orders %A" edit)

              // the parent's kind rewritten takes k out with it
              Expect.isFalse
                  (independentAt inPlaceBase (TreeOp.EditNode(NodeId "p1", (leaf "p1").Kind)) (tone "k"))
                  "a kind edit of the parent takes the child out"

              Expect.isFalse
                  (independentAt inPlaceBase (TreeOp.ReplaceRoot(leaf "other")) (tone "k"))
                  "a replaced root takes every node out"
          }

          test "base-aware: one slot is ordered against itself and against its whole" {
              let prop (v: string) =
                  TreeOp.UpdateProp(NodeId "k", "Text", PropValue.Native(nn v))

              Expect.isFalse (independentAt inPlaceBase (prop "a") (prop "b")) "two prop edits: the kind slot"
              Expect.isFalse (independentAt inPlaceBase (tone "k") (tone "k")) "one style field twice"

              Expect.isFalse
                  (independentAt inPlaceBase (TreeOp.UpdateStyle(NodeId "k", Defaults.style)) (tone "k"))
                  "a whole restyle against a field"
          }

          test "base-aware: a target the walk does not locate takes the pure arm" {
              let absent = tone "ghost"
              Expect.equal (TreeOpFootprint.ofOpIn inPlaceBase absent) (TreeOpFootprint.ofOp absent) "absent"

              // A node held only in a keyed position (a `state` alternative) is not located either.
              let held =
                  TreeOp.UpdateState(
                      NodeId "k",
                      { Defaults.stateBehaviour<Msg> with
                          OnEmpty = Some(leaf "k-empty") }
                  )

              let after =
                  match Apply.apply held inPlaceBase with
                  | Ok t -> t
                  | Error e -> failtestf "%A" e

              Expect.equal
                  (TreeOpFootprint.ofOpIn after (tone "k-empty"))
                  (TreeOpFootprint.ofOp (tone "k-empty"))
                  "keyed-only"
          }

          test "base-aware: a structural op is Core's footprintIn, and a script is threaded" {
              let remove = TreeOp.RemoveNode(NodeId "p1")
              let f = TreeOpFootprint.ofOpIn inPlaceBase remove

              Expect.isEmpty f.UnknownParentWrites "the parent is named"
              Expect.equal f.StructureWrites (Set.singleton "root") "the parent's child list"
              Expect.isTrue (f.ContentWrites.Contains "k") "the destroyed subtree"

              // an op on a node an earlier op of the script put in is located at the threaded tree
              let script = [ TreeOp.InsertChild(NodeId "p2", leaf "n"); tone "n" ]
              let each = TreeOpFootprint.ofEachIn inPlaceBase script

              Expect.isEmpty (List.item 1 each).UnknownParentWrites "located after the insert"

              Expect.equal
                  (TreeOpFootprint.ofScriptIn inPlaceBase script)
                  (List.reduce Fuaran.Core.Footprint.union each)
                  "unioned"
          } ]
