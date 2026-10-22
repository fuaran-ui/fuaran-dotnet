namespace Fuaran.UI.Ops

// ============================================================================
//  Placement — placed insert / move / nudge, and the clone verbs (duplicate /
//  paste) built on top of them, as Fuaran.Core's `TreePlacement` over this
//  tier's witnesses (Phase 2306).
//
//  The op vocabulary is deliberately positionless: `InsertChild` and `MoveNode`
//  append, and an explicit order is stated only by `ReorderChildren` naming
//  every sibling id (an id is checkable; an ordinal is not). Placing a node
//  anywhere but last is therefore `Batch [InsertChild|MoveNode;
//  ReorderChildren]`, and deriving that permutation is Core's placement algebra
//  (fuaran-core Phase 312): an `Anchor` (`First` / `Last` / `Before` / `After` /
//  `Index`) lowers to the bare op when appending already lands the node there,
//  to the batch otherwise, and — for a node moved within its own parent — to a
//  bare `ReorderChildren`, or to nothing when it is already in place. A reorder
//  footprints the parent alone, where a move also writes the node and an
//  unknown parent, so the same-parent form serialises against fewer concurrent
//  edits.
//
//  No verdict here is a mirror of the apply engine. Each verb asks the engine:
//
//   1. the engine's own dry run of the op over the whole tree, at the keyed
//      reach `Apply` runs at (`Ops.canApplyContainedKeyed` under the tier's
//      container predicate) — absence, a duplicate id anywhere in the tree or
//      the graft, a childless destination, a move into itself or its own
//      subtree. Its rejection is carried unchanged as `PlaceError.Refused`;
//   2. the keyed-position rule, which is this tier's and not Core's: a
//      structural op cannot vacate a keyed position or cross one
//      (`PositionNotStructural`, labelled with the slot — Core names the holder
//      but not the label, fuaran-core#492). It descends into a position exactly
//      as `Apply` does, one level per turn, so the scope Core lowers against is
//      the subtree the engine will run the op against;
//   3. Core's `TreePlacement.…Contained` over that scope, which resolves the
//      anchor (`UnknownAnchor` / `IndexOutOfRange`, Core's cases) and emits
//      the script.
//
//  The clone verbs rename through Core's `FreshIds.repairDuplicates` over the
//  keyed walk (`Tree.traversal`), so an id held in a Switch case or an
//  ErrorBoundary slot is renamed with the rest — the id-uniqueness contract is
//  tree-wide.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Placement vocabulary ────────────────────────────────────────────────────

/// A structural destination: which parent, and where among its children —
/// stated as Core's `Anchor` (Phase 2306: it was a four-case `Placement` of the
/// tier's own; `Anchor` is the same four plus `Index`, a position among the
/// destination's children other than the node placed, `0` first to their count
/// last).
type Target =
    { ParentId: NodeId
      Placement: Anchor<NodeId> }

/// Why a placement could not become an op: Core's `PlaceError` cases, plus the
/// two answers that belong to this tier rather than to the placement algebra.
[<RequireQualifiedAccess>]
type PlaceError =
    /// The engine's dry run refuses the op the placement lowers to — an unknown
    /// node or parent, a duplicate id, a childless destination, a move of the
    /// root or under itself. The rejection is the engine's own, unchanged.
    | Refused of rejection: Rejection<NodeId>
    /// The anchor names no child of `parent` other than the node being placed;
    /// `siblings` are the ids that are, in order.
    | UnknownAnchor of parent: NodeId * anchor: NodeId * siblings: NodeId list
    /// The index is outside `0 .. count`, where `count` is the number of
    /// `parent`'s children other than the node being placed.
    | IndexOutOfRange of parent: NodeId * index: int * count: int
    /// The op would change the ARITY of a keyed position, or cross one (apply:
    /// `PositionNotStructural`). Carries the node addressed and the position's
    /// slot label in the §3.3 spelling (`Switch.cases[0].child`).
    ///
    /// Three shapes reach it, and they are one fact seen from three sides: the
    /// node IS the position's own node, so moving it would leave the position
    /// empty; the node sits below one position and the destination below
    /// another; or one of the two sits below a position and the other on the
    /// structural spine. Reaching THROUGH a position is not an error at all — a
    /// node below one moves, nudges and takes inserts normally, WITHIN that
    /// position's subtree.
    | PositionNotStructural of nodeId: NodeId * slot: string
    /// The root has no siblings to nudge among.
    | CannotNudgeRoot of nodeId: NodeId

// ─── The verbs ───────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
module Placement =

    /// The tier's container predicate — the one `Apply` runs the structural
    /// legs under: a Layout holds children, a leaf does not.
    let private canHold (n: Node<'Msg>) =
        Introspect.getChildren n.Kind |> Option.isSome

    /// The keyed walk: structural children plus every keyed position, so a
    /// clone's subtree lookup and rename reach every node the tree-wide
    /// duplicate-id contract covers.
    let private traversal<'Msg> : NodeWitness<Node<'Msg>, NodeId> =
        Tree.traversal Introspect.nodeWitness Introspect.keyedWitness

    let private ofCore (error: Fuaran.Core.PlaceError<NodeId>) : PlaceError =
        match error with
        | Fuaran.Core.PlaceError.Refused rejection -> PlaceError.Refused rejection
        | Fuaran.Core.PlaceError.UnknownAnchor(parent, anchor, siblings) ->
            PlaceError.UnknownAnchor(parent, anchor, siblings)
        | Fuaran.Core.PlaceError.IndexOutOfRange(parent, index, count) ->
            PlaceError.IndexOutOfRange(parent, index, count)

    /// A skeleton op as the tier's op. `TreePlacement` lowers to the structural
    /// ops alone (its module note), so `UpdateNode` cannot arise and answers
    /// `None`.
    let rec private ofSkeleton (op: SkeletonOp<Node<'Msg>, NodeId>) : TreeOp<'Msg> option =
        match op with
        | SkeletonOp.InsertChild(parent, child) -> Some(TreeOp.InsertChild(parent, child))
        | SkeletonOp.RemoveNode target -> Some(TreeOp.RemoveNode target)
        | SkeletonOp.MoveNode(target, newParent) -> Some(TreeOp.MoveNode(target, newParent))
        | SkeletonOp.ReorderChildren(parent, order) -> Some(TreeOp.ReorderChildren(parent, order))
        | SkeletonOp.Batch inner -> Some(TreeOp.Batch(inner |> List.choose ofSkeleton))
        | SkeletonOp.UpdateNode _ -> None

    /// A placement script as ONE op: `None` for the empty script (a node already
    /// where it was asked to go), the op itself for a one-op script.
    let private ofScript (script: SkeletonOp<Node<'Msg>, NodeId> list) : TreeOp<'Msg> option =
        match script |> List.choose ofSkeleton with
        | [] -> None
        | [ op ] -> Some op
        | ops -> Some(TreeOp.Batch ops)

    /// The engine's dry run of `op` over the whole tree, at the keyed reach the
    /// apply engine runs at. A node held AT a keyed position, which the keyed
    /// engine refuses as `KeyedPosition` (naming the holder), is reported with
    /// its slot label.
    let private dryRun (op: SkeletonOp<Node<'Msg>, NodeId>) (root: Node<'Msg>) : Result<unit, PlaceError> =
        match
            Fuaran.Core.Ops.canApply
                Policy.none
                (OpScope.keyed Introspect.keyedWitness canHold Introspect.nodeWitness Introspect.idWitness)
                op
                root
        with
        | Ok() -> Ok()
        | Error(Rejection.KeyedPosition(target, _, _) as rejection) ->
            match Introspect.nonStructuralAncestor target root with
            | Some(_, slot, _, true) -> Error(PlaceError.PositionNotStructural(target, slot))
            | _ -> Error(PlaceError.Refused rejection)
        | Error rejection -> Error(PlaceError.Refused rejection)

    /// The subtree a structural op addressing `addressed` runs against — the
    /// descent `Apply` performs (Phase 1666), one keyed position per turn — or
    /// the keyed-position refusal. `vacated` is the node the op detaches, which
    /// may not BE a position's own node. Reads `Introspect.nonStructuralAncestor`,
    /// the lens the apply engine classifies with, so this cannot drift from the
    /// engine by having its own idea of where the positions are.
    let rec private scopeOf
        (addressed: NodeId list)
        (vacated: NodeId option)
        (root: Node<'Msg>)
        : Result<Node<'Msg>, PlaceError> =
        let located =
            addressed |> List.map (fun id -> id, Introspect.nonStructuralAncestor id root)

        match located |> List.choose (fun (id, at) -> at |> Option.map (fun a -> id, a)) with
        | [] -> Ok root
        | (_, (_, firstSlot, _, _)) :: _ as inside ->
            let positions =
                inside
                |> List.map (fun (_, (holder, slot, _, _)) -> holder, slot)
                |> List.distinct

            // Reported at the first addressed node — the moved one, where the
            // op moves one — and at the first position met on the way.
            let refuse slot =
                Error(PlaceError.PositionNotStructural(List.head addressed, slot))

            if List.length positions > 1 || List.length inside <> List.length located then
                refuse firstSlot
            else
                match inside |> List.tryFind (fun (id, (_, _, _, isAt)) -> isAt && Some id = vacated) with
                | Some(id, (_, slot, _, _)) -> Error(PlaceError.PositionNotStructural(id, slot))
                | None ->
                    let _, (_, _, sub, _) = List.head inside
                    scopeOf addressed vacated sub

    /// The verdict, then the script: the engine's dry run of `op`, the
    /// keyed-position rule, then Core's lowering at the scope the engine will
    /// run the op against.
    let private lower
        (op: SkeletonOp<Node<'Msg>, NodeId>)
        (addressed: NodeId list)
        (vacated: NodeId option)
        (lowerAt: Node<'Msg> -> Result<SkeletonOp<Node<'Msg>, NodeId> list, Fuaran.Core.PlaceError<NodeId>>)
        (root: Node<'Msg>)
        : Result<TreeOp<'Msg> option, PlaceError> =
        dryRun op root
        |> Result.bind (fun () -> scopeOf addressed vacated root)
        |> Result.bind (fun scope -> lowerAt scope |> Result.mapError ofCore)
        |> Result.map ofScript

    /// The op an insertion becomes: Core's `placeContained` — a bare
    /// `InsertChild` when the child lands last, else `Batch [InsertChild;
    /// ReorderChildren]`.
    let placeOp (root: Node<'Msg>) (child: Node<'Msg>) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        lower
            (SkeletonOp.InsertChild(target.ParentId, child))
            [ target.ParentId ]
            None
            (TreePlacement.place
                Policy.none
                (OpScope.contained canHold Introspect.nodeWitness Introspect.idWitness)
                target.ParentId
                target.Placement
                child)
            root
        |> Result.map (
            // `place` always lowers to one op; the insert itself is the floor.
            Option.defaultValue (TreeOp.InsertChild(target.ParentId, child))
        )

    /// The op a move becomes: Core's `move` under the contained scope. To another parent, a bare
    /// `MoveNode` when the node lands last, else `Batch [MoveNode;
    /// ReorderChildren]`; within its own parent, a bare `ReorderChildren` —
    /// or `None` when the node is already where `target` puts it, since an
    /// op that changes nothing would read in the op-stream as an edit that
    /// happened.
    let moveOp (root: Node<'Msg>) (moved: NodeId) (target: Target) : Result<TreeOp<'Msg> option, PlaceError> =
        lower
            (SkeletonOp.MoveNode(moved, target.ParentId))
            [ moved; target.ParentId ]
            (Some moved)
            (TreePlacement.move
                Policy.none
                (OpScope.contained canHold Introspect.nodeWitness Introspect.idWitness)
                moved
                target.ParentId
                target.Placement)
            root

    /// Whether `moved` may legally take up residence at `target` — the verdict
    /// `moveOp` reaches, which an editor uses to grey out an illegal drop.
    let canPlace (root: Node<'Msg>) (moved: NodeId) (target: Target) : Result<unit, PlaceError> =
        moveOp root moved target |> Result.map ignore

    /// The op a keyboard move-up (`-1`) / move-down (`+1`) becomes: Core's move
    /// within the node's own parent to `Anchor.Index (index + delta)` — so a
    /// bare `ReorderChildren`, `None` for a zero delta, and `IndexOutOfRange`
    /// past either end. A delta of more than one SHIFTS the node that many
    /// places (it is a move), it does not swap it with the sibling there.
    let nudgeOp (root: Node<'Msg>) (nodeId: NodeId) (delta: int) : Result<TreeOp<'Msg> option, PlaceError> =
        if NodeId root.Id = nodeId then
            Error(PlaceError.CannotNudgeRoot nodeId)
        else
            // The parent search runs at the KEYED reach: a container held inside a
            // keyed position is a legal nudge target (Phase 1666).
            match Introspect.findParentWithin Fuaran.UI.NodeChildren.Reach.keyed nodeId root with
            | Some(parent, index) ->
                moveOp
                    root
                    nodeId
                    { ParentId = NodeId parent.Id
                      Placement = Anchor.Index(index + delta) }
            | None ->
                // No structural parent: the node is absent, or it IS a keyed
                // position's own node. The engine's dry run of vacating it says
                // which, by name.
                dryRun (SkeletonOp.RemoveNode nodeId) root |> Result.map (fun () -> None)

    /// The op an ORDER-LEVEL reorder becomes: an arbitrary whole permutation of
    /// a parent's children, stated as one bare `ReorderChildren` — or `None`
    /// when `desired` is already the order the children are in.
    ///
    /// **Why this exists beside the verbs above rather than inside them.** Every
    /// other verb here is `Node<'Msg>`-typed and anchor-relative: it takes the
    /// tree, finds the parent, and expresses ONE node's movement relative to a
    /// sibling. That is the right shape for a drag, a keyboard nudge, a paste.
    /// It is the wrong shape for a caller that has already computed the whole
    /// order it wants and holds no `Node<'Msg>` tree to hand — a structural
    /// rewriter working over a skeleton, say. Such callers were re-deriving the
    /// same two lines, and the interesting one is the second.
    ///
    /// **The identity-drop is the point.** Emitting a `ReorderChildren` that
    /// restates the existing order is not wrong, exactly — the apply engine
    /// accepts it and the tree is unchanged — but it puts a no-op in the
    /// op-stream, where it reads as an edit that happened, replays as one, and
    /// shows up in a diff a human is asked to review. Dropping it is the whole
    /// reason the two lines are worth packaging, and it is the leg a
    /// re-derivation is most likely to omit.
    ///
    /// **The permutation obligation is the CALLER's**, and deliberately so.
    /// `ReorderChildren` requires the full sibling id set — a partial list is
    /// refused by the apply engine as `OrderingMismatch`, and rightly, since a
    /// partial order is not one. This helper takes `current` rather than a tree
    /// precisely so it needs no traversal, which means it also has no way to
    /// verify membership beyond what it was handed; checking `desired` against
    /// `current` alone would prove nothing a caller sorting `current` did not
    /// already know. The engine remains the enforcer.
    let reorderOp (parentId: NodeId) (current: NodeId list) (desired: NodeId list) : TreeOp<'Msg> option =
        if desired = current then
            None
        else
            Some(TreeOp.ReorderChildren(parentId, desired))

    // ─── Clone verbs ─────────────────────────────────────────────────────────

    /// Rename every id of `incoming` that `targetRoot` already holds, or that
    /// `incoming` repeats, through Core's `FreshIds.repairDuplicates` over the
    /// keyed walk; every other id is kept.
    let private repairFor
        (mint: NodeId -> Set<string> -> NodeId)
        (targetRoot: Node<'Msg>)
        (incoming: Node<'Msg>)
        : Node<'Msg> =
        let taken =
            Tree.ids traversal targetRoot
            |> List.map Introspect.idWitness.ToString
            |> Set.ofList

        FreshIds.repairDuplicates
            traversal
            Introspect.idWitness
            (fun (NodeId fresh) n -> { n with Id = fresh })
            mint
            taken
            incoming
        |> fst

    /// Duplicate the subtree rooted at `source` and place the copy at `target`,
    /// minting replacement ids with `mint` (`FreshIds.derived
    /// Introspect.idWitness`, `FreshIds.sequential Introspect.idWitness prefix`,
    /// or a host's own strategy of that shape).
    ///
    /// These are `TreePlacement.cloneContained`'s own two steps — rename against
    /// the tree's ids, then place — run at the keyed reach: Core's form looks the
    /// source up and collects the taken ids over the structural witness, which
    /// cannot see a node held in a keyed position.
    let duplicateOpWith
        (mint: NodeId -> Set<string> -> NodeId)
        (root: Node<'Msg>)
        (source: NodeId)
        (target: Target)
        : Result<TreeOp<'Msg>, PlaceError> =
        match Tree.subtree traversal Introspect.idWitness source root with
        | None -> Error(PlaceError.Refused(Rejection.UnknownNode(source, Tree.ids traversal root)))
        | Some sub -> placeOp root (repairFor mint root sub) target

    /// `duplicateOpWith` under Core's derived-suffix strategy (`<id>-copy`,
    /// `<id>-copy-2`, …).
    let duplicateOp (root: Node<'Msg>) (source: NodeId) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        duplicateOpWith (FreshIds.derived Introspect.idWitness) root source target

    /// Place a subtree lifted from a DIFFERENT tree into `targetRoot`, renaming
    /// any id that collides with one already present or that the subtree
    /// repeats; every other id is kept.
    let pasteOpWith
        (mint: NodeId -> Set<string> -> NodeId)
        (targetRoot: Node<'Msg>)
        (incoming: Node<'Msg>)
        (target: Target)
        : Result<TreeOp<'Msg>, PlaceError> =
        placeOp targetRoot (repairFor mint targetRoot incoming) target

    /// `pasteOpWith` under Core's derived-suffix strategy.
    let pasteOp (targetRoot: Node<'Msg>) (incoming: Node<'Msg>) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        pasteOpWith (FreshIds.derived Introspect.idWitness) targetRoot incoming target
