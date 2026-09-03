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
//  two renderers cannot drift on those axes. The element *structure* is
//  re-emitted here against the ViewEngine API (the same Feliz-shaped calls), so
//  it stays near-identical to `Render.fs`.
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
        /// The render SCOPE this tree runs under (Phase 783). `None` is the root
        /// scope. Custom-renderer lookup is constrained to it, so a tree rendered
        /// for a public surface cannot invoke a renderer registered for a
        /// privileged one — the server twin of the client's `RenderContext.Scope`.
        Scope: string option
        /// Phase 1026 — the ambient destination policy, the server twin of the
        /// client's `RenderContext.EgressPolicy`. Same default
        /// (`Sanitize.denyNonLocalEgress`), same reasoning, same
        /// reached-by-name opt-out.
        ///
        /// **It has to move with the client's, not after it.** The two tiers'
        /// emitted DOM is parity-locked (`SsrParityTests`, `docs/SSR.md` "SSR
        /// parity contract"), so a policy that refused an origin on one tier and
        /// admitted it on the other would not be a partial adoption — it would
        /// be a parity defect, and the tier that admitted it would be the one a
        /// crawler reads.
        ///
        /// If anything the server side is the sharper of the two: an SSR
        /// document is emitted once and read by every visitor, so an undeclared
        /// `Image.src` in a decoded tree becomes a request from each of their
        /// browsers, carrying each of their IP addresses and referers.
        EgressPolicy: Sanitize.EgressPolicy
    }

// ─── Text + value helpers ──────────────────────────────────────────────────

// `internal`, not `private`: the email projection (`Email.fs`) resolves its
// TextSources through THIS function rather than a copy, so an email digest and
// the SSR document cannot disagree about what a bound label says. Assembly-
// internal, so no public API surface changes.
let internal renderText (ctx: ServerRenderContext) (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | TextSource.Bound binding ->
        // Phase 632 — text slots resolve through the scalar path, so a
        // `Binding.Transform` yields its 1×1 result cell (never the rows list).
        // Same dispatch as the client's `renderText` — SSR↔CSR parity.
        BindingResolver.tryResolveScalarText ctx.Sources binding
        |> Option.defaultValue ""
    | TextSource.I18n(key, args) ->
        match Map.tryFind key ctx.Sources.I18n with
        | Some template ->
            // Keep byte-identical to the client renderer's I18n fold (SSR
            // parity): scalar JVal args render as their display string; a
            // composite arg falls back to compact canonical JSON.
            args
            |> Map.fold
                (fun (acc: string) (k: string) (v: Fuaran.Core.JVal) ->
                    let needle = "{" + k + "}"

                    let replacement =
                        match v with
                        | Fuaran.Core.JStr s -> s
                        | Fuaran.Core.JInt i -> string i
                        | Fuaran.Core.JFloat f -> string f
                        | Fuaran.Core.JBool b -> (if b then "true" else "false")
                        | composite -> Fuaran.Core.Json.render composite

                    acc.Replace(needle, replacement))
                template
        | None -> sprintf "[i18n:%s]" key

/// Mirrors the client `formatNumber` (the `CellFormat` projection) so a Metric /
/// LabelValueRow value reads identically server- and client-side.
/// `internal` for the same reason as `renderText` above — the email projection
/// formats a Metric / LabelValueRow value through this exact function, so the
/// figure in a digest reads identically to the figure on the page.
let internal formatNumber (format: CellFormat) (value: float) : string =
    match format with
    | CellFormat.None -> string value
    | CellFormat.Number(Some decimals) -> value.ToString("F" + string decimals)
    | CellFormat.Number None ->
        if value = floor value then
            sprintf "%.0f" value
        else
            sprintf "%g" value
    | CellFormat.Currency code -> sprintf "%s %.2f" code value
    | CellFormat.Percent(Some decimals) -> (value * 100.0).ToString("F" + string decimals) + "%"
    | CellFormat.Percent None -> sprintf "%.1f%%" (value * 100.0)
    | CellFormat.SignificantDigits digits -> value.ToString("G" + string digits)
    | CellFormat.Date _ -> string value
    // Phase 819 — both delegate to the shared Renderer.Core helpers so SSR,
    // CSR and the grid adapter render byte-identically (locale-independent).
    | CellFormat.Duration(unit, style) -> Formatting.formatDuration unit style value
    | CellFormat.RelativeTime unit -> Formatting.formatRelativeEnglish unit value
    | CellFormat.Custom f -> f (CellValue.Numeric value)

// ─── Options resolution (mirrors the client `resolveOptions`) ───────────────

let private opaqueOptionsSentinel = "<opaque>"

let private isOpaqueOptionPlaceholder (option: SelectOption) : bool =
    // `SelectOption.Label` is a bare string since the swap — no TextSource unwrap.
    option.Value = opaqueOptionsSentinel && option.Label = opaqueOptionsSentinel

let private resolveOptions (ctx: ServerRenderContext) (binding: Binding<SelectOption list>) : SelectOption list =
    let resolved =
        BindingResolver.tryResolve ctx.Sources binding |> Option.defaultValue []

    // Per-render hot path (Phase 207) — the client mirror's reasoning, verbatim:
    // `List.filter` allocates a fresh list even when it removes nothing, and the
    // opaque placeholder is a decode-time rarity. Ask first, copy only when there
    // is something to drop. Result-identical; do NOT "simplify" back to a bare
    // `List.filter`.
    if resolved |> List.exists isOpaqueOptionPlaceholder then
        resolved |> List.filter (isOpaqueOptionPlaceholder >> not)
    else
        resolved

// ─── Variant class helpers (shared vocabulary with the client) ──────────────

let private badgeVariantClass (variant: BadgeVariant) : string =
    match variant with
    | BadgeVariant.Neutral -> "neutral"
    | BadgeVariant.Brand -> "brand"
    | BadgeVariant.Success -> "success"
    | BadgeVariant.Warning -> "warning"
    | BadgeVariant.Critical -> "critical"
    | BadgeVariant.Info -> "info"

// ─── The uniform icon hook ─────────────────────────────────────────────────
//
// Every icon-bearing spec (TabHeader / Fact / Metric / Callout / Button)
// renders its `IconSource` as ONE empty placement element:
//
//   <span class="fuaran-icon fuaran-{kind}-icon" data-icon="{name}" aria-hidden="true"></span>
//
// The icon NAME rides the `data-icon` attribute, never the text content — the
// reference CSS ships no glyphs, so a host with no icon system sees nothing
// (not the raw name), and a host maps `data-icon` to glyphs via its own
// mechanism (CSS `::before` content, font classes, or hydration-time SVG
// injection). `aria-hidden` because every icon-bearing spec pairs the icon
// with a visible text label. Mirrors the client renderer's `iconHook` — the
// SSR parity corpus pins the shape.

let private iconHook (kindClass: string) (name: string) : ReactElement =
    Html.span
        [ prop.className ("fuaran-icon " + kindClass)
          prop.custom ("data-icon", name)
          prop.custom ("aria-hidden", "true") ]

let private buttonVariantClass (variant: ButtonVariant) : string =
    match variant with
    | ButtonVariant.Primary -> "primary"
    | ButtonVariant.Secondary -> "secondary"
    | ButtonVariant.Tertiary -> "tertiary"
    | ButtonVariant.Destructive -> "destructive"

// ─── Fragment registry (mirrors the client `collectFragments`) ──────────────

let private fragmentChildren (node: Node<obj>) : Node<obj> list =
    match node.Kind with
    | NodeKind.Box(s) -> s.Children
    | NodeKind.SplitPanel(s) -> s.Children
    | NodeKind.Tabs(s) -> s.Children
    | NodeKind.Stepper(s) -> s.Children
    | NodeKind.SummaryList(s) -> s.Children
    | NodeKind.Disclosure(s) -> s.Children
    | NodeKind.Modal(s) -> s.Children
    | NodeKind.ScrollArea(s) -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child ]
    | NodeKind.Switch s -> (s.Cases |> List.map _.Child) @ [ s.Default ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | _ -> []

/// Collect the tree's `FragmentDecl` bodies by name.
///
/// ITERATIVE, with an explicit stack (Phase 781). This runs from `mkContext`,
/// i.e. BEFORE `renderNode` on every server render, so it is the first walk an
/// untrusted tree meets on this path — a recursive version would have made the
/// depth guard on `renderNode` unreachable for anything deep enough to matter.
/// Insertion order is preserved against the previous fold: children are pushed
/// in reverse so they pop left-to-right, and a later `Map.add` for the same
/// fragment name wins exactly as it did before.
let internal collectFragments (acc: Map<string, Node<obj>>) (node: Node<obj>) : Map<string, Node<obj>> =
    let pending = System.Collections.Generic.Stack<Node<obj>>()
    pending.Push node
    let mutable result = acc

    while pending.Count > 0 do
        let current = pending.Pop()

        match current.Kind with
        | NodeKind.FragmentDecl spec -> result <- Map.add spec.Name spec.Body result
        | _ -> ()

        for child in List.rev (fragmentChildren current) do
            pending.Push child

    result

// ─── Accessibility + node attribute helpers ─────────────────────────────────

/// Attribute pairs, not props: Phase 951 routes them to the wrapper or to the
/// kind body before either becomes an `IReactProperty`.
let private toProps (pairs: (string * string) list) : IReactProperty list =
    pairs |> List.map (fun (k, v) -> prop.custom (k, v))

let private a11yPairs (ctx: ServerRenderContext) (a11y: Accessibility option) : (string * string) list =
    Accessibility.accessibilityAttributes ctx.Sources a11y

let private extraAttrPairs (node: Node<obj>) : (string * string) list =
    match node.ExtraAttributes with
    | Some attrs ->
        attrs
        // The island marker (Phase 163) is lifted onto the boundary wrapper,
        // not the node's own element — strip it here so it never double-emits.
        |> Map.remove Fuaran.UI.Node.IslandAttributeKey
        |> Sanitize.sanitizeExtraAttributes
        |> Map.toList
        // Defence in depth (Phase 788). `Sanitize.sanitizeExtraAttributes` has
        // already gated the key, but this is the site that writes an attribute
        // NAME verbatim: Feliz.ViewEngine's `ViewBuilder.buildElement` emits
        // `" " + key + "=\"" + value + "\""`, and `Interop.mkAttr` escapes the
        // VALUE only — nothing anywhere escapes the name. React's own attribute-
        // name validation gives the client renderer an accidental floor the
        // server path does not have, so the server re-checks rather than
        // depending on upstream validation staying correct. Dropping, not
        // escaping, is the response: HTML cannot escape an illegal character in
        // an attribute name (see `Sanitize.isSafeAttributeName`).
        |> List.filter (fun (k, _) -> Sanitize.isSafeAttributeName k)
    | None -> []

// ─── The renderer ───────────────────────────────────────────────────────────

/// The element emitted in place of a subtree nested past `WireLimits.MaxDepth`
/// (Phase 781). The renderer's contract is `Node<obj> -> ReactElement`, total by
/// signature, so refusal cannot be a `Result` here without breaking every caller
/// — it is a visible, machine-readable truncation marker instead. Two properties
/// make that acceptable rather than a silent lie: the marker CARRIES the fact
/// (`data-fuaran-depth-exceeded` + the limit, plus text in the rendered output),
/// and every path that produces a tree from the wire has already refused this
/// input with a typed error, so a host reaching the marker built the tree
/// in-process rather than accepting it from outside.
///
/// It exists because the alternative is not "a deep tree renders" — it is
/// `StackOverflowException`. Measured, this walk costs roughly 15 KB of stack
/// per node level in Release and 34 KB in Debug, surviving 67 / 30 levels on the
/// .NET default 1 MB thread stack: by a wide margin the shallowest walk in the
/// stack, and the reason `MaxDepth` is the number it is.
let private depthExceededElement (id: string) : ReactElement =
    Html.div
        [ prop.id id
          prop.custom ("data-fuaran-node-id", id)
          prop.custom ("data-fuaran-depth-exceeded", string Fuaran.UI.WireLimits.MaxDepth)
          prop.className "fuaran-depth-exceeded"
          prop.custom ("role", "note")
          prop.children
              [ Html.text (
                    "[subtree omitted: nesting exceeds the wire limit MaxDepth = "
                    + string Fuaran.UI.WireLimits.MaxDepth
                    + "]"
                ) ] ]

let rec private renderNode (depth: int) (ctx: ServerRenderContext) (node: Node<obj>) : ReactElement =
    if depth > Fuaran.UI.WireLimits.MaxDepth then
        depthExceededElement node.Id
    else
        renderNodeCore depth ctx node

/// The node render proper. Reached only through `renderNode`, which is what
/// enforces `MaxDepth` — split out so the guard is two lines rather than a
/// wrapper around fifty indented ones.
and private renderNodeCore (depth: int) (ctx: ServerRenderContext) (node: Node<obj>) : ReactElement =
    let id = node.Id

    let baseClassName =
        Theme.nodeClassName node.Kind (node.Style |> Option.defaultValue Fuaran.UI.Defaults.style)

    let className =
        match node.Motion with
        // Per-node hot path — string concat, not sprintf. Do not "simplify".
        | Some motion -> baseClassName + " fuaran-motion-" + Theme.motionVar motion
        | None -> baseClassName

    let a11y = a11yPairs ctx node.Accessibility
    let extras = extraAttrPairs node

    // Phase 951 — route the projection. A kind whose body IS the node's
    // semantic element takes the a11y attributes (plus the `aria-*` half of
    // `ExtraAttributes`) onto that element; the wrapper keeps only the `data-*`
    // addressing half. Every other kind is unchanged: a11y then extras, on the
    // wrapper, in that order. See `Accessibility.forwardsToSemanticElement`.
    // Phase 1114 — the `dir="auto"` isolation for a display leaf carrying
    // runtime-bound text. Wrapper-side in BOTH arms and FIRST in the list, so
    // the emitted attribute order is identical under SSR and CSR; the policy
    // itself is the shared `Accessibility.bidiAttributes`.
    let bidi = Accessibility.bidiAttributes node.Kind

    let wrapperAttrs, semanticAttrs =
        if Accessibility.forwardsToSemanticElement node.Kind then
            let dataExtras, ariaExtras = Accessibility.partitionExtraAttributes extras
            bidi @ dataExtras, a11y @ ariaExtras
        else
            bidi @ a11y @ extras, []

    let kindBody = renderKind depth ctx id node.State node.Kind semanticAttrs

    // Per-node wrapper props — parity-locked with the Fable renderer's shape.
    // Common case (nothing to carry) is a 4-element literal; otherwise a
    // ResizeArray, not the old 4-way `List.append`. Order is load-bearing
    // (id, node-id, class, attrs, children). Do not "simplify" to `@`.
    let wrapperProps: IReactProperty list =
        match wrapperAttrs with
        | [] ->
            [ prop.id id
              prop.custom ("data-fuaran-node-id", id)
              prop.className className
              prop.children [ kindBody ] ]
        | _ ->
            let props = ResizeArray<IReactProperty>(4 + List.length wrapperAttrs)
            props.Add(prop.id id)
            props.Add(prop.custom ("data-fuaran-node-id", id))
            props.Add(prop.className className)
            props.AddRange(toProps wrapperAttrs)
            props.Add(prop.children [ kindBody ])
            List.ofSeq props

    let element = Html.div wrapperProps

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

and private renderKind
    (depth: int)
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
    // -- Layout --
    // Phase 390 — the unified container; role + layout drive the emitted
    // element + classes so SSR output stays byte-identical to the pre-merge
    // per-kind emission (and to the client renderer — SSR parity corpus).
    | NodeKind.Box spec ->
        match spec.Role, spec.Layout with
        | BoxRole.Card, _ ->
            Html.section
                [ prop.className "fuaran-layout-card"
                  prop.children
                      [ match spec.Heading with
                        | Some heading ->
                            Html.header [ prop.className "fuaran-card-heading"; prop.text (renderText ctx heading) ]
                        | None -> Html.none
                        Html.div
                            [ prop.className "fuaran-card-body"
                              prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ] ] ]
        | BoxRole.Dashboard, _
        | BoxRole.Group, BoxLayout.Auto ->
            Html.div
                [ prop.className "fuaran-layout-dashboard"
                  prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ]
        | BoxRole.Separator, _ -> Html.hr [ prop.className "fuaran-layout-separator" ]
        | BoxRole.Group, BoxLayout.Grid(cols, gridTemplateColumns, gridGap) ->
            let templateColumns =
                match gridTemplateColumns with
                | Some custom -> custom
                | None -> sprintf "repeat(%d, 1fr)" cols

            // `gap` (Phase 459) emits only when set — gap-free grids stay
            // byte-identical to the pre-459 emission (SSR parity with the client).
            // The property name deliberately diverges from the client arm's
            // camelCase `gridTemplateColumns`: Feliz.ViewEngine emits the key
            // verbatim into the style attribute (where CSS ignores camelCase),
            // while the client's React style object requires camelCase.
            let gridStyle =
                [ style.custom ("grid-template-columns", templateColumns) ]
                @ (match gridGap with
                   | Some n -> [ style.custom ("gap", sprintf "%dpx" n) ]
                   | None -> [])

            Html.div
                [ prop.className "fuaran-layout-grid"
                  prop.style gridStyle
                  prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ]
        | BoxRole.Group, BoxLayout.Masonry(cols, masonryGap) ->
            // Phase 1082 — column-FILL, realised through the CSS MULTI-COLUMN
            // property family (`column-count` + the `gap` shorthand's
            // `column-gap` half), which is what WIRE_FORMAT §3.6.7 makes
            // normative. `grid-template-rows: masonry` is the other candidate
            // and is deliberately not used: it is not deterministically
            // available across engines, and a layout mode whose behaviour
            // depends on which browser reads it is not a wire contract.
            //
            // The column count rides inline for the same reason `Grid`'s does —
            // so a CSS host need not pre-declare every N — and the same
            // narrow-viewport rules in the reference sheet collapse it.
            let masonryStyle =
                [ style.custom ("column-count", string cols) ]
                @ (match masonryGap with
                   | Some n -> [ style.custom ("gap", sprintf "%dpx" n) ]
                   | None -> [])

            Html.div
                [ prop.className "fuaran-layout-masonry"
                  prop.style masonryStyle
                  prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ]
        | BoxRole.Group, BoxLayout.Flex(direction, flexWrap, flexGap) ->
            let dir =
                match direction with
                | Orientation.Vertical -> "fuaran-stack-vertical"
                | Orientation.Horizontal -> "fuaran-stack-horizontal"

            let wrap = if flexWrap then " fuaran-stack-wrap" else ""

            Html.div (
                [ prop.className (Css.layoutStack dir wrap) ]
                @ (match flexGap with
                   | Some n -> [ prop.style [ style.custom ("gap", sprintf "%dpx" n) ] ]
                   | None -> [])
                @ [ prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ]
            )
    | NodeKind.SplitPanel spec ->
        let weightLeft = max 0.0 (min 1.0 spec.Weight)
        let weightRight = 1.0 - weightLeft
        let renderedChildren = spec.Children |> List.map (renderNode (depth + 1) ctx)

        let leftChildren, rightChildren =
            match renderedChildren with
            | [] -> [], []
            | [ a ] -> [ a ], []
            | a :: rest -> [ a ], rest

        Html.div
            [ prop.className "fuaran-layout-split-panel"
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-split-pane fuaran-split-pane-left"
                          prop.style [ style.custom ("flex", sprintf "%f 1 0" weightLeft) ]
                          prop.children leftChildren ]
                    Html.div
                        [ prop.className "fuaran-split-pane fuaran-split-pane-right"
                          prop.style [ style.custom ("flex", sprintf "%f 1 0" weightRight) ]
                          prop.children rightChildren ] ] ]
    | NodeKind.Tabs spec ->
        // Static tablist + the active panel. Keyboard nav + click dispatch are
        // client-only (hydration); the server emits the ARIA structure inert.
        let parentNodeIdStr = parentNodeId

        let tabsLabelFromChild (child: Node<obj>) : string =
            match child.Kind with
            | NodeKind.Box { Role = BoxRole.Card
                             Heading = Some h } -> renderText ctx h
            | _ -> child.Id

        let perTab
            : {| label: string
                 icon: string option
                 disabled: bool |} list =
            match spec.TabHeaders with
            | Some headers ->
                headers
                |> List.map (fun h ->
                    let disabled =
                        h.Disabled
                        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
                        |> Option.defaultValue false

                    // `TabHeader.Icon` is a bare `string option` since the swap.
                    let icon = h.Icon

                    {| label = renderText ctx h.Label
                       icon = icon
                       disabled = disabled |})
            | None ->
                spec.Children
                |> List.map (fun child ->
                    {| label = tabsLabelFromChild child
                       icon = None
                       disabled = false |})

        let orientationClass =
            match spec.Orientation with
            | Orientation.Horizontal -> "fuaran-tabs-horizontal"
            | Orientation.Vertical -> "fuaran-tabs-vertical"

        let isVertical = spec.Orientation = Orientation.Vertical

        let resolvedFromTag: int option =
            match spec.TabTags, spec.ActiveTag with
            | Some tags, Some tagBinding ->
                BindingResolver.tryResolve ctx.Sources tagBinding
                |> Option.bind (fun tag -> tags |> List.tryFindIndex ((=) tag))
            | _ -> None

        let activeIndex =
            resolvedFromTag
            |> Option.orElseWith (fun () -> BindingResolver.tryResolve ctx.Sources spec.ActiveIndex)
            |> Option.defaultValue 0
            |> max 0
            |> min (max 0 (spec.Children.Length - 1))

        let activeChild =
            spec.Children
            |> List.tryItem activeIndex
            |> Option.orElseWith (fun () -> spec.Children |> List.tryHead)

        let tabId (i: int) = Ids.tab parentNodeIdStr i
        let panelId (i: int) = Ids.panel parentNodeIdStr i

        Html.div
            [ prop.className (Css.layoutTabs orientationClass)
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-tabs-bar"
                          prop.role "tablist"
                          prop.custom ("aria-orientation", (if isVertical then "vertical" else "horizontal"))
                          prop.children
                              [ for (i, t) in List.indexed perTab ->
                                    let isActive = i = activeIndex

                                    let cls =
                                        String.concat
                                            " "
                                            [ "fuaran-tab"
                                              if isActive then
                                                  "fuaran-tab-active"
                                              if t.disabled then
                                                  "fuaran-tab-disabled" ]

                                    Html.button (
                                        [ prop.id (tabId i)
                                          prop.className cls
                                          prop.role "tab"
                                          prop.custom ("aria-selected", (if isActive then "true" else "false"))
                                          prop.custom ("aria-controls", panelId i)
                                          prop.tabIndex (if isActive then 0 else -1)
                                          prop.custom ("data-tab-index", string i) ]
                                        @ (if t.disabled then
                                               [ prop.custom ("aria-disabled", "true"); prop.disabled true ]
                                           else
                                               [])
                                        @ [ prop.children
                                                [ match t.icon with
                                                  | Some iconSrc -> iconHook "fuaran-tab-icon" iconSrc
                                                  | None -> Html.none
                                                  Html.span [ prop.className "fuaran-tab-label"; prop.text t.label ] ] ]
                                    ) ] ]
                    Html.div
                        [ prop.className "fuaran-tabs-panels"
                          prop.children (
                              match activeChild with
                              | Some childNode ->
                                  [ Html.div
                                        [ prop.id (panelId activeIndex)
                                          prop.role "tabpanel"
                                          prop.custom ("aria-labelledby", tabId activeIndex)
                                          prop.tabIndex 0
                                          prop.className "fuaran-tabs-panel"
                                          prop.children [ renderNode (depth + 1) ctx childNode ] ] ]
                              | None -> []
                          ) ] ] ]
    | NodeKind.SummaryList spec ->
        Html.section
            [ prop.className "fuaran-layout-summary-list"
              prop.children
                  [ match spec.Heading with
                    | Some heading ->
                        Html.header
                            [ prop.className "fuaran-summary-list-heading"
                              prop.text (renderText ctx heading) ]
                    | None -> Html.none
                    Html.div
                        [ prop.className "fuaran-summary-list-body"
                          prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ] ] ]
    | NodeKind.Disclosure spec ->
        let resolvedOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open
            |> Option.defaultValue spec.DefaultOpen

        Html.details (
            [ prop.className "fuaran-layout-disclosure" ]
            @ (if resolvedOpen then [ prop.custom ("open", "") ] else [])
            @ [ prop.children
                    [ Html.summary
                          [ prop.className "fuaran-disclosure-summary"
                            prop.text (renderText ctx spec.Heading) ]
                      Html.div
                          [ prop.className "fuaran-disclosure-body"
                            prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ] ] ]
        )
    | NodeKind.Stepper spec ->
        let activeIndex =
            BindingResolver.tryResolve ctx.Sources spec.ActiveStep |> Option.defaultValue 0

        Html.div
            [ prop.className "fuaran-layout-stepper"
              prop.children
                  [ Html.ol
                        [ prop.className "fuaran-stepper-numbers"
                          prop.children
                              [ for i in 0 .. spec.Children.Length - 1 ->
                                    let isActive = i = activeIndex

                                    Html.li
                                        [ prop.className (
                                              if isActive then
                                                  "fuaran-stepper-step fuaran-stepper-step-active"
                                              else
                                                  "fuaran-stepper-step"
                                          )
                                          // Server-driven step selection: the live
                                          // shim bridges a step-header click to
                                          // `payload.index` off this attribute
                                          // (mirrors the tab bar's data-tab-index).
                                          prop.custom ("data-step-index", string i)
                                          prop.text (sprintf "%d" (i + 1)) ] ] ]
                    Html.div
                        [ prop.className "fuaran-stepper-body"
                          prop.children (
                              match List.tryItem activeIndex spec.Children with
                              | Some node -> [ renderNode (depth + 1) ctx node ]
                              | None -> []
                          ) ] ] ]
    | NodeKind.Modal spec ->
        // Phase 289 overlay render-fidelity contract (server half): the overlay
        // is ALWAYS emitted (no portal), positioned + z-indexed by CSS; closed =
        // the `hidden` attribute. Structure is byte-identical to the client
        // renderer (same classes + `role="dialog"` + `aria-modal`) so React
        // hydration finds the DOM it expects. Dismiss handlers are client-only
        // (attached on hydration) — not a structural difference.
        let isOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

        let headingEls =
            match spec.Heading with
            | Some h -> [ Html.h2 [ prop.className "fuaran-modal-heading"; prop.text (renderText ctx h) ] ]
            | None -> []

        let dismissEls =
            if spec.Dismissable then
                [ Html.button
                      [ prop.className "fuaran-modal-dismiss"
                        prop.custom ("type", "button")
                        prop.ariaLabel "Close"
                        prop.text "×" ] ]
            else
                []

        Html.div (
            [ prop.className "fuaran-modal-overlay" ]
            @ (if not isOpen then [ prop.custom ("hidden", "") ] else [])
            @ [ prop.children
                    [ Html.div
                          [ prop.className "fuaran-modal-dialog"
                            prop.role "dialog"
                            prop.custom ("aria-modal", "true")
                            prop.children (
                                headingEls
                                @ dismissEls
                                @ [ Html.div
                                        [ prop.className "fuaran-modal-body"
                                          prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ] ]
                            ) ] ] ]
        )
    | NodeKind.ScrollArea spec ->
        let axisClass =
            match spec.Orientation with
            | ScrollOrientation.Vertical -> "fuaran-scrollarea fuaran-scrollarea-vertical"
            | ScrollOrientation.Horizontal -> "fuaran-scrollarea fuaran-scrollarea-horizontal"
            | ScrollOrientation.Both -> "fuaran-scrollarea fuaran-scrollarea-both"

        let styleProps =
            [ match spec.MaxHeight with
              | Some h -> style.maxHeight (length.px h)
              | None -> ()
              match spec.MaxWidth with
              | Some w -> style.maxWidth (length.px w)
              | None -> () ]

        Html.div (
            // `prop.custom ("tabindex", ...)` emits the lowercase attribute the
            // client's React `prop.tabIndex` normalises to — keeps SSR↔CSR
            // byte-identical (Feliz.ViewEngine's `prop.tabIndex` would emit the
            // camelCase `tabIndex`, diverging from React's DOM `tabindex`).
            [ prop.className axisClass; prop.custom ("tabindex", "0") ]
            @ (if styleProps.IsEmpty then [] else [ prop.style styleProps ])
            @ [ prop.children (spec.Children |> List.map (renderNode (depth + 1) ctx)) ]
        )
    // -- Display --
    | NodeKind.Heading spec ->
        let variantSuffix =
            match spec.Variant with
            | HeadingVariant.Standard -> ""
            | HeadingVariant.Eyebrow -> " fuaran-heading-eyebrow"
            | HeadingVariant.Caption -> " fuaran-heading-caption"
            | HeadingVariant.Lead -> " fuaran-heading-lead"

        let props =
            [ prop.className (Css.heading variantSuffix)
              prop.text (renderText ctx spec.Text) ]

        match spec.Level with
        | 1 -> Html.h1 props
        | 2 -> Html.h2 props
        | 3 -> Html.h3 props
        | 4 -> Html.h4 props
        | 5 -> Html.h5 props
        | _ -> Html.h6 props
    | NodeKind.Markdown spec ->
        // Phase 1032 — the markdown body's own link/image destinations are
        // policy-checked with the same `ctx.EgressPolicy` the href/src emission
        // sites use. The two tiers' emitted DOM is parity-locked, so this call
        // and the client's must pass the same policy or the parity corpus is the
        // thing that breaks.
        Html.div
            [ prop.className "fuaran-markdown"
              prop.dangerouslySetInnerHTML (Markdown.toHtmlWithEgress ctx.EgressPolicy (renderText ctx spec.Text)) ]
    | NodeKind.Metric spec ->
        // Phase 632 — the Metric value is a scalar slot: a `Binding.Transform`
        // resolves to its 1×1 result cell (a global aggregate / row-field
        // lookup), the same dispatch as the client's `renderMetric`.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match resolution, (state |> Option.bind _.OnLoading) with
        | BindingResolver.NotResolved, Some loadingNode -> renderNode (depth + 1) ctx loadingNode
        | _ ->
            Html.div
                [ prop.className (Css.metric (Theme.toneVar spec.Tone))
                  prop.children
                      [ match spec.Icon with
                        | Some icon -> iconHook "fuaran-metric-icon" icon
                        | None -> Html.none
                        Html.div [ prop.className "fuaran-metric-label"; prop.text (renderText ctx spec.Label) ]
                        Html.div
                            [ prop.className "fuaran-metric-value"
                              prop.text (
                                  match resolution with
                                  | BindingResolver.Resolved value -> formatNumber spec.Format value
                                  | BindingResolver.NotResolved -> "—"
                                  | BindingResolver.Errored msg -> sprintf "(error: %s)" msg
                                  | BindingResolver.I18nUnresolved key -> sprintf "[i18n:%s]" key
                              ) ]
                        match spec.Trend with
                        | Some trendBinding ->
                            // Phase 867 — mirrors the client renderer byte-for-byte:
                            // the trend element carries a SENTIMENT (sign, and from
                            // Part B the declared polarity), not an unconditional
                            // success class. `tone` still colours the tile alone.
                            match BindingResolver.tryResolveScalarFloat ctx.Sources trendBinding with
                            | Some t ->
                                let sentiment, glyph = Theme.trendSentiment spec.TrendPolarity t

                                Html.div
                                    [ prop.className ("fuaran-metric-trend fuaran-metric-trend-" + sentiment)
                                      prop.children
                                          [ Html.span
                                                [ prop.className "fuaran-metric-trend-glyph"
                                                  prop.role "img"
                                                  prop.ariaLabel sentiment
                                                  prop.text glyph ]
                                            Html.text (
                                                formatNumber (spec.TrendFormat |> Option.defaultValue CellFormat.None) t
                                            ) ] ]
                            | None -> Html.div [ prop.className "fuaran-metric-trend"; prop.text "" ]
                        | None -> Html.none
                        match spec.Subtext with
                        | Some subtext ->
                            Html.div [ prop.className "fuaran-metric-subtext"; prop.text (renderText ctx subtext) ]
                        | None -> Html.none ] ]
    | NodeKind.Badge spec ->
        Html.span
            [ prop.className (Css.badge (badgeVariantClass spec.Variant))
              prop.text (renderText ctx spec.Label) ]
    | NodeKind.Skeleton spec ->
        Html.div
            [ prop.className "fuaran-skeleton"
              prop.children [ for _ in 1 .. spec.Rows -> Html.div [ prop.className "fuaran-skeleton-row" ] ] ]
    | NodeKind.Icon spec ->
        // Phase 821 — the standalone icon-only display kind. The glyph NAME
        // rides `data-icon` (the uniform icon-hook contract above — no text
        // content, hosts map it to glyphs); size + tone are modifier classes.
        // A11y: decorative (`Label = None`) emits `aria-hidden="true"`;
        // labelled emits `role="img"` + `aria-label`. Mirrors the client
        // renderer byte-for-byte.
        Html.span
            [ prop.className (Css.icon (Theme.iconSizeClass spec.Size) (Theme.toneVar spec.Tone))
              prop.custom ("data-icon", spec.Icon)
              match spec.Label with
              | Some label ->
                  prop.custom ("role", "img")
                  prop.custom ("aria-label", label)
              | None -> prop.custom ("aria-hidden", "true") ]
    | NodeKind.Callout spec ->
        Html.div
            [ prop.className (Css.callout (Theme.toneVar spec.Tone))
              prop.children
                  [ match spec.Icon with
                    | Some icon -> iconHook "fuaran-callout-icon" icon
                    | None -> Html.none
                    match spec.Heading with
                    | Some heading ->
                        Html.div [ prop.className "fuaran-callout-heading"; prop.text (renderText ctx heading) ]
                    | None -> Html.none
                    Html.div [ prop.className "fuaran-callout-body"; prop.text (renderText ctx spec.Body) ] ] ]
    | NodeKind.Progress spec ->
        let resolution = BindingResolver.resolve ctx.Sources spec.Fraction

        match resolution, (state |> Option.bind _.OnLoading) with
        | BindingResolver.NotResolved, Some loadingNode -> renderNode (depth + 1) ctx loadingNode
        | _ ->
            let fraction =
                match resolution with
                | BindingResolver.Resolved value -> value
                | _ -> 0.0

            Html.div
                [ prop.className (
                      Css.progress
                          (Theme.toneVar spec.Tone)
                          (if spec.Indeterminate then
                               " fuaran-progress-indeterminate"
                           else
                               "")
                  )
                  prop.children
                      [ match spec.Label with
                        | Some label ->
                            Html.div [ prop.className "fuaran-progress-label"; prop.text (renderText ctx label) ]
                        | None -> Html.none
                        Html.div
                            [ prop.className "fuaran-progress-bar"
                              prop.children
                                  [ Html.div
                                        [ prop.className "fuaran-progress-fill"
                                          prop.style [ style.custom ("width", sprintf "%f%%" (fraction * 100.0)) ] ] ] ] ] ]
    | NodeKind.Sparkline _ ->
        // The client emits an inline SVG polyline; SSR emits the same hook +
        // an em-dash placeholder (the data renders client-side on hydration).
        Html.div [ prop.className "fuaran-sparkline fuaran-sparkline-empty"; prop.text "—" ]
    | NodeKind.Drawing spec ->
        // Phase 525 — the SAME canonical Core SVG string the client emits (so
        // SSR ↔ CSR are byte-identical for this static-geometry node); resolved
        // + rendered on the server, headless included (D4).
        Html.div [ prop.dangerouslySetInnerHTML (DrawingSvg.render ctx.Sources (renderText ctx) spec) ]
    | NodeKind.LabelValueRow spec ->
        // Phase 632 — a scalar slot: Transform resolves to its 1×1 result cell,
        // and an ambiguous (>1×1) result stays loud, matching the client's
        // `renderLabelValueRow` value projection.
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        match resolution, (state |> Option.bind _.OnLoading) with
        | BindingResolver.NotResolved, Some loadingNode -> renderNode (depth + 1) ctx loadingNode
        | _ ->
            let emphasisSuffix =
                if spec.Emphasis then
                    " fuaran-label-value-row-emphasis"
                else
                    ""

            Html.div
                [ prop.className (Css.labelValueRow emphasisSuffix)
                  prop.children
                      [ Html.span
                            [ prop.className "fuaran-label-value-row-label"
                              prop.text (renderText ctx spec.Label) ]
                        Html.span
                            [ prop.className "fuaran-label-value-row-value"
                              prop.text (
                                  match resolution with
                                  | BindingResolver.Resolved value -> formatNumber spec.Format value
                                  | BindingResolver.NotResolved -> "—"
                                  | BindingResolver.Errored msg -> sprintf "(error: %s)" msg
                                  | BindingResolver.I18nUnresolved key -> sprintf "[i18n:%s]" key
                              ) ] ] ]
    | NodeKind.Fact spec ->
        // Server-side Fact mirrors the client tile; `renderText` resolves the
        // TextSource value identically on both sides (see the module doc's
        // SSR<->CSR parity note).
        let emphasisSuffix = if spec.Emphasis then " fuaran-fact-emphasis" else ""

        Html.div
            [ prop.className (Css.fact (Theme.toneVar spec.Tone) emphasisSuffix)
              prop.children
                  [ Html.div [ prop.className "fuaran-fact-label"; prop.text (renderText ctx spec.Label) ]
                    Html.div
                        [ prop.className "fuaran-fact-value"
                          prop.children
                              [ match spec.Icon with
                                | Some icon -> iconHook "fuaran-fact-icon" icon
                                | None -> Html.none
                                Html.span [ prop.text (renderText ctx spec.Value) ] ] ]
                    match spec.Help with
                    | Some help -> Html.div [ prop.className "fuaran-fact-help"; prop.text (renderText ctx help) ]
                    | None -> Html.none ] ]
    | NodeKind.Link spec ->
        let resolvedHref =
            BindingResolver.tryResolve ctx.Sources spec.Href |> Option.defaultValue ""

        // Phase 1026 — the ambient destination policy; the client tier's `Link`
        // arm makes the identical call with the identical class, which is what
        // keeps the two emitted hrefs parity-locked.
        let safeHref, egressAttrs =
            Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Hyperlink resolvedHref

        match spec.Protection with
        | Some LinkProtection.Email when safeHref.StartsWith("mailto:", System.StringComparison.Ordinal) ->
            // Phase 812 — protected email link. The address must not appear in
            // plaintext in the emitted document, so every character of the
            // sanitised href AND the label is emitted as a decimal HTML entity.
            // The browser decodes entities in both positions, so the anchor is
            // a working `mailto:` with no JavaScript, while the raw source
            // carries no scrapeable address. Encoding every character (not
            // just specials) makes the fragment injection-proof by
            // construction, which is why `dangerouslySetInnerHTML` is safe
            // here. CSR emits the same structure with the decoded href — the
            // two DOMs are identical after entity decoding.
            let entityEncode (s: string) =
                s |> Seq.map (fun c -> "&#" + string (int c) + ";") |> String.concat ""

            let anchor =
                "<a class=\"fuaran-link fuaran-link-protected\" href=\""
                + entityEncode safeHref
                + "\">"
                + entityEncode (renderText ctx spec.Label)
                + "</a>"

            // Phase 951 — the anchor here is an entity-encoded opaque string,
            // so the projection lands on the wrap `<span>`: the only element
            // this arm owns in BOTH tiers, and parity with the client (which
            // does build a real `<a>`) outranks reaching one tier's anchor.
            // Writing attribute names into the raw-HTML string would open a new
            // injection seam in a `dangerouslySetInnerHTML` payload — declined
            // here, recorded in `docs/DECISIONS.md` D4.
            Html.span (
                [ prop.className "fuaran-link-protected-wrap"
                  prop.dangerouslySetInnerHTML anchor ]
                @ toProps semanticAttrs
            )
        | _ ->
            Html.a (
                [ prop.className "fuaran-link"; prop.href safeHref ]
                @ (match spec.Rel with
                   | Some rel -> [ prop.custom ("rel", rel) ]
                   | None -> [])
                @ (match spec.Target with
                   | Some target -> [ prop.custom ("target", target) ]
                   | None -> [])
                @ (if spec.Download then
                       [ prop.custom ("download", "") ]
                   else
                       [])
                // Phase 951 — the node's a11y projection lands on the anchor.
                @ toProps semanticAttrs
                // Phase 1026 — the refusal marker rides the element carrying
                // the refused href. Empty on an allow.
                @ toProps egressAttrs
                @ [ prop.text (renderText ctx spec.Label) ]
            )
    | NodeKind.Image spec ->
        let resolvedSrc =
            BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

        // Phase 1026 — `Media`: the class the browser fetches with no user act.
        let safeSrc, egressAttrs =
            Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolvedSrc

        let variantClass =
            match spec.Variant with
            | ImageVariant.Default -> "fuaran-image"
            | ImageVariant.Avatar -> "fuaran-image fuaran-image-avatar"
            | ImageVariant.Rounded -> "fuaran-image fuaran-image-rounded"

        // Phase 1077 — the presentation tokens map to classes and nothing
        // else: no value from the tree ever reaches a style attribute.
        // `Natural` emits NO class on either axis, so a pre-phase tree's class
        // attribute is byte-identical to what it was.
        let fitClass =
            match spec.Fit with
            | ImageFit.Natural -> ""
            | ImageFit.Cover -> " fuaran-image-fit-cover"
            | ImageFit.Contain -> " fuaran-image-fit-contain"

        let aspectClass =
            match spec.AspectRatio with
            | ImageAspect.Natural -> ""
            | ImageAspect.Square -> " fuaran-image-aspect-square"
            | ImageAspect.FourThree -> " fuaran-image-aspect-four-three"
            | ImageAspect.ThreeTwo -> " fuaran-image-aspect-three-two"
            | ImageAspect.SixteenNine -> " fuaran-image-aspect-sixteen-nine"

        // Phase 1077 — `Eager` emits no attribute at all (the browser default);
        // only `Lazy` is a declaration.
        let loadingAttrs =
            match spec.Loading with
            | ImageLoading.Eager -> []
            | ImageLoading.Lazy -> [ "loading", "lazy" ]

        // Phase 1080 — the responsive candidate list. Three properties, each
        // load-bearing and each the reason a line here exists:
        //
        // SANITISED PER ENTRY. Every candidate's `Src` goes through the SAME
        // `sanitizeUrlForEgress` call the primary `src` does, at the same
        // `Media` egress class. A srcset entry is a URL the browser fetches with
        // no user act — exactly what the floor exists for — so routing only the
        // primary through it would make `srcSet` a bypass of the one rule this
        // node has.
        //
        // A FAILING ENTRY IS DROPPED, not neutered. The primary `src` collapses
        // to `about:blank` because an `<img>` must have one; a candidate has no
        // such obligation, and emitting `about:blank 400w` would offer the
        // browser a rendition guaranteed to fail. Dropping it leaves the
        // primary `src` — which is the fallback the whole mechanism is built on.
        //
        // ASCENDING BY WIDTH, sorted HERE. The wire preserves authored array
        // order (a JSON array is ordered data; the canonical encoder sorts
        // object keys only), so canonical SSR output is the renderer's
        // obligation, not the codec's. `List.sortBy` is stable, so two entries
        // declaring the same width keep their authored order rather than
        // swapping on a re-render.
        let srcSetAttrs =
            let candidates =
                spec.SrcSet
                |> List.sortBy _.Width
                |> List.choose (fun entry ->
                    let resolved =
                        BindingResolver.tryResolve ctx.Sources entry.Src |> Option.defaultValue ""

                    // A non-empty refusal-marker list IS the refusal — read from
                    // the seam's own verdict rather than by string-comparing the
                    // URL it substitutes, so a later change to that substitute
                    // cannot silently turn a dropped candidate into a served one.
                    let safe, refusal =
                        Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolved

                    if safe = "" || not (List.isEmpty refusal) then
                        None
                    else
                        Some(safe + " " + string entry.Width + "w"))

            match candidates with
            | [] -> []
            // `sizes` is BOUNDED, and `100vw` is the only value the tree can
            // justify: nothing in the document says how wide this element will
            // be laid out, and the language has no media-query slot for an
            // author to say so. It is stated rather than left to the HTML
            // default so the candidate arithmetic is visible in the emitted
            // markup instead of implied by it.
            | xs -> [ "srcset", String.concat ", " xs; "sizes", "100vw" ]

        // Phase 951 — the a11y projection lands on the `<img>` itself.
        let img =
            Html.img (
                [ prop.className (variantClass + fitClass + aspectClass)
                  prop.src safeSrc
                  prop.alt (renderText ctx spec.Alt) ]
                @ toProps srcSetAttrs
                @ toProps loadingAttrs
                @ toProps semanticAttrs
                @ toProps egressAttrs
            )

        // Phase 1079 — the expansion affordance. Three decisions, all of them
        // visible in these six lines.
        //
        // THE BASELINE IS A REAL LINK, not a marked-up control waiting for
        // script. `<a href="{the sanitised primary src}">` around the `<img>`
        // means a reader with no JavaScript — a crawler, a text browser, a
        // locked-down enterprise client, a hydration that failed — clicks the
        // thumbnail and gets the full-size asset in the browser's own viewer.
        // The `data-fuaran-expandable` marker is what the enhancement tier
        // reads; it is a marker on a WORKING link, never the mechanism itself.
        // The attribute is VALUELESS because the slot is a bool whose `false`
        // is the absence of the attribute — unlike `data-fuaran-sortable`,
        // which carries a value precisely because a table has three states
        // (unstated / exempt / affirmative) under a host's broad default.
        //
        // A REFUSED `src` EMITS NO ANCHOR. This is the srcSet rule turned on
        // the affordance: the `<img>`'s `src` must exist, so it collapses to
        // the refusal URL, but an anchor has no such obligation, and a link to
        // `about:blank` is exactly the dead control this design exists to
        // avoid. The image still renders — with its refusal marker — and the
        // reader is simply not offered an expansion that could not work.
        //
        // NOTHING CROSSES THE DISPATCH GATE. There is no `Action`, no handler,
        // no `onClick` in the emitted markup. The expansion is presentation:
        // the wire declares that the asset is reachable, the anchor makes it
        // reachable, and the overlay is a rendering choice the client tier
        // makes over that anchor.
        let expandable =
            if spec.Expandable && safeSrc <> "" && List.isEmpty egressAttrs then
                Html.a
                    [ prop.className "fuaran-image-expand"
                      prop.href safeSrc
                      prop.custom ("data-fuaran-expandable", "")
                      prop.children [ img ] ]
            else
                img

        // Phase 1078 — the caption. `None` returns the emission UNTOUCHED,
        // which is the acceptance criterion expressed as control flow rather
        // than as a claim: there is no wrapper to be byte-identical to, because
        // there is no wrapper. `Some` wraps it in the semantic pair, which is
        // the whole point — an ad-hoc `Text` sibling carried the same pixels
        // and no binding, so assistive technology read it as the next
        // paragraph. Nothing moves onto the `<figure>`: the a11y projection,
        // the egress marker and the sanitised `src` all stay on the element
        // they describe.
        //
        // Phase 1079 — the NESTING, stated once here and mirrored in the other
        // three renderers: `<figure>` wraps `<a>` wraps `<img>`. The caption is
        // OUTSIDE the link target, which is the composition decision rather
        // than an ordering accident. A `<figcaption>` inside the anchor would
        // make the caption's own text a click target for the expansion, and a
        // caption is prose a reader selects, quotes and reads — sometimes
        // several lines of it — not a second button. It would also put
        // interactive content inside the element whose job is to LABEL the
        // image, which is the relationship `<figure>`/`<figcaption>` exists to
        // express.
        match spec.Caption with
        | None -> expandable
        | Some caption ->
            Html.figure
                [ prop.className "fuaran-image-figure"
                  prop.children
                      [ expandable
                        Html.figcaption
                            [ prop.className "fuaran-image-figure-caption"
                              prop.text (renderText ctx caption) ] ] ]
    // Phase 1076 — the media transport. Deterministic, script-free markup: a
    // real `<video>` / `<audio>` a browser plays with no runtime, exactly as
    // `Image` emits a real `<img>`.
    //
    // Four things here are contract rather than choice, and each is stated
    // normatively in the wire spec because a host that got any of them wrong
    // would still round-trip the bytes perfectly:
    //
    //   * `aria-label` ALWAYS. The label is mandatory on the wire and there is
    //     no decorative case, so unlike `Image`'s `alt` there is no branch: the
    //     attribute is emitted whatever the label resolves to. FUARAN108 is
    //     what stops an empty one reaching here in the first place.
    //   * `autoplay` NEVER WITHOUT `muted`. The pairing is not a default a
    //     caller can override — it is what `Autoplay` means, which is why the
    //     wire carries no separate `muted` slot to get out of step with it.
    //     Every browser blocks unmuted autoplay anyway, so an unmuted emission
    //     would produce a player that silently does not start: the declaration
    //     would be a lie and the failure would be invisible.
    //   * NO AUTOPLAY PATHWAY ON AUDIO, at all. Not "off by default" — the
    //     `MediaKind.Audio` case carries no slot to read, so this arm has
    //     nothing to branch on and cannot acquire one by a later edit here.
    //   * BOTH URLS THROUGH THE EGRESS FLOOR. `Src` and `Poster` are each
    //     fetched by the browser with no user act, which is the whole of the
    //     `Media` egress class. They differ in what a REFUSAL means: an element
    //     must have a source, so `src` collapses to the refusal URL and carries
    //     the marker, while a poster simply leaves — a `<video>` with no poster
    //     shows its first frame, which is a working rendering, whereas a poster
    //     pointing at the refusal URL is a broken image painted over the
    //     player. Same rule as a refused `SrcSet` candidate, same reason.
    //
    // The boolean attributes use Feliz's TYPED props rather than
    // `prop.custom (name, "")`, and that is deliberate on this kind alone: the
    // muted pairing has to hold in the DOM on BOTH tiers, and the typed props
    // are the one spelling each backend renders correctly without a per-arm
    // string (`autoPlay` → `autoplay` here, → React's `autoPlay` on the client).
    | NodeKind.Media spec ->
        let resolvedSrc =
            BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

        let safeSrc, egressAttrs =
            Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolvedSrc

        // Phase 1110 - the timed-text tracks. Three obligations live in this one
        // fold, and each of them is something the bytes cannot carry:
        //
        //   * AUTHORED ORDER. The list is emitted exactly as the wire carries
        //     it. This is the OPPOSITE of `srcSet`, which the renderers sort
        //     ascending by width, and the difference is not an inconsistency: a
        //     browser picks ONE candidate from a srcset by an algorithm, while a
        //     reader picks a track from a menu the user agent builds in document
        //     order. Sorting the first is canonicalisation; sorting the second
        //     would be reordering someone else's menu.
        //   * ONE DEFAULT PER KIND, FIRST WINS. A document electing two default
        //     captions tracks is legal bytes - the decoder does not refuse it,
        //     because a lenient host would render it anyway - and HTML leaves
        //     two defaults of one kind undefined. So the host decides, and every
        //     host decides the same way: the first election of a kind is
        //     honoured and a later one emits WITHOUT the attribute. The track is
        //     still emitted; only its claim on the menu is dropped.
        //   * A REFUSED SOURCE DROPS THE TRACK. The poster rule rather than the
        //     source rule: an element must have a source, but it need not have
        //     this track, and a `<track>` pointing at the refusal URL is a menu
        //     entry that opens onto nothing.
        let trackKindToken (k: TrackKind) =
            match k with
            | TrackKind.Subtitles -> "subtitles"
            | TrackKind.Captions -> "captions"
            | TrackKind.Descriptions -> "descriptions"
            | TrackKind.Chapters -> "chapters"

        let trackEls =
            spec.Tracks
            |> List.fold
                (fun (claimed: Set<string>, acc) (t: TrackEntry) ->
                    let resolved =
                        BindingResolver.tryResolve ctx.Sources t.Src |> Option.defaultValue ""

                    let safe, refusal =
                        Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolved

                    if safe = "" || not (List.isEmpty refusal) then
                        claimed, acc
                    else
                        let token = trackKindToken t.Kind
                        let takesDefault = t.Default && not (Set.contains token claimed)

                        let el =
                            Html.track (
                                [ prop.custom ("kind", token)
                                  prop.src safe
                                  prop.custom ("srclang", t.SrcLang)
                                  prop.custom ("label", renderText ctx t.Label) ]
                                @ (if takesDefault then [ prop.custom ("default", "") ] else [])
                            )

                        (if takesDefault then Set.add token claimed else claimed), el :: acc)
                (Set.empty, [])
            |> snd
            |> List.rev

        let trackChildren =
            if List.isEmpty trackEls then
                []
            else
                [ prop.children trackEls ]

        // Phase 1110 - the transcript, rendered BESIDE the transport rather than
        // inside it. `<video>` and `<audio>` admit only source-ish children, so
        // a transcript in there would be fallback content a browser never shows;
        // the disclosure has to be a sibling, which is why a present transcript
        // is the one case that wraps. Absent, the emission is the bare element
        // it always was - the `Image` caption treatment, for the same reason.
        //
        // The `<details>` carries the MEDIA's resolved label as its accessible
        // name, so a reader meeting the disclosure out of context is told which
        // recording it transcribes. The summary text is renderer chrome (the
        // `Toast` dismiss precedent); the transcript itself is the document's.
        let withTranscript (el: ReactElement) =
            match spec.Transcript with
            | None -> el
            | Some transcript ->
                Html.div
                    [ prop.className "fuaran-media-group"
                      prop.children
                          [ el
                            Html.details
                                [ prop.className "fuaran-media-transcript"
                                  prop.custom ("aria-label", renderText ctx spec.Label)
                                  prop.children
                                      [ Html.summary
                                            [ prop.className "fuaran-media-transcript-summary"; prop.text "Transcript" ]
                                        Html.div
                                            [ prop.className "fuaran-media-transcript-body"
                                              prop.text (renderText ctx transcript) ] ] ] ] ]

        let sharedProps (variantClass: string) =
            [ prop.className ("fuaran-media " + variantClass)
              prop.src safeSrc
              prop.custom ("aria-label", renderText ctx spec.Label) ]
            @ (if spec.Controls then [ prop.controls true ] else [])
            @ (if spec.Loop then [ prop.loop true ] else [])

        match spec.Kind with
        | MediaKind.Video(autoplay, poster) ->
            let posterProps =
                match poster with
                | None -> []
                | Some p ->
                    let resolved = BindingResolver.tryResolve ctx.Sources p |> Option.defaultValue ""

                    // The refusal is read from the seam's own marker list, not
                    // by comparing against whatever URL it substitutes — the
                    // `SrcSet` candidate rule, applied to the one other URL
                    // this vocabulary fetches unprompted.
                    let safe, refusal =
                        Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolved

                    if safe = "" || not (List.isEmpty refusal) then
                        []
                    else
                        [ prop.poster safe ]

            let autoplayProps =
                if autoplay then
                    [ prop.autoPlay true; prop.muted true ]
                else
                    []

            withTranscript (
                Html.video (
                    sharedProps "fuaran-media-video"
                    @ posterProps
                    @ autoplayProps
                    @ toProps semanticAttrs
                    @ toProps egressAttrs
                    @ trackChildren
                )
            )
        | MediaKind.Audio ->
            withTranscript (
                Html.audio (
                    sharedProps "fuaran-media-audio"
                    @ toProps semanticAttrs
                    @ toProps egressAttrs
                    @ trackChildren
                )
            )
    | NodeKind.Embed spec ->
        // Phase 1111 — the sandboxed third-party embed. Four obligations live
        // here, and every one of them is something the bytes cannot carry.
        //
        //   * SANDBOX BY DEFAULT. The `sandbox` attribute is emitted ALWAYS,
        //     and with no permissions it is emitted EMPTY, which is the
        //     maximally-restrictive value. Omitting it on a permissionless
        //     embed would be the same markup as an unsandboxed frame, so the
        //     attribute is unconditional rather than derived from the list
        //     being non-empty.
        //   * DECLARATION ORDER, DE-DUPLICATED. The wire preserves whatever
        //     order the document authored (the `tracks` rule, not `srcSet`'s —
        //     no re-sorting of a document's own list), so the DETERMINISM the
        //     SSR output needs is established here instead: the tokens are
        //     emitted in the enum's declaration order and each at most once, so
        //     two documents naming the same set produce byte-identical markup.
        //   * FULLSCREEN IS NOT A SANDBOX TOKEN. It is a permissions-policy
        //     directive and rides the `allow` attribute, which is emitted only
        //     when it was declared — an empty `allow` is not the same statement
        //     as an absent one.
        //   * A REFUSED SOURCE DROPS THE ATTRIBUTE ENTIRELY. Not the refusal
        //     URL that `Link` and `Image` substitute: an `<iframe>` pointed at
        //     `about:blank#…` renders that page, where a frame with no `src` is
        //     a well-defined empty frame that fetches nothing. The marker
        //     attribute still records the refusal, so the two facts stay apart.
        let resolvedSrc =
            BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

        let safeSrc, egressAttrs =
            Sanitize.sanitizeEmbedSrcForEgress ctx.EgressPolicy resolvedSrc

        let aspectClass =
            match spec.AspectRatio with
            | ImageAspect.Natural -> ""
            | ImageAspect.Square -> " fuaran-embed-aspect-square"
            | ImageAspect.FourThree -> " fuaran-embed-aspect-four-three"
            | ImageAspect.ThreeTwo -> " fuaran-embed-aspect-three-two"
            | ImageAspect.SixteenNine -> " fuaran-embed-aspect-sixteen-nine"

        let has p = List.contains p spec.Permissions

        let sandboxTokens =
            [ if has EmbedPermission.AllowScripts then
                  "allow-scripts"
              if has EmbedPermission.AllowSameOrigin then
                  "allow-same-origin"
              if has EmbedPermission.AllowForms then
                  "allow-forms" ]

        Html.iframe (
            [ prop.className ("fuaran-embed" + aspectClass)
              prop.title (renderText ctx spec.Title)
              prop.custom ("sandbox", String.concat " " sandboxTokens)
              // Always lazy, and there is deliberately no slot to say otherwise:
              // a third-party document is the one subresource whose fetch is
              // never worth doing before the reader has scrolled to it.
              prop.custom ("loading", "lazy")
              // Conservative, but NOT `no-referrer`: several ubiquitous embed
              // providers restrict playback by referring domain, so stripping
              // the header outright breaks a legitimate embed. Sending the
              // origin and nothing else satisfies them while leaking no path
              // and no query.
              prop.custom ("referrerpolicy", "strict-origin-when-cross-origin") ]
            @ (match safeSrc with
               | Some s -> [ prop.src s ]
               | None -> [])
            @ (if has EmbedPermission.AllowFullscreen then
                   [ prop.custom ("allow", "fullscreen") ]
               else
                   [])
            @ toProps semanticAttrs
            @ toProps egressAttrs
        )
    | NodeKind.List spec ->
        let items =
            spec.Items
            |> List.map (fun item -> Html.li [ prop.className "fuaran-list-item"; prop.text (renderText ctx item) ])

        if spec.Ordered then
            Html.ol [ prop.className "fuaran-list fuaran-list-ordered"; prop.children items ]
        else
            Html.ul [ prop.className "fuaran-list fuaran-list-unordered"; prop.children items ]
    | NodeKind.Toast spec ->
        // Phase 289 overlay render-fidelity contract (server half): ALWAYS
        // emitted; closed = the `hidden` attribute. `role="status"` +
        // `aria-live="polite"` — byte-identical to the client renderer.
        let isOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

        let toneClass =
            match spec.Tone with
            | ToneVariant.Default -> "default"
            | ToneVariant.Subdued -> "subdued"
            | ToneVariant.Brand -> "brand"
            | ToneVariant.Success -> "success"
            | ToneVariant.Warning -> "warning"
            | ToneVariant.Critical -> "critical"
            | ToneVariant.Info -> "info"

        let dismissEls =
            if spec.Dismissable then
                [ Html.button
                      [ prop.className "fuaran-toast-dismiss"
                        prop.custom ("type", "button")
                        prop.ariaLabel "Dismiss"
                        prop.text "×" ] ]
            else
                []

        Html.div (
            [ prop.className (Css.toast toneClass)
              prop.role "status"
              prop.custom ("aria-live", "polite") ]
            @ (if not isOpen then [ prop.custom ("hidden", "") ] else [])
            @ [ prop.children (
                    [ Html.span
                          [ prop.className "fuaran-toast-message"
                            prop.text (renderText ctx spec.Message) ] ]
                    @ dismissEls
                ) ]
        )
    | NodeKind.CodeBlock spec ->
        // Phase 290 — DETERMINISTIC `<pre><code>` (HTML-escaped via `prop.text`,
        // no markdown library), byte-identical to the client renderer. Syntax
        // highlighting is a client-only post-hydration enhancement (targets
        // `language-{x}`) — not emitted here, so it is outside the parity output.
        let containerClass =
            if spec.LineNumbers then
                "fuaran-codeblock fuaran-codeblock-numbered"
            else
                "fuaran-codeblock"

        let highlightAttr =
            match spec.HighlightLines with
            | [] -> []
            | lines -> [ prop.custom ("data-highlight-lines", String.concat "," (lines |> List.map string)) ]

        let copyEls =
            if spec.Copyable then
                [ Html.button
                      [ prop.className "fuaran-codeblock-copy"
                        prop.custom ("type", "button")
                        prop.ariaLabel "Copy"
                        prop.text "Copy" ] ]
            else
                []

        Html.div (
            [ prop.className containerClass; prop.custom ("data-language", spec.Language) ]
            @ highlightAttr
            @ [ prop.children (
                    copyEls
                    @ [ Html.pre
                            [ prop.className "fuaran-codeblock-pre"
                              prop.children
                                  [ Html.code [ prop.className (Css.codeBlockCode spec.Language); prop.text spec.Code ] ] ] ]
                ) ]
        )
    | NodeKind.Math spec ->
        // Phase 658 — DETERMINISTIC native MathML for the closed subset (real
        // superscripts with NO JavaScript); the raw escaped-source span for
        // out-of-subset input. Byte-identical to the client renderer via the
        // shared `MathMl.translate`. KaTeX upgrades either shape client-only
        // (targets the `.fuaran-math` container), outside parity. See
        // docs/MATH-DEGRADATION.md.
        let mathml = MathMl.translate spec.Source spec.Display

        let displayStr, isBlock =
            match spec.Display with
            | MathDisplay.Block -> "block", true
            | MathDisplay.Inline -> "inline", false

        let containerProps =
            [ prop.className (
                  if isBlock then
                      "fuaran-math fuaran-math-block"
                  else
                      "fuaran-math fuaran-math-inline"
              )
              prop.custom ("data-math-display", displayStr)
              prop.custom ("data-fuaran-math-src", spec.Source) ]

        let content =
            match mathml with
            | Some markup -> [ prop.dangerouslySetInnerHTML markup ]
            | None -> [ prop.children [ Html.span [ prop.className "fuaran-math-source"; prop.text spec.Source ] ] ]

        if isBlock then
            Html.div (containerProps @ content)
        else
            Html.span (containerProps @ content)
    // -- Input --
    | NodeKind.Button spec ->
        let variantClass = buttonVariantClass spec.Variant

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
              | Some icon -> prop.children [ iconHook "fuaran-button-icon" icon; Html.text (renderText ctx spec.Label) ]
              | None -> prop.text (renderText ctx spec.Label) ]
            @ (match spec.Tooltip with
               | Some t -> [ prop.title (renderText ctx t) ]
               | None -> [])
            @ commitProps
            // Phase 951 — the a11y projection lands on the `<button>` itself.
            @ toProps semanticAttrs
            @ (if isDisabled then [ prop.disabled true ] else [])
        )
    | NodeKind.Select spec ->
        let options = resolveOptions ctx spec.Source
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
            [ prop.className "fuaran-select"
              prop.children
                  [ Html.span [ prop.className "fuaran-select-label"; prop.text (renderText ctx spec.Label) ]
                    Html.select (
                        [ prop.className "fuaran-select-control" ]
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
                [ prop.className "fuaran-form-submit"
                  prop.custom ("type", "submit")
                  prop.text (renderText ctx spec.SubmitLabel) ]

        let body = fieldNodes @ [ submitNode ]

        let formChildren =
            match spec.Disabled with
            | Some disabled ->
                let isDisabled =
                    BindingResolver.tryResolve ctx.Sources disabled |> Option.defaultValue false

                [ Html.fieldSet (
                      [ prop.className "fuaran-form-fieldset" ]
                      @ (if isDisabled then [ prop.disabled true ] else [])
                      @ [ prop.children body ]
                  ) ]
            | None -> body

        Html.form [ prop.className "fuaran-form"; prop.children formChildren ]
    | NodeKind.Filters specs ->
        Html.div
            [ prop.className "fuaran-filters"
              prop.children [ for spec in specs.Items -> renderFilterSpec ctx spec ] ]
    | NodeKind.FileUpload spec ->
        Html.label
            [ prop.className "fuaran-file-upload"
              prop.children
                  [ Html.span
                        [ prop.className "fuaran-file-upload-label"
                          prop.text (renderText ctx spec.Label) ]
                    Html.input [ prop.className "fuaran-file-upload-control"; prop.custom ("type", "file") ] ] ]
    // -- Vis --
    | NodeKind.DataGrid spec ->
        match spec.StaticRows with
        | Some sr ->
            // Phase 393 — static read-only mode: SSR renders the full semantic <table> statically
            // (byte-identical to the retired Table), NOT a hydration placeholder.
            let headerCells =
                [ for h in sr.Headers -> Html.th [ prop.className "fuaran-table-header"; prop.text (renderText ctx h) ] ]

            let bodyRows =
                [ for row in sr.Rows ->
                      Html.tr
                          [ prop.className "fuaran-table-row"
                            prop.children
                                [ for cell in row ->
                                      Html.td [ prop.className "fuaran-table-cell"; prop.text (renderText ctx cell) ] ] ] ]

            Html.table
                [ prop.className "fuaran-table"
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
                [ prop.className "fuaran-grid fuaran-grid-ssr-placeholder"
                  prop.custom ("data-fuaran-ssr-placeholder", "DataGrid")
                  prop.custom ("data-fuaran-row-count", string rowCount)
                  prop.text (sprintf "[Grid: %d rows — hydrates client-side]" rowCount) ]
    | NodeKind.Chart spec ->
        match BindingResolver.resolve<Row seq> ctx.Sources spec.Source, spec.Kind with
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
                            Html.div [ prop.className "fuaran-chart-title"; prop.text (renderText ctx title) ]
                        | None -> Html.none
                        Html.div
                            [ prop.className "fuaran-chart-placeholder"
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
        // Server has no throws to catch — render the protected child subtree
        // directly. The Fallback is the client-runtime degradation path.
        renderNode (depth + 1) ctx spec.Child
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
            | on -> BindingResolver.tryResolve ctx.Sources on |> Option.map box

        let matched =
            match currentValue with
            | Some v ->
                let valueStr = if isNull v then "" else string v

                spec.Cases
                |> List.tryPick (fun c -> if c.Match = valueStr then Some c.Child else None)
            | None -> None

        match matched with
        | Some child -> renderNode (depth + 1) ctx child
        | None -> renderNode (depth + 1) ctx spec.Default
    | NodeKind.FragmentDecl _ ->
        // Zero-paint: the decl is a template, not visible output.
        Html.none
    | NodeKind.FragmentRef spec ->
        match Map.tryFind spec.Name ctx.Fragments with
        | Some body -> renderNode (depth + 1) ctx body
        | None ->
            let raw = spec.Name

            Html.div
                [ prop.className "fuaran-fragment-unresolved-placeholder"
                  prop.custom ("data-fuaran-fragment-unresolved", raw)
                  prop.text (sprintf "[fuaran:fragment unresolved '%s']" raw) ]
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
        Html.div
            [ prop.className "fuaran-mount-boundary"
              prop.custom ("data-fuaran-mount-scope", spec.ScopeId)
              prop.children
                  [ Html.div
                        [ prop.className "fuaran-mount-placeholder"
                          prop.text (sprintf "[fuaran:mount '%s' — guest loader not attached]" spec.ScopeId) ] ] ]

// ─── Custom escape hatch (Phase 141 — server registry seam) ─────────────────

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
                      [ prop.className "fuaran-custom-props"
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
                  [ Html.div [ prop.className "fuaran-custom-label"; prop.text described.Label ] ]
                  @ summary
                  @ props
                  @ defects
              ) ]

    let placeholder () =
        match ctx.Cards.TryFind(moduleId, componentId) with
        | None -> identityOnly ()
        | Some card -> carded (Fuaran.UI.CustomCard.describe contentHash props card)

    // Phase 783 — SCOPE-CONSTRAINED lookup. No cross-scope fallback.
    match Registry.tryRenderInScope ctx.Scope moduleId componentId ctx.Customs with
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
            Registry.tryHashInScope ctx.Scope moduleId componentId ctx.Customs

        let floor = CustomHash.currentCustomHashFloor ()

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
            | _ ->
                // Unverifiable: refuse under an enforcing floor, render otherwise.
                if floor = HashStrictness.AdvisoryWarning then
                    None
                else
                    Some floor

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

// ─── Layouts ────────────────────────────────────────────────────────────────

// ─── Displays ────────────────────────────────────────────────────────────────

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
        | FormFieldKind.Text(v, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.text))

            // Phase 864 — a declared `format` rule retypes the control so the
            // BROWSER enforces it (`<input type=email|url|tel>`). Absent, the
            // control stays `text` and the emitted markup is byte-identical to
            // a pre-864 tree.
            let t =
                match field.Rule |> Option.bind (fun r -> r.Format) with
                | Some TextFormat.Email -> "email"
                | Some TextFormat.Url -> "url"
                | Some TextFormat.Tel -> "tel"
                | None -> "text"

            t, (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.Number(v, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.number))

            "number",
            (BindingResolver.tryResolve ctx.Sources v
             |> Option.map string
             |> Option.defaultValue "")
        | FormFieldKind.RangedNumber(v, _, _, _, _) ->
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
        | FormFieldKind.TextArea(v, _, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.text))

            "textarea", (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.Choice _ -> "choice", ""
        | FormFieldKind.SegmentedChoice _ -> "segmented-choice", ""
        | FormFieldKind.Date(v, _, variant, _, _, _) ->
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.date))

            let t =
                match variant with
                | DateVariant.Date -> "date"
                | DateVariant.Time -> "time"
                | DateVariant.DateTime -> "datetime-local"

            t, (BindingResolver.tryResolve ctx.Sources v |> Option.defaultValue "")
        | FormFieldKind.DateRange(_, _, variant, _, _, _) ->
            // Phase 725 — the variant types BOTH ends; the pair itself is
            // rendered by the dedicated dual-input branch below, so the
            // single-control tuple carries only the type.
            let t =
                match variant with
                | DateVariant.Date -> "date"
                | DateVariant.Time -> "time"
                | DateVariant.DateTime -> "datetime-local"

            t, ""

    // `data-fuaran-field` is the per-field buffer marker (Phase 152 form policy):
    // the server-driven shim reads + harvests it (the live DOM value IS the
    // client-side buffer — policy (b)), and the Phase 156 field-error patch
    // addresses it. Additive, server-tier-only — the same shim-bridge pattern as
    // `data-tab-index` / `data-filter-name` / `data-step-index`.
    let control =
        match field.Kind with
        | FormFieldKind.DateRange(v, _, _, mn, mx, st) ->
            // Phase 725 — SSR parity with the client's dual-input range: two
            // native date/time inputs (per variant) over the pair's ends,
            // sharing the min/max/step attributes. Inert like every other
            // server-rendered control; the FROM end carries the per-field
            // buffer marker (the pair's addressable slot).
            let v =
                v
                |> Option.defaultValue (Binding.State(field.Id, Some Fuaran.UI.Defaults.ControlValueDefaults.dateRange))

            // The pair is the `DateRangePair` record since the swap (was a
            // `(from, to)` tuple); same defaults, same emitted markup.
            let current: DateRangePair =
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
                [ prop.className "fuaran-field-range"
                  prop.children
                      [ Html.input (
                            [ prop.className "fuaran-form-field-control fuaran-field-range-min"
                              prop.custom ("data-fuaran-field", field.Id)
                              prop.custom ("type", controlType)
                              prop.value fromV ]
                            @ constraintAttrs
                        )
                        Html.span [ prop.className "fuaran-field-range-sep"; prop.text "–" ]
                        Html.input (
                            [ prop.className "fuaran-form-field-control fuaran-field-range-max"
                              prop.custom ("type", controlType)
                              prop.value toV ]
                            @ constraintAttrs
                        ) ] ]
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
                      | Some c ->
                          let opText =
                              match c.Op with
                              | CompareOp.Eq -> "eq"
                              | CompareOp.Neq -> "neq"
                              | CompareOp.Lt -> "lt"
                              | CompareOp.Lte -> "lte"
                              | CompareOp.Gt -> "gt"
                              | CompareOp.Gte -> "gte"

                          let againstText =
                              match c.Against with
                              | Binding.State(key, _) -> key
                              | _ -> ""

                          prop.custom ("data-fuaran-field-compare", opText + ":" + againstText)
                      | None -> () ]

            match controlType with
            | "textarea" ->
                Html.textarea (
                    [ prop.className "fuaran-form-field-control"
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.value valueText ]
                    @ ruleAttrs
                )
            | _ ->
                Html.input (
                    [ prop.className "fuaran-form-field-control"
                      prop.custom ("data-fuaran-field", field.Id)
                      prop.custom ("type", controlType)
                      prop.value valueText ]
                    @ ruleAttrs
                )

    Html.label
        [ prop.className "fuaran-form-field"
          prop.children
              [ Html.span
                    [ prop.className "fuaran-form-field-label"
                      prop.text (renderText ctx field.Label) ]
                control ] ]

/// A filter rendered with its real control markup (the client renderer's
/// class vocabulary), inert server-side like every other input. Each control
/// carries `data-filter-name` so the live shim can bridge the changed/clicked
/// filter's identity across as `payload.name` (the server-driven Filters
/// resolution is name-addressed — one Filters node holds many filters).
and private renderFilterSpec (ctx: ServerRenderContext) (spec: FilterSpec<obj>) : ReactElement =
    // 0.2.0 filters-unification: the chip's control is an ordinary
    // FormFieldKind; class vocabulary keeps the four legacy filter families
    // for the shapes that map onto them, with sensible classes for the rest.
    let kindClass =
        match spec.Kind with
        | FormFieldKind.Text _
        | FormFieldKind.TextArea _ -> "text"
        | FormFieldKind.Range _ -> "range"
        | FormFieldKind.Toggle _ -> "toggle"
        | FormFieldKind.Choice _ -> "choice"
        | FormFieldKind.SegmentedChoice _ -> "segmented"
        | FormFieldKind.Number _
        | FormFieldKind.RangedNumber _ -> "number"
        | FormFieldKind.Checkbox _ -> "checkbox"
        | FormFieldKind.Date _ -> "date"
        // Phase 725 — a date range is a range chip whose ends are dates; it
        // reuses the existing `range` chip class rather than minting one.
        | FormFieldKind.DateRange _ -> "range"

    let labelText = renderText ctx spec.Label

    // Phase 596 — the value slots are `Binding<_> option` since the swap. An
    // absent (`None`) slot on a filter chip is the auto-bind form: substitute
    // exactly the binding the decoder used to synthesise —
    // `Binding.Filter(spec.Name, None)` — so SSR resolution is unchanged.
    let control =
        match spec.Kind with
        | FormFieldKind.Text(value, _)
        | FormFieldKind.TextArea(value, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.custom ("type", "text")
                  prop.placeholder labelText
                  prop.value current
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Number(value, _)
        | FormFieldKind.RangedNumber(value, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue 0.0

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.custom ("type", "number")
                  prop.value (string current)
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Checkbox(value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className "fuaran-filter-checkbox"
                  prop.custom ("type", "checkbox")
                  prop.custom ("data-filter-name", spec.Name)
                  if current then
                      prop.custom ("checked", "checked") ]
        // Phase 766 — the SSR twin. The `role`/`aria-checked` pair must appear
        // in the SERVER HTML too: a switch that only becomes a switch after
        // hydration is announced wrongly on first paint, and for a static
        // (never-hydrated) render it would never be announced at all.
        | FormFieldKind.Toggle(value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current =
                BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue false

            Html.input
                [ prop.className "fuaran-filter-toggle"
                  prop.custom ("type", "checkbox")
                  prop.custom ("role", "switch")
                  prop.custom ("aria-checked", (if current then "true" else "false"))
                  prop.custom ("data-filter-name", spec.Name)
                  if current then
                      prop.custom ("checked", "checked") ]
        | FormFieldKind.Date(value, _, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let current = BindingResolver.tryResolve ctx.Sources value |> Option.defaultValue ""

            Html.input
                [ prop.className "fuaran-filter-input"
                  prop.custom ("type", "date")
                  prop.value current
                  prop.custom ("data-filter-name", spec.Name) ]
        | FormFieldKind.Choice(options, value, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            let opts = resolveOptions ctx options

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
                [ prop.className "fuaran-filter-select"
                  prop.value (current |> Option.defaultValue "")
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children optionItems ]
        | FormFieldKind.Range(value, _, _, _, _) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            // The range value is the `RangePair` record since the swap (was a
            // `(min, max)` tuple); same defaults, same emitted markup.
            let current: RangePair =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue { Min = 0.0; Max = 0.0 }

            let minV, maxV = current.Min, current.Max

            Html.span
                [ prop.className "fuaran-filter-range"
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children
                      [ Html.input
                            [ prop.className "fuaran-filter-range-min"
                              prop.custom ("type", "number")
                              prop.value (string minV) ]
                        Html.span [ prop.className "fuaran-filter-range-sep"; prop.text "–" ]
                        Html.input
                            [ prop.className "fuaran-filter-range-max"
                              prop.custom ("type", "number")
                              prop.value (string maxV) ] ] ]
        | FormFieldKind.DateRange(value, _, variant, mn, mx, st) ->
            // Phase 725 — the date-range chip, inert: two native date/time
            // inputs over ONE filter param carrying the whole pair. Mirrors
            // the client renderer's shape + class vocabulary.
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))

            let current: DateRangePair =
                BindingResolver.tryResolve ctx.Sources value
                |> Option.defaultValue { From = ""; To = "" }

            let fromV, toV = current.From, current.To

            let inputType =
                match variant with
                | DateVariant.Date -> "date"
                | DateVariant.Time -> "time"
                | DateVariant.DateTime -> "datetime-local"

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
                [ prop.className "fuaran-filter-range"
                  prop.custom ("data-filter-name", spec.Name)
                  prop.children
                      [ Html.input (
                            [ prop.className "fuaran-filter-input fuaran-filter-range-min"
                              prop.custom ("type", inputType)
                              prop.value fromV ]
                            @ constraintAttrs
                        )
                        Html.span [ prop.className "fuaran-filter-range-sep"; prop.text "–" ]
                        Html.input (
                            [ prop.className "fuaran-filter-input fuaran-filter-range-max"
                              prop.custom ("type", inputType)
                              prop.value toV ]
                            @ constraintAttrs
                        ) ] ]
        | FormFieldKind.SegmentedChoice(options, value, _, orientation) ->
            let value = value |> Option.defaultValue (Binding.Filter(spec.Name, None))
            renderSegmentedFilter ctx spec.Name options value orientation

    Html.label
        [ prop.className (Css.filter kindClass)
          prop.children
              [ Html.span [ prop.className "fuaran-filter-label"; prop.text labelText ]
                control ] ]

/// The segmented-control / radio-group filter, inert. Mirrors the client
/// `renderSegmentedChoiceCore` shapes (id namespace = the filter's `Name`):
/// Horizontal — `role="radiogroup"` of `role="radio"` buttons, each carrying
/// `data-filter-value` so the shim bridges a click to `payload.value`;
/// Vertical — `<fieldset>` of native radio inputs (a change event carries the
/// chosen value natively).
and private renderSegmentedFilter
    (ctx: ServerRenderContext)
    (idNamespace: string)
    (options: Binding<SelectOption list>)
    (value: Binding<string>)
    (orientation: Orientation)
    : ReactElement =
    let opts = resolveOptions ctx options

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
                [ prop.className "fuaran-segmented-option"
                  prop.custom ("type", "button")
                  prop.id (optionId index)
                  prop.custom ("aria-checked", (if isActive then "true" else "false"))
                  prop.role "radio"
                  prop.tabIndex (
                      if isActive then 0
                      elif activeIndex < 0 && index = 0 then 0
                      else -1
                  )
                  prop.custom ("data-filter-value", option.Value)
                  prop.text option.Label ]

        Html.div
            [ prop.className "fuaran-segmented-horizontal"
              prop.id idNamespace
              prop.role "radiogroup"
              prop.custom ("aria-orientation", "horizontal")
              prop.custom ("data-filter-name", idNamespace)
              prop.children [ for index, option in List.indexed opts -> optionButton index option ] ]
    | Orientation.Vertical ->
        let optionRow (index: int) (option: SelectOption) : ReactElement =
            let inputId = optionId index
            let isChecked = current = Some option.Value

            Html.div
                [ prop.className "fuaran-segmented-row"
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
            [ prop.className "fuaran-segmented-vertical"
              prop.custom ("aria-orientation", "vertical")
              prop.custom ("data-filter-name", idNamespace)
              prop.children (
                  Html.legend [ prop.className "fuaran-segmented-legend"; prop.text idNamespace ]
                  :: [ for index, option in List.indexed opts -> optionRow index option ]
              ) ]

// ─── Visualisations ──────────────────────────────────────────────────────────

// ─── Public entry points ─────────────────────────────────────────────────────

/// Build a `ServerRenderContext` with the one-shot fragment registry collected
/// from `node` and a host-supplied Custom-renderer registry. Empty fragment map
/// for trees that declare none (zero cost).
let mkContextWith
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    // Phase 1075 — the SEEDING PASS, at the server tier's single context choke
    // point so it lands on every entry point below by construction rather than
    // by each one remembering it (the same argument the egress default makes
    // just under this line).
    //
    // A `Binding.State` carrying a `defaultValue` declares the value of its
    // slot, so `ctx.Sources.State` is populated by the same pass the client
    // renderer runs and the two hosts' first render read an identical map —
    // which is what keeps hydration mismatch-free (`docs/SSR.md`). A
    // host-furnished value wins over a seed (charter §4); the fold lays the
    // caller's map over the seeds, never under them.
    let seeded =
        let seeds = Fuaran.UI.BindingWalk.stateSeeds node

        if Map.isEmpty seeds then
            sources
        else
            { sources with
                State = sources.State |> Map.fold (fun acc k v -> Map.add k v acc) seeds }

    { Sources = seeded
      Fragments = collectFragments Map.empty node
      Customs = customs
      // Phase 1108 — empty by default. A host that holds cards reaches them by
      // name (`mkContextWithCards`), exactly as it reaches a non-default egress
      // policy, so no existing entry point changes what it emits.
      Cards = Fuaran.UI.CustomCardStore.Empty
      Scope = None
      // Phase 1026 — default-deny, reached-by-name opt-out
      // (`mkContextWithEgress`). This is the server tier's single context choke
      // point, so the default lands on every entry point below by construction
      // rather than by each one remembering it.
      EgressPolicy = Sanitize.denyNonLocalEgress }

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
    { mkContextWith customs sources node with
        EgressPolicy = egressPolicy }

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

/// Build a `ServerRenderContext` under a named render SCOPE (Phase 783) —
/// Custom-renderer lookup is then constrained to renderers registered for that
/// scope (`Registry.registerInScope`).
let mkContextInScope
    (scope: string)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : ServerRenderContext =
    { mkContextWith customs sources node with
        Scope = Some scope }

/// Build a `ServerRenderContext` with no Custom renderers registered (every
/// `Custom` node falls back to the labelled placeholder).
let mkContext (sources: BindingResolver.BindingSources) (node: Node<obj>) : ServerRenderContext =
    mkContextWith Registry.empty sources node

/// Render a `Node<obj>` tree to a `ReactElement` for host composition (e.g.
/// embedding inside a host's own ViewEngine document layout). The host calls
/// `Feliz.ViewEngine.Render.htmlView` (or `htmlDocument`) when ready.
let renderToElement (sources: BindingResolver.BindingSources) (node: Node<obj>) : ReactElement =
    renderNode 1 (mkContext sources node) node

/// Render a `Node<obj>` tree to an HTML string on plain .NET. The body-fragment
/// HTML only — the host owns `<html>` / `<head>` / meta / the reference CSS.
let render (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.htmlView (renderToElement sources node)

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
    Render.htmlView (renderNode 1 (mkContextWith customs sources node) node)

/// `renderWith` under an EXPLICIT destination policy (Phase 1026) — the named
/// opt-out for a host composing this tier without going through the Giraffe
/// handlers (which carry `FuaranGiraffeOptions.EgressPolicy` instead).
let renderWithEgress
    (egressPolicy: Sanitize.EgressPolicy)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode 1 (mkContextWithEgress egressPolicy customs sources node) node)

/// Render under a named render SCOPE with a host-supplied registry (Phase 783).
/// Only renderers registered for `scope` are reachable from the tree.
let renderWithInScope
    (scope: string)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode 1 (mkContextInScope scope customs sources node) node)

/// `renderWith` plus a host-supplied CONTRACT-CARD store (Phase 1108) — an
/// unregistered `Custom` node whose identity the store knows renders the
/// card-derived labelled placeholder instead of the identity-only one.
let renderWithCards
    (cards: Fuaran.UI.CustomCardStore)
    (customs: Registry.ServerCustomRendererRegistry)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    Render.htmlView (renderNode 1 (mkContextWithCards cards customs sources node) node)

/// A `<style>` element carrying the Phase 12.K `Theme` → CSS-variable `:root`
/// projection — parity with the client `themeStyleElement`. The host mounts
/// this once (typically in `<head>`); the renderer's class vocabulary keys off
/// these variables.
let themeStyleElement (theme: Theme) : ReactElement =
    Html.style [ prop.dangerouslySetInnerHTML (Theme.toCss theme) ]

/// Render the theme `:root` style block followed by the tree's body HTML, as a
/// single string. Convenience for a host that wants both in one emission.
let renderWithTheme (theme: Theme) (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    Render.htmlView (themeStyleElement theme) + render sources node

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
