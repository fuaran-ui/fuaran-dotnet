module Fuaran.UI.PreEmitValidate

// ============================================================================
//  Pre-emit tree-invariant checks for AI authors + human developers.
//
//  Fuaran's type system enforces *node-level* invariants (every Node has
//  required State + Style; every spec record's fields are typed). Two
//  invariants live above the type level — tree-wide identity uniqueness +
//  non-emptiness of identifier strings — and must be checked by walking
//  the tree. This module is the canonical walker.
//
//  Designed for both consumer paths:
//
//   1. **AI author pre-emit self-check** — before submitting a constructed
//      tree to the wire, the AI agent (or a planner harness) calls
//      `PreEmitValidate.validate` and fixes whatever the result reports.
//      Catches duplicate-NodeId collisions and empty identifier strings
//      cheaper than the wire-side `Apply` engine's `DuplicateNodeId` /
//      `PathInvalid` envelopes.
//
//   2. **Human-author test gate** — Expecto suites covering author-side
//      construction code call `validate` to assert the seed tree is
//      well-formed before exercising the renderer. Catches authoring
//      typos that the §4b type contract can't.
//
//  Fable-compatible — no reflection, no `obj` peek-through, no
//  `System.*` server-only API. Walks `Node<'Msg>.Kind` via direct
//  pattern match.
//
//  See `docs/AI_AUTHORING_GUIDE.md` § "Self-checking before you emit"
//  and `docs/ERROR_CODES.md` for the AI-side recovery patterns.
//
//  JSON-encoder counterpart (`JsonEncode.node : Node<'Msg> -> string`)
//  lives with op-stream persistence — it carries the same canonical-JSON
//  algorithm `OpStream` uses for hash chaining, so factoring it out keeps
//  the encoder definitions centralised. This module is the tree-shape half;
//  the wire-shape half ships with op-stream persistence.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// A pre-emit defect surfaced by `validate`. Stable, AI-friendly
/// discriminator (camelCase rendering at the docs boundary).
[<RequireQualifiedAccess>]
type PreEmitDefect =
    /// `id` appears as the `NodeId` of multiple nodes in the tree. Carries
    /// the offending id string and the observed count (≥ 2). The §4g op
    /// vocabulary depends on `NodeId` uniqueness — duplicate ids make
    /// `EditNode`, `RemoveNode`, `UpdateProp` ambiguous.
    | DuplicateNodeId of id: string * count: int
    /// A `NodeId` is `""` (empty string). The `NodeId` newtype permits
    /// the empty string at the F# type level, but the wire form requires
    /// a non-empty identifier so the orchestrator can address the node
    /// in subsequent turns.
    | EmptyNodeId
    /// A `Custom` node has an empty `moduleId` or `componentId` string.
    /// The wire-form `kind: "Custom"` envelope needs both to dispatch.
    | EmptyCustomKindIdentifier of moduleId: string * componentId: string
    /// **FUARAN047 (Error)**. A `TabsSpec` carries
    /// `TabHeaders = Some hs` whose length does not equal `Children.Length`.
    /// The renderer aligns headers 1:1 with children by index; mismatched
    /// lengths leave tabs without labels or labels without targets. Carries
    /// the offending `NodeId`, the header count, and the children count.
    | TabHeaderCountMismatch of nodeId: string * headerCount: int * childrenCount: int
    /// **FUARAN048 (Error)**. A `TabsSpec` carries
    /// `TabTags = Some ts` whose length does not equal `Children.Length`.
    /// The typed tag overlay maps tags to children by index; mismatched
    /// lengths break the tag → index round-trip.
    | TabTagCountMismatch of nodeId: string * tagCount: int * childrenCount: int
    /// **FUARAN049 (Warning)**. A `TabsSpec` carries
    /// `ActiveTag = Some _` but `TabTags = None`. The tag binding has
    /// nothing to resolve against; the renderer silently falls back to
    /// `ActiveIndex`, which is rarely what the author intended. Carries
    /// the offending `NodeId`.
    | TabActiveTagWithoutTags of nodeId: string
    /// **FUARAN070 (Error)**. A `Binding.Selection` names a `NodeId` absent
    /// from the tree — the dangling-edge check `Selection` never had (the
    /// filter-wiring record's symmetry gap, closed by Phase 427). The reader
    /// can never resolve: no node exists to produce (or be host-fed) a
    /// selection under that id. Carries the reading node's id and the missing
    /// target id. Fix: point the binding at the selection-producing node's id.
    | DanglingSelection of readerNodeId: string * target: string
    /// **FUARAN071 (Warning)**. A `Binding.Selection` targets a node that
    /// exists but is not a selection-PRODUCING kind (a `Visualisation` — the
    /// grid's Phase 427 default row-click write; charts/tables/maps via host
    /// closures). A host can still populate `BindingSources.Selections` for
    /// any id, but nothing in the tree will ever write it — usually a
    /// mis-targeted id. Carries the reading node's id and the target id.
    | SelectionOverNonProducer of readerNodeId: string * target: string
    /// **FUARAN074 (Warning)**. A declared filter chip (`FilterSpec.Name` on a
    /// `Filters` node) is consumed by NOTHING — no `Binding.Filter` read
    /// outside the declaring chip, no `Query.dependsOn` reference, no
    /// `Transform` param source (the 421/424 consumption union). A decorative
    /// filter: the user can set it and nothing changes. Carries the declaring
    /// node's id and the filter name. (A chip's own `Binding.Filter` self-read
    /// — the 423 declarative-chip shape — deliberately does NOT count.)
    | DecorativeFilter of declaringNodeId: string * name: string
    /// **FUARAN075 (Error)**. A DECLARED filter→consumer edge — a
    /// `Query.dependsOn` name, or a `Transform` param whose source is
    /// `Binding.Filter` — references a filter no `Filters` chip declares
    /// (Phases 421/424). The edge can never fire from the tree; usually a
    /// name typo. (A plain `Binding.Filter` VALUE read is exempt — a host may
    /// legitimately feed `BindingSources.Filters` without chips.) Carries the
    /// reading node's id and the undeclared filter name.
    | DanglingFilterReference of readerNodeId: string * name: string
    /// **FUARAN076 (Warning)**. A `Binding.Transform` declares a `params`
    /// entry whose name the pipeline never references (`Transform.paramsOf`) —
    /// dead weight, usually a rename that missed the pipeline or vice versa.
    /// Carries the reading node's id and the param name.
    | UnreferencedTransformParam of readerNodeId: string * name: string
    /// **FUARAN077 (Warning)**. A grid column carries NEITHER a `Value`
    /// closure NOR a declarative `Field` (Phase 425) — the column renders
    /// blank in every host. Always give a decoded column a `field`. Carries
    /// the grid node's id and the column label.
    | BlankGridColumn of nodeId: string * columnLabel: string
    /// **FUARAN078 (Warning)**. A `DataGrid` carries NEITHER a `RowKey`
    /// closure NOR a declarative `RowKeyField` (Phase 425) — no stable row
    /// identity, so the Phase 427 selected-row state and any keyed diffing
    /// degrade. Carries the grid node's id.
    | UnstableRowIdentity of nodeId: string
    /// **FUARAN072 (Warning)**. An `Action.Call` carries `into: Query <name>`
    /// but no `Binding.Query <name>` anywhere in the tree reads that slot —
    /// an orphan fetch: the response lands where nothing looks (Phase 428).
    /// Usually a name typo; point the target at the query name a reader
    /// binds, or bind a reader. Carries the calling node's id and the name.
    | OrphanQueryFetch of readerNodeId: string * queryName: string
    /// **FUARAN073 (Warning)**. An `Action.Call` has neither an `onResult`
    /// closure nor an `into` target — the response is dropped (Phase 428).
    /// Legitimate for command-style endpoints (fire-and-forget), hence a
    /// warning; add `into` when the response carries data a reader needs.
    /// Carries the calling node's id and the endpoint.
    | CallResultDropped of readerNodeId: string * endpoint: string
    /// **FUARAN069 (Warning)**. An interactive control's event handler is
    /// omitted (`None` — the declarative / Phase 426 write-back shape) but its
    /// value binding is **not** a writable store binding (directly
    /// `Binding.State` or `Binding.Filter`), so the control is inert: no
    /// closure dispatches and the renderer has no slot to write the change
    /// back to. Carries the owning `NodeId` and a short control descriptor
    /// (e.g. `"FormField(profile-name)"`, `"Select"`, `"Tabs.ActiveIndex"`).
    /// Fix: bind the control's value to `$state.<key>` / `$filters.<name>`,
    /// or supply the handler.
    | InertControl of nodeId: string * control: string
    /// **FUARAN085 (Warning)**. Two handler-free form fields write back to the
    /// SAME `$state` key (Phase 596 — with the symmetric auto-bind, an omitted
    /// `value` binds `$state.<field id>`, so duplicated field ids across forms,
    /// or an explicit `State` key reusing another field's id, silently alias:
    /// typing in one field overwrites the other's captured value). READERS of
    /// the key are the feature and are not flagged — only two WRITERS collide.
    /// Carries the state key and the colliding (nodeId, fieldId) pairs.
    | DuplicateWriteBackKey of stateKey: string * writers: (string * string) list
    /// **FUARAN068 (Error)**. A REGISTERED custom component's prop bag
    /// violates its declared `PropSchema` — a required prop is missing, or a
    /// present prop's shape doesn't match its declared `PropType`. Carries
    /// the offending `NodeId`, the component identity, and the per-prop
    /// defect list from `CustomRegistry.ValidateProps`. Surfaced only by
    /// `validateWithRegistry` (the plain `validate` has no registry to check
    /// against — an unregistered custom kind is a host trust-boundary
    /// concern, not a schema violation).
    | CustomPropSchemaViolation of
        nodeId: string *
        moduleId: string *
        componentId: string *
        propDefects: CustomPropDefect list
    /// **FUARAN082 (Error)**. A `NodeKind.Switch` carries two or more cases with
    /// the same `match` value (Phase 392). First-match-wins makes the later case
    /// dead — it can never render, so it is almost always an authoring mistake
    /// (a copy-paste that forgot to change the match). Carries the switch node's
    /// id and the duplicated match value. (Missing `default` / `stateKey` /
    /// `cases` are decode-time `MISSING_FIELD` rejects — a required field is
    /// always present on an in-memory tree, so there is no pre-emit advisory for
    /// them.)
    | DuplicateSwitchMatch of nodeId: string * matchValue: string
    /// **FUARAN083 (Warning)**. A `NodeKind.Switch` carries an empty `stateKey`
    /// (Phase 392) — the ungrounded-state-key defect. A switch reads its state
    /// key to select a case; an empty key can never resolve, so the switch is
    /// stuck on its `default` forever. The default-deny posture (Phase 12.Y):
    /// a state consumer must name a key. (The policy-manifest grounding of a key
    /// against an *allowed* set is the orchestration tier's concern, where that
    /// manifest lives; the language tier grounds by shape — a named key.) Carries
    /// the switch node's id.
    | UngroundedSwitchStateKey of nodeId: string
    /// **FUARAN086 (Error)**. A `ChartSpec` field reference (`XField` or a
    /// `YFields` entry) names a column absent from the chart's statically-known
    /// data schema (Phase 640). Today the schema is statically known for a
    /// `Binding.Transform` over an `Embedded` table with an EMPTY pipeline —
    /// the common inline-data chart. A non-empty pipeline (Derive/Project/
    /// GroupBy change the column set), a `Ref` source, a `Query`, or a host
    /// `Static` obj-seq is unknowable pre-emit and deliberately passes
    /// ungrounded (the fuaran-core#90 rule: only refuse what is PROVABLY
    /// wrong). Carries the chart node's id and the ungrounded field name.
    | ChartFieldUngrounded of nodeId: string * field: string
    /// **FUARAN087 (Error)**. A grounded chart value field (a `YFields` entry,
    /// or `XField` on a `Scatter`) carries a column type the lowering cannot
    /// plot numerically — anything outside int/float/bool (bool coerces 1/0)
    /// reads 0.0 at lowering time, a silently-flat series (Phase 640). Fires
    /// only when the schema is statically known (see FUARAN086). Carries the
    /// node id, the field, and the offending column-type tag.
    | ChartFieldTypeMismatch of nodeId: string * field: string * columnType: string
    /// **FUARAN088 (Error)**. A `Pie` chart carries other than exactly ONE
    /// `YFields` series (Phase 640/638). The pie lowering REFUSES multi-series
    /// geometry rather than silently truncating to the first series, so a
    /// multi-series (or zero-series) pie renders nothing — always an authoring
    /// mistake: plot one share column, or switch kind. Carries the node id and
    /// the series count.
    | ChartPieSeriesShape of nodeId: string * seriesCount: int
    /// **FUARAN089 (Warning)**. `Stacked = true` on a `ChartKind` where
    /// stacking is meaningless (`Line` / `Scatter` / `Pie`) — the lowering
    /// ignores the flag (Phase 637), so the emission carries dead intent;
    /// usually a kind switch that forgot the flag. Carries the node id and the
    /// kind name.
    | ChartStackedMeaningless of nodeId: string * kind: string

/// The shared walk behind `validate` / `validateWithRegistry`. `customCheck`
/// runs at every `NodeKind.Custom` (node id, moduleId, componentId, props) —
/// `validate` passes a no-op; `validateWithRegistry` passes the registry's
/// schema check. One walk, so the two entry points can never drift.
/// A binding the Phase 426 control write-back default can write to: directly
/// `Binding.State` (the renderer writes the StateStore slot) or
/// `Binding.Filter` (the FilterStore slot). Any other shape gives an omitted
/// handler nothing to write — the FUARAN069 inert-control condition. A
/// `Binding.Local` also counts as live: its Phase 62 commit pipeline carries
/// the change independently of the handler.
let private isWriteBackTarget (binding: Binding<'T>) : bool =
    match binding with
    | Binding.State _
    | Binding.Filter(_, None)
    | Binding.Local _ -> true
    | _ -> false

let private validateCore
    (customCheck: string -> string -> string -> Map<string, JVal> -> PreEmitDefect option)
    (node: Node<'Msg>)
    : Result<unit, PreEmitDefect list> =
    let defects = ResizeArray<PreEmitDefect>()
    // Phase 596 (FUARAN085) — (stateKey, nodeId, fieldId) per handler-free
    // form-field write-back; duplicates surface post-walk.
    let writeBackKeys = ResizeArray<string * string * string>()
    let nodeIdCounts = System.Collections.Generic.Dictionary<string, int>()

    let recordNodeId (NodeId raw) =
        if raw = "" then
            defects.Add PreEmitDefect.EmptyNodeId
        else
            match nodeIdCounts.TryGetValue raw with
            | true, n -> nodeIdCounts[raw] <- n + 1
            | false, _ -> nodeIdCounts[raw] <- 1

    let rec walk (n: Node<'Msg>) =
        recordNodeId n.Id
        // Per-kind: check kind-specific invariants + enumerate children.
        match n.Kind with
        | NodeKind.Layout layout ->
            match layout with
            | LayoutKind.Box spec -> spec.Children |> List.iter walk
            | LayoutKind.SplitPanel spec -> spec.Children |> List.iter walk
            | LayoutKind.Tabs spec ->
                // FUARAN047 / FUARAN048 / FUARAN049
                // tabs-shape invariants. Length mismatches are construction
                // defects (the renderer would silently drop headers or tags
                // past the children boundary); ActiveTag-without-TabTags
                // is a semantic mistake (the tag binding has nowhere to
                // resolve to). The `NodeId raw` extraction matches the
                // existing `recordNodeId` pattern.
                let (NodeId nodeIdStr) = n.Id
                let childrenCount = spec.Children.Length

                match spec.TabHeaders with
                | Some hs when hs.Length <> childrenCount ->
                    defects.Add(PreEmitDefect.TabHeaderCountMismatch(nodeIdStr, hs.Length, childrenCount))
                | _ -> ()

                match spec.TabTags with
                | Some ts when ts.Length <> childrenCount ->
                    defects.Add(PreEmitDefect.TabTagCountMismatch(nodeIdStr, ts.Length, childrenCount))
                | _ -> ()

                match spec.ActiveTag, spec.TabTags with
                | Some _, None -> defects.Add(PreEmitDefect.TabActiveTagWithoutTags nodeIdStr)
                | _ -> ()

                // FUARAN069 (Phase 426): tabs are live when either channel can
                // carry a click — a handler, or a writable slot the write-back
                // default targets (integer: ActiveIndex; tag overlay:
                // ActiveTag when TabTags is populated).
                let indexLive = spec.OnSelect.IsSome || isWriteBackTarget spec.ActiveIndex

                let tagLive =
                    match spec.OnSelectTag, spec.TabTags, spec.ActiveTag with
                    | Some _, Some _, _ -> true
                    | None, Some _, Some tagBinding -> isWriteBackTarget tagBinding
                    | _ -> false

                if not (indexLive || tagLive) then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Tabs"))

                spec.Children |> List.iter walk
            | LayoutKind.Stepper spec -> spec.Children |> List.iter walk
            | LayoutKind.SummaryList spec -> spec.Children |> List.iter walk
            | LayoutKind.Disclosure spec ->
                // FUARAN069 (Phase 426): no toggle handler and no writable
                // `Open` slot — the model never hears the native toggle.
                let (NodeId nodeIdStr) = n.Id

                if spec.OnToggle.IsNone && not (isWriteBackTarget spec.Open) then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Disclosure"))

                spec.Children |> List.iter walk
            | LayoutKind.Modal spec ->
                // FUARAN069 (Phase 426): a dismissable modal with no dismiss
                // action and no writable `Open` slot can never close.
                let (NodeId nodeIdStr) = n.Id

                if spec.Dismissable && spec.OnDismiss.IsNone && not (isWriteBackTarget spec.Open) then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Modal"))

                spec.Children |> List.iter walk
            | LayoutKind.ScrollArea spec -> spec.Children |> List.iter walk
        | NodeKind.Visualisation(VisKind.DataGrid spec) ->
            // FUARAN077 / FUARAN078 (Phase 425 follow-up): the declarative grid
            // display floor — every column needs a Value closure or a Field,
            // and the grid needs a RowKey closure or a RowKeyField for stable
            // row identity (the 427 selected-row state keys off it).
            let (NodeId nodeIdStr) = n.Id

            for col in spec.Columns do
                if col.Value.IsNone && col.Field.IsNone then
                    defects.Add(PreEmitDefect.BlankGridColumn(nodeIdStr, col.Label))

            if spec.RowKey.IsNone && spec.RowKeyField.IsNone then
                defects.Add(PreEmitDefect.UnstableRowIdentity nodeIdStr)
        | NodeKind.Display _ -> () // Display kinds are leaves; future kind-specific invariants (e.g. HeadingLevel ∈ [1..6]) land here.
        | NodeKind.Input input ->
            // FUARAN069 (Phase 426): an interactive input whose handler is
            // omitted needs a writable value binding for the write-back
            // default to target; anything else is an inert control. Filter
            // chips are exempt — a handler-free chip always writes its own
            // `$filters.<name>` (Phase 423), so it can never be inert.
            let (NodeId nodeIdStr) = n.Id

            let checkField (field: FormField<'Msg>) =
                // Phase 596 (FUARAN085): a handler-free field whose value is
                // directly `State(key, _)` WRITES that key — record it so
                // post-walk we can flag two writers on one key.
                let recordWriteBack (value: Binding<'v>) (handlerAbsent: bool) =
                    match value with
                    | Binding.State(key, _) when handlerAbsent -> writeBackKeys.Add(key, nodeIdStr, field.Id)
                    | _ -> ()

                (match field.Kind with
                 | FormFieldKind.Text(value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Number(value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Checkbox(value, ot) -> recordWriteBack value ot.IsNone
                 | FormFieldKind.Choice(_, value, oc) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.TextArea(value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.RangedNumber(value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Range(value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.SegmentedChoice(_, value, oc, _) -> recordWriteBack value oc.IsNone
                 | FormFieldKind.Date(value, oc, _, _) -> recordWriteBack value oc.IsNone)

                let inert =
                    match field.Kind with
                    | FormFieldKind.Text(value, oc) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.Number(value, oc) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.Checkbox(value, ot) -> ot.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.Choice(_, value, oc) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.TextArea(value, oc, _) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.RangedNumber(value, oc, _) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.Range(value, oc, _) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.SegmentedChoice(_, value, oc, _) -> oc.IsNone && not (isWriteBackTarget value)
                    | FormFieldKind.Date(value, oc, _, _) -> oc.IsNone && not (isWriteBackTarget value)

                if inert then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, sprintf "FormField(%s)" field.Id))

            match input with
            | InputKind.Form spec -> spec.Fields |> List.iter checkField
            | InputKind.Select spec ->
                if spec.Multiple then
                    let valuesLive =
                        match spec.Values with
                        | Some values -> isWriteBackTarget values
                        | None -> false

                    if spec.OnChangeMulti.IsNone && not valuesLive then
                        defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Select(multiple)"))
                elif spec.OnChange.IsNone && not (isWriteBackTarget spec.Value) then
                    defects.Add(PreEmitDefect.InertControl(nodeIdStr, "Select"))
            | InputKind.Filters _
            | InputKind.Button _
            | InputKind.FileUpload _ -> ()
        | NodeKind.Visualisation(VisKind.Chart spec) ->
            // FUARAN086–089 (Phase 640): schema-grounded chart validation. An
            // ungrounded field reference is the LANGUAGE's defect to catch
            // before lowering — a wrong field name otherwise lowers to a
            // silently flat/empty chart.
            let (NodeId nodeIdStr) = n.Id

            let kindName =
                match spec.Kind with
                | ChartKind.Line -> "Line"
                | ChartKind.Bar -> "Bar"
                | ChartKind.Area -> "Area"
                | ChartKind.Pie -> "Pie"
                | ChartKind.Scatter -> "Scatter"
                | ChartKind.Heatmap -> "Heatmap"

            // FUARAN088 — pie needs exactly one series (the 638 lowering
            // refuses multi-series geometry rather than truncating).
            (match spec.Kind with
             | ChartKind.Pie when spec.YFields.Length <> 1 ->
                 defects.Add(PreEmitDefect.ChartPieSeriesShape(nodeIdStr, spec.YFields.Length))
             | _ -> ())

            // FUARAN089 — Stacked is dead intent outside Bar/Area.
            (match spec.Kind with
             | ChartKind.Line
             | ChartKind.Scatter
             | ChartKind.Pie when spec.Stacked ->
                 defects.Add(PreEmitDefect.ChartStackedMeaningless(nodeIdStr, kindName))
             | _ -> ())

            // FUARAN086/087 — grounding, only where the schema is statically
            // known: an Embedded table with an EMPTY pipeline (a non-empty
            // pipeline changes the column set — Derive adds, Project/GroupBy
            // remove — and no static output-schema derivation exists yet, so
            // it deliberately passes ungrounded rather than false-positive).
            (match spec.Source with
             | Binding.Transform(DataSource.Embedded table, [], _) ->
                 let colType (name: string) : ColumnType option =
                     table.Schema |> List.tryFind (fun (c, _) -> c = name) |> Option.map snd

                 let numeric (t: ColumnType) : bool =
                     match t with
                     | ColumnType.IntType
                     | ColumnType.FloatType
                     | ColumnType.BoolType -> true
                     | _ -> false

                 (match colType spec.XField with
                  | None -> defects.Add(PreEmitDefect.ChartFieldUngrounded(nodeIdStr, spec.XField))
                  | Some t ->
                      match spec.Kind with
                      | ChartKind.Scatter when not (numeric t) ->
                          defects.Add(PreEmitDefect.ChartFieldTypeMismatch(nodeIdStr, spec.XField, ColumnType.tag t))
                      | _ -> ())

                 for yf in spec.YFields do
                     match colType yf with
                     | None -> defects.Add(PreEmitDefect.ChartFieldUngrounded(nodeIdStr, yf))
                     | Some t ->
                         if not (numeric t) then
                             defects.Add(PreEmitDefect.ChartFieldTypeMismatch(nodeIdStr, yf, ColumnType.tag t))
             | _ -> ())
        | NodeKind.Visualisation _ -> () // The other visualisations are leaves with no pre-emit invariants yet.
        | NodeKind.Custom(moduleId, componentId, props, _, _) ->
            if moduleId = "" || componentId = "" then
                defects.Add(PreEmitDefect.EmptyCustomKindIdentifier(moduleId, componentId))

            let (NodeId rawId) = n.Id
            customCheck rawId moduleId componentId props |> Option.iter defects.Add
        | NodeKind.ErrorBoundary spec ->
            // The boundary's `Child` + `Fallback`
            // subtrees both participate in the tree-wide NodeId uniqueness
            // check + empty-id surface. Nested boundaries are permitted —
            // each inner boundary's child + fallback walks normally. No
            // boundary-specific defect at v1 (the AI may legitimately emit
            // structurally identical child + fallback shapes during
            // exploratory authoring).
            walk spec.Child
            walk spec.Fallback
        | NodeKind.Switch spec ->
            let (NodeId nodeIdStr) = n.Id

            // FUARAN083 (Phase 392): an empty state key is ungrounded — the
            // switch can never resolve a case, so it is stuck on `default`.
            if spec.StateKey = "" then
                defects.Add(PreEmitDefect.UngroundedSwitchStateKey nodeIdStr)

            // FUARAN082 (Phase 392): duplicate `match` values make the later
            // case dead (first-match-wins). Report each duplicated value once.
            let seen = System.Collections.Generic.HashSet<string>()
            let reported = System.Collections.Generic.HashSet<string>()

            for (matchValue, _) in spec.Cases do
                if not (seen.Add matchValue) && reported.Add matchValue then
                    defects.Add(PreEmitDefect.DuplicateSwitchMatch(nodeIdStr, matchValue))

            // The case children + default participate in the tree-wide NodeId
            // uniqueness + empty-id surface, so walk them all.
            spec.Cases |> List.iter (fun (_, child) -> walk child)
            walk spec.Default
        | NodeKind.FragmentDecl spec ->
            // The decl's `Body` participates in the
            // tree-wide NodeId uniqueness check. Note that uniqueness here
            // is *pre-expansion* — at render time the renderer namespaces
            // interior ids by the ref's id, so the same body referenced by
            // two refs produces DOM-unique ids without an authoring
            // duplicate. Name-level uniqueness + unresolved/cyclic ref
            // checks are AST-walk concerns and live in the validator
            // (FUARAN056 / FUARAN057 / FUARAN058).
            walk spec.Body
        | NodeKind.FragmentRef _ -> ()
        // Mount (§4o) is an opaque isolation boundary — the guest interior is
        // a separate scope with its own id space, produced host-side by the
        // guest loader, so it is not walked into the host tree's NodeId
        // uniqueness check (same posture as FragmentRef). The mount node's own
        // id was already recorded via `recordNodeId n.Id`.
        | NodeKind.Mount _ -> ()

    walk node

    // Collect every id observed ≥ 2 times.
    for KeyValue(id, count) in nodeIdCounts do
        if count >= 2 then
            defects.Add(PreEmitDefect.DuplicateNodeId(id, count))

    // ── Cross-tree binding checks (Phase 427; the BindingWalk facts) ──
    // FUARAN070 / FUARAN071: every `Binding.Selection` read must target an
    // existing node (error), and that node should be a selection-producing
    // kind (warn) — `Binding.Selection` reaches parity with the declared-edge
    // checks the filter channel got in 421/424.
    let facts = BindingWalk.collect node

    for u in facts.Uses do
        match u.Use with
        | BindingWalk.BindingUse.Selection target ->
            match Map.tryFind target facts.Nodes with
            | None -> defects.Add(PreEmitDefect.DanglingSelection(u.Reader, target))
            | Some isProducer ->
                if not isProducer then
                    defects.Add(PreEmitDefect.SelectionOverNonProducer(u.Reader, target))
        | _ -> ()

    // FUARAN072 / FUARAN073: every wire-survivable `Action.Call` either
    // dispatches through a closure, or lands its response where a reader
    // looks (Phase 428).
    let readQueryNames =
        facts.Uses
        |> List.choose (fun u ->
            match u.Use with
            | BindingWalk.BindingUse.Query(name, _) -> Some name
            | _ -> None)
        |> Set.ofList

    for c in facts.Calls do
        match c.HasOnResult, c.Into with
        | false, None -> defects.Add(PreEmitDefect.CallResultDropped(c.Reader, c.Endpoint))
        | _, Some(CallResultTarget.IntoQuery name) when not (Set.contains name readQueryNames) ->
            defects.Add(PreEmitDefect.OrphanQueryFetch(c.Reader, name))
        | _ -> ()

    // ── The 421/424 filter consumption union (the consolidated deferral) ──
    // FUARAN074: a declared chip nothing consumes (a `Binding.Filter` read
    // outside the declaring chip, a `Query.dependsOn` reference, or a
    // `Transform` param source each count). FUARAN075: a DECLARED edge
    // (`dependsOn` / a param's Filter source) naming a filter no chip
    // declares. FUARAN076: a `params` entry the pipeline never references.
    let declaredFilterNames = facts.DeclaredFilters |> List.map snd |> Set.ofList

    for (ownerNodeId, name) in facts.DeclaredFilters do
        let consumed =
            facts.Uses
            |> List.exists (fun u ->
                match u.Use with
                | BindingWalk.BindingUse.Filter n -> n = name && u.Reader <> ownerNodeId
                | BindingWalk.BindingUse.TransformParamFilter n -> n = name
                | BindingWalk.BindingUse.Query(_, dependsOn) -> List.contains name dependsOn
                | _ -> false)

        if not consumed then
            defects.Add(PreEmitDefect.DecorativeFilter(ownerNodeId, name))

    for u in facts.Uses do
        match u.Use with
        | BindingWalk.BindingUse.TransformParamFilter n when not (Set.contains n declaredFilterNames) ->
            defects.Add(PreEmitDefect.DanglingFilterReference(u.Reader, n))
        | BindingWalk.BindingUse.Query(_, dependsOn) ->
            for n in dependsOn do
                if not (Set.contains n declaredFilterNames) then
                    defects.Add(PreEmitDefect.DanglingFilterReference(u.Reader, n))
        | BindingWalk.BindingUse.TransformParam(n, false) ->
            defects.Add(PreEmitDefect.UnreferencedTransformParam(u.Reader, n))
        | _ -> ()

    // FUARAN085 — two handler-free write-back writers on one state key.
    writeBackKeys
    |> Seq.groupBy (fun (key, _, _) -> key)
    |> Seq.iter (fun (key, writers) ->
        let ws = writers |> Seq.map (fun (_, nid, fid) -> nid, fid) |> List.ofSeq

        if ws.Length > 1 then
            defects.Add(PreEmitDefect.DuplicateWriteBackKey(key, ws)))

    if defects.Count = 0 then
        Ok()
    else
        Error(List.ofSeq defects)

/// Walk `node` (depth-first, pre-order) and surface every pre-emit defect.
/// Returns `Ok ()` on a clean tree; `Error defects` carries every defect
/// found (NOT short-circuited on the first one) so the AI can repair the
/// tree in a single turn rather than discovering defects one at a time.
let validate (node: Node<'Msg>) : Result<unit, PreEmitDefect list> = validateCore (fun _ _ _ _ -> None) node

/// `validate` + custom-prop schema enforcement (**FUARAN068**): every
/// `NodeKind.Custom` whose `(moduleId, componentId)` is registered has its
/// prop bag checked against the declared `PropSchema` via
/// `CustomRegistry.ValidateProps` — the shipped path that makes a registered
/// custom kind validated like a built-in, instead of a surface the host must
/// remember to call separately. An UNregistered custom kind passes untouched
/// (the registry only speaks for what it knows); a host with no registry
/// keeps calling the plain `validate`.
let validateWithRegistry (registry: CustomRegistry) (node: Node<'Msg>) : Result<unit, PreEmitDefect list> =
    validateCore
        (fun nodeId moduleId componentId props ->
            match registry.ValidateProps(moduleId, componentId, props) with
            | [] -> None
            | propDefects -> Some(PreEmitDefect.CustomPropSchemaViolation(nodeId, moduleId, componentId, propDefects)))
        node
