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
      TreeOp.UpdateAccessibility(NodeId "k", Defaults.Accessibility.empty), "UpdateAccessibility", [ NodeId "k" ]
      TreeOp.UpdateTooltip(NodeId "k", None), "UpdateTooltip", [ NodeId "k" ]
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

/// Whether the two ops reach one tree in both orders, or are refused alike: an
/// independent verdict is a promise of exactly this.
let private commuteAt (state: Node<Msg>) (a: TreeOp<Msg>) (b: TreeOp<Msg>) : bool =
    let run x y =
        Apply.apply x state
        |> Result.bind (Apply.apply y)
        |> Result.map Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode
        |> Result.toOption

    run a b = run b a

/// root [ a [ x [] ]; b [ y [] ]; c [ k ] ] — two containers under different parents that can
/// be moved into each other, and a container with one child.
let private crossBase: Node<Msg> =
    let box (id: string) (kids: Node<Msg> list) =
        Fuaran.box
            id
            { Defaults.box<Msg> with
                Children = kids }

    box "root" [ box "a" [ box "x" [] ]; box "b" [ box "y" [] ]; box "c" [ leaf "k" ] ]

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

              // Phase 2527's trait ops are slots of their own: ordered against themselves,
              // free against the kind and style slots and against each other.
              let describe (v: string) =
                  TreeOp.UpdateAccessibility(
                      NodeId "k",
                      { Defaults.Accessibility.empty with
                          DescribedBy = Some v }
                  )

              let hint (v: string) =
                  TreeOp.UpdateTooltip(NodeId "k", Some(TextSource.Literal v))

              Expect.isFalse (independentAt inPlaceBase (describe "a") (describe "b")) "two accessibility edits"
              Expect.isFalse (independentAt inPlaceBase (hint "a") (hint "b")) "two tooltip edits"
              Expect.isTrue (independentAt inPlaceBase (describe "a") (hint "b")) "accessibility beside tooltip"
              Expect.isTrue (independentAt inPlaceBase (describe "a") (prop "b")) "accessibility beside the kind slot"
              Expect.isTrue (independentAt inPlaceBase (hint "a") (tone "k")) "tooltip beside a style field"
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
          }

          test "base-aware: a move writes its source parent's kind slot, never the whole parent (Phase 2651)" {
              let move = TreeOp.MoveNode(NodeId "k", NodeId "p2")
              let f = TreeOpFootprint.ofOpIn inPlaceBase move

              Expect.isEmpty f.UnknownParentWrites "the source parent is resolved"
              Expect.equal f.StructureWrites (Set.singleton "p2") "only the destination is a named structure write"
              Expect.isFalse (f.Reads.Contains "p1") "the source parent is not read whole"
              Expect.isFalse (f.ContentWrites.Contains "p1") "nor content-written"
              Expect.isEmpty f.ContentWrites "nothing is content-written (Phase 2670)"
              Expect.isTrue (f.SlotWrites.Contains("k", "parent")) "the moved node, at its parent slot"
              Expect.isFalse (f.Reads.Contains "k") "and not read whole"
              Expect.isTrue (f.SlotWrites.Contains("p1", "kind")) "the source's child list, in its kind slot"
              Expect.isTrue (f.SlotReads.Contains("p1", "kind")) "read as it is rewritten"

              Expect.isTrue
                  (f.SlotWrites.Contains("p1", Fuaran.Core.Footprint.childListSlot))
                  "and at Core's child-list slot (Phase 2691)"

              Expect.isTrue
                  (f.SlotReads.Contains("p1", Fuaran.Core.Footprint.childListSlot))
                  "read there as it is rewritten"

              Expect.isTrue (f.SlotReads.Contains("p2", "parent")) "the destination's own position, for the cycle check"
              Expect.isFalse (f.Reads.Contains "root") "the destination's ancestor is not read whole"
              Expect.isTrue (f.SlotReads.Contains("root", "parent")) "it is read at its parent slot"

              // the pair the phase frees: a restyle of the container the node left
              for restyle in [ tone "p1"; TreeOp.UpdateStyle(NodeId "p1", Defaults.style) ] do
                  Expect.isTrue (independentAt inPlaceBase move restyle) (sprintf "move beside %A" restyle)
                  Expect.isTrue (bothOrdersApply inPlaceBase move restyle) "and both orders apply"

              // still ordered: everything else that touches the source's child list or its kind
              let independent other =
                  Fuaran.Core.Ops.independent f (TreeOpFootprint.ofOpIn inPlaceBase other)

              Expect.isFalse (independent (TreeOp.InsertChild(NodeId "p1", leaf "n"))) "an insert into the source"
              Expect.isFalse (independent (TreeOp.RemoveNode(NodeId "p1"))) "the source's removal"
              Expect.isFalse (independent (TreeOp.MoveNode(NodeId "p1", NodeId "p2"))) "a move of the source"
              Expect.isFalse (independent (TreeOp.EditNode(NodeId "p1", (leaf "p1").Kind))) "a kind edit of the source"

              Expect.isFalse
                  (Fuaran.Core.Ops.independent f (Fuaran.Core.Footprint.slotEdit "p1" "kind"))
                  "a prop edit of the source (its kind slot)"

              // freed by the Core 0.42.0 pin (D246): a structure write of the destination meets a
              // slot write of it only at the child-list slot, which a restyle does not touch
              Expect.isTrue (independent (tone "p2")) "a restyle of the destination (Phase 2691)"
              Expect.isTrue (commuteAt inPlaceBase move (tone "p2")) "and the two orders agree"

              // the destination's ancestor: a restyle of it commutes, its relocation does not
              Expect.isTrue (independent (tone "root")) "a restyle of the destination's ancestor"

              Expect.isFalse
                  (Fuaran.Core.Ops.independent f (Fuaran.Core.Footprint.contentEdit "root"))
                  "a whole-node write of the ancestor"

              // a move out of p1 beside a move WITHIN p2, whose cycle check reads root: the two
              // collide only at the destination they share, the append the fold interleaves
              let shapes =
                  Fuaran.Core.Dag.conflictsIn
                      TreeOpFootprint.ofOpIn
                      Apply.apply
                      inPlaceBase
                      [ move ]
                      [ TreeOp.MoveNode(NodeId "q", NodeId "p2") ]
                  |> List.map (fun c -> c.Address, c.Shape)

              Expect.equal
                  shapes
                  [ "p2", Fuaran.Core.MergeConflictShape.InsertPositionClash ]
                  "only the shared destination's child list"

              // a move within one parent keeps Core's arm whole
              let within =
                  TreeOpFootprint.ofOpIn inPlaceBase (TreeOp.MoveNode(NodeId "k", NodeId "p1"))

              Expect.equal
                  within.SlotWrites
                  (Set.singleton ("k", "parent"))
                  "no kind slot for a move within one parent, only the moved node's position"

              Expect.equal within.StructureWrites (Set.singleton "p1") "the one parent, whole"
          }

          test "base-aware: a move writes its target's position, not its content (Phase 2670)" {
              let move = TreeOp.MoveNode(NodeId "k", NodeId "p2")

              // freed: an in-place edit of the moved node, and a reorder of its own children
              for edit in
                  [ tone "k"
                    voice "k"
                    TreeOp.UpdateStyle(NodeId "k", Defaults.style)
                    TreeOp.EditNode(NodeId "k", (leaf "k").Kind)
                    TreeOp.UpdateProp(NodeId "k", "Text", PropValue.Native(nn "edited")) ] do
                  Expect.isTrue (independentAt inPlaceBase move edit) (sprintf "move beside %A" edit)
                  Expect.isTrue (bothOrdersApply inPlaceBase move edit) "and both orders apply"

              let container = TreeOp.MoveNode(NodeId "p1", NodeId "p2")

              let reorderOwn = TreeOp.ReorderChildren(NodeId "p1", [ NodeId "k" ])

              Expect.isTrue (independentAt inPlaceBase container reorderOwn) "a moved container beside its own reorder"
              Expect.isTrue (bothOrdersApply inPlaceBase container reorderOwn) "and both orders apply"

              // still ordered: what reads or writes the moved node's position or existence
              Expect.isFalse (independentAt inPlaceBase move (TreeOp.RemoveNode(NodeId "k"))) "the moved node's removal"

              Expect.isFalse
                  (independentAt inPlaceBase move (TreeOp.RemoveNode(NodeId "p1")))
                  "its source's removal (a whole-node write of the subtree)"

              Expect.isFalse
                  (independentAt inPlaceBase move (TreeOp.MoveNode(NodeId "k", NodeId "root")))
                  "another move of it"

              Expect.isFalse
                  (independentAt inPlaceBase move (TreeOp.ReorderChildren(NodeId "p1", [ NodeId "k" ])))
                  "a reorder of the parent it leaves (which lists it)"

              // freed by the Core 0.42.0 pin (D246): an insert reads its parent's existence, which a
              // move does not change, and writes its child list, which the move does not touch
              let intoMoved = TreeOp.InsertChild(NodeId "p1", leaf "n")

              Expect.isTrue
                  (independentAt inPlaceBase container intoMoved)
                  "an insert into a moved container (Phase 2691)"

              Expect.isTrue (bothOrdersApply inPlaceBase container intoMoved) "and both orders apply"
              Expect.isTrue (commuteAt inPlaceBase container intoMoved) "to one tree"

              // a move whose cycle check reads the moved container as an ancestor of its destination
              Expect.isFalse
                  (independentAt inPlaceBase container (TreeOp.MoveNode(NodeId "q", NodeId "k")))
                  "a move into a node below the moved container"
          }

          test "base-aware: a reorder reads its children at a slot, not whole (Phase 2670)" {
              let reorder = TreeOp.ReorderChildren(NodeId "root", [ NodeId "p2"; NodeId "p1" ])
              let f = TreeOpFootprint.ofOpIn inPlaceBase reorder

              Expect.isEmpty f.Reads "no whole-node read"
              Expect.isEmpty f.StructureWrites "the child list is the parent's kind slot"
              Expect.isEmpty f.ContentWrites "nothing content-written"
              Expect.isEmpty f.UnknownParentWrites "resolved"
              let children = Fuaran.Core.Footprint.childListSlot

              Expect.equal
                  f.SlotWrites
                  (Set.ofList [ "root", "kind"; "root", children ])
                  "the parent's kind slot and its child-list slot (Phase 2691), written"

              Expect.equal
                  f.SlotReads
                  (Set.ofList [ "root", "kind"; "root", children; "p1", "parent"; "p2", "parent" ])
                  "the parent's two slots and each listed child's parent slot, read"

              // freed: an in-place edit of a listed child, and a restyle of the parent
              for edit in
                  [ tone "p1"
                    voice "p2"
                    TreeOp.UpdateStyle(NodeId "p1", Defaults.style)
                    TreeOp.EditNode(NodeId "p1", (Fuaran.box "p1" Defaults.box<Msg>).Kind)
                    tone "root" ] do
                  Expect.isTrue (independentAt inPlaceBase reorder edit) (sprintf "reorder beside %A" edit)
                  Expect.isTrue (bothOrdersApply inPlaceBase reorder edit) "and both orders apply"

              // still ordered: everything that changes the child list or the parent's kind
              for other in
                  [ TreeOp.InsertChild(NodeId "root", leaf "n")
                    TreeOp.RemoveNode(NodeId "p1")
                    TreeOp.MoveNode(NodeId "q", NodeId "root")
                    TreeOp.MoveNode(NodeId "p1", NodeId "p2")
                    TreeOp.ReorderChildren(NodeId "root", [ NodeId "p1"; NodeId "p2" ])
                    TreeOp.EditNode(NodeId "root", (leaf "root").Kind) ] do
                  Expect.isFalse (independentAt inPlaceBase reorder other) (sprintf "reorder against %A" other)

              Expect.isFalse
                  (Fuaran.Core.Ops.independent f (Fuaran.Core.Footprint.slotEdit "root" "kind"))
                  "a prop edit of the parent (its kind slot)"

              // the shapes a refused pair now reports: the parent's child list at Core's slot
              let shapes x y =
                  Fuaran.Core.Dag.conflictsIn TreeOpFootprint.ofOpIn Apply.apply inPlaceBase [ x ] [ y ]
                  |> List.map (fun c -> c.Address, c.Shape)

              Expect.equal
                  (shapes reorder (TreeOp.ReorderChildren(NodeId "root", [ NodeId "p1"; NodeId "p2" ])))
                  [ "root", Fuaran.Core.MergeConflictShape.SlotClash Fuaran.Core.Footprint.childListSlot
                    "root", Fuaran.Core.MergeConflictShape.SlotClash "kind" ]
                  "two reorders of one parent: its child-list slot and its kind slot (Phase 2691)"

              Expect.equal
                  (shapes reorder (TreeOp.InsertChild(NodeId "root", leaf "n")))
                  [ "root", Fuaran.Core.MergeConflictShape.SlotClash Fuaran.Core.Footprint.childListSlot ]
                  "a reorder against an insert: the parent's child-list slot (Phase 2691; Core D246)"

              // a reorder the tree does not resolve is Core's arm
              let ghost = TreeOp.ReorderChildren(NodeId "ghost", [])

              Expect.equal (TreeOpFootprint.ofOpIn inPlaceBase ghost) (TreeOpFootprint.ofOp ghost) "unresolved"
          }

          test
              "base-aware: the Core 0.42.0 pin frees a container's restyle beside a change of its child list (Phase 2691)" {
              // Each changes p1's child list: an insert into it, a move into it, a move out of it and
              // a removal under it. Core D246 compares each against a slot access at the child-list
              // slot, which an edit of the style, a trait or the state does not touch.
              let structural =
                  [ TreeOp.InsertChild(NodeId "p1", leaf "n")
                    TreeOp.MoveNode(NodeId "q", NodeId "p1")
                    TreeOp.MoveNode(NodeId "k", NodeId "p2")
                    TreeOp.RemoveNode(NodeId "k") ]

              let restyles =
                  [ tone "p1"
                    voice "p1"
                    TreeOp.UpdateStyle(NodeId "p1", Defaults.style)
                    TreeOp.UpdateAccessibility(
                        NodeId "p1",
                        { Defaults.Accessibility.empty with
                            DescribedBy = Some "d" }
                    )
                    TreeOp.UpdateTooltip(NodeId "p1", Some(TextSource.Literal "t")) ]

              for change in structural do
                  for restyle in restyles do
                      Expect.isTrue (independentAt inPlaceBase change restyle) (sprintf "%A beside %A" change restyle)

                      Expect.isTrue (bothOrdersApply inPlaceBase change restyle) "both orders apply"
                      Expect.isTrue (commuteAt inPlaceBase change restyle) "to one tree"
          }

          test "base-aware: what reads or rewrites a child list stays ordered against a change of it (Phase 2691)" {
              let structural =
                  [ TreeOp.InsertChild(NodeId "p1", leaf "n")
                    TreeOp.MoveNode(NodeId "q", NodeId "p1")
                    TreeOp.MoveNode(NodeId "k", NodeId "p2")
                    TreeOp.RemoveNode(NodeId "k") ]

              // a kind edit replaces the child list (here: a leaf, which holds none), and a reorder
              // lists the children it permutes; both declare the child-list slot
              let readers =
                  [ TreeOp.EditNode(NodeId "p1", (leaf "p1").Kind)
                    TreeOp.EditNode(NodeId "p1", (Fuaran.box "p1" Defaults.box<Msg>).Kind)
                    TreeOp.ReorderChildren(NodeId "p1", [ NodeId "k" ]) ]

              for change in structural do
                  for reader in readers do
                      Expect.isFalse (independentAt inPlaceBase change reader) (sprintf "%A against %A" change reader)

              // the clash is named at the child-list slot, by Dag.conflicts as by Ops.interference
              let shapes x y =
                  Fuaran.Core.Dag.conflictsIn TreeOpFootprint.ofOpIn Apply.apply inPlaceBase [ x ] [ y ]
                  |> List.map (fun c -> c.Address, c.Shape)

              Expect.contains
                  (shapes (TreeOp.InsertChild(NodeId "p1", leaf "n")) (TreeOp.EditNode(NodeId "p1", (leaf "p1").Kind)))
                  ("p1", Fuaran.Core.MergeConflictShape.SlotClash Fuaran.Core.Footprint.childListSlot)
                  "an insert against a kind edit of its parent"

              // the pair that would be freed without the slot does not commute: the edit to a leaf
              // refuses the insert in one order and drops the inserted child in the other
              Expect.isFalse
                  (commuteAt
                      inPlaceBase
                      (TreeOp.InsertChild(NodeId "p1", leaf "n"))
                      (TreeOp.EditNode(NodeId "p1", (leaf "p1").Kind)))
                  "the edit and the insert disagree between orders"
          }

          test
              "base-aware: a move reads its destination's position, so two containers moved into each other stay ordered (Phase 2691)" {
              // x (under a) into y, and y (under b) into x: whichever applies second nests a node under
              // itself and is refused. Each move's cycle check reads its destination's own parent slot,
              // which the other move writes; until Core D246 the destination's existence read caught it.
              let xIntoY = TreeOp.MoveNode(NodeId "x", NodeId "y")
              let yIntoX = TreeOp.MoveNode(NodeId "y", NodeId "x")

              Expect.isTrue
                  ((TreeOpFootprint.ofOpIn crossBase xIntoY).SlotReads.Contains("y", "parent"))
                  "the destination's parent slot is read"

              Expect.isFalse (independentAt crossBase xIntoY yIntoX) "the two moves are ordered"

              Expect.isFalse
                  (bothOrdersApply crossBase xIntoY yIntoX)
                  "because the second move is refused in either order"

              // with the read erased, the pair reads as independent: the gap the read closes
              let erased (dest: string) (op: TreeOp<Msg>) =
                  let f = TreeOpFootprint.ofOpIn crossBase op

                  { f with
                      SlotReads = Set.remove (dest, "parent") f.SlotReads }

              Expect.isTrue
                  (Fuaran.Core.Ops.independent (erased "y" xIntoY) (erased "x" yIntoX))
                  "erased, the two moves would be promised to commute"

              // the same chain read frees nothing it should not: a restyle of the destination commutes
              Expect.isTrue (independentAt crossBase xIntoY (tone "y")) "a restyle of the destination"
              Expect.isTrue (commuteAt crossBase xIntoY (tone "y")) "to one tree"
          } ]
