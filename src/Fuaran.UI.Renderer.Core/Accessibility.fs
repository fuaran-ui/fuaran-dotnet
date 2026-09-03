module Fuaran.UI.Renderer.Accessibility

// ============================================================================
//  Fuaran — accessibility-attribute emission (pure spine, Phase 138).
//
//  Renders a `Node`'s `Accessibility` trait into a list of `(key, value)`
//  HTML-attribute pairs. The client renderer feeds these into `prop.custom`;
//  the server renderer (Phase 140) feeds the same pairs into ViewEngine
//  attributes — one parity-locked projection, two emission backends.
//
//  Kept as a pure helper (no Feliz / Fable reference) so `AccessibilityTests.fs`
//  can assert the attribute set without spinning up a ReactElement (Feliz's
//  .NET-side ReactElement is opaque; `(string * string) list` is the pure-F#
//  contract the tests pin) AND so the server renderer can call it from a
//  pure-.NET context. Extracted out of the Feliz-coupled `Render.fs` into
//  `Fuaran.UI.Renderer.Core` for exactly that reuse; `Render.fs` keeps a thin
//  re-export (`Render.accessibilityAttributes`) so existing call sites and
//  tests are unchanged.
//
//  `aria-labelledby` / `aria-describedby` carry the referenced Node's HTML
//  `id` — same string the `render` function emits as `prop.id` (the
//  `NodeId`'s inner string).
//
//  WHERE the projection lands is a separate question from what it contains,
//  and `forwardsToSemanticElement` below is the answer: see `docs/DECISIONS.md`
//  D4 (2026-08-20). Until then every renderer emitted the projection on the
//  node's wrapper `<div>` unconditionally, which put `role` / `aria-*` on a
//  non-interactive container for a kind whose body IS the semantic element.
// ============================================================================

open Fuaran.UI.Types

let private ariaRoleString (role: AriaRole) : string =
    match role with
    | AriaRole.Button -> "button"
    | AriaRole.Link -> "link"
    | AriaRole.Dialog -> "dialog"
    | AriaRole.Alert -> "alert"
    | AriaRole.Status -> "status"
    | AriaRole.Banner -> "banner"
    | AriaRole.Navigation -> "navigation"
    | AriaRole.Main -> "main"
    | AriaRole.Form -> "form"
    | AriaRole.Region -> "region"
    | AriaRole.Heading -> "heading"
    | AriaRole.Progressbar -> "progressbar"
    | AriaRole.Tab -> "tab"
    | AriaRole.Tablist -> "tablist"
    | AriaRole.Tabpanel -> "tabpanel"
    | AriaRole.Custom raw -> raw

let private liveRegionString (kind: LiveRegionKind) : string =
    match kind with
    | LiveRegionKind.Polite -> "polite"
    | LiveRegionKind.Assertive -> "assertive"
    | LiveRegionKind.Off -> "off"

/// Pure-F# helper: project an `Accessibility option` (resolved against the
/// supplied `BindingSources`) into the `(attr-name, attr-value)` pairs the
/// renderer emits as DOM attributes. Public so `AccessibilityTests.fs` can
/// assert the projection without a Feliz round-trip, and so the server
/// renderer (Phase 140) can emit the same pairs without Feliz/Fable.
let accessibilityAttributes
    (sources: BindingResolver.BindingSources)
    (a11y: Accessibility option)
    : (string * string) list =
    match a11y with
    | None -> []
    | Some a ->
        let labelAttr =
            a.Label
            |> Option.bind (fun b -> BindingResolver.tryResolve sources b)
            |> Option.filter (fun t -> t <> "")
            |> Option.map (fun t -> "aria-label", t)

        let labelledByAttr = a.LabelledBy |> Option.map (fun nid -> "aria-labelledby", nid)

        let describedByAttr =
            a.DescribedBy |> Option.map (fun nid -> "aria-describedby", nid)

        let roleAttr = a.Role |> Option.map (fun r -> "role", ariaRoleString r)

        let liveAttr = a.LiveRegion |> Option.map (fun k -> "aria-live", liveRegionString k)

        let hiddenAttr =
            a.Hidden
            |> Option.bind (fun b -> BindingResolver.tryResolve sources b)
            |> Option.bind (fun h -> if h then Some("aria-hidden", "true") else None)

        [ labelAttr; labelledByAttr; describedByAttr; roleAttr; liveAttr; hiddenAttr ]
        |> List.choose id

/// Does this kind render a body that IS the node's semantic element — so the
/// a11y projection belongs on the body, not on the wrapper `<div>`?
///
/// Three conditions, all required (`docs/DECISIONS.md` D4):
///
///  1. the body is a SINGLE root element — not a container of siblings, not a
///     label-wrapped control;
///  2. that element carries native semantics of its own (an interactive role,
///     or a graphic), so `role` / `aria-*` on an ancestor `<div>` is announced
///     against the wrong node;
///  3. the element IS the node — nothing else in the body competes for the
///     accessible name.
///
/// `Link` (`<a>`), `Button` (`<button>`) and `Image` (`<img>`) satisfy all
/// three. The form-field kinds deliberately do NOT: `Select` renders
/// `<label><span>…</span><select></label>`, so the control is not the body root
/// (1) and the wrapping `<label>` already supplies an accessible name (3) — a
/// forwarded `aria-label` would compete with it. Field-level targeting needs a
/// per-kind target selector, not this predicate.
///
/// Kind-level by construction: the wrapper must decide before the body is
/// rendered, and the only thing it has then is the `NodeKind`. Where an arm has
/// a runtime branch (the protected-email `Link`), the ARM owns placement within
/// its own body — see the `Link` arm in either renderer.
let forwardsToSemanticElement (kind: NodeKind<'Msg>) : bool =
    match kind with
    | NodeKind.Link _
    | NodeKind.Button _
    | NodeKind.Image _
    // Phase 1076 — `Media` satisfies all three on the same reading `Image`
    // does. The `<video>` / `<audio>` IS the body root, it carries native
    // interactive semantics (a transport a user focuses and operates), and
    // nothing else in the body competes for the name. As with `Image`'s `alt`,
    // a node-level `Accessibility.Label` overrides the spec's own `label` —
    // which is the right precedence: the node-level slot is the author saying
    // this specific instance is named something else.
    // Phase 1111 — `Embed` satisfies the same three. The `<iframe>` IS the body
    // root, a frame carries native interactive semantics (it is a focus
    // container a reader tabs into), and nothing else competes for the name. A
    // node-level `Accessibility.Label` overrides the spec's `title` on
    // `Media`'s precedence, which is what a node-level slot is for.
    | NodeKind.Embed _ -> true
    | _ -> false

// ─── The `dir="auto"` policy (Phase 1114) ───────────────────────────────────
//
// A document declares ONE direction (`<html dir>`, derived from its locale).
// That is right for everything the AUTHOR wrote and wrong for everything the
// DATA carries: an Arabic customer name inside an English page, an English
// product code inside an Arabic one. `dir="auto"` is the HTML answer — the
// browser reads the element's first strong directional character and lays that
// element out accordingly — and the whole question is which elements get it.
//
// THE DECIDED SLOT SET: a node whose visible text is supplied at RUNTIME
// through a `TextSource.Bound`, and which is a DISPLAY LEAF.
//
// Both halves are load-bearing.
//
//  - RUNTIME, not authored. `TextSource.Literal` is the author writing in the
//    document's language, and `TextSource.I18n` is a host-resolved translation
//    of the document's own locale; both are correct under the document's `dir`
//    and putting `auto` on them would let one stray character re-lay-out a line
//    the author controls. `Bound` is the only case whose direction genuinely
//    cannot be known at author time, which is exactly when `auto` is right.
//
//  - DISPLAY LEAF, not container. `dir` is inherited, so `auto` on a layout
//    container would resolve ONE direction from the first strong character
//    anywhere beneath it and impose it on every child — the opposite of what a
//    mixed-direction page needs. Every layout kind is therefore `false`, and a
//    mixed tree gets its isolation from the leaves that actually carry data.
//
// Interactive kinds are `false` too, but for a third reason: a control's label
// is authored, and its VALUE is inside a form control, where the browser
// already applies bidi isolation to the field's own contents.
//
// Kind-level and total by construction, exactly like `forwardsToSemanticElement`
// above: the wrapper must decide before the body is rendered, and the only
// thing it has then is the `NodeKind`. Adding a kind therefore forces an answer
// here rather than defaulting to silence.

let private isBoundText (text: TextSource) : bool =
    match text with
    | TextSource.Bound _ -> true
    | TextSource.Literal _
    | TextSource.I18n _ -> false

/// Does this kind display runtime-bound text that needs its own bidi
/// isolation? See the policy note above for what decides it.
let isBidiIsolated (kind: NodeKind<'Msg>) : bool =
    match kind with
    // Display leaves whose primary text is a `TextSource`.
    | NodeKind.Heading s -> isBoundText s.Text
    | NodeKind.Badge s -> isBoundText s.Label
    | NodeKind.Markdown s -> isBoundText s.Text
    | NodeKind.List s -> s.Items |> List.exists isBoundText
    | NodeKind.Link s -> isBoundText s.Label
    | NodeKind.Media s -> isBoundText s.Label
    // Phase 1111 — `Title` is attribute text, not laid-out content, so there is
    // nothing on the page for `auto` to resolve. This is `Image.Alt`'s reading,
    // and unlike `Image` there is no laid-out half to qualify: the embedded
    // document's own text belongs to the embedded document.
    | NodeKind.Embed _ -> false
    | NodeKind.Callout s ->
        isBoundText s.Body
        || (s.Heading |> Option.map isBoundText |> Option.defaultValue false)
    | NodeKind.Metric s ->
        isBoundText s.Label
        || (s.Subtext |> Option.map isBoundText |> Option.defaultValue false)
    | NodeKind.LabelValueRow s -> isBoundText s.Label
    | NodeKind.Fact s -> isBoundText s.Label || isBoundText s.Value
    | NodeKind.Toast s -> isBoundText s.Message
    | NodeKind.Progress s -> s.Label |> Option.map isBoundText |> Option.defaultValue false
    // `Image`'s bound slot is `Alt` — attribute text, not laid-out content, so
    // there is nothing on the page for `auto` to resolve. Its `Caption` IS laid
    // out, and is the half that qualifies.
    | NodeKind.Image s -> s.Caption |> Option.map isBoundText |> Option.defaultValue false
    // Display leaves carrying no `TextSource` at all: their content is a glyph,
    // a number, a shape, or author-written source.
    | NodeKind.Math _
    | NodeKind.Skeleton _
    | NodeKind.Icon _
    | NodeKind.Sparkline _
    | NodeKind.CodeBlock _
    | NodeKind.Drawing _
    // Layout containers — `dir` is inherited, so `auto` here would impose one
    // resolved direction on every descendant.
    | NodeKind.Box _
    | NodeKind.SplitPanel _
    | NodeKind.SummaryList _
    | NodeKind.Disclosure _
    | NodeKind.Modal _
    | NodeKind.ScrollArea _
    | NodeKind.Tabs _
    | NodeKind.Stepper _
    | NodeKind.ErrorBoundary _
    | NodeKind.FragmentDecl _
    | NodeKind.FragmentRef _
    | NodeKind.Switch _
    | NodeKind.Mount _
    // Interactive kinds — authored labels, and the browser already isolates a
    // form control's own value.
    | NodeKind.Button _
    | NodeKind.Select _
    | NodeKind.FileUpload _
    | NodeKind.Form _
    | NodeKind.Filters _
    // Data surfaces that own their own cell-level emission. A grid, a chart and
    // a map lay out per-cell / per-datum text through their own arms, and a
    // wrapper-level `auto` would resolve one direction for the whole surface —
    // the container argument again, on a kind that happens to be a leaf.
    | NodeKind.DataGrid _
    | NodeKind.Chart _
    | NodeKind.Map _
    // A custom kind's body is host-rendered: this library cannot know what text
    // it puts on the page, and asserting a direction over it would be a claim
    // about content it never saw.
    | NodeKind.Custom _ -> false

/// The wrapper attribute pairs the policy above emits — empty, or one `dir`.
/// Returned as pairs rather than a bool so both renderer arms append it the
/// same way they append every other wrapper attribute.
///
/// Phase 1472 — a DECLARED direction wins over the Phase 1114 heuristic, and
/// the precedence runs that way round for one reason: `auto` is an inference
/// from the value's own first strong character, and the declaration exists
/// precisely for the values that inference gets wrong. A reference number
/// beginning with a Hebrew-lettered prefix resolves `auto` to right-to-left
/// and reorders the digits after it; `dir="ltr"` is the document saying so.
/// An `Auto` declaration is the identity and changes nothing — it falls
/// through to the heuristic, which is what a document that says nothing gets.
let bidiAttributes (kind: NodeKind<'Msg>) (style: SemanticStyle) : (string * string) list =
    match style.Direction with
    | TextDirection.Ltr -> [ "dir", "ltr" ]
    | TextDirection.Rtl -> [ "dir", "rtl" ]
    | TextDirection.Auto -> if isBidiIsolated kind then [ "dir", "auto" ] else []

/// Split already-sanitised `ExtraAttributes` pairs into the half that stays on
/// the wrapper and the half that follows the a11y projection: `(data-*, aria-*)`.
///
/// `data-*` is ADDRESSING — it sits beside `data-fuaran-node-id`, which is what
/// layout observers, DOM-snapshot hooks and the in-page introspection surface
/// scan for, so moving it would move the node's address. An `aria-*` hatch is an
/// accessibility attribute and belongs wherever the accessibility attributes go;
/// that half is the whole of the `aria-current` defect D4 records.
///
/// Only consulted for a kind that forwards — elsewhere both halves land on the
/// wrapper in the Map's key-sorted order, exactly as before.
let partitionExtraAttributes (pairs: (string * string) list) : (string * string) list * (string * string) list =
    pairs
    |> List.partition (fun (k, _) -> not (k.StartsWith("aria-", System.StringComparison.Ordinal)))

// ─── The node-level tooltip trait (Phase 1112) ──────────────────────────────
//
// A tooltip is a DESCRIPTION of the node, revealed by the renderer's own hover /
// focus / long-press affordance and announced through `aria-describedby`. Two
// placement questions follow from one principle, and getting either wrong makes
// the hint reach nobody:
//
//  1. THE ELEMENT THAT CARRIES `aria-describedby` MUST BE THE ELEMENT THAT TAKES
//     FOCUS. A description on a wrapper the keyboard never lands on is announced
//     on no interaction at all; a description on a control while the wrapper is
//     the focus stop is the same failure with the parts swapped.
//  2. A node whose body is not a focus stop therefore needs one — `tabindex="0"`
//     on the wrapper — or the hint is pointer-only, which is WCAG 2.1.1.
//
// So the two decisions are ONE decision, taken here, and both renderers read it:
// where the projection forwards to a semantic element that takes focus natively,
// the description rides that element and the wrapper stays untouched; everywhere
// else the description rides the wrapper and the wrapper takes the focus stop.
//
// `Image` is the case that shows why this is not simply `forwardsToSemanticElement`:
// it forwards, and `<img>` takes no focus, so an image with a hint needs the
// wrapper stop AND the wrapper description — the pair, or neither.

/// Does a node-level tooltip's `aria-describedby` ride the kind's own semantic
/// element (rather than the wrapper)? True exactly when the projection forwards
/// AND the forwarded-to element is a native focus stop — see the note above.
///
/// A narrow allow-list rather than an exhaustive match, deliberately: the DEFAULT
/// answer (describe the wrapper, and give the wrapper a focus stop) is always
/// reachable and correct, at worst one redundant tab stop on a node the author
/// chose to annotate. The opposite default silently loses the keyboard route
/// altogether, which is not a failure anything downstream would report.
let tooltipRidesSemanticElement (kind: NodeKind<'Msg>) : bool =
    forwardsToSemanticElement kind
    && (match kind with
        // `<button>`, `<a href>`, `<video controls>` / `<audio controls>` and
        // `<iframe>` are each a native focus stop.
        | NodeKind.Button _
        | NodeKind.Link _
        | NodeKind.Media _
        | NodeKind.Embed _ -> true
        // `Image` forwards its projection to `<img>`, which is not focusable.
        | _ -> false)

/// Merge a tooltip's hint id into an attribute list's `aria-describedby`.
///
/// Appended, never substituted: `aria-describedby` is an ID LIST, and a node
/// that declares `accessibility.describedBy` AND carries a hint has said two
/// different things a reader is owed both of. Overwriting would silently drop
/// whichever the renderer happened to apply second.
let withTooltipDescribedBy (hintId: string) (attrs: (string * string) list) : (string * string) list =
    if attrs |> List.exists (fun (k, _) -> k = "aria-describedby") then
        attrs
        |> List.map (fun (k, v) -> if k = "aria-describedby" then k, v + " " + hintId else k, v)
    else
        attrs @ [ "aria-describedby", hintId ]

/// The DOM id of the hint element a node's tooltip renders as. Derived from the
/// node id so both renderers, and any host reading the emitted markup, compute
/// the same string without carrying a second identifier on the wire.
let tooltipHintId (nodeId: string) : string = nodeId + "-tooltip"
