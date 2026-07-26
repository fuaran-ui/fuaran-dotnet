module Fuaran.UI.Ops.Apply

// ============================================================================
//  Fuaran tree-op apply engine — main entry point.
//
//  apply : TreeOp<'Msg> -> Node<'Msg> -> Result<Node<'Msg>, ApplyError>
//
//  Sequential within a turn (§4g lines 951–957): callers fold across an op
//  list themselves, or wrap the list in `TreeOp.Batch` to get all-or-nothing
//  atomicity. The engine never partially mutates a tree — on any error path,
//  the input tree is returned unchanged via the Result.Error branch (the
//  caller's reference is unchanged, so "revert" is implicit).
//
//  Per-op dispatch tables live below. Adding a new NodeKind requires
//  updating:
//   - `Introspect.kindName`            (always)
//   - `Introspect.availableFields`     (always)
//   - `Introspect.availableBindingSlots` (when the new kind has Binding-typed slots)
//   - `Introspect.getChildren` + `withChildren` (when the new kind is a layout)
//   - `updateField*` dispatch          (when the kind should support UpdateProp)
//   - `updateNested*` dispatch + `Introspect.availableNestedPaths`
//                                      (when the kind has a list-of-record /
//                                       list-of-scalar field that should take
//                                       nested UpdateProp paths — Phase 364;
//                                       the two tables must stay in sync)
//   - `replaceBinding*` dispatch       (when the kind has Binding-typed slots)
//   - `AiTools.Tools.extractBindings` + `extractSlot` (the introspection mirror
//      of `availableBindingSlots`; lives in the Fuaran.UI.AiTools package, but
//      must carry an arm for every Binding-typed slot the two tables above do)
//  The three Binding-slot tables (`availableBindingSlots`, `replaceBinding*`,
//  `extractSlot`) are asserted mutually consistent by the Phase 118 property
//  test in Fuaran.UI.AiTools.Tests — a slot added to one but not the others
//  fails the build.
//  Missing any of these turns the new kind into "AI gets a structured
//  PathNotSupportedYet hint enumerating what *is* supported" — which is the
//  graceful failure mode by design, not a defect.
// ============================================================================

#nowarn "3261"

open System.Collections.Generic
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect

// ─── Error builders ────────────────────────────────────────────────────────

let private nodeNotFound (NodeId rawId) : ApplyError =
    { Code = ApplyErrorCode.NodeNotFound
      Message = sprintf "Node '%s' not found in tree." rawId
      Hint = ApplyHint.empty }

let private parentNotFound (NodeId rawId) : ApplyError =
    { Code = ApplyErrorCode.ParentNotFound
      Message = sprintf "Parent node '%s' not found in tree." rawId
      Hint = ApplyHint.empty }

let private childlessKind (kind: NodeKind<'Msg>) (NodeId rawId) : ApplyError =
    { Code = ApplyErrorCode.ChildlessKind
      Message =
        sprintf
            "Node '%s' (kind=%s) has no Children field — only Layout kinds accept structural child ops."
            rawId
            (kindName kind)
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            Suggestion =
                Some
                    "Address a Layout node (Box / SplitPanel / Tabs / Stepper / SummaryList / Disclosure / Modal / ScrollArea)." } }

let private positionOutOfRange (NodeId rawId) (childCount: int) (position: int) : ApplyError =
    { Code = ApplyErrorCode.PositionOutOfRange
      Message =
        sprintf
            "Position %d is out of range for parent '%s' (valid: 0..%d, inclusive of append)."
            position
            rawId
            childCount
      Hint =
        { ApplyHint.empty with
            Suggestion = Some(sprintf "Pick a position between 0 and %d." childCount) } }

let private duplicateNodeId (NodeId rawId) : ApplyError =
    { Code = ApplyErrorCode.DuplicateNodeId
      Message = sprintf "NodeId '%s' is already present in the tree; ids must be unique per §4g." rawId
      Hint =
        { ApplyHint.empty with
            Suggestion = Some "Pick a different id, or RemoveNode the existing occurrence first." } }

let private fieldNotFound (kind: NodeKind<'Msg>) (NodeId rawId) (field: string) (otherNodes: NodeId list) : ApplyError =
    let suggestion =
        match otherNodes with
        | [] -> None
        | (NodeId otherId) :: _ ->
            Some(
                sprintf
                    "Field '%s' exists on node '%s' (and others) — UpdateProp against that node instead."
                    field
                    otherId
            )

    { Code = ApplyErrorCode.FieldNotFound
      Message = sprintf "Field '%s' not found on node '%s' (kind=%s)." field rawId (kindName kind)
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableFields kind
            NodesWithField =
                if List.isEmpty otherNodes then
                    None
                else
                    Some(field, otherNodes)
            Suggestion = suggestion } }

let private slotNotFound (kind: NodeKind<'Msg>) (NodeId rawId) (slot: string) : ApplyError =
    { Code = ApplyErrorCode.SlotNotFound
      Message = sprintf "Binding slot '%s' not found on node '%s' (kind=%s)." slot rawId (kindName kind)
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableBindingSlots kind } }

let private kindMismatch (message: string) (hint: ApplyHint) : ApplyError =
    { Code = ApplyErrorCode.KindMismatch
      Message = message
      Hint = hint }

let private pathGrammarSuggestion =
    "Paths are dot-separated field segments with optional 0-based list indices, e.g. Columns[0].Label."

/// PathInvalid — the path violates the WIRE_FORMAT.md §3.4 grammar. When the
/// target node resolved, the hint enumerates its addressable paths (top-level
/// fields + nested patterns) so the AI recovers in one turn.
let private pathInvalidWith (path: string) (reason: string) (resolved: NodeKind<'Msg> option) : ApplyError =
    let hint =
        match resolved with
        | Some kind ->
            { ApplyHint.empty with
                NodeKind = Some(kindName kind)
                AvailableFields = availableFields kind @ availableNestedPaths kind
                Suggestion = Some pathGrammarSuggestion }
        | None ->
            { ApplyHint.empty with
                Suggestion = Some pathGrammarSuggestion }

    { Code = ApplyErrorCode.PathInvalid
      Message = sprintf "Path '%s' is structurally invalid: %s." path reason
      Hint = hint }

/// PathInvalid — a list-typed segment was addressed without an index
/// (`Columns.Label`). Names the indexed form + the element count.
let private missingListIndex (kind: NodeKind<'Msg>) (NodeId rawId) (listField: string) (count: int) : ApplyError =
    { Code = ApplyErrorCode.PathInvalid
      Message =
        sprintf
            "Field '%s' on node '%s' (kind=%s) is a list — address an element with a 0-based index (the list has %d element(s))."
            listField
            rawId
            (kindName kind)
            count
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableNestedPaths kind
            Suggestion = Some(sprintf "Index the list: %s[i] with 0 <= i < %d." listField count) } }

/// PositionOutOfRange — a nested path's list index is outside the list.
let private nestedIndexOutOfRange
    (kind: NodeKind<'Msg>)
    (NodeId rawId)
    (listField: string)
    (count: int)
    (requested: int)
    : ApplyError =
    { Code = ApplyErrorCode.PositionOutOfRange
      Message =
        sprintf
            "Index %d is out of range for '%s' on node '%s' (%s)."
            requested
            listField
            rawId
            (if count = 0 then
                 "the list is empty"
             else
                 sprintf "valid: 0..%d" (count - 1))
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableNestedPaths kind
            Suggestion =
                Some(
                    if count = 0 then
                        sprintf "'%s' has no elements — install content via EditNode first." listField
                    else
                        sprintf "Pick an index between 0 and %d." (count - 1)
                ) } }

/// FieldNotFound at a nested segment — the hint enumerates the sub-paths
/// available at the failing segment (the §4d enumerate-alternatives
/// discipline, one level down).
let private nestedFieldNotFound
    (kind: NodeKind<'Msg>)
    (NodeId rawId)
    (path: string)
    (failingSegment: string)
    (availableAtSegment: string list)
    : ApplyError =
    { Code = ApplyErrorCode.FieldNotFound
      Message =
        sprintf "Field '%s' (in path '%s') not found on node '%s' (kind=%s)." failingSegment path rawId (kindName kind)
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableAtSegment
            Suggestion = Some(sprintf "Available at this segment: %s." (String.concat ", " availableAtSegment)) } }

let private pathNotSupportedYet (kind: NodeKind<'Msg>) (NodeId rawId) (path: string) : ApplyError =
    { Code = ApplyErrorCode.PathNotSupportedYet
      Message =
        sprintf "Path '%s' on node '%s' (kind=%s) is not yet supported by the apply engine." path rawId (kindName kind)
      Hint =
        { ApplyHint.empty with
            NodeKind = Some(kindName kind)
            AvailableFields = availableFields kind @ availableNestedPaths kind
            Suggestion = Some "Use a structural op (EditNode / InsertChild / RemoveNode) instead." } }

let private orderingMismatch (NodeId rawId) (expected: NodeId list) : ApplyError =
    let expectedRaw =
        expected |> List.map (fun (NodeId s) -> sprintf "'%s'" s) |> String.concat ", "

    { Code = ApplyErrorCode.OrderingMismatch
      Message =
        sprintf
            "ReorderChildren for '%s' did not list exactly the current child ids — expected: [%s]."
            rawId
            expectedRaw
      Hint =
        { ApplyHint.empty with
            AvailableFields = expected |> List.map (fun (NodeId s) -> s)
            Suggestion = Some "Provide a permutation of the parent's current child ids." } }

// ─── Binding-cast helper ───────────────────────────────────────────────────

/// Reshape a `Binding<obj>` (the wire-shape `ReplaceBinding` carries) into a
/// typed `Binding<'T>` by composing each case's payload through `unbox<'T>`.
/// Predicate gating the boxed-value cast-mismatch handlers below. The apply
/// engine only ever wants to recover from an `InvalidCastException` thrown by
/// `unbox` / `castBinding` on an inner-type mismatch; any other exception must
/// propagate. Fable cannot type-test exceptions (`with :? InvalidCastException`
/// is a compile error), so the handlers use `with ex when isCastMismatch ex`
/// instead: under .NET this narrows to `InvalidCastException` exactly as before;
/// under the Fable browser host it accepts any exception (equivalent in
/// practice — Fable's `unbox` is a no-op that throws nothing, so the guarded
/// recovery never fires there). (Phase 191 — Fable-portable apply engine.)
let inline private isCastMismatch (ex: exn) : bool =
#if FABLE_COMPILER
    true
#else
    (ex :? System.InvalidCastException)
#endif

/// Throws on Static / State whose inner value is not actually the right
/// runtime type; the apply engine catches and surfaces as KindMismatch.
let rec private castBinding<'T> (b: Binding<obj>) : Binding<'T> =
    match b with
    | Binding.Static v -> Binding.Static(unbox<'T> v)
    | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> unbox<'T>, dependsOn)
    | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map unbox<'T>)
    | Binding.Selection(id, accessor, dv, fld) ->
        Binding.Selection(id, accessor >> unbox<'T>, dv |> Option.map unbox<'T>, fld)
    | Binding.State(key, defaultValue) -> Binding.State(key, unbox<'T> defaultValue)
    | Binding.Computed f -> Binding.Computed(f >> unbox<'T>)
    // i18n bindings carry only string key + obj-typed args, no
    // 'T payload to cast. Pass through; the resolver enforces 'T = string at
    // resolution time.
    | Binding.I18n(key, args) -> Binding.I18n(key, args)
    // Local bindings carry an InitialFrom of the same 'T plus
    // obj-erased OnCommit / Format / Parse. Recurse InitialFrom; box-wrap
    // 'T → obj on the obj-typed projections so the typed payload matches.
    | Binding.Local local ->
        Binding.Local
            { InitialFrom = castBinding<'T> local.InitialFrom
              FlushOn = local.FlushOn
              OnCommit = (fun (t: 'T) -> local.OnCommit(box t))
              Format = local.Format |> Option.map (fun f -> fun (t: 'T) -> f (box t))
              Parse = (fun s -> local.Parse s |> Result.map unbox<'T>) }
    // Format bindings carry a `Binding<float>` source + bounded
    // Format / LocaleSource — no 'T payload to cast (the formatter always
    // produces a string). Pass through; the resolver enforces 'T = string
    // at resolution time (same posture as `Binding.I18n`).
    | Binding.Format(source, format, locale) -> Binding.Format(source, format, locale)
    // Transform bindings carry a `Fuaran.Core` source + pipeline (no 'T payload — the rows are
    // produced obj-boxed by the evaluator). Pass through; the resolver evaluates it at resolution
    // time (same posture as `Binding.Format` / `Binding.I18n`).
    // `parameters` are `Binding<obj>` sources (no outer 'T payload — like I18n args), so they pass
    // through unchanged alongside the source + pipeline.
    | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
    // Invoke bindings carry a capability id + scalar args (no 'T payload — the resolved value is a
    // host-produced `Deferred`). Pass through; the resolver dispatches at resolution time.
    | Binding.Invoke(capabilityId, args) -> Binding.Invoke(capabilityId, args)

// ─── UpdateProp dispatch — per spec-record top-level fields ────────────────
//
// Each dispatch returns `Result<NodeKind<'Msg>, string>` — `Error msg` is
// caught above and folded into `fieldNotFound` / `kindMismatch` /
// `pathNotSupportedYet` as appropriate. The `PropValue` payload is lowered to
// a single `obj` representation at the `applyOne` entry (see
// `PropValue.toObj`): a `Native` carries a boxed F# value of the field's
// expected type (the in-process fast path); a `Wire` lowers its `JVal` to the
// structural shapes the `JsonDecode.Coerce.*` decoders parse.

type private UpdateResult<'Msg> =
    | Updated of NodeKind<'Msg>
    | UnknownField
    | NotSupportedYet
    | TypeMismatch of string

/// Coerce the boxed `obj` carried by an `UpdateProp` value into the
/// dispatcher's expected typed `'T`. Native F# callers (typed orchestrator
/// paths, `Action.CommitLocal` test fixtures, etc.) hand us a `box typedValue`
/// which `unbox<'T>` resolves directly — the .NET fast path. The wire-decoder
/// path (`Fuaran.UI.Ops.JsonDecode.decodeOp` → `UpdateProp`) emits primitives /
/// `Map<string, obj>` shapes for DU / record / option values that direct unbox
/// can't resolve (it throws `InvalidCastException`); the fallback runs the
/// structural `JsonDecode.Coerce.*` decoder named explicitly at the call site.
/// The `KindMismatch` surfaced by the apply engine when both paths fail carries
/// the structural decoder's error message so the AI consumer gets a
/// discriminator-shape-aware hint rather than a CLR cast string.
///
/// **Why the coercer is named statically (not dispatched on `typeof<'T>`):**
/// under Fable, `unbox` is a runtime no-op that never throws, so the
/// `InvalidCastException`-guarded fallback below would be *dead code* and a
/// wire value would pass through structurally un-coerced — silently mis-shaping
/// every `TextSource` / `Binding<_>` / format field. And Fable erases generic
/// type arguments, so the old `typeof<'T>` dispatch couldn't recover the right
/// decoder there either. Naming the coercer at the call site makes coercion run
/// identically on both pipelines: Fable runs it unconditionally; .NET keeps the
/// fast-path-then-fallback shape. (Phase 191 — Fable-portable apply engine.)
let inline private coerceField (coerce: obj -> Result<'T, string>) (v: obj) : Result<'T, string> =
#if FABLE_COMPILER
    coerce v
#else
    try
        Ok(unbox<'T> v)
    with ex when isCastMismatch ex ->
        coerce v
#endif

// A `PropValue` lowers to the coercer-facing `obj` at the `applyOne` entry via
// the shared `PropValue.toObj` (Ops.Abstractions): `Native` passes through
// untouched; `Wire` lowers to the structural shapes the `JsonDecode.Coerce.*`
// decoders parse (numbers boxed as `float`, objects as `Map<string, obj>`) —
// byte-for-byte the representation the wire decoder's old obj path produced,
// so the whole per-field dispatch below is agnostic to which population
// arrived.

let private updateMetric (field: string) (v: obj) (spec: MetricSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Metric newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Value" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBindingFloat v
            |> Result.map (fun x -> { spec with Value = x }))
    | "Format" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryCellFormat v
            |> Result.map (fun x -> { spec with Format = x }))
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | "Weight" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStyleWeight v
            |> Result.map (fun x -> { spec with Weight = x }))
    | "Emphasis" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryEmphasis v
            |> Result.map (fun x -> { spec with Emphasis = x }))
    | "Trend" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBindingFloatOption v
            |> Result.map (fun x -> { spec with Trend = x }))
    | "TrendFormat" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryCellFormatOption v
            |> Result.map (fun x -> { spec with TrendFormat = x }))
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIconSourceOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Subtext" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Subtext = x }))
    | _ -> UnknownField

let private updateHeading (field: string) (v: obj) (spec: HeadingSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Heading newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Level" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryInt v
            |> Result.map (fun x -> { spec with Level = x }))
    | "Text" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Text = x }))
    | "Variant" ->
        // Additive HeadingVariant DU axis.
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryHeadingVariant v
            |> Result.map (fun x -> { spec with Variant = x }))
    | _ -> UnknownField

let private updateMarkdown (field: string) (v: obj) (spec: MarkdownSpec) : UpdateResult<'Msg> =
    match field with
    | "Text" ->
        match coerceField JsonDecode.Coerce.tryTextSource v with
        | Ok x -> Updated(NodeKind.Display(DisplayKind.Markdown { spec with Text = x }))
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

let private updateBadge (field: string) (v: obj) (spec: BadgeSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Badge newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Variant" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBadgeVariant v
            |> Result.map (fun x -> { spec with Variant = x }))
    | _ -> UnknownField

let private updateLink (field: string) (v: obj) (spec: LinkSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Link newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Href" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBindingString v
            |> Result.map (fun x -> { spec with Href = x }))
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Rel" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Rel = x }))
    | "Target" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Target = x }))
    | "Download" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Download = x }))
    | _ -> UnknownField

let private updateSkeleton (field: string) (v: obj) (spec: SkeletonSpec) : UpdateResult<'Msg> =
    match field with
    | "Rows" ->
        match coerceField JsonDecode.Coerce.tryInt v with
        | Ok x -> Updated(NodeKind.Display(DisplayKind.Skeleton { spec with Rows = x }))
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

let private updateCallout (field: string) (v: obj) (spec: CalloutSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Callout newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | "Heading" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Heading = x }))
    | "Body" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Body = x }))
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIconSourceOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Dismissable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Dismissable = x }))
    | _ -> UnknownField

let private updateProgress (field: string) (v: obj) (spec: ProgressSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Progress newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Fraction" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBindingFloat v
            |> Result.map (fun x -> { spec with Fraction = x }))
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Caveat" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Caveat = x }))
    | "Indeterminate" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Indeterminate = x }))
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | _ -> UnknownField

// Phase 390 — the unified container's field-level UpdateProp. Accepts the
// retired kinds' field names (`Orientation` / `Wrap` on a Flex box, `Cols` /
// `TemplateColumns` on a Grid box, `Heading` on any) so pre-merge op-streams
// replaying `UpdateProp` against an upgraded `Box` node keep working. A field
// that does not match the box's current layout mode is `UnknownField`.
let private updateBox (field: string) (v: obj) (spec: BoxSpec<'Msg>) : UpdateResult<'Msg> =
    let updated (s: BoxSpec<'Msg>) =
        Updated(NodeKind.Layout(LayoutKind.Box s))

    match field, spec.Layout with
    | "Orientation", BoxLayout.Flex f ->
        match coerceField JsonDecode.Coerce.tryOrientation v with
        | Ok x ->
            updated
                { spec with
                    Layout = BoxLayout.Flex { f with Direction = x } }
        | Error msg -> TypeMismatch msg
    | "Wrap", BoxLayout.Flex f ->
        match coerceField JsonDecode.Coerce.tryBool v with
        | Ok x ->
            updated
                { spec with
                    Layout = BoxLayout.Flex { f with Wrap = x } }
        | Error msg -> TypeMismatch msg
    | "Cols", BoxLayout.Grid g ->
        match coerceField JsonDecode.Coerce.tryInt v with
        | Ok x ->
            updated
                { spec with
                    Layout = BoxLayout.Grid { g with Cols = x } }
        | Error msg -> TypeMismatch msg
    | "TemplateColumns", BoxLayout.Grid g ->
        // Additive optional `string option` field. Accepts either a raw string
        // (sugar — wraps in `Some`) or an explicit `string option` payload.
        match coerceField JsonDecode.Coerce.tryStringOption v with
        | Ok x ->
            updated
                { spec with
                    Layout = BoxLayout.Grid { g with TemplateColumns = x } }
        | Error _ ->
            match coerceField JsonDecode.Coerce.tryString v with
            | Ok x ->
                updated
                    { spec with
                        Layout = BoxLayout.Grid { g with TemplateColumns = Some x } }
            | Error msg -> TypeMismatch msg
    | "Heading", _ ->
        match coerceField JsonDecode.Coerce.tryTextSourceOption v with
        | Ok x -> updated { spec with Heading = x }
        | Error msg -> TypeMismatch msg
    | "Children", _ -> NotSupportedYet
    | _ -> UnknownField

let private updateSplitPanel (field: string) (v: obj) (spec: SplitPanelSpec<'Msg>) : UpdateResult<'Msg> =
    match field with
    | "Weight" ->
        match coerceField JsonDecode.Coerce.tryFloat v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.SplitPanel { spec with Weight = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateTabs (field: string) (v: obj) (spec: TabsSpec<'Msg>) : UpdateResult<'Msg> =
    match field with
    | "Orientation" ->
        match coerceField JsonDecode.Coerce.tryOrientation v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.Tabs { spec with Orientation = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateStepper (field: string) (v: obj) (spec: StepperSpec<'Msg>) : UpdateResult<'Msg> =
    match field with
    | "ActiveStep" ->
        match coerceField JsonDecode.Coerce.tryBindingInt v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.Stepper { spec with ActiveStep = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateSummaryList (field: string) (v: obj) (spec: SummaryListSpec<'Msg>) : UpdateResult<'Msg> =
    // SummaryList carries an optional section heading + a Children
    // list. Children is structural and routes through the v1 "use a
    // structural op" hint, same as every other layout.
    match field with
    | "Heading" ->
        match coerceField JsonDecode.Coerce.tryTextSourceOption v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.SummaryList { spec with Heading = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateDisclosure (field: string) (v: obj) (spec: DisclosureSpec<'Msg>) : UpdateResult<'Msg> =
    // Disclosure carries Heading (required) + Open binding +
    // DefaultOpen bool + Children list. OnToggle is a closure — the apply
    // engine doesn't expose it as a field (mirrors Tabs.OnSelect).
    match field with
    | "Heading" ->
        match coerceField JsonDecode.Coerce.tryTextSource v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.Disclosure { spec with Heading = x }))
        | Error msg -> TypeMismatch msg
    | "Open" ->
        match coerceField JsonDecode.Coerce.tryBindingBool v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.Disclosure { spec with Open = x }))
        | Error msg -> TypeMismatch msg
    | "DefaultOpen" ->
        match coerceField JsonDecode.Coerce.tryBool v with
        | Ok x -> Updated(NodeKind.Layout(LayoutKind.Disclosure { spec with DefaultOpen = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateLabelValueRow (field: string) (v: obj) (spec: LabelValueRowSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.LabelValueRow newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Value" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBindingFloat v
            |> Result.map (fun x -> { spec with Value = x }))
    | "Format" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryCellFormat v
            |> Result.map (fun x -> { spec with Format = x }))
    | "Emphasis" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryEmphasisFlag v
            |> Result.map (fun x -> { spec with Emphasis = x }))
    | "Help" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Help = x }))
    | _ -> UnknownField

let private updateFact (field: string) (v: obj) (spec: FactSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Display(DisplayKind.Fact newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Value" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Value = x }))
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | "Emphasis" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryEmphasisFlag v
            |> Result.map (fun x -> { spec with Emphasis = x }))
    | "Help" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Help = x }))
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIconSourceOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | _ -> UnknownField

let private updateFragmentDecl (field: string) (v: obj) (spec: FragmentDeclSpec<'Msg>) : UpdateResult<'Msg> =
    // The only structurally swappable field is
    // `Name` — rebrands the decl so refs targeting the new name resolve
    // here. The body is a Node subtree the AI swaps via EditNode against
    // the body's root NodeId (reachable through the tree walkers per
    // `Introspect.getChildren`'s FragmentDecl arm).
    match field with
    | "Name" ->
        match coerceField JsonDecode.Coerce.tryString v with
        | Ok name -> Updated(NodeKind.FragmentDecl { spec with Name = FragmentId name })
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

let private updateFragmentRef (field: string) (v: obj) (spec: FragmentRefSpec<'Msg>) : UpdateResult<'Msg> =
    // Swap the referenced fragment by name. This is the
    // "swap a fragment reference" case —
    // a single op rebinds the ref to a different decl without
    // touching either body.
    match field with
    | "Name" ->
        match coerceField JsonDecode.Coerce.tryString v with
        | Ok name -> Updated(NodeKind.FragmentRef { spec with Name = FragmentId name })
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

let private dispatchUpdateField (field: string) (v: obj) (kind: NodeKind<'Msg>) : UpdateResult<'Msg> =
    match kind with
    | NodeKind.Layout layout ->
        match layout with
        | LayoutKind.Box spec -> updateBox field v spec
        | LayoutKind.SplitPanel spec -> updateSplitPanel field v spec
        | LayoutKind.Tabs spec -> updateTabs field v spec
        | LayoutKind.Stepper spec -> updateStepper field v spec
        | LayoutKind.SummaryList spec -> updateSummaryList field v spec
        | LayoutKind.Disclosure spec -> updateDisclosure field v spec
        // Phase 289 — Modal / ScrollArea field-level UpdateProp not wired yet
        // (mirrors the Sparkline / Input precedent). Their child subtrees are
        // still mutable via structural ops (Introspect.getChildren enumerates
        // them) and the whole node is swappable via EditNode.
        | LayoutKind.Modal _ -> NotSupportedYet
        | LayoutKind.ScrollArea _ -> NotSupportedYet
    | NodeKind.Display display ->
        match display with
        | DisplayKind.Heading spec -> updateHeading field v spec
        | DisplayKind.Markdown spec -> updateMarkdown field v spec
        | DisplayKind.Metric spec -> updateMetric field v spec
        | DisplayKind.Badge spec -> updateBadge field v spec
        | DisplayKind.Sparkline _ -> NotSupportedYet
        | DisplayKind.Callout spec -> updateCallout field v spec
        | DisplayKind.Progress spec -> updateProgress field v spec
        | DisplayKind.Skeleton spec -> updateSkeleton field v spec
        | DisplayKind.LabelValueRow spec -> updateLabelValueRow field v spec
        | DisplayKind.Fact spec -> updateFact field v spec
        | DisplayKind.Link spec -> updateLink field v spec
        // Phase 287/289 — Image / List / Toast field-level UpdateProp not wired
        // yet (mirrors the Sparkline precedent); whole-node swap via EditNode
        // remains available.
        | DisplayKind.Image _ -> NotSupportedYet
        | DisplayKind.List _ -> NotSupportedYet
        | DisplayKind.Toast _ -> NotSupportedYet
        // Phase 290/293 — CodeBlock / Math field-level UpdateProp not wired
        // (the spec is mostly literal strings, not the bound surface UpdateProp
        // targets); whole-node swap via EditNode remains available.
        | DisplayKind.CodeBlock _ -> NotSupportedYet
        | DisplayKind.Math _ -> NotSupportedYet
        // Phase 524 — Drawing field-level UpdateProp not wired (a Drawing is a
        // whole-artefact swap via EditNode); whole-node swap remains available.
        | DisplayKind.Drawing _ -> NotSupportedYet
    | NodeKind.Input _ -> NotSupportedYet
    | NodeKind.Visualisation _ -> NotSupportedYet
    | NodeKind.Custom _ -> NotSupportedYet
    // ErrorBoundary's `Child` + `Fallback` are
    // Node subtrees, not field-shaped values — the AI swaps them via
    // EditNode (kind-level wholesale swap) rather than UpdateProp. No
    // field-level surface exists for the boundary.
    | NodeKind.ErrorBoundary _ -> NotSupportedYet
    // Switch (Phase 392): `StateKey` is a string and `Cases` / `Default` are
    // Node subtrees — field-level UpdateProp not wired, and (like ErrorBoundary)
    // the switch is opaque to the apply engine's structural walkers. Swap the
    // whole switch via EditNode.
    | NodeKind.Switch _ -> NotSupportedYet
    | NodeKind.FragmentDecl spec -> updateFragmentDecl field v spec
    | NodeKind.FragmentRef spec -> updateFragmentRef field v spec
    // Mount (§4o) is an opaque isolation boundary — no field-level UpdateProp
    // surface (the guest interior is a scope reference, not editable fields).
    // The whole node is swappable via EditNode; guest-side edits route through
    // the guest's own scoped apply engine, not the host's.
    | NodeKind.Mount _ -> NotSupportedYet

// ─── UpdateProp path parser (WIRE_FORMAT.md §3.4 grammar — Phase 364) ──────
//
//   path     := segment ( "." segment )*
//   segment  := field ( "[" index "]" )?
//   field    := [A-Za-z_][A-Za-z0-9_]*
//   index    := "0" | [1-9][0-9]*
//
// Hand-rolled (no regex) so the parse is byte-identical across .NET and
// Fable. Character classes are strict ASCII per the grammar — Unicode
// letters/digits are not field-name or index characters.

type private PathSeg = { Field: string; Index: int option }

let inline private isFieldStart (c: char) =
    (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '_'

let inline private isFieldChar (c: char) =
    isFieldStart c || (c >= '0' && c <= '9')

let inline private isAsciiDigit (c: char) = c >= '0' && c <= '9'

let private parseSegment (raw: string) : Result<PathSeg, string> =
    if raw.Length = 0 then
        Error "empty segment"
    else
        let bracket = raw.IndexOf '['
        let fieldPart = if bracket < 0 then raw else raw.Substring(0, bracket)

        if
            fieldPart.Length = 0
            || not (isFieldStart fieldPart[0])
            || not (fieldPart |> Seq.forall isFieldChar)
        then
            Error(sprintf "segment '%s' is not a field name" raw)
        elif bracket < 0 then
            Ok { Field = fieldPart; Index = None }
        else
            let indexPart = raw.Substring bracket

            if indexPart.Length < 3 || indexPart[indexPart.Length - 1] <> ']' then
                Error(sprintf "malformed index in segment '%s'" raw)
            else
                let digits = indexPart.Substring(1, indexPart.Length - 2)

                if digits.Length = 0 || not (digits |> Seq.forall isAsciiDigit) then
                    Error(sprintf "index in segment '%s' must be a non-negative decimal integer" raw)
                elif digits.Length > 1 && digits[0] = '0' then
                    Error(sprintf "index in segment '%s' has a leading zero" raw)
                else
                    match System.Int32.TryParse digits with
                    | true, n -> Ok { Field = fieldPart; Index = Some n }
                    | _ -> Error(sprintf "index in segment '%s' is out of range" raw)

let private parsePath (path: string) : Result<PathSeg list, string> =
    if System.String.IsNullOrWhiteSpace path then
        Error "empty path"
    else
        let rec loop acc remaining =
            match remaining with
            | [] -> Ok(List.rev acc)
            | raw :: rest ->
                match parseSegment raw with
                | Ok seg -> loop (seg :: acc) rest
                | Error reason -> Error reason

        loop [] (path.Split '.' |> Array.toList)

// ─── UpdateProp nested dispatch — per-kind typed traversal (Phase 364) ─────
//
// The nested legs of the per-kind dispatch table: each resolves a parsed
// multi-segment path against the spec record with the same coercion
// discipline the top-level dispatch uses. No reflection — a kind gains
// nested addressing by adding an arm here + its patterns in
// `Introspect.availableNestedPaths` (the two must stay in sync; the hint
// quality is bounded by the pattern list). Closure-bearing sub-fields
// (Column.Value / Column.Kind / FormField.Kind) are never addressable.

type private NestedUpdate<'Msg> =
    | NestedUpdated of NodeKind<'Msg>
    | NestedFieldNotFound of failingSegment: string * availableAtSegment: string list
    | NestedMissingIndex of listField: string * count: int
    | NestedIndexOutOfRange of listField: string * count: int * requested: int
    | NestedNotSupported
    | NestedTypeMismatch of string

let private replaceAt (i: int) (item: 'a) (xs: 'a list) : 'a list =
    xs |> List.mapi (fun j x -> if j = i then item else x)

let private updateNestedGrid (segs: PathSeg list) (v: obj) (spec: GridSpec<'Msg>) : NestedUpdate<'Msg> =
    match segs with
    | { Field = "Columns"; Index = None } :: _ -> NestedMissingIndex("Columns", spec.Columns.Length)
    | { Field = "Columns"; Index = Some i } :: _ when i >= spec.Columns.Length ->
        NestedIndexOutOfRange("Columns", spec.Columns.Length, i)
    | [ { Field = "Columns"; Index = Some i }; { Field = leaf; Index = None } ] ->
        let col = spec.Columns[i]

        let rebuild c =
            NodeKind.Visualisation(
                VisKind.DataGrid
                    { spec with
                        Columns = replaceAt i c spec.Columns }
            )

        match leaf with
        | "Label" ->
            match coerceField JsonDecode.Coerce.tryString v with
            | Ok x -> NestedUpdated(rebuild { col with Label = x })
            | Error msg -> NestedTypeMismatch msg
        | "Format" ->
            match coerceField JsonDecode.Coerce.tryCellFormat v with
            | Ok x -> NestedUpdated(rebuild { col with Format = x })
            | Error msg -> NestedTypeMismatch msg
        | "Width" ->
            match coerceField JsonDecode.Coerce.tryColumnWidth v with
            | Ok x -> NestedUpdated(rebuild { col with Width = x })
            | Error msg -> NestedTypeMismatch msg
        // Value / Kind are closure-bearing — never addressable.
        | "Value"
        | "Kind" -> NestedNotSupported
        | _ -> NestedFieldNotFound(leaf, [ "Label"; "Format"; "Width" ])
    | { Field = "Columns"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ ->
        NestedFieldNotFound(f, availableFields (NodeKind.Visualisation(VisKind.DataGrid spec)))
    | [] -> NestedNotSupported

let private updateNestedChart (segs: PathSeg list) (v: obj) (spec: ChartSpec<'Msg>) : NestedUpdate<'Msg> =
    match segs with
    | { Field = "YFields"; Index = None } :: _ -> NestedMissingIndex("YFields", spec.YFields.Length)
    | { Field = "YFields"; Index = Some i } :: _ when i >= spec.YFields.Length ->
        NestedIndexOutOfRange("YFields", spec.YFields.Length, i)
    | [ { Field = "YFields"; Index = Some i } ] ->
        // Indexed scalar leaf — the element IS the string value.
        match coerceField JsonDecode.Coerce.tryString v with
        | Ok x ->
            NestedUpdated(
                NodeKind.Visualisation(
                    VisKind.Chart
                        { spec with
                            YFields = replaceAt i x spec.YFields }
                )
            )
        | Error msg -> NestedTypeMismatch msg
    | { Field = "YFields"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ ->
        NestedFieldNotFound(f, availableFields (NodeKind.Visualisation(VisKind.Chart spec)))
    | [] -> NestedNotSupported

let private updateNestedTabs (segs: PathSeg list) (v: obj) (spec: TabsSpec<'Msg>) : NestedUpdate<'Msg> =
    // TabHeaders is optional — an absent header list addresses like an empty
    // one (PositionOutOfRange with count 0 + an install-via-EditNode hint).
    let headers = spec.TabHeaders |> Option.defaultValue []

    match segs with
    | { Field = "TabHeaders"; Index = None } :: _ -> NestedMissingIndex("TabHeaders", headers.Length)
    | { Field = "TabHeaders"; Index = Some i } :: _ when i >= headers.Length ->
        NestedIndexOutOfRange("TabHeaders", headers.Length, i)
    | [ { Field = "TabHeaders"; Index = Some i }; { Field = leaf; Index = None } ] ->
        let hdr = headers[i]

        let rebuild h =
            NodeKind.Layout(
                LayoutKind.Tabs
                    { spec with
                        TabHeaders = Some(replaceAt i h headers) }
            )

        match leaf with
        | "Label" ->
            match coerceField JsonDecode.Coerce.tryTextSource v with
            | Ok x -> NestedUpdated(rebuild { hdr with Label = x })
            | Error msg -> NestedTypeMismatch msg
        | "Icon" ->
            match coerceField JsonDecode.Coerce.tryIconSourceOption v with
            | Ok x -> NestedUpdated(rebuild { hdr with Icon = x })
            | Error msg -> NestedTypeMismatch msg
        | "Disabled" ->
            // Optional typed binding; replacing it installs `Some`, mirroring
            // how Metric.Trend / Button.Disabled are set.
            match coerceField JsonDecode.Coerce.tryBindingBool v with
            | Ok x -> NestedUpdated(rebuild { hdr with Disabled = Some x })
            | Error msg -> NestedTypeMismatch msg
        | _ -> NestedFieldNotFound(leaf, [ "Label"; "Icon"; "Disabled" ])
    | { Field = "TabHeaders"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.Layout(LayoutKind.Tabs spec)))
    | [] -> NestedNotSupported

let private updateNestedForm (segs: PathSeg list) (v: obj) (spec: FormSpec<'Msg>) : NestedUpdate<'Msg> =
    match segs with
    | { Field = "Fields"; Index = None } :: _ -> NestedMissingIndex("Fields", spec.Fields.Length)
    | { Field = "Fields"; Index = Some i } :: _ when i >= spec.Fields.Length ->
        NestedIndexOutOfRange("Fields", spec.Fields.Length, i)
    | [ { Field = "Fields"; Index = Some i }; { Field = leaf; Index = None } ] ->
        let fld = spec.Fields[i]

        let rebuild f =
            NodeKind.Input(
                InputKind.Form
                    { spec with
                        Fields = replaceAt i f spec.Fields }
            )

        match leaf with
        | "Label" ->
            match coerceField JsonDecode.Coerce.tryTextSource v with
            | Ok x -> NestedUpdated(rebuild { fld with Label = x })
            | Error msg -> NestedTypeMismatch msg
        | "Required" ->
            match coerceField JsonDecode.Coerce.tryBool v with
            | Ok x -> NestedUpdated(rebuild { fld with Required = x })
            | Error msg -> NestedTypeMismatch msg
        | "Help" ->
            match coerceField JsonDecode.Coerce.tryTextSourceOption v with
            | Ok x -> NestedUpdated(rebuild { fld with Help = x })
            | Error msg -> NestedTypeMismatch msg
        // Id is the form-store key (rewiring it silently breaks onChange
        // routing); Kind is closure-bearing. Neither is addressable.
        | "Id"
        | "Kind" -> NestedNotSupported
        | _ -> NestedFieldNotFound(leaf, [ "Label"; "Required"; "Help" ])
    | { Field = "Fields"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.Input(InputKind.Form spec)))
    | [] -> NestedNotSupported

let private dispatchNestedUpdate (segs: PathSeg list) (v: obj) (kind: NodeKind<'Msg>) : NestedUpdate<'Msg> =
    match kind with
    | NodeKind.Visualisation(VisKind.DataGrid spec) -> updateNestedGrid segs v spec
    | NodeKind.Visualisation(VisKind.Chart spec) -> updateNestedChart segs v spec
    | NodeKind.Layout(LayoutKind.Tabs spec) -> updateNestedTabs segs v spec
    | NodeKind.Input(InputKind.Form spec) -> updateNestedForm segs v spec
    | _ -> NestedNotSupported

// ─── ReplaceBinding dispatch — per spec-record Binding-typed slots ─────────

let private replaceBindingMetric
    (slot: string)
    (b: Binding<obj>)
    (spec: MetricSpec)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Value" ->
            Ok(
                NodeKind.Display(
                    DisplayKind.Metric
                        { spec with
                            Value = castBinding<float> b }
                )
            )
        | "Trend" ->
            Ok(
                NodeKind.Display(
                    DisplayKind.Metric
                        { spec with
                            Trend = Some(castBinding<float> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Display(DisplayKind.Metric spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Metric.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Metric"
                    AvailableFields = [ "Value"; "Trend" ] }
        )

let private replaceBindingSparkline
    (slot: string)
    (b: Binding<obj>)
    (spec: SparklineSpec)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Source" ->
            Ok(
                NodeKind.Display(
                    DisplayKind.Sparkline
                        { spec with
                            Source = castBinding<float seq> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Display(DisplayKind.Sparkline spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Sparkline.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Sparkline"
                    AvailableFields = [ "Source" ] }
        )

let private replaceBindingLabelValueRow
    (slot: string)
    (b: Binding<obj>)
    (spec: LabelValueRowSpec)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Value" ->
            Ok(
                NodeKind.Display(
                    DisplayKind.LabelValueRow
                        { spec with
                            Value = castBinding<float> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Display(DisplayKind.LabelValueRow spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match LabelValueRow.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "LabelValueRow"
                    AvailableFields = [ "Value" ] }
        )

let private replaceBindingProgress
    (slot: string)
    (b: Binding<obj>)
    (spec: ProgressSpec)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Fraction" ->
            Ok(
                NodeKind.Display(
                    DisplayKind.Progress
                        { spec with
                            Fraction = castBinding<float> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Display(DisplayKind.Progress spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Progress.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Progress"
                    AvailableFields = [ "Fraction" ] }
        )

let private replaceBindingStepper
    (slot: string)
    (b: Binding<obj>)
    (spec: StepperSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "ActiveStep" ->
            Ok(
                NodeKind.Layout(
                    LayoutKind.Stepper
                        { spec with
                            ActiveStep = castBinding<int> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Layout(LayoutKind.Stepper spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Stepper.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Stepper"
                    AvailableFields = [ "ActiveStep" ] }
        )

let private replaceBindingGrid
    (slot: string)
    (b: Binding<obj>)
    (spec: GridSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Source" ->
            Ok(
                NodeKind.Visualisation(
                    VisKind.DataGrid
                        { spec with
                            Source = castBinding<obj seq> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Visualisation(VisKind.DataGrid spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Grid.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Grid"
                    AvailableFields = [ "Source" ] }
        )

let private replaceBindingChart
    (slot: string)
    (b: Binding<obj>)
    (spec: ChartSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Source" ->
            Ok(
                NodeKind.Visualisation(
                    VisKind.Chart
                        { spec with
                            Source = castBinding<obj seq> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Visualisation(VisKind.Chart spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Chart.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Chart"
                    AvailableFields = [ "Source" ] }
        )

let private replaceBindingMap
    (slot: string)
    (b: Binding<obj>)
    (spec: MapSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Source" ->
            Ok(
                NodeKind.Visualisation(
                    VisKind.Map
                        { spec with
                            Source = castBinding<MapMarker seq> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Visualisation(VisKind.Map spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Map.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Map"
                    AvailableFields = [ "Source" ] }
        )

let private replaceBindingButton
    (slot: string)
    (b: Binding<obj>)
    (spec: ButtonSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        // `Disabled` is an optional typed binding; replacing it installs
        // `Some`, mirroring how `Metric.Trend` / `Tabs.ActiveTag` are set.
        | "Disabled" ->
            Ok(
                NodeKind.Input(
                    InputKind.Button
                        { spec with
                            Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Input(InputKind.Button spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Button.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Button"
                    AvailableFields = [ "Disabled" ] }
        )

let private replaceBindingSelect
    (slot: string)
    (b: Binding<obj>)
    (spec: SelectSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "Source" ->
            Ok(
                NodeKind.Input(
                    InputKind.Select
                        { spec with
                            Source = castBinding<SelectOption list> b }
                )
            )
        | "Value" ->
            Ok(
                NodeKind.Input(
                    InputKind.Select
                        { spec with
                            Value = castBinding<string option> b }
                )
            )
        // Phase 130: optional bound disabled-state; replacing it installs
        // `Some`, mirroring Button.Disabled / Metric.Trend.
        | "Disabled" ->
            Ok(
                NodeKind.Input(
                    InputKind.Select
                        { spec with
                            Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Input(InputKind.Select spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Select.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Select"
                    AvailableFields = [ "Source"; "Value"; "Disabled" ] }
        )

let private replaceBindingForm
    (slot: string)
    (b: Binding<obj>)
    (spec: FormSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        // Phase 130: optional bound form-level disabled-state; replacing it
        // installs `Some`, mirroring Button.Disabled.
        | "Disabled" ->
            Ok(
                NodeKind.Input(
                    InputKind.Form
                        { spec with
                            Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Input(InputKind.Form spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Form.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Form"
                    AvailableFields = [ "Disabled" ] }
        )

let private replaceBindingFileUpload
    (slot: string)
    (b: Binding<obj>)
    (spec: FileUploadSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        // Phase 130: optional bound disabled-state; replacing it installs
        // `Some`, mirroring Button.Disabled.
        | "Disabled" ->
            Ok(
                NodeKind.Input(
                    InputKind.FileUpload
                        { spec with
                            Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Input(InputKind.FileUpload spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match FileUpload.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "FileUpload"
                    AvailableFields = [ "Disabled" ] }
        )

let private replaceBindingTabs
    (slot: string)
    (b: Binding<obj>)
    (spec: TabsSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        | "ActiveIndex" ->
            Ok(
                NodeKind.Layout(
                    LayoutKind.Tabs
                        { spec with
                            ActiveIndex = castBinding<int> b }
                )
            )
        // ActiveTag is an optional typed-tag overlay binding; replacing it
        // installs `Some`, mirroring how `Metric.Trend` is set.
        | "ActiveTag" ->
            Ok(
                NodeKind.Layout(
                    LayoutKind.Tabs
                        { spec with
                            ActiveTag = Some(castBinding<string> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Layout(LayoutKind.Tabs spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Tabs.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Tabs"
                    AvailableFields = [ "ActiveIndex"; "ActiveTag" ] }
        )

let private replaceBindingDisclosure
    (slot: string)
    (b: Binding<obj>)
    (spec: DisclosureSpec<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    try
        match slot with
        // `Open` is dual-surface (also an UpdateProp field via
        // `updateDisclosure`); ReplaceBinding is the semantically-correct
        // path for swapping the controlled-open binding.
        | "Open" -> Ok(NodeKind.Layout(LayoutKind.Disclosure { spec with Open = castBinding<bool> b }))
        | _ -> Error(slotNotFound (NodeKind.Layout(LayoutKind.Disclosure spec)) (NodeId "_") slot)
    with ex when isCastMismatch ex ->
        Error(
            kindMismatch
                (sprintf "Binding inner value does not match Disclosure.%s expected type: %s" slot ex.Message)
                { ApplyHint.empty with
                    NodeKind = Some "Disclosure"
                    AvailableFields = [ "Open" ] }
        )

let private dispatchReplaceBinding
    (slot: string)
    (b: Binding<obj>)
    (kind: NodeKind<'Msg>)
    : Result<NodeKind<'Msg>, ApplyError> =
    match kind with
    | NodeKind.Display(DisplayKind.Metric spec) -> replaceBindingMetric slot b spec
    | NodeKind.Display(DisplayKind.Sparkline spec) -> replaceBindingSparkline slot b spec
    | NodeKind.Display(DisplayKind.Progress spec) -> replaceBindingProgress slot b spec
    | NodeKind.Display(DisplayKind.LabelValueRow spec) -> replaceBindingLabelValueRow slot b spec
    | NodeKind.Layout(LayoutKind.Stepper spec) -> replaceBindingStepper slot b spec
    | NodeKind.Layout(LayoutKind.Tabs spec) -> replaceBindingTabs slot b spec
    | NodeKind.Layout(LayoutKind.Disclosure spec) -> replaceBindingDisclosure slot b spec
    | NodeKind.Input(InputKind.Button spec) -> replaceBindingButton slot b spec
    | NodeKind.Input(InputKind.Select spec) -> replaceBindingSelect slot b spec
    | NodeKind.Input(InputKind.Form spec) -> replaceBindingForm slot b spec
    | NodeKind.Input(InputKind.FileUpload spec) -> replaceBindingFileUpload slot b spec
    | NodeKind.Visualisation(VisKind.DataGrid spec) -> replaceBindingGrid slot b spec
    | NodeKind.Visualisation(VisKind.Chart spec) -> replaceBindingChart slot b spec
    | NodeKind.Visualisation(VisKind.Map spec) -> replaceBindingMap slot b spec
    | _ -> Error(slotNotFound kind (NodeId "_") slot)

// ─── Core.Ops delegation (Phase 379) ───────────────────────────────────────
// The structural-five legs (InsertChild / RemoveNode / MoveNode / ReorderChildren,
// plus a Batch's structural members via the applyOne fold) run through
// `Fuaran.Core.Ops` at runtime rather than a second hand-rolled tree walker: UI's
// `Node<'Msg>` is a homogeneous-at-the-protocol-level tree, so it presents the Core
// `NodeWitness` / `IdWitness` and Core's certified skeleton engine does the surgery.
// The per-kind vertical (UpdateProp / ReplaceBinding / EditNode / UpdateStyle /
// UpdateState) and the whole-tree op (ReplaceRoot) stay UI-owned — that is domain
// slice, not skeleton. `Node<'Msg>` needs no `: equality` here: Core.Ops compares
// only ids (via the IdWitness), never whole nodes — node equality is a
// conformance-law concern, bridged test-side in CoreAdoptionTests.
//
// The Core rejection envelope is total but coarser than UI's §4d `ApplyError`; the
// per-op mappers below translate each `Rejection` back onto the UI code + hints,
// re-deriving the kind-aware hint (`kindName`, available fields) from `root`. The
// two UI-specific legalities Core does not model as distinct codes — the
// move-into-self / move-into-descendant distinction, and ReorderChildren on a
// childless leaf — are preserved by cheap pre-checks (containment legality stays
// domain-side, exactly as CanHold does).

let private coreIdw: Fuaran.Core.IdWitness<NodeId> =
    { ToString = fun (NodeId s) -> s
      OfString = NodeId
      Equals = (=) }

let private applyStructural (op: TreeOp<'Msg>) (root: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
    let nodew: Fuaran.Core.NodeWitness<Node<'Msg>, NodeId> =
        { Id = fun n -> n.Id
          KindTag = fun n -> kindName n.Kind
          Children = fun n -> getChildren n.Kind |> Option.defaultValue []
          ReplaceChildren =
            fun n cs ->
                match withChildren n.Kind cs with
                | Some k -> { n with Kind = k }
                | None -> n }

    // A Layout holds children; every Display / Input / Visualisation leaf does not.
    let canHold (n: Node<'Msg>) = getChildren n.Kind |> Option.isSome

    // `NotAContainer` against an existing parent → UI's kind-aware ChildlessKind.
    let childlessOrParent (pid: NodeId) : ApplyError =
        match findNode pid root with
        | Some n -> childlessKind n.Kind pid
        | None -> parentNotFound pid

    let unmapped (rej: Fuaran.Core.Rejection<NodeId>) : ApplyError =
        kindMismatch (sprintf "Structural op rejected: %A" rej) ApplyHint.empty

    let run (coreOp: Fuaran.Core.SkeletonOp<Node<'Msg>, NodeId>) (mapRej: Fuaran.Core.Rejection<NodeId> -> ApplyError) =
        match Fuaran.Core.Ops.applyContained canHold nodew coreIdw coreOp root with
        | Ok updated -> Ok updated
        | Error rej -> Error(mapRej rej)

    match op with
    | TreeOp.InsertChild(parentId, position, child) ->
        run (Fuaran.Core.SkeletonOp.InsertChild(parentId, position, child)) (fun rej ->
            match rej with
            | Fuaran.Core.Rejection.DuplicateId id -> duplicateNodeId id
            | Fuaran.Core.Rejection.NotAContainer _ -> childlessOrParent parentId
            | Fuaran.Core.Rejection.IndexOutOfRange(_, _, count) -> positionOutOfRange parentId count position
            | Fuaran.Core.Rejection.UnknownNode _ -> parentNotFound parentId
            | other -> unmapped other)

    | TreeOp.RemoveNode target ->
        run (Fuaran.Core.SkeletonOp.RemoveNode target) (fun rej ->
            match rej with
            | Fuaran.Core.Rejection.CannotRemoveRoot ->
                kindMismatch
                    "Cannot RemoveNode the root."
                    { ApplyHint.empty with
                        Suggestion = Some "Compose a new tree instead of removing the root." }
            | Fuaran.Core.Rejection.UnknownNode _ -> nodeNotFound target
            | other -> unmapped other)

    | TreeOp.ReorderChildren(parentId, newOrder) ->
        // Core's reorder validator has no container check — a leaf presents empty
        // children, so a non-empty reorder would surface as ReorderMismatch rather
        // than UI's ChildlessKind. Preserve the domain contract with a pre-check.
        match findNode parentId root with
        | Some parent when Option.isNone (getChildren parent.Kind) -> Error(childlessKind parent.Kind parentId)
        | _ ->
            run (Fuaran.Core.SkeletonOp.ReorderChildren(parentId, newOrder)) (fun rej ->
                match rej with
                | Fuaran.Core.Rejection.UnknownNode _ -> parentNotFound parentId
                | Fuaran.Core.Rejection.ReorderMismatch(_, expected, _) -> orderingMismatch parentId expected
                | other -> unmapped other)

    | TreeOp.MoveNode(target, newParentId, newPosition) ->
        // Preserve UI's self / cycle codes+messages, which Core collapses into
        // WouldNestUnderSelf/CannotRemoveRoot and checks in a different order than
        // its canHold gate. After these two pre-checks Core sees only existence /
        // container / index failures on the remove-then-insert it performs.
        if target = newParentId then
            Error(
                kindMismatch
                    "Cannot move a node into itself."
                    { ApplyHint.empty with
                        Suggestion = Some "Pick a different newParentId." }
            )
        elif isAncestorOf target newParentId root then
            Error(
                kindMismatch
                    "Cannot move a node into its own descendant (would create a cycle)."
                    { ApplyHint.empty with
                        Suggestion = Some "Pick a newParentId outside the target's subtree." }
            )
        else
            run (Fuaran.Core.SkeletonOp.MoveNode(target, newParentId, newPosition)) (fun rej ->
                match rej with
                | Fuaran.Core.Rejection.NotAContainer _ -> childlessOrParent newParentId
                | Fuaran.Core.Rejection.IndexOutOfRange(_, _, count) ->
                    positionOutOfRange newParentId count newPosition
                | Fuaran.Core.Rejection.UnknownNode(id, _) ->
                    if id = target then
                        nodeNotFound target
                    else
                        parentNotFound newParentId
                | other -> unmapped other)

    | _ -> failwith "applyStructural: only the structural-five legs route here"

// ─── Single-op apply ───────────────────────────────────────────────────────

let rec private applyOne (op: TreeOp<'Msg>) (root: Node<'Msg>) : Result<Node<'Msg>, ApplyError> =
    match op with
    | TreeOp.EditNode(target, newKind) ->
        match mapNode target (fun n -> { n with Kind = newKind }) root with
        | Some updated -> Ok updated
        | None -> Error(nodeNotFound target)

    | TreeOp.UpdateProp(target, path, value) ->
        let v = PropValue.toObj value

        match parsePath path with
        | Error reason ->
            // Grammar violation. Resolve the target (when present) purely to
            // enrich the hint with the kind's addressable paths.
            let resolvedKind = findNode target root |> Option.map _.Kind
            Error(pathInvalidWith path reason resolvedKind)
        | Ok segs ->
            match findNode target root with
            | None -> Error(nodeNotFound target)
            | Some targetNode ->
                let finish newKind =
                    match mapNode target (fun n -> { n with Kind = newKind }) root with
                    | Some updated -> Ok updated
                    | None -> Error(nodeNotFound target)

                let valueTypeMismatch (detail: string) =
                    Error(
                        kindMismatch
                            (sprintf
                                "UpdateProp value for '%s' on node '%s' does not match the field's expected type: %s"
                                path
                                (match target with
                                 | NodeId s -> s)
                                detail)
                            { ApplyHint.empty with
                                NodeKind = Some(kindName targetNode.Kind)
                                AvailableFields = availableFields targetNode.Kind }
                    )

                match segs with
                | [ { Field = field; Index = None } ] ->
                    // Top-level path — the original per-kind field dispatch.
                    match dispatchUpdateField field v targetNode.Kind with
                    | Updated newKind -> finish newKind
                    | UnknownField ->
                        let otherNodes = nodesWithField field root |> List.filter (fun id -> id <> target)

                        Error(fieldNotFound targetNode.Kind target field otherNodes)
                    | NotSupportedYet -> Error(pathNotSupportedYet targetNode.Kind target field)
                    | TypeMismatch detail -> valueTypeMismatch detail
                | _ ->
                    // Nested path (Phase 364) — the per-kind typed traversal.
                    match dispatchNestedUpdate segs v targetNode.Kind with
                    | NestedUpdated newKind -> finish newKind
                    | NestedMissingIndex(listField, count) ->
                        Error(missingListIndex targetNode.Kind target listField count)
                    | NestedIndexOutOfRange(listField, count, requested) ->
                        Error(nestedIndexOutOfRange targetNode.Kind target listField count requested)
                    | NestedFieldNotFound(failingSegment, availableAtSegment) ->
                        Error(nestedFieldNotFound targetNode.Kind target path failingSegment availableAtSegment)
                    | NestedNotSupported -> Error(pathNotSupportedYet targetNode.Kind target path)
                    | NestedTypeMismatch detail -> valueTypeMismatch detail

    | TreeOp.ReplaceBinding(target, slot, binding) ->
        match findNode target root with
        | None -> Error(nodeNotFound target)
        | Some targetNode ->
            match dispatchReplaceBinding slot binding targetNode.Kind with
            | Ok newKind ->
                match mapNode target (fun n -> { n with Kind = newKind }) root with
                | Some updated -> Ok updated
                | None -> Error(nodeNotFound target)
            | Error err ->
                // Repair the slotNotFound id placeholder with the real target id.
                match err.Code with
                | ApplyErrorCode.SlotNotFound -> Error(slotNotFound targetNode.Kind target slot)
                | _ -> Error err

    | TreeOp.UpdateStyle(target, style) ->
        match mapNode target (fun n -> { n with Style = style }) root with
        | Some updated -> Ok updated
        | None -> Error(nodeNotFound target)

    | TreeOp.UpdateState(target, state) ->
        match mapNode target (fun n -> { n with State = state }) root with
        | Some updated -> Ok updated
        | None -> Error(nodeNotFound target)

    | TreeOp.InsertChild _
    | TreeOp.RemoveNode _
    | TreeOp.MoveNode _
    | TreeOp.ReorderChildren _ ->
        // The structural-five legs run through `Fuaran.Core.Ops` (Phase 379). See the
        // Core.Ops delegation block above for the witnesses + rejection mapping.
        applyStructural op root

    | TreeOp.ReplaceRoot node ->
        // The whole-tree swap: the only op that legally changes the root node id.
        Ok node

    | TreeOp.Batch inner ->
        let rec loop idx state remaining =
            match remaining with
            | [] -> Ok state
            | next :: rest ->
                match applyOne next state with
                | Ok updated -> loop (idx + 1) updated rest
                | Error err ->
                    Error
                        { err with
                            Code = ApplyErrorCode.BatchAborted idx
                            Message = sprintf "Batch aborted at inner op #%d: %s" idx err.Message }

        match loop 0 root inner with
        | Ok updated -> Ok updated
        | Error err ->
            // All-or-nothing: do NOT return any partial state. Caller's
            // original `root` reference is unchanged; the Error path returns
            // the structured failure so the AI can recover.
            Error err

// ─── Public entry ──────────────────────────────────────────────────────────

/// Apply a single tree-op against `root`, returning either the updated tree
/// or a structured §4d AI-recovery error. Callers fold this themselves to
/// apply an ordered op list; for atomic application of multiple ops, wrap
/// in `TreeOp.Batch`.
let apply (op: TreeOp<'Msg>) (root: Node<'Msg>) : Result<Node<'Msg>, ApplyError> = applyOne op root
