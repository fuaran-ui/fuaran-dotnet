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
        CapabilityInvoker: string -> (string * string) list -> Deferred<obj>
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
      CapabilityInvoker = (fun _ _ -> Deferred.Pending) }

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
        // Phase-632 scalar path), then surface the result rows as `obj seq` — each row a
        // `Map<string,obj>` keyed by column name, the cell scalar boxed. Constrained to
        // `Binding<obj seq>` use at a data-bearing node (same 'T-constraint posture as
        // `Binding.Format` / `Binding.I18n`); a SCALAR slot (TextSource, Metric/LabelValueRow
        // values) resolves through `resolveScalarWith` instead (Phase 632).
        match evalTransformFrame sources source pipeline (defaultArg parameters []) with
        | Error m -> Errored m
        | Ok result ->
            let names = Fuaran.Core.Table.columnNames result
            let rowCount = Fuaran.Core.Table.rowCount result

            let rows: obj seq =
                [ for i in 0 .. rowCount - 1 ->
                      names
                      |> List.map (fun n ->
                          let cell =
                              Fuaran.Core.Table.tryColumn n result
                              |> Option.map (Fuaran.Core.Column.cell i)
                              |> Option.defaultValue Fuaran.Core.Null

                          n, cellToObj cell)
                      |> Map.ofList
                      |> box ]

            try
                Resolved(unbox<'T> (box rows))
            with ex ->
                Errored(
                    sprintf
                        "Transform binding produced rows that did not unbox to the expected type (Binding.Transform is constrained to Binding<obj seq>): %s"
                        ex.Message
                )
    | Binding.Invoke(capabilityId, args) ->
        // Phase 283 — dispatch a host-registered capability for a value. The host invoker resolves
        // (capabilityId, args) to a `Deferred<obj>`; map `Pending` → `NotResolved` (the node's
        // `onLoading`), `Ready` → `Resolved`, `Error` → `Errored` (its `onError`) — reusing the
        // `StateBehaviour` surface, no new node. The default invoker is `Pending` until a host
        // (the AiTools registry) wires real dispatch + Phase-27 replay.
        match sources.CapabilityInvoker capabilityId (args |> List.map (fun (a: InvokeArg) -> a.Addr, a.Value)) with
        | Deferred.Pending -> NotResolved
        | Deferred.Error m -> Errored m
        | Deferred.Ready v ->
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
    (source: Fuaran.Core.DataSource)
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

        match source with
        | Fuaran.Core.Ref name ->
            Error(
                sprintf
                    "Transform 'Ref' source '%s' is not host-resolved yet (Phase 282 evaluates Embedded sources)"
                    name
            )
        | Fuaran.Core.Embedded inputTable ->
            match Fuaran.Core.DataFrame.evalPipelineInEnv env pipeline inputTable with
            | Error e -> Error("Transform evaluation failed: " + Fuaran.Core.DataFrame.errorString e)
            | Ok result -> Ok result

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

/// Project a named field off a row `obj` to a `CellValue`. A `Map<string,obj>`
/// row (the `Transform` shape) reads by key; a missing key is `CellValue.Empty`.
/// The row-field display floor for a decoded grid column.
///
/// Fable erases the `Map<_,_>` generic, so a `:? Map<string,obj>` type-test evals
/// false there (it can't discriminate the instantiation). On the decoded-`Field`
/// path the row is ALWAYS a `Transform`-produced `Map<string,obj>` (an AI-authored
/// `Static` grid source is `"<opaque>"` and can't ride the wire), so on Fable we
/// unbox + `tryFind` directly; on .NET we type-test so a non-Map row is `Empty`.
let projectRowFieldValue (row: obj) (field: string) : CellValue =
#if FABLE_COMPILER
    if isNull (box row) then
        CellValue.Empty
    else
        match Map.tryFind field (unbox<Map<string, obj>> row) with
        | Some v -> objToCellValue v
        | None -> CellValue.Empty
#else
    match row with
    | :? Map<string, obj> as m ->
        match Map.tryFind field m with
        | Some v -> objToCellValue v
        | None -> CellValue.Empty
    | _ -> CellValue.Empty
#endif

/// Project a named field off a row `obj` to a string (the row-key floor). Empty
/// string when the field is missing (the caller may fall back to the row index).
let projectRowFieldString (row: obj) (field: string) : string =
    match projectRowFieldValue row field with
    | CellValue.Text s -> s
    | CellValue.Numeric f -> string f
    | CellValue.Bool b -> (if b then "true" else "false")
    | CellValue.Date d -> d.ToString("o")
    | CellValue.Empty -> ""
