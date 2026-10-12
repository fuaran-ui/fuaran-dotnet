module Fuaran.UI.Renderer.Css

// ============================================================================
//  Fuaran — the per-node CLASS-STRING vocabulary (Phase 207).
//
//  Every builder below is one `inline` string concatenation producing a class
//  attribute the renderers emit per node per frame. Two reasons they live here
//  rather than at the call sites:
//
//   1. PARITY. The class-name vocabulary is locked across the F# client
//      renderer, the F# SSR renderer, and the other reference-implementation
//      renderers (the SSR class/aria parity corpus pins it). Two renderers that
//      each spell `"fuaran-metric fuaran-metric-" + tone` inline can drift by a
//      space; one shared builder cannot. The vocabulary is now readable in one
//      place — this file IS the class contract.
//
//   2. PERF. These run on the hottest path the renderers have. `sprintf` parses
//      its format string at RUNTIME on every call — under Fable that is a JS
//      format-parse per node per frame, paid in the browser — where `+` lowers
//      to a plain concatenation on both pipelines. The builders are `inline`, so
//      a call site pays no function-call overhead either.
//
//  **This is a perf primitive: do NOT "simplify" any body back to `sprintf` or
//  to a fragment list + `String.concat`.** The output is byte-identical, so no
//  behavioural test can see the regression — the named builder is the guard, and
//  `Fuaran.UI.Tests/HotPathVocabularyTests.fs` asserts the call sites keep using
//  it.
//
//  Every builder takes ALREADY-RESOLVED fragments (`Theme.toneVar spec.Tone`,
//  a variant class, a pre-computed suffix) rather than the typed spec, so both
//  renderers can share it whatever they resolved from. The one exception is the
//  VARIANT MAPS (Phase 2070): a class chosen from a closed enumeration reads
//  that enum here, and only the enum — never a spec — so each map has one
//  definition rather than one per renderer.
//
//  Phase 2070 also brought every fixed class string both renderers emit here,
//  as `Css.Class.*` literals, with the builders and variant maps beside them.
//  This file is therefore the whole of the class vocabulary the two renderers
//  share; a class only one renderer emits stays at its use site.
// ============================================================================

open Fuaran.UI.Types

// ─── Layout ────────────────────────────────────────────────────────────────

/// `fuaran-layout-stack <direction><wrapSuffix>` — `wrapSuffix` is `""` or
/// `" fuaran-stack-wrap"`, already spaced by the caller.
let inline layoutStack (direction: string) (wrapSuffix: string) : string =
    "fuaran-layout-stack " + direction + wrapSuffix

/// `fuaran-layout-tabs <orientationClass>`.
let inline layoutTabs (orientationClass: string) : string =
    "fuaran-layout-tabs " + orientationClass

// ─── Display ───────────────────────────────────────────────────────────────

/// `fuaran-heading<variantSuffix>` — `variantSuffix` is `""` or a leading-space
/// modifier (` fuaran-heading-eyebrow` …).
let inline heading (variantSuffix: string) : string = "fuaran-heading" + variantSuffix

/// `fuaran-icon fuaran-icon--<sizeClass> fuaran-icon-<tone>` — the standalone
/// `Icon` display kind's class, emitted once per icon node.
let inline icon (sizeClass: string) (tone: string) : string =
    "fuaran-icon fuaran-icon--" + sizeClass + " fuaran-icon-" + tone

/// `fuaran-badge fuaran-badge-<variantClass>`.
let inline badge (variantClass: string) : string =
    "fuaran-badge fuaran-badge-" + variantClass

/// `fuaran-toast fuaran-toast-<toneClass>`.
let inline toast (toneClass: string) : string =
    "fuaran-toast fuaran-toast-" + toneClass

/// `fuaran-codeblock-code language-<language>`.
let inline codeBlockCode (language: string) : string =
    "fuaran-codeblock-code language-" + language

/// `fuaran-metric fuaran-metric-<tone>`.
let inline metric (tone: string) : string = "fuaran-metric fuaran-metric-" + tone

/// `fuaran-fact fuaran-fact-<tone><emphasisSuffix>` — `emphasisSuffix` is `""`
/// or `" fuaran-fact-emphasis"`.
let inline fact (tone: string) (emphasisSuffix: string) : string =
    "fuaran-fact fuaran-fact-" + tone + emphasisSuffix

/// `fuaran-callout fuaran-callout-<tone>`.
let inline callout (tone: string) : string = "fuaran-callout fuaran-callout-" + tone

/// `fuaran-progress fuaran-progress-<tone><indeterminateSuffix>` —
/// `indeterminateSuffix` is `""` or `" fuaran-progress-indeterminate"`.
let inline progress (tone: string) (indeterminateSuffix: string) : string =
    "fuaran-progress fuaran-progress-" + tone + indeterminateSuffix

/// `fuaran-label-value-row<emphasisSuffix>`.
let inline labelValueRow (emphasisSuffix: string) : string =
    "fuaran-label-value-row" + emphasisSuffix

// ─── Input ─────────────────────────────────────────────────────────────────

/// `fuaran-button fuaran-button-<variantClass>`.
let inline button (variantClass: string) : string =
    "fuaran-button fuaran-button-" + variantClass

/// `fuaran-button fuaran-button-<variantClass> fuaran-button-unwired` — the
/// client-only unwired-action operator cue.
let inline buttonUnwired (variantClass: string) : string =
    "fuaran-button fuaran-button-" + variantClass + " fuaran-button-unwired"

/// `fuaran-filter fuaran-filter-<kindClass>`.
let inline filter (kindClass: string) : string =
    "fuaran-filter fuaran-filter-" + kindClass

/// The per-control class for a plain form field, keyed on the HTML control TYPE
/// the renderer is about to emit (Phase 1648).
///
/// It lives here because two renderers emit it and they had drifted: the client
/// split the vocabulary per control (`fuaran-form-input` / `-select` /
/// `-textarea` / `-checkbox` / `-toggle`, plus the `fuaran-form-date` modifier)
/// while the server emitted one coarse `fuaran-form-field-control` for all of
/// them. The Phase 431 coverage test aliased the STYLING consequence through
/// declared absences, so the sheet stayed correct and the EMISSION divergence
/// stood — which is a latent trap for any host whose own CSS selects on the
/// class the client gave it and gets the server's instead.
///
/// Keyed on the control type rather than on `FormFieldKind` deliberately: the
/// type is what the markup actually says, both renderers already compute it,
/// and it keeps this module free of a dependency on the field vocabulary.
///
/// `fuaran-form-field-control` is NOT retired by this. It remains the class for
/// the composite controls that are not a plain input — the combobox's text box,
/// the tokens box, the rating, the range ends — where BOTH renderers already
/// agreed on it. This function covers the plain ones, which are where they
/// disagreed.
let formControl (controlType: string) : string =
    match controlType with
    | "textarea" -> "fuaran-form-textarea"
    | "select" -> "fuaran-form-select"
    | "checkbox" -> "fuaran-form-checkbox"
    | "toggle" -> "fuaran-form-toggle"
    // The date family carries the per-kind modifier ALONGSIDE the input class,
    // exactly as the client emits it — `fuaran-form-date` is a declared absence
    // covered by `fuaran-form-input`, so emitting it alone would be an unstyled
    // control.
    | "date"
    | "time"
    | "datetime-local" -> "fuaran-form-input fuaran-form-date"
    | _ -> "fuaran-form-input"

// ─── Vis ───────────────────────────────────────────────────────────────────

/// `fuaran-grid-cell-pill fuaran-pill-<tone>` — emitted per CELL, so this is
/// the densest builder here: a 1000-row grid with a pill column calls it 1000
/// times per frame.
let inline gridCellPill (tone: string) : string =
    "fuaran-grid-cell-pill fuaran-pill-" + tone

// ─── Custom (host-extension placements) ────────────────────────────────────
//
//  `moduleId` / `componentId` reach these already sanitised by the caller
//  (`Theme.sanitiseClassFragment`'s discipline) — these builders concatenate,
//  they do not sanitise.

/// `fuaran-kind-custom fuaran-custom-<moduleId>-<componentId>` — the per-node
/// KIND class for a `Custom` node, reached from `Theme.kindClass` on every
/// render of every Custom node.
let inline kindCustom (moduleId: string) (componentId: string) : string =
    "fuaran-kind-custom fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-kind-custom-placeholder fuaran-custom-<moduleId>-<componentId>`.
let inline customPlaceholder (moduleId: string) (componentId: string) : string =
    "fuaran-kind-custom-placeholder fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-custom-hash-mismatch fuaran-custom-<moduleId>-<componentId>`.
let inline customHashMismatch (moduleId: string) (componentId: string) : string =
    "fuaran-custom-hash-mismatch fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-custom-wrapper fuaran-custom-<moduleId>-<componentId>`.
let inline customWrapper (moduleId: string) (componentId: string) : string =
    "fuaran-custom-wrapper fuaran-custom-" + moduleId + "-" + componentId

// ─── Suffixes and per-node wrappers (Phase 2070) ───────────────────────────
//
//  The optional modifiers both renderers append, decided from a flag the
//  caller already holds. Each returns `""` or a LEADING-SPACE modifier, the
//  shape the builders above take.

/// `" fuaran-stack-wrap"` when the stack wraps, else `""`.
let inline stackWrap (wrap: bool) : string =
    if wrap then " fuaran-stack-wrap" else ""

/// `" fuaran-fact-emphasis"` when the fact is emphasised, else `""`.
let inline factEmphasis (emphasis: bool) : string =
    if emphasis then " fuaran-fact-emphasis" else ""

/// `" fuaran-progress-indeterminate"` for an indeterminate bar, else `""`.
let inline progressIndeterminate (indeterminate: bool) : string =
    if indeterminate then
        " fuaran-progress-indeterminate"
    else
        ""

/// `" fuaran-label-value-row-emphasis"` when the row is emphasised, else `""`.
let inline labelValueRowEmphasis (emphasis: bool) : string =
    if emphasis then " fuaran-label-value-row-emphasis" else ""

/// The per-node wrapper class: the node's base class, then
/// `fuaran-motion-<token>` when a motion token is set (`motionVar`, already
/// resolved through `Theme.motionVar`), then `fuaran-has-tooltip` when the node
/// draws a tooltip. Emitted once per node per frame on both renderers.
let inline nodeWrapper (baseClassName: string) (motionVar: string option) (hasTooltip: bool) : string =
    let withMotion =
        match motionVar with
        | Some motion -> baseClassName + " fuaran-motion-" + motion
        | None -> baseClassName

    if hasTooltip then
        withMotion + " fuaran-has-tooltip"
    else
        withMotion

/// `fuaran-icon <kindClass>` — the uniform icon PLACEMENT element an
/// icon-bearing spec (tab header, fact, metric, callout, button) renders, where
/// `kindClass` is that kind's `fuaran-<kind>-icon`. Distinct from [[icon]],
/// which is the standalone `Icon` display kind.
let inline iconPlacement (kindClass: string) : string = "fuaran-icon " + kindClass

/// `fuaran-media <variantClass>`.
let inline media (variantClass: string) : string = "fuaran-media " + variantClass

/// `fuaran-metric-trend fuaran-metric-trend-<sentiment>`.
let inline metricTrend (sentiment: string) : string =
    "fuaran-metric-trend fuaran-metric-trend-" + sentiment

// ─── Variant maps (Phase 2070) ─────────────────────────────────────────────
//
//  The closed enumerations a class is chosen from. These are the one place this
//  module reads the tree vocabulary, and they read only its closed enums —
//  never a spec — so the class contract stays one file: two renderers that
//  each kept a copy of these maps were two places a new case could be spelled
//  two ways.

/// The badge variant's class fragment, for [[badge]].
let badgeVariant (variant: BadgeVariant) : string =
    match variant with
    | BadgeVariant.Neutral -> "neutral"
    | BadgeVariant.Brand -> "brand"
    | BadgeVariant.Success -> "success"
    | BadgeVariant.Warning -> "warning"
    | BadgeVariant.Critical -> "critical"
    | BadgeVariant.Info -> "info"

/// The button variant's class fragment, for [[button]] / [[buttonUnwired]].
let buttonVariant (variant: ButtonVariant) : string =
    match variant with
    | ButtonVariant.Primary -> "primary"
    | ButtonVariant.Secondary -> "secondary"
    | ButtonVariant.Tertiary -> "tertiary"
    | ButtonVariant.Destructive -> "destructive"

/// The heading variant's modifier, for [[heading]]: `""` for the standard
/// heading (so its class is byte-identical to a pre-variant tree), else a
/// leading-space `fuaran-heading-<variant>`.
let headingVariant (variant: HeadingVariant) : string =
    match variant with
    | HeadingVariant.Standard -> ""
    | HeadingVariant.Eyebrow -> " fuaran-heading-eyebrow"
    | HeadingVariant.Caption -> " fuaran-heading-caption"
    | HeadingVariant.Lead -> " fuaran-heading-lead"

/// A flex stack's direction class, for [[layoutStack]].
let stackDirection (direction: Orientation) : string =
    match direction with
    | Orientation.Vertical -> "fuaran-stack-vertical"
    | Orientation.Horizontal -> "fuaran-stack-horizontal"

/// A tab set's orientation class, for [[layoutTabs]].
let tabsOrientation (orientation: Orientation) : string =
    match orientation with
    | Orientation.Horizontal -> "fuaran-tabs-horizontal"
    | Orientation.Vertical -> "fuaran-tabs-vertical"

/// A scroll area's class: `fuaran-scrollarea fuaran-scrollarea-<axis>`.
let scrollArea (orientation: ScrollOrientation) : string =
    match orientation with
    | ScrollOrientation.Vertical -> "fuaran-scrollarea fuaran-scrollarea-vertical"
    | ScrollOrientation.Horizontal -> "fuaran-scrollarea fuaran-scrollarea-horizontal"
    | ScrollOrientation.Both -> "fuaran-scrollarea fuaran-scrollarea-both"

/// An image's variant class: `fuaran-image`, plus `fuaran-image-avatar` or
/// `fuaran-image-rounded`.
let imageVariant (variant: ImageVariant) : string =
    match variant with
    | ImageVariant.Default -> "fuaran-image"
    | ImageVariant.Avatar -> "fuaran-image fuaran-image-avatar"
    | ImageVariant.Rounded -> "fuaran-image fuaran-image-rounded"

/// Phase 1077 — an image's fit modifier. `Natural` emits NO class, so a
/// pre-1077 tree's class attribute is byte-identical to what it was.
let imageFit (fit: ImageFit) : string =
    match fit with
    | ImageFit.Natural -> ""
    | ImageFit.Cover -> " fuaran-image-fit-cover"
    | ImageFit.Contain -> " fuaran-image-fit-contain"

/// Phase 1077 — an image's aspect modifier. `Natural` emits NO class.
let imageAspect (aspect: ImageAspect) : string =
    match aspect with
    | ImageAspect.Natural -> ""
    | ImageAspect.Square -> " fuaran-image-aspect-square"
    | ImageAspect.FourThree -> " fuaran-image-aspect-four-three"
    | ImageAspect.ThreeTwo -> " fuaran-image-aspect-three-two"
    | ImageAspect.SixteenNine -> " fuaran-image-aspect-sixteen-nine"

/// An embed's aspect modifier, over the image's aspect vocabulary. `Natural`
/// emits NO class.
let embedAspect (aspect: ImageAspect) : string =
    match aspect with
    | ImageAspect.Natural -> ""
    | ImageAspect.Square -> " fuaran-embed-aspect-square"
    | ImageAspect.FourThree -> " fuaran-embed-aspect-four-three"
    | ImageAspect.ThreeTwo -> " fuaran-embed-aspect-three-two"
    | ImageAspect.SixteenNine -> " fuaran-embed-aspect-sixteen-nine"

/// A math container's class: `fuaran-math fuaran-math-block` for display
/// math, `fuaran-math fuaran-math-inline` otherwise.
let mathContainer (isBlock: bool) : string =
    if isBlock then
        "fuaran-math fuaran-math-block"
    else
        "fuaran-math fuaran-math-inline"

// ─── Class names (Phase 2070) ──────────────────────────────────────────────
//
//  Every fixed class string BOTH renderers emit, named once. A literal spelled
//  in each renderer is two places one class can drift by a character; a
//  `[<Literal>]` here costs nothing on either pipeline (it is inlined at the
//  use site) and makes this file the whole of the shared class contract. A
//  class only one renderer emits stays at its use site. A multi-token value
//  carries its base class as well, exactly as it is emitted.

module Class =

    [<Literal>]
    let ButtonIcon = "fuaran-button-icon"

    [<Literal>]
    let CalloutBody = "fuaran-callout-body"

    [<Literal>]
    let CalloutHeading = "fuaran-callout-heading"

    [<Literal>]
    let CalloutIcon = "fuaran-callout-icon"

    [<Literal>]
    let CardBody = "fuaran-card-body"

    [<Literal>]
    let CardHeading = "fuaran-card-heading"

    [<Literal>]
    let ChartPlaceholder = "fuaran-chart-placeholder"

    [<Literal>]
    let ChartTitle = "fuaran-chart-title"

    [<Literal>]
    let Codeblock = "fuaran-codeblock"

    [<Literal>]
    let CodeblockCopy = "fuaran-codeblock-copy"

    [<Literal>]
    let CodeblockNumbered = "fuaran-codeblock fuaran-codeblock-numbered"

    [<Literal>]
    let CodeblockPre = "fuaran-codeblock-pre"

    [<Literal>]
    let CustomLabel = "fuaran-custom-label"

    [<Literal>]
    let CustomProps = "fuaran-custom-props"

    [<Literal>]
    let DepthExceeded = "fuaran-depth-exceeded"

    [<Literal>]
    let DisclosureBody = "fuaran-disclosure-body"

    [<Literal>]
    let DisclosureSummary = "fuaran-disclosure-summary"

    [<Literal>]
    let Embed = "fuaran-embed"

    [<Literal>]
    let FactHelp = "fuaran-fact-help"

    [<Literal>]
    let FactIcon = "fuaran-fact-icon"

    [<Literal>]
    let FactLabel = "fuaran-fact-label"

    [<Literal>]
    let FactValue = "fuaran-fact-value"

    [<Literal>]
    let FieldRange = "fuaran-field-range"

    [<Literal>]
    let FieldRangeSep = "fuaran-field-range-sep"

    [<Literal>]
    let FileUpload = "fuaran-file-upload"

    [<Literal>]
    let FileUploadInput = "fuaran-file-upload-input"

    [<Literal>]
    let FileUploadLabel = "fuaran-file-upload-label"

    [<Literal>]
    let FilterCheckbox = "fuaran-filter-checkbox"

    [<Literal>]
    let FilterInput = "fuaran-filter-input"

    [<Literal>]
    let FilterInputColorInput = "fuaran-filter-input fuaran-color-input"

    [<Literal>]
    let FilterInputComboboxInput = "fuaran-filter-input fuaran-combobox-input"

    [<Literal>]
    let FilterInputFilterRangeMax = "fuaran-filter-input fuaran-filter-range-max"

    [<Literal>]
    let FilterInputFilterRangeMin = "fuaran-filter-input fuaran-filter-range-min"

    [<Literal>]
    let FilterLabel = "fuaran-filter-label"

    [<Literal>]
    let FilterRange = "fuaran-filter-range"

    [<Literal>]
    let FilterRangeMax = "fuaran-filter-range-max"

    [<Literal>]
    let FilterRangeMin = "fuaran-filter-range-min"

    [<Literal>]
    let FilterRangeSep = "fuaran-filter-range-sep"

    [<Literal>]
    let FilterSelect = "fuaran-filter-select"

    [<Literal>]
    let FilterToggle = "fuaran-filter-toggle"

    [<Literal>]
    let Filters = "fuaran-filters"

    [<Literal>]
    let Form = "fuaran-form"

    [<Literal>]
    let FormField = "fuaran-form-field"

    [<Literal>]
    let FormFieldControlComboboxInput =
        "fuaran-form-field-control fuaran-combobox-input"

    [<Literal>]
    let FormFieldControlTokensInput = "fuaran-form-field-control fuaran-tokens-input"

    [<Literal>]
    let FormFieldset = "fuaran-form-fieldset"

    [<Literal>]
    let FormHelp = "fuaran-form-help"

    [<Literal>]
    let FormSubmit = "fuaran-form-submit"

    [<Literal>]
    let FragmentCyclePlaceholder = "fuaran-fragment-cycle-placeholder"

    [<Literal>]
    let FragmentUnresolvedPlaceholder = "fuaran-fragment-unresolved-placeholder"

    [<Literal>]
    let Image = "fuaran-image"

    [<Literal>]
    let ImageAvatar = "fuaran-image fuaran-image-avatar"

    [<Literal>]
    let ImageExpand = "fuaran-image-expand"

    [<Literal>]
    let ImageFigure = "fuaran-image-figure"

    [<Literal>]
    let ImageFigureCaption = "fuaran-image-figure-caption"

    [<Literal>]
    let ImageRounded = "fuaran-image fuaran-image-rounded"

    [<Literal>]
    let LabelValueRowLabel = "fuaran-label-value-row-label"

    [<Literal>]
    let LabelValueRowValue = "fuaran-label-value-row-value"

    [<Literal>]
    let LayoutCard = "fuaran-layout-card"

    [<Literal>]
    let LayoutDashboard = "fuaran-layout-dashboard"

    [<Literal>]
    let LayoutDisclosure = "fuaran-layout-disclosure"

    [<Literal>]
    let LayoutGrid = "fuaran-layout-grid"

    [<Literal>]
    let LayoutMasonry = "fuaran-layout-masonry"

    [<Literal>]
    let LayoutSeparator = "fuaran-layout-separator"

    [<Literal>]
    let LayoutSplitPanel = "fuaran-layout-split-panel"

    [<Literal>]
    let LayoutStepper = "fuaran-layout-stepper"

    [<Literal>]
    let LayoutSummaryList = "fuaran-layout-summary-list"

    [<Literal>]
    let Link = "fuaran-link"

    [<Literal>]
    let LinkProtectedWrap = "fuaran-link-protected-wrap"

    [<Literal>]
    let ListItem = "fuaran-list-item"

    [<Literal>]
    let ListOrdered = "fuaran-list fuaran-list-ordered"

    [<Literal>]
    let ListUnordered = "fuaran-list fuaran-list-unordered"

    [<Literal>]
    let Markdown = "fuaran-markdown"

    [<Literal>]
    let MathBlock = "fuaran-math fuaran-math-block"

    [<Literal>]
    let MathInline = "fuaran-math fuaran-math-inline"

    [<Literal>]
    let MathSource = "fuaran-math-source"

    [<Literal>]
    let MediaAudio = "fuaran-media-audio"

    [<Literal>]
    let MediaGroup = "fuaran-media-group"

    [<Literal>]
    let MediaTranscript = "fuaran-media-transcript"

    [<Literal>]
    let MediaTranscriptBody = "fuaran-media-transcript-body"

    [<Literal>]
    let MediaTranscriptSummary = "fuaran-media-transcript-summary"

    [<Literal>]
    let MediaVideo = "fuaran-media-video"

    [<Literal>]
    let MetricIcon = "fuaran-metric-icon"

    [<Literal>]
    let MetricLabel = "fuaran-metric-label"

    [<Literal>]
    let MetricSubtext = "fuaran-metric-subtext"

    [<Literal>]
    let MetricTrend = "fuaran-metric-trend"

    [<Literal>]
    let MetricTrendGlyph = "fuaran-metric-trend-glyph"

    [<Literal>]
    let MetricValue = "fuaran-metric-value"

    [<Literal>]
    let ModalBody = "fuaran-modal-body"

    [<Literal>]
    let ModalDialog = "fuaran-modal-dialog"

    [<Literal>]
    let ModalDismiss = "fuaran-modal-dismiss"

    [<Literal>]
    let ModalHeading = "fuaran-modal-heading"

    [<Literal>]
    let ModalOverlay = "fuaran-modal-overlay"

    [<Literal>]
    let MountBoundary = "fuaran-mount-boundary"

    [<Literal>]
    let MountPlaceholder = "fuaran-mount-placeholder"

    [<Literal>]
    let NodeFallback = "fuaran-node-fallback"

    [<Literal>]
    let Popover = "fuaran-popover"

    [<Literal>]
    let PopoverBody = "fuaran-popover-body"

    [<Literal>]
    let PopoverDismiss = "fuaran-popover-dismiss"

    [<Literal>]
    let PopoverHeading = "fuaran-popover-heading"

    [<Literal>]
    let PopoverSurface = "fuaran-popover-surface"

    [<Literal>]
    let ProgressBar = "fuaran-progress-bar"

    [<Literal>]
    let ProgressFill = "fuaran-progress-fill"

    [<Literal>]
    let ProgressLabel = "fuaran-progress-label"

    [<Literal>]
    let ScrollareaBoth = "fuaran-scrollarea fuaran-scrollarea-both"

    [<Literal>]
    let ScrollareaHorizontal = "fuaran-scrollarea fuaran-scrollarea-horizontal"

    [<Literal>]
    let ScrollareaVertical = "fuaran-scrollarea fuaran-scrollarea-vertical"

    [<Literal>]
    let SegmentedHorizontal = "fuaran-segmented-horizontal"

    [<Literal>]
    let SegmentedLegend = "fuaran-segmented-legend"

    [<Literal>]
    let SegmentedOption = "fuaran-segmented-option"

    [<Literal>]
    let SegmentedRow = "fuaran-segmented-row"

    [<Literal>]
    let SegmentedVertical = "fuaran-segmented-vertical"

    [<Literal>]
    let Select = "fuaran-select"

    [<Literal>]
    let SelectControl = "fuaran-select-control"

    [<Literal>]
    let SelectLabel = "fuaran-select-label"

    [<Literal>]
    let Skeleton = "fuaran-skeleton"

    [<Literal>]
    let SkeletonRow = "fuaran-skeleton-row"

    [<Literal>]
    let Sparkline = "fuaran-sparkline"

    [<Literal>]
    let SparklineEmpty = "fuaran-sparkline fuaran-sparkline-empty"

    [<Literal>]
    let SplitPaneLeft = "fuaran-split-pane fuaran-split-pane-left"

    [<Literal>]
    let SplitPaneRight = "fuaran-split-pane fuaran-split-pane-right"

    [<Literal>]
    let StackHorizontal = "fuaran-stack-horizontal"

    [<Literal>]
    let StackVertical = "fuaran-stack-vertical"

    [<Literal>]
    let StepperBody = "fuaran-stepper-body"

    [<Literal>]
    let StepperNumbers = "fuaran-stepper-numbers"

    [<Literal>]
    let StepperStep = "fuaran-stepper-step"

    [<Literal>]
    let StepperStepActive = "fuaran-stepper-step fuaran-stepper-step-active"

    [<Literal>]
    let SummaryListBody = "fuaran-summary-list-body"

    [<Literal>]
    let SummaryListHeading = "fuaran-summary-list-heading"

    [<Literal>]
    let Tab = "fuaran-tab"

    [<Literal>]
    let TabActive = "fuaran-tab-active"

    [<Literal>]
    let TabDisabled = "fuaran-tab-disabled"

    [<Literal>]
    let TabIcon = "fuaran-tab-icon"

    [<Literal>]
    let TabLabel = "fuaran-tab-label"

    [<Literal>]
    let Table = "fuaran-table"

    [<Literal>]
    let TableCell = "fuaran-table-cell"

    [<Literal>]
    let TableHeader = "fuaran-table-header"

    [<Literal>]
    let TableRow = "fuaran-table-row"

    [<Literal>]
    let TabsBar = "fuaran-tabs-bar"

    [<Literal>]
    let TabsHorizontal = "fuaran-tabs-horizontal"

    [<Literal>]
    let TabsPanel = "fuaran-tabs-panel"

    [<Literal>]
    let TabsPanels = "fuaran-tabs-panels"

    [<Literal>]
    let TabsVertical = "fuaran-tabs-vertical"

    [<Literal>]
    let ToastDismiss = "fuaran-toast-dismiss"

    [<Literal>]
    let ToastMessage = "fuaran-toast-message"

    [<Literal>]
    let Tooltip = "fuaran-tooltip"

    [<Literal>]
    let Tree = "fuaran-tree"

    [<Literal>]
    let TreeGroup = "fuaran-tree-group"

    [<Literal>]
    let TreeItem = "fuaran-tree-item"

    [<Literal>]
    let TreeLabel = "fuaran-tree-label"

    [<Literal>]
    let UnknownPlaceholder = "fuaran-unknown-placeholder"
