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
//    - `InsertChild child` → `InsertFragment(parent, pos, renderFragment child)`
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
    let reRender (id: NodeId) : DomPatch list =
        match findNode id newTree with
        | Some node -> [ DomPatch.ReplaceFragment(raw id, renderFragment node) ]
        | None -> []

    match op with
    | TreeOp.RemoveNode id -> [ DomPatch.RemoveNode(raw id) ]
    | TreeOp.ReorderChildren(parentId, orderedIds) ->
        [ DomPatch.ReorderChildren(raw parentId, orderedIds |> List.map raw) ]
    | TreeOp.MoveNode(id, newParentId, position) -> [ DomPatch.MoveNode(raw id, raw newParentId, position) ]
    | TreeOp.InsertChild(parentId, position, child) ->
        [ DomPatch.InsertFragment(raw parentId, position, renderFragment child) ]
    | TreeOp.EditNode(id, _)
    | TreeOp.UpdateProp(id, _, _)
    | TreeOp.UpdateStyle(id, _)
    | TreeOp.UpdateState(id, _)
    | TreeOp.ReplaceBinding(id, _, _) -> reRender id
    // Whole-tree swap: re-render the new root fragment (= replace all content).
    | TreeOp.ReplaceRoot node -> reRender node.Id
    | TreeOp.Batch ops -> ops |> List.collect (lowerOp renderFragment newTree)

/// Lower a `TreeOp` list (e.g. a `TreeOpDiff.diff` result) to the `DomPatch`
/// list the shim applies, in order.
let lower<'Msg> (renderFragment: Node<'Msg> -> string) (newTree: Node<'Msg>) (ops: TreeOp<'Msg> list) : DomPatch list =
    ops |> List.collect (lowerOp renderFragment newTree)
