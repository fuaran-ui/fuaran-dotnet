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
//  `PatchStyle`, `UpdateState`, `UpdateAccessibility`, `UpdateTooltip`)
//  rewrites one node in place, and is Core's
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
//  `style` (UpdateStyle), `style.<field>` beside a read of `style`
//  (PatchStyle), and `accessibility` / `tooltip` (UpdateAccessibility /
//  UpdateTooltip, Phase 2527) — plus every id it puts in or takes out. A kind edit and a
//  restyle of one node, a state edit beside a prop edit, and two in-place
//  edits under different parents are independent; an in-place edit beside the
//  removal of its ancestor is not (the node is in the removed set).
//
//  A prop or binding is the `kind` slot, not a slot per path: a field's
//  validity can read another field of the same spec (a select's `Value` is
//  checked against its `Multiple`), so two prop edits of one node are ordered.
//
//  THE CHILD LIST (Phase 2691, Core D246). Core compares a structure write of
//  `p` — an insert into `p`, a move into or out of it, a removal under it —
//  against a slot access as a read and a write of `(p, childListSlot)`, and a
//  slot write meets the node sets only at a content write. So every arm that
//  reads or rewrites a node's structural children by slot declares that slot:
//  a resolved reorder and a resolved move's source parent (beside the `kind`
//  slot the tier holds the list in; a reorder that leaves the order as it is
//  only reads both, Phase 2696), an `EditNode` (a new kind can hold other
//  children, or none), and any in-place edit whose held set moved. A restyle,
//  a state edit, a trait edit and a prop edit write no child list — the props
//  path refuses `Children`, and admission (`canHold`) reads the kind's case,
//  which only `EditNode` changes — so each commutes with an insert into its
//  node, a move into or out of it and a removal under it. A state alternative
//  is a keyed position, not the child list, so `UpdateState` declares none.
//  A resolved move reads its destination's `parent` slot with the ancestors
//  above it: the cycle check reads the whole chain, and since D246 the
//  destination's existence read no longer collides with a move of it.
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
    | TreeOp.PatchStyle(id, _)
    // Replaces one trait of the node's envelope (Phase 2527); neither holds a node.
    | TreeOp.UpdateAccessibility(id, _)
    | TreeOp.UpdateTooltip(id, _) -> vertical id []
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

/// Core's child-list slot (Phase 2691, Core D246).
let private childListSlot = CoreFootprint.childListSlot

/// `f` with a read and a write of `node`'s child-list slot beside it.
let private childList (node: string) (f: Fuaran.Core.Footprint) =
    CoreFootprint.union f (CoreFootprint.slotEdit node childListSlot)

/// `slotted` for an op whose contract moves no node: where its held set moved
/// anyway (`heldChange` non-empty), the child list is written too. Empty for
/// every such op the engine has, so the slot is a guard, not an arm.
let private guarded (writes: string list) (reads: string list) (target: string) (touched: Set<string>) =
    let f = slotted writes reads target touched
    if Set.isEmpty touched then f else childList target f

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
    // A resolved move (Phase 2651) touches no parent WHOLE. The SOURCE
    // parent's child list is the only part of it the move changes, and the
    // child list is held in the parent's `kind`: Core's read and structure
    // write of it become a read and a write of its `kind` slot and of Core's
    // child-list slot (Phase 2691), so a restyle or a state edit of the
    // container the node left commutes with the move, and an insert into it, a
    // reorder or an edit of its kind does not. The destination and its
    // ANCESTORS are read for the cycle check, which only a relocation or
    // removal of one of them can change: each is read at its `parent` slot,
    // which only a move writes and every whole-node write of it hits, so a
    // move beside an edit of a container above its destination commutes too.
    // The destination keeps Core's named structure write and read — the
    // address the fold interleaves two appends at. The TARGET changes its
    // position and nothing of itself (Phase 2670): it is read and written at
    // its `parent` slot, so a restyle, an edit or a reorder of the moved node
    // commutes with the move, while its removal, a removal of an ancestor (a
    // whole-node write), another move of it and a move whose cycle check reads
    // it as an ancestor (a read of that slot) stay ordered. The cycle check
    // needs no whole read of the target: whether the destination lies under it
    // is a fact of the destination's ancestor chain, which is read above. A
    // move within one parent keeps the parent whole; an unresolved move is
    // Core's pure arm.
    | TreeOp.MoveNode(target, newParent) ->
        let core = structural (Fuaran.Core.SkeletonOp.MoveNode(target, newParent))
        let moved = key target
        let destination = key newParent

        if not (Set.isEmpty core.UnknownParentWrites) then
            core
        else
            let source =
                match Set.toList (Set.remove destination core.StructureWrites) with
                | [ p ] -> Some p
                | _ -> None

            let named = [ Some moved; Some destination; source ] |> List.choose id |> Set.ofList

            // The destination's own `parent` slot is part of the chain the
            // cycle check reads (Phase 2691): until Core D246 its existence
            // read collided with a move of it, and now nothing else does.
            let ancestors =
                Set.difference core.Reads named
                |> Set.add destination
                |> Seq.map (fun a -> CoreFootprint.readingSlot a "parent")
                |> Seq.fold CoreFootprint.union CoreFootprint.empty

            let sourceSlot =
                source
                |> Option.map (fun p -> childList p (CoreFootprint.slotEdit p "kind"))
                |> Option.defaultValue CoreFootprint.empty

            [ ancestors
              sourceSlot
              CoreFootprint.slotEdit moved "parent"
              { core with
                  Reads = Set.intersect core.Reads (Set.singleton destination)
                  StructureWrites = Set.singleton destination
                  ContentWrites = Set.remove moved core.ContentWrites } ]
            |> List.fold CoreFootprint.union CoreFootprint.empty
    // A resolved reorder (Phase 2670) permutes the parent's child list, which
    // the parent holds in its `kind`, and touches no child: the parent is read
    // and written at its `kind` slot and at Core's child-list slot (Phase
    // 2691), where every structure write of it meets the reorder, and each listed child is read at its
    // `parent` slot — the one fact of it the reorder depends on (that it sits
    // under this parent). So a restyle or an edit of a listed child, and a
    // restyle of the parent, commute with the reorder; an insert into the
    // parent, a removal or move of a child or of the parent, and an edit of the
    // parent's kind stay ordered. An unresolved reorder is Core's arm.
    //
    // An UNCHANGED reorder (Phase 2696) — its list is the parent's child order
    // in `state`: the same ids, in the same order, of the same length — changes
    // nothing where it applies, so it declares the reads above and no write.
    // Every op that changes the child list writes one of the two slots it still
    // reads, so the reorder stays ordered against it: a real reorder of the same
    // parent among them, which does not commute with it under replay (applied
    // second, the unchanged reorder restores the order it lists). What it frees
    // is a pair of reorders that each leave their own list as it was, and an op
    // that only reads the list. A list naming a missing child or omitting a
    // present one is not unchanged, and does not apply.
    | TreeOp.ReorderChildren(parent, order) ->
        match after, find parent state with
        | Some _, Some node ->
            let p = key parent

            let unchanged =
                Introspect.getChildren node.Kind
                |> Option.defaultValue []
                |> List.map (fun c -> NodeId c.Id) = order

            let listSlots =
                if unchanged then
                    CoreFootprint.union (CoreFootprint.readingSlot p "kind") (CoreFootprint.readingSlot p childListSlot)
                else
                    childList p (CoreFootprint.slotEdit p "kind")

            order
            |> List.map (fun c -> CoreFootprint.readingSlot (key c) "parent")
            |> List.fold CoreFootprint.union listSlots
        | _ -> structural (Fuaran.Core.SkeletonOp.ReorderChildren(parent, order))
    // The kind is rewritten whole: the nodes the old kind held are taken out
    // and the nodes the new kind holds put in, and the child list (and whether
    // the node can hold one at all) with it.
    | TreeOp.EditNode(id, _) ->
        vertical id (fun node ->
            let old = Fuaran.UI.Ops.TreeOp.inserted (TreeOp.EditNode(id, node.Kind))

            slotted
                [ "kind"; childListSlot ]
                []
                (key id)
                (Set.union (idsOf old) (idsOf (Fuaran.UI.Ops.TreeOp.inserted op))))
    | TreeOp.UpdateProp(id, _, _)
    | TreeOp.ReplaceBinding(id, _, _) -> vertical id (fun node -> guarded [ "kind" ] [] (key id) (heldChange id node))
    // The state block is replaced whole: its old alternatives out, the new in.
    | TreeOp.UpdateState(id, _) ->
        vertical id (fun node ->
            let old =
                match node.State with
                | Some s -> [ s.OnLoading; s.OnEmpty ] |> List.choose (fun n -> n)
                | None -> []

            slotted [ "state" ] [] (key id) (Set.union (idsOf old) (idsOf (Fuaran.UI.Ops.TreeOp.inserted op))))
    | TreeOp.UpdateStyle(id, _) -> vertical id (fun node -> guarded [ "style" ] [] (key id) (heldChange id node))
    // A trait of the envelope is its own slot (Phase 2527): neither holds a node.
    | TreeOp.UpdateAccessibility(id, _) ->
        vertical id (fun node -> guarded [ "accessibility" ] [] (key id) (heldChange id node))
    | TreeOp.UpdateTooltip(id, _) -> vertical id (fun node -> guarded [ "tooltip" ] [] (key id) (heldChange id node))
    | TreeOp.PatchStyle(id, delta) ->
        vertical id (fun node ->
            guarded
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
