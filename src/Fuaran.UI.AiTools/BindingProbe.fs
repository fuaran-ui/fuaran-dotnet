module Fuaran.UI.AiTools.BindingProbe

// ============================================================================
//  Fuaran runtime-introspection AI tools — binding-resolution probe.
//
//  Resolves a typed `Binding<'T>` against the introspection context's
//  `Fuaran.UI.BindingSources`, returning a typed `ResolvedBindingResult` the
//  §4i tools can hand back through the cross-cutting obj-erasure boundary.
//
//  The duplication that motivated this file is now closed on BOTH halves.
//
//  Phase 213 closed the SHAPE half: the source record is no longer a
//  hand-copied `BindingProbeSources` but the canonical
//  `Fuaran.UI.BindingSources` the resolver resolves against, so a field the
//  resolver gains can never again be silently missing here (it was missing
//  `Locale`, `Now`, `ComputedContext`, `I18nResolver` and `CapabilityInvoker`).
//
//  Phase 1532 closes the RESOLUTION half. Seven arms — `Computed` / `Now` /
//  `I18n` / `Format` / `Transform` / `Expr` / `Invoke` — used to DECLINE with
//  `NotResolvedYet` on the rationale that resolving them needs "renderer-side"
//  machinery. That rationale was true when it was written and is not true now:
//  the Intl / Globalization formatter, the dataframe pipeline evaluator, the
//  host capability dispatcher and the clock all live in
//  `Fuaran.UI.Renderer.Core` — the EMISSION-AGNOSTIC spine (FSharp.Core +
//  Fable.Core, no Feliz / React / Browser), not in a renderer. So the probe
//  DELEGATES those arms to the resolver entry point the slot uses, and reports
//  what the resolver reports. The remaining arms are kept because delegation
//  would LOSE information: they carry the probe's own richer
//  `BindingErrorCode` vocabulary (`SourceUnregistered` vs `NotResolvedYet` vs
//  `AccessorThrew` vs `TypeMismatch`), where the resolver has one undifferentiated
//  `NotResolved`.
//
//  WHICH resolver entry point is a property of the SLOT, not of the binding, so
//  it is a parameter here rather than a choice this file makes: a
//  `Binding.Transform` is its rows in a `DataGrid.Source` and its 1x1 result
//  cell in a `Metric.Value`, and one answer is wrong in the other slot. The two
//  entry points below name the two, and `Tools.fs` picks per slot the same way
//  the renderer does.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Renderer
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
    | Binding.Expr _ ->
        // Fuaran-UI Phase 1534 — a scalar expression over the binding's own params. Labelled
        // distinctly ($expr) on the same reasoning as $transform: the orchestrator needs to know the
        // field is a derived scalar, not a standard query path. `BindingSource.Computed` is reused
        // rather than widening that DU, exactly as `Now` / `Local` / `Format` / `Transform` do —
        // the wire expression carries the distinction.
        BindingSource.Computed, "$expr"
    | Binding.Invoke _ ->
        // Invoke binding (Phase 283) — a host-registered capability dispatched for a value. Labelled
        // distinctly ($invoke) so the orchestrator knows the field is a compute invocation.
        BindingSource.Computed, "$invoke"

// ─── Text provenance (Phase 1547) ───────────────────────────────────────────

/// Classify a `TextSource` into its `TextProvenance`. It sits beside
/// `identify` because it IS `identify` for the bound case: reusing that
/// classification is what keeps the text mark and the binding-slot token
/// from drifting into two vocabularies for one fact.
///
/// Note what it does NOT do: it never resolves the text. A bound heading's
/// resolved string stays where it was, behind the renderer, and the honest
/// reason is that surfacing it here would ADD the reading surface this mark
/// exists to warn about. The mark says where the bytes come from; the
/// consumer decides whether it wants them.
let textProvenance (text: TextSource) : TextProvenance =
    match text with
    | TextSource.Literal _ -> TextProvenance.Literal
    | TextSource.I18n(key, _) -> TextProvenance.I18n key
    | TextSource.Bound binding ->
        let source, expression = identify binding
        TextProvenance.Bound(source, expression)

// ─── Resolution against the introspection sources ───────────────────────────

/// Resolve a typed `Binding<'T>` through the given resolver entry point and
/// project the answer onto `ResolvedBindingResult`. `entry` is
/// `BindingResolver.resolve` for a slot the renderer resolves generally, and
/// `BindingResolver.resolveScalarWith coerce` for one it resolves as a scalar;
/// the two public wrappers below name them.
let rec private resolveThrough<'T>
    (entry: Fuaran.UI.BindingSources -> Binding<'T> -> BindingResolver.Resolution<'T>)
    (ctx: IntrospectionContext)
    (binding: Binding<'T>)
    : ResolvedBindingResult =
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


    | Binding.Local(_, _, initialFrom, _, _) ->
        // Local binding's read side is its initialFrom source. Recursed rather
        // than delegated so the underlying source keeps THIS function's richer
        // error vocabulary; the buffer-overlay state is a render-time concern
        // and not part of the introspection surface.
        resolveThrough entry ctx initialFrom

    // ─── Phase 1532 — the delegated arms ────────────────────────────────────
    //
    // Every binding whose resolution is a COMPUTATION rather than a store read:
    // the ComputedContext merge, the host-furnished instant, the i18n resolver
    // and its argument substitution, the locale formatter, the dataframe
    // pipeline evaluator, the scalar-expression evaluator, the capability
    // dispatcher. All of it lives in `Fuaran.UI.Renderer.Core`, so the probe
    // ASKS it rather than re-deciding — which is what makes the probe's answer
    // and the rendered value one answer instead of two that can disagree.
    //
    // The arms above are NOT delegated, and that is deliberate rather than
    // residual: each carries a discrimination the resolver does not make. A
    // `Query` with no registered result is `SourceUnregistered` here and a bare
    // `NotResolved` there; a `Filter` whose stored value will not unbox is
    // `TypeMismatch` here and `Errored` there. Delegating them would lose the
    // vocabulary the §4i tool surface reports through.
    | Binding.Computed _
    | Binding.Now _
    | Binding.I18n _
    | Binding.Format _
    | Binding.Transform _
    | Binding.Expr _
    | Binding.Invoke _ ->
        match entry ctx.Sources binding with
        | BindingResolver.Resolved value -> resolvedOk value
        | BindingResolver.NotResolved ->
            // The resolver's single "no value yet" verdict, which spans an
            // unfurnished host instant, a capability dispatch still pending, a
            // null result cell and a transform that filtered to nothing. All of
            // them are the slot's empty state — exactly what `NotResolvedYet`
            // names, and what the node renders its placeholder for.
            failed
                BindingErrorCode.NotResolvedYet
                (sprintf "%s resolved with no current value." expression)
                (Some
                    "The slot's empty state — the renderer shows its placeholder here too. Check what the binding reads: the host instant, a capability's readiness, or the rows a transform filtered to.")
        | BindingResolver.Errored message ->
            // `AccessorThrew` rather than a new code. `BindingErrorCode` is a
            // public four-case DU and widening it would break every exhaustive
            // consumer match; and every resolver `Errored` IS author-supplied
            // machinery failing under evaluation — an accessor, a pipeline, a
            // capability, an unbox at the slot boundary. The resolver's own
            // message is carried verbatim, which is where the discrimination
            // between those actually lives.
            failed
                BindingErrorCode.AccessorThrew
                message
                (Some "The renderer reports this same failure for this binding — repair the declaration, not the probe.")
        | BindingResolver.I18nUnresolved key ->
            failed
                BindingErrorCode.NotResolvedYet
                (sprintf "I18n key '%s' has no translation in the host's catalogue." key)
                (Some
                    "Register the key with the host's I18nResolver; until then the renderer shows the loud `[i18n:<key>]` placeholder.")

/// Resolve a typed `Binding<'T>` against the probe's sources and return a
/// `ResolvedBindingResult`. The typed `'T` value is obj-boxed at the return; the
/// orchestrator decodes per-slot using its schema.
///
/// This is the GENERAL slot: it delegates to `BindingResolver.resolve`, which is
/// the entry point the renderer uses for every slot but the numeric-scalar ones.
/// A `Binding.Transform` here is its ROWS.
let tryResolveBinding<'T> (ctx: IntrospectionContext) (binding: Binding<'T>) : ResolvedBindingResult =
    resolveThrough BindingResolver.resolve ctx binding

/// Fuaran-UI Phase 1532 — the SCALAR-slot twin of `tryResolveBinding`, for a slot
/// the renderer resolves through `BindingResolver.resolveScalarWith`
/// (`Metric.Value`, `Metric.Trend`, `LabelValueRow.Value`, and every text slot).
/// A `Binding.Transform` here is its 1x1 result cell and a `Binding.Expr` its
/// result cell — the values the reader is actually looking at — where the general
/// entry point reports the rows failing to unbox to the slot's type.
///
/// `coerce` is the slot's OWN cell coercion, and it is a parameter rather than a
/// generic unbox for the reason the numeric slot makes concrete: a `count`
/// aggregate yields an `Int` cell into a `Binding&lt;float&gt;` slot, which
/// `BindingResolver.cellToFloat` reads as `2.0` and a raw unbox throws on. Pass
/// the same coercion the renderer passes for that slot — `cellToFloat`,
/// `cellToText`, `cellToBool` — and the probe cannot report a different value
/// from the one on screen.
let tryResolveScalarBindingWith<'T>
    (coerce: Fuaran.Core.Cell -> Result<'T, string>)
    (ctx: IntrospectionContext)
    (binding: Binding<'T>)
    : ResolvedBindingResult =
    resolveThrough (BindingResolver.resolveScalarWith coerce) ctx binding
