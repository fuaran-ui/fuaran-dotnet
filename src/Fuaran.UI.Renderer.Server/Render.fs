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

/// The `CellFormat` projection — `Formatting.formatNumber`, the one definition
/// both renderers format through (Phase 2108). `internal` for the same reason
/// as `renderText` above: the email and speech projections format a Metric /
/// LabelValueRow value through this name, so a figure in a digest reads
/// identically to the figure on the page.
let internal formatNumber (format: CellFormat) (value: float) : string = Formatting.formatNumber format value

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
    /// Attribute pairs as properties.
    let attrs (pairs: (string * string) list) : IReactProperty list =
        pairs |> List.map (fun (k, v) -> prop.custom (k, v))

    /// The boolean attributes this renderer wrote through the element
    /// library's boolean props before Phase 2278, which spell them
    /// `name="true"`; every other flag was a custom attribute with an empty
    /// value. Keeping both spellings keeps the served bytes.
    let private libraryBooleans =
        set [ "disabled"; "controls"; "loop"; "autoplay"; "muted" ]

    /// A planned property; a behaviour attachment point writes nothing — the
    /// served affordance is inert (Phase 2278).
    let private property (p: ElementPlan.Prop) : IReactProperty list =
        match p with
        | ElementPlan.Prop.Attr(name, value) -> [ prop.custom (name, value) ]
        | ElementPlan.Prop.Style declarations ->
            [ prop.style (declarations |> List.map (fun (k, v) -> style.custom (k, v))) ]
        | ElementPlan.Prop.Flag name when libraryBooleans.Contains name -> [ prop.custom (name, "true") ]
        | ElementPlan.Prop.Flag name -> [ prop.custom (name, "") ]
        | ElementPlan.Prop.Behaviour _ -> []

    /// Emit a plan.
    let rec plan (p: ElementPlan.Plan<ReactElement>) : ReactElement =
        match p with
        | ElementPlan.Plan.Host element -> element
        | ElementPlan.Plan.Nothing -> Html.none
        | ElementPlan.Plan.TextNode text -> Html.text text
        | ElementPlan.Plan.Element spec ->
            let props =
                [ yield! spec.Props |> List.collect property
                  match spec.Content with
                  | ElementPlan.Content.Empty -> ()
                  | ElementPlan.Content.Text text -> prop.text text
                  | ElementPlan.Content.Children children -> prop.children (children |> List.map plan)
                  | ElementPlan.Content.InnerHtml markup -> prop.dangerouslySetInnerHTML markup ]

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
        // result cell.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match
            ElementPlan.DisplayPlan.stateSlot
                parentNodeId
                (state |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour)
                resolution
        with
        | Some slot -> renderNode (Walk.deeper walk) ctx slot
        | None -> Emit.plan (ElementPlan.DisplayPlan.metric ctx.Sources resolution spec)
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
        let expandedKeyNamed = spec.ExpandedStateKey.IsSome
        let expanded = BindingResolver.readExpandedItems ctx.Sources spec.ExpandedStateKey
        let selected = BindingResolver.readSelectedItem ctx.Sources spec.SelectionStateKey

        let focusable =
            TreeModel.focusableTreeItem expandedKeyNamed expanded selected spec.Items

        let rec renderItems (level: int) (items: TreeItem list) : ReactElement list =
            let setSize = List.length items

            items
            |> List.mapi (fun i item ->
                let isOpen = TreeModel.treeItemExpanded expandedKeyNamed expanded item
                let hasChildren = not (List.isEmpty item.Children)
                let label = renderText ctx item.Label

                Html.li (
                    [ prop.className Css.Class.TreeItem
                      prop.role "treeitem"
                      // The accessible name is stated rather than computed from
                      // contents, and that is deliberate: a treeitem OWNS its
                      // child group, so a name derived from its subtree would
                      // read the whole branch out as the row's own name. The
                      // string is byte-identical to the visible label, so
                      // "label in name" holds.
                      prop.custom ("aria-label", label)
                      prop.custom ("aria-level", string level)
                      prop.custom ("aria-setsize", string setSize)
                      prop.custom ("aria-posinset", string (i + 1))
                      prop.custom ("data-fuaran-tree-item", item.Id)
                      prop.tabIndex (if Some item.Id = focusable then 0 else -1) ]
                    // `aria-expanded` is emitted ONLY on a row that has
                    // children. On a leaf the attribute would assert a
                    // collapsed subtree that does not exist, and assistive
                    // technology announces such a row as closed — which is a
                    // reader being told there is more when there is not.
                    @ (if hasChildren then
                           [ prop.custom ("aria-expanded", (if isOpen then "true" else "false")) ]
                       else
                           [])
                    // `aria-selected` likewise appears only where a selection
                    // key is named. Emitting `false` on every row of a tree
                    // that has no selection at all would declare a selectable
                    // widget with nothing selected, rather than a tree that
                    // does not select.
                    @ (match spec.SelectionStateKey with
                       | Some _ ->
                           [ prop.custom ("aria-selected", (if Some item.Id = selected then "true" else "false")) ]
                       | None -> [])
                    @ [ prop.children (
                            [ Html.span [ prop.className Css.Class.TreeLabel; prop.text label ] ]
                            @ (if isOpen then
                                   [ Html.ul
                                         [ prop.className Css.Class.TreeGroup
                                           prop.role "group"
                                           prop.children (renderItems (level + 1) item.Children) ] ]
                               else
                                   [])
                        ) ]
                ))

        Html.ul
            [ prop.className Css.Class.Tree
              prop.role "tree"
              prop.children (renderItems 1 spec.Items) ]
    | NodeKind.List spec -> Emit.plan (ElementPlan.DisplayPlan.list ctx.Sources spec)
    | NodeKind.Toast spec -> Emit.plan (ElementPlan.DisplayPlan.toast ctx.Sources spec)
    | NodeKind.CodeBlock spec -> Emit.plan (ElementPlan.DisplayPlan.codeBlock spec)
    | NodeKind.Math spec -> Emit.plan (ElementPlan.DisplayPlan.math spec)
    // -- Input --
    | NodeKind.Button spec ->
        let variantClass = Css.buttonVariant spec.Variant

        let isDisabled =
            spec.Disabled
            |> Option.bind (BindingResolver.tryResolve ctx.Sources)
            |> Option.defaultValue false

        // `data-fuaran-commit` (Phase 152 form policy): a button whose action is
        // an explicit per-field flush (`Action.CommitLocal fieldId`) carries the
        // committed field id so the server-driven shim harvests that buffered
        // field's value into the click payload (the "Apply" boundary, the
        // `OnCommitAction` analogue of submit). Server-tier-only marker.
        let commitProps =
            match spec.OnClick with
            | Action.CommitLocal fieldId -> [ prop.custom ("data-fuaran-commit", fieldId) ]
            | _ -> []

        Html.button (
            [ prop.className (Css.button variantClass)
              // The uniform icon hook: an icon-bearing button wraps its label
              // as a text node beside the hook; an icon-less button keeps the
              // plain `prop.text` shape (markup unchanged for existing trees).
              match spec.Icon with
              | Some icon ->
                  prop.children
                      [ Emit.plan (ElementPlan.DisplayPlan.iconHook Css.Class.ButtonIcon icon)
                        Html.text (renderText ctx spec.Label) ]
              | None -> prop.text (renderText ctx spec.Label) ]
            @ (match spec.Tooltip with
               | Some t -> [ prop.title (renderText ctx t) ]
               | None -> [])
            @ commitProps
            // Phase 951 — the a11y projection lands on the `<button>` itself.
            @ Emit.attrs semanticAttrs
            @ (if isDisabled then [ prop.disabled true ] else [])
        )
    | NodeKind.Select spec ->
        let options = BindingResolver.resolveOptions ctx.Sources spec.Source
        // The select value is `Binding<string>` since the swap — a null/empty
        // resolution is no-selection (the segmented-filter shape).
        let selected =
            BindingResolver.tryResolve ctx.Sources spec.Value
            |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)

        let isDisabled =
            spec.Disabled
            |> Option.bind (BindingResolver.tryResolve ctx.Sources)
            |> Option.defaultValue false

        let placeholderItem =
            match spec.Placeholder with
            | Some placeholder -> [ Html.option [ prop.value ""; prop.text (renderText ctx placeholder) ] ]
            | None -> []

        let optionItems =
            [ for option in options -> Html.option [ prop.value option.Value; prop.text option.Label ] ]

        Html.label
            [ prop.className Css.Class.Select
              prop.children
                  [ Html.span [ prop.className Css.Class.SelectLabel; prop.text (renderText ctx spec.Label) ]
                    Html.select (
                        [ prop.className Css.Class.SelectControl ]
                        // Phase 291 — emit `multiple` for a multi-select; the
                        // single-value `value` only when single-select (a
                        // controlled `<select multiple>` rejects a scalar value).
                        @ (if spec.Multiple = Some true then
                               [ prop.custom ("multiple", "") ]
                           else
                               [ prop.value (selected |> Option.defaultValue "") ])
                        @ (if isDisabled then [ prop.disabled true ] else [])
                        @ [ prop.children (placeholderItem @ optionItems) ]
                    ) ] ]
    | NodeKind.Form spec ->
        let fieldNodes = spec.Fields |> List.map (renderFormField ctx)

        let submitNode =
            Html.button
                [ prop.className Css.Class.FormSubmit
                  prop.custom ("type", "submit")
                  prop.text (renderText ctx spec.SubmitLabel) ]

        let body = fieldNodes @ [ submitNode ]

        let formChildren =
            match spec.Disabled with
            | Some disabled ->
                let isDisabled =
                    BindingResolver.tryResolve ctx.Sources disabled |> Option.defaultValue false

                [ Html.fieldSet (
                      [ prop.className Css.Class.FormFieldset ]
                      @ (if isDisabled then [ prop.disabled true ] else [])
                      @ [ prop.children body ]
                  ) ]
            | None -> body

        Html.form [ prop.className Css.Class.Form; prop.children formChildren ]
    | NodeKind.Filters specs ->
        Html.div
            [ prop.className Css.Class.Filters
              prop.children [ for spec in specs.Items -> renderFilterSpec ctx spec ] ]
    | NodeKind.FileUpload spec ->
        // Phase 1115 — the SSR floor for `dropTarget` / `acceptPaste` is the
        // PLAIN PICKER, and that is a decision rather than an omission. Both
        // routes need script: there is no drop without a `drop` listener and no
        // paste without a `paste` one, and a CSS-only drop state does not exist
        // (`:hover` does not fire during a drag). Emitting drop-zone markup a
        // no-script host could never wire would be a fake affordance — an
        // invitation the document cannot honour — so the flags degrade to the
        // control the picker always was, which is a fully working upload.
        //
        // What DOES ride is a marker per declared gesture, on the Phase 1113
        // `data-fuaran-combobox-constrained` precedent: it records that the
        // declaration was READ rather than silently dropped, and it is
        // explicitly NOT claimed as coverage — nothing in this tier acts on it.
        // Phase 1117 — the declared upload DESTINATION takes the read-marker
        // shape too, and it is worth saying why it is a marker and not the
        // destination itself. A transfer needs script — there is no streaming
        // without a change listener and a sink — so this tier's floor is the
        // plain picker, exactly as it is for the two gestures above.
        //
        // The marker deliberately records only THAT a destination was declared,
        // never WHICH. The id is the host's registry key, and a static document
        // is a thing anybody can read: putting the key in the markup would
        // publish the host's registry to every reader for no gain, since
        // nothing on this path can act on it. A host that hydrates has the
        // decoded tree and reads the destination from there.
        let gestureAttrs =
            [ if spec.DropTarget then
                  prop.custom ("data-fuaran-upload-drop", "declared")
              if spec.AcceptPaste then
                  prop.custom ("data-fuaran-upload-paste", "declared")
              if spec.Destination.IsSome then
                  prop.custom ("data-fuaran-upload-destination", "declared")
              // Phase 1548 — one marker per declared ceiling, on exactly the
              // shape above. This tier's floor for both is the plain picker,
              // and that is a decision rather than an omission: HTML has no
              // attribute for a byte ceiling at all, and `multiple` is a
              // boolean rather than a count, so there is nothing a no-script
              // host could enforce. Emitting markup that claimed otherwise
              // would be a fake affordance.
              //
              // The markers record only THAT a ceiling was declared, never its
              // VALUE — and unlike the destination marker above, that is not a
              // privacy argument. It is the same posture for a different
              // reason: nothing on this path can act on the number, so putting
              // it in the markup would invite a reader to believe this tier
              // enforces it. A host that hydrates has the decoded tree and
              // reads both ceilings from there, which is where the client
              // renderer's floor and the G1 gate take them.
              if spec.MaxBytes.IsSome then
                  prop.custom ("data-fuaran-upload-max-bytes", "declared")
              if spec.MaxFiles.IsSome then
                  prop.custom ("data-fuaran-upload-max-files", "declared") ]

        // Phase 1116 — `capture` is the one declaration on this control whose
        // floor FULLY holds, and it is emitted as the attribute rather than as a
        // read-marker for exactly that reason. It needs no listener and no
        // script: the user agent reads it off the static markup, opens the
        // camera or the recorder on a device that has one, and shows the file
        // browser on one that does not. That is the whole of the behaviour, so
        // this tier degrades nowhere and the two renderers emit the same pair.
        //
        // `accept` rides with it, and must. The device is selected by `accept`,
        // not by the keyword — a `capture` with no filter beside it asks the
        // user agent to guess which device, which is the uncertainty the
        // declaration exists to remove. Emitting one without the other would
        // make this tier's floor hold only by accident. (`accept` was absent
        // here before this phase, which no test caught because nothing paired
        // the two; the client renderer has emitted it since Phase 130.)
        //
        // SANITIZATION.md: both are ordinary attribute VALUES, which the view
        // engine escapes — no URL, no markup and no event seam. `capture`'s
        // value is a closed-enum keyword and never author data at all; `accept`
        // carries the same author strings the client renderer has emitted as an
        // attribute value since Phase 130, at the same seam.
        let acceptAttr =
            if List.isEmpty spec.Accept then
                []
            else
                [ prop.custom ("accept", String.concat "," spec.Accept) ]

        let captureAttr =
            match spec.Capture with
            | Some source -> [ prop.custom ("capture", Fuaran.UI.MediaCapture.keyword source) ]
            | None -> []

        Html.label (
            [ prop.className Css.Class.FileUpload ]
            @ gestureAttrs
            @ [ prop.children
                    [ Html.span
                          [ prop.className Css.Class.FileUploadLabel
                            prop.text (renderText ctx spec.Label) ]
                      Html.input (
                          [ prop.className Css.Class.FileUploadInput; prop.custom ("type", "file") ]
                          @ acceptAttr
                          @ captureAttr
                      ) ] ]
        )
    // -- Vis --
    | NodeKind.DataGrid spec ->
        // Phase 1473 — the grid's own two declarations, on the same shared
        // helper the client renderer uses. They ride BOTH legs: the static
        // table, where the rows and the header row group are real elements the
        // print rules reach, and the hydration placeholder, so the declaration
        // is not silently dropped on the bound path.
        let gridBrk = Theme.gridPrintBreakClasses spec.KeepRowsTogether spec.RepeatHeader

        match spec.StaticRows with
        | Some sr ->
            // Phase 393 — static read-only mode: SSR renders the full semantic <table> statically
            // (byte-identical to the retired Table), NOT a hydration placeholder.
            let headerCells =
                [ for h in sr.Headers -> Html.th [ prop.className Css.Class.TableHeader; prop.text (renderText ctx h) ] ]

            let bodyRows =
                [ for row in sr.Rows ->
                      Html.tr
                          [ prop.className Css.Class.TableRow
                            prop.children
                                [ for cell in row ->
                                      Html.td [ prop.className Css.Class.TableCell; prop.text (renderText ctx cell) ] ] ] ]

            Html.table
                [ prop.className (Css.Class.Table + gridBrk)
                  // Phase 801 — the declared sort intent, surfaced as data attributes so the
                  // reference table enhancement honours it without re-parsing wire. Emitted
                  // ONLY when declared: an undeclared table's SSR bytes are unchanged.
                  match sr.Sortable with
                  | Some sortable -> prop.custom ("data-fuaran-sortable", if sortable then "true" else "false")
                  | None -> ()
                  match sr.DefaultSort with
                  | Some ds ->
                      prop.custom ("data-fuaran-sort-column", string ds.Column)

                      prop.custom (
                          "data-fuaran-sort-direction",
                          match ds.Direction with
                          | SortDirection.Asc -> "asc"
                          | SortDirection.Desc -> "desc"
                      )
                  | None -> ()
                  prop.children [ Html.thead [ Html.tr headerCells ]; Html.tbody bodyRows ] ]
        | None ->
            // Client-library grid. SSR emits a deterministic placeholder carrying a
            // row-count data attribute for later hydration (never a blank).
            let rowCount =
                BindingResolver.tryResolve ctx.Sources spec.Source
                |> Option.map Seq.length
                |> Option.defaultValue 0

            Html.div
                [ prop.className ("fuaran-grid fuaran-grid-ssr-placeholder" + gridBrk)
                  prop.custom ("data-fuaran-ssr-placeholder", "DataGrid")
                  prop.custom ("data-fuaran-row-count", string rowCount)
                  prop.text (sprintf "[Grid: %d rows — hydrates client-side]" rowCount) ]
    | NodeKind.Chart spec ->
        let resolution = BindingResolver.resolve<Row seq> ctx.Sources spec.Source

        // Phase 2041 — the state slots, as the client's chart arm reads them:
        // an unresolved source with `OnLoading` wired renders the loading node,
        // an errored source with `OnError` wired renders the node it builds
        // from a `BindingResolution` payload whose correlation id is the
        // client's (a hash of the chart's node id). Otherwise the chart body.
        match resolution, (state |> Option.bind _.OnLoading), (state |> Option.bind _.OnError) with
        | BindingResolver.NotResolved, Some loadingNode, _ -> renderNode (Walk.deeper walk) ctx loadingNode
        | BindingResolver.Errored msg, _, Some errorFn ->
            renderNode
                (Walk.deeper walk)
                ctx
                (errorFn
                    { Kind = ErrorKind.BindingResolution
                      Message = msg
                      CorrelationId = Ids.deterministicCorrelationId parentNodeId })
        | _ ->
            match resolution, spec.Kind with
            | BindingResolver.Resolved rows, kind when Fuaran.UI.Charts.isLowered kind ->
                // Phase 526 — the SSR renders the SAME first-party lowered Drawing
                // SVG the client does (static geometry ⇒ no client-hydration
                // placeholder for a lowered kind; SSR ↔ CSR byte-parity via the
                // shared lowering + Drawing builder). The lowered-kind set is
                // `Charts.isLowered` — one source of truth with the client branch.
                //
                // Phase 643 — the emission goes through `Charts.renderSvg`, the
                // single entry point the CLIENT arm also calls, so the provenance
                // scope threads through both tiers identically by construction
                // rather than by two call sites kept in step. The host-installed
                // scope ships `Off`, so these SSR bytes are unchanged.
                Html.div
                    [ prop.dangerouslySetInnerHTML (Fuaran.UI.Charts.renderSvg ctx.Sources (renderText ctx) spec rows) ]
            | resolution, _ ->
                // Unresolved data, or a not-yet-lowered kind (Heatmap): the
                // client-hydration placeholder.
                let rowCount =
                    match resolution with
                    | BindingResolver.Resolved seq -> Seq.length seq
                    | _ -> 0

                Html.div
                    [ prop.className "fuaran-chart fuaran-chart-ssr-placeholder"
                      prop.custom ("data-fuaran-ssr-placeholder", "Chart")
                      prop.custom ("data-fuaran-row-count", string rowCount)
                      prop.children
                          [ match spec.Title with
                            | Some title ->
                                Html.div [ prop.className Css.Class.ChartTitle; prop.text (renderText ctx title) ]
                            | None -> Html.none
                            Html.div
                                [ prop.className Css.Class.ChartPlaceholder
                                  prop.text (sprintf "[Chart: %d rows — hydrates client-side]" rowCount) ] ] ]
    | NodeKind.Map spec ->
        let markerCount =
            BindingResolver.tryResolve ctx.Sources spec.Source
            |> Option.map Seq.length
            |> Option.defaultValue 0

        Html.div
            [ prop.className "fuaran-map fuaran-map-ssr-placeholder"
              prop.custom ("data-fuaran-ssr-placeholder", "Map")
              prop.custom ("data-fuaran-marker-count", string markerCount)
              prop.text (sprintf "[Map: %d markers — hydrates client-side]" markerCount) ]
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

// ─── Inputs (rendered inert — no dispatch server-side) ──────────────────────

/// A form field rendered inert: the labelled control with the correct class
/// vocabulary. Interactive binding (onChange) is a client-hydration concern.
and private renderFormField (ctx: ServerRenderContext) (field: FormField<obj>) : ReactElement =
    // Phase 596 — the value slots are `Binding<_> option` since the swap. An
    // absent (`None`) slot is the auto-bind form: substitute exactly the
    // binding the decoder used to synthesise — `Binding.State(field.Id,
    // <typed placeholder from Defaults.ControlValueDefaults>)` — so SSR
    // resolution behaves identically to the pre-swap decoded shape.
    let controlType, valueText =
        match field.Kind with
        | FormFieldKind.Text(FieldView.Text v, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.text))

            // Phase 864 — a declared `format` rule retypes the control so the
            // BROWSER enforces it (`<input type=email|url|tel>`). Absent, the
            // control stays `text` and the emitted markup is byte-identical to
            // a pre-864 tree. One mapping, shared with the client (`FieldRules`).
            FieldRules.textInputType field.Rule, (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.Number(FieldView.Number v, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.number))

            "number",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.map string
             |> Option.defaultValue "")
        | FormFieldKind.RangedNumber(FieldView.Number v, _, _, _, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.number))

            "number",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.map string
             |> Option.defaultValue "")
        | FormFieldKind.Range _ -> "range", ""
        | FormFieldKind.Checkbox _ -> "checkbox", ""
        | FormFieldKind.Toggle _ -> "toggle", ""
        | FormFieldKind.TextArea(FieldView.Text v, _, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.text))

            "textarea", (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.Choice _ -> "choice", ""
        | FormFieldKind.SegmentedChoice _ -> "segmented-choice", ""
        | FormFieldKind.Combobox(_, _, _, FieldView.Text v) ->
            // Phase 1113 — the combobox's own branch below builds the control;
            // this tuple carries the resolved entry text so the SSR floor ships
            // a filled input rather than an empty one.
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.combobox))

            // Phase 1648 — the NULL COLLAPSE this arm was missing, and the one
            // place in this renderer that lacked it.
            //
            // `ControlValueDefaults.combobox` is `None` — deliberately, because
            // "no selection" is what a combobox's absent value means and the
            // codec's collapse keys off exactly that — so the auto-bound
            // placeholder is `Binding.State(id, None)`. `BindingResolver.resolve`
            // answers a default-less `State` whose key is absent with
            // `Resolved Unchecked.defaultof<'T>`, which for a `string` slot is
            // NULL, so `tryResolve` returned `Some null` and `Option.defaultValue`
            // — which only replaces `None` — passed it straight through to
            // `prop.value`. Feliz.ViewEngine's `mkAttr` then threw a
            // NullReferenceException, and the whole document failed to render on
            // the commonest shape the docs teach: a `Combobox` field with no
            // value binding.
            //
            // The client renderer has guarded this since Phase 1113 (`if isNull s
            // || s = "" then None`), and so do the three other resolutions in
            // THIS file — the filter twin among them. This arm was the omission,
            // which is why the defect reached a shipped 0.70.0 while every
            // neighbouring control was fine.
            //
            // Fixed HERE rather than at the resolver: making a default-less
            // `State` resolve to a slot-typed empty instead of the CLR default
            // would change what "absent" means for every host and every slot
            // type, which is a wire-semantics decision and not this phase's to
            // take. Recorded as a finding beside the phase.
            "combobox",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.bind (fun s -> if isNull s then None else Some s)
             |> Option.defaultValue "")
        // Phase 1130 — the rating's own branch below builds the control (a star
        // row or a radio group, per what the document can honour), so this
        // tuple carries only the discriminator.
        | FormFieldKind.Rating _ -> "rating", ""
        // Phase 1130 — the colour control needs no branch of its own: a native
        // `<input type="color">` IS the control, and the generic input arm
        // below emits exactly that from this tuple. A value the input could not
        // hold falls back to the unset black rather than being passed through,
        // for the client renderer's reason — a native colour input substitutes
        // its own default silently, so handing it a bad literal would show a
        // colour the document did not choose.
        | FormFieldKind.Color(_, FieldView.Text v) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.color))

            "color",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.filter Fuaran.UI.HostPrelude.HexColor.isValid
             |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.color)
        // Phase 1121 — THE SSR FLOOR FOR A TOKEN FIELD IS ONE TEXT INPUT, and
        // it is not an approximation of the client's chip row so much as the
        // only honest thing this medium can render. A chip row is BUILT by a
        // keystroke handler: zero-JS there is no gesture that adds a chip, no
        // gesture that removes one, and a row of static chips with dead remove
        // buttons would be an affordance the markup cannot honour — which is
        // the failure the whole SSR family exists to avoid. So the floor is a
        // single `<input type="text">` carrying the tokens comma-separated,
        // which a reader CAN edit and which submits with the form.
        //
        // The separator lives in `TokensModel` and not here, so the projection
        // and its inverse cannot drift apart.
        //
        // TWO RECORDED KNOWN LIMITS, neither claimed as coverage.
        //   1. A token CONTAINING A COMMA cannot survive the projection — it
        //      re-parses as two. Escaping it would put a quoting grammar into a
        //      degraded medium that no reader can see, which trades a visible
        //      limit for an invisible one.
        //   2. `allowFreeText = false` is NOT enforced here and cannot be: a
        //      text input has no native membership constraint. The declaration
        //      rides as `data-fuaran-tokens-constrained` so a reader can see it
        //      was not silently dropped, on the `data-fuaran-combobox-constrained`
        //      precedent — nothing in the platform reads it. Enforcement is the
        //      server-side re-check in `Fuaran.UI.ServerDriven.FormValidation`,
        //      which is where a constraint a client can type past belongs.
        | FormFieldKind.Tokens(_, _, _, FieldView.Tokens v) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.tokens))

            "text",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.tokens
             |> TokensModel.toCommaSeparated)
        | FormFieldKind.DateTime(FieldView.Text v, _, variant, _, _, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.dateTime))

            let t =
                match variant with
                | DateTimeVariant.Date -> "date"
                | DateTimeVariant.Time -> "time"
                | DateTimeVariant.DateTime -> "datetime-local"

            t, (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.DateTimeRange(_, _, variant, _, _, _) ->
            // Phase 725 — the variant types BOTH ends; the pair itself is
            // rendered by the dedicated dual-input branch below, so the
            // single-control tuple carries only the type.
            let t =
                match variant with
                | DateTimeVariant.Date -> "date"
                | DateTimeVariant.Time -> "time"
                | DateTimeVariant.DateTime -> "datetime-local"

            t, ""

    // `data-fuaran-field` is the per-field buffer marker (Phase 152 form policy):
    // the server-driven shim reads + harvests it (the live DOM value IS the
    // client-side buffer — policy (b)), and the Phase 156 field-error patch
    // addresses it. Additive, server-tier-only — the same shim-bridge pattern as
    // `data-tab-index` / `data-filter-name` / `data-step-index`.
    let control =
        match field.Kind with
        // Phase 1113 — THE SSR FLOOR FOR A COMBOBOX, and the whole point of it
        // is that it is not an approximation of the client's ARIA widget: a
        // native `<input list>` bound to a `<datalist>` IS a combobox to the
        // user agent, which supplies the popup, the filtering, the keyboard
        // interaction and the accessibility semantics itself, with no script.
        //
        // NO HAND-WRITTEN ARIA IS EMITTED HERE, deliberately. Adding
        // `role="combobox"` + `aria-expanded` to a `<input list>` would REPLACE
        // the user agent's own correct semantics with a static claim this inert
        // markup can never keep true — an `aria-expanded="false"` that never
        // becomes `true` is worse than the native role it overwrote. The client
        // tier owns the full pattern precisely because it owns the state that
        // makes those attributes honest.
        //
        // RECORDED KNOWN LIMIT — `allowFreeText = false` is NOT enforced here,
        // and cannot be: a `<datalist>` is a suggestion list, not a constraint,
        // and HTML offers no native membership check for one. The declaration is
        // emitted as `data-fuaran-combobox-constrained` so a reader can see it
        // was not silently dropped, and it is NOT claimed as coverage — nothing
        // in the platform reads that attribute. The enforcement is the
        // server-side re-check (`Fuaran.UI.ServerDriven.FormValidation`), which
        // is where a constraint that a client can type past belongs anyway.
        // Phase 1130 — THE SSR FLOOR FOR A RATING, and it is deliberately not one
        // markup but two, chosen by what the document can honour.
        //
        // A rating that CANNOT be written (no handler, and a value binding the
        // write-back default cannot reach — the bound-average display case) has
        // no interaction to floor, so this emits the IDENTICAL `role="img"`
        // star row the client renders, from the same `RatingModel.fills`. The
        // two tiers agree byte-for-byte because there is nothing for hydration
        // to add.
        //
        // A WRITABLE rating floors on native RADIOS. Zero-JS, a
        // `<span role="slider">` can be neither adjusted nor submitted; radios
        // are keyboard-adjustable and submit with the form, and the user agent
        // supplies the group semantics itself. NO HAND-WRITTEN ARIA IS EMITTED
        // — the combobox arm's rule, for its reason: a static `aria-valuenow`
        // that can never change is worse than the native semantics it replaced.
        //
        // RECORDED KNOWN LIMIT — a writable rating whose current value is a
        // FRACTION that lands on no enterable position (a bound 4.3 the reader
        // may then overwrite) checks no radio here. The floor shows the
        // positions a reader can choose, not the average; the exact figure
        // rides as `data-fuaran-rating-value` so it is visibly not dropped, and
        // it is NOT claimed as coverage — nothing in the platform reads that
        // attribute. Hydration restores the fraction.
        | FormFieldKind.Rating(allowHalf, max, FieldView.OnNumber onChange, FieldView.Number value) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.rating))

            let current =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.rating

            let shown = RatingModel.clamp max current

            let starRow =
                [ for index, fill in List.indexed (RatingModel.fills max shown) ->
                      Html.span
                          [ prop.key index
                            prop.className ("fuaran-rating-star " + RatingModel.fillClass fill)
                            prop.custom ("aria-hidden", "true") ] ]

            if onChange.IsNone && not (Fuaran.UI.BindingWalk.isWriteBackTarget value) then
                Html.span
                    [ prop.className "fuaran-form-field-control fuaran-rating fuaran-rating-static"
                      prop.custom ("role", "img")
                      prop.custom ("aria-label", RatingModel.valueText max shown)
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.children starRow ]
            else
                let positions = int (System.Math.Round(float max / RatingModel.step allowHalf))

                Html.span
                    [ prop.className "fuaran-form-field-control fuaran-rating fuaran-rating-choices"
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.custom ("data-fuaran-rating-value", RatingModel.valueText max shown)
                      prop.children (
                          starRow
                          @ [ for i in 1..positions do
                                  let target = RatingModel.snap allowHalf max (float i * RatingModel.step allowHalf)

                                  let radio =
                                      Html.input
                                          [ prop.custom ("type", "radio")
                                            prop.custom ("name", field.Id)
                                            prop.value (string target)
                                            if abs (target - shown) < 1e-9 then
                                                prop.custom ("checked", true) ]

                                  let caption =
                                      Html.span
                                          [ prop.className "fuaran-rating-choice-label"
                                            prop.text (RatingModel.valueText max target) ]

                                  Html.label [ prop.className "fuaran-rating-choice"; prop.children [ radio; caption ] ] ]
                      ) ]
        | FormFieldKind.Combobox(allowFreeText, _, options, _) ->
            let listId = field.Id + "-options"
            let opts = BindingResolver.resolveOptions ctx.Sources options

            Html.span
                [ prop.className "fuaran-combobox"
                  prop.children
                      [ Html.input
                            [ prop.className Css.Class.FormFieldControlComboboxInput
                              prop.custom ("data-fuaran-field", field.Id)
                              prop.custom ("type", "text")
                              prop.custom ("list", listId)
                              // The browser's own history dropdown would compete
                              // with the datalist popup for the same gesture.
                              prop.custom ("autocomplete", "off")
                              prop.custom (
                                  "data-fuaran-combobox-constrained",
                                  (if allowFreeText then "false" else "true")
                              )
                              prop.required field.Required
                              prop.value valueText ]
                        Html.datalist
                            [ prop.id listId
                              prop.children
                                  [ for option in opts ->
                                        Html.option [ prop.value option.Value; prop.text option.Label ] ] ] ] ]
        // Phase 1121 — the token floor. `valueText` above already carries the
        // comma-separated projection; this branch exists to emit the two things
        // the generic input arm cannot: the declared constraint marker, and the
        // `<datalist>` when a suggestion source resolves. The datalist is a real
        // gain here rather than decoration — it lets the user agent suggest
        // tokens as the reader types the comma-separated string, with no script,
        // which is the same trade the combobox floor makes one branch above.
        | FormFieldKind.Tokens(allowFreeText, _, suggestions, _) ->
            let listId = field.Id + "-suggestions"

            let opts = suggestions |> Option.map (BindingResolver.resolveOptions ctx.Sources)

            Html.span
                [ prop.className "fuaran-tokens"
                  prop.children
                      [ Html.input
                            [ prop.className Css.Class.FormFieldControlTokensInput
                              prop.custom ("data-fuaran-field", field.Id)
                              prop.custom ("type", "text")
                              if Option.isSome opts then
                                  prop.custom ("list", listId)
                              // The browser's own history dropdown would compete
                              // with the datalist popup for the same gesture.
                              prop.custom ("autocomplete", "off")
                              // Declared, never claimed — see the limits recorded
                              // at the `controlType` branch above.
                              prop.custom (
                                  "data-fuaran-tokens-constrained",
                                  (if allowFreeText then "false" else "true")
                              )
                              prop.required field.Required
                              prop.value valueText ]
                        match opts with
                        | Some options ->
                            Html.datalist
                                [ prop.id listId
                                  prop.children
                                      [ for option in options ->
                                            Html.option [ prop.value option.Value; prop.text option.Label ] ] ]
                        | None -> () ] ]
        | FormFieldKind.DateTimeRange(FieldView.DateRange v, _, _, mn, mx, st) ->
            // Phase 725 — SSR parity with the client's dual-input range: two
            // native date/time inputs (per variant) over the pair's ends,
            // sharing the min/max/step attributes. Inert like every other
            // server-rendered control; the FROM end carries the per-field
            // buffer marker (the pair's addressable slot).
            let v =
                v
                |> Option.defaultValue (
                    Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.dateTimeRange)
                )

            // The pair is the `DateTimeRangePair` record since the swap (was a
            // `(from, to)` tuple); same defaults, same emitted markup.
            let current: DateTimeRangePair =
                BindingResolver.tryResolve ctx.Sources v
                |> Option.defaultValue { From = ""; To = "" }

            let fromV, toV = current.From, current.To

            let constraintAttrs =
                [ match mn with
                  | Some m -> prop.custom ("min", m)
                  | None -> ()
                  match mx with
                  | Some m -> prop.custom ("max", m)
                  | None -> ()
                  match st with
                  | Some s -> prop.custom ("step", string s)
                  | None -> () ]

            Html.span
                [ prop.className Css.Class.FieldRange
                  prop.children
                      [ Html.input (
                            [ prop.className "fuaran-form-field-control fuaran-field-range-min"
                              prop.custom ("data-fuaran-field", field.Id)
                              prop.custom ("type", controlType)
                              prop.value fromV ]
                            @ constraintAttrs
                        )
                        Html.span [ prop.className Css.Class.FieldRangeSep; prop.text "–" ]
                        Html.input (
                            [ prop.className "fuaran-form-field-control fuaran-field-range-max"
                              prop.custom ("type", controlType)
                              prop.value toV ]
                            @ constraintAttrs
                        ) ] ]
        // Phase 1605 — THE SSR FLOOR FOR A TOGGLE, closing the first half of
        // the asymmetry Phase 1591 declared as data rather than smoothed over.
        // Until now a form toggle fell through to the generic input arm below,
        // which emitted `type="toggle"` — a type no user agent knows, so the
        // browser degraded it to a text box — and no role at all: a screen
        // reader on the static floor heard a text field where the hydrated page
        // announces on/off. The FILTER twin has emitted the pair on both
        // pipelines since Phase 766; this is the same emission, for the same
        // boolean data, on the field side.
        //
        // Built on a native checkbox for the client arm's reason: keyboard
        // operation (Space) and focus come from the platform, where an ARIA role
        // on a non-interactive element would have to reimplement both — the
        // usual way a hand-rolled switch becomes unusable.
        //
        // `aria-checked` is RESOLVED here rather than deferred to hydration. A
        // switch announced `false` on first paint and corrected later is
        // announced wrongly to the reader who is already there, and for a
        // static (never-hydrated) render it would never be corrected at all.
        // The control stays inert like every other server-rendered field
        // (`RichTier.Behavioural`): no handler is wired, and the per-field
        // buffer marker is unchanged, so the shim harvests `el.checked` where
        // it previously read a text box that could hold no boolean.
        | FormFieldKind.Toggle(FieldView.Bool value, _) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className (Css.formControl "toggle")
                  prop.custom ("data-fuaran-field", field.Id)
                  prop.custom ("type", "checkbox")
                  prop.custom ("role", "switch")
                  prop.custom ("aria-checked", (if current then "true" else "false"))
                  if current then
                      prop.custom ("checked", "checked") ]
        // Phase 1648 — THE SSR FLOOR FOR A CHOICE IS A `<select>`, which is what
        // the client has always rendered and what the markup has to be.
        //
        // The field arm fell through to the generic input arm and emitted
        // `<input type="choice">`. `choice` is not an HTML input type, so every
        // browser fell back to `type="text"` per the spec's invalid-value
        // default: a no-script reader met a free-text box where the hydrated
        // page shows a dropdown, could type anything at all, and submitted a
        // value no option offered. The SegmentedChoice arm above was given its
        // own floor in Phase 1605 for exactly this reason; this is its twin, and
        // it was left behind.
        //
        // Mirrors the client's shape, including the leading `—` placeholder
        // option that makes "no selection" selectable rather than merely
        // initial. Inert like every other server-rendered control: the
        // per-field buffer marker is the only wiring, and `harvestFields` reads
        // `el.value` off a `<select>` exactly as it does off an `<input>`.
        | FormFieldKind.Choice(options, FieldView.Text value, _) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.choice))

            // The default-less auto-bind resolves `Unchecked.defaultof<string>`
            // — null — so the guard is the same one the client applies and the
            // same one the Combobox arm above needed.
            let current =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)

            let optionItems =
                Html.option
                    [ prop.value ""
                      prop.text "—"
                      if current.IsNone then
                          prop.custom ("selected", "selected") ]
                :: [ for option in BindingResolver.resolveOptions ctx.Sources options ->
                         Html.option
                             [ prop.value option.Value
                               prop.text option.Label
                               if current = Some option.Value then
                                   prop.custom ("selected", "selected") ] ]

            Html.select
                [ prop.className (Css.formControl "select")
                  prop.custom ("data-fuaran-field", field.Id)
                  prop.required field.Required
                  prop.children optionItems ]
        // Phase 1605 — THE SSR FLOOR FOR A SEGMENTED CHOICE, the second half of
        // that asymmetry. The field arm fell through to the generic input arm
        // too and emitted `type="segmented-choice"`, so a no-script reader met
        // one text box where the hydrated page renders a `radiogroup` of
        // options (Horizontal) or a fieldset of native radios (Vertical). It now
        // shares the server's own segmented core with the FILTER arm, which has
        // emitted those roles since the filters unification: ONE shape, two
        // callers, so the two floors can no longer drift apart the way the
        // fidelity table recorded them doing.
        //
        // The CONTAINER carries the per-field buffer marker rather than each
        // option — the writable-Rating precedent above, where the interactive
        // children sit inside the marked container. No per-option shim marker is
        // emitted: `data-filter-value` is what bridges a filter CLICK to a
        // value, and wiring an interaction is not what this phase ships. The
        // floor stays inert, and the selection is still visible to a reader and
        // to assistive technology through `aria-checked` / the checked radio.
        | FormFieldKind.SegmentedChoice(options, FieldView.Text value, _, orientation) ->
            let value =
                value
                |> Option.defaultValue (Binding.State(field.Id, Fuaran.UI.Defaults.ControlValueDefaults.choice))

            renderSegmentedChoiceCore ctx field.Id "data-fuaran-field" None options value orientation
        | _ ->
            // Phase 864 — a static emitter MUST project a declared rule into
            // the platform's OWN constraint attributes, so the platform (here,
            // the browser receiving this HTML) is what enforces it: `pattern`
            // carries ECMA-262 source with HTML `pattern` semantics, and
            // `minlength` / `maxlength` are the native length bounds. `type` is
            // handled above, at the `controlType` computation.
            //
            // RECORDED KNOWN LIMIT — `compare` has NO HTML equivalent. It is
            // emitted as a `data-fuaran-field-compare` DECLARATION (matching the
            // client renderer's marker) so a reader can see the constraint was
            // not silently dropped, and it is NOT claimed as coverage: nothing
            // in the platform reads that attribute, and this emitter produces
            // inert markup with no gate of its own. A cross-field comparison is
            // enforced by a rendering host's submit gate and, non-bypassably, by
            // the server-side re-check (`Fuaran.UI.ServerDriven.FormValidation`).
            let ruleAttrs =
                match field.Rule with
                | None -> []
                | Some r ->
                    [ match r.Pattern with
                      // `<textarea>` has no `pattern` attribute in HTML — emit
                      // nothing rather than an attribute the platform ignores.
                      | Some p when controlType <> "textarea" -> prop.custom ("pattern", p)
                      | _ -> ()
                      match r.MinLength with
                      | Some n -> prop.custom ("minlength", string n)
                      | None -> ()
                      match r.MaxLength with
                      | Some n -> prop.custom ("maxlength", string n)
                      | None -> ()
                      match r.Compare with
                      | Some c -> prop.custom ("data-fuaran-field-compare", FieldRules.compareDeclaration c)
                      | None -> () ]

            // Phase 1648 — the per-control class, from the SHARED mapping the
            // client's split defines (`Css.formControl`). This arm emitted the
            // coarse `fuaran-form-field-control` for every plain control while
            // the client emitted `fuaran-form-input` / `-textarea` /
            // `-checkbox`, plus the `fuaran-form-date` modifier on the date
            // family. The Phase 431 coverage test aliased the STYLING
            // consequence through declared absences, so the sheet stayed
            // correct and the divergence stood — and a host selecting on the
            // class the client gave it got a different one from the server.
            match controlType with
            | "textarea" ->
                Html.textarea (
                    [ prop.className (Css.formControl controlType)
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.value valueText ]
                    @ ruleAttrs
                )
            | _ ->
                Html.input (
                    [ prop.className (Css.formControl controlType)
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.custom ("type", controlType)
                      prop.value valueText ]
                    @ ruleAttrs
                )

    Html.label
        [ prop.className Css.Class.FormField
          prop.children
              [ Html.span
                    [ prop.className "fuaran-form-field-label"
                      prop.text (renderText ctx field.Label) ]
                control
                // Phase 1648 — `FormField.Help` was DROPPED here entirely. The
                // client has rendered it as `fuaran-form-help` since the slot
                // existed, so a document declaring help text showed it once the
                // page hydrated and never on the static floor — which is the
                // reader least likely to be able to work the control without it.
                // The same element, the same class, in the same position.
                match field.Help with
                | Some help -> Html.div [ prop.className Css.Class.FormHelp; prop.text (renderText ctx help) ]
                | None -> Html.none ] ]

/// A filter rendered with its real control markup (the client renderer's
/// class vocabulary), inert server-side like every other input. Each control
/// carries `data-filter-name` so the live shim can bridge the changed/clicked
/// filter's identity across as `payload.name` (the server-driven Filters
/// resolution is name-addressed — one Filters node holds many filters).
and private renderFilterSpec (ctx: ServerRenderContext) (spec: FilterSpec<obj>) : ReactElement =
    // 0.2.0 filters-unification: the chip's control is an ordinary
    // FormFieldKind; class vocabulary keeps the four legacy filter families
    // for the shapes that map onto them, with sensible classes for the rest.
    // Phase 1648 - the chip-kind table moved to `Theme.filterKindClass`, where the
    // client renderer reads it too. It lived here alone, and the client emitted a
    // bare `fuaran-filter` with no suffix at all.
    let kindClass = Theme.filterKindClass spec.Kind

    let labelText = renderText ctx spec.Label

    // Phase 596 — the value slots are `Binding<_> option` since the swap. An
    // absent (`None`) slot on a filter chip is the auto-bind form: substitute
    // exactly the binding the decoder used to synthesise —
    // `Binding.Filter(spec.Name, None)` — so SSR resolution is unchanged.
    let control =
        match spec.Kind with
        | FormFieldKind.Text(FieldView.Text value, _)
        | FormFieldKind.TextArea(FieldView.Text value, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className Css.Class.FilterInput
                  prop.custom ("type", "text")
                  prop.placeholder labelText
                  prop.value current
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Number(FieldView.Number value, _)
        | FormFieldKind.RangedNumber(FieldView.Number value, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

            Html.input
                [ prop.className Css.Class.FilterInput
                  prop.custom ("type", "number")
                  prop.value (string current)
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Checkbox(FieldView.Bool value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className Css.Class.FilterCheckbox
                  prop.custom ("type", "checkbox")
                  prop.custom ("data-filter-name", spec.Name)
                  if current then
                      prop.custom ("checked", "checked") ]
        // Phase 766 — the SSR twin. The `role`/`aria-checked` pair must appear
        // in the SERVER HTML too: a switch that only becomes a switch after
        // hydration is announced wrongly on first paint, and for a static
        // (never-hydrated) render it would never be announced at all.
        | FormFieldKind.Toggle(FieldView.Bool value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className Css.Class.FilterToggle
                  prop.custom ("type", "checkbox")
                  prop.custom ("role", "switch")
                  prop.custom ("aria-checked", (if current then "true" else "false"))
                  prop.custom ("data-filter-name", spec.Name)
                  if current then
                      prop.custom ("checked", "checked") ]
        | FormFieldKind.DateTime(FieldView.Text value, _, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className Css.Class.FilterInput
                  prop.custom ("type", "date")
                  prop.value current
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Choice(options, FieldView.Text value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let opts = BindingResolver.resolveOptions ctx.Sources options

            // The choice value is `Binding<string>` since the swap ("no
            // selection" was `Binding<string option>`): a null/empty
            // resolution is no-selection, so the placeholder still renders.
            let current =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)

            let optionItems =
                Html.option [ prop.value ""; prop.text "—" ]
                :: [ for option in opts -> Html.option [ prop.value option.Value; prop.text option.Label ] ]

            Html.select
                [ prop.className Css.Class.FilterSelect
                  prop.value (current |> Option.defaultValue "")
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children optionItems ]
        | FormFieldKind.Range(FieldView.Range value, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            // The range value is the `RangePair` record since the swap (was a
            // `(min, max)` tuple); same defaults, same emitted markup.
            let current: RangePair =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue { Min = 0.0; Max = 0.0 }

            let minV, maxV = current.Min, current.Max

            Html.span
                [ prop.className Css.Class.FilterRange
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children
                      [ Html.input
                            [ prop.className Css.Class.FilterRangeMin
                              prop.custom ("type", "number")
                              prop.value (string minV) ]
                        Html.span [ prop.className Css.Class.FilterRangeSep; prop.text "–" ]
                        Html.input
                            [ prop.className Css.Class.FilterRangeMax
                              prop.custom ("type", "number")
                              prop.value (string maxV) ] ] ]
        | FormFieldKind.DateTimeRange(FieldView.DateRange value, _, variant, mn, mx, st) ->
            // Phase 725 — the date-range chip, inert: two native date/time
            // inputs over ONE filter param carrying the whole pair. Mirrors
            // the client renderer's shape + class vocabulary.
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current: DateTimeRangePair =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue { From = ""; To = "" }

            let fromV, toV = current.From, current.To

            let inputType =
                match variant with
                | DateTimeVariant.Date -> "date"
                | DateTimeVariant.Time -> "time"
                | DateTimeVariant.DateTime -> "datetime-local"

            let constraintAttrs =
                [ match mn with
                  | Some m -> prop.custom ("min", m)
                  | None -> ()
                  match mx with
                  | Some m -> prop.custom ("max", m)
                  | None -> ()
                  match st with
                  | Some s -> prop.custom ("step", string s)
                  | None -> () ]

            Html.span
                [ prop.className Css.Class.FilterRange
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children
                      [ Html.input (
                            [ prop.className Css.Class.FilterInputFilterRangeMin
                              prop.custom ("type", inputType)
                              prop.value fromV ]
                            @ constraintAttrs
                        )
                        Html.span [ prop.className Css.Class.FilterRangeSep; prop.text "–" ]
                        Html.input (
                            [ prop.className Css.Class.FilterInputFilterRangeMax
                              prop.custom ("type", inputType)
                              prop.value toV ]
                            @ constraintAttrs
                        ) ] ]
        | FormFieldKind.SegmentedChoice(options, FieldView.Text value, _, orientation) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            renderSegmentedChoiceCore
                ctx
                spec.Name
                "data-filter-name"
                (Some "data-filter-value")
                options
                value
                orientation
        | FormFieldKind.Combobox(allowFreeText, _, options, FieldView.Text value) ->
            // Phase 1113 — the form field's SSR floor, chip-addressed. Same
            // native `<input list>` + `<datalist>`, same recorded limit on
            // `allowFreeText = false`, `data-filter-name` in place of
            // `data-fuaran-field`.
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let listId = spec.Name + "-filter-options"
            let opts = BindingResolver.resolveOptions ctx.Sources options

            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.span
                [ prop.className "fuaran-combobox"
                  prop.children
                      [ Html.input
                            [ prop.className Css.Class.FilterInputComboboxInput
                              prop.custom ("type", "text")
                              prop.custom ("list", listId)
                              prop.custom ("autocomplete", "off")
                              prop.custom (
                                  "data-fuaran-combobox-constrained",
                                  (if allowFreeText then "false" else "true")
                              )
                              prop.placeholder labelText
                              prop.value current
                              prop.custom ("data-filter-name", spec.Name) ]
                        Html.datalist
                            [ prop.id listId
                              prop.children
                                  [ for option in opts ->
                                        Html.option [ prop.value option.Value; prop.text option.Label ] ] ] ] ]
        // Phase 1130 — a chip's declarative write always has somewhere to go
        // (`$filters.<name>`), so a filter rating is always the writable shape:
        // native radios here, the slider after hydration. Same reasoning as the
        // form field's branch above, with the question already answered.
        | FormFieldKind.Rating(allowHalf, max, _, FieldView.Number value) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let shown =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.rating
                |> RatingModel.clamp max

            let positions = int (System.Math.Round(float max / RatingModel.step allowHalf))

            Html.span
                [ prop.className "fuaran-rating fuaran-rating-choices"
                  prop.custom ("data-filter-name", spec.Name)
                  prop.custom ("data-fuaran-rating-value", RatingModel.valueText max shown)
                  prop.children (
                      [ for index, fill in List.indexed (RatingModel.fills max shown) ->
                            Html.span
                                [ prop.className ("fuaran-rating-star " + RatingModel.fillClass fill)
                                  prop.custom ("aria-hidden", "true") ] ]
                      @ [ for i in 1..positions do
                              let target = RatingModel.snap allowHalf max (float i * RatingModel.step allowHalf)

                              let radio =
                                  Html.input
                                      [ prop.custom ("type", "radio")
                                        prop.custom ("name", spec.Name)
                                        prop.value (string target)
                                        if abs (target - shown) < 1e-9 then
                                            prop.custom ("checked", true) ]

                              let caption =
                                  Html.span
                                      [ prop.className "fuaran-rating-choice-label"
                                        prop.text (RatingModel.valueText max target) ]

                              Html.label [ prop.className "fuaran-rating-choice"; prop.children [ radio; caption ] ] ]
                  ) ]
        | FormFieldKind.Color(_, FieldView.Text value) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.filter Fuaran.UI.HostPrelude.HexColor.isValid
                |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.color

            Html.input
                [ prop.className Css.Class.FilterInputColorInput
                  prop.custom ("type", "color")
                  prop.value current
                  prop.custom ("data-filter-name", spec.Name) ]
        // Phase 1121 — the token chip's floor is the field floor's, chip-
        // addressed: one comma-separated text input, optionally backed by a
        // `<datalist>`, with the same two recorded limits. The chip route gets
        // the same floor as the field route because it carries the same control
        // since the 0.2.0 unification — a floor on one and not the other is how
        // half a vocabulary ends up unrendered without JavaScript.
        | FormFieldKind.Tokens(allowFreeText, _, suggestions, FieldView.Tokens value) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue Fuaran.UI.Defaults.ControlValueDefaults.tokens

            let listId = spec.Name + "-suggestions"
            let opts = suggestions |> Option.map (BindingResolver.resolveOptions ctx.Sources)

            Html.span
                [ prop.className "fuaran-tokens"
                  prop.children
                      [ Html.input
                            [ prop.className "fuaran-filter-input fuaran-tokens-input"
                              prop.custom ("type", "text")
                              if Option.isSome opts then
                                  prop.custom ("list", listId)
                              prop.custom ("autocomplete", "off")
                              prop.custom (
                                  "data-fuaran-tokens-constrained",
                                  (if allowFreeText then "false" else "true")
                              )
                              prop.value (TokensModel.toCommaSeparated current)
                              prop.custom ("data-filter-name", spec.Name) ]
                        match opts with
                        | Some options ->
                            Html.datalist
                                [ prop.id listId
                                  prop.children
                                      [ for option in options ->
                                            Html.option [ prop.value option.Value; prop.text option.Label ] ] ]
                        | None -> () ] ]

    Html.label
        [ prop.className (Css.filter kindClass)
          prop.children
              [ Html.span [ prop.className Css.Class.FilterLabel; prop.text labelText ]
                control ] ]

/// The segmented-control / radio-group floor, inert. Mirrors the client
/// `renderSegmentedChoiceCore` shapes, and — since Phase 1605 — drives BOTH of
/// its callers, as the client core already did:
/// `FormFieldKind.SegmentedChoice` on a `Form` (id namespace = the field's
/// `Id`) and on a `Filters` chip (id namespace = the filter's `Name`).
/// Horizontal — `role="radiogroup"` of `role="radio"` buttons;
/// Vertical — `<fieldset>` of native radio inputs (a change event carries the
/// chosen value natively).
///
/// One shape with two callers rather than two arms: the fidelity table recorded
/// the form side emitting no group role at all where the filter side emitted
/// both, and two arms is how that divergence arose.
and private renderSegmentedChoiceCore
    (ctx: ServerRenderContext)
    (idNamespace: string)
    // The shim marker the CONTAINER carries: `data-filter-name` for a filter
    // chip (the name-addressed round trip), `data-fuaran-field` for a form
    // field (the per-field buffer marker).
    (containerAttr: string)
    // The per-OPTION marker, where the shim bridges a click to a value
    // (`data-filter-value`). `None` for a form field, whose floor is inert:
    // emitting one would wire an interaction no server-driven form handler
    // reads.
    (optionAttr: string option)
    (options: Binding<SelectOption list>)
    (value: Binding<string>)
    (orientation: Orientation)
    : ReactElement =
    let opts = BindingResolver.resolveOptions ctx.Sources options

    // The choice value is `Binding<string>` since the swap — a null/empty
    // resolution is no-selection.
    let current =
        BindingResolver.tryResolve ctx.Sources value
        |> Option.bind (fun s -> if isNull s || s = "" then None else Some s)

    let optionId (index: int) : string = Ids.optionId idNamespace index

    match orientation with
    | Orientation.Horizontal ->
        let activeIndex =
            match current with
            | Some v -> opts |> List.tryFindIndex (fun o -> o.Value = v) |> Option.defaultValue -1
            | None -> -1

        let optionButton (index: int) (option: SelectOption) : ReactElement =
            let isActive = index = activeIndex

            Html.button
                [ prop.className Css.Class.SegmentedOption
                  prop.custom ("type", "button")
                  prop.id (optionId index)
                  prop.custom ("aria-checked", (if isActive then "true" else "false"))
                  prop.role "radio"
                  prop.tabIndex (
                      if isActive then 0
                      elif activeIndex < 0 && index = 0 then 0
                      else -1
                  )
                  match optionAttr with
                  | Some attr -> prop.custom (attr, option.Value)
                  | None -> ()
                  prop.text option.Label ]

        Html.div
            [ prop.className Css.Class.SegmentedHorizontal
              prop.id idNamespace
              prop.role "radiogroup"
              prop.custom ("aria-orientation", "horizontal")
              prop.custom (containerAttr, idNamespace)
              prop.children [ for index, option in List.indexed opts -> optionButton index option ] ]
    | Orientation.Vertical ->
        let optionRow (index: int) (option: SelectOption) : ReactElement =
            let inputId = optionId index
            let isChecked = current = Some option.Value

            Html.div
                [ prop.className Css.Class.SegmentedRow
                  prop.children
                      [ Html.input (
                            [ prop.custom ("type", "radio")
                              prop.id inputId
                              prop.name idNamespace
                              prop.value option.Value ]
                            @ (if isChecked then [ prop.custom ("checked", "") ] else [])
                        )
                        Html.label [ prop.htmlFor inputId; prop.text option.Label ] ] ]

        Html.fieldSet
            [ prop.className Css.Class.SegmentedVertical
              prop.custom ("aria-orientation", "vertical")
              prop.custom (containerAttr, idNamespace)
              prop.children (
                  Html.legend [ prop.className Css.Class.SegmentedLegend; prop.text idNamespace ]
                  :: [ for index, option in List.indexed opts -> optionRow index option ]
              ) ]

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
