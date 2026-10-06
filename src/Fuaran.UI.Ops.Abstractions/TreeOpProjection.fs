namespace Fuaran.UI.Ops

// ============================================================================
//  TreeOp projections — the op's NAME, the ids it ADDRESSES, and the ids it
//  TOUCHES, each answered once (Phase 2044).
//
//  Many modules need one of these three facts about an op and nothing else,
//  and each used to carry its own exhaustive match to get it. The copies
//  diverged: batch acceptance recorded an `InsertChild`'s child id but not the
//  subtree under it, so an op on a node INSIDE an inserted subtree did not pull
//  the insert into its dependency closure, and the kept batch failed to apply.
//
//  So the three questions are answered here, and a module that needs only a
//  name, the addressed ids or the footprint projects from them. A module that
//  genuinely TRANSFORMS an op (encodes it, relabels it, applies it) keeps its
//  own match — the projections are not a visitor.
//
//  The inserted-subtree walk reads `NodeChildren`, the tier's one enumeration
//  of what a node's children are, at its `keyed` reach — every position a node
//  holds another in, the same surface id uniqueness quantifies over — so an id
//  introduced under a `Switch` case, a `state.onLoading` alternative or a slot
//  argument is in the footprint exactly as one in an ordered child list is.
//
//  FSharp.Core + the tier's own types only, Fable-portable: no IO, no clock,
//  no reflection.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TreeOp =

    /// The op's case name — `"EditNode"`, `"InsertChild"`, `"Batch"` — the
    /// spelling the wire's `$type` discriminator, the error envelope's `kind`
    /// and the audit record all use.
    let kindName (op: TreeOp<'Msg>) : string =
        match op with
        | TreeOp.EditNode _ -> "EditNode"
        | TreeOp.UpdateProp _ -> "UpdateProp"
        | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
        | TreeOp.UpdateStyle _ -> "UpdateStyle"
        | TreeOp.UpdateState _ -> "UpdateState"
        | TreeOp.InsertChild _ -> "InsertChild"
        | TreeOp.RemoveNode _ -> "RemoveNode"
        | TreeOp.MoveNode _ -> "MoveNode"
        | TreeOp.ReorderChildren _ -> "ReorderChildren"
        | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
        | TreeOp.Batch _ -> "Batch"

    /// The ids the op ADDRESSES, in the order it names them: the edited node,
    /// the parent of an insert or reorder, the moved node then its new parent,
    /// the replacement root's own id. A `Batch` is its members' targets in
    /// order, each id once.
    ///
    /// Not the ids it merely mentions: a reorder's `newOrder` and an insert's
    /// subtree are payload, and are in `footprint` instead.
    let rec targets (op: TreeOp<'Msg>) : NodeId list =
        match op with
        | TreeOp.EditNode(id, _)
        | TreeOp.UpdateProp(id, _, _)
        | TreeOp.ReplaceBinding(id, _, _)
        | TreeOp.UpdateStyle(id, _)
        | TreeOp.UpdateState(id, _)
        | TreeOp.RemoveNode id -> [ id ]
        | TreeOp.InsertChild(parentId, _)
        | TreeOp.ReorderChildren(parentId, _) -> [ parentId ]
        | TreeOp.MoveNode(id, newParentId) -> [ id; newParentId ]
        | TreeOp.ReplaceRoot node -> [ NodeId node.Id ]
        | TreeOp.Batch ops -> ops |> List.collect targets |> List.distinct

    /// Every id in `node`'s subtree, `node` included, through every keyed
    /// position.
    let rec private subtreeIds (node: Node<'Msg>) (acc: Set<NodeId>) : Set<NodeId> =
        NodeChildren.children NodeChildren.Reach.keyed node
        |> List.fold (fun a child -> subtreeIds child a) (Set.add (NodeId node.Id) acc)

    /// The nodes a new `NodeKind` holds — what an `EditNode` puts into the tree
    /// below the node it rewrites. Read through an envelope-free carrier, so
    /// only the kind's own positions are enumerated: the rewritten node keeps
    /// its existing envelope.
    let private heldBy (id: NodeId) (kind: NodeKind<'Msg>) : Node<'Msg> list =
        let (NodeId raw) = id

        let carrier: Node<'Msg> =
            { Id = raw
              Kind = kind
              Accessibility = None
              ExtraAttributes = None
              Fallback = None
              Motion = None
              State = None
              Style = None
              Tooltip = None
              Visible = None }

        NodeChildren.children NodeChildren.Reach.keyed carrier

    /// Every id the op TOUCHES: its `targets`, every id in a subtree it puts
    /// into the tree (an inserted child, a replacement root, the nodes a new
    /// kind or a new `state` block holds), and a reorder's `newOrder`. Two ops
    /// whose footprints are disjoint do not depend on each other; an op on a
    /// node INSIDE an inserted subtree shares an id with the insert.
    let rec footprint (op: TreeOp<'Msg>) : Set<NodeId> =
        let addressed = Set.ofList (targets op)

        match op with
        | TreeOp.UpdateProp _
        | TreeOp.ReplaceBinding _
        | TreeOp.UpdateStyle _
        | TreeOp.RemoveNode _
        | TreeOp.MoveNode _ -> addressed
        | TreeOp.EditNode(id, kind) -> heldBy id kind |> List.fold (fun a n -> subtreeIds n a) addressed
        | TreeOp.UpdateState(_, state) ->
            [ state.OnLoading; state.OnEmpty ]
            |> List.choose id
            |> List.fold (fun a n -> subtreeIds n a) addressed
        | TreeOp.InsertChild(_, child) -> subtreeIds child addressed
        | TreeOp.ReorderChildren(_, newOrder) -> Set.union addressed (Set.ofList newOrder)
        | TreeOp.ReplaceRoot node -> subtreeIds node addressed
        | TreeOp.Batch ops -> ops |> List.map footprint |> Set.unionMany
