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
//  Two forms. The PURE form (`ofOp` / `ofScript`) is computed from the op
//  alone, and is node-precise: over Core's address-free unknown-parent clause
//  an unknown-parent write collides with every structural or unknown-parent
//  write of the other script, and every non-empty `TreeOp` writes one of the
//  two, so a vertical op is independent of no tier op but an empty `Batch`
//  whatever slot it declared. Slots would free nothing there, and Core's
//  `Conformance.footprintLawsAt` refuses such a declaration as vacuous.
//
//  The BASE-AWARE form (`ofOpIn` / `ofScriptIn`, Phase 2327) is computed at
//  the tree the op applies to, over Core's `Ops.footprintIn` (fuaran-core
//  Phase 500): a removal content-writes the whole subtree it destroys and
//  names its parent, so a vertical op whose node the structural walk LOCATES
//  needs no unknown-parent write, and is declared at the SLOT it changes —
//  `kind` (EditNode, UpdateProp, ReplaceBinding), `state` (UpdateState),
//  `style` (UpdateStyle) and `style.<field>` beside a read of `style`
//  (PatchStyle) — plus every id it puts in or takes out. A kind edit and a
//  restyle of one node, a state edit beside a prop edit, and two in-place
//  edits under different parents are independent; an in-place edit beside the
//  removal of its ancestor is not (the node is in the removed set).
//
//  A prop or binding is the `kind` slot, not a slot per path: a field's
//  validity can read another field of the same spec (a select's `Value` is
//  checked against its `Multiple`), so two prop edits of one node are ordered.
//
//  The script is THREADED as Core threads it: each op is footprinted at the
//  tree the ops before it reach under `Apply.apply`, and from an op that does
//  not apply onwards every op takes the pure arm. A node held only in a keyed
//  position (a switch case, a `state` alternative) is not located, and takes
//  the pure arm, as Core's resolution does. A `ReplaceRoot` content-writes
//  every id of the tree it replaces as well as of the tree it installs.
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

/// The keyed scope the structural arms are footprinted under — the walk the
/// apply engine edits through.
let private keyedScope<'Msg> : Fuaran.Core.OpScope<Node<'Msg>, NodeId> =
    Fuaran.Core.OpScope.keyed Introspect.keyedWitness (fun _ -> true) Introspect.nodeWitness Introspect.idWitness

/// Every id in `node`'s subtree, `node` included, through every position the
/// tier holds a node in — the walk `TreeOp.inserted`'s subtrees are counted by.
let private keyedIds (node: Node<'Msg>) : Set<string> =
    CoreTree.idsKeyed Introspect.nodeWitness Introspect.keyedWitness node
    |> List.map key
    |> Set.ofList

/// A structural op, through Core's keyed footprint.
let private skeleton (op: Fuaran.Core.SkeletonOp<Node<'Msg>, NodeId>) : Fuaran.Core.Footprint =
    CoreOps.footprint Fuaran.Core.Policy.none keyedScope [ op ]

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

// ---- the base-aware form (Phase 2327) ----

/// Every id `node` holds below itself, through every position the tier holds
/// a node in.
let private heldIds (node: Node<'Msg>) : Set<string> = Set.remove node.Id (keyedIds node)

/// The ids a list of subtree roots carries.
let private idsOf (nodes: Node<'Msg> list) : Set<string> =
    nodes |> List.fold (fun acc n -> Set.union acc (keyedIds n)) Set.empty

/// A slot-precise in-place rewrite of `target`: the slot writes, the slot
/// reads, and every id the op puts in or takes out read and content-written.
let private slotted (writes: string list) (reads: string list) (target: string) (touched: Set<string>) =
    let slots =
        (writes |> List.map (CoreFootprint.slotEdit target))
        @ (reads |> List.map (CoreFootprint.readingSlot target))
        |> List.fold CoreFootprint.union CoreFootprint.empty

    { slots with
        Reads = Set.union slots.Reads touched
        ContentWrites = Set.union slots.ContentWrites touched }

/// The base-aware arm of one non-batch op at `state`, given the tree it
/// reaches (`None` where it does not apply).
let private resolvedAt (state: Node<'Msg>) (after: Node<'Msg> option) (op: TreeOp<'Msg>) : Fuaran.Core.Footprint =
    let structural (s: Fuaran.Core.SkeletonOp<Node<'Msg>, NodeId>) =
        CoreOps.footprintIn Fuaran.Core.Policy.none keyedScope state [ s ]

    let find (id: NodeId) (tree: Node<'Msg>) =
        CoreTree.tryFind Introspect.nodeWitness Introspect.idWitness id tree

    // The ids an op whose contract puts no node in (`TreeOp.inserted`) moved
    // anyway: the node's held sets before and after, where they differ. A
    // guard, not an arm — empty for every such op the engine has.
    let heldChange (id: NodeId) (node: Node<'Msg>) =
        let before = heldIds node

        match after |> Option.bind (find id) |> Option.map heldIds with
        | Some a when a = before -> Set.empty
        | Some a -> Set.union before a
        | None -> before

    // A vertical op is slot-precise only where the structural walk locates its
    // node and it applies; otherwise it is the pure arm.
    let vertical (id: NodeId) (arm: Node<'Msg> -> Fuaran.Core.Footprint) =
        match find id state, after with
        | Some node, Some _ -> arm node
        | _ -> ofOp op

    match op with
    | TreeOp.InsertChild(parent, child) -> structural (Fuaran.Core.SkeletonOp.InsertChild(parent, child))
    | TreeOp.RemoveNode target -> structural (Fuaran.Core.SkeletonOp.RemoveNode target)
    | TreeOp.MoveNode(target, newParent) -> structural (Fuaran.Core.SkeletonOp.MoveNode(target, newParent))
    | TreeOp.ReorderChildren(parent, order) -> structural (Fuaran.Core.SkeletonOp.ReorderChildren(parent, order))
    // The kind is rewritten whole: the nodes the old kind held are taken out
    // and the nodes the new kind holds put in.
    | TreeOp.EditNode(id, _) ->
        vertical id (fun node ->
            let old = Fuaran.UI.Ops.TreeOp.inserted (TreeOp.EditNode(id, node.Kind))
            slotted [ "kind" ] [] (key id) (Set.union (idsOf old) (idsOf (Fuaran.UI.Ops.TreeOp.inserted op))))
    | TreeOp.UpdateProp(id, _, _)
    | TreeOp.ReplaceBinding(id, _, _) -> vertical id (fun node -> slotted [ "kind" ] [] (key id) (heldChange id node))
    // The state block is replaced whole: its old alternatives out, the new in.
    | TreeOp.UpdateState(id, _) ->
        vertical id (fun node ->
            let old =
                match node.State with
                | Some s -> [ s.OnLoading; s.OnEmpty ] |> List.choose (fun n -> n)
                | None -> []

            slotted [ "state" ] [] (key id) (Set.union (idsOf old) (idsOf (Fuaran.UI.Ops.TreeOp.inserted op))))
    | TreeOp.UpdateStyle(id, _) -> vertical id (fun node -> slotted [ "style" ] [] (key id) (heldChange id node))
    | TreeOp.PatchStyle(id, delta) ->
        vertical id (fun node ->
            slotted
                (StyleDelta.fieldNames delta |> List.map (fun f -> "style." + f))
                [ "style" ]
                (key id)
                (heldChange id node))
    // The tree replaced: every id it held is taken out, beside the pure arm.
    | TreeOp.ReplaceRoot _ ->
        let plain = ofOp op
        let replaced = keyedIds state

        { plain with
            Reads = Set.union plain.Reads replaced
            ContentWrites = Set.union plain.ContentWrites replaced
            StructureWrites = Set.add state.Id plain.StructureWrites }
    | TreeOp.Batch inner -> ofScript inner

/// One op at the state the ops before it reached, and the state it reaches.
let rec private threaded (state: Node<'Msg> option) (op: TreeOp<'Msg>) : Fuaran.Core.Footprint * Node<'Msg> option =
    match state, op with
    | None, _ -> ofOp op, None
    | Some t, TreeOp.Batch inner ->
        inner
        |> List.fold
            (fun (acc, st) o ->
                let f, st' = threaded st o
                CoreFootprint.union acc f, st')
            (CoreFootprint.empty, Some t)
    | Some t, _ ->
        let after = Apply.apply op t |> Result.toOption
        resolvedAt t after op, after

/// The footprint of one op at the tree it applies to (Phase 2327): `ofOp`'s
/// address kinds with every parent the pure form cannot name resolved against
/// `state`, and a located vertical op declared at its slot (see the module
/// header). A `Batch` is threaded member by member. Total, never throws; sound
/// where `ofOp` is and never less precise (`Conformance.footprintInLawsAt`).
/// The shape `Dag.conflictsIn` / `Dag.reconcileIn` take.
let ofOpIn (state: Node<'Msg>) (op: TreeOp<'Msg>) : Fuaran.Core.Footprint = threaded (Some state) op |> fst

/// The footprint of a script at the tree it applies to: each op at the tree
/// the ops before it reach, unioned. `[]` is `Footprint.empty`.
let ofScriptIn (baseTree: Node<'Msg>) (ops: TreeOp<'Msg> list) : Fuaran.Core.Footprint =
    ofOpIn baseTree (TreeOp.Batch ops)

/// Each op of a script at the tree the ops before it reach, in order: the
/// per-op footprints `ofScriptIn` unions, for a caller that orders the ops of
/// one script against each other (a partial accept).
let ofEachIn (baseTree: Node<'Msg>) (ops: TreeOp<'Msg> list) : Fuaran.Core.Footprint list =
    ops |> List.mapFold (fun st op -> threaded st op) (Some baseTree) |> fst
