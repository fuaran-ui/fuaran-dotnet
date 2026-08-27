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

open Fuaran.UI.Types

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
          Now = "" }
