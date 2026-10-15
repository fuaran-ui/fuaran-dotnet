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
