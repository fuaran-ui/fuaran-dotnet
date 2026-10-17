module Fuaran.UI.Ops.TreeOpFootprint

// ============================================================================
//  The footprint of a `TreeOp` — what it reads and writes, as Fuaran.Core's
//  `Footprint` (Phase 2292).
//
//  ONE projection, shipped, total over the DU. A footprint is a fact about the
//  op algebra rather than about any store, so it lives beside the apply engine
//  whose behaviour it summarises, and everything that asks whether two scripts
//  of tier ops commute reads it: `Fuaran.Core.Ops.independent` /
//  `Ops.interference` for arbitration and partial accept, `Dag.conflicts` and
//  the lane fold for a merge. Two scripts whose footprints are independent
//  provably commute under `Apply.apply`; a dependent verdict is always safe.
//
//  The structural ops are Core's: they lower to the matching `SkeletonOp` and
//  take `Ops.footprintKeyed` over the tier's witnesses, the same keyed walk the
//  apply engine runs (`Apply` edits through `Ops.applyContainedKeyed`), so an
//  id an inserted subtree holds in a switch case, a `state` alternative or a
//  slot argument is in the footprint exactly as one in an ordered child list.
//
//  A VERTICAL op (`EditNode`, `UpdateProp`, `ReplaceBinding`, `UpdateStyle`,
//  `PatchStyle`, `UpdateState`) rewrites one node in place, and is Core's
//  `UpdateNode` footprint: the node read and content-written, every id of a
//  subtree the op puts in read and content-written, and an UNKNOWN-PARENT
//  write of the node (Phase 2004). The third term is required for soundness,
//  not caution: removing an ANCESTOR of the node names neither the node nor
//  any part of it, yet the edit lands in one order and is refused in the
//  other, and only the unknown-parent clause of `Ops.independent` sees that
//  pair.
//
//  Node precision, not slot precision — decided, not deferred. Over the pinned
//  Core an unknown-parent write collides with every structural or
//  unknown-parent write of the other script, and every non-empty `TreeOp`
//  writes one of the two. So a vertical op is independent of no tier op but an
//  empty `Batch`, whatever slot it declares: declaring `Footprint.slotEdit`
//  slots (`kind`, `state`, `style.<field>`) would free no pair of tier ops and
//  would change only the wording of the clause that refuses them. Core's
//  `Conformance.footprintLawsAt` refuses such a declaration as vacuous at the
//  slot dimension (it finds no pair the slots alone free). A slot is the right
//  address once Core can say "a parent this script cannot name" without that
//  colliding with another in-place rewrite; until then the node is.
//
//  FSharp.Core + the tier's own types + Fuaran.Core, Fable-portable: no IO,
//  no clock, no reflection. Total — no op throws.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

module CoreOps = Fuaran.Core.Ops
module CoreTree = Fuaran.Core.Tree
module CoreFootprint = Fuaran.Core.Footprint

let private key (NodeId raw) = raw

/// Every id in `node`'s subtree, `node` included, through every position the
/// tier holds a node in — the walk `TreeOp.inserted`'s subtrees are counted by.
let private keyedIds (node: Node<'Msg>) : Set<string> =
    CoreTree.idsKeyed Introspect.nodeWitness Introspect.keyedWitness node
    |> List.map key
    |> Set.ofList

/// A structural op, through Core's keyed footprint.
let private skeleton (op: Fuaran.Core.SkeletonOp<Node<'Msg>, NodeId>) : Fuaran.Core.Footprint =
    CoreOps.footprintKeyed Introspect.keyedWitness Introspect.nodeWitness Introspect.idWitness [ op ]

/// An in-place rewrite of `id` that puts `incoming` into the tree: Core's
/// `UpdateNode` footprint over the keyed engine.
let private vertical (id: NodeId) (incoming: Node<'Msg> list) : Fuaran.Core.Footprint =
    let target = key id

    let introduced =
        incoming |> List.fold (fun acc n -> Set.union acc (keyedIds n)) Set.empty

    { CoreFootprint.empty with
        Reads = Set.add target introduced
        ContentWrites = Set.add target introduced
        UnknownParentWrites = Set.singleton target }

/// The footprint of one op. Total over the DU.
let rec ofOp (op: TreeOp<'Msg>) : Fuaran.Core.Footprint =
    match op with
    // Appends a subtree under a NAMED parent: the parent's child list is a
    // structure write, and every id of the subtree is read and authored.
    | TreeOp.InsertChild(parent, child) -> skeleton (Fuaran.Core.SkeletonOp.InsertChild(parent, child))
    // Destroys the node and its subtree under a parent the op cannot name: an
    // unknown-parent write.
    | TreeOp.RemoveNode target -> skeleton (Fuaran.Core.SkeletonOp.RemoveNode target)
    // Relocates the node: a structure write of the named destination, and an
    // unknown-parent write for the source it cannot name.
    | TreeOp.MoveNode(target, newParent) -> skeleton (Fuaran.Core.SkeletonOp.MoveNode(target, newParent))
    // Rewrites a named parent's child order, reading every child it names.
    | TreeOp.ReorderChildren(parent, order) -> skeleton (Fuaran.Core.SkeletonOp.ReorderChildren(parent, order))
    // Swaps the node's whole `Kind` — every spec field and the child list —
    // and puts the nodes the new kind holds into the tree.
    | TreeOp.EditNode(id, _) -> vertical id (Fuaran.UI.Ops.TreeOp.inserted op)
    // Writes one field of the node's spec. The node, not the field: see the
    // module header for why a slot would free nothing.
    | TreeOp.UpdateProp(id, _, _)
    // Writes one binding-typed field of the node's spec; as `UpdateProp`.
    | TreeOp.ReplaceBinding(id, _, _)
    // Replaces the node's whole `Style` record.
    | TreeOp.UpdateStyle(id, _)
    // Writes the style fields the delta names (Phase 2291); the node, as
    // `UpdateProp`, for the same reason.
    | TreeOp.PatchStyle(id, _) -> vertical id []
    // Replaces the node's `State` block, putting its `onLoading` / `onEmpty`
    // alternatives into the tree.
    | TreeOp.UpdateState(id, _) -> vertical id (Fuaran.UI.Ops.TreeOp.inserted op)
    // The whole tree rewritten: every id of the new tree read and authored,
    // the root's child list written, and an unknown-parent write of the root,
    // so it is ordered against every non-empty tier op. Total — it declares
    // what it does rather than refusing to be asked.
    | TreeOp.ReplaceRoot node ->
        let ids = keyedIds node

        { CoreFootprint.empty with
            Reads = ids
            ContentWrites = ids
            StructureWrites = Set.singleton node.Id
            UnknownParentWrites = Set.singleton node.Id }
    // Its members' footprints, unioned.
    | TreeOp.Batch inner -> ofScript inner

/// The footprint of a script: its ops' footprints, unioned. `[]` is
/// `Footprint.empty`, independent of everything.
and ofScript (ops: TreeOp<'Msg> list) : Fuaran.Core.Footprint =
    ops
    |> List.fold (fun acc op -> CoreFootprint.union acc (ofOp op)) CoreFootprint.empty
