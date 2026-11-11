module Fuaran.UI.Ops.Tests.ChildListFootprintLawTests

// ============================================================================
//  Phase 2691 — the base-aware footprint over an INSERT family, on the Core
//  0.42.0 pin.
//
//  Core D246 compares a structure write of `p` (an insert into it, a move into
//  or out of it, a removal under it) against a slot access as a read and a
//  write of `(p, Footprint.childListSlot)`, and a slot access meets the node
//  sets only at a content write. That frees a container's restyle beside every
//  change of its child list, and it would also free a kind edit of the
//  container and a move into a container being moved, unless the tier's arms
//  declare what they read: an `EditNode` writes the child-list slot (a new kind
//  holds other children, or none), and a resolved move reads its destination's
//  `parent` slot (the cycle check reads the whole chain). Core's
//  `Conformance.footprintInLawsAt`, with its child-list law, certifies the
//  family against the tier's reducer, and each declaration has a falsifier that
//  turns the family red on the pair it would free.
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
let private seed = 20261111

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

let private laws (gen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>>) footprintIn =
    CoreConf.footprintInLawsAt sw TreeOpFootprint.ofOp footprintIn CanonicalJson.encodeNode gen seed 300

let private failuresOf (results: Fuaran.Core.LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

/// The failures of a SUBJECT law (soundness, refinement, determinism, the child-list law) — a
/// falsifier must refute the footprint, not merely starve the adequacy guard.
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

/// root [ a [ a0; a1 ]; b [ b0 ]; c [ d [] ] ] — a container `a` whose child list changes, a
/// second container `b` a child moves to and from, and `d` (under `c`), which `a` can be moved
/// into while `d` is moved into `a`.
let private insertBase: Node<obj> =
    box
        "root"
        [ box "a" [ leaf "a0"; leaf "a1" ]
          box "b" [ leaf "b0" ]
          box "c" [ box "d" [] ] ]

/// One op of the insert family. A restyle of `a` (3 in 16), the pair the pin frees, beside every
/// change of `a`'s child list: an insert into it (2 in 16), a move into it and a move out of it, a
/// removal under it; a kind edit of `a` (to a leaf, or to an empty container) and a reorder of it,
/// which read or rewrite the list and must stay ordered against those changes; an insert into `b`
/// and a removal under it (free of `a` under every footprint); and the cross pair, `a` into `d`
/// beside `d` into `a` (4 in 16), whose cycle checks read each other's position.
let private genInsertOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 16 rng
    let j, r2 = CoreRng.intBelow 3 r1

    let restyle =
        TreeOp.PatchStyle(
            NodeId "a",
            { StyleDelta.empty with
                Tone = Some tones[j] }
        )

    match shape with
    | 0
    | 1 -> restyle, r2
    | 2 -> TreeOp.UpdateStyle(NodeId "a", Defaults.style), r2
    | 3
    | 4 ->
        let v, r3 = CoreRng.next r2
        TreeOp.InsertChild(NodeId "a", leaf (sprintf "n%d" (v % 1000))), r3
    | 5 ->
        let v, r3 = CoreRng.next r2
        TreeOp.InsertChild(NodeId "b", leaf (sprintf "m%d" (v % 1000))), r3
    | 6 -> TreeOp.MoveNode(NodeId "b0", NodeId "a"), r2
    | 7 -> TreeOp.MoveNode(NodeId "a0", NodeId "b"), r2
    | 8 -> TreeOp.RemoveNode(NodeId "a1"), r2
    | 9 when j = 0 -> TreeOp.EditNode(NodeId "a", (leaf "a").Kind), r2
    | 9 -> TreeOp.EditNode(NodeId "a", (box "a" []).Kind), r2
    | 10 -> TreeOp.ReorderChildren(NodeId "a", [ NodeId "a1"; NodeId "a0" ]), r2
    | 11
    | 12 -> TreeOp.MoveNode(NodeId "a", NodeId "d"), r2
    | 13
    | 14 -> TreeOp.MoveNode(NodeId "d", NodeId "a"), r2
    | _ -> TreeOp.RemoveNode(NodeId "b0"), r2

let private insertGen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>> =
    { State0 = insertBase
      Op = genInsertOp }

/// The footprint with `slot` of the node `arm` selects taken out, and nothing else changed.
let private erasing (arm: TreeOp<obj> -> string option) (slot: string) (state: Node<obj>) (op: TreeOp<obj>) =
    let f = TreeOpFootprint.ofOpIn state op

    match arm op with
    | Some node ->
        { f with
            SlotWrites = Set.remove (node, slot) f.SlotWrites
            SlotReads = Set.remove (node, slot) f.SlotReads }
    | None -> f

[<Tests>]
let tests =
    testList
        "TreeOpFootprint insert family on the Core 0.42.0 pin (Phase 2691)"
        [ test "footprintInLawsAt holds over the insert family, with the child-list law" {
              laws insertGen TreeOpFootprint.ofOpIn |> expectHolds "insert"
          }

          test "the insert family sees a kind-edit pair (an erased child-list slot on EditNode is red)" {
              // Without it a kind edit of `a` reads as independent of an insert into `a`: the edit to
              // a leaf refuses the insert in one order and drops the inserted child in the other.
              let arm op =
                  match op with
                  | TreeOp.EditNode(NodeId n, _) -> Some n
                  | _ -> None

              Expect.isNonEmpty
                  (laws insertGen (erasing arm Fuaran.Core.Footprint.childListSlot) |> refutedBy)
                  "an understated kind edit refutes the family"
          }

          test "the insert family sees a reorder pair (an erased child-list slot on ReorderChildren is red)" {
              let arm op =
                  match op with
                  | TreeOp.ReorderChildren(NodeId p, _) -> Some p
                  | _ -> None

              Expect.isNonEmpty
                  (laws insertGen (erasing arm Fuaran.Core.Footprint.childListSlot) |> refutedBy)
                  "an understated reorder refutes the family"
          }

          test "the insert family sees the cross pair (an erased destination position read is red)" {
              // Without it `a` into `d` and `d` into `a` read as independent, though whichever
              // applies second nests a node under itself and is refused.
              let arm op =
                  match op with
                  | TreeOp.MoveNode(_, NodeId dest) -> Some dest
                  | _ -> None

              Expect.isNonEmpty
                  (laws insertGen (erasing arm "parent") |> refutedBy)
                  "an understated move refutes the family"
          } ]
