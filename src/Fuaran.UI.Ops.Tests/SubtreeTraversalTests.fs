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
        { StateKey = "mode"
          Cases =
            [ { Match = "compact"
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
