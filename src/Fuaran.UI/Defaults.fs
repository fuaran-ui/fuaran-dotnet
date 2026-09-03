module Fuaran.UI.Defaults

// ============================================================================
//  Fuaran — typed Defaults.X records (§4b amendment lines 471–491, §4d line 712)
//
//  Every spec record ships a typed `Defaults.X` value. F# authors use
//  `{ Defaults.metric with Label = ... }`. AI emits only fields that differ from
//  default; the renderer (session 3+) re-applies defaults during JSON ingest.
//
//  Session 2 seed covers the seven shapes needed for the §4c lines 504–542
//  authoring example (dashboard / metric / grid / markdown / button / callout /
//  progress) plus the supporting state-behaviour / style / column defaults the
//  seed components touch. Layouts other than Dashboard get defaults too — the
//  seven LayoutKind cases all need a Defaults.X for parity.
// ============================================================================

open Fuaran.UI.Types

// ─── Cross-cutting defaults (referenced from every component) ───────────────

let style: SemanticStyle =
    { Tone = ToneVariant.Default
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Role = StyleRole.None
      Voice = FontVoice.Default
      // Phase 1472 — the identity: this value declares no direction of its own,
      // so the bidirectional algorithm resolves it from its own characters
      // exactly as it did before the slot existed. Omitted on the wire.
      Direction = TextDirection.Auto }

let stateBehaviour<'Msg> : StateBehaviour<'Msg> =
    { OnLoading = Option.None
      OnEmpty = Option.None
      OnError = Option.None }

let private emptyLiteral: TextSource = TextSource.Literal ""

/// Sentinel `Binding.Query` name the resolver returns `NotResolved` for.
/// Consumers MUST NOT register a query under this name in
/// `BindingSources.QueryResults`; the double-underscore + repo-namespaced
/// shape makes a real collision effectively impossible. Encodes
/// "`Source =` is mandatory at runtime but the author hasn't overridden
/// the default yet" in the existing §4b DU shape — the renderer reads
/// `NotResolved` and substitutes the `OnLoading` slot rather than
/// silently formatting `Unchecked.defaultof<'T>` (which would render a
/// missing-source Metric as `0`).
[<Literal>]
let NotProvidedSentinel = "__fuaran_not_provided__"

// Sentinel default `Binding<'T>` for spec records whose source is mandatory
// at runtime but must have a placeholder at default-construction time.
// Encoded as a `Binding.Query` against `NotProvidedSentinel`; the resolver
// short-circuits on that name to `NotResolved`, and even without the
// short-circuit `Query` semantics return `NotResolved` for unregistered
// names. The accessor returns `Unchecked.defaultof<'T>` but is never
// invoked under the sentinel path.
let private noBinding<'T> : Binding<'T> =
    Binding.Query(NotProvidedSentinel, (fun _ -> Unchecked.defaultof<'T>), None)

// ─── Local binding default ───────────────────────────────────────────────────
//
// `localBinding` is the `Binding.Local` stub authors / smart-ctors use as a
// starting point (positional since the swap — the `LocalBinding<'T>` record is
// retired). Like `noBinding<'T>`, the initialFrom is the `NotProvidedSentinel`
// query — authors override via `binding.local`. The identity-error `parse`
// represents the "must-be-overridden" state PreEmitValidate FUARAN042 catches
// if it reaches the wire.

#nowarn "3261"

let localBinding<'T> : Binding<'T> =
    Binding.Local(
        LocalFlushTrigger.OnBlur,
        (fun (v: 'T) -> string (box v)),
        noBinding<'T>,
        // Non-null sentinel string — F# 10 nullness disallows `box ()` here
        // because the boxed unit value is `null`. The renderer never
        // dispatches this sentinel (validator FUARAN042 rejects a Local
        // binding without Format/Parse, and the renderer only dispatches
        // through `onCommit` after `parse` succeeds, which the defaulted
        // `parse` can never do).
        Some(fun _ -> box "__fuaran_local_no_commit__"),
        (fun _ -> Error "no Parse function supplied to Binding.Local")
    )

#warnon "3261"

// ─── Layout defaults ────────────────────────────────────────────────────────

let dashboard<'Msg> : DashboardSpec<'Msg> = { Children = [] }

let stack<'Msg> : StackSpec<'Msg> =
    { Orientation = Orientation.Vertical
      Children = []
      Wrap = false }

let gridLayout<'Msg> : GridLayoutSpec<'Msg> =
    { Cols = 12
      Children = []
      // Typed-escape additive — `None` preserves the
      // `repeat({Cols}, 1fr)` emission shape byte-identical.
      // Authors who need irregular column sizing reach for
      // `Fuaran.gridLayoutTemplated` (which pre-populates this field) or
      // record-with the spec directly.
      TemplateColumns = Option.None }

/// Phase 1082 — column-fill. `3` rather than `gridLayout`'s `12`: the grid
/// default is a twelve-slot TRACK model an author subdivides, while a masonry
/// column is a real column of content, and three is the count a picture wall
/// actually wants. A default of 12 would render 12 near-empty ribbons.
let masonryLayout<'Msg> : MasonryLayoutSpec<'Msg> = { Cols = 3; Children = [] }

let splitPanel<'Msg> : SplitPanelSpec<'Msg> = { Weight = 0.5; Children = [] }

let tabs<'Msg> : TabsSpec<'Msg> =
    // `OnSelect = None` (Phase 426): the declarative floor — a State/Filter-bound
    // `ActiveIndex` gets the clicked index written back by the renderer; the
    // static default renders but never switches (same dead behaviour as the
    // pre-426 no-op closure, minus the sentinel on the wire).
    { Children = []
      ActiveIndex = Binding.Static(Some 0)
      OnSelect = Option.None
      TabHeaders = Option.None
      TabTags = Option.None
      ActiveTag = Option.None
      OnSelectTag = Option.None
      Orientation = Orientation.Horizontal }

/// Empty header — Literal "" label, no icon, no
/// disabled binding. Pair with `Fuaran.tabsTagged` or the with-syntax record
/// update to populate per-tab labels.
let tabHeader: TabHeader =
    { Label = emptyLiteral
      Icon = Option.None
      Disabled = Option.None }

let card<'Msg> : CardSpec<'Msg> = { Heading = Option.None; Children = [] }

let stepper<'Msg> : StepperSpec<'Msg> =
    // `OnSelect = None` since the swap (the generated record's handler is an
    // option) — ≡ the old no-op `Action.Chain []` closure: steps render and
    // active styling tracks `ActiveStep`, no dispatch on click.
    { ActiveStep = Binding.Static(Some 0)
      Children = []
      OnSelect = Option.None }

let summaryList<'Msg> : SummaryListSpec<'Msg> =
    { Heading = Option.None; Children = [] }

let disclosure<'Msg> : DisclosureSpec<'Msg> =
    // `OnToggle = None` (Phase 426): the write-back default — a State/Filter-bound
    // `Open` gets the new open value written back by the renderer.
    { Heading = emptyLiteral
      Open = Binding.Static(Some false)
      OnToggle = Option.None
      Children = []
      DefaultOpen = false }

let modal<'Msg> : ModalSpec<'Msg> =
    // `OnDismiss = None` (Phase 426): the write-back default — a State/Filter-bound
    // `Open` gets `false` written back on dismiss.
    // Phase 1119: `Modality = Modal` IS the wire identity — the member is omitted
    // at this value, so the default spec and every pre-1119 modal document encode
    // to the same bytes. `Anchor = None` for the same reason: a blocking dialog
    // has nothing to anchor to.
    { Open = Binding.Static(Some false)
      Heading = Option.None
      Dismissable = true
      Children = []
      OnDismiss = Option.None
      Modality = ModalityKind.Modal
      Anchor = Option.None }

let scrollArea<'Msg> : ScrollAreaSpec<'Msg> =
    { Orientation = ScrollOrientation.Vertical
      Children = []
      MaxHeight = Option.None
      MaxWidth = Option.None }

// ─── Display defaults ───────────────────────────────────────────────────────

let heading: HeadingSpec =
    { Level = 2
      Text = emptyLiteral
      Variant = HeadingVariant.Standard }

let labelValueRow: LabelValueRowSpec =
    { Label = emptyLiteral
      Value = noBinding<float>
      Format = CellFormat.None
      Emphasis = false
      Help = Option.None }

let fact: FactSpec =
    { Label = emptyLiteral
      Value = emptyLiteral
      Icon = Option.None
      Tone = ToneVariant.Default
      Emphasis = false
      Help = Option.None }

let markdown: MarkdownSpec = { Text = emptyLiteral }

let metric: MetricSpec =
    { Label = emptyLiteral
      Value = noBinding<float>
      Format = CellFormat.None
      Tone = ToneVariant.Default
      Weight = StyleWeight.Standard
      Emphasis = Emphasis.Normal
      Trend = Option.None
      TrendFormat = Option.None
      TrendPolarity = TrendPolarity.HigherIsBetter
      Icon = Option.None
      Subtext = Option.None }

let badge: BadgeSpec =
    { Label = emptyLiteral
      Variant = BadgeVariant.Neutral }

let link: LinkSpec =
    { Href = noBinding<string>
      Label = emptyLiteral
      Rel = Option.None
      Target = Option.None
      Download = false
      Protection = Option.None }

let image: ImageSpec =
    { Src = noBinding<string>
      Alt = emptyLiteral
      Variant = ImageVariant.Default
      // Phase 1077 — all three presentation slots default to today's
      // behaviour, so `Defaults.image` is the pre-phase image exactly.
      Fit = ImageFit.Natural
      AspectRatio = ImageAspect.Natural
      Loading = ImageLoading.Eager
      // Phase 1080 — no alternate sources. The empty list is the identity here
      // in the strongest sense: an image with no candidates is served from
      // `Src` alone, which is what every image did before the slot existed.
      SrcSet = []
      // Phase 1079 — not expandable. `false` is the identity in the same sense
      // the empty candidate list is: an image that declares no expansion
      // renders exactly the `<img>` every image rendered before the slot
      // existed, with no anchor around it.
      Expandable = false
      // Phase 1078 — no caption is the pre-phase image, and `None` is the only
      // honest default: a caption is content, and inventing one is not a
      // default but a fabrication.
      Caption = None }

/// Phase 1076 — the default media surface. `Controls = true` is the one value
/// here that is a POSITION rather than an identity: a `<video>` with no
/// transport cannot be paused or muted by a keyboard user, so the accessible
/// setting is what an author gets without asking, and turning it off is the
/// deviation. `Kind` defaults to `Video` with autoplay off and no poster —
/// autoplay is never a default on any surface, which is the whole of that
/// slot's design. `Label` is `emptyLiteral` for the same reason `image`'s `Alt`
/// is: `Defaults` never invents content, and the empty label is what the
/// pre-emit validator refuses (unlike an image, media has no decorative case,
/// so an empty label here is always a defect rather than sometimes a
/// declaration).
let media: MediaSpec =
    { Controls = true
      Kind = MediaKind.Video(false, None)
      Label = emptyLiteral
      Loop = false
      Src = noBinding<string>
      // Phase 1110 — both new slots default to ABSENT, and neither invents
      // content: `Defaults` cannot know what a recording says. An empty track
      // list and no transcript are the honest starting point, and they are also
      // the wire's identity for both fields, so a default-constructed media node
      // encodes to exactly the bytes it encoded to before this phase.
      Tracks = []
      Transcript = None }

/// Phase 1111 — the sandboxed third-party embed. `Permissions` is EMPTY, which
/// is total denial: the default-constructed embed grants the framed document
/// nothing at all, and every relaxation is something a caller adds by name.
/// `Title` is `emptyLiteral` on `media`'s rule — `Defaults` never invents
/// content, and the empty title is exactly what FUARAN115 refuses, so a caller
/// who never set one is told rather than shipped a frame announced as "frame".
/// `AspectRatio` is `Natural`, the wire identity, meaning the host reserves no
/// box for the frame.
let embed: EmbedSpec =
    { AspectRatio = ImageAspect.Natural
      Permissions = []
      Src = noBinding<string>
      Title = emptyLiteral }

let list: ListSpec = { Items = []; Ordered = false }

/// Phase 1120 — the empty tree. No rows, and neither State key named: a tree
/// authored from this default renders an empty hierarchy with no reader-driven
/// behaviour, which is the honest starting point. Naming a key here would give
/// every default-constructed tree a claim on a State slot its author never
/// chose.
let tree: TreeSpec<'Msg> =
    { ExpandedStateKey = None
      Items = []
      OnSelect = None
      SelectionStateKey = None }

/// Phase 1120 — one row. `Label` is the empty literal FUARAN127 refuses, on
/// `Defaults.media`'s pattern exactly: the record has to be constructible before
/// it can be filled in, and the pre-emit gate is what makes the unfilled case a
/// defect rather than a silent one.
let treeItem: TreeItem =
    { Children = []
      Icon = None
      Id = ""
      Label = emptyLiteral }

let toast: ToastSpec =
    { Message = emptyLiteral
      Tone = ToneVariant.Info
      Open = Binding.Static(Some false)
      Dismissable = true }

let codeBlock: CodeBlockSpec =
    { Code = ""
      Language = "text"
      LineNumbers = false
      HighlightLines = []
      Copyable = true }

let math: MathSpec =
    { Source = ""
      Display = MathDisplay.Block }

let sparkline: SparklineSpec = { Source = noBinding<float list> }

/// An all-inherited draw style — every field `None`, so a shape emits `{}` and
/// inherits the renderer's defaults (Phase 524).
let drawStyle: DrawStyle =
    { Fill = Option.None
      Stroke = Option.None
      StrokeWidth = Option.None
      Opacity = Option.None
      TextAnchor = Option.None
      FontSize = Option.None
      Emphasis = Option.None
      FontFamily = Option.None
      MarkId = Option.None
      Rotation = Option.None
      Tip = Option.None }

/// An empty drawing over a unit-square viewBox (Phase 524). Authors set
/// `ViewBox` + `Shapes`; a chart lowering (Phase 526) produces both.
let drawing: DrawingSpec =
    { ViewBox =
        { MinX = 0.0
          MinY = 0.0
          Width = 100.0
          Height = 100.0 }
      Shapes = []
      Style = drawStyle
      Title = Option.None
      Description = Option.None }

let skeleton: SkeletonSpec = { Rows = 3 }

let callout: CalloutSpec =
    { Tone = ToneVariant.Info
      Heading = Option.None
      Body = emptyLiteral
      Icon = Option.None
      Dismissable = false }

let progress: ProgressSpec =
    { Fraction = Binding.Static(Some 0.0)
      Label = Option.None
      Caveat = Option.None
      Indeterminate = false
      Tone = ToneVariant.Default }

// ─── Input defaults ─────────────────────────────────────────────────────────

let button<'Msg> : ButtonSpec<'Msg> =
    { Label = emptyLiteral
      OnClick = Action.Chain []
      Variant = ButtonVariant.Secondary
      Icon = Option.None
      Tooltip = Option.None
      Disabled = Option.None }

let select<'Msg> : SelectSpec<'Msg> =
    // `OnChange = None` (Phase 426): the write-back default — a State/Filter-bound
    // `Value` gets the chosen option written back by the renderer.
    { Label = emptyLiteral
      Source = Binding.Static(Some [])
      Value = Binding.Static None
      OnChange = Option.None
      Placeholder = Option.None
      Disabled = Option.None
      // Phase 291: single-select by default — Multiple/Values/OnChangeMulti
      // omitted on the wire so every existing Select fixture stays byte-identical.
      Multiple = Option.None
      Values = Option.None
      OnChangeMulti = Option.None }

let form<'Msg> : FormSpec<'Msg> =
    { Fields = []
      OnSubmit = Action.Chain []
      SubmitLabel = TextSource.Literal "Submit"
      Disabled = Option.None }

let formField<'Msg> : FormField<'Msg> =
    // Handler-free `Text` kind (Phase 426): the write-back default — a
    // State/Filter-bound `value` gets the typed string written back.
    { Id = ""
      Label = emptyLiteral
      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Option.None)
      Required = false
      Help = Option.None
      // Phase 864 — an unconstrained field. `required` is the pre-existing
      // degenerate rule and stays above; everything else a field may demand of
      // its value lives in `Rule`, and the default is to demand nothing.
      Rule = Option.None }

/// `NumberFieldConstraints` default — all three bounds absent.
/// Authors who want the canonical `FormFieldKind.Number` shape (no bounds)
/// don't have to write the record; the `FormFieldKind.rangedNumber` /
/// `numberStepped` smart-ctors layer the trio in.
let numberFieldConstraints: NumberFieldConstraints =
    { Min = Option.None
      Max = Option.None
      Step = Option.None }

/// `DateFieldConstraints` default — all bounds absent (Phase 288). The
/// `FormFieldKind.date` smart-ctor layers them in when an author supplies
/// min/max/step.
let dateFieldConstraints: DateFieldConstraints =
    { Min = Option.None
      Max = Option.None
      Step = Option.None }

let filter<'Msg> : FilterSpec<'Msg> =
    { Name = ""
      Label = emptyLiteral
      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), Option.None) }

let fileUpload<'Msg> : FileUploadSpec<'Msg> =
    { Label = emptyLiteral
      Accept = []
      Multiple = false
      OnSelect = Some(fun _ -> Action.Chain [])
      Disabled = Option.None
      // Phase 1115 — both gestures OFF by default, which is the wire identity:
      // a default upload encodes to exactly the bytes it always did, and the
      // shortest document is the plain picker. Turning a gesture on is the
      // thing an emitter has to ask for.
      AcceptPaste = false
      DropTarget = false }

// ─── Visualisation defaults ─────────────────────────────────────────────────

let chart<'Msg> : ChartSpec<'Msg> =
    { Source = noBinding<Row seq>
      Kind = ChartKind.Line
      XField = ""
      YFields = []
      Title = Option.None
      ValueFormat = Option.None
      XTitle = Option.None
      YTitle = Option.None
      Subtitle = Option.None
      LegendPosition = Option.None
      DataLabels = Option.None
      XScale = Option.None
      OnPointClick = Option.None
      Stacked = false }

let table<'Msg> : TableSpec<'Msg> =
    { Headers = []
      Rows = []
      OnRowClick = Option.None
      // Phase 801 — sort intent is opt-in; absent is absent on the wire.
      Sortable = Option.None
      DefaultSort = Option.None }

let map<'Msg> : MapSpec<'Msg> =
    { Source = noBinding<MapMarker list>
      CentreLatitude = 0.0
      CentreLongitude = 0.0
      Zoom = 4
      OnMarkerClick = Option.None }

/// Typed author-facing default for `Fuaran.grid<'row,'Msg>`. The author writes
/// `{ Defaults.grid<MyRow,Msg> with Source = ...; RowKey = ...; Columns = ... }`.
/// Smart-ctor `Fuaran.grid` boxes into the tree-level `GridSpec<'Msg>`.
let grid<'row, 'Msg> : GridSpecOf<'row, 'Msg> =
    { Source = noBinding<'row seq>
      RowKey = (fun _ -> "")
      Columns = []
      OnRowClick = Option.None
      Editable = false
      // Phases 861 / 862 / 863 — the declarative grid-behaviour slots. All
      // absent by default: the pre-861 wire, byte-for-byte.
      SortStateKey = Option.None
      DefaultSort = Option.None
      PageSize = Option.None
      PageStateKey = Option.None
      EditStateKey = Option.None
      // Phase 1473 — the print-break declarations. Both off by default: the
      // pre-1473 wire, byte-for-byte, and a screen rendering that is unchanged.
      KeepRowsTogether = false
      RepeatHeader = false
      // Phase 1123 — the cross-container transfer pair. Both absent by default,
      // so a grid built from this default exchanges rows with nothing and emits
      // the bytes and the DOM it always did.
      TransferOutKey = Option.None
      TransferInKey = Option.None }

let column<'Msg> : Column<'Msg> =
    { Label = ""
      Value = (fun _ -> CellValue.Empty)
      Format = CellFormat.None
      Kind = CellKind.Text
      Width = ColumnWidth.Auto
      // Phases 861 / 863 — both narrowing flags inherit by default.
      Sortable = Option.None
      Editable = Option.None }

// ─── Custom — bounded-escape defaults ────────────────────────────────────────
//
// `NodeKind.Custom` carries optional `contentHash` (`None` =
// opt-out) + `exposedNodeIds` (`[]` = renderer doesn't introspect interior
// elements). The smart-ctor `Fuaran.custom` populates these from the
// arguments the author supplies; this default-record is here for
// completeness — authors who construct `NodeKind.Custom`
// directly can write `Custom("mod", "comp", props, Defaults.customContentHash,
// Defaults.customExposedNodeIds)`.

let customContentHash: ContentHash option = Option.None

let customExposedNodeIds: NodeId list = []

/// The zero `PropDecl` (Phase 1107): an unnamed, optional, any-JSON prop
/// declaring no inner wire format. Authored as
/// `{ Defaults.propDecl with Name = "points"; Type = PropType.PString }` per the
/// Phase-1106 construction convention, so the next additive field on the prop
/// schema costs no edit at any authoring site — which is what made the
/// payload-language annotation affordable in the first place.
let propDecl: PropDecl =
    { Name = ""
      Type = PropType.PJson
      Required = false
      PayloadLanguage = Option.None }

// ─── ErrorBoundary defaults ───────────────────────────────────────────────────
//
// `ErrorBoundary` is the AI-emittable shape for "if this subtree breaks,
// render this other thing instead". The default fallback is a labelled
// Skeleton placeholder so authors who start from `Defaults.errorBoundary`
// see a recognisable graceful-degradation surface even before they author
// their own fallback. The child defaults to the same Skeleton — authors
// must override at least `Child` (or both fields) to author a useful
// boundary; an unspecified child is structurally a no-op.

let private errorBoundaryPlaceholder<'Msg> : Node<'Msg> =
    // `State = None` / `Style = None` since the swap — the canonical empty /
    // default envelope shape (the encoder omitted the old empty records).
    { Id = "fuaran-error-boundary-placeholder"
      Kind = NodeKind.Skeleton({ Rows = 1 })
      State = Option.None
      Style = Option.None
      Accessibility = Option.None
      Motion = Option.None
      ExtraAttributes = Option.None
      Tooltip = Option.None }

let errorBoundary<'Msg> : ErrorBoundarySpec<'Msg> =
    { Child = errorBoundaryPlaceholder<'Msg>
      Fallback = errorBoundaryPlaceholder<'Msg> }

// `Switch` (Phase 392) defaults to an empty case list, a placeholder default
// child, and an empty state key — so a `{ Defaults.switch with StateKey = …;
// Cases = … }` authoring shape compiles mid-edit. Authors must set `StateKey`
// and at least one case for a useful switch; the empty-cases default renders
// only the `Default` child (a valid degenerate). FUARAN082 flags duplicate
// match values at validate time.
let switch<'Msg> : SwitchSpec<'Msg> =
    // Phase 768 — the default selector is the empty-key State form, so the
    // FUARAN083 "ungrounded switch" validator still fires on an unedited default.
    // Phase 1122 — `AutoAdvanceMs = None`: the default switch does not advance.
    // `None` is the only spelling of "no timer", so an unedited default is
    // exactly the pre-1122 switch in the type, on the wire and on the screen.
    { On = Binding.State("", None)
      Cases = []
      Default = errorBoundaryPlaceholder<'Msg>
      AutoAdvanceMs = None }

// ─── Fragment defaults ─────────────────────────────────────────────────────────
//
// `FragmentDecl` defaults to an empty-named decl wrapping a single-row
// Skeleton placeholder body. Authors must override `Name` (FUARAN056
// catches an empty name at validate time) and almost always `Body` —
// the placeholder is here so a `{ Defaults.fragmentDecl with Name = ... }`
// authoring shape compiles even mid-edit. `FragmentRef` defaults to an
// empty `Name` for the same reason; FUARAN057 catches the unresolved
// reference at validate time.

let private fragmentPlaceholder<'Msg> : Node<'Msg> =
    { Id = "fuaran-fragment-placeholder"
      Kind = NodeKind.Skeleton({ Rows = 1 })
      State = Option.None
      Style = Option.None
      Accessibility = Option.None
      Motion = Option.None
      ExtraAttributes = Option.None
      Tooltip = Option.None }

let fragmentDecl<'Msg> : FragmentDeclSpec<'Msg> =
    // `Holes = None` / `Effect = None` since the swap — ≡ the old `[]` /
    // pure-deterministic degenerate shape (both omitted on the wire).
    { Name = ""
      Body = fragmentPlaceholder<'Msg>
      Holes = Option.None
      Effect = Option.None }

let fragmentRef<'Msg> : FragmentRefSpec<'Msg> =
    // `Args = None` since the swap — ≡ the old empty map (omitted on the wire).
    { Name = ""; Args = Option.None }

// ─── Accessibility defaults ──────────────────────────────────────────────────
//
// Per-component defaults for the `Accessibility` trait on `Node<'Msg>`. The
// smart constructors in `Fuaran.fs` pass these into `buildNode` so authors
// don't have to author ARIA metadata for the common cases. Authors override
// per-Node by setting the `Accessibility` field on the constructed Node (via
// `Node.withAccessibility` helper if added later, or by record-with syntax).
//
// Design rules:
//   - Defaults stay generic (Role + LiveRegion where applicable); they do
//     NOT bake i18n keys into `Label`. When no `Accessibility.Label` is set
//     the renderer emits no `aria-label` — the spec's structural Label becomes
//     the element's text content, which supplies its accessible name.
//   - Decorative / structural Nodes (Spacer, Skeleton, Heading, Stack)
//     default to `None` — no aria-* emission. Adding ARIA metadata for
//     every Node pollutes the type contract without screen-reader benefit.
//   - Interactive Nodes (Button, Select, Form, FileUpload) default to
//     `Some { Role = ...; }`. The renderer + validator enforce a derivable
//     Label per interactive Kind.
//   - Notification Nodes (Callout, Progress) default to `Some { Role = Alert
//     or Status; LiveRegion = Assertive or Polite }` so dynamic content is
//     announced.

module Accessibility =
    /// Empty Accessibility trait — useful for tests and for Nodes whose Kind
    /// has a `None` default but the author wants to set the field explicitly.
    let empty: Accessibility =
        { Label = Option.None
          LabelledBy = Option.None
          DescribedBy = Option.None
          Role = Option.None
          LiveRegion = Option.None
          Hidden = Option.None }

    /// Most Nodes don't need ARIA metadata (decorative / structural shapes).
    /// The smart constructor passes this for layouts other than Dashboard,
    /// for Heading, Markdown, Spacer, Skeleton, Sparkline.
    let none: Accessibility option = Option.None

    // ─── Interactive defaults (validator-enforced via FUARAN040) ────────────

    /// Button: `Role = Button`. When `Accessibility.Label` is `None` no
    /// `aria-label` is emitted — `ButtonSpec.Label` renders as the button's
    /// text content, which supplies its accessible name.
    let button: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Button }

    /// Select: `Role = Custom "combobox"` — there's no canonical `AriaRole`
    /// case for combobox in this DU (kept small for the common cases);
    /// authors who need to override can write `Some AriaRole.Custom "..."`.
    let select: Accessibility option =
        Some
            { empty with
                Role = Some(AriaRole.Custom "combobox") }

    /// Form: `Role = Form`. When `Accessibility.Label` is `None` no
    /// `aria-label` is emitted — `FormSpec.SubmitLabel` names only the submit
    /// button (as its text content), not the form itself. Authors typically
    /// supply a real label for the form.
    let form: Accessibility option = Some { empty with Role = Some AriaRole.Form }

    /// FileUpload: `Role = Button` — file inputs render as a styled button
    /// in most design systems; the underlying `<input type=file>` is hidden.
    let fileUpload: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Button }

    // ─── Notification / status defaults ───────────────────────────────────

    /// Callout: `Role = Alert; LiveRegion = Assertive`. Validator FUARAN041
    /// warns if a `Warning` / `Critical` tone callout overrides this with
    /// `Accessibility = None` (loses the screen-reader announcement).
    let callout: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Alert
                LiveRegion = Some LiveRegionKind.Assertive }

    /// Progress: `Role = Progressbar; LiveRegion = Polite` — progress
    /// updates are announced without interrupting.
    let progress: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Progressbar
                LiveRegion = Some LiveRegionKind.Polite }

    /// Metric: no specific role (a Metric is a labelled numeric display, best
    /// served by structural `<div>` + screen-reader-friendly label text).
    /// `LiveRegion = Polite` so live-updating Metrics announce changes.
    let metric: Accessibility option =
        Some
            { empty with
                LiveRegion = Some LiveRegionKind.Polite }

    /// Toast: `Role = Status; LiveRegion = Polite` — a transient notification
    /// is announced without interrupting (Phase 289). A `Critical`-tone toast
    /// is still Polite at the Node level; escalate to Assertive by overriding
    /// the field when the message is genuinely urgent.
    let toast: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Status
                LiveRegion = Some LiveRegionKind.Polite }

    // ─── Layout defaults ──────────────────────────────────────────────────

    /// Dashboard: `Role = Main` — the dashboard is the page's primary
    /// content region.
    let dashboard: Accessibility option = Some { empty with Role = Some AriaRole.Main }

    /// Card: `Role = Region` — cards group related content.
    let card: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// SummaryList is a Region — groups related label/value rows
    /// the same way Card groups arbitrary content.
    let summaryList: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// Disclosure is a Region — groups related content the same
    /// way Card does. The `<details>` element itself carries the open/closed
    /// semantics + screen-reader `aria-expanded` state via native HTML; the
    /// Node-level Region role marks the outer container so the disclosure's
    /// content reads as a discrete group.
    let disclosure: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// Modal: `Role = Dialog` — the overlay is a dialog surface (Phase 289).
    /// The renderer adds `aria-modal="true"` on the dialog element; the
    /// Node-level Dialog role marks the container for screen-reader navigation.
    let modal: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Dialog }

    /// ScrollArea: `Role = Region` — a scrollable region groups its content
    /// (Phase 289); the renderer adds `tabindex="0"` so it is keyboard-
    /// focusable for scroll.
    let scrollArea: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// Tabs: `Role = Tablist` on the container — the renderer already
    /// emits per-tab `role="tab"` and `role="tablist"` on the inner bar,
    /// but having `tablist` on the Node lets screen readers announce the
    /// component shape immediately.
    let tabs: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Tablist }

    // ─── Visualisation defaults ───────────────────────────────────────────

    /// Grid: `Role = Region` — grids are interactive but their per-cell
    /// semantics are owned by the cell renderers. AG Grid sets its own
    /// `role="grid"` on the inner element; the Node-level Region marks
    /// the outer container for screen-reader navigation.
    let grid: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// Chart: `Role = Region; LiveRegion = Polite` — charts are
    /// data-bound; live data updates announce politely.
    let chart: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region
                LiveRegion = Some LiveRegionKind.Polite }

    /// Map: `Role = Region` — same rationale as Chart.
    let map: Accessibility option =
        Some
            { empty with
                Role = Some AriaRole.Region }

    /// Table: `Role = None` — the renderer emits a real `<table>`
    /// element, which has built-in `role="table"` semantics. No override
    /// needed.
    let table: Accessibility option = Option.None

// ─── Motion defaults ──────────────────────────────────────────────────────────
//
// Per-component defaults for the `Motion` trait on `Node<'Msg>`. Every
// smart constructor passes `Motion.none` (i.e. `Option.None`) by default
// — motion is opt-in via `Node.withMotion`. The module mirrors the
// `Accessibility` shape so AI authors and human authors find a
// per-component default if one is wanted; authors see no
// behavioural change.

module Motion =
    /// The "no motion" baseline that every smart constructor passes. `None`
    /// emits no `fuaran-motion-*` class. Equivalent to `Option.None` but
    /// surfaced under `Defaults.Motion.none` for parity with
    /// `Defaults.Accessibility.none`.
    let none: Motion option = Option.None

    /// Sensible defaults — none of these are applied automatically; smart
    /// constructors don't reach into this module. Authors use them as
    /// starting points for `Node.withMotion`.
    let metric: Motion option = Some Motion.FadeInOnMount
    let callout: Motion option = Some Motion.SlideInFromBelow
    let progress: Motion option = Some Motion.PulseDuringLoad
    let errorCallout: Motion option = Some Motion.ShakeOnError

// ─── Theme defaults ──────────────────────────────────────────────────────────
//
// `Defaults.theme` is the baseline Theme that mirrors the
// `fuaran-reference.css` `:root` block byte-for-byte. Apps that don't
// supply a Theme to the renderer see no visual change. A regression test
// in `Fuaran.UI.Tests/ThemeTests.fs` pins every variable value against
// the reference CSS so a stray edit to either side surfaces immediately.
//
// Every hex value, every pixel string, and every unitless number below
// mirrors the matching `--fuaran-*` declaration in `fuaran-reference.css`.
// When the reference CSS shifts (e.g. a future palette refresh), both
// files move in the same commit.

let tones: Tones =
    { Default =
        { Background = ColorVar.Hex "#ffffff"
          Foreground = ColorVar.Hex "#1f2937"
          Border = ColorVar.Hex "#e5e7eb" }
      Subdued =
        { Background = ColorVar.Hex "#f3f4f6"
          Foreground = ColorVar.Hex "#6b7280"
          Border = ColorVar.Hex "#d1d5db" }
      Brand =
        { Background = ColorVar.Hex "#eff6ff"
          Foreground = ColorVar.Hex "#1d4ed8"
          Border = ColorVar.Hex "#93c5fd" }
      Success =
        { Background = ColorVar.Hex "#ecfdf5"
          Foreground = ColorVar.Hex "#047857"
          Border = ColorVar.Hex "#6ee7b7" }
      Warning =
        { Background = ColorVar.Hex "#fffbeb"
          Foreground = ColorVar.Hex "#b45309"
          Border = ColorVar.Hex "#fcd34d" }
      Critical =
        { Background = ColorVar.Hex "#fef2f2"
          Foreground = ColorVar.Hex "#b91c1c"
          Border = ColorVar.Hex "#fca5a5" }
      Info =
        { Background = ColorVar.Hex "#eff6ff"
          Foreground = ColorVar.Hex "#1e40af"
          Border = ColorVar.Hex "#93c5fd" } }

let spacing: Spacing =
    { Xs = "4px"
      Sm = "8px"
      Md = "12px"
      Lg = "16px"
      Xl = "24px" }

let fontScale: FontScale =
    { Xs = "12px"
      Sm = "13px"
      Base = "14px"
      Lg = "16px"
      Xl = "20px"
      XXl = "24px"
      XXXl = "28px" }

let fontWeight: FontWeight =
    { Regular = 400
      Medium = 500
      Semibold = 600
      Bold = 700 }

let lineHeight: LineHeight =
    { Tight = 1.25
      Normal = 1.5
      Relaxed = 1.75 }

let radius: Radius =
    { Sm = "4px"
      Md = "6px"
      Lg = "8px"
      Full = "9999px" }

/// Mirrors the reference CSS where `--fuaran-button-pad-y` and
/// `--fuaran-button-pad-x` route through the spacing scale via
/// `var(--fuaran-space-{sm|md}, ...)` (composable density override). The
/// `FontSize` default routes through `--fuaran-text-base` for the same
/// reason.
let buttonSize: ButtonSize =
    { PadY = "var(--fuaran-space-sm, 8px)"
      PadX = "var(--fuaran-space-md, 12px)"
      FontSize = "var(--fuaran-text-base, 14px)" }

// ─── Interaction state matrix ──────────────────────────────────────────────────
//
// Per-state × per-tone × per-slot tokens — 7 tones × 4 states × 3 slots =
// 84 tokens, plus 4 global focus-ring tokens = 88. Mirrors the
// `--fuaran-tone-{tone}-{state}-{slot}` + `--fuaran-focus-ring-*` surface
// declared in `fuaran-reference.css` (`:root` block) byte-for-
// byte. The byte-for-byte regression in `ThemeTests.fs` reads the
// reference CSS at runtime and asserts every declaration matches.
//
// Static fallback rules — these mirror the rules documented in the phase
// task ("Hover fallback = brightness-adjusted base tone; focus fallback =
// brand border; active fallback = brightness-darker base tone; disabled
// fallback = subdued slot"):
//
//   Hover:    each slot = a darker variant of the base tone's same slot
//             (Tailwind's next-stop-down on the palette ladder).
//   Focus:    bg / fg keep the base value (no surface shift); border
//             defaults to brand-border so a focus ring reads as a brand-
//             coloured edge. The `:focus-visible` outline is the primary
//             affordance — these per-slot tokens give consumers a tint
//             surface for hosts that prefer an internal border highlight.
//   Active:   each slot = a darker-than-hover variant.
//   Disabled: every tone's slot collapses to subdued (the convention is
//             "drained colour" rather than per-tone disabled colours).

let private toneStops (bg: string) (fg: string) (border: string) : ToneStops =
    { Background = ColorVar.Hex bg
      Foreground = ColorVar.Hex fg
      Border = ColorVar.Hex border }

/// Hover state — darker-shift on each slot.
let toneStateMatrixHover: ToneStateMatrix =
    { Default = toneStops "#f9fafb" "#111827" "#d1d5db"
      Subdued = toneStops "#e5e7eb" "#4b5563" "#9ca3af"
      Brand = toneStops "#dbeafe" "#1e40af" "#60a5fa"
      Success = toneStops "#d1fae5" "#065f46" "#34d399"
      Warning = toneStops "#fef3c7" "#92400e" "#fbbf24"
      Critical = toneStops "#fee2e2" "#991b1b" "#f87171"
      Info = toneStops "#dbeafe" "#1e3a8a" "#60a5fa" }

/// Focus state — bg / fg unchanged; border defaults to brand-border so
/// the focused-edge tint reads as a brand accent.
let toneStateMatrixFocus: ToneStateMatrix =
    { Default = toneStops "#ffffff" "#1f2937" "#93c5fd"
      Subdued = toneStops "#f3f4f6" "#6b7280" "#93c5fd"
      Brand = toneStops "#eff6ff" "#1d4ed8" "#93c5fd"
      Success = toneStops "#ecfdf5" "#047857" "#93c5fd"
      Warning = toneStops "#fffbeb" "#b45309" "#93c5fd"
      Critical = toneStops "#fef2f2" "#b91c1c" "#93c5fd"
      Info = toneStops "#eff6ff" "#1e40af" "#93c5fd" }

/// Active state — darker-than-hover shift on each slot.
let toneStateMatrixActive: ToneStateMatrix =
    { Default = toneStops "#f3f4f6" "#111827" "#9ca3af"
      Subdued = toneStops "#d1d5db" "#374151" "#6b7280"
      Brand = toneStops "#bfdbfe" "#1e3a8a" "#3b82f6"
      Success = toneStops "#a7f3d0" "#064e3b" "#10b981"
      Warning = toneStops "#fde68a" "#78350f" "#f59e0b"
      Critical = toneStops "#fecaca" "#7f1d1d" "#ef4444"
      Info = toneStops "#bfdbfe" "#172554" "#3b82f6" }

/// Disabled state — every tone collapses to subdued's slots.
let toneStateMatrixDisabled: ToneStateMatrix =
    let subdued = toneStops "#f3f4f6" "#6b7280" "#d1d5db"

    { Default = subdued
      Subdued = subdued
      Brand = subdued
      Success = subdued
      Warning = subdued
      Critical = subdued
      Info = subdued }

/// Focus-ring shape — `:focus-visible` outline. The default colour
/// matches `Tones.Brand.Border` so the ring reads as a brand accent.
let focusRing: FocusRing =
    { Color = ColorVar.Hex "#93c5fd"
      Width = "2px"
      Offset = "2px"
      Style = "solid" }

let interaction: Interaction =
    { FocusRing = focusRing
      Hover = toneStateMatrixHover
      Focus = toneStateMatrixFocus
      Active = toneStateMatrixActive
      Disabled = toneStateMatrixDisabled }

/// Tab-bar tokens. Colour vars default to
/// `var(--fuaran-tone-brand-*)` references so themes that override the
/// brand stops carry through to tabs automatically. Reference CSS at
/// `fuaran-reference.css` mirrors this byte-for-byte.
let tabBar: TabBar =
    { PaddingY = "8px"
      PaddingX = "24px"
      IndicatorColor = ColorVar.CssRaw "var(--fuaran-tone-brand-fg, #1d4ed8)"
      IndicatorHeight = "2px"
      TextColor = ColorVar.CssRaw "var(--fuaran-tone-subdued-fg, #6b7280)"
      TextActiveColor = ColorVar.CssRaw "var(--fuaran-tone-brand-fg, #1d4ed8)"
      TextHoverColor = ColorVar.CssRaw "var(--fuaran-tone-brand-hover-fg, #1e40af)" }

/// Segmented-control / radio-group tokens. Routes
/// through the tone palette by default so themes that override
/// `Tones.Subdued.Background` / `Tones.Brand.Foreground` / `Tones.Default.Border`
/// carry through to the segmented control surface automatically. Reference
/// CSS at `fuaran-reference.css` mirrors this byte-for-byte.
let segmented: Segmented =
    { Background = ColorVar.CssRaw "var(--fuaran-tone-subdued-bg, #f3f4f6)"
      ActiveBackground = ColorVar.CssRaw "var(--fuaran-tone-default-bg, #ffffff)"
      ActiveForeground = ColorVar.CssRaw "var(--fuaran-tone-brand-fg, #1d4ed8)"
      DividerColor = ColorVar.CssRaw "var(--fuaran-tone-default-border, #e5e7eb)" }

/// Responsive breakpoint thresholds (Phase 58). The `sm` / `md` / `lg`
/// min-width boundaries the reference-CSS `@media` rules collapse layout
/// at. Mirrors the `--fuaran-breakpoint-*` declarations in
/// `fuaran-reference.css` byte-for-byte (the media-query conditions repeat
/// the px values literally — CSS can't read a custom property inside a
/// media condition).
let breakpoints: Breakpoints =
    { Sm = "640px"
      Md = "768px"
      Lg = "1024px" }

let theme: Theme =
    { Tones = tones
      Spacing = spacing
      FontScale = fontScale
      FontWeight = fontWeight
      LineHeight = lineHeight
      Radius = radius
      ButtonSize = buttonSize
      BorderWidth = "1px"
      Interaction = interaction
      TabBar = tabBar
      Segmented = segmented
      Breakpoints = breakpoints }

/// Phase 596 — the typed placeholder defaults the symmetric form-field
/// auto-bind synthesizes: a Form field whose `value` slot is absent on the
/// wire decodes to `Binding.State(field.Id, <this placeholder>)`, and the
/// canonical encoder omits a `value` that is exactly that auto-binding.
/// Decode, encode, and the resolver all reference THESE values — the
/// round-trip is byte-stable only while all three agree, so a new control
/// type adds its placeholder here first.
module ControlValueDefaults =
    let text: string = ""
    let number: float = 0.0
    let checkbox: bool = false
    let choice: string option = None

    /// Phase 1113 — the `Combobox` value slot IS `Choice`'s (a `Binding<string>`
    /// whose absent `Static` payload is "no selection"), so its placeholder is
    /// deliberately a BINDING of the same value rather than a second literal:
    /// the searchable form of a control must move when the control moves, and
    /// two literals spelling one fact is how that stops being true. It gets its
    /// own name because this module's contract is one entry per control type —
    /// decode, encode and the resolver all reference the control by name.
    let combobox: string option = choice

    let range: RangePair = { Max = 0.0; Min = 0.0 }
    /// ISO-empty — the Date control's value is an ISO-8601 string.
    let date: string = ""
    /// ISO-empty both ends — the DateRange control's value is an ordered
    /// `(from, to)` pair of ISO-8601 strings (Phase 725). Since the swap the
    /// pair IS the generated `DateRangePair` record, as `range` is `RangePair`.
    let dateRange: DateRangePair = { From = ""; To = "" }
