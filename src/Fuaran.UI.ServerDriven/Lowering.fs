module Fuaran.UI.ServerDriven.Lowering

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect

// ============================================================================
//  TreeOp → DomPatch lowering (Phase 152, Track B).
//
//  Joins Track A (the `TreeOp`-emitting diff) to Track B (the browser-applyable
//  `DomPatch` wire): a per-turn `TreeOp` list is lowered to the closure-free
//  `DomPatch`es the generic JS shim applies.
//
//  **Renderer injected, not depended on.** Structure-adding / content-changing
//  ops need a freshly-rendered HTML fragment; rather than take a hard dependency
//  on `Fuaran.UI.Renderer.Server` (Feliz.ViewEngine, server-only — which would
//  drag a non-Fable dep + the `Node<obj>` shape into this Fable-clean core), the
//  host injects a `renderFragment : Node<'Msg> -> string` (wired via
//  `Render.render` server-side). The "six portability rules" seam posture: the
//  lowering owns the op→patch mapping; the host owns HTML production.
//
//  **Floor lowering.** Content-changing ops (`EditNode` / `UpdateProp` /
//  `UpdateStyle` / `UpdateState` / `ReplaceBinding`) re-render the **changed
//  node** and emit `ReplaceFragment(id, html)` — targeted (only that node's
//  subtree, not the document; the Track-A diff already localised to the changed
//  node). The granular `SetText` / `SetAttr` optimisation (avoid even the node
//  re-render where a `UpdateProp` field maps cleanly to one DOM mutation) is the
//  documented follow-on. Structural ops lower directly:
//    - `RemoveNode`        → `DomPatch.RemoveNode`
//    - `ReorderChildren`   → `DomPatch.ReorderChildren` (same-parent DOM reorder)
//    - `MoveNode`          → `DomPatch.MoveNode` (identity-preserving relocate)
//    - `InsertChild child` → `InsertFragment(parent, pos, renderFragment child)` — `pos`
//      DERIVED from `newTree` (the ops append; the DOM still needs an index)
//    - `Batch ops`         → the concatenated lowering of its inner ops.
//
//  `newTree` is the post-apply tree — content ops look the freshly-updated node
//  up by id (`findNode`) to render it. (A `RemoveNode`'s node is gone in
//  `newTree`, correctly — it renders nothing.)
// ============================================================================

let private raw (NodeId s) : string = s

/// Lower one `TreeOp` to its `DomPatch`es. `renderFragment` renders a node's
/// HTML (host-injected); `newTree` is the post-apply tree.
let rec lowerOp<'Msg> (renderFragment: Node<'Msg> -> string) (newTree: Node<'Msg>) (op: TreeOp<'Msg>) : DomPatch list =
    // The DOM genuinely needs an insertion index — but it is DERIVED here, at the
    // boundary that needs it, from the post-apply tree, rather than carried through
    // the op vocabulary. `InsertChild` / `MoveNode` append, so the landed child is
    // last under its new parent. ONE fallback when it cannot be found (Phase 2071):
    // the APPEND position, the parent's child count — 0 under a missing parent or
    // a childless kind, where the patch is a no-op client-side anyway. (A child
    // absent from a found parent used to get `len-1`, which the shim reads as
    // "before the last child".)
    let landedIndex (parentId: NodeId) (childId: NodeId) : int =
        let kids =
            findNode parentId newTree
            |> Option.bind (fun parent -> getChildren parent.Kind)
            |> Option.defaultValue []

        kids
        |> List.tryFindIndex (fun c -> NodeId c.Id = childId)
        |> Option.defaultValue (List.length kids)

    let reRender (id: NodeId) : DomPatch list =
        match findNode id newTree with
        | Some node -> [ DomPatch.ReplaceFragment(raw id, renderFragment node) ]
        | None -> []

    match op with
    | TreeOp.RemoveNode id -> [ DomPatch.RemoveNode(raw id) ]
    | TreeOp.ReorderChildren(parentId, orderedIds) ->
        [ DomPatch.ReorderChildren(raw parentId, orderedIds |> List.map raw) ]
    | TreeOp.MoveNode(id, newParentId) -> [ DomPatch.MoveNode(raw id, raw newParentId, landedIndex newParentId id) ]
    | TreeOp.InsertChild(parentId, child) ->
        [ DomPatch.InsertFragment(raw parentId, landedIndex parentId (NodeId child.Id), renderFragment child) ]
    | TreeOp.EditNode(id, _)
    | TreeOp.UpdateProp(id, _, _)
    | TreeOp.UpdateStyle(id, _)
    | TreeOp.PatchStyle(id, _)
    | TreeOp.UpdateState(id, _)
    | TreeOp.ReplaceBinding(id, _, _) -> reRender id
    // Whole-tree swap: re-render the new root fragment (= replace all content).
    | TreeOp.ReplaceRoot node -> reRender (NodeId node.Id)
    | TreeOp.Batch ops -> ops |> List.collect (lowerOp renderFragment newTree)

/// Lower a `TreeOp` list to the `DomPatch` list the shim applies, in order,
/// reading every op against the ONE post-apply tree `newTree`. Exact for a
/// single op, and for a list whose inserts and moves land where they stay and
/// whose content ops re-render nodes no later op restructures. A whole diff
/// script is not such a list — `lowerScript` is its lowering.
let lower<'Msg> (renderFragment: Node<'Msg> -> string) (newTree: Node<'Msg>) (ops: TreeOp<'Msg> list) : DomPatch list =
    ops |> List.collect (lowerOp renderFragment newTree)

/// Lower a whole `TreeOp` script — a `TreeOpDiff.diff` result — applied to
/// `oldTree`, reading each op against the tree as it stands right AFTER that op
/// (Phase 2304). The DOM the shim patches is the tree the script has built so
/// far, so this is the reading under which every patch addresses what the DOM
/// holds: an insert's or move's landed index is its index at that step (the
/// append position), not its final index among siblings a later removal has
/// yet to drop, and a container re-rendered by an early content op renders the
/// children it holds at that step, which the later structural patches then
/// rearrange. Core's diff orders a script so that holds by construction —
/// content of containers first, inserts and moves, removals, reorders, then
/// content of leaves — which `lower` against the final tree does not
/// tolerate. An op the apply engine refuses lowers to nothing and leaves the
/// tree as it was.
let rec private lowerSteps<'Msg>
    (renderFragment: Node<'Msg> -> string)
    (tree: Node<'Msg>)
    (ops: TreeOp<'Msg> list)
    : DomPatch list list * Node<'Msg> =
    // Chunks accumulate newest-first, so a long script stays linear.
    ops
    |> List.fold
        (fun (chunks: DomPatch list list, t: Node<'Msg>) op ->
            match op with
            | TreeOp.Batch inner ->
                let inner', t' = lowerSteps renderFragment t inner
                inner' @ chunks, t'
            | _ ->
                match Fuaran.UI.Ops.Apply.apply op t with
                | Ok t' -> lowerOp renderFragment t' op :: chunks, t'
                | Error _ -> chunks, t)
        ([], tree)

/// `lowerSteps` from `oldTree` — the lowering of a diff script (see above).
let lowerScript<'Msg>
    (renderFragment: Node<'Msg> -> string)
    (oldTree: Node<'Msg>)
    (ops: TreeOp<'Msg> list)
    : DomPatch list =
    lowerSteps renderFragment oldTree ops |> fst |> List.rev |> List.concat
