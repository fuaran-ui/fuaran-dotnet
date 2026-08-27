module Fuaran.UI.AiTools.BindingProbe

// ============================================================================
//  Fuaran runtime-introspection AI tools — binding-resolution probe.
//
//  Resolves a typed `Binding<'T>` against the introspection context's
//  `Fuaran.UI.BindingSources`, returning a typed `ResolvedBindingResult` the
//  §4i tools can hand back through the cross-cutting obj-erasure
//  boundary. The implementation mirrors
//  `Fuaran.UI.Renderer.BindingResolver.resolve` — same semantics, same
//  Defaults sentinel handling, same exception-to-typed-error mapping —
//  but lives here so `Fuaran.UI.AiTools` stays free of `Fuaran.UI.Renderer`
//  (which pulls in Feliz / Fable / browser substrate the orchestrator
//  doesn't want).
//
//  Phase 213 closed the SHAPE half of that duplication: the source record is
//  no longer a hand-copied `BindingProbeSources` but the canonical
//  `Fuaran.UI.BindingSources` the renderer resolves against, so a field the
//  renderer gains can never again be silently missing here (it was missing
//  `Locale`, `Now`, `ComputedContext`, `I18nResolver` and `CapabilityInvoker`).
//  The RESOLUTION half stays deliberately split: the arms below that decline
//  (`Computed` / `Now` / `I18n` / `Format` / `Transform` / `Invoke`) decline
//  because their resolution needs renderer-side machinery — the Intl /
//  Globalization formatter, the dataframe pipeline evaluator, the host
//  capability dispatcher — not because the probe lacks the data. Reporting
//  `NotResolvedYet` is the honest answer there; inventing a second
//  implementation of that machinery here would manufacture exactly the
//  probe-disagrees-with-the-render defect this file exists to avoid.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools.Seams

// ─── Source identification for a Binding<'T> ────────────────────────────────

/// Pull the `BindingSource` discriminator + the canonical wire `Expression`
/// out of a typed `Binding<'T>`. The `Expression` shape is the v1 short
/// form per Types.fs encoding decision (3) — `$queries.<name>` etc., no
/// accessor-path tail.
let identify<'T> (binding: Binding<'T>) : BindingSource * string =
    match binding with
    | Binding.Static _ -> BindingSource.Static, "$static"
    | Binding.Query(name, _, _) -> BindingSource.Query name, sprintf "$queries.%s" name
    | Binding.Filter(name, _) -> BindingSource.Filter name, sprintf "$filters.%s" name
    | Binding.Selection(nodeId, _, _, _) ->
        // Bare-string nodeId since the swap; the probe's own `BindingSource`
        // vocabulary keeps the `NodeId` wrapper.
        BindingSource.Selection(NodeId nodeId), sprintf "$selection.%s" nodeId
    | Binding.State(key, _) -> BindingSource.State key, sprintf "$state.%s" key
    | Binding.Computed _ -> BindingSource.Computed, "$computed"
    // Phase 765 — reuses `BindingSource.Computed` rather than widening that
    // DU, exactly as `Local` does below: neither reads a named store, and the
    // wire expression carries the distinction.
    | Binding.Now _ -> BindingSource.Computed, "$now"
    | Binding.I18n(key, _) -> BindingSource.I18n key, sprintf "$i18n.%s" key
    | Binding.Local _ ->
        // Local binding's identity-wise reads through to its
        // InitialFrom source for the orchestrator's introspection lens.
        // The probe stays at the "buffer overlay" layer rather than
        // descending recursively; the wire-shape expression labels it
        // distinctly so the orchestrator can mark fields it should not
        // try to address via the standard query path.
        BindingSource.Computed, "$local"
    | Binding.Format _ ->
        // Format binding's identity reads through to its numeric
        // source + the bounded Format/locale intent. Like Local, the probe
        // stays at the overlay layer and labels it distinctly ($format) so
        // the orchestrator knows the field is a formatted projection, not a
        // standard query path.
        BindingSource.Computed, "$format"
    | Binding.Transform _ ->
        // Transform binding (Phase 282) — a declarative `Fuaran.Core.DataFrame` pipeline evaluated
        // client-side as data. Like Format/Local, the probe labels it distinctly ($transform) so
        // the orchestrator knows the field is a computed dataframe, not a standard query path.
        BindingSource.Computed, "$transform"
    | Binding.Invoke _ ->
        // Invoke binding (Phase 283) — a host-registered capability dispatched for a value. Labelled
        // distinctly ($invoke) so the orchestrator knows the field is a compute invocation.
        BindingSource.Computed, "$invoke"

// ─── Mirror of BindingResolver.Resolution<'T> against introspection sources ─

/// Resolve a typed `Binding<'T>` against the probe's sources and return
/// a `ResolvedBindingResult`. The typed `'T` value is obj-boxed at the
/// return; the orchestrator decodes per-slot using its schema.
let rec tryResolveBinding<'T> (ctx: IntrospectionContext) (binding: Binding<'T>) : ResolvedBindingResult =
    let source, expression = identify binding

    let typeHint =
        try
            Some(typeof<'T>.Name)
        with _ ->
            // Fable erases generics — typeof<'T>.Name throws or returns
            // a placeholder. Swallow and surface as None; the orchestrator
            // knows the slot's expected 'T from schema.
            None

    let resolvedOk (value: 'T) : ResolvedBindingResult =
        ResolvedBindingResult.Resolved
            { Expression = expression
              // F# 10 `box _` types as `obj | null`; the ResolvedValue
              // contract is nonnull-obj, so launder via Unchecked.nonNull.
              // Same workaround Fuaran.UI.Ops.Tests uses for its `nn` helper.
              ResolvedValue = Some(box value |> Unchecked.nonNull)
              TypeHint = typeHint
              Source = source }

    let resolvedNone () : ResolvedBindingResult =
        ResolvedBindingResult.Resolved
            { Expression = expression
              ResolvedValue = None
              TypeHint = typeHint
              Source = source }

    let failed (code: BindingErrorCode) (message: string) (suggestion: string option) : ResolvedBindingResult =
        ResolvedBindingResult.Failed
            { Expression = expression
              Source = source
              Code = code
              Message = message
              Hint =
                { NodeKind = None
                  Suggestion = suggestion
                  AvailableAlternatives = [] } }

    match binding with
    // Mirror BindingResolver: the absent payload resolves to the slot's
    // default representation (the pre-swap `Static` carried exactly that).
    | Binding.Static(Some value) -> resolvedOk value
    | Binding.Static None -> resolvedOk Unchecked.defaultof<'T>

    | Binding.Query(name, _, _) when name = Fuaran.UI.Defaults.NotProvidedSentinel ->
        // Mirror BindingResolver: the Defaults sentinel encodes "the
        // author hasn't overridden this Source-is-mandatory field yet."
        // Surface as "Resolved but with no value" — distinct from a
        // genuine NotResolvedYet (data-source absent), which the
        // orchestrator pattern-matches separately.
        resolvedNone ()

    | Binding.Query(name, accessor, _) ->
        match Map.tryFind name ctx.Sources.QueryResults with
        | Some raw ->
            try
                resolvedOk (accessor raw)
            with ex ->
                failed
                    BindingErrorCode.AccessorThrew
                    (sprintf "Query '%s' accessor threw: %s" name ex.Message)
                    (Some "Verify the accessor's expected row shape matches the query result.")
        | None ->
            failed
                BindingErrorCode.SourceUnregistered
                (sprintf "Query '%s' has no registered result." name)
                (Some "Wait for query to complete or check the query is wired in module init.")

    | Binding.Filter(name, _) ->
        match Map.tryFind name ctx.Sources.Filters with
        | Some raw ->
            try
                resolvedOk (unbox<'T> raw)
            with ex ->
                failed
                    BindingErrorCode.TypeMismatch
                    (sprintf "Filter '%s' value did not unbox to expected type: %s" name ex.Message)
                    (Some "Filter store value's runtime type does not match the binding's typed declaration.")
        | None ->
            failed
                BindingErrorCode.NotResolvedYet
                (sprintf "Filter '%s' has no value set." name)
                (Some "Set the filter via a Filters component or via SetState on the filter key.")

    | Binding.Selection(nodeId, accessor, defaultValue, _) ->
        match Map.tryFind (NodeId nodeId) ctx.Sources.Selections with
        | Some raw ->
            try
                resolvedOk (accessor raw)
            with ex ->
                failed
                    BindingErrorCode.AccessorThrew
                    (sprintf "Selection on '%s' accessor threw: %s" nodeId ex.Message)
                    (Some "Verify the accessor's expected row shape matches the selected row.")
        | None ->
            // 0.2.9 (Phase 629) — mirror the renderer's resolver: an
            // unselected node with a declared default is resolved, not
            // NotResolvedYet (the introspection lens must agree with what
            // the user sees rendered).
            match defaultValue with
            | Some d -> resolvedOk d
            | None ->
                failed
                    BindingErrorCode.NotResolvedYet
                    (sprintf "Selection on '%s' has no current value." nodeId)
                    (Some "Selection is set when the user picks a row; wait for the interaction or seed via SetState.")

    | Binding.State(key, defaultValue) ->
        match Map.tryFind key ctx.Sources.State with
        | Some raw ->
            try
                resolvedOk (unbox<'T> raw)
            with ex ->
                failed
                    BindingErrorCode.TypeMismatch
                    (sprintf "State '%s' value did not unbox to expected type: %s" key ex.Message)
                    (Some "State store value's runtime type does not match the binding's typed declaration.")
        | None ->
            // Mirror BindingResolver: absent State key resolves to the
            // binding's declared default, not NotResolved; a default-less
            // binding resolves to the slot's default representation.
            match defaultValue with
            | Some d -> resolvedOk d
            | None -> resolvedOk Unchecked.defaultof<'T>

    | Binding.Computed _ ->
        // Computed bindings carry a `BindingContext -> 'T` function. The seed
        // `ComputedContext` is on `ctx.Sources` since Phase 213, but resolving
        // also means reproducing the renderer's live-state merge policy
        // (`sources.State` authoritative, injected keys underneath) — a second
        // implementation of a policy, which is the drift class this file's
        // header warns about. And the closure is an F#-only escape that never
        // serialises, so a `Computed` value is not wire-traversable in the
        // first place. v1 surfaces as a NotResolvedYet hint pointing the
        // orchestrator at a structural workaround.
        failed
            BindingErrorCode.NotResolvedYet
            "Computed bindings are not yet introspectable (the BindingContext is renderer-side)."
            (Some "Express the same derivation via Binding.Query + a computed-column accessor, then introspect that.")

    | Binding.Now _ ->
        // Phase 765 — the instant lives in `BindingSources.Now`. Since Phase
        // 213 that field IS on `ctx.Sources` (the shape is the renderer's), so
        // the probe no longer lacks the datum; what it lacks is a decision that
        // the probe should start reporting a value where it has reported
        // NotResolvedYet since 765. Turning that on is a deliberate
        // behaviour change to a public tool surface, not a side-effect of the
        // shape unification, so it stays declined here. Reporting
        // NotResolvedYet is never WRONG — it is the honest "ask the renderer";
        // inventing a clock here would be.
        failed
            BindingErrorCode.NotResolvedYet
            "Now bindings resolve against the host-furnished instant (BindingSources.Now), which is renderer-side."
            (Some "Introspect the rendered value instead, or supply the instant to the renderer and read it there.")

    | Binding.Local(_, _, initialFrom, _, _) ->
        // Local binding's read side is its initialFrom source.
        // Recurse the resolution so the orchestrator sees the underlying
        // value; the buffer-overlay state is renderer-only and not part
        // of the introspection surface.
        tryResolveBinding<'T> ctx initialFrom

    | Binding.I18n(key, _args) ->
        // i18n bindings resolve via `BindingSources.I18nResolver` — present on
        // `ctx.Sources` since Phase 213 — but resolving one also means
        // resolving its `Binding<JVal>` args and applying the renderer's
        // placeholder-substitution policy, which is renderer-side machinery.
        // v1 surfaces as NotResolvedYet with a hint; the orchestrator can ask
        // the host for catalog state via a separate seam if it cares about the
        // resolved string.
        failed
            BindingErrorCode.NotResolvedYet
            (sprintf "I18n binding for key '%s' requires the renderer's I18nResolver, not exposed to the probe." key)
            (Some
                "Inspect the i18n catalog via a host-supplied tool; the AI probe deliberately stops at the unresolved key.")

    | Binding.Format(_source, _format, _locale) ->
        // Format bindings format their numeric source via the
        // renderer's Intl / Globalization formatter, which the probe has no
        // handle on — the FORMATTER is renderer-side, not the locale. The
        // ambient `Locale` the formatter reads is on `ctx.Sources` since Phase
        // 213 (it was the field the old hand-duplicated probe record had
        // silently dropped), so the gap here is the formatter alone. v1
        // surfaces as NotResolvedYet with a hint — same posture as
        // I18n / Computed.
        failed
            BindingErrorCode.NotResolvedYet
            "Format bindings require the renderer's locale formatter, not exposed to the probe."
            (Some "Introspect the underlying numeric source binding directly; the formatted string is renderer-side.")
    | Binding.Transform _ ->
        // Transform bindings (Phase 282) evaluate a `Fuaran.Core.DataFrame` pipeline in the
        // renderer's resolver, not the probe (the pipeline EVALUATOR is renderer-side; the source
        // record is shared since Phase 213). v1 surfaces as NotResolvedYet with a hint — same
        // posture as Format / I18n / Computed.
        failed
            BindingErrorCode.NotResolvedYet
            "Transform bindings evaluate a dataframe pipeline in the renderer's resolver, not the probe."
            (Some
                "The transformed rows (or, in a scalar slot, the 1×1 result cell — Phase 632) are renderer-side; introspect the declared source schema + pipeline directly.")
    | Binding.Invoke(capabilityId, _) ->
        // Invoke bindings (Phase 283) dispatch a host-registered capability in the renderer's
        // resolver/registry, not the probe. v1 surfaces as NotResolvedYet with a hint.
        failed
            BindingErrorCode.NotResolvedYet
            (sprintf "Invoke binding for capability '%s' is dispatched host-side, not by the probe." capabilityId)
            (Some "Enumerate the capability registry for its typed signature; the realized value is renderer-side.")
