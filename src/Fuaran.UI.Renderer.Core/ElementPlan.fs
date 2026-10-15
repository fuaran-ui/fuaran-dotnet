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
    { Sources: BindingResolver.BindingSources
      Csp: Csp.CspMode
      Collector: Csp.StyleCollector option
      NodeId: string
      Child: Node<'Msg> -> Plan<'E> }

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
