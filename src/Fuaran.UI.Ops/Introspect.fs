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

open Fuaran.UI.Types

module NodeChildren = Fuaran.UI.NodeChildren

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
    // Phase 1962 — a multi-select carries `Values`, not `Value`.
    | NodeKind.Select s ->
        if s.Multiple = Some true then
            [ "Label"; "Source"; "Placeholder" ]
        else
            [ "Label"; "Source"; "Value"; "Placeholder" ]
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
    // Phase 1962 — a multi-select has no `Value` slot (it carries `Values`).
    | NodeKind.Select(s) ->
        if s.Multiple = Some true then
            [ "Source"; "Disabled" ]
        else
            [ "Source"; "Value"; "Disabled" ]
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

/// The ordered child list of a container kind — the surface the structural
/// ops edit — or `None` for a kind that holds none. Read from the tier's one
/// enumeration (`Fuaran.UI.NodeChildren`) rather than a copy of it.
///
/// FragmentRef is a pure leaf here — the renderer expands it at render time
/// via a separate resolver walk; the apply engine treats it as opaque. Address
/// a referenced body by its bare interior NodeId (which `mapNode` reaches via
/// the decl), not via the ref.
let getChildren (kind: NodeKind<'Msg>) : Node<'Msg> list option = NodeChildren.ordered kind

/// The kind with exactly this ordered child list, or `None` where it holds no
/// ordered list. The write half of `getChildren`.
let withChildren (kind: NodeKind<'Msg>) (children: Node<'Msg> list) : NodeKind<'Msg> option =
    NodeChildren.withOrdered kind children

// ─── Non-structural node positions ─────────────────────────────────────────
//
// `getChildren` answers ONE question — which kinds accept the structural ops —
// and several kinds hold real child nodes in shapes that are not a plain
// ordered list, so they answer `None` there and are right to. But traversal was
// built on the same function and inherited that answer, which made those nodes
// invisible to `allNodeIds` / `findNode` / `collectNodeIdsInto` — and, because
// `applyStructural` hands Fuaran.Core a `NodeWitness` whose `Children` IS
// `getChildren`, invisible to the duplicate-id rejection too. §4g promises ids
// are unique per tree; the positions below are what `keyedWitness` declares to
// Core, so its keyed engine refuses a collision inside a Switch case.
//
// Returned as a LENS — the nodes plus a function putting a same-length list
// back in the same positions — so the read and the write cannot drift apart.
// Since Phase 2038 the lens is `NodeChildren`'s, at its non-structural reach:
// the match that enumerates the positions lives there, once, for the whole
// tier, rather than here beside two renderer copies that disagreed with it.
//
// `StateBehaviour.OnError` is deliberately absent: it is `ErrorPayload -> Node`,
// so there is no node to enumerate until it is applied.
//
// Phase 1666 — each position carries its own LABEL, in the §3.3 spelling an
// author would recognise (`state.onLoading`, `Switch.cases[0].child`,
// `Mount.inputs[header]`).

let private nonStructuralSlots (node: Node<'Msg>) : (string * Node<'Msg>) list * (Node<'Msg> list -> Node<'Msg>) =
    let slots = NodeChildren.slotsIn NodeChildren.Reach.nonStructural node
    slots |> List.map (fun s -> s.Label, s.Node), NodeChildren.replace NodeChildren.Reach.nonStructural node

/// Every node held one step below `node` — structural children AND the
/// non-list positions. The traversal surface, as distinct from the
/// structural-op surface `getChildren` describes.
let descendantNodes (node: Node<'Msg>) : Node<'Msg> list =
    NodeChildren.children NodeChildren.Reach.keyed node

/// Rebuild `node` with `replacements` in exactly the positions `descendantNodes`
/// enumerates (structural children first, then the non-structural slots). The
/// write companion to `descendantNodes`, making the pair a lens over the whole
/// traversal surface the way `getChildren` / `withChildren` are over the
/// structural one. Positional only: the replacement list must have the same
/// length as `descendantNodes node` (any other length answers `node`
/// unchanged) — callers rebuild in place (an id-remap, a whole-tree map), never
/// structurally (insert / remove stay with the structural surface, where the
/// apply engine gates them).
let replaceDescendantNodes (node: Node<'Msg>) (replacements: Node<'Msg> list) : Node<'Msg> =
    NodeChildren.replace NodeChildren.Reach.keyed node replacements

// ─── Non-structural positions, by name (Phase 1666) ────────────────────────
//
// The structural ops walk `getChildren`, so everything a non-structural
// position holds — and everything BELOW it — was unreachable to them, and the
// engine said `NodeNotFound` for a node `findNode` and `UpdateProp` reach
// perfectly well. These two functions are what let it say something true
// instead, and neither introduces a traversal: both read the one lens above.
//
// The DISTINCTION they draw is the whole of the design. A node sitting AT a
// keyed position and a node sitting BELOW one are different problems. Below
// one, its removal or move is ordinary structural surgery at its own parent and
// the only thing missing is a descent — the position's arity does not change,
// which is exactly what the positional lens supports. AT one, the arity WOULD
// change, and several of these positions cannot express absence at all
// (`ErrorBoundary.fallback` is required; a `Switch` case with no child is not a
// case). There is no single right semantics for that, so it is named and
// refused rather than guessed.

/// The non-structural positions held one step below `node`, each with the slot
/// LABEL that names it (`state.onLoading`, `Switch.cases[0].child`,
/// `Mount.inputs[header]`). Read-only view of the same lens
/// `replaceDescendantNodes` writes through.
let nonStructuralPositions (node: Node<'Msg>) : (string * Node<'Msg>) list =
    let slots, _ = nonStructuralSlots node
    slots

/// Replace ONE non-structural position of `node`, named by its label, leaving
/// every other position and every structural child alone. `None` when the label
/// names no position on this node — the caller has misidentified the holder,
/// which is a defect rather than a no-op.
///
/// Arity-preserving by construction: it writes back through the same
/// same-length `put` the lens returns, so the position keeps its shape whatever
/// the replacement subtree looks like inside.
let replaceNonStructuralPosition (node: Node<'Msg>) (label: string) (replacement: Node<'Msg>) : Node<'Msg> option =
    let slots, put = nonStructuralSlots node

    if slots |> List.exists (fun (l, _) -> l = label) then
        slots
        |> List.map (fun (l, n) -> if l = label then replacement else n)
        |> put
        |> Some
    else
        None

// ─── The tier's Fuaran.Core witnesses (Phase 2038) ────────────────────────
//
// The structural apply delegates to Fuaran.Core.Ops, which reads the tree
// through these. `nodeWitness` is the STRUCTURAL witness — what Core edits and
// rebuilds through, so its `Children` is the ordered list (`getChildren`) and
// nothing more. `keyedWitness` declares every other position this tier holds a
// node in, DERIVED from the same lens `descendantNodes` reads, so the keyed walk
// Core runs (`Tree.traversal`, `Ops.applyContainedKeyed`,
// `Tree.graftWellFormedKeyed`) and the tier's own id walk cannot disagree about
// which nodes the tree holds. A second hand-written list of keyed positions is
// exactly the drift this replaces.

/// The tier's structural `NodeWitness` — what Fuaran.Core edits through.
let nodeWitness<'Msg> : Fuaran.Core.NodeWitness<Node<'Msg>, NodeId> =
    // `Node.Id` is a bare string since the swap; the op layer's addressing
    // stays `NodeId`-typed, wrapped at this witness boundary.
    { Id = fun n -> NodeId n.Id
      KindTag = fun n -> kindName n.Kind
      Children = fun n -> getChildren n.Kind |> Option.defaultValue []
      ReplaceChildren =
        fun n cs ->
            match withChildren n.Kind cs with
            | Some k -> { n with Kind = k }
            | None -> n }

/// The tier's `IdWitness` over `NodeId`.
let idWitness: Fuaran.Core.IdWitness<NodeId> =
    { ToString = fun (NodeId s) -> s
      OfString = NodeId
      Equals = (=) }

/// A READ-ONLY witness whose `Children` is `reach` — for Core's read walks
/// (`Tree.ids`, `Tree.tryPick`) at a reach the two witnesses above do not
/// spell. Its `ReplaceChildren` is the structural one, which does not
/// round-trip this `Children`, so it is never handed to a rebuild.
let private readingAt (reach: NodeChildren.Reach) : Fuaran.Core.NodeWitness<Node<'Msg>, NodeId> =
    { nodeWitness with
        Children = NodeChildren.children reach }

/// The tier's `KeyedWitness`: every position `nodeWitness.Children` does not
/// report — the `state` alternatives, the envelope `fallback`, the
/// error-boundary and switch arms, slot arguments — read and written through
/// `NodeChildren.Reach.nonStructural`, the lens `nonStructuralPositions` exposes.
let keyedWitness<'Msg> : Fuaran.Core.KeyedWitness<Node<'Msg>, NodeId> =
    { Surface = "Fuaran.UI.Ops.Introspect.allNodeIds — the tier's whole-tree id walk"
      KeyedChildren = fun n -> NodeChildren.children NodeChildren.Reach.nonStructural n
      ReplaceKeyedChildren = fun n rs -> NodeChildren.replace NodeChildren.Reach.nonStructural n rs
      // Place by re-identifying the node in the node's FIRST keyed position:
      // arity-preserving, so it never invents a position the node does not
      // hold, and `None` where it holds none.
      PlaceKeyedChild =
        fun n (NodeId id) ->
            match NodeChildren.children NodeChildren.Reach.nonStructural n with
            | [] -> None
            | first :: rest ->
                Some(NodeChildren.replace NodeChildren.Reach.nonStructural n ({ first with Id = id } :: rest))
      IdsUnique =
        fun n ->
            let ids = Fuaran.Core.Tree.ids (readingAt NodeChildren.Reach.keyed) n
            List.length (List.distinct ids) = List.length ids }

// ─── The whole-tree walks are Fuaran.Core's (Phase 2060) ──────────────────
//
// Every lookup below is a Fuaran.Core.Tree combinator over `walk`, Core's
// `Tree.traversal` of the two witnesses above — the same walk Core's keyed
// engine locates through when it applies a structural op. None of them is a
// walker of its own: what stays local is the witnesses and the questions.
//
// ORDER (decided in Phase 2060). Core's traversal visits a node's KEYED
// positions before its structural children; the tier's own walks visited them
// the other way round. On a tree whose ids are unique — every tree §8.1
// admits — the order is unobservable. On a tree that holds one id twice (the
// decoder judges shape and admits one) it decides WHICH node an id names, and
// the structural ops, which locate through Core, already answered keyed-first
// while `findNode` and `mapNode` answered structural-first: one op stream could
// edit one copy with `UpdateProp` and move the other with `MoveNode`. So every
// lookup here answers as Core does, keyed-first — one answer per tree to "which
// node is this id", pinned by the repeated-id case in `CoreWalkAdoptionTests`.

/// Core's keyed walk over the tier's witnesses: a node's children are its
/// keyed positions, then its structural list.
let private walk<'Msg> : Fuaran.Core.NodeWitness<Node<'Msg>, NodeId> =
    Fuaran.Core.Tree.traversal nodeWitness keyedWitness

/// The node carrying `target`, over every position the tree holds a node in.
let findNode (target: NodeId) (node: Node<'Msg>) : Node<'Msg> option =
    Fuaran.Core.Tree.tryFind walk idWitness target node

/// The OUTERMOST non-structural position on the path from `root` down to
/// `target` (Phase 1666): the holder's id, the slot label, the position's own
/// subtree, and whether `target` IS that subtree's root.
///
/// `None` means `target` is reachable from `root` through structural children
/// alone OR is absent from the tree entirely — the two are told apart by
/// `findNode`, and callers need that distinction because they report different
/// errors. Outermost rather than nearest, deliberately: a caller descending into
/// the position and re-running its own walk reaches a nested position on the
/// next turn, so one level per call composes; naming the innermost would
/// require the caller to know the path it was about to take.
///
/// One walk (Phase 2060): Core's `Tree.path` finds the id-path to `target`, and
/// this follows that path down from `root` one step at a time, asking at each
/// step only whether the next node on it is a structural child or a keyed
/// position. Until 2060 it walked the structural spine and ran a whole
/// `findNode` inside every keyed position at every level.
let nonStructuralAncestor (target: NodeId) (root: Node<'Msg>) : (NodeId * string * Node<'Msg> * bool) option =
    let (NodeId targetRaw) = target

    let rec follow (node: Node<'Msg>) (below: NodeId list) =
        match below with
        | [] -> None
        | NodeId next :: rest ->
            match getChildren node.Kind |> Option.bind (List.tryFind (fun c -> c.Id = next)) with
            | Some child -> follow child rest
            | None ->
                nonStructuralPositions node
                |> List.tryFind (fun (_, positioned) -> positioned.Id = next)
                |> Option.map (fun (label, positioned) -> NodeId node.Id, label, positioned, positioned.Id = targetRaw)

    match Fuaran.Core.Tree.path walk idWitness target root with
    | Some(_ :: below) -> follow root below
    | _ -> None

/// The node whose STRUCTURAL child list holds `target`, and `target`'s index in
/// it — searched through every node `reach` descends to. `None` for the root, for
/// a target not present, and for a target held directly in a keyed position
/// (it has no structural parent).
///
/// The reach decides only how far the SEARCH goes; the parent found is always a
/// structural container, so an index names a place in its own child list.
/// `NodeChildren.Reach.structural` walks the structural spine alone;
/// `NodeChildren.Reach.keyed` also finds a container held below a keyed
/// position — a `Box` in a `Switch` case, say — which the structural ops reach
/// by descending into the position. The search is Core's `Tree.tryPick` over
/// the reach (Phase 2060).
let findParentWithin (reach: NodeChildren.Reach) (target: NodeId) (root: Node<'Msg>) : (Node<'Msg> * int) option =
    let (NodeId targetRaw) = target

    root
    |> Fuaran.Core.Tree.tryPick (readingAt reach) (fun node ->
        getChildren node.Kind
        |> Option.bind (List.tryFindIndex (fun c -> c.Id = targetRaw))
        |> Option.map (fun i -> node, i))

/// `findParentWithin` over the structural spine alone: `Some (parent,
/// indexOfTarget)` when `target` is a structural child of a node reachable from
/// `root` through structural children.
let findParent (target: NodeId) (root: Node<'Msg>) : (Node<'Msg> * int) option =
    findParentWithin NodeChildren.Reach.structural target root

/// Every id in the tree, over the WHOLE traversal surface — including nodes held
/// in positions the structural ops cannot edit — in Core's keyed-walk preorder
/// (`Tree.idsKeyed`).
let allNodeIds (node: Node<'Msg>) : NodeId list =
    Fuaran.Core.Tree.idsKeyed nodeWitness keyedWitness node

/// Every NodeId whose kind reports `field` in its `availableFields` list, over
/// the structural spine in preorder. Used to populate the §4d
/// `hint.nodes_with_<field>_field` payload so the AI can pivot to a different
/// addressable node.
let nodesWithField (field: string) (root: Node<'Msg>) : NodeId list =
    root
    |> Fuaran.Core.Tree.filter nodeWitness (fun node -> availableFields node.Kind |> List.contains field)
    |> List.map (fun node -> NodeId node.Id)

/// Replace the node with `target` id by `replace` applied to it. Returns the new
/// root, or `None` if `target` is held nowhere in the tree.
///
/// Core's `Tree.updateNode` over the keyed walk (Phase 2060): ONE pass that
/// locates the target, iteratively, and rebuilds only the root-to-target path,
/// sharing every other subtree. It is the workhorse for the ops that rewrite a
/// single node in place (EditNode / UpdateProp / ReplaceBinding / UpdateStyle /
/// PatchStyle / UpdateState / UpdateAccessibility / UpdateTooltip), and it
/// reaches every node `findNode` does — an addressable-but-uneditable node
/// would be a trap of its own.
let mapNode (target: NodeId) (replace: Node<'Msg> -> Node<'Msg>) (node: Node<'Msg>) : Node<'Msg> option =
    Fuaran.Core.Tree.updateNode walk idWitness target replace node

/// The depth of `target` (the root is 0) over the keyed walk — Core's
/// `Tree.depth`, one early-exit pass. `None` when the tree holds no `target`.
let depthOf (target: NodeId) (root: Node<'Msg>) : int option =
    Fuaran.Core.Tree.depth walk idWitness target root

/// True when `ancestorId` is `descendantId` or lies on the path from the root
/// down to it — Core's `Tree.path` over the keyed walk, one early-exit pass.
/// Used to prevent MoveNode cycles.
let isAncestorOf (ancestorId: NodeId) (descendantId: NodeId) (root: Node<'Msg>) : bool =
    Fuaran.Core.Tree.path walk idWitness descendantId root
    |> Option.exists (List.contains ancestorId)

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
// `JsonDecode`'s decode-side collapses mirror it. It lives HERE because it maps
// through this module's keyed walk — Core's `Tree.map`, no second per-kind walk.

let private canonicalFormField (field: FormField<'Msg>) : FormField<'Msg> =
    // A value slot that spells the field's exact auto-binding collapses to
    // `None` (the canonical omitted form). Any other value passes through.
    let collapse
        (expected: Fuaran.Core.JVal option)
        (value: Binding<Fuaran.Core.JVal> option)
        : Binding<Fuaran.Core.JVal> option =
        match value with
        | Some(Binding.State(key, dv)) when key = field.Id && dv = expected -> None
        | v -> v

    let kind =
        match field.Kind with
        | FormFieldKind.Text(value, oc) ->
            FormFieldKind.Text(
                collapse (Some(FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.text)) value,
                oc
            )
        | FormFieldKind.Number(value, oc) ->
            FormFieldKind.Number(
                collapse (Some(FieldValue.encodeNumber Fuaran.UI.Defaults.ControlValueDefaults.number)) value,
                oc
            )
        | FormFieldKind.Checkbox(value, ot) ->
            FormFieldKind.Checkbox(
                collapse (Some(FieldValue.encodeBool Fuaran.UI.Defaults.ControlValueDefaults.checkbox)) value,
                ot
            )
        | FormFieldKind.Toggle(value, ot) ->
            FormFieldKind.Toggle(
                collapse (Some(FieldValue.encodeBool Fuaran.UI.Defaults.ControlValueDefaults.checkbox)) value,
                ot
            )
        | FormFieldKind.Choice(options, value, oc) ->
            FormFieldKind.Choice(
                options,
                collapse (Option.map FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.choice) value,
                oc
            )
        | FormFieldKind.TextArea(value, oc, rows) ->
            FormFieldKind.TextArea(
                collapse (Some(FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.text)) value,
                oc,
                rows
            )
        | FormFieldKind.RangedNumber(value, oc, mn, mx, st) ->
            FormFieldKind.RangedNumber(
                collapse (Some(FieldValue.encodeNumber Fuaran.UI.Defaults.ControlValueDefaults.number)) value,
                oc,
                mn,
                mx,
                st
            )
        | FormFieldKind.Range(value, oc, mn, mx, st) ->
            FormFieldKind.Range(
                collapse (Some(FieldValue.encodeRange Fuaran.UI.Defaults.ControlValueDefaults.range)) value,
                oc,
                mn,
                mx,
                st
            )
        | FormFieldKind.SegmentedChoice(options, value, oc, orientation) ->
            FormFieldKind.SegmentedChoice(
                options,
                collapse (Option.map FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.choice) value,
                oc,
                orientation
            )
        | FormFieldKind.Combobox(allowFreeText, oc, options, value) ->
            FormFieldKind.Combobox(
                allowFreeText,
                oc,
                options,
                collapse (Option.map FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.combobox) value
            )
        | FormFieldKind.DateTime(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateTime(
                collapse (Some(FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.dateTime)) value,
                oc,
                variant,
                mn,
                mx,
                st
            )
        | FormFieldKind.DateTimeRange(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateTimeRange(
                collapse (Some(FieldValue.encodeDateRange Fuaran.UI.Defaults.ControlValueDefaults.dateTimeRange)) value,
                oc,
                variant,
                mn,
                mx,
                st
            )
        // Phase 1130 — a rating's placeholder is the scale's own floor, a
        // colour's is the unset black a native colour input reports; both are
        // named once in `ControlValueDefaults` so this collapse and the two
        // decoders cannot drift.
        | FormFieldKind.Rating(allowHalf, max, oc, value) ->
            FormFieldKind.Rating(
                allowHalf,
                max,
                oc,
                collapse (Some(FieldValue.encodeNumber Fuaran.UI.Defaults.ControlValueDefaults.rating)) value
            )
        | FormFieldKind.Color(oc, value) ->
            FormFieldKind.Color(
                oc,
                collapse (Some(FieldValue.encodeText Fuaran.UI.Defaults.ControlValueDefaults.color)) value
            )
        // Phase 1121 — the token list collapses on the same rule: a value slot
        // spelling exactly this field's auto-binding IS the omitted form, and
        // the empty list is the one placeholder a token field has.
        | FormFieldKind.Tokens(allowFreeText, oc, suggestions, value) ->
            FormFieldKind.Tokens(
                allowFreeText,
                oc,
                suggestions,
                collapse (Some(FieldValue.encodeTokens Fuaran.UI.Defaults.ControlValueDefaults.tokens)) value
            )

    { field with Kind = kind }

let private canonicalFilterItem (item: FilterSpec<'Msg>) : FilterSpec<'Msg> =
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
        | FormFieldKind.DateTime(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateTime(collapse value, oc, variant, mn, mx, st)
        | FormFieldKind.DateTimeRange(value, oc, variant, mn, mx, st) ->
            FormFieldKind.DateTimeRange(collapse value, oc, variant, mn, mx, st)
        | FormFieldKind.Rating(allowHalf, max, oc, value) -> FormFieldKind.Rating(allowHalf, max, oc, collapse value)
        | FormFieldKind.Color(oc, value) -> FormFieldKind.Color(oc, collapse value)
        | FormFieldKind.Tokens(allowFreeText, oc, suggestions, value) ->
            FormFieldKind.Tokens(allowFreeText, oc, suggestions, collapse value)

    { item with Kind = kind }

/// One node's own §16 collapses — its kind's value slots, its `state` and
/// `style` envelopes — with everything it holds left as it is.
let private canonicalNode (node: Node<'Msg>) : Node<'Msg> =
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

    { node with
        Kind = kind
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

/// Rewrite a whole tree into §16 canonical form. Identity on trees that are
/// already canonical (every decoded tree is, since the decode-side collapses).
///
/// Core's `Tree.map` over the keyed walk (Phase 2060): every node the tree
/// holds — structural children, `state` alternatives, kind-held arms — is
/// rewritten bottom-up by `canonicalNode`, iteratively. A node's own collapses
/// read nothing below it, so bottom-up is the same answer the top-down
/// recursion this replaces gave.
let canonicalForm (node: Node<'Msg>) : Node<'Msg> =
    Fuaran.Core.Tree.map walk canonicalNode node

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
          Fallback = None
          ExtraAttributes = None
          // A scratch envelope: no node-level trait can be carried by a bare
          // `NodeKind`, so the tooltip and the visibility predicate are `None`
          // here for the same reason every other envelope slot is.
          Tooltip = None
          Visible = None })
        .Kind
