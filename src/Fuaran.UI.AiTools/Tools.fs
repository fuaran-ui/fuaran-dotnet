module Fuaran.UI.AiTools.Tools

// ============================================================================
//  Fuaran runtime-introspection AI tools — the four §4i entry points.
//
//  getNodeState  : ctx -> includeKeys -> tree -> nodeId -> Result<NodeState, IntrospectError>
//  getBindingValue: ctx -> tree -> nodeId -> slot -> Result<ResolvedBindingResult, IntrospectError>
//  getRenderedDom: ctx -> tree -> nodeId -> Result<GeometryTree, IntrospectError>
//  getRuntimeErrors: ctx -> sinceTurn -> ErrorEntry list
//
//  All tools are read-only and idempotent — same inputs → same observable
//  state, per the deterministic-contract acceptance criterion. Time-sensitive
//  fields flow through the `IIntrospectionClock` seam; geometry through
//  `IGeometryProbe`; current-state through `ICurrentStateProbe`. Binding
//  resolution uses `BindingProbe.tryResolveBinding` against the context's
//  `Sources`.
//
//  The Introspect helpers from `Fuaran.UI.Ops.Introspect` (kindName /
//  availableBindingSlots / findNode / getChildren) are re-used here — both
//  packages share the reflection-free per-NodeKind dispatch shape, and
//  promoting them to a common helper module would expand this opener's
//  scope beyond the declared file set.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools.Seams
open Fuaran.UI.AiTools.BindingProbe

// ─── Error builders ────────────────────────────────────────────────────────

let private nodeNotFound (NodeId rawId) : IntrospectError =
    { Code = IntrospectErrorCode.NodeNotFound
      Message = sprintf "Node '%s' not found in tree." rawId
      Hint = IntrospectErrorHint.empty }

let private slotNotFound (kind: NodeKind<'Msg>) (NodeId rawId) (slot: string) : IntrospectError =
    { Code = IntrospectErrorCode.SlotNotFound
      Message = sprintf "Binding slot '%s' not found on node '%s' (kind=%s)." slot rawId (kindName kind)
      Hint =
        { NodeKind = Some(kindName kind)
          AvailableFields = availableBindingSlots kind
          Suggestion =
            Some
                "Pick a slot from `available_fields`, or use the node's spec-record top-level fields via getNodeState's `props` block." } }

// ─── Per-NodeKind binding-slot extraction ──────────────────────────────────
//
// Mirrors `Fuaran.UI.Ops.Introspect.availableBindingSlots` but returns the
// resolved value per slot, not just the slot name. Returns an empty Map
// for Kinds without Binding-typed slots (every Layout except Stepper / Tabs /
// Disclosure, every Display except Metric/Sparkline/Progress/LabelValueRow,
// every Input except Button/Select, Visualisation Table, Custom). The Phase 118
// consistency test pins this table against availableBindingSlots + the
// Apply-engine replaceBinding dispatch.

let private resolveSlot<'T> (ctx: IntrospectionContext) (binding: Binding<'T>) : ResolvedBindingResult =
    tryResolveBinding ctx binding

/// Resolve an optional `Binding<'T> option` slot (Phase 130 interactive
/// disabled-state shape). `Some` resolves the binding; `None` surfaces the
/// synthetic-None Resolved result (Expression `"$none"`) so callers can tell
/// "slot exists, no override" from "slot missing" — same posture as
/// Button.Disabled / Tabs.ActiveTag / Metric.Trend.
let private resolveOptionalSlot<'T>
    (ctx: IntrospectionContext)
    (binding: Binding<'T> option)
    : ResolvedBindingResult option =
    match binding with
    | Some b -> Some(resolveSlot ctx b)
    | None ->
        Some(
            ResolvedBindingResult.Resolved
                { Expression = "$none"
                  ResolvedValue = None
                  TypeHint = None
                  Source = BindingSource.Static }
        )

let private extractBindings (ctx: IntrospectionContext) (kind: NodeKind<'Msg>) : Map<string, ResolvedBindingResult> =
    match kind with
    | NodeKind.Metric(spec) ->
        let baseMap = Map.empty |> Map.add "Value" (resolveSlot ctx spec.Value)

        match spec.Trend with
        | Some trend -> baseMap |> Map.add "Trend" (resolveSlot ctx trend)
        | None -> baseMap
    | NodeKind.Sparkline(spec) -> Map.ofList [ "Source", resolveSlot ctx spec.Source ]
    | NodeKind.Progress(spec) -> Map.ofList [ "Fraction", resolveSlot ctx spec.Fraction ]
    // LabelValueRow's single Binding-typed slot.
    | NodeKind.LabelValueRow(spec) -> Map.ofList [ "Value", resolveSlot ctx spec.Value ]
    // Link's Href is a Binding<string> slot.
    | NodeKind.Link(spec) -> Map.ofList [ "Href", resolveSlot ctx spec.Href ]
    | NodeKind.Stepper(spec) -> Map.ofList [ "ActiveStep", resolveSlot ctx spec.ActiveStep ]
    // Tabs: ActiveIndex always resolves; ActiveTag is the optional typed-tag
    // overlay — added only when `Some`, mirroring Metric.Trend's shape above.
    | NodeKind.Tabs(spec) ->
        let baseMap = Map.empty |> Map.add "ActiveIndex" (resolveSlot ctx spec.ActiveIndex)

        match spec.ActiveTag with
        | Some tag -> baseMap |> Map.add "ActiveTag" (resolveSlot ctx tag)
        | None -> baseMap
    | NodeKind.Disclosure(spec) -> Map.ofList [ "Open", resolveSlot ctx spec.Open ]
    // Button: Disabled is the optional bound disabled-state — added only when
    // `Some`, mirroring Metric.Trend / Tabs.ActiveTag above.
    | NodeKind.Button(spec) ->
        match spec.Disabled with
        | Some disabled -> Map.ofList [ "Disabled", resolveSlot ctx disabled ]
        | None -> Map.empty
    // Select: Source + Value always resolve; Disabled is the optional bound
    // disabled-state (Phase 130) — added only when `Some`, mirroring
    // Button.Disabled / Metric.Trend.
    | NodeKind.Select(spec) ->
        let baseMap =
            Map.ofList [ "Source", resolveSlot ctx spec.Source; "Value", resolveSlot ctx spec.Value ]

        match spec.Disabled with
        | Some disabled -> baseMap |> Map.add "Disabled" (resolveSlot ctx disabled)
        | None -> baseMap
    // Form / FileUpload: the optional bound disabled-state (Phase 130) is the
    // only Binding-typed slot — added only when `Some`.
    | NodeKind.Form(spec) ->
        match spec.Disabled with
        | Some disabled -> Map.ofList [ "Disabled", resolveSlot ctx disabled ]
        | None -> Map.empty
    | NodeKind.FileUpload(spec) ->
        match spec.Disabled with
        | Some disabled -> Map.ofList [ "Disabled", resolveSlot ctx disabled ]
        | None -> Map.empty
    | NodeKind.DataGrid(spec) -> Map.ofList [ "Source", resolveSlot ctx spec.Source ]
    | NodeKind.Chart(spec) -> Map.ofList [ "Source", resolveSlot ctx spec.Source ]
    | NodeKind.Map(spec) -> Map.ofList [ "Source", resolveSlot ctx spec.Source ]
    | _ -> Map.empty

// ─── Per-NodeKind binding-slot lookup (for getBindingValue) ─────────────────
//
// Returns the resolved value for a single slot or `None` if the slot isn't
// declared on the node's kind. Caller maps `None` to a `SlotNotFound` error.

let private extractSlot<'Msg>
    (ctx: IntrospectionContext)
    (kind: NodeKind<'Msg>)
    (slot: string)
    : ResolvedBindingResult option =
    match kind, slot with
    | NodeKind.Metric(spec), "Value" -> Some(resolveSlot ctx spec.Value)
    | NodeKind.Metric(spec), "Trend" ->
        match spec.Trend with
        | Some trend -> Some(resolveSlot ctx trend)
        | None ->
            // Trend is an optional slot — when absent, surface as Resolved
            // with no value rather than NotFound; the orchestrator
            // pattern-matches on `ResolvedBinding.ResolvedValue = None` to
            // distinguish "slot exists but no override" from "slot missing".
            Some(
                ResolvedBindingResult.Resolved
                    { Expression = "$none"
                      ResolvedValue = None
                      TypeHint = None
                      Source = BindingSource.Static }
            )
    | NodeKind.Sparkline(spec), "Source" -> Some(resolveSlot ctx spec.Source)
    | NodeKind.Progress(spec), "Fraction" -> Some(resolveSlot ctx spec.Fraction)
    // LabelValueRow's Source slot.
    | NodeKind.LabelValueRow(spec), "Value" -> Some(resolveSlot ctx spec.Value)
    | NodeKind.Link(spec), "Href" -> Some(resolveSlot ctx spec.Href)
    | NodeKind.Stepper(spec), "ActiveStep" -> Some(resolveSlot ctx spec.ActiveStep)
    | NodeKind.Tabs(spec), "ActiveIndex" -> Some(resolveSlot ctx spec.ActiveIndex)
    | NodeKind.Tabs(spec), "ActiveTag" ->
        match spec.ActiveTag with
        | Some tag -> Some(resolveSlot ctx tag)
        | None ->
            // ActiveTag is an optional slot — when absent, surface as Resolved
            // with no value (same posture as Metric.Trend) so the orchestrator
            // distinguishes "slot exists but no override" from "slot missing".
            Some(
                ResolvedBindingResult.Resolved
                    { Expression = "$none"
                      ResolvedValue = None
                      TypeHint = None
                      Source = BindingSource.Static }
            )
    | NodeKind.Disclosure(spec), "Open" -> Some(resolveSlot ctx spec.Open)
    | NodeKind.Button(spec), "Disabled" ->
        match spec.Disabled with
        | Some disabled -> Some(resolveSlot ctx disabled)
        | None ->
            // Disabled is an optional slot — when absent, surface as Resolved
            // with no value (same posture as Tabs.ActiveTag / Metric.Trend) so
            // the orchestrator distinguishes "slot exists but no override" from
            // "slot missing".
            Some(
                ResolvedBindingResult.Resolved
                    { Expression = "$none"
                      ResolvedValue = None
                      TypeHint = None
                      Source = BindingSource.Static }
            )
    | NodeKind.Select(spec), "Source" -> Some(resolveSlot ctx spec.Source)
    | NodeKind.Select(spec), "Value" -> Some(resolveSlot ctx spec.Value)
    // Phase 130: Select / Form / FileUpload optional bound disabled-state.
    // When absent, surface as Resolved with no value (synthetic-None posture,
    // mirroring Button.Disabled / Tabs.ActiveTag / Metric.Trend) so the
    // orchestrator distinguishes "slot exists but no override" from "slot
    // missing".
    | NodeKind.Select(spec), "Disabled" -> resolveOptionalSlot ctx spec.Disabled
    | NodeKind.Form(spec), "Disabled" -> resolveOptionalSlot ctx spec.Disabled
    | NodeKind.FileUpload(spec), "Disabled" -> resolveOptionalSlot ctx spec.Disabled
    | NodeKind.DataGrid(spec), "Source" -> Some(resolveSlot ctx spec.Source)
    | NodeKind.Chart(spec), "Source" -> Some(resolveSlot ctx spec.Source)
    | NodeKind.Map(spec), "Source" -> Some(resolveSlot ctx spec.Source)
    | _ -> None

// ─── Per-NodeKind props extraction ─────────────────────────────────────────
//
// Mirrors `Fuaran.UI.Ops.Introspect.availableFields` but returns
// (name, boxed-value) pairs. Function-typed slots (OnSubmit / OnChange /
// OnSelect / OnRowClick / etc.) are surfaced as PropEntry with
// `Value = None` + a TypeHint indicating the function shape — enough for
// the orchestrator to know the slot is wired without us serialising a
// closure. Children are omitted (the orchestrator queries those via
// getRenderedDom + tree summary diff).

/// F# 10 `box _` types as `obj | null`; PropEntry / ResolvedBinding
/// require nonnull-obj. Launder via Unchecked.nonNull — same shape
/// Fuaran.UI.Ops.Tests's `nn` helper uses for its boxing.
let private boxNN (value: 'T) : obj = box value |> Unchecked.nonNull

let private entry (name: string) (value: obj option) (typeHint: string option) : PropEntry =
    { Name = name
      Value = value
      TypeHint = typeHint }

let private opaqueFn (name: string) (signature: string) : PropEntry = entry name None (Some signature)

let private valueEntry (name: string) (value: obj) : PropEntry =
    let typeName =
        // value.GetType() is `Type | null` under F# 10 nullness; launder
        // before dereferencing .Name.
        (value.GetType() |> Unchecked.nonNull).Name

    entry name (Some(boxNN value)) (Some typeName)

let private extractProps (kind: NodeKind<'Msg>) : PropEntry list =
    match kind with
    | NodeKind.Box(spec) ->
        // Phase 390 — surface the layout-mode-appropriate props (mirroring the
        // retired Stack/GridLayout/Card surfaces) plus the optional Heading.
        let layoutEntries =
            match spec.Layout with
            | BoxLayout.Flex(direction, wrap, _) -> [ valueEntry "Orientation" direction; valueEntry "Wrap" wrap ]
            | BoxLayout.Grid(cols, templateColumns, _) ->
                let baseEntries = [ valueEntry "Cols" cols ]

                match templateColumns with
                | Some t -> baseEntries @ [ valueEntry "TemplateColumns" t ]
                | None -> baseEntries @ [ entry "TemplateColumns" None (Some "string option") ]
            | BoxLayout.Auto -> []

        let headingEntries =
            match spec.Heading with
            | Some h -> [ valueEntry "Heading" h ]
            | None -> [ entry "Heading" None (Some "TextSource option") ]

        layoutEntries @ headingEntries
    | NodeKind.SplitPanel(spec) -> [ valueEntry "Weight" spec.Weight ]
    | NodeKind.Tabs(spec) -> [ valueEntry "Orientation" spec.Orientation ]
    | NodeKind.Stepper(_) ->
        // ActiveStep is a Binding slot exposed via `extractBindings`, not
        // a top-level prop. No non-binding scalars on Stepper.
        []
    | NodeKind.SummaryList(spec) ->
        // Surface the optional section heading.
        match spec.Heading with
        | Some h -> [ valueEntry "Heading" h ]
        | None -> [ entry "Heading" None (Some "TextSource option") ]
    | NodeKind.Disclosure(spec) ->
        // Surface required Heading + DefaultOpen scalar.
        // Open is a Binding<bool> — exposed via the binding-slots extractor
        // rather than as a scalar prop.
        [ valueEntry "Heading" spec.Heading; valueEntry "DefaultOpen" spec.DefaultOpen ]
    | NodeKind.Modal(spec) ->
        // Phase 289 — Dismissable + optional Heading scalars (Open binding +
        // OnDismiss action are not scalar props).
        [ valueEntry "Dismissable" spec.Dismissable
          (match spec.Heading with
           | Some h -> valueEntry "Heading" h
           | None -> entry "Heading" None (Some "TextSource option")) ]
    | NodeKind.ScrollArea(spec) ->
        // Phase 289 — scroll axis + optional pixel bounds.
        [ valueEntry "Orientation" spec.Orientation
          (match spec.MaxHeight with
           | Some h -> valueEntry "MaxHeight" h
           | None -> entry "MaxHeight" None (Some "int option"))
          (match spec.MaxWidth with
           | Some w -> valueEntry "MaxWidth" w
           | None -> entry "MaxWidth" None (Some "int option")) ]
    | NodeKind.Heading(spec) ->
        // Include Variant alongside Level + Text.
        [ valueEntry "Level" spec.Level
          valueEntry "Text" spec.Text
          valueEntry "Variant" spec.Variant ]
    | NodeKind.Markdown(spec) -> [ valueEntry "Text" spec.Text ]
    | NodeKind.Metric(spec) ->
        [ valueEntry "Label" spec.Label
          valueEntry "Format" spec.Format
          valueEntry "Tone" spec.Tone
          valueEntry "Weight" spec.Weight
          valueEntry "Emphasis" spec.Emphasis
          (match spec.TrendFormat with
           | Some f -> valueEntry "TrendFormat" f
           | None -> entry "TrendFormat" None (Some "CellFormat option"))
          valueEntry "TrendPolarity" spec.TrendPolarity
          (match spec.Icon with
           | Some i -> valueEntry "Icon" i
           | None -> entry "Icon" None (Some "IconSource option"))
          (match spec.Subtext with
           | Some s -> valueEntry "Subtext" s
           | None -> entry "Subtext" None (Some "TextSource option")) ]
    | NodeKind.Badge(spec) -> [ valueEntry "Label" spec.Label; valueEntry "Variant" spec.Variant ]
    | NodeKind.Sparkline(_) -> []
    | NodeKind.Callout(spec) ->
        [ valueEntry "Tone" spec.Tone
          (match spec.Heading with
           | Some h -> valueEntry "Heading" h
           | None -> entry "Heading" None (Some "TextSource option"))
          valueEntry "Body" spec.Body
          (match spec.Icon with
           | Some i -> valueEntry "Icon" i
           | None -> entry "Icon" None (Some "IconSource option"))
          valueEntry "Dismissable" spec.Dismissable ]
    | NodeKind.Progress(spec) ->
        [ (match spec.Label with
           | Some l -> valueEntry "Label" l
           | None -> entry "Label" None (Some "TextSource option"))
          (match spec.Caveat with
           | Some c -> valueEntry "Caveat" c
           | None -> entry "Caveat" None (Some "TextSource option"))
          valueEntry "Indeterminate" spec.Indeterminate
          valueEntry "Tone" spec.Tone ]
    | NodeKind.Skeleton(spec) -> [ valueEntry "Rows" spec.Rows ]
    | NodeKind.Icon(spec) ->
        // Phase 821 — all four Icon fields are scalar props (no Binding slot).
        [ valueEntry "Icon" spec.Icon
          valueEntry "Size" spec.Size
          valueEntry "Tone" spec.Tone
          (match spec.Label with
           | Some l -> valueEntry "Label" l
           | None -> entry "Label" None (Some "string option")) ]
    | NodeKind.LabelValueRow(spec) ->
        // Source is a Binding slot exposed via
        // extractBindings; the remaining four fields are scalar props.
        [ valueEntry "Label" spec.Label
          valueEntry "Format" spec.Format
          valueEntry "Emphasis" spec.Emphasis
          (match spec.Help with
           | Some h -> valueEntry "Help" h
           | None -> entry "Help" None (Some "TextSource option")) ]
    | NodeKind.Fact(spec) ->
        // No Binding slot: `Value` is a TextSource (its Bound leg resolves
        // through renderText, not the slot surface) — all five are props.
        [ valueEntry "Label" spec.Label
          valueEntry "Value" spec.Value
          valueEntry "Tone" spec.Tone
          valueEntry "Emphasis" spec.Emphasis
          (match spec.Help with
           | Some h -> valueEntry "Help" h
           | None -> entry "Help" None (Some "TextSource option")) ]
    | NodeKind.Link(spec) ->
        // Href is a Binding slot exposed via extractBindings; the remaining
        // fields are scalar props.
        [ valueEntry "Label" spec.Label
          (match spec.Rel with
           | Some r -> valueEntry "Rel" r
           | None -> entry "Rel" None (Some "string option"))
          (match spec.Target with
           | Some t -> valueEntry "Target" t
           | None -> entry "Target" None (Some "string option"))
          valueEntry "Download" spec.Download ]
    | NodeKind.Image(spec) ->
        // Phase 287 — Src is a Binding<string> (sanitised at render); Alt +
        // Variant are scalar props.
        [ valueEntry "Alt" spec.Alt; valueEntry "Variant" spec.Variant ]
    | NodeKind.List(spec) ->
        [ valueEntry "Ordered" spec.Ordered
          entry "ItemCount" (Some(boxNN (List.length spec.Items))) (Some "TextSource list (count)") ]
    | NodeKind.Toast(spec) ->
        // Phase 289 — Open is a Binding<bool>; Message + Tone + Dismissable are
        // scalar props.
        [ valueEntry "Message" spec.Message
          valueEntry "Tone" spec.Tone
          valueEntry "Dismissable" spec.Dismissable ]
    | NodeKind.CodeBlock(spec) ->
        // Phase 290 — Language + LineNumbers + Copyable scalars + a code-length
        // peek (the raw source itself is not surfaced as a scalar prop).
        [ valueEntry "Language" spec.Language
          valueEntry "LineNumbers" spec.LineNumbers
          valueEntry "Copyable" spec.Copyable
          entry "CodeLength" (Some(boxNN spec.Code.Length)) (Some "int (source length)")
          entry "HighlightLineCount" (Some(boxNN (List.length spec.HighlightLines))) (Some "int list (count)") ]
    | NodeKind.Math(spec) ->
        // Phase 293 — Display mode + a source-length peek (the LaTeX source is
        // not surfaced as a scalar prop).
        [ valueEntry "Display" spec.Display
          entry "SourceLength" (Some(boxNN spec.Source.Length)) (Some "int (LaTeX source length)") ]
    | NodeKind.Drawing(spec) ->
        // Phase 524 — surface shape-count + a viewBox peek; the typed Shape list
        // is inspected structurally, not as scalar props.
        [ entry "ShapeCount" (Some(boxNN (List.length spec.Shapes))) (Some "Shape list (count)")
          entry
              "ViewBox"
              (Some(
                  boxNN (
                      sprintf "%g %g %g %g" spec.ViewBox.MinX spec.ViewBox.MinY spec.ViewBox.Width spec.ViewBox.Height
                  )
              ))
              (Some "ViewBox (minX minY width height)") ]
    | NodeKind.Form(spec) ->
        [ valueEntry "SubmitLabel" spec.SubmitLabel
          opaqueFn "OnSubmit" "Action<'Msg>"
          entry "Fields" (Some(boxNN (List.length spec.Fields))) (Some "FormField list (count)") ]
    | NodeKind.Filters(spec) ->
        [ entry "FilterCount" (Some(boxNN (List.length spec.Items))) (Some "FilterSpec list (count)") ]
    | NodeKind.Button(spec) ->
        [ valueEntry "Label" spec.Label
          valueEntry "Variant" spec.Variant
          (match spec.Icon with
           | Some i -> valueEntry "Icon" i
           | None -> entry "Icon" None (Some "IconSource option"))
          opaqueFn "OnClick" "Action<'Msg>" ]
    | NodeKind.FileUpload(spec) ->
        [ valueEntry "Label" spec.Label
          entry "Accept" (Some(boxNN (String.concat ", " spec.Accept))) (Some "string list (comma-joined for the wire)")
          valueEntry "Multiple" spec.Multiple
          opaqueFn "OnSelect" "FileSelection list -> Action<'Msg>" ]
    | NodeKind.Select(spec) ->
        [ valueEntry "Label" spec.Label
          (match spec.Placeholder with
           | Some p -> valueEntry "Placeholder" p
           | None -> entry "Placeholder" None (Some "TextSource option"))
          opaqueFn "OnChange" "string option -> Action<'Msg>" ]
    | NodeKind.DataGrid(spec) ->
        [ entry "ColumnCount" (Some(boxNN (List.length spec.Columns))) (Some "Column list (count)")
          valueEntry "Editable" spec.Editable
          (match spec.OnRowClick with
           | Some _ -> opaqueFn "OnRowClick" "obj -> Action<'Msg>"
           | None -> entry "OnRowClick" None (Some "(obj -> Action<'Msg>) option"))
          // Phase 393 — a static read-only grid reports its TextSource matrix dimensions.
          yield!
              (match spec.StaticRows with
               | Some sr ->
                   [ entry "HeaderCount" (Some(boxNN (List.length sr.Headers))) (Some "TextSource list (count)")
                     entry "RowCount" (Some(boxNN (List.length sr.Rows))) (Some "TextSource list list (count)") ]
               | None -> []) ]
    | NodeKind.Chart(spec) ->
        [ valueEntry "Kind" spec.Kind
          valueEntry "XField" spec.XField
          entry
              "YFields"
              (Some(boxNN (String.concat ", " spec.YFields)))
              (Some "string list (comma-joined for the wire)")
          (match spec.Title with
           | Some t -> valueEntry "Title" t
           | None -> entry "Title" None (Some "TextSource option"))
          (match spec.OnPointClick with
           | Some _ -> opaqueFn "OnPointClick" "obj -> Action<'Msg>"
           | None -> entry "OnPointClick" None (Some "(obj -> Action<'Msg>) option")) ]
    | NodeKind.Map(spec) ->
        [ valueEntry "CentreLatitude" spec.CentreLatitude
          valueEntry "CentreLongitude" spec.CentreLongitude
          valueEntry "Zoom" spec.Zoom
          (match spec.OnMarkerClick with
           | Some _ -> opaqueFn "OnMarkerClick" "MapMarker -> Action<'Msg>"
           | None -> entry "OnMarkerClick" None (Some "(MapMarker -> Action<'Msg>) option")) ]
    | NodeKind.Custom spec ->
        // Expose the bounded-escape declarations through the AI
        // introspection surface so `fuaran.getNodeState` shows whether a
        // Custom node has opted into hash verification / exposed-NodeIds
        // observability. The AI consumer can read this to decide whether
        // to address an exposed interior id with structural ops or to
        // treat the Custom body as opaque.
        let hashEntry =
            match spec.ContentHash with
            | Some h ->
                [ valueEntry "ContentHashAlgorithm" h.Algorithm
                  valueEntry "ContentHash" h.Hash
                  valueEntry "ContentHashStrictness" (sprintf "%A" h.Strictness) ]
            | None -> [ entry "ContentHash" None (Some "ContentHash option") ]

        // `ExposedNodeIds` is `string list option` since the swap — `None` ≡
        // the old empty list, so the introspection output is unchanged.
        let exposedIdStrings = spec.ExposedNodeIds |> Option.defaultValue []

        [ valueEntry "ModuleId" spec.ModuleId
          valueEntry "ComponentId" spec.ComponentId
          yield! hashEntry
          valueEntry "ExposedNodeIds" (String.concat ", " exposedIdStrings)
          valueEntry "ExposedNodeIdCount" (List.length exposedIdStrings) ]
    | NodeKind.ErrorBoundary spec ->
        // Surface the boundary's child + fallback
        // NodeIds so the AI introspection consumer can address them
        // structurally (e.g. `fuaran.getNodeState` on the fallback for
        // post-mortem inspection). No further scalar surface — both
        // halves are Node subtrees rather than typed-spec scalars.
        // `Node.Id` is a bare string since the swap — no unwrap needed.
        [ valueEntry "ChildNodeId" spec.Child.Id
          valueEntry "FallbackNodeId" spec.Fallback.Id ]
    | NodeKind.Switch spec ->
        // State-bound conditional child (Phase 392). Surface the state key that
        // selects the case, the ordered match values, and the default child's
        // NodeId so AI introspection can read "what does this switch key off,
        // and what can it show?". The case/default subtrees are addressable via
        // the console tree-walker; these entries are the scalar peek.
        // Phase 768 — the selector is any Binding; surface the State key
        // verbatim (the common case, name unchanged for tool consumers) and a
        // coarse tag for the rest.
        [ (match spec.On with
           | Binding.State(key, _) -> valueEntry "StateKey" key
           | Binding.Selection(nodeId, _, _, field) ->
               valueEntry
                   "On"
                   (sprintf "$selection.%s%s" nodeId (field |> Option.map (sprintf ".%s") |> Option.defaultValue ""))
           | Binding.Filter(name, _) -> valueEntry "On" (sprintf "$filter.%s" name)
           | _ -> valueEntry "On" "$binding")
          valueEntry "MatchValues" (spec.Cases |> List.map _.Match |> String.concat ", ")
          valueEntry "DefaultNodeId" spec.Default.Id ]
    | NodeKind.FragmentDecl spec ->
        // Expose the decl's fragment name + the
        // body root NodeId so AI introspection can address either. The
        // body itself is reachable via the standard tree-walker getChildren
        // arm; this entry is the "what's this decl called?" peek.
        // `FragmentDeclSpec.Name` is a bare string since the swap.
        [ valueEntry "FragmentName" spec.Name; valueEntry "BodyNodeId" spec.Body.Id ]
    | NodeKind.FragmentRef spec ->
        // The ref carries only its target name —
        // the body lives at the decl site. Surface as a single scalar so
        // `fuaran.getNodeState` shows "this ref points at fragment X".
        [ valueEntry "FragmentName" spec.Name ]
    | NodeKind.Mount spec ->
        // Isolation/embedding boundary (§4o). Surface the guest scope id, the
        // declared channel direction + optional message shape, and the
        // default-deny capability tags so AI introspection can read a mount's
        // policy posture. The guest interior is opaque here (its own scope);
        // OnBubble / Inputs are not scalar props.
        let directionStr =
            match spec.Channel.Direction with
            | ChannelDirection.OutOnly -> "OutOnly"
            | ChannelDirection.TwoWay -> "TwoWay"

        // `Capabilities` is a bare `string list` since the swap (the old
        // wrapper DU is deleted — Phase 694).
        let capabilityTags = spec.Capabilities

        [ valueEntry "ScopeId" spec.ScopeId
          valueEntry "ChannelDirection" directionStr
          (match spec.Channel.MessageShape with
           | Some s -> valueEntry "MessageShape" s
           | None -> entry "MessageShape" None (Some "string option"))
          valueEntry "Capabilities" (String.concat ", " capabilityTags)
          valueEntry "CapabilityCount" (List.length spec.Capabilities) ]

// ─── Include-key filtering ─────────────────────────────────────────────────

let private hasInclude (keys: IncludeKey list) (key: IncludeKey) : bool =
    List.isEmpty keys || List.contains key keys

// ─── getNodeState ──────────────────────────────────────────────────────────

/// `fuaran.getNodeState(nodeId, options?)` — returns the full per-node
/// observable state envelope. `includeKeys = []` returns every block
/// (matches the §4i default); a non-empty list filters to those blocks.
let getNodeState
    (ctx: IntrospectionContext)
    (includeKeys: IncludeKey list)
    (root: Node<'Msg>)
    (nodeId: NodeId)
    : Result<NodeState, IntrospectError> =
    match findNode nodeId root with
    | None -> Error(nodeNotFound nodeId)
    | Some node ->
        let props =
            if hasInclude includeKeys IncludeKey.Props then
                Some(extractProps node.Kind)
            else
                None

        let bindings =
            if hasInclude includeKeys IncludeKey.Bindings then
                Some(extractBindings ctx node.Kind)
            else
                None

        let probedState =
            if
                hasInclude includeKeys IncludeKey.CurrentState
                || hasInclude includeKeys IncludeKey.StateDetail
            then
                ctx.CurrentState.TryGetCurrentState nodeId
            else
                None

        let currentState =
            if hasInclude includeKeys IncludeKey.CurrentState then
                Some(
                    match probedState with
                    | Some(state, _) -> state
                    | None -> CurrentState.Normal
                )
            else
                None

        let stateDetail =
            if hasInclude includeKeys IncludeKey.StateDetail then
                match probedState with
                | Some(_, detail) -> detail
                | None -> None
            else
                None

        let geometry =
            if hasInclude includeKeys IncludeKey.Geometry then
                ctx.Geometry.TryGetGeometry nodeId
            else
                None

        Ok
            // `Node.Id` is a bare string since the swap; `NodeState.Id` keeps
            // the `NodeId` boundary type — wrap at the envelope.
            { Id = NodeId node.Id
              Kind = kindName node.Kind
              Props = props
              Bindings = bindings
              CurrentState = currentState
              StateDetail = stateDetail
              Geometry = geometry }

// ─── getBindingValue ───────────────────────────────────────────────────────

/// `fuaran.getBindingValue(nodeId, slot)` — returns the resolved value (or
/// typed BindingError) for a single Binding-typed slot. Returns
/// `SlotNotFound` IntrospectError when the slot isn't declared on the
/// node's kind; returns the resolution result (which may itself be a
/// BindingError) otherwise.
let getBindingValue
    (ctx: IntrospectionContext)
    (root: Node<'Msg>)
    (nodeId: NodeId)
    (slot: string)
    : Result<ResolvedBindingResult, IntrospectError> =
    match findNode nodeId root with
    | None -> Error(nodeNotFound nodeId)
    | Some node ->
        match extractSlot ctx node.Kind slot with
        | Some result -> Ok result
        | None -> Error(slotNotFound node.Kind nodeId slot)

// ─── getRenderedDom ────────────────────────────────────────────────────────

/// `fuaran.getRenderedDom(nodeId)` — returns the geometry tree rooted at the
/// addressed node. Child geometry mirrors the typed-tree children; each
/// node's `Geometry` is whatever `IGeometryProbe` returned for it (None
/// under the default probe). The structural tree is always populated;
/// geometry data is opportunistic.
let rec private geometryFor (ctx: IntrospectionContext) (node: Node<'Msg>) : GeometryTree =
    let children =
        match getChildren node.Kind with
        | None -> []
        | Some kids -> kids |> List.map (geometryFor ctx)

    // `Node.Id` is a bare string since the swap; the geometry tree + probe
    // keep the `NodeId` boundary type — wrap at the seam.
    { Id = NodeId node.Id
      Kind = kindName node.Kind
      Geometry = ctx.Geometry.TryGetGeometry(NodeId node.Id)
      Children = children }

let getRenderedDom
    (ctx: IntrospectionContext)
    (root: Node<'Msg>)
    (nodeId: NodeId)
    : Result<GeometryTree, IntrospectError> =
    match findNode nodeId root with
    | None -> Error(nodeNotFound nodeId)
    | Some node -> Ok(geometryFor ctx node)

// ─── getRuntimeErrors ──────────────────────────────────────────────────────

/// `fuaran.getRuntimeErrors(sinceTurn?)` — drain the runtime-error sink. The
/// orchestrator passes `None` at session start (every entry) or a turn
/// watermark on subsequent iterations (strictly-newer entries; entries
/// with `TurnId = None` are filtered out when sinceTurn is `Some _`).
let getRuntimeErrors (ctx: IntrospectionContext) (sinceTurn: int option) : ErrorEntry list = ctx.Errors.Drain sinceTurn

// ─── Convenience: record one error ─────────────────────────────────────────

/// Helper for diagnostics-side writers — increments a small entry-id
/// counter via the sink's `Cleared` flag is left to the sink's own
/// implementation. Callers needing strict monotonic ids supply their own
/// id generator; this helper uses a Guid-fragment per entry which is
/// stable per invocation but not globally ordered.
let recordError
    (ctx: IntrospectionContext)
    (code: string)
    (message: string)
    (nodeId: NodeId option)
    (turnId: int option)
    : unit =
    let id =
        // Fable-compatible short id — same shape Renderer.Render's
        // correlationId helper uses. Not used for ordering; the
        // orchestrator orders by Drain order + TurnId.
        System.Guid.NewGuid().ToString("N").Substring(0, 12)

    let entry =
        { Id = sprintf "err-%s" id
          TurnId = turnId
          Code = code
          Message = message
          NodeId = nodeId
          RecordedAt = ctx.Clock.Now() }

    ctx.Errors.Record entry
