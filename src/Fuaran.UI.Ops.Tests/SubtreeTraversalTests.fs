module Fuaran.UI.Tests.SubtreeTraversal

// ============================================================================
//  Every node in the tree must be visible to id enumeration — including nodes
//  that are not STRUCTURALLY editable.
//
//  Two different questions had been answered by one function. `Introspect.
//  getChildren` exists to say "which kinds accept InsertChild / RemoveNode /
//  MoveNode / ReorderChildren", i.e. which have a plain ordered child list. But
//  `allNodeIds` and `collectNodeIdsInto` were also built on it, so they
//  inherited its answer — and several kinds hold real child nodes in shapes
//  that are not a plain list:
//
//    * `Switch.Cases` (keyed `(match, child)` pairs) and `Switch.Default`
//    * `ErrorBoundary.Child` and `.Fallback`
//    * `StateBehaviour.OnLoading` / `.OnEmpty` — on EVERY node in the tree
//    * `FragmentArg.Slot` inside `FragmentRef.Args` and `Mount.Inputs`
//
//  None of those was enumerated, and the consequence reaches further than
//  traversal: `applyStructural` hands Fuaran.Core a `NodeWitness` whose
//  `Children` IS `getChildren`, so Core's duplicate-id rejection walked the same
//  partial view. A node id living inside a Switch case was therefore invisible
//  to the uniqueness guarantee §4g states, and an insert colliding with it was
//  accepted.
//
//  The fix keeps the two questions apart. `getChildren` still answers the
//  structural one and is unchanged — a `Switch`'s cases are keyed, so
//  `InsertChild(switchId, node)` should NOT acquire a meaning. `descendantNodes`
//  answers the traversal one, and the enumeration + uniqueness paths use it.
//
//  `StateBehaviour.OnError` stays invisible and that is correct: it is
//  `ErrorPayload -> Node`, a closure, so there is no node to enumerate until it
//  is applied.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

type Msg = Noop

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private leaf (id: string) = Fuaran.markdown id $"body of {id}"

/// A node hidden in each of the four non-structural positions.
let private inSwitchCase = leaf "hidden-in-switch-case"
let private inSwitchDefault = leaf "hidden-in-switch-default"
let private inBoundaryFallback = leaf "hidden-in-boundary-fallback"
let private inStateSlot = leaf "hidden-in-state-slot"

let private switchNode: Node<Msg> =
    Fuaran.switch
        "mode-switch"
        { Defaults.switch<Msg> with
            On = Binding.State("mode", None)
            Cases =
                [ { Match = Some "compact"
                    When = None
                    Child = inSwitchCase } ]
            Default = inSwitchDefault }

let private boundaryNode: Node<Msg> =
    Fuaran.errorBoundary
        "boundary"
        { Child = leaf "boundary-child"
          Fallback = inBoundaryFallback }

/// A plain node carrying an empty-state alternative in its `State` block.
let private nodeWithStateSlot: Node<Msg> =
    let n = leaf "has-state-slot"

    { n with
        State =
            Some
                { OnLoading = None
                  OnEmpty = Some inStateSlot
                  OnError = None } }

let private root: Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children = [ switchNode; boundaryNode; nodeWithStateSlot ] }

let private hiddenIds =
    [ "hidden-in-switch-case"
      "hidden-in-switch-default"
      "hidden-in-boundary-fallback"
      "hidden-in-state-slot" ]

[<Tests>]
let traversalTests =
    testList
        "Subtree traversal reaches non-structural node positions"
        [ test "allNodeIds enumerates nodes held in non-list positions" {
              let ids = Introspect.allNodeIds root |> List.map (fun (NodeId s) -> s)

              for hidden in hiddenIds do
                  Expect.contains ids hidden $"'{hidden}' must be visible to id enumeration"
          }

          test "findNode reaches a node inside a Switch case" {
              Expect.isSome
                  (Introspect.findNode (NodeId "hidden-in-switch-case") root)
                  "a Switch case child is part of the tree and must be findable"
          }

          test "getChildren is deliberately NOT widened — Switch stays structurally childless" {
              // The two questions stay apart. A Switch's cases are keyed, so
              // giving InsertChild a position in them would invent semantics
              // nobody asked for; the structural surface is unchanged.
              Expect.isNone (Introspect.getChildren switchNode.Kind) "Switch must not become a structural container"

              Expect.isNone
                  (Introspect.getChildren boundaryNode.Kind)
                  "ErrorBoundary must not become a structural container"
          } ]

[<Tests>]
let duplicateIdTests =
    // §4g states ids are unique per tree. Each case inserts a node whose id
    // already exists in a non-structural position; every one was accepted
    // before the traversal fix.
    let collisionCase (hidden: string) =
        test $"InsertChild is rejected when the id already exists at '{hidden}'" {
            let op = TreeOp.InsertChild(NodeId "root", leaf hidden)

            match Apply.apply op root with
            | Ok _ ->
                failtestf
                    "InsertChild accepted a node whose id '%s' already exists in the tree — the                      uniqueness guarantee does not hold for nodes held outside a plain child list"
                    hidden
            | Error err ->
                Expect.equal err.Code ApplyErrorCode.DuplicateNodeId $"expected a duplicate-id rejection for '{hidden}'"
        }

    let freshIdStillInserts =
        // The guard must reject collisions without rejecting everything: a
        // widened traversal that reported false positives would be a worse
        // defect than the one it replaced.
        test "a genuinely fresh id still inserts" {
            let op = TreeOp.InsertChild(NodeId "root", leaf "genuinely-new")

            match Apply.apply op root with
            | Ok updated -> Expect.isSome (Introspect.findNode (NodeId "genuinely-new") updated) "the new node landed"
            | Error err -> failtestf "a fresh id was rejected: %A %s" err.Code err.Message
        }

    testList
        "Duplicate-id rejection sees the whole tree"
        ((hiddenIds |> List.map collisionCase) @ [ freshIdStillInserts ])

// ============================================================================
//  Phase 1666 — ADDRESSING through a non-structural position.
//
//  The block above closed the ENUMERATION gap: every node in a keyed position
//  became visible to `allNodeIds`, `findNode` and §4g's uniqueness guard. What
//  it deliberately did not close is the ADDRESSING gap, and the two are
//  different. `applyStructural` hands Fuaran.Core a `NodeWitness` whose
//  `Children` IS `getChildren`, and the comment in `Introspect.fs` records why
//  that witness must not simply be widened: Core REBUILDS through the same
//  function, so a widened witness would have it restructure keyed cases as an
//  ordered list, and `ReorderChildren`'s permutation check would start
//  demanding non-structural ids.
//
//  So the engine reported `NodeNotFound` for nodes it was demonstrably holding
//  — `findNode` reaches them and `UpdateProp` edits them. That is a WRONG
//  answer, not a missing feature, and it sends an author to look for a node
//  that is right there.
//
//  These tests pin the two halves the fix draws apart:
//
//    * BELOW a position — ordinary structural surgery at the node's own parent,
//      with a DESCENT the only thing that was missing. The position's arity
//      never changes, which is exactly what the existing positional lens
//      supports, so it applies normally.
//    * AT a position, or ACROSS two of them — the arity would change, and
//      several of these positions cannot express absence at all. Refused BY
//      NAME with `PositionNotStructural`, never guessed at.
//
//  And the two properties a fix like this breaks silently if unpinned: the
//  purely-structural path must behave exactly as before, and an absent node
//  must still be `NodeNotFound` rather than swept into the new refusal.
// ============================================================================

/// A container sitting AT `Switch.cases[0].child`, with ordinary structural
/// children of its own — the shape the descent exists for. The nested boundary
/// is a position INSIDE this position, so one descent per call has to compose
/// to reach `deep-leaf`.
let private casePanel: Node<Msg> =
    Fuaran.dashboard
        "case-panel"
        { Defaults.dashboard<Msg> with
            Children =
                [ leaf "panel-first"
                  leaf "panel-second"
                  Fuaran.errorBoundary
                      "nested-boundary"
                      { Child =
                          Fuaran.dashboard
                              "deep-panel"
                              { Defaults.dashboard<Msg> with
                                  Children = [ leaf "deep-leaf" ] }
                        Fallback = leaf "nested-fallback" } ] }

/// A second container at a DIFFERENT position on the same holder, so a move
/// between two keyed positions has somewhere to be refused going.
let private defaultPanel: Node<Msg> =
    Fuaran.dashboard
        "default-panel"
        { Defaults.dashboard<Msg> with
            Children = [ leaf "default-first" ] }

let private addressingSwitch: Node<Msg> =
    Fuaran.switch
        "addr-switch"
        { Defaults.switch<Msg> with
            On = Binding.State("mode", None)
            Cases =
                [ { Match = Some "compact"
                    When = None
                    Child = casePanel } ]
            Default = defaultPanel }

let private addressingRoot: Node<Msg> =
    Fuaran.dashboard
        "addr-root"
        { Defaults.dashboard<Msg> with
            Children = [ addressingSwitch; leaf "spine-sibling" ] }

/// The one non-structural label the tests assert on, spelled as §3.3 spells it.
let private caseChildSlot = "Switch.cases[0].child"

[<Tests>]
let nonStructuralAddressingTests =
    testList
        "Structural ops address nodes below a non-structural position (Phase 1666)"
        [ test "RemoveNode reaches a node below a Switch case and leaves the position's arity alone" {
              let op = TreeOp.RemoveNode(NodeId "panel-first")

              match Apply.apply op addressingRoot with
              | Error err -> failtestf "a node below a keyed position was unreachable: %A %s" err.Code err.Message
              | Ok updated ->
                  Expect.isNone (Introspect.findNode (NodeId "panel-first") updated) "the addressed node was removed"

                  Expect.isSome
                      (Introspect.findNode (NodeId "panel-second") updated)
                      "its sibling below the same position survived"

                  // The arity guard, from the other side: the position still
                  // holds exactly the one node it held, and it is still the
                  // same node.
                  match Introspect.findNode (NodeId "addr-switch") updated with
                  | None -> failtest "the holder itself went missing"
                  | Some holder ->
                      let positions = Introspect.nonStructuralPositions holder

                      Expect.equal
                          (positions |> List.map fst)
                          [ caseChildSlot; "Switch.default" ]
                          "the holder's positions are unchanged in number and in name"

                      match positions |> List.tryFind (fun (l, _) -> l = caseChildSlot) with
                      | Some(_, positioned) ->
                          Expect.equal positioned.Id "case-panel" "the position still holds its own node"
                      | None -> failtest "the case position vanished"
          }

          test "InsertChild and ReorderChildren apply below a keyed position too" {
              match Apply.apply (TreeOp.InsertChild(NodeId "case-panel", leaf "panel-third")) addressingRoot with
              | Error err -> failtestf "InsertChild below a keyed position was refused: %A %s" err.Code err.Message
              | Ok updated ->
                  Expect.isSome
                      (Introspect.findNode (NodeId "panel-third") updated)
                      "the inserted node landed below the position"

              let reorder =
                  TreeOp.ReorderChildren(
                      NodeId "case-panel",
                      [ NodeId "panel-second"; NodeId "panel-first"; NodeId "nested-boundary" ]
                  )

              match Apply.apply reorder addressingRoot with
              | Error err -> failtestf "ReorderChildren below a keyed position was refused: %A %s" err.Code err.Message
              | Ok _ -> ()
          }

          test "one descent per call composes — a position nested inside a position is reachable" {
              // `nonStructuralAncestor` answers the OUTERMOST position, so
              // reaching `deep-leaf` takes two descents: into the Switch case,
              // then into the boundary's child arm.
              match Apply.apply (TreeOp.RemoveNode(NodeId "deep-leaf")) addressingRoot with
              | Error err -> failtestf "a doubly-nested position was unreachable: %A %s" err.Code err.Message
              | Ok updated ->
                  Expect.isNone (Introspect.findNode (NodeId "deep-leaf") updated) "the doubly-nested node was removed"

                  Expect.isSome
                      (Introspect.findNode (NodeId "deep-panel") updated)
                      "its holder — which IS the boundary's child position — is untouched"
          }

          test "removing the node AT a position is refused BY NAME, not silently reinterpreted" {
              match Apply.apply (TreeOp.RemoveNode(NodeId "case-panel")) addressingRoot with
              | Ok _ -> failtest "removing the node at a keyed position would leave the position empty"
              | Error err ->
                  Expect.equal
                      err.Code
                      (ApplyErrorCode.PositionNotStructural caseChildSlot)
                      "the refusal names the POSITION that cannot express absence, not the node"

                  Expect.stringContains err.Message caseChildSlot "the message names the slot an author would recognise"
          }

          test "an ErrorBoundary fallback cannot be removed — the position is required" {
              match Apply.apply (TreeOp.RemoveNode(NodeId "nested-fallback")) addressingRoot with
              | Ok _ -> failtest "an ErrorBoundary with no fallback is not an ErrorBoundary"
              | Error err ->
                  Expect.equal
                      err.Code
                      (ApplyErrorCode.PositionNotStructural "ErrorBoundary.fallback")
                      "the required arm refuses under its own label"
          }

          test "MoveNode across two keyed positions is refused, and the message names both" {
              match Apply.apply (TreeOp.MoveNode(NodeId "panel-first", NodeId "default-panel")) addressingRoot with
              | Ok _ -> failtest "a structural move cannot cross a keyed position"
              | Error err ->
                  match err.Code with
                  | ApplyErrorCode.PositionNotStructural slot ->
                      Expect.equal slot caseChildSlot "the refusal is reported at the position the op started in"
                  | other -> failtestf "expected PositionNotStructural, got %A" other

                  Expect.stringContains
                      err.Message
                      "Switch.default"
                      "the message names the other side of the crossing too"
          }

          test "MoveNode out of a keyed position onto the structural spine is refused" {
              // The same crossing seen from one end only: the target sits inside
              // a position and the new parent does not, so there is no single
              // subtree the op can be run against.
              match Apply.apply (TreeOp.MoveNode(NodeId "panel-first", NodeId "addr-root")) addressingRoot with
              | Ok _ -> failtest "a move from inside a keyed position to the spine cannot be expressed in one write"
              | Error err ->
                  match err.Code with
                  | ApplyErrorCode.PositionNotStructural _ -> ()
                  | other -> failtestf "expected PositionNotStructural, got %A" other
          }

          test "§4g is still checked against the WHOLE tree on a descent" {
              // The descent runs the same op against the POSITION'S SUBTREE, and
              // the engine's own duplicate-id pre-check runs against whatever
              // root it is handed — so an id colliding elsewhere in the tree
              // would be invisible to it. The check has to happen before the
              // descent, with the whole tree still in hand.
              let op = TreeOp.InsertChild(NodeId "case-panel", leaf "spine-sibling")

              match Apply.apply op addressingRoot with
              | Ok _ -> failtest "an insert below a keyed position collided with an id on the spine and was accepted"
              | Error err ->
                  Expect.equal err.Code ApplyErrorCode.DuplicateNodeId "§4g holds across the position boundary"
          }

          test "the purely structural path is unchanged, and an absent node is still NodeNotFound" {
              // The two regressions a change like this causes silently.
              match Apply.apply (TreeOp.RemoveNode(NodeId "spine-sibling")) addressingRoot with
              | Error err -> failtestf "an ordinary structural remove regressed: %A %s" err.Code err.Message
              | Ok updated ->
                  Expect.isNone (Introspect.findNode (NodeId "spine-sibling") updated) "the spine child was removed"

              match Apply.apply (TreeOp.RemoveNode(NodeId "not-in-this-tree")) addressingRoot with
              | Ok _ -> failtest "an absent node was accepted"
              | Error err ->
                  Expect.equal
                      err.Code
                      ApplyErrorCode.NodeNotFound
                      "an absent node is NOT swept into the new refusal — the two answers stay distinguishable"
          } ]
