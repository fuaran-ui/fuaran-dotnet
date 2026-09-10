namespace Fuaran.UI.Ops

// ============================================================================
//  Placement algebra — placed insert / move / nudge, and the clone verbs
//  (duplicate / paste) built on top of them.
//
//  The op vocabulary is deliberately positionless: `InsertChild` and `MoveNode`
//  append, and an explicit order is stated only by `ReorderChildren` naming
//  every sibling id (an id is checkable; an ordinal is not). Placing a node
//  anywhere but last is therefore `Batch [InsertChild|MoveNode;
//  ReorderChildren]` — correct, but it leaves every consumer deriving the full
//  sibling permutation itself. This module ships that derivation once, purely
//  additively: every helper emits ops built from the existing vocabulary
//  (`InsertChild` / `MoveNode` / `RemoveNode` / `ReorderChildren` / `Batch`),
//  so the wire format, the apply engine, and the node contract are untouched —
//  and the reorder leg is dropped whenever appending already yields the wanted
//  order, keeping the common case a single bare op.
//
//  Pre-checks mirror the apply engine's own rejections (childless kind,
//  move-into-self, move-into-descendant, duplicate id) so an editor can grey
//  out an illegal drop without a dry-run apply — with one deliberate
//  tightening: an anchor that is not among the destination's post-op children
//  is REFUSED (`UnknownAnchor`) rather than silently appended. The only op
//  that could honour such an anchor would be a `ReorderChildren` naming it,
//  which the apply engine refuses as `OrderingMismatch`; saying so before
//  emission is friendlier than a rejection after it.
//
//  The clone verbs wrap `Fuaran.Core.Tree.subtree` + `remapIds` (whose doc
//  comment names exactly this use: rewrite a copied subtree's ids to a fresh,
//  collision-free set before `InsertChild`). The remap runs over the WHOLE
//  traversal surface — `Introspect.descendantNodes` / `replaceDescendantNodes`,
//  not just the structural child lists — because the id-uniqueness contract is
//  tree-wide, and a clone that kept an old id inside a Switch case or an
//  ErrorBoundary slot would smuggle a duplicate past it.
// ============================================================================

open System.Collections.Generic
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ─── Placement vocabulary ────────────────────────────────────────────────────

/// Where a node should sit among its destination siblings, stated the only way
/// the op vocabulary allows: by naming an existing sibling, or an end.
[<RequireQualifiedAccess>]
type Placement =
    /// Append — what `InsertChild` / `MoveNode` do on their own.
    | Last
    /// Prepend — before every current sibling.
    | First
    /// Immediately before the named sibling.
    | Before of anchor: NodeId
    /// Immediately after the named sibling.
    | After of anchor: NodeId

/// A structural destination: which parent, and where among its children.
type Target =
    { ParentId: NodeId
      Placement: Placement }

/// Why a placement could not become an op. Each case is a pre-statement of the
/// apply-time refusal the emitted op would have met, so a helper rejection and
/// an apply rejection agree — no false permit, no false refuse.
[<RequireQualifiedAccess>]
type PlaceError =
    /// The destination parent is not in the tree (apply: `ParentNotFound`).
    | ParentNotFound of parentId: NodeId
    /// The destination parent's kind has no `Children` field (apply:
    /// `ChildlessKind`).
    | ChildlessKind of parentId: NodeId
    /// The node to move / nudge / duplicate is not in the tree at all (apply:
    /// `NodeNotFound`).
    ///
    /// Phase 1666 NARROWED this case. It used to cover a node held in — or
    /// below — a keyed position as well, because the apply engine refused those
    /// as `NodeNotFound` too. The engine no longer does: it descends into a
    /// keyed position to reach a node below one, and names the position when it
    /// refuses. This case is absence, and only absence.
    | NodeNotFound of nodeId: NodeId
    /// The op would change the ARITY of a keyed position, or cross one (apply:
    /// `PositionNotStructural`). Carries the node addressed and the position's
    /// slot label in the §3.3 spelling (`Switch.cases[0].child`).
    ///
    /// Three shapes reach it, and they are one fact seen from three sides: the
    /// node IS the position's own node, so removing or moving it would leave
    /// the position empty; the node sits below one position and the destination
    /// below another; or one of the two sits below a position and the other on
    /// the structural spine. A single write through an arity-preserving lens
    /// cannot express any of them, and inventing a two-write form would be a
    /// wire decision rather than an engine one.
    ///
    /// Reaching THROUGH a position is not an error at all — a node below one
    /// moves, nudges and takes inserts normally, WITHIN that position's subtree.
    | PositionNotStructural of nodeId: NodeId * slot: string
    /// The placement anchor is not among the destination's post-op children.
    /// The only op that could honour it — a `ReorderChildren` naming it — is
    /// refused by the apply engine as `OrderingMismatch`.
    | UnknownAnchor of anchor: NodeId
    /// The subtree being inserted carries an id already present in the tree
    /// (apply: `DuplicateNodeId`).
    | DuplicateId of nodeId: NodeId
    /// The node would become its own parent (apply: `KindMismatch`).
    | MoveIntoSelf of nodeId: NodeId
    /// The destination sits inside the node's own subtree — a cycle (apply:
    /// `KindMismatch`).
    | MoveIntoDescendant of nodeId: NodeId * parentId: NodeId
    /// The root has no siblings to nudge among.
    | CannotNudgeRoot of nodeId: NodeId
    /// The nudge would leave the sibling range (already first / already last).
    | NudgeOutOfRange of nodeId: NodeId * delta: int

// ─── Fresh-id strategy (the clone verbs' id-minting seam) ────────────────────

/// How the clone verbs mint replacement ids: given the id being replaced and a
/// predicate over every id already claimed (the whole target tree, the whole
/// incoming subtree, and ids minted earlier in the same remap), return an id
/// the predicate refuses. Injectable so a host with its own id discipline can
/// supply it; `FreshIds.derived` is the default.
type FreshIds = string -> (string -> bool) -> string

[<RequireQualifiedAccess>]
module FreshIds =
    /// The default: `<oldId>-copy`, then `<oldId>-copy-2`, `-copy-3`, … — the
    /// first candidate not already taken. Deterministic (derived from the id
    /// it replaces, no ambient state) and collision-free by probing — the same
    /// derive-and-probe convention the tree's other id-minting surfaces use.
    let derived: FreshIds =
        fun oldId taken ->
            let rec probe (n: int) =
                let candidate =
                    if n = 1 then
                        oldId + "-copy"
                    else
                        oldId + "-copy-" + string n

                if taken candidate then probe (n + 1) else candidate

            probe 1

    /// Sequential ids under a fixed prefix (`<prefix>-1`, `-2`, …) — the
    /// deterministic-replay option: the minted sequence depends only on the
    /// prefix and the order of requests, never on the ids being replaced.
    /// Each call to `sequential` starts its own counter.
    let sequential (prefix: string) : FreshIds =
        let counter = ref 0

        fun _ taken ->
            let rec probe () =
                counter.Value <- counter.Value + 1
                let candidate = prefix + "-" + string counter.Value
                if taken candidate then probe () else candidate

            probe ()

// ─── The verbs ───────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
module Placement =

    let private idw: IdWitness<NodeId> =
        { ToString = fun (NodeId s) -> s
          OfString = NodeId
          Equals = (=) }

    /// The whole-traversal-surface witness: children are `descendantNodes`
    /// (structural children plus the non-structural slots), so the clone
    /// verbs' subtree extraction and id-remap reach every node the tree-wide
    /// duplicate-id contract covers — not just the structural surface.
    let private traversalWitness () : NodeWitness<Node<'Msg>, NodeId> =
        { Id = fun n -> NodeId n.Id
          KindTag = fun n -> Introspect.kindName n.Kind
          Children = Introspect.descendantNodes
          ReplaceChildren = fun n cs -> Introspect.replaceDescendantNodes n cs }

    /// The destination's current child ids, or the mirrored apply-side refusal
    /// (absent parent / childless kind).
    let private containerChildren (root: Node<'Msg>) (parentId: NodeId) : Result<NodeId list, PlaceError> =
        match Introspect.findNode parentId root with
        | None -> Error(PlaceError.ParentNotFound parentId)
        | Some parent ->
            match Introspect.getChildren parent.Kind with
            | None -> Error(PlaceError.ChildlessKind parentId)
            | Some children -> Ok(children |> List.map (fun c -> NodeId c.Id))

    /// Place `moved` within `order` (which already contains it) per
    /// `placement`. An anchor that is not in the list is refused — the honest
    /// alternative (silently appending) would emit an op that does not honour
    /// the caller's stated intent.
    let private reposition
        (order: NodeId list)
        (moved: NodeId)
        (placement: Placement)
        : Result<NodeId list, PlaceError> =
        let rest = order |> List.filter (fun id -> id <> moved)

        let anchored (anchor: NodeId) (offset: int) =
            match rest |> List.tryFindIndex (fun id -> id = anchor) with
            | None -> Error(PlaceError.UnknownAnchor anchor)
            | Some i ->
                let at = i + offset
                Ok((rest |> List.truncate at) @ [ moved ] @ (rest |> List.skip at))

        match placement with
        | Placement.Last -> Ok(rest @ [ moved ])
        | Placement.First -> Ok(moved :: rest)
        | Placement.Before anchor -> anchored anchor 0
        | Placement.After anchor -> anchored anchor 1

    /// The keyed position `nodeId` sits AT or BELOW, as `(holder, slot, isAt)`,
    /// or `None` when it is reachable through `Children` from the root
    /// (Phase 1666).
    ///
    /// Reads `Introspect.nonStructuralAncestor` — the same lens the apply engine
    /// classifies with — so the helper cannot drift from the engine by having
    /// its own idea of where the positions are. `None` here does NOT distinguish
    /// a spine node from an absent one; `Introspect.findNode` answers that, and
    /// every caller below asks it first.
    let private keyedPosition (nodeId: NodeId) (root: Node<'Msg>) : (NodeId * string * bool) option =
        Introspect.nonStructuralAncestor nodeId root
        |> Option.map (fun (holder, slot, _, isAt) -> holder, slot, isAt)

    /// The position IDENTITY, for the crossing test: two nodes may take part in
    /// one structural op iff they answer the same value here.
    let private positionKey (position: (NodeId * string * bool) option) : (NodeId * string) option =
        position |> Option.map (fun (holder, slot, _) -> holder, slot)

    /// The node whose STRUCTURAL children contain `target`, searched over the
    /// whole tree rather than the structural spine alone (Phase 1666).
    ///
    /// `Introspect.findParent` walks `getChildren` from the root, so it cannot
    /// see a container held inside a keyed position — and since the engine now
    /// DESCENDS into such a position, a nudge below one is legal and this helper
    /// has to be able to express it. Everything else about the answer is
    /// unchanged: the parent found is always a structural container, so the
    /// reorder it produces names that container's own children.
    let private findStructuralParent (target: NodeId) (root: Node<'Msg>) : (Node<'Msg> * int) option =
        let (NodeId targetRaw) = target

        let rec walk (node: Node<'Msg>) =
            let here =
                Introspect.getChildren node.Kind
                |> Option.bind (fun children ->
                    children
                    |> List.tryFindIndex (fun c -> c.Id = targetRaw)
                    |> Option.map (fun i -> node, i))

            match here with
            | Some hit -> Some hit
            | None -> Introspect.descendantNodes node |> List.tryPick walk

        walk root

    /// Whether `moved` may legally take up residence at `target` — the
    /// pre-check an editor uses to grey out an illegal drop without a dry-run
    /// apply. Mirrors the apply engine's rejections: absent node, move into
    /// itself, move into its own descendant (a cycle), absent or childless
    /// destination, unknown anchor.
    let canPlace (root: Node<'Msg>) (moved: NodeId) (target: Target) : Result<unit, PlaceError> =
        // Phase 1666 — the three answers the engine now gives, in its order:
        // absence first (nothing else is meaningful about a node that is not
        // there), then the ARITY guard, then the CROSSING guard.
        let movedPosition = keyedPosition moved root
        let destPosition = keyedPosition target.ParentId root

        if Introspect.findNode moved root |> Option.isNone then
            Error(PlaceError.NodeNotFound moved)
        elif movedPosition |> Option.exists (fun (_, _, isAt) -> isAt) then
            let _, slot, _ = Option.get movedPosition
            Error(PlaceError.PositionNotStructural(moved, slot))
        elif positionKey movedPosition <> positionKey destPosition then
            // The op crosses a keyed position — between two of them, or between
            // one and the structural spine. Reported at the position the MOVED
            // node is in where there is one, since that is the side the caller
            // addressed; otherwise at the destination's.
            let slot =
                match movedPosition, destPosition with
                | Some(_, slot, _), _
                | None, Some(_, slot, _) -> slot
                // Unreachable: the keys differ, so at least one side is Some.
                | None, None -> ""

            Error(PlaceError.PositionNotStructural(moved, slot))
        elif target.ParentId = moved then
            Error(PlaceError.MoveIntoSelf moved)
        elif Introspect.isAncestorOf moved target.ParentId root then
            Error(PlaceError.MoveIntoDescendant(moved, target.ParentId))
        else
            containerChildren root target.ParentId
            |> Result.bind (fun siblings ->
                let membership = (siblings |> List.filter (fun id -> id <> moved)) @ [ moved ]
                reposition membership moved target.Placement |> Result.map ignore)

    /// The op an insertion becomes. `InsertChild` appends, so the wanted order
    /// is computed over the post-insert membership and stated by
    /// `ReorderChildren` naming every sibling id; the reorder leg is dropped
    /// when appending already produces that order.
    let placeOp (root: Node<'Msg>) (child: Node<'Msg>) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        containerChildren root target.ParentId
        |> Result.bind (fun siblings ->
            match Introspect.firstSharedId root child with
            | Some dup -> Error(PlaceError.DuplicateId dup)
            | None ->
                let childId = NodeId child.Id
                let appended = siblings @ [ childId ]

                reposition appended childId target.Placement
                |> Result.map (fun wanted ->
                    let insert = TreeOp.InsertChild(target.ParentId, child)

                    if wanted = appended then
                        insert
                    else
                        TreeOp.Batch [ insert; TreeOp.ReorderChildren(target.ParentId, wanted) ]))

    /// The op a move becomes. `MoveNode` appends under the new parent, and the
    /// node may already be one of that parent's children (a re-placement
    /// within one parent), so the post-move membership is the siblings WITHOUT
    /// it plus it.
    let moveOp (root: Node<'Msg>) (moved: NodeId) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        canPlace root moved target
        |> Result.bind (fun () ->
            containerChildren root target.ParentId
            |> Result.bind (fun siblings ->
                let appended = (siblings |> List.filter (fun id -> id <> moved)) @ [ moved ]

                reposition appended moved target.Placement
                |> Result.map (fun wanted ->
                    let move = TreeOp.MoveNode(moved, target.ParentId)

                    if wanted = appended then
                        move
                    else
                        TreeOp.Batch [ move; TreeOp.ReorderChildren(target.ParentId, wanted) ])))

    /// The op a keyboard move-up (`-1`) / move-down (`+1`) becomes: the node
    /// swapped with the sibling `delta` positions away, stated as the FULL
    /// sibling id order (which is what `ReorderChildren` requires — a partial
    /// list is refused by the apply engine, and rightly, since a partial order
    /// is not one).
    let nudgeOp (root: Node<'Msg>) (nodeId: NodeId) (delta: int) : Result<TreeOp<'Msg>, PlaceError> =
        if NodeId root.Id = nodeId then
            Error(PlaceError.CannotNudgeRoot nodeId)
        else
            // Phase 1666 — `findStructuralParent`, not `Introspect.findParent`:
            // a container held inside a keyed position is a legal nudge target
            // now that the engine descends into one, and a node that IS a
            // position's own node has no sibling list to nudge among, which is
            // a different answer from absence.
            match findStructuralParent nodeId root with
            | None ->
                match keyedPosition nodeId root with
                | Some(_, slot, true) -> Error(PlaceError.PositionNotStructural(nodeId, slot))
                | _ -> Error(PlaceError.NodeNotFound nodeId)
            | Some(parent, index) ->
                let ids =
                    Introspect.getChildren parent.Kind
                    |> Option.defaultValue []
                    |> List.map (fun c -> NodeId c.Id)

                let swapWith = index + delta

                if swapWith < 0 || swapWith >= List.length ids then
                    Error(PlaceError.NudgeOutOfRange(nodeId, delta))
                else
                    let reordered =
                        ids
                        |> List.mapi (fun i id ->
                            if i = index then ids[swapWith]
                            elif i = swapWith then ids[index]
                            else id)

                    Ok(TreeOp.ReorderChildren(NodeId parent.Id, reordered))

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

    /// Rewrite every id in `incoming` that collides with an id in `targetRoot`
    /// to a fresh, collision-free one (via `Fuaran.Core.Tree.remapIds` over the
    /// whole-traversal-surface witness). Ids with no collision are preserved —
    /// a pasted subtree keeps its identity where it can; a subtree duplicated
    /// within its own tree remaps every id, since every one collides.
    let private remapForInsert (freshIds: FreshIds) (targetRoot: Node<'Msg>) (incoming: Node<'Msg>) : Node<'Msg> =
        let existing = HashSet<string>()

        for (NodeId s) in Introspect.allNodeIds targetRoot do
            existing.Add s |> ignore

        // Fresh ids must also dodge the incoming subtree's own ids (a minted id
        // colliding with a not-yet-visited incoming node would re-introduce the
        // duplicate the remap exists to remove) and each other.
        let taken = HashSet<string>(existing)

        for (NodeId s) in Introspect.allNodeIds incoming do
            taken.Add s |> ignore

        let rename =
            Introspect.allNodeIds incoming
            |> List.choose (fun (NodeId oldId) ->
                if existing.Contains oldId then
                    let fresh = freshIds oldId (fun candidate -> taken.Contains candidate)
                    taken.Add fresh |> ignore
                    Some(oldId, fresh)
                else
                    None)
            |> Map.ofList

        if Map.isEmpty rename then
            incoming
        else
            Tree.remapIds
                (traversalWitness ())
                (fun (NodeId fresh) n -> { n with Id = fresh })
                (fun (NodeId oldId) -> NodeId(rename |> Map.tryFind oldId |> Option.defaultValue oldId))
                incoming

    /// Duplicate the subtree rooted at `source` and place the clone at
    /// `target`, minting replacement ids with `freshIds`. The emitted op is an
    /// ordinary placed insert — the clone is a fresh subtree, so the standard
    /// apply gate (including the tree-wide duplicate-id check) accepts it
    /// unchanged.
    let duplicateOpWith
        (freshIds: FreshIds)
        (root: Node<'Msg>)
        (source: NodeId)
        (target: Target)
        : Result<TreeOp<'Msg>, PlaceError> =
        match Tree.subtree (traversalWitness ()) idw source root with
        | None -> Error(PlaceError.NodeNotFound source)
        | Some sub -> placeOp root (remapForInsert freshIds root sub) target

    /// `duplicateOpWith` under the default derived-suffix id strategy.
    let duplicateOp (root: Node<'Msg>) (source: NodeId) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        duplicateOpWith FreshIds.derived root source target

    /// Place a subtree lifted from a DIFFERENT tree into `targetRoot`,
    /// remapping any id that collides with one already present (ids with no
    /// collision are preserved). The incoming subtree's ids must be unique
    /// within itself — a subtree extracted from any well-formed tree is.
    let pasteOpWith
        (freshIds: FreshIds)
        (targetRoot: Node<'Msg>)
        (incoming: Node<'Msg>)
        (target: Target)
        : Result<TreeOp<'Msg>, PlaceError> =
        placeOp targetRoot (remapForInsert freshIds targetRoot incoming) target

    /// `pasteOpWith` under the default derived-suffix id strategy.
    let pasteOp (targetRoot: Node<'Msg>) (incoming: Node<'Msg>) (target: Target) : Result<TreeOp<'Msg>, PlaceError> =
        pasteOpWith FreshIds.derived targetRoot incoming target
