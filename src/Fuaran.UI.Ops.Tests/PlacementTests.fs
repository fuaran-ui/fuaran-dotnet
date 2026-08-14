module Fuaran.UI.Tests.PlacementTests

// ============================================================================
//  Placement algebra acceptance + property set.
//
//  Two obligations, stated as properties over generated trees and checked
//  against the REAL apply engine (never a re-derivation of its logic):
//
//   (1) No false permit — every op a helper emits is accepted by the apply
//       engine, and the applied tree exhibits the placement's declared order
//       (the moved/inserted node sits exactly where the `Placement` said,
//       with the other siblings' order preserved).
//
//   (2) No false refuse — every helper rejection corresponds to an apply-side
//       rejection of the op the helper would otherwise have emitted (or, for
//       `UnknownAnchor`, to the `OrderingMismatch` refusal of the only op that
//       could have honoured the anchor).
//
//  The clone verbs add the tree-wide id obligations: a duplicate never
//  collides with any id in the target tree (including ids held in
//  non-structural positions), the clone is structurally equal to its source
//  modulo ids, and a paste preserves non-colliding ids while remapping
//  colliding ones.
// ============================================================================

open Expecto
open FsCheck
open FsCheck.FSharp
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

// ─── Fixtures ────────────────────────────────────────────────────────────────

let private leaf (id: string) : Node<obj> = Fuaran.markdown id "body"

let private container (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<obj> with
            Children = children }

/// root ── left [a; b; c] · solo (childless leaf) · right [d] · empty []
let private fixture () : Node<obj> =
    container
        "root"
        [ container "left" [ leaf "a"; leaf "b"; leaf "c" ]
          leaf "solo"
          container "right" [ leaf "d" ]
          container "empty" [] ]

let private childIdsIn (root: Node<obj>) (parentId: string) : string list =
    match Introspect.findNode (NodeId parentId) root with
    | Some p ->
        Introspect.getChildren p.Kind
        |> Option.defaultValue []
        |> List.map (fun c -> c.Id)
    | None -> failtestf "parent '%s' not found in tree" parentId

let private applied (op: TreeOp<obj>) (root: Node<obj>) : Node<obj> =
    match Apply.apply op root with
    | Ok updated -> updated
    | Error e -> failtestf "apply refused an op the helper emitted: %A" e

let private refusedAs (op: TreeOp<obj>) (root: Node<obj>) : ApplyErrorCode =
    match Apply.apply op root with
    | Ok _ -> failtest "apply accepted an op the helper refused"
    | Error e -> e.Code

/// `TreeOp` carries closures and has no structural equality, so a helper
/// verdict is matched by its refusal alone.
let private expectRefusal (result: Result<TreeOp<obj>, PlaceError>) (expected: PlaceError) (message: string) =
    match result with
    | Error e -> Expect.equal e expected message
    | Ok _ -> failtestf "expected the refusal %A, got Ok (%s)" expected message

let private rawIds (root: Node<obj>) : string list =
    Introspect.allNodeIds root |> List.map (fun (NodeId s) -> s)

let private allDistinct (root: Node<obj>) : bool =
    let ids = rawIds root
    List.length ids = List.length (List.distinct ids)

/// Preorder kind tags over the whole traversal surface — structural equality
/// modulo ids.
let rec private kindShape (n: Node<obj>) : string list =
    Introspect.kindName n.Kind
    :: (Introspect.descendantNodes n |> List.collect kindShape)

let private insertedChild (op: TreeOp<obj>) : Node<obj> =
    match op with
    | TreeOp.InsertChild(_, c) -> c
    | TreeOp.Batch [ TreeOp.InsertChild(_, c); _ ] -> c
    | other -> failtestf "expected a placed insert, got %A" other

let private at (parentId: string) (placement: Placement) : Target =
    { ParentId = NodeId parentId
      Placement = placement }

// ─── Unit tests: placeOp ─────────────────────────────────────────────────────

[<Tests>]
let placeOpTests =
    testList
        "Placement.placeOp"
        [ test "Last emits a bare InsertChild and appends" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" Placement.Last) with
              | Ok(TreeOp.InsertChild _ as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "a"; "b"; "c"; "x" ] "appended last"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          test "First emits Batch [insert; reorder] and lands first" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" Placement.First) with
              | Ok(TreeOp.Batch [ TreeOp.InsertChild _; TreeOp.ReorderChildren _ ] as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "x"; "a"; "b"; "c" ] "prepended"
              | other -> failtestf "expected Batch [InsertChild; ReorderChildren], got %A" other
          }

          test "First into an empty container stays a bare InsertChild" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "empty" Placement.First) with
              | Ok(TreeOp.InsertChild _ as op) -> Expect.equal (childIdsIn (applied op t) "empty") [ "x" ] "sole child"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          test "Before an interior sibling lands immediately before it" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" (Placement.Before(NodeId "b"))) with
              | Ok op -> Expect.equal (childIdsIn (applied op t) "left") [ "a"; "x"; "b"; "c" ] "before b"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "After the last sibling stays a bare InsertChild" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" (Placement.After(NodeId "c"))) with
              | Ok(TreeOp.InsertChild _ as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "a"; "b"; "c"; "x" ] "after c = append"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          test "absent parent is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "ghost" Placement.Last))
                  (PlaceError.ParentNotFound(NodeId "ghost"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "ghost", leaf "x")) t)
                  ApplyErrorCode.ParentNotFound
                  "apply-side twin"
          }

          test "childless parent is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "solo" Placement.Last))
                  (PlaceError.ChildlessKind(NodeId "solo"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "solo", leaf "x")) t)
                  ApplyErrorCode.ChildlessKind
                  "apply-side twin"
          }

          test "duplicate id is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.placeOp t (leaf "a") (at "right" Placement.Last))
                  (PlaceError.DuplicateId(NodeId "a"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "right", leaf "a")) t)
                  ApplyErrorCode.DuplicateNodeId
                  "apply-side twin"
          }

          test "an anchor that is not a destination child is refused, matching the reorder's OrderingMismatch" {
              let t = fixture ()

              // "d" exists in the tree but is not a child of "left".
              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "left" (Placement.Before(NodeId "d"))))
                  (PlaceError.UnknownAnchor(NodeId "d"))
                  "helper refusal"

              // The only op that could honour the anchor names it in a reorder,
              // which the apply engine refuses.
              Expect.equal
                  (refusedAs
                      (TreeOp.ReorderChildren(NodeId "left", [ NodeId "d"; NodeId "a"; NodeId "b"; NodeId "c" ]))
                      t)
                  ApplyErrorCode.OrderingMismatch
                  "apply-side twin"
          } ]

// ─── Unit tests: moveOp / canPlace ──────────────────────────────────────────

[<Tests>]
let moveOpTests =
    testList
        "Placement.moveOp / canPlace"
        [ test "cross-parent Last emits a bare MoveNode" {
              let t = fixture ()

              match Placement.moveOp t (NodeId "a") (at "right" Placement.Last) with
              | Ok(TreeOp.MoveNode _ as op) ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "right") [ "d"; "a" ] "moved in"
                  Expect.equal (childIdsIn updated "left") [ "b"; "c" ] "moved out"
              | other -> failtestf "expected a bare MoveNode, got %A" other
          }

          test "same-parent re-placement emits Batch [move; reorder]" {
              let t = fixture ()

              match Placement.moveOp t (NodeId "c") (at "left" (Placement.Before(NodeId "a"))) with
              | Ok(TreeOp.Batch [ TreeOp.MoveNode _; TreeOp.ReorderChildren _ ] as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "c"; "a"; "b" ] "c to the head"
              | other -> failtestf "expected Batch [MoveNode; ReorderChildren], got %A" other
          }

          test "move into itself is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "left") (at "left" Placement.Last))
                  (PlaceError.MoveIntoSelf(NodeId "left"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "left", NodeId "left")) t)
                  ApplyErrorCode.KindMismatch
                  "apply-side twin"
          }

          test "move into a descendant is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "root") (at "left" Placement.Last))
                  (PlaceError.MoveIntoDescendant(NodeId "root", NodeId "left"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "root", NodeId "left")) t)
                  ApplyErrorCode.KindMismatch
                  "apply-side twin"
          }

          test "absent node is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "ghost") (at "left" Placement.Last))
                  (PlaceError.NodeNotFound(NodeId "ghost"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "ghost", NodeId "left")) t)
                  ApplyErrorCode.NodeNotFound
                  "apply-side twin"
          }

          test "childless destination is refused as the apply engine would refuse it" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "a") (at "solo" Placement.Last))
                  (PlaceError.ChildlessKind(NodeId "solo"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "a", NodeId "solo")) t)
                  ApplyErrorCode.ChildlessKind
                  "apply-side twin"
          }

          test "anchoring a move on the moved node itself is an unknown anchor" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "a") (at "right" (Placement.After(NodeId "a"))))
                  (PlaceError.UnknownAnchor(NodeId "a"))
                  "the moved node is not among its own destination siblings"
          }

          test "a node held in a non-structural position is not movable — refused as the engine refuses it" {
              let t =
                  container "root" [ Node.onLoading (leaf "ph") (leaf "m"); container "box" [] ]

              expectRefusal
                  (Placement.moveOp t (NodeId "ph") (at "box" Placement.Last))
                  (PlaceError.NodeNotFound(NodeId "ph"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "ph", NodeId "box")) t)
                  ApplyErrorCode.NodeNotFound
                  "apply-side twin"
          }

          test "canPlace agrees with moveOp on the legal drop" {
              let t = fixture ()

              Expect.equal
                  (Placement.canPlace t (NodeId "a") (at "right" (Placement.Before(NodeId "d"))))
                  (Ok())
                  "legal drop"
          } ]

// ─── Unit tests: nudgeOp ─────────────────────────────────────────────────────

[<Tests>]
let nudgeOpTests =
    testList
        "Placement.nudgeOp"
        [ test "-1 swaps the node with its previous sibling via the full permutation" {
              let t = fixture ()

              match Placement.nudgeOp t (NodeId "b") -1 with
              | Ok(TreeOp.ReorderChildren _ as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "b"; "a"; "c" ] "b up"
              | other -> failtestf "expected ReorderChildren, got %A" other
          }

          test "+2 swaps across the list" {
              let t = fixture ()

              match Placement.nudgeOp t (NodeId "a") 2 with
              | Ok op -> Expect.equal (childIdsIn (applied op t) "left") [ "c"; "b"; "a" ] "a and c swapped"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "the first sibling cannot move up" {
              let t = fixture ()

              expectRefusal
                  (Placement.nudgeOp t (NodeId "a") -1)
                  (PlaceError.NudgeOutOfRange(NodeId "a", -1))
                  "already first"
          }

          test "the last sibling cannot move down" {
              let t = fixture ()

              expectRefusal
                  (Placement.nudgeOp t (NodeId "c") 1)
                  (PlaceError.NudgeOutOfRange(NodeId "c", 1))
                  "already last"
          }

          test "the root has no siblings to nudge among" {
              let t = fixture ()

              expectRefusal (Placement.nudgeOp t (NodeId "root") 1) (PlaceError.CannotNudgeRoot(NodeId "root")) "root"
          }

          test "an absent node is refused" {
              let t = fixture ()

              expectRefusal (Placement.nudgeOp t (NodeId "ghost") 1) (PlaceError.NodeNotFound(NodeId "ghost")) "ghost"
          } ]

// ─── Unit tests: duplicateOp / pasteOp ──────────────────────────────────────

[<Tests>]
let cloneVerbTests =
    testList
        "Placement.duplicateOp / pasteOp"
        [ test "duplicate places a fresh-id clone beside its source" {
              let t = fixture ()

              match Placement.duplicateOp t (NodeId "left") (at "root" (Placement.After(NodeId "left"))) with
              | Ok op ->
                  let updated = applied op t

                  Expect.equal
                      (childIdsIn updated "root")
                      [ "left"; "left-copy"; "solo"; "right"; "empty" ]
                      "clone lands immediately after its source"

                  Expect.equal
                      (childIdsIn updated "left-copy")
                      [ "a-copy"; "b-copy"; "c-copy" ]
                      "interior ids remapped by the default derived strategy"

                  Expect.isTrue (allDistinct updated) "no id collides anywhere in the tree"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate is structurally equal to its source modulo ids" {
              let t = fixture ()

              match Placement.duplicateOp t (NodeId "left") (at "right" Placement.Last) with
              | Ok op ->
                  let source =
                      Introspect.findNode (NodeId "left") t
                      |> Option.defaultWith (fun () -> failtest "fixture")

                  Expect.equal (kindShape (insertedChild op)) (kindShape source) "same kind tags, same shape"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "the injectable strategy mints deterministic sequential ids" {
              let t = fixture ()

              match
                  Placement.duplicateOpWith (FreshIds.sequential "dup") t (NodeId "left") (at "root" Placement.Last)
              with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (List.last (childIdsIn updated "root")) "dup-1" "clone root is dup-1"

                  Expect.equal
                      (childIdsIn updated "dup-1")
                      [ "dup-2"; "dup-3"; "dup-4" ]
                      "interior ids minted in traversal order"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate remaps ids held in non-structural positions too" {
              // `ph` lives in a State slot — invisible to the structural child
              // lists, but inside the tree-wide id-uniqueness contract.
              let t = container "root" [ Node.onLoading (leaf "ph") (leaf "m") ]

              match Placement.duplicateOp t (NodeId "m") (at "root" Placement.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.isTrue (allDistinct updated) "the State-slot id was remapped, not smuggled"
                  Expect.contains (rawIds updated) "ph-copy" "the placeholder clone carries a fresh id"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate of an absent source is refused" {
              let t = fixture ()

              expectRefusal
                  (Placement.duplicateOp t (NodeId "ghost") (at "root" Placement.Last))
                  (PlaceError.NodeNotFound(NodeId "ghost"))
                  "ghost"
          }

          test "paste remaps colliding ids and preserves the rest" {
              let t = fixture ()

              // Lifted from a different tree: "left" and "a" collide with the
              // target; "z" does not.
              let foreign = container "left" [ leaf "a"; leaf "z" ]

              match Placement.pasteOp t foreign (at "right" Placement.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "right") [ "d"; "left-copy" ] "pasted under right"
                  Expect.equal (childIdsIn updated "left-copy") [ "a-copy"; "z" ] "collisions remapped, z preserved"
                  Expect.isTrue (allDistinct updated) "no id collides anywhere in the tree"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "paste with no collisions preserves every id" {
              let t = fixture ()
              let foreign = container "p" [ leaf "q" ]

              match Placement.pasteOp t foreign (at "empty" Placement.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "empty") [ "p" ] "pasted"
                  Expect.equal (childIdsIn updated "p") [ "q" ] "interior id untouched"
              | Error e -> failtestf "expected Ok, got %A" e
          } ]

// ─── Property tests ──────────────────────────────────────────────────────────
//
// Generated trees are Box containers over Markdown leaves with sequential
// preorder ids (`n1`, `n2`, …) under a fixed `root` container; parents,
// anchors, and moved nodes are drawn from the tree's OWN ids plus a `ghost`,
// so both the legal and every illegal class is generated.

type private Shape =
    | SLeaf
    | SBox of Shape list

let rec private genShape (depth: int) : Gen<Shape> =
    if depth <= 0 then
        Gen.constant SLeaf
    else
        Gen.frequency
            [ 2, Gen.constant SLeaf
              3,
              gen {
                  let! n = Gen.choose (0, 3)
                  let! children = Gen.listOfLength n (genShape (depth - 1))
                  return SBox children
              } ]

let private build (shape: Shape) : Node<obj> =
    let mutable counter = 0

    let rec go (shape: Shape) : Node<obj> =
        counter <- counter + 1
        let id = "n" + string counter

        match shape with
        | SLeaf -> leaf id
        | SBox children -> container id (children |> List.map go)

    container "root" [ go shape ]

let private genTree: Gen<Node<obj>> = Gen.map build (genShape 3)

let private genPick (t: Node<obj>) : Gen<NodeId> =
    Gen.elements (NodeId "ghost" :: Introspect.allNodeIds t)

let private genTarget (t: Node<obj>) : Gen<Target> =
    gen {
        let! parent = genPick t

        let! placement =
            Gen.oneof
                [ Gen.constant Placement.Last
                  Gen.constant Placement.First
                  Gen.map Placement.Before (genPick t)
                  Gen.map Placement.After (genPick t) ]

        return
            { ParentId = parent
              Placement = placement }
    }

let private childNodeIds (root: Node<obj>) (parentId: NodeId) : NodeId list =
    Introspect.findNode parentId root
    |> Option.bind (fun p -> Introspect.getChildren p.Kind)
    |> Option.defaultValue []
    |> List.map (fun c -> NodeId c.Id)

/// The moved/inserted node sits exactly where the placement declared, and the
/// other siblings keep their relative order.
let private declaredOrderHolds (before: Node<obj>) (after: Node<obj>) (moved: NodeId) (target: Target) : bool =
    let ids = childNodeIds after target.ParentId

    match ids |> List.tryFindIndex (fun id -> id = moved) with
    | None -> false
    | Some idx ->
        let othersAfter = ids |> List.filter (fun id -> id <> moved)

        let othersBefore =
            childNodeIds before target.ParentId |> List.filter (fun id -> id <> moved)

        othersAfter = othersBefore
        && (match target.Placement with
            | Placement.Last -> idx = List.length ids - 1
            | Placement.First -> idx = 0
            | Placement.Before a -> idx + 1 < List.length ids && ids[idx + 1] = a
            | Placement.After a -> idx > 0 && ids[idx - 1] = a)

/// The anchor is genuinely not among the destination's post-op children
/// (excluding the moved node itself, which is never its own anchor).
let private anchorNotASibling (t: Node<obj>) (parentId: NodeId) (moved: NodeId) (anchor: NodeId) : bool =
    let siblings = childNodeIds t parentId |> List.filter (fun id -> id <> moved)
    not (List.contains anchor siblings)

let private anchorOf (p: Placement) : NodeId option =
    match p with
    | Placement.Before a
    | Placement.After a -> Some a
    | _ -> None

let private placeCorresponds (t: Node<obj>) (target: Target) : bool =
    let fresh = leaf "fresh-child"

    match Placement.placeOp t fresh target with
    | Ok op ->
        match Apply.apply op t with
        | Error _ -> false
        | Ok updated -> declaredOrderHolds t updated (NodeId "fresh-child") target
    | Error(PlaceError.ParentNotFound p) ->
        p = target.ParentId
        && refusedAs (TreeOp.InsertChild(target.ParentId, fresh)) t = ApplyErrorCode.ParentNotFound
    | Error(PlaceError.ChildlessKind p) ->
        p = target.ParentId
        && refusedAs (TreeOp.InsertChild(target.ParentId, fresh)) t = ApplyErrorCode.ChildlessKind
    | Error(PlaceError.UnknownAnchor a) ->
        anchorOf target.Placement = Some a
        && anchorNotASibling t target.ParentId (NodeId "fresh-child") a
    | Error _ -> false

let private moveCorresponds (t: Node<obj>) (moved: NodeId) (target: Target) : bool =
    let naive = TreeOp.MoveNode(moved, target.ParentId)

    match Placement.moveOp t moved target with
    | Ok op ->
        match Apply.apply op t with
        | Error _ -> false
        | Ok updated ->
            declaredOrderHolds t updated moved target
            && (match Introspect.findParent moved updated with
                | Some(p, _) -> NodeId p.Id = target.ParentId
                | None -> false)
    | Error(PlaceError.NodeNotFound _)
    | Error(PlaceError.MoveIntoSelf _)
    | Error(PlaceError.MoveIntoDescendant _)
    | Error(PlaceError.ParentNotFound _)
    | Error(PlaceError.ChildlessKind _) ->
        // Every one of these classes is a refusal of the bare MoveNode itself.
        Apply.apply naive t |> Result.isError
    | Error(PlaceError.UnknownAnchor a) ->
        anchorOf target.Placement = Some a
        && anchorNotASibling t target.ParentId moved a
    | Error _ -> false

let private canPlaceAgreesWithMoveOp (t: Node<obj>) (moved: NodeId) (target: Target) : bool =
    match Placement.canPlace t moved target, Placement.moveOp t moved target with
    | Ok(), Ok _ -> true
    | Error a, Error b -> a = b
    | _ -> false

let private nudgeCorresponds (t: Node<obj>) (node: NodeId) (delta: int) : bool =
    match Placement.nudgeOp t node delta with
    | Ok op ->
        match Introspect.findParent node t, Apply.apply op t with
        | Some(parent, idx), Ok updated ->
            let before = childNodeIds t (NodeId parent.Id)
            let after = childNodeIds updated (NodeId parent.Id)

            after = (before
                     |> List.mapi (fun i id ->
                         if i = idx then before[idx + delta]
                         elif i = idx + delta then before[idx]
                         else id))
        | _ -> false
    | Error(PlaceError.CannotNudgeRoot n) -> n = node && NodeId t.Id = node
    | Error(PlaceError.NodeNotFound _) -> NodeId t.Id <> node && Introspect.findParent node t |> Option.isNone
    | Error(PlaceError.NudgeOutOfRange _) ->
        match Introspect.findParent node t with
        | None -> false
        | Some(parent, idx) ->
            let len = childNodeIds t (NodeId parent.Id) |> List.length
            idx + delta < 0 || idx + delta >= len
    | Error _ -> false

let private duplicateCorresponds (t: Node<obj>) (source: NodeId) (target: Target) : bool =
    match Placement.duplicateOp t source target with
    | Ok op ->
        match Apply.apply op t with
        | Error _ -> false
        | Ok updated ->
            let sourceNode =
                Introspect.findNode source t
                |> Option.defaultWith (fun () -> failwith "source vanished")

            let ids = rawIds updated

            List.length ids = List.length (List.distinct ids)
            && List.length ids = List.length (rawIds t) + List.length (Introspect.allNodeIds sourceNode)
            && (match op with
                | TreeOp.InsertChild(_, clone)
                | TreeOp.Batch [ TreeOp.InsertChild(_, clone); _ ] -> kindShape clone = kindShape sourceNode
                | _ -> false)
    | Error(PlaceError.NodeNotFound n) -> n = source && Introspect.findNode source t |> Option.isNone
    | Error(PlaceError.ParentNotFound p) -> p = target.ParentId && Introspect.findNode p t |> Option.isNone
    | Error(PlaceError.ChildlessKind p) ->
        (Introspect.findNode p t
         |> Option.map (fun n -> Introspect.getChildren n.Kind |> Option.isNone)
         |> Option.defaultValue false)
    | Error(PlaceError.UnknownAnchor a) ->
        // The clone's fresh root id is never its own anchor, so exclusion is moot.
        anchorOf target.Placement = Some a
        && anchorNotASibling t target.ParentId (NodeId "«none»") a
    | Error _ -> false

let private pasteCorresponds (tA: Node<obj>) (tB: Node<obj>) (source: NodeId) (target: Target) : bool =
    match Introspect.findNode source tA with
    | None -> true // only tree ids are generated; ghost source is not the paste contract under test
    | Some lifted ->
        match Placement.pasteOp tB lifted target with
        | Ok op ->
            match Apply.apply op tB with
            | Error _ -> false
            | Ok updated ->
                let ids = rawIds updated
                let before = rawIds tB |> Set.ofList

                let preserved =
                    rawIds lifted |> List.filter (fun id -> not (Set.contains id before))

                List.length ids = List.length (List.distinct ids)
                && (preserved |> List.forall (fun id -> List.contains id ids))
        | Error(PlaceError.ParentNotFound p) -> Introspect.findNode p tB |> Option.isNone
        | Error(PlaceError.ChildlessKind p) ->
            (Introspect.findNode p tB
             |> Option.map (fun n -> Introspect.getChildren n.Kind |> Option.isNone)
             |> Option.defaultValue false)
        | Error(PlaceError.UnknownAnchor a) -> anchorOf target.Placement = Some a
        | Error _ -> false

let private config = Config.QuickThrowOnFailure.WithMaxTest(400)

[<Tests>]
let propertyTests =
    testList
        "Placement properties"
        [ testCase "placeOp: emitted ops apply to the declared order; refusals mirror the apply engine" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! target = genTarget t
                      return t, target
                  }

              Check.One(config, Prop.forAll (Arb.fromGen scenarios) (fun (t, target) -> placeCorresponds t target)))

          testCase "moveOp: emitted ops apply to the declared order; refusals mirror the apply engine" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! moved = genPick t
                      let! target = genTarget t
                      return t, moved, target
                  }

              Check.One(
                  config,
                  Prop.forAll (Arb.fromGen scenarios) (fun (t, moved, target) -> moveCorresponds t moved target)
              ))

          testCase "canPlace agrees with moveOp verdict-for-verdict" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! moved = genPick t
                      let! target = genTarget t
                      return t, moved, target
                  }

              Check.One(
                  config,
                  Prop.forAll (Arb.fromGen scenarios) (fun (t, moved, target) ->
                      canPlaceAgreesWithMoveOp t moved target)
              ))

          testCase "nudgeOp: the swap lands where declared; refusals are honest" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! node = genPick t
                      let! delta = Gen.choose (-2, 2)
                      return t, node, delta
                  }

              Check.One(
                  config,
                  Prop.forAll (Arb.fromGen scenarios) (fun (t, node, delta) -> nudgeCorresponds t node delta)
              ))

          testCase "duplicateOp: never collides, grows by exactly the subtree, clones the shape" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! source = genPick t
                      let! target = genTarget t
                      return t, source, target
                  }

              Check.One(
                  config,
                  Prop.forAll (Arb.fromGen scenarios) (fun (t, source, target) -> duplicateCorresponds t source target)
              ))

          testCase "pasteOp: colliding ids remap, non-colliding ids survive, no duplicates" (fun () ->
              let scenarios =
                  gen {
                      let! tA = genTree
                      let! tB = genTree
                      let! source = genPick tA
                      let! target = genTarget tB
                      return tA, tB, source, target
                  }

              Check.One(
                  config,
                  Prop.forAll (Arb.fromGen scenarios) (fun (tA, tB, source, target) ->
                      pasteCorresponds tA tB source target)
              )) ]
