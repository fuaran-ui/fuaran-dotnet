module Fuaran.UI.Ops.Tests.MoveFootprintLawTests

// ============================================================================
//  Phase 2651 — the base-aware footprint over the MOVE family.
//
//  A `MoveNode` changes its source parent's child list and nothing else of it,
//  so `TreeOpFootprint.ofOpIn` declares the source parent at its `kind` slot
//  rather than as a whole-node read and structure write. That is what frees a
//  move out of a container beside a restyle of the container. Core's
//  `Conformance.footprintInLawsAt` certifies the change against the tier's own
//  reducer over a generator built around that pair: moves of leaves between
//  containers, beside restyles, inserts and removals of the containers and a
//  reorder of their parent. Soundness (a pair reported clean commutes),
//  refinement (the base-aware form frees every pair the pure form frees) and
//  determinism, with the kit's guard demanding freed pairs beside the
//  interfering ones. A falsifier with the destination's structure write
//  erased goes red, so the family is shown to see the move pairs it certifies.
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
let private seed = 20261109

let private leaf (id: string) : Node<obj> = Fuaran.markdown id $"body of {id}"

let private box (id: string) (kids: Node<obj> list) : Node<obj> =
    Fuaran.box
        id
        { Defaults.box<obj> with
            Children = kids }

/// root [ a [ a0; a1; d [] ]; b [ b0 ]; c [] ] — four containers a leaf can move between, one of
/// them nested, so a move into `d` reads an ancestor (`a`) that a concurrent op can restyle.
let private moveBase: Node<obj> =
    box
        "root"
        [ box "a" [ leaf "a0"; leaf "a1"; box "d" [] ]
          box "b" [ leaf "b0" ]
          box "c" [] ]

let private leaves = [| NodeId "a0"; NodeId "a1"; NodeId "b0" |]
let private containers = [| NodeId "a"; NodeId "b"; NodeId "c"; NodeId "d" |]

let private tones =
    [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let private sw: Fuaran.Core.StreamWitness<TreeOp<obj>, Node<obj>, ApplyError> =
    { Apply = Apply.apply
      Encode = CanonicalJson.encodeOp
      Decode = fun s -> JsonDecode.decodeOp s |> Result.mapError (sprintf "%A") }

/// One op of the move family. Moves of a leaf to any container (3 in 10) and restyles of a
/// container, by field or whole (3 in 10), so a move out of a container beside its restyle — the
/// pair the phase frees — is drawn often; inserts under a container (3 in 10), whose pairs under
/// different containers are the class the pure footprint already frees; and the removal of a
/// container or a reorder of the root (1 in 10), which supply the interfering pairs.
let private genMoveOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let shape, r1 = CoreRng.intBelow 10 rng
    let i, r2 = CoreRng.intBelow 4 r1
    let j, r3 = CoreRng.intBelow 4 r2

    match shape with
    | 0
    | 1
    | 2 -> TreeOp.MoveNode(leaves[i % 3], containers[j]), r3
    | 3
    | 4 ->
        TreeOp.PatchStyle(
            containers[i],
            { StyleDelta.empty with
                Tone = Some tones[j % 3] }
        ),
        r3
    | 5 -> TreeOp.UpdateStyle(containers[i], Defaults.style), r3
    | 6
    | 7
    | 8 ->
        let v, r4 = CoreRng.next r3
        TreeOp.InsertChild(containers[i], leaf (sprintf "n%d" (v % 1000))), r4
    | _ when j = 0 -> TreeOp.ReorderChildren(NodeId "root", [ NodeId "c"; NodeId "b"; NodeId "a" ]), r3
    | _ -> TreeOp.RemoveNode containers[i], r3

let private moveGen: Fuaran.Core.StreamGen<TreeOp<obj>, Node<obj>> =
    { State0 = moveBase; Op = genMoveOp }

let private laws footprintIn =
    CoreConf.footprintInLawsAt sw TreeOpFootprint.ofOp footprintIn CanonicalJson.encodeNode moveGen seed 300

[<Tests>]
let tests =
    testList
        "TreeOpFootprint move family (Phase 2651)"
        [ test "footprintInLawsAt holds over the move family" {
              let failures = laws TreeOpFootprint.ofOpIn |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failures) then
                  failures
                  |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                  |> String.concat "\n"
                  |> failtestf "footprintInLawsAt over the move family failed:\n%s"
          }

          test "the family sees a move pair (an erased destination write is red)" {
              // The destination's named structure write and read taken out of every move and
              // nothing else changed: two moves into one container then read as independent,
              // though the container's child order depends on which applies first.
              let erased (state: Node<obj>) (op: TreeOp<obj>) =
                  let f = TreeOpFootprint.ofOpIn state op

                  match op with
                  | TreeOp.MoveNode(_, NodeId np) ->
                      { f with
                          StructureWrites = Set.remove np f.StructureWrites
                          Reads = Set.remove np f.Reads }
                  | _ -> f

              let failures = laws erased |> List.filter (fun r -> not r.Passed)
              Expect.isNonEmpty failures "an understated move refutes the family"
          }

          test "a move out of a container beside a restyle of the container is clean and commutes" {
              let move = TreeOp.MoveNode(NodeId "a0", NodeId "c")

              let restyle =
                  TreeOp.PatchStyle(
                      NodeId "a",
                      { StyleDelta.empty with
                          Tone = Some ToneVariant.Brand }
                  )

              Expect.isEmpty
                  (Fuaran.Core.Dag.conflictsIn TreeOpFootprint.ofOpIn sw.Apply moveBase [ move ] [ restyle ])
                  "Dag.conflictsIn reports the pair clean"

              let run x y =
                  Apply.apply x moveBase
                  |> Result.bind (Apply.apply y)
                  |> Result.map CanonicalJson.encodeNode

              Expect.equal (run move restyle) (run restyle move) "both orders reach one tree"
              Expect.isOk (run move restyle) "and apply"
          } ]
