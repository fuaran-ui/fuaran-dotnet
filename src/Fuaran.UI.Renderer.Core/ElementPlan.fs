module Fuaran.UI.Renderer.ElementPlan

open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  ElementPlan — what a node renders as, computed once for both renderers
//  (Phase 2108).
//
//  The client renderer (Feliz, React) and the server renderer (Feliz.ViewEngine,
//  an HTML string) used to be two hand-synchronised copies of one per-kind
//  match, coupled only by parity tests. A PLAN is the element tree a kind
//  renders — tag, attributes in emission order, inline style declarations,
//  content — as plain data, computed here from the node and its resolved
//  values, and emitted twice: once by each renderer's `Emit` module. The two
//  element libraries share the shape this needs (an element is a tag plus a
//  property list; a property is an attribute, the children, or the text), so
//  one plan is emittable by both.
//
//  `'E` is the EMITTER's own element type. Whatever a renderer has already
//  rendered for itself — a nested node, a kind body, a state slot — rides into
//  a plan as `Plan.Host`, so this module never names either element library.
//
//  What a plan does NOT carry, by design:
//    * event handlers — attachment is the client emitter's act; the server
//      renders every affordance inert (no runtime to dispatch to);
//    * hydration markers, render-failure telemetry, the tooltip dismissal
//      listener, depth tracking and the CSP collector — each renderer's own
//      concern, kept in its `Render.fs`.
//
//  FSharp.Core only and Fable-portable, like the rest of this project.
// ============================================================================

/// One property of a planned element, in emission order.
[<RequireQualifiedAccess>]
type Prop =
    /// An HTML attribute, spelled as it lands in the document (`class`, not
    /// `className`; `aria-label`, `data-*`, `tabindex`).
    | Attr of name: string * value: string
    /// Inline style declarations in CANONICAL CSS spelling (kebab-case keys,
    /// the `Csp.Declarations` values). The client emitter camel-cases the keys
    /// for React's style object; the server writes them verbatim.
    | Style of declarations: (string * string) list
    /// A boolean attribute that is present (Phase 2278): `disabled`, `hidden`,
    /// `open`. Each emitter writes it in its element library's own boolean
    /// spelling, which is what keeps the served HTML byte-identical to the
    /// pre-plan emission and gives React a real boolean (an empty string is a
    /// FALSE boolean to React, so `hidden=""` written as a plain attribute was
    /// dropped on the client).
    | Flag of name: string
    /// A named point where the CLIENT attaches behaviour (Phase 2278). The plan
    /// names the point and the item it belongs to (`"tab-select"`, `"3"`); the
    /// client emitter asks the arm's resolver closure for the handler
    /// properties, so the plan carries no handler type. The server emitter
    /// writes nothing for it: every affordance it serves is inert.
    | Behaviour of point: string * key: string

/// What a planned element contains.
[<RequireQualifiedAccess>]
type Content<'E> =
    /// No children at all.
    | Empty
    /// A single escaped text child.
    | Text of string
    /// Child plans, in document order.
    | Children of Plan<'E> list
    /// Pre-built markup from a shared Core builder (Markdown, DrawingSvg,
    /// MathMl, CodeHighlight). The client emitter passes it through Trusted
    /// Types; the server writes it raw.
    | InnerHtml of string

/// A planned element tree.
and [<RequireQualifiedAccess>] Plan<'E> =
    | Element of ElementSpec<'E>
    /// A bare text node.
    | TextNode of string
    /// Nothing at all (the empty element).
    | Nothing
    /// An element the emitting renderer has already built for itself.
    | Host of 'E

/// One planned element.
and ElementSpec<'E> =
    {
        Tag: string
        Props: Prop list
        /// The reconciliation key a repeated child carries. React reads it; it is
        /// not a DOM attribute, so the server emitter drops it.
        Key: string option
        Content: Content<'E>
    }

[<RequireQualifiedAccess>]
module Plan =

    /// An element with no key.
    let element (tag: string) (props: Prop list) (content: Content<'E>) : Plan<'E> =
        Plan.Element
            { Tag = tag
              Props = props
              Key = None
              Content = content }

    /// An element carrying only a class and a text child — the commonest leaf.
    let classText (tag: string) (className: string) (text: string) : Plan<'E> =
        element tag [ Prop.Attr("class", className) ] (Content.Text text)

    /// An element carrying only a class and child plans.
    let classChildren (tag: string) (className: string) (children: Plan<'E> list) : Plan<'E> =
        element tag [ Prop.Attr("class", className) ] (Content.Children children)

    /// Attribute pairs as plan properties.
    let attrs (pairs: (string * string) list) : Prop list = pairs |> List.map Prop.Attr

    /// `Some x` → the plan `f x`; `None` → nothing.
    let ofOption (f: 'a -> Plan<'E>) (value: 'a option) : Plan<'E> =
        match value with
        | Some v -> f v
        | None -> Plan.Nothing

/// The tags an HTML serialiser writes as void elements (no closing tag). The
/// server emitter routes these through its void-element constructor, which is
/// the set its element library's own `Html.*` members use.
let voidTags: Set<string> =
    Set.ofList
        [ "base"
          "br"
          "col"
          "hr"
          "img"
          "input"
          "link"
          "meta"
          "param"
          "source"
          "track"
          "wbr" ]

/// `foo-bar-baz` → `fooBarBaz`: a canonical CSS property name in the spelling
/// React's style object requires. Custom properties (`--x`) pass unchanged.
let camelCaseProperty (property: string) : string =
    if property.StartsWith "--" then
        property
    else
        let parts = property.Split '-'

        parts
        |> Array.mapi (fun i part ->
            if i = 0 || part.Length = 0 then
                part
            else
                string (System.Char.ToUpperInvariant part.[0]) + part.Substring 1)
        |> String.concat ""

// ─── Continuous-value style sites (Phase 1545, shared since Phase 2108) ──────

/// The class suffix and the inline declarations one continuous-value site
/// emits, under the render's CSP posture. `declarations` are the canonical
/// pairs from `Csp.Declarations`. Under `Permissive` they are the element's
/// inline style and the suffix is empty; under `Strict` there is no inline
/// style, the suffix names the generated class, and the declarations are
/// registered in `collector` when the renderer holds one (the server, which
/// writes the stylesheet; the client only has to NAME the rule the server
/// wrote).
let styleSite
    (csp: Csp.CspMode)
    (collector: Csp.StyleCollector option)
    (nodeId: string)
    (slot: string)
    (declarations: (string * string) list)
    : string * Prop list =
    if List.isEmpty declarations then
        "", []
    elif Csp.isStrict csp then
        let className = Csp.generatedClass nodeId slot declarations

        match collector with
        | Some c -> c.Register(className, declarations)
        | None -> ()

        " " + className, []
    else
        "", [ Prop.Style declarations ]

// ─── The per-node wrapper (Stage 1) ────────────────────────────────────────

/// Where a node's wrapper-level attributes go, decided once for both
/// renderers.
type WrapperRouting =
    {
        /// The wrapper's class string (kind/style class, motion, tooltip hook).
        ClassName: string
        /// Attributes the wrapper carries after `id` / `data-fuaran-node-id` /
        /// `class`, in emission order.
        WrapperAttrs: (string * string) list
        /// Attributes forwarded onto the kind body's own semantic element
        /// (Phase 951); `[]` for every kind that does not take them.
        SemanticAttrs: (string * string) list
        /// The resolved, non-blank tooltip hint, when the node declares one.
        TooltipText: string option
    }

[<RequireQualifiedAccess>]
module NodePlan =

    /// The node's `ExtraAttributes` that may reach the document: the
    /// `data-*` / `aria-*` allowlist (`Sanitize.sanitizeExtraAttributes`),
    /// key-sorted, and — defence in depth (Phase 788) — only names an HTML
    /// serialiser can write (`Sanitize.isSafeAttributeName`). An attribute NAME
    /// is written verbatim by the server's element library, which escapes values
    /// only; the client's React drops such a name itself, so applying the filter
    /// on both tiers changes nothing the client emitted.
    let extraAttributePairs (node: Node<'Msg>) : (string * string) list =
        match node.ExtraAttributes with
        | Some attrs ->
            attrs
            |> Sanitize.sanitizeExtraAttributes
            |> Map.toList
            |> List.filter (fun (k, _) -> Sanitize.isSafeAttributeName k)
        | None -> []

    /// Route the node's wrapper-level attributes.
    ///
    /// - Phase 1112: an EMPTY resolved tooltip emits nothing at all.
    /// - Phase 1114 / 1472: the bidi isolation is wrapper-side and FIRST.
    /// - Phase 951: a kind whose body IS the node's semantic element takes the
    ///   a11y attributes plus the `aria-*` half of the extras; the wrapper keeps
    ///   the `data-*` half. Every other kind: bidi, a11y, extras on the wrapper.
    /// - Phase 1112: the hint's `aria-describedby` rides the described element,
    ///   and where that is the wrapper it also gets the focus stop.
    let route (sources: BindingResolver.BindingSources) (node: Node<'Msg>) : WrapperRouting =
        let style = node.Style |> Option.defaultValue Defaults.style
        let baseClassName = Theme.nodeClassName node.Kind style

        let tooltipText =
            node.Tooltip
            |> Option.map (BindingResolver.resolveTextSource sources)
            |> Option.filter (fun t -> t.Trim() <> "")

        let className =
            Css.nodeWrapper baseClassName (node.Motion |> Option.map Theme.motionVar) tooltipText.IsSome

        let a11y = Accessibility.accessibilityAttributes sources node.Accessibility
        let extras = extraAttributePairs node
        let bidi = Accessibility.bidiAttributes node.Kind style

        let wrapperAttrs0, semanticAttrs0 =
            if Accessibility.forwardsToSemanticElement node.Kind then
                let dataExtras, ariaExtras = Accessibility.partitionExtraAttributes extras
                bidi @ dataExtras, a11y @ ariaExtras
            else
                bidi @ a11y @ extras, []

        let wrapperAttrs, semanticAttrs =
            match tooltipText with
            | None -> wrapperAttrs0, semanticAttrs0
            | Some _ ->
                let hintId = Accessibility.tooltipHintId node.Id

                if Accessibility.tooltipRidesSemanticElement node.Kind then
                    wrapperAttrs0, Accessibility.withTooltipDescribedBy hintId semanticAttrs0
                else
                    (Accessibility.withTooltipDescribedBy hintId wrapperAttrs0)
                    @ [ "tabindex", "0" ],
                    semanticAttrs0

        { ClassName = className
          WrapperAttrs = wrapperAttrs
          SemanticAttrs = semanticAttrs
          TooltipText = tooltipText }

    /// The per-node wrapper: `id`, `data-fuaran-node-id` (the addressable-element
    /// marker the layout observer scans for), the class, the routed attributes,
    /// then the body and — when the node has a hint — the hint element as the
    /// body's sibling, which is what keeps it hoverable (WCAG 1.4.13) and reads
    /// thing-then-description.
    let wrapper (nodeId: string) (routing: WrapperRouting) (body: Plan<'E>) : Plan<'E> =
        let hint =
            match routing.TooltipText with
            | None -> []
            | Some t ->
                [ Plan.element
                      "span"
                      [ Prop.Attr("id", Accessibility.tooltipHintId nodeId)
                        Prop.Attr("class", Css.Class.Tooltip)
                        Prop.Attr("role", "tooltip") ]
                      (Content.Text t) ]

        Plan.element
            "div"
            ([ Prop.Attr("id", nodeId)
               Prop.Attr("data-fuaran-node-id", nodeId)
               Prop.Attr("class", routing.ClassName) ]
             @ Plan.attrs routing.WrapperAttrs)
            (Content.Children(body :: hint))

    /// The render guard's fallback element (Phase 2041): its text from
    /// `RenderParity`, so a node that fails on both tiers emits the same markup
    /// on both. `correlationId` is the FAILURE's id — the per-node guard's own
    /// (`RenderParity.renderFailureCorrelationId nodeId kindName`), or, for an
    /// `ErrorBoundary` whose fallback failed too, the PRIMARY failure's, so the
    /// placeholder joins the two reports.
    let renderFailure (nodeId: string) (kindName: string) (errorMessage: string) (correlationId: string) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.NodeFallback)
              Prop.Attr("data-fuaran-render-failed", "true")
              Prop.Attr("data-fuaran-render-correlation", correlationId) ]
            (Content.Text(RenderParity.renderFailureText nodeId kindName errorMessage))

    /// The marker emitted in place of a subtree nested past
    /// `WireLimits.MaxDepth` (Phase 781).
    let depthExceeded (nodeId: string) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("id", nodeId)
              Prop.Attr("data-fuaran-node-id", nodeId)
              Prop.Attr("data-fuaran-depth-exceeded", string WireLimits.MaxDepth)
              Prop.Attr("class", Css.Class.DepthExceeded)
              Prop.Attr("role", "note") ]
            (Content.Children [ Plan.TextNode RenderParity.depthExceededText ])

// ─── The leaf display kinds (Stage 2) ──────────────────────────────────────

/// The identity of one member of a repeated-children arm: a usable declared key
/// wins; anything else is the position, marked so it can never collide with a
/// real key. The empty string and the `"<closure>"` constant a decoded `RowKey`
/// projects are refused as identities — under the second every row would
/// answer one key, which is worse than positional reconciliation.
let reconciliationKey (declared: string option) (index: int) : string =
    match declared with
    | Some key when key <> "" && key <> "<closure>" -> key
    | _ -> "#" + string index

[<RequireQualifiedAccess>]
module DisplayPlan =

    /// The uniform icon hook: ONE empty placement element whose glyph NAME
    /// rides `data-icon` (never text content — the reference CSS ships no
    /// glyphs, and a host maps the name itself), `aria-hidden` because every
    /// icon-bearing spec pairs the icon with a visible label.
    let iconHook (kindClass: string) (name: string) : Plan<'E> =
        Plan.element
            "span"
            [ Prop.Attr("class", Css.iconPlacement kindClass)
              Prop.Attr("data-icon", name)
              Prop.Attr("aria-hidden", "true") ]
            Content.Empty

    /// The node a value-bearing kind renders INSTEAD of its body: the
    /// `OnLoading` slot while the value has not resolved, the `OnError` slot
    /// (with a deterministic correlation id) when it errored. `None` renders
    /// the body. One rule for both tiers — the server used to honour only
    /// `OnLoading` (Phase 2108 parity fix (d)).
    let stateSlot
        (nodeId: string)
        (state: StateBehaviour<'Msg>)
        (resolution: BindingResolver.Resolution<'T>)
        : Node<'Msg> option =
        match resolution, state.OnLoading, state.OnError with
        | BindingResolver.NotResolved, Some loading, _ -> Some loading
        | BindingResolver.Errored msg, _, Some onError ->
            Some(
                onError
                    { Kind = ErrorKind.BindingResolution
                      Message = msg
                      CorrelationId = Ids.deterministicCorrelationId nodeId }
            )
        | _ -> None

    /// A scalar's display text: the formatted value, the em-dash while it has
    /// not resolved, and the loud error / missing-translation sentinels.
    let scalarText (format: CellFormat) (resolution: BindingResolver.Resolution<float>) : string =
        match resolution with
        | BindingResolver.Resolved value -> Formatting.formatNumber format value
        | BindingResolver.NotResolved -> "—"
        | BindingResolver.Errored msg -> sprintf "(error: %s)" msg
        | BindingResolver.I18nUnresolved key -> sprintf "[i18n:%s]" key

    let heading (sources: BindingResolver.BindingSources) (spec: HeadingSpec) : Plan<'E> =
        // HTML heading levels are 1..6; anything outside that range falls to h6.
        let tag =
            match spec.Level with
            | 1
            | 2
            | 3
            | 4
            | 5 -> "h" + string spec.Level
            | _ -> "h6"

        Plan.classText
            tag
            (Css.heading (Css.headingVariant spec.Variant))
            (BindingResolver.resolveTextSource sources spec.Text)

    /// Phase 292 / 1032 — one deterministic GFM renderer, its link and image
    /// destinations under the render's egress policy.
    let markdown
        (egress: Sanitize.EgressPolicy)
        (sources: BindingResolver.BindingSources)
        (spec: MarkdownSpec)
        : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.Markdown) ]
            (Content.InnerHtml(Markdown.toHtmlWithEgress egress (BindingResolver.resolveTextSource sources spec.Text)))

    let badge (sources: BindingResolver.BindingSources) (spec: BadgeSpec) : Plan<'E> =
        Plan.classText
            "span"
            (Css.badge (Css.badgeVariant spec.Variant))
            (BindingResolver.resolveTextSource sources spec.Label)

    /// A skeleton's rows are interchangeable placeholders with no identity
    /// beyond their position, so the index IS the key.
    let skeleton (spec: SkeletonSpec) : Plan<'E> =
        Plan.classChildren
            "div"
            Css.Class.Skeleton
            [ for i in 1 .. spec.Rows ->
                  Plan.Element
                      { Tag = "div"
                        Props = [ Prop.Attr("class", Css.Class.SkeletonRow) ]
                        Key = Some(reconciliationKey None i)
                        Content = Content.Empty } ]

    /// Phase 821 — the standalone icon. Decorative (`Label = None`) is
    /// `aria-hidden`; labelled is `role="img"` + `aria-label`.
    let icon (spec: IconSpec) : Plan<'E> =
        Plan.element
            "span"
            ([ Prop.Attr("class", Css.icon (Theme.iconSizeClass spec.Size) (Theme.toneVar spec.Tone))
               Prop.Attr("data-icon", spec.Icon) ]
             @ (match spec.Label with
                | Some label -> [ Prop.Attr("role", "img"); Prop.Attr("aria-label", label) ]
                | None -> [ Prop.Attr("aria-hidden", "true") ]))
            Content.Empty

    let metric
        (sources: BindingResolver.BindingSources)
        (resolution: BindingResolver.Resolution<float>)
        (spec: MetricSpec)
        : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        // Phase 867 — the trend element carries a SENTIMENT (sign and declared
        // polarity), not a constant; `tone` colours the tile alone.
        let trend =
            match spec.Trend with
            | Some trendBinding ->
                match BindingResolver.tryResolveScalarFloat sources trendBinding with
                | Some t ->
                    let sentiment, glyph = Theme.trendSentiment spec.TrendPolarity t

                    Plan.classChildren
                        "div"
                        (Css.metricTrend sentiment)
                        [ Plan.element
                              "span"
                              [ Prop.Attr("class", Css.Class.MetricTrendGlyph)
                                Prop.Attr("role", "img")
                                Prop.Attr("aria-label", sentiment) ]
                              (Content.Text glyph)
                          Plan.TextNode(
                              Formatting.formatNumber (spec.TrendFormat |> Option.defaultValue CellFormat.None) t
                          ) ]
                | None -> Plan.classText "div" Css.Class.MetricTrend ""
            | None -> Plan.Nothing

        Plan.classChildren
            "div"
            (Css.metric (Theme.toneVar spec.Tone))
            [ spec.Icon |> Plan.ofOption (iconHook Css.Class.MetricIcon)
              Plan.classText "div" Css.Class.MetricLabel (text spec.Label)
              Plan.classText "div" Css.Class.MetricValue (scalarText spec.Format resolution)
              trend
              spec.Subtext
              |> Plan.ofOption (fun s -> Plan.classText "div" Css.Class.MetricSubtext (text s)) ]

    /// A labelled TEXT fact tile — Metric's chrome for a TextSource value.
    let fact (sources: BindingResolver.BindingSources) (spec: FactSpec) : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        Plan.classChildren
            "div"
            (Css.fact (Theme.toneVar spec.Tone) (Css.factEmphasis spec.Emphasis))
            [ Plan.classText "div" Css.Class.FactLabel (text spec.Label)
              Plan.classChildren
                  "div"
                  Css.Class.FactValue
                  [ spec.Icon |> Plan.ofOption (iconHook Css.Class.FactIcon)
                    Plan.element "span" [] (Content.Text(text spec.Value)) ]
              spec.Help
              |> Plan.ofOption (fun h -> Plan.classText "div" Css.Class.FactHelp (text h)) ]

    /// The dismiss affordance is inert on BOTH tiers — no handler is attached
    /// on the client either (Phase 2108 parity fix (a): the server omitted it).
    let callout (sources: BindingResolver.BindingSources) (spec: CalloutSpec) : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        Plan.classChildren
            "div"
            (Css.callout (Theme.toneVar spec.Tone))
            [ spec.Icon |> Plan.ofOption (iconHook Css.Class.CalloutIcon)
              spec.Heading
              |> Plan.ofOption (fun h -> Plan.classText "div" Css.Class.CalloutHeading (text h))
              Plan.classText "div" Css.Class.CalloutBody (text spec.Body)
              if spec.Dismissable then
                  Plan.element
                      "button"
                      [ Prop.Attr("class", "fuaran-callout-dismiss")
                        Prop.Attr("aria-label", "Dismiss") ]
                      (Content.Text "×")
              else
                  Plan.Nothing ]

    /// The fill's width is a continuous-value style site (Phase 1545), placed
    /// by `styleSite` under the render's CSP posture. The caveat is parity fix
    /// (b): the server omitted it.
    let progress
        (sources: BindingResolver.BindingSources)
        (styleSite: string -> (string * string) list -> string * Prop list)
        (resolution: BindingResolver.Resolution<float>)
        (spec: ProgressSpec)
        : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        let fraction =
            match resolution with
            | BindingResolver.Resolved value -> value
            | _ -> 0.0

        let fillClass, fillProps =
            styleSite "progress-fill" (Csp.Declarations.progressFill fraction)

        Plan.classChildren
            "div"
            (Css.progress (Theme.toneVar spec.Tone) (Css.progressIndeterminate spec.Indeterminate))
            [ spec.Label
              |> Plan.ofOption (fun l -> Plan.classText "div" Css.Class.ProgressLabel (text l))
              Plan.classChildren
                  "div"
                  Css.Class.ProgressBar
                  [ Plan.element
                        "div"
                        (Prop.Attr("class", Css.Class.ProgressFill + fillClass) :: fillProps)
                        Content.Empty ]
              spec.Caveat
              |> Plan.ofOption (fun c -> Plan.classText "div" "fuaran-progress-caveat" (text c)) ]

    /// Phase 1098 — the shared `Sparkline -> Drawing` lowering, emitted through
    /// the same `DrawingSvg` builder; an unresolved or empty series keeps the
    /// em-dash element. `lowered` is the caller's lowering of the resolved
    /// series (the lowering lives in a package that depends on this one).
    let sparkline (sources: BindingResolver.BindingSources) (lowered: DrawingSpec option) : Plan<'E> =
        match lowered with
        | Some drawing ->
            Plan.element
                "div"
                [ Prop.Attr("class", Css.Class.Sparkline) ]
                (Content.InnerHtml(DrawingSvg.render sources (BindingResolver.resolveTextSource sources) drawing))
        | None -> Plan.classText "div" Css.Class.SparklineEmpty "—"

    /// Phase 525 — first-party inline SVG from the canonical Core builder.
    let drawing (sources: BindingResolver.BindingSources) (spec: DrawingSpec) : Plan<'E> =
        Plan.element
            "div"
            []
            (Content.InnerHtml(DrawingSvg.render sources (BindingResolver.resolveTextSource sources) spec))

    /// Label left, value right. The label block with its optional help is
    /// parity fix (c): the server emitted a flat label and no help.
    let labelValueRow
        (sources: BindingResolver.BindingSources)
        (resolution: BindingResolver.Resolution<float>)
        (spec: LabelValueRowSpec)
        : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        Plan.classChildren
            "div"
            (Css.labelValueRow (Css.labelValueRowEmphasis spec.Emphasis))
            [ Plan.classChildren
                  "div"
                  "fuaran-label-value-row-label-block"
                  [ Plan.classText "span" Css.Class.LabelValueRowLabel (text spec.Label)
                    spec.Help
                    |> Plan.ofOption (fun h -> Plan.classText "span" "fuaran-label-value-row-help" (text h)) ]
              Plan.classText "span" Css.Class.LabelValueRowValue (scalarText spec.Format resolution) ]

    /// Phase 287 — `<ol>` / `<ul>` of literal items in document order, so the
    /// position is the identity.
    let list (sources: BindingResolver.BindingSources) (spec: ListSpec) : Plan<'E> =
        let items =
            spec.Items
            |> List.mapi (fun i item ->
                Plan.Element
                    { Tag = "li"
                      Props = [ Prop.Attr("class", Css.Class.ListItem) ]
                      Key = Some(reconciliationKey None i)
                      Content = Content.Text(BindingResolver.resolveTextSource sources item) })

        if spec.Ordered then
            Plan.classChildren "ol" Css.Class.ListOrdered items
        else
            Plan.classChildren "ul" Css.Class.ListUnordered items

    /// Phase 289 — ALWAYS in the DOM (no portal); closed is the `hidden`
    /// attribute; `role="status"` + `aria-live="polite"` announce politely.
    let toast (sources: BindingResolver.BindingSources) (spec: ToastSpec) : Plan<'E> =
        let isOpen =
            BindingResolver.tryResolve sources spec.Open |> Option.defaultValue false

        Plan.element
            "div"
            ([ Prop.Attr("class", Css.toast (Theme.toneVar spec.Tone))
               Prop.Attr("role", "status")
               Prop.Attr("aria-live", "polite") ]
             @ (if isOpen then [] else [ Prop.Attr("hidden", "") ]))
            (Content.Children
                [ Plan.classText "span" Css.Class.ToastMessage (BindingResolver.resolveTextSource sources spec.Message)
                  if spec.Dismissable then
                      Plan.element
                          "button"
                          [ Prop.Attr("class", Css.Class.ToastDismiss)
                            Prop.Attr("type", "button")
                            Prop.Attr("aria-label", "Dismiss") ]
                          (Content.Text "×")
                  else
                      Plan.Nothing ])

    /// Phase 290 / 1854 — a deterministic `<pre><code>`; a language with a
    /// grammar gets the class-only highlighting tier, marked so a richer
    /// client-only highlighter can skip it.
    let codeBlock (spec: CodeBlockSpec) : Plan<'E> =
        let containerClass =
            if spec.LineNumbers then
                Css.Class.CodeblockNumbered
            else
                Css.Class.Codeblock

        let code =
            match CodeHighlight.highlight spec.Language spec.Code with
            | Some markup ->
                Plan.element
                    "code"
                    [ Prop.Attr("class", Css.codeBlockCode spec.Language)
                      Prop.Attr("data-highlighted", CodeHighlight.TierMarker) ]
                    (Content.InnerHtml markup)
            | None -> Plan.classText "code" (Css.codeBlockCode spec.Language) spec.Code

        Plan.element
            "div"
            ([ Prop.Attr("class", containerClass)
               Prop.Attr("data-language", spec.Language) ]
             @ (match spec.HighlightLines with
                | [] -> []
                | lines -> [ Prop.Attr("data-highlight-lines", String.concat "," (lines |> List.map string)) ]))
            (Content.Children
                [ if spec.Copyable then
                      Plan.element
                          "button"
                          [ Prop.Attr("class", Css.Class.CodeblockCopy)
                            Prop.Attr("type", "button")
                            Prop.Attr("aria-label", "Copy") ]
                          (Content.Text "Copy")
                  else
                      Plan.Nothing
                  Plan.classChildren "pre" Css.Class.CodeblockPre [ code ] ])

    /// Phase 658 — native MathML for the closed subset, the escaped source
    /// span otherwise; KaTeX upgrades either shape client-only, outside parity.
    let math (spec: MathSpec) : Plan<'E> =
        let displayStr, isBlock =
            match spec.Display with
            | MathDisplay.Block -> "block", true
            | MathDisplay.Inline -> "inline", false

        Plan.element
            (if isBlock then "div" else "span")
            [ Prop.Attr("class", Css.mathContainer isBlock)
              Prop.Attr("data-math-display", displayStr)
              Prop.Attr("data-fuaran-math-src", spec.Source) ]
            (match MathMl.translate spec.Source spec.Display with
             | Some markup -> Content.InnerHtml markup
             | None -> Content.Children [ Plan.classText "span" Css.Class.MathSource spec.Source ])

// ─── Container kinds (Phase 2278) ──────────────────────────────────────────

/// What a container kind's plan needs from the emitting renderer: the
/// resolved sources, the CSP posture with the renderer's style collector (the
/// server's; the client only names the rule the server wrote), the node id the
/// kind's ids and style classes derive from, and how to render a CHILD node —
/// the walk, which is each renderer's own (depth, error-boundary suspension,
/// fragment expansion), arriving as `Plan.Host`.
type PlanContext<'Msg, 'E> =
    {
        Sources: BindingResolver.BindingSources
        Csp: Csp.CspMode
        Collector: Csp.StyleCollector option
        /// The render's egress policy, for the URL-bearing kinds.
        Egress: Sanitize.EgressPolicy
        NodeId: string
        Child: Node<'Msg> -> Plan<'E>
    }

/// The behaviour attachment points the planned kinds declare. The client's
/// resolver closures match on these names; a test pins which kind declares
/// which, so a handler cannot be dropped silently.
[<RequireQualifiedAccess>]
module Attach =
    /// A tab button was activated; key = the tab index.
    [<Literal>]
    let TabSelect = "tab-select"

    /// A key went down on the tab bar (roving focus); key = "".
    [<Literal>]
    let TabsKeyDown = "tabs-keydown"

    /// A `<details>` toggled; key = "".
    [<Literal>]
    let DisclosureToggle = "disclosure-toggle"

    /// A step number was activated; key = the step index.
    [<Literal>]
    let StepSelect = "step-select"

    /// The dismiss button of a modal / popover; key = "".
    [<Literal>]
    let ModalDismiss = "modal-dismiss"

    /// A click on a dismissable modal's overlay; key = "".
    [<Literal>]
    let ModalOverlay = "modal-overlay"

    /// A key went down on a tree row; key = the item id (Phase 2290).
    [<Literal>]
    let TreeKeyDown = "tree-keydown"

    /// A tree row's label was clicked; key = the item id (Phase 2290).
    [<Literal>]
    let TreeLabelClick = "tree-label-click"

    /// A click inside a lowered chart, delegated to its marks; key = ""
    /// (Phase 2290).
    [<Literal>]
    let ChartPointClick = "chart-point-click"

    /// A grid's export button was activated; key = "" (Phase 2290).
    [<Literal>]
    let GridExport = "grid-export"

    /// A map marker was clicked; key = its index (Phase 2290).
    [<Literal>]
    let MarkerClick = "marker-click"

    /// Every attachment point a plan declares, in document order, with its
    /// key — what the attachment-point pins compare.
    let rec points (p: Plan<'E>) : (string * string) list =
        match p with
        | Plan.Element spec ->
            (spec.Props
             |> List.choose (function
                 | Prop.Behaviour(point, key) -> Some(point, key)
                 | _ -> None))
            @ (match spec.Content with
               | Content.Children cs -> cs |> List.collect points
               | _ -> [])
        | _ -> []

/// One tab of a `Tabs` set, resolved.
type ResolvedTab =
    { Label: string
      Icon: string option
      Disabled: bool }

/// A `Tabs` node resolved once for both tiers: its headers, the active index
/// and child, and the orientation. The client's keyboard model reads it too.
type TabsModel<'Msg> =
    { Tabs: ResolvedTab list
      ActiveIndex: int
      ActiveChild: Node<'Msg> option
      Vertical: bool }

[<RequireQualifiedAccess>]
module TabsModel =

    let ofSpec (sources: BindingResolver.BindingSources) (spec: TabsSpec<'Msg>) : TabsModel<'Msg> =
        // A child without a declared header is labelled by its Card heading,
        // else by its id.
        let labelFromChild (child: Node<'Msg>) : string =
            match child.Kind with
            | NodeKind.Box { Role = BoxRole.Card
                             Heading = Some h } -> BindingResolver.resolveTextSource sources h
            | _ -> child.Id

        let tabs =
            match spec.TabHeaders with
            | Some headers ->
                headers
                |> List.map (fun h ->
                    { Label = BindingResolver.resolveTextSource sources h.Label
                      Icon = h.Icon
                      Disabled =
                        h.Disabled
                        |> Option.bind (BindingResolver.tryResolve sources)
                        |> Option.defaultValue false })
            | None ->
                spec.Children
                |> List.map (fun child ->
                    { Label = labelFromChild child
                      Icon = None
                      Disabled = false })

        // An `ActiveTag` that resolves to a declared tag wins over `ActiveIndex`;
        // the index is clamped to the children.
        let resolvedFromTag =
            match spec.TabTags, spec.ActiveTag with
            | Some tags, Some tagBinding ->
                BindingResolver.tryResolve sources tagBinding
                |> Option.bind (fun tag -> tags |> List.tryFindIndex ((=) tag))
            | _ -> None

        let activeIndex =
            resolvedFromTag
            |> Option.orElseWith (fun () -> BindingResolver.tryResolve sources spec.ActiveIndex)
            |> Option.defaultValue 0
            |> max 0
            |> min (max 0 (spec.Children.Length - 1))

        { Tabs = tabs
          ActiveIndex = activeIndex
          ActiveChild =
            spec.Children
            |> List.tryItem activeIndex
            |> Option.orElseWith (fun () -> spec.Children |> List.tryHead)
          Vertical = spec.Orientation = Orientation.Vertical }

    /// The next enabled tab from `start` in direction `dir` (±1), wrapping;
    /// `start` itself when every other tab is disabled.
    let nextEnabled (model: TabsModel<'Msg>) (start: int) (dir: int) : int =
        let n = model.Tabs.Length

        if n = 0 then
            0
        else
            let rec loop (visited: int) (idx: int) =
                if visited >= n then
                    start
                else
                    let candidate = ((idx + dir) % n + n) % n

                    if model.Tabs[candidate].Disabled then
                        loop (visited + 1) candidate
                    else
                        candidate

            loop 0 start

    /// The tab a key on the tab bar moves to (WAI-ARIA tabs pattern): the
    /// orientation's arrows, Home / End, and Enter / Space re-selecting the
    /// active tab. `None` — the key is not the tab bar's.
    let keyTarget (model: TabsModel<'Msg>) (key: string) : int option =
        let prevKey = if model.Vertical then "ArrowUp" else "ArrowLeft"
        let nextKey = if model.Vertical then "ArrowDown" else "ArrowRight"

        if key = prevKey then
            Some(nextEnabled model model.ActiveIndex -1)
        elif key = nextKey then
            Some(nextEnabled model model.ActiveIndex 1)
        elif key = "Home" then
            Some(
                model.Tabs
                |> List.tryFindIndex (fun t -> not t.Disabled)
                |> Option.defaultValue 0
            )
        elif key = "End" then
            model.Tabs
            |> List.mapi (fun i t -> i, t)
            |> List.rev
            |> List.tryFind (fun (_, t) -> not t.Disabled)
            |> Option.map fst
            |> Option.defaultValue (max 0 (model.Tabs.Length - 1))
            |> Some
        elif key = "Enter" || key = " " then
            Some model.ActiveIndex
        else
            None

[<RequireQualifiedAccess>]
module LayoutPlan =

    let private children (pc: PlanContext<'Msg, 'E>) (nodes: Node<'Msg> list) : Content<'E> =
        Content.Children(nodes |> List.map pc.Child)

    /// A container div: class (plus the style site's class), the site's
    /// inline props, extra attributes, then the children. The slot comes
    /// first, where the strict-CSP slot inventory scan reads it.
    let private styled
        (pc: PlanContext<'Msg, 'E>)
        (slot: string)
        (className: string)
        (declarations: (string * string) list)
        (extra: Prop list)
        (nodes: Node<'Msg> list)
        : Plan<'E> =
        let siteClass, styleProps =
            styleSite pc.Csp pc.Collector pc.NodeId slot declarations

        Plan.element "div" ([ Prop.Attr("class", className + siteClass) ] @ styleProps @ extra) (children pc nodes)

    /// Phase 390 — the unified container. Role + layout drive the element and
    /// classes: Card → `<section>` with header + body; Dashboard / Group+Auto
    /// → dashboard div; Separator → `<hr>`; Grid / Masonry / Flex → a div whose
    /// continuous values ride the CSP style site. Phase 1473 — the print-break
    /// classes ride the container's own class in every arm.
    let box (pc: PlanContext<'Msg, 'E>) (spec: BoxSpec<'Msg>) : Plan<'E> =
        let brk = Theme.printBreakClasses spec.KeepTogether spec.BreakBefore

        match spec.Role, spec.Layout with
        | BoxRole.Card, _ ->
            Plan.element
                "section"
                [ Prop.Attr("class", Css.Class.LayoutCard + brk) ]
                (Content.Children
                    [ spec.Heading
                      |> Plan.ofOption (fun heading ->
                          Plan.classText
                              "header"
                              Css.Class.CardHeading
                              (BindingResolver.resolveTextSource pc.Sources heading))
                      Plan.element "div" [ Prop.Attr("class", Css.Class.CardBody) ] (children pc spec.Children) ])
        | BoxRole.Dashboard, _
        | BoxRole.Group, BoxLayout.Auto ->
            Plan.element "div" [ Prop.Attr("class", Css.Class.LayoutDashboard + brk) ] (children pc spec.Children)
        | BoxRole.Separator, _ ->
            Plan.element "hr" [ Prop.Attr("class", Css.Class.LayoutSeparator + brk) ] Content.Empty
        | BoxRole.Group, BoxLayout.Grid(cols, gridTemplateColumns, gridGap) ->
            // Column count rides inline style, not a `-cols-N` class, so hosts
            // need not pre-declare every N. Phase 1523 — the template passes the
            // CSS-value grammar on both tiers; a refused value is replaced and
            // marked visibly, the same way on every host.
            let declared =
                match gridTemplateColumns with
                | Some custom -> custom
                | None -> sprintf "repeat(%d, 1fr)" cols

            let templateColumns, refusalAttrs =
                Sanitize.sanitizeCssValueForSlot "grid-template-columns" declared

            styled
                pc
                "grid"
                (Css.Class.LayoutGrid + brk)
                (Csp.Declarations.grid templateColumns gridGap)
                (Plan.attrs refusalAttrs)
                spec.Children
        | BoxRole.Group, BoxLayout.Masonry(cols, masonryGap) ->
            // Phase 1082 — column-FILL through the CSS multi-column family.
            styled
                pc
                "masonry"
                (Css.Class.LayoutMasonry + brk)
                (Csp.Declarations.masonry cols masonryGap)
                []
                spec.Children
        | BoxRole.Group, BoxLayout.Flex(direction, flexWrap, flexGap) ->
            // `gap` emits only when set (Phase 459).
            styled
                pc
                "flex"
                (Css.layoutStack (Css.stackDirection direction) (Css.stackWrap flexWrap) + brk)
                (Csp.Declarations.flex flexGap)
                []
                spec.Children

    /// Two panes: the first child takes `Weight` (clamped to 0..1) of the row,
    /// the rest the remainder. Tolerant of any child count.
    let splitPanel (pc: PlanContext<'Msg, 'E>) (spec: SplitPanelSpec<'Msg>) : Plan<'E> =
        let weightLeft = max 0.0 (min 1.0 spec.Weight)

        let left, right =
            match spec.Children with
            | [] -> [], []
            | a :: rest -> [ a ], rest

        Plan.classChildren
            "div"
            Css.Class.LayoutSplitPanel
            [ styled pc "split-left" Css.Class.SplitPaneLeft (Csp.Declarations.splitPane weightLeft) [] left
              styled pc "split-right" Css.Class.SplitPaneRight (Csp.Declarations.splitPane (1.0 - weightLeft)) [] right ]

    /// The WAI-ARIA tabs pattern: a tablist of buttons (roving tabindex; a
    /// disabled tab is `aria-disabled` + `disabled` and attaches nothing) and
    /// the active child's tabpanel.
    let tabs (pc: PlanContext<'Msg, 'E>) (spec: TabsSpec<'Msg>) (model: TabsModel<'Msg>) : Plan<'E> =
        let tabId i = Ids.tab pc.NodeId i
        let panelId i = Ids.panel pc.NodeId i

        let tabButton (i: int) (t: ResolvedTab) : Plan<'E> =
            let isActive = i = model.ActiveIndex

            let cls =
                String.concat
                    " "
                    [ Css.Class.Tab
                      if isActive then
                          Css.Class.TabActive
                      if t.Disabled then
                          Css.Class.TabDisabled ]

            Plan.Element
                { Tag = "button"
                  // The tab's own DOM id is its identity under reconciliation.
                  Key = Some(tabId i)
                  Props =
                    [ Prop.Attr("id", tabId i)
                      Prop.Attr("class", cls)
                      Prop.Attr("role", "tab")
                      Prop.Attr("aria-selected", (if isActive then "true" else "false"))
                      Prop.Attr("aria-controls", panelId i)
                      Prop.Attr("tabIndex", (if isActive then "0" else "-1"))
                      Prop.Attr("data-tab-index", string i)
                      if t.Disabled then
                          Prop.Attr("aria-disabled", "true")
                          Prop.Flag "disabled"
                      else
                          Prop.Behaviour(Attach.TabSelect, string i) ]
                  Content =
                    Content.Children
                        [ t.Icon |> Plan.ofOption (DisplayPlan.iconHook Css.Class.TabIcon)
                          Plan.classText "span" Css.Class.TabLabel t.Label ] }

        Plan.classChildren
            "div"
            (Css.layoutTabs (Css.tabsOrientation spec.Orientation))
            [ Plan.element
                  "div"
                  [ Prop.Attr("class", Css.Class.TabsBar)
                    Prop.Attr("role", "tablist")
                    Prop.Attr("aria-orientation", (if model.Vertical then "vertical" else "horizontal"))
                    Prop.Behaviour(Attach.TabsKeyDown, "") ]
                  (Content.Children(model.Tabs |> List.mapi tabButton))
              Plan.classChildren
                  "div"
                  Css.Class.TabsPanels
                  [ model.ActiveChild
                    |> Plan.ofOption (fun child ->
                        Plan.element
                            "div"
                            [ Prop.Attr("id", panelId model.ActiveIndex)
                              Prop.Attr("role", "tabpanel")
                              Prop.Attr("aria-labelledby", tabId model.ActiveIndex)
                              Prop.Attr("tabIndex", "0")
                              Prop.Attr("class", Css.Class.TabsPanel) ]
                            (Content.Children [ pc.Child child ])) ] ]

    let summaryList (pc: PlanContext<'Msg, 'E>) (spec: SummaryListSpec<'Msg>) : Plan<'E> =
        Plan.classChildren
            "section"
            Css.Class.LayoutSummaryList
            [ spec.Heading
              |> Plan.ofOption (fun heading ->
                  Plan.classText
                      "header"
                      Css.Class.SummaryListHeading
                      (BindingResolver.resolveTextSource pc.Sources heading))
              Plan.element "div" [ Prop.Attr("class", Css.Class.SummaryListBody) ] (children pc spec.Children) ]

    /// A native `<details>`: open from the bound `Open`, else `DefaultOpen`.
    let disclosure (pc: PlanContext<'Msg, 'E>) (spec: DisclosureSpec<'Msg>) : Plan<'E> =
        let isOpen =
            BindingResolver.tryResolve pc.Sources spec.Open
            |> Option.defaultValue spec.DefaultOpen

        Plan.element
            "details"
            [ Prop.Attr("class", Css.Class.LayoutDisclosure)
              if isOpen then
                  Prop.Flag "open"
              Prop.Behaviour(Attach.DisclosureToggle, "") ]
            (Content.Children
                [ Plan.classText
                      "summary"
                      Css.Class.DisclosureSummary
                      (BindingResolver.resolveTextSource pc.Sources spec.Heading)
                  Plan.element "div" [ Prop.Attr("class", Css.Class.DisclosureBody) ] (children pc spec.Children) ])

    /// Numbered steps (a step IS its position, so the index is its key) and the
    /// active step's child. A step attaches selection only when `OnSelect` is
    /// declared.
    let stepper (pc: PlanContext<'Msg, 'E>) (spec: StepperSpec<'Msg>) : Plan<'E> =
        let activeIndex =
            BindingResolver.tryResolve pc.Sources spec.ActiveStep |> Option.defaultValue 0

        let step (i: int) : Plan<'E> =
            Plan.Element
                { Tag = "li"
                  Key = Some(reconciliationKey None i)
                  Props =
                    [ Prop.Attr(
                          "class",
                          (if i = activeIndex then
                               Css.Class.StepperStepActive
                           else
                               Css.Class.StepperStep)
                      )
                      Prop.Attr("data-step-index", string i)
                      if spec.OnSelect.IsSome then
                          Prop.Behaviour(Attach.StepSelect, string i) ]
                  Content = Content.Text(sprintf "%d" (i + 1)) }

        Plan.classChildren
            "div"
            Css.Class.LayoutStepper
            [ Plan.classChildren "ol" Css.Class.StepperNumbers (List.init spec.Children.Length step)
              Plan.classChildren
                  "div"
                  Css.Class.StepperBody
                  [ List.tryItem activeIndex spec.Children |> Plan.ofOption pc.Child ] ]

    /// Whether a `Modal` is open (an unresolved `Open` is closed).
    let modalOpen (sources: BindingResolver.BindingSources) (spec: ModalSpec<'Msg>) : bool =
        BindingResolver.tryResolve sources spec.Open |> Option.defaultValue false

    let private surfaceChildren
        (pc: PlanContext<'Msg, 'E>)
        (spec: ModalSpec<'Msg>)
        (headingClass: string)
        (dismissClass: string)
        (bodyClass: string)
        : Plan<'E> list =
        [ spec.Heading
          |> Plan.ofOption (fun h -> Plan.classText "h2" headingClass (BindingResolver.resolveTextSource pc.Sources h))
          if spec.Dismissable then
              Plan.element
                  "button"
                  [ Prop.Attr("class", dismissClass)
                    Prop.Attr("type", "button")
                    Prop.Attr("aria-label", "Close")
                    Prop.Behaviour(Attach.ModalDismiss, "") ]
                  (Content.Text "×")
          Plan.element "div" [ Prop.Attr("class", bodyClass) ] (children pc spec.Children) ]

    /// A popover's dialog surface — the child of its positioned outer element.
    let popoverDialog (pc: PlanContext<'Msg, 'E>) (spec: ModalSpec<'Msg>) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.PopoverSurface); Prop.Attr("role", "dialog") ]
            (Content.Children(
                surfaceChildren pc spec Css.Class.PopoverHeading Css.Class.PopoverDismiss Css.Class.PopoverBody
            ))

    /// A modal (overlay + dialog) or a popover (outer element + dialog surface).
    /// A closed one renders `hidden`. The client's popover outer element is its
    /// own positioning component, which writes the same attributes; it takes
    /// `popoverDialog` as its child.
    let modal (pc: PlanContext<'Msg, 'E>) (spec: ModalSpec<'Msg>) : Plan<'E> =
        let isOpen = modalOpen pc.Sources spec

        if spec.Modality = ModalityKind.Popover then
            Plan.element
                "div"
                [ Prop.Attr("class", Css.Class.Popover)
                  if not isOpen then
                      Prop.Flag "hidden"
                  match spec.Anchor with
                  | Some a -> Prop.Attr("data-fuaran-popover-anchor", a)
                  | None -> () ]
                (Content.Children [ popoverDialog pc spec ])
        else
            Plan.element
                "div"
                [ Prop.Attr("class", Css.Class.ModalOverlay)
                  if not isOpen then
                      Prop.Flag "hidden"
                  if spec.Dismissable then
                      Prop.Behaviour(Attach.ModalOverlay, "") ]
                (Content.Children
                    [ Plan.element
                          "div"
                          [ Prop.Attr("class", Css.Class.ModalDialog)
                            Prop.Attr("role", "dialog")
                            Prop.Attr("aria-modal", "true") ]
                          (Content.Children(
                              surfaceChildren pc spec Css.Class.ModalHeading Css.Class.ModalDismiss Css.Class.ModalBody
                          )) ])

    /// A focusable scroll container (keyboard-scrollable, WCAG 2.1.1) whose
    /// max extents ride the CSP style site.
    let scrollArea (pc: PlanContext<'Msg, 'E>) (spec: ScrollAreaSpec<'Msg>) : Plan<'E> =
        let siteClass, styleProps =
            styleSite pc.Csp pc.Collector pc.NodeId "scroll" (Csp.Declarations.scrollArea spec.MaxHeight spec.MaxWidth)

        Plan.element
            "div"
            ([ Prop.Attr("class", Css.scrollArea spec.Orientation + siteClass)
               Prop.Attr("tabindex", "0") ]
             @ styleProps)
            (children pc spec.Children)

// ─── Media kinds (Phase 2278) ──────────────────────────────────────────────

/// Link, Image, Media and Embed: the egress, srcset and class-map logic each
/// renderer used to carry twice "kept in step" by comment. `semanticAttrs` is
/// the node's a11y projection for the kinds whose body IS the semantic element
/// (Phase 951).
[<RequireQualifiedAccess>]
module MediaPlan =

    /// A media URL that passed egress, or `None` when it resolved empty or was
    /// refused — a refused secondary URL (a srcset candidate, a track, a
    /// poster) is dropped rather than marked.
    let private acceptedMediaUrl (pc: PlanContext<'Msg, 'E>) (src: Binding<string>) : string option =
        let resolved = BindingResolver.tryResolve pc.Sources src |> Option.defaultValue ""

        let safe, refusal =
            Sanitize.sanitizeUrlForEgress pc.Egress Sanitize.EgressClass.Media resolved

        if safe = "" || not (List.isEmpty refusal) then
            None
        else
            Some safe

    /// Every character as a decimal character reference — the protected
    /// e-mail link's obfuscation (Phase 1056).
    let private entityEncode (s: string) : string =
        s |> Seq.map (fun c -> "&#" + string (int c) + ";") |> String.concat ""

    /// The protected anchor's classes, as their own literal so the class
    /// vocabulary scan reads them.
    let private protectedLinkClass = "fuaran-link fuaran-link-protected"

    /// A hyperlink through the hyperlink egress class and the target / rel
    /// sanitiser. A protected `mailto:` link is written as entity-encoded
    /// markup inside its wrapper, so a scraper reading the source finds no
    /// plain address; the document it builds is the same anchor on both tiers.
    let link (pc: PlanContext<'Msg, 'E>) (spec: LinkSpec) (semanticAttrs: (string * string) list) : Plan<'E> =
        let resolvedHref =
            BindingResolver.tryResolve pc.Sources spec.Href |> Option.defaultValue ""

        let safeHref, egressAttrs =
            Sanitize.sanitizeUrlForEgress pc.Egress Sanitize.EgressClass.Hyperlink resolvedHref

        let safeTarget, safeRel = Sanitize.sanitizeLinkAnchor spec.Target spec.Rel
        let label = BindingResolver.resolveTextSource pc.Sources spec.Label

        match spec.Protection with
        | Some LinkProtection.Email when safeHref.StartsWith("mailto:", System.StringComparison.Ordinal) ->
            Plan.element
                "span"
                ([ Prop.Attr("class", Css.Class.LinkProtectedWrap) ] @ Plan.attrs semanticAttrs)
                (Content.InnerHtml(
                    "<a class=\""
                    + protectedLinkClass
                    + "\" href=\""
                    + entityEncode safeHref
                    + "\">"
                    + entityEncode label
                    + "</a>"
                ))
        | _ ->
            Plan.element
                "a"
                ([ Prop.Attr("class", Css.Class.Link)
                   Prop.Attr("href", safeHref)
                   match safeRel with
                   | Some rel -> Prop.Attr("rel", rel)
                   | None -> ()
                   match safeTarget with
                   | Some target -> Prop.Attr("target", target)
                   | None -> ()
                   if spec.Download then
                       Prop.Flag "download" ]
                 @ Plan.attrs semanticAttrs
                 @ Plan.attrs egressAttrs)
                (Content.Text label)

    /// An image: variant / fit / aspect classes, egress-checked `src`, the
    /// width-sorted `srcset` of the candidates that pass egress, lazy loading,
    /// an expand link (only for an accepted, unrefused source) and a caption.
    let image (pc: PlanContext<'Msg, 'E>) (spec: ImageSpec) (semanticAttrs: (string * string) list) : Plan<'E> =
        let resolvedSrc =
            BindingResolver.tryResolve pc.Sources spec.Src |> Option.defaultValue ""

        let safeSrc, egressAttrs =
            Sanitize.sanitizeUrlForEgress pc.Egress Sanitize.EgressClass.Media resolvedSrc

        let srcSetAttrs =
            let candidates =
                spec.SrcSet
                |> List.sortBy _.Width
                |> List.choose (fun entry ->
                    acceptedMediaUrl pc entry.Src
                    |> Option.map (fun safe -> safe + " " + string entry.Width + "w"))

            match candidates with
            | [] -> []
            | xs -> [ "srcset", String.concat ", " xs; "sizes", "100vw" ]

        let loadingAttrs =
            match spec.Loading with
            | ImageLoading.Eager -> []
            | ImageLoading.Lazy -> [ "loading", "lazy" ]

        let img =
            Plan.element
                "img"
                ([ Prop.Attr(
                       "class",
                       Css.imageVariant spec.Variant
                       + Css.imageFit spec.Fit
                       + Css.imageAspect spec.AspectRatio
                   )
                   Prop.Attr("src", safeSrc)
                   Prop.Attr("alt", BindingResolver.resolveTextSource pc.Sources spec.Alt) ]
                 @ Plan.attrs srcSetAttrs
                 @ Plan.attrs loadingAttrs
                 @ Plan.attrs semanticAttrs
                 @ Plan.attrs egressAttrs)
                Content.Empty

        let expandable =
            if spec.Expandable && safeSrc <> "" && List.isEmpty egressAttrs then
                Plan.element
                    "a"
                    [ Prop.Attr("class", Css.Class.ImageExpand)
                      Prop.Attr("href", safeSrc)
                      Prop.Attr("data-fuaran-expandable", "") ]
                    (Content.Children [ img ])
            else
                img

        match spec.Caption with
        | None -> expandable
        | Some caption ->
            Plan.classChildren
                "figure"
                Css.Class.ImageFigure
                [ expandable
                  Plan.classText
                      "figcaption"
                      Css.Class.ImageFigureCaption
                      (BindingResolver.resolveTextSource pc.Sources caption) ]

    let private trackKindToken (k: TrackKind) : string =
        match k with
        | TrackKind.Subtitles -> "subtitles"
        | TrackKind.Captions -> "captions"
        | TrackKind.Descriptions -> "descriptions"
        | TrackKind.Chapters -> "chapters"

    /// Video or audio: egress-checked source, the timed-text tracks that pass
    /// egress (the first `Default` per kind keeps it — HTML allows one), an
    /// accepted poster, autoplay always muted, and an optional transcript
    /// disclosure beside the element.
    let media (pc: PlanContext<'Msg, 'E>) (spec: MediaSpec) (semanticAttrs: (string * string) list) : Plan<'E> =
        let resolvedSrc =
            BindingResolver.tryResolve pc.Sources spec.Src |> Option.defaultValue ""

        let safeSrc, egressAttrs =
            Sanitize.sanitizeUrlForEgress pc.Egress Sanitize.EgressClass.Media resolvedSrc

        let label = BindingResolver.resolveTextSource pc.Sources spec.Label

        let tracks =
            spec.Tracks
            |> List.fold
                (fun (claimed: Set<string>, acc) (t: TrackEntry) ->
                    match acceptedMediaUrl pc t.Src with
                    | None -> claimed, acc
                    | Some safe ->
                        let token = trackKindToken t.Kind
                        let takesDefault = t.Default && not (Set.contains token claimed)

                        let track =
                            Plan.element
                                "track"
                                [ Prop.Attr("kind", token)
                                  Prop.Attr("src", safe)
                                  Prop.Attr("srclang", t.SrcLang)
                                  Prop.Attr("label", BindingResolver.resolveTextSource pc.Sources t.Label)
                                  if takesDefault then
                                      Prop.Flag "default" ]
                                Content.Empty

                        (if takesDefault then Set.add token claimed else claimed), track :: acc)
                (Set.empty, [])
            |> snd
            |> List.rev

        let shared (variantClass: string) : Prop list =
            [ Prop.Attr("class", Css.media variantClass)
              Prop.Attr("src", safeSrc)
              Prop.Attr("aria-label", label)
              if spec.Controls then
                  Prop.Flag "controls"
              if spec.Loop then
                  Prop.Flag "loop" ]

        let trailing = Plan.attrs semanticAttrs @ Plan.attrs egressAttrs

        let content =
            if List.isEmpty tracks then
                Content.Empty
            else
                Content.Children tracks

        let element =
            match spec.Kind with
            | MediaKind.Video(autoplay, poster) ->
                Plan.element
                    "video"
                    (shared Css.Class.MediaVideo
                     @ [ match poster |> Option.bind (acceptedMediaUrl pc) with
                         | Some p -> Prop.Attr("poster", p)
                         | None -> ()
                         if autoplay then
                             Prop.Flag "autoplay"
                             Prop.Flag "muted" ]
                     @ trailing)
                    content
            | MediaKind.Audio -> Plan.element "audio" (shared Css.Class.MediaAudio @ trailing) content

        match spec.Transcript with
        | None -> element
        | Some transcript ->
            Plan.classChildren
                "div"
                Css.Class.MediaGroup
                [ element
                  Plan.element
                      "details"
                      [ Prop.Attr("class", Css.Class.MediaTranscript)
                        Prop.Attr("aria-label", label) ]
                      (Content.Children
                          [ Plan.classText "summary" Css.Class.MediaTranscriptSummary "Transcript"
                            Plan.classText
                                "div"
                                Css.Class.MediaTranscriptBody
                                (BindingResolver.resolveTextSource pc.Sources transcript) ]) ]

    /// A sandboxed, lazy, strict-referrer iframe; the sandbox grants exactly
    /// the declared permissions, fullscreen rides `allow`, and a refused source
    /// omits `src` and carries the refusal marker.
    let embed (pc: PlanContext<'Msg, 'E>) (spec: EmbedSpec) (semanticAttrs: (string * string) list) : Plan<'E> =
        let resolvedSrc =
            BindingResolver.tryResolve pc.Sources spec.Src |> Option.defaultValue ""

        let safeSrc, egressAttrs = Sanitize.sanitizeEmbedSrcForEgress pc.Egress resolvedSrc
        let has p = List.contains p spec.Permissions

        let sandboxTokens =
            [ if has EmbedPermission.AllowScripts then
                  "allow-scripts"
              if has EmbedPermission.AllowSameOrigin then
                  "allow-same-origin"
              if has EmbedPermission.AllowForms then
                  "allow-forms" ]

        Plan.element
            "iframe"
            ([ Prop.Attr("class", Css.Class.Embed + Css.embedAspect spec.AspectRatio)
               Prop.Attr("title", BindingResolver.resolveTextSource pc.Sources spec.Title)
               Prop.Attr("sandbox", String.concat " " sandboxTokens)
               Prop.Attr("loading", "lazy")
               Prop.Attr("referrerpolicy", "strict-origin-when-cross-origin")
               match safeSrc with
               | Some s -> Prop.Attr("src", s)
               | None -> ()
               if has EmbedPermission.AllowFullscreen then
                   Prop.Attr("allow", "fullscreen") ]
             @ Plan.attrs semanticAttrs
             @ Plan.attrs egressAttrs)
            Content.Empty

// ─── Structural kinds' leaf markup (Phase 2278) ────────────────────────────

/// The six structural kinds (ErrorBoundary, Switch, Custom, FragmentDecl,
/// FragmentRef, Mount) are WALK CONTROL — they decide which node renders and
/// how (error suspension, case selection, fragment expansion with cycle
/// tracking, host renderer registries, guest loaders), and the walk is each
/// renderer's own, so they stay renderer-side by nature (Phase 2278 ruling).
/// The leaf markup both tiers emit for them is planned here.
/// The visualisation kinds (Phase 2290).
[<RequireQualifiedAccess>]
module VisPlan =

    /// One `Tree` row, resolved: what the client's keyboard and click
    /// handlers need, keyed by the item id.
    type TreeRow = { HasChildren: bool; IsOpen: bool }

    /// The `Tree` model both tiers read: the open set, the selection, the one
    /// focusable row, and each row's state by item id.
    type TreeState =
        { ExpandedKeyNamed: bool
          Expanded: Set<string>
          Selected: string option
          Rows: Map<string, TreeRow> }

    let treeState (sources: BindingResolver.BindingSources) (spec: TreeSpec<'Msg>) : TreeState =
        let expandedKeyNamed = spec.ExpandedStateKey.IsSome
        let expanded = BindingResolver.readExpandedItems sources spec.ExpandedStateKey

        let rec rows (items: TreeItem list) : (string * TreeRow) list =
            items
            |> List.collect (fun item ->
                (item.Id,
                 { HasChildren = not (List.isEmpty item.Children)
                   IsOpen = TreeModel.treeItemExpanded expandedKeyNamed expanded item })
                :: rows item.Children)

        { ExpandedKeyNamed = expandedKeyNamed
          Expanded = expanded
          Selected = BindingResolver.readSelectedItem sources spec.SelectionStateKey
          Rows = Map.ofList (rows spec.Items) }

    /// The WAI-ARIA tree: rows state their accessible name (a treeitem owns
    /// its group, so a computed name would read the whole branch),
    /// `aria-expanded` only on a row with children, `aria-selected` only when
    /// a selection key is named, one roving tab stop. The client attaches the
    /// row's keyboard model at `Attach.TreeKeyDown` and the label click at
    /// `Attach.TreeLabelClick`, both keyed by the item id.
    let tree (sources: BindingResolver.BindingSources) (spec: TreeSpec<'Msg>) (state: TreeState) : Plan<'E> =
        let focusable =
            TreeModel.focusableTreeItem state.ExpandedKeyNamed state.Expanded state.Selected spec.Items

        let rec items (level: int) (rows: TreeItem list) : Plan<'E> list =
            let setSize = List.length rows

            rows
            |> List.mapi (fun i item ->
                let row =
                    { HasChildren = not (List.isEmpty item.Children)
                      IsOpen = TreeModel.treeItemExpanded state.ExpandedKeyNamed state.Expanded item }

                let label = BindingResolver.resolveTextSource sources item.Label

                Plan.Element
                    { Tag = "li"
                      // The item's declared id: expansion and selection key on it.
                      Key = Some(reconciliationKey (Some item.Id) i)
                      Props =
                        [ Prop.Attr("class", Css.Class.TreeItem)
                          Prop.Attr("role", "treeitem")
                          Prop.Attr("aria-label", label)
                          Prop.Attr("aria-level", string level)
                          Prop.Attr("aria-setsize", string setSize)
                          Prop.Attr("aria-posinset", string (i + 1))
                          Prop.Attr("data-fuaran-tree-item", item.Id)
                          Prop.Attr("tabIndex", (if Some item.Id = focusable then "0" else "-1"))
                          Prop.Behaviour(Attach.TreeKeyDown, item.Id)
                          if row.HasChildren then
                              Prop.Attr("aria-expanded", (if row.IsOpen then "true" else "false"))
                          match spec.SelectionStateKey with
                          | Some _ ->
                              Prop.Attr("aria-selected", (if Some item.Id = state.Selected then "true" else "false"))
                          | None -> () ]
                      Content =
                        Content.Children
                            [ Plan.element
                                  "span"
                                  [ Prop.Attr("class", Css.Class.TreeLabel)
                                    Prop.Behaviour(Attach.TreeLabelClick, item.Id) ]
                                  (Content.Text label)
                              if row.IsOpen then
                                  Plan.element
                                      "ul"
                                      [ Prop.Attr("class", Css.Class.TreeGroup); Prop.Attr("role", "group") ]
                                      (Content.Children(items (level + 1) item.Children)) ] })

        Plan.element
            "ul"
            [ Prop.Attr("class", Css.Class.Tree); Prop.Attr("role", "tree") ]
            (Content.Children(items 1 spec.Items))

    /// A chart whose kind is lowered and whose rows resolved: the shared
    /// Drawing SVG (`Charts.renderSvg` is called by the renderer, which owns
    /// the text resolver) in a container the client delegates point clicks
    /// from (`Attach.ChartPointClick`).
    let loweredChart (svg: string) : Plan<'E> =
        Plan.element "div" [ Prop.Behaviour(Attach.ChartPointClick, "") ] (Content.InnerHtml svg)

    /// The export control's accessible name (Phase 1125): it states the scope
    /// and the count the control can deliver — the whole grid, or the page a
    /// host-paged grid holds.
    let exportLabel (page: bool) (rows: int) : string =
        match page, rows with
        | false, 1 -> "Export 1 row as CSV"
        | false, n -> "Export " + string n + " rows as CSV"
        | true, 1 -> "Export this page (1 row) as CSV"
        | true, n -> "Export this page (" + string n + " rows) as CSV"

    /// The grid's export button: the scope in its accessible name and title;
    /// activation is `Attach.GridExport` (inert on the server, R10).
    let exportButton (accessibleName: string) : Plan<'E> =
        Plan.element
            "button"
            [ Prop.Attr("class", "fuaran-grid-export")
              Prop.Attr("type", "button")
              Prop.Attr("aria-label", accessibleName)
              Prop.Attr("title", accessibleName)
              Prop.Behaviour(Attach.GridExport, "") ]
            (Content.Text "Export CSV")

    /// Phase 393 — a static read-only `DataGrid` is the semantic `<table>`;
    /// Phase 801's declared sort intent rides as data attributes (only when
    /// declared); Phase 1473's print-break classes ride the table. An
    /// `Exportable` grid sits beside its export button (Phase 1125, R10).
    let staticGrid (sources: BindingResolver.BindingSources) (spec: DataGridSpec<'Msg>) (sr: StaticRows) : Plan<'E> =
        let text = BindingResolver.resolveTextSource sources

        let keyed (tag: string) (className: string) (index: int) (content: Content<'E>) : Plan<'E> =
            Plan.Element
                { Tag = tag
                  // A static table's rows are literals in document order.
                  Key = Some(reconciliationKey None index)
                  Props = [ Prop.Attr("class", className) ]
                  Content = content }

        let table =
            Plan.element
                "table"
                [ Prop.Attr(
                      "class",
                      Css.Class.Table
                      + Theme.gridPrintBreakClasses spec.KeepRowsTogether spec.RepeatHeader
                  )
                  match sr.Sortable with
                  | Some sortable -> Prop.Attr("data-fuaran-sortable", (if sortable then "true" else "false"))
                  | None -> ()
                  match sr.DefaultSort with
                  | Some ds ->
                      Prop.Attr("data-fuaran-sort-column", string ds.Column)

                      Prop.Attr(
                          "data-fuaran-sort-direction",
                          (match ds.Direction with
                           | SortDirection.Asc -> "asc"
                           | SortDirection.Desc -> "desc")
                      )
                  | None -> () ]
                (Content.Children
                    [ Plan.element
                          "thead"
                          []
                          (Content.Children
                              [ Plan.element
                                    "tr"
                                    []
                                    (Content.Children(
                                        sr.Headers
                                        |> List.mapi (fun i h ->
                                            keyed "th" Css.Class.TableHeader i (Content.Text(text h)))
                                    )) ])
                      Plan.element
                          "tbody"
                          []
                          (Content.Children(
                              sr.Rows
                              |> List.mapi (fun i row ->
                                  keyed
                                      "tr"
                                      Css.Class.TableRow
                                      i
                                      (Content.Children(
                                          row
                                          |> List.mapi (fun j cell ->
                                              keyed "td" Css.Class.TableCell j (Content.Text(text cell)))
                                      )))
                          )) ])

        if spec.Exportable then
            Plan.classChildren
                "div"
                "fuaran-grid-exportable"
                [ table; exportButton (exportLabel false (List.length sr.Rows)) ]
        else
            table

    /// The bound grid's no-script floor: a deterministic hydration placeholder
    /// with the row count (never a blank). The client renders its grid in its
    /// place, by nature.
    let gridPlaceholder (sources: BindingResolver.BindingSources) (spec: DataGridSpec<'Msg>) : Plan<'E> =
        let rowCount =
            BindingResolver.tryResolve sources spec.Source
            |> Option.map Seq.length
            |> Option.defaultValue 0

        Plan.element
            "div"
            [ Prop.Attr(
                  "class",
                  "fuaran-grid fuaran-grid-ssr-placeholder"
                  + Theme.gridPrintBreakClasses spec.KeepRowsTogether spec.RepeatHeader
              )
              Prop.Attr("data-fuaran-ssr-placeholder", "DataGrid")
              Prop.Attr("data-fuaran-row-count", string rowCount) ]
            (Content.Text(sprintf "[Grid: %d rows — hydrates client-side]" rowCount))

    /// Phase 2041 — a visualisation's state slots: an unresolved source with
    /// `OnLoading` renders the loading node; an errored one with `OnError` the
    /// node it builds, whose correlation id hashes the node id. `None` — render
    /// the body.
    let stateSlot
        (pc: PlanContext<'Msg, 'E>)
        (state: StateBehaviour<'Msg> option)
        (resolution: BindingResolver.Resolution<'T>)
        : Plan<'E> option =
        match resolution, (state |> Option.bind _.OnLoading), (state |> Option.bind _.OnError) with
        | BindingResolver.NotResolved, Some loadingNode, _ -> Some(pc.Child loadingNode)
        | BindingResolver.Errored msg, _, Some errorFn ->
            Some(
                pc.Child(
                    errorFn
                        { Kind = ErrorKind.BindingResolution
                          Message = msg
                          CorrelationId = Ids.deterministicCorrelationId pc.NodeId }
                )
            )
        | _ -> None

    /// A chart that cannot draw yet — unresolved data, or a kind whose
    /// lowering has not shipped: the labelled placeholder with the row count
    /// (R11: the served markup on both tiers).
    let chartPlaceholder (sources: BindingResolver.BindingSources) (spec: ChartSpec<'Msg>) (rowCount: int) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", "fuaran-chart fuaran-chart-ssr-placeholder")
              Prop.Attr("data-fuaran-ssr-placeholder", "Chart")
              Prop.Attr("data-fuaran-row-count", string rowCount) ]
            (Content.Children
                [ spec.Title
                  |> Plan.ofOption (fun title ->
                      Plan.classText "div" Css.Class.ChartTitle (BindingResolver.resolveTextSource sources title))
                  Plan.classText
                      "div"
                      Css.Class.ChartPlaceholder
                      (sprintf "[Chart: %d rows — hydrates client-side]" rowCount) ])

    /// A `Chart`: its state slots, else — when the kind is `lowered` (`Charts.isLowered`) — the Drawing SVG
    /// (`svg` is the renderer's `Charts.renderSvg`, the one entry point both tiers call),
    /// else the placeholder.
    let chart
        (pc: PlanContext<'Msg, 'E>)
        (state: StateBehaviour<'Msg> option)
        (spec: ChartSpec<'Msg>)
        (lowered: bool)
        (svg: Row list -> string)
        : Plan<'E> =
        let resolution = BindingResolver.resolve<Row seq> pc.Sources spec.Source

        match stateSlot pc state resolution with
        | Some slot -> slot
        | None ->
            match resolution with
            | BindingResolver.Resolved rows when lowered -> loweredChart (svg (List.ofSeq rows))
            | BindingResolver.Resolved rows -> chartPlaceholder pc.Sources spec (Seq.length rows)
            | _ -> chartPlaceholder pc.Sources spec 0

    /// A `Map` (R12): its state slots, else the placeholder with the marker
    /// count and, when markers resolve, the marker list; a marker's click is
    /// `Attach.MarkerClick` (key = its index).
    let map (pc: PlanContext<'Msg, 'E>) (state: StateBehaviour<'Msg> option) (spec: MapSpec<'Msg>) : Plan<'E> =
        let resolution = BindingResolver.resolve<MapMarker list> pc.Sources spec.Source

        match stateSlot pc state resolution with
        | Some slot -> slot
        | None ->
            let markers =
                match resolution with
                | BindingResolver.Resolved markers -> markers
                | _ -> []

            let caption = sprintf "[Map: %d markers — hydrates client-side]" markers.Length

            Plan.element
                "div"
                [ Prop.Attr("class", "fuaran-map fuaran-map-ssr-placeholder")
                  Prop.Attr("data-fuaran-ssr-placeholder", "Map")
                  Prop.Attr("data-fuaran-marker-count", string markers.Length) ]
                (if markers.IsEmpty then
                     Content.Text caption
                 else
                     Content.Children
                         [ Plan.TextNode caption
                           Plan.classChildren
                               "ul"
                               "fuaran-map-marker-list"
                               (markers
                                |> List.mapi (fun i marker ->
                                    Plan.Element
                                        { Tag = "li"
                                          Key = Some(reconciliationKey (Some marker.Label) i)
                                          Props =
                                            [ Prop.Attr("class", "fuaran-map-marker")
                                              Prop.Behaviour(Attach.MarkerClick, string i) ]
                                          Content =
                                            Content.Text(
                                                sprintf
                                                    "%s @ (%.4f, %.4f)"
                                                    marker.Label
                                                    marker.Latitude
                                                    marker.Longitude
                                            ) })) ])

[<RequireQualifiedAccess>]
module StructuralPlan =

    /// A `FragmentRef` that re-enters a fragment already being expanded.
    let fragmentCycle (name: string) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.FragmentCyclePlaceholder)
              Prop.Attr("data-fuaran-fragment-cycle", name) ]
            (Content.Text(RenderParity.fragmentCycleText name))

    /// A `FragmentRef` naming no declaration in the tree.
    let fragmentUnresolved (name: string) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.FragmentUnresolvedPlaceholder)
              Prop.Attr("data-fuaran-fragment-unresolved", name) ]
            (Content.Text(sprintf "[fuaran:fragment unresolved '%s']" name))

    /// A `Mount`'s boundary around what the guest seam rendered (or the
    /// placeholder).
    let mountBoundary (scopeId: string) (child: Plan<'E>) : Plan<'E> =
        Plan.element
            "div"
            [ Prop.Attr("class", Css.Class.MountBoundary)
              Prop.Attr("data-fuaran-mount-scope", scopeId) ]
            (Content.Children [ child ])

    /// What a `Mount` shows when no guest loader is attached.
    let mountPlaceholder (scopeId: string) : Plan<'E> =
        Plan.classText
            "div"
            Css.Class.MountPlaceholder
            (sprintf "[fuaran:mount '%s' — guest loader not attached]" scopeId)
