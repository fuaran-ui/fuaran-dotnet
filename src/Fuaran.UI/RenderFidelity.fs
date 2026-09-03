module Fuaran.UI.RenderFidelity

// ============================================================================
//  The render-fidelity manifest (Phase 442) — the declared, per-NodeKind
//  answer to "which render tiers exist for this kind, what does the
//  parity-checked fallback pin, and what is declared client-only rich?".
//
//  The tiers themselves are not new. They were pinned by the shipped fidelity
//  contracts — the overlay/overflow contract (Phase 289), the deterministic
//  `<pre><code>` + client-only highlight seam (290), the one deterministic GFM
//  renderer (292), and the MathML/escaped-source floor + client-only KaTeX
//  (293/658) — and they have lived in prose ever since: WIRE_FORMAT §3.2,
//  §14, and `fuaran-dotnet/docs/SSR.md`. Prose is not derivable. A consumer
//  that wants to SAY which tier it is delivering for a given node (a fidelity
//  badge, a certification report, a degradation ladder) had to hand-annotate,
//  and a hand annotation is a second source of truth that drifts silently.
//
//  This table makes the existing contract explicit and machine-readable. It
//  changes no wire byte and no renderer behaviour: every row TRANSCRIBES what
//  already ships. It is emitted as the corpus-root artefact
//  `render-fidelity.json` beside `schema.json`, and the completeness test
//  asserts one row per canonical wire kind — the Phase 430 capability-table
//  discipline applied to fidelity, so the class cannot silently grow.
//
//  Placement note (the same call Phase 430 made for `SlotCapability`): the
//  table lands in `Fuaran.UI` rather than `Fuaran.UI.Validator`. The Validator
//  is a build-time F# AST walker; this declaration is read by renderers, host
//  tiers and the emitted artefact alike, and must stay Fable-compatible.
// ============================================================================

open Fuaran.UI.Types

/// The third tier: what a client-only pass adds on top of the parity-checked
/// fallback, and whether it may touch the hydrated DOM.
///
/// The distinction is load-bearing. `ClientOnly` REPLACES or upgrades the
/// floor's DOM after hydration and is therefore deliberately outside every
/// byte-diff (cross-host and SSR-CSR alike). `Behavioural` attaches behaviour
/// at hydration and must NOT alter the hydrated DOM — it cannot cause a
/// mismatch by construction, which is exactly why the overlay contract admits
/// focus management while refusing a portal.
[<RequireQualifiedAccess>]
type RichTier =
    /// No client-only tier. The parity-checked render IS the whole render.
    | None
    /// Additive behaviour attached on hydration that does not alter the
    /// hydrated DOM (focus traps, keyboard navigation, event handlers on an
    /// otherwise inert control).
    | Behavioural of enhancement: string * seam: string
    /// A declared client-only render that changes the DOM after hydration, and
    /// is excluded from every parity comparison by contract.
    | ClientOnly of technique: string * seam: string

/// A RENDER OBLIGATION a conformant host owes for a kind (Phase 1105) — one
/// member of a CLOSED vocabulary of checkable claims.
///
/// The distinction from the `Fallback` prose beside it is the whole point. That
/// prose is a paragraph: complete, normative, and unfalsifiable by a machine, so
/// a host can render the kind, pass every byte-parity fixture, and still have
/// silently dropped an obligation the paragraph states. (The one media defect
/// the Rust compiler could not catch was exactly that — a boolean site, not a
/// missing arm.) These claims are the checkable REMAINDER: each names one
/// consequence a host's own render suite can assert in emitted output, and each
/// is bound to the spec section that states it.
///
/// The set is closed on purpose. A host reads the manifest, matches each claim
/// it knows, and reports every claim it does NOT — so a new obligation lands in
/// every adopting host as an unasserted claim rather than as a paragraph nobody
/// re-read. It is deliberately NOT a free-form string: an open vocabulary would
/// let a host silently accept a claim it has no checker for.
[<RequireQualifiedAccess>]
type ObligationClaim =
    /// An accessible name is emitted on EVERY instance of the kind — never only
    /// where an author supplied a decorative-looking value.
    | AccessibleNameAlways
    /// `autoplay` is emitted only together with `muted`; neither appears alone.
    | AutoplayMutedPairing
    /// A variant that declares no autoplay slot emits no autoplay pathway of any
    /// kind — not the attribute, not its paired companion.
    | NoAutoplayPathway
    /// A secondary source the URL-scheme + egress floor refuses is DROPPED
    /// rather than emitted at the refusal URL.
    | RefusedSourceDropped
    /// The alternative-text attribute is emitted always, the empty string
    /// included — an absent attribute and an empty one are different claims to
    /// assistive technology.
    | AltAlwaysEmitted
    /// A declared expansion emits a real, working anchor to the full-size asset
    /// — the affordance survives with no script at all.
    | AnchorAffordanceOnExpandable
    /// A source the egress floor refused emits NO affordance: an expansion that
    /// cannot be honoured is worse than none.
    | RefusedSrcNoAffordance
    /// The caption sits OUTSIDE the expansion anchor, so the caption text is not
    /// part of the link target.
    | FigureCaptionOutsideLink
    /// Responsive candidates are emitted in ascending width order, and a
    /// candidate the egress floor refuses is dropped rather than emitted.
    | SrcSetAscendingByWidth
    /// An unregistered `Custom` node for which a CONTRACT CARD is available
    /// renders a labelled placeholder derived from that card — the component
    /// identity, the card's summary, and a machine-readable verdict marker — and
    /// never a blank, never a guess. Where no card is available the placeholder
    /// is unchanged.
    | UnregisteredCustomLabelled
    /// Repeated children are emitted in the AUTHORED order the wire carries,
    /// never re-sorted - the order is content, not a candidate list an algorithm
    /// picks from.
    | AuthoredChildOrder
    /// At most one repeated child per declared kind carries the default marker;
    /// a later duplicate election is emitted WITHOUT it rather than producing
    /// two defaults the user agent must choose between.
    | SingleDefaultPerKind
    /// A declared transcript renders as a disclosure beside the transport,
    /// carrying its own accessible name - never inside an element that would
    /// hide it, and never as an unnamed toggle.
    | TranscriptDisclosureNamed
    /// The sandbox declaration is emitted on EVERY instance — empty when the
    /// document grants nothing — and the relaxation tokens emitted across every
    /// attribute that carries one are EXACTLY the declared set, de-duplicated
    /// and in the vocabulary's declaration order. Omitting the attribute on a
    /// permissionless instance would be the same markup as no sandbox at all,
    /// and emitting a token nobody declared would make the declaration advisory.
    | SandboxAlwaysExactlyDeclared
    /// A PRIMARY source the egress floor refuses omits the source attribute
    /// entirely rather than substituting the refusal URL — a browsing context
    /// pointed at a refusal URL renders that document, where one with no source
    /// is a well-defined empty context that fetches nothing. The refusal is
    /// still recorded in the emitted markup.
    | RefusedEmbedSourceOmitted
    /// Every declared ingress route is ADDITIONAL: the file picker and its label
    /// are emitted whatever gestures the document declares, so the
    /// keyboard-accessible route survives and a no-script host renders a working
    /// upload. A host that replaced the picker with a drop zone would leave a
    /// pointer-only control behind, and a static one would leave no control at
    /// all.
    | PickerAlwaysPresent

/// The stable wire token for a claim. This is what the artefact carries and what
/// a host's checker registry is keyed by, so it may not change without a version
/// — the `match` is exhaustive, so a new case fails to compile until it is named.
let claimId (claim: ObligationClaim) : string =
    match claim with
    | ObligationClaim.AccessibleNameAlways -> "accessible-name-always"
    | ObligationClaim.AutoplayMutedPairing -> "autoplay-muted-pairing"
    | ObligationClaim.NoAutoplayPathway -> "no-autoplay-pathway"
    | ObligationClaim.RefusedSourceDropped -> "refused-source-dropped"
    | ObligationClaim.AltAlwaysEmitted -> "alt-always-emitted"
    | ObligationClaim.AnchorAffordanceOnExpandable -> "anchor-affordance-on-expandable"
    | ObligationClaim.RefusedSrcNoAffordance -> "refused-src-no-affordance"
    | ObligationClaim.FigureCaptionOutsideLink -> "figure-caption-outside-link"
    | ObligationClaim.SrcSetAscendingByWidth -> "srcset-ascending-by-width"
    | ObligationClaim.UnregisteredCustomLabelled -> "unregistered-custom-labelled"
    | ObligationClaim.AuthoredChildOrder -> "authored-child-order"
    | ObligationClaim.SingleDefaultPerKind -> "single-default-per-kind"
    | ObligationClaim.TranscriptDisclosureNamed -> "transcript-disclosure-named"
    | ObligationClaim.SandboxAlwaysExactlyDeclared -> "sandbox-always-exactly-declared"
    | ObligationClaim.RefusedEmbedSourceOmitted -> "refused-embed-source-omitted"
    | ObligationClaim.PickerAlwaysPresent -> "picker-always-present"

/// What the claim MEANS, kind-independently — the vocabulary entry a host reads
/// when it meets a claim id it does not yet implement, so "unchecked" can be
/// reported with the obligation's substance rather than as a bare token.
let claimMeaning (claim: ObligationClaim) : string =
    match claim with
    | ObligationClaim.AccessibleNameAlways ->
        "an accessible name is emitted on every instance of the kind, never only where an author supplied one"
    | ObligationClaim.AutoplayMutedPairing ->
        "autoplay is emitted only together with muted; neither attribute ever appears without the other"
    | ObligationClaim.NoAutoplayPathway ->
        "a variant declaring no autoplay slot emits no autoplay pathway at all - neither the attribute nor its pair"
    | ObligationClaim.RefusedSourceDropped ->
        "a secondary source the URL-scheme and egress floor refuses is dropped rather than emitted at the refusal URL"
    | ObligationClaim.AltAlwaysEmitted ->
        "the alternative-text attribute is emitted always, the empty string included - absent and empty are different claims"
    | ObligationClaim.AnchorAffordanceOnExpandable ->
        "a declared expansion emits a real working anchor to the full-size asset, honoured with no script at all"
    | ObligationClaim.RefusedSrcNoAffordance ->
        "a source the egress floor refused emits no affordance - an expansion that cannot be honoured is worse than none"
    | ObligationClaim.FigureCaptionOutsideLink ->
        "the caption sits outside the expansion anchor, so the caption text is not part of the link target"
    | ObligationClaim.SrcSetAscendingByWidth ->
        "responsive candidates are emitted in ascending width order, and a refused candidate is dropped rather than emitted"
    | ObligationClaim.UnregisteredCustomLabelled ->
        "an unregistered custom node with an available contract card renders a labelled placeholder derived from that card - identity, summary and a machine-readable verdict marker - never a blank and never a guess"
    | ObligationClaim.AuthoredChildOrder ->
        "repeated children are emitted in the authored order the wire carries, never re-sorted"
    | ObligationClaim.SingleDefaultPerKind ->
        "at most one repeated child per declared kind carries the default marker; a later duplicate election is emitted without it"
    | ObligationClaim.TranscriptDisclosureNamed ->
        "a declared transcript renders as a disclosure beside the transport, carrying its own accessible name"
    | ObligationClaim.SandboxAlwaysExactlyDeclared ->
        "the sandbox declaration is emitted on every instance, empty when nothing is granted, and carries exactly the declared relaxations - de-duplicated, in declaration order, and never a token the document did not name"
    | ObligationClaim.RefusedEmbedSourceOmitted ->
        "a primary source the egress floor refuses omits the source attribute entirely rather than pointing the browsing context at the refusal URL, while still recording the refusal"
    | ObligationClaim.PickerAlwaysPresent ->
        "a declared ingress gesture is additional: the file picker and its label are emitted whatever the document declares, so the keyboard-accessible route survives and a no-script host renders a working upload"

/// The closed vocabulary, in declaration order.
///
/// `claimId` and `claimMeaning` are exhaustive matches, so a new case cannot
/// compile without being named there; this list is the ENUMERATION, and a
/// reflection test (`RenderObligationVocabularyTests`) asserts it is complete —
/// the one guard a Fable-safe module cannot state about itself.
let allClaims: ObligationClaim list =
    [ ObligationClaim.AccessibleNameAlways
      ObligationClaim.AutoplayMutedPairing
      ObligationClaim.NoAutoplayPathway
      ObligationClaim.RefusedSourceDropped
      ObligationClaim.AltAlwaysEmitted
      ObligationClaim.AnchorAffordanceOnExpandable
      ObligationClaim.RefusedSrcNoAffordance
      ObligationClaim.FigureCaptionOutsideLink
      ObligationClaim.SrcSetAscendingByWidth
      ObligationClaim.UnregisteredCustomLabelled
      ObligationClaim.AuthoredChildOrder
      ObligationClaim.SingleDefaultPerKind
      ObligationClaim.TranscriptDisclosureNamed
      ObligationClaim.SandboxAlwaysExactlyDeclared
      ObligationClaim.RefusedEmbedSourceOmitted
      ObligationClaim.PickerAlwaysPresent ]

/// One obligation as a row declares it: which claim, the normative sentence for
/// THIS kind, and the spec section that states it.
///
/// `Statement` is per-kind because the same claim reads differently on different
/// kinds (an accessible name on a transport is mandatory-on-the-wire; on a
/// decorative image it is the empty string). `Section` is what makes the claim
/// falsifiable against the specification rather than against a host's habit.
type Obligation =
    {
        Claim: ObligationClaim
        /// The normative sentence, as the cited section states it for this kind.
        Statement: string
        /// The spec section that states it (`WIRE_FORMAT.md 3.6.6`).
        Section: string
    }

/// One row: a canonical wire kind and its declared fidelity posture.
type FidelityRow =
    {
        /// The wire discriminator (`kind.$type`), NOT `Kind.name` — see
        /// `wireNameOf` for the one place the two diverge.
        Kind: string
        /// True when the kind carries an EXPLICIT, phase-pinned render-fidelity
        /// contract. False means trivially single-tier: the deterministic
        /// render is the only render, and nothing had to be negotiated.
        Sensitive: bool
        /// What the wire carries — the deterministic, parity-clean data.
        Source: string
        /// What the parity-checked render pins. This is the tier the SSR-parity
        /// corpus and the cross-host byte-diff compare.
        Fallback: string
        /// The declared third tier, if any.
        Rich: RichTier
        /// Corpus fixture ids (`nodes/<id>.json`) that pin the floor for this
        /// kind. Declared for the fidelity-sensitive rows only; a test asserts
        /// every named fixture is in the corpus manifest, so a rename cannot
        /// leave a dangling pin.
        Fixtures: string list
        /// The checkable render obligations this kind carries (Phase 1105).
        /// Empty means the row states no claim a host suite is expected to
        /// assert — NOT that the kind's fallback prose is optional.
        Obligations: Obligation list
        /// Where the contract is written down: the phase that pinned it plus
        /// the normative doc section.
        Contract: string
    }

let private row kind sensitive source fallback rich fixtures contract =
    { Kind = kind
      Sensitive = sensitive
      Source = source
      Fallback = fallback
      Rich = rich
      Fixtures = fixtures
      Obligations = []
      Contract = contract }

/// Attach the checkable obligations to a row. A combinator rather than a tenth
/// parameter on `row`: every row would otherwise carry an empty list at its call
/// site, which reads as "considered and found none" on forty rows where only two
/// were considered at all.
let private obliged (obligations: Obligation list) (r: FidelityRow) = { r with Obligations = obligations }

/// One obligation, spelled at the call site.
let private owes claim section statement =
    { Claim = claim
      Statement = statement
      Section = section }

/// A trivially single-tier row: the deterministic render is the whole render.
let private plain kind source fallback =
    row kind false source fallback RichTier.None [] "WIRE_FORMAT.md 3.2; docs/SSR.md (per-kind server behaviour)"

// ─── The table ───────────────────────────────────────────────────────────────
//
// One row per canonical wire kind. Ordered by wire name (Ordinal), so a new
// kind lands as one clean insert and a reorder produces no diff — the same
// diffability posture the IDL artefact takes.

/// The full render-fidelity declaration over the canonical `NodeKind` surface.
let all: FidelityRow list =
    [ plain "Badge" "the label TextSource + tone" "a `fuaran-badge` span carrying the resolved label and tone class"

      // Phase 1082 — the fallback text names the fill direction because
      // `Masonry` made it a RENDER obligation rather than a styling choice:
      // WIRE_FORMAT §3.6.7 fixes the realising CSS family and the
      // break-avoidance rule, so "the full structural container" is no longer
      // the whole of what a conformant host owes for this kind.
      plain
          "Box"
          "the child list + layout spec"
          "the full structural container, classes identical on both sides; the layout mode's declared fill direction realised per WIRE_FORMAT §3.6.7 (row-fill for `Grid`, column-fill for `Masonry`)"

      row
          "Button"
          false
          "the label, variant, disabled binding and the declared action"
          "the button element with its classes, resolved label and `disabled` state, carrying no event handlers"
          (RichTier.Behavioural(
              "the declared action is wired on hydration",
              "the control is inert server-side and gains behaviour, not structure, at hydration (Phase 143)"
          ))
          []
          "docs/SSR.md (Input - rendered inert)"

      plain
          "Callout"
          "the callout TextSource + tone"
          "the full structural callout element, tone class and ARIA identical on both sides"

      row
          "Chart"
          true
          "the chart kind, the row-source binding, and the axis / legend / value-format declarations"
          "for a lowered kind over resolved rows, the SAME first-party lowered `Drawing` SVG the client emits (byte-parity via one shared lowering); otherwise a deterministic `fuaran-chart-ssr-placeholder` carrying `data-fuaran-row-count` and the title - never a blank"
          (RichTier.ClientOnly(
              "the client chart library draws into the placeholder",
              "hydration, for the not-yet-lowered kinds only; a lowered kind has no client-only tier"
          ))
          [ "chart-1" ]
          "Phase 526; docs/SSR.md (Visualisation)"

      row
          "CodeBlock"
          true
          "the code text, a `language` tag, optional `lineNumbers` / `highlightLines`, and `copyable` - deterministic data on the wire"
          "a deterministic HTML-escaped `<pre><code class=\"language-{x}\">` with `data-language`, the optional `data-highlight-lines`, and a structurally-present copy button; no markdown library on either side"
          (RichTier.ClientOnly(
              "syntax highlighting",
              "a post-hydration pass targeting `.language-{x}`; a host integration seam, never emitted by a renderer"
          ))
          [ "code-1" ]
          "Phase 290; WIRE_FORMAT.md 3.2; docs/SSR.md (deterministic-render + client-only-enhancement contract)"

      // Phase 1108 — the fallback prose now names the CARDED degradation,
      // because "the same labelled placeholder on both sides" stopped being the
      // whole of what a host owes for an unregistered node the moment a
      // description of it became obtainable. The obligation below is the
      // checkable remainder of that sentence.
      row
          "Custom"
          true
          "the component name, a bounded prop map, and the optional content-identity envelope"
          "whatever the host's SERVER custom-renderer registry emits for the component; an unregistered node renders a labelled placeholder, identical on both sides — derived from the contract card (WIRE_FORMAT §25) where one is available, and identity-only where none is"
          (RichTier.ClientOnly(
              "the host's registered client renderer",
              "the `IFuaranRuntime.RegisterCustomRenderer` trust boundary (Phase 141) - the sanctioned escape a host uses for a library render such as a diagram"
          ))
          [ "custom-1" ]
          "Phase 141; Phase 1108; docs/SSR.md (custom-renderer registry); WIRE_FORMAT.md 25; SANITIZATION.md"
      |> obliged
          [ owes
                ObligationClaim.UnregisteredCustomLabelled
                "WIRE_FORMAT.md 25.4"
                "where no renderer is registered for a Custom node and a contract card for its identity is available, the emitted placeholder carries the component identity, the card's summary where the card declares one, and a machine-readable verdict marker; it emits no prop value and no guess at the component's appearance, and where the card's content hash contradicts the node's it withholds the description rather than showing one that describes a different shape" ]

      row
          "DataGrid"
          true
          "the column declarations, the row-source binding, and the declared sort / page / edit intent"
          "in static read-only mode the full semantic `<table class=\"fuaran-table\">` (byte-identical to the retired `Table`, with the declared sort surfaced as `data-fuaran-sort-*`); for a client-library grid a deterministic `fuaran-grid-ssr-placeholder` carrying `data-fuaran-row-count` - never a blank"
          (RichTier.ClientOnly(
              "the client grid library draws into the placeholder",
              "hydration, for the client-library form only; a `staticRows` grid has no client-only tier"
          ))
          [ "grid-1" ]
          "Phase 393; docs/SSR.md (Visualisation, sortable rendered tables)"

      row
          "Disclosure"
          false
          "the summary, the child tree, and the open binding"
          "a native `<details>`/`<summary>` whose `open` attribute reflects the resolved binding - the disclosure opens and closes with no JavaScript at all"
          RichTier.None
          []
          "docs/SSR.md (Layout.Disclosure)"

      plain
          "Drawing"
          "the declared shape / label geometry"
          "a deterministic SVG built by the shared `DrawingSvg` builder, byte-identical on both sides"

      plain
          "ErrorBoundary"
          "the protected child subtree and the fallback subtree"
          "the protected child subtree rendered directly; the fallback is a client-runtime degradation path (the server has no throws to catch)"

      plain "Fact" "the label / value TextSources" "the resolved, formatted fact row"

      (row
          "FileUpload"
          false
          "the accept / multiple declarations, the label, and the two Phase 1115 ingress declarations (`dropTarget` / `acceptPaste`)"
          "the file control with its classes and `disabled` state, carrying no event handlers. The ingress declarations degrade to the PLAIN PICKER: a drop needs a `drop` listener and a paste needs a `paste` one, and no CSS observes a drag, so a static host emits the control it always emitted and marks each declared gesture with a `data-fuaran-upload-drop` / `data-fuaran-upload-paste` attribute recording that the declaration was read - the marker is NOT coverage and nothing in this tier acts on it"
          (RichTier.Behavioural(
              "selection handling is wired on hydration, and a client tier honouring `dropTarget` / `acceptPaste` writes dropped or pasted files into the control's OWN input so the same selection path and the same `Accept` filter run whatever route the file arrived by",
              "the control is inert server-side (Phase 143); the `onSelect` payload is non-scalar and needs host wiring"
          ))
          [ "upload-1"; "upload-drop-1"; "upload-paste-1" ]
          "Phase 1115; WIRE_FORMAT.md 3.6.10; docs/SSR.md (Input - rendered inert)"
       |> obliged
           [ owes
                 ObligationClaim.PickerAlwaysPresent
                 "WIRE_FORMAT.md 3.6.10"
                 "the `<input type=\"file\">` and its label are emitted whatever gestures the document declares - a drop zone is an ADDITIONAL route and never a replacement, which is what keeps the keyboard-accessible route intact and what makes the no-script floor a working upload rather than an inert box" ])

      row
          "Filters"
          false
          "the filter item declarations and their bound stores"
          "the filter controls with their classes and resolved values, carrying no event handlers"
          (RichTier.Behavioural(
              "filter write-back is wired on hydration",
              "the control is inert server-side (Phase 143); the declarative floor is write-back to the filter's own store"
          ))
          []
          "docs/SSR.md (Input - rendered inert)"

      row
          "Form"
          false
          "the field declarations, their bound values and the submit action"
          "the form and its fields with their classes, resolved values and `disabled` state, carrying no event handlers"
          (RichTier.Behavioural(
              "field write-back and submit are wired on hydration",
              "the controls are inert server-side (Phase 143)"
          ))
          []
          "docs/SSR.md (Input - rendered inert)"

      plain
          "FragmentDecl"
          "the fragment template and its hole declarations"
          "zero paint - the declaration is a template, not a rendered node"

      plain
          "FragmentRef"
          "the fragment name and its argument bindings"
          "the fragment expanded against the one-shot registry collected from the tree; an unresolved reference renders a labelled placeholder identically on both sides"

      plain "Heading" "the heading TextSource + level" "the full structural heading element"

      plain "Icon" "the icon name + size" "the resolved icon markup through the uniform icon hook"

      // Phase 1079 promotes `Image` out of `plain`. It now carries an explicit,
      // phase-pinned three-tier contract of exactly the Phase 290 shape: a
      // deterministic parity-checked floor (the anchor), and a declared
      // client-only tier (the overlay) that no renderer emits and no parity
      // comparison sees. The row was `plain` while the kind had only one tier;
      // saying so now is the difference between a table that describes the
      // renderers and one that merely lists the kinds.
      (row
          "Image"
          true
          "the src, alt, variant, the fit / aspectRatio / loading presentation tokens, the optional caption, the srcSet candidate list, and the `expandable` declaration"
          "a real `<img>` with the sanitised `src` (unknown or dangerous schemes collapse to `about:blank`), `alt` always emitted, the per-variant class, and the per-token fit / aspect classes plus `loading=\"lazy\"` under `loading = Lazy`. The aspect box is reserved by CSS alone, so the space is held in the server-rendered output with no script. A `caption` wraps the emission in `<figure class=\"fuaran-image-figure\">` with the resolved text in a `<figcaption>`; with no caption there is no wrapper at all. A non-empty `srcSet` emits `srcset` with `<url> <width>w` candidates ordered ASCENDING by width plus `sizes=\"100vw\"`; every candidate's url passes the same URL-scheme + egress floor as `src`, and one that fails it is dropped from the list rather than emitted, so the primary `src` remains the fallback. An empty `srcSet` emits neither attribute. Under `expandable` the `<img>` is wrapped in a real `<a class=\"fuaran-image-expand\" href=\"<the sanitised src>\" data-fuaran-expandable>` — a WORKING link to the full-size asset with no script, which is the whole no-JS story — nested INSIDE the `<figure>` so a caption is not part of the link target; a `src` the egress floor refused emits no anchor at all, because an affordance that cannot be honoured is worse than none"
          (RichTier.ClientOnly(
              "the in-page lightbox overlay",
              "a post-hydration pass targeting `[data-fuaran-expandable]` (the packaged `content/fuaran-image-expand.js`, or the `@fuaran-ui/renderer/enhance-expandable` module); it appends an overlay to `document.body` and suppresses the anchor's navigation only once that overlay is up, so a failure leaves the working link"
          ))
          [ "image-expandable-1"; "image-expandable-figure-1" ]
          "Phase 1079; WIRE_FORMAT.md 3.6.5; docs/SSR.md (expandable images)"
       |> obliged
           [ owes
                 ObligationClaim.AltAlwaysEmitted
                 "WIRE_FORMAT.md 3.6.2"
                 "`alt` is emitted on every image, the empty string included — a decorative image declares an empty alt rather than omitting the attribute"
             owes
                 ObligationClaim.AnchorAffordanceOnExpandable
                 "WIRE_FORMAT.md 3.6.5"
                 "under `expandable` the `<img>` is wrapped in a real `<a class=\"fuaran-image-expand\" href=\"<the sanitised src>\" data-fuaran-expandable>` — a WORKING link to the full-size asset with no script"
             owes
                 ObligationClaim.RefusedSrcNoAffordance
                 "WIRE_FORMAT.md 3.6.5"
                 "a `src` the URL-scheme + egress floor refused emits no anchor at all, because an affordance that cannot be honoured is worse than none"
             owes
                 ObligationClaim.FigureCaptionOutsideLink
                 "WIRE_FORMAT.md 3.6.3, 3.6.5"
                 "with a caption the anchor nests INSIDE the `<figure>` — `<figure>` wraps `<a>` wraps `<img>`, and the `<figcaption>` follows the anchor — so the caption is not part of the link target"
             owes
                 ObligationClaim.SrcSetAscendingByWidth
                 "WIRE_FORMAT.md 3.6.4"
                 "a non-empty `srcSet` emits `<url> <width>w` candidates ordered ASCENDING by width; a candidate the egress floor refuses is dropped rather than emitted, so the primary `src` remains the fallback" ])

      plain "LabelValueRow" "the label / value TextSources" "the resolved, formatted row"

      row
          "Link"
          false
          "the href, label, and the optional `protection` declaration"
          "a real crawlable `<a href>` with a sanitised href - the no-JS navigation path. Under `protection = email` the SSR side emits every character of the href and label as decimal HTML entities while the client sets the decoded href, so the two agree POST-ENTITY-DECODE rather than byte-for-byte; that narrowing is the declared contract, not a divergence"
          RichTier.None
          [ "link-protected-1" ]
          "Phase 812; WIRE_FORMAT.md 3.2 (link protection); docs/SSR.md (protected email links)"

      plain "List" "the item TextSources + ordered flag" "an `<ol>`/`<ul>` of `<li>`, classes identical on both sides"

      row
          "Map"
          true
          "the marker source binding and the map spec"
          "a deterministic `fuaran-map-ssr-placeholder` carrying `data-fuaran-marker-count` - never a blank"
          (RichTier.ClientOnly("the client map library draws into the placeholder", "hydration"))
          [ "map-1" ]
          "docs/SSR.md (Visualisation)"

      row
          "Markdown"
          true
          "the raw markdown text as a `TextSource`. Markdown is NEVER parsed into the wire format, so a markdown feature change is not a wire change (WIRE_FORMAT.md 14)"
          "one deterministic GFM-to-HTML render, shared by the F# client and server renderers and re-implemented byte-identically by every other host; pinned by its own corpus at `markdown/corpus.json` and its own cross-host gate"
          (RichTier.ClientOnly(
              "KaTeX over inline `$...$` / `$$...$$` spans",
              "a post-hydration pass over `.fuaran-markdown`; the deterministic renderer leaves those spans as escaped literal text, which is the precondition that keeps the enhancement outside the byte-diff"
          ))
          [ "markdown-1" ]
          "Phases 292, 293; WIRE_FORMAT.md 14; docs/MARKDOWN.md"


      row
          "Math"
          true
          "the LaTeX `source` string plus `display` (inline | block) - deterministic, parity-clean data on the wire; the wire never carries rendered math"
          "native MathML for the closed LaTeX subset (real superscripts, subscripts and fractions with NO JavaScript), or the raw escaped source in a `fuaran-math-source` span for out-of-subset input, both inside a `fuaran-math-{block,inline}` container carrying `data-math-display` and `data-fuaran-math-src`. Byte-identical across every renderer; the subset and its byte-exact fixture table are normative in docs/MATH-DEGRADATION.md"
          (RichTier.ClientOnly(
              "KaTeX",
              "a post-hydration pass targeting the `.fuaran-math` container, reading the LaTeX from `data-fuaran-math-src` and replacing the floor wholesale; never emitted by a renderer"
          ))
          [ "math-1" ]
          "Phases 293, 658; WIRE_FORMAT.md 3.2; docs/SSR.md; docs/MATH-DEGRADATION.md"

      // Phase 1076 — `Media` is `sensitive` for the reason `Image` is: the
      // element fetches its `src` (and its `poster`) with no user act, so
      // RENDERING the tree IS the request. It carries NO rich tier, and that is
      // a claim rather than a blank: a `<video controls>` is already a complete
      // interactive control in every browser, so there is nothing a client-only
      // pass would add and no renderer attaches one.
      (row
          "Media"
          true
          "the src binding, the mandatory accessible label, the controls / loop declarations, and the MediaKind variant — Video, carrying autoplay and an optional poster binding, or Audio, carrying neither"
          "a real `<video class=\"fuaran-media fuaran-media-video\">` or `<audio class=\"fuaran-media fuaran-media-audio\">` a browser plays with no script. The sanitised `src` (unknown or dangerous schemes collapse to the refusal URL) and an `aria-label` carrying the resolved label are ALWAYS emitted — the label is mandatory on the wire and a transport has no decorative case. `controls` emits unless the document switches it off; `loop` only when declared. A `poster` passes the same URL-scheme + egress floor as `src` and a refused one is DROPPED rather than emitted, because a `<video>` with no poster shows its first frame while a poster at the refusal URL is a broken image over the player. `autoplay` is emitted ONLY together with `muted` — the pairing is what the declaration means, not a default, which is why the wire carries no separate muted slot to fall out of step with it. The Audio variant has NO autoplay pathway at all: the case declares no such slot, so there is nothing for a renderer to read"
          RichTier.None
          [ "media-video-1"
            "media-video-poster-1"
            "media-video-autoplay-1"
            "media-audio-1"
            "media-video-captions-1"
            "media-video-tracks-2"
            "media-audio-transcript-1" ]
          "Phase 1076; WIRE_FORMAT.md 3.6.6; docs/SSR.md (media)"
       |> obliged
           [ owes
                 ObligationClaim.AccessibleNameAlways
                 "WIRE_FORMAT.md 3.6.6"
                 "an `aria-label` carrying the resolved label is ALWAYS emitted — the label is mandatory on the wire and a transport has no decorative case"
             owes
                 ObligationClaim.AutoplayMutedPairing
                 "WIRE_FORMAT.md 3.6.6"
                 "`autoplay` is emitted ONLY together with `muted`, and `muted` rides `autoplay` — the pairing is what the declaration means, not a default, which is why the wire carries no separate muted slot to fall out of step with it"
             owes
                 ObligationClaim.NoAutoplayPathway
                 "WIRE_FORMAT.md 3.6.6"
                 "the Audio variant has NO autoplay pathway at all: the case declares no such slot, so an `<audio>` emission carries neither `autoplay` nor `muted`"
             owes
                 ObligationClaim.RefusedSourceDropped
                 "WIRE_FORMAT.md 3.6.6"
                 "a `poster` or a `track` source the URL-scheme + egress floor refuses is DROPPED rather than emitted, because a `<video>` with no poster shows its first frame while a poster at the refusal URL is a broken image over the player, and a `<track>` at the refusal URL is a caption menu entry that opens onto nothing"
             owes
                 ObligationClaim.AuthoredChildOrder
                 "WIRE_FORMAT.md 3.6.6"
                 "`<track>` children are emitted in the AUTHORED order the wire carries, never re-sorted - the opposite of `srcSet`, because a browser picks one candidate from a srcset by an algorithm while a reader picks a track from a menu built in document order"
             owes
                 ObligationClaim.SingleDefaultPerKind
                 "WIRE_FORMAT.md 3.6.6"
                 "at most one `<track>` of a given `kind` carries `default`: the FIRST election of a kind is honoured and a later one is emitted without the attribute, the track itself still emitted"
             owes
                 ObligationClaim.TranscriptDisclosureNamed
                 "WIRE_FORMAT.md 3.6.6"
                 "a declared `transcript` renders as a `<details>` disclosure BESIDE the transport, carrying the media's resolved label as its accessible name - never as a child of the media element, where a browser would treat it as fallback content and never show it" ])

      // Phase 1111 — `Embed` is `sensitive` for a SHARPER reason than `Media`
      // and `Image` are. Those fetch their source with no user act, so rendering
      // the tree IS the request; an embed fetches a DOCUMENT with no user act
      // and then runs it, so rendering the tree is the request AND the
      // execution. It carries NO rich tier, and that is a claim rather than a
      // blank: a sandboxed `<iframe>` is already a complete browsing context in
      // every browser, and an enhancement pass that reached into it would be
      // doing the thing the sandbox exists to prevent.
      (row
          "Embed"
          true
          "the src binding, the mandatory accessible title, an optional declared aspect ratio, and the closed list of sandbox relaxations - empty by default, which is total denial"
          "a real `<iframe class=\"fuaran-embed\">` a browser loads with no script. The `sandbox` attribute is ALWAYS emitted and is EMPTY when the document grants nothing, which is the maximally-restrictive value; `allow-scripts`, `allow-same-origin` and `allow-forms` appear only where declared, de-duplicated and in the vocabulary's declaration order so the markup is deterministic whatever order the document authored. Fullscreen is NOT a sandbox token - it is a permissions-policy directive riding an `allow` attribute emitted only when declared. A `title` carrying the resolved title is ALWAYS emitted: it is mandatory on the wire and a browsing context has no decorative case. `loading=\"lazy\"` and a conservative `referrerpolicy` are unconditional. The `src` passes the `embed` egress class - `https` only, no other scheme and no schemeless reference - and a refused one omits the attribute entirely rather than pointing the frame at the refusal URL, the refusal still recorded as a data attribute. A declared aspect ratio is a CLASS on the frame; no value from the tree ever reaches a style attribute"
          RichTier.None
          [ "embed-1"; "embed-aspect-1"; "embed-permissions-1" ]
          "Phase 1111; WIRE_FORMAT.md 3.6.8, 19.1; docs/SSR.md (embed)"
       |> obliged
           [ owes
                 ObligationClaim.AccessibleNameAlways
                 "WIRE_FORMAT.md 3.6.8"
                 "a `title` attribute carrying the resolved title is ALWAYS emitted - the title is mandatory on the wire and a browsing context has no decorative case"
             owes
                 ObligationClaim.SandboxAlwaysExactlyDeclared
                 "WIRE_FORMAT.md 3.6.8"
                 "`sandbox` is emitted on every embed and is EMPTY when no permission is declared; the tokens it carries are exactly the declared relaxations, de-duplicated and in declaration order, and `AllowFullscreen` rides `allow` rather than `sandbox`"
             owes
                 ObligationClaim.RefusedEmbedSourceOmitted
                 "WIRE_FORMAT.md 19.1"
                 "a `src` the `embed` egress class refuses omits the attribute entirely - an `<iframe>` at the refusal URL renders that page, where one with no `src` is an empty frame that fetches nothing - and the refusal is recorded as the egress-refusal data attribute" ])

      plain "Metric" "the label + value source + format" "the resolved, formatted metric tile"

      row
          "Modal"
          true
          "the child tree, the `open` `Binding<bool>`, and the heading / dismissable declarations"
          "rendered INLINE at its tree position - no portal, ever. Position, centring, stacking and backdrop are CSS-owned (`position: fixed`, `z-index: var(--fuaran-z-modal)`), so both sides emit the same tree shape. A closed modal stays in the DOM behind the native `[hidden]` attribute rather than being omitted. ARIA is structural and literal: `role=\"dialog\"` + `aria-modal=\"true\"`. Dismiss and heading affordances are structurally present and inert server-side"
          (RichTier.Behavioural(
              "focus trap, restore-focus and Esc-to-dismiss",
              "attached on hydration; behaviour, not structure, so it cannot cause a hydration mismatch"
          ))
          [ "modal-1" ]
          "Phase 289; WIRE_FORMAT.md 3.2; docs/SSR.md (overlay + overflow render-fidelity contract)"

      plain
          "Mount"
          "the guest tree reference and the isolation boundary declaration"
          "the guest subtree rendered within its isolation boundary; the host-bubble channel is host composition, not a rendered affordance"

      plain "Progress" "the value source + max" "the resolved progress element"

      row
          "ScrollArea"
          true
          "the child tree, the scroll orientation, and the optional pixel bounds"
          "a `fuaran-scrollarea-{vertical,horizontal,both}` overflow container whose clip and scrollbar are entirely CSS-owned, with a structural `role=\"region\"` and a LOWERCASE `tabindex=\"0\"` scroll target (lowercase deliberately: the camelCase form diverges from what React normalises the client's `tabIndex` to). Pixel bounds render as an identical inline `max-height`/`max-width` on both sides"
          RichTier.None
          [ "scroll-1" ]
          "Phase 289; WIRE_FORMAT.md 3.2; docs/SSR.md (overlay + overflow render-fidelity contract)"

      row
          "Select"
          false
          "the options, the bound value and the multiple flag"
          "the `<select>` (with the `multiple` attribute and no scalar `value` in multi mode) carrying its classes and resolved selection, inert server-side"
          (RichTier.Behavioural(
              "selection write-back is wired on hydration",
              "the control is inert server-side (Phase 143); the declarative floor is write-back to the control's own writable binding"
          ))
          []
          "Phase 291; docs/SSR.md (Input - rendered inert)"

      plain "Skeleton" "the skeleton shape declaration" "the full structural placeholder element"

      row
          "Sparkline"
          true
          "the series values"
          "the `fuaran-sparkline` hook element plus an em-dash placeholder - a readable, deterministic stand-in rather than a blank"
          (RichTier.ClientOnly("the polyline is drawn", "hydration"))
          [ "spark-1" ]
          "docs/SSR.md (Display.Sparkline)"

      plain
          "SplitPanel"
          "the two child subtrees + the split declaration"
          "the full structural container, classes identical on both sides"

      row
          "Stepper"
          false
          "the step declarations and the active-step binding"
          "the full structural step list with the active step marked, ARIA-complete"
          (RichTier.Behavioural("step selection is wired on hydration", "the control is inert server-side (Phase 143)"))
          []
          "docs/SSR.md (Layout)"

      plain "SummaryList" "the summary rows" "the full structural list, classes identical on both sides"

      plain
          "Switch"
          "the selector binding and the per-case child subtrees"
          "the case matching the resolved initial value, else the default. The client's first render reads the same seeded value, so both sides emit the same tree shape"

      row
          "Tabs"
          false
          "the tab declarations, their panels, and the active-tab binding"
          "a static `role=\"tablist\"` plus the active panel, ARIA-complete"
          (RichTier.Behavioural(
              "keyboard navigation and tab switching are wired on hydration",
              "the control is inert server-side (Phase 143); switching a tab after hydration is interaction, not a fidelity tier"
          ))
          []
          "docs/SSR.md (Layout.Tabs)"

      row
          "Toast"
          true
          "the content, the `open` `Binding<bool>`, and the tone"
          "rendered INLINE at its tree position with no portal, always emitted, `role=\"status\"` + `aria-live=\"polite\"`, and `[hidden]` when closed rather than absent. This is the DECLARATIVE, hydration-stable notification surface; the imperative `Action.Notify` path renders no node at all and is a host-chrome trigger, not a tier of this kind"
          RichTier.None
          [ "toast-1" ]
          "Phase 289; WIRE_FORMAT.md 3.2 (Toast vs Action.Notify); docs/SSR.md" ]

/// The canonical wire-kind enumeration this table declares a posture for — the
/// `kind.$type` vocabulary of WIRE_FORMAT.md 3.2, Ordinal-sorted.
///
/// This is the completeness seam, and it is deliberately a pinned list rather
/// than a derivation: `NodeKind` cannot be enumerated without reflection, which
/// Fable does not support (the same constraint that makes `SchemaGen` and
/// `JsonDecode` hand-written mirrors). It does not drift silently, because the
/// completeness test measures it against the GENERATED `manifest.json` `kinds`
/// array — which is itself derived from the encoded corpus fixtures. So a new
/// `NodeKind` that follows the WIRE_FORMAT 11 forward-coupling rule (encoder,
/// decoder, corpus, schema in one change) lands in the manifest, and the test
/// then fails until this list and a row above are added.
let wireKindNames: string list =
    [ "Badge"
      "Box"
      "Button"
      "Callout"
      "Chart"
      "CodeBlock"
      "Custom"
      "DataGrid"
      "Disclosure"
      "Drawing"
      "Embed"
      "ErrorBoundary"
      "Fact"
      "FileUpload"
      "Filters"
      "Form"
      "FragmentDecl"
      "FragmentRef"
      "Heading"
      "Icon"
      "Image"
      "LabelValueRow"
      "Link"
      "List"
      "Map"
      "Markdown"
      "Math"
      "Media"
      "Metric"
      "Modal"
      "Mount"
      "Progress"
      "ScrollArea"
      "Select"
      "Skeleton"
      "Sparkline"
      "SplitPanel"
      "Stepper"
      "SummaryList"
      "Switch"
      "Tabs"
      "Toast" ]

/// The wire discriminator of a node's kind — the token this table is keyed by.
///
/// `Kind.name` is the KIND-CONSTRAINT vocabulary, which coincides with the wire
/// token for every kind but one: `NodeKind.DataGrid` tags as `"Grid"` there and
/// `"DataGrid"` on the wire. This is the same adaptation
/// `Fuaran.UI.Renderer.Relay.wireKindName` performs at the relay boundary, for
/// the same reason (moving `Kind.name` would break every published
/// `kindConstraint`), and `RelayTests.fs` pins the mapping against the canonical
/// encoder so a SECOND divergence fails the build rather than silently
/// mis-keying this table.
let wireNameOf (kind: NodeKind<'Msg>) : string =
    match kind with
    | NodeKind.DataGrid _ -> "DataGrid"
    | other -> Kind.name other

/// The declared posture of a wire kind, or `None` for a kind with no row (which
/// the completeness test makes impossible for a canonical kind, and which is the
/// honest answer for an unknown kind preserved by the 15.3 tolerance path).
let tryFind (wireKind: string) : FidelityRow option =
    all |> List.tryFind (fun r -> r.Kind = wireKind)

/// The posture of a node's own kind.
let ofNode (node: Node<'Msg>) : FidelityRow option = tryFind (wireNameOf node.Kind)

// ─── Obligation coverage (Phase 1105) ────────────────────────────────────────
//
// The reporting shape every adopting host uses, declared once here so the hosts
// answer the same question in the same words rather than each inventing a way to
// say "we did not check that".

/// Every declared obligation, paired with the kind that owes it, in table order.
let allObligations: (string * Obligation) list =
    [ for r in all do
          for o in r.Obligations -> r.Kind, o ]

/// A host's answer for one declared obligation.
///
/// `Unchecked` is the case the whole mechanism exists for. A host that renders a
/// kind and has no checker for one of its claims must say so, WITH a reason —
/// "not checked" is not "passed", and an obligation that quietly falls out of a
/// host's suite is exactly the silent failure the closed vocabulary replaces.
[<RequireQualifiedAccess>]
type ObligationOutcome =
    /// The host renders this kind and its suite asserts this claim in emitted
    /// output.
    | Asserted
    /// The host renders this kind but has no checker for this claim yet.
    | Unchecked of reason: string
    /// The host does not render this kind at all, so the claim does not arise.
    /// Distinct from `Unchecked`: nothing is owed, rather than owed and unpaid.
    | NotRendered of reason: string

/// One line of a host's obligation report.
type ObligationReport =
    { Kind: string
      ClaimId: string
      Statement: string
      Section: string
      Outcome: ObligationOutcome }

/// Project the declaration through a host's own answer, producing one report
/// line per declared obligation. A host supplies `statusOf`; the enumeration is
/// the declaration's, never the host's, so a NEW obligation appears in the
/// report the moment it is declared rather than when someone remembers it.
let reportWith (statusOf: string -> ObligationClaim -> ObligationOutcome) : ObligationReport list =
    allObligations
    |> List.map (fun (kind, o) ->
        { Kind = kind
          ClaimId = claimId o.Claim
          Statement = o.Statement
          Section = o.Section
          Outcome = statusOf kind o.Claim })

/// The report lines a host must SURFACE: everything it did not assert. Empty is
/// the only silent result — anything else is printed, so an unchecked obligation
/// is visible in the run rather than inferable from its absence.
let unasserted (report: ObligationReport list) : ObligationReport list =
    report
    |> List.filter (fun line ->
        match line.Outcome with
        | ObligationOutcome.Asserted -> false
        | _ -> true)

/// The one-line rendering of a report line, so the same sentence appears in
/// every host's output.
let describeReport (line: ObligationReport) : string =
    let outcome =
        match line.Outcome with
        | ObligationOutcome.Asserted -> "asserted"
        | ObligationOutcome.Unchecked reason -> "UNCHECKED (" + reason + ")"
        | ObligationOutcome.NotRendered reason -> "not rendered (" + reason + ")"

    line.Kind + "/" + line.ClaimId + " [" + line.Section + "]: " + outcome

// ─── Badge derivation ────────────────────────────────────────────────────────
//
// The consumer-facing projection: three segments per node, derived from the row
// rather than hand-annotated. A surface that shows per-node fidelity badges
// (the degradation-ladder exhibit is the motivating one) reads these; it must
// hard-code nothing, because a hard-coded badge is precisely the second source
// of truth this manifest exists to remove.

/// One badge segment: which tier, whether the kind HAS that tier, and the
/// detail a hover surface shows.
type BadgeSegment =
    { Tier: string
      Present: bool
      Detail: string }

/// The three-segment fidelity badge for a row: source / fallback / rich.
///
/// `Present = false` on the rich segment means the kind declares no client-only
/// tier at all, which is a positive statement ("the fallback IS the render"),
/// not missing information. A `Behavioural` rich tier is present but marked as
/// such in its detail: it adds behaviour, never DOM.
let badge (r: FidelityRow) : BadgeSegment list =
    [ { Tier = "source"
        Present = true
        Detail = r.Source }
      { Tier = "fallback"
        Present = true
        Detail = r.Fallback }
      { Tier = "rich"
        Present =
          match r.Rich with
          | RichTier.None -> false
          | _ -> true
        Detail =
          match r.Rich with
          | RichTier.None -> "no client-only tier - the parity-checked fallback is the whole render"
          | RichTier.Behavioural(enhancement, seam) ->
              "behaviour only, no DOM change: " + enhancement + " (" + seam + ")"
          | RichTier.ClientOnly(technique, seam) ->
              "client-only, outside every parity comparison: " + technique + " (" + seam + ")" } ]
