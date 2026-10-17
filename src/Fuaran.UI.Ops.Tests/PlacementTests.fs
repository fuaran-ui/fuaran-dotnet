module Fuaran.UI.Tests.PlacementTests

// ============================================================================
//  Placement acceptance + property set.
//
//  The verbs are Fuaran.Core's `TreePlacement` over this tier's witnesses
//  (Phase 2306), so two obligations are stated as properties over generated
//  trees and checked against the REAL apply engine (never a re-derivation of
//  its logic):
//
//   (1) No false permit — every op a verb emits is accepted by the apply
//       engine, and the applied tree exhibits the anchor's declared position
//       (the moved/inserted node sits exactly where the `Anchor` said, with
//       the other siblings' order preserved). A move within one parent emits
//       a bare `ReorderChildren`, or nothing when the node is already there.
//
//   (2) No false refuse — every `Refused` carries the engine's own rejection
//       for the op the verb would otherwise have emitted, and the anchor
//       refusals (`UnknownAnchor` / `IndexOutOfRange`) name an anchor that
//       genuinely names no position.
//
//  The clone verbs add the tree-wide id obligations: a duplicate never
//  collides with any id in the target tree (including ids held in
//  non-structural positions), the clone is structurally equal to its source
//  modulo ids, and a paste preserves non-colliding ids while renaming
//  colliding ones.
// ============================================================================

open Expecto
open FsCheck
open FsCheck.FSharp
open Fuaran.Core
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
let private expectRefusal (result: Result<'T, PlaceError>) (expected: PlaceError) (message: string) =
    match result with
    | Error e -> Expect.equal e expected message
    | Ok _ -> failtestf "expected the refusal %A, got Ok (%s)" expected message

/// A refusal carrying the engine's own rejection, matched by `expected` (an
/// `UnknownNode` rejection enumerates every addressable id, so it is matched
/// rather than spelt out).
let private expectRefused (result: Result<'T, PlaceError>) (expected: Rejection<NodeId> -> bool) (message: string) =
    match result with
    | Error(PlaceError.Refused r) when expected r -> ()
    | Error e -> failtestf "expected an engine refusal (%s), got %A" message e
    | Ok _ -> failtestf "expected an engine refusal, got Ok (%s)" message

let private unknownNode (id: string) (r: Rejection<NodeId>) =
    match r with
    | Rejection.UnknownNode(NodeId n, _) -> n = id
    | _ -> false

let private notAContainer (id: string) (r: Rejection<NodeId>) =
    match r with
    | Rejection.NotAContainer(NodeId n, _) -> n = id
    | _ -> false

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

let private at (parentId: string) (placement: Anchor<NodeId>) : Target =
    { ParentId = NodeId parentId
      Placement = placement }

let private ids (raw: string list) : NodeId list = raw |> List.map NodeId

// ─── Unit tests: placeOp ─────────────────────────────────────────────────────

[<Tests>]
let placeOpTests =
    testList
        "Placement.placeOp"
        [ test "Last emits a bare InsertChild and appends" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" Anchor.Last) with
              | Ok(TreeOp.InsertChild _ as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "a"; "b"; "c"; "x" ] "appended last"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          test "First emits Batch [insert; reorder] and lands first" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" Anchor.First) with
              | Ok(TreeOp.Batch [ TreeOp.InsertChild _; TreeOp.ReorderChildren _ ] as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "x"; "a"; "b"; "c" ] "prepended"
              | other -> failtestf "expected Batch [InsertChild; ReorderChildren], got %A" other
          }

          test "First into an empty container stays a bare InsertChild" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "empty" Anchor.First) with
              | Ok(TreeOp.InsertChild _ as op) -> Expect.equal (childIdsIn (applied op t) "empty") [ "x" ] "sole child"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          test "Before an interior sibling lands immediately before it" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" (Anchor.Before(NodeId "b"))) with
              | Ok op -> Expect.equal (childIdsIn (applied op t) "left") [ "a"; "x"; "b"; "c" ] "before b"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "After the last sibling stays a bare InsertChild" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" (Anchor.After(NodeId "c"))) with
              | Ok(TreeOp.InsertChild _ as op) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "a"; "b"; "c"; "x" ] "after c = append"
              | other -> failtestf "expected a bare InsertChild, got %A" other
          }

          // Phase 2306 — the anchor Core's placement has and the tier's own did not.
          test "an Index anchor lands at that position, and the count itself is last" {
              let t = fixture ()

              match Placement.placeOp t (leaf "x") (at "left" (Anchor.Index 1)) with
              | Ok op -> Expect.equal (childIdsIn (applied op t) "left") [ "a"; "x"; "b"; "c" ] "index 1"
              | Error e -> failtestf "expected Ok, got %A" e

              match Placement.placeOp t (leaf "x") (at "left" (Anchor.Index 3)) with
              | Ok(TreeOp.InsertChild _) -> ()
              | other -> failtestf "index = count is last, a bare InsertChild; got %A" other
          }

          test "an Index past the end is refused by Core, naming the count" {
              let t = fixture ()

              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "left" (Anchor.Index 4)))
                  (PlaceError.IndexOutOfRange(NodeId "left", 4, 3))
                  "index 4 among three siblings"

              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "left" (Anchor.Index -1)))
                  (PlaceError.IndexOutOfRange(NodeId "left", -1, 3))
                  "a negative index"
          }

          test "absent parent is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefused
                  (Placement.placeOp t (leaf "x") (at "ghost" Anchor.Last))
                  (unknownNode "ghost")
                  "unknown parent"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "ghost", leaf "x")) t)
                  ApplyErrorCode.ParentNotFound
                  "apply-side twin"
          }

          test "childless parent is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefused
                  (Placement.placeOp t (leaf "x") (at "solo" Anchor.Last))
                  (notAContainer "solo")
                  "a leaf holds no children"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "solo", leaf "x")) t)
                  ApplyErrorCode.ChildlessKind
                  "apply-side twin"
          }

          test "duplicate id is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefusal
                  (Placement.placeOp t (leaf "a") (at "right" Anchor.Last))
                  (PlaceError.Refused(Rejection.DuplicateId(NodeId "a")))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.InsertChild(NodeId "right", leaf "a")) t)
                  ApplyErrorCode.DuplicateNodeId
                  "apply-side twin"
          }

          test "an anchor that is not a destination child is refused, naming the children that are" {
              let t = fixture ()

              // "d" exists in the tree but is not a child of "left".
              expectRefusal
                  (Placement.placeOp t (leaf "x") (at "left" (Anchor.Before(NodeId "d"))))
                  (PlaceError.UnknownAnchor(NodeId "left", NodeId "d", ids [ "a"; "b"; "c" ]))
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

              match Placement.moveOp t (NodeId "a") (at "right" Anchor.Last) with
              | Ok(Some(TreeOp.MoveNode _ as op)) ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "right") [ "d"; "a" ] "moved in"
                  Expect.equal (childIdsIn updated "left") [ "b"; "c" ] "moved out"
              | other -> failtestf "expected a bare MoveNode, got %A" other
          }

          test "cross-parent anywhere but last emits Batch [move; reorder]" {
              let t = fixture ()

              match Placement.moveOp t (NodeId "a") (at "right" Anchor.First) with
              | Ok(Some(TreeOp.Batch [ TreeOp.MoveNode _; TreeOp.ReorderChildren _ ] as op)) ->
                  Expect.equal (childIdsIn (applied op t) "right") [ "a"; "d" ] "a to the head of right"
              | other -> failtestf "expected Batch [MoveNode; ReorderChildren], got %A" other
          }

          // Phase 2306 — the shape change this phase exists for. The tier used to
          // emit `Batch [MoveNode; ReorderChildren]` here; a reorder footprints the
          // parent alone, where a move also writes the node and an unknown parent.
          test "same-parent re-placement emits a bare ReorderChildren" {
              let t = fixture ()

              match Placement.moveOp t (NodeId "c") (at "left" (Anchor.Before(NodeId "a"))) with
              | Ok(Some(TreeOp.ReorderChildren(parent, order) as op)) ->
                  Expect.equal parent (NodeId "left") "the parent it stays in"
                  Expect.equal order (ids [ "c"; "a"; "b" ]) "the whole order, by id"
                  Expect.equal (childIdsIn (applied op t) "left") [ "c"; "a"; "b" ] "c to the head"
              | other -> failtestf "expected a bare ReorderChildren, got %A" other
          }

          test "a same-parent move to where the node already is emits nothing" {
              let t = fixture ()

              for anchor in [ Anchor.Last; Anchor.After(NodeId "b"); Anchor.Index 2 ] do
                  match Placement.moveOp t (NodeId "c") (at "left" anchor) with
                  | Ok None -> ()
                  | other -> failtestf "%A: expected no op for a node already in place, got %A" anchor other
          }

          test "move into itself is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "left") (at "left" Anchor.Last))
                  (PlaceError.Refused(Rejection.WouldNestUnderSelf(NodeId "left", NestRelation.Self)))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "left", NodeId "left")) t)
                  ApplyErrorCode.KindMismatch
                  "apply-side twin"
          }

          test "move into a descendant is refused with the engine's own rejection" {
              let t = container "root" [ container "outer" [ container "inner" [] ] ]

              expectRefusal
                  (Placement.moveOp t (NodeId "outer") (at "inner" Anchor.Last))
                  (PlaceError.Refused(Rejection.WouldNestUnderSelf(NodeId "outer", NestRelation.Descendant)))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "outer", NodeId "inner")) t)
                  ApplyErrorCode.KindMismatch
                  "apply-side twin"
          }

          test "moving the root is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "root") (at "left" Anchor.Last))
                  (PlaceError.Refused Rejection.CannotRemoveRoot)
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "root", NodeId "left")) t)
                  ApplyErrorCode.KindMismatch
                  "apply-side twin"
          }

          test "absent node is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefused
                  (Placement.moveOp t (NodeId "ghost") (at "left" Anchor.Last))
                  (unknownNode "ghost")
                  "unknown node"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "ghost", NodeId "left")) t)
                  ApplyErrorCode.NodeNotFound
                  "apply-side twin"
          }

          test "childless destination is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefused
                  (Placement.moveOp t (NodeId "a") (at "solo" Anchor.Last))
                  (notAContainer "solo")
                  "a leaf holds no children"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "a", NodeId "solo")) t)
                  ApplyErrorCode.ChildlessKind
                  "apply-side twin"
          }

          test "anchoring a move on the moved node itself is an unknown anchor" {
              let t = fixture ()

              expectRefusal
                  (Placement.moveOp t (NodeId "a") (at "right" (Anchor.After(NodeId "a"))))
                  (PlaceError.UnknownAnchor(NodeId "right", NodeId "a", ids [ "d" ]))
                  "the moved node is not among its own destination siblings"
          }

          // Phase 1666 — a node at a keyed position is not movable, and the
          // refusal names the position whose arity the op would change. The
          // keyed engine names the HOLDER (`KeyedPosition`); the slot label is
          // this tier's, derived from its own lens (fuaran-core#492).
          test "a node AT a non-structural position is not movable — refused as the engine refuses it" {
              let t =
                  container "root" [ Node.onLoading (leaf "ph") (leaf "m"); container "box" [] ]

              expectRefusal
                  (Placement.moveOp t (NodeId "ph") (at "box" Anchor.Last))
                  (PlaceError.PositionNotStructural(NodeId "ph", "state.onLoading"))
                  "helper refusal"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "ph", NodeId "box")) t)
                  (ApplyErrorCode.PositionNotStructural "state.onLoading")
                  "apply-side twin"
          }

          // Phase 1666 — the three answers drawn apart, each paired with its
          // apply-side twin. The pairing is the point: a helper that refuses
          // what the engine permits greys out a legal drop, and one that
          // permits what the engine refuses emits an op that fails after the
          // user has committed to the gesture.
          test "a node BELOW a non-structural position moves WITHIN it — helper and engine agree" {
              let t =
                  container "root" [ Node.onLoading (container "panel" [ leaf "p1"; container "inner" [] ]) (leaf "m") ]

              match Placement.moveOp t (NodeId "p1") (at "inner" Anchor.Last) with
              | Error e -> failtestf "a legal move below a keyed position was greyed out: %A" e
              | Ok None -> failtest "a cross-parent move cannot be a no-op"
              | Ok(Some op) ->
                  match Apply.apply op t with
                  | Error err -> failtestf "the helper emitted an op the engine refused: %A %s" err.Code err.Message
                  | Ok updated ->
                      match Introspect.findNode (NodeId "inner") updated with
                      | Some inner ->
                          Expect.equal
                              (Introspect.getChildren inner.Kind |> Option.defaultValue [] |> List.map _.Id)
                              [ "p1" ]
                              "the moved node landed in its new parent, still inside the position"
                      | None -> failtest "the destination went missing"
          }

          test "a same-parent move below a non-structural position is a bare reorder the engine accepts" {
              let t =
                  container "root" [ Node.onLoading (container "panel" [ leaf "p1"; leaf "p2" ]) (leaf "m") ]

              match Placement.moveOp t (NodeId "p2") (at "panel" Anchor.First) with
              | Ok(Some(TreeOp.ReorderChildren _ as op)) ->
                  match Introspect.findNode (NodeId "panel") (applied op t) with
                  | Some panel ->
                      Expect.equal
                          (Introspect.getChildren panel.Kind |> Option.defaultValue [] |> List.map _.Id)
                          [ "p2"; "p1" ]
                          "reordered inside the position"
                  | None -> failtest "the position lost its node"
              | other -> failtestf "expected a bare ReorderChildren, got %A" other
          }

          test "a move OUT of a non-structural position is refused as the engine refuses it" {
              let t =
                  container
                      "root"
                      [ Node.onLoading (container "panel" [ leaf "p1" ]) (leaf "m")
                        container "box" [] ]

              expectRefusal
                  (Placement.moveOp t (NodeId "p1") (at "box" Anchor.Last))
                  (PlaceError.PositionNotStructural(NodeId "p1", "state.onLoading"))
                  "helper refusal — the op would cross the position"

              match refusedAs (TreeOp.MoveNode(NodeId "p1", NodeId "box")) t with
              | ApplyErrorCode.PositionNotStructural _ -> ()
              | other -> failtestf "apply-side twin: expected PositionNotStructural, got %A" other
          }

          test "an absent node is still absence, not swept into the position refusal" {
              // The two answers must remain tellable apart: absence is the
              // engine's `UnknownNode`, never the position refusal.
              let t =
                  container "root" [ Node.onLoading (leaf "ph") (leaf "m"); container "box" [] ]

              expectRefused
                  (Placement.moveOp t (NodeId "nowhere") (at "box" Anchor.Last))
                  (unknownNode "nowhere")
                  "unknown node"

              Expect.equal
                  (refusedAs (TreeOp.MoveNode(NodeId "nowhere", NodeId "box")) t)
                  ApplyErrorCode.NodeNotFound
                  "apply-side twin"
          }

          test "nudge reaches below a keyed position, and refuses the position's own node by name" {
              let t =
                  container "root" [ Node.onLoading (container "panel" [ leaf "p1"; leaf "p2" ]) (leaf "m") ]

              match Placement.nudgeOp t (NodeId "p2") -1 with
              | Error e -> failtestf "a legal nudge below a keyed position was refused: %A" e
              | Ok None -> failtest "a one-place nudge cannot be a no-op"
              | Ok(Some op) ->
                  match Apply.apply op t with
                  | Error err -> failtestf "the helper emitted a nudge the engine refused: %A %s" err.Code err.Message
                  | Ok updated ->
                      match Introspect.findNode (NodeId "panel") updated with
                      | Some panel ->
                          Expect.equal
                              (Introspect.getChildren panel.Kind |> Option.defaultValue [] |> List.map _.Id)
                              [ "p2"; "p1" ]
                              "the two children swapped inside the position"
                      | None -> failtest "the position lost its node"

              expectRefusal
                  (Placement.nudgeOp t (NodeId "panel") 1)
                  (PlaceError.PositionNotStructural(NodeId "panel", "state.onLoading"))
                  "the position's own node has no sibling list to nudge among"
          }

          test "canPlace is moveOp's verdict" {
              let t = fixture ()

              Expect.equal
                  (Placement.canPlace t (NodeId "a") (at "right" (Anchor.Before(NodeId "d"))))
                  (Ok())
                  "legal drop"

              Expect.equal
                  (Placement.canPlace t (NodeId "c") (at "left" Anchor.Last))
                  (Ok())
                  "a drop where the node already is is legal — and emits nothing"

              expectRefused
                  (Placement.canPlace t (NodeId "a") (at "solo" Anchor.Last))
                  (notAContainer "solo")
                  "illegal drop"
          } ]

// ─── Unit tests: reorderOp ───────────────────────────────────────────────────

[<Tests>]
let reorderOpTests =
    testList
        "Placement.reorderOp"
        [ test "a changed order becomes one bare ReorderChildren the engine accepts" {
              let t = fixture ()
              let ids = [ NodeId "c"; NodeId "a"; NodeId "b" ]

              match Placement.reorderOp (NodeId "left") [ NodeId "a"; NodeId "b"; NodeId "c" ] ids with
              | Some(TreeOp.ReorderChildren(parent, order) as op) ->
                  Expect.equal parent (NodeId "left") "the parent it was given"
                  Expect.equal order ids "the order it was given, verbatim"
                  // Checked against the REAL apply engine, never a re-derivation.
                  Expect.equal (childIdsIn (applied op t) "left") [ "c"; "a"; "b" ] "the tree ends up in that order"
              | other -> failtestf "expected Some ReorderChildren, got %A" other
          }

          test "an UNCHANGED order emits nothing at all" {
              // The identity-drop, which is the reason this helper is worth
              // packaging: a ReorderChildren restating the existing order is
              // accepted by the engine and changes nothing, so it lands in the
              // op-stream as an edit that never happened.
              let same = [ NodeId "a"; NodeId "b"; NodeId "c" ]

              Expect.isNone
                  (Placement.reorderOp (NodeId "left") same same: TreeOp<obj> option)
                  "no op for an order that is already what was asked for"
          }

          test "the drop is by ORDER, not by set membership" {
              // A permutation of the same ids is a real change and must survive;
              // proving the previous test passes because the ORDERS matched, not
              // because the helper compares sets or lengths.
              let current = [ NodeId "a"; NodeId "b" ]
              let swapped = [ NodeId "b"; NodeId "a" ]

              Expect.isSome
                  (Placement.reorderOp (NodeId "left") current swapped: TreeOp<obj> option)
                  "the same ids in a different order is a change"
          }

          test "a single child is always identity, and an empty parent likewise" {
              Expect.isNone
                  (Placement.reorderOp (NodeId "left") [ NodeId "a" ] [ NodeId "a" ]: TreeOp<obj> option)
                  "one child cannot be reordered"

              Expect.isNone (Placement.reorderOp (NodeId "left") [] []: TreeOp<obj> option) "nor can none"
          }

          test "a PARTIAL order is emitted and refused by the engine, not by the helper" {
              // The stated contract: the permutation obligation is the caller's,
              // and the apply engine is the enforcer. Asserting the refusal here
              // pins the division of labour rather than leaving it as prose —
              // and shows what a caller that gets it wrong actually sees.
              let t = fixture ()
              let partial_ = [ NodeId "a"; NodeId "b" ]

              match Placement.reorderOp (NodeId "left") [ NodeId "a"; NodeId "b"; NodeId "c" ] partial_ with
              | Some op ->
                  match Apply.apply op t with
                  | Error e -> Expect.equal e.Code ApplyErrorCode.OrderingMismatch "the engine refuses a partial order"
                  | Ok _ -> failtest "expected the apply engine to refuse a partial order"
              | None -> failtest "the helper drops only the IDENTITY order, never a wrong one"
          } ]


// ─── Unit tests: nudgeOp ─────────────────────────────────────────────────────

[<Tests>]
let nudgeOpTests =
    testList
        "Placement.nudgeOp"
        [ test "-1 swaps the node with its previous sibling via the full permutation" {
              let t = fixture ()

              match Placement.nudgeOp t (NodeId "b") -1 with
              | Ok(Some(TreeOp.ReorderChildren _ as op)) ->
                  Expect.equal (childIdsIn (applied op t) "left") [ "b"; "a"; "c" ] "b up"
              | other -> failtestf "expected ReorderChildren, got %A" other
          }

          // Phase 2306 — a nudge is Core's move to `Index (index + delta)`, so a
          // delta of two SHIFTS the node two places; it used to swap it with the
          // sibling two places away ([c; b; a]).
          test "+2 shifts the node two places — a move, not a swap" {
              let t = fixture ()

              match Placement.nudgeOp t (NodeId "a") 2 with
              | Ok(Some op) -> Expect.equal (childIdsIn (applied op t) "left") [ "b"; "c"; "a" ] "a shifted to the end"
              | other -> failtestf "expected Ok Some, got %A" other
          }

          test "a zero nudge emits nothing" {
              let t = fixture ()

              match Placement.nudgeOp t (NodeId "b") 0 with
              | Ok None -> ()
              | other -> failtestf "expected no op, got %A" other
          }

          test "the first sibling cannot move up" {
              let t = fixture ()

              expectRefusal
                  (Placement.nudgeOp t (NodeId "a") -1)
                  (PlaceError.IndexOutOfRange(NodeId "left", -1, 2))
                  "already first"
          }

          test "the last sibling cannot move down" {
              let t = fixture ()

              expectRefusal
                  (Placement.nudgeOp t (NodeId "c") 1)
                  (PlaceError.IndexOutOfRange(NodeId "left", 3, 2))
                  "already last"
          }

          test "the root has no siblings to nudge among" {
              let t = fixture ()

              expectRefusal (Placement.nudgeOp t (NodeId "root") 1) (PlaceError.CannotNudgeRoot(NodeId "root")) "root"
          }

          test "an absent node is refused with the engine's own rejection" {
              let t = fixture ()

              expectRefused (Placement.nudgeOp t (NodeId "ghost") 1) (unknownNode "ghost") "ghost"
          } ]

// ─── Unit tests: duplicateOp / pasteOp ──────────────────────────────────────

[<Tests>]
let cloneVerbTests =
    testList
        "Placement.duplicateOp / pasteOp"
        [ test "duplicate places a fresh-id clone beside its source" {
              let t = fixture ()

              match Placement.duplicateOp t (NodeId "left") (at "root" (Anchor.After(NodeId "left"))) with
              | Ok op ->
                  let updated = applied op t

                  Expect.equal
                      (childIdsIn updated "root")
                      [ "left"; "left-copy"; "solo"; "right"; "empty" ]
                      "clone lands immediately after its source"

                  Expect.equal
                      (childIdsIn updated "left-copy")
                      [ "a-copy"; "b-copy"; "c-copy" ]
                      "interior ids renamed by Core's derived strategy"

                  Expect.isTrue (allDistinct updated) "no id collides anywhere in the tree"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate is structurally equal to its source modulo ids" {
              let t = fixture ()

              match Placement.duplicateOp t (NodeId "left") (at "right" Anchor.Last) with
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
                  Placement.duplicateOpWith
                      (FreshIds.sequential Introspect.idWitness "dup")
                      t
                      (NodeId "left")
                      (at "root" Anchor.Last)
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

          test "duplicate renames ids held in non-structural positions too" {
              // `ph` lives in a State slot — invisible to the structural child
              // lists, but inside the tree-wide id-uniqueness contract.
              let t = container "root" [ Node.onLoading (leaf "ph") (leaf "m") ]

              match Placement.duplicateOp t (NodeId "m") (at "root" Anchor.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.isTrue (allDistinct updated) "the State-slot id was renamed, not smuggled"
                  Expect.contains (rawIds updated) "ph-copy" "the placeholder clone carries a fresh id"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate reaches a source held in a non-structural position" {
              let t = container "root" [ Node.onLoading (leaf "ph") (leaf "m") ]

              match Placement.duplicateOp t (NodeId "ph") (at "root" Anchor.Last) with
              | Ok op ->
                  Expect.equal (childIdsIn (applied op t) "root") [ "m"; "ph-copy" ] "the copy lands on the spine"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "duplicate of an absent source is refused with the engine's rejection" {
              let t = fixture ()

              expectRefused
                  (Placement.duplicateOp t (NodeId "ghost") (at "root" Anchor.Last))
                  (unknownNode "ghost")
                  "ghost"
          }

          test "paste renames colliding ids and preserves the rest" {
              let t = fixture ()

              // Lifted from a different tree: "left" and "a" collide with the
              // target; "z" does not.
              let foreign = container "left" [ leaf "a"; leaf "z" ]

              match Placement.pasteOp t foreign (at "right" Anchor.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "right") [ "d"; "left-copy" ] "pasted under right"
                  Expect.equal (childIdsIn updated "left-copy") [ "a-copy"; "z" ] "collisions renamed, z preserved"
                  Expect.isTrue (allDistinct updated) "no id collides anywhere in the tree"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          test "paste with no collisions preserves every id" {
              let t = fixture ()
              let foreign = container "p" [ leaf "q" ]

              match Placement.pasteOp t foreign (at "empty" Anchor.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "empty") [ "p" ] "pasted"
                  Expect.equal (childIdsIn updated "p") [ "q" ] "interior id untouched"
              | Error e -> failtestf "expected Ok, got %A" e
          }

          // Core's `repairDuplicates` also repairs an id the subtree repeats
          // within itself — the later occurrence is renamed.
          test "paste renames an id the incoming subtree repeats within itself" {
              let t = fixture ()
              let foreign = container "p" [ leaf "q"; leaf "q" ]

              match Placement.pasteOp t foreign (at "empty" Anchor.Last) with
              | Ok op ->
                  let updated = applied op t
                  Expect.equal (childIdsIn updated "p") [ "q"; "q-copy" ] "the second q renamed"
                  Expect.isTrue (allDistinct updated) "no id collides anywhere in the tree"
              | Error e -> failtestf "expected Ok, got %A" e
          } ]

// ─── Property tests ──────────────────────────────────────────────────────────
//
// Generated trees are Box containers over Markdown leaves with sequential
// preorder ids (`n1`, `n2`, …) under a fixed `root` container; parents,
// anchors, and moved nodes are drawn from the tree's OWN ids plus a `ghost`,
// and indices from just outside the range on either side, so both the legal
// and every illegal class is generated.

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
                [ Gen.constant Anchor.Last
                  Gen.constant Anchor.First
                  Gen.map Anchor.Before (genPick t)
                  Gen.map Anchor.After (genPick t)
                  Gen.map Anchor.Index (Gen.choose (-1, 5)) ]

        return
            { ParentId = parent
              Placement = placement }
    }

let private childNodeIds (root: Node<obj>) (parentId: NodeId) : NodeId list =
    Introspect.findNode parentId root
    |> Option.bind (fun p -> Introspect.getChildren p.Kind)
    |> Option.defaultValue []
    |> List.map (fun c -> NodeId c.Id)

/// The moved/inserted node sits exactly where the anchor declared, and the
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
            | Anchor.Last -> idx = List.length ids - 1
            | Anchor.First -> idx = 0
            | Anchor.Before a -> idx + 1 < List.length ids && ids[idx + 1] = a
            | Anchor.After a -> idx > 0 && ids[idx - 1] = a
            | Anchor.Index i -> idx = i)

/// The destination's children other than `moved` — what an anchor positions among.
let private othersIn (t: Node<obj>) (parentId: NodeId) (moved: NodeId) : NodeId list =
    childNodeIds t parentId |> List.filter (fun id -> id <> moved)

/// An anchor refusal is honest: it names the destination, the anchor the
/// target stated, and an anchor that genuinely names no position.
let private anchorRefusalHonest (t: Node<obj>) (moved: NodeId) (target: Target) (error: PlaceError) : bool =
    let others = othersIn t target.ParentId moved

    match error, target.Placement with
    | PlaceError.UnknownAnchor(p, a, siblings), (Anchor.Before stated | Anchor.After stated) ->
        p = target.ParentId
        && a = stated
        && siblings = others
        && not (List.contains a others)
    | PlaceError.IndexOutOfRange(p, i, count), Anchor.Index stated ->
        p = target.ParentId
        && i = stated
        && count = List.length others
        && (i < 0 || i > count)
    | _ -> false

let private placeCorresponds (t: Node<obj>) (target: Target) : bool =
    let fresh = leaf "fresh-child"
    let insert = TreeOp.InsertChild(target.ParentId, fresh)

    match Placement.placeOp t fresh target with
    | Ok op ->
        match Apply.apply op t with
        | Error _ -> false
        | Ok updated -> declaredOrderHolds t updated (NodeId "fresh-child") target
    // The engine's own refusal of the bare insert.
    | Error(PlaceError.Refused _) -> Apply.apply insert t |> Result.isError
    | Error e ->
        Apply.apply insert t |> Result.isOk
        && anchorRefusalHonest t (NodeId "fresh-child") target e

let private moveCorresponds (t: Node<obj>) (moved: NodeId) (target: Target) : bool =
    let naive = TreeOp.MoveNode(moved, target.ParentId)

    let sameParent =
        match Introspect.findParent moved t with
        | Some(p, _) -> NodeId p.Id = target.ParentId
        | None -> false

    match Placement.moveOp t moved target with
    | Ok None ->
        // Nothing to do: the node is already in that parent, at that position.
        sameParent && declaredOrderHolds t t moved target
    | Ok(Some op) ->
        // A same-parent move is a bare reorder — the minimal op.
        let minimal =
            not sameParent
            || (match op with
                | TreeOp.ReorderChildren _ -> true
                | _ -> false)

        minimal
        && (match Apply.apply op t with
            | Error _ -> false
            | Ok updated ->
                declaredOrderHolds t updated moved target
                && (match Introspect.findParent moved updated with
                    | Some(p, _) -> NodeId p.Id = target.ParentId
                    | None -> false))
    // Every engine refusal is a refusal of the bare MoveNode itself.
    | Error(PlaceError.Refused _) -> Apply.apply naive t |> Result.isError
    | Error e -> Apply.apply naive t |> Result.isOk && anchorRefusalHonest t moved target e

let private canPlaceAgreesWithMoveOp (t: Node<obj>) (moved: NodeId) (target: Target) : bool =
    match Placement.canPlace t moved target, Placement.moveOp t moved target with
    | Ok(), Ok _ -> true
    | Error a, Error b -> a = b
    | _ -> false

let private nudgeCorresponds (t: Node<obj>) (node: NodeId) (delta: int) : bool =
    match Placement.nudgeOp t node delta with
    | Ok None -> delta = 0 && Introspect.findParent node t |> Option.isSome
    | Ok(Some op) ->
        match Introspect.findParent node t, Apply.apply op t with
        | Some(parent, idx), Ok updated ->
            let before = childNodeIds t (NodeId parent.Id)
            let after = childNodeIds updated (NodeId parent.Id)
            // The node shifted `delta` places; every other sibling kept its order.
            after = List.insertAt (idx + delta) node (before |> List.filter (fun id -> id <> node))
        | _ -> false
    | Error(PlaceError.CannotNudgeRoot n) -> n = node && NodeId t.Id = node
    | Error(PlaceError.Refused(Rejection.UnknownNode(n, _))) -> n = node && Introspect.findNode node t |> Option.isNone
    | Error(PlaceError.IndexOutOfRange(p, i, count)) ->
        match Introspect.findParent node t with
        | None -> false
        | Some(parent, idx) ->
            let len = childNodeIds t (NodeId parent.Id) |> List.length

            p = NodeId parent.Id
            && i = idx + delta
            && count = len - 1
            && (i < 0 || i > count)
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
    | Error(PlaceError.Refused(Rejection.UnknownNode(n, _))) when n = source ->
        Introspect.findNode source t |> Option.isNone
    // The clone's insert refused: an absent or childless destination.
    | Error(PlaceError.Refused _) ->
        Introspect.findNode target.ParentId t
        |> Option.map (fun n -> Introspect.getChildren n.Kind |> Option.isNone)
        |> Option.defaultValue true
    // The clone's fresh root id is never its own anchor, so exclusion is moot.
    | Error e -> anchorRefusalHonest t (NodeId "«none»") target e

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
        | Error(PlaceError.Refused _) ->
            Introspect.findNode target.ParentId tB
            |> Option.map (fun n -> Introspect.getChildren n.Kind |> Option.isNone)
            |> Option.defaultValue true
        | Error e -> anchorRefusalHonest tB (NodeId "«none»") target e

let private config = Config.QuickThrowOnFailure.WithMaxTest(400)

[<Tests>]
let propertyTests =
    testList
        "Placement properties"
        [ testCase "placeOp: emitted ops apply to the declared position; refusals are the engine's" (fun () ->
              let scenarios =
                  gen {
                      let! t = genTree
                      let! target = genTarget t
                      return t, target
                  }

              Check.One(config, Prop.forAll (Arb.fromGen scenarios) (fun (t, target) -> placeCorresponds t target)))

          testCase "moveOp: emitted ops apply to the declared position, minimally; refusals are the engine's" (fun () ->
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

          testCase "nudgeOp: the shift lands where declared; refusals are honest" (fun () ->
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

          testCase "pasteOp: colliding ids rename, non-colliding ids survive, no duplicates" (fun () ->
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
