namespace Fuaran.UI

open Fuaran.Core

// F# 10 nullness (FS3261) is scoped where it fires — at the obj-erasure
// boundary in `Column.erase` (lines 121-150) and `Fuaran.grid` (lines 257-271).
// Both turn typed accessors into obj-typed closures via `box` / `unbox<'T>`;
// the smart-ctor layer IS the type-erasure boundary, so nullness annotations
// there are noise. Typed surfaces (Types.fs, Defaults.fs) keep nullness on.

// ============================================================================
//  Fuaran — smart constructors (§4c lines 504–542, idioms lines 585–626)
//
//  Module layout (matches the §4c author surface):
//   - Components are under `Fuaran.X`         (`Fuaran.dashboard`, `Fuaran.metric`, ...)
//   - Cross-cutting helpers are top-level    (`binding.query`,  `Action.dispatch`,
//                                             `format.currency`, `Column.text`,
//                                             `Node.onLoading`)
//
//  Per-Defect (1)+(2): typed entry points box typed accessors into the
//  tree-level obj-erased shapes. Authors never see `obj`.
// ============================================================================

open Fuaran.UI.Types

// ─── Per-Node postfix-pipe modifiers ───────────────────────────────────────

module Node =
    // Since the swap the Node envelope carries `State` / `Style` as OPTIONS —
    // `None` is the canonical empty-state / default-style shape (the encoder
    // omits both). The helpers below materialise the default locally, apply
    // the change, and NORMALISE back to `None` when the result is the empty /
    // default record, so the wire stays byte-identical with the pre-swap
    // always-present-record behaviour.

    let private stateOf (node: Node<'Msg>) : StateBehaviour<'Msg> =
        node.State |> Option.defaultValue Defaults.stateBehaviour<'Msg>

    let private normaliseState (s: StateBehaviour<'Msg>) : StateBehaviour<'Msg> option =
        match s.OnLoading, s.OnEmpty, s.OnError with
        | Option.None, Option.None, Option.None -> Option.None
        | _ -> Some s

    let private styleOf (node: Node<'Msg>) : SemanticStyle =
        node.Style |> Option.defaultValue Defaults.style

    let private normaliseStyle (s: SemanticStyle) : SemanticStyle option =
        // Enum-only record — structural equality is safe here.
        if s = Defaults.style then Option.None else Some s

    /// The node's id as the ops/API-boundary `NodeId` wrapper. The generated
    /// envelope carries `Id` as a bare `string`; wrap at the boundary where a
    /// `NodeId` value is needed (NodeMap keys, TreeOp args, stores).
    let nodeId (node: Node<'Msg>) : NodeId = NodeId node.Id

    let onLoading (placeholder: Node<'Msg>) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            State =
                Some
                    { stateOf node with
                        OnLoading = Some placeholder } }

    let onEmpty (placeholder: Node<'Msg>) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            State =
                Some
                    { stateOf node with
                        OnEmpty = Some placeholder } }

    let onError (render: ErrorPayload -> Node<'Msg>) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            State =
                Some
                    { stateOf node with
                        OnError = Some render } }

    let withTone (tone: ToneVariant) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            Style = normaliseStyle { styleOf node with Tone = tone } }

    let withWeight (weight: StyleWeight) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            Style = normaliseStyle { styleOf node with Weight = weight } }

    let withEmphasis (emphasis: Emphasis) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            Style =
                normaliseStyle
                    { styleOf node with
                        Emphasis = emphasis } }

    /// Tag the node with a semantic content role (Phase 147) — emits a
    /// `fuaran-role-{role}` class the host CSS owns. `StyleRole.None` clears
    /// the role (back to the byte-identical default). On a `Heading`, prefer
    /// `HeadingVariant.Eyebrow` / `Caption` over the matching `StyleRole`.
    let withRole (role: StyleRole) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            Style = normaliseStyle { styleOf node with Role = role } }

    /// Tag the node's font voice (Phase 147) — the display-vs-structural
    /// split. Emits a `fuaran-voice-{voice}` class; `FontVoice.Default`
    /// clears it.
    let withVoice (voice: FontVoice) (node: Node<'Msg>) : Node<'Msg> =
        { node with
            Style = normaliseStyle { styleOf node with Voice = voice } }

    /// Replace the Node's `Accessibility` trait with the supplied
    /// value. Use `Some Defaults.Accessibility.empty` to start from an empty
    /// trait and override individual fields with record-with syntax.
    let withAccessibility (a11y: Accessibility option) (node: Node<'Msg>) : Node<'Msg> =
        { node with Accessibility = a11y }

    /// Opt into one of the 8 canonical motion tokens for this
    /// node. `Motion.None` is allowed (emits the `fuaran-motion-none` no-op
    /// hook) so consumers can author CSS overrides keyed on it.
    let withMotion (motion: Motion) (node: Node<'Msg>) : Node<'Msg> = { node with Motion = Some motion }

    /// Consumer-side hatch for `data-*` / `aria-*`
    /// attributes (test hooks, analytics tags). Keys are validated against
    /// the `data-*` / `aria-*` prefix policy; non-conforming keys are
    /// dropped and a warning is written to stderr (consistent with the
    /// `DiagnosticRuntime` shape). Use record-with syntax to bypass the
    /// validator when populating from a wire payload you've already
    /// validated upstream.
    ///
    /// Intentionally separate from `Accessibility.Label` — that field is
    /// AI-authorable; this one is consumer-only (the §4d JSON wire shape
    /// omits `ExtraAttributes` on emit, so the AI never sees it).
    let withExtraAttribute (key: string) (value: string) (node: Node<'Msg>) : Node<'Msg> =
        // F# 10 nullness model says `key: string` is non-null; the .NET-side
        // typechecker enforces this at the call site. Fable consumers can
        // still feed `null` via JS interop, in which case `.Trim()` throws —
        // an unambiguous runtime error rather than a silent "" key.
        let trimmedKey = key.Trim()

        let isAllowedPrefix =
            trimmedKey.StartsWith("data-") || trimmedKey.StartsWith("aria-")

        if not isAllowedPrefix then
            eprintfn
                "[Fuaran] Node.withExtraAttribute: rejected key '%s' — only data-* and aria-* prefixes are allowed."
                trimmedKey

            node
        else
            let current =
                match node.ExtraAttributes with
                | Some m -> m
                | None -> Map.empty

            { node with
                ExtraAttributes = Some(Map.add trimmedKey value current) }

    /// The reserved `ExtraAttributes` key that marks a node as a hydration
    /// **island** (Phase 163). Render-time-only: `ExtraAttributes` is omitted
    /// from the §4d wire shape, so islands do not ride the wire (no
    /// forward-coupling). The server lifts this marker onto the island's
    /// boundary `<div data-fuaran-island="…">` wrapper (the hydration
    /// container); the client mounts `hydrateRoot` per island only.
    [<Literal>]
    let IslandAttributeKey = "data-fuaran-island"

    /// Mark a subtree as a hydration **island** (Phase 163). The server emits
    /// the page statically with a per-island boundary marker + a per-island
    /// hydrate payload; the client hydrates exactly the island subtrees,
    /// leaving everything outside an island as inert static HTML. A page with
    /// zero islands ships zero hydration script. Render-time-only (rides on
    /// the wire-omitted `ExtraAttributes`, so no wire-format change).
    let asIsland (islandId: string) (node: Node<'Msg>) : Node<'Msg> =
        withExtraAttribute IslandAttributeKey islandId node

    /// The island id a node carries (via `asIsland`), or `None`.
    let islandId (node: Node<'Msg>) : string option =
        node.ExtraAttributes |> Option.bind (Map.tryFind IslandAttributeKey)

    /// The node with its island marker removed — the inert inner content the
    /// island boundary wraps (server) and the client re-renders into the
    /// boundary (so the hydrated container's children are byte-identical to
    /// the server-rendered inner HTML — mismatch-free).
    let stripIsland (node: Node<'Msg>) : Node<'Msg> =
        match node.ExtraAttributes with
        | Some m when Map.containsKey IslandAttributeKey m ->
            let trimmed = Map.remove IslandAttributeKey m

            { node with
                ExtraAttributes = (if Map.isEmpty trimmed then None else Some trimmed) }
        | _ -> node

    /// Relabel a whole `Node<'a>` tree to `Node<'b>` (Phase 268): rewrites every
    /// `Action<'a>` slot across every `NodeKind` / spec by applying `f` to each
    /// dispatched message (and composing `f` through every action-returning
    /// closure), recursing through the whole tree. Pure + FSharp.Core-only; the
    /// exhaustive combinator lives in `NodeMap`. This is the erasure the typed
    /// `embed` convenience lowers through — `Node.mapMsg box` turns a typed
    /// child `Node<'sub>` into the `Node<obj>` a `Mount` guest requires. Prefer
    /// a structural map to an unsafe `box |> unbox<Node<obj>>` cast (which throws
    /// under .NET's reified generics).
    let mapMsg (f: 'a -> 'b) (node: Node<'a>) : Node<'b> = NodeMap.mapMsg f node

// ─── Typed binding entry points (§4c idioms lines 591–602) ────────────────

[<RequireQualifiedAccess>]
module binding =
    // Stage 1 of the 692-694 swap: these builders construct the GENERATED
    // `Binding<'T>` (Types.fs aliases it). Signatures are unchanged — the
    // shape deltas (option payloads, bare-string nodeId, record params) are
    // absorbed here, which is what keeps author code stable.

    let none<'T> : Binding<'T> = Binding.Static None

    let ``static`` (value: 'T) : Binding<'T> = Binding.Static(Some value)

    /// Per Defect (2) resolution: typed accessor `'result -> 'T` is boxed
    /// to `obj -> 'T` at the tree level. The author writes
    /// `binding.query "totalRevenue" _.amount` — `'result` is inferred from
    /// the schema-registered query result type at the typed-API surface.
    let query (name: string) (accessor: 'result -> 'T) : Binding<'T> =
        Binding.Query(name, (fun (o: obj) -> accessor (unbox<'result> o)), None)

    /// A host-computed query that declares its filter dependency edge (Phase 421). `dependsOn` names
    /// the filters that scope this consumer — the tree owns the edge (AI-authorable, validator-visible,
    /// op-stream-replayable), the accessor closure still owns the predicate. A filter-store change
    /// re-resolves the query. An empty list is "no edge" and stays off the wire (`None`).
    let queryDependsOn (name: string) (dependsOn: string list) (accessor: 'result -> 'T) : Binding<'T> =
        Binding.Query(
            name,
            (fun (o: obj) -> accessor (unbox<'result> o)),
            (if List.isEmpty dependsOn then None else Some dependsOn)
        )

    let filter (name: string) : Binding<'T> = Binding.Filter(name, None)

    /// The host-furnished current instant, as an ISO-8601 UTC string
    /// (`2026-08-02T06:59:24Z`) — Phase 765.
    ///
    /// The tree names "now"; it never *reads* a clock. The host resolves the
    /// instant once per render pass into `BindingSources.Now`, which is what
    /// keeps a tree a pure value and lets a replayed op-stream reproduce its
    /// original render rather than drifting to replay-time "now". A host that
    /// furnishes nothing leaves the slot `NotResolved`, so the node shows its
    /// placeholder instead of a plausible wrong date.
    ///
    /// The ISO-8601 form composes with `Fuaran.Core`'s `dateDiffDays`, which
    /// reads the leading `YYYY-MM-DD` — so "days overdue" is
    /// `dateDiffDays(col "due", now)` with no Core change.
    let now: Binding<string> = Binding.Now(fun (o: obj) -> unbox<string> o)

    /// `now` at an arbitrary slot type, for a `Transform` param or a
    /// `JVal`-typed position where the host value is projected by the caller.
    let nowAs (accessor: string -> 'T) : Binding<'T> =
        Binding.Now(fun (o: obj) -> accessor (unbox<string> o))

    let selection (nodeId: string) (accessor: 'row -> 'T) : Binding<'T> =
        Binding.Selection(nodeId, (fun (o: obj) -> accessor (unbox<'row> o)), None, None)

    /// A selection read with a default (Phase 629): yields `defaultValue`
    /// until the user first selects a row on `nodeId`.
    let selectionWithDefault (nodeId: string) (accessor: 'row -> 'T) (defaultValue: 'T) : Binding<'T> =
        Binding.Selection(nodeId, (fun (o: obj) -> accessor (unbox<'row> o)), Some defaultValue, None)

    /// A declarative row-field selection read (Phase 632): projects `field`
    /// off the clicked row — the wire-expressible twin of a typed accessor.
    /// `defaultValue` (the projected scalar, not a row) yields until the user
    /// first selects a row on `nodeId`.
    let selectionField (nodeId: string) (field: string) (defaultValue: 'T option) : Binding<'T> =
        Binding.Selection(nodeId, Binding.projectSelectionField<'T> field, defaultValue, Some field)

    let state (key: string) (defaultValue: 'T) : Binding<'T> = Binding.State(key, Some defaultValue)

    /// A state read whose slot carries NO default — resolves to nothing until
    /// the key is first written (the wire's `{"$type":"State","key":…}` form).
    let stateNoDefault (key: string) : Binding<'T> = Binding.State(key, None)

    let computed (f: BindingContext -> 'T) : Binding<'T> =
        Binding.Computed(fun (o: obj) -> f (unbox<BindingContext> o))

    /// Locale-aware formatted value (Phase 102). Wraps a numeric `source`
    /// binding and projects it to a localised display *string* via the
    /// renderer's `Intl` (Fable) / `System.Globalization` (.NET) formatter.
    /// `fmt` is the bounded semantic intent ([[Format]]); `locale` selects the
    /// BCP-47 locale ([[LocaleSource]]; `locale.ambient` defers to the host's
    /// 12.I locale source). Returns a `Binding<string>` — drop it into any
    /// `Binding<string>` slot (`TextSource.Bound`, `Accessibility.Label`, …).
    /// Build `fmt` with the `localeFormat` module; build `locale` with the
    /// `locale` module.
    let format (source: Binding<float>) (fmt: Format) (locale: LocaleSource) : Binding<string> =
        Binding.Format(source, fmt, locale)

    /// A declarative dataframe transform over a columnar source (Phase 282 / Wave 42 — the Compute
    /// layer). `source` is a `Fuaran.Core.DataSource` (embedded columns or a host-resolved `ref`);
    /// `pipeline` the full v1 verb set (`Fuaran.Core.DataFrame.Transform list`). Produces the
    /// transformed rows as a `Binding<obj seq>` for a data-bearing node (`DataGrid` / `Chart` /
    /// `Table` / `Metric`); the `Fuaran.UI.Ops` evaluator runs it via the `Fuaran.Core.DataFrame`
    /// reference evaluator. Authored idiomatically with the `Fuaran.Core.DataFrame` algebra ctors.
    let transform (source: Fuaran.Core.DataSource) (pipeline: Fuaran.Core.Transform list) : Binding<obj seq> =
        Binding.Transform(TransformSource.Data source, pipeline, None)

    /// A LIVE declarative dataframe transform (Phase 818 — the reactive-derivation first cut).
    /// `source` is a binding (typically `binding.state` carrying initial rows in its default; a
    /// `binding.selection` / `binding.query` also works) the runtime re-evaluates the pipeline
    /// against with subscription semantics — a `SetState` on the key re-derives every reader.
    /// The initial snapshot (what SSR / diagnostic evaluation reads) derives from the binding's
    /// carried default data via the Phase-815 normalisation (row-major rows transpose to
    /// columnar); a source with no carried table data starts from the empty table.
    let transformLive (source: Binding<JVal>) (pipeline: Fuaran.Core.Transform list) : Binding<obj seq> =
        let initial =
            match source with
            | Binding.State(_, Some data)
            | Binding.Selection(_, _, Some data, _) ->
                match HostPrelude.TransformLive.initialSource data with
                | Ok ds -> ds
                | Error _ -> HostPrelude.TransformLive.emptySource
            | _ -> HostPrelude.TransformLive.emptySource

        Binding.Transform(TransformSource.Live(source, initial), pipeline, None)

    /// A parameterised declarative dataframe transform (Phase 424). `parameters` binds each
    /// `ColExpr.Param` name the pipeline references to a scalar `Binding<JVal>` source (author a
    /// `binding.filter`/`binding.state`/`binding.static`), so a `filter` step comparing a `col`
    /// against a `param` scopes the rows by a live filter/state value with zero host code. The
    /// filter→consumer edge is derived from `Transform.paramsOf` — never separately declared.
    /// (`Binding<JVal>` since the swap — the typed verbatim carrier for the obj-erased position, D3.)
    let transformWith
        (source: Fuaran.Core.DataSource)
        (pipeline: Fuaran.Core.Transform list)
        (parameters: (string * Binding<JVal>) list)
        : Binding<obj seq> =
        let ps =
            parameters
            |> List.map (fun (name, fromB) -> ({ From = fromB; Name = name }: TransformParam))

        Binding.Transform(TransformSource.Data source, pipeline, (if List.isEmpty ps then None else Some ps))

    /// Invoke a host-registered compute capability for a value (Phase 283 — the Compute layer's
    /// hard-stuff seam). `capabilityId` references a `Fuaran.Core.Capability` in the host registry;
    /// `args` are scalar `(addr, value)` pairs validated against its `Signature` before dispatch.
    /// Resolves to a `Deferred<'T>` rendered through the node's `StateBehaviour` (`Pending` →
    /// `onLoading`, `Error` → `onError`). The body is host-provided, never on the wire.
    let invoke (capabilityId: string) (args: (string * string) list) : Binding<'T> =
        Binding.Invoke(capabilityId, args |> List.map (fun (addr, v) -> ({ Addr = addr; Value = v }: InvokeArg)))

    /// Component-scoped local buffer for controlled text/number
    /// inputs. `initialFrom` re-sync source (typically another binding like
    /// `binding.state` or `binding.query`); `flushOn` commit boundary
    /// (`OnBlur` is the Salary-style canonical shape); `onCommit` typed
    /// dispatch fired when the buffer flushes; `format` / `parse` the
    /// display-format hook + reverse-format pair (a `None` Format means
    /// the renderer uses `string<'T>` as the default; `Parse` is mandatory).
    ///
    /// `'T` is whatever the surrounding `FormFieldKind.Text` /
    /// `FormFieldKind.Number` binds (string / float / decimal at the typed
    /// surface — float at the wire); validator FUARAN044 rejects use
    /// against any other field shape.
    #nowarn "3261"

    let local
        (initialFrom: Binding<'T>)
        (flushOn: LocalFlushTrigger)
        (onCommit: 'T -> Action<'Msg>)
        (format: ('T -> string) option)
        (parse: string -> Result<'T, string>)
        : Binding<'T> =
        // Positional since the swap (flushOn, format, initialFrom, onCommit, parse).
        // `format` is a required slot on the generated case — a `None` here bakes
        // the renderer's old default (`string<'T>`) into the closure itself.
        // The Action<'Msg> obj-erasure is unchanged (the Defect (2) resolution):
        // the renderer recovers it by unboxing at dispatch time.
        Binding.Local(
            flushOn,
            (match format with
             | Some f -> f
             | None -> fun (v: 'T) -> string (box v)),
            initialFrom,
            Some(fun (t: 'T) -> box (onCommit t)),
            parse
        )

    #warnon "3261"

// ─── Typed action entry points (§4c lines 528, 530) ───────────────────────

[<RequireQualifiedAccess>]
module Action =
    let dispatch (msg: 'Msg) : Action<'Msg> = Action.Dispatch msg

    /// Per Defect (2) resolution: typed `'a -> 'Msg` is boxed to
    /// `obj -> 'Msg` at the tree level. The builder keeps the `ApiEndpoint`
    /// wrapper at the author surface; the generated case carries the bare
    /// endpoint string (stage 2 of the swap).
    let call (ApiEndpoint endpoint) (onResult: 'a -> 'Msg) : Action<'Msg> =
        Action.Call(endpoint, Some(fun (o: obj) -> onResult (unbox<'a> o)), None)

    /// Declarative fetch (Phase 428): call the endpoint and write the response
    /// to the reactive `$state.<key>` slot — every `Binding.State key` reader
    /// re-renders on completion. The closure-free shape an AI author emits.
    let callIntoState (ApiEndpoint endpoint) (key: string) : Action<'Msg> =
        Action.Call(endpoint, None, Some(CallResultTarget.State key))

    /// Declarative fetch (Phase 428): call the endpoint and write the response
    /// to the `queryResults` slot `<name>` — every `Binding.Query name` reader
    /// re-renders on completion (data-preserving on the decoded path per the
    /// Phase 421 identity accessor).
    let callIntoQuery (ApiEndpoint endpoint) (name: string) : Action<'Msg> =
        Action.Call(endpoint, None, Some(CallResultTarget.Query name))

    let notify (channel: string) (payload: JVal) : Action<'Msg> = Action.Notify(channel, payload)

    let navigate (route: string) : Action<'Msg> = Action.Navigate route

    let setState (key: string) (value: JVal) : Action<'Msg> = Action.SetState(key, Some value, None)

    /// Phase 818 — write a DERIVED value to the State channel: `source` is a
    /// Binding evaluated at dispatch time inside the existing gate (value XOR
    /// valueFrom on the wire). Closes "set state from what the user
    /// clicked/typed" without closures — e.g.
    /// `action.setStateFrom "chosen-id" (binding.selectionField "grid" "id")`.
    let setStateFrom (key: string) (source: Binding<JVal>) : Action<'Msg> = Action.SetState(key, None, Some source)

    let aiTool (toolName: string) (args: JVal) : Action<'Msg> = Action.AiTool(toolName, args)

    let chain (actions: Action<'Msg> list) : Action<'Msg> = Action.Chain actions

    /// Read a previously-selected file's body (Phase 136). `file` is the
    /// opaque `FileRef` handed to the author on a `FileSelection`; `encoding`
    /// picks the byte→string projection; `onRead` maps the read body into a
    /// follow-on message. The renderer routes through
    /// `IFuaranRuntime.ReadFileBody` and dispatches `onRead body` when the
    /// read completes — no consumer-side `FileReader` interop.
    let readFileBody (file: FileRef) (encoding: FileReadEncoding) (onRead: string -> 'Msg) : Action<'Msg> =
        // The author-surface FileRef record splits at the generated seam:
        // the wire id + the host-only handle ride as separate slots.
        Action.ReadFileBody(file.Id, file.Handle, encoding, Some onRead)

    /// Invoke a host-registered compute capability as an effect (Phase 283 — the effectful sibling
    /// of `binding.invoke` / `action.call` / `action.aiTool`). `capabilityId` references a
    /// `Fuaran.Core.Capability`; `args` are scalar `(addr, value)` pairs validated against its
    /// `Signature` before dispatch. The body is host-registered, never on the wire.
    let invoke (capabilityId: string) (args: (string * string) list) : Action<'Msg> =
        Action.Invoke(capabilityId, args |> List.map (fun (addr, v) -> ({ Addr = addr; Value = v }: InvokeArg)))

// ─── Typed format entry points (§4c lines 513, 528) ───────────────────────

[<RequireQualifiedAccess>]
module format =
    let currency (code: string) : CellFormat = CellFormat.Currency code

    let percent (decimals: int option) : CellFormat = CellFormat.Percent decimals

    let number (decimals: int option) : CellFormat = CellFormat.Number decimals

    let significantDigits (digits: int) : CellFormat = CellFormat.SignificantDigits digits

    let date (fmt: string) : CellFormat = CellFormat.Date fmt

// ─── Locale-aware Format / LocaleSource entry points (Phase 102) ───────────
//
// The bounded, semantic formatting intent + locale selector carried by
// `binding.format`. Distinct from the `format` module above (which builds
// `CellFormat` for grid columns / Metric display) — these build the locale-aware
// `Format` DU. No raw `Intl` option-bag escape (FGP 1).

[<RequireQualifiedAccess>]
module localeFormat =
    /// Plain localised number; `decimals` pins the fraction-digit count
    /// (`None` = locale default).
    let number (decimals: int option) : Format = Format.Number decimals

    /// Currency amount; `isoCode` is the ISO-4217 code (e.g. `"GBP"`). The
    /// validator (FUARAN061) rejects a blank code at build time.
    let currency (isoCode: string) : Format = Format.Currency isoCode

    /// Percentage of a ratio source (`0.42` → "42%"); `decimals` pins the
    /// fraction-digit count (`None` = locale default).
    let percent (decimals: int option) : Format = Format.Percent decimals

    /// Absolute date/time (source read as whole Unix-epoch seconds).
    let date (dateStyle: DateStyle) : Format = Format.Date dateStyle

    /// Relative time (source read as a signed count of `unit` relative to now).
    let relativeTime (unit: RelativeTimeUnit) : Format = Format.RelativeTime unit

[<RequireQualifiedAccess>]
module locale =
    /// Defer to the host-supplied ambient locale (the 12.I locale source).
    let ambient: LocaleSource = LocaleSource.Ambient

    /// Pin an explicit BCP-47 locale tag (e.g. `"en-GB"`, `"fr-FR"`).
    let explicit (tag: string) : LocaleSource = LocaleSource.Explicit tag

// ─── FormFieldKind smart constructors ──────────────────────────────────────
//
// Number-with-constraints helpers. Authors call:
//
//   FormFieldKind.rangedNumber
//       (value = binding.state "year" 2024.0)
//       (onChange = SetYear >> Action.dispatch)
//       (min = 1979.0)
//       (max = 2028.0)
//
// to layer the optional Min / Max / Step trio onto the parallel-additive
// `RangedNumber` DU case without forcing every author to write the full
// `NumberFieldConstraints` record. The classic two-positional `Number`
// case stays as-is — authors who need no constraints construct it
// directly.

[<RequireQualifiedAccess>]
module FormFieldKind =
    /// `RangedNumber` with all three of `min` / `max` / `step`
    /// available as optional named arguments. Pass `?min`, `?max`, `?step`
    /// to omit any of them (each defaults to `None`).
    let rangedNumber
        (value: Binding<float>)
        (onChange: float -> Action<'Msg>)
        (min: float option)
        (max: float option)
        (step: float option)
        : FormFieldKind<'Msg> =
        FormFieldKind.RangedNumber(Some value, Some onChange, min, max, step)

    /// `RangedNumber` shorthand for the common "stepped only"
    /// shape (e.g. a percentage field that allows any value but advances
    /// by 0.5 on the spinner). Equivalent to `rangedNumber value onChange
    /// ?step=step` — distinct name for grep-ability at the call site.
    let numberStepped (value: Binding<float>) (onChange: float -> Action<'Msg>) (step: float) : FormFieldKind<'Msg> =
        FormFieldKind.RangedNumber(Some value, Some onChange, None, None, Some step)

    /// `SegmentedChoice` with the same triple `Choice` takes plus
    /// an optional orientation defaulting to `Horizontal` (segmented row).
    /// Pass `Orientation.Vertical` for the radio-button-list shape. Author
    /// example:
    ///
    ///   FormFieldKind.segmentedChoice
    ///       (options = binding.static [ { Value = "low"; Label = TextSource.Literal "Low" }; ... ])
    ///       (value = binding.state "tier" None)
    ///       (onChange = SetTier >> Action.dispatch)
    ///       Orientation.Horizontal
    let segmentedChoice
        (options: Binding<SelectOption list>)
        (value: Binding<string>)
        (onChange: string option -> Action<'Msg>)
        (orientation: Orientation)
        : FormFieldKind<'Msg> =
        // "No selection" is `Binding.Static None` since the swap (the slot is
        // `Binding<string> option`; the old `Binding<string option>` payload
        // option moved into the generated Static payload).
        FormFieldKind.SegmentedChoice(options, Some value, Some onChange, orientation)

    /// `Date` / `Time` / `DateTime` field (Phase 288) with the optional
    /// ISO-8601 `min` / `max` + numeric `step` (seconds) constraints as named
    /// options. `variant` selects the native control (defaults are layered by
    /// the caller). The bound value is an ISO-8601 string. Author example:
    ///
    ///   FormFieldKind.date
    ///       (value = binding.state "checkIn" "")
    ///       (onChange = SetCheckIn >> Action.dispatch)
    ///       DateVariant.Date
    ///       (min = Some "2026-01-01")
    ///       None None
    let date
        (value: Binding<string>)
        (onChange: string -> Action<'Msg>)
        (variant: DateVariant)
        (min: string option)
        (max: string option)
        (step: float option)
        : FormFieldKind<'Msg> =
        // The generated Date handler receives `string option` (a clearable
        // control); the typed builder keeps its plain-string signature and
        // maps a cleared value to "".
        FormFieldKind.Date(Some value, Some(fun v -> onChange (defaultArg v "")), variant, min, max, step)

    /// Single-control date range (Phase 725) — the pair-valued sibling of
    /// `date`. `value` is a `Binding<DateRangePair>` carrying the ordered
    /// `{From; To}` ISO-8601 pair (the generated record IS the wire object,
    /// exactly as `RangePair` is for `Range`); `variant` selects the native
    /// control for both ends; `min` / `max` (ISO strings) + `step` (seconds)
    /// bound BOTH ends. Author example:
    ///
    ///   FormFieldKind.dateRange
    ///       (value = binding.state "stay" { From = ""; To = "" })
    ///       (onChange = SetStay >> Action.dispatch)
    ///       DateVariant.Date
    ///       (Some "2026-01-01")
    ///       None None
    let dateRange
        (value: Binding<DateRangePair>)
        (onChange: string * string -> Action<'Msg>)
        (variant: DateVariant)
        (min: string option)
        (max: string option)
        (step: float option)
        : FormFieldKind<'Msg> =
        FormFieldKind.DateRange(Some value, Some onChange, variant, min, max, step)

    // ── Handler-free (declarative) ctors — Phase 426, the control write-back
    //    default. Each emits `onChange = None`, the shape an AI author uses: the
    //    renderer writes the changed value back to the field's own `value`
    //    binding when that binding is directly `Binding.State` / `Binding.Filter`
    //    (any other shape is inert — FUARAN069 warns). Mirrors the Phase 423
    //    `FilterKind.*Declarative` family.

    /// Handler-free `Text` — the renderer writes the typed string to the
    /// `value` binding's own State/Filter slot on change.
    let textDeclarative (value: Binding<string>) : FormFieldKind<'Msg> = FormFieldKind.Text(Some value, None)

    /// Handler-free `Number` — writes the typed float back to the value slot.
    let numberDeclarative (value: Binding<float>) : FormFieldKind<'Msg> = FormFieldKind.Number(Some value, None)

    /// Handler-free `Checkbox` — writes the toggled bool back to the value slot.
    let checkboxDeclarative (value: Binding<bool>) : FormFieldKind<'Msg> =
        FormFieldKind.Checkbox(Some value, None)

    /// Handler-free `Toggle` — the on/off SWITCH affordance (Phase 766). Same
    /// boolean data and write-back as `checkbox`; what differs is the rendered
    /// control and its a11y contract (`role="switch"` + `aria-checked`, which a
    /// screen reader announces as on/off rather than checked).
    ///
    /// Reach for it when the prompt says switch / toggle / on-off / start-stop;
    /// reach for `checkbox` for consent, opt-in and multi-select list items.
    let toggleDeclarative (value: Binding<bool>) : FormFieldKind<'Msg> = FormFieldKind.Toggle(Some value, None)

    /// Handler-free `Choice` — writes the chosen option (string option) back to
    /// the value slot; a cleared choice clears the slot.
    let choiceDeclarative (options: Binding<SelectOption list>) (value: Binding<string>) : FormFieldKind<'Msg> =
        FormFieldKind.Choice(options, Some value, None)

    /// Handler-free `TextArea` — writes the typed string back to the value slot.
    let textAreaDeclarative (value: Binding<string>) (rows: int) : FormFieldKind<'Msg> =
        FormFieldKind.TextArea(Some value, None, rows)

    /// Handler-free `RangedNumber` — writes the typed float back to the value slot.
    let rangedNumberDeclarative
        (value: Binding<float>)
        (min: float option)
        (max: float option)
        (step: float option)
        : FormFieldKind<'Msg> =
        FormFieldKind.RangedNumber(Some value, None, min, max, step)

    /// Handler-free `SegmentedChoice` — writes the chosen option back to the value slot.
    let segmentedChoiceDeclarative
        (options: Binding<SelectOption list>)
        (value: Binding<string>)
        (orientation: Orientation)
        : FormFieldKind<'Msg> =
        FormFieldKind.SegmentedChoice(options, Some value, None, orientation)

    /// Handler-free `Date` — writes the ISO-8601 string back to the value slot.
    let dateDeclarative
        (value: Binding<string>)
        (variant: DateVariant)
        (min: string option)
        (max: string option)
        (step: float option)
        : FormFieldKind<'Msg> =
        FormFieldKind.Date(Some value, None, variant, min, max, step)

    /// Handler-free `DateRange` — writes the changed `(from, to)` ISO-8601
    /// pair back to the value slot.
    let dateRangeDeclarative
        (value: Binding<DateRangePair>)
        (variant: DateVariant)
        (min: string option)
        (max: string option)
        (step: float option)
        : FormFieldKind<'Msg> =
        FormFieldKind.DateRange(Some value, None, variant, min, max, step)

/// Smart-ctors for filter strips. Closure-bearing ctors (`Some onChange`) preserve the
/// F#-authored dispatch behaviour byte-for-byte; the closure-free ctors (Phase 423) emit
/// `onChange = None`, the shape an AI author uses — the renderer's reactive FilterStore then
/// writes `$filters.<name>` on change with zero host code. Pair a closure-free ctor with a
/// `Binding.Filter` self-read on the `value` to close the loop.
///
/// 0.2.0 filters-unification: filter chips carry ordinary `FormFieldKind`
/// controls. These helpers build the closure-free (declarative) shapes an AI
/// author emits — the renderer writes `$filters.<name>` itself. The old
/// `FilterKind` family and its helpers are retired (pre-launch clean break).
[<RequireQualifiedAccess>]
module FilterField =
    /// Text chip bound to its own filter key.
    let text (name: string) : FormFieldKind<'Msg> =
        FormFieldKind.Text(Some(Binding.Filter(name, None)), None)

    /// Dropdown choice chip bound to its own filter key.
    let choice (name: string) (options: Binding<SelectOption list>) : FormFieldKind<'Msg> =
        FormFieldKind.Choice(options, Some(Binding.Filter(name, None)), None)

    /// Segmented choice chip bound to its own filter key.
    let segmented
        (name: string)
        (options: Binding<SelectOption list>)
        (orientation: Orientation)
        : FormFieldKind<'Msg> =
        FormFieldKind.SegmentedChoice(options, Some(Binding.Filter(name, None)), None, orientation)

    /// Dual-thumb range chip bound to its own filter key.
    let range (name: string) : FormFieldKind<'Msg> =
        FormFieldKind.Range(Some(Binding.Filter(name, None)), None, None, None, None)

    /// Date-range chip bound to its own filter key (Phase 725). ONE filter
    /// param carries the whole `(from, to)` pair — the reason the case exists.
    let dateRange (name: string) (variant: DateVariant) : FormFieldKind<'Msg> =
        FormFieldKind.DateRange(Some(Binding.Filter(name, None)), None, variant, None, None, None)

// ─── Column helpers (§4c lines 523–529) ───────────────────────────────────

module Column =
    /// Lower a typed `Column<'Msg>` into the tree-level `ColumnErased<'Msg>`.
    /// Since fuaran#665 the accessors are already `Row`-typed on both sides, so
    /// this is a shape adaptation (required → optional-override slots), not a
    /// row erasure — the unbox ceremony is gone.
    let erase (col: Column<'Msg>) : ColumnErased<'Msg> =
        { Label = col.Label
          // Phase 425 — the typed closure is the override (`Some`); a field-named erased column
          // (`Field = Some …`, `Value = None`) is produced on the decoded path.
          Value = Some col.Value
          Field = None
          // Phase 861 / 863, surfaced on the typed facade by Phase 873 — both
          // narrowing flags pass through. They are declarations, not closures,
          // so the erasure has nothing to do to them.
          Sortable = col.Sortable
          Editable = col.Editable
          Format = col.Format
          Kind =
            match col.Kind with
            | CellKind.Text -> CellKindErased.Text
            | CellKind.Numeric -> CellKindErased.Numeric
            | CellKind.Date -> CellKindErased.Date
            | CellKind.Editable f -> CellKindErased.Editable(Some f)
            | CellKind.Checkbox(get, onToggle) -> CellKindErased.Checkbox(get, Some onToggle)
            | CellKind.Button(label, onClick) -> CellKindErased.Button(label, Some onClick)
            | CellKind.ButtonGroup btns ->
                CellKindErased.ButtonGroup(btns |> List.map (fun (l, f) -> { Label = l; OnClick = Some f }))
            | CellKind.Link(href, label) -> CellKindErased.Link(href, label)
            | CellKind.Pill(label, tone) -> CellKindErased.Pill(label, tone)
            | CellKind.TonedPill(field, toneMap, defaultTone) -> CellKindErased.TonedPill(field, toneMap, defaultTone)
            | CellKind.Progress(fraction, label) -> CellKindErased.Progress(fraction, label)
            | CellKind.Custom render -> CellKindErased.Custom render
          Width = col.Width }

    let text (label: string) (value: Row -> string) : Column<'Msg> =
        { Defaults.column<'Msg> with
            Label = label
            Value = (fun r -> CellValue.Text(value r))
            Kind = CellKind.Text }

    let numeric (label: string) (value: Row -> float) : Column<'Msg> =
        { Defaults.column<'Msg> with
            Label = label
            Value = (fun r -> CellValue.Numeric(value r))
            Kind = CellKind.Numeric }

    let date (label: string) (value: Row -> System.DateTimeOffset) : Column<'Msg> =
        { Defaults.column<'Msg> with
            Label = label
            Value = (fun r -> CellValue.Date(value r))
            Kind = CellKind.Date }

    let bool (label: string) (value: Row -> bool) : Column<'Msg> =
        { Defaults.column<'Msg> with
            Label = label
            Value = (fun r -> CellValue.Bool(value r))
            Kind = CellKind.Text }

    /// Postfix pipe: convert a non-interactive Column into an Editable one.
    let editable (onEdit: Row -> CellValue -> Action<'Msg>) (col: Column<'Msg>) : Column<'Msg> =
        { col with
            Kind = CellKind.Editable(fun (r, v) -> onEdit r v) }

    /// Project a `CellValue` into the `TextSource` the Pill cell renders as its
    /// label. Mirrors the renderer's default (formatless) `CellValue` → string
    /// mapping so a `withPill` column reads the same label the source column did.
    let private cellValueToText (cv: CellValue) : TextSource =
        match cv with
        | CellValue.Text s -> TextSource.Literal s
        | CellValue.Numeric n -> TextSource.Literal(string n)
        | CellValue.Bool b -> TextSource.Literal(if b then "true" else "false")
        | CellValue.Date d -> TextSource.Literal(d.ToString("yyyy-MM-dd"))
        | CellValue.Empty -> TextSource.Literal ""

    /// Postfix pipe: render a column's value as a tone-bearing pill. Reuses the
    /// column's existing value accessor for the pill label (projected to text)
    /// and the supplied function for the per-row `ToneVariant`. The renderer
    /// already ships the `CellKind.Pill` cell — this is the author-surface entry
    /// point so grids reach the pill idiom without dropping to `CellKindErased`.
    ///
    /// Example:
    ///   Column.text "Status" (_.Status) |> Column.withPill statusTone
    let withPill (tone: Row -> ToneVariant) (col: Column<'Msg>) : Column<'Msg> =
        { col with
            Kind = CellKind.Pill((fun r -> cellValueToText (col.Value r)), tone) }

    /// Postfix pipe: render a column's value as a tone-bearing pill whose tone comes
    /// from a DECLARED value→tone map rather than a host closure (Phase 750). Unlike
    /// [[withPill]] this survives the wire intact — the pill's label and tone key are
    /// both the named row property, so a decoded grid renders the distinction with
    /// zero host code, and an AI author can emit it.
    ///
    /// `defaultTone` covers a value the map does not mention; pass
    /// `ToneVariant.Default` for "leave the rest plain" (it is then omitted on the
    /// wire).
    ///
    /// Example:
    ///   Column.text "Status" (_.Status)
    ///   |> Column.withTonedPill "status" (Map [ "Delayed", ToneVariant.Warning ]) ToneVariant.Default
    let withTonedPill
        (field: string)
        (toneMap: Map<string, ToneVariant>)
        (defaultTone: ToneVariant)
        (col: Column<'Msg>)
        : Column<'Msg> =
        { col with
            Kind = CellKind.TonedPill(field, toneMap, defaultTone) }

    let withFormat (format: CellFormat) (col: Column<'Msg>) : Column<'Msg> = { col with Format = format }

    let withWidth (width: ColumnWidth) (col: Column<'Msg>) : Column<'Msg> = { col with Width = width }

// ─── Drawing style helpers (Phase 877) ───────────────────────────────────────

/// Authoring helpers over `DrawStyle`. The record-with idiom
/// (`{ Defaults.drawStyle with … }`) stays the general surface; these exist for
/// the fields that carry a *discipline* a bare record update would let an author
/// skip.
[<RequireQualifiedAccess>]
module drawStyle =
    /// Round-half-up to 2 dp — the same deterministic rule the chart lowering
    /// applies to coordinates, restated here so the language tier does not
    /// depend on `Fuaran.UI.Charts`. Avoids banker's rounding and
    /// platform float-print divergence, so every host reads the same bytes.
    let private r2 (x: float) : float = floor (x * 100.0 + 0.5) / 100.0

    /// Phase 877 — rotate a `Label`'s text by `degrees` CLOCKWISE about the
    /// label's own anchor point, applying the canonical 2-dp rounding.
    ///
    /// Rounding is an AUTHORING discipline, not a codec rule: the wire carries
    /// the value as authored and no host re-rounds on decode, so byte-parity
    /// across the five conformant hosts needs no shared rounding implementation.
    /// Rotating through this constructor is what keeps an emitted angle from
    /// carrying float noise (`29.999999999999996`) that would differ per host's
    /// shortest-round-trip printer.
    ///
    /// A non-finite angle is dropped rather than encoded — NaN / ±∞ have no
    /// canonical wire form here, and an unrotated label is the honest fallback.
    ///
    ///   Defaults.drawStyle |> drawStyle.rotate -30.0
    let rotate (degrees: float) (style: DrawStyle) : DrawStyle =
        if System.Double.IsNaN degrees || System.Double.IsInfinity degrees then
            { style with Rotation = None }
        else
            { style with
                Rotation = Some(r2 degrees) }

    /// Clear any rotation — the upright default. Encodes nothing (rule 4).
    let upright (style: DrawStyle) : DrawStyle = { style with Rotation = None }

    /// Phase 883 — give a shape a hover-readable value: the renderer emits it
    /// as an SVG `&lt;title&gt;` child of the shape's own element, which is both
    /// the native browser tooltip and the element's accessible name. No script,
    /// so it works in a static SSR page and under a screen reader alike.
    ///
    /// Applies to EVERY shape, not just `Label` — a tip is for the marks a
    /// reader hovers (a bar, a wedge, a point). An EMPTY tip is dropped rather
    /// than encoded: an empty `&lt;title&gt;` suppresses the tooltip AND
    /// overrides the accessible name with nothing, which is strictly worse than
    /// having no title at all.
    ///
    ///   Defaults.drawStyle |> drawStyle.tip "Revenue · Q1 · 1,234"
    let tip (text: string) (style: DrawStyle) : DrawStyle =
        if System.String.IsNullOrEmpty text then
            { style with Tip = None }
        else
            { style with
                Tip = Some(TextSource.Literal text) }

    /// The `TextSource` form of `tip`, for a bound or i18n readout.
    let tipFrom (text: TextSource) (style: DrawStyle) : DrawStyle = { style with Tip = Some text }

    /// Clear any tip — the untipped default. Encodes nothing (rule 4), so the
    /// shape keeps its self-closing element.
    let untipped (style: DrawStyle) : DrawStyle = { style with Tip = None }

// ─── Components — the `Fuaran.X` author surface ──────────────────────────────

module Fuaran =
    let private buildNode (id: string) (kind: NodeKind<'Msg>) (accessibility: Accessibility option) : Node<'Msg> =
        // `State = None` / `Style = None` since the swap — the canonical
        // empty-state / default-style envelope (the encoder omits both, exactly
        // as it omitted the old always-present empty/default records).
        { Id = id
          Kind = kind
          State = Option.None
          Style = Option.None
          Accessibility = accessibility
          Motion = Defaults.Motion.none
          ExtraAttributes = Option.None }

    // ─── Layout ─────────────────────────────────────────────────────────

    /// The unified container smart-ctor (Phase 390). `Stack` / `GridLayout` /
    /// `Dashboard` / `Card` are `Box`-emitting convenience surfaces over this.
    let box (id: string) (spec: BoxSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Box(spec)) Defaults.Accessibility.none

    let dashboard (id: string) (spec: DashboardSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Auto
                  Role = BoxRole.Dashboard
                  Heading = Option.None
                  Children = spec.Children }
            ))
            Defaults.Accessibility.dashboard

    let stack (id: string) (spec: StackSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Flex(spec.Orientation, spec.Wrap, Option.None)
                  Role = BoxRole.Group
                  Heading = Option.None
                  Children = spec.Children }
            ))
            Defaults.Accessibility.none

    let gridLayout (id: string) (spec: GridLayoutSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Grid(spec.Cols, spec.TemplateColumns, Option.None)
                  Role = BoxRole.Group
                  Heading = Option.None
                  Children = spec.Children }
            ))
            Defaults.Accessibility.none

    /// Irregular-grid smart-ctor. Pre-populates the
    /// additive `GridLayoutSpec.TemplateColumns` with the supplied verbatim
    /// `grid-template-columns` string; the renderer short-circuits the
    /// `Cols`-based `repeat(N, 1fr)` emission when the field is `Some`.
    /// Use when the typed `Cols: int` shape can't express the required
    /// sizing function (irregular `1fr 2fr`, fixed-plus-flex
    /// `100px repeat(5, 1fr)`, content-driven `min-content max-content`,
    /// auto-fit `repeat(auto-fit, minmax(150px, 1fr))`). The plain
    /// `Fuaran.gridLayout` shape is unchanged for callers who don't need
    /// the escape.
    let gridLayoutTemplated (id: string) (templateColumns: string) (spec: GridLayoutSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Grid(spec.Cols, Some templateColumns, Option.None)
                  Role = BoxRole.Group
                  Heading = Option.None
                  Children = spec.Children }
            ))
            Defaults.Accessibility.none

    /// Column-fill smart-ctor (Phase 1082) — the masonry hang. Children flow
    /// DOWN each column in turn instead of across each row, so children of
    /// unequal height pack without the whitespace a row-aligned grid leaves
    /// beneath its shorter cells. Use it for a picture wall of mixed
    /// portraits and landscapes; use `Fuaran.gridLayout` when the children are
    /// similarly proportioned or must be read in sequence (multi-column fill
    /// puts document order down each column, not across the page).
    let masonryLayout (id: string) (spec: MasonryLayoutSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Masonry(spec.Cols, Option.None)
                  Role = BoxRole.Group
                  Heading = Option.None
                  Children = spec.Children }
            ))
            Defaults.Accessibility.none

    let splitPanel (id: string) (spec: SplitPanelSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.SplitPanel(spec)) Defaults.Accessibility.none

    let tabs (id: string) (spec: TabsSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Tabs(spec)) Defaults.Accessibility.tabs

    /// Tabs smart-ctor for the typed-tag overlay entry
    /// point. Requires both `TabHeaders` and `TabTags` to be populated — the
    /// "I'm using the typed overlay" contract. The plain `Fuaran.tabs`
    /// remains for the existing integer-only authoring shape. Defects in the
    /// header/tag/child length triple surface as FUARAN047 / FUARAN048 at
    /// validation time; tag/binding-without-tags surfaces as FUARAN049.
    let tabsTagged (id: string) (spec: TabsSpec<'Msg>) : Node<'Msg> =
        match spec.TabHeaders, spec.TabTags with
        | Some _, Some _ -> buildNode id (NodeKind.Tabs(spec)) Defaults.Accessibility.tabs
        | _ ->
            failwith
                "Fuaran.tabsTagged requires both TabHeaders and TabTags to be populated; use Fuaran.tabs for the integer-indexed shape."

    let card (id: string) (spec: CardSpec<'Msg>) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Flex(Orientation.Vertical, false, Option.None)
                  Role = BoxRole.Card
                  Heading = spec.Heading
                  Children = spec.Children }
            ))
            Defaults.Accessibility.card

    let stepper (id: string) (spec: StepperSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Stepper(spec)) Defaults.Accessibility.none

    /// Feliz-parity additive: single-card-of-rows
    /// layout — typically wraps `Fuaran.labelValueRow` children. See
    /// `NodeKind.SummaryList` for when to reach for this instead of `Card`.
    let summaryList (id: string) (spec: SummaryListSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.SummaryList(spec)) Defaults.Accessibility.summaryList

    /// Typed accordion / collapsible layout.
    /// Renders as HTML-native `<details>` / `<summary>` with `aria-expanded`
    /// wired from the `Open` binding. Choose `Disclosure` over `Card` when
    /// the section's open/closed state is part of the user's interaction
    /// model (advanced settings, "Additional X" sub-form, FAQ entries);
    /// choose `Card` when the content is always visible.
    let disclosure (id: string) (spec: DisclosureSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Disclosure(spec)) Defaults.Accessibility.disclosure

    /// Out-of-flow overlay dialog (Phase 289). Carries an `Open` binding, an
    /// optional heading, a `Dismissable` flag, an `OnDismiss` action, and a
    /// child subtree. Renders inline (no portal), positioned + z-indexed by
    /// CSS, with `role="dialog"` + `aria-modal` — SSR and CSR emit identical
    /// structure (no hydration mismatch). See the render-fidelity contract in
    /// docs/SSR.md.
    let modal (id: string) (spec: ModalSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Modal(spec)) Defaults.Accessibility.modal

    /// Overflow / scroll container (Phase 289). `Orientation` selects the
    /// scroll axis; optional `MaxHeight` / `MaxWidth` (pixels) bound the
    /// viewport. Overflow + scrollbar render identically SSR↔CSR (pinned by
    /// the SSR-parity corpus).
    let scrollArea (id: string) (spec: ScrollAreaSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.ScrollArea(spec)) Defaults.Accessibility.scrollArea

    // ─── Display ────────────────────────────────────────────────────────

    let metric (id: string) (spec: MetricSpec) : Node<'Msg> =
        buildNode id (NodeKind.Metric(spec)) Defaults.Accessibility.metric

    /// Feliz-parity additive: typed heading
    /// constructor. Use `Variant = Eyebrow` / `Caption` / `Lead` for
    /// uppercase mini-label / italic small-print / hero text shapes; the
    /// `<h{Level}>` tag is preserved across variants for screen-reader
    /// semantics. Default callers see no class change (Variant defaults
    /// to Standard via `Defaults.heading`).
    let heading (id: string) (spec: HeadingSpec) : Node<'Msg> =
        buildNode id (NodeKind.Heading(spec)) Defaults.Accessibility.none

    /// Feliz-parity additive: single-row
    /// label-left / value-right primitive. Consumed by `Fuaran.summaryList`
    /// children, but can stand alone (renderer treats it like a Metric for
    /// the resolution / state-slot dispatch — `OnLoading` / `OnError`
    /// honoured).
    let labelValueRow (id: string) (spec: LabelValueRowSpec) : Node<'Msg> =
        buildNode id (NodeKind.LabelValueRow(spec)) Defaults.Accessibility.none

    /// A labeled TEXT fact tile — the complementary kind to the numeric
    /// `Metric` / `LabelValueRow`. Positional 80% case:
    /// `Fuaran.fact "id" "Patient" "Alice Smith"`; the record form via
    /// `factSpec` for tone / emphasis / icon / help.
    let fact (id: string) (label: string) (value: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Fact(
                { Defaults.fact with
                    Label = TextSource.Literal label
                    Value = TextSource.Literal value }
            ))
            Defaults.Accessibility.none

    /// Record-form `Fact` (tone / emphasis / icon / help).
    let factSpec (id: string) (spec: FactSpec) : Node<'Msg> =
        buildNode id (NodeKind.Fact(spec)) Defaults.Accessibility.none

    /// Crawlable hyperlink (Phase 139). Two-tier API per §4c: positional
    /// `Fuaran.link "id" "https://…" "Label"` for the static-href 80% case;
    /// the full record form via the `linkSpec` twin for bound hrefs / `rel` /
    /// `target` / `download`. Renders a real `<a href>` (SEO-crawlable,
    /// works with JavaScript disabled), distinct from `Fuaran.button` +
    /// `Action.Navigate` (the SPA client-routing gesture).
    let link (id: string) (href: string) (label: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Link(
                { Defaults.link with
                    Href = Binding.Static(Some href)
                    Label = TextSource.Literal label }
            ))
            Defaults.Accessibility.none

    let linkSpec (id: string) (spec: LinkSpec) : Node<'Msg> =
        buildNode id (NodeKind.Link(spec)) Defaults.Accessibility.none

    /// Protected email link (Phase 812). A `mailto:` Link whose
    /// address the renderers must not emit in plaintext: SSR entity-encodes the
    /// href and label character-by-character (works without JavaScript, absent
    /// from the raw page source); CSR renders the real href post-hydration —
    /// the decoded DOM is identical. Positional 80% case:
    /// `Fuaran.emailLink "id" "user@example.com" "user@example.com"`; the full
    /// record form via `linkSpec` with `Protection = Some LinkProtection.Email`.
    let emailLink (id: string) (address: string) (label: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Link(
                { Defaults.link with
                    Href = Binding.Static(Some("mailto:" + address))
                    Label = TextSource.Literal label
                    Protection = Some LinkProtection.Email }
            ))
            Defaults.Accessibility.none

    /// Standalone image (Phase 287). Two-tier API: positional
    /// `Fuaran.image "id" "https://…" "alt text"` for the static-src 80% case;
    /// the full record form via the `imageSpec` twin for bound srcs / Avatar
    /// variant. `src` is sanitised at render time; `alt` is the accessibility
    /// floor.
    let image (id: string) (src: string) (alt: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Image(
                { Defaults.image with
                    Src = Binding.Static(Some src)
                    Alt = TextSource.Literal alt }
            ))
            Defaults.Accessibility.none

    let imageSpec (id: string) (spec: ImageSpec) : Node<'Msg> =
        buildNode id (NodeKind.Image(spec)) Defaults.Accessibility.none

    /// Media playback surface (Phase 1076). Positional
    /// `Fuaran.video "id" "/clip.mp4" "Studio walkthrough"` and
    /// `Fuaran.audio "id" "/talk.mp3" "Curator's commentary"` build the two
    /// variants with a transport, no loop, no autoplay and no poster; the
    /// `mediaSpec` twin takes the full record.
    ///
    /// The label is a REQUIRED positional argument on both, not an optional
    /// tail: it is the accessible name, there is no decorative case for a
    /// transport, and a smart constructor that let it be omitted would make the
    /// easiest thing to write the thing FUARAN108 refuses.
    let video (id: string) (src: string) (label: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Media(
                { Defaults.media with
                    Src = Binding.Static(Some src)
                    Label = TextSource.Literal label
                    Kind = MediaKind.Video(false, Option.None) }
            ))
            Defaults.Accessibility.none

    let audio (id: string) (src: string) (label: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Media(
                { Defaults.media with
                    Src = Binding.Static(Some src)
                    Label = TextSource.Literal label
                    Kind = MediaKind.Audio }
            ))
            Defaults.Accessibility.none

    let mediaSpec (id: string) (spec: MediaSpec) : Node<'Msg> =
        buildNode id (NodeKind.Media(spec)) Defaults.Accessibility.none

    /// Structured item list (Phase 287). Positional
    /// `Fuaran.list "id" [ "First"; "Second" ]` builds an unordered list of
    /// `Literal` items; the `listSpec` twin takes the full record (ordered
    /// lists, bound item text).
    let list (id: string) (items: string list) : Node<'Msg> =
        buildNode
            id
            (NodeKind.List(
                { Defaults.list with
                    Items = items |> List.map TextSource.Literal }
            ))
            Defaults.Accessibility.none

    let listSpec (id: string) (spec: ListSpec) : Node<'Msg> =
        buildNode id (NodeKind.List(spec)) Defaults.Accessibility.none

    /// A separator — a plain horizontal rule (Phase 459: the retired `Divider`,
    /// now a `Box` `Separator` role). Renders `<hr class="fuaran-layout-separator">`.
    /// For a labelled / vertical separator, author a `Fuaran.box` with
    /// `Role = Separator` + a `Heading` / horizontal layout axis directly.
    let divider (id: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Box(
                { Layout = BoxLayout.Flex(Orientation.Horizontal, false, Option.None)
                  Role = BoxRole.Separator
                  Heading = Option.None
                  Children = [] }
            ))
            Defaults.Accessibility.none

    /// Transient notification surface (Phase 289). The declarative, in-tree,
    /// SSR-rendered counterpart to `Action.Notify`. Renders inline (no portal)
    /// with `role="status"` + `aria-live` — see the overlay render-fidelity
    /// contract in docs/SSR.md.
    let toast (id: string) (spec: ToastSpec) : Node<'Msg> =
        buildNode id (NodeKind.Toast(spec)) Defaults.Accessibility.toast

    /// First-class code display (Phase 290). Positional
    /// `Fuaran.codeBlock "id" "fsharp" "let x = 1"` for the language+code 80%
    /// case; the `codeBlockSpec` twin takes the full record (line numbers,
    /// highlight ranges, copy toggle). Renders a deterministic `<pre><code>`
    /// (HTML-escaped, no markdown library); highlighting is client-only.
    let codeBlock (id: string) (language: string) (code: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.CodeBlock(
                { Defaults.codeBlock with
                    Language = language
                    Code = code }
            ))
            Defaults.Accessibility.none

    let codeBlockSpec (id: string) (spec: CodeBlockSpec) : Node<'Msg> =
        buildNode id (NodeKind.CodeBlock(spec)) Defaults.Accessibility.none

    /// LaTeX math (Phase 293). `Fuaran.math "id" "x^2 + y^2 = z^2"` is a block
    /// equation; the `mathSpec` twin takes the full record (inline mode). The
    /// parity render is the deterministic escaped-source fallback; KaTeX is a
    /// client-only post-hydration enhancement.
    let math (id: string) (source: string) : Node<'Msg> =
        buildNode id (NodeKind.Math({ Defaults.math with Source = source })) Defaults.Accessibility.none

    let mathSpec (id: string) (spec: MathSpec) : Node<'Msg> =
        buildNode id (NodeKind.Math(spec)) Defaults.Accessibility.none

    /// Bounded, typed vector-graphics primitive (Phase 524) — the shared render
    /// target charts lower to and the substrate for maps/diagrams. Positional
    /// `Fuaran.drawing "id" viewBox shapes` for the 80% case (root style +
    /// title/desc default); the full record form via the `drawingSpec` twin.
    /// The closed `Shape` DU (`Group` / `Rectangle` / `Line` / `Polyline` /
    /// `Polygon` / `Curve` / `Circle` / `Ellipse` / `Label`) carries no raw SVG
    /// — the opposite of `custom`.
    let drawing (id: string) (viewBox: ViewBox) (shapes: Shape list) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Drawing(
                { Defaults.drawing with
                    ViewBox = viewBox
                    Shapes = shapes }
            ))
            Defaults.Accessibility.none

    let drawingSpec (id: string) (spec: DrawingSpec) : Node<'Msg> =
        buildNode id (NodeKind.Drawing(spec)) Defaults.Accessibility.none

    /// Two-tier API per §4c: positional text for the 80% case
    /// (`Fuaran.markdown "id" "body"`), full record form via the `markdownSpec`
    /// twin (`Fuaran.markdownSpec "id" { Defaults.markdown with Text = ... }`).
    let markdown (id: string) (body: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Markdown(
                { Defaults.markdown with
                    Text = TextSource.Literal body }
            ))
            Defaults.Accessibility.none

    let markdownSpec (id: string) (spec: MarkdownSpec) : Node<'Msg> =
        buildNode id (NodeKind.Markdown(spec)) Defaults.Accessibility.none

    let callout (id: string) (spec: CalloutSpec) : Node<'Msg> =
        buildNode id (NodeKind.Callout(spec)) Defaults.Accessibility.callout

    let progress (id: string) (spec: ProgressSpec) : Node<'Msg> =
        buildNode id (NodeKind.Progress(spec)) Defaults.Accessibility.progress

    /// `Badge` and `Sparkline` had no smart constructor while every other
    /// `NodeKind` did — a §11 step-6 omission, which left callers building them
    /// from bare `Node` records and so re-deciding the envelope
    /// (`State` / `Style` / `Motion` / `Accessibility`) by hand each time. Both
    /// take the shape every sibling display kind takes, with
    /// `Defaults.Accessibility.none` for the same reason `markdown` / `skeleton`
    /// / `icon` do: neither kind carries an a11y contract of its own beyond what
    /// the renderer emits.
    let badge (id: string) (spec: BadgeSpec) : Node<'Msg> =
        buildNode id (NodeKind.Badge(spec)) Defaults.Accessibility.none

    let sparkline (id: string) (spec: SparklineSpec) : Node<'Msg> =
        buildNode id (NodeKind.Sparkline(spec)) Defaults.Accessibility.none

    let skeleton (id: string) (rows: int) : Node<'Msg> =
        buildNode id (NodeKind.Skeleton({ Rows = rows })) Defaults.Accessibility.none

    /// Phase 821 — the standalone icon-only display kind, decorative form
    /// (`Label = None` → `aria-hidden`). Full record form via `iconSpec`.
    let icon (id: string) (name: string) : Node<'Msg> =
        buildNode
            id
            (NodeKind.Icon(
                { Icon = name
                  Size = IconSize.Medium
                  Tone = ToneVariant.Default
                  Label = None }
            ))
            Defaults.Accessibility.none

    let iconSpec (id: string) (spec: IconSpec) : Node<'Msg> =
        buildNode id (NodeKind.Icon(spec)) Defaults.Accessibility.none

    // ─── Input ──────────────────────────────────────────────────────────

    let button (id: string) (spec: ButtonSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Button(spec)) Defaults.Accessibility.button

    let select (id: string) (spec: SelectSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Select(spec)) Defaults.Accessibility.select

    /// Multi-select (Phase 291). Sets `Multiple = true` and wires the
    /// list-valued selection (`Values` / `OnChangeMulti`); the single-value
    /// `Value` / `OnChange` are left at their defaults (unused in multi mode).
    /// Renders a `<select multiple>`. Author passes the option `Source`, the
    /// selected-`Values` binding, and an `onChange` over the value list.
    let multiSelect
        (id: string)
        (label: TextSource)
        (source: Binding<SelectOption list>)
        (values: Binding<string list>)
        (onChange: string list -> Action<'Msg>)
        : Node<'Msg> =
        buildNode
            id
            (NodeKind.Select(
                { Defaults.select with
                    Label = label
                    Source = source
                    Multiple = Some true
                    Values = Some values
                    OnChangeMulti = Some onChange }
            ))
            Defaults.Accessibility.select

    let form (id: string) (spec: FormSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Form(spec)) Defaults.Accessibility.form

    let filters (id: string) (specs: FilterSpec<'Msg> list) : Node<'Msg> =
        // The generated case carries a `FiltersSpec` record — the public
        // list-taking signature is unchanged; the list is wrapped here.
        buildNode id (NodeKind.Filters({ Items = specs })) Defaults.Accessibility.none

    let fileUpload (id: string) (spec: FileUploadSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.FileUpload(spec)) Defaults.Accessibility.fileUpload

    // ─── Visualisation ──────────────────────────────────────────────────

    let chart (id: string) (spec: ChartSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Chart(spec)) Defaults.Accessibility.chart

    /// Phase 393 — a static read-only table. Lowers into the `StaticRows` mode of
    /// `NodeKind.DataGrid` (one tabular kind); the renderer emits semantic `<table>`
    /// markup from the `TextSource` cells. `OnRowClick` on the retired `TableSpec`
    /// was host-only and is not carried (the mode is non-interactive).
    ///
    /// Phase 801 — `spec.Sortable` / `spec.DefaultSort` ride through to the
    /// `StaticRows` payload verbatim. Both `None` (the `Defaults.table` shape)
    /// emits the pre-801 wire byte-for-byte.
    let table (id: string) (spec: TableSpec<'Msg>) : Node<'Msg> =
        // `StaticRows` cells are `TextSource` (stage 4b closed the transient
        // string-narrowing at the IDL seam) — `Bound` / `I18n` cells ride
        // through unchanged, exactly as the retired hand shape carried them.
        let staticGrid: GridSpec<'Msg> =
            { Source = Binding.Static None
              RowKey = None
              RowKeyField = None
              SortStateKey = None
              PageSize = None
              PageStateKey = None
              DefaultSort = None
              EditStateKey = None
              Reorderable = false
              Columns = []
              OnRowClick = None
              Editable = false
              StaticRows =
                Some
                    { DefaultSort = spec.DefaultSort
                      Headers = spec.Headers
                      Rows = spec.Rows
                      Sortable = spec.Sortable } }

        buildNode id (NodeKind.DataGrid(staticGrid)) Defaults.Accessibility.table

    /// Phase 801 — the sort-intent shorthand over [[table]]: a static read-only
    /// table that declares it invites column sorting, with an optional initial
    /// order. `sortableTable id headers rows None` is the commonest shape; pass
    /// `Some { Column = 2; Direction = SortDirection.Desc }` to declare the table
    /// arrives sorted by its third column, descending.
    ///
    /// The declaration is intent, not behaviour: a host with no sorting
    /// affordance renders exactly the authored order, and the table is complete
    /// and readable without one.
    let sortableTable
        (id: string)
        (headers: TextSource list)
        (rows: TextSource list list)
        (defaultSort: DefaultSort option)
        : Node<'Msg> =
        table
            id
            { Defaults.table<'Msg> with
                Headers = headers
                Rows = rows
                Sortable = Some true
                DefaultSort = defaultSort }

    let map (id: string) (spec: MapSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Map(spec)) Defaults.Accessibility.map

    /// Take a typed `GridSpecOf<'row,'Msg>` and the REQUIRED `toRow` projection
    /// (fuaran#665, operator decision 2026-07-31): every `'row` the source
    /// yields is projected to the wire-expressible `Row` at this boundary, so a
    /// Static/State rows payload survives canonical encoding instead of
    /// collapsing to `"<opaque>"`. `toRow` is deliberately not defaultable —
    /// the only candidate defaults silently produce empty rows or throw, and an
    /// optional obligation is how the erasure went invisible in the first
    /// place. A call site whose rows are already `Row`-shaped passes `id`.
    let grid (id: string) (toRow: 'row -> Row) (spec: GridSpecOf<'row, 'Msg>) : Node<'Msg> =
        let erased: GridSpec<'Msg> =
            { Source =
                match spec.Source with
                | Binding.Static rows -> Binding.Static(rows |> Option.map (Seq.map toRow))
                | Binding.Query(name, acc, dependsOn) ->
                    Binding.Query(name, (fun o -> acc o |> Seq.map toRow), dependsOn)
                | Binding.Filter(name, _) -> Binding.Filter(name, None)
                | Binding.Selection(nodeId, acc, dv, fld) ->
                    Binding.Selection(nodeId, (fun o -> acc o |> Seq.map toRow), dv |> Option.map (Seq.map toRow), fld)
                | Binding.State(key, dv) -> Binding.State(key, dv |> Option.map (Seq.map toRow))
                | Binding.Computed f -> Binding.Computed(fun ctx -> f ctx |> Seq.map toRow)
                // Phase 765 — `Now` yields the host-furnished instant, a scalar.
                // It is meaningless as a grid's row source, but the DU is
                // parameterised on 'T so the typechecker permits it here; map
                // the accessor through `toRow` for uniformity and let resolution
                // surface the cast, exactly as `I18n` does below.
                | Binding.Now acc -> Binding.Now(fun ctx -> acc ctx |> Seq.map toRow)
                // `Binding.I18n` is semantically for `Binding<string>`
                // bindings, but the DU is parameterised on 'T so the typechecker
                // allows it on a grid's `Binding<'row seq>` Source. Pass through —
                // resolution will throw at the cast site, surfacing the
                // mis-use loudly rather than producing an empty grid silently.
                | Binding.I18n(key, args) -> Binding.I18n(key, args)
                // `Binding.Local` is semantically for `FormFieldKind.Text`
                // / `FormFieldKind.Number` only — validator FUARAN044 rejects it
                // outside those Kinds. The DU permits it on a grid's `Binding<'row
                // seq>` Source at the type level; resolution falls through to the
                // InitialFrom binding, surfacing the mis-use as the underlying
                // source rather than swallowing it silently.
                | Binding.Local _ -> Binding.Static None
                // `Binding.Format` is semantically for `Binding<string>`
                // (the formatter returns a string). The DU is parameterised on
                // 'T so it type-checks on a grid's `Binding<'row seq>` Source;
                // pass through — resolution throws at the cast site,
                // surfacing the mis-use loudly (same posture as `Binding.I18n`).
                | Binding.Format(source, fmt, locale) -> Binding.Format(source, fmt, locale)
                // `Binding.Transform` produces `Row seq` rows directly (the Core
                // evaluator's `Map<string,obj>` output IS the `Row` shape); it is
                // 'T-agnostic, so re-wrap unchanged — `toRow` has nothing to do here.
                | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
                // `Binding.Invoke` is 'T-agnostic (the resolved value is a host-produced `Deferred`);
                // re-wrap unchanged.
                | Binding.Invoke(capabilityId, args) -> Binding.Invoke(capabilityId, args)
              // The accessors are `Row`-typed on both sides since fuaran#665 —
              // no unbox wrapper survives.
              RowKey = Some spec.RowKey
              RowKeyField = None
              // Phases 861 / 862 / 863 (surfaced on the typed facade by Phase
              // 873): the declarative grid-behaviour slots pass through
              // unchanged. They are wire DECLARATIONS, not closures, so unlike
              // `Source` they need no `toRow` projection.
              SortStateKey = spec.SortStateKey
              PageSize = spec.PageSize
              PageStateKey = spec.PageStateKey
              DefaultSort = spec.DefaultSort
              EditStateKey = spec.EditStateKey
              // Phase 934's `reorderable` has no typed-facade slot yet — the
              // §11-step-6 follow-up for that phase, not this one's.
              Reorderable = false
              Columns = spec.Columns |> List.map Column.erase
              OnRowClick = spec.OnRowClick
              Editable = spec.Editable
              StaticRows = None }

        buildNode id (NodeKind.DataGrid(erased)) Defaults.Accessibility.grid

    // ─── Custom — bounded escape ─────────────────────────────────────────
    //
    // Parity smart-ctor for the existing `NodeKind.Custom` escape hatch.
    // Authors who construct `NodeKind.Custom` directly stay supported;
    // `Fuaran.custom` is the recommended shape because it (a) routes
    // through the same `buildNode` defaults every other kind uses,
    // (b) keeps `contentHash` + `exposedNodeIds` explicit at the call
    // site (instead of buried in a positional 5-tuple), and (c) gives
    // the validator a stable lexical pattern to walk for the FUARAN053
    // / FUARAN054 / FUARAN055 rules.
    //
    // Cultural-posture: Custom is the language's last-resort escape.
    // Reach for `Fuaran.metric` / `Fuaran.card` / etc. first; reach for
    // `Fuaran.custom` only when the typed surface genuinely doesn't
    // reach. See `docs/AI_AUTHORING_GUIDE.md` § "Custom — the last-
    // resort bounded escape".
    let custom
        (id: string)
        (moduleId: string)
        (componentId: string)
        (props: Map<string, JVal>)
        (contentHash: ContentHash option)
        (exposedNodeIds: NodeId list)
        : Node<'Msg> =
        // The generated case carries a `CustomSpec` record; the public
        // signature is unchanged. `ExposedNodeIds` is `string list option`
        // since the swap — `[]` maps to `None` (the wire-omitted shape) and
        // the `NodeId` wrappers unwrap at this boundary.
        buildNode
            id
            (NodeKind.Custom(
                { ModuleId = moduleId
                  ComponentId = componentId
                  Props = props
                  ContentHash = contentHash
                  ExposedNodeIds =
                    match exposedNodeIds with
                    | [] -> Option.None
                    | ids -> Some(ids |> List.map (fun (NodeId s) -> s)) }
            ))
            Defaults.Accessibility.none

    // ─── ErrorBoundary — render-time graceful-degradation ───────────────
    //
    // AI-emittable shape for "render this fallback if the child subtree
    // throws during render". Pairs with the per-node render guard in
    // Render.fs: the guard is the always-on floor (one broken leaf renders
    // a fallback placeholder instead of blanking the page); the boundary
    // is the explicit, author-chosen wrapper. Author signals "I expect
    // this subtree may fail under some inputs" by wrapping it in
    // `Fuaran.errorBoundary`; the renderer routes through the fallback on
    // any throw inside the child subtree and emits a render-failure
    // telemetry event so the orchestrator learns its emission was
    // malformed and can self-correct.
    let errorBoundary (id: string) (spec: ErrorBoundarySpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.ErrorBoundary spec) Defaults.Accessibility.none

    // ─── Switch — state-bound conditional child (Phase 392) ─────────────
    //
    // Renders one of several child subtrees based on a reactive StateStore
    // key. `StateKey` names the key; `Cases` is an ordered list of
    // `(matchValue, child)` pairs; `Default` renders when no case matches.
    // The renderer resolves the state value's string form, renders the first
    // case whose `matchValue` equals it (first-match-wins), else `Default`.
    // State transitions arrive as ordinary typed `Action.SetState` actions
    // through the default-deny policy gate (FGP 3 — no new dispatch path); the
    // client re-renders the matching case on change, the server/SSR renders the
    // initial match. The kernel-power piece for conditional regions / wizard
    // panes / empty-state alternatives / mode toggles — compose over `Switch`
    // rather than proposing a new kind (see `docs/VOCABULARY.md` §1.2). See
    // `NodeKind.Switch` in `Types.fs`.
    let switch (id: string) (spec: SwitchSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Switch spec) Defaults.Accessibility.none

    // ─── Fragment — reusable subtree primitive ──────────────────────────
    //
    // `fragmentDecl` declares a named reusable body that renders nothing
    // at the decl site — the body becomes visible only when a matching
    // `fragmentRef` expands it. `fragmentRef` expands the named decl
    // with interior `data-fuaran-node-id` values namespaced by the
    // ref's own id (`<refId>.<innerId>`) so multiple refs to the same
    // fragment produce DOM-unique addressable ids. See
    // `NodeKind.FragmentDecl` / `FragmentRef` in `Types.fs` for the
    // scoping + cycle-resolution rules; see `cookbook/` "Reusable
    // subtree" recipe (forthcoming) for canonical authoring shapes.
    let fragmentDecl (id: string) (spec: FragmentDeclSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.FragmentDecl spec) Defaults.Accessibility.none

    let fragmentRef (id: string) (name: string) : Node<'Msg> =
        // `Name` is a bare string on the generated spec; `Args = None` is the
        // name-only degenerate ref (≡ the old empty map, omitted on the wire).
        buildNode id (NodeKind.FragmentRef { Name = name; Args = Option.None }) Defaults.Accessibility.none

    // ─── Mount — isolation/embedding boundary (Phase 265, §4o) ──────────
    //
    // `mount` embeds an isolated guest subtree at this point: the guest
    // carries its own message space, scoped state + runtime, op-stream, and a
    // per-mount default-deny capability gate (iframe-like composition). The
    // guest's `'GuestMsg` lives *behind* the mount (resolved by the host
    // orchestration tier), reaching the host as an `Action<obj>` via
    // `MountSpec.OnBubble`. The guest's own render tree is produced host-side
    // by the guest loader from the scope — it is not carried in the spec. See
    // §4o + `NodeKind.Mount` in `Types.fs`. For the same-process case where a
    // guest and host share a typed `'Msg` family, prefer a typed `embed`
    // convenience (which lowers to a `Mount` via `Node.mapMsg`, Phase 268) over
    // a hand-built `Mount`. `guestOutChannel` builds the default OutOnly
    // channel; `MountSpec.Capabilities = []` is the default-deny posture.
    let mount (id: string) (spec: MountSpec<'Msg>) : Node<'Msg> =
        buildNode id (NodeKind.Mount spec) Defaults.Accessibility.none

    /// A default out-only guest channel (safe for untrusted guests) with no
    /// declared message shape. Compose with `{ guestOutChannel with … }` for a
    /// named shape or `TwoWay` direction.
    let guestOutChannel: GuestChannel =
        { Direction = ChannelDirection.OutOnly
          MessageShape = Option.None }
