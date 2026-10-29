module Fuaran.UI.Renderer.Server.Render

// ============================================================================
//  Fuaran — server-side renderer (Phase 140).
//
//  Turns a `Node<'Msg>` tree into an HTML *string* on plain .NET — no React, no
//  Fable — via `Feliz.ViewEngine`. A Giraffe / ASP.NET host server-renders
//  Fuaran chrome for SEO surfaces with no client-runtime requirement and no
//  visual degradation; the same tree later hydrates client-side (Phase 143).
//
//  Parity is the load-bearing property: the emitted **class names + ARIA
//  attributes** must match the Feliz client renderer for the same tree (locked
//  by the Phase 142 conformance corpus). The class-name vocabulary, the
//  accessibility-attribute projection, sanitize, binding resolution, and locale
//  formatting all come from the shared `Fuaran.UI.Renderer.Core` spine, so the
//  two renderers cannot drift on those axes. The per-node wrapper and the leaf
//  display kinds are `ElementPlan` values both renderers emit (Phase 2108), so
//  their element structure is defined once; the remaining kinds are still
//  re-emitted here against the ViewEngine API (the same Feliz-shaped calls).
//
//  Server semantics (see `docs/SSR.md` for the full table):
//   - No runtime, no dispatch. `Action`-bearing nodes render inert: a `Button`
//     renders its `<button>` (dead until hydration); a `Link` renders a real
//     crawlable `<a href>` (the no-JS navigation path). FGP 3 is satisfied
//     vacuously — there is no host to dispatch to server-side.
//   - Binding resolution: `Static` → value; `Query` → the server-supplied
//     `BindingSources`; `State` / `Local` / `Selection` / `Filter` →
//     initial/default (`Selection.defaultValue` resolves in the shared
//     resolver — Phase 629 — so preselected master-detail SSR matches CSR);
//     `Transform` in a scalar slot → its 1×1 result cell via the shared
//     scalar path (Phase 632); `Computed` → best-effort. The resolved/loaded
//     `StateBehaviour` branch renders by default (`OnLoading` only when a
//     binding genuinely doesn't resolve server-side).
//   - Client-library visualisations (`Chart` / `Map` / `DataGrid`) render a
//     deterministic placeholder + data attributes for later hydration, never a
//     blank.
//   - Markdown renders real HTML via the shared deterministic GFM renderer
//     (`.Core` `Markdown.toHtml`, Phase 292 — which retired the server-side
//     Markdig), not the degraded `<pre>`.
//
//  The document shell (`<html>` / `<head>` / meta / JSON-LD) and the reference
//  CSS stay host-owned; this renderer emits the body-fragment HTML only.
// ============================================================================

open Feliz.ViewEngine
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// Read-only context for a server render. No runtime, no dispatch sink — the
/// server renders structure, not behaviour. `Sources` drives binding
/// resolution; `Fragments` is the one-shot fragment registry collected from the
/// input tree (empty for trees that declare none).
type ServerRenderContext =
    {
        Sources: BindingResolver.BindingSources
        Fragments: Map<string, Node<obj>>
        /// Host-supplied server Custom-renderer registry (Phase 141). Empty by
        /// default — every `Custom` node then falls back to the labelled
        /// placeholder, matching the client's unregistered path.
        Customs: Registry.ServerCustomRendererRegistry
        /// Host-supplied CONTRACT CARDS (Phase 1108; WIRE_FORMAT.md §25). Empty
        /// by default, and an empty store leaves every emitted byte exactly as it
        /// was — the identity-only placeholder is unchanged for a host that holds
        /// no card, which is what makes this addition free for every existing
        /// consumer.
        ///
        /// Deliberately a SECOND store rather than a field on `Customs`. A
        /// renderer registry answers "can I render this"; a card store answers
        /// "can I describe this", and the whole point of the phase is the case
        /// where the second is true and the first is not. Folding the cards into
        /// the registry would have made a description obtainable only where a
        /// renderer already existed.
        Cards: Fuaran.UI.CustomCardStore
        /// What this render may do: its egress, `Custom` hash floor, CSP
        /// posture and scope (`RenderPolicy`, shared in shape with the client
        /// context). The two tiers' emitted DOM is parity-locked, so a posture
        /// moves on both at once or it is a parity defect.
        Policy: RenderPolicy
        /// Phase 1545 — the rules this render generated, in walk order. Empty
        /// and untouched under `Permissive`: nothing on that path registers
        /// anything, which is what keeps the default emission unchanged.
        ///
        /// One collector belongs to one context. It is mutable because
        /// collection happens DURING the walk while the element carrying it can
        /// only be emitted after — see `Csp.StyleCollector`, which carries the
        /// full argument for the shape.
        Styles: Csp.StyleCollector
    }

// ─── Text + value helpers ──────────────────────────────────────────────────

// `internal`, not `private`: the email projection (`Email.fs`) resolves its
// TextSources through THIS function rather than a copy, so an email digest and
// the SSR document cannot disagree about what a bound label says. Assembly-
// internal, so no public API surface changes.
let internal renderText (ctx: ServerRenderContext) (text: TextSource) : string =
    // Phase 1126 — SSR↔CSR parity is now structural rather than maintained:
    // this and the client renderer call the SAME
    // `BindingResolver.resolveTextSource`, so the two cannot drift apart in the
    // way `ScalarSsrParityTests` was written to detect. The semantics it pins
    // are unchanged.
    BindingResolver.resolveTextSource ctx.Sources text

/// The `CellFormat` projection — `Formatting.formatNumberIn`, the one definition
/// both renderers format through (Phase 2108), under the render's ambient
/// locale for a figure whose `locale` is `Ambient` (Phase 2490). `internal` for
/// the same reason as `renderText` above: the email and speech projections
/// format a Metric / LabelValueRow value through this name, so a figure in a
/// digest reads identically to the figure on the page.
let internal formatNumber (ctx: ServerRenderContext) (format: CellFormat) (value: float) : string =
    Formatting.formatNumberIn ctx.Sources.Locale format value

// ─── The plan emitter (Phase 2108) ──────────────────────────────────────────

/// This renderer's emitter for `ElementPlan` — the one place the server turns
/// a planned element into a Feliz.ViewEngine element. Attributes land verbatim
/// (`class` included, which is what `prop.className` writes); a void tag goes
/// through the void-element constructor the library's own `Html.*` members
/// use; inline style declarations are written in their canonical spelling;
/// a reconciliation key is React's alone and is dropped; pre-built markup is
/// written raw. The client renderer's `Emit` is the same mapping onto Feliz.
[<RequireQualifiedAccess>]
module private Emit =
    /// The boolean attributes this renderer wrote through the element
    /// library's boolean props before Phase 2278, which spell them
    /// `name="true"`; every other flag was a custom attribute with an empty
    /// value. Keeping both spellings keeps the served bytes.
    let private libraryBooleans =
        set [ "disabled"; "controls"; "loop"; "autoplay"; "muted"; "required" ]

    /// A planned property on an element of `tag`; a behaviour attachment
    /// point writes nothing — the served affordance is inert (Phase 2278).
    /// Phase 2290 — a controlled value lands where the document can show it:
    /// the `value` attribute on an input, nothing on a `<select>` or a
    /// `<textarea>` (whose selection / text the plan carries elsewhere).
    let private property (tag: string) (p: ElementPlan.Prop) : IReactProperty list =
        match p with
        | ElementPlan.Prop.Attr(name, value) -> [ prop.custom (name, value) ]
        | ElementPlan.Prop.Style declarations ->
            [ prop.style (declarations |> List.map (fun (k, v) -> style.custom (k, v))) ]
        | ElementPlan.Prop.Flag name when libraryBooleans.Contains name -> [ prop.custom (name, "true") ]
        | ElementPlan.Prop.Flag name -> [ prop.custom (name, "") ]
        | ElementPlan.Prop.Value value when tag <> "select" && tag <> "textarea" -> [ prop.custom ("value", value) ]
        | ElementPlan.Prop.Checked(true, served) -> [ prop.custom ("checked", served) ]
        | ElementPlan.Prop.Selected -> [ prop.custom ("selected", "selected") ]
        | ElementPlan.Prop.Value _
        | ElementPlan.Prop.Values _
        | ElementPlan.Prop.Checked _
        | ElementPlan.Prop.Behaviour _ -> []

    /// A `<textarea>`'s served text is its controlled value (Phase 2290).
    let private textareaText (spec: ElementPlan.ElementSpec<ReactElement>) : string option =
        if spec.Tag = "textarea" then
            spec.Props
            |> List.tryPick (function
                | ElementPlan.Prop.Value v -> Some v
                | _ -> None)
        else
            None

    /// Emit a plan.
    let rec plan (p: ElementPlan.Plan<ReactElement>) : ReactElement =
        match p with
        | ElementPlan.Plan.Host element -> element
        | ElementPlan.Plan.Nothing -> Html.none
        | ElementPlan.Plan.TextNode text -> Html.text text
        | ElementPlan.Plan.Element spec ->
            let props =
                [ yield! spec.Props |> List.collect (property spec.Tag)
                  match spec.Content, textareaText spec with
                  | ElementPlan.Content.Empty, Some text -> prop.text text
                  | ElementPlan.Content.Empty, None -> ()
                  | ElementPlan.Content.Text text, _ -> prop.text text
                  | ElementPlan.Content.Children children, _ -> prop.children (children |> List.map plan)
                  | ElementPlan.Content.InnerHtml markup, _ -> prop.dangerouslySetInnerHTML markup ]

            if ElementPlan.voidTags.Contains spec.Tag then
                Interop.createVoidElement spec.Tag props
            else
                Interop.createElement spec.Tag props

// ─── The renderer ───────────────────────────────────────────────────────────

/// What the walk carries that the PUBLIC `ServerRenderContext` does not
/// (Phase 2041). Private, so adding to it is free; on the record it would be a
/// breaking change for every host that constructs a context.
///
/// - `Depth` — the node's nesting level; the root is 1 (Phase 781).
/// - `InErrorBoundary` — true under an `ErrorBoundary`'s child. The per-node
///   guard then SUSPENDS, so a throw reaches the boundary and its `Fallback`
///   renders — the client's `RenderContext.InErrorBoundary`, same semantics.
/// - `Expanding` — the fragments being expanded on the path to this node; a ref
///   that re-enters one renders the cycle placeholder — the client's
///   `RenderContext.ExpandingFragments`.
type private Walk =
    { Depth: int
      InErrorBoundary: bool
      Expanding: Set<string> }

[<RequireQualifiedAccess>]
module private Walk =
    let root: Walk =
        { Depth = 1
          InErrorBoundary = false
          Expanding = Set.empty }

    /// The same walk one level down.
    let deeper (walk: Walk) : Walk = { walk with Depth = walk.Depth + 1 }

let rec private renderNode (walk: Walk) (ctx: ServerRenderContext) (node: Node<obj>) : ReactElement =
    if walk.Depth > Fuaran.UI.WireLimits.MaxDepth then
        // Phase 781 — a visible, machine-readable truncation marker in place of a
        // subtree nested past `WireLimits.MaxDepth`: the renderer is total by
        // signature, and the alternative is a `StackOverflowException` (this walk
        // costs ~15 KB of stack per level in Release). Every wire path refused such
        // a tree already, so a host reaching it built the tree in-process.
        Emit.plan (ElementPlan.NodePlan.depthExceeded node.Id)
    // Fuaran-UI Phase 1535 — CONDITIONAL PRESENCE. A resolved `false` on
    // `node.Visible` emits NOTHING: no element, no placeholder, no comment
    // marker and no `aria-hidden`. The rule is `BindingResolver.isNodeVisible`,
    // the same call the client renderer makes against the same seeded sources,
    // which is what makes hydration agree — the client's first render takes the
    // identical decision, so the two trees have the same shape and React has
    // nothing to reconcile.
    //
    // Absence, `NotResolved` and `Errored` all render (the rule's own doc
    // comment carries the reasoning). The server has no `Warn` channel on this
    // path, so the `Errored` warning is the client's alone; the RENDERING is
    // identical either way, which is the property hydration depends on.
    elif not (BindingResolver.isNodeVisible ctx.Sources node) then
        Html.none
    else
        renderNodeCore walk ctx node

/// The node render proper. Reached only through `renderNode`, which is what
/// enforces `MaxDepth` — split out so the guard is two lines rather than a
/// wrapper around fifty indented ones.
and private renderNodeCore (walk: Walk) (ctx: ServerRenderContext) (node: Node<obj>) : ReactElement =
    let id = node.Id

    // Phase 2108 — the wrapper is the shared `NodePlan`: tooltip, motion class,
    // bidi, accessibility / extra-attribute routing and the wrapper props are
    // decided once for both renderers. The island marker (Phase 163) is lifted
    // onto the boundary below, not the node's own wrapper, so the routing reads
    // the node without it.
    let routing =
        ElementPlan.NodePlan.route ctx.Sources (Fuaran.UI.Node.stripIsland node)

    // Per-node render guard (Phase 2041) — the client renderer's, same
    // semantics. A node body that throws (a host `ServerCustomRenderer`, a
    // `CellFormat.Custom` closure, a malformed spec) degrades to the fallback
    // element inside this node's wrapper, so its siblings and the rest of the
    // request still render; the wrapper keeps `data-fuaran-node-id`. Under an
    // `ErrorBoundary`'s child the guard suspends and the throw reaches the
    // boundary, which renders its `Fallback` — the subtree the author asked for
    // in place of a placeholder where the bad leaf was.
    let kindBody =
        try
            renderKind walk ctx id node.State node.Kind routing.SemanticAttrs
        with ex when not walk.InErrorBoundary ->
            let kindName = RenderParity.nodeKindName node.Kind

            Emit.plan (
                ElementPlan.NodePlan.renderFailure
                    id
                    kindName
                    ex.Message
                    (RenderParity.renderFailureCorrelationId id kindName)
            )

    let element =
        Emit.plan (ElementPlan.NodePlan.wrapper id routing (ElementPlan.Plan.Host kindBody))

    // Hydration island boundary (Phase 163). When the node is marked
    // `Node.asIsland`, wrap its rendered element in a `data-fuaran-island`
    // boundary `<div>` — the hydration container whose children are exactly
    // this node's static HTML, so the client `hydrateRoot` is mismatch-free.
    // Zero islands → no wrapper (this match is `None` for every ordinary node).
    match Fuaran.UI.Node.islandId node with
    | Some island ->
        Html.div
            [ prop.className "fuaran-island"
              prop.custom ("data-fuaran-island", island)
              prop.children [ element ] ]
    | None -> element

/// The per-kind dispatch. Phase 2108 / 2278 — a planned kind is ONE behaviour
/// emitted twice: its arm is a call to the shared `ElementPlan` planner (the
/// display, layout and media kinds) plus, on the client, the resolver closure
/// that binds the plan's named attachment points. The six STRUCTURAL arms —
/// `ErrorBoundary`, `Switch`, `Custom`, `FragmentDecl`, `FragmentRef`, `Mount` —
/// stay here by nature (Phase 2278 ruling): they are walk control, deciding
/// which node renders and how (error suspension, case selection, fragment
/// expansion with cycle tracking, host renderer registries, guest loaders),
/// and the walk is each renderer's own; the leaf markup they emit is
/// `ElementPlan.StructuralPlan`.
///
/// Phase 2290 — the input and visualisation kinds are planned too
/// (`ElementPlan.InputPlan` / `VisPlan`). Their WIDGET controls are named
/// by-nature arms on the CLIENT: the Combobox, Tokens and writable Rating
/// controls (field and filter chip), a `Binding.Local` Text / Number /
/// RangedNumber field, the FileUpload drop zone and stream shell, the bound
/// DataGrid, and a Chart a host VisAdapter draws. Each is a stateful React
/// component the client renders in place of the plan's no-script FLOOR, which
/// is what this tier serves.
and private renderKind
    (walk: Walk)
    (ctx: ServerRenderContext)
    (parentNodeId: string)
    (state: StateBehaviour<obj> option)
    (kind: NodeKind<obj>)
    // Phase 951 — the node's a11y projection, for the kinds that carry it on
    // their own semantic element. `[]` for every other kind, which is every
    // arm below except `Link` / `Button` / `Image`.
    (semanticAttrs: (string * string) list)
    : ReactElement =
    match kind with
    // Phase 2278 — the layout kinds are `ElementPlan.LayoutPlan` values,
    // emitted inert (the server binds no attachment point).
    | NodeKind.Box spec -> Emit.plan (ElementPlan.LayoutPlan.box (planContext walk ctx parentNodeId) spec)
    | NodeKind.SplitPanel spec -> Emit.plan (ElementPlan.LayoutPlan.splitPanel (planContext walk ctx parentNodeId) spec)
    | NodeKind.Tabs spec ->
        Emit.plan (
            ElementPlan.LayoutPlan.tabs
                (planContext walk ctx parentNodeId)
                spec
                (ElementPlan.TabsModel.ofSpec ctx.Sources spec)
        )
    | NodeKind.SummaryList spec ->
        Emit.plan (ElementPlan.LayoutPlan.summaryList (planContext walk ctx parentNodeId) spec)
    | NodeKind.Disclosure spec -> Emit.plan (ElementPlan.LayoutPlan.disclosure (planContext walk ctx parentNodeId) spec)
    | NodeKind.Stepper spec -> Emit.plan (ElementPlan.LayoutPlan.stepper (planContext walk ctx parentNodeId) spec)
    | NodeKind.Modal spec -> Emit.plan (ElementPlan.LayoutPlan.modal (planContext walk ctx parentNodeId) spec)
    | NodeKind.ScrollArea spec -> Emit.plan (ElementPlan.LayoutPlan.scrollArea (planContext walk ctx parentNodeId) spec)
    | NodeKind.Heading spec -> Emit.plan (ElementPlan.DisplayPlan.heading ctx.Sources spec)
    | NodeKind.Markdown spec -> Emit.plan (ElementPlan.DisplayPlan.markdown ctx.Policy.Egress ctx.Sources spec)
    | NodeKind.Metric spec ->
        // Phase 632 — a scalar slot: a `Binding.Transform` resolves to its 1×1
        // result cell. Phase 2209 — its bound field rides the same evaluation
        // (WIRE_FORMAT §3.6.27).
        let resolution, field =
            BindingResolver.resolveScalarWithField BindingResolver.cellToFloat ctx.Sources spec.Value

        match
            ElementPlan.DisplayPlan.stateSlot
                parentNodeId
                (state |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour)
                resolution
        with
        | Some slot -> renderNode (Walk.deeper walk) ctx slot
        | None -> Emit.plan (ElementPlan.DisplayPlan.metric ctx.Sources resolution field spec)
    | NodeKind.Badge spec -> Emit.plan (ElementPlan.DisplayPlan.badge ctx.Sources spec)
    | NodeKind.Skeleton spec -> Emit.plan (ElementPlan.DisplayPlan.skeleton spec)
    | NodeKind.Icon spec -> Emit.plan (ElementPlan.DisplayPlan.icon spec)
    | NodeKind.Callout spec -> Emit.plan (ElementPlan.DisplayPlan.callout ctx.Sources spec)
    | NodeKind.Progress spec ->
        let resolution = BindingResolver.resolve ctx.Sources spec.Fraction

        match
            ElementPlan.DisplayPlan.stateSlot
                parentNodeId
                (state |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour)
                resolution
        with
        | Some slot -> renderNode (Walk.deeper walk) ctx slot
        | None ->
            Emit.plan (
                ElementPlan.DisplayPlan.progress
                    ctx.Sources
                    (ElementPlan.styleSite ctx.Policy.Csp (Some ctx.Styles) parentNodeId)
                    resolution
                    spec
            )
    | NodeKind.Sparkline spec ->
        BindingResolver.tryResolve ctx.Sources spec.Source
        |> Option.bind (Fuaran.UI.Charts.tryLowerSparkline spec)
        |> ElementPlan.DisplayPlan.sparkline ctx.Sources
        |> Emit.plan
    | NodeKind.Drawing spec -> Emit.plan (ElementPlan.DisplayPlan.drawing ctx.Sources spec)
    | NodeKind.LabelValueRow spec ->
        // Phase 632 — a scalar slot, as `Metric`'s.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match
            ElementPlan.DisplayPlan.stateSlot
                parentNodeId
                (state |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour)
                resolution
        with
        | Some slot -> renderNode (Walk.deeper walk) ctx slot
        | None -> Emit.plan (ElementPlan.DisplayPlan.labelValueRow ctx.Sources resolution spec)
    | NodeKind.Fact spec -> Emit.plan (ElementPlan.DisplayPlan.fact ctx.Sources spec)
    // Phase 2278 — the media kinds are `ElementPlan.MediaPlan` values.
    | NodeKind.Link spec ->
        Emit.plan (ElementPlan.MediaPlan.link (planContext walk ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Image spec ->
        Emit.plan (ElementPlan.MediaPlan.image (planContext walk ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Media spec ->
        Emit.plan (ElementPlan.MediaPlan.media (planContext walk ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Embed spec ->
        Emit.plan (ElementPlan.MediaPlan.embed (planContext walk ctx parentNodeId) spec semanticAttrs)
    | NodeKind.Tree spec ->
        Emit.plan (ElementPlan.VisPlan.tree ctx.Sources spec (ElementPlan.VisPlan.treeState ctx.Sources spec))
    | NodeKind.List spec -> Emit.plan (ElementPlan.DisplayPlan.list ctx.Sources spec)
    | NodeKind.Toast spec -> Emit.plan (ElementPlan.DisplayPlan.toast ctx.Sources spec)
    | NodeKind.CodeBlock spec -> Emit.plan (ElementPlan.DisplayPlan.codeBlock spec)
    | NodeKind.Math spec -> Emit.plan (ElementPlan.DisplayPlan.math spec)
    // -- Input --
    | NodeKind.Button spec -> Emit.plan (ElementPlan.InputPlan.button ctx.Sources spec semanticAttrs)
    | NodeKind.Select spec -> Emit.plan (ElementPlan.InputPlan.select ctx.Sources spec)
    | NodeKind.Form spec ->
        Emit.plan (
            ElementPlan.InputPlan.form
                (planContext walk ctx parentNodeId)
                spec
                [ for field in spec.Fields ->
                      ElementPlan.InputPlan.formField
                          ctx.Sources
                          field
                          (ElementPlan.InputPlan.fieldControl ctx.Sources field) ]
        )
    | NodeKind.Filters specs ->
        Emit.plan (
            ElementPlan.InputPlan.filters
                [ for spec in specs.Items ->
                      ElementPlan.InputPlan.filter
                          ctx.Sources
                          spec
                          (ElementPlan.InputPlan.filterControl ctx.Sources spec) ]
        )
    | NodeKind.FileUpload spec -> Emit.plan (ElementPlan.InputPlan.fileUpload ctx.Sources spec)
    // -- Vis --
    | NodeKind.DataGrid spec ->
        // Phase 393 — a static grid is the semantic table; a bound grid is the
        // client's, and this tier serves its hydration placeholder (by nature).
        match spec.StaticRows with
        | Some sr -> Emit.plan (ElementPlan.VisPlan.staticGrid ctx.Sources parentNodeId spec sr)
        | None -> Emit.plan (ElementPlan.VisPlan.gridPlaceholder ctx.Sources spec)
    | NodeKind.Chart spec ->
        // Phase 526 / 643 — the lowered kinds draw the shared Drawing SVG through
        // `Charts.renderSvg`, the one entry point the client also calls.
        Emit.plan (
            ElementPlan.VisPlan.chart
                (planContext walk ctx parentNodeId)
                state
                spec
                (Fuaran.UI.Charts.isLowered spec.Kind)
                (Fuaran.UI.Charts.renderSvg ctx.Sources (renderText ctx))
        )
    | NodeKind.Map spec -> Emit.plan (ElementPlan.VisPlan.map (planContext walk ctx parentNodeId) state spec)
    | NodeKind.ErrorBoundary spec ->
        // Phase 2041 — the client's boundary, same semantics. The child renders
        // with the per-node guard suspended, so a throw anywhere in it (a host
        // closure, typically) lands here and the typed `Fallback` renders in its
        // place. The fallback renders with the guard back on, so a flaky
        // fallback degrades node by node rather than escalating; if it throws
        // outside any guard as well, the boundary reports both failures in the
        // per-node fallback element, exactly as the client does.
        let child = Walk.deeper walk

        try
            renderNode { child with InErrorBoundary = true } ctx spec.Child
        with ex ->
            try
                renderNode { child with InErrorBoundary = false } ctx spec.Fallback
            with ex2 ->
                // The placeholder carries the PRIMARY failure's correlation id, as
                // the client's does, so a log filter joins the two reports (Phase
                // 2108: this tier used to hash the `.Fallback` name instead).
                let kindName = RenderParity.nodeKindName kind

                Emit.plan (
                    ElementPlan.NodePlan.renderFailure
                        parentNodeId
                        (kindName + ".Fallback")
                        (RenderParity.boundaryDoubleFailureMessage ex.Message ex2.Message)
                        (RenderParity.renderFailureCorrelationId parentNodeId kindName)
                )
    | NodeKind.Switch spec ->
        // State-bound conditional child (Phase 392). SSR resolves the initial
        // state value from `ctx.Sources.State` (host pre-populated) and renders
        // the matching case — else the Default. The client's first render reads
        // the same initial state (the StateStore seeded identically), so server
        // and client emit the same tree shape → hydration is mismatch-free
        // (docs/SSR.md); after hydration a client `SetState` re-selects a case.
        // Phase 768 — the selector is any Binding. The State form keeps the
        // host-seeded map read (hydration-parity path, unchanged); other
        // bindings resolve through the standard resolver, so an SSR switch on a
        // pre-seeded Selection/Filter renders the same branch the client will.
        let currentValue: obj option =
            match spec.On with
            | Binding.State(key, dv) ->
                match Map.tryFind key ctx.Sources.State with
                | Some v -> Some v
                | None -> dv |> Option.map box
            | Binding.Selection(nodeId, _, dv, fld) ->
                let projector: obj -> obj =
                    match fld with
                    | Some f -> Binding.projectSelectionField<obj> f
                    | None -> id

                BindingResolver.tryResolve ctx.Sources (Binding.Selection(nodeId, projector, dv |> Option.map box, fld))
            // Fuaran-UI Phase 1535 — the SCALAR resolver, matching the client
            // renderer line for line: `tryResolve`'s `Transform` arm is row-only
            // and cannot serve a string slot, so a computed selector fell
            // through to `Default` on both hosts. Every other binding case
            // resolves exactly as before.
            | on -> BindingResolver.tryResolveScalarText ctx.Sources on |> Option.map box

        // Fuaran-UI Phase 1535 — first-match-wins over both kinds of case,
        // through the one shared definition in `Renderer.Core`, so this renderer
        // and the client renderer cannot drift on the order or on what a
        // predicate that fails to resolve means.
        let matched =
            let selector =
                currentValue |> Option.map (fun v -> if isNull v then "" else string v)

            BindingResolver.selectSwitchCase ctx.Sources selector spec.Cases

        match matched with
        | Some child -> renderNode (Walk.deeper walk) ctx child
        | None -> renderNode (Walk.deeper walk) ctx spec.Default
    | NodeKind.FragmentDecl _ ->
        // Zero-paint: the decl is a template, not visible output.
        Html.none
    | NodeKind.FragmentRef spec ->
        // Phase 2041 — the client's expansion, through the SAME shared walk: the
        // body's interior ids are namespaced under the ref's own id
        // (`ref1.btn` / `ref2.btn`), so two refs to one fragment emit unique DOM
        // ids and the ids the client hydrates against. A ref that re-enters a
        // fragment already being expanded on this path renders the client's
        // cycle placeholder rather than recursing to the depth limit.
        let raw = spec.Name

        if Set.contains raw walk.Expanding then
            Emit.plan (ElementPlan.StructuralPlan.fragmentCycle raw)
        else
            match Map.tryFind raw ctx.Fragments with
            | Some body ->
                let namespaced =
                    FragmentExpansion.expandNamespaced (FragmentExpansion.refPrefix parentNodeId) body

                renderNode
                    { Walk.deeper walk with
                        Expanding = Set.add raw walk.Expanding }
                    ctx
                    namespaced
            | None -> Emit.plan (ElementPlan.StructuralPlan.fragmentUnresolved raw)
    | NodeKind.Custom spec ->
        renderCustom
            ctx
            spec.ModuleId
            spec.ComponentId
            spec.Props
            spec.ContentHash
            (spec.ExposedNodeIds |> Option.defaultValue [])
    | NodeKind.Mount spec ->
        // Isolation/embedding boundary (Phase 265, §4o). SSR renders the same
        // declared empty state + `data-fuaran-mount-scope` boundary attribute
        // as the client renderer, so server and client emit byte-identical
        // structure across the boundary (no hydration mismatch); the guest
        // loader attaches client-side (Phase 266).
        Emit.plan (
            ElementPlan.StructuralPlan.mountBoundary
                spec.ScopeId
                (ElementPlan.StructuralPlan.mountPlaceholder spec.ScopeId)
        )

// ─── Custom escape hatch (Phase 141 — server registry seam) ─────────────────

/// What a container kind's plan needs from this renderer (Phase 2278): the
/// style collector, and a child rendered one walk level down.
and private planContext
    (walk: Walk)
    (ctx: ServerRenderContext)
    (nodeId: string)
    : ElementPlan.PlanContext<obj, ReactElement> =
    { Sources = ctx.Sources
      Csp = ctx.Policy.Csp
      Collector = Some ctx.Styles
      Egress = ctx.Policy.Egress
      NodeId = nodeId
      Child = fun child -> ElementPlan.Plan.Host(renderNode (Walk.deeper walk) ctx child) }

and private renderCustom
    (ctx: ServerRenderContext)
    (moduleId: string)
    (componentId: string)
    (props: Map<string, Fuaran.Core.JVal>)
    (contentHash: ContentHash option)
    (exposedNodeIds: string list)
    : ReactElement =
    // Unregistered placeholder — identical labelled shape to the client's
    // unregistered Custom path (parity).
    //
    // Phase 1108 — TWO shapes, and the first one is byte-for-byte what shipped
    // before. A host holding no card for this identity emits exactly the
    // identity-only div it always emitted; a host holding one emits the
    // card-derived degradation the §25.4 obligation states. That the uncarded
    // path is untouched is not a courtesy: it is what makes the obligation
    // safe to declare on a kind every roster host already renders.
    let identityOnly () =
        Html.div
            [ prop.className (Css.customPlaceholder moduleId componentId)
              prop.custom ("data-fuaran-custom-module", moduleId)
              prop.custom ("data-fuaran-custom-component", componentId)
              prop.text (sprintf "[fuaran:custom %s.%s]" moduleId componentId) ]

    let carded (described: Fuaran.UI.CardPlaceholder) =
        let summary =
            match described.Summary with
            | Some s -> [ Html.div [ prop.className "fuaran-custom-summary"; prop.text s ] ]
            | None -> []

        let props =
            match described.PropLines with
            | [] -> []
            | lines ->
                [ Html.ul
                      [ prop.className Css.Class.CustomProps
                        prop.children (lines |> List.map (fun line -> Html.li [ prop.text line ])) ] ]

        // The node's own prop bag, judged against the card. This is the half a
        // labelling pass alone would miss: a foreign host can now say the node
        // is MALFORMED, where before it could only fail to render it. Defect
        // KEYS and messages are the card's own words about its declared schema —
        // never a prop VALUE, which the host was not asked to interpret.
        let defects =
            match described.Validation.Defects with
            | [] -> []
            | ds ->
                [ Html.ul
                      [ prop.className "fuaran-custom-defects"
                        prop.children (ds |> List.map (fun d -> Html.li [ prop.text d.Message ])) ] ]

        Html.div
            [ prop.className (Css.customPlaceholder moduleId componentId)
              prop.custom ("data-fuaran-custom-module", moduleId)
              prop.custom ("data-fuaran-custom-component", componentId)
              prop.custom ("data-fuaran-custom-card", Fuaran.UI.CustomCard.verdictMarker described.HashVerdict)
              prop.children (
                  [ Html.div [ prop.className Css.Class.CustomLabel; prop.text described.Label ] ]
                  @ summary
                  @ props
                  @ defects
              ) ]

    let placeholder () =
        match ctx.Cards.TryFind(moduleId, componentId) with
        | None -> identityOnly ()
        | Some card -> carded (Fuaran.UI.CustomCard.describe contentHash props card)

    // Phase 783 — SCOPE-CONSTRAINED lookup. No cross-scope fallback.
    match Registry.tryRenderInScope ctx.Policy.Scope moduleId componentId ctx.Customs with
    | None -> placeholder ()
    | Some renderer ->
        // Bounded-escape hash check (Phase 70). When the node declares a
        // ContentHash AND the registry recorded the renderer's hash, compare per
        // strictness: StrictReplay / Enforced route a hard mismatch to a labelled
        // error placeholder; AdvisoryWarning renders the body + a drift marker.
        // Phase 783 — the strictness applied is the tighter of the HOST's floor
        // and the tree's own declaration, and under an enforcing floor a hash
        // that cannot be VERIFIED (no tree hash, or no registered hash) is a
        // refusal rather than a silent render. Reading strictness from the
        // tree's own record alone let an attacker who declared a hash pick
        // `AdvisoryWarning` and get warn-then-render, and omitting the hash
        // skipped verification altogether.
        let registeredHash =
            Registry.tryHashInScope ctx.Policy.Scope moduleId componentId ctx.Customs

        // Phase 1532 — the strictest of the process floor and this render's own
        // declaration. The server tier is where the single-tenant floor hurt
        // most: one process serves every tenant's documents.
        let floor = CustomHash.effectiveFloor ctx.Policy.CustomHashFloor

        let outcome =
            match contentHash, registeredHash with
            | Some declared, Some registered when declared.Hash <> registered ->
                let effective =
                    if declared.Strictness = HashStrictness.AdvisoryWarning then
                        floor
                    else
                        declared.Strictness

                Some effective
            | Some _, Some _ -> None
            | None, _ ->
                // Phase 1550 — TREE-SIDE absence. The floor governs mismatch,
                // and a tree that declares no hash has nothing to mismatch, so
                // only `StrictReplay` refuses it. Split from the registry-side
                // arm below (they shared one catch-all) because the flip of the
                // default to `Enforced` made the two answers differ: lumped, an
                // unconfigured host would refuse every hash-less `Custom` node
                // server-side, which is exactly the upgrade break the flip is
                // designed not to cause.
                if CustomHash.refusesUnverifiable floor then
                    Some floor
                else
                    None
            | Some _, None ->
                // REGISTRY-SIDE absence: the tree made a claim the registry
                // cannot check. A verification failure, not an absence — any
                // enforcing floor refuses it, as before.
                if CustomHash.isEnforcing floor then Some floor else None

        match outcome with
        | Some HashStrictness.StrictReplay
        | Some HashStrictness.Enforced ->
            Html.div
                [ prop.className (Css.customHashMismatch moduleId componentId)
                  prop.custom ("data-fuaran-custom-hash-mismatch", "strict")
                  prop.text (
                      sprintf "[fuaran:custom %s.%s — content-hash mismatch (StrictReplay)]" moduleId componentId
                  ) ]
        | advisory ->
            // The registered closure is a host trust boundary — it escapes its
            // own output; the server does not sanitize it (same posture as the
            // client RegisterCustomRenderer). See SANITIZATION.md.
            let body = renderer props

            // Emit the declared exposed-node-ids as an addressable data attribute
            // so the layout observer / hydration can locate interior nodes. Only
            // wraps when ids are declared (zero structural change otherwise).
            let exposedAttr =
                match exposedNodeIds with
                | [] -> []
                | ids ->
                    let joined = ids |> String.concat " "
                    [ prop.custom ("data-fuaran-exposed-node-ids", joined) ]

            let advisoryAttr =
                match advisory with
                | Some HashStrictness.AdvisoryWarning ->
                    [ prop.custom ("data-fuaran-custom-hash-mismatch", "advisory") ]
                | _ -> []

            match exposedAttr @ advisoryAttr with
            | [] -> body
            | attrs ->
                Html.div (
                    [ prop.className (Css.customWrapper moduleId componentId) ]
                    @ attrs
                    @ [ prop.children [ body ] ]
                )

// ─── Public entry points ─────────────────────────────────────────────────────

/// Build a `ServerRenderContext` with the one-shot fragment registry collected
/// from `node` and a host-supplied Custom-renderer registry. Empty fragment map
/// for trees that declare none (zero cost).
let mkContextWith
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    // Phase 1075 — the SEEDING PASS (`StateSeeds.apply`, the fold the client
    // builder runs too), at the server tier's single context choke point, so it
    // lands on every entry point below by construction and the two hosts' first
    // render read an identical `State` map (`docs/SSR.md`).
    { Sources = StateSeeds.apply node sources
      // The registry walk the client builds its table with too (Phase 2244).
      Fragments = FragmentExpansion.collectFragments id Map.empty node
      Customs = customs
      // Phase 1108 — empty by default. A host that holds cards reaches them by
      // name (`mkContextWithCards`), exactly as it reaches a non-default egress
      // policy, so no existing entry point changes what it emits.
      Cards = Fuaran.UI.CustomCardStore.Empty
      // Default-deny egress, the process hash floor, `Permissive` CSP and the
      // root scope; each other posture is reached BY NAME below
      // (`mkContextWithEgress` / `mkContextWithCsp` / `mkContextInScope`).
      Policy = RenderPolicy.defaults
      // One collector per context, so it can never outlive the render that
      // built it or be shared between two. Untouched under `Permissive`.
      Styles = Csp.StyleCollector() }

/// `mkContextWith` with an EXPLICIT destination policy (Phase 1026) — the named
/// opt-out from the ambient default-deny, and the server twin of the client's
/// `renderWithSourcesAndEgress`.
///
/// The policy a Giraffe host declares arrives here; see
/// `FuaranGiraffeOptions.EgressPolicy`. A host composing this tier directly
/// builds its policy with `Sanitize.allowOrigin` and passes it in. Passing
/// `Sanitize.denyNonLocalEgress` is exactly `mkContextWith`.
let mkContextWithEgress
    (egressPolicy: Sanitize.EgressPolicy)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    let ctx = mkContextWith customs sources node

    { ctx with
        Policy =
            { ctx.Policy with
                Egress = egressPolicy } }

/// `mkContextWith` plus a host-supplied CONTRACT-CARD store (Phase 1108) — what
/// a host passes when it can DESCRIBE components it cannot render.
///
/// The two stores are independent, and all four combinations are meaningful: a
/// renderer and a card (the card is unused — a rendered node needs no
/// description), a renderer only (unchanged), a card only (the case this phase
/// exists for), and neither (unchanged).
let mkContextWithCards
    (cards: Fuaran.UI.CustomCardStore)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    { mkContextWith customs sources node with
        Cards = cards }

/// `mkContextWith` under an explicit CSP posture (Phase 1545) — the named
/// opt-in to strict mode.
///
/// Under `Csp.Strict nonce` the context's collector fills as the tree is
/// walked, so the caller must read it from THIS context after rendering —
/// `collectedStyleHtml` does that, and `renderWithCsp` composes both in one
/// call. Passing `Csp.Permissive` is exactly `mkContextWith`.
let mkContextWithCsp
    (csp: Csp.CspMode)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    let ctx = mkContextWith customs sources node

    { ctx with
        Policy = { ctx.Policy with Csp = csp } }

/// Build a `ServerRenderContext` under a named render SCOPE (Phase 783) —
/// Custom-renderer lookup is then constrained to renderers registered for that
/// scope (`Registry.registerInScope`).
let mkContextInScope
    (scope: string)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    let ctx = mkContextWith customs sources node

    { ctx with
        Policy = { ctx.Policy with Scope = Some scope } }

/// Build a `ServerRenderContext` with no Custom renderers registered (every
/// `Custom` node falls back to the labelled placeholder).
let mkContext (sources: BindingResolver.BindingSources) (node: Node<obj>) : ServerRenderContext =
    mkContextWith Registry.empty sources node

/// Render a `Node<obj>` tree to a `ReactElement` for host composition (e.g.
/// embedding inside a host's own ViewEngine document layout). The host calls
/// `Feliz.ViewEngine.Render.htmlView` (or `htmlDocument`) when ready.
let renderToElement (sources: BindingResolver.BindingSources) (node: Node<obj>) : ReactElement =
    renderNode Walk.root (mkContext sources node) node

/// Render a `Node<obj>` tree to an HTML string on plain .NET. The body-fragment
/// HTML only — the host owns `<html>` / `<head>` / meta / the reference CSS.
let render (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.htmlView (renderToElement sources node)

/// Phase 1812 — the BEHIND reader's render (WIRE_FORMAT §15.3): a `Rendered`
/// view (the node, or the `fallback` lifted from an `Unknown`) renders as any
/// node does; a `Placeholder` is the labelled degrade, byte-identical to the
/// client's. Produce the view with `Fuaran.UI.Ops.JsonDecode.BehindReader.view`.
let renderBehindToElement (sources: BindingResolver.BindingSources) (view: BehindView<obj>) : ReactElement =
    match view with
    | BehindView.Rendered node -> renderToElement sources node
    | BehindView.Placeholder(kind, required) ->
        let label =
            match required with
            | Some p -> sprintf "needs %s" p
            | None -> sprintf "unknown kind %s" kind

        Html.div
            [ prop.className Css.Class.UnknownPlaceholder
              prop.custom ("data-fuaran-kind", kind)
              match required with
              | Some p -> prop.custom ("data-fuaran-requires", p)
              | None -> ()
              prop.text label ]

/// `renderBehindToElement` to an HTML string.
let renderBehind (sources: BindingResolver.BindingSources) (view: BehindView<obj>) : string =
    Render.htmlView (renderBehindToElement sources view)

/// Render a no-dynamic-bindings `Node<obj>` tree to an HTML string — the common
/// static-SSR case. Bakes in `BindingResolver.empty`, so a host with no dynamic
/// bindings renders a tree in one call, identical to `render BindingResolver.empty
/// tree`. Matches the `renderWithTheme` convenience precedent (Phase 434).
let renderStatic (node: Node<obj>) : string = render BindingResolver.empty node

/// Render a `Node<obj>` tree to an HTML string with a host-supplied server
/// Custom-renderer registry (Phase 141) — domain components plug in here.
let renderWith
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode Walk.root (mkContextWith customs sources node) node)

/// `renderWith` under an EXPLICIT destination policy (Phase 1026) — the named
/// opt-out for a host composing this tier without going through the Giraffe
/// handlers (which carry `FuaranGiraffeOptions.EgressPolicy` instead).
let renderWithEgress
    (egressPolicy: Sanitize.EgressPolicy)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode Walk.root (mkContextWithEgress egressPolicy customs sources node) node)

/// Render under a named render SCOPE with a host-supplied registry (Phase 783).
/// Only renderers registered for `scope` are reachable from the tree.
let renderWithInScope
    (scope: string)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode Walk.root (mkContextInScope scope customs sources node) node)

/// `renderWith` plus a host-supplied CONTRACT-CARD store (Phase 1108) — an
/// unregistered `Custom` node whose identity the store knows renders the
/// card-derived labelled placeholder instead of the identity-only one.
let renderWithCards
    (cards: Fuaran.UI.CustomCardStore)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode Walk.root (mkContextWithCards cards customs sources node) node)

/// A `<style>` element carrying the Phase 12.K `Theme` → CSS-variable `:root`
/// projection — parity with the client `themeStyleElement`. The host mounts
/// this once (typically in `<head>`); the renderer's class vocabulary keys off
/// these variables.
let themeStyleElement (theme: Theme) : ReactElement =
    Html.style [ prop.dangerouslySetInnerHTML (Theme.toCss theme) ]

/// `themeStyleElement` under an explicit CSP posture (Phase 1545). Under
/// `Csp.Strict nonce` the element carries `nonce="…"`, so a host serving
/// `style-src 'nonce-…'` can inject the theme without `'unsafe-inline'`; under
/// `Permissive` it is `themeStyleElement`, byte for byte.
///
/// The nonce lands in an ATTRIBUTE value and is emitted through ViewEngine, so
/// it is escaped for free — nothing here has to handle a host's own string
/// beyond placing it.
let themeStyleElementWithCsp (csp: Csp.CspMode) (theme: Theme) : ReactElement =
    match Csp.nonce csp with
    | Some n -> Html.style [ prop.custom ("nonce", n); prop.dangerouslySetInnerHTML (Theme.toCss theme) ]
    | None -> themeStyleElement theme

/// Render the theme `:root` style block followed by the tree's body HTML, as a
/// single string. Convenience for a host that wants both in one emission.
///
/// Phase 2482 — the CHART palette is not themed by this call. A chart's series
/// colours are baked into its drawing from the host-installed chart style, and
/// the install is process-wide, so a per-request install here would let two
/// concurrent renders under different themes race on it. An SSR host installs
/// its theme's chart style ONCE, at boot —
/// `Fuaran.UI.Charts.installChartStyleFromTheme theme`, which validates the
/// palette and returns any refusal by slot or pair — and every chart this
/// renderer and the client renderer draw then carries the same palette. A host
/// serving several themes from one process lowers per call with
/// `Charts.lowerWithStyle` and `Charts.chartStyleOfTheme` instead.
let renderWithTheme (theme: Theme) (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.htmlView (themeStyleElement theme) + render sources node

// ─── Strict-CSP rendering (Phase 1545) ─────────────────────────────────────

/// The collected `<style>` element for a render that has ALREADY happened
/// against `ctx`, as an HTML string — `""` when the render generated no rule,
/// which is every render under `Permissive` and every strict render of a tree
/// carrying no continuous value.
///
/// One element per render root, carrying every generated rule in walk order.
/// The rules go in as raw `<style>` content, which is why the collector refuses
/// `<` and `>` in a declaration value on top of the shared CSS grammar (see
/// `Csp.isCollectableValue`); everything else about them is renderer-formatted
/// numbers or a value that already passed its emission-site gate.
let collectedStyleHtml (ctx: ServerRenderContext) : string =
    if ctx.Styles.IsEmpty then
        ""
    else
        let css = Csp.stylesheetText ctx.Styles.Rules

        let element =
            match Csp.nonce ctx.Policy.Csp with
            | Some n -> Html.style [ prop.custom ("nonce", n); prop.dangerouslySetInnerHTML css ]
            | None -> Html.style [ prop.dangerouslySetInnerHTML css ]

        Render.htmlView element

/// Render a tree under an explicit CSP posture, returning the collected
/// stylesheet followed by the body-fragment HTML.
///
/// Under `Csp.Strict nonce` the emitted markup carries NO `style` attribute:
/// every continuous value is a generated class whose declaration rides the one
/// nonce-bearing `<style>` element this returns ahead of the body. Under
/// `Csp.Permissive` it is exactly `renderWith` — no element is emitted, because
/// nothing registered anything.
///
/// The stylesheet leads rather than trails because a document is parsed in
/// order and a rule that arrives after the element it styles is a flash of
/// unstyled content on a slow connection. It is INSIDE the body fragment, not
/// in the head, because this tier owns the fragment and not the shell — a host
/// that wants it in `<head>` renders through `mkContextWithCsp` and places
/// `collectedStyleHtml` itself.
let renderWithCsp
    (csp: Csp.CspMode)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    let ctx = mkContextWithCsp csp customs sources node
    let body = Render.htmlView (renderNode Walk.root ctx node)
    collectedStyleHtml ctx + body

/// `renderWithCsp` with no host Custom-renderer registry — the strict-mode twin
/// of `render`.
let renderStrict (nonce: string) (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    renderWithCsp (Csp.Strict nonce) Registry.empty sources node

/// The theme block, the collected stylesheet and the body, in one emission
/// under one posture — the strict-mode twin of `renderWithTheme`.
///
/// Both `<style>` elements carry the same nonce, which is the whole shape a
/// host needs: one `style-src 'self' 'nonce-…'` directive
/// (`Csp.styleSrcDirective`) and no `'unsafe-inline'` anywhere.
let renderWithThemeAndCsp
    (csp: Csp.CspMode)
    (theme: Theme)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (themeStyleElementWithCsp csp theme)
    + renderWithCsp csp Registry.empty sources node

// ─── Served-stylesheet fingerprint (Phase 433) ─────────────────────────────
//
//  An SSR host serves its stylesheet from `Fuaran.UI.Renderer` and emits its
//  classes from THIS package. Nothing couples the two package versions, so a
//  host that pins one and serves the other's sheet renders unstyled or
//  mis-styled with no error anywhere — a shipped control appearing as a bare
//  browser input reads as a design choice, not as version skew. The reference
//  stylesheet carries a stamp naming the class vocabulary it was written
//  against; these three make that assertable at startup, where it is one line
//  and one restart rather than a bug report about a page looking wrong.
//
//  The host owns the assertion. Nothing here reads a file or fails a render:
//  the renderer does not know where a host's stylesheet comes from (a static
//  file, an embedded resource, a CDN it fetched at boot), and a check that
//  guessed would be checking the wrong bytes.
//
//  SCOPE — it fingerprints the class VOCABULARY, not the rules. A sheet whose
//  colours or token defaults were changed still matches, deliberately: it
//  answers "does this sheet know the classes this renderer emits", which is the
//  skew that silently breaks a control. A host that also wants byte identity
//  with the packaged sheet should hash it against the packaged copy instead.

/// The class-vocabulary fingerprint this renderer emits against — re-exported
/// from the shared spine so an SSR host asserts against the package it actually
/// emits with, rather than reaching across to the client renderer's.
let vocabularyFingerprint: string = Theme.vocabularyFingerprint

/// The fingerprint a stylesheet is stamped with, read from its header comment.
/// Takes the stylesheet TEXT, not a path — where a host's sheet comes from is
/// the host's business. `None` means the sheet carries no stamp at all: either
/// it predates the fingerprint or it is a consumer's own replacement sheet, and
/// a host must decide which of those it tolerates rather than have this guess.
let stylesheetFingerprint (css: string) : string option =
    let marker = Theme.vocabularyFingerprintMarker
    let at = css.IndexOf(marker, System.StringComparison.Ordinal)

    if at < 0 then
        None
    else
        let rest = css.Substring(at + marker.Length)

        let token =
            rest.Split([| ' '; '\t'; '\r'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)

        if token.Length = 0 then None else Some token[0]

/// Assert a served stylesheet was written against this renderer's class
/// vocabulary. `Error` carries a message naming both fingerprints and the
/// remedy — a host is expected to fail its own startup on it, loudly:
///
/// ```fsharp
/// match Render.checkStylesheet (File.ReadAllText servedCssPath) with
/// | Ok () -> ()
/// | Error message -> failwith message
/// ```
///
/// An UNSTAMPED sheet is an `Error` too, and that is the deliberate call: a host
/// that has opted into this check has said the served sheet is the packaged one,
/// and silence about a sheet that cannot be identified is exactly the outcome
/// the check exists to remove. A host serving its own replacement sheet does not
/// call this — the class hooks it implements are documented for that purpose.
let checkStylesheet (css: string) : Result<unit, string> =
    match stylesheetFingerprint css with
    | Some stamped when stamped = vocabularyFingerprint -> Ok()
    | Some stamped ->
        Error(
            sprintf
                "Fuaran stylesheet version skew: the served stylesheet is stamped with class vocabulary %s, but this renderer emits %s. The two packages disagree on what classes exist, so nodes will render unstyled or mis-styled with no other error. Serve the fuaran-reference.css shipped with the matching Fuaran.UI.Renderer version, or align the package versions."
                stamped
                vocabularyFingerprint
        )
    | None ->
        Error(
            sprintf
                "Fuaran stylesheet carries no `%s` stamp, so it cannot be checked against this renderer's class vocabulary (%s). Either it predates the fingerprint — align the package versions — or it is a replacement sheet, which should not be passed to this check."
                Theme.vocabularyFingerprintMarker
                vocabularyFingerprint
        )
