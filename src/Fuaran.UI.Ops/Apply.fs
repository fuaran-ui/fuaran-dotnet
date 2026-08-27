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
/// `mapBinding` is the converter-parametric walker; `castBinding` composes it
/// with `unbox<'T>` (the historical shape), and the rows slot composes it with
/// the structural `castRowSeq` (fuaran#665 — an element-wise reshape `unbox`
/// alone cannot express).
let rec private mapBinding<'T> (conv: obj -> 'T) (b: Binding<obj>) : Binding<'T> =
    match b with
    | Binding.Static v -> Binding.Static(v |> Option.map conv)
    | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> conv, dependsOn)
    | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map conv)
    | Binding.Selection(id, accessor, dv, fld) -> Binding.Selection(id, accessor >> conv, dv |> Option.map conv, fld)
    | Binding.State(key, defaultValue) -> Binding.State(key, defaultValue |> Option.map conv)
    | Binding.Computed f -> Binding.Computed(f >> conv)
    // Phase 765 — the host furnishes the instant; the accessor composes like
    // any other obj-erased source.
    | Binding.Now accessor -> Binding.Now(accessor >> conv)
    // i18n bindings carry only string key + JVal-typed args, no
    // 'T payload to cast. Pass through; the resolver enforces 'T = string at
    // resolution time.
    | Binding.I18n(key, args) -> Binding.I18n(key, args)
    // Local bindings carry an initialFrom of the same 'T plus
    // obj-erased onCommit / format / parse. Recurse initialFrom; box-wrap
    // 'T → obj on the obj-typed projections so the typed payload matches.
    | Binding.Local(flushOn, format, initialFrom, onCommit, parse) ->
        Binding.Local(
            flushOn,
            (fun (t: 'T) -> format (box t)),
            mapBinding conv initialFrom,
            onCommit |> Option.map (fun oc -> fun (t: 'T) -> oc (box t)),
            (fun s -> parse s |> Result.map conv)
        )
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

let private castBinding<'T> (b: Binding<obj>) : Binding<'T> = mapBinding unbox<'T> b

/// fuaran#665 — reshape a boxed rows payload into the typed `Row seq`. A
/// decoded `ReplaceBinding` payload carries rows as an `obj` list whose
/// elements are boxed `Map<string,obj>` (`decodeObj`'s object shape), so the
/// element-wise unbox is exact; a host-built payload already carrying a
/// `Row seq` passes through the same route via `seq` covariance. EAGER by
/// design: a mismatched element must throw here, inside the guarded `try`,
/// not lazily at first enumeration during render.
let private castRowSeq (v: obj) : Row seq =
    unbox<obj seq> v |> Seq.toList |> List.map unbox<Row> |> Seq.ofList

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
        | Ok newSpec -> Updated(NodeKind.Metric(newSpec))
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
    | "TrendPolarity" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTrendPolarity v
            |> Result.map (fun x -> { spec with TrendPolarity = x }))
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Subtext" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Subtext = x }))
    | _ -> UnknownField

let private updateHeading (field: string) (v: obj) (spec: HeadingSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Heading(newSpec))
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
        | Ok x -> Updated(NodeKind.Markdown({ spec with Text = x }))
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

let private updateBadge (field: string) (v: obj) (spec: BadgeSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Badge(newSpec))
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
        | Ok newSpec -> Updated(NodeKind.Link(newSpec))
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
        | Ok x -> Updated(NodeKind.Skeleton({ spec with Rows = x }))
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

// Phase 821 — the standalone Icon display kind's field surface.
let private updateIcon (field: string) (v: obj) (spec: IconSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Icon(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryString v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Size" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIconSize v
            |> Result.map (fun x -> { spec with Size = x }))
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Label = x }))
    | _ -> UnknownField

let private updateCallout (field: string) (v: obj) (spec: CalloutSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Callout(newSpec))
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
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Dismissable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Dismissable = x }))
    | _ -> UnknownField

let private updateProgress (field: string) (v: obj) (spec: ProgressSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Progress(newSpec))
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
    let updated (s: BoxSpec<'Msg>) = Updated(NodeKind.Box(s))

    match field, spec.Layout with
    | "Orientation", LayoutMode.Flex(_, wrap, gap) ->
        match coerceField JsonDecode.Coerce.tryOrientation v with
        | Ok x ->
            updated
                { spec with
                    Layout = LayoutMode.Flex(x, wrap, gap) }
        | Error msg -> TypeMismatch msg
    | "Wrap", LayoutMode.Flex(direction, _, gap) ->
        match coerceField JsonDecode.Coerce.tryBool v with
        | Ok x ->
            updated
                { spec with
                    Layout = LayoutMode.Flex(direction, x, gap) }
        | Error msg -> TypeMismatch msg
    | "Cols", LayoutMode.Grid(_, templateColumns, gap) ->
        match coerceField JsonDecode.Coerce.tryInt v with
        | Ok x ->
            updated
                { spec with
                    Layout = LayoutMode.Grid(x, templateColumns, gap) }
        | Error msg -> TypeMismatch msg
    | "TemplateColumns", LayoutMode.Grid(cols, _, gap) ->
        // Additive optional `string option` field. Accepts either a raw string
        // (sugar — wraps in `Some`) or an explicit `string option` payload.
        match coerceField JsonDecode.Coerce.tryStringOption v with
        | Ok x ->
            updated
                { spec with
                    Layout = LayoutMode.Grid(cols, x, gap) }
        | Error _ ->
            match coerceField JsonDecode.Coerce.tryString v with
            | Ok x ->
                updated
                    { spec with
                        Layout = LayoutMode.Grid(cols, Some x, gap) }
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
        | Ok x -> Updated(NodeKind.SplitPanel({ spec with Weight = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateTabs (field: string) (v: obj) (spec: TabsSpec<'Msg>) : UpdateResult<'Msg> =
    match field with
    | "Orientation" ->
        match coerceField JsonDecode.Coerce.tryOrientation v with
        | Ok x -> Updated(NodeKind.Tabs({ spec with Orientation = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateStepper (field: string) (v: obj) (spec: StepperSpec<'Msg>) : UpdateResult<'Msg> =
    match field with
    | "ActiveStep" ->
        match coerceField JsonDecode.Coerce.tryBindingInt v with
        | Ok x -> Updated(NodeKind.Stepper({ spec with ActiveStep = x }))
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
        | Ok x -> Updated(NodeKind.SummaryList({ spec with Heading = x }))
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
        | Ok x -> Updated(NodeKind.Disclosure({ spec with Heading = x }))
        | Error msg -> TypeMismatch msg
    | "Open" ->
        match coerceField JsonDecode.Coerce.tryBindingBool v with
        | Ok x -> Updated(NodeKind.Disclosure({ spec with Open = x }))
        | Error msg -> TypeMismatch msg
    | "DefaultOpen" ->
        match coerceField JsonDecode.Coerce.tryBool v with
        | Ok x -> Updated(NodeKind.Disclosure({ spec with DefaultOpen = x }))
        | Error msg -> TypeMismatch msg
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateLabelValueRow (field: string) (v: obj) (spec: LabelValueRowSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.LabelValueRow(newSpec))
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
        | Ok newSpec -> Updated(NodeKind.Fact(newSpec))
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
            coerceField JsonDecode.Coerce.tryStringOption v
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
        | Ok name -> Updated(NodeKind.FragmentDecl { spec with Name = name })
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
        | Ok name -> Updated(NodeKind.FragmentRef { spec with Name = name })
        | Error msg -> TypeMismatch msg
    | _ -> UnknownField

// ─── Input-family field updates ────────────────────────────────────────────
//
// The whole `NodeKind.Input` family previously returned `NotSupportedYet` for
// every top-level path, so no input control was editable field-by-field: the
// only way to change a button's text was to swap the entire node via
// `EditNode`. "Change this button's label" is about as ordinary as UI edits
// get, and `Introspect.availableFields` was already ADVERTISING `Label` for a
// Button — so the hint pointed authors at a path that then failed, which is a
// hint that manufactures the retry it exists to prevent.
//
// The division of labour these arms follow is the one the rest of the engine
// already uses, and it is why the handler and binding fields stay unsupported
// rather than being wired here:
//
//   * UpdateProp      — literal, field-shaped values (a label, a variant, a
//                       flag, an accept list).
//   * ReplaceBinding  — `Binding<_>` slots (`Select.Source` / `Select.Value`,
//                       and every kind's optional `Disabled`), which is what
//                       `Introspect.bindingSlots` enumerates them for.
//   * EditNode        — `Action<_>` handlers and closure-bearing fields
//                       (`OnClick`, `OnSubmit`, `OnChange`, `OnSelect`), which
//                       are not expressible as a wire value at all.
//
// A closure-bearing or binding field therefore reports `NotSupportedYet` (which
// names the right op in its remediation) rather than `UnknownField` (which
// would claim the field does not exist).

let private updateButton (field: string) (v: obj) (spec: ButtonSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Button(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Variant" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryButtonVariant v
            |> Result.map (fun x -> { spec with Variant = x }))
    | "Icon" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with Icon = x }))
    | "Tooltip" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Tooltip = x }))
    | "OnClick"
    | "Disabled" -> NotSupportedYet
    | _ -> UnknownField

let private updateSelect (field: string) (v: obj) (spec: SelectSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Select(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Placeholder" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Placeholder = x }))
    | "Source"
    | "Value"
    | "OnChange"
    | "Disabled" -> NotSupportedYet
    | _ -> UnknownField

let private updateFileUpload (field: string) (v: obj) (spec: FileUploadSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.FileUpload(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Label" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Label = x }))
    | "Accept" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringList v
            |> Result.map (fun x -> { spec with Accept = x }))
    | "Multiple" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Multiple = x }))
    | "OnSelect"
    | "Disabled" -> NotSupportedYet
    | _ -> UnknownField

let private updateForm (field: string) (v: obj) (spec: FormSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Form(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "SubmitLabel" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with SubmitLabel = x }))
    // `Fields` is a collection: its items are addressed by the NESTED surface
    // (`Fields[i].Label` and friends, per Introspect.availableNestedPaths and
    // `updateNestedForm`), never replaced wholesale through a top-level path.
    | "Fields"
    | "OnSubmit"
    | "Disabled" -> NotSupportedYet
    | _ -> UnknownField

// ─── Display / Layout / Visualisation kinds added after the original table ──
//
// These were unwired for one reason, and it is worth naming because it is not
// a design constraint: each cited the previous omission as precedent. The chain
// begins with `Sparkline`, whose omission is CORRECT — its only field is a
// `Binding`, which is `ReplaceBinding`'s territory, so there is genuinely
// nothing for `UpdateProp` to do there. That valid special case was then quoted
// by kinds with two to five plainly settable fields each, across roughly 250
// phase numbers, and every individual instance looked reasonable beside the one
// before it. None of them was blocked by anything structural.
//
// The division of labour is unchanged: `Binding` slots stay `ReplaceBinding`'s,
// `Action` handlers and child subtrees stay `EditNode`'s and the structural
// ops', and both report `NotSupportedYet` so the refusal names the right op
// rather than denying the field exists.

let private updateImage (field: string) (v: obj) (spec: ImageSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Image(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Alt" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Alt = x }))
    | "Variant" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryImageVariant v
            |> Result.map (fun x -> { spec with Variant = x }))
    // Phase 1077 — the presentation slots are ordinary closed-enum fields, so
    // they join the field-level UpdateProp surface on the same terms as
    // `Variant`.
    | "Fit" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryImageFit v
            |> Result.map (fun x -> { spec with Fit = x }))
    | "AspectRatio" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryImageAspect v
            |> Result.map (fun x -> { spec with AspectRatio = x }))
    | "Loading" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryImageLoading v
            |> Result.map (fun x -> { spec with Loading = x }))
    // `Src` is a Binding with no ReplaceBinding slot declared for Image, so it
    // is reachable only via EditNode today. Deliberately not advertised.
    | "Src" -> NotSupportedYet
    | _ -> UnknownField

let private updateToast (field: string) (v: obj) (spec: ToastSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Toast(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Message" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSource v
            |> Result.map (fun x -> { spec with Message = x }))
    | "Tone" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTone v
            |> Result.map (fun x -> { spec with Tone = x }))
    | "Dismissable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Dismissable = x }))
    | "Open" -> NotSupportedYet
    | _ -> UnknownField

let private updateMath (field: string) (v: obj) (spec: MathSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Math(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Source" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryString v
            |> Result.map (fun x -> { spec with Source = x }))
    | "Display" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryMathDisplay v
            |> Result.map (fun x -> { spec with Display = x }))
    | _ -> UnknownField

let private updateCodeBlock (field: string) (v: obj) (spec: CodeBlockSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.CodeBlock(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Code" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryString v
            |> Result.map (fun x -> { spec with Code = x }))
    | "Language" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryString v
            |> Result.map (fun x -> { spec with Language = x }))
    | "LineNumbers" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with LineNumbers = x }))
    | "HighlightLines" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIntList v
            |> Result.map (fun x -> { spec with HighlightLines = x }))
    | "Copyable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Copyable = x }))
    | _ -> UnknownField

let private updateList (field: string) (v: obj) (spec: ListSpec) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.List(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Items" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceList v
            |> Result.map (fun x -> { spec with Items = x }))
    | "Ordered" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Ordered = x }))
    | _ -> UnknownField

let private updateModal (field: string) (v: obj) (spec: ModalSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Modal(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Heading" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Heading = x }))
    | "Dismissable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Dismissable = x }))
    // `Open` is a Binding with no declared ReplaceBinding slot for Modal (a gap
    // worth closing separately); `Children` is the structural ops' surface;
    // `OnDismiss` is an Action.
    | "Open"
    | "Children"
    | "OnDismiss" -> NotSupportedYet
    | _ -> UnknownField

let private updateScrollArea (field: string) (v: obj) (spec: ScrollAreaSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.ScrollArea(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Orientation" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryScrollOrientation v
            |> Result.map (fun x -> { spec with Orientation = x }))
    | "MaxHeight" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIntOption v
            |> Result.map (fun x -> { spec with MaxHeight = x }))
    | "MaxWidth" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryIntOption v
            |> Result.map (fun x -> { spec with MaxWidth = x }))
    | "Children" -> NotSupportedYet
    | _ -> UnknownField

let private updateChartTop (field: string) (v: obj) (spec: ChartSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Chart(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Kind" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryChartKind v
            |> Result.map (fun x -> { spec with Kind = x }))
    | "XField" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryString v
            |> Result.map (fun x -> { spec with XField = x }))
    | "YFields" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringList v
            |> Result.map (fun x -> { spec with YFields = x }))
    | "Title" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryTextSourceOption v
            |> Result.map (fun x -> { spec with Title = x }))
    | "Stacked" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Stacked = x }))
    | "Source"
    | "OnPointClick" -> NotSupportedYet
    | _ -> UnknownField

let private updateGridTop (field: string) (v: obj) (spec: GridSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.DataGrid(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "Editable" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryBool v
            |> Result.map (fun x -> { spec with Editable = x }))
    | "RowKeyField" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryStringOption v
            |> Result.map (fun x -> { spec with RowKeyField = x }))
    // `Columns` is addressed through the nested surface (`Columns[i].Label`);
    // `Source` is a ReplaceBinding slot; the rest are closures.
    | "Source"
    | "Columns"
    | "RowKey"
    | "OnRowClick"
    | "StaticRows" -> NotSupportedYet
    | _ -> UnknownField

let private updateMapTop (field: string) (v: obj) (spec: MapSpec<'Msg>) : UpdateResult<'Msg> =
    let wrap f =
        match f v with
        | Ok newSpec -> Updated(NodeKind.Map(newSpec))
        | Error msg -> TypeMismatch msg

    match field with
    | "CentreLatitude" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryFloat v
            |> Result.map (fun x -> { spec with CentreLatitude = x }))
    | "CentreLongitude" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryFloat v
            |> Result.map (fun x -> { spec with CentreLongitude = x }))
    | "Zoom" ->
        wrap (fun v ->
            coerceField JsonDecode.Coerce.tryInt v
            |> Result.map (fun x -> { spec with Zoom = x }))
    | "Source"
    | "OnMarkerClick" -> NotSupportedYet
    | _ -> UnknownField

let private dispatchUpdateField (field: string) (v: obj) (kind: NodeKind<'Msg>) : UpdateResult<'Msg> =
    match kind with
    // -- Layout --
    | NodeKind.Box spec -> updateBox field v spec
    | NodeKind.SplitPanel spec -> updateSplitPanel field v spec
    | NodeKind.Tabs spec -> updateTabs field v spec
    | NodeKind.Stepper spec -> updateStepper field v spec
    | NodeKind.SummaryList spec -> updateSummaryList field v spec
    | NodeKind.Disclosure spec -> updateDisclosure field v spec
    | NodeKind.Modal spec -> updateModal field v spec
    | NodeKind.ScrollArea spec -> updateScrollArea field v spec
    // -- Display --
    | NodeKind.Heading spec -> updateHeading field v spec
    | NodeKind.Markdown spec -> updateMarkdown field v spec
    | NodeKind.Metric spec -> updateMetric field v spec
    | NodeKind.Badge spec -> updateBadge field v spec
    // Sparkline's omission is PRINCIPLED, not a to-do, and this comment is
    // load-bearing because the previous wording read as precedent and was
    // quoted as such by five later kinds. `SparklineSpec` has exactly one
    // field, `Source: Binding<float seq>`, which is a declared
    // `ReplaceBinding` slot. There is no field-shaped value here for
    // `UpdateProp` to set, so wiring it would add an arm that could only
    // ever refuse. Do NOT cite this as a reason to skip another kind:
    // check whether that kind has settable fields, which is a question
    // about that kind and not about this one.
    | NodeKind.Sparkline _ -> NotSupportedYet
    | NodeKind.Callout spec -> updateCallout field v spec
    | NodeKind.Progress spec -> updateProgress field v spec
    | NodeKind.Skeleton spec -> updateSkeleton field v spec
    | NodeKind.Icon spec -> updateIcon field v spec
    | NodeKind.LabelValueRow spec -> updateLabelValueRow field v spec
    | NodeKind.Fact spec -> updateFact field v spec
    | NodeKind.Link spec -> updateLink field v spec
    | NodeKind.Image spec -> updateImage field v spec
    | NodeKind.List spec -> updateList field v spec
    | NodeKind.Toast spec -> updateToast field v spec
    // The old justification here claimed the spec is "mostly literal
    // strings, not the bound surface UpdateProp targets". That had it
    // backwards: literal strings are exactly what UpdateProp sets
    // (`Markdown.Text`, `Heading.Text`), and "change the code in this
    // block" is an obvious edit. Wired.
    | NodeKind.CodeBlock spec -> updateCodeBlock field v spec
    | NodeKind.Math spec -> updateMath field v spec
    // Phase 524 — Drawing field-level UpdateProp not wired (a Drawing is a
    // whole-artefact swap via EditNode); whole-node swap remains available.
    | NodeKind.Drawing _ -> NotSupportedYet
    // -- Input --
    | NodeKind.Button spec -> updateButton field v spec
    | NodeKind.Select spec -> updateSelect field v spec
    | NodeKind.FileUpload spec -> updateFileUpload field v spec
    | NodeKind.Form spec -> updateForm field v spec
    // Filters carries a bare `FilterSpec list`, not a record, so it has no
    // top-level field surface at all — `Introspect.availableFields` already
    // reports `[]` for it, and the two agree.
    | NodeKind.Filters _ -> NotSupportedYet
    // -- Visualisation --
    | NodeKind.Chart spec -> updateChartTop field v spec
    | NodeKind.DataGrid spec -> updateGridTop field v spec
    | NodeKind.Map spec -> updateMapTop field v spec
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

/// Canonicalise a path segment's field name to the PascalCase spelling the
/// per-kind matchers compare against.
///
/// Wire field names are camelCase — that is how every field of every node is
/// spelled on the wire, and how the didactic teaches them — whereas
/// `UpdateProp.path` was resolved against the F# record field names, which are
/// PascalCase. So `{"path":"subtext"}` was refused `FieldNotFound` whilst
/// `{"path":"Subtext"}` applied: an author following the wire format everywhere
/// else was punished for being consistent.
///
/// This is not a hypothetical. Two independent model families both emitted the
/// camelCase spelling against a `Metric` when asked to change a caption, and
/// both were refused; the asymmetry was ours, not theirs. Accepting the wire
/// spelling is purely WIDENING — upper-casing an already-upper first character
/// is the identity, so no path that previously resolved changes meaning, and
/// nothing that previously applied now applies differently.
let private canonicaliseField (field: string) : string =
    if field.Length = 0 || System.Char.IsUpper field[0] then
        field
    else
        string (System.Char.ToUpperInvariant field[0]) + field.Substring 1

/// Drop a redundant leading `kind.` segment.
///
/// `UpdateProp.path` is rooted INSIDE the node's kind spec — `"subtext"`, not
/// `"kind.subtext"` — but the wire nests those fields under a `kind` object
/// (`{"id":"…","kind":{"$type":"Metric","subtext":"…"}}`), so that rooting
/// convention is a fact about the op surface that the serialised tree gives no
/// hint of. An author who reads the JSON in front of them and addresses the field
/// by the path it actually occupies writes `kind.subtext`, and was refused.
///
/// That is not a hypothetical either: it is the exact shape one of the two model
/// families emitted, whilst the other emitted the unprefixed spelling. Both
/// readings are defensible, so both are accepted.
///
/// Unambiguous, because a node has no addressable top-level field named `Kind` —
/// `kind` IS the spec container, and the two nested `Kind` sub-fields that exist
/// (`Columns[i].Kind`, `Fields[i].Kind`) are closure-bearing and deliberately
/// never addressable. Only a LEADING segment is stripped, and only when
/// something follows it, so `Columns[0].Kind` is untouched.
let private stripKindPrefix (segs: PathSeg list) : PathSeg list =
    match segs with
    | { Field = "Kind"; Index = None } :: rest when not rest.IsEmpty -> rest
    | _ -> segs

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
            NodeKind.DataGrid(
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
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.DataGrid(spec)))
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
                NodeKind.Chart(
                    { spec with
                        YFields = replaceAt i x spec.YFields }
                )
            )
        | Error msg -> NestedTypeMismatch msg
    | { Field = "YFields"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.Chart(spec)))
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
            NodeKind.Tabs(
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
            | Ok x ->
                // The TabHeader icon slot is a bare string since the swap; the
                // coercer's IconSource wrapper unwraps at this boundary.
                NestedUpdated(
                    rebuild
                        { hdr with
                            Icon = x |> Option.map (fun (IconSource s) -> s) }
                )
            | Error msg -> NestedTypeMismatch msg
        | "Disabled" ->
            // Optional typed binding; replacing it installs `Some`, mirroring
            // how Metric.Trend / Button.Disabled are set.
            match coerceField JsonDecode.Coerce.tryBindingBool v with
            | Ok x -> NestedUpdated(rebuild { hdr with Disabled = Some x })
            | Error msg -> NestedTypeMismatch msg
        | _ -> NestedFieldNotFound(leaf, [ "Label"; "Icon"; "Disabled" ])
    | { Field = "TabHeaders"; Index = Some _ } :: _ -> NestedNotSupported
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.Tabs(spec)))
    | [] -> NestedNotSupported

let private updateNestedForm (segs: PathSeg list) (v: obj) (spec: FormSpec<'Msg>) : NestedUpdate<'Msg> =
    match segs with
    | { Field = "Fields"; Index = None } :: _ -> NestedMissingIndex("Fields", spec.Fields.Length)
    | { Field = "Fields"; Index = Some i } :: _ when i >= spec.Fields.Length ->
        NestedIndexOutOfRange("Fields", spec.Fields.Length, i)
    | [ { Field = "Fields"; Index = Some i }; { Field = leaf; Index = None } ] ->
        let fld = spec.Fields[i]

        let rebuild f =
            NodeKind.Form(
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
    | { Field = f; Index = _ } :: _ -> NestedFieldNotFound(f, availableFields (NodeKind.Form(spec)))
    | [] -> NestedNotSupported

let private dispatchNestedUpdate (segs: PathSeg list) (v: obj) (kind: NodeKind<'Msg>) : NestedUpdate<'Msg> =
    match kind with
    | NodeKind.DataGrid(spec) -> updateNestedGrid segs v spec
    | NodeKind.Chart(spec) -> updateNestedChart segs v spec
    | NodeKind.Tabs(spec) -> updateNestedTabs segs v spec
    | NodeKind.Form(spec) -> updateNestedForm segs v spec
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
                NodeKind.Metric(
                    { spec with
                        Value = castBinding<float> b }
                )
            )
        | "Trend" ->
            Ok(
                NodeKind.Metric(
                    { spec with
                        Trend = Some(castBinding<float> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Metric(spec)) (NodeId "_") slot)
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
                NodeKind.Sparkline(
                    { spec with
                        Source = castBinding<float list> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Sparkline(spec)) (NodeId "_") slot)
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
                NodeKind.LabelValueRow(
                    { spec with
                        Value = castBinding<float> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.LabelValueRow(spec)) (NodeId "_") slot)
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
                NodeKind.Progress(
                    { spec with
                        Fraction = castBinding<float> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Progress(spec)) (NodeId "_") slot)
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
                NodeKind.Stepper(
                    { spec with
                        ActiveStep = castBinding<int> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Stepper(spec)) (NodeId "_") slot)
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
                NodeKind.DataGrid(
                    { spec with
                        Source = mapBinding castRowSeq b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.DataGrid(spec)) (NodeId "_") slot)
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
                NodeKind.Chart(
                    { spec with
                        Source = mapBinding castRowSeq b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Chart(spec)) (NodeId "_") slot)
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
                NodeKind.Map(
                    { spec with
                        Source = castBinding<MapMarker list> b }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Map(spec)) (NodeId "_") slot)
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
                NodeKind.Button(
                    { spec with
                        Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Button(spec)) (NodeId "_") slot)
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
                NodeKind.Select(
                    { spec with
                        Source = castBinding<SelectOption list> b }
                )
            )
        | "Value" ->
            Ok(
                NodeKind.Select(
                    { spec with
                        Value = castBinding<string> b }
                )
            )
        // Phase 130: optional bound disabled-state; replacing it installs
        // `Some`, mirroring Button.Disabled / Metric.Trend.
        | "Disabled" ->
            Ok(
                NodeKind.Select(
                    { spec with
                        Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Select(spec)) (NodeId "_") slot)
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
                NodeKind.Form(
                    { spec with
                        Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Form(spec)) (NodeId "_") slot)
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
                NodeKind.FileUpload(
                    { spec with
                        Disabled = Some(castBinding<bool> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.FileUpload(spec)) (NodeId "_") slot)
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
                NodeKind.Tabs(
                    { spec with
                        ActiveIndex = castBinding<int> b }
                )
            )
        // ActiveTag is an optional typed-tag overlay binding; replacing it
        // installs `Some`, mirroring how `Metric.Trend` is set.
        | "ActiveTag" ->
            Ok(
                NodeKind.Tabs(
                    { spec with
                        ActiveTag = Some(castBinding<string> b) }
                )
            )
        | _ -> Error(slotNotFound (NodeKind.Tabs(spec)) (NodeId "_") slot)
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
        | "Open" -> Ok(NodeKind.Disclosure({ spec with Open = castBinding<bool> b }))
        | _ -> Error(slotNotFound (NodeKind.Disclosure(spec)) (NodeId "_") slot)
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
    | NodeKind.Metric(spec) -> replaceBindingMetric slot b spec
    | NodeKind.Sparkline(spec) -> replaceBindingSparkline slot b spec
    | NodeKind.Progress(spec) -> replaceBindingProgress slot b spec
    | NodeKind.LabelValueRow(spec) -> replaceBindingLabelValueRow slot b spec
    | NodeKind.Stepper(spec) -> replaceBindingStepper slot b spec
    | NodeKind.Tabs(spec) -> replaceBindingTabs slot b spec
    | NodeKind.Disclosure(spec) -> replaceBindingDisclosure slot b spec
    | NodeKind.Button(spec) -> replaceBindingButton slot b spec
    | NodeKind.Select(spec) -> replaceBindingSelect slot b spec
    | NodeKind.Form(spec) -> replaceBindingForm slot b spec
    | NodeKind.FileUpload(spec) -> replaceBindingFileUpload slot b spec
    | NodeKind.DataGrid(spec) -> replaceBindingGrid slot b spec
    | NodeKind.Chart(spec) -> replaceBindingChart slot b spec
    | NodeKind.Map(spec) -> replaceBindingMap slot b spec
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
    | TreeOp.InsertChild(parentId, child) ->
        // Duplicate-id pre-check, ahead of Core.
        //
        // Core does its own, but through `nodew.Children` — which IS
        // `getChildren`, the STRUCTURAL surface. So Core's check cannot see a
        // node held in a Switch case, an ErrorBoundary slot, a `State`
        // alternative or a fragment `Slot` arg, and an insert colliding with one
        // of those ids was accepted. §4g promises ids are unique per tree, and
        // that promise is what `firstSharedId` was written to keep; it walks
        // `descendantNodes`, so it sees positions the structural surface omits.
        // Checking here rather than widening the witness is deliberate: the
        // witness's `Children` is also what Core REBUILDS through, so widening
        // it would have Core try to restructure keyed cases as an ordered list.
        match firstSharedId root child with
        | Some dup -> Error(duplicateNodeId dup)
        | None ->

            run (Fuaran.Core.SkeletonOp.InsertChild(parentId, child)) (fun rej ->
                match rej with
                | Fuaran.Core.Rejection.DuplicateId id -> duplicateNodeId id
                | Fuaran.Core.Rejection.NotAContainer _ -> childlessOrParent parentId
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

    | TreeOp.MoveNode(target, newParentId) ->
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
            run (Fuaran.Core.SkeletonOp.MoveNode(target, newParentId)) (fun rej ->
                match rej with
                | Fuaran.Core.Rejection.NotAContainer _ -> childlessOrParent newParentId
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
        | Ok rawSegs ->
            // Accept the camelCase wire spelling of every segment, top-level and
            // nested alike (see `canonicaliseField`). Applied here rather than in
            // `parsePath` so the grammar diagnostics above still quote exactly
            // what the author wrote.
            let segs =
                rawSegs
                |> List.map (fun seg ->
                    { seg with
                        Field = canonicaliseField seg.Field })
                |> stripKindPrefix

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
        // `Node.Style` is an option since the swap; `None` is the canonical
        // default form (the encoder omits the key on `None`, where a
        // `Some Defaults.style` would emit an empty `"style":{}`), so a
        // default-valued payload normalises to `None` here.
        let normalised =
            if style = Fuaran.UI.Defaults.style then
                None
            else
                Some style

        match mapNode target (fun n -> { n with Style = normalised }) root with
        | Some updated -> Ok updated
        | None -> Error(nodeNotFound target)

    | TreeOp.UpdateState(target, state) ->
        // Same normalisation as UpdateStyle — an all-`None` StateBehaviour is
        // the canonical `None` envelope slot.
        let normalised =
            if state.OnLoading.IsNone && state.OnEmpty.IsNone && state.OnError.IsNone then
                None
            else
                Some state

        match mapNode target (fun n -> { n with State = normalised }) root with
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
