module Fuaran.UI.ServerDriven.Lowering

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.Renderer

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
//  **Floor lowering.** A content op (`EditNode` / `UpdateProp` / `UpdateStyle` /
//  `PatchStyle` / `UpdateState` / `UpdateAccessibility` / `UpdateTooltip` /
//  `ReplaceBinding`) re-renders the **changed
//  node** and emits `ReplaceFragment(id, html)` — targeted (only that node's
//  subtree, not the document; the Track-A diff already localised to the changed
//  node). The floor is always correct, and it is what a write gets unless the
//  granular arm below claims it.
//
//  **Granular lowering (Phase 2526).** A write the prop map covers needs no
//  render at all:
//    - `UpdateProp` on a mapped attribute → `SetAttr` per attribute it moves, or
//      `RemoveAttr` where the value cleared the attribute;
//    - `UpdateProp` on a text field holding a literal → `SetText`;
//    - a text-only `EditNode` of a `Heading` or `Badge` → `SetText`, in a script
//      (`lowerScript`), the one reading that holds the node's old kind;
//    - an `UpdateState` that leaves a stateless node stateless → nothing, in a
//      script (the diff pairs every `EditNode` with one).
//  Everything else keeps the floor: a bound text (its value needs the host's
//  sources, which this core does not hold), a prop that moves the tag or the
//  element structure, a URL (`ReplaceBinding`, egress-checked by the host's
//  render), every style, state and binding edit, and both trait edits: a node's
//  accessibility and tooltip land on its wrapper unless the kind forwards them
//  to its body, and a granular patch cannot reach the wrapper.
//
//  The map is `PropSlot.slotOf`, and it says WHERE a prop lands, never what it
//  renders as: the value is read off `ElementPlan`, the same plan the server
//  renderer emits, so the patch and the fragment cannot disagree. A node's
//  `data-fuaran-node-id` sits on its wrapper; the body element the plan describes
//  is the wrapper's first element child, and that is where the shim lands a
//  node-addressed `SetText` / `SetAttr` / `RemoveAttr`.
//
//  Structural ops lower directly:
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

// ─── The prop map (Phase 2526) ──────────────────────────────────────────────

/// Where one prop lands on a node's body element.
[<RequireQualifiedAccess>]
type PropSlot =
    /// The body element's whole text, written from this source.
    | Text of TextSource
    /// These attributes of the body element. A prop may move more than one:
    /// a link's `target` decides its `rel`.
    | Attrs of string list

[<RequireQualifiedAccess>]
module PropSlot =
    /// The prop map. A kind or field not listed keeps the floor.
    let slotOf (kind: NodeKind<'Msg>) (field: string) : PropSlot option =
        match kind, field with
        | NodeKind.Heading spec, "Text" -> Some(PropSlot.Text spec.Text)
        | NodeKind.Heading _, "Variant" -> Some(PropSlot.Attrs [ "class" ])
        | NodeKind.Badge spec, "Label" -> Some(PropSlot.Text spec.Label)
        | NodeKind.Badge _, "Variant" -> Some(PropSlot.Attrs [ "class" ])
        // A declared affordance stamps annotations read off the button's own
        // fields, and an icon makes the label one child of two.
        | NodeKind.Button spec, "Label" when spec.Icon.IsNone && spec.Affordance.IsNone ->
            Some(PropSlot.Text spec.Label)
        | NodeKind.Button spec, "Variant" when spec.Affordance.IsNone -> Some(PropSlot.Attrs [ "class" ])
        | NodeKind.Icon _, "Icon" -> Some(PropSlot.Attrs [ "data-icon" ])
        | NodeKind.Icon _, ("Size" | "Tone") -> Some(PropSlot.Attrs [ "class" ])
        | NodeKind.Icon _, "Label" -> Some(PropSlot.Attrs [ "role"; "aria-label"; "aria-hidden" ])
        // A protected link's anchor is pre-built markup inside its wrap.
        | NodeKind.Link spec, "Label" when spec.Protection.IsNone -> Some(PropSlot.Text spec.Label)
        | NodeKind.Link spec, "Rel" when spec.Protection.IsNone -> Some(PropSlot.Attrs [ "rel" ])
        | NodeKind.Link spec, "Target" when spec.Protection.IsNone -> Some(PropSlot.Attrs [ "target"; "rel" ])
        | NodeKind.Link spec, "Download" when spec.Protection.IsNone -> Some(PropSlot.Attrs [ "download" ])
        | _ -> None

    /// The body plan the server renderer emits for a mapped kind, under no
    /// sources: every value this module reads from it is independent of them
    /// (a `Text` slot is taken only for a literal).
    let private bodyPlan (kind: NodeKind<'Msg>) : ElementPlan.Plan<unit> option =
        let sources = BindingResolver.empty

        match kind with
        | NodeKind.Heading spec -> Some(ElementPlan.DisplayPlan.heading sources spec)
        | NodeKind.Badge spec -> Some(ElementPlan.DisplayPlan.badge sources spec)
        | NodeKind.Icon spec -> Some(ElementPlan.DisplayPlan.icon spec)
        | NodeKind.Button spec -> Some(ElementPlan.InputPlan.button sources spec [])
        | NodeKind.Link spec ->
            let context: ElementPlan.PlanContext<'Msg, unit> =
                { Sources = sources
                  Csp = Csp.Permissive
                  Collector = None
                  Egress = Sanitize.permissiveEgress
                  NodeId = ""
                  Child = fun _ -> ElementPlan.Plan.Nothing }

            Some(ElementPlan.MediaPlan.link context spec [])
        | _ -> None

    let private attribute (body: ElementPlan.ElementSpec<unit>) (name: string) : string option =
        body.Props
        |> List.tryPick (function
            | ElementPlan.Prop.Attr(n, value) when n = name -> Some value
            | ElementPlan.Prop.Flag n when n = name -> Some ""
            | _ -> None)

    /// The patches that take the node's rendered body to `node`'s, for one
    /// slot; `None` when the plan does not bear the slot out.
    let patches (node: Node<'Msg>) (slot: PropSlot) : DomPatch list option =
        match slot, bodyPlan node.Kind with
        | PropSlot.Text(TextSource.Literal _),
          Some(ElementPlan.Plan.Element { Content = ElementPlan.Content.Text text }) ->
            Some [ DomPatch.SetText(node.Id, text) ]
        | PropSlot.Attrs names, Some(ElementPlan.Plan.Element body) ->
            Some
                [ for name in names ->
                      match attribute body name with
                      | Some value -> DomPatch.SetAttr(node.Id, name, value)
                      | None -> DomPatch.RemoveAttr(node.Id, name) ]
        | _ -> None

    /// The `SetText` of an `EditNode` that moved `after`'s text and nothing else
    /// the body shows. Only the kinds whose every field reaches the plan: a
    /// bound or closure-bearing field could differ under an equal plan.
    let textOnlyEdit (before: Node<'Msg>) (after: Node<'Msg>) : DomPatch list option =
        match before.Kind, after.Kind with
        | NodeKind.Heading a, NodeKind.Heading b when a.Level = b.Level && a.Variant = b.Variant ->
            patches after (PropSlot.Text b.Text)
        | NodeKind.Badge a, NodeKind.Badge b when a.Variant = b.Variant -> patches after (PropSlot.Text b.Label)
        | _ -> None

let private stateless (node: Node<'Msg>) : bool =
    match node.State with
    | None -> true
    | Some state -> state.OnLoading.IsNone && state.OnEmpty.IsNone && state.OnError.IsNone

/// `lowerOp` under an explicit map, reading `oldTree` where the caller holds the
/// tree the op was applied to. The law perturbs the map through this.
let rec lowerOpWith<'Msg>
    (slotOf: NodeKind<'Msg> -> string -> PropSlot option)
    (renderFragment: Node<'Msg> -> string)
    (oldTree: Node<'Msg> option)
    (newTree: Node<'Msg>)
    (op: TreeOp<'Msg>)
    : DomPatch list =
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

    // The node as it stands before the op and after it, where both are held.
    let bothSides (id: NodeId) : (Node<'Msg> * Node<'Msg>) option =
        match oldTree |> Option.bind (findNode id), findNode id newTree with
        | Some before, Some after -> Some(before, after)
        | _ -> None

    match op with
    | TreeOp.RemoveNode id -> [ DomPatch.RemoveNode(raw id) ]
    | TreeOp.ReorderChildren(parentId, orderedIds) ->
        [ DomPatch.ReorderChildren(raw parentId, orderedIds |> List.map raw) ]
    | TreeOp.MoveNode(id, newParentId) -> [ DomPatch.MoveNode(raw id, raw newParentId, landedIndex newParentId id) ]
    | TreeOp.InsertChild(parentId, child) ->
        [ DomPatch.InsertFragment(raw parentId, landedIndex parentId (NodeId child.Id), renderFragment child) ]
    | TreeOp.UpdateProp(id, field, _) ->
        findNode id newTree
        |> Option.bind (fun node -> slotOf node.Kind field |> Option.bind (PropSlot.patches node))
        |> Option.defaultWith (fun () -> reRender id)
    | TreeOp.EditNode(id, _) ->
        bothSides id
        |> Option.bind (fun (before, after) -> PropSlot.textOnlyEdit before after)
        |> Option.defaultWith (fun () -> reRender id)
    | TreeOp.UpdateState(id, _) ->
        match bothSides id with
        | Some(before, after) when stateless before && stateless after -> []
        | _ -> reRender id
    | TreeOp.UpdateStyle(id, _)
    | TreeOp.PatchStyle(id, _)
    | TreeOp.UpdateAccessibility(id, _)
    | TreeOp.UpdateTooltip(id, _)
    | TreeOp.ReplaceBinding(id, _, _) -> reRender id
    // Whole-tree swap: re-render the new root fragment (= replace all content).
    | TreeOp.ReplaceRoot node -> reRender (NodeId node.Id)
    | TreeOp.Batch ops -> ops |> List.collect (lowerOpWith slotOf renderFragment oldTree newTree)

/// Lower one `TreeOp` to its `DomPatch`es. `renderFragment` renders a node's
/// HTML (host-injected); `newTree` is the post-apply tree.
let lowerOp<'Msg> (renderFragment: Node<'Msg> -> string) (newTree: Node<'Msg>) (op: TreeOp<'Msg>) : DomPatch list =
    lowerOpWith PropSlot.slotOf renderFragment None newTree op

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
    (slotOf: NodeKind<'Msg> -> string -> PropSlot option)
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
                let inner', t' = lowerSteps slotOf renderFragment t inner
                inner' @ chunks, t'
            | _ ->
                match Fuaran.UI.Ops.Apply.apply op t with
                | Ok t' -> lowerOpWith slotOf renderFragment (Some t) t' op :: chunks, t'
                | Error _ -> chunks, t)
        ([], tree)

/// Two adjacent re-renders of one node ship the first for nothing: the second
/// replaces it whole. The diff's floor spells one node's change as an `EditNode`
/// followed by its state and style edits, each a step of its own.
let private supersede (patches: DomPatch list) : DomPatch list =
    List.foldBack
        (fun patch kept ->
            match patch, kept with
            | DomPatch.ReplaceFragment(a, _), DomPatch.ReplaceFragment(b, _) :: _ when a = b -> kept
            | _ -> patch :: kept)
        patches
        []

/// `lowerScript` under an explicit map.
let lowerScriptWith<'Msg>
    (slotOf: NodeKind<'Msg> -> string -> PropSlot option)
    (renderFragment: Node<'Msg> -> string)
    (oldTree: Node<'Msg>)
    (ops: TreeOp<'Msg> list)
    : DomPatch list =
    lowerSteps slotOf renderFragment oldTree ops
    |> fst
    |> List.rev
    |> List.concat
    |> supersede

/// `lowerSteps` from `oldTree` — the lowering of a diff script (see above).
let lowerScript<'Msg>
    (renderFragment: Node<'Msg> -> string)
    (oldTree: Node<'Msg>)
    (ops: TreeOp<'Msg> list)
    : DomPatch list =
    lowerScriptWith PropSlot.slotOf renderFragment oldTree ops
