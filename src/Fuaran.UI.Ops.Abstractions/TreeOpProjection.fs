namespace Fuaran.UI.Ops

// ============================================================================
//  TreeOp projections — the op's NAME, the ids it ADDRESSES, and the subtrees
//  it puts INTO the tree, each answered once (Phase 2044).
//
//  Many modules need one of these facts about an op and nothing else, and
//  each used to carry its own exhaustive match to get it. The copies diverged:
//  batch acceptance recorded an `InsertChild`'s child id but not the subtree
//  under it, so an op on a node INSIDE an inserted subtree did not pull the
//  insert into its dependency closure, and the kept batch failed to apply.
//
//  So the questions are answered here, and a module that needs only a name,
//  the addressed ids or the inserted nodes projects from them. A module that
//  genuinely TRANSFORMS an op (encodes it, relabels it, applies it) keeps its
//  own match — the projections are not a visitor.
//
//  What an op reads and writes is NOT answered here: that is
//  `Fuaran.UI.Ops.TreeOpFootprint` (Phase 2292), Fuaran.Core's `Footprint`
//  over the tier's witnesses, in the package that carries the apply engine
//  and the Core reference. The flat id set this module once returned had no
//  way to say that a remove or a move writes a parent it cannot name, so a
//  partial accept read over it could drop a move a later reorder needed.
//
//  The nodes an `EditNode` puts in are read through `NodeChildren`, the tier's
//  one enumeration of what a node's children are, at its `keyed` reach —
//  every position a node holds another in, the same surface id uniqueness
//  quantifies over.
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
        | TreeOp.PatchStyle _ -> "PatchStyle"
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
    /// subtree are payload (the subtree is `inserted`).
    let rec targets (op: TreeOp<'Msg>) : NodeId list =
        match op with
        | TreeOp.EditNode(id, _)
        | TreeOp.UpdateProp(id, _, _)
        | TreeOp.ReplaceBinding(id, _, _)
        | TreeOp.UpdateStyle(id, _)
        | TreeOp.PatchStyle(id, _)
        | TreeOp.UpdateState(id, _)
        | TreeOp.RemoveNode id -> [ id ]
        | TreeOp.InsertChild(parentId, _)
        | TreeOp.ReorderChildren(parentId, _) -> [ parentId ]
        | TreeOp.MoveNode(id, newParentId) -> [ id; newParentId ]
        | TreeOp.ReplaceRoot node -> [ NodeId node.Id ]
        | TreeOp.Batch ops -> ops |> List.collect targets |> List.distinct

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

    /// The subtree roots the op puts INTO the tree, in the order it names them:
    /// an inserted child, a replacement root, the nodes a new kind holds (an
    /// `EditNode`), a new `state` block's `onLoading` / `onEmpty` alternatives
    /// (an `UpdateState`). A `Batch` is its members' insertions in order. Every
    /// other op puts no node in, and answers `[]`.
    ///
    /// NODES, not ids (Phase 2141). The question "does this op add to the
    /// tree" is about what it carries, and an id set cannot answer it: a
    /// payload whose ids repeat collapses to fewer ids than it has nodes, down
    /// to none beyond the op's own target.
    let rec inserted (op: TreeOp<'Msg>) : Node<'Msg> list =
        match op with
        | TreeOp.UpdateProp _
        | TreeOp.ReplaceBinding _
        | TreeOp.UpdateStyle _
        | TreeOp.PatchStyle _
        | TreeOp.RemoveNode _
        | TreeOp.MoveNode _
        | TreeOp.ReorderChildren _ -> []
        | TreeOp.EditNode(id, kind) -> heldBy id kind
        | TreeOp.UpdateState(_, state) -> [ state.OnLoading; state.OnEmpty ] |> List.choose id
        | TreeOp.InsertChild(_, child) -> [ child ]
        | TreeOp.ReplaceRoot node -> [ node ]
        | TreeOp.Batch ops -> ops |> List.collect inserted
