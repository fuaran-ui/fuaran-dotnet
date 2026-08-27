module Fuaran.UI.Renderer.Server.Email

// ============================================================================
//  Fuaran — the email-safe render projection (Phase 441).
//
//  HTML email is the most hostile render target in computing: no JavaScript,
//  no external stylesheet, no flexbox or grid worth relying on, and a rendering
//  engine per client (Outlook desktop still lays out through Word). It is also
//  a projection of the same `Node<obj>` tree everything else renders — a
//  scheduled digest is not a fork of the application, it is another emission of
//  it.
//
//  THE SCOPE LINE IS THE FEATURE. The projection is bounded hard to the
//  **Display subset** — the kinds that carry information rather than
//  interaction: headings and text, KPI / Metric tiles, Facts and label-value
//  rows, badges, callouts, lists, summary lists, static tables, and the typed
//  crawlable `Link`. Everything interactive projects to a labelled "open live"
//  link. That rule is not a nicety: a `<button>` in an email is a control that
//  looks live and cannot be, and a `<form>` that posts nowhere is worse than an
//  absent one. The projection never emits a half-working control. `scope`
//  below is that decision, machine-readable, one row per canonical wire kind.
//
//  IT IS CROSS-CHECKED AGAINST THE FIDELITY MANIFEST, NOT ASSERTED. Phase 442
//  made the per-kind render-fidelity contract machine-readable
//  (`Fuaran.UI.RenderFidelity`), and a `RichTier.Behavioural` row means exactly
//  "this control renders inert server-side and gains its behaviour at
//  hydration" — which is the definition of a kind that must NOT be rendered
//  into an email. `interactiveWireKinds` derives that set from the manifest
//  rather than restating it, and the test corpus asserts every member of it has
//  an `OpenLive` row here. A new interactive kind therefore fails the build
//  rather than silently shipping a dead button to an inbox. `scope`'s
//  completeness is measured against the same manifest's kind list, so a new
//  `NodeKind` cannot arrive with no declared email posture.
//
//  DETERMINISM. Same tree, same options ⇒ same bytes. There is no clock, no
//  identifier minting, and no iteration over an unordered collection anywhere
//  in this file; text resolves through `Render.renderText` and figures through
//  `Render.formatNumber` — the same functions the SSR document uses, so a
//  digest and the page it links to cannot disagree about what a number is.
//  Byte-pinned by the golden corpus in
//  `Fuaran.UI.Renderer.Server.Tests/email-corpus/`.
//
//  WHAT THIS IS NOT. It is not parity with the SSR renderer, and must not be
//  read as a fourth conformant host: the SSR-parity corpus (Phase 142) locks
//  the `fuaran-*` class vocabulary shared by the client and server renderers,
//  and this projection deliberately emits none of it — an email has no
//  stylesheet to key those classes off. The two renderers answer different
//  questions about one tree. See `docs/SSR.md` "Email-safe render projection".
// ============================================================================

open Feliz.ViewEngine
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── The declared Display-subset scope ──────────────────────────────────────

/// What the email projection does with a wire kind. Four dispositions, and the
/// distinction between the last two matters: `OpenLive` says "this exists and
/// you have to leave the inbox to use it", `Omitted` says "this carries nothing
/// a static digest can convey". A reader can act on the first.
[<RequireQualifiedAccess>]
type Disposition =
    /// Rendered into email-safe HTML — the Display subset proper.
    | Rendered of note: string
    /// A structure carrier: the node itself paints nothing beyond layout, its
    /// children render.
    | Structural of note: string
    /// Projected to a labelled "open live" link. Never a half-working control.
    | OpenLive of note: string
    /// Zero-paint in an email, deliberately.
    | Omitted of note: string

/// The projection's fidelity declaration: one row per canonical wire kind,
/// ordered by wire name (Ordinal) so a new kind lands as one clean insert.
///
/// This is the authored source of the scope line documented in `docs/SSR.md`;
/// the doc restates it for a reader, this list is what the code and the tests
/// agree on.
let scope: (string * Disposition) list =
    [ "Badge", Disposition.Rendered "an inline pill: the resolved label in a bordered, tone-coloured span"

      "Box", Disposition.Structural "the container becomes a layout table; a Card gains a bordered heading row"

      "Button",
      Disposition.OpenLive
          "a button in an inbox is a control that cannot fire - the declared action needs a runtime that is not there"

      "Callout", Disposition.Rendered "a bordered, tone-tinted notice table carrying the heading and body"

      "Chart",
      Disposition.OpenLive
          "even a server-lowered chart emits SVG, which Outlook's Word engine does not draw; a broken picture is worse than a link"

      "CodeBlock",
      Disposition.Rendered
          "the deterministic escaped `<pre><code>` floor, restyled inline; no highlighting pass exists here"

      "Custom",
      Disposition.OpenLive
          "a host renderer's output is outside this projection's email audit, so it is linked rather than embedded unchecked"

      "DataGrid",
      Disposition.Rendered
          "the `staticRows` form renders as a real bordered `<table>`; the client-library form has no server-side rows to show and projects to open-live"

      "Disclosure",
      Disposition.Structural
          "always EXPANDED - `<details>` does not open in Outlook, and a permanently-shut section is content the reader silently loses"

      "Drawing", Disposition.OpenLive "inline SVG, for the same reason as Chart"

      "ErrorBoundary", Disposition.Structural "the protected child renders; the fallback is a client-runtime path"

      "Fact", Disposition.Rendered "a label/value tile with its optional help text"

      "FileUpload", Disposition.OpenLive "interactive (fidelity manifest: Behavioural)"

      "Filters", Disposition.OpenLive "interactive (fidelity manifest: Behavioural)"

      "Form",
      Disposition.OpenLive
          "interactive (fidelity manifest: Behavioural) - a form that posts nowhere is worse than an absent one"

      "FragmentDecl", Disposition.Omitted "a template declaration paints nothing, here as everywhere"

      "FragmentRef",
      Disposition.Structural
          "expanded against the tree's fragment registry; an unresolved reference renders a labelled note"

      "Heading", Disposition.Rendered "a real `<h1>`-`<h6>` with an inline type scale"

      "Icon",
      Disposition.Omitted
          "the uniform icon hook carries the name in `data-icon` and relies on host CSS for the glyph; an email has neither, so the hook would paint an empty box"

      "Image",
      Disposition.Rendered "a real `<img>` with the sanitised src, `alt` always present, and no CSS-dependent shaping"

      "LabelValueRow", Disposition.Rendered "a two-cell row, the value formatted through the shared projection"

      "Link",
      Disposition.Rendered
          "the point of the whole exercise: a real crawlable `<a href>`, the one interaction email genuinely has"

      "List", Disposition.Rendered "a real `<ol>`/`<ul>` with inline item spacing; no CSS list resets to rely on"

      "Map", Disposition.OpenLive "a client map library draws it; the server has markers, not a picture"

      "Markdown",
      Disposition.Rendered
          "the deterministic GFM render, which escapes raw HTML by construction; task-list checkboxes become ballot glyphs, since a disabled `<input>` is a control"

      "Math",
      Disposition.Rendered
          "NARROWED to the escaped source in a monospace span. The MathML floor is correct in a browser and blank in Outlook, so the projection prefers readable LaTeX to invisible mathematics"

      "Media",
      Disposition.OpenLive
          "a transport, not a picture. No mainstream mail client plays inline media, and the fallbacks are all worse than a link: a `<video>` degrades to a blank rectangle, and a poster frame rendered as a bare `<img>` is a still image that silently looks like a broken player. The label is the link text, which is exactly what it was written to be"

      "Metric", Disposition.Rendered "the KPI tile: label, formatted value, trend and subtext, stacked in a cell"

      "Modal",
      Disposition.OpenLive
          "interactive (fidelity manifest: Behavioural) - an overlay has no meaning in a document with no viewport"

      "Mount", Disposition.OpenLive "the guest tree attaches client-side; there is nothing to project"

      "Progress",
      Disposition.Rendered
          "a two-cell table bar plus the percentage as text, so the figure survives a client that drops backgrounds"

      "ScrollArea",
      Disposition.Structural "children render in full - an email has no clipping, and hidden content is lost content"

      "Select", Disposition.OpenLive "interactive (fidelity manifest: Behavioural)"

      "Skeleton", Disposition.Omitted "a loading placeholder describes a state a delivered email is never in"

      "Sparkline",
      Disposition.OpenLive "the polyline is drawn client-side; the SSR floor is an em-dash, which is not worth an inbox"

      "SplitPanel",
      Disposition.Structural "a two-cell row at the declared weights, which collapses acceptably on narrow clients"

      "Stepper", Disposition.OpenLive "interactive (fidelity manifest: Behavioural)"

      "SummaryList", Disposition.Structural "the heading plus its children, stacked"

      "Switch",
      Disposition.Structural "the case matching the resolved selector, else the default - the same branch SSR renders"

      "Tabs",
      Disposition.OpenLive
          "interactive (fidelity manifest: Behavioural). Deliberately NOT 'render the active panel': a digest that silently drops the other panels is a lie about how much it contains"

      "Toast",
      Disposition.Rendered
          "an OPEN toast renders as a static notice; a CLOSED one is OMITTED rather than `[hidden]`, because `[hidden]` is not honoured everywhere and a leaked notification is a disclosure bug" ]

/// The declared posture of a wire kind, or `None` for a kind with no row (which
/// the completeness test makes impossible for a canonical kind).
let dispositionOf (wireKind: string) : Disposition option =
    scope |> List.tryPick (fun (k, d) -> if k = wireKind then Some d else None)

/// The kinds the Phase 442 render-fidelity manifest declares `Behavioural` —
/// "inert server-side, gains behaviour at hydration". DERIVED, never restated:
/// this is precisely the set that must not render as a control in an email, and
/// the corpus asserts each member has an `OpenLive` row in `scope`.
let interactiveWireKinds: string list =
    Fuaran.UI.RenderFidelity.all
    |> List.filter (fun r ->
        match r.Rich with
        | Fuaran.UI.RenderFidelity.RichTier.Behavioural _ -> true
        | _ -> false)
    |> List.map _.Kind

// ─── Options ────────────────────────────────────────────────────────────────

/// Host-supplied knobs. Deliberately small: an email projection with a theme
/// engine is a CSS framework, and the client fragmentation this file exists to
/// survive is exactly what defeats one.
type EmailOptions =
    {
        /// The live surface the "open live" affordances point at. `None` emits
        /// the label WITHOUT an anchor — a dangling href is a broken promise,
        /// whereas a labelled note is honest about what is not in the email.
        LiveUrl: string option
        /// The content column width. 600px is the conventional safe maximum:
        /// it is what the Outlook reading pane fits without horizontal scroll.
        MaxWidthPx: int
        /// The inline font stack. Every text element carries it, because email
        /// has no cascade to inherit from.
        FontStack: string
        /// Phase 1026 — the destination policy every TREE-AUTHORED `href` /
        /// `src` in the projection is checked against. Defaults to
        /// `Sanitize.denyNonLocalEgress`.
        ///
        /// `LiveUrl` above is deliberately NOT subject to it: that URL is
        /// supplied by the host in this very record, so checking a host's own
        /// declaration against the host's own allowlist tests nothing and would
        /// make the common case ("point at my app") fail unless the host
        /// remembered to allowlist itself.
        EgressPolicy: Sanitize.EgressPolicy
    }

/// The conventional defaults: no live surface declared, a 600px column, and a
/// system font stack with no webfont (a webfont is an external asset request
/// most clients refuse).
///
/// The stack is deliberately UNQUOTED. CSS permits a multi-word family name as
/// a sequence of identifiers, and quoting it would be escaped into `&apos;`
/// entities on the way into the style attribute — which HTML4-era mail parsers
/// render as literal text rather than decoding, so the whole declaration is
/// discarded and the message falls back to the client's default serif.
let defaults: EmailOptions =
    { LiveUrl = None
      MaxWidthPx = 600
      FontStack = "-apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif"
      EgressPolicy = Sanitize.denyNonLocalEgress }

// ─── The inline style vocabulary ────────────────────────────────────────────
//
// Literal strings, not a token system. An email client that supports CSS
// variables is not the one the projection has to survive, and every declaration
// below is chosen for the intersection of what Gmail (which strips a great deal)
// and Outlook desktop (which lays out through Word) both honour.

let private inkColour = "#1c1f23"
let private mutedColour = "#5b6570"
let private ruleColour = "#dfe3e8"
let private panelColour = "#f6f7f9"
let private linkColour = "#1f4f82"

let private toneColour (tone: ToneVariant) : string =
    match tone with
    | ToneVariant.Default -> inkColour
    | ToneVariant.Subdued -> mutedColour
    | ToneVariant.Brand -> "#1f4f82"
    | ToneVariant.Success -> "#1c6b45"
    | ToneVariant.Warning -> "#8a5a06"
    | ToneVariant.Critical -> "#9b2226"
    | ToneVariant.Info -> "#1f4f82"

let private badgeColour (variant: BadgeVariant) : string =
    match variant with
    | BadgeVariant.Neutral -> mutedColour
    | BadgeVariant.Brand -> "#1f4f82"
    | BadgeVariant.Success -> "#1c6b45"
    | BadgeVariant.Warning -> "#8a5a06"
    | BadgeVariant.Critical -> "#9b2226"
    | BadgeVariant.Info -> "#1f4f82"

/// The base text declarations every text-bearing element carries, in an
/// explicit colour. Email has no inheritance worth relying on, so this is
/// repeated on each element rather than hoisted to a parent.
///
/// Colour is a PARAMETER rather than something a caller appends, so a
/// declaration block carries exactly one `color:`. Two would be legal CSS —
/// last wins — but this is the one place in the stack where "legal CSS" and
/// "what the client does" part company: several mail parsers keep the first
/// declaration, which would silently invert the intended emphasis.
let private textIn (opts: EmailOptions) (colour: string) (extra: string) : string =
    "margin:0;font-family:" + opts.FontStack + ";color:" + colour + ";" + extra

/// The same, in the default ink colour.
let private textStyle (opts: EmailOptions) (extra: string) : string = textIn opts inkColour extra

/// A presentation table. `role="presentation"` keeps a screen reader from
/// announcing layout scaffolding as data; the three zeroed attributes are the
/// HTML-4 forms Outlook honours where the CSS equivalents are ignored.
let private tableProps (extraStyle: string) : IReactProperty list =
    [ prop.custom ("role", "presentation")
      prop.custom ("cellpadding", "0")
      prop.custom ("cellspacing", "0")
      prop.custom ("border", "0")
      prop.custom ("width", "100%")
      prop.custom ("style", "border-collapse:collapse;width:100%;" + extraStyle) ]

let private layoutTable (extraStyle: string) (rows: ReactElement list) : ReactElement =
    Html.table (tableProps extraStyle @ [ prop.children [ Html.tbody rows ] ])

/// One stacked block: a full-width row whose single cell holds the child.
let private stackedRow (cellStyle: string) (child: ReactElement) : ReactElement =
    Html.tr
        [ Html.td
              [ prop.custom ("style", "padding:0 0 12px 0;" + cellStyle)
                prop.custom ("valign", "top")
                prop.children [ child ] ] ]

let private stacked (children: ReactElement list) : ReactElement =
    layoutTable "" (children |> List.map (stackedRow ""))

/// Chunk a list into rows of `n` — the grid projection. `n <= 0` degenerates to
/// a single row rather than looping forever.
let private chunk (n: int) (items: 'a list) : 'a list list =
    if n <= 1 then
        items |> List.map List.singleton
    else
        items
        |> List.indexed
        |> List.groupBy (fun (i, _) -> i / n)
        |> List.map (fun (_, group) -> group |> List.map snd)

// ─── The renderer ───────────────────────────────────────────────────────────

let private openLiveAnchor (opts: EmailOptions) (nodeId: string) (label: string) : ReactElement =
    let linkStyle =
        textIn opts linkColour "display:inline-block;font-size:14px;line-height:20px;text-decoration:underline;"

    match opts.LiveUrl with
    | Some baseUrl ->
        let href = Sanitize.sanitizeUrlOrBlank (baseUrl + "#" + nodeId)

        Html.a
            [ prop.custom ("style", linkStyle)
              prop.href href
              prop.custom ("data-fuaran-email-open-live", nodeId)
              prop.text (label + " — open live") ]
    | None ->
        // No live surface declared. Say what is not here rather than emitting a
        // dead anchor: the reader learns the digest is partial, which is true.
        Html.span
            [ prop.custom ("style", textIn opts mutedColour "font-size:14px;line-height:20px;")
              prop.custom ("data-fuaran-email-open-live", nodeId)
              prop.text (label + " — available in the live view") ]

/// The labelled note an `Omitted` kind leaves behind: nothing at all. Kept as a
/// named function so the intent reads as a decision rather than a missing arm.
let private omitted: ReactElement = Html.none

let private headingStyle (opts: EmailOptions) (level: int) (variant: HeadingVariant) : string =
    let size, weight =
        match level with
        | 1 -> "26px", "700"
        | 2 -> "22px", "700"
        | 3 -> "18px", "600"
        | 4 -> "16px", "600"
        | 5 -> "15px", "600"
        | _ -> "14px", "600"

    match variant with
    | HeadingVariant.Eyebrow ->
        textIn
            opts
            mutedColour
            "font-size:12px;line-height:16px;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;"
    | HeadingVariant.Caption -> textIn opts mutedColour "font-size:13px;line-height:18px;font-weight:400;"
    | HeadingVariant.Lead -> textStyle opts "font-size:18px;line-height:26px;font-weight:400;"
    | HeadingVariant.Standard -> textStyle opts ("font-size:" + size + ";line-height:1.3;font-weight:" + weight + ";")

/// Drop the `data-fuaran-egress-refused` markers the markdown renderer attaches
/// to a refused destination, leaving the refused `href` / `src` itself in place.
///
/// The same rule the `Link` and `Image` projections above keep, applied to the
/// one surface that emits its attributes as a string rather than as props:
/// `data-*` attributes do not survive the sanitisers most mail clients run, so a
/// marker here is a signal that cannot be relied on to arrive. The refusal is
/// NOT dropped — the destination is still `egressRefusalUrl`, which is the half
/// that stops it being reached.
///
/// String surgery over a KNOWN shape, in the same place and for the same reason
/// as the checkbox substitution below it: the marker's value is a class name and
/// a normalised host, so it never contains a quote, and the renderer emits the
/// attribute in exactly one spelling.
let private stripEgressMarkers (html: string) : string =
    let needle = " " + Sanitize.egressRefusalAttribute + "=\""
    let mutable s = html
    let mutable go = true

    while go do
        let i = s.IndexOf(needle, System.StringComparison.Ordinal)

        if i < 0 then
            go <- false
        else
            let close = s.IndexOf('"', i + needle.Length)

            if close < 0 then
                go <- false
            else
                s <- s.Remove(i, close + 1 - i)

    s

/// Markdown, made email-safe. The GFM renderer escapes raw HTML by
/// construction, so its output is a closed tag set — with one exception worth
/// naming: a task-list item emits a disabled `<input type="checkbox">`, which is
/// a form control (invisible in several clients, and against this projection's
/// own rule). It is substituted for a ballot glyph, which every client draws.
///
/// Phase 1032: the body's own link and image destinations are policy-checked
/// with the digest's declared policy, with the marker stripped per the rule
/// above. A digest is the surface where an undeclared markdown image IS the
/// tracking pixel, so leaving this one unchecked would have been the largest
/// remaining hole rather than the smallest.
let private emailSafeMarkdown (policy: Sanitize.EgressPolicy) (markdownText: string) : string =
    let html =
        Sanitize.sanitizeMarkdownHtml (Markdown.toHtmlWithEgress policy markdownText)
        |> stripEgressMarkers

    html
        .Replace("<input class=\"fuaran-task-checkbox\" checked=\"\" disabled=\"\" type=\"checkbox\" /> ", "&#9745; ")
        .Replace("<input class=\"fuaran-task-checkbox\" disabled=\"\" type=\"checkbox\" /> ", "&#9744; ")

let rec private renderNode
    (opts: EmailOptions)
    (depth: int)
    (ctx: Render.ServerRenderContext)
    (node: Node<obj>)
    : ReactElement =
    if depth > Fuaran.UI.WireLimits.MaxDepth then
        Html.p
            [ prop.custom ("style", textIn opts mutedColour "font-size:13px;")
              prop.text (
                  "[subtree omitted: nesting exceeds the wire limit MaxDepth = "
                  + string Fuaran.UI.WireLimits.MaxDepth
                  + "]"
              ) ]
    else
        renderKind opts depth ctx node

and private children
    (opts: EmailOptions)
    (depth: int)
    (ctx: Render.ServerRenderContext)
    (nodes: Node<obj> list)
    : ReactElement list =
    nodes |> List.map (renderNode opts (depth + 1) ctx)

and private renderKind
    (opts: EmailOptions)
    (depth: int)
    (ctx: Render.ServerRenderContext)
    (node: Node<obj>)
    : ReactElement =
    // NOT `id` — that would shadow the `id` function, which the Switch arm's
    // selection projector below needs.
    let selfId = node.Id
    let text = Render.renderText ctx

    /// The label an open-live affordance carries. The node's declared
    /// accessible label wins where it resolves — it is the author's own words
    /// for what this is — and the kind name is the honest fallback.
    let liveLabel (fallback: string) =
        node.Accessibility
        |> Option.bind _.Label
        |> Option.bind (BindingResolver.tryResolve ctx.Sources)
        |> Option.filter (fun s -> s <> "")
        |> Option.defaultValue fallback

    let live (fallback: string) =
        openLiveAnchor opts selfId (liveLabel fallback)

    match node.Kind with

    // ── Structure ────────────────────────────────────────────────────────────
    | NodeKind.Box spec ->
        let kids = children opts depth ctx spec.Children

        match spec.Role, spec.Layout with
        | BoxRole.Separator, _ ->
            // A bordered, zero-height cell rather than `<hr>`: Word's engine
            // gives `<hr>` a margin no declaration reliably removes.
            layoutTable
                ""
                [ Html.tr
                      [ Html.td
                            [ prop.custom (
                                  "style",
                                  "border-top:1px solid " + ruleColour + ";font-size:0;line-height:0;height:1px;"
                              )
                              prop.children [ Html.text " " ] ] ] ]
        | BoxRole.Card, _ ->
            let headingRow =
                match spec.Heading with
                | Some heading ->
                    [ Html.tr
                          [ Html.td
                                [ prop.custom (
                                      "style",
                                      "padding:12px 16px;border-bottom:1px solid "
                                      + ruleColour
                                      + ";background:"
                                      + panelColour
                                      + ";"
                                  )
                                  prop.children
                                      [ Html.p
                                            [ prop.custom (
                                                  "style",
                                                  textStyle opts "font-size:15px;line-height:20px;font-weight:600;"
                                              )
                                              prop.text (text heading) ] ] ] ] ]
                | None -> []

            layoutTable
                ("border:1px solid " + ruleColour + ";")
                (headingRow
                 @ [ Html.tr [ Html.td [ prop.custom ("style", "padding:16px;"); prop.children [ stacked kids ] ] ] ])
        | BoxRole.Group, BoxLayout.Grid(cols, _, _) ->
            // The one place a table earns its keep: N-across KPI rows, which is
            // exactly the shape a digest opens with, and which flex/grid cannot
            // express in an inbox at all.
            let columns = max 1 cols
            let width = string (100 / columns) + "%"

            layoutTable
                ""
                [ for rowKids in chunk columns kids ->
                      Html.tr
                          [ for kid in rowKids ->
                                Html.td
                                    [ prop.custom ("width", width)
                                      prop.custom ("valign", "top")
                                      prop.custom ("style", "padding:0 8px 12px 0;vertical-align:top;width:" + width)
                                      prop.children [ kid ] ] ] ]
        | BoxRole.Group, BoxLayout.Flex(Orientation.Horizontal, _, _) ->
            let count = max 1 (List.length kids)
            let width = string (100 / count) + "%"

            layoutTable
                ""
                [ Html.tr
                      [ for kid in kids ->
                            Html.td
                                [ prop.custom ("width", width)
                                  prop.custom ("valign", "top")
                                  prop.custom ("style", "padding:0 8px 0 0;vertical-align:top;width:" + width)
                                  prop.children [ kid ] ] ] ]
        | _ -> stacked kids

    | NodeKind.SplitPanel spec ->
        let kids = children opts depth ctx spec.Children
        let leftWeight = max 0.0 (min 1.0 spec.Weight)
        let leftPct = int (System.Math.Round(leftWeight * 100.0))

        let leftKids, rightKids =
            match kids with
            | [] -> [], []
            | [ a ] -> [ a ], []
            | a :: rest -> [ a ], rest

        layoutTable
            ""
            [ Html.tr
                  [ Html.td
                        [ prop.custom ("width", string leftPct + "%")
                          prop.custom ("valign", "top")
                          prop.custom ("style", "padding:0 8px 0 0;vertical-align:top;")
                          prop.children [ stacked leftKids ] ]
                    Html.td
                        [ prop.custom ("width", string (100 - leftPct) + "%")
                          prop.custom ("valign", "top")
                          prop.custom ("style", "padding:0;vertical-align:top;")
                          prop.children [ stacked rightKids ] ] ] ]

    | NodeKind.SummaryList spec ->
        let kids = children opts depth ctx spec.Children

        let headingEls =
            match spec.Heading with
            | Some heading ->
                [ Html.p
                      [ prop.custom ("style", textStyle opts "font-size:15px;line-height:20px;font-weight:600;")
                        prop.text (text heading) ] ]
            | None -> []

        stacked (headingEls @ kids)

    | NodeKind.Disclosure spec ->
        // Always expanded. `<details>` is inert in Outlook, so a collapsed
        // section would be content the reader never learns exists.
        let kids = children opts depth ctx spec.Children

        stacked (
            [ Html.p
                  [ prop.custom ("style", textStyle opts "font-size:15px;line-height:20px;font-weight:600;")
                    prop.text (text spec.Heading) ] ]
            @ kids
        )

    | NodeKind.ScrollArea spec -> stacked (children opts depth ctx spec.Children)

    | NodeKind.ErrorBoundary spec -> renderNode opts (depth + 1) ctx spec.Child

    | NodeKind.Switch spec ->
        // The same branch SSR picks, resolved through the same sources, so the
        // digest and the page it links to show the same case.
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

        renderNode opts (depth + 1) ctx (matched |> Option.defaultValue spec.Default)

    | NodeKind.FragmentRef spec ->
        match Map.tryFind spec.Name ctx.Fragments with
        | Some body -> renderNode opts (depth + 1) ctx body
        | None ->
            Html.p
                [ prop.custom ("style", textIn opts mutedColour "font-size:13px;")
                  prop.text ("[fuaran:fragment unresolved '" + spec.Name + "']") ]

    | NodeKind.FragmentDecl _ -> omitted

    // ── Display ──────────────────────────────────────────────────────────────
    | NodeKind.Heading spec ->
        let props =
            [ prop.custom ("style", headingStyle opts spec.Level spec.Variant)
              prop.text (text spec.Text) ]

        match spec.Level with
        | 1 -> Html.h1 props
        | 2 -> Html.h2 props
        | 3 -> Html.h3 props
        | 4 -> Html.h4 props
        | 5 -> Html.h5 props
        | _ -> Html.h6 props

    | NodeKind.Markdown spec ->
        layoutTable
            ""
            [ Html.tr
                  [ Html.td
                        [ prop.custom ("style", textStyle opts "font-size:15px;line-height:22px;")
                          prop.dangerouslySetInnerHTML (emailSafeMarkdown ctx.EgressPolicy (text spec.Text)) ] ] ]

    | NodeKind.Metric spec ->
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        let valueText =
            match resolution with
            | BindingResolver.Resolved value -> Render.formatNumber spec.Format value
            | BindingResolver.NotResolved -> "—"
            | BindingResolver.Errored msg -> "(error: " + msg + ")"
            | BindingResolver.I18nUnresolved key -> "[i18n:" + key + "]"

        let trendEls =
            match spec.Trend with
            | Some trendBinding ->
                match BindingResolver.tryResolveScalarFloat ctx.Sources trendBinding with
                | Some t ->
                    [ Html.p
                          [ prop.custom ("style", textIn opts (toneColour spec.Tone) "font-size:13px;line-height:18px;")
                            prop.text (Render.formatNumber (spec.TrendFormat |> Option.defaultValue CellFormat.None) t) ] ]
                | None -> []
            | None -> []

        let subtextEls =
            match spec.Subtext with
            | Some subtext ->
                [ Html.p
                      [ prop.custom ("style", textIn opts mutedColour "font-size:13px;line-height:18px;")
                        prop.text (text subtext) ] ]
            | None -> []

        layoutTable
            ("border:1px solid " + ruleColour + ";background:" + panelColour + ";")
            [ Html.tr
                  [ Html.td
                        [ prop.custom ("style", "padding:14px 16px;")
                          prop.children (
                              [ Html.p
                                    [ prop.custom (
                                          "style",
                                          textIn
                                              opts
                                              mutedColour
                                              "font-size:12px;line-height:16px;letter-spacing:0.06em;text-transform:uppercase;"
                                      )
                                      prop.text (text spec.Label) ]
                                Html.p
                                    [ prop.custom (
                                          "style",
                                          textIn
                                              opts
                                              (toneColour spec.Tone)
                                              "font-size:28px;line-height:34px;font-weight:700;padding-top:4px;"
                                      )
                                      prop.text valueText ] ]
                              @ trendEls
                              @ subtextEls
                          ) ] ] ]

    | NodeKind.Fact spec ->
        let helpEls =
            match spec.Help with
            | Some help ->
                [ Html.p
                      [ prop.custom ("style", textIn opts mutedColour "font-size:12px;line-height:16px;")
                        prop.text (text help) ] ]
            | None -> []

        let weight = if spec.Emphasis then "700" else "600"

        layoutTable
            ""
            [ Html.tr
                  [ Html.td
                        [ prop.custom ("style", "padding:0;")
                          prop.children (
                              [ Html.p
                                    [ prop.custom ("style", textIn opts mutedColour "font-size:12px;line-height:16px;")
                                      prop.text (text spec.Label) ]
                                Html.p
                                    [ prop.custom (
                                          "style",
                                          textIn
                                              opts
                                              (toneColour spec.Tone)
                                              ("font-size:16px;line-height:22px;font-weight:" + weight + ";")
                                      )
                                      prop.text (text spec.Value) ] ]
                              @ helpEls
                          ) ] ] ]

    | NodeKind.LabelValueRow spec ->
        let resolution = BindingResolver.resolveScalarFloat ctx.Sources spec.Value

        let valueText =
            match resolution with
            | BindingResolver.Resolved value -> Render.formatNumber spec.Format value
            | BindingResolver.NotResolved -> "—"
            | BindingResolver.Errored msg -> "(error: " + msg + ")"
            | BindingResolver.I18nUnresolved key -> "[i18n:" + key + "]"

        let weight = if spec.Emphasis then "700" else "400"

        layoutTable
            ""
            [ Html.tr
                  [ Html.td
                        [ prop.custom (
                              "style",
                              "padding:6px 0;border-bottom:1px solid "
                              + ruleColour
                              + ";"
                              + textStyle opts "font-size:14px;line-height:20px;"
                          )
                          prop.text (text spec.Label) ]
                    Html.td
                        [ prop.custom ("align", "right")
                          prop.custom (
                              "style",
                              "padding:6px 0;text-align:right;border-bottom:1px solid "
                              + ruleColour
                              + ";"
                              + textStyle opts ("font-size:14px;line-height:20px;font-weight:" + weight + ";")
                          )
                          prop.text valueText ] ] ]

    | NodeKind.Badge spec ->
        let colour = badgeColour spec.Variant

        Html.span
            [ prop.custom (
                  "style",
                  textIn
                      opts
                      colour
                      ("display:inline-block;padding:2px 8px;border:1px solid "
                       + colour
                       + ";font-size:12px;line-height:18px;")
              )
              prop.text (text spec.Label) ]

    | NodeKind.Callout spec ->
        let colour = toneColour spec.Tone

        let headingEls =
            match spec.Heading with
            | Some heading ->
                [ Html.p
                      [ prop.custom ("style", textIn opts colour "font-size:15px;line-height:20px;font-weight:600;")
                        prop.text (text heading) ] ]
            | None -> []

        layoutTable
            ("border:1px solid "
             + ruleColour
             + ";border-left:4px solid "
             + colour
             + ";background:"
             + panelColour
             + ";")
            [ Html.tr
                  [ Html.td
                        [ prop.custom ("style", "padding:12px 16px;")
                          prop.children (
                              headingEls
                              @ [ Html.p
                                      [ prop.custom (
                                            "style",
                                            textStyle opts "font-size:14px;line-height:20px;padding-top:2px;"
                                        )
                                        prop.text (text spec.Body) ] ]
                          ) ] ] ]

    | NodeKind.List spec ->
        let items =
            [ for item in spec.Items ->
                  Html.li
                      [ prop.custom ("style", textStyle opts "font-size:15px;line-height:22px;padding-bottom:4px;")
                        prop.text (text item) ] ]

        let listStyle = "margin:0;padding:0 0 0 22px;"

        if spec.Ordered then
            Html.ol [ prop.custom ("style", listStyle); prop.children items ]
        else
            Html.ul [ prop.custom ("style", listStyle); prop.children items ]

    | NodeKind.Link spec ->
        // The one genuine interaction an email has, and it passes the same gate
        // the SSR anchor does — Phase 1026's ambient destination policy, read
        // off the shared `ServerRenderContext`.
        let resolvedHref =
            BindingResolver.tryResolve ctx.Sources spec.Href |> Option.defaultValue ""

        // The refusal ATTRIBUTE is deliberately dropped here where the SSR
        // anchor keeps it: `data-*` attributes do not survive the sanitisers
        // most mail clients run, so emitting one would put a marker in the
        // document that cannot be relied on to arrive. The refusal itself is
        // not dropped — `safeHref` still becomes `egressRefusalUrl`, which is
        // the part that stops the destination being reached.
        let safeHref, _ =
            Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Hyperlink resolvedHref

        // A protected email link entity-encodes its address so no plaintext
        // address sits in the document source. That defence is aimed at
        // crawlers over a PUBLIC page; an email is already addressed to one
        // reader, and several clients rewrite anchors on delivery. The
        // projection therefore emits the ordinary anchor and keeps the
        // sanitisation, rather than shipping an entity blob a client may mangle.
        Html.a
            [ prop.custom ("style", textIn opts linkColour "font-size:15px;line-height:22px;text-decoration:underline;")
              prop.href safeHref
              prop.children [ Html.text (text spec.Label) ] ]

    | NodeKind.Image spec ->
        let resolvedSrc =
            BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

        // Phase 1026 — `Media`, and in a digest this is precisely the tracking
        // pixel: an undeclared `src` here is fetched by each recipient's client
        // on open, reporting that this named person read this message.
        let safeSrc, _ =
            Sanitize.sanitizeUrlForEgress ctx.EgressPolicy Sanitize.EgressClass.Media resolvedSrc

        Html.img
            [ prop.src safeSrc
              prop.alt (text spec.Alt)
              prop.custom ("border", "0")
              prop.custom ("style", "display:block;max-width:100%;height:auto;border:0;outline:none;") ]

    | NodeKind.Progress spec ->
        let fraction =
            match BindingResolver.resolve ctx.Sources spec.Fraction with
            | BindingResolver.Resolved value -> max 0.0 (min 1.0 value)
            | _ -> 0.0

        let pct = int (System.Math.Round(fraction * 100.0))
        let colour = toneColour spec.Tone

        let labelEls =
            match spec.Label with
            | Some label ->
                [ Html.p
                      [ prop.custom ("style", textStyle opts "font-size:13px;line-height:18px;padding-bottom:4px;")
                        prop.text (text label + " — " + string pct + "%") ] ]
            | None ->
                [ Html.p
                      [ prop.custom ("style", textStyle opts "font-size:13px;line-height:18px;padding-bottom:4px;")
                        prop.text (string pct + "%") ] ]

        // The percentage rides as TEXT as well as bar geometry: a client that
        // strips background colours then still conveys the figure.
        let bar =
            layoutTable
                ("border:1px solid " + ruleColour + ";")
                [ Html.tr
                      [ if pct > 0 then
                            Html.td
                                [ prop.custom ("width", string pct + "%")
                                  prop.custom (
                                      "style",
                                      "width:"
                                      + string pct
                                      + "%;background:"
                                      + colour
                                      + ";font-size:0;line-height:0;height:8px;"
                                  )
                                  prop.children [ Html.text " " ] ]
                        if pct < 100 then
                            Html.td
                                [ prop.custom ("width", string (100 - pct) + "%")
                                  prop.custom (
                                      "style",
                                      "width:"
                                      + string (100 - pct)
                                      + "%;background:"
                                      + panelColour
                                      + ";font-size:0;line-height:0;height:8px;"
                                  )
                                  prop.children [ Html.text " " ] ] ] ]

        stacked (labelEls @ [ bar ])

    | NodeKind.CodeBlock spec ->
        Html.pre
            [ prop.custom (
                  "style",
                  "margin:0;padding:12px;background:"
                  + panelColour
                  + ";border:1px solid "
                  + ruleColour
                  + ";font-family:Consolas, Courier New, monospace;font-size:13px;line-height:18px;color:"
                  + inkColour
                  + ";white-space:pre-wrap;"
              )
              prop.children [ Html.code [ prop.text spec.Code ] ] ]

    | NodeKind.Math spec ->
        // The declared narrowing: readable LaTeX rather than MathML no Word-
        // engine client will draw. `docs/SSR.md` records it as a scope decision.
        Html.span
            [ prop.custom (
                  "style",
                  textStyle opts "font-family:Consolas, Courier New, monospace;font-size:14px;line-height:20px;"
              )
              prop.custom ("data-fuaran-math-src", spec.Source)
              prop.text spec.Source ]

    | NodeKind.Toast spec ->
        let isOpen =
            BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

        if not isOpen then
            // OMITTED, not `[hidden]`. `[hidden]` is unreliable across clients,
            // and a notification that leaks into a digest it was closed in is a
            // disclosure bug, not a cosmetic one.
            omitted
        else
            let colour = toneColour spec.Tone

            layoutTable
                ("border:1px solid " + colour + ";background:" + panelColour + ";")
                [ Html.tr
                      [ Html.td
                            [ prop.custom ("style", "padding:10px 14px;")
                              prop.children
                                  [ Html.p
                                        [ prop.custom ("style", textIn opts colour "font-size:14px;line-height:20px;")
                                          prop.text (text spec.Message) ] ] ] ] ]

    | NodeKind.DataGrid spec ->
        match spec.StaticRows with
        | Some sr ->
            let headerCells =
                [ for h in sr.Headers ->
                      Html.th
                          [ prop.custom ("align", "left")
                            prop.custom (
                                "style",
                                "padding:8px 10px;text-align:left;border:1px solid "
                                + ruleColour
                                + ";background:"
                                + panelColour
                                + ";"
                                + textStyle opts "font-size:13px;line-height:18px;font-weight:600;"
                            )
                            prop.text (text h) ] ]

            let bodyRows =
                [ for row in sr.Rows ->
                      Html.tr
                          [ for cell in row ->
                                Html.td
                                    [ prop.custom (
                                          "style",
                                          "padding:8px 10px;border:1px solid "
                                          + ruleColour
                                          + ";"
                                          + textStyle opts "font-size:13px;line-height:18px;"
                                      )
                                      prop.text (text cell) ] ] ]

            Html.table (
                tableProps ""
                @ [ prop.children [ Html.thead [ Html.tr headerCells ]; Html.tbody bodyRows ] ]
            )
        | None ->
            // A client-library grid has no rows server-side to lay out. Link.
            live "Table"

    // ── Interactive + client-drawn: labelled open-live links, never controls ──
    | NodeKind.Button _ -> live "Action"
    | NodeKind.Form _ -> live "Form"
    | NodeKind.Select _ -> live "Selection"
    | NodeKind.FileUpload _ -> live "File upload"
    | NodeKind.Filters _ -> live "Filters"
    | NodeKind.Tabs _ -> live "Tabbed section"
    | NodeKind.Stepper _ -> live "Step-by-step section"
    | NodeKind.Modal _ -> live "Dialog"
    | NodeKind.Chart spec ->
        live (
            spec.Title
            |> Option.map text
            |> Option.filter (fun s -> s <> "")
            |> Option.defaultValue "Chart"
        )
    | NodeKind.Map _ -> live "Map"
    // Phase 1076 — the media transport. The node's own `Label` is the link
    // text, falling back to the surface name only when it resolves to nothing:
    // a mandatory accessible name is precisely a sentence describing what the
    // player plays, which is what an "open live" link wants to say anyway.
    | NodeKind.Media spec ->
        let surface =
            match spec.Kind with
            | MediaKind.Video _ -> "Video"
            | MediaKind.Audio -> "Audio"

        // `liveLabel` prefers the node's `Accessibility.Label`; the spec's own
        // mandatory `Label` is the next-best answer and the surface name is the
        // last resort, so a reader is told what the recording is rather than
        // that a recording exists.
        live (
            match text spec.Label with
            | "" -> surface
            | label -> label
        )
    | NodeKind.Sparkline _ -> live "Trend"
    | NodeKind.Drawing _ -> live "Diagram"
    | NodeKind.Custom _ -> live "Component"
    | NodeKind.Mount _ -> live "Embedded view"

    // ── Zero-paint ───────────────────────────────────────────────────────────
    | NodeKind.Icon _ -> omitted
    | NodeKind.Skeleton _ -> omitted

// ─── Email-hostile-construct lint ───────────────────────────────────────────
//
// The client-matrix question ("does Outlook draw this?") cannot be answered
// offline, and this lint does not pretend to answer it. What it DOES answer is
// the falsifiable half: whether the emitted HTML contains a construct the
// client matrix is already known to break on. A clean lint is not a claim of
// email-safety; a dirty one is proof of the opposite, and that asymmetry is
// worth automating. `docs/SSR.md` carries the matrix itself, marked pending
// real-client validation.

/// One finding. `Construct` is the literal token matched, so a finding is
/// reproducible by grep rather than by re-running the scanner.
type LintFinding =
    { Code: string
      Construct: string
      Detail: string }

let private hostileConstructs: (string * string * string) list =
    [ "EMAIL-FLEX",
      "display:flex",
      "flexbox is unsupported in Outlook's Word engine and unreliable in several webmail clients"
      "EMAIL-FLEX", "display:inline-flex", "as EMAIL-FLEX"
      "EMAIL-FLEX", "flex-direction", "a flex declaration implies a flex container"
      "EMAIL-GRID", "display:grid", "CSS grid has no meaningful support in email; lay out with tables"
      "EMAIL-GRID", "grid-template", "a grid declaration implies a grid container"
      "EMAIL-GRID", "gap:", "the gap shorthand only applies to flex/grid containers, so its presence implies one"
      "EMAIL-POSITION", "position:fixed", "positioning is stripped or ignored; content escapes the message body"
      "EMAIL-POSITION", "position:absolute", "as EMAIL-POSITION"
      "EMAIL-POSITION", "position:sticky", "as EMAIL-POSITION"
      "EMAIL-EXTERNAL-CSS", "<link", "external stylesheets are not fetched; every rule must be inline"
      "EMAIL-EXTERNAL-CSS",
      "<style",
      "embedded style blocks are stripped by Gmail and others; every rule must be inline"
      "EMAIL-EXTERNAL-CSS", "@import", "as EMAIL-EXTERNAL-CSS"
      "EMAIL-EXTERNAL-CSS", "var(--", "CSS custom properties do not resolve in the clients this projection targets"
      "EMAIL-SCRIPT", "<script", "scripts never execute in an email client and mark the message as suspicious"
      "EMAIL-SCRIPT", "javascript:", "as EMAIL-SCRIPT"
      "EMAIL-SCRIPT", " onclick=", "inline event handlers never fire"
      "EMAIL-SCRIPT", " onload=", "as EMAIL-SCRIPT"
      "EMAIL-SCRIPT", " onerror=", "as EMAIL-SCRIPT"
      "EMAIL-CONTROL", "<form", "a form that cannot post is worse than an absent one - project to an open-live link"
      "EMAIL-CONTROL", "<button", "a button that cannot fire is a half-working control"
      "EMAIL-CONTROL", "<input", "as EMAIL-CONTROL"
      "EMAIL-CONTROL", "<select", "as EMAIL-CONTROL"
      "EMAIL-CONTROL", "<textarea", "as EMAIL-CONTROL"
      "EMAIL-EMBED", "<iframe", "embedded documents are stripped by every mainstream client"
      "EMAIL-EMBED", "<svg", "Outlook's Word engine does not draw inline SVG - link to the live view instead"
      "EMAIL-EMBED", "<canvas", "as EMAIL-EMBED"
      "EMAIL-EMBED", "<video", "as EMAIL-EMBED"
      "EMAIL-EMBED", "<audio", "as EMAIL-EMBED"
      "EMAIL-EMBED", "<object", "as EMAIL-EMBED"
      "EMAIL-EMBED", "<embed", "as EMAIL-EMBED"
      "EMAIL-DIV-LAYOUT",
      "<div",
      "this projection lays out entirely in tables, so a div is evidence of an unaudited emission path"
      // Found by reading the first generated golden rather than by reasoning:
      // a quoted CSS font family is escaped to `&apos;` on the way into the
      // style attribute, and an HTML4-era mail parser prints that literally
      // instead of decoding it — which invalidates the whole declaration and
      // drops the message to the client's default serif.
      "EMAIL-ENTITY-QUOTE",
      "&apos;",
      "an apostrophe entity inside a style attribute is not decoded by HTML4-era mail parsers; use an unquoted CSS identifier sequence"
      "EMAIL-ENTITY-QUOTE", "&#39;", "as EMAIL-ENTITY-QUOTE" ]

/// Scan emitted HTML for constructs the client matrix is known to break on.
///
/// An ordinal, case-insensitive substring scan — deliberately, not a parser. It
/// is a smoke detector over output THIS module produced, not a sanitiser for
/// arbitrary HTML, and it is sound in that direction only: it cannot certify a
/// document, and every finding is a genuine construct present in the bytes.
let lint (html: string) : LintFinding list =
    if System.String.IsNullOrEmpty html then
        []
    else
        hostileConstructs
        |> List.filter (fun (_, token, _) -> html.Contains(token, System.StringComparison.OrdinalIgnoreCase))
        |> List.map (fun (code, token, detail) ->
            { Code = code
              Construct = token
              Detail = detail })

// ─── Public entry points ────────────────────────────────────────────────────

/// Render a tree to an email-safe body fragment — the content column, ready to
/// drop inside a host's own `<body>` or a mock-inbox frame.
let renderWith (opts: EmailOptions) (sources: BindingResolver.BindingSources) (node: Node<obj>) : string =
    // Phase 1026 — the projection's tree-authored destinations are checked
    // against the policy the host declared in `opts`.
    let ctx = Render.mkContextWithEgress opts.EgressPolicy Registry.empty sources node
    let body = renderNode opts 1 ctx node

    let column =
        Html.table (
            [ prop.custom ("role", "presentation")
              prop.custom ("cellpadding", "0")
              prop.custom ("cellspacing", "0")
              prop.custom ("border", "0")
              prop.custom ("width", string opts.MaxWidthPx)
              prop.custom (
                  "style",
                  "border-collapse:collapse;width:100%;max-width:"
                  + string opts.MaxWidthPx
                  + "px;margin:0 auto;background:#ffffff;"
              ) ]
            @ [ prop.children
                    [ Html.tbody
                          [ Html.tr [ Html.td [ prop.custom ("style", "padding:24px;"); prop.children [ body ] ] ] ] ] ]
        )

    // The outer 100%-width table is the conventional centring wrapper: margin
    // auto alone does not centre in Outlook, and `align="center"` does.
    Render.htmlView (
        Html.table (
            tableProps ("background:" + panelColour + ";")
            @ [ prop.children
                    [ Html.tbody
                          [ Html.tr
                                [ Html.td
                                      [ prop.custom ("align", "center")
                                        prop.custom ("style", "padding:16px;text-align:center;")
                                        prop.children [ column ] ] ] ] ] ]
        )
    )

/// Render with the conventional defaults (no live surface declared).
let render (sources: BindingResolver.BindingSources) (node: Node<obj>) : string = renderWith defaults sources node

/// Render a tree with no dynamic bindings — the common scheduled-digest case.
let renderStatic (node: Node<obj>) : string = render BindingResolver.empty node

/// A complete, sendable email document: doctype, the two meta tags every client
/// wants, a `<title>`, and the body fragment. A fragment is not an email; this
/// is the entry a digest sender uses.
///
/// The subject rides the `<title>` only — the envelope's Subject header is the
/// sender's concern, not the renderer's.
let renderDocument
    (opts: EmailOptions)
    (subject: string)
    (sources: BindingResolver.BindingSources)
    (node: Node<obj>)
    : string =
    let head =
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n"
        + "<meta charset=\"utf-8\" />\n"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n"
        + "<title>"
        + Render.htmlView (Html.text subject)
        + "</title>\n</head>\n<body style=\"margin:0;padding:0;background:"
        + panelColour
        + ";\">\n"

    head + renderWith opts sources node + "\n</body>\n</html>"
