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
// `NodeKind.DataGrid → "Grid"`); callers already on `Introspect.kindName` need no
// change.
//
// THIS IS THE DISPLAY / KIND-CONSTRAINT TAG, NOT THE WIRE DISCRIMINATOR — and the
// example two lines up is the whole of the difference. The wire carries the kind
// name directly (`"kind":{"$type":"Heading",…}`) and the two vocabularies coincide
// for every kind BUT ONE: `NodeKind.DataGrid` is `"DataGrid"` on the wire (and so
// in `SchemaGen`, the published JSON Schema, and every conformant host) while this
// surface says `"Grid"`.
//
// So do NOT key a wire- or schema-facing lookup off `kindName`: it resolves for 38
// kinds and misses on `DataGrid` alone, silently. Key it off the encoded node's
// `kind.$type` — or off `Fuaran.UI.Renderer.Relay.wireKindName`, the adapted
// projection the relay boundary already ships for exactly this reason. See
// `Kind.name` in `Fuaran.UI.Types` for the vocabulary's own contract.

let kindName (kind: NodeKind<'Msg>) : string = Kind.name kind

// ─── Available top-level fields per kind (UpdateProp surface) ──────────────
//
// THE CONTRACT, stated because it was drifted from once and the drift was
// expensive: this list is the **UpdateProp surface**, not an inventory of a
// kind's fields. A name appears here only if it is reachable, by one of exactly
// three routes, and `HintHonestyTests` enforces that:
//
//   1. UpdateProp sets it directly (the common case).
//   2. It is a `Binding` slot, so `ReplaceBinding` sets it — it must then also
//      appear in `availableBindingSlots` (`Select.Source`, `Sparkline.Source`).
//   3. It is reached by the nested surface or the structural ops — it must then
//      be a prefix of an `availableNestedPaths` entry (`Columns` ⇒
//      `Columns[i].Label`), or the literal `Children`, whose route is
//      InsertChild / RemoveNode / MoveNode / ReorderChildren.
//
// `Action` fields (`OnClick`, `OnSubmit`, `OnChange`, `OnSelect`, `OnRowClick`,
// …) are on NONE of those routes: a closure is not expressible as a wire value,
// so no op sets one and `EditNode` replaces the whole kind instead. They were
// listed here and are not any more. Advertising an unreachable field is worse
// than omitting it, because the author believes the hint, tries the path, and
// gets a refusal that reads as a bug in their own emission — which is exactly
// what two independent model families did against `Button.Label`. If actions
// ever need describing, they want their own enumeration beside
// `availableBindingSlots`, not a slot on this one.

let availableFields (kind: NodeKind<'Msg>) : string list =
    match kind with
    // -- Layout --
    | NodeKind.Box spec ->
        // Field surface is layout-mode-dependent, preserving the retired
        // kinds' updatable fields: Flex → Orientation/Wrap (Stack), Grid →
        // Cols/TemplateColumns (GridLayout), plus Heading (Card) always.
        let layoutFields =
            match spec.Layout with
            | BoxLayout.Flex _ -> [ "Orientation"; "Wrap" ]
            | BoxLayout.Grid _ -> [ "Cols"; "TemplateColumns" ]
            // `Masonry` advertises `Cols` and NOT `TemplateColumns`: it carries
            // no template slot, so advertising one would name a field an
            // `UpdateProp` could never set.
            | BoxLayout.Masonry _ -> [ "Cols" ]
            | BoxLayout.Auto -> []

        // Phase 1473 — the print-break declarations are advertised on every
        // layout mode, unlike the mode-dependent pair above: they are properties
        // of the CONTAINER, not of how it arranges its children, so an `Auto` box
        // carries them exactly as a `Flex` one does.
        layoutFields @ [ "Heading"; "KeepTogether"; "BreakBefore"; "Children" ]
    | NodeKind.SplitPanel _ -> [ "Weight"; "Children" ]
    | NodeKind.Tabs _ -> [ "Orientation"; "Children" ]
    | NodeKind.Stepper _ -> [ "ActiveStep"; "Children" ]
    | NodeKind.SummaryList _ -> [ "Heading"; "Children" ]
    | NodeKind.Disclosure _ -> [ "Heading"; "Open"; "DefaultOpen"; "Children" ]
    // `Children` is advertised but not UpdateProp-settable anywhere: it is
    // the structural ops' surface (getChildren / withChildren below), and
    // the NotSupportedYet hint names them. That is a signpost, not a lie.
    | NodeKind.Modal _ -> [ "Heading"; "Dismissable"; "Modality"; "Anchor"; "Children" ]
    | NodeKind.ScrollArea _ -> [ "Orientation"; "MaxHeight"; "MaxWidth"; "Children" ]
    // -- Display --
    | NodeKind.Heading _ -> [ "Level"; "Text"; "Variant" ]
    | NodeKind.Markdown _ -> [ "Text" ]
    | NodeKind.Metric _ ->
        [ "Label"
          "Value"
          "Format"
          "Tone"
          "Weight"
          "Emphasis"
          "Trend"
          "TrendFormat"
          "TrendPolarity"
          "Icon"
          "Subtext" ]
    | NodeKind.Badge _ -> [ "Label"; "Variant" ]
    | NodeKind.Sparkline _ -> [ "Source" ]
    | NodeKind.Callout _ -> [ "Tone"; "Heading"; "Body"; "Icon"; "Dismissable" ]
    | NodeKind.Progress _ -> [ "Fraction"; "Label"; "Caveat"; "Indeterminate"; "Tone" ]
    | NodeKind.Skeleton _ -> [ "Rows" ]
    | NodeKind.Icon _ -> [ "Icon"; "Size"; "Tone"; "Label" ]
    | NodeKind.LabelValueRow _ -> [ "Label"; "Value"; "Format"; "Emphasis"; "Help" ]
    | NodeKind.Fact _ -> [ "Label"; "Value"; "Icon"; "Tone"; "Emphasis"; "Help" ]
    | NodeKind.Link _ -> [ "Href"; "Label"; "Rel"; "Target"; "Download" ]
    | NodeKind.Image _ -> [ "Alt"; "Variant" ]
    // Phase 1076 — the three coercible scalars. `Src` (a Binding) and `Kind`
    // (a payload-bearing union) are `NotSupportedYet` in `updateMedia`, so
    // advertising them here would promise a field-level edit that answers
    // "not yet" — the same restraint `Image` shows by omitting `Src`.
    | NodeKind.Media _ -> [ "Label"; "Controls"; "Loop" ]
    // Phase 1111 — one field. `Src`, `AspectRatio` and `Permissions` are all
    // `NotSupportedYet` in `updateEmbed`, so advertising them would promise an
    // edit that answers "not yet"; `Permissions` would additionally advertise a
    // field-level route into a sandbox relaxation, which is not a route this
    // surface should offer at all.
    | NodeKind.Embed _ -> [ "Title" ]
    // Phase 1120 — the EMPTY list, and it is the honest answer rather than an
    // omission. Every slot on `TreeSpec` is `NotSupportedYet` in `updateTree`,
    // so advertising any of them would promise a field-level edit that answers
    // "not yet"; the restraint `Image` shows by omitting `Src`, applied to a
    // spec on which it happens to reach every field.
    | NodeKind.Tree _ -> []
    | NodeKind.List _ -> [ "Items"; "Ordered" ]
    | NodeKind.Toast _ -> [ "Message"; "Tone"; "Dismissable" ]
    | NodeKind.CodeBlock _ -> [ "Code"; "Language"; "LineNumbers"; "HighlightLines"; "Copyable" ]
    | NodeKind.Math _ -> [ "Source"; "Display" ]
    // Drawing stays a whole-artefact swap via EditNode: `Shapes` is a
    // geometry list with no per-item identity, so there is nothing here a
    // field-level path could address today. Empty, therefore honest.
    | NodeKind.Drawing _ -> []
    // -- Input --
    | NodeKind.Form _ -> [ "Fields"; "SubmitLabel" ]
    // NodeKind.Filters carries a bare list, not a record; no
    // top-level field surface for v1 UpdateProp.
    | NodeKind.Filters _ -> []
    // `Tooltip` joined the list when the Input family gained field-level
    // UpdateProp: it is addressable, so advertising it is now accurate
    // rather than aspirational.
    | NodeKind.Button _ -> [ "Label"; "Variant"; "Icon"; "Tooltip" ]
    | NodeKind.FileUpload _ -> [ "Label"; "Accept"; "Multiple" ]
    | NodeKind.Select _ -> [ "Label"; "Source"; "Value"; "Placeholder" ]
    // -- Visualisation --
    // Phase 1473 — the grid's two print-break declarations are plain wire
    // booleans with a coercion, so an `UpdateProp` sets them exactly as it sets
    // `Editable`. Advertised because they ARE settable: a name here that
    // `Apply` answered `UnknownField` to would be an introspection surface
    // naming a field it cannot set.
    | NodeKind.DataGrid _ ->
        [ "Source"
          "Columns"
          "Editable"
          "RowKeyField"
          "KeepRowsTogether"
          "RepeatHeader"
          // Phase 1125 — advertised on the same reasoning: `Apply` sets it, so
          // withholding the name would leave an introspection surface hiding a
          // field it can in fact change.
          "Exportable" ]
    | NodeKind.Chart _ -> [ "Source"; "Kind"; "XField"; "YFields"; "Title"; "Stacked" ]
    | NodeKind.Map _ -> [ "Source"; "CentreLatitude"; "CentreLongitude"; "Zoom" ]
    | NodeKind.Custom _ -> []
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
    | NodeKind.Metric(_) -> [ "Value"; "Trend" ]
    | NodeKind.Sparkline(_) -> [ "Source" ]
    | NodeKind.Progress(_) -> [ "Fraction" ]
    | NodeKind.LabelValueRow(_) -> [ "Value" ]
    | NodeKind.Stepper(_) -> [ "ActiveStep" ]
    // Tabs' active-tab state: the integer `ActiveIndex` (canonical wire shape,
    // mirrors Stepper.ActiveStep) plus the optional typed-tag overlay
    // `ActiveTag` (mirrors Metric.Trend — always listed; resolves to a
    // synthetic-None when the option is absent).
    | NodeKind.Tabs(_) -> [ "ActiveIndex"; "ActiveTag" ]
    // Disclosure's controlled open-state binding. Note `Open` is *also* an
    // UpdateProp field (`availableFields` above) — it is dual-surface, so
    // both representations must stay in sync (Phase 118 consistency test).
    | NodeKind.Disclosure(_) -> [ "Open" ]
    // Button's optional bound disabled-state. `Disabled` is always listed
    // (mirrors Metric.Trend / Tabs.ActiveTag) — it resolves to a
    // synthetic-None when the option is absent, and ReplaceBinding installs
    // `Some`.
    | NodeKind.Button(_) -> [ "Disabled" ]
    // Select's bound source / value plus the Phase 130 optional bound
    // disabled-state (always listed; synthetic-None when absent, mirroring
    // Button.Disabled / Metric.Trend).
    | NodeKind.Select(_) -> [ "Source"; "Value"; "Disabled" ]
    // Form / FileUpload gain a single optional bound disabled-state slot
    // (Phase 130 — the interactive-state class-fix). Form had no binding slot
    // before; FileUpload neither. Both are always listed.
    | NodeKind.Form(_) -> [ "Disabled" ]
    | NodeKind.FileUpload(_) -> [ "Disabled" ]
    | NodeKind.DataGrid(_) -> [ "Source" ]
    | NodeKind.Chart(_) -> [ "Source" ]
    | NodeKind.Map(_) -> [ "Source" ]
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
    | NodeKind.DataGrid(_) -> [ "Columns[i].Label"; "Columns[i].Format"; "Columns[i].Width" ]
    | NodeKind.Chart(_) -> [ "YFields[i]" ]
    | NodeKind.Tabs(_) -> [ "TabHeaders[i].Label"; "TabHeaders[i].Icon"; "TabHeaders[i].Disabled" ]
    | NodeKind.Form(_) -> [ "Fields[i].Label"; "Fields[i].Required"; "Fields[i].Help" ]
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
    | NodeKind.Button(_) -> [ "Disabled" ]
    | NodeKind.Select(_) -> [ "Disabled" ]
    | NodeKind.Form(_) -> [ "Disabled" ]
    | NodeKind.FileUpload(_) -> [ "Disabled" ]
    | _ -> []

// ─── Children getter / setter ─────────────────────────────────────────────

let getChildren (kind: NodeKind<'Msg>) : Node<'Msg> list option =
    match kind with
    // -- Layout --
    | NodeKind.Box spec -> Some spec.Children
    | NodeKind.SplitPanel spec -> Some spec.Children
    | NodeKind.Tabs spec -> Some spec.Children
    | NodeKind.Stepper spec -> Some spec.Children
    | NodeKind.SummaryList spec -> Some spec.Children
    | NodeKind.Disclosure spec -> Some spec.Children
    | NodeKind.Modal spec -> Some spec.Children
    | NodeKind.ScrollArea spec -> Some spec.Children
    | NodeKind.FragmentDecl spec -> Some [ spec.Body ]
    // FragmentRef is a pure leaf — the renderer expands it at render
    // time via a separate resolver walk; the apply engine treats it as
    // opaque. Address a referenced body by its bare interior NodeId
    // (which mapNode reaches via the decl, above), not via the ref.
    | _ -> None

let withChildren (kind: NodeKind<'Msg>) (children: Node<'Msg> list) : NodeKind<'Msg> option =
    match kind with
    // -- Layout --
    | NodeKind.Box spec -> Some(NodeKind.Box({ spec with Children = children }))
    | NodeKind.SplitPanel spec -> Some(NodeKind.SplitPanel({ spec with Children = children }))
    | NodeKind.Tabs spec -> Some(NodeKind.Tabs({ spec with Children = children }))
    | NodeKind.Stepper spec -> Some(NodeKind.Stepper({ spec with Children = children }))
    | NodeKind.SummaryList spec -> Some(NodeKind.SummaryList({ spec with Children = children }))
    | NodeKind.Disclosure spec -> Some(NodeKind.Disclosure({ spec with Children = children }))
    | NodeKind.Modal spec -> Some(NodeKind.Modal({ spec with Children = children }))
    | NodeKind.ScrollArea spec -> Some(NodeKind.ScrollArea({ spec with Children = children }))
    | NodeKind.FragmentDecl spec ->
        match children with
        | [ single ] -> Some(NodeKind.FragmentDecl { spec with Body = single })
        | _ -> None
    | _ -> None

// ─── Tree traversal ────────────────────────────────────────────────────────

// ─── Non-structural node positions ─────────────────────────────────────────
//
// `getChildren` answers ONE question — which kinds accept the structural ops —
// and several kinds hold real child nodes in shapes that are not a plain
// ordered list, so they answer `None` there and are right to. But traversal was
// built on the same function and inherited that answer, which made those nodes
// invisible to `allNodeIds` / `findNode` / `collectNodeIdsInto` — and, because
// `applyStructural` hands Fuaran.Core a `NodeWitness` whose `Children` IS
// `getChildren`, invisible to the duplicate-id rejection too. §4g promises ids
// are unique per tree; before this, an insert colliding with an id inside a
// Switch case was accepted.
//
// Returned as a LENS — the nodes plus a function putting a same-length list
// back in the same positions — so the read and the write cannot drift apart.
// Two parallel matches would be a standing invitation to fix one and not the
// other, which is the shape of the defect this closes.
//
// `StateBehaviour.OnError` is deliberately absent: it is `ErrorPayload -> Node`,
// so there is no node to enumerate until it is applied.

let private nonStructuralSlots (node: Node<'Msg>) : Node<'Msg> list * (Node<'Msg> list -> Node<'Msg>) =
    // `Node.State` is an option since the swap — an absent envelope has no
    // slots to enumerate, and the rebuild writes back through the same option.
    let onLoading = node.State |> Option.bind _.OnLoading
    let onEmpty = node.State |> Option.bind _.OnEmpty

    let stateSlots =
        [ match onLoading with
          | Some n -> n
          | None -> ()
          match onEmpty with
          | Some n -> n
          | None -> () ]

    let hasLoading = onLoading.IsSome
    let hasEmpty = onEmpty.IsSome

    let putState (replacements: Node<'Msg> list) (rest: Node<'Msg> list) =
        let loading, afterLoading =
            if hasLoading then
                Some(List.head replacements), List.tail replacements
            else
                None, replacements

        let empty, _ =
            if hasEmpty then
                Some(List.head afterLoading), List.tail afterLoading
            else
                None, afterLoading

        { node with
            State =
                node.State
                |> Option.map (fun s ->
                    { s with
                        OnLoading = loading
                        OnEmpty = empty }) },
        rest

    // Kind-held nodes, in a fixed order the rebuild mirrors exactly.
    let kindSlots, putKind =
        match node.Kind with
        | NodeKind.Switch spec ->
            let caseNodes = spec.Cases |> List.map _.Child

            caseNodes @ [ spec.Default ],
            fun (rs: Node<'Msg> list) ->
                let cases =
                    List.zip spec.Cases (List.truncate spec.Cases.Length rs)
                    |> List.map (fun (c, replaced) -> { c with Child = replaced })

                NodeKind.Switch
                    { spec with
                        Cases = cases
                        Default = List.last rs }
        | NodeKind.ErrorBoundary spec ->
            [ spec.Child; spec.Fallback ],
            fun (rs: Node<'Msg> list) ->
                NodeKind.ErrorBoundary
                    { spec with
                        Child = rs[0]
                        Fallback = rs[1] }
        | NodeKind.FragmentRef spec ->
            let argMap = spec.Args |> Option.defaultValue Map.empty

            let keyed =
                argMap
                |> Map.toList
                |> List.choose (fun (k, v) ->
                    match v with
                    | FragmentArg.SlotArg n -> Some(k, n)
                    | _ -> None)

            keyed |> List.map snd,
            fun (rs: Node<'Msg> list) ->
                let args =
                    List.zip keyed rs
                    |> List.fold (fun acc ((k, _), replaced) -> Map.add k (FragmentArg.SlotArg replaced) acc) argMap

                // An absent/empty arg bag stays `None` (omitted on the wire).
                NodeKind.FragmentRef
                    { spec with
                        Args = (if Map.isEmpty args then None else Some args) }
        | NodeKind.Mount spec ->
            let inputMap = spec.Inputs |> Option.defaultValue Map.empty

            let keyed =
                inputMap
                |> Map.toList
                |> List.choose (fun (k, v) ->
                    match v with
                    | FragmentArg.SlotArg n -> Some(k, n)
                    | _ -> None)

            keyed |> List.map snd,
            fun (rs: Node<'Msg> list) ->
                let inputs =
                    List.zip keyed rs
                    |> List.fold (fun acc ((k, _), replaced) -> Map.add k (FragmentArg.SlotArg replaced) acc) inputMap

                // An absent/empty input bag stays `None` (omitted on the wire).
                NodeKind.Mount
                    { spec with
                        Inputs = (if Map.isEmpty inputs then None else Some inputs) }
        | _ -> [], (fun _ -> node.Kind)

    let all = stateSlots @ kindSlots

    let put (replacements: Node<'Msg> list) : Node<'Msg> =
        let stateCount = List.length stateSlots
        let stateReplacements = List.truncate stateCount replacements
        let kindReplacements = List.skip stateCount replacements
        let withState, _ = putState stateReplacements []

        if List.isEmpty kindSlots then
            withState
        else
            { withState with
                Kind = putKind kindReplacements }

    all, put

/// Every node held one step below `node` — structural children AND the
/// non-list positions. The traversal surface, as distinct from the
/// structural-op surface `getChildren` describes.
let descendantNodes (node: Node<'Msg>) : Node<'Msg> list =
    let structural = getChildren node.Kind |> Option.defaultValue []
    let nonStructural, _ = nonStructuralSlots node
    structural @ nonStructural

/// Rebuild `node` with `replacements` in exactly the positions `descendantNodes`
/// enumerates (structural children first, then the non-structural slots). The
/// write companion to `descendantNodes`, making the pair a lens over the whole
/// traversal surface the way `getChildren` / `withChildren` are over the
/// structural one. Positional only: the replacement list must have the same
/// length as `descendantNodes node` — callers rebuild in place (an id-remap, a
/// whole-tree map), never structurally (insert / remove stay with the
/// structural surface, where the apply engine gates them).
let replaceDescendantNodes (node: Node<'Msg>) (replacements: Node<'Msg> list) : Node<'Msg> =
    let structuralCount =
        getChildren node.Kind |> Option.map List.length |> Option.defaultValue 0

    let structural = replacements |> List.truncate structuralCount
    let nonStructural = replacements |> List.skip structuralCount

    let rebuilt =
        match withChildren node.Kind structural with
        | Some kind -> { node with Kind = kind }
        | None -> node

    if List.isEmpty nonStructural then
        rebuilt
    else
        let _, put = nonStructuralSlots rebuilt
        put nonStructural

let rec findNode (target: NodeId) (node: Node<'Msg>) : Node<'Msg> option =
    // The op layer addresses by `NodeId`; `Node.Id` is a bare string since the
    // swap — unwrap at the comparison.
    let (NodeId targetRaw) = target

    if node.Id = targetRaw then
        Some node
    else
        descendantNodes node |> List.tryPick (findNode target)

/// Returns `Some (parent, indexOfTarget)` if `target` is a child of some node
/// reachable from `root`. Returns `None` for the root itself, or for a target
/// not present in the tree.
let findParent (target: NodeId) (root: Node<'Msg>) : (Node<'Msg> * int) option =
    let (NodeId targetRaw) = target

    let rec walk (node: Node<'Msg>) =
        match getChildren node.Kind with
        | None -> None
        | Some children ->
            match children |> List.tryFindIndex (fun c -> c.Id = targetRaw) with
            | Some idx -> Some(node, idx)
            | None -> children |> List.tryPick walk

    walk root

let rec allNodeIds (node: Node<'Msg>) : NodeId list =
    // `descendantNodes`, not `getChildren`: id enumeration must see the WHOLE
    // tree, including nodes held in positions the structural ops cannot edit.
    NodeId node.Id :: (descendantNodes node |> List.collect allNodeIds)

/// DFS-add every NodeId in `node`'s subtree to `acc`. No intermediate list —
/// the membership-probe path (firstSharedId) wants a HashSet, not a list it
/// would immediately fold into a Set.
let rec collectNodeIdsInto (acc: HashSet<NodeId>) (node: Node<'Msg>) : unit =
    acc.Add(NodeId node.Id) |> ignore

    for c in descendantNodes node do
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
        if existing.Contains(NodeId node.Id) then
            Some(NodeId node.Id)
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
                [ NodeId node.Id ]
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
    if NodeId node.Id = target then
        Some(replace node)
    else
        // Structural children first, then the non-list positions. Both are
        // walked so that anything `findNode` can reach, `mapNode` can edit —
        // an addressable-but-uneditable node would be a trap of its own.
        let mutable found = false

        let mapInto (nodes: Node<'Msg> list) =
            nodes
            |> List.map (fun child ->
                if found then
                    child
                else
                    match mapNode target replace child with
                    | Some replaced ->
                        found <- true
                        replaced
                    | None -> child)

        let structuralResult =
            match getChildren node.Kind with
            | None -> None
            | Some children ->
                let newChildren = mapInto children

                if found then withChildren node.Kind newChildren else None

        match structuralResult with
        | Some newKind -> Some { node with Kind = newKind }
        | None when found ->
            // Found in a structural child but the kind refused to rebuild —
            // preserve the original contract (None) rather than silently
            // dropping the edit.
            None
        | None ->
            let slots, put = nonStructuralSlots node

            if List.isEmpty slots then
                None
            else
                let replaced = mapInto slots
                if found then Some(put replaced) else None

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
            if children |> List.exists (fun c -> NodeId c.Id = target) then
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
        if NodeId ancestor.Id = descendantId then
            true
        else
            allNodeIds ancestor |> List.contains descendantId

// ─── §16 canonical-form projection (Phase 694) ─────────────────────────────
//
// The policy half the retired hand-written encoder carried inline: the
// canonical wire spells three things as ABSENCE that the type system can also
// spell explicitly — a control value that is exactly its context's Phase 596
// auto-binding (`State(field id, typed placeholder)` on a form field,
// `Filter(name, None)` on a chip), an all-default `SemanticStyle`, and an
// all-`None` `StateBehaviour`. `canonicalForm` rewrites a tree into that
// canonical shape so the structural (generated) encoder emits canonical bytes;
// the tier's policy encoder (`CanonicalJson.encodeNode`) applies it, and
// `JsonDecode`'s decode-side collapses mirror it. It lives HERE because the
// traversal reuses this module's kind-complete lens — no second per-kind walk.

let rec private canonicalFormField (field: FormField<'Msg>) : FormField<'Msg> =
    // A value slot that spells the field's exact auto-binding collapses to
    // `None` (the canonical omitted form). Any other value passes through.
    let collapse (expected: 'v option) (value: Binding<'v> option) : Binding<'v> option =
        match value with
        | Some(Binding.State(key, dv)) when key = field.Id && dv = expected -> None
        | v -> v

    let kind =
        match field.Kind with
        | FormFieldKind.Text(value, oc) ->
            FormFieldKind.Text(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.text) value, oc)
        | FormFieldKind.Number(value, oc) ->
            FormFieldKind.Number(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.number) value, oc)
        | FormFieldKind.Checkbox(value, ot) ->
            FormFieldKind.Checkbox(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) value, ot)
        | FormFieldKind.Toggle(value, ot) ->
            FormFieldKind.Toggle(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) value, ot)
        | FormFieldKind.Choice(options, value, oc) ->
            FormFieldKind.Choice(options, collapse Fuaran.UI.Defaults.ControlValueDefaults.choice value, oc)
        | FormFieldKind.TextArea(value, oc, rows) ->
            FormFieldKind.TextArea(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.text) value, oc, rows)
        | FormFieldKind.RangedNumber(value, oc, mn, mx, st) ->
            FormFieldKind.RangedNumber(
                collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.number) value,
                oc,
                mn,
                mx,
                st
            )
        | FormFieldKind.Range(value, oc, mn, mx, st) ->
            FormFieldKind.Range(collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.range) value, oc, mn, mx, st)
        | FormFieldKind.SegmentedChoice(options, value, oc, orientation) ->
            FormFieldKind.SegmentedChoice(
                options,
                collapse Fuaran.UI.Defaults.ControlValueDefaults.choice value,
                oc,
                orientation
            )
        | FormFieldKind.Combobox(allowFreeText, oc, options, value) ->
            FormFieldKind.Combobox(
                allowFreeText,
                oc,
                options,
                collapse Fuaran.UI.Defaults.ControlValueDefaults.combobox value
            )
        | FormFieldKind.Date(value, oc, variant, mn, mx, st) ->
            FormFieldKind.Date(
                collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.date) value,
                oc,
                variant,
                mn,
                mx,
                st
            )
        | FormFieldKind.DateRange(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateRange(
                collapse (Some Fuaran.UI.Defaults.ControlValueDefaults.dateRange) value,
                oc,
                variant,
                mn,
                mx,
                st
            )

    { field with Kind = kind }

and private canonicalFilterItem (item: FilterSpec<'Msg>) : FilterSpec<'Msg> =
    // A chip value that spells the chip's own auto-binding collapses to `None`.
    let collapse (value: Binding<'v> option) : Binding<'v> option =
        match value with
        | Some(Binding.Filter(name, None)) when name = item.Name -> None
        | v -> v

    let kind =
        match item.Kind with
        | FormFieldKind.Text(value, oc) -> FormFieldKind.Text(collapse value, oc)
        | FormFieldKind.Number(value, oc) -> FormFieldKind.Number(collapse value, oc)
        | FormFieldKind.Checkbox(value, ot) -> FormFieldKind.Checkbox(collapse value, ot)
        | FormFieldKind.Toggle(value, ot) -> FormFieldKind.Toggle(collapse value, ot)
        | FormFieldKind.Choice(options, value, oc) -> FormFieldKind.Choice(options, collapse value, oc)
        | FormFieldKind.TextArea(value, oc, rows) -> FormFieldKind.TextArea(collapse value, oc, rows)
        | FormFieldKind.RangedNumber(value, oc, mn, mx, st) ->
            FormFieldKind.RangedNumber(collapse value, oc, mn, mx, st)
        | FormFieldKind.Range(value, oc, mn, mx, st) -> FormFieldKind.Range(collapse value, oc, mn, mx, st)
        | FormFieldKind.SegmentedChoice(options, value, oc, orientation) ->
            FormFieldKind.SegmentedChoice(options, collapse value, oc, orientation)
        | FormFieldKind.Combobox(allowFreeText, oc, options, value) ->
            FormFieldKind.Combobox(allowFreeText, oc, options, collapse value)
        | FormFieldKind.Date(value, oc, variant, mn, mx, st) ->
            FormFieldKind.Date(collapse value, oc, variant, mn, mx, st)
        | FormFieldKind.DateRange(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateRange(collapse value, oc, variant, mn, mx, st)

    { item with Kind = kind }

/// Rewrite a whole tree into §16 canonical form. Identity on trees that are
/// already canonical (every decoded tree is, since the decode-side collapses).
and canonicalForm (node: Node<'Msg>) : Node<'Msg> =
    // Kind-level value-slot collapses.
    let kind =
        match node.Kind with
        | NodeKind.Form spec ->
            NodeKind.Form
                { spec with
                    Fields = spec.Fields |> List.map canonicalFormField }
        | NodeKind.Filters spec ->
            NodeKind.Filters
                { spec with
                    Items = spec.Items |> List.map canonicalFilterItem }
        | k -> k

    // Structural children.
    let kind =
        match getChildren kind with
        | Some children ->
            match withChildren kind (children |> List.map canonicalForm) with
            | Some rewritten -> rewritten
            | None -> kind
        | None -> kind

    let node = { node with Kind = kind }

    // Non-structural slots (state OnLoading/OnEmpty + kind-held nodes the
    // structural-children walk cannot reach) recurse through the shared lens.
    let slots, put = nonStructuralSlots node

    let node =
        if List.isEmpty slots then
            node
        else
            put (slots |> List.map canonicalForm)

    { node with
        State =
            node.State
            |> Option.bind (fun s ->
                if s.OnLoading.IsNone && s.OnEmpty.IsNone && s.OnError.IsNone then
                    None
                else
                    Some s)
        Style =
            node.Style
            |> Option.bind (fun s -> if s = Fuaran.UI.Defaults.style then None else Some s) }

/// Rewrite a bare `NodeKind` into §16 canonical form (the op codec's
/// `EditNode.newKind` splice). Routed through `canonicalForm` on a scratch
/// envelope so there is exactly ONE canonicalising traversal — never a second
/// per-kind walk to keep in step.
let canonicalFormKind (kind: NodeKind<'Msg>) : NodeKind<'Msg> =
    (canonicalForm
        { Id = "«canonical-form-scratch»"
          Kind = kind
          State = None
          Style = None
          Accessibility = None
          Motion = None
          ExtraAttributes = None
          // A scratch envelope: no node-level trait can be carried by a bare
          // `NodeKind`, so the tooltip is `None` here for the same reason every
          // other envelope slot is.
          Tooltip = None })
        .Kind
