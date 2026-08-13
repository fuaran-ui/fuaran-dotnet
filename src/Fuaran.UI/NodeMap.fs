module Fuaran.UI.NodeMap

// ============================================================================
//  Node.mapMsg — structural relabel of a Node tree's message type (Phase 268).
//
//  `mapMsg : ('a -> 'b) -> Node<'a> -> Node<'b>` rewrites EVERY `Action<'a>`
//  slot across every `NodeKind` / spec into an `Action<'b>`, recursing through
//  the whole tree. It is the erasure primitive `embed` needs: `mapMsg box`
//  turns a typed child `Node<'sub>` into the `Node<obj>` a `NodeKind.Mount`
//  guest requires (the renderer's Mount arm is obj-typed; polymorphic recursion
//  is forbidden at the boundary, and an unsafe `box |> unbox<Node<obj>>` cast
//  throws under .NET's reified generics — so a genuine structural map is the
//  only portable option, and it must run on both the Fable and .NET pipelines).
//
//  DESIGN NOTES
//
//   * Pure + FSharp.Core-only (FGP 2). No renderer / orchestration dependency;
//     this is a language-tier combinator over the §4b type contract.
//
//   * `'Msg` lives ONLY in `Node` / `NodeKind` / `Action` and the spec records
//     that carry an `Action<'Msg>`, a `Node<'Msg>`, or a function returning one.
//     `Binding<'T>` is `'Msg`-FREE (its `Local.OnCommit : 'T -> obj` returns
//     `obj`, never `'Msg`), so every `Binding`-typed field passes through
//     untouched. `Accessibility`, `SemanticStyle`, `Motion`, and every
//     `DisplayKind` spec are likewise `'Msg`-free — but the DUs that carry them
//     are generic in `'Msg` (a phantom parameter), so each case is still
//     reconstructed to change the type argument.
//
//   * Action-bearing functions map by COMPOSITION: an `onChange : string ->
//     Action<'a>` becomes `onChange >> mapAction f`; an `OnBubble : obj ->
//     Action<'a>` composes the same way; `Action.Call`'s `onResult : obj -> 'a`
//     becomes `onResult >> f`; `ReadFileBody`'s `onRead : string -> 'a` becomes
//     `onRead >> f`; a `CellKindErased.Custom`'s tree-producing closure
//     `render : (obj -> JVal) -> Node<'a>` becomes `render >> mapMsg f`.
//
//   * EXHAUSTIVE by construction — every match has NO wildcard, so adding a new
//     `NodeKind` / `Action` / spec case surfaces here as an FS0025 incomplete-
//     match error (a build failure under the warnings-as-errors gate) rather
//     than silently escaping the relabel. The tree only ever stores the erased
//     grid shapes (`GridSpec` / `ColumnErased` / `CellKindErased`), never the
//     typed author facade (`GridSpecOf` / `Column` / `CellKind`), so those are
//     intentionally out of scope.
// ============================================================================

open Fuaran.UI.Types

/// Relabel an `Action<'a>` to `Action<'b>`, rewriting every `'a` payload and
/// every function that returns an `'a` / `Action<'a>` by composition.
let rec mapAction (f: 'a -> 'b) (action: Action<'a>) : Action<'b> =
    match action with
    | Action.Dispatch msg -> Action.Dispatch(f msg)
    | Action.Call(endpoint, onResult, into) ->
        // `onResult` is optional (Phase 428) — map through the Option; the
        // declarative `into` target is 'Msg-free data and passes through.
        Action.Call(endpoint, onResult |> Option.map (fun g -> g >> f), into)
    | Action.Notify(channel, payload) -> Action.Notify(channel, payload)
    | Action.Navigate route -> Action.Navigate route
    | Action.SetState(key, value, valueFrom) -> Action.SetState(key, value, valueFrom)
    | Action.AiTool(toolName, args) -> Action.AiTool(toolName, args)
    | Action.Chain actions -> Action.Chain(actions |> List.map (mapAction f))
    | Action.CommitLocal nodeId -> Action.CommitLocal nodeId
    | Action.WriteToClipboard text -> Action.WriteToClipboard text
    | Action.ReadFileBody(fileRef, fileHandle, encoding, onRead) ->
        Action.ReadFileBody(fileRef, fileHandle, encoding, onRead |> Option.map (fun r -> r >> f))
    | Action.Invoke(capabilityId, args) -> Action.Invoke(capabilityId, args)

/// Relabel a whole `Node<'a>` to `Node<'b>`. The `'Msg`-free traits (`Style`,
/// `Accessibility`, `Motion`, `ExtraAttributes`) pass through; `Kind` + `State`
/// recurse.
let rec mapMsg (f: 'a -> 'b) (node: Node<'a>) : Node<'b> =
    { Id = node.Id
      Kind = mapKind f node.Kind
      State = node.State |> Option.map (mapState f)
      Style = node.Style
      Accessibility = node.Accessibility
      Motion = node.Motion
      ExtraAttributes = node.ExtraAttributes }

and mapState (f: 'a -> 'b) (state: StateBehaviour<'a>) : StateBehaviour<'b> =
    { OnLoading = state.OnLoading |> Option.map (mapMsg f)
      OnEmpty = state.OnEmpty |> Option.map (mapMsg f)
      OnError = state.OnError |> Option.map (fun render -> render >> mapMsg f) }

and mapKind (f: 'a -> 'b) (kind: NodeKind<'a>) : NodeKind<'b> =
    let mapChildren = List.map (mapMsg f)
    // Every `DisplayKind` case is `'Msg`-free (pure presentation); `_f` is
    // unused — each case is reconstructed only to change the phantom `'Msg`.

    match kind with
    // ── Layout ──
    | NodeKind.Box spec ->
        NodeKind.Box
            { Layout = spec.Layout
              Role = spec.Role
              Heading = spec.Heading
              Children = mapChildren spec.Children }
    | NodeKind.SplitPanel spec ->
        NodeKind.SplitPanel
            { Weight = spec.Weight
              Children = mapChildren spec.Children }
    | NodeKind.Tabs spec ->
        // `OnSelect` is optional (Phase 426) — map through the Option, so a
        // `None` (declarative / write-back) shape stays `None`.
        NodeKind.Tabs
            { Children = mapChildren spec.Children
              ActiveIndex = spec.ActiveIndex
              OnSelect = spec.OnSelect |> Option.map (fun g -> g >> mapAction f)
              TabHeaders = spec.TabHeaders
              TabTags = spec.TabTags
              ActiveTag = spec.ActiveTag
              OnSelectTag = spec.OnSelectTag |> Option.map (fun g -> g >> mapAction f)
              Orientation = spec.Orientation }
    | NodeKind.Stepper spec ->
        NodeKind.Stepper
            { ActiveStep = spec.ActiveStep
              Children = mapChildren spec.Children
              OnSelect = spec.OnSelect |> Option.map (fun g -> g >> mapAction f) }
    | NodeKind.SummaryList spec ->
        NodeKind.SummaryList
            { Heading = spec.Heading
              Children = mapChildren spec.Children }
    | NodeKind.Disclosure spec ->
        NodeKind.Disclosure
            { Heading = spec.Heading
              Open = spec.Open
              OnToggle = spec.OnToggle |> Option.map (fun g -> g >> mapAction f)
              Children = mapChildren spec.Children
              DefaultOpen = spec.DefaultOpen }
    | NodeKind.Modal spec ->
        NodeKind.Modal
            { Open = spec.Open
              Heading = spec.Heading
              Dismissable = spec.Dismissable
              Children = mapChildren spec.Children
              OnDismiss = spec.OnDismiss |> Option.map (mapAction f) }
    | NodeKind.ScrollArea spec ->
        NodeKind.ScrollArea
            { Orientation = spec.Orientation
              Children = mapChildren spec.Children
              MaxHeight = spec.MaxHeight
              MaxWidth = spec.MaxWidth }
    // ── Display ──
    | NodeKind.Heading spec -> NodeKind.Heading spec
    | NodeKind.Markdown spec -> NodeKind.Markdown spec
    | NodeKind.Metric spec -> NodeKind.Metric spec
    | NodeKind.Badge spec -> NodeKind.Badge spec
    | NodeKind.Sparkline spec -> NodeKind.Sparkline spec
    | NodeKind.Callout spec -> NodeKind.Callout spec
    | NodeKind.Progress spec -> NodeKind.Progress spec
    | NodeKind.Skeleton spec -> NodeKind.Skeleton spec
    | NodeKind.Icon spec -> NodeKind.Icon spec
    | NodeKind.LabelValueRow spec -> NodeKind.LabelValueRow spec
    | NodeKind.Fact spec -> NodeKind.Fact spec
    | NodeKind.Link spec -> NodeKind.Link spec
    | NodeKind.Image spec -> NodeKind.Image spec
    | NodeKind.List spec -> NodeKind.List spec
    | NodeKind.Toast spec -> NodeKind.Toast spec
    | NodeKind.CodeBlock spec -> NodeKind.CodeBlock spec
    | NodeKind.Math spec -> NodeKind.Math spec
    | NodeKind.Drawing spec -> NodeKind.Drawing spec
    // ── Input ──
    | NodeKind.Form spec -> NodeKind.Form(mapFormSpec f spec)
    | NodeKind.Filters spec -> NodeKind.Filters { Items = spec.Items |> List.map (mapFilterSpec f) }
    | NodeKind.Button spec -> NodeKind.Button(mapButtonSpec f spec)
    | NodeKind.FileUpload spec -> NodeKind.FileUpload(mapFileUploadSpec f spec)
    | NodeKind.Select spec -> NodeKind.Select(mapSelectSpec f spec)
    // ── Visualisation ──
    | NodeKind.DataGrid spec -> NodeKind.DataGrid(mapGridSpec f spec)
    | NodeKind.Chart spec -> NodeKind.Chart(mapChartSpec f spec)
    | NodeKind.Map spec -> NodeKind.Map(mapMapSpec f spec)
    | NodeKind.Custom spec ->
        // `CustomSpec` is non-generic since the swap — no `'Msg` to relabel.
        NodeKind.Custom spec
    | NodeKind.ErrorBoundary spec ->
        NodeKind.ErrorBoundary
            { Child = mapMsg f spec.Child
              Fallback = mapMsg f spec.Fallback }
    | NodeKind.Switch spec ->
        NodeKind.Switch
            { On = spec.On
              Cases =
                spec.Cases
                |> List.map (fun c ->
                    { Match = c.Match
                      Child = mapMsg f c.Child })
              Default = mapMsg f spec.Default }
    | NodeKind.FragmentDecl spec ->
        NodeKind.FragmentDecl
            { Name = spec.Name
              Body = mapMsg f spec.Body
              Holes = spec.Holes
              Effect = spec.Effect }
    | NodeKind.FragmentRef spec ->
        NodeKind.FragmentRef
            { Name = spec.Name
              Args = spec.Args |> Option.map (Map.map (fun _ arg -> mapFragmentArg f arg)) }
    | NodeKind.Mount spec ->
        NodeKind.Mount
            { ScopeId = spec.ScopeId
              Inputs = spec.Inputs |> Option.map (Map.map (fun _ arg -> mapFragmentArg f arg))
              Channel = spec.Channel
              OnBubble = spec.OnBubble |> Option.map (fun g -> g >> mapAction f)
              Capabilities = spec.Capabilities }

and mapFragmentArg (f: 'a -> 'b) (arg: FragmentArg<'a>) : FragmentArg<'b> =
    match arg with
    | FragmentArg.Int v -> FragmentArg.Int v
    | FragmentArg.Float v -> FragmentArg.Float v
    | FragmentArg.Bool v -> FragmentArg.Bool v
    | FragmentArg.Str v -> FragmentArg.Str v
    | FragmentArg.SlotArg node -> FragmentArg.SlotArg(mapMsg f node)

// ─── Layouts — every case carries `Children`; some carry Action handlers ────


// ─── Display — every case is `'Msg`-free; reconstructed to change the phantom ─
and mapButtonSpec (f: 'a -> 'b) (spec: ButtonSpec<'a>) : ButtonSpec<'b> =
    { Label = spec.Label
      OnClick = mapAction f spec.OnClick
      Variant = spec.Variant
      Icon = spec.Icon
      Tooltip = spec.Tooltip
      Disabled = spec.Disabled }

and mapSelectSpec (f: 'a -> 'b) (spec: SelectSpec<'a>) : SelectSpec<'b> =
    { Label = spec.Label
      Source = spec.Source
      Value = spec.Value
      OnChange = spec.OnChange |> Option.map (fun g -> g >> mapAction f)
      Placeholder = spec.Placeholder
      Disabled = spec.Disabled
      Multiple = spec.Multiple
      Values = spec.Values
      OnChangeMulti = spec.OnChangeMulti |> Option.map (fun g -> g >> mapAction f) }

and mapFormSpec (f: 'a -> 'b) (spec: FormSpec<'a>) : FormSpec<'b> =
    { Fields = spec.Fields |> List.map (mapFormField f)
      OnSubmit = mapAction f spec.OnSubmit
      SubmitLabel = spec.SubmitLabel
      Disabled = spec.Disabled }

and mapFormField (f: 'a -> 'b) (field: FormField<'a>) : FormField<'b> =
    { Id = field.Id
      Label = field.Label
      Kind = mapFormFieldKind f field.Kind
      Required = field.Required
      Help = field.Help }

and mapFormFieldKind (f: 'a -> 'b) (kind: FormFieldKind<'a>) : FormFieldKind<'b> =
    // Handlers are optional (Phase 426) — map through the Option, so a `None`
    // (declarative / write-back) field stays `None` and a `Some` closure is
    // remapped exactly as before (the Phase 423 `mapFilterKind` treatment).
    let mapHandler g =
        Option.map (fun h -> h >> mapAction f) g

    match kind with
    | FormFieldKind.Text(value, onChange) -> FormFieldKind.Text(value, mapHandler onChange)
    | FormFieldKind.Number(value, onChange) -> FormFieldKind.Number(value, mapHandler onChange)
    | FormFieldKind.Checkbox(value, onToggle) -> FormFieldKind.Checkbox(value, mapHandler onToggle)
    | FormFieldKind.Toggle(value, onToggle) -> FormFieldKind.Toggle(value, mapHandler onToggle)
    | FormFieldKind.Choice(options, value, onChange) -> FormFieldKind.Choice(options, value, mapHandler onChange)
    | FormFieldKind.TextArea(value, onChange, rows) -> FormFieldKind.TextArea(value, mapHandler onChange, rows)
    | FormFieldKind.RangedNumber(value, onChange, mn, mx, st) ->
        FormFieldKind.RangedNumber(value, mapHandler onChange, mn, mx, st)
    | FormFieldKind.Range(value, onChange, mn, mx, st) -> FormFieldKind.Range(value, mapHandler onChange, mn, mx, st)
    | FormFieldKind.SegmentedChoice(options, value, onChange, orientation) ->
        FormFieldKind.SegmentedChoice(options, value, mapHandler onChange, orientation)
    | FormFieldKind.Date(value, onChange, variant, mn, mx, st) ->
        FormFieldKind.Date(value, mapHandler onChange, variant, mn, mx, st)
    | FormFieldKind.DateRange(value, onChange, variant, mn, mx, st) ->
        FormFieldKind.DateRange(value, mapHandler onChange, variant, mn, mx, st)

and mapFilterSpec (f: 'a -> 'b) (spec: FilterSpec<'a>) : FilterSpec<'b> =
    // 0.2.0 filters-unification: the chip's control is an ordinary
    // FormFieldKind — one mapper serves both forms and filter strips.
    { Name = spec.Name
      Label = spec.Label
      Kind = mapFormFieldKind f spec.Kind }

and mapFileUploadSpec (f: 'a -> 'b) (spec: FileUploadSpec<'a>) : FileUploadSpec<'b> =
    { Label = spec.Label
      Accept = spec.Accept
      Multiple = spec.Multiple
      OnSelect = spec.OnSelect |> Option.map (fun h -> h >> mapAction f)
      Disabled = spec.Disabled }

// ─── Visualisations — data-bound; the tree stores the erased grid shapes ────

and mapGridSpec (f: 'a -> 'b) (spec: GridSpec<'a>) : GridSpec<'b> =
    { Source = spec.Source
      RowKey = spec.RowKey
      RowKeyField = spec.RowKeyField
      SortStateKey = spec.SortStateKey
      Columns = spec.Columns |> List.map (mapColumnErased f)
      OnRowClick = spec.OnRowClick |> Option.map (fun g -> g >> mapAction f)
      Editable = spec.Editable
      // Phase 393 — `TextSource` static rows are 'Msg-free, copied verbatim.
      StaticRows = spec.StaticRows }

and mapColumnErased (f: 'a -> 'b) (col: ColumnErased<'a>) : ColumnErased<'b> =
    { Label = col.Label
      Value = col.Value
      Field = col.Field
      Format = col.Format
      Kind = mapCellKindErased f col.Kind
      Width = col.Width }

and mapCellKindErased (f: 'a -> 'b) (kind: CellKindErased<'a>) : CellKindErased<'b> =
    match kind with
    | CellKindErased.Text -> CellKindErased.Text
    | CellKindErased.Numeric -> CellKindErased.Numeric
    | CellKindErased.Date -> CellKindErased.Date
    | CellKindErased.Editable onEdit -> CellKindErased.Editable(onEdit |> Option.map (fun g -> g >> mapAction f))
    | CellKindErased.Checkbox(get, onToggle) ->
        CellKindErased.Checkbox(get, onToggle |> Option.map (fun g -> g >> mapAction f))
    | CellKindErased.Button(label, onClick) ->
        CellKindErased.Button(label, onClick |> Option.map (fun g -> g >> mapAction f))
    | CellKindErased.ButtonGroup items ->
        CellKindErased.ButtonGroup(
            items
            |> List.map (fun item ->
                { Label = item.Label
                  OnClick = item.OnClick |> Option.map (fun g -> g >> mapAction f) })
        )
    | CellKindErased.Link(href, label) -> CellKindErased.Link(href, label)
    | CellKindErased.Pill(label, tone) -> CellKindErased.Pill(label, tone)
    // Phase 750 — the declarative pill is pure data ('Msg-free), copied verbatim.
    | CellKindErased.TonedPill(field, toneMap, defaultTone) -> CellKindErased.TonedPill(field, toneMap, defaultTone)
    | CellKindErased.Progress(fraction, label) -> CellKindErased.Progress(fraction, label)
    | CellKindErased.Custom render -> CellKindErased.Custom(render >> mapMsg f)

and mapChartSpec (f: 'a -> 'b) (spec: ChartSpec<'a>) : ChartSpec<'b> =
    { Source = spec.Source
      Kind = spec.Kind
      XField = spec.XField
      YFields = spec.YFields
      Title = spec.Title
      OnPointClick = spec.OnPointClick |> Option.map (fun g -> g >> mapAction f)
      Stacked = spec.Stacked }

and mapMapSpec (f: 'a -> 'b) (spec: MapSpec<'a>) : MapSpec<'b> =
    { Source = spec.Source
      CentreLatitude = spec.CentreLatitude
      CentreLongitude = spec.CentreLongitude
      Zoom = spec.Zoom
      OnMarkerClick = spec.OnMarkerClick |> Option.map (fun g -> g >> mapAction f) }
