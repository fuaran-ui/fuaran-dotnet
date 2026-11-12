namespace Fuaran.UI

// ============================================================================
//  Fuaran — the canonical binding-source record (Phase 213).
//
//  The data-only shape a host furnishes so a `Binding<'T>` can be resolved:
//  query results, module state, filters, selections, the computed-context
//  seed, the i18n catalog + resolver, the ambient locale, the capability
//  invoker, and the host-furnished instant. NO resolver logic lives here —
//  `Fuaran.UI.Renderer.BindingResolver` owns resolution, and
//  `Fuaran.UI.AiTools.BindingProbe` owns the introspection probe.
//
//  WHY IT LIVES HERE. It used to live in the renderer, and `Fuaran.UI.AiTools`
//  hand-duplicated it as `BindingProbeSources` because AiTools must stay free
//  of the renderer (Feliz / Fable / browser substrate the orchestrator does not
//  want). The duplicate drifted: it never gained `Locale` (Phase 102), nor
//  `Now` (Phase 765), `CapabilityInvoker` (Phase 283), `ComputedContext`
//  (Phase 137) or `I18nResolver` — so the introspection surface could not carry
//  what the renderer resolves against, and the AI tool the orchestrator trusts
//  could disagree with the rendered UI. `Fuaran.UI` is the one package BOTH
//  already depend on and is FSharp.Core-only, so promoting the record here
//  breaks the cycle without pulling the renderer into AiTools.
//
//  Both old spellings are preserved as type abbreviations at their original
//  locations, so no consumer's `BindingResolver.BindingSources` annotation or
//  `BindingResolver.empty` call site changes.
//
//  ADDING A FIELD: add it HERE and nowhere else. The drift guard in
//  `Fuaran.UI.AiTools.Tests/BindingSourcesDriftTests.fs` fails if the
//  introspection source shape and the renderer's resolver shape ever become
//  two types again.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// A session-held store of PRIMED live-`Transform` evaluations — the seam the
/// renderer's `TransformSource.Live` arm consults before evaluating a pipeline
/// in full (Phase 1586), and since Phase 1761 its `TransformSource.Data` arm
/// too. Either arm hands it the pipeline CLOSED over its bound params (every
/// bound scalar substituted as a literal), so the empty-environment promise
/// below covers a parameterised site as well.
///
/// WHY THE INTERFACE IS HERE AND THE IMPLEMENTATION IS NOT. The store needs
/// somewhere to keep primed state BETWEEN renders, and the render path has
/// nowhere: a resolver call is a pure function of the sources it is handed, by
/// design and worth keeping. Whatever holds a session holds the store — a tier
/// two packages above this one — so the SEAM is declared here, where
/// `BindingSources` can name it, and the implementation stays where the session
/// is. Typed over `Fuaran.Compute.DataFrame` alone, which this package already
/// references for `Binding.Transform`, so declaring it adds no dependency.
///
/// WHAT AN IMPLEMENTATION MUST PROMISE, AND WHAT IT NEED NOT. It must promise
/// exactly ONE thing: the table it returns is the table a full evaluation of
/// `pipeline` over `source` produces, in the empty environment. It need NOT
/// promise to have done less work — a store that evaluated everything on every
/// call is a correct (if pointless) implementation, and that is deliberate: it
/// makes the seam's contract checkable by comparing two renders rather than by
/// trusting a counter.
///
/// THE SITE KEY IS AN IDENTITY, NOT AN ADDRESS. `site` identifies one READER —
/// two grids over one state key are two sites with two pipelines, and one grid
/// keeps its primed state across every edit to that key. Because the promise
/// above is stated over `source` and not over the state, a store may key
/// however it likes and two readers COLLIDING on one key costs recomputation
/// and never correctness. The renderer's own derivation is
/// [[BindingWalk.liveSiteKey]].
///
/// ROW IDENTITY IS THE STORE'S, NOT THE CALLER'S. Nothing in a rendered tree
/// declares which column identifies a row, so the renderer has no such
/// declaration to pass and would have to guess one. A store that wants to
/// restrict its recomputation takes the declaration from whoever constructed it
/// — the host that knows its own data — and one that has none evaluates in full
/// and is still correct.
type ILiveTransformStore =
    /// Evaluate `pipeline` over `source` for the reader identified by `site`,
    /// reusing and updating whatever primed state the store holds for it.
    abstract Evaluate:
        site: string * pipeline: Fuaran.Compute.Transform list * source: Fuaran.Core.Table ->
            Result<Fuaran.Core.Table, string>

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
        /// the localised string with values from the `TextSource.I18n` args map
        /// — each of which is a `Binding<JVal>` since Phase 1661, so an argument
        /// may read the same slot the surrounding page does; a literal argument
        /// substitutes exactly the characters it always did.
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
        /// The session-held store of primed `Transform` evaluations (Phase
        /// 1586) the renderer consults before evaluating a
        /// `TransformSource.Live` pipeline in full — and, since Phase 1761, a
        /// `TransformSource.Data` one, which is what lets a filter-chip
        /// dashboard re-render a static chart whose chips did not move without
        /// re-evaluating it.
        ///
        /// `None` — the default, and what every host furnishes until it opts in
        /// — is today's path exactly: every render evaluates the pipeline in
        /// full. Absence is therefore not a degraded mode, which is what makes
        /// the slot additive in behaviour as well as in shape.
        ///
        /// The renderer consults it ONLY where the two evaluations are the same
        /// question. The seam evaluates in the empty environment, so the
        /// renderer hands it the effective pipeline CLOSED over its bound params
        /// (Core's certified substitution law), keyed by that closed pipeline —
        /// a write to a param the pipeline does not read therefore leaves the
        /// key, and the stored answer, where they were. A pipeline with a param
        /// still standing after that (unbound in a `derive`, or a slot param
        /// bound to a cell of the wrong shape) evaluates in full, so its error
        /// is the one it always was. A one-row `Binding.Expr` never consults it.
        LiveTransforms: ILiveTransformStore option
        /// Request parameter name -> the names of the `QueryResults` sources whose
        /// requests depend on it (Phase 2523). A host fills it from the page's
        /// sidecar; this record only carries it.
        ///
        /// When an admitted `SetState` or filter write changes a named parameter,
        /// exactly the sources listed under it are asked again (see
        /// [[BindingSources.reResolve]]); every other source is left where it was.
        /// The empty map — the default — means no source depends on a parameter,
        /// which is today's behaviour exactly: a `Binding.Query` is resolved when
        /// the host fills the record and never re-asked.
        ParameterDependents: Map<string, Set<string>>
    }

/// One request parameter whose value an admitted action changed (Phase 2523).
/// `Value` is the literal the action carried; `None` when the new value is
/// derived at dispatch (`SetState` with `valueFrom`), in which case the
/// resolver reads it from wherever the host keeps it.
type ParameterChange = { Name: string; Value: JVal option }

/// What a host's resolver answers for one source (Phase 2523): the result it
/// stores under the source's name, and the schema of that result when it has one.
/// A `None` schema skips the typed check, exactly as a source with no schema does
/// today.
type ResolvedSource = { Value: obj; Schema: Schema option }

/// A source the resolver refused, or whose result failed the typed check
/// (Phase 2523). It is stored under the source's name in `QueryResults` in place
/// of the value, so the stale value cannot be read back: `BindingResolver`
/// answers a `Binding.Query` over it with an error naming the source and the
/// reason.
type SourceRefusal = { Source: string; Reason: string }

/// One re-resolved source: its name and fresh value, or its named refusal.
type SourceOutcome = Result<string * obj, SourceRefusal>

/// The host's resolver for a query-bound source: given the parameters that
/// changed and the source's name, the fresh result or the reason it cannot be had.
type SourceResolver = ParameterChange list -> string -> Result<ResolvedSource, string>

/// Companion values for [[BindingSources]]. Data-only: the identity defaults
/// and the empty record. Resolution lives in the renderer.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module BindingSources =

    /// The default `II18nResolver`. Pass-through identity:
    /// returns the debug placeholder `[i18n:<key>]` for every key so missing
    /// translations stay visually loud in dev. Apps wire a real resolver
    /// (a platform host's i18n resolver, or a hand-rolled
    /// gettext / ICU / template-string resolver) by replacing
    /// `BindingSources.I18nResolver`.
    let passthroughI18nResolver: II18nResolver =
        { new II18nResolver with
            member _.Resolve(key, _args) = sprintf "[i18n:%s]" key }

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
          CapabilityInvoker = (fun _ _ -> Deferred.Pending)
          Now = ""
          LiveTransforms = None
          ParameterDependents = Map.empty }

    /// The sources whose requests depend on any of `changes` (Phase 2523), in
    /// name order. A parameter nobody depends on contributes nothing.
    let dependentsOf (sources: BindingSources) (changes: ParameterChange list) : string list =
        changes
        |> List.collect (fun c ->
            match Map.tryFind c.Name sources.ParameterDependents with
            | Some names -> Set.toList names
            | None -> [])
        |> List.distinct
        |> List.sort

    /// Ask the host's resolver again for exactly the sources that depend on
    /// `changes` (Phase 2523) and say what became of each.
    ///
    /// `check` is the typed check (`QueryBinding.check` over the tree the caller
    /// holds): it takes the source's name and its resolved schema and returns
    /// the defects as messages. It is a function because this file compiles
    /// before `QueryBinding` and holds no tree. A result with defects is refused
    /// rather than rendered, and a source with no schema is not checked. A
    /// resolver that throws refuses its source. Nothing beyond the dependents is
    /// asked, so a source no changed parameter names is never touched.
    let reResolve
        (resolve: SourceResolver)
        (check: string -> Schema -> string list)
        (sources: BindingSources)
        (changes: ParameterChange list)
        : SourceOutcome list =
        dependentsOf sources changes
        |> List.map (fun name ->
            let refuse reason =
                Error { Source = name; Reason = reason }

            let answer =
                try
                    resolve changes name
                with ex ->
                    Error ex.Message

            match answer with
            | Error reason -> refuse reason
            | Ok r ->
                match r.Schema |> Option.map (check name) |> Option.defaultValue [] with
                | [] -> Ok(name, r.Value)
                | defects -> refuse (String.concat "; " defects))

    /// Fold `outcomes` into `sources` (Phase 2523): a fresh value replaces the
    /// source's result; a refusal replaces it with the [[SourceRefusal]].
    let applyOutcomes (sources: BindingSources) (outcomes: SourceOutcome list) : BindingSources =
        let results =
            outcomes
            |> List.fold
                (fun acc o ->
                    match o with
                    | Ok(name, value) -> Map.add name value acc
                    | Error refusal -> Map.add refusal.Source (Unchecked.nonNull (box refusal)) acc)
                sources.QueryResults

        { sources with QueryResults = results }
