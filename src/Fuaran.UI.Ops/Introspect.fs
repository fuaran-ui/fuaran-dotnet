module Fuaran.UI.Ops.Introspect

// ============================================================================
//  Fuaran tree-op apply engine — reflection-free shape helpers.
//
//  Each helper hand-dispatches per NodeKind to satisfy the Fable-compat
//  constraint (System.Reflection is server-only). The dispatch tables are
//  load-bearing for two purposes:
//
//   (1) `availableFields` + `availableBindingSlots` populate the §4d
//       `hint.available_fields` payload when UpdateProp / ReplaceBinding
//       fail. An AI consumer's error-recovery quality is bounded by the
//       hint quality; missing entries here translate directly to lower
//       closed-loop convergence.
//
//   (2) `getChildren` + `withChildren` are the single shape over which
//       every structural op (InsertChild / RemoveNode / MoveNode /
//       ReorderChildren) traverses. Adding a new LayoutKind requires
//       a row in both helpers; missing a row turns the new layout into
//       a `ChildlessKind` rather than failing the build, so keep them
//       in sync.
//
//  v1 dispatch coverage: every NodeKind ships a `kindName`. `availableFields`
//  covers Layouts + every Display kind + ButtonSpec / SelectSpec / FormSpec /
//  FileUploadSpec / VisKind shapes. Filters (a bare `FilterSpec list`) and
//  Custom (an open prop bag) return an empty available-fields list to keep
//  the AI from speculatively addressing fields the engine can't apply.
// ============================================================================

open System.Collections.Generic
open Fuaran.UI.Types

// ─── Kind name ─────────────────────────────────────────────────────────────
//
// The kind-tag vocabulary now lives in the base package as `Kind.name` (over
// `NodeKind`, alongside its definition) so every tier — including the renderer's
// fragment applier and the base fragment surface — can name a kind without a
// dependency on this Ops package. This is a thin re-export preserving the
// historical `Introspect.kindName` name + behaviour byte-for-byte (e.g.
// `VisKind.DataGrid → "Grid"`); callers already on `Introspect.kindName` need no
// change. The wire discriminator carries the kind name directly
// (`"kind":{"$type":"Heading",…}`), so this surface and the wire vocabulary stay
// identical.

let kindName (kind: NodeKind<'Msg>) : string = Kind.name kind

// ─── Available top-level fields per kind (UpdateProp surface) ──────────────

let availableFields (kind: NodeKind<'Msg>) : string list =
    match kind with
    | NodeKind.Layout layout ->
        match layout with
        | LayoutKind.Box spec ->
            // Field surface is layout-mode-dependent, preserving the retired
            // kinds' updatable fields: Flex → Orientation/Wrap (Stack), Grid →
            // Cols/TemplateColumns (GridLayout), plus Heading (Card) always.
            let layoutFields =
                match spec.Layout with
                | BoxLayout.Flex _ -> [ "Orientation"; "Wrap" ]
                | BoxLayout.Grid _ -> [ "Cols"; "TemplateColumns" ]
                | BoxLayout.Auto -> []

            layoutFields @ [ "Heading"; "Children" ]
        | LayoutKind.SplitPanel _ -> [ "Weight"; "Children" ]
        | LayoutKind.Tabs _ -> [ "Orientation"; "Children" ]
        | LayoutKind.Stepper _ -> [ "ActiveStep"; "Children" ]
        | LayoutKind.SummaryList _ -> [ "Heading"; "Children" ]
        | LayoutKind.Disclosure _ -> [ "Heading"; "Open"; "DefaultOpen"; "Children" ]
        // Phase 289 — Modal / ScrollArea field-level UpdateProp not wired
        // (Apply returns NotSupportedYet); empty surface keeps the UpdateProp
        // dispatcher from accepting names. Children are mutated via structural
        // ops (getChildren / withChildren below).
        | LayoutKind.Modal _ -> []
        | LayoutKind.ScrollArea _ -> []
    | NodeKind.Display display ->
        match display with
        | DisplayKind.Heading _ -> [ "Level"; "Text"; "Variant" ]
        | DisplayKind.Markdown _ -> [ "Text" ]
        | DisplayKind.Metric _ ->
            [ "Label"
              "Value"
              "Format"
              "Tone"
              "Weight"
              "Emphasis"
              "Trend"
              "TrendFormat"
              "Icon"
              "Subtext" ]
        | DisplayKind.Badge _ -> [ "Label"; "Variant" ]
        | DisplayKind.Sparkline _ -> [ "Source" ]
        | DisplayKind.Callout _ -> [ "Tone"; "Heading"; "Body"; "Icon"; "Dismissable" ]
        | DisplayKind.Progress _ -> [ "Fraction"; "Label"; "Caveat"; "Indeterminate"; "Tone" ]
        | DisplayKind.Skeleton _ -> [ "Rows" ]
        | DisplayKind.LabelValueRow _ -> [ "Label"; "Value"; "Format"; "Emphasis"; "Help" ]
        | DisplayKind.Fact _ -> [ "Label"; "Value"; "Icon"; "Tone"; "Emphasis"; "Help" ]
        | DisplayKind.Link _ -> [ "Href"; "Label"; "Rel"; "Target"; "Download" ]
        // Phase 287/289 — field-level UpdateProp not wired (Apply returns
        // NotSupportedYet); whole-node swap via EditNode remains available.
        | DisplayKind.Image _ -> []
        | DisplayKind.List _ -> []
        | DisplayKind.Toast _ -> []
        // Phase 290/293 — CodeBlock / Math UpdateProp not wired.
        | DisplayKind.CodeBlock _ -> []
        | DisplayKind.Math _ -> []
        // Phase 524 — Drawing field-level UpdateProp not wired (geometry is a
        // whole-artefact swap via EditNode); empty surface keeps the dispatcher
        // from accepting names.
        | DisplayKind.Drawing _ -> []
    | NodeKind.Input input ->
        match input with
        | InputKind.Form _ -> [ "Fields"; "OnSubmit"; "SubmitLabel" ]
        // InputKind.Filters carries a bare list, not a record; no
        // top-level field surface for v1 UpdateProp.
        | InputKind.Filters _ -> []
        // `Tooltip` joined the list when the Input family gained field-level
        // UpdateProp: it is addressable, so advertising it is now accurate
        // rather than aspirational.
        | InputKind.Button _ -> [ "Label"; "OnClick"; "Variant"; "Icon"; "Tooltip" ]
        | InputKind.FileUpload _ -> [ "Label"; "Accept"; "Multiple"; "OnSelect" ]
        | InputKind.Select _ -> [ "Label"; "Source"; "Value"; "OnChange"; "Placeholder" ]
    | NodeKind.Visualisation vis ->
        match vis with
        | VisKind.DataGrid _ -> [ "Source"; "RowKey"; "Columns"; "OnRowClick"; "Editable"; "StaticRows" ]
        | VisKind.Chart _ -> [ "Source"; "Kind"; "XField"; "YFields"; "Title"; "OnPointClick" ]
        | VisKind.Map _ -> [ "Source"; "CentreLatitude"; "CentreLongitude"; "Zoom"; "OnMarkerClick" ]
    // Custom is an open prop bag; the AI should swap it wholesale via
    // EditNode rather than edit individual props through this engine.
    | NodeKind.Custom(_, _, _, _, _) -> []
    // ErrorBoundary carries `Child` + `Fallback`
    // as Node subtrees; the AI should swap them via EditNode rather
    // than edit individual fields through this engine. Keep the surface
    // empty so the UpdateProp dispatcher won't accept these names.
    | NodeKind.ErrorBoundary _ -> []
    // Switch (Phase 392) carries `StateKey` (string) + `Cases` / `Default`
    // (Node subtrees). Field-level UpdateProp is not wired (Apply returns
    // NotSupportedYet); swap the whole switch via EditNode, and edit the case /
    // default children via structural ops. Empty surface keeps the UpdateProp
    // dispatcher from accepting names.
    | NodeKind.Switch _ -> []
    // FragmentDecl carries `Name` + `Body`.
    // `Name` is the only structurally swappable field (UpdateProp
    // path "Name" rebrands the decl); `Body` is a Node subtree the
    // AI should swap via EditNode through `Apply.applyInsideFragment`
    // — explicit hand-edits of an interior leaf land at the namespaced
    // node id (`<refId>.<innerId>`) and the apply engine routes them
    // back to the decl's Body. Listing only "Name" keeps the
    // UpdateProp dispatcher from misleading the AI.
    | NodeKind.FragmentDecl _ -> [ "Name" ]
    // FragmentRef carries only `Name` — swap the referenced
    // fragment id with `UpdateProp(refId, "Name", new-name)`. This is
    // the "swap a fragment reference" case.
    | NodeKind.FragmentRef _ -> [ "Name" ]
    // Mount (§4o) is an opaque isolation boundary — the guest interior is a
    // scope reference, not editable via field-level UpdateProp. Swap a mount
    // wholesale via EditNode; empty surface keeps the UpdateProp dispatcher
    // from accepting names.
    | NodeKind.Mount _ -> []

// ─── Available Binding-typed slots per kind (ReplaceBinding surface) ────────

let availableBindingSlots (kind: NodeKind<'Msg>) : string list =
    match kind with
    | NodeKind.Display(DisplayKind.Metric _) -> [ "Value"; "Trend" ]
    | NodeKind.Display(DisplayKind.Sparkline _) -> [ "Source" ]
    | NodeKind.Display(DisplayKind.Progress _) -> [ "Fraction" ]
    | NodeKind.Display(DisplayKind.LabelValueRow _) -> [ "Value" ]
    | NodeKind.Layout(LayoutKind.Stepper _) -> [ "ActiveStep" ]
    // Tabs' active-tab state: the integer `ActiveIndex` (canonical wire shape,
    // mirrors Stepper.ActiveStep) plus the optional typed-tag overlay
    // `ActiveTag` (mirrors Metric.Trend — always listed; resolves to a
    // synthetic-None when the option is absent).
    | NodeKind.Layout(LayoutKind.Tabs _) -> [ "ActiveIndex"; "ActiveTag" ]
    // Disclosure's controlled open-state binding. Note `Open` is *also* an
    // UpdateProp field (`availableFields` above) — it is dual-surface, so
    // both representations must stay in sync (Phase 118 consistency test).
    | NodeKind.Layout(LayoutKind.Disclosure _) -> [ "Open" ]
    // Button's optional bound disabled-state. `Disabled` is always listed
    // (mirrors Metric.Trend / Tabs.ActiveTag) — it resolves to a
    // synthetic-None when the option is absent, and ReplaceBinding installs
    // `Some`.
    | NodeKind.Input(InputKind.Button _) -> [ "Disabled" ]
    // Select's bound source / value plus the Phase 130 optional bound
    // disabled-state (always listed; synthetic-None when absent, mirroring
    // Button.Disabled / Metric.Trend).
    | NodeKind.Input(InputKind.Select _) -> [ "Source"; "Value"; "Disabled" ]
    // Form / FileUpload gain a single optional bound disabled-state slot
    // (Phase 130 — the interactive-state class-fix). Form had no binding slot
    // before; FileUpload neither. Both are always listed.
    | NodeKind.Input(InputKind.Form _) -> [ "Disabled" ]
    | NodeKind.Input(InputKind.FileUpload _) -> [ "Disabled" ]
    | NodeKind.Visualisation(VisKind.DataGrid _) -> [ "Source" ]
    | NodeKind.Visualisation(VisKind.Chart _) -> [ "Source" ]
    | NodeKind.Visualisation(VisKind.Map _) -> [ "Source" ]
    | _ -> []

// ─── Available nested paths per kind (Phase 364 — UpdateProp nested surface) ─
//
// The per-kind nested-addressing patterns `Apply.dispatchNestedUpdate` can
// traverse (WIRE_FORMAT.md §3.4 "UpdateProp.path grammar"). Populates the §4d
// hint payload for the nested-path failure classes (PathInvalid /
// PositionOutOfRange / PathNotSupportedYet), the same way `availableFields`
// feeds the top-level ones. A kind absent here has no nested surface; adding a
// nested leg in Apply.fs adds its pattern here in the same change.

let availableNestedPaths (kind: NodeKind<'Msg>) : string list =
    match kind with
    | NodeKind.Visualisation(VisKind.DataGrid _) -> [ "Columns[i].Label"; "Columns[i].Format"; "Columns[i].Width" ]
    | NodeKind.Visualisation(VisKind.Chart _) -> [ "YFields[i]" ]
    | NodeKind.Layout(LayoutKind.Tabs _) -> [ "TabHeaders[i].Label"; "TabHeaders[i].Icon"; "TabHeaders[i].Disabled" ]
    | NodeKind.Input(InputKind.Form _) -> [ "Fields[i].Label"; "Fields[i].Required"; "Fields[i].Help" ]
    | _ -> []

// ─── Interactive runtime-state slots (Phase 130) ───────────────────────────
//
// The canonical registry of renderer-honoured *interactive runtime states*
// (disabled / busy / readonly / …) each interactive `NodeKind` exposes as a
// bindable slot. This is the "no renderer-only interactive state" contract:
// every entry here MUST also appear in `availableBindingSlots` (and therefore
// in the `Apply.dispatchReplaceBinding` ReplaceBinding dispatch + the
// `AiTools.Tools.extractSlot` resolver). The Phase 118 cross-table consistency
// property test, extended in Phase 130, asserts that inclusion and fails the
// build if a future interactive kind gains a renderer-honoured state without
// the matching bindable slot in all tables.
//
// v1 coverage: `Disabled` on Button (Phase 129) + Select / Form / FileUpload
// (Phase 130). Tabs honours per-tab disabled via `TabHeader.Disabled` — a
// sub-element field rather than a node-level binding slot — so it is not
// listed here. `Filters` is a bare `FilterSpec list` with no node-level spec,
// so it exposes no node-level interactive-state slot (same rationale as its
// empty `availableBindingSlots`).
let interactiveStateSlots (kind: NodeKind<'Msg>) : string list =
    match kind with
    | NodeKind.Input(InputKind.Button _) -> [ "Disabled" ]
    | NodeKind.Input(InputKind.Select _) -> [ "Disabled" ]
    | NodeKind.Input(InputKind.Form _) -> [ "Disabled" ]
    | NodeKind.Input(InputKind.FileUpload _) -> [ "Disabled" ]
    | _ -> []

// ─── Children getter / setter ─────────────────────────────────────────────

let getChildren (kind: NodeKind<'Msg>) : Node<'Msg> list option =
    match kind with
    | NodeKind.Layout layout ->
        match layout with
        | LayoutKind.Box spec -> Some spec.Children
        | LayoutKind.SplitPanel spec -> Some spec.Children
        | LayoutKind.Tabs spec -> Some spec.Children
        | LayoutKind.Stepper spec -> Some spec.Children
        | LayoutKind.SummaryList spec -> Some spec.Children
        | LayoutKind.Disclosure spec -> Some spec.Children
        | LayoutKind.Modal spec -> Some spec.Children
        | LayoutKind.ScrollArea spec -> Some spec.Children
    // FragmentDecl exposes its `Body` as a
    // single-element children list so the standard tree walkers
    // (`findNode` / `mapNode` / `allNodeIds` / `nodesWithField`) traverse
    // into it. This means interior fragment-body nodes are addressable
    // by their bare NodeId through the normal `Apply.apply` dispatch —
    // `EditNode("btn", ...)` / `UpdateProp("btn", ...)` reach into a
    // referenced fragment's body the same way they reach a Layout's
    // child. Structural ops (InsertChild / RemoveNode) against the decl
    // itself surface `ChildlessKind` via `withChildren`'s length-≠-1
    // guard below.
    | NodeKind.FragmentDecl spec -> Some [ spec.Body ]
    // FragmentRef is a pure leaf — the renderer expands it at render
    // time via a separate resolver walk; the apply engine treats it as
    // opaque. Address a referenced body by its bare interior NodeId
    // (which mapNode reaches via the decl, above), not via the ref.
    | _ -> None

let withChildren (kind: NodeKind<'Msg>) (children: Node<'Msg> list) : NodeKind<'Msg> option =
    match kind with
    | NodeKind.Layout layout ->
        match layout with
        | LayoutKind.Box spec -> Some(NodeKind.Layout(LayoutKind.Box { spec with Children = children }))
        | LayoutKind.SplitPanel spec -> Some(NodeKind.Layout(LayoutKind.SplitPanel { spec with Children = children }))
        | LayoutKind.Tabs spec -> Some(NodeKind.Layout(LayoutKind.Tabs { spec with Children = children }))
        | LayoutKind.Stepper spec -> Some(NodeKind.Layout(LayoutKind.Stepper { spec with Children = children }))
        | LayoutKind.SummaryList spec -> Some(NodeKind.Layout(LayoutKind.SummaryList { spec with Children = children }))
        | LayoutKind.Disclosure spec -> Some(NodeKind.Layout(LayoutKind.Disclosure { spec with Children = children }))
        | LayoutKind.Modal spec -> Some(NodeKind.Layout(LayoutKind.Modal { spec with Children = children }))
        | LayoutKind.ScrollArea spec -> Some(NodeKind.Layout(LayoutKind.ScrollArea { spec with Children = children }))
    // Re-pack the single-element children list back into the
    // decl's `Body`. Pass-through of a length-1 list happens during the
    // normal `mapNode` traversal (one child was mapped, one comes back).
    // A length-≠-1 list means the caller is trying to InsertChild /
    // RemoveNode against the decl itself — fragments don't structurally
    // accept children, only a body; returning `None` surfaces
    // `ChildlessKind` through the apply engine's standard path.
    | NodeKind.FragmentDecl spec ->
        match children with
        | [ single ] -> Some(NodeKind.FragmentDecl { spec with Body = single })
        | _ -> None
    | _ -> None

// ─── Tree traversal ────────────────────────────────────────────────────────

let rec findNode (target: NodeId) (node: Node<'Msg>) : Node<'Msg> option =
    if node.Id = target then
        Some node
    else
        match getChildren node.Kind with
        | None -> None
        | Some children -> children |> List.tryPick (findNode target)

/// Returns `Some (parent, indexOfTarget)` if `target` is a child of some node
/// reachable from `root`. Returns `None` for the root itself, or for a target
/// not present in the tree.
let findParent (target: NodeId) (root: Node<'Msg>) : (Node<'Msg> * int) option =
    let rec walk (node: Node<'Msg>) =
        match getChildren node.Kind with
        | None -> None
        | Some children ->
            match children |> List.tryFindIndex (fun c -> c.Id = target) with
            | Some idx -> Some(node, idx)
            | None -> children |> List.tryPick walk

    walk root

let rec allNodeIds (node: Node<'Msg>) : NodeId list =
    let childIds =
        match getChildren node.Kind with
        | None -> []
        | Some children -> children |> List.collect allNodeIds

    node.Id :: childIds

/// DFS-add every NodeId in `node`'s subtree to `acc`. No intermediate list —
/// the membership-probe path (firstSharedId) wants a HashSet, not a list it
/// would immediately fold into a Set.
let rec collectNodeIdsInto (acc: HashSet<NodeId>) (node: Node<'Msg>) : unit =
    acc.Add node.Id |> ignore

    match getChildren node.Kind with
    | None -> ()
    | Some children ->
        for c in children do
            collectNodeIdsInto acc c

/// The first NodeId (DFS pre-order over `incoming`) that already exists in
/// `root`, or None when the two subtrees share no id. This is the named
/// duplicate-id check the structural ops use before grafting a subtree: it is
/// O(|root| + |incoming|) via HashSet membership, replacing the per-op
/// `allNodeIds root |> Set.ofList` build-then-probe (O(n log n) + a balanced
/// tree allocation on every InsertChild / MoveNode). Reach for THIS rather than
/// re-deriving `allNodeIds |> Set.ofList` — the slow shape is what it replaces.
let firstSharedId (root: Node<'Msg>) (incoming: Node<'Msg>) : NodeId option =
    let existing = HashSet<NodeId>()
    collectNodeIdsInto existing root

    let rec findIn (node: Node<'Msg>) : NodeId option =
        if existing.Contains node.Id then
            Some node.Id
        else
            match getChildren node.Kind with
            | None -> None
            | Some children -> children |> List.tryPick findIn

    findIn incoming

/// DFS collect every NodeId whose kind reports `field` in its
/// `availableFields` list. Used to populate the §4d
/// `hint.nodes_with_<field>_field` payload so the AI can pivot to a
/// different addressable node.
let nodesWithField (field: string) (root: Node<'Msg>) : NodeId list =
    let rec walk (node: Node<'Msg>) =
        let here =
            if availableFields node.Kind |> List.contains field then
                [ node.Id ]
            else
                []

        let below =
            match getChildren node.Kind with
            | None -> []
            | Some children -> children |> List.collect walk

        here @ below

    walk root

/// DFS map over the tree, replacing the node with `target` id by applying
/// `replace` to it. Returns the new root, or `None` if `target` was not
/// found anywhere.
///
/// The function is the workhorse for ops that mutate a single node in
/// place (EditNode / UpdateProp / ReplaceBinding / UpdateStyle /
/// UpdateState).
let rec mapNode (target: NodeId) (replace: Node<'Msg> -> Node<'Msg>) (node: Node<'Msg>) : Node<'Msg> option =
    if node.Id = target then
        Some(replace node)
    else
        match getChildren node.Kind with
        | None -> None
        | Some children ->
            let mutable found = false

            let newChildren =
                children
                |> List.map (fun child ->
                    if found then
                        child
                    else
                        match mapNode target replace child with
                        | Some replaced ->
                            found <- true
                            replaced
                        | None -> child)

            if found then
                match withChildren node.Kind newChildren with
                | Some newKind -> Some { node with Kind = newKind }
                | None -> None
            else
                None

/// DFS map over the parent of `target`, applying `replaceChildren` to that
/// parent's children list. Returns the new root, or `None` if `target` was
/// not found (or is the root itself, which has no parent).
let mapParentOf
    (target: NodeId)
    (replaceChildren: Node<'Msg> list -> Node<'Msg> list)
    (root: Node<'Msg>)
    : Node<'Msg> option =
    let rec walk (node: Node<'Msg>) =
        match getChildren node.Kind with
        | None -> None
        | Some children ->
            if children |> List.exists (fun c -> c.Id = target) then
                match withChildren node.Kind (replaceChildren children) with
                | Some newKind -> Some { node with Kind = newKind }
                | None -> None
            else
                let mutable found = false

                let newChildren =
                    children
                    |> List.map (fun child ->
                        if found then
                            child
                        else
                            match walk child with
                            | Some replaced ->
                                found <- true
                                replaced
                            | None -> child)

                if found then
                    match withChildren node.Kind newChildren with
                    | Some newKind -> Some { node with Kind = newKind }
                    | None -> None
                else
                    None

    walk root

/// True when `ancestorId` reaches `descendantId` via getChildren traversal.
/// Used to prevent MoveNode cycles.
let isAncestorOf (ancestorId: NodeId) (descendantId: NodeId) (root: Node<'Msg>) : bool =
    match findNode ancestorId root with
    | None -> false
    | Some ancestor ->
        if ancestor.Id = descendantId then
            true
        else
            allNodeIds ancestor |> List.contains descendantId
