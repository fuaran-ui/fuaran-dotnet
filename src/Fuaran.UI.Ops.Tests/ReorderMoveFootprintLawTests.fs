module Fuaran.UI.Ops.Tests.ReorderMoveFootprintLawTests

// ============================================================================
//  Phase 2670 — the base-aware footprint over a REORDER family and a
//  MOVE-WITH-EDIT family.
//
//  A resolved `ReorderChildren` reads and writes its parent at the `kind` slot
//  (the slot that holds the child list) and reads each listed child at its
//  `parent` slot; a resolved `MoveNode` reads and writes its target at the
//  target's `parent` slot rather than reading and content-writing it whole.
//  Those two narrowings free a reorder beside an edit or restyle of a listed
//  child (and a restyle of the parent), and a move beside a restyle, an edit
//  or a reorder of the moved node. Core's `Conformance.footprintInLawsAt`
//  certifies each against the tier's own reducer over a generator built
//  around the freed pairs and the pairs that must stay ordered: soundness (a
//  pair reported clean commutes), refinement and determinism, with the kit's
//  guard demanding freed pairs beside interfering ones. Each family has a
//  falsifier: an understated footprint goes red, so the family is shown to see
//  the pairs it certifies. 2651's `MoveFootprintLawTests` is the shape.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module CoreConf = Fuaran.Core.Conformance
module CoreRng = Fuaran.Core.ConfRng

/// One seed, so a refutation names a run anyone can reproduce.
let private seed = 20261110

let private leaf (id: string) : Node<obj> = Fuaran.markdown id $"body of {id}"

let private box (id: string) (kids: Node<obj> list) : Node<obj> =
    Fuaran.box
        id
        { Defaults.box<obj> with
            Children = kids }

let private tones =
    [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let private sw: Fuaran.Core.StreamWitness<TreeOp<obj>, Node<obj>, ApplyError> =
    { Apply = Apply.apply
      Encode = CanonicalJson.encodeOp
      Decode = fun s -> JsonDecode.decodeOp s |> Result.mapError (sprintf "%A") }

let private restyle (id: NodeId) (j: int) : TreeOp<obj> =
    TreeOp.PatchStyle(
        id,
        { StyleDelta.empty with
            Tone = Some tones[j % 3] }
    )

let private edit (NodeId raw as id) (j: int) : TreeOp<obj> =
    TreeOp.EditNode(id, (Fuaran.markdown raw (sprintf "edit %d" j)).Kind)

let private laws (gen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>>) footprintIn =
    CoreConf.footprintInLawsAt sw TreeOpFootprint.ofOp footprintIn CanonicalJson.encodeNode gen seed 300

let private failuresOf (results: Fuaran.Core.LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

/// The failures of a SUBJECT law (soundness, refinement, determinism) — a falsifier must refute the
/// footprint, not merely starve the adequacy guard.
let private refutedBy (results: Fuaran.Core.LawResult list) =
    failuresOf results
    |> List.filter (fun r ->
        match r.Kind with
        | Fuaran.Core.LawKind.Subject _ -> true
        | _ -> false)

let private expectHolds (name: string) (results: Fuaran.Core.LawResult list) =
    let failures = failuresOf results

    if not (List.isEmpty failures) then
        failures
        |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "footprintInLawsAt over the %s family failed:\n%s" name

// ---- the reorder family ----

/// root [ a [ a0; a1; a2 ]; b [ b0 ] ] — a container whose children a reorder lists, and a
/// second container a child can move to.
let private reorderBase: Node<obj> =
    box "root" [ box "a" [ leaf "a0"; leaf "a1"; leaf "a2" ]; box "b" [ leaf "b0" ] ]

let private listed = [| NodeId "a0"; NodeId "a1"; NodeId "a2" |]

/// Two orders of `a`'s base children. A reorder that no longer lists exactly the current
/// children does not apply, which is one of the interfering pairs the laws must see ordered.
let private orders =
    [| [ NodeId "a2"; NodeId "a1"; NodeId "a0" ]
       [ NodeId "a1"; NodeId "a0"; NodeId "a2" ] |]

/// One op of the reorder family. Reorders of `a` (3 in 10) beside restyles and kind edits of a
/// listed child (3 in 10), the pair the phase frees, and a restyle of `a` itself (1 in 10); an
/// insert into `a` or `b` (two inserts under different parents are the class even the pure
/// footprint frees), a move of a listed child to `b`, or its removal (3 in 10), which change the
/// child list and supply the interfering pairs.
let private genReorderOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 10 rng
    let i, r2 = CoreRng.intBelow 3 r1
    let j, r3 = CoreRng.intBelow 3 r2

    match shape with
    | 0
    | 1
    | 2 -> TreeOp.ReorderChildren(NodeId "a", orders[i % 2]), r3
    | 3
    | 4 -> restyle listed[i] j, r3
    | 5 -> edit listed[i] j, r3
    | 6 -> restyle (NodeId "a") j, r3
    | 7 ->
        let v, r4 = CoreRng.next r3
        TreeOp.InsertChild((if i = 0 then NodeId "b" else NodeId "a"), leaf (sprintf "n%d" (v % 1000))), r4
    | 8 -> TreeOp.MoveNode(listed[i], NodeId "b"), r3
    | _ -> TreeOp.RemoveNode listed[i], r3

let private reorderGen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>> =
    { State0 = reorderBase
      Op = genReorderOp }

// ---- the move-with-edit family ----

/// root [ a [ x [ x0; y [] ]; a1 ]; e [ d [] ]; b [] ] — a container `x` that moves and whose own
/// children can be reordered, beside leaves that move, and a cross pair: `x` moving under `d`
/// while `e` (above `d`) moves under `y` (below `x`), which nests one into the other.
let private moveBase: Node<obj> =
    box
        "root"
        [ box "a" [ box "x" [ leaf "x0"; box "y" [] ]; leaf "a1" ]
          box "e" [ box "d" [] ]
          box "b" [] ]

let private movers = [| NodeId "x"; NodeId "a1"; NodeId "x0" |]
let private destinations = [| NodeId "a"; NodeId "b"; NodeId "d" |]

let private inserts = [| NodeId "x"; NodeId "b"; NodeId "d" |]

/// One op of the move-with-edit family. A move of `x`, `a1` or `x0` to a container (1 in 10)
/// beside restyles and edits of a mover (3 in 10) and a reorder of `x`'s own children, the pairs
/// the phase frees; inserts into `x`, `b` or `d` (2 in 10 — two under different parents are the
/// class even the pure footprint frees); the removal of a mover; and the cross pair, `x` under
/// `d` beside `e` under `y` (2 in 10, with a move of `x0` into `y` beside them), which read or
/// write a moved node's position and supply the interfering pairs.
let private genMoveEditOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 10 rng
    let i, r2 = CoreRng.intBelow 3 r1
    let j, r3 = CoreRng.intBelow 3 r2

    match shape with
    | 0 -> TreeOp.MoveNode(movers[i], destinations[j]), r3
    | 1 -> TreeOp.MoveNode(NodeId "x", NodeId "d"), r3
    | 2 -> TreeOp.MoveNode(NodeId "e", NodeId "y"), r3
    | 3
    | 4 -> restyle movers[i] j, r3
    | 5 when i > 0 -> edit movers[i] j, r3
    | 5 -> TreeOp.UpdateStyle(NodeId "x", Defaults.style), r3
    | 6 when j = 0 -> TreeOp.MoveNode(NodeId "x0", NodeId "y"), r3
    | 6 -> TreeOp.ReorderChildren(NodeId "x", [ NodeId "y"; NodeId "x0" ]), r3
    | 7
    | 8 ->
        let v, r4 = CoreRng.next r3
        TreeOp.InsertChild(inserts[j], leaf (sprintf "n%d" (v % 1000))), r4
    | _ -> TreeOp.RemoveNode movers[i], r3

let private moveEditGen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>> =
    { State0 = moveBase
      Op = genMoveEditOp }

[<Tests>]
let tests =
    testList
        "TreeOpFootprint reorder and move-with-edit families (Phase 2670)"
        [ test "footprintInLawsAt holds over the reorder family" {
              laws reorderGen TreeOpFootprint.ofOpIn |> expectHolds "reorder"
          }

          test "the reorder family sees a reorder pair (an erased kind-slot write is red)" {
              // The parent's kind slot taken out of every reorder and nothing else changed: a
              // reorder then reads as independent of an insert into its parent, though the insert
              // makes the reorder's list stale in one order and not the other.
              let erased (state: Node<obj>) (op: TreeOp<obj>) =
                  let f = TreeOpFootprint.ofOpIn state op

                  match op with
                  | TreeOp.ReorderChildren(NodeId p, _) ->
                      { f with
                          SlotWrites = Set.remove (p, "kind") f.SlotWrites
                          SlotReads = Set.remove (p, "kind") f.SlotReads }
                  | _ -> f

              Expect.isNonEmpty (laws reorderGen erased |> refutedBy) "an understated reorder refutes the family"
          }

          test "footprintInLawsAt holds over the move-with-edit family" {
              laws moveEditGen TreeOpFootprint.ofOpIn |> expectHolds "move-with-edit"
          }

          test "the move-with-edit family sees a move pair (an erased target-slot write is red)" {
              // The moved node's parent slot taken out of every move: `x` under `d` and `e` under
              // `y` then read as independent (each reads the other's target only as an ancestor,
              // at that slot), though whichever applies second nests a node under itself.
              let erased (state: Node<obj>) (op: TreeOp<obj>) =
                  let f = TreeOpFootprint.ofOpIn state op

                  match op with
                  | TreeOp.MoveNode(NodeId t, _) ->
                      { f with
                          SlotWrites = Set.remove (t, "parent") f.SlotWrites
                          SlotReads = Set.remove (t, "parent") f.SlotReads }
                  | _ -> f

              Expect.isNonEmpty (laws moveEditGen erased |> refutedBy) "an understated move refutes the family"
          } ]
