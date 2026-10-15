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
