module Fuaran.UI.Renderer.BindingResolver

// ============================================================================
//  Fuaran — binding resolution (§4b Binding<'T>, §4c idioms lines 591–602)
//
//  Resolves a typed `Binding<'T>` to its current `'T` value.  Authors write
//  `binding.query "totalRevenue" _.amount` and the renderer must produce a
//  current `float` to feed the Metric.  This file is the typed surface; the
//  data sources (query result cache, module state store, filter / selection
//  registry) live consumer-side and are passed in via `BindingSources`.
//
//  Session 3a scope: `Static`, `State`, and a stub `Query` that pulls from a
//  caller-supplied `Map<string, obj>`.  `Selection` and `Filter` need the
//  renderer to track which nodes have selections / filters active — that's
//  session 3b once the renderer has a runtime registry.  `Computed` is the
//  F#-only escape that doesn't serialise; sessions 3+ leave it best-effort.
//
//  Per-Defect (2) note: `Binding.Query` / `Binding.Selection` carry obj-erased
//  accessors at the tree level.  The resolver unboxes the captured value back
//  to `'T` via the stored closure.  Authors never see the obj boundary.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

// This module lives at the typed-tree's obj-erasure boundary: a `Binding<'T>`
// resolves through boxed `obj` cells, and `null` is a first-class value there
// (an absent query result, a `Cell.Null`, a JSON null). The `isNull` tests and
// `null` match arms below are therefore load-bearing, and F# 10's nullness
// checker rejects them on a bare `obj` (FS3261). The project-wide
// `<Nullable>disable</Nullable>` does not travel under Fable — there the ENTRY
// project's setting governs the whole source graph — so the suppression is
// file-scoped here, per the obj-erasure `#nowarn` precedents in
// Fuaran.UI/Types.fs (`module Binding`) and Fuaran.fs. See `Sanitize.fs`.
#nowarn "3261"

/// The default `II18nResolver`. Pass-through identity:
/// returns the debug placeholder `[i18n:<key>]` for every key so missing
/// translations stay visually loud in dev. Apps wire a real resolver
/// (a platform host's i18n resolver, or a hand-rolled
/// gettext / ICU / template-string resolver) by replacing
/// `BindingSources.I18nResolver`.
let passthroughI18nResolver: II18nResolver =
    { new II18nResolver with
        member _.Resolve(key, _args) = sprintf "[i18n:%s]" key }

/// Build an `II18nResolver` that consults a `Map<string, string>` catalog
/// and substitutes `{argName}` placeholders with values from the args map.
/// Convenience constructor for tests + the simple single-language case;
/// real apps typically plug in a richer resolver. Missing keys fall through
/// to the `[i18n:<key>]` debug shape.
let makeI18nResolver (catalog: Map<string, string>) : II18nResolver =
    { new II18nResolver with
        member _.Resolve(key, argsOpt) =
            match Map.tryFind key catalog with
            | Some template ->
                match argsOpt with
                | Some args ->
                    args
                    |> Map.fold
                        (fun (acc: string) (k: string) (v: obj) ->
                            let needle = "{" + k + "}"
                            let replacement = if isNull v then "" else string v
                            acc.Replace(needle, replacement))
                        template
                | None -> template
            | None -> sprintf "[i18n:%s]" key }

/// Data sources the renderer consults when it encounters a binding.
/// Consumers (consumer apps, a future AI consumer) provide
/// their own implementation; session 3a ships only the in-memory variant.
type BindingSources =
    {
        /// Resolved query results keyed by the binding's `name`.  The stored
        /// value is the `'result` the author's `binding.query` accessor was
        /// declared against — the resolver hands it to the accessor as-is via
        /// the obj-erasure closure.
        QueryResults: Map<string, obj>
        /// Module-level state, keyed by `binding.state`'s `key`.
        State: Map<string, obj>
        /// Active filter values, keyed by `binding.filter`'s `name`.
        Filters: Map<string, obj>
        /// Selection state keyed by `NodeId` — e.g. the currently-selected row
        /// in a Grid is stored under the Grid's NodeId.
        Selections: Map<NodeId, obj>
        /// Seed `BindingContext` for `Binding.Computed`. Phase 137: the
        /// resolver projects the live `State` bag (above) into the context it
        /// hands the closure, so a `Computed` closure reads state via
        /// `ctx.TryGetState<'T> key` with no extra wiring. This field lets a
        /// consumer inject *additional* keys not in `State`; live `State` wins
        /// on a key clash. Defaults to `BindingContext.empty`.
        ComputedContext: BindingContext
        /// i18n catalog — keys map to localised strings. Session 3b's
        /// `TextSource.I18n` resolver substitutes `{argName}` placeholders in
        /// the localised string with values from the `TextSource.I18n` args map.
        /// Empty map ⇒ the renderer falls back to a `[i18n:key]` debug
        /// placeholder so missing-translation cases stay loud (same
        /// behaviour as session 3a). `Binding.I18n` uses
        /// `I18nResolver` (below) instead — the map remains for `TextSource.I18n`
        /// backward compat.
        I18n: Map<string, string>
        /// `II18nResolver` for `Binding.I18n` resolution. Default
        /// is `passthroughI18nResolver` (every key → `[i18n:<key>]`). Apps
        /// override by replacing this field on the `BindingSources` record.
        I18nResolver: II18nResolver
        /// Ambient locale (BCP-47 tag) the host supplies for
        /// `Binding.Format` resolution when the binding's `LocaleSource` is
        /// `Ambient` (Phase 102 — the 12.I locale source). Default is the
        /// empty string, the identity-default meaning "use the runtime default
        /// locale" (`Intl` with `undefined` / .NET `InvariantCulture`). A
        /// data-heavy app lifts its global locale toggle into
        /// this field so every `Binding.Format` re-renders against the chosen
        /// locale. `LocaleSource.Explicit` bypasses this and pins its own tag.
        Locale: string
        /// Host capability invoker (Phase 283 — the Compute layer). Given a `Binding.Invoke`'s
        /// `capabilityId` + scalar `(addr, value)` args, returns the current `Deferred<obj>`
        /// resolution of the invocation. The default returns `Pending` (the node shows its
        /// `onLoading` subtree); a real host — the `Fuaran.UI.AiTools` capability registry + its
        /// dispatch/replay loop — replaces this to validate args against the capability's
        /// `Signature`, run the host body, and journal non-deterministic results through the
        /// determinism-capture seam.
        CapabilityInvoker: string -> (string * string) list -> Fuaran.UI.Types.Deferred<obj>
        /// The current instant the host furnishes for `Binding.Now` (Phase 765),
        /// as an **ISO-8601 UTC** string (`2026-08-02T06:59:24Z`).
        ///
        /// The clock lives HERE, in the host — never on the wire and never read
        /// during resolution. That is what keeps a tree a pure value: a replayed
        /// op-stream re-supplies the instant it recorded, so replay reproduces
        /// the original render instead of drifting to whatever "now" means at
        /// replay time. Resolve it ONCE per render pass and hold it for the
        /// whole pass, or two `Now` slots in one tree can disagree.
        ///
        /// The ISO-8601 instant form is deliberate: `Fuaran.Core`'s
        /// `DateDiffDays` reads the leading `YYYY-MM-DD`, so a day-delta against
        /// `Now` composes with **zero** Core change, while the retained time
        /// component leaves finer-grained verbs (relative minutes) possible later
        /// without re-cutting the wire.
        ///
        /// Default is the empty string — "this host furnishes no clock" — which
        /// resolves `NotResolved`, so the node shows its `onLoading`/placeholder
        /// surface. That is deliberately LOUD: a host that forgets to supply the
        /// instant must not silently render a plausible wrong date.
        Now: string
    }

/// The empty `BindingSources` — useful for tests and for the renderer
/// scaffolding before consumer data plumbing lands.
let empty: BindingSources =
    { QueryResults = Map.empty
      State = Map.empty
      Filters = Map.empty
      Selections = Map.empty
      ComputedContext = BindingContext.empty
      I18n = Map.empty
      I18nResolver = passthroughI18nResolver
      Locale = ""
      CapabilityInvoker = (fun _ _ -> Fuaran.UI.Types.Deferred.Pending)
      Now = "" }

/// Resolution result.  Renderer code treats `NotResolved` as the trigger for
/// the `OnLoading` state behaviour; `Resolved` flows into the component body;
/// `Error` flows into `OnError`.
type Resolution<'T> =
    | Resolved of 'T
    | NotResolved
    | Errored of message: string
    /// `Binding.I18n` resolution returned the debug-placeholder
    /// shape (`[i18n:<key>]`), signalling the resolver had no translation for
    /// the key. Distinct from `NotResolved` (a data-source absence) so the
    /// renderer + Catalog can surface missing translations specifically.
    | I18nUnresolved of key: string

// ─── Transform cell bridging (Phase 282/424; module-level since Phase 632 so the
//     rows path and the scalar path share one vocabulary) ─────────────────────

/// Unbox a `Fuaran.Core.Cell` to the boxed row-cell `obj` shape rows carry.
let private cellToObj (c: Fuaran.Core.Cell) : obj =
    match c with
    | Fuaran.Core.Int i -> box i
    | Fuaran.Core.Float f -> box f
    | Fuaran.Core.Bool b -> box b
    | Fuaran.Core.Str s -> box s
    | Fuaran.Core.Date s -> box s
    | Fuaran.Core.Timestamp s -> box s
    | Fuaran.Core.Null -> null

/// Coerce a resolved scalar to a `Cell`. Every numeric arm yields `Float` (int/float are
/// indistinguishable under Fable, so this stays cross-host-deterministic); DataFrame
/// comparison treats `Int`/`Float` as numbers, so a float-carried param compares correctly.
let private objToCell (v: obj) : Fuaran.Core.Cell option =
    match v with
    | :? string as s -> Some(Fuaran.Core.Str s)
    | :? bool as b -> Some(Fuaran.Core.Bool b)
    | :? float as f -> Some(Fuaran.Core.Float f)
    | :? int as i -> Some(Fuaran.Core.Float(float i))
    | :? int64 as i -> Some(Fuaran.Core.Float(float i))
    | null -> Some Fuaran.Core.Null
    | _ -> None

/// The `JVal` twin of [[objToCell]] — a Transform param's `from` source is
/// `Binding<JVal>` since the swap. Same numeric policy (every number yields
/// `Float`); an array / object is non-scalar (`None`).
let private jvalToCell (v: JVal) : Fuaran.Core.Cell option =
    // `JVal` has no null case (D1 — absence is structural, never a value).
    match v with
    | JStr s -> Some(Fuaran.Core.Str s)
    | JBool b -> Some(Fuaran.Core.Bool b)
    | JFloat f -> Some(Fuaran.Core.Float f)
    | JInt i -> Some(Fuaran.Core.Float(float i))
    | JArr _
    | JObj _ -> None

/// Project a resolved i18n arg's `JVal` to the `obj` shape the
/// `II18nResolver` contract carries (scalars box; structures stringify
/// canonically — the resolver's template substitution stringifies anyway).
let private jvalToArgObj (v: JVal) : obj =
    match v with
    | JStr s -> box s
    | JBool b -> box b
    | JFloat f -> box f
    | JInt i -> box i
    | other -> box (Fuaran.Core.Canon.render other)

// ─── The Binding<JVal> → Binding<obj> erasure for STORE-READING sources ──────
//
// A `Transform` param / `I18n` arg source is `Binding<JVal>` since the swap
// (the typed verbatim carrier, D3) — but the Filter / State / Selection stores
// hold RAW host values (a chip write stores `box "eng"`, a grid click the raw
// row), exactly as before the swap. Resolving such a source AT `JVal` would
// unbox-cast a raw primitive to a union — a throw on .NET and, worse, a silent
// mis-match under Fable erasure. So these paths resolve at `obj` through this
// erasure and COERCE afterwards: a genuine `JVal` payload (an authored Static,
// a decoded defaultValue) passes through as itself; a raw store value stays raw.
let rec private objOfJValBinding (b: Binding<JVal>) : Binding<obj> =
    match b with
    | Binding.Static v -> Binding.Static(v |> Option.map box)
    | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> box, dependsOn)
    | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map box)
    | Binding.Selection(nodeId, _, dv, fld) ->
        // The JVal-typed accessor is bypassed on this path: project the RAW
        // row field (the store holds raw cells), matching the pre-swap
        // `Binding<obj>` param behaviour byte-for-byte at resolution time.
        let accessor: obj -> obj =
            match fld with
            | Some f -> Binding.projectSelectionField<obj> f
            | None -> id

        Binding.Selection(nodeId, accessor, dv |> Option.map box, fld)
    | Binding.State(key, dv) -> Binding.State(key, dv |> Option.map box)
    | Binding.Computed f -> Binding.Computed(f >> box)
    // Phase 765 — the JVal-typed accessor is bypassed here for the same
    // reason `Selection`'s is: the host furnishes a RAW string, not a
    // `JVal`, so resolving at `JVal` would unbox-cast a primitive to a
    // union (a throw on .NET, a silent mismatch under Fable erasure).
    | Binding.Now _ -> Binding.Now id
    | Binding.I18n(key, args) -> Binding.I18n(key, args)
    | Binding.Local(flushOn, format, initialFrom, onCommit, parse) ->
        Binding.Local(
            flushOn,
            (fun (o: obj) -> format (unbox<JVal> o)),
            objOfJValBinding initialFrom,
            onCommit |> Option.map (fun oc -> fun (o: obj) -> oc (unbox<JVal> o)),
            (fun s -> parse s |> Result.map box)
        )
    | Binding.Format(source, format, locale) -> Binding.Format(source, format, locale)
    | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
    | Binding.Invoke(capabilityId, args) -> Binding.Invoke(capabilityId, args)

/// Coerce a param source's resolved `obj` to a `Cell`: a genuine `JVal`
/// (authored/decoded payload) via [[jvalToCell]]; anything else raw via
/// [[objToCell]].
let private resolvedToCell (v: obj) : Fuaran.Core.Cell option =
    match v with
    | :? JVal as jv -> jvalToCell jv
    | other -> objToCell other

// ─── Store-value → JVal lift (Phase 818 — the reactive-derivation first cut) ─
//
// A LIVE Transform source and a `SetState.valueFrom` both read a store value
// and need it as structured data. What a store actually holds differs by host:
// the browser StateStore holds PLAIN JS values (the runtime lowers a SetState
// JVal through the JSON bridge before storing); the bounded server store holds
// the `JValObj` lowering (numbers as floats, objects as `Map<string, obj>`,
// arrays as `obj list`); a decoded binding's carried defaultValue resolves as
// a genuine `JVal` instance on both. The lift re-raises each to `JVal`
// structurally; a value with no faithful lift is `None` — every caller
// surfaces that LOUDLY (the Phase-427 mismatch posture, never a silent wrong
// value). Number policy matches the wire bridge (`jsonToJVal`): an integral
// in-int32-range number lifts to `JInt`, everything else to `JFloat`.

#if FABLE_COMPILER
module private JsLift =
    open Fable.Core

    [<Emit("typeof $0")>]
    let jsTypeof (_: obj) : string = jsNative

    [<Emit("Array.isArray($0)")>]
    let jsIsArray (_: obj) : bool = jsNative

    [<Emit("$0 !== null && typeof $0 === 'object' && ($0.constructor === Object || Object.getPrototypeOf($0) === null)")>]
    let jsIsPlainObject (_: obj) : bool = jsNative

    [<Emit("Object.keys($0)")>]
    let jsKeys (_: obj) : string[] = jsNative

    [<Emit("$0[$1]")>]
    let jsGet (_: obj) (_: string) : obj = jsNative
#endif

let private liftNumber (f: float) : JVal =
    if
        not (System.Double.IsNaN f)
        && not (System.Double.IsInfinity f)
        && floor f = f
        && abs f <= 2147483647.0
    then
        JInt(int f)
    else
        JFloat f

/// Lift a store-resolved value to a `JVal` (see the section note above).
/// `None` when the value has no faithful structural lift — callers surface
/// that loudly, never silently.
let rec jvalOfResolved (v: obj) : JVal option =
#if FABLE_COMPILER
    match v with
    | :? JVal as jv -> Some jv
    | _ when isNull v -> None
    | _ ->
        match JsLift.jsTypeof v with
        | "string" -> Some(JStr(unbox<string> v))
        | "boolean" -> Some(JBool(unbox<bool> v))
        | "number" -> Some(liftNumber (unbox<float> v))
        | _ ->
            if JsLift.jsIsArray v then
                let items = unbox<obj[]> v |> Array.toList |> List.map jvalOfResolved

                if items |> List.forall Option.isSome then
                    Some(JArr(items |> List.map Option.get))
                else
                    None
            elif JsLift.jsIsPlainObject v then
                let fields =
                    JsLift.jsKeys v
                    |> Array.toList
                    |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                    |> List.map (fun k -> k, jvalOfResolved (JsLift.jsGet v k))

                if fields |> List.forall (fun (_, fv) -> Option.isSome fv) then
                    Some(JObj(fields |> List.map (fun (k, fv) -> k, Option.get fv)))
                else
                    None
            else
                None
#else
    match v with
    | :? JVal as jv -> Some jv
    | :? string as s -> Some(JStr s)
    | :? bool as b -> Some(JBool b)
    | :? int as i -> Some(JInt i)
    | :? int64 as i -> Some(liftNumber (float i))
    | :? float as f -> Some(liftNumber f)
    | :? (obj list) as xs ->
        // The `JValObj` array lowering (the bounded server store's shape).
        let items = xs |> List.map jvalOfResolved

        if items |> List.forall Option.isSome then
            Some(JArr(items |> List.map Option.get))
        else
            None
    | :? Map<string, obj> as m ->
        // The `JValObj` object lowering / a single `Row`.
        let fields = m |> Map.toList |> List.map (fun (k, fv) -> k, jvalOfResolved fv)

        if fields |> List.forall (fun (_, fv) -> Option.isSome fv) then
            Some(JObj(fields |> List.map (fun (k, fv) -> k, Option.get fv)))
        else
            None
    | :? seq<Row> as rows ->
        // The editable-grid write-back shape: a `Row seq` written whole to a
        // State key. Rows lift element-wise (each `Row` is the Map arm above).
        let items = rows |> Seq.toList |> List.map (box >> jvalOfResolved)

        if items |> List.forall Option.isSome then
            Some(JArr(items |> List.map Option.get))
        else
            None
    | _ -> None
#endif

/// Phase 818 — materialise a LIVE Transform source's resolved store value as
/// the input table: lift to `JVal`, normalise (row-major rows transpose to
/// canonical columnar — the same rule the decode-time snapshot applies), then
/// decode through Core's columnar codec. Loud on every unliftable /
/// non-tabular value.
let private liveValueToTable (v: obj) : Result<Fuaran.Core.Table, string> =
    match jvalOfResolved v with
    | None ->
        Error
            "Transform live source resolved to a value that cannot be read as data — expected rows (an array of row objects) or canonical columnar data"
    | Some jv ->
        match Fuaran.UI.HostPrelude.TransformLive.initialSource jv with
        | Ok(Fuaran.Core.Embedded t) -> Ok t
        | Ok(Fuaran.Core.Ref name) ->
            Error(sprintf "Transform live source resolved to a 'ref' source ('%s'), which is not host-resolved" name)
        | Error e -> Error("Transform live source: " + Fuaran.Core.ColumnCodec.errorString e)

/// Resolve a typed `Binding<'T>` against the supplied sources.
///
/// Per-Defect (2) note: `Query` / `Selection` accessors are obj-typed at the
/// tree level.  The resolver hands the obj-typed source value straight to
/// the captured accessor; the accessor closure was built by `binding.query` /
/// `binding.selection` and knows how to unbox internally.  No reflection.
let rec resolve<'T> (sources: BindingSources) (binding: Binding<'T>) : Resolution<'T> =
    match binding with
    // Since the swap the payload is `'T option`; the absent form resolves to
    // the slot's default representation — exactly the value the pre-swap
    // `Static` carried in the absent case (`null` / inner `None`), so an
    // option-typed slot still reads "no selection" and renders its placeholder.
    | Binding.Static(Some value) -> Resolved value
    | Binding.Static None -> Resolved Unchecked.defaultof<'T>
    | Binding.Query(name, _, _) when name = Fuaran.UI.Defaults.NotProvidedSentinel ->
        // `Defaults.noBinding<'T>` encodes "Source = is mandatory but the
        // author hasn't overridden the default yet" as a Query against this
        // sentinel name. Short-circuit before the accessor closure
        // (which would return `Unchecked.defaultof<'T>`) is ever called.
        NotResolved
    // `dependsOn` (Phase 421) is a re-render/invalidation edge, not a resolution input — the accessor
    // still owns the predicate. The renderer subscribes on those filter keys (Render.fs `keysOfBinding`),
    // so a filter-store change re-resolves this Query; resolution itself is unchanged.
    | Binding.Query(name, accessor, _) ->
        match Map.tryFind name sources.QueryResults with
        | Some raw ->
            try
                Resolved(accessor raw)
            with ex ->
                Errored(sprintf "Query '%s' accessor threw: %s" name ex.Message)
        | None -> NotResolved
    | Binding.Filter(name, defaultValue) ->
        match Map.tryFind name sources.Filters with
        | Some raw ->
            try
                Resolved(unbox<'T> raw)
            with ex ->
                // Fable erases generic type info at runtime so we can't quote
                // `typeof<'T>.Name` here — keep the message Fable-compatible.
                Errored(sprintf "Filter '%s' value did not unbox to expected type: %s" name ex.Message)
        | None ->
            // 0.2.0 — the pre-selected-filter gap: an unwritten filter key
            // resolves to the binding's declared default (the renderer also
            // seeds the store from it on first write-back).
            match defaultValue with
            | Some d -> Resolved d
            | None -> NotResolved
    | Binding.Selection(nodeId, accessor, defaultValue, _) ->
        match Map.tryFind (NodeId nodeId) sources.Selections with
        | Some raw ->
            try
                Resolved(accessor raw)
            with ex ->
                Errored(sprintf "Selection on %A accessor threw: %s" nodeId ex.Message)
        | None ->
            // 0.2.9 (Phase 629) — the pre-selected-row gap, the Filter law
            // replayed for the selection channel: no row selected yet resolves
            // to the binding's declared default; the first real selection
            // (a SelectionStore write) takes over and the default never
            // reapplies while the entry exists.
            match defaultValue with
            | Some d -> Resolved d
            | None -> NotResolved
    | Binding.State(key, defaultValue) ->
        match Map.tryFind key sources.State with
        | Some raw ->
            try
                Resolved(unbox<'T> raw)
            with ex ->
                Errored(sprintf "State '%s' value did not unbox to expected type: %s" key ex.Message)
        | None ->
            // `Binding.State` declares its own default, so absence is not
            // a NotResolved condition — it's the "no override yet" steady
            // state and resolves to the default. A default-less binding
            // (`None` since the swap) resolves to the slot's default
            // representation, matching the pre-swap decode placeholder.
            match defaultValue with
            | Some d -> Resolved d
            | None -> Resolved Unchecked.defaultof<'T>
    | Binding.Now accessor ->
        // Phase 765 — the host-furnished instant. The clock is NOT read here:
        // `sources.Now` was resolved once, host-side, for the whole render pass,
        // which is what makes a replayed op-stream reproduce its original render
        // instead of drifting to replay-time "now".
        //
        // An unset instant is `NotResolved`, so the node shows its
        // `onLoading`/placeholder surface. That is deliberately loud — a host
        // that forgets to furnish the clock must not silently render a
        // plausible wrong date, which is exactly the failure the models were
        // producing by hardcoding one.
        if System.String.IsNullOrEmpty sources.Now then
            NotResolved
        else
            Resolved(accessor (box sources.Now))
    | Binding.Computed f ->
        // Phase 137: hand the closure a context with typed read access to the
        // live module-state bag. `sources.State` is authoritative (the same map
        // `Binding.State` resolves against); any keys explicitly injected via
        // `ComputedContext` are merged underneath, live state winning on a clash.
        let ctx =
            let injected = sources.ComputedContext.State

            let merged =
                if Map.isEmpty injected then
                    sources.State
                else
                    sources.State |> Map.fold (fun acc k v -> Map.add k v acc) injected

            { sources.ComputedContext with
                State = merged }

        try
            Resolved(f ctx)
        with ex ->
            Errored(sprintf "Computed binding threw: %s" ex.Message)
    | Binding.Local(_, _, initialFrom, _, _) ->
        // A `Binding.Local` is structurally a re-sync source +
        // local-buffer overlay. Pure resolution returns the InitialFrom-
        // side value — the per-NodeId React.useState slot is mounted by
        // the renderer's `LocalBindings` module and overrides this read
        // at the form-field render layer. Bindings used outside that
        // context (e.g. accidentally placed on a Metric Source) resolve to
        // the underlying source, which is the principled behaviour: the
        // validator's FUARAN044 catches the misplacement at build time,
        // but if it slips to runtime the read is still meaningful.
        resolve<'T> sources initialFrom
    | Binding.I18n(key, argsOpt) ->
        // Resolve args (each `Binding<obj>`) into a `Map<string, obj>`
        // for the resolver. Failed arg resolutions substitute an empty string;
        // the resolver still gets called so the key-level template substitution
        // proceeds. The resolver returns a string; cast to 'T via unbox — only
        // valid when 'T = string (same shape as `Binding.Query` accessor's cast).
        let resolvedArgs =
            argsOpt
            |> Option.map (fun args ->
                args
                |> Map.map (fun _ b ->
                    // Resolve at obj through the store-reading erasure (see
                    // objOfJValBinding); a genuine JVal payload projects to the
                    // resolver-contract obj shape, a raw store value stays raw.
                    match resolve<obj> sources (objOfJValBinding b) with
                    | Resolved v ->
                        (match v with
                         | :? JVal as jv -> jvalToArgObj jv
                         | other -> other)
                    | NotResolved
                    | Errored _
                    | I18nUnresolved _ -> box ""))

        let resolved = sources.I18nResolver.Resolve(key, resolvedArgs)
        // Detect debug-placeholder shape (the passthrough resolver's
        // `[i18n:<key>]` and the map-backed resolver's miss-fallback)
        // so the renderer can branch on `I18nUnresolved` for missing
        // translations. A real resolver that genuinely returns a string
        // that happens to match this shape is technically conflated; the
        // signal is intentional (an i18n catalog should never contain a
        // translation of the form `[i18n:something-else]`).
        let placeholderShape = sprintf "[i18n:%s]" key

        if resolved = placeholderShape then
            I18nUnresolved key
        else
            try
                Resolved(unbox<'T> (box resolved))
            with ex ->
                Errored(
                    sprintf
                        "I18n '%s' resolved string did not unbox to expected type (Binding.I18n is constrained to Binding<string>): %s"
                        key
                        ex.Message
                )
    | Binding.Format(source, fmt, localeSource) ->
        // Resolve the numeric source, then project to a localised
        // string via the Intl (Fable) / Globalization (.NET) formatter. The
        // resolved string is cast to 'T via unbox — only valid when 'T =
        // string (same constraint + failure shape as Binding.I18n).
        match resolve<float> sources source with
        | Resolved n ->
            let localeTag =
                match localeSource with
                | LocaleSource.Explicit tag -> tag
                | LocaleSource.Ambient -> sources.Locale

            let formatted = Formatting.format localeTag fmt n

            try
                Resolved(unbox<'T> (box formatted))
            with ex ->
                Errored(
                    sprintf
                        "Format binding produced a string that did not unbox to the expected type (Binding.Format is constrained to Binding<string>): %s"
                        ex.Message
                )
        | NotResolved -> NotResolved
        | Errored m -> Errored m
        | I18nUnresolved k -> I18nUnresolved k
    | Binding.Transform(source, pipeline, parameters) ->
        // Phase 282 — the declarative Compute layer. Evaluate the serialisable dataframe pipeline
        // via the `Fuaran.Core.DataFrame` reference evaluator (the same evaluator `transformLaws`
        // certifies — the param/prune/eval machinery lives in `evalTransformFrame`, shared with the
        // Phase-632 scalar path), then surface the result rows as `Row seq` — each row the
        // `Map<string,obj>` keyed by column name, the cell scalar boxed (fuaran#665 named the
        // shape; the representation is unchanged). Constrained to `Binding<Row seq>` use at a
        // data-bearing node (same 'T-constraint posture as `Binding.Format` / `Binding.I18n`);
        // a SCALAR slot (TextSource, Metric/LabelValueRow values) resolves through
        // `resolveScalarWith` instead (Phase 632).
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m
        | Ok result ->
            let names = Fuaran.Core.Table.columnNames result
            let rowCount = Fuaran.Core.Table.rowCount result

            let rows: Row list =
                [ for i in 0 .. rowCount - 1 ->
                      names
                      |> List.map (fun n ->
                          let cell =
                              Fuaran.Core.Table.tryColumn n result
                              |> Option.map (Fuaran.Core.Column.cell i)
                              |> Option.defaultValue Fuaran.Core.Null

                          n, cellToObj cell)
                      |> Map.ofList ]

            try
                Resolved(unbox<'T> (box (Seq.ofList rows)))
            with ex ->
                Errored(
                    sprintf
                        "Transform binding produced rows that did not unbox to the expected type (Binding.Transform is constrained to Binding<Row seq>): %s"
                        ex.Message
                )
    | Binding.Invoke(capabilityId, args) ->
        // Phase 283 — dispatch a host-registered capability for a value. The host invoker resolves
        // (capabilityId, args) to a `Deferred<obj>`; map `Pending` → `NotResolved` (the node's
        // `onLoading`), `Ready` → `Resolved`, `Error` → `Errored` (its `onError`) — reusing the
        // `StateBehaviour` surface, no new node. The default invoker is `Pending` until a host
        // (the AiTools registry) wires real dispatch + Phase-27 replay.
        match sources.CapabilityInvoker capabilityId (args |> List.map (fun (a: InvokeArg) -> a.Addr, a.Value)) with
        | Fuaran.UI.Types.Deferred.Pending -> NotResolved
        | Fuaran.UI.Types.Deferred.Error m -> Errored m
        | Fuaran.UI.Types.Deferred.Ready v ->
            try
                Resolved(unbox<'T> v)
            with ex ->
                Errored(sprintf "Invoke '%s' result did not unbox to the expected type: %s" capabilityId ex.Message)

/// The shared Transform evaluation (Phase 282/424 machinery, extracted in Phase 632 so the
/// rows path and the scalar path evaluate identically): resolve each `parameters` entry's
/// scalar `from` binding to a `Cell` and build the evaluation env; a param whose source is
/// `NotResolved` (an unset choice filter) is *unbound* — every `filter` step referencing an
/// unbound param is PRUNED (the one lenient "unset filter ⇒ no constraint" rule — Core stays
/// strict), while a *non-filter* step referencing an unbound param surfaces Core's
/// `UnboundParam` loudly (never silent). A non-scalar or `Errored` param source, a `Ref`
/// source (deferred — Phase 282 evaluates Embedded), and an evaluator error are all `Error`.
and private evalTransformFrame
    (sources: BindingSources)
    (source: TransformSource)
    (pipeline: Fuaran.Core.Transform list)
    (parameters: TransformParam list)
    : Result<Fuaran.Core.Table, string> =
    let rec resolveParams (env: Map<string, Fuaran.Core.Cell>) (unbound: Set<string>) remaining =
        match remaining with
        | [] -> Ok(env, unbound)
        | (p: TransformParam) :: rest ->
            let name = p.Name

            match resolve<obj> sources (objOfJValBinding p.From) with
            | Resolved v ->
                match resolvedToCell v with
                | Some cell -> resolveParams (Map.add name cell env) unbound rest
                | None -> Error(sprintf "Transform param '%s' resolved to a non-scalar value" name)
            | NotResolved -> resolveParams env (Set.add name unbound) rest
            | Errored m -> Error(sprintf "Transform param '%s' source errored: %s" name m)
            | I18nUnresolved k -> Error(sprintf "Transform param '%s' source is an unresolved i18n key '%s'" name k)

    match resolveParams Map.empty Set.empty parameters with
    | Error m -> Error m
    | Ok(env, unbound) ->

        // Prune every `filter` step whose params include an unbound name (unset filter ⇒ no constraint).
        let pipeline =
            pipeline
            |> List.filter (fun step ->
                match step with
                | Fuaran.Core.Filter pred ->
                    Fuaran.Core.ColExpr.paramsOf pred
                    |> List.forall (fun p -> not (Set.contains p unbound))
                | _ -> true)

        let evalTable (inputTable: Fuaran.Core.Table) : Result<Fuaran.Core.Table, string> =
            match Fuaran.Core.DataFrame.evalPipelineInEnv env pipeline inputTable with
            | Error e -> Error("Transform evaluation failed: " + Fuaran.Core.DataFrame.errorString e)
            | Ok result -> Ok result

        match source with
        | TransformSource.Data(Fuaran.Core.Ref name) ->
            Error(
                sprintf
                    "Transform 'Ref' source '%s' is not host-resolved yet (Phase 282 evaluates Embedded sources)"
                    name
            )
        | TransformSource.Data(Fuaran.Core.Embedded inputTable) -> evalTable inputTable
        // Phase 818 — the LIVE source: resolve the preserved binding against
        // the reactive stores (through the store-reading erasure, so raw store
        // values stay raw) and evaluate over the CURRENT data; an unwritten
        // store falls back to the decode-time initial snapshot, which is what
        // makes SSR byte-identical to the Phase-815 snapshot semantics. The
        // renderer's reactive key walk subscribes the source binding's channel
        // keys, so a store write re-evaluates every reader.
        | TransformSource.Live(binding, initial) ->
            let tableR =
                match resolve<obj> sources (objOfJValBinding binding) with
                | Resolved v -> liveValueToTable v
                | NotResolved ->
                    (match initial with
                     | Fuaran.Core.Embedded t -> Ok t
                     | Fuaran.Core.Ref name ->
                         Error(
                             sprintf "Transform live source initial snapshot is a non-host-resolved 'ref' ('%s')" name
                         ))
                | Errored m -> Error(sprintf "Transform live source errored: %s" m)
                | I18nUnresolved k -> Error(sprintf "Transform live source is an unresolved i18n key '%s'" k)

            tableR |> Result.bind evalTable

/// Phase 818 — resolve a `Binding<JVal>` (a `SetState.valueFrom` source) against
/// the stores at `obj` through the store-reading erasure, lifting the resolved
/// raw store value back to `JVal`. An unliftable value is `Errored` — loud, the
/// Phase-427 mismatch posture — never a silent wrong write.
let resolveJVal (sources: BindingSources) (binding: Binding<JVal>) : Resolution<JVal> =
    match resolve<obj> sources (objOfJValBinding binding) with
    | Resolved v ->
        (match jvalOfResolved v with
         | Some jv -> Resolved jv
         | None ->
             Errored
                 "the binding resolved to a value with no wire representation (a host object) — it cannot be written as a derived state value")
    | NotResolved -> NotResolved
    | Errored m -> Errored m
    | I18nUnresolved k -> I18nUnresolved k

/// Best-effort `tryResolve` — returns `Some` for `Resolved`, otherwise `None`.
/// Convenience for renderer call sites that treat NotResolved + Errored
/// identically (e.g. simple fall-through to the empty-state placeholder).
let tryResolve<'T> (sources: BindingSources) (binding: Binding<'T>) : 'T option =
    match resolve<'T> sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

// ─── Scalar-slot resolution (Phase 632) ──────────────────────────────────────
//
// A `Binding.Transform` in a SCALAR slot (a `TextSource.Bound`, a Metric /
// LabelValueRow value) resolves to the lone cell of an exactly-1×1 pipeline
// result — the r42 shape (`filter → project [col] → limit 1`) and the global
// aggregate (`groupBy(keys: [], aggs: [one agg])`) are the two canonical
// terminals; no new wire vocabulary. The rule is LOUD on ambiguity (Phase-427
// posture — never a silent first cell) and renders absence for an empty
// result, except a trailing global single-`count` groupBy which resolves 0
// (the count of nothing is 0 — the host completes the SQL global-aggregate
// semantic Core's strict fold leaves empty).
//
// This is a DISTINCT entry point wired at the scalar call sites rather than a
// `typeof<'T>` dispatch inside `resolve` — type-directed detection does not
// survive Fable erasure (an `unbox<'T>` of the rows list would "succeed" for
// every 'T on Fable and silently render the rows array in a text slot).

/// Coerce a result cell to a text-slot string. Numbers format invariantly
/// (F# `string` is invariant-culture for IFormattable on .NET and the JS
/// number-to-string on Fable — the same digits for the wire-legal cell range).
let cellToText (c: Fuaran.Core.Cell) : Result<string, string> =
    match c with
    | Fuaran.Core.Str s -> Ok s
    | Fuaran.Core.Int i -> Ok(string i)
    | Fuaran.Core.Float f -> Ok(string f)
    | Fuaran.Core.Bool b -> Ok(if b then "true" else "false")
    | Fuaran.Core.Date s -> Ok s
    | Fuaran.Core.Timestamp s -> Ok s
    | Fuaran.Core.Null -> Error "Transform yielded a null cell in a text slot"

/// Coerce a result cell to a numeric-slot float.
let cellToFloat (c: Fuaran.Core.Cell) : Result<float, string> =
    match c with
    | Fuaran.Core.Int i -> Ok(float i)
    | Fuaran.Core.Float f -> Ok f
    | Fuaran.Core.Str s ->
        Error(
            sprintf
                "Transform yielded a text cell ('%s') in a numeric slot — project a numeric column, or aggregate with count / sum / mean"
                s
        )
    | Fuaran.Core.Bool _ -> Error "Transform yielded a bool cell in a numeric slot"
    | Fuaran.Core.Date s
    | Fuaran.Core.Timestamp s ->
        Error(
            sprintf
                "Transform yielded a date cell ('%s') in a numeric slot — project a numeric column, or aggregate with count / sum / mean"
                s
        )
    | Fuaran.Core.Null -> Error "Transform yielded a null cell in a numeric slot"

/// Resolve a binding in a SCALAR slot: `Binding.Transform` evaluates through the
/// shared frame machinery and interprets the result as one cell (see the section
/// note above); every other binding case resolves exactly as `resolve` does.
let resolveScalarWith<'T>
    (coerce: Fuaran.Core.Cell -> Result<'T, string>)
    (sources: BindingSources)
    (binding: Binding<'T>)
    : Resolution<'T> =
    match binding with
    | Binding.Transform(source, pipeline, parameters) ->
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m
        | Ok result ->
            let names = Fuaran.Core.Table.columnNames result
            let rowCount = Fuaran.Core.Table.rowCount result
            let colCount = List.length names

            if rowCount = 1 && colCount = 1 then
                let cell =
                    Fuaran.Core.Table.tryColumn (List.head names) result
                    |> Option.map (Fuaran.Core.Column.cell 0)
                    |> Option.defaultValue Fuaran.Core.Null

                match cell with
                | Fuaran.Core.Null -> NotResolved
                | c ->
                    match coerce c with
                    | Ok v -> Resolved v
                    | Error m -> Errored m
            elif rowCount = 0 then
                // The count of nothing is 0: Core's strict groupBy fold yields zero
                // groups over an empty frame; a trailing global single-`count`
                // aggregate completes to 0 here. Every other empty result is the
                // slot's empty state ("filter matched nothing" renders as absence).
                match List.tryLast pipeline with
                | Some(Fuaran.Core.GroupBy([], [ agg ])) when agg.Fn = Fuaran.Core.AggFn.Count ->
                    match coerce (Fuaran.Core.Int 0) with
                    | Ok v -> Resolved v
                    | Error m -> Errored m
                | _ -> NotResolved
            else
                Errored(
                    sprintf
                        "Transform in a scalar slot must yield exactly one row × one column (got %d×%d) — end the pipeline with `project` to one column + `limit` 1 (a row-field lookup), or aggregate with `groupBy` keys [] + one agg (count / sum / mean / first)"
                        rowCount
                        colCount
                )
    | other -> resolve<'T> sources other

/// Scalar-slot resolution for a text slot (`TextSource.Bound` and friends).
let resolveScalarText (sources: BindingSources) (binding: Binding<string>) : Resolution<string> =
    resolveScalarWith cellToText sources binding

/// Scalar-slot resolution for a numeric slot (Metric / LabelValueRow values).
let resolveScalarFloat (sources: BindingSources) (binding: Binding<float>) : Resolution<float> =
    resolveScalarWith cellToFloat sources binding

/// Best-effort scalar text resolution — the `tryResolve` twin for text slots.
let tryResolveScalarText (sources: BindingSources) (binding: Binding<string>) : string option =
    match resolveScalarText sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

/// Best-effort scalar float resolution — the `tryResolve` twin for numeric slots.
let tryResolveScalarFloat (sources: BindingSources) (binding: Binding<float>) : float option =
    match resolveScalarFloat sources binding with
    | Resolved value -> Some value
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> None

// ─── Row-field projection contract (Phase 425) ───────────────────────────────
//
// The declarative-grid floor: a decoded `DataGrid` column names a row property
// (`Field`) / the spec names a row-key property (`RowKeyField`) instead of a
// host closure. The projection reads that property off a row `obj` and coerces
// it to a `CellValue`. A `Binding.Transform` produces rows as `Map<string,obj>`
// (each cell scalar boxed), the canonical embedded-data shape; a `Static obj
// seq` may carry any boxed row. Missing field ⇒ `CellValue.Empty` (never a
// throw). Parity-locked with the TS renderer's `projectRowField`.

/// Coerce a boxed row-cell value to a `CellValue`. Numeric int/float both map to
/// `Numeric` (cross-host-deterministic — JS erases the int/float distinction).
let objToCellValue (v: obj) : CellValue =
    match v with
    | null -> CellValue.Empty
    | :? string as s -> CellValue.Text s
    | :? bool as b -> CellValue.Bool b
    | :? float as f -> CellValue.Numeric f
    | :? int as i -> CellValue.Numeric(float i)
    | :? int64 as i -> CellValue.Numeric(float i)
    | :? System.DateTimeOffset as d -> CellValue.Date d
    | _ -> CellValue.Empty

/// Project a named field off a `Row` to a `CellValue`; a missing key is
/// `CellValue.Empty`. The row-field display floor for a decoded grid column.
///
/// fuaran#665 — the rows slot is statically `Row = Map<string,obj>` end to end
/// (typed wire payload, typed `Transform` output, typed facade projection), so
/// the old Fable `#if`-split unbox hack — needed when the slot was `obj` and a
/// `:? Map<string,obj>` type-test evaluated false under Fable — is gone: no
/// runtime test exists to get wrong.
let projectRowFieldValue (row: Row) (field: string) : CellValue =
    match Map.tryFind field row with
    | Some v -> objToCellValue v
    | None -> CellValue.Empty

/// Project a named field off a `Row` to a string (the row-key floor). Empty
/// string when the field is missing (the caller may fall back to the row index).
let projectRowFieldString (row: Row) (field: string) : string =
    match projectRowFieldValue row field with
    | CellValue.Text s -> s
    | CellValue.Numeric f -> string f
    | CellValue.Bool b -> (if b then "true" else "false")
    | CellValue.Date d -> d.ToString("o")
    | CellValue.Empty -> ""

// ─── Data-bound grid sort (Phase 818 — `sortStateKey`) ───────────────────────
//
// A data-bound grid whose spec names a `sortStateKey` sorts its RESOLVED rows
// by the state-carried descriptor `{"column": <header index>, "direction":
// "asc" | "desc"}` before rendering — runtime-side sort, the author wires no
// Transform. One implementation serves the client renderer and any SSR host
// that seeds the key, so two surfaces cannot disagree about ordering rules
// (the `tonedPillOf` single-definition rationale). The rules mirror the
// Phase-801 reference table enhancement where they overlap: empty cells sort
// LAST in both directions (unmeasured is not zero), ties keep their authored
// relative order (`List.sortWith` is stable).

/// Read the sort descriptor carried at `key` in the State store. Every part is
/// validated rather than trusted — a malformed descriptor reads as "no sort"
/// so the authored order stands (never an arbitrary one).
let readSortDescriptor (sources: BindingSources) (key: string) : (int * SortDirection) option =
    Map.tryFind key sources.State
    |> Option.bind jvalOfResolved
    |> Option.bind (fun jv ->
        match jv with
        | JObj fields ->
            let col =
                fields
                |> List.tryPick (function
                    | ("column", JInt i) -> Some i
                    | ("column", JFloat f) when floor f = f -> Some(int f)
                    | _ -> None)

            let dir =
                fields
                |> List.tryPick (function
                    | ("direction", JStr "asc") -> Some SortDirection.Asc
                    | ("direction", JStr "desc") -> Some SortDirection.Desc
                    | _ -> None)

            (match col, dir with
             | Some c, Some d when c >= 0 -> Some(c, d)
             | _ -> None)
        | _ -> None)

let private cellSortRank (v: CellValue) : int =
    match v with
    | CellValue.Numeric _ -> 0
    | CellValue.Bool _ -> 1
    | CellValue.Date _ -> 2
    | CellValue.Text _ -> 3
    | CellValue.Empty -> 4

let private compareCells (a: CellValue) (b: CellValue) : int =
    match a, b with
    | CellValue.Numeric x, CellValue.Numeric y -> compare x y
    | CellValue.Bool x, CellValue.Bool y -> compare x y
    | CellValue.Date x, CellValue.Date y -> compare x y
    | CellValue.Text x, CellValue.Text y -> System.String.CompareOrdinal(x.ToLowerInvariant(), y.ToLowerInvariant())
    | _ -> compare (cellSortRank a) (cellSortRank b)

/// Sort resolved grid rows by a sort descriptor over the column set. Sorting
/// keys off the addressed column's declared `field` (the row property it
/// displays); a column index outside the set or a field-less closure column
/// leaves the authored order standing — a declaration that cannot be honoured
/// is ignored, not guessed at. Empty cells sort last in BOTH directions; the
/// sort is stable.
let sortRowsByDescriptor
    (columns: ColumnErased<'Msg> list)
    (descriptor: (int * SortDirection) option)
    (rows: Row list)
    : Row list =
    match descriptor with
    | None -> rows
    | Some(colIndex, direction) ->
        match List.tryItem colIndex columns |> Option.bind _.Field with
        | None -> rows
        | Some field ->
            rows
            |> List.map (fun r -> projectRowFieldValue r field, r)
            |> List.sortWith (fun (ka, _) (kb, _) ->
                match ka, kb with
                | CellValue.Empty, CellValue.Empty -> 0
                | CellValue.Empty, _ -> 1
                | _, CellValue.Empty -> -1
                | _ ->
                    let c = compareCells ka kb

                    (match direction with
                     | SortDirection.Asc -> c
                     | SortDirection.Desc -> -c))
            |> List.map snd

// ─── Data-bound grid pagination (Phase 862 — `pageStateKey` / `pageSize`) ────
//
// The second instance of the Phase-860 grid-behaviour rule, and it mirrors the
// sort machinery above deliberately: the grid names a State key, the runtime
// reads a validated descriptor from it, and the affordance that writes the key
// is renderer-owned. One implementation serves the client renderer and any SSR
// host, so two surfaces cannot disagree about which rows are on page 3.
//
// Who slices is decided by the SOURCE shape, never by a second declaration. A
// `Binding.Query` whose `dependsOn` names the page key re-runs host-side on a
// page change and already returns the page, so the grid must NOT slice again;
// any other source resolves to the whole set and the grid slices it.

/// Read the page descriptor carried at `key` in the State store: `{"page": N}`,
/// 1-based. Validated rather than trusted — a malformed or absent descriptor
/// reads as page 1, which is the honest default (never an arbitrary offset).
let readPageDescriptor (sources: BindingSources) (key: string) : int =
    Map.tryFind key sources.State
    |> Option.bind jvalOfResolved
    |> Option.bind (fun jv ->
        match jv with
        | JObj fields ->
            fields
            |> List.tryPick (function
                | ("page", JInt i) when i >= 1 -> Some i
                | ("page", JFloat f) when floor f = f && f >= 1.0 -> Some(int f)
                | _ -> None)
        | _ -> None)
    |> Option.defaultValue 1

/// Does this source page HOST-side for the given page key? True when the source
/// is a `Query` declaring a `dependsOn` on that key — the query re-runs on a
/// page change and hands back the page itself, so a client-side slice on top
/// would page the page (FUARAN096 warns pre-emit).
let sourceHostPagesOn (source: Binding<'T>) (pageKey: string) : bool =
    match source with
    | Binding.Query(_, _, Some deps) -> deps |> List.contains pageKey
    | _ -> false

/// How many pages a row count divides into at this page size. Always at least
/// one, so an empty grid still reads as "page 1 of 1" rather than "of 0".
let pageCountOf (pageSize: int) (rowCount: int) : int =
    if pageSize <= 0 then
        1
    else
        max 1 ((rowCount + pageSize - 1) / pageSize)

/// The page actually shown, given where the user left the position and how many
/// rows there now are. A page past the end clamps to the LAST page rather than
/// rendering empty: the row count can shrink under a filter while the position
/// stays put, and showing nothing there reads as data loss rather than as the
/// end of the list. Single-sourced because the slice and the row-index offset
/// the write-back path uses must agree — an off-by-one page between them would
/// commit an edit to the wrong row.
let clampPage (pageSize: int) (page: int) (rowCount: int) : int =
    min (max 1 page) (pageCountOf pageSize rowCount)

/// The rows on `page` (1-based) at `pageSize`, after clamping.
let sliceRowsToPage (pageSize: int) (page: int) (rows: Row list) : Row list =
    if pageSize <= 0 then
        rows
    else
        let count = List.length rows
        let clamped = clampPage pageSize page count

        rows
        |> List.skip (min count ((clamped - 1) * pageSize))
        |> List.truncate pageSize

/// Phase 750 — lower a `CellKindErased.TonedPill` for one row: the named field's
/// text IS the pill's label, and its tone is the map's entry for that text, or
/// `defaultTone` for a value the map does not mention.
///
/// This is the whole of the declarative pill's semantics, in ONE place, because
/// three surfaces render it (the simple-table cell, the AG Grid cell renderer, and
/// the TS tier's two renderers) and a per-surface copy of a lookup-with-fallback is
/// exactly how two hosts come to disagree about an unmapped value. Keyed on the
/// same `projectRowFieldString` the row-key floor uses, so a numeric or boolean
/// field maps by its canonical text rather than by a second coercion rule.
/// Parity-locked with the TS renderers' `tonedPillOf`.
let tonedPillOf
    (row: Row)
    (field: string)
    (toneMap: Map<string, ToneVariant>)
    (defaultTone: ToneVariant)
    : string * ToneVariant =
    let label = projectRowFieldString row field
    let tone = toneMap |> Map.tryFind label |> Option.defaultValue defaultTone
    label, tone
