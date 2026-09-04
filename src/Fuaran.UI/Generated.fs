// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.
module Fuaran.UI.Generated

open Fuaran.Core

[<RequireQualifiedAccess>]
type BadgeVariant =
    | Neutral
    | Brand
    | Success
    | Warning
    | Critical
    | Info

[<RequireQualifiedAccess>]
type BoxRole =
    | Dashboard
    | Card
    | Group
    | Separator

[<RequireQualifiedAccess>]
type ButtonVariant =
    | Primary
    | Secondary
    | Tertiary
    | Destructive

[<RequireQualifiedAccess>]
type CaptureSource =
    | Camera
    | Microphone

[<RequireQualifiedAccess>]
type ChannelDirection =
    | OutOnly
    | TwoWay

/// Whether a chart writes its values directly onto the picture, and where
/// (Phase 881).
///
/// A WIRE vocabulary, on the D8 line's semantic side: whether the reader is
/// meant to READ THE NUMBERS or read the shape is the author's meaning; the
/// type size, the offsets and the fit rule that realise it stay the host's, in
/// `ChartStyle`.
///
/// THE CASE SET IS TWO, AND THAT IS THE POINT. There is deliberately no
/// "all points" case: a number on every interior point is the clutter this
/// vocabulary exists to avoid, so no shape of this API can request one. `Ends`
/// names the selective placements that read — a bar's cap, a line's last point
/// — and the set is closed there. Adding an all-points case later would not be
/// an extension; it would retract the guarantee.
[<RequireQualifiedAccess>]
type ChartDataLabels =
    /// No data labels (the shipped default, and what an absent field means).
    | Off
    /// Label the ENDS only: every bar's cap (a stacked bar's TOTAL at the stack
    /// cap, never its interior segments) and the last point of every line or
    /// area edge.
    | Ends

[<RequireQualifiedAccess>]
type ChartKind =
    | Line
    | Bar
    | Area
    | Pie
    | Scatter
    | Heatmap

/// Which edge of the chart the series legend occupies — or `None`, which
/// suppresses it entirely (Phase 880).
///
/// A WIRE vocabulary, on the same side of the D8 line as `ChartSpec.Title`:
/// WHERE an author wants the legend is their meaning; the geometry that puts it
/// there — column widths, pitches, how the plot shrinks — stays the host's, in
/// `ChartStyle`. `ChartStyle.LegendPosition` carries the DEFAULT (`Right`); an
/// explicit `ChartSpec.LegendPosition` beats it.
///
/// The case set is the four an author can mean. `Left` was declared by Phase 885
/// alongside the reserved field and is RETIRED here without ever having been
/// consumed by a lowering path: the left edge is the y axis's, so a legend there
/// competes with the tick column and the rotated axis title for the same band,
/// and the vocabulary charter's demand gate found no evidence for it. Retiring
/// an unconsumed case is cheaper than shipping a wire value that renders as a
/// guess.
[<RequireQualifiedAccess>]
type ChartLegendPosition =
    /// A horizontal row in the top margin, under the title (the pre-880 shape).
    | Top
    /// A vertical column on the right — one row per series, the plot shrinking
    /// by the column's width. The shipped default.
    | Right
    /// The same horizontal row, mirrored below the x-axis title.
    | Bottom
    /// No legend box at all.
    | None

/// What a chart's x axis MEANS — the scale its x column is read on (Phase 882).
///
/// A WIRE vocabulary on the D8 line's semantic side: whether a column is a set
/// of CATEGORIES or a run of DATES is a fact about the data the author is
/// declaring, not an appearance choice. Where the ticks land, how they are
/// formatted and how much margin they need are the host's, in `ChartStyle`.
///
/// DECLARED, NOT INFERRED, and that is the point of the field. The chart's data
/// schema is statically known only for an embedded table with an empty pipeline,
/// so an inferred axis would make one wire tree draw a band axis or a temporal
/// one depending on where its rows came from; and sniffing the cell strings for
/// an ISO-8601 shape is a guess dressed as a rule. Declaring it lets the
/// pre-emit validator GROUND the claim instead (FUARAN097 refuses a temporal
/// axis over a non-date column) — the author says what the column is, and the
/// language refuses to be wrong about it quietly.
[<RequireQualifiedAccess>]
type ChartXScale =
    /// Discrete categories, one band per row, in row order (the shipped
    /// default, and what an absent field means).
    | Category
    /// Dates: the x column carries canonical ISO-8601 dates (`YYYY-MM-DD`, or a
    /// timestamp whose time-of-day is discarded) and the axis is CONTINUOUS —
    /// points sit at their date, ticks land on calendar boundaries, and the
    /// tick labels adapt to the data's granularity.
    | Temporal

[<RequireQualifiedAccess>]
type CompareOp =
    | Eq
    | Neq
    | Lt
    | Lte
    | Gt
    | Gte

[<RequireQualifiedAccess>]
type DateStyle =
    | Short
    | Medium
    | Long
    | Full

[<RequireQualifiedAccess>]
type DateVariant =
    | Date
    | Time
    | DateTime

[<RequireQualifiedAccess>]
type DeterminismSource =
    | Deterministic
    | Clock
    | Random
    | Network

/// Phase 819 — presentation style for a duration: `Compact` "1h 20m",
/// `Clock` "1:20:00", `Long` "1 hour 20 minutes".
[<RequireQualifiedAccess>]
type DurationStyle =
    | Compact
    | Clock
    | Long

/// Phase 819 — how `Format.Duration` / `CellFormat.Duration` interpret the
/// numeric source: the unit the raw float counts.
[<RequireQualifiedAccess>]
type DurationUnit =
    | Seconds
    | Minutes
    | Hours

[<RequireQualifiedAccess>]
type EmbedPermission =
    | AllowScripts
    | AllowSameOrigin
    | AllowForms
    | AllowFullscreen

[<RequireQualifiedAccess>]
type Emphasis =
    | Quiet
    | Normal
    | Loud

[<RequireQualifiedAccess>]
type FileReadEncoding =
    | Text
    | Base64
    | DataUrl

[<RequireQualifiedAccess>]
type FontVoice =
    | Default
    | Display
    | Structural

[<RequireQualifiedAccess>]
type HashStrictness =
    | StrictReplay
    | AdvisoryWarning
    | Enforced

[<RequireQualifiedAccess>]
type HeadingVariant =
    | Standard
    | Eyebrow
    | Caption
    | Lead

[<RequireQualifiedAccess>]
type HostEffect =
    | Pure
    | ReadsHost
    | WritesHost

/// Phase 821 — size class for the standalone `Icon` display kind; `Medium`
/// is the default and is omitted on the wire.
[<RequireQualifiedAccess>]
type IconSize =
    | Small
    | Medium
    | Large

[<RequireQualifiedAccess>]
type ImageAspect =
    | Natural
    | Square
    | FourThree
    | ThreeTwo
    | SixteenNine

[<RequireQualifiedAccess>]
type ImageFit =
    | Natural
    | Cover
    | Contain

[<RequireQualifiedAccess>]
type ImageLoading =
    | Eager
    | Lazy

[<RequireQualifiedAccess>]
type ImageVariant =
    | Default
    | Avatar
    | Rounded

/// Phase 812 — anti-scraper render strategy for a `Link`. `Email` marks a
/// `mailto:` link whose address must not appear in plaintext in emitted HTML
/// (the renderers own the emission strategy).
[<RequireQualifiedAccess>]
type LinkProtection =
    | Email

[<RequireQualifiedAccess>]
type LiveRegionKind =
    | Polite
    | Assertive
    | Off

[<RequireQualifiedAccess>]
type MathDisplay =
    | Inline
    | Block

[<RequireQualifiedAccess>]
type ModalityKind =
    | Modal
    | Popover

[<RequireQualifiedAccess>]
type Motion =
    | None
    | PulseDuringLoad
    | FadeInOnMount
    | SlideInFromBelow
    | ShakeOnError
    | RotateOnRefresh
    | SlideInFromRight
    | ExpandCollapse
    | CrossFade
    | SlideBetween

[<RequireQualifiedAccess>]
type Orientation =
    | Vertical
    | Horizontal

[<RequireQualifiedAccess>]
type RelativeTimeUnit =
    | Second
    | Minute
    | Hour
    | Day
    | Week
    | Month
    | Year

[<RequireQualifiedAccess>]
type ScrollOrientation =
    | Vertical
    | Horizontal
    | Both

[<RequireQualifiedAccess>]
type SortDirection =
    | Asc
    | Desc

[<RequireQualifiedAccess>]
type StyleRole =
    | None
    | Eyebrow
    | Data
    | Lede
    | Caption

[<RequireQualifiedAccess>]
type StyleWeight =
    | Compact
    | Standard
    | Spacious

[<RequireQualifiedAccess>]
type TextAnchor =
    | Start
    | Middle
    | End

[<RequireQualifiedAccess>]
type TextDirection =
    | Auto
    | Ltr
    | Rtl

[<RequireQualifiedAccess>]
type TextFormat =
    | Email
    | Url
    | Tel

[<RequireQualifiedAccess>]
type ToneVariant =
    | Default
    | Subdued
    | Brand
    | Success
    | Warning
    | Critical
    | Info

[<RequireQualifiedAccess>]
type TrackKind =
    | Subtitles
    | Captions
    | Descriptions
    | Chapters

[<RequireQualifiedAccess>]
type TrendPolarity =
    | HigherIsBetter
    | LowerIsBetter

[<RequireQualifiedAccess>]
type Action<'Msg> =
    | Chain of ops: Action<'Msg> list
    /// Write `text` to the reader's clipboard.
    ///
    /// Phase 1126 — the payload is a `TextSource`, so what a reader copies may
    /// be a bound value or a computed reference rather than only a literal the
    /// author typed. The case was WIDENED rather than joined by a
    /// `WriteToClipboardBound` sibling: two cases for one intent is the
    /// permanent near-synonym pair the vocabulary charter exists to forbid, and
    /// a source break that the compiler names once is cheaper than a
    /// vocabulary that stays ambiguous forever.
    ///
    /// **The wire does not move for a literal payload.** `TextSource.Literal`
    /// is canonically the bare JSON string, so
    /// `{"$type":"WriteToClipboard","text":"…"}` is emitted and accepted
    /// exactly as it was before this release. Construction sites are what
    /// break, and they break at compile time: wrap the old argument in
    /// `TextSource.Literal`.
    ///
    /// A `Bound` payload resolves at DISPATCH time, through the same binding
    /// resolver the surrounding tree renders through — never at decode time,
    /// so the copied text is what the reader was looking at when they asked.
    ///
    /// There is deliberately no clipboard *read*: a tree that could read the
    /// clipboard without a paste gesture is a keylogger-adjacent capability.
    /// Paste is user-initiated by construction, and that is the boundary.
    | WriteToClipboard of text: TextSource
    | Dispatch of msg: ('Msg)
    | Invoke of capabilityId: string * args: InvokeArg list
    | ReadFileBody of fileRef: string * fileHandle: (obj option) * encoding: FileReadEncoding * onRead: (string -> 'Msg) option
    | Call of endpoint: string * onResult: (obj -> 'Msg) option * into: CallResultTarget option
    | Navigate of route: string
    | CommitLocal of nodeId: string
    | Notify of channel: string * payload: JVal
    // Phase 818 — `valueFrom` (a Binding evaluated at dispatch time inside the
    // existing gate) is a SIBLING of the literal `value`; decode enforces
    // value XOR valueFrom. `value` became an option in the same change so the
    // valueFrom-only wire shape is representable without a placeholder.
    | SetState of key: string * value: JVal option * valueFrom: Binding<JVal> option
    | AiTool of toolName: string * args: JVal
    /// Phase 1124 — open the reader's own print dialogue. The first
    /// PAYLOAD-FREE `Action` case, and the emptiness is the ruling: the
    /// paged MEDIUM is Host chrome, so a document may say *print now* and
    /// nothing about how. No page size, no margin, no sheet range, no
    /// target subtree — `{"$type":"Print"}` is the whole encoding.
    ///
    /// Not a hatch. `window.print()` opens a dialogue the reader operates
    /// and can cancel, hands the page to no third party, and returns
    /// nothing the tree can read — so it discloses less than the clipboard
    /// write beside it. It is gated all the same
    /// (`ActionDescriptor.Print`), because a host that renders untrusted
    /// trees must be able to refuse an unbidden dialogue.
    | Print

and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of value: 'T option
    | Query of name: string * accessor: (obj -> 'T) * dependsOn: string list option
    | Filter of name: string * defaultValue: 'T option
    | Selection of nodeId: string * accessor: (obj -> 'T) * defaultValue: 'T option * field: string option
    | State of key: string * defaultValue: 'T option
    | Now of accessor: (obj -> 'T)
    | Computed of fn: (obj -> 'T)
    | Local of flushOn: LocalFlushTrigger * format: ('T -> string) * initialFrom: Binding<'T> * onCommit: ('T -> obj) option * parse: (string -> Result<'T, string>)
    | Format of source: Binding<float> * format: Format * locale: LocaleSource
    | I18n of key: string * args: Map<string, Binding<JVal>> option
    // Phase 818 — the source slot widened from `Fuaran.Core.DataSource` to the
    // host `TransformSource` DU so a binding-shaped wire source (State /
    // Selection / Query) is PRESERVED for live re-evaluation instead of being
    // snapshotted at decode (the Phase-815 leniency's semantics upgrade).
    | Transform of source: TransformSource * pipeline: Fuaran.Core.Transform list * ``params``: TransformParam list option
    | Invoke of capabilityId: string * args: InvokeArg list

and [<RequireQualifiedAccess>] CallResultTarget =
    | State of key: string
    | Query of name: string

and [<RequireQualifiedAccess>] CellFormat =
    | None
    | Number of decimals: int option
    | Currency of code: string
    | Percent of decimals: int option
    | SignificantDigits of digits: int
    | Date of format: string
    /// Phase 819 — trendable duration cells: the raw float counts `unit`s,
    /// rendered per `style`.
    | Duration of unit: DurationUnit * style: DurationStyle
    /// Phase 819 — cell-vocabulary parity with `Format.RelativeTime`: the
    /// raw float is a signed count of `unit`.
    | RelativeTime of unit: RelativeTimeUnit
    | Custom of fn: (Fuaran.UI.HostPrelude.CellValue -> string)

and [<RequireQualifiedAccess>] CellKindErased<'Msg> =
    | Text
    | Numeric
    | Date
    | Editable of onEdit: (Fuaran.Core.Row * Fuaran.UI.HostPrelude.CellValue -> Action<'Msg>) option
    | Checkbox of get: (Fuaran.Core.Row -> bool) * onToggle: (Fuaran.Core.Row * bool -> Action<'Msg>) option
    | Button of label: TextSource * onClick: (Fuaran.Core.Row -> Action<'Msg>) option
    | ButtonGroup of buttons: ButtonGroupItem<'Msg> list
    | Link of hrefFn: (Fuaran.Core.Row -> string) * labelFn: (Fuaran.Core.Row -> TextSource)
    | Pill of labelFn: (Fuaran.Core.Row -> TextSource) * toneFn: (Fuaran.Core.Row -> ToneVariant)
    | TonedPill of field: string * map: Map<string, ToneVariant> * ``default``: ToneVariant
    | Progress of fractionFn: (Fuaran.Core.Row -> float) * labelFn: (Fuaran.Core.Row -> TextSource) option
    | Custom of fn: ((Fuaran.Core.Row -> JVal) -> Node<'Msg>)

and [<RequireQualifiedAccess>] ColumnWidth =
    | Auto
    | Fixed of pixels: int
    | Flex of weight: float

and [<RequireQualifiedAccess>] CurveCommand =
    | MoveTo of ``to``: DrawPoint
    | LineTo of ``to``: DrawPoint
    | CubicTo of control1: DrawPoint * control2: DrawPoint * ``to``: DrawPoint
    | QuadraticTo of control: DrawPoint * ``to``: DrawPoint
    | Close

and [<RequireQualifiedAccess>] FormFieldKind<'Msg> =
    | Text of value: Binding<string> option * onChange: (string -> Action<'Msg>) option
    | Number of value: Binding<float> option * onChange: (float -> Action<'Msg>) option
    | Checkbox of value: Binding<bool> option * onToggle: (bool -> Action<'Msg>) option
    | Toggle of value: Binding<bool> option * onToggle: (bool -> Action<'Msg>) option
    | Choice of options: Binding<SelectOption list> * value: Binding<string> option * onChange: (string option -> Action<'Msg>) option
    | TextArea of value: Binding<string> option * onChange: (string -> Action<'Msg>) option * rows: int
    | RangedNumber of value: Binding<float> option * onChange: (float -> Action<'Msg>) option * min: float option * max: float option * step: float option
    | Range of value: Binding<RangePair> option * onChange: (float * float -> Action<'Msg>) option * min: float option * max: float option * step: float option
    | SegmentedChoice of options: Binding<SelectOption list> * value: Binding<string> option * onChange: (string option -> Action<'Msg>) option * orientation: Orientation
    | Date of value: Binding<string> option * onChange: (string option -> Action<'Msg>) option * variant: DateVariant * min: string option * max: string option * step: float option
    | DateRange of value: Binding<DateRangePair> option * onChange: (string * string -> Action<'Msg>) option * variant: DateVariant * min: string option * max: string option * step: float option
    | Combobox of allowFreeText: bool * onChange: (string option -> Action<'Msg>) option * options: Binding<SelectOption list> * value: Binding<string> option
    | Rating of allowHalf: bool * max: int * onChange: (float -> Action<'Msg>) option * value: Binding<float> option
    | Color of onChange: (string -> Action<'Msg>) option * value: Binding<string> option

and [<RequireQualifiedAccess>] Format =
    | Number of decimals: int option
    | Currency of isoCode: string
    | Percent of decimals: int option
    | Date of dateStyle: DateStyle
    | RelativeTime of unit: RelativeTimeUnit
    /// Phase 819 — locale-independent duration formatting: the numeric
    /// source counts `unit`s, rendered per `style`.
    | Duration of unit: DurationUnit * style: DurationStyle

and [<RequireQualifiedAccess>] FragmentArg<'Msg> =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string
    | SlotArg of tree: Node<'Msg>

and [<RequireQualifiedAccess>] HoleDecl =
    | Value of name: string * space: HoleValueSpace * ``default``: Scalar option
    | Slot of name: string * kindConstraint: string option
    | Repeat of name: string * countSpace: HoleValueSpace

and [<RequireQualifiedAccess>] HoleValueSpace =
    | IntRange of min: int * max: int
    | FloatRange of min: float * max: float
    | StringLen of minLen: int * maxLen: int
    | Enum of choices: string list
    | AnyString

and [<RequireQualifiedAccess>] LayoutMode =
    | Auto
    | Flex of direction: Orientation * wrap: bool * gap: int option
    | Grid of cols: int * templateColumns: string option * gap: int option
    | Masonry of cols: int * gap: int option

and [<RequireQualifiedAccess>] LocalFlushTrigger =
    | OnBlur
    | OnSubmit
    | OnDebounce of milliseconds: int
    | OnCommitAction

and [<RequireQualifiedAccess>] LocaleSource =
    | Ambient
    | Explicit of tag: string

and [<RequireQualifiedAccess>] MediaKind =
    | Video of autoplay: bool * poster: Binding<string> option
    | Audio

and [<RequireQualifiedAccess>] Scalar =
    | Int of value: int
    | Float of value: float
    | Bool of value: bool
    | Str of value: string

and [<RequireQualifiedAccess>] Shape =
    | Group of children: Shape list * style: DrawStyle
    | Rectangle of x: float * y: float * width: float * height: float * cornerRadius: float option * style: DrawStyle
    | Line of x1: float * y1: float * x2: float * y2: float * style: DrawStyle
    | Polyline of points: DrawPoint list * style: DrawStyle
    | Polygon of points: DrawPoint list * style: DrawStyle
    | Curve of commands: CurveCommand list * style: DrawStyle
    | Circle of cx: float * cy: float * r: float * style: DrawStyle
    | Ellipse of cx: float * cy: float * rx: float * ry: float * style: DrawStyle
    | Label of x: float * y: float * text: TextSource * style: DrawStyle

and [<RequireQualifiedAccess>] TextSource =
    | Literal of text: string
    | Bound of binding: Binding<string>
    | I18n of key: string * args: Map<string, JVal>

and Accessibility =
    {
      DescribedBy: string option
      Hidden: Binding<bool> option
      Label: Binding<string> option
      LabelledBy: string option
      LiveRegion: LiveRegionKind option
      Role: Fuaran.UI.HostPrelude.AriaRole option
    }

and ButtonGroupItem<'Msg> =
    {
      Label: TextSource
      OnClick: (Fuaran.Core.Row -> Action<'Msg>) option
    }

and ColumnErased<'Msg> =
    {
      Field: string option
      // Phase 861 — per-column sort NARROWING on the bound path (Phase 860's
      // charter rule: a column flag narrows a behaviour, never widens it).
      // Absent = inherit, i.e. sortable iff the column has a `field` and the
      // grid declares `sortStateKey`. `false` opts this column out. `true` is
      // the inherited default made explicit and is an ERROR where the grid
      // declares no `sortStateKey` — a column cannot turn on a behaviour whose
      // state key does not exist. Omitted on the wire when absent.
      Sortable: bool option
      // Phase 863 — per-column EDITABILITY narrowing, the same rule on the
      // write side. Absent = inherit the grid-level `editable`. `false` makes
      // this column read-only under a grid-level `true` — the declaration
      // "read-only implied by omission" could not express. `true` is the
      // inherited default made explicit and is an ERROR where the grid is not
      // editable: a column narrows, never widens. Omitted when absent.
      Editable: bool option
      Format: CellFormat
      Kind: CellKindErased<'Msg>
      Label: string
      Value: (Fuaran.Core.Row -> Fuaran.UI.HostPrelude.CellValue) option
      Width: ColumnWidth
    }

and CompareRule =
    {
      Against: Binding<JVal>
      Op: CompareOp
    }

and ContentHash =
    {
      Algorithm: string
      Hash: string
      Strictness: HashStrictness
    }

and DateRangePair =
    {
      From: string
      To: string
    }

and DefaultSort =
    {
      Column: int
      Direction: SortDirection
    }

and DrawPoint =
    {
      X: float
      Y: float
    }

and DrawStyle =
    {
      Emphasis: Emphasis option
      Fill: Binding<string> option
      FontFamily: string option
      FontSize: float option
      MarkId: string option
      Opacity: Binding<float> option
      Rotation: float option
      Stroke: Binding<string> option
      StrokeWidth: Binding<float> option
      TextAnchor: TextAnchor option
      Tip: TextSource option
    }

and EffectClass =
    {
      Determinism: DeterminismSource
      HostEffect: HostEffect
    }

and FieldRule =
    {
      Compare: CompareRule option
      Format: TextFormat option
      MaxLength: int option
      Message: TextSource option
      MinLength: int option
      Pattern: string option
    }

and FilterSpec<'Msg> =
    {
      Kind: FormFieldKind<'Msg>
      Label: TextSource
      Name: string
    }

and FormField<'Msg> =
    {
      Id: string
      Kind: FormFieldKind<'Msg>
      Label: TextSource
      Required: bool
      Help: TextSource option
      Rule: FieldRule option
    }

and GuestChannel =
    {
      Direction: ChannelDirection
      MessageShape: string option
    }

and InvokeArg =
    {
      Addr: string
      Value: string
    }

and MapMarker =
    {
      Label: string
      Latitude: float
      Longitude: float
    }

and RangePair =
    {
      Max: float
      Min: float
    }

and SelectOption =
    {
      Label: string
      Value: string
    }

and SemanticStyle =
    {
      Direction: TextDirection
      Emphasis: Emphasis
      Role: StyleRole
      Tone: ToneVariant
      Voice: FontVoice
      Weight: StyleWeight
    }

and SrcSetEntry =
    {
      Src: Binding<string>
      Width: int
    }

and StateBehaviour<'Msg> =
    {
      OnEmpty: Node<'Msg> option
      OnError: (Fuaran.UI.HostPrelude.ErrorPayload -> Node<'Msg>) option
      OnLoading: Node<'Msg> option
    }

and StaticRows =
    {
      DefaultSort: DefaultSort option
      Headers: TextSource list
      Rows: TextSource list list
      Sortable: bool option
    }

and SwitchCase<'Msg> =
    {
      Child: Node<'Msg>
      Match: string
    }

and TabHeader =
    {
      Label: TextSource
      Icon: string option
      Disabled: Binding<bool> option
    }

and TrackEntry =
    {
      Default: bool
      Kind: TrackKind
      Label: TextSource
      Src: Binding<string>
      SrcLang: string
    }

and TransformParam =
    {
      From: Binding<JVal>
      Name: string
    }

and TreeItem =
    {
      Children: TreeItem list
      Icon: string option
      Id: string
      Label: TextSource
    }

and ViewBox =
    {
      Height: float
      MinX: float
      MinY: float
      Width: float
    }

// Display
and BadgeSpec =
    {
      Label: TextSource
      Variant: BadgeVariant
    }

// Layout
and BoxSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Heading: TextSource option
      Layout: LayoutMode
      Role: BoxRole
      // Phase 1473 — the box and everything under it stay on ONE page when the
      // rendering is paged: `break-inside: avoid`, and nothing at all on a
      // continuous medium.
      //
      // The declaration is IRREDUCIBLE in the charter's §1.2 sense and this is
      // the clearest instance of it: a host laying out pages sees boxes, and
      // cannot infer that the totals block is ONE THING that reads wrong when
      // halved. Only the tree knows its own subtrees, and no rendering carries
      // that fact back. It is the `sortStateKey` shape — a behaviour the host
      // performs, keyed by something only the document can name.
      //
      // It names nothing about the MEDIUM: no page size, no margin, no sheet
      // number, no running header or footer. Those are the host's, and the
      // ratified charter row keeps them out of the language.
      //
      // Omitted on the wire at `false`.
      KeepTogether: bool
      // Phase 1473 — the box starts at the top of a fresh page when the
      // rendering is paged. `break-before: page` on a paged medium and
      // nothing at all on a continuous one, so a screen rendering is
      // byte-for-byte the rendering it always was.
      //
      // There is deliberately NO break-AFTER counterpart: a break after this
      // box is a break before the next one, so a second spelling would buy
      // nothing and would be exactly the near-synonym pressure the vocabulary
      // charter's §3.2 confusion review exists to prevent.
      //
      // Omitted on the wire at `false`.
      BreakBefore: bool
    }

// Input
and ButtonSpec<'Msg> =
    {
      Label: TextSource
      OnClick: Action<'Msg>
      Variant: ButtonVariant
      Icon: string option
      Tooltip: (TextSource option)
      Disabled: Binding<bool> option
    }

// Display
and CalloutSpec =
    {
      Body: TextSource
      Dismissable: bool
      Tone: ToneVariant
      Heading: TextSource option
      Icon: string option
    }

// Visualisation
and ChartSpec<'Msg> =
    {
      Kind: ChartKind
      Source: Binding<Fuaran.Core.Row seq>
      Stacked: bool
      XField: string
      YFields: string list
      Title: TextSource option
      // Phase 876 — the VALUE axis's number format, reusing the existing
      // `Format` vocabulary (the Phase 819 family) rather than minting a
      // parallel formatting DU. It is a SEMANTIC declaration ("these numbers
      // are pounds / a ratio / two-decimal quantities"), which is why it is a
      // wire field where `ChartStyle` is not (D8): appearance is the host's,
      // meaning is the author's. Absent means "no declared meaning" — the
      // lowering still applies its canonical default rendering (thousands
      // separators + step-derived decimals), which is a LOWERING behaviour,
      // not wire state. Omitted on the wire when absent.
      ValueFormat: Format option
      // Phase 878 — the axis NAMES and the subtitle. Semantic wire fields for
      // the same reason `Title` is one and `ChartStyle` is not (D8): what an
      // axis is CALLED is the author's meaning; where and how it is drawn is
      // the host's appearance.
      //
      // All three are DEFAULT-ON in the sense that matters: absent `XTitle` /
      // `YTitle` fall back to the capitalised field name, so an axis is never
      // nameless. Absent is therefore the ordinary shape, not an opt-out —
      // omitted on the wire, and identical to what the author would have
      // written by hand.
      XTitle: TextSource option
      YTitle: TextSource option
      // The muted line under the visible title — the natural home for a units
      // statement ("Revenue by quarter / £m"). An explicit subtitle SUPPRESSES
      // the lowering's own display-unit slot (Phase 876): the author has said
      // it, so the machine does not repeat it.
      Subtitle: TextSource option
      // Phase 880 — WHERE the legend sits, and whether it sits anywhere at all.
      // Semantic for the same reason the titles above are (D8): the edge an
      // author wants the legend on is their meaning; the column widths and
      // pitches that realise it are the host's, in `ChartStyle`.
      //
      // Absent means "the style's default" (`ChartStyle.LegendPosition`, which
      // ships as `Right`) — NOT "no legend"; suppression is the explicit
      // `ChartLegendPosition.None`. So absence stays the ordinary shape and is
      // omitted on the wire, and an author who wants no legend says so.
      LegendPosition: ChartLegendPosition option
      // Phase 881 — whether the values are written onto the picture. Semantic
      // in the same way (D8): whether a reader is meant to read the NUMBERS or
      // the shape is the author's meaning; the type size, the offsets and the
      // fit rule that decide whether a given label actually draws are the
      // host's, in `ChartStyle`.
      //
      // Absent means `Off`, and `Off` is also the shipped default — the one
      // place this field differs from `LegendPosition`, deliberately: a legend
      // is chrome an author is opting OUT of, where a data label is ink an
      // author is opting IN to. So an absent field is byte-identical to the
      // pre-881 wire and to the pre-881 picture.
      DataLabels: ChartDataLabels option
      // Phase 882 — what the x column MEANS: discrete categories, or dates on a
      // continuous temporal scale. Semantic in the same way (D8): whether a
      // column is a set of categories or a run of dates is a fact about the
      // data; the tick ladder, the label format and the margins that realise it
      // are the host's, in `ChartStyle`.
      //
      // Absent means `Category`, which is also the shipped default, so an
      // absent field is byte-identical to the pre-882 wire AND to the pre-882
      // picture. `Temporal` is a DECLARATION the pre-emit validator grounds
      // against the column type (FUARAN097) — never an inference from the data,
      // which would make the same tree draw differently depending on where its
      // rows came from.
      XScale: ChartXScale option
      OnPointClick: (Fuaran.Core.Row -> Action<'Msg>) option
    }

// Display
and CodeBlockSpec =
    {
      Code: string
      Copyable: bool
      HighlightLines: int list
      Language: string
      LineNumbers: bool
    }

// Meta
and CustomSpec =
    {
      ModuleId: string
      ComponentId: string
      Props: Map<string, JVal>
      ContentHash: ContentHash option
      ExposedNodeIds: string list option
    }

// Visualisation
and DataGridSpec<'Msg> =
    {
      Columns: ColumnErased<'Msg> list
      Editable: bool
      RowKey: (Fuaran.Core.Row -> string) option
      RowKeyField: string option
      // Phase 818 — the grid-sort header affordance for a DATA-BOUND grid:
      // names the State key carrying the sort descriptor
      // `{"column": <index>, "direction": "asc"|"desc"}`. When set, the
      // runtime renders sortable column headers (a header click writes the
      // toggled descriptor via the SetState path) and sorts its resolved rows
      // by the state-carried descriptor before rendering. Sorting keys off the
      // clicked column's `field` — a field-less closure column renders without
      // the affordance. Omitted on the wire when absent; `staticRows`' own
      // Phase-801 sort intent is untouched.
      SortStateKey: string option
      // Phase 862 — declarative pagination, the second instance of the
      // grid-behaviour rule (Phase 860's charter): a behaviour the user drives
      // names the State key the grid both writes and reads. `pageStateKey`
      // carries the descriptor `{"page": <1-based int>}`; `pageSize` is how
      // many rows a page holds. When both are set the runtime renders a pager
      // and shows one page at a time; the pager is renderer-owned, so a
      // decorative pager (a button writing state nothing reads) cannot be
      // authored. Where the source is a `Query` whose `dependsOn` names the
      // page key, the HOST pages and the grid does not slice. Both omitted on
      // the wire when absent.
      PageSize: int option
      PageStateKey: string option
      // Phase 861 — the bound path's declared INITIAL order, reusing the same
      // `DefaultSort` record and field name `staticRows` carries (Phase 801):
      // same behaviour, same spelling. It applies when the sort state key
      // carries nothing yet; once the user has sorted, the state wins. A grid
      // may declare it with no `sortStateKey` at all — an initial order
      // without interactive re-sorting, exactly as a static table may.
      DefaultSort: DefaultSort option
      // Phase 863 — the DECLARED edit destination: the State key an edited
      // cell's whole updated rows value is committed to. Absent keeps Phase
      // 663's shipped behaviour exactly — write back to the grid's own
      // `source` when that source is a direct `Binding.State`, display-only
      // otherwise. Present, it names the destination explicitly, which is what
      // census row #27 asked for: a decoded editable grid could not say where
      // its edits land, because the only spelling was a closure erasing to
      // `"<closure>"`. Omitted on the wire when absent.
      EditStateKey: string option
      // Phase 934 — declarative row reorder. Omit-when-false, matching its nearest
      // sibling `editable`: for an affordance flag "not stated" and "explicitly off"
      // are the same state, so an option would carry a distinction the renderer
      // cannot act on. The reordered rows commit to `editStateKey` above — a reorder
      // IS a write of the whole updated rows value, so it needs no destination of
      // its own.
      Reorderable: bool
      // Phase 1123 — this grid ACCEPTS rows arriving on the named State key.
      // Paired with `TransferOutKey` below, which is the same key declared from
      // the releasing side: two grids naming one key may exchange rows, and a
      // grid declaring both does each. The drop writes
      // `{"itemId","from","to","index"}` to that key, and the renderer also
      // commits each half through the end's own `EditStateKey` destination — a
      // record nothing applies would be an affordance that moves nothing.
      //
      // The pairing is the fact only the tree knows: a host cannot infer that
      // two grids on a page are one board rather than two unrelated tables.
      // Omitted on the wire when absent.
      TransferInKey: string option
      // Phase 1123 — this grid may RELEASE rows to the named State key; see
      // `TransferInKey` above for the pair. TWO fields rather than one symmetric
      // key because the one-way ends are ordinary — an archive column that
      // accepts and never releases, a Done column that releases nothing back.
      // Omitted on the wire when absent.
      TransferOutKey: string option
      // Phase 1473 — a ROW is one thing: when the rendering is paged, no row
      // is split across the boundary, so a wrapped cell does not leave half
      // its lines on one page and half on the next. `break-inside: avoid` on
      // the row group's rows, and nothing at all on a continuous medium.
      //
      // This is the half of the print-break vocabulary NO WRAPPER reaches. A
      // `Box.keepTogether` around the grid keeps the whole grid together,
      // which is why there is no grid-level keep-together slot; but nothing
      // outside the grid knows where a row ends, so the boundary can only be
      // declared here.
      //
      // Omitted on the wire at `false`.
      KeepRowsTogether: bool
      // Phase 1473 — the column headers repeat at the top of every page the
      // grid continues onto, so a reader meeting the middle of a long grid on
      // page four still knows what each column is. The header row group is
      // projected as a TABLE HEADER GROUP, which is the one construct that
      // makes the repetition the paged formatter's own job rather than
      // script's — so it holds with no JavaScript at all.
      //
      // Irreducible for the same reason `keepRowsTogether` is: the header is
      // the grid's, and nothing outside it can name that row group.
      //
      // Omitted on the wire at `false`.
      RepeatHeader: bool
      // Phase 1125 — the grid may be TAKEN AWAY as a file. Declaring it is the
      // whole of the affordance: the renderer draws an export control and, on
      // activation, serialises the rows the client holds to RFC 4180 CSV and
      // hands them to the reader. Nothing is written back to the tree.
      //
      // The affordance charter's governing sentence decides it unamended — the
      // wire names a capability on the node that both hosts the gesture and
      // consumes its effect, and the grid is both ends: only it holds its
      // resolved rows, its columns, its declared formats and the order the
      // reader sorted them into. A button beside the grid reaches none of that.
      //
      // A plain flag rather than a state key, which is what separates it from
      // the grid-level `sortable` / `pageable` booleans the charter refuses by
      // name: those are refused because the KEY is the affordance and a flag
      // with no key behind it drives nothing. An export writes no state, so
      // there is no key for it to name.
      //
      // What it exports is what the CLIENT HOLDS — the resolved, sorted set,
      // not the page on screen — and where the host pages, that set is one
      // page. The control says which it is rather than implying a total the
      // tree cannot substantiate.
      //
      // Omitted on the wire at `false`.
      Exportable: bool
      Source: Binding<Fuaran.Core.Row seq>
      StaticRows: StaticRows option
      OnRowClick: (Fuaran.Core.Row -> Action<'Msg>) option
    }

// Layout
and DisclosureSpec<'Msg> =
    {
      Children: Node<'Msg> list
      DefaultOpen: bool
      Heading: TextSource
      OnToggle: (bool -> Action<'Msg>) option
      Open: Binding<bool>
    }

// Display
and DrawingSpec =
    {
      Description: TextSource option
      Shapes: Shape list
      Style: DrawStyle
      Title: TextSource option
      ViewBox: ViewBox
    }

// Display
and EmbedSpec =
    {
      AspectRatio: ImageAspect
      Permissions: EmbedPermission list
      Src: Binding<string>
      Title: TextSource
    }

// Meta
and ErrorBoundarySpec<'Msg> =
    {
      Child: Node<'Msg>
      Fallback: Node<'Msg>
    }

// Display
and FactSpec =
    {
      Emphasis: bool
      Help: TextSource option
      Icon: string option
      Label: TextSource
      Tone: ToneVariant
      Value: TextSource
    }

// Input
and FileUploadSpec<'Msg> =
    {
      Accept: string list
      Label: TextSource
      Multiple: bool
      OnSelect: (Fuaran.UI.HostPrelude.FileSelection list -> Action<'Msg>) option
      Disabled: Binding<bool> option
      AcceptPaste: bool
      DropTarget: bool
      Capture: CaptureSource option
    }

// Input
and FiltersSpec<'Msg> =
    {
      Items: FilterSpec<'Msg> list
    }

// Input
and FormSpec<'Msg> =
    {
      Fields: FormField<'Msg> list
      OnSubmit: Action<'Msg>
      SubmitLabel: TextSource
      Disabled: Binding<bool> option
    }

// Meta
and FragmentDeclSpec<'Msg> =
    {
      Body: Node<'Msg>
      Name: string
      Holes: HoleDecl list option
      Effect: EffectClass option
    }

// Meta
and FragmentRefSpec<'Msg> =
    {
      Name: string
      Args: Map<string, FragmentArg<'Msg>> option
    }

// Display
and HeadingSpec =
    {
      Level: int
      Text: TextSource
      Variant: HeadingVariant
    }

// Display
/// Phase 821 — the standalone icon-only display kind: a decorative or
/// labelled glyph with no Button / Image envelope. `Icon` names a
/// glyph from the existing icon vocabulary (the `data-icon` hook); `Label =
/// None` is decorative (`aria-hidden="true"`), `Some` is meaningful
/// (`role="img"` + `aria-label`).
and IconSpec =
    {
      Icon: string
      Size: IconSize
      Tone: ToneVariant
      Label: string option
    }

// Display
and ImageSpec =
    {
      Alt: TextSource
      Src: Binding<string>
      Variant: ImageVariant
      Fit: ImageFit
      AspectRatio: ImageAspect
      Loading: ImageLoading
      SrcSet: SrcSetEntry list
      Expandable: bool
      Caption: TextSource option
    }

// Display
and LabelValueRowSpec =
    {
      Emphasis: bool
      Format: CellFormat
      Label: TextSource
      Value: Binding<float>
      Help: TextSource option
    }

// Display
and LinkSpec =
    {
      Href: Binding<string>
      Label: TextSource
      Download: bool
      Rel: string option
      Target: string option
      Protection: LinkProtection option
    }

// Display
and ListSpec =
    {
      Items: TextSource list
      Ordered: bool
    }

// Visualisation
and MapSpec<'Msg> =
    {
      CentreLatitude: float
      CentreLongitude: float
      Source: Binding<MapMarker list>
      Zoom: int
      OnMarkerClick: (MapMarker -> Action<'Msg>) option
    }

// Display
and MarkdownSpec =
    {
      Text: TextSource
    }

// Display
and MathSpec =
    {
      Source: string
      Display: MathDisplay
    }

// Display
and MediaSpec =
    {
      Controls: bool
      Kind: MediaKind
      Label: TextSource
      Loop: bool
      Src: Binding<string>
      Tracks: TrackEntry list
      Transcript: TextSource option
    }

// Display
and MetricSpec =
    {
      Label: TextSource
      Value: Binding<float>
      Format: CellFormat
      Tone: ToneVariant
      Weight: StyleWeight
      Emphasis: Emphasis
      Trend: Binding<float> option
      TrendFormat: CellFormat option
      TrendPolarity: TrendPolarity
      Icon: string option
      Subtext: TextSource option
    }

// Layout
and ModalSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Dismissable: bool
      OnDismiss: Action<'Msg> option
      Open: Binding<bool>
      Heading: TextSource option
      Modality: ModalityKind
      Anchor: string option
    }

// Meta
and MountSpec<'Msg> =
    {
      Capabilities: string list
      Channel: GuestChannel
      Inputs: Map<string, FragmentArg<'Msg>> option
      OnBubble: (obj -> Action<'Msg>) option
      ScopeId: string
    }

// Display
and ProgressSpec =
    {
      Fraction: Binding<float>
      Indeterminate: bool
      Tone: ToneVariant
      Label: TextSource option
      Caveat: TextSource option
    }

// Layout
and ScrollAreaSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Orientation: ScrollOrientation
      MaxHeight: int option
      MaxWidth: int option
    }

// Input
and SelectSpec<'Msg> =
    {
      Label: TextSource
      OnChange: (string option -> Action<'Msg>) option
      OnChangeMulti: (string list -> Action<'Msg>) option
      Source: Binding<SelectOption list>
      Value: Binding<string>
      Placeholder: TextSource option
      Disabled: Binding<bool> option
      Multiple: bool option
      Values: Binding<string list> option
    }

// Display
and SkeletonSpec =
    {
      Rows: int
    }

// Display
and SparklineSpec =
    {
      Source: Binding<float list>
    }

// Layout
and SplitPanelSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Weight: float
    }

// Layout
and StepperSpec<'Msg> =
    {
      ActiveStep: Binding<int>
      Children: Node<'Msg> list
      OnSelect: (int -> Action<'Msg>) option
    }

// Layout
and SummaryListSpec<'Msg> =
    {
      Children: Node<'Msg> list
      Heading: TextSource option
    }

// Meta
and SwitchSpec<'Msg> =
    {
      Cases: SwitchCase<'Msg> list
      Default: Node<'Msg>
      // Phase 768 — the branch SELECTOR is any Binding, not only a StateStore
      // key: `on: {"$type":"Selection",…}` lets the branch follow the clicked
      // row with no writer at all, which is what closes 032/c6 (the failing
      // emissions wired a Switch to a stateKey nothing emittable could write).
      // The state form keeps its compact spelling on the wire — see the
      // encoder's collapse rule.
      On: Binding<string>
      // Fuaran-UI Phase 1122 — the timed-advance interval, in milliseconds.
      // `None` is the only spelling of "does not advance": the renderer starts
      // no timer, and a switch authored before this release is unchanged in the
      // type, on the wire and on the screen.
      //
      // A duration rather than a flag, because "advances" with no interval is
      // not renderable and two hosts inventing a period is the divergence the
      // corpus exists to prevent. Non-positive values are refused at decode
      // rather than read as "off" — absence already means off.
      AutoAdvanceMs: int option
    }

// Layout
and TabsSpec<'Msg> =
    {
      ActiveIndex: Binding<int>
      Children: Node<'Msg> list
      Orientation: Orientation
      OnSelect: (int -> Action<'Msg>) option
      OnSelectTag: (string -> Action<'Msg>) option
      TabHeaders: TabHeader list option
      TabTags: string list option
      ActiveTag: Binding<string> option
    }

// Display
and ToastSpec =
    {
      Dismissable: bool
      Message: TextSource
      Open: Binding<bool>
      Tone: ToneVariant
    }

// Display
and TreeSpec<'Msg> =
    {
      ExpandedStateKey: string option
      Items: TreeItem list
      OnSelect: (string -> Action<'Msg>) option
      SelectionStateKey: string option
    }

and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | Badge of BadgeSpec
    | Box of BoxSpec<'Msg>
    | Button of ButtonSpec<'Msg>
    | Callout of CalloutSpec
    | Chart of ChartSpec<'Msg>
    | CodeBlock of CodeBlockSpec
    | Custom of CustomSpec
    | DataGrid of DataGridSpec<'Msg>
    | Disclosure of DisclosureSpec<'Msg>
    | Drawing of DrawingSpec
    | Embed of EmbedSpec
    | ErrorBoundary of ErrorBoundarySpec<'Msg>
    | Fact of FactSpec
    | FileUpload of FileUploadSpec<'Msg>
    | Filters of FiltersSpec<'Msg>
    | Form of FormSpec<'Msg>
    | FragmentDecl of FragmentDeclSpec<'Msg>
    | FragmentRef of FragmentRefSpec<'Msg>
    | Heading of HeadingSpec
    | Icon of IconSpec
    | Image of ImageSpec
    | LabelValueRow of LabelValueRowSpec
    | Link of LinkSpec
    | List of ListSpec
    | Map of MapSpec<'Msg>
    | Markdown of MarkdownSpec
    | Math of MathSpec
    | Media of MediaSpec
    | Metric of MetricSpec
    | Modal of ModalSpec<'Msg>
    | Mount of MountSpec<'Msg>
    | Progress of ProgressSpec
    | ScrollArea of ScrollAreaSpec<'Msg>
    | Select of SelectSpec<'Msg>
    | Skeleton of SkeletonSpec
    | Sparkline of SparklineSpec
    | SplitPanel of SplitPanelSpec<'Msg>
    | Stepper of StepperSpec<'Msg>
    | SummaryList of SummaryListSpec<'Msg>
    | Switch of SwitchSpec<'Msg>
    | Tabs of TabsSpec<'Msg>
    | Toast of ToastSpec
    | Tree of TreeSpec<'Msg>

and Node<'Msg> =
    {
      Id: string
      Kind: NodeKind<'Msg>
      Accessibility: Accessibility option
      ExtraAttributes: (Map<string, string> option)
      Motion: (Motion option)
      State: StateBehaviour<'Msg> option
      Style: SemanticStyle option
      Tooltip: TextSource option
    }

/// Phase 818 — a `Binding.Transform`'s source slot. `Data` is the
/// canonical columnar / `ref` source (the pre-818 shape, byte-identical on the
/// wire). `Live` preserves a binding-shaped source (State / Selection / Query)
/// verbatim so a runtime re-evaluates the Transform with subscription
/// semantics when the binding's channel changes; `initial` is the decode-time
/// snapshot table derived from the binding's carried default data (never
/// encoded — the binding IS the wire form), which SSR / diagnostic evaluation
/// reads, byte-identical to the Phase-815 snapshot for the same input.
and [<RequireQualifiedAccess>] TransformSource =
    | Data of source: Fuaran.Core.DataSource
    | Live of binding: Binding<JVal> * initial: Fuaran.Core.DataSource

let private encBadgeVariant (v: BadgeVariant) : JVal =
    match v with
    | BadgeVariant.Neutral -> JStr "Neutral"
    | BadgeVariant.Brand -> JStr "Brand"
    | BadgeVariant.Success -> JStr "Success"
    | BadgeVariant.Warning -> JStr "Warning"
    | BadgeVariant.Critical -> JStr "Critical"
    | BadgeVariant.Info -> JStr "Info"

let private encBoxRole (v: BoxRole) : JVal =
    match v with
    | BoxRole.Dashboard -> JStr "Dashboard"
    | BoxRole.Card -> JStr "Card"
    | BoxRole.Group -> JStr "Group"
    | BoxRole.Separator -> JStr "Separator"

let private encButtonVariant (v: ButtonVariant) : JVal =
    match v with
    | ButtonVariant.Primary -> JStr "Primary"
    | ButtonVariant.Secondary -> JStr "Secondary"
    | ButtonVariant.Tertiary -> JStr "Tertiary"
    | ButtonVariant.Destructive -> JStr "Destructive"

let private encCaptureSource (v: CaptureSource) : JVal =
    match v with
    | CaptureSource.Camera -> JStr "Camera"
    | CaptureSource.Microphone -> JStr "Microphone"

let private encChannelDirection (v: ChannelDirection) : JVal =
    match v with
    | ChannelDirection.OutOnly -> JStr "OutOnly"
    | ChannelDirection.TwoWay -> JStr "TwoWay"

let private encChartDataLabels (v: ChartDataLabels) : JVal =
    match v with
    | ChartDataLabels.Off -> JStr "Off"
    | ChartDataLabels.Ends -> JStr "Ends"

let private encChartKind (v: ChartKind) : JVal =
    match v with
    | ChartKind.Line -> JStr "Line"
    | ChartKind.Bar -> JStr "Bar"
    | ChartKind.Area -> JStr "Area"
    | ChartKind.Pie -> JStr "Pie"
    | ChartKind.Scatter -> JStr "Scatter"
    | ChartKind.Heatmap -> JStr "Heatmap"

let private encChartLegendPosition (v: ChartLegendPosition) : JVal =
    match v with
    | ChartLegendPosition.Top -> JStr "Top"
    | ChartLegendPosition.Right -> JStr "Right"
    | ChartLegendPosition.Bottom -> JStr "Bottom"
    | ChartLegendPosition.None -> JStr "None"

let private encChartXScale (v: ChartXScale) : JVal =
    match v with
    | ChartXScale.Category -> JStr "Category"
    | ChartXScale.Temporal -> JStr "Temporal"

let private encCompareOp (v: CompareOp) : JVal =
    match v with
    | CompareOp.Eq -> JStr "eq"
    | CompareOp.Neq -> JStr "neq"
    | CompareOp.Lt -> JStr "lt"
    | CompareOp.Lte -> JStr "lte"
    | CompareOp.Gt -> JStr "gt"
    | CompareOp.Gte -> JStr "gte"

let private encDateStyle (v: DateStyle) : JVal =
    match v with
    | DateStyle.Short -> JStr "Short"
    | DateStyle.Medium -> JStr "Medium"
    | DateStyle.Long -> JStr "Long"
    | DateStyle.Full -> JStr "Full"

let private encDateVariant (v: DateVariant) : JVal =
    match v with
    | DateVariant.Date -> JStr "Date"
    | DateVariant.Time -> JStr "Time"
    | DateVariant.DateTime -> JStr "DateTime"

let private encDeterminismSource (v: DeterminismSource) : JVal =
    match v with
    | DeterminismSource.Deterministic -> JStr "Deterministic"
    | DeterminismSource.Clock -> JStr "Clock"
    | DeterminismSource.Random -> JStr "Random"
    | DeterminismSource.Network -> JStr "Network"

let private encDurationStyle (v: DurationStyle) : JVal =
    match v with
    | DurationStyle.Compact -> JStr "Compact"
    | DurationStyle.Clock -> JStr "Clock"
    | DurationStyle.Long -> JStr "Long"

// Phase 819 — Duration format enums.
let private encDurationUnit (v: DurationUnit) : JVal =
    match v with
    | DurationUnit.Seconds -> JStr "Seconds"
    | DurationUnit.Minutes -> JStr "Minutes"
    | DurationUnit.Hours -> JStr "Hours"

let private encEmbedPermission (v: EmbedPermission) : JVal =
    match v with
    | EmbedPermission.AllowScripts -> JStr "AllowScripts"
    | EmbedPermission.AllowSameOrigin -> JStr "AllowSameOrigin"
    | EmbedPermission.AllowForms -> JStr "AllowForms"
    | EmbedPermission.AllowFullscreen -> JStr "AllowFullscreen"

let private encEmphasis (v: Emphasis) : JVal =
    match v with
    | Emphasis.Quiet -> JStr "Quiet"
    | Emphasis.Normal -> JStr "Normal"
    | Emphasis.Loud -> JStr "Loud"

let private encFileReadEncoding (v: FileReadEncoding) : JVal =
    match v with
    | FileReadEncoding.Text -> JStr "Text"
    | FileReadEncoding.Base64 -> JStr "Base64"
    | FileReadEncoding.DataUrl -> JStr "DataUrl"

let private encFontVoice (v: FontVoice) : JVal =
    match v with
    | FontVoice.Default -> JStr "Default"
    | FontVoice.Display -> JStr "Display"
    | FontVoice.Structural -> JStr "Structural"

let private encHashStrictness (v: HashStrictness) : JVal =
    match v with
    | HashStrictness.StrictReplay -> JStr "StrictReplay"
    | HashStrictness.AdvisoryWarning -> JStr "AdvisoryWarning"
    | HashStrictness.Enforced -> JStr "Enforced"

let private encHeadingVariant (v: HeadingVariant) : JVal =
    match v with
    | HeadingVariant.Standard -> JStr "Standard"
    | HeadingVariant.Eyebrow -> JStr "Eyebrow"
    | HeadingVariant.Caption -> JStr "Caption"
    | HeadingVariant.Lead -> JStr "Lead"

let private encHostEffect (v: HostEffect) : JVal =
    match v with
    | HostEffect.Pure -> JStr "Pure"
    | HostEffect.ReadsHost -> JStr "ReadsHost"
    | HostEffect.WritesHost -> JStr "WritesHost"

// Phase 821 — Icon size class.
let private encIconSize (v: IconSize) : JVal =
    match v with
    | IconSize.Small -> JStr "Small"
    | IconSize.Medium -> JStr "Medium"
    | IconSize.Large -> JStr "Large"

let private encImageAspect (v: ImageAspect) : JVal =
    match v with
    | ImageAspect.Natural -> JStr "Natural"
    | ImageAspect.Square -> JStr "Square"
    | ImageAspect.FourThree -> JStr "FourThree"
    | ImageAspect.ThreeTwo -> JStr "ThreeTwo"
    | ImageAspect.SixteenNine -> JStr "SixteenNine"

let private encImageFit (v: ImageFit) : JVal =
    match v with
    | ImageFit.Natural -> JStr "Natural"
    | ImageFit.Cover -> JStr "Cover"
    | ImageFit.Contain -> JStr "Contain"

let private encImageLoading (v: ImageLoading) : JVal =
    match v with
    | ImageLoading.Eager -> JStr "Eager"
    | ImageLoading.Lazy -> JStr "Lazy"

let private encImageVariant (v: ImageVariant) : JVal =
    match v with
    | ImageVariant.Default -> JStr "Default"
    | ImageVariant.Avatar -> JStr "Avatar"
    | ImageVariant.Rounded -> JStr "Rounded"

let private encLinkProtection (v: LinkProtection) : JVal =
    match v with
    | LinkProtection.Email -> JStr "email"

let private encLiveRegionKind (v: LiveRegionKind) : JVal =
    match v with
    | LiveRegionKind.Polite -> JStr "polite"
    | LiveRegionKind.Assertive -> JStr "assertive"
    | LiveRegionKind.Off -> JStr "off"

let private encMathDisplay (v: MathDisplay) : JVal =
    match v with
    | MathDisplay.Inline -> JStr "Inline"
    | MathDisplay.Block -> JStr "Block"

let private encModalityKind (v: ModalityKind) : JVal =
    match v with
    | ModalityKind.Modal -> JStr "Modal"
    | ModalityKind.Popover -> JStr "Popover"

let private encMotion (v: Motion) : JVal =
    match v with
    | Motion.None -> JStr "None"
    | Motion.PulseDuringLoad -> JStr "PulseDuringLoad"
    | Motion.FadeInOnMount -> JStr "FadeInOnMount"
    | Motion.SlideInFromBelow -> JStr "SlideInFromBelow"
    | Motion.ShakeOnError -> JStr "ShakeOnError"
    | Motion.RotateOnRefresh -> JStr "RotateOnRefresh"
    | Motion.SlideInFromRight -> JStr "SlideInFromRight"
    | Motion.ExpandCollapse -> JStr "ExpandCollapse"
    | Motion.CrossFade -> JStr "CrossFade"
    | Motion.SlideBetween -> JStr "SlideBetween"

let private encOrientation (v: Orientation) : JVal =
    match v with
    | Orientation.Vertical -> JStr "Vertical"
    | Orientation.Horizontal -> JStr "Horizontal"

let private encRelativeTimeUnit (v: RelativeTimeUnit) : JVal =
    match v with
    | RelativeTimeUnit.Second -> JStr "Second"
    | RelativeTimeUnit.Minute -> JStr "Minute"
    | RelativeTimeUnit.Hour -> JStr "Hour"
    | RelativeTimeUnit.Day -> JStr "Day"
    | RelativeTimeUnit.Week -> JStr "Week"
    | RelativeTimeUnit.Month -> JStr "Month"
    | RelativeTimeUnit.Year -> JStr "Year"

let private encScrollOrientation (v: ScrollOrientation) : JVal =
    match v with
    | ScrollOrientation.Vertical -> JStr "Vertical"
    | ScrollOrientation.Horizontal -> JStr "Horizontal"
    | ScrollOrientation.Both -> JStr "Both"

let private encSortDirection (v: SortDirection) : JVal =
    match v with
    | SortDirection.Asc -> JStr "asc"
    | SortDirection.Desc -> JStr "desc"

let private encStyleRole (v: StyleRole) : JVal =
    match v with
    | StyleRole.None -> JStr "None"
    | StyleRole.Eyebrow -> JStr "Eyebrow"
    | StyleRole.Data -> JStr "Data"
    | StyleRole.Lede -> JStr "Lede"
    | StyleRole.Caption -> JStr "Caption"

let private encStyleWeight (v: StyleWeight) : JVal =
    match v with
    | StyleWeight.Compact -> JStr "Compact"
    | StyleWeight.Standard -> JStr "Standard"
    | StyleWeight.Spacious -> JStr "Spacious"

let private encTextAnchor (v: TextAnchor) : JVal =
    match v with
    | TextAnchor.Start -> JStr "Start"
    | TextAnchor.Middle -> JStr "Middle"
    | TextAnchor.End -> JStr "End"

let private encTextDirection (v: TextDirection) : JVal =
    match v with
    | TextDirection.Auto -> JStr "auto"
    | TextDirection.Ltr -> JStr "ltr"
    | TextDirection.Rtl -> JStr "rtl"

let private encTextFormat (v: TextFormat) : JVal =
    match v with
    | TextFormat.Email -> JStr "email"
    | TextFormat.Url -> JStr "url"
    | TextFormat.Tel -> JStr "tel"

let private encToneVariant (v: ToneVariant) : JVal =
    match v with
    | ToneVariant.Default -> JStr "Default"
    | ToneVariant.Subdued -> JStr "Subdued"
    | ToneVariant.Brand -> JStr "Brand"
    | ToneVariant.Success -> JStr "Success"
    | ToneVariant.Warning -> JStr "Warning"
    | ToneVariant.Critical -> JStr "Critical"
    | ToneVariant.Info -> JStr "Info"

let private encTrackKind (v: TrackKind) : JVal =
    match v with
    | TrackKind.Subtitles -> JStr "Subtitles"
    | TrackKind.Captions -> JStr "Captions"
    | TrackKind.Descriptions -> JStr "Descriptions"
    | TrackKind.Chapters -> JStr "Chapters"

let private encTrendPolarity (v: TrendPolarity) : JVal =
    match v with
    | TrendPolarity.HigherIsBetter -> JStr "HigherIsBetter"
    | TrendPolarity.LowerIsBetter -> JStr "LowerIsBetter"

// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
// `JFloat` is what keeps the emitted `JVal` renderable by the GUARDED
// `Fuaran.Core.Wire.tryRender`, which refuses a non-finite `JFloat` outright. The core
// wire model still has no non-finite float — the sentinel is a string, which it carries
// perfectly — so this widens the generated float slot's spelling, not the model.
let private encFloat (f: float) : JVal =
    if System.Double.IsNaN f then JStr "NaN"
    elif System.Double.IsPositiveInfinity f then JStr "Infinity"
    elif System.Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f

let rec private encNodeKind (k: NodeKind<'Msg>) : JVal =
    match k with
    | NodeKind.Badge s -> encBadgeSpec s
    | NodeKind.Box s -> encBoxSpec s
    | NodeKind.Button s -> encButtonSpec s
    | NodeKind.Callout s -> encCalloutSpec s
    | NodeKind.Chart s -> encChartSpec s
    | NodeKind.CodeBlock s -> encCodeBlockSpec s
    | NodeKind.Custom s -> encCustomSpec s
    | NodeKind.DataGrid s -> encDataGridSpec s
    | NodeKind.Disclosure s -> encDisclosureSpec s
    | NodeKind.Drawing s -> encDrawingSpec s
    | NodeKind.Embed s -> encEmbedSpec s
    | NodeKind.ErrorBoundary s -> encErrorBoundarySpec s
    | NodeKind.Fact s -> encFactSpec s
    | NodeKind.FileUpload s -> encFileUploadSpec s
    | NodeKind.Filters s -> encFiltersSpec s
    | NodeKind.Form s -> encFormSpec s
    | NodeKind.FragmentDecl s -> encFragmentDeclSpec s
    | NodeKind.FragmentRef s -> encFragmentRefSpec s
    | NodeKind.Heading s -> encHeadingSpec s
    | NodeKind.Icon s -> encIconSpec s
    | NodeKind.Image s -> encImageSpec s
    | NodeKind.LabelValueRow s -> encLabelValueRowSpec s
    | NodeKind.Link s -> encLinkSpec s
    | NodeKind.List s -> encListSpec s
    | NodeKind.Map s -> encMapSpec s
    | NodeKind.Markdown s -> encMarkdownSpec s
    | NodeKind.Math s -> encMathSpec s
    | NodeKind.Media s -> encMediaSpec s
    | NodeKind.Metric s -> encMetricSpec s
    | NodeKind.Modal s -> encModalSpec s
    | NodeKind.Mount s -> encMountSpec s
    | NodeKind.Progress s -> encProgressSpec s
    | NodeKind.ScrollArea s -> encScrollAreaSpec s
    | NodeKind.Select s -> encSelectSpec s
    | NodeKind.Skeleton s -> encSkeletonSpec s
    | NodeKind.Sparkline s -> encSparklineSpec s
    | NodeKind.SplitPanel s -> encSplitPanelSpec s
    | NodeKind.Stepper s -> encStepperSpec s
    | NodeKind.SummaryList s -> encSummaryListSpec s
    | NodeKind.Switch s -> encSwitchSpec s
    | NodeKind.Tabs s -> encTabsSpec s
    | NodeKind.Toast s -> encToastSpec s
    | NodeKind.Tree s -> encTreeSpec s

and private encNode (n: Node<'Msg>) : JVal =
    let kind = encNodeKind n.Kind

    JObj([ Some("id", JStr n.Id); Some("kind", kind); (n.Accessibility |> Option.map (fun v -> "accessibility", encAccessibility v)); None; None; (n.State |> Option.map (fun v -> "state", encStateBehaviour v)); (n.Style |> Option.map (fun v -> "style", encSemanticStyle v)); (n.Tooltip |> Option.map (fun v -> "tooltip", encTextSource v)) ] |> List.choose id)

and private encAction<'Msg> (v: Action<'Msg>) : JVal =
    match v with
    | Action.Chain ops -> Canon.typed "Chain" [ "ops", JArr(List.map encAction ops) ]
    | Action.WriteToClipboard text -> Canon.typed "WriteToClipboard" [ "text", encTextSource text ]
    | Action.Dispatch msg -> Canon.typed "Dispatch" ([ None ] |> List.choose id)
    | Action.Invoke (capabilityId, args) -> Canon.typed "Invoke" [ "capabilityId", JStr capabilityId; "args", JArr(List.map encInvokeArg args) ]
    | Action.ReadFileBody (fileRef, fileHandle, encoding, onRead) -> Canon.typed "ReadFileBody" ([ Some("fileRef", JStr fileRef); None; Some("encoding", encFileReadEncoding encoding); (onRead |> Option.map (fun v -> "onRead", JStr "<closure>")) ] |> List.choose id)
    | Action.Call (endpoint, onResult, into) -> Canon.typed "Call" ([ Some("endpoint", JStr endpoint); (onResult |> Option.map (fun v -> "onResult", JStr "<closure>")); (into |> Option.map (fun v -> "into", encCallResultTarget v)) ] |> List.choose id)
    | Action.Navigate route -> Canon.typed "Navigate" [ "route", JStr route ]
    | Action.CommitLocal nodeId -> Canon.typed "CommitLocal" [ "nodeId", JStr nodeId ]
    | Action.Notify (channel, payload) -> Canon.typed "Notify" [ "channel", JStr channel; "payload", id payload ]
    // Phase 818 — `value` / `valueFrom` are XOR siblings; each is emitted only
    // when present (Canon sorts keys, so the field order stays alphabetical).
    | Action.SetState (key, value, valueFrom) -> Canon.typed "SetState" ([ Some("key", JStr key); (value |> Option.map (fun v -> "value", id v)); (valueFrom |> Option.map (fun v -> "valueFrom", (encBinding id) v)) ] |> List.choose id)
    | Action.AiTool (toolName, args) -> Canon.typed "AiTool" [ "toolName", JStr toolName; "args", id args ]
    | Action.Print -> Canon.typed "Print" [  ]

and private encBinding<'T> (encT: 'T -> JVal) (v: Binding<'T>) : JVal =
    match v with
    | Binding.Static value -> Canon.typed "Static" ([ (value |> Option.map (fun v -> "value", encT v)) ] |> List.choose id)
    | Binding.Query (name, accessor, dependsOn) -> Canon.typed "Query" ([ Some("name", JStr name); None; (dependsOn |> Option.map (fun v -> "dependsOn", JArr(List.map JStr v))) ] |> List.choose id)
    | Binding.Filter (name, defaultValue) -> Canon.typed "Filter" ([ Some("name", JStr name); (defaultValue |> Option.map (fun v -> "defaultValue", encT v)) ] |> List.choose id)
    | Binding.Selection (nodeId, accessor, defaultValue, field) -> Canon.typed "Selection" ([ Some("nodeId", JStr nodeId); None; (defaultValue |> Option.map (fun v -> "defaultValue", encT v)); (field |> Option.map (fun v -> "field", JStr v)) ] |> List.choose id)
    | Binding.State (key, defaultValue) -> Canon.typed "State" ([ Some("key", JStr key); (defaultValue |> Option.map (fun v -> "defaultValue", encT v)) ] |> List.choose id)
    | Binding.Now accessor -> Canon.typed "Now" ([ None ] |> List.choose id)
    | Binding.Computed fn -> Canon.typed "Computed" [ "fn", JStr "<closure>" ]
    | Binding.Local (flushOn, format, initialFrom, onCommit, parse) -> Canon.typed "Local" ([ Some("flushOn", encLocalFlushTrigger flushOn); Some("format", JStr "<closure>"); Some("initialFrom", (encBinding encT) initialFrom); (onCommit |> Option.map (fun v -> "onCommit", JStr "<closure>")); Some("parse", JStr "<closure>") ] |> List.choose id)
    | Binding.Format (source, format, locale) -> Canon.typed "Format" [ "source", (encBinding encFloat) source; "format", encFormat format; "locale", encLocaleSource locale ]
    | Binding.I18n (key, args) -> Canon.typed "I18n" ([ Some("key", JStr key); (args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, (encBinding id) v))) v)) ] |> List.choose id)
    | Binding.Transform (source, pipeline, ``params``) -> Canon.typed "Transform" ([ Some("source", encTransformSource source); Some("pipeline", JArr(List.map Fuaran.Core.DataFrameCodec.encodeTransform pipeline)); (``params`` |> Option.map (fun v -> "params", JArr(List.map encTransformParam v))) ] |> List.choose id)
    | Binding.Invoke (capabilityId, args) -> Canon.typed "Invoke" [ "capabilityId", JStr capabilityId; "args", JArr(List.map encInvokeArg args) ]

and private encCallResultTarget (v: CallResultTarget) : JVal =
    match v with
    | CallResultTarget.State key -> Canon.typed "State" [ "key", JStr key ]
    | CallResultTarget.Query name -> Canon.typed "Query" [ "name", JStr name ]

and private encCellFormat (v: CellFormat) : JVal =
    match v with
    | CellFormat.None -> Canon.typed "None" [  ]
    | CellFormat.Number decimals -> Canon.typed "Number" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.Currency code -> Canon.typed "Currency" [ "code", JStr code ]
    | CellFormat.Percent decimals -> Canon.typed "Percent" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | CellFormat.SignificantDigits digits -> Canon.typed "SignificantDigits" [ "digits", JInt digits ]
    | CellFormat.Date format -> Canon.typed "Date" [ "format", JStr format ]
    | CellFormat.Duration (unit, style) -> Canon.typed "Duration" [ "unit", encDurationUnit unit; "style", encDurationStyle style ]
    | CellFormat.RelativeTime unit -> Canon.typed "RelativeTime" [ "unit", encRelativeTimeUnit unit ]
    | CellFormat.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encCellKindErased<'Msg> (v: CellKindErased<'Msg>) : JVal =
    match v with
    | CellKindErased.Text -> Canon.typed "Text" [  ]
    | CellKindErased.Numeric -> Canon.typed "Numeric" [  ]
    | CellKindErased.Date -> Canon.typed "Date" [  ]
    | CellKindErased.Editable onEdit -> Canon.typed "Editable" ([ (onEdit |> Option.map (fun v -> "onEdit", JStr "<closure>")) ] |> List.choose id)
    | CellKindErased.Checkbox (get, onToggle) -> Canon.typed "Checkbox" ([ Some("get", JStr "<closure>"); (onToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")) ] |> List.choose id)
    | CellKindErased.Button (label, onClick) -> Canon.typed "Button" ([ Some("label", encTextSource label); (onClick |> Option.map (fun v -> "onClick", JStr "<closure>")) ] |> List.choose id)
    | CellKindErased.ButtonGroup buttons -> Canon.typed "ButtonGroup" [ "buttons", JArr(List.map encButtonGroupItem buttons) ]
    | CellKindErased.Link (hrefFn, labelFn) -> Canon.typed "Link" [ "hrefFn", JStr "<closure>"; "labelFn", JStr "<closure>" ]
    | CellKindErased.Pill (labelFn, toneFn) -> Canon.typed "Pill" [ "labelFn", JStr "<closure>"; "toneFn", JStr "<closure>" ]
    | CellKindErased.TonedPill (field, map, ``default``) -> Canon.typed "TonedPill" ([ Some("field", JStr field); Some("map", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encToneVariant v))) map); (if ``default`` = ToneVariant.Default then None else Some("default", encToneVariant ``default``)) ] |> List.choose id)
    | CellKindErased.Progress (fractionFn, labelFn) -> Canon.typed "Progress" ([ Some("fractionFn", JStr "<closure>"); (labelFn |> Option.map (fun v -> "labelFn", JStr "<closure>")) ] |> List.choose id)
    | CellKindErased.Custom fn -> Canon.typed "Custom" [ "fn", JStr "<closure>" ]

and private encColumnWidth (v: ColumnWidth) : JVal =
    match v with
    | ColumnWidth.Auto -> Canon.typed "Auto" [  ]
    | ColumnWidth.Fixed pixels -> Canon.typed "Fixed" [ "pixels", JInt pixels ]
    | ColumnWidth.Flex weight -> Canon.typed "Flex" [ "weight", encFloat weight ]

and private encCurveCommand (v: CurveCommand) : JVal =
    match v with
    | CurveCommand.MoveTo ``to`` -> Canon.typed "MoveTo" [ "to", encDrawPoint ``to`` ]
    | CurveCommand.LineTo ``to`` -> Canon.typed "LineTo" [ "to", encDrawPoint ``to`` ]
    | CurveCommand.CubicTo (control1, control2, ``to``) -> Canon.typed "CubicTo" [ "control1", encDrawPoint control1; "control2", encDrawPoint control2; "to", encDrawPoint ``to`` ]
    | CurveCommand.QuadraticTo (control, ``to``) -> Canon.typed "QuadraticTo" [ "control", encDrawPoint control; "to", encDrawPoint ``to`` ]
    | CurveCommand.Close -> Canon.typed "Close" [  ]

and private encFormFieldKind<'Msg> (v: FormFieldKind<'Msg>) : JVal =
    match v with
    | FormFieldKind.Text (value, onChange) -> Canon.typed "Text" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Number (value, onChange) -> Canon.typed "Number" ([ (value |> Option.map (fun v -> "value", (encBinding encFloat) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Checkbox (value, onToggle) -> Canon.typed "Checkbox" ([ (value |> Option.map (fun v -> "value", (encBinding JBool) v)); (onToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Toggle (value, onToggle) -> Canon.typed "Toggle" ([ (value |> Option.map (fun v -> "value", (encBinding JBool) v)); (onToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.Choice (options, value, onChange) -> Canon.typed "Choice" ([ Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")) ] |> List.choose id)
    | FormFieldKind.TextArea (value, onChange, rows) -> Canon.typed "TextArea" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("rows", JInt rows) ] |> List.choose id)
    | FormFieldKind.RangedNumber (value, onChange, min, max, step) -> Canon.typed "RangedNumber" ([ (value |> Option.map (fun v -> "value", (encBinding encFloat) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (min |> Option.map (fun v -> "min", encFloat v)); (max |> Option.map (fun v -> "max", encFloat v)); (step |> Option.map (fun v -> "step", encFloat v)) ] |> List.choose id)
    | FormFieldKind.Range (value, onChange, min, max, step) -> Canon.typed "Range" ([ (value |> Option.map (fun v -> "value", (fun (v: Binding<RangePair>) -> match v with | Binding.Static(Some p) -> encRangePair p | __other -> encBinding encRangePair __other) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (min |> Option.map (fun v -> "min", encFloat v)); (max |> Option.map (fun v -> "max", encFloat v)); (step |> Option.map (fun v -> "step", encFloat v)) ] |> List.choose id)
    | FormFieldKind.SegmentedChoice (options, value, onChange, orientation) -> Canon.typed "SegmentedChoice" ([ Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("orientation", encOrientation orientation) ] |> List.choose id)
    | FormFieldKind.Date (value, onChange, variant, min, max, step) -> Canon.typed "Date" ([ (value |> Option.map (fun v -> "value", (encBinding JStr) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", encFloat v)) ] |> List.choose id)
    | FormFieldKind.DateRange (value, onChange, variant, min, max, step) -> Canon.typed "DateRange" ([ (value |> Option.map (fun v -> "value", (fun (v: Binding<DateRangePair>) -> match v with | Binding.Static(Some p) -> encDateRangePair p | __other -> encBinding encDateRangePair __other) v)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("variant", encDateVariant variant); (min |> Option.map (fun v -> "min", JStr v)); (max |> Option.map (fun v -> "max", JStr v)); (step |> Option.map (fun v -> "step", encFloat v)) ] |> List.choose id)
    | FormFieldKind.Combobox (allowFreeText, onChange, options, value) -> Canon.typed "Combobox" ([ (if allowFreeText = false then None else Some("allowFreeText", JBool allowFreeText)); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); Some("options", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) options); (value |> Option.map (fun v -> "value", (encBinding JStr) v)) ] |> List.choose id)
    | FormFieldKind.Rating (allowHalf, max, onChange, value) -> Canon.typed "Rating" ([ (if allowHalf = false then None else Some("allowHalf", JBool allowHalf)); Some("max", JInt max); (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (value |> Option.map (fun v -> "value", (encBinding encFloat) v)) ] |> List.choose id)
    | FormFieldKind.Color (onChange, value) -> Canon.typed "Color" ([ (onChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (value |> Option.map (fun v -> "value", (encBinding JStr) v)) ] |> List.choose id)

and private encFormat (v: Format) : JVal =
    match v with
    | Format.Number decimals -> Canon.typed "Number" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | Format.Currency isoCode -> Canon.typed "Currency" [ "isoCode", JStr isoCode ]
    | Format.Percent decimals -> Canon.typed "Percent" ([ (decimals |> Option.map (fun v -> "decimals", JInt v)) ] |> List.choose id)
    | Format.Date dateStyle -> Canon.typed "Date" [ "dateStyle", encDateStyle dateStyle ]
    | Format.RelativeTime unit -> Canon.typed "RelativeTime" [ "unit", encRelativeTimeUnit unit ]
    | Format.Duration (unit, style) -> Canon.typed "Duration" [ "unit", encDurationUnit unit; "style", encDurationStyle style ]

and private encFragmentArg<'Msg> (v: FragmentArg<'Msg>) : JVal =
    match v with
    | FragmentArg.Int value -> Canon.typed "Int" [ "value", JInt value ]
    | FragmentArg.Float value -> Canon.typed "Float" [ "value", encFloat value ]
    | FragmentArg.Bool value -> Canon.typed "Bool" [ "value", JBool value ]
    | FragmentArg.Str value -> Canon.typed "Str" [ "value", JStr value ]
    | FragmentArg.SlotArg tree -> Canon.typed "SlotArg" [ "tree", encNode tree ]

and private encHoleDecl (v: HoleDecl) : JVal =
    match v with
    | HoleDecl.Value (name, space, ``default``) -> Canon.typed "Value" ([ Some("name", JStr name); Some("space", encHoleValueSpace space); (``default`` |> Option.map (fun v -> "default", encScalar v)) ] |> List.choose id)
    | HoleDecl.Slot (name, kindConstraint) -> Canon.typed "Slot" ([ Some("name", JStr name); (kindConstraint |> Option.map (fun v -> "kindConstraint", JStr v)) ] |> List.choose id)
    | HoleDecl.Repeat (name, countSpace) -> Canon.typed "Repeat" [ "name", JStr name; "countSpace", encHoleValueSpace countSpace ]

and private encHoleValueSpace (v: HoleValueSpace) : JVal =
    match v with
    | HoleValueSpace.IntRange (min, max) -> Canon.typed "IntRange" [ "min", JInt min; "max", JInt max ]
    | HoleValueSpace.FloatRange (min, max) -> Canon.typed "FloatRange" [ "min", encFloat min; "max", encFloat max ]
    | HoleValueSpace.StringLen (minLen, maxLen) -> Canon.typed "StringLen" [ "minLen", JInt minLen; "maxLen", JInt maxLen ]
    | HoleValueSpace.Enum choices -> Canon.typed "Enum" [ "choices", JArr(List.map JStr choices) ]
    | HoleValueSpace.AnyString -> Canon.typed "AnyString" [  ]

and private encLayoutMode (v: LayoutMode) : JVal =
    match v with
    | LayoutMode.Auto -> Canon.typed "Auto" [  ]
    | LayoutMode.Flex (direction, wrap, gap) -> Canon.typed "Flex" ([ Some("direction", encOrientation direction); Some("wrap", JBool wrap); (gap |> Option.map (fun v -> "gap", JInt v)) ] |> List.choose id)
    | LayoutMode.Grid (cols, templateColumns, gap) -> Canon.typed "Grid" ([ Some("cols", JInt cols); (templateColumns |> Option.map (fun v -> "templateColumns", JStr v)); (gap |> Option.map (fun v -> "gap", JInt v)) ] |> List.choose id)
    | LayoutMode.Masonry (cols, gap) -> Canon.typed "Masonry" ([ Some("cols", JInt cols); (gap |> Option.map (fun v -> "gap", JInt v)) ] |> List.choose id)

and private encLocalFlushTrigger (v: LocalFlushTrigger) : JVal =
    match v with
    | LocalFlushTrigger.OnBlur -> Canon.typed "OnBlur" [  ]
    | LocalFlushTrigger.OnSubmit -> Canon.typed "OnSubmit" [  ]
    | LocalFlushTrigger.OnDebounce milliseconds -> Canon.typed "OnDebounce" [ "milliseconds", JInt milliseconds ]
    | LocalFlushTrigger.OnCommitAction -> Canon.typed "OnCommitAction" [  ]

and private encLocaleSource (v: LocaleSource) : JVal =
    match v with
    | LocaleSource.Ambient -> Canon.typed "Ambient" [  ]
    | LocaleSource.Explicit tag -> Canon.typed "Explicit" [ "tag", JStr tag ]

and private encMediaKind (v: MediaKind) : JVal =
    match v with
    | MediaKind.Video (autoplay, poster) -> Canon.typed "Video" ([ (if autoplay = false then None else Some("autoplay", JBool autoplay)); (poster |> Option.map (fun v -> "poster", (encBinding JStr) v)) ] |> List.choose id)
    | MediaKind.Audio -> Canon.typed "Audio" [  ]

and private encScalar (v: Scalar) : JVal =
    match v with
    | Scalar.Int value -> Canon.typed "Int" [ "value", JInt value ]
    | Scalar.Float value -> Canon.typed "Float" [ "value", encFloat value ]
    | Scalar.Bool value -> Canon.typed "Bool" [ "value", JBool value ]
    | Scalar.Str value -> Canon.typed "Str" [ "value", JStr value ]

and private encShape (v: Shape) : JVal =
    match v with
    | Shape.Group (children, style) -> Canon.typed "Group" [ "children", JArr(List.map encShape children); "style", encDrawStyle style ]
    | Shape.Rectangle (x, y, width, height, cornerRadius, style) -> Canon.typed "Rectangle" ([ Some("x", encFloat x); Some("y", encFloat y); Some("width", encFloat width); Some("height", encFloat height); (cornerRadius |> Option.map (fun v -> "cornerRadius", encFloat v)); Some("style", encDrawStyle style) ] |> List.choose id)
    | Shape.Line (x1, y1, x2, y2, style) -> Canon.typed "Line" [ "x1", encFloat x1; "y1", encFloat y1; "x2", encFloat x2; "y2", encFloat y2; "style", encDrawStyle style ]
    | Shape.Polyline (points, style) -> Canon.typed "Polyline" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Polygon (points, style) -> Canon.typed "Polygon" [ "points", JArr(List.map encDrawPoint points); "style", encDrawStyle style ]
    | Shape.Curve (commands, style) -> Canon.typed "Curve" [ "commands", JArr(List.map encCurveCommand commands); "style", encDrawStyle style ]
    | Shape.Circle (cx, cy, r, style) -> Canon.typed "Circle" [ "cx", encFloat cx; "cy", encFloat cy; "r", encFloat r; "style", encDrawStyle style ]
    | Shape.Ellipse (cx, cy, rx, ry, style) -> Canon.typed "Ellipse" [ "cx", encFloat cx; "cy", encFloat cy; "rx", encFloat rx; "ry", encFloat ry; "style", encDrawStyle style ]
    | Shape.Label (x, y, text, style) -> Canon.typed "Label" [ "x", encFloat x; "y", encFloat y; "text", encTextSource text; "style", encDrawStyle style ]

and private encTextSource (v: TextSource) : JVal =
    match v with
    | TextSource.Literal text -> JStr text
    | TextSource.Bound binding -> Canon.typed "Bound" [ "binding", (encBinding JStr) binding ]
    | TextSource.I18n (key, args) -> Canon.typed "I18n" [ "key", JStr key; "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, id v))) args ]

and private encAccessibility (s: Accessibility) : JVal =
    JObj([ (s.DescribedBy |> Option.map (fun v -> "describedBy", JStr v)); (s.Hidden |> Option.map (fun v -> "hidden", (encBinding JBool) v)); (s.Label |> Option.map (fun v -> "label", (encBinding JStr) v)); (s.LabelledBy |> Option.map (fun v -> "labelledBy", JStr v)); (s.LiveRegion |> Option.map (fun v -> "liveRegion", encLiveRegionKind v)); (s.Role |> Option.map (fun v -> "role", Fuaran.UI.HostPrelude.encAriaRole v)) ] |> List.choose id)

and private encButtonGroupItem<'Msg> (s: ButtonGroupItem<'Msg>) : JVal =
    JObj([ Some("label", encTextSource s.Label); (s.OnClick |> Option.map (fun v -> "onClick", JStr "<closure>")) ] |> List.choose id)

and private encColumnErased<'Msg> (s: ColumnErased<'Msg>) : JVal =
    JObj([ (s.Field |> Option.map (fun v -> "field", JStr v)); (s.Sortable |> Option.map (fun v -> "sortable", JBool v)); (s.Editable |> Option.map (fun v -> "editable", JBool v)); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); Some("kind", encCellKindErased s.Kind); Some("label", JStr s.Label); (s.Value |> Option.map (fun v -> "value", JStr "<closure>")); (match s.Width with | ColumnWidth.Auto -> None | _ -> Some("width", encColumnWidth s.Width)) ] |> List.choose id)

and private encCompareRule (s: CompareRule) : JVal =
    JObj([ Some("against", (encBinding id) s.Against); Some("op", encCompareOp s.Op) ] |> List.choose id)

and private encContentHash (s: ContentHash) : JVal =
    JObj([ Some("algorithm", JStr s.Algorithm); Some("hash", JStr s.Hash); Some("strictness", encHashStrictness s.Strictness) ] |> List.choose id)

and private encDateRangePair (s: DateRangePair) : JVal =
    JObj([ Some("from", JStr s.From); Some("to", JStr s.To) ] |> List.choose id)

and private encDefaultSort (s: DefaultSort) : JVal =
    JObj([ Some("column", JInt s.Column); Some("direction", encSortDirection s.Direction) ] |> List.choose id)

and private encDrawPoint (s: DrawPoint) : JVal =
    JObj([ Some("x", encFloat s.X); Some("y", encFloat s.Y) ] |> List.choose id)

and private encDrawStyle (s: DrawStyle) : JVal =
    JObj([ (s.Emphasis |> Option.map (fun v -> "emphasis", encEmphasis v)); (s.Fill |> Option.map (fun v -> "fill", (encBinding JStr) v)); (s.FontFamily |> Option.map (fun v -> "fontFamily", JStr v)); (s.FontSize |> Option.map (fun v -> "fontSize", encFloat v)); (s.MarkId |> Option.map (fun v -> "markId", JStr v)); (s.Opacity |> Option.map (fun v -> "opacity", (encBinding encFloat) v)); (s.Rotation |> Option.map (fun v -> "rotation", encFloat v)); (s.Stroke |> Option.map (fun v -> "stroke", (encBinding JStr) v)); (s.StrokeWidth |> Option.map (fun v -> "strokeWidth", (encBinding encFloat) v)); (s.TextAnchor |> Option.map (fun v -> "textAnchor", encTextAnchor v)); (s.Tip |> Option.map (fun v -> "tip", encTextSource v)) ] |> List.choose id)

and private encEffectClass (s: EffectClass) : JVal =
    JObj([ Some("determinism", encDeterminismSource s.Determinism); Some("hostEffect", encHostEffect s.HostEffect) ] |> List.choose id)

and private encFieldRule (s: FieldRule) : JVal =
    JObj([ (s.Compare |> Option.map (fun v -> "compare", encCompareRule v)); (s.Format |> Option.map (fun v -> "format", encTextFormat v)); (s.MaxLength |> Option.map (fun v -> "maxLength", JInt v)); (s.Message |> Option.map (fun v -> "message", encTextSource v)); (s.MinLength |> Option.map (fun v -> "minLength", JInt v)); (s.Pattern |> Option.map (fun v -> "pattern", JStr v)) ] |> List.choose id)

and private encFilterSpec<'Msg> (s: FilterSpec<'Msg>) : JVal =
    JObj([ Some("kind", encFormFieldKind s.Kind); Some("label", encTextSource s.Label); Some("name", JStr s.Name) ] |> List.choose id)

and private encFormField<'Msg> (s: FormField<'Msg>) : JVal =
    JObj([ Some("id", JStr s.Id); Some("kind", encFormFieldKind s.Kind); Some("label", encTextSource s.Label); Some("required", JBool s.Required); (s.Help |> Option.map (fun v -> "help", encTextSource v)); (s.Rule |> Option.map (fun v -> "rule", encFieldRule v)) ] |> List.choose id)

and private encGuestChannel (s: GuestChannel) : JVal =
    JObj([ Some("direction", encChannelDirection s.Direction); (s.MessageShape |> Option.map (fun v -> "messageShape", JStr v)) ] |> List.choose id)

and private encInvokeArg (s: InvokeArg) : JVal =
    JObj([ Some("addr", JStr s.Addr); Some("value", JStr s.Value) ] |> List.choose id)

and private encMapMarker (s: MapMarker) : JVal =
    JObj([ Some("label", JStr s.Label); Some("latitude", encFloat s.Latitude); Some("longitude", encFloat s.Longitude) ] |> List.choose id)

and private encRangePair (s: RangePair) : JVal =
    JObj([ Some("max", encFloat s.Max); Some("min", encFloat s.Min) ] |> List.choose id)

and private encSelectOption (s: SelectOption) : JVal =
    JObj([ Some("label", JStr s.Label); Some("value", JStr s.Value) ] |> List.choose id)

and private encSemanticStyle (s: SemanticStyle) : JVal =
    JObj([ (if s.Direction = TextDirection.Auto then None else Some("direction", encTextDirection s.Direction)); (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); (if s.Role = StyleRole.None then None else Some("role", encStyleRole s.Role)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (if s.Voice = FontVoice.Default then None else Some("voice", encFontVoice s.Voice)); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)) ] |> List.choose id)

and private encSrcSetEntry (s: SrcSetEntry) : JVal =
    JObj([ Some("src", (encBinding JStr) s.Src); Some("width", JInt s.Width) ] |> List.choose id)

and private encStateBehaviour<'Msg> (s: StateBehaviour<'Msg>) : JVal =
    JObj([ (s.OnEmpty |> Option.map (fun v -> "onEmpty", encNode v)); (s.OnError |> Option.map (fun v -> "onError", JStr "<closure>")); (s.OnLoading |> Option.map (fun v -> "onLoading", encNode v)) ] |> List.choose id)

and private encStaticRows (s: StaticRows) : JVal =
    JObj([ (s.DefaultSort |> Option.map (fun v -> "defaultSort", encDefaultSort v)); Some("headers", JArr(List.map encTextSource s.Headers)); Some("rows", JArr(List.map (fun __xs -> JArr(List.map encTextSource __xs)) s.Rows)); (s.Sortable |> Option.map (fun v -> "sortable", JBool v)) ] |> List.choose id)

and private encSwitchCase<'Msg> (s: SwitchCase<'Msg>) : JVal =
    JObj([ Some("child", encNode s.Child); Some("match", JStr s.Match) ] |> List.choose id)

and private encTabHeader (s: TabHeader) : JVal =
    JObj([ Some("label", encTextSource s.Label); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encTrackEntry (s: TrackEntry) : JVal =
    JObj([ (if s.Default = false then None else Some("default", JBool s.Default)); Some("kind", encTrackKind s.Kind); Some("label", encTextSource s.Label); Some("src", (encBinding JStr) s.Src); Some("srcLang", JStr s.SrcLang) ] |> List.choose id)

and private encTransformParam (s: TransformParam) : JVal =
    JObj([ Some("from", (encBinding id) s.From); Some("name", JStr s.Name) ] |> List.choose id)

and private encTreeItem (s: TreeItem) : JVal =
    JObj([ (if List.isEmpty s.Children then None else Some("children", JArr(List.map encTreeItem s.Children))); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("id", JStr s.Id); Some("label", encTextSource s.Label) ] |> List.choose id)

and private encViewBox (s: ViewBox) : JVal =
    JObj([ Some("height", encFloat s.Height); Some("minX", encFloat s.MinX); Some("minY", encFloat s.MinY); Some("width", encFloat s.Width) ] |> List.choose id)

and private encBadgeSpec (s: BadgeSpec) : JVal =
    Canon.typed "Badge" ([ Some("label", encTextSource s.Label); Some("variant", encBadgeVariant s.Variant) ] |> List.choose id)

and private encBoxSpec<'Msg> (s: BoxSpec<'Msg>) : JVal =
    Canon.typed "Box" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); Some("layout", encLayoutMode s.Layout); Some("role", encBoxRole s.Role); (if s.KeepTogether = false then None else Some("keepTogether", JBool s.KeepTogether)); (if s.BreakBefore = false then None else Some("breakBefore", JBool s.BreakBefore)) ] |> List.choose id)

and private encButtonSpec<'Msg> (s: ButtonSpec<'Msg>) : JVal =
    Canon.typed "Button" ([ Some("label", encTextSource s.Label); Some("onClick", encAction s.OnClick); Some("variant", encButtonVariant s.Variant); (s.Icon |> Option.map (fun v -> "icon", JStr v)); None; (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encCalloutSpec (s: CalloutSpec) : JVal =
    Canon.typed "Callout" ([ Some("body", encTextSource s.Body); (if s.Dismissable = false then None else Some("dismissable", JBool s.Dismissable)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)) ] |> List.choose id)

and private encChartSpec<'Msg> (s: ChartSpec<'Msg>) : JVal =
    Canon.typed "Chart" ([ Some("kind", encChartKind s.Kind); Some("source", (encBinding Fuaran.Core.RowCodec.encodeRows) s.Source); Some("stacked", JBool s.Stacked); Some("xField", JStr s.XField); Some("yFields", JArr(List.map JStr s.YFields)); (s.Title |> Option.map (fun v -> "title", encTextSource v)); (s.ValueFormat |> Option.map (fun v -> "valueFormat", encFormat v)); (s.XTitle |> Option.map (fun v -> "xTitle", encTextSource v)); (s.YTitle |> Option.map (fun v -> "yTitle", encTextSource v)); (s.Subtitle |> Option.map (fun v -> "subtitle", encTextSource v)); (s.LegendPosition |> Option.map (fun v -> "legendPosition", encChartLegendPosition v)); (s.DataLabels |> Option.map (fun v -> "dataLabels", encChartDataLabels v)); (s.XScale |> Option.map (fun v -> "xScale", encChartXScale v)); (s.OnPointClick |> Option.map (fun v -> "onPointClick", JStr "<closure>")) ] |> List.choose id)

and private encCodeBlockSpec (s: CodeBlockSpec) : JVal =
    Canon.typed "CodeBlock" ([ Some("code", JStr s.Code); Some("copyable", JBool s.Copyable); Some("highlightLines", JArr(List.map JInt s.HighlightLines)); Some("language", JStr s.Language); Some("lineNumbers", JBool s.LineNumbers) ] |> List.choose id)

and private encCustomSpec (s: CustomSpec) : JVal =
    Canon.typed "Custom" ([ Some("moduleId", JStr s.ModuleId); Some("componentId", JStr s.ComponentId); Some("props", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, id v))) s.Props); (s.ContentHash |> Option.map (fun v -> "contentHash", encContentHash v)); (s.ExposedNodeIds |> Option.map (fun v -> "exposedNodeIds", JArr(List.map JStr v))) ] |> List.choose id)

and private encDataGridSpec<'Msg> (s: DataGridSpec<'Msg>) : JVal =
    Canon.typed "DataGrid" ([ Some("columns", JArr(List.map encColumnErased s.Columns)); (if s.Editable = false then None else Some("editable", JBool s.Editable)); (s.RowKey |> Option.map (fun v -> "rowKey", JStr "<closure>")); (s.RowKeyField |> Option.map (fun v -> "rowKeyField", JStr v)); (s.SortStateKey |> Option.map (fun v -> "sortStateKey", JStr v)); (s.PageSize |> Option.map (fun v -> "pageSize", JInt v)); (s.PageStateKey |> Option.map (fun v -> "pageStateKey", JStr v)); (s.DefaultSort |> Option.map (fun v -> "defaultSort", encDefaultSort v)); (s.EditStateKey |> Option.map (fun v -> "editStateKey", JStr v)); (if s.Reorderable = false then None else Some("reorderable", JBool s.Reorderable)); (s.TransferInKey |> Option.map (fun v -> "transferInKey", JStr v)); (s.TransferOutKey |> Option.map (fun v -> "transferOutKey", JStr v)); (if s.KeepRowsTogether = false then None else Some("keepRowsTogether", JBool s.KeepRowsTogether)); (if s.RepeatHeader = false then None else Some("repeatHeader", JBool s.RepeatHeader)); (if s.Exportable = false then None else Some("exportable", JBool s.Exportable)); Some("source", (encBinding Fuaran.Core.RowCodec.encodeRows) s.Source); (s.StaticRows |> Option.map (fun v -> "staticRows", encStaticRows v)); (s.OnRowClick |> Option.map (fun v -> "onRowClick", JStr "<closure>")) ] |> List.choose id)

and private encDisclosureSpec<'Msg> (s: DisclosureSpec<'Msg>) : JVal =
    Canon.typed "Disclosure" ([ Some("children", JArr(List.map encNode s.Children)); Some("defaultOpen", JBool s.DefaultOpen); Some("heading", encTextSource s.Heading); (s.OnToggle |> Option.map (fun v -> "onToggle", JStr "<closure>")); Some("open", (encBinding JBool) s.Open) ] |> List.choose id)

and private encDrawingSpec (s: DrawingSpec) : JVal =
    Canon.typed "Drawing" ([ (s.Description |> Option.map (fun v -> "description", encTextSource v)); Some("shapes", JArr(List.map encShape s.Shapes)); Some("style", encDrawStyle s.Style); (s.Title |> Option.map (fun v -> "title", encTextSource v)); Some("viewBox", encViewBox s.ViewBox) ] |> List.choose id)

and private encEmbedSpec (s: EmbedSpec) : JVal =
    Canon.typed "Embed" ([ (if s.AspectRatio = ImageAspect.Natural then None else Some("aspectRatio", encImageAspect s.AspectRatio)); (if List.isEmpty s.Permissions then None else Some("permissions", JArr(List.map encEmbedPermission s.Permissions))); Some("src", (encBinding JStr) s.Src); Some("title", encTextSource s.Title) ] |> List.choose id)

and private encErrorBoundarySpec<'Msg> (s: ErrorBoundarySpec<'Msg>) : JVal =
    Canon.typed "ErrorBoundary" ([ Some("child", encNode s.Child); Some("fallback", encNode s.Fallback) ] |> List.choose id)

and private encFactSpec (s: FactSpec) : JVal =
    Canon.typed "Fact" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (s.Help |> Option.map (fun v -> "help", encTextSource v)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); Some("label", encTextSource s.Label); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); Some("value", encTextSource s.Value) ] |> List.choose id)

and private encFileUploadSpec<'Msg> (s: FileUploadSpec<'Msg>) : JVal =
    Canon.typed "FileUpload" ([ Some("accept", JArr(List.map JStr s.Accept)); Some("label", encTextSource s.Label); Some("multiple", JBool s.Multiple); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (if s.AcceptPaste = false then None else Some("acceptPaste", JBool s.AcceptPaste)); (if s.DropTarget = false then None else Some("dropTarget", JBool s.DropTarget)); (s.Capture |> Option.map (fun v -> "capture", encCaptureSource v)) ] |> List.choose id)

and private encFiltersSpec<'Msg> (s: FiltersSpec<'Msg>) : JVal =
    Canon.typed "Filters" ([ Some("items", JArr(List.map encFilterSpec s.Items)) ] |> List.choose id)

and private encFormSpec<'Msg> (s: FormSpec<'Msg>) : JVal =
    Canon.typed "Form" ([ Some("fields", JArr(List.map encFormField s.Fields)); Some("onSubmit", encAction s.OnSubmit); Some("submitLabel", encTextSource s.SubmitLabel); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)) ] |> List.choose id)

and private encFragmentDeclSpec<'Msg> (s: FragmentDeclSpec<'Msg>) : JVal =
    Canon.typed "FragmentDecl" ([ Some("body", encNode s.Body); Some("name", JStr s.Name); (s.Holes |> Option.map (fun v -> "holes", JArr(List.map encHoleDecl v))); (s.Effect |> Option.map (fun v -> "effect", encEffectClass v)) ] |> List.choose id)

and private encFragmentRefSpec<'Msg> (s: FragmentRefSpec<'Msg>) : JVal =
    Canon.typed "FragmentRef" ([ Some("name", JStr s.Name); (s.Args |> Option.map (fun v -> "args", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)) ] |> List.choose id)

and private encHeadingSpec (s: HeadingSpec) : JVal =
    Canon.typed "Heading" ([ Some("level", JInt s.Level); Some("text", encTextSource s.Text); Some("variant", encHeadingVariant s.Variant) ] |> List.choose id)

// Phase 821 — Icon display kind.
// `size` omitted-when-`Medium`, `tone` omitted-when-`Default`, `label`
// omitted-when-`None` (decorative).
and private encIconSpec (s: IconSpec) : JVal =
    Canon.typed "Icon" ([ Some("icon", JStr s.Icon); (if s.Size = IconSize.Medium then None else Some("size", encIconSize s.Size)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Label |> Option.map (fun v -> "label", JStr v)) ] |> List.choose id)

and private encImageSpec (s: ImageSpec) : JVal =
    Canon.typed "Image" ([ Some("alt", encTextSource s.Alt); Some("src", (encBinding JStr) s.Src); Some("variant", encImageVariant s.Variant); (if s.Fit = ImageFit.Natural then None else Some("fit", encImageFit s.Fit)); (if s.AspectRatio = ImageAspect.Natural then None else Some("aspectRatio", encImageAspect s.AspectRatio)); (if s.Loading = ImageLoading.Eager then None else Some("loading", encImageLoading s.Loading)); (if List.isEmpty s.SrcSet then None else Some("srcSet", JArr(List.map encSrcSetEntry s.SrcSet))); (if s.Expandable = false then None else Some("expandable", JBool s.Expandable)); (s.Caption |> Option.map (fun v -> "caption", encTextSource v)) ] |> List.choose id)

and private encLabelValueRowSpec (s: LabelValueRowSpec) : JVal =
    Canon.typed "LabelValueRow" ([ (if s.Emphasis = false then None else Some("emphasis", JBool s.Emphasis)); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); Some("label", encTextSource s.Label); Some("value", (encBinding encFloat) s.Value); (s.Help |> Option.map (fun v -> "help", encTextSource v)) ] |> List.choose id)

and private encLinkSpec (s: LinkSpec) : JVal =
    Canon.typed "Link" ([ Some("href", (encBinding JStr) s.Href); Some("label", encTextSource s.Label); Some("download", JBool s.Download); (s.Rel |> Option.map (fun v -> "rel", JStr v)); (s.Target |> Option.map (fun v -> "target", JStr v)); (s.Protection |> Option.map (fun v -> "protection", encLinkProtection v)) ] |> List.choose id)

and private encListSpec (s: ListSpec) : JVal =
    Canon.typed "List" ([ Some("items", JArr(List.map encTextSource s.Items)); Some("ordered", JBool s.Ordered) ] |> List.choose id)

and private encMapSpec<'Msg> (s: MapSpec<'Msg>) : JVal =
    Canon.typed "Map" ([ Some("centreLatitude", encFloat s.CentreLatitude); Some("centreLongitude", encFloat s.CentreLongitude); Some("source", (encBinding (fun __xs -> JArr(List.map encMapMarker __xs))) s.Source); Some("zoom", JInt s.Zoom); (s.OnMarkerClick |> Option.map (fun v -> "onMarkerClick", JStr "<closure>")) ] |> List.choose id)

and private encMarkdownSpec (s: MarkdownSpec) : JVal =
    Canon.typed "Markdown" ([ Some("text", encTextSource s.Text) ] |> List.choose id)

and private encMathSpec (s: MathSpec) : JVal =
    Canon.typed "Math" ([ Some("source", JStr s.Source); Some("display", encMathDisplay s.Display) ] |> List.choose id)

and private encMediaSpec (s: MediaSpec) : JVal =
    Canon.typed "Media" ([ (if s.Controls = true then None else Some("controls", JBool s.Controls)); Some("kind", encMediaKind s.Kind); Some("label", encTextSource s.Label); (if s.Loop = false then None else Some("loop", JBool s.Loop)); Some("src", (encBinding JStr) s.Src); (if List.isEmpty s.Tracks then None else Some("tracks", JArr(List.map encTrackEntry s.Tracks))); (s.Transcript |> Option.map (fun v -> "transcript", encTextSource v)) ] |> List.choose id)

and private encMetricSpec (s: MetricSpec) : JVal =
    Canon.typed "Metric" ([ Some("label", encTextSource s.Label); Some("value", (encBinding encFloat) s.Value); (match s.Format with | CellFormat.None -> None | _ -> Some("format", encCellFormat s.Format)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (if s.Weight = StyleWeight.Standard then None else Some("weight", encStyleWeight s.Weight)); (if s.Emphasis = Emphasis.Normal then None else Some("emphasis", encEmphasis s.Emphasis)); (s.Trend |> Option.map (fun v -> "trend", (encBinding encFloat) v)); (s.TrendFormat |> Option.map (fun v -> "trendFormat", encCellFormat v)); (if s.TrendPolarity = TrendPolarity.HigherIsBetter then None else Some("trendPolarity", encTrendPolarity s.TrendPolarity)); (s.Icon |> Option.map (fun v -> "icon", JStr v)); (s.Subtext |> Option.map (fun v -> "subtext", encTextSource v)) ] |> List.choose id)

and private encModalSpec<'Msg> (s: ModalSpec<'Msg>) : JVal =
    Canon.typed "Modal" ([ Some("children", JArr(List.map encNode s.Children)); Some("dismissable", JBool s.Dismissable); (s.OnDismiss |> Option.map (fun v -> "onDismiss", encAction v)); Some("open", (encBinding JBool) s.Open); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)); (if s.Modality = ModalityKind.Modal then None else Some("modality", encModalityKind s.Modality)); (s.Anchor |> Option.map (fun v -> "anchor", JStr v)) ] |> List.choose id)

and private encMountSpec<'Msg> (s: MountSpec<'Msg>) : JVal =
    Canon.typed "Mount" ([ Some("capabilities", JArr(List.map JStr s.Capabilities)); Some("channel", encGuestChannel s.Channel); (s.Inputs |> Option.map (fun v -> "inputs", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encFragmentArg v))) v)); (s.OnBubble |> Option.map (fun v -> "onBubble", JStr "<closure>")); Some("scopeId", JStr s.ScopeId) ] |> List.choose id)

and private encProgressSpec (s: ProgressSpec) : JVal =
    Canon.typed "Progress" ([ Some("fraction", (encBinding encFloat) s.Fraction); (if s.Indeterminate = false then None else Some("indeterminate", JBool s.Indeterminate)); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)); (s.Label |> Option.map (fun v -> "label", encTextSource v)); (s.Caveat |> Option.map (fun v -> "caveat", encTextSource v)) ] |> List.choose id)

and private encScrollAreaSpec<'Msg> (s: ScrollAreaSpec<'Msg>) : JVal =
    Canon.typed "ScrollArea" ([ Some("children", JArr(List.map encNode s.Children)); Some("orientation", encScrollOrientation s.Orientation); (s.MaxHeight |> Option.map (fun v -> "maxHeight", JInt v)); (s.MaxWidth |> Option.map (fun v -> "maxWidth", JInt v)) ] |> List.choose id)

and private encSelectSpec<'Msg> (s: SelectSpec<'Msg>) : JVal =
    Canon.typed "Select" ([ Some("label", encTextSource s.Label); (s.OnChange |> Option.map (fun v -> "onChange", JStr "<closure>")); (s.OnChangeMulti |> Option.map (fun v -> "onChangeMulti", JStr "<closure>")); Some("source", (encBinding (fun __xs -> JArr(List.map encSelectOption __xs))) s.Source); Some("value", (encBinding JStr) s.Value); (s.Placeholder |> Option.map (fun v -> "placeholder", encTextSource v)); (s.Disabled |> Option.map (fun v -> "disabled", (encBinding JBool) v)); (s.Multiple |> Option.map (fun v -> "multiple", JBool v)); (s.Values |> Option.map (fun v -> "values", (encBinding (fun __xs -> JArr(List.map JStr __xs))) v)) ] |> List.choose id)

and private encSkeletonSpec (s: SkeletonSpec) : JVal =
    Canon.typed "Skeleton" ([ Some("rows", JInt s.Rows) ] |> List.choose id)

and private encSparklineSpec (s: SparklineSpec) : JVal =
    Canon.typed "Sparkline" ([ Some("source", (encBinding (fun __xs -> JArr(List.map encFloat __xs))) s.Source) ] |> List.choose id)

and private encSplitPanelSpec<'Msg> (s: SplitPanelSpec<'Msg>) : JVal =
    Canon.typed "SplitPanel" ([ Some("children", JArr(List.map encNode s.Children)); Some("weight", encFloat s.Weight) ] |> List.choose id)

and private encStepperSpec<'Msg> (s: StepperSpec<'Msg>) : JVal =
    Canon.typed "Stepper" ([ Some("activeStep", (encBinding JInt) s.ActiveStep); Some("children", JArr(List.map encNode s.Children)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")) ] |> List.choose id)

and private encSummaryListSpec<'Msg> (s: SummaryListSpec<'Msg>) : JVal =
    Canon.typed "SummaryList" ([ Some("children", JArr(List.map encNode s.Children)); (s.Heading |> Option.map (fun v -> "heading", encTextSource v)) ] |> List.choose id)

and private encSwitchSpec<'Msg> (s: SwitchSpec<'Msg>) : JVal =
    Canon.typed "Switch" ([ Some("cases", JArr(List.map encSwitchCase s.Cases)); Some("default", encNode s.Default); (match s.On with | Binding.State (key, None) -> Some("stateKey", JStr key) | on -> Some("on", (encBinding JStr) on)); (s.AutoAdvanceMs |> Option.map (fun v -> "autoAdvanceMs", JInt v)) ] |> List.choose id)

and private encTabsSpec<'Msg> (s: TabsSpec<'Msg>) : JVal =
    Canon.typed "Tabs" ([ Some("activeIndex", (encBinding JInt) s.ActiveIndex); Some("children", JArr(List.map encNode s.Children)); (if s.Orientation = Orientation.Horizontal then None else Some("orientation", encOrientation s.Orientation)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.OnSelectTag |> Option.map (fun v -> "onSelectTag", JStr "<closure>")); (s.TabHeaders |> Option.map (fun v -> "tabHeaders", JArr(List.map encTabHeader v))); (s.TabTags |> Option.map (fun v -> "tabTags", JArr(List.map JStr v))); (s.ActiveTag |> Option.map (fun v -> "activeTag", (encBinding JStr) v)) ] |> List.choose id)

and private encToastSpec (s: ToastSpec) : JVal =
    Canon.typed "Toast" ([ (if s.Dismissable = true then None else Some("dismissable", JBool s.Dismissable)); Some("message", encTextSource s.Message); Some("open", (encBinding JBool) s.Open); (if s.Tone = ToneVariant.Default then None else Some("tone", encToneVariant s.Tone)) ] |> List.choose id)

and private encTreeSpec<'Msg> (s: TreeSpec<'Msg>) : JVal =
    Canon.typed "Tree" ([ (s.ExpandedStateKey |> Option.map (fun v -> "expandedStateKey", JStr v)); Some("items", JArr(List.map encTreeItem s.Items)); (s.OnSelect |> Option.map (fun v -> "onSelect", JStr "<closure>")); (s.SelectionStateKey |> Option.map (fun v -> "selectionStateKey", JStr v)) ] |> List.choose id)

// Phase 818 — a `Data` source keeps the Core columnar encoding byte-identical; a
// `Live` source re-encodes the preserved binding itself (one wire dialect — the
// State/Selection/Query-shaped source round-trips byte-for-byte; the derived
// `initial` snapshot is never encoded).
and private encTransformSource (s: TransformSource) : JVal =
    match s with
    | TransformSource.Data ds -> Fuaran.Core.ColumnCodec.encodeJson ds
    | TransformSource.Live (b, _) -> (encBinding id) b

let encodeNode (n: Node<'Msg>) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node<'Msg>) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind<'Msg>) : JVal = encNodeKind k

let encodeStateBehaviourJson (s: StateBehaviour<'Msg>) : JVal = encStateBehaviour s

let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s

// Phase 818 — JVal-level accessor for a data-shaped Action (the
// `encodeNodeKindJson` precedent): the server resume script re-encodes a
// `SetState` whose payload is a `valueFrom` Binding through the canonical
// encoder rather than growing a second hand-rolled binding encoder.
let encodeActionJson (a: Action<'Msg>) : JVal = encAction a

// Phase 1126 — the same accessor one level down, for the two hand-written
// codecs that encode a `TextSource` slot OUTSIDE a node: the op-stream's
// canonical-JSON action encoder and the action-log's payload projection. Both
// grew a `JStr` where the clipboard payload used to be a bare string; neither
// should grow a second hand-rolled `TextSource` encoder to replace it, because
// `TextSource.Literal`'s bare-string canonical form (§3.6) is exactly the kind
// of rule that two copies drift on.
let encodeTextSourceJson (t: TextSource) : JVal = encTextSource t

let private dObj (j: JVal) : Result<(string * JVal) list, string> =
    match j with
    | JObj fs -> Ok fs
    | _ -> Error "expected an object"

let private dTag (fs: (string * JVal) list) : Result<string, string> =
    match fs |> List.tryFind (fun (k, _) -> k = "$type") with
    | Some(_, JStr t) -> Ok t
    | _ -> Error "missing or non-string $type"

let private dStr (j: JVal) : Result<string, string> =
    match j with
    | JStr s -> Ok s
    | _ -> Error "expected a string"

let private dInt (j: JVal) : Result<int, string> =
    match j with
    | JInt i -> Ok i
    | _ -> Error "expected an int"

let private dBool (j: JVal) : Result<bool, string> =
    match j with
    | JBool b -> Ok b
    | _ -> Error "expected a bool"

// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, string> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> Error "expected a number"

let private dUnit (_: JVal) : Result<unit, string> = Ok()

// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, string> = Ok j

let private dList (dec: JVal -> Result<'T, string>) (j: JVal) : Result<'T list, string> =
    match j with
    | JArr xs ->
        (Ok [], xs)
        ||> List.fold (fun acc x ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec x |> Result.map (fun v -> v :: items))
        |> Result.map List.rev
    | _ -> Error "expected an array"

let private dMap (dec: JVal -> Result<'T, string>) (j: JVal) : Result<Map<string, 'T>, string> =
    match j with
    | JObj fs ->
        (Ok [], fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items -> dec v |> Result.map (fun d -> (k, d) :: items))
        |> Result.map (List.rev >> Map.ofList)
    | _ -> Error "expected an object"

let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Error("missing required field '" + name + "'")

let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T option, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> Result.map Some
    | None -> Ok None

let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) (dflt: 'T) : Result<'T, string> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v
    | None -> Ok dflt

// An optional closure / opaque field: the value is a sentinel carrying nothing,
// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.
let private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, string> =
    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))

let private decBadgeVariant (j: JVal) : Result<BadgeVariant, string> =
    match j with
    | JStr "Neutral" -> Ok BadgeVariant.Neutral
    | JStr "Brand" -> Ok BadgeVariant.Brand
    | JStr "Success" -> Ok BadgeVariant.Success
    | JStr "Warning" -> Ok BadgeVariant.Warning
    | JStr "Critical" -> Ok BadgeVariant.Critical
    | JStr "Info" -> Ok BadgeVariant.Info
    | _ -> Error "not a BadgeVariant"

let private decBoxRole (j: JVal) : Result<BoxRole, string> =
    match j with
    | JStr "Dashboard" -> Ok BoxRole.Dashboard
    | JStr "Card" -> Ok BoxRole.Card
    | JStr "Group" -> Ok BoxRole.Group
    | JStr "Separator" -> Ok BoxRole.Separator
    | _ -> Error "not a BoxRole"

let private decButtonVariant (j: JVal) : Result<ButtonVariant, string> =
    match j with
    | JStr "Primary" -> Ok ButtonVariant.Primary
    | JStr "Secondary" -> Ok ButtonVariant.Secondary
    | JStr "Tertiary" -> Ok ButtonVariant.Tertiary
    | JStr "Destructive" -> Ok ButtonVariant.Destructive
    | _ -> Error "not a ButtonVariant"

let private decCaptureSource (j: JVal) : Result<CaptureSource, string> =
    match j with
    | JStr "Camera" -> Ok CaptureSource.Camera
    | JStr "Microphone" -> Ok CaptureSource.Microphone
    | _ -> Error "not a CaptureSource"

let private decChannelDirection (j: JVal) : Result<ChannelDirection, string> =
    match j with
    | JStr "OutOnly" -> Ok ChannelDirection.OutOnly
    | JStr "TwoWay" -> Ok ChannelDirection.TwoWay
    | _ -> Error "not a ChannelDirection"

let private decChartDataLabels (j: JVal) : Result<ChartDataLabels, string> =
    match j with
    | JStr "Off" -> Ok ChartDataLabels.Off
    | JStr "Ends" -> Ok ChartDataLabels.Ends
    | _ -> Error "not a ChartDataLabels"

let private decChartKind (j: JVal) : Result<ChartKind, string> =
    match j with
    | JStr "Line" -> Ok ChartKind.Line
    | JStr "Bar" -> Ok ChartKind.Bar
    | JStr "Area" -> Ok ChartKind.Area
    | JStr "Pie" -> Ok ChartKind.Pie
    | JStr "Scatter" -> Ok ChartKind.Scatter
    | JStr "Heatmap" -> Ok ChartKind.Heatmap
    | _ -> Error "not a ChartKind"

let private decChartLegendPosition (j: JVal) : Result<ChartLegendPosition, string> =
    match j with
    | JStr "Top" -> Ok ChartLegendPosition.Top
    | JStr "Right" -> Ok ChartLegendPosition.Right
    | JStr "Bottom" -> Ok ChartLegendPosition.Bottom
    | JStr "None" -> Ok ChartLegendPosition.None
    | _ -> Error "not a ChartLegendPosition"

let private decChartXScale (j: JVal) : Result<ChartXScale, string> =
    match j with
    | JStr "Category" -> Ok ChartXScale.Category
    | JStr "Temporal" -> Ok ChartXScale.Temporal
    | _ -> Error "not a ChartXScale"

let private decCompareOp (j: JVal) : Result<CompareOp, string> =
    match j with
    | JStr "eq" -> Ok CompareOp.Eq
    | JStr "neq" -> Ok CompareOp.Neq
    | JStr "lt" -> Ok CompareOp.Lt
    | JStr "lte" -> Ok CompareOp.Lte
    | JStr "gt" -> Ok CompareOp.Gt
    | JStr "gte" -> Ok CompareOp.Gte
    | _ -> Error "not a CompareOp"

let private decDateStyle (j: JVal) : Result<DateStyle, string> =
    match j with
    | JStr "Short" -> Ok DateStyle.Short
    | JStr "Medium" -> Ok DateStyle.Medium
    | JStr "Long" -> Ok DateStyle.Long
    | JStr "Full" -> Ok DateStyle.Full
    | _ -> Error "not a DateStyle"

let private decDateVariant (j: JVal) : Result<DateVariant, string> =
    match j with
    | JStr "Date" -> Ok DateVariant.Date
    | JStr "Time" -> Ok DateVariant.Time
    | JStr "DateTime" -> Ok DateVariant.DateTime
    | _ -> Error "not a DateVariant"

let private decDeterminismSource (j: JVal) : Result<DeterminismSource, string> =
    match j with
    | JStr "Deterministic" -> Ok DeterminismSource.Deterministic
    | JStr "Clock" -> Ok DeterminismSource.Clock
    | JStr "Random" -> Ok DeterminismSource.Random
    | JStr "Network" -> Ok DeterminismSource.Network
    | _ -> Error "not a DeterminismSource"

let private decDurationStyle (j: JVal) : Result<DurationStyle, string> =
    match j with
    | JStr "Compact" -> Ok DurationStyle.Compact
    | JStr "Clock" -> Ok DurationStyle.Clock
    | JStr "Long" -> Ok DurationStyle.Long
    | _ -> Error "not a DurationStyle"

// Phase 819 — Duration format enums.
let private decDurationUnit (j: JVal) : Result<DurationUnit, string> =
    match j with
    | JStr "Seconds" -> Ok DurationUnit.Seconds
    | JStr "Minutes" -> Ok DurationUnit.Minutes
    | JStr "Hours" -> Ok DurationUnit.Hours
    | _ -> Error "not a DurationUnit"

let private decEmbedPermission (j: JVal) : Result<EmbedPermission, string> =
    match j with
    | JStr "AllowScripts" -> Ok EmbedPermission.AllowScripts
    | JStr "AllowSameOrigin" -> Ok EmbedPermission.AllowSameOrigin
    | JStr "AllowForms" -> Ok EmbedPermission.AllowForms
    | JStr "AllowFullscreen" -> Ok EmbedPermission.AllowFullscreen
    | _ -> Error "not a EmbedPermission"

let private decEmphasis (j: JVal) : Result<Emphasis, string> =
    match j with
    | JStr "Quiet" -> Ok Emphasis.Quiet
    | JStr "Normal" -> Ok Emphasis.Normal
    | JStr "Loud" -> Ok Emphasis.Loud
    | _ -> Error "not a Emphasis"

let private decFileReadEncoding (j: JVal) : Result<FileReadEncoding, string> =
    match j with
    | JStr "Text" -> Ok FileReadEncoding.Text
    | JStr "Base64" -> Ok FileReadEncoding.Base64
    | JStr "DataUrl" -> Ok FileReadEncoding.DataUrl
    | _ -> Error "not a FileReadEncoding"

let private decFontVoice (j: JVal) : Result<FontVoice, string> =
    match j with
    | JStr "Default" -> Ok FontVoice.Default
    | JStr "Display" -> Ok FontVoice.Display
    | JStr "Structural" -> Ok FontVoice.Structural
    | _ -> Error "not a FontVoice"

let private decHashStrictness (j: JVal) : Result<HashStrictness, string> =
    match j with
    | JStr "StrictReplay" -> Ok HashStrictness.StrictReplay
    | JStr "AdvisoryWarning" -> Ok HashStrictness.AdvisoryWarning
    | JStr "Enforced" -> Ok HashStrictness.Enforced
    | _ -> Error "not a HashStrictness"

let private decHeadingVariant (j: JVal) : Result<HeadingVariant, string> =
    match j with
    | JStr "Standard" -> Ok HeadingVariant.Standard
    | JStr "Eyebrow" -> Ok HeadingVariant.Eyebrow
    | JStr "Caption" -> Ok HeadingVariant.Caption
    | JStr "Lead" -> Ok HeadingVariant.Lead
    | _ -> Error "not a HeadingVariant"

let private decHostEffect (j: JVal) : Result<HostEffect, string> =
    match j with
    | JStr "Pure" -> Ok HostEffect.Pure
    | JStr "ReadsHost" -> Ok HostEffect.ReadsHost
    | JStr "WritesHost" -> Ok HostEffect.WritesHost
    | _ -> Error "not a HostEffect"

// Phase 821 — Icon size class.
let private decIconSize (j: JVal) : Result<IconSize, string> =
    match j with
    | JStr "Small" -> Ok IconSize.Small
    | JStr "Medium" -> Ok IconSize.Medium
    | JStr "Large" -> Ok IconSize.Large
    | _ -> Error "not a IconSize"

let private decImageAspect (j: JVal) : Result<ImageAspect, string> =
    match j with
    | JStr "Natural" -> Ok ImageAspect.Natural
    | JStr "Square" -> Ok ImageAspect.Square
    | JStr "FourThree" -> Ok ImageAspect.FourThree
    | JStr "ThreeTwo" -> Ok ImageAspect.ThreeTwo
    | JStr "SixteenNine" -> Ok ImageAspect.SixteenNine
    | _ -> Error "not a ImageAspect"

let private decImageFit (j: JVal) : Result<ImageFit, string> =
    match j with
    | JStr "Natural" -> Ok ImageFit.Natural
    | JStr "Cover" -> Ok ImageFit.Cover
    | JStr "Contain" -> Ok ImageFit.Contain
    | _ -> Error "not a ImageFit"

let private decImageLoading (j: JVal) : Result<ImageLoading, string> =
    match j with
    | JStr "Eager" -> Ok ImageLoading.Eager
    | JStr "Lazy" -> Ok ImageLoading.Lazy
    | _ -> Error "not a ImageLoading"

let private decImageVariant (j: JVal) : Result<ImageVariant, string> =
    match j with
    | JStr "Default" -> Ok ImageVariant.Default
    | JStr "Avatar" -> Ok ImageVariant.Avatar
    | JStr "Rounded" -> Ok ImageVariant.Rounded
    | _ -> Error "not a ImageVariant"

let private decLinkProtection (j: JVal) : Result<LinkProtection, string> =
    match j with
    | JStr "email" -> Ok LinkProtection.Email
    | _ -> Error "not a LinkProtection"

let private decLiveRegionKind (j: JVal) : Result<LiveRegionKind, string> =
    match j with
    | JStr "polite" -> Ok LiveRegionKind.Polite
    | JStr "assertive" -> Ok LiveRegionKind.Assertive
    | JStr "off" -> Ok LiveRegionKind.Off
    | _ -> Error "not a LiveRegionKind"

let private decMathDisplay (j: JVal) : Result<MathDisplay, string> =
    match j with
    | JStr "Inline" -> Ok MathDisplay.Inline
    | JStr "Block" -> Ok MathDisplay.Block
    | _ -> Error "not a MathDisplay"

let private decModalityKind (j: JVal) : Result<ModalityKind, string> =
    match j with
    | JStr "Modal" -> Ok ModalityKind.Modal
    | JStr "Popover" -> Ok ModalityKind.Popover
    | _ -> Error "not a ModalityKind"

let private decMotion (j: JVal) : Result<Motion, string> =
    match j with
    | JStr "None" -> Ok Motion.None
    | JStr "PulseDuringLoad" -> Ok Motion.PulseDuringLoad
    | JStr "FadeInOnMount" -> Ok Motion.FadeInOnMount
    | JStr "SlideInFromBelow" -> Ok Motion.SlideInFromBelow
    | JStr "ShakeOnError" -> Ok Motion.ShakeOnError
    | JStr "RotateOnRefresh" -> Ok Motion.RotateOnRefresh
    | JStr "SlideInFromRight" -> Ok Motion.SlideInFromRight
    | JStr "ExpandCollapse" -> Ok Motion.ExpandCollapse
    | JStr "CrossFade" -> Ok Motion.CrossFade
    | JStr "SlideBetween" -> Ok Motion.SlideBetween
    | _ -> Error "not a Motion"

let private decOrientation (j: JVal) : Result<Orientation, string> =
    match j with
    | JStr "Vertical" -> Ok Orientation.Vertical
    | JStr "Horizontal" -> Ok Orientation.Horizontal
    | _ -> Error "not a Orientation"

let private decRelativeTimeUnit (j: JVal) : Result<RelativeTimeUnit, string> =
    match j with
    | JStr "Second" -> Ok RelativeTimeUnit.Second
    | JStr "Minute" -> Ok RelativeTimeUnit.Minute
    | JStr "Hour" -> Ok RelativeTimeUnit.Hour
    | JStr "Day" -> Ok RelativeTimeUnit.Day
    | JStr "Week" -> Ok RelativeTimeUnit.Week
    | JStr "Month" -> Ok RelativeTimeUnit.Month
    | JStr "Year" -> Ok RelativeTimeUnit.Year
    | _ -> Error "not a RelativeTimeUnit"

let private decScrollOrientation (j: JVal) : Result<ScrollOrientation, string> =
    match j with
    | JStr "Vertical" -> Ok ScrollOrientation.Vertical
    | JStr "Horizontal" -> Ok ScrollOrientation.Horizontal
    | JStr "Both" -> Ok ScrollOrientation.Both
    | _ -> Error "not a ScrollOrientation"

let private decSortDirection (j: JVal) : Result<SortDirection, string> =
    match j with
    | JStr "asc" -> Ok SortDirection.Asc
    | JStr "desc" -> Ok SortDirection.Desc
    | _ -> Error "not a SortDirection"

let private decStyleRole (j: JVal) : Result<StyleRole, string> =
    match j with
    | JStr "None" -> Ok StyleRole.None
    | JStr "Eyebrow" -> Ok StyleRole.Eyebrow
    | JStr "Data" -> Ok StyleRole.Data
    | JStr "Lede" -> Ok StyleRole.Lede
    | JStr "Caption" -> Ok StyleRole.Caption
    | _ -> Error "not a StyleRole"

let private decStyleWeight (j: JVal) : Result<StyleWeight, string> =
    match j with
    | JStr "Compact" -> Ok StyleWeight.Compact
    | JStr "Standard" -> Ok StyleWeight.Standard
    | JStr "Spacious" -> Ok StyleWeight.Spacious
    | _ -> Error "not a StyleWeight"

let private decTextAnchor (j: JVal) : Result<TextAnchor, string> =
    match j with
    | JStr "Start" -> Ok TextAnchor.Start
    | JStr "Middle" -> Ok TextAnchor.Middle
    | JStr "End" -> Ok TextAnchor.End
    | _ -> Error "not a TextAnchor"

let private decTextDirection (j: JVal) : Result<TextDirection, string> =
    match j with
    | JStr "auto" -> Ok TextDirection.Auto
    | JStr "ltr" -> Ok TextDirection.Ltr
    | JStr "rtl" -> Ok TextDirection.Rtl
    | _ -> Error "not a TextDirection"

let private decTextFormat (j: JVal) : Result<TextFormat, string> =
    match j with
    | JStr "email" -> Ok TextFormat.Email
    | JStr "url" -> Ok TextFormat.Url
    | JStr "tel" -> Ok TextFormat.Tel
    | _ -> Error "not a TextFormat"

let private decToneVariant (j: JVal) : Result<ToneVariant, string> =
    match j with
    | JStr "Default" -> Ok ToneVariant.Default
    | JStr "Subdued" -> Ok ToneVariant.Subdued
    | JStr "Brand" -> Ok ToneVariant.Brand
    | JStr "Success" -> Ok ToneVariant.Success
    | JStr "Warning" -> Ok ToneVariant.Warning
    | JStr "Critical" -> Ok ToneVariant.Critical
    | JStr "Info" -> Ok ToneVariant.Info
    | _ -> Error "not a ToneVariant"

let private decTrackKind (j: JVal) : Result<TrackKind, string> =
    match j with
    | JStr "Subtitles" -> Ok TrackKind.Subtitles
    | JStr "Captions" -> Ok TrackKind.Captions
    | JStr "Descriptions" -> Ok TrackKind.Descriptions
    | JStr "Chapters" -> Ok TrackKind.Chapters
    | _ -> Error "not a TrackKind"

let private decTrendPolarity (j: JVal) : Result<TrendPolarity, string> =
    match j with
    | JStr "HigherIsBetter" -> Ok TrendPolarity.HigherIsBetter
    | JStr "LowerIsBetter" -> Ok TrendPolarity.LowerIsBetter
    | _ -> Error "not a TrendPolarity"

let rec private decNodeKind (j: JVal) : Result<NodeKind<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Badge" -> decBadgeSpec j |> Result.map NodeKind.Badge
    | "Box" -> decBoxSpec j |> Result.map NodeKind.Box
    | "Button" -> decButtonSpec j |> Result.map NodeKind.Button
    | "Callout" -> decCalloutSpec j |> Result.map NodeKind.Callout
    | "Chart" -> decChartSpec j |> Result.map NodeKind.Chart
    | "CodeBlock" -> decCodeBlockSpec j |> Result.map NodeKind.CodeBlock
    | "Custom" -> decCustomSpec j |> Result.map NodeKind.Custom
    | "DataGrid" -> decDataGridSpec j |> Result.map NodeKind.DataGrid
    | "Disclosure" -> decDisclosureSpec j |> Result.map NodeKind.Disclosure
    | "Drawing" -> decDrawingSpec j |> Result.map NodeKind.Drawing
    | "Embed" -> decEmbedSpec j |> Result.map NodeKind.Embed
    | "ErrorBoundary" -> decErrorBoundarySpec j |> Result.map NodeKind.ErrorBoundary
    | "Fact" -> decFactSpec j |> Result.map NodeKind.Fact
    | "FileUpload" -> decFileUploadSpec j |> Result.map NodeKind.FileUpload
    | "Filters" -> decFiltersSpec j |> Result.map NodeKind.Filters
    | "Form" -> decFormSpec j |> Result.map NodeKind.Form
    | "FragmentDecl" -> decFragmentDeclSpec j |> Result.map NodeKind.FragmentDecl
    | "FragmentRef" -> decFragmentRefSpec j |> Result.map NodeKind.FragmentRef
    | "Heading" -> decHeadingSpec j |> Result.map NodeKind.Heading
    | "Icon" -> decIconSpec j |> Result.map NodeKind.Icon
    | "Image" -> decImageSpec j |> Result.map NodeKind.Image
    | "LabelValueRow" -> decLabelValueRowSpec j |> Result.map NodeKind.LabelValueRow
    | "Link" -> decLinkSpec j |> Result.map NodeKind.Link
    | "List" -> decListSpec j |> Result.map NodeKind.List
    | "Map" -> decMapSpec j |> Result.map NodeKind.Map
    | "Markdown" -> decMarkdownSpec j |> Result.map NodeKind.Markdown
    | "Math" -> decMathSpec j |> Result.map NodeKind.Math
    | "Media" -> decMediaSpec j |> Result.map NodeKind.Media
    | "Metric" -> decMetricSpec j |> Result.map NodeKind.Metric
    | "Modal" -> decModalSpec j |> Result.map NodeKind.Modal
    | "Mount" -> decMountSpec j |> Result.map NodeKind.Mount
    | "Progress" -> decProgressSpec j |> Result.map NodeKind.Progress
    | "ScrollArea" -> decScrollAreaSpec j |> Result.map NodeKind.ScrollArea
    | "Select" -> decSelectSpec j |> Result.map NodeKind.Select
    | "Skeleton" -> decSkeletonSpec j |> Result.map NodeKind.Skeleton
    | "Sparkline" -> decSparklineSpec j |> Result.map NodeKind.Sparkline
    | "SplitPanel" -> decSplitPanelSpec j |> Result.map NodeKind.SplitPanel
    | "Stepper" -> decStepperSpec j |> Result.map NodeKind.Stepper
    | "SummaryList" -> decSummaryListSpec j |> Result.map NodeKind.SummaryList
    | "Switch" -> decSwitchSpec j |> Result.map NodeKind.Switch
    | "Tabs" -> decTabsSpec j |> Result.map NodeKind.Tabs
    | "Toast" -> decToastSpec j |> Result.map NodeKind.Toast
    | "Tree" -> decTreeSpec j |> Result.map NodeKind.Tree
    | __other -> Error ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    dOpt "accessibility" __fs decAccessibility |> Result.bind (fun accessibility ->
    Ok (None) |> Result.bind (fun extraAttributes ->
    Ok (None) |> Result.bind (fun motion ->
    dOpt "state" __fs decStateBehaviour |> Result.bind (fun state ->
    dOpt "style" __fs decSemanticStyle |> Result.bind (fun style ->
    dOpt "tooltip" __fs decTextSource |> Result.bind (fun tooltip ->
    Ok { Id = id; Kind = kind; Accessibility = accessibility; ExtraAttributes = extraAttributes; Motion = motion; State = state; Style = style; Tooltip = tooltip })))))))))

and private decAction (j: JVal) : Result<Action<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Chain" ->
            dReq "ops" __fs (dList decAction) |> Result.bind (fun ops ->
            Ok(Action.Chain(ops)))
        | "WriteToClipboard" ->
            dReq "text" __fs decTextSource |> Result.bind (fun text ->
            Ok(Action.WriteToClipboard(text)))
        | "Dispatch" ->
            Ok ((("<dispatch>" :> obj))) |> Result.bind (fun msg ->
            Ok(Action.Dispatch(msg)))
        | "Invoke" ->
            dReq "capabilityId" __fs dStr |> Result.bind (fun capabilityId ->
            dReq "args" __fs (dList decInvokeArg) |> Result.bind (fun args ->
            Ok(Action.Invoke(capabilityId, args))))
        | "ReadFileBody" ->
            dReq "fileRef" __fs dStr |> Result.bind (fun fileRef ->
            Ok (None) |> Result.bind (fun fileHandle ->
            dReq "encoding" __fs decFileReadEncoding |> Result.bind (fun encoding ->
            (dPresent "onRead" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> ("<closure>" :> obj))))) |> Result.bind (fun onRead ->
            Ok(Action.ReadFileBody(fileRef, fileHandle, encoding, onRead))))))
        | "Call" ->
            dReq "endpoint" __fs dStr |> Result.bind (fun endpoint ->
            (dPresent "onResult" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> ("<closure>" :> obj))))) |> Result.bind (fun onResult ->
            dOpt "into" __fs decCallResultTarget |> Result.bind (fun into ->
            Ok(Action.Call(endpoint, onResult, into)))))
        | "Navigate" ->
            dReq "route" __fs dStr |> Result.bind (fun route ->
            Ok(Action.Navigate(route)))
        | "CommitLocal" ->
            dReq "nodeId" __fs dStr |> Result.bind (fun nodeId ->
            Ok(Action.CommitLocal(nodeId)))
        | "Notify" ->
            dReq "channel" __fs dStr |> Result.bind (fun channel ->
            dReq "payload" __fs dJson |> Result.bind (fun payload ->
            Ok(Action.Notify(channel, payload))))
        | "SetState" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            dOpt "value" __fs dJson |> Result.bind (fun value ->
            dOpt "valueFrom" __fs (decBinding dJson) |> Result.bind (fun valueFrom ->
            // Phase 818 — value XOR valueFrom (a literal, or a Binding
            // evaluated at dispatch time); exactly one must be present.
            match value, valueFrom with
            | Some _, Some _ -> Error "SetState carries both 'value' and 'valueFrom' — exactly one is allowed ('value' is a literal; 'valueFrom' derives the written value from a Binding at dispatch time)"
            | None, None -> Error "SetState requires 'value' (a literal JSON value) or 'valueFrom' (a Binding evaluated at dispatch time)"
            | _ -> Ok(Action.SetState(key, value, valueFrom)))))
        | "AiTool" ->
            dReq "toolName" __fs dStr |> Result.bind (fun toolName ->
            dReq "args" __fs dJson |> Result.bind (fun args ->
            Ok(Action.AiTool(toolName, args))))
        | "Print" -> Ok Action.Print
        | __other -> Error ("unknown Action case: " + __other))
    | _ -> Error "expected a Action object"

and private decBinding<'T> (decT: JVal -> Result<'T, string>) (j: JVal) : Result<Binding<'T>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Static" ->
            dOpt "value" __fs decT |> Result.bind (fun value ->
            Ok(Binding.Static(value)))
        | "Query" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok ((fun (raw: obj) -> unbox raw)) |> Result.bind (fun accessor ->
            dOpt "dependsOn" __fs (dList dStr) |> Result.bind (fun dependsOn ->
            Ok(Binding.Query(name, accessor, dependsOn)))))
        | "Filter" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dOpt "defaultValue" __fs decT |> Result.bind (fun defaultValue ->
            Ok(Binding.Filter(name, defaultValue))))
        | "Selection" ->
            dReq "nodeId" __fs dStr |> Result.bind (fun nodeId ->
            Ok ((fun (raw: obj) -> unbox raw)) |> Result.bind (fun accessor ->
            dOpt "defaultValue" __fs decT |> Result.bind (fun defaultValue ->
            dOpt "field" __fs dStr |> Result.bind (fun field ->
            Ok(Binding.Selection(nodeId, accessor, defaultValue, field))))))
        | "State" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            dOpt "defaultValue" __fs decT |> Result.bind (fun defaultValue ->
            Ok(Binding.State(key, defaultValue))))
        // Identity accessor (the Phase 427 Selection fix replayed): the
        // host-furnished instant is already the wire-shaped string, so a
        // decoded reader receives it as-is; a value-discarding placeholder
        // would make every decoded `Now` resolve to nothing.
        | "Now" ->
            Ok ((fun (raw: obj) -> unbox raw)) |> Result.bind (fun accessor ->
            Ok(Binding.Now(accessor)))
        | "Computed" ->
            Ok ((fun _ -> Unchecked.defaultof<'T>)) |> Result.bind (fun fn ->
            Ok(Binding.Computed(fn)))
        | "Local" ->
            dReq "flushOn" __fs decLocalFlushTrigger |> Result.bind (fun flushOn ->
            Ok ((fun _ -> "")) |> Result.bind (fun format ->
            dReq "initialFrom" __fs (decBinding decT) |> Result.bind (fun initialFrom ->
            (dPresent "onCommit" __fs |> Result.map (Option.map (fun () -> (fun _ -> ("<closure>" :> obj))))) |> Result.bind (fun onCommit ->
            Ok ((fun _ -> Error "<closure>")) |> Result.bind (fun parse ->
            Ok(Binding.Local(flushOn, format, initialFrom, onCommit, parse)))))))
        | "Format" ->
            dReq "source" __fs (decBinding dFloat) |> Result.bind (fun source ->
            dReq "format" __fs decFormat |> Result.bind (fun format ->
            dReq "locale" __fs decLocaleSource |> Result.bind (fun locale ->
            Ok(Binding.Format(source, format, locale)))))
        | "I18n" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            dOpt "args" __fs (dMap (decBinding dJson)) |> Result.bind (fun args ->
            Ok(Binding.I18n(key, args))))
        // Phase 818 — a binding-shaped source (State / Selection / Query
        // `$type`) is preserved as `TransformSource.Live`; the initial
        // snapshot derives from the binding's carried default data via the
        // host-prelude helpers (a State source must carry data — the
        // Phase-815 posture; Selection/Query fall back to the empty
        // table). Anything else decodes through Core's columnar codec as
        // before, byte-identical.
        | "Transform" ->
            dReq "source" __fs decTransformSource |> Result.bind (fun source ->
            dReq "pipeline" __fs (dList (fun __j -> Fuaran.Core.DataFrameCodec.decodeTransform __j |> Result.mapError string)) |> Result.bind (fun pipeline ->
            dOpt "params" __fs (dList decTransformParam) |> Result.bind (fun ``params`` ->
            Ok(Binding.Transform(source, pipeline, ``params``)))))
        | "Invoke" ->
            dReq "capabilityId" __fs dStr |> Result.bind (fun capabilityId ->
            dReq "args" __fs (dList decInvokeArg) |> Result.bind (fun args ->
            Ok(Binding.Invoke(capabilityId, args))))
        | __other -> Error ("unknown Binding case: " + __other))
    | _ -> Error "expected a Binding object"

and private decCallResultTarget (j: JVal) : Result<CallResultTarget, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "State" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            Ok(CallResultTarget.State(key)))
        | "Query" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            Ok(CallResultTarget.Query(name)))
        | __other -> Error ("unknown CallResultTarget case: " + __other))
    | _ -> Error "expected a CallResultTarget object"

and private decCellFormat (j: JVal) : Result<CellFormat, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "None" -> Ok CellFormat.None
        | "Number" ->
            dOpt "decimals" __fs dInt |> Result.bind (fun decimals ->
            Ok(CellFormat.Number(decimals)))
        | "Currency" ->
            dReq "code" __fs dStr |> Result.bind (fun code ->
            Ok(CellFormat.Currency(code)))
        | "Percent" ->
            dOpt "decimals" __fs dInt |> Result.bind (fun decimals ->
            Ok(CellFormat.Percent(decimals)))
        | "SignificantDigits" ->
            dReq "digits" __fs dInt |> Result.bind (fun digits ->
            Ok(CellFormat.SignificantDigits(digits)))
        | "Date" ->
            dReq "format" __fs dStr |> Result.bind (fun format ->
            Ok(CellFormat.Date(format)))
        | "Duration" ->
            dReq "unit" __fs decDurationUnit |> Result.bind (fun unit ->
            dReq "style" __fs decDurationStyle |> Result.bind (fun style ->
            Ok(CellFormat.Duration(unit, style))))
        | "RelativeTime" ->
            dReq "unit" __fs decRelativeTimeUnit |> Result.bind (fun unit ->
            Ok(CellFormat.RelativeTime(unit)))
        | "Custom" ->
            Ok ((fun _ -> "")) |> Result.bind (fun fn ->
            Ok(CellFormat.Custom(fn)))
        | __other -> Error ("unknown CellFormat case: " + __other))
    | _ -> Error "expected a CellFormat object"

and private decCellKindErased (j: JVal) : Result<CellKindErased<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" -> Ok CellKindErased.Text
        | "Numeric" -> Ok CellKindErased.Numeric
        | "Date" -> Ok CellKindErased.Date
        | "Editable" ->
            (dPresent "onEdit" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row * Fuaran.UI.HostPrelude.CellValue) -> Action.Chain [])))) |> Result.bind (fun onEdit ->
            Ok(CellKindErased.Editable(onEdit)))
        | "Checkbox" ->
            Ok ((fun _ -> false)) |> Result.bind (fun get ->
            (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row * bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
            Ok(CellKindErased.Checkbox(get, onToggle))))
        | "Button" ->
            dReq "label" __fs decTextSource |> Result.bind (fun label ->
            (dPresent "onClick" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row) -> Action.Chain [])))) |> Result.bind (fun onClick ->
            Ok(CellKindErased.Button(label, onClick))))
        | "ButtonGroup" ->
            dReq "buttons" __fs (dList decButtonGroupItem) |> Result.bind (fun buttons ->
            Ok(CellKindErased.ButtonGroup(buttons)))
        | "Link" ->
            Ok ((fun _ -> "")) |> Result.bind (fun hrefFn ->
            Ok ((fun _ -> TextSource.Literal "")) |> Result.bind (fun labelFn ->
            Ok(CellKindErased.Link(hrefFn, labelFn))))
        | "Pill" ->
            Ok ((fun _ -> TextSource.Literal "")) |> Result.bind (fun labelFn ->
            Ok ((fun _ -> ToneVariant.Default)) |> Result.bind (fun toneFn ->
            Ok(CellKindErased.Pill(labelFn, toneFn))))
        | "TonedPill" ->
            dReq "field" __fs dStr |> Result.bind (fun field ->
            dReq "map" __fs (dMap decToneVariant) |> Result.bind (fun map ->
            dDef "default" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun ``default`` ->
            Ok(CellKindErased.TonedPill(field, map, ``default``)))))
        | "Progress" ->
            Ok ((fun _ -> 0.0)) |> Result.bind (fun fractionFn ->
            (dPresent "labelFn" __fs |> Result.map (Option.map (fun () -> (fun _ -> TextSource.Literal "")))) |> Result.bind (fun labelFn ->
            Ok(CellKindErased.Progress(fractionFn, labelFn))))
        | "Custom" ->
            Ok ((fun _ -> Unchecked.defaultof<Node<obj>>)) |> Result.bind (fun fn ->
            Ok(CellKindErased.Custom(fn)))
        | __other -> Error ("unknown CellKindErased case: " + __other))
    | _ -> Error "expected a CellKindErased object"

and private decColumnWidth (j: JVal) : Result<ColumnWidth, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Auto" -> Ok ColumnWidth.Auto
        | "Fixed" ->
            dReq "pixels" __fs dInt |> Result.bind (fun pixels ->
            Ok(ColumnWidth.Fixed(pixels)))
        | "Flex" ->
            dReq "weight" __fs dFloat |> Result.bind (fun weight ->
            Ok(ColumnWidth.Flex(weight)))
        | __other -> Error ("unknown ColumnWidth case: " + __other))
    | _ -> Error "expected a ColumnWidth object"

and private decCurveCommand (j: JVal) : Result<CurveCommand, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "MoveTo" ->
            dReq "to" __fs decDrawPoint |> Result.bind (fun ``to`` ->
            Ok(CurveCommand.MoveTo(``to``)))
        | "LineTo" ->
            dReq "to" __fs decDrawPoint |> Result.bind (fun ``to`` ->
            Ok(CurveCommand.LineTo(``to``)))
        | "CubicTo" ->
            dReq "control1" __fs decDrawPoint |> Result.bind (fun control1 ->
            dReq "control2" __fs decDrawPoint |> Result.bind (fun control2 ->
            dReq "to" __fs decDrawPoint |> Result.bind (fun ``to`` ->
            Ok(CurveCommand.CubicTo(control1, control2, ``to``)))))
        | "QuadraticTo" ->
            dReq "control" __fs decDrawPoint |> Result.bind (fun control ->
            dReq "to" __fs decDrawPoint |> Result.bind (fun ``to`` ->
            Ok(CurveCommand.QuadraticTo(control, ``to``))))
        | "Close" -> Ok CurveCommand.Close
        | __other -> Error ("unknown CurveCommand case: " + __other))
    | _ -> Error "expected a CurveCommand object"

and private decFormFieldKind (j: JVal) : Result<FormFieldKind<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Text" ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            Ok(FormFieldKind.Text(value, onChange))))
        | "Number" ->
            dOpt "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: float) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            Ok(FormFieldKind.Number(value, onChange))))
        | "Checkbox" ->
            dOpt "value" __fs (decBinding dBool) |> Result.bind (fun value ->
            (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
            Ok(FormFieldKind.Checkbox(value, onToggle))))
        | "Toggle" ->
            dOpt "value" __fs (decBinding dBool) |> Result.bind (fun value ->
            (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
            Ok(FormFieldKind.Toggle(value, onToggle))))
        | "Choice" ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string option) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            Ok(FormFieldKind.Choice(options, value, onChange)))))
        | "TextArea" ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dReq "rows" __fs dInt |> Result.bind (fun rows ->
            Ok(FormFieldKind.TextArea(value, onChange, rows)))))
        | "RangedNumber" ->
            dOpt "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: float) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dOpt "min" __fs dFloat |> Result.bind (fun min ->
            dOpt "max" __fs dFloat |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.RangedNumber(value, onChange, min, max, step)))))))
        | "Range" ->
            dOpt "value" __fs (fun (j: JVal) -> match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = "$type")) -> decRangePair j |> Result.map (fun p -> Binding.Static(Some p)) | __other -> decBinding decRangePair __other) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: float * float) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dOpt "min" __fs dFloat |> Result.bind (fun min ->
            dOpt "max" __fs dFloat |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.Range(value, onChange, min, max, step)))))))
        | "SegmentedChoice" ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string option) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dReq "orientation" __fs decOrientation |> Result.bind (fun orientation ->
            Ok(FormFieldKind.SegmentedChoice(options, value, onChange, orientation))))))
        | "Date" ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string option) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dReq "variant" __fs decDateVariant |> Result.bind (fun variant ->
            dOpt "min" __fs dStr |> Result.bind (fun min ->
            dOpt "max" __fs dStr |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.Date(value, onChange, variant, min, max, step))))))))
        | "DateRange" ->
            dOpt "value" __fs (fun (j: JVal) -> match j with | JObj __rf when not (__rf |> List.exists (fun (k, _) -> k = "$type")) -> decDateRangePair j |> Result.map (fun p -> Binding.Static(Some p)) | __other -> decBinding decDateRangePair __other) |> Result.bind (fun value ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string * string) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dReq "variant" __fs decDateVariant |> Result.bind (fun variant ->
            dOpt "min" __fs dStr |> Result.bind (fun min ->
            dOpt "max" __fs dStr |> Result.bind (fun max ->
            dOpt "step" __fs dFloat |> Result.bind (fun step ->
            Ok(FormFieldKind.DateRange(value, onChange, variant, min, max, step))))))))
        | "Combobox" ->
            dDef "allowFreeText" __fs dBool (false) |> Result.bind (fun allowFreeText ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string option) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dReq "options" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun options ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            Ok(FormFieldKind.Combobox(allowFreeText, onChange, options, value))))))
        | "Rating" ->
            dDef "allowHalf" __fs dBool (false) |> Result.bind (fun allowHalf ->
            dReq "max" __fs dInt |> Result.bind (fun max ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: float) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dOpt "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
            if max < 1 then
                Error (sprintf "Rating 'max' must be at least 1 — a scale with %d positions cannot be rendered or announced" max)
            else
                Ok(FormFieldKind.Rating(allowHalf, max, onChange, value))))))
        | "Color" ->
            (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> Action.Chain [])))) |> Result.bind (fun onChange ->
            dOpt "value" __fs (decBinding dStr) |> Result.bind (fun value ->
            match value with
            | Some(Binding.Static(Some __text)) when not (Fuaran.UI.HostPrelude.HexColor.isValid __text) ->
                Error (sprintf "Color 'value' must be a '#rrggbb' hex colour — got %s" __text)
            | _ -> Ok(FormFieldKind.Color(onChange, value))))
        | __other -> Error ("unknown FormFieldKind case: " + __other))
    | _ -> Error "expected a FormFieldKind object"

and private decFormat (j: JVal) : Result<Format, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Number" ->
            dOpt "decimals" __fs dInt |> Result.bind (fun decimals ->
            Ok(Format.Number(decimals)))
        | "Currency" ->
            dReq "isoCode" __fs dStr |> Result.bind (fun isoCode ->
            Ok(Format.Currency(isoCode)))
        | "Percent" ->
            dOpt "decimals" __fs dInt |> Result.bind (fun decimals ->
            Ok(Format.Percent(decimals)))
        | "Date" ->
            dReq "dateStyle" __fs decDateStyle |> Result.bind (fun dateStyle ->
            Ok(Format.Date(dateStyle)))
        | "RelativeTime" ->
            dReq "unit" __fs decRelativeTimeUnit |> Result.bind (fun unit ->
            Ok(Format.RelativeTime(unit)))
        | "Duration" ->
            dReq "unit" __fs decDurationUnit |> Result.bind (fun unit ->
            dReq "style" __fs decDurationStyle |> Result.bind (fun style ->
            Ok(Format.Duration(unit, style))))
        | __other -> Error ("unknown Format case: " + __other))
    | _ -> Error "expected a Format object"

and private decFragmentArg (j: JVal) : Result<FragmentArg<obj>, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Int" ->
            dReq "value" __fs dInt |> Result.bind (fun value ->
            Ok(FragmentArg.Int(value)))
        | "Float" ->
            dReq "value" __fs dFloat |> Result.bind (fun value ->
            Ok(FragmentArg.Float(value)))
        | "Bool" ->
            dReq "value" __fs dBool |> Result.bind (fun value ->
            Ok(FragmentArg.Bool(value)))
        | "Str" ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(FragmentArg.Str(value)))
        | "SlotArg" ->
            dReq "tree" __fs decNode |> Result.bind (fun tree ->
            Ok(FragmentArg.SlotArg(tree)))
        | __other -> Error ("unknown FragmentArg case: " + __other))
    | _ -> Error "expected a FragmentArg object"

and private decHoleDecl (j: JVal) : Result<HoleDecl, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Value" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dReq "space" __fs decHoleValueSpace |> Result.bind (fun space ->
            dOpt "default" __fs decScalar |> Result.bind (fun ``default`` ->
            Ok(HoleDecl.Value(name, space, ``default``)))))
        | "Slot" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dOpt "kindConstraint" __fs dStr |> Result.bind (fun kindConstraint ->
            Ok(HoleDecl.Slot(name, kindConstraint))))
        | "Repeat" ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dReq "countSpace" __fs decHoleValueSpace |> Result.bind (fun countSpace ->
            Ok(HoleDecl.Repeat(name, countSpace))))
        | __other -> Error ("unknown HoleDecl case: " + __other))
    | _ -> Error "expected a HoleDecl object"

and private decHoleValueSpace (j: JVal) : Result<HoleValueSpace, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "IntRange" ->
            dReq "min" __fs dInt |> Result.bind (fun min ->
            dReq "max" __fs dInt |> Result.bind (fun max ->
            Ok(HoleValueSpace.IntRange(min, max))))
        | "FloatRange" ->
            dReq "min" __fs dFloat |> Result.bind (fun min ->
            dReq "max" __fs dFloat |> Result.bind (fun max ->
            Ok(HoleValueSpace.FloatRange(min, max))))
        | "StringLen" ->
            dReq "minLen" __fs dInt |> Result.bind (fun minLen ->
            dReq "maxLen" __fs dInt |> Result.bind (fun maxLen ->
            Ok(HoleValueSpace.StringLen(minLen, maxLen))))
        | "Enum" ->
            dReq "choices" __fs (dList dStr) |> Result.bind (fun choices ->
            Ok(HoleValueSpace.Enum(choices)))
        | "AnyString" -> Ok HoleValueSpace.AnyString
        | __other -> Error ("unknown HoleValueSpace case: " + __other))
    | _ -> Error "expected a HoleValueSpace object"

and private decLayoutMode (j: JVal) : Result<LayoutMode, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Auto" -> Ok LayoutMode.Auto
        | "Flex" ->
            dReq "direction" __fs decOrientation |> Result.bind (fun direction ->
            dReq "wrap" __fs dBool |> Result.bind (fun wrap ->
            dOpt "gap" __fs dInt |> Result.bind (fun gap ->
            Ok(LayoutMode.Flex(direction, wrap, gap)))))
        | "Grid" ->
            dReq "cols" __fs dInt |> Result.bind (fun cols ->
            dOpt "templateColumns" __fs dStr |> Result.bind (fun templateColumns ->
            dOpt "gap" __fs dInt |> Result.bind (fun gap ->
            Ok(LayoutMode.Grid(cols, templateColumns, gap)))))
        | "Masonry" ->
            dReq "cols" __fs dInt |> Result.bind (fun cols ->
            dOpt "gap" __fs dInt |> Result.bind (fun gap ->
            Ok(LayoutMode.Masonry(cols, gap))))
        | __other -> Error ("unknown LayoutMode case: " + __other))
    | _ -> Error "expected a LayoutMode object"

and private decLocalFlushTrigger (j: JVal) : Result<LocalFlushTrigger, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "OnBlur" -> Ok LocalFlushTrigger.OnBlur
        | "OnSubmit" -> Ok LocalFlushTrigger.OnSubmit
        | "OnDebounce" ->
            dReq "milliseconds" __fs dInt |> Result.bind (fun milliseconds ->
            Ok(LocalFlushTrigger.OnDebounce(milliseconds)))
        | "OnCommitAction" -> Ok LocalFlushTrigger.OnCommitAction
        | __other -> Error ("unknown LocalFlushTrigger case: " + __other))
    | _ -> Error "expected a LocalFlushTrigger object"

and private decLocaleSource (j: JVal) : Result<LocaleSource, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Ambient" -> Ok LocaleSource.Ambient
        | "Explicit" ->
            dReq "tag" __fs dStr |> Result.bind (fun tag ->
            Ok(LocaleSource.Explicit(tag)))
        | __other -> Error ("unknown LocaleSource case: " + __other))
    | _ -> Error "expected a LocaleSource object"

and private decMediaKind (j: JVal) : Result<MediaKind, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Video" ->
            dDef "autoplay" __fs dBool (false) |> Result.bind (fun autoplay ->
            dOpt "poster" __fs (decBinding dStr) |> Result.bind (fun poster ->
            Ok(MediaKind.Video(autoplay, poster))))
        | "Audio" -> Ok MediaKind.Audio
        | __other -> Error ("unknown MediaKind case: " + __other))
    | _ -> Error "expected a MediaKind object"

and private decScalar (j: JVal) : Result<Scalar, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Int" ->
            dReq "value" __fs dInt |> Result.bind (fun value ->
            Ok(Scalar.Int(value)))
        | "Float" ->
            dReq "value" __fs dFloat |> Result.bind (fun value ->
            Ok(Scalar.Float(value)))
        | "Bool" ->
            dReq "value" __fs dBool |> Result.bind (fun value ->
            Ok(Scalar.Bool(value)))
        | "Str" ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(Scalar.Str(value)))
        | __other -> Error ("unknown Scalar case: " + __other))
    | _ -> Error "expected a Scalar object"

and private decShape (j: JVal) : Result<Shape, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Group" ->
            dReq "children" __fs (dList decShape) |> Result.bind (fun children ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Group(children, style))))
        | "Rectangle" ->
            dReq "x" __fs dFloat |> Result.bind (fun x ->
            dReq "y" __fs dFloat |> Result.bind (fun y ->
            dReq "width" __fs dFloat |> Result.bind (fun width ->
            dReq "height" __fs dFloat |> Result.bind (fun height ->
            dOpt "cornerRadius" __fs dFloat |> Result.bind (fun cornerRadius ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Rectangle(x, y, width, height, cornerRadius, style))))))))
        | "Line" ->
            dReq "x1" __fs dFloat |> Result.bind (fun x1 ->
            dReq "y1" __fs dFloat |> Result.bind (fun y1 ->
            dReq "x2" __fs dFloat |> Result.bind (fun x2 ->
            dReq "y2" __fs dFloat |> Result.bind (fun y2 ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Line(x1, y1, x2, y2, style)))))))
        | "Polyline" ->
            dReq "points" __fs (dList decDrawPoint) |> Result.bind (fun points ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Polyline(points, style))))
        | "Polygon" ->
            dReq "points" __fs (dList decDrawPoint) |> Result.bind (fun points ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Polygon(points, style))))
        | "Curve" ->
            dReq "commands" __fs (dList decCurveCommand) |> Result.bind (fun commands ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Curve(commands, style))))
        | "Circle" ->
            dReq "cx" __fs dFloat |> Result.bind (fun cx ->
            dReq "cy" __fs dFloat |> Result.bind (fun cy ->
            dReq "r" __fs dFloat |> Result.bind (fun r ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Circle(cx, cy, r, style))))))
        | "Ellipse" ->
            dReq "cx" __fs dFloat |> Result.bind (fun cx ->
            dReq "cy" __fs dFloat |> Result.bind (fun cy ->
            dReq "rx" __fs dFloat |> Result.bind (fun rx ->
            dReq "ry" __fs dFloat |> Result.bind (fun ry ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Ellipse(cx, cy, rx, ry, style)))))))
        | "Label" ->
            dReq "x" __fs dFloat |> Result.bind (fun x ->
            dReq "y" __fs dFloat |> Result.bind (fun y ->
            dReq "text" __fs decTextSource |> Result.bind (fun text ->
            dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
            Ok(Shape.Label(x, y, text, style))))))
        | __other -> Error ("unknown Shape case: " + __other))
    | _ -> Error "expected a Shape object"

and private decTextSource (j: JVal) : Result<TextSource, string> =
    match j with
    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = "$type")) ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Literal" ->
            dReq "text" __fs dStr |> Result.bind (fun text ->
            Ok(TextSource.Literal(text)))
        | "Bound" ->
            dReq "binding" __fs (decBinding dStr) |> Result.bind (fun binding ->
            Ok(TextSource.Bound(binding)))
        | "I18n" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            dReq "args" __fs (dMap dJson) |> Result.bind (fun args ->
            Ok(TextSource.I18n(key, args))))
        | __other -> Error ("unknown TextSource case: " + __other))
    | __bare ->
        dStr __bare |> Result.bind (fun text -> Ok(TextSource.Literal(text)))

and private decAccessibility (j: JVal) : Result<Accessibility, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "describedBy" __fs dStr |> Result.bind (fun describedBy ->
    dOpt "hidden" __fs (decBinding dBool) |> Result.bind (fun hidden ->
    dOpt "label" __fs (decBinding dStr) |> Result.bind (fun label ->
    dOpt "labelledBy" __fs dStr |> Result.bind (fun labelledBy ->
    dOpt "liveRegion" __fs decLiveRegionKind |> Result.bind (fun liveRegion ->
    dOpt "role" __fs Fuaran.UI.HostPrelude.decAriaRole |> Result.bind (fun role ->
    Ok { DescribedBy = describedBy; Hidden = hidden; Label = label; LabelledBy = labelledBy; LiveRegion = liveRegion; Role = role })))))))

and private decButtonGroupItem (j: JVal) : Result<ButtonGroupItem<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    (dPresent "onClick" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row) -> Action.Chain [])))) |> Result.bind (fun onClick ->
    Ok { Label = label; OnClick = onClick })))

and private decColumnErased (j: JVal) : Result<ColumnErased<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "field" __fs dStr |> Result.bind (fun field ->
    dOpt "sortable" __fs dBool |> Result.bind (fun sortable ->
    dOpt "editable" __fs dBool |> Result.bind (fun editable ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dReq "kind" __fs decCellKindErased |> Result.bind (fun kind ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    (dPresent "value" __fs |> Result.map (Option.map (fun () -> (fun _ -> Fuaran.UI.HostPrelude.CellValue.Empty)))) |> Result.bind (fun value ->
    dDef "width" __fs decColumnWidth (ColumnWidth.Auto) |> Result.bind (fun width ->
    Ok { Field = field; Sortable = sortable; Editable = editable; Format = format; Kind = kind; Label = label; Value = value; Width = width })))))))))

and private decCompareRule (j: JVal) : Result<CompareRule, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "against" __fs (decBinding dJson) |> Result.bind (fun against ->
    dReq "op" __fs decCompareOp |> Result.bind (fun op ->
    Ok { Against = against; Op = op })))

and private decContentHash (j: JVal) : Result<ContentHash, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "algorithm" __fs dStr |> Result.bind (fun algorithm ->
    dReq "hash" __fs dStr |> Result.bind (fun hash ->
    dReq "strictness" __fs decHashStrictness |> Result.bind (fun strictness ->
    Ok { Algorithm = algorithm; Hash = hash; Strictness = strictness }))))

and private decDateRangePair (j: JVal) : Result<DateRangePair, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "from" __fs dStr |> Result.bind (fun from ->
    dReq "to" __fs dStr |> Result.bind (fun ``to`` ->
    Ok { From = from; To = ``to`` })))

and private decDefaultSort (j: JVal) : Result<DefaultSort, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "column" __fs dInt |> Result.bind (fun column ->
    dReq "direction" __fs decSortDirection |> Result.bind (fun direction ->
    Ok { Column = column; Direction = direction })))

and private decDrawPoint (j: JVal) : Result<DrawPoint, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "x" __fs dFloat |> Result.bind (fun x ->
    dReq "y" __fs dFloat |> Result.bind (fun y ->
    Ok { X = x; Y = y })))

and private decDrawStyle (j: JVal) : Result<DrawStyle, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "emphasis" __fs decEmphasis |> Result.bind (fun emphasis ->
    dOpt "fill" __fs (decBinding dStr) |> Result.bind (fun fill ->
    dOpt "fontFamily" __fs dStr |> Result.bind (fun fontFamily ->
    dOpt "fontSize" __fs dFloat |> Result.bind (fun fontSize ->
    dOpt "markId" __fs dStr |> Result.bind (fun markId ->
    dOpt "opacity" __fs (decBinding dFloat) |> Result.bind (fun opacity ->
    dOpt "rotation" __fs dFloat |> Result.bind (fun rotation ->
    dOpt "stroke" __fs (decBinding dStr) |> Result.bind (fun stroke ->
    dOpt "strokeWidth" __fs (decBinding dFloat) |> Result.bind (fun strokeWidth ->
    dOpt "textAnchor" __fs decTextAnchor |> Result.bind (fun textAnchor ->
    dOpt "tip" __fs decTextSource |> Result.bind (fun tip ->
    Ok { Emphasis = emphasis; Fill = fill; FontFamily = fontFamily; FontSize = fontSize; MarkId = markId; Opacity = opacity; Rotation = rotation; Stroke = stroke; StrokeWidth = strokeWidth; TextAnchor = textAnchor; Tip = tip }))))))))))))

and private decEffectClass (j: JVal) : Result<EffectClass, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "determinism" __fs decDeterminismSource |> Result.bind (fun determinism ->
    dReq "hostEffect" __fs decHostEffect |> Result.bind (fun hostEffect ->
    Ok { Determinism = determinism; HostEffect = hostEffect })))

and private decFieldRule (j: JVal) : Result<FieldRule, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "compare" __fs decCompareRule |> Result.bind (fun compare ->
    dOpt "format" __fs decTextFormat |> Result.bind (fun format ->
    dOpt "maxLength" __fs dInt |> Result.bind (fun maxLength ->
    dOpt "message" __fs decTextSource |> Result.bind (fun message ->
    dOpt "minLength" __fs dInt |> Result.bind (fun minLength ->
    dOpt "pattern" __fs dStr |> Result.bind (fun pattern ->
    Ok { Compare = compare; Format = format; MaxLength = maxLength; Message = message; MinLength = minLength; Pattern = pattern })))))))

and private decFilterSpec (j: JVal) : Result<FilterSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decFormFieldKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    Ok { Kind = kind; Label = label; Name = name }))))

and private decFormField (j: JVal) : Result<FormField<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decFormFieldKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "required" __fs dBool |> Result.bind (fun required ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    dOpt "rule" __fs decFieldRule |> Result.bind (fun rule ->
    Ok { Id = id; Kind = kind; Label = label; Required = required; Help = help; Rule = rule })))))))

and private decGuestChannel (j: JVal) : Result<GuestChannel, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "direction" __fs decChannelDirection |> Result.bind (fun direction ->
    dOpt "messageShape" __fs dStr |> Result.bind (fun messageShape ->
    Ok { Direction = direction; MessageShape = messageShape })))

and private decInvokeArg (j: JVal) : Result<InvokeArg, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "addr" __fs dStr |> Result.bind (fun addr ->
    dReq "value" __fs dStr |> Result.bind (fun value ->
    Ok { Addr = addr; Value = value })))

and private decMapMarker (j: JVal) : Result<MapMarker, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "latitude" __fs dFloat |> Result.bind (fun latitude ->
    dReq "longitude" __fs dFloat |> Result.bind (fun longitude ->
    Ok { Label = label; Latitude = latitude; Longitude = longitude }))))

and private decRangePair (j: JVal) : Result<RangePair, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "max" __fs dFloat |> Result.bind (fun max ->
    dReq "min" __fs dFloat |> Result.bind (fun min ->
    Ok { Max = max; Min = min })))

and private decSelectOption (j: JVal) : Result<SelectOption, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "value" __fs dStr |> Result.bind (fun value ->
    Ok { Label = label; Value = value })))

and private decSemanticStyle (j: JVal) : Result<SemanticStyle, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "direction" __fs decTextDirection (TextDirection.Auto) |> Result.bind (fun direction ->
    dDef "emphasis" __fs decEmphasis (Emphasis.Normal) |> Result.bind (fun emphasis ->
    dDef "role" __fs decStyleRole (StyleRole.None) |> Result.bind (fun role ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dDef "voice" __fs decFontVoice (FontVoice.Default) |> Result.bind (fun voice ->
    dDef "weight" __fs decStyleWeight (StyleWeight.Standard) |> Result.bind (fun weight ->
    Ok { Direction = direction; Emphasis = emphasis; Role = role; Tone = tone; Voice = voice; Weight = weight })))))))

and private decSrcSetEntry (j: JVal) : Result<SrcSetEntry, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dReq "width" __fs dInt |> Result.bind (fun width ->
    Ok { Src = src; Width = width })))

and private decStateBehaviour (j: JVal) : Result<StateBehaviour<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "onEmpty" __fs decNode |> Result.bind (fun onEmpty ->
    (dPresent "onError" __fs |> Result.map (Option.map (fun () -> (fun _ -> Unchecked.defaultof<Node<obj>>)))) |> Result.bind (fun onError ->
    dOpt "onLoading" __fs decNode |> Result.bind (fun onLoading ->
    Ok { OnEmpty = onEmpty; OnError = onError; OnLoading = onLoading }))))

and private decStaticRows (j: JVal) : Result<StaticRows, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "defaultSort" __fs decDefaultSort |> Result.bind (fun defaultSort ->
    dReq "headers" __fs (dList decTextSource) |> Result.bind (fun headers ->
    dReq "rows" __fs (dList (dList decTextSource)) |> Result.bind (fun rows ->
    dOpt "sortable" __fs dBool |> Result.bind (fun sortable ->
    Ok { DefaultSort = defaultSort; Headers = headers; Rows = rows; Sortable = sortable })))))

and private decSwitchCase (j: JVal) : Result<SwitchCase<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "child" __fs decNode |> Result.bind (fun child ->
    dReq "match" __fs dStr |> Result.bind (fun ``match`` ->
    Ok { Child = child; Match = ``match`` })))

and private decTabHeader (j: JVal) : Result<TabHeader, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; Icon = icon; Disabled = disabled }))))

and private decTrackEntry (j: JVal) : Result<TrackEntry, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "default" __fs dBool (false) |> Result.bind (fun ``default`` ->
    dReq "kind" __fs decTrackKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dReq "srcLang" __fs dStr |> Result.bind (fun srcLang ->
    Ok { Default = ``default``; Kind = kind; Label = label; Src = src; SrcLang = srcLang }))))))

and private decTransformParam (j: JVal) : Result<TransformParam, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "from" __fs (decBinding dJson) |> Result.bind (fun from ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    Ok { From = from; Name = name })))

and private decTreeItem (j: JVal) : Result<TreeItem, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "children" __fs (dList decTreeItem) ([]) |> Result.bind (fun children ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    Ok { Children = children; Icon = icon; Id = id; Label = label })))))

and private decViewBox (j: JVal) : Result<ViewBox, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "height" __fs dFloat |> Result.bind (fun height ->
    dReq "minX" __fs dFloat |> Result.bind (fun minX ->
    dReq "minY" __fs dFloat |> Result.bind (fun minY ->
    dReq "width" __fs dFloat |> Result.bind (fun width ->
    Ok { Height = height; MinX = minX; MinY = minY; Width = width })))))

and private decBadgeSpec (j: JVal) : Result<BadgeSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "variant" __fs decBadgeVariant |> Result.bind (fun variant ->
    Ok { Label = label; Variant = variant })))

and private decBoxSpec (j: JVal) : Result<BoxSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dReq "layout" __fs decLayoutMode |> Result.bind (fun layout ->
    dReq "role" __fs decBoxRole |> Result.bind (fun role ->
    dDef "keepTogether" __fs dBool (false) |> Result.bind (fun keepTogether ->
    dDef "breakBefore" __fs dBool (false) |> Result.bind (fun breakBefore ->
    Ok { Children = children; Heading = heading; Layout = layout; Role = role; KeepTogether = keepTogether; BreakBefore = breakBefore })))))))

and private decButtonSpec (j: JVal) : Result<ButtonSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "onClick" __fs decAction |> Result.bind (fun onClick ->
    dReq "variant" __fs decButtonVariant |> Result.bind (fun variant ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    Ok (None) |> Result.bind (fun tooltip ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Label = label; OnClick = onClick; Variant = variant; Icon = icon; Tooltip = tooltip; Disabled = disabled })))))))

and private decCalloutSpec (j: JVal) : Result<CalloutSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "body" __fs decTextSource |> Result.bind (fun body ->
    dDef "dismissable" __fs dBool (false) |> Result.bind (fun dismissable ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    Ok { Body = body; Dismissable = dismissable; Tone = tone; Heading = heading; Icon = icon }))))))

and private decChartSpec (j: JVal) : Result<ChartSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "kind" __fs decChartKind |> Result.bind (fun kind ->
    dReq "source" __fs (decBinding Fuaran.Core.RowCodec.decodeRows) |> Result.bind (fun source ->
    dReq "stacked" __fs dBool |> Result.bind (fun stacked ->
    dReq "xField" __fs dStr |> Result.bind (fun xField ->
    dReq "yFields" __fs (dList dStr) |> Result.bind (fun yFields ->
    dOpt "title" __fs decTextSource |> Result.bind (fun title ->
    dOpt "valueFormat" __fs decFormat |> Result.bind (fun valueFormat ->
    dOpt "xTitle" __fs decTextSource |> Result.bind (fun xTitle ->
    dOpt "yTitle" __fs decTextSource |> Result.bind (fun yTitle ->
    dOpt "subtitle" __fs decTextSource |> Result.bind (fun subtitle ->
    dOpt "legendPosition" __fs decChartLegendPosition |> Result.bind (fun legendPosition ->
    dOpt "dataLabels" __fs decChartDataLabels |> Result.bind (fun dataLabels ->
    dOpt "xScale" __fs decChartXScale |> Result.bind (fun xScale ->
    (dPresent "onPointClick" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row) -> Action.Chain [])))) |> Result.bind (fun onPointClick ->
    Ok { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = title; ValueFormat = valueFormat; XTitle = xTitle; YTitle = yTitle; Subtitle = subtitle; LegendPosition = legendPosition; DataLabels = dataLabels; XScale = xScale; OnPointClick = onPointClick })))))))))))))))

and private decCodeBlockSpec (j: JVal) : Result<CodeBlockSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "code" __fs dStr |> Result.bind (fun code ->
    dReq "copyable" __fs dBool |> Result.bind (fun copyable ->
    dReq "highlightLines" __fs (dList dInt) |> Result.bind (fun highlightLines ->
    dReq "language" __fs dStr |> Result.bind (fun language ->
    dReq "lineNumbers" __fs dBool |> Result.bind (fun lineNumbers ->
    Ok { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers }))))))

and private decCustomSpec (j: JVal) : Result<CustomSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "moduleId" __fs dStr |> Result.bind (fun moduleId ->
    dReq "componentId" __fs dStr |> Result.bind (fun componentId ->
    dReq "props" __fs (dMap dJson) |> Result.bind (fun props ->
    dOpt "contentHash" __fs decContentHash |> Result.bind (fun contentHash ->
    dOpt "exposedNodeIds" __fs (dList dStr) |> Result.bind (fun exposedNodeIds ->
    Ok { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = contentHash; ExposedNodeIds = exposedNodeIds }))))))

and private decDataGridSpec (j: JVal) : Result<DataGridSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "columns" __fs (dList decColumnErased) |> Result.bind (fun columns ->
    dDef "editable" __fs dBool (false) |> Result.bind (fun editable ->
    (dPresent "rowKey" __fs |> Result.map (Option.map (fun () -> (fun _ -> "")))) |> Result.bind (fun rowKey ->
    dOpt "rowKeyField" __fs dStr |> Result.bind (fun rowKeyField ->
    dOpt "sortStateKey" __fs dStr |> Result.bind (fun sortStateKey ->
    dOpt "pageSize" __fs dInt |> Result.bind (fun pageSize ->
    dOpt "pageStateKey" __fs dStr |> Result.bind (fun pageStateKey ->
    dOpt "defaultSort" __fs decDefaultSort |> Result.bind (fun defaultSort ->
    dOpt "editStateKey" __fs dStr |> Result.bind (fun editStateKey ->
    dDef "reorderable" __fs dBool (false) |> Result.bind (fun reorderable ->
    dOpt "transferInKey" __fs dStr |> Result.bind (fun transferInKey ->
    dOpt "transferOutKey" __fs dStr |> Result.bind (fun transferOutKey ->
    dDef "keepRowsTogether" __fs dBool (false) |> Result.bind (fun keepRowsTogether ->
    dDef "repeatHeader" __fs dBool (false) |> Result.bind (fun repeatHeader ->
    dDef "exportable" __fs dBool (false) |> Result.bind (fun exportable ->
    dReq "source" __fs (decBinding Fuaran.Core.RowCodec.decodeRows) |> Result.bind (fun source ->
    dOpt "staticRows" __fs decStaticRows |> Result.bind (fun staticRows ->
    (dPresent "onRowClick" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.Core.Row) -> Action.Chain [])))) |> Result.bind (fun onRowClick ->
    Ok { Columns = columns; Editable = editable; RowKey = rowKey; RowKeyField = rowKeyField; SortStateKey = sortStateKey; PageSize = pageSize; PageStateKey = pageStateKey; DefaultSort = defaultSort; EditStateKey = editStateKey; Reorderable = reorderable; TransferInKey = transferInKey; TransferOutKey = transferOutKey; KeepRowsTogether = keepRowsTogether; RepeatHeader = repeatHeader; Exportable = exportable; Source = source; StaticRows = staticRows; OnRowClick = onRowClick })))))))))))))))))))

and private decDisclosureSpec (j: JVal) : Result<DisclosureSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "defaultOpen" __fs dBool |> Result.bind (fun defaultOpen ->
    dReq "heading" __fs decTextSource |> Result.bind (fun heading ->
    (dPresent "onToggle" __fs |> Result.map (Option.map (fun () -> (fun (_: bool) -> Action.Chain [])))) |> Result.bind (fun onToggle ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    Ok { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = onToggle; Open = ``open`` }))))))

and private decDrawingSpec (j: JVal) : Result<DrawingSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "description" __fs decTextSource |> Result.bind (fun description ->
    dReq "shapes" __fs (dList decShape) |> Result.bind (fun shapes ->
    dReq "style" __fs decDrawStyle |> Result.bind (fun style ->
    dOpt "title" __fs decTextSource |> Result.bind (fun title ->
    dReq "viewBox" __fs decViewBox |> Result.bind (fun viewBox ->
    Ok { Description = description; Shapes = shapes; Style = style; Title = title; ViewBox = viewBox }))))))

and private decEmbedSpec (j: JVal) : Result<EmbedSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "aspectRatio" __fs decImageAspect (ImageAspect.Natural) |> Result.bind (fun aspectRatio ->
    dDef "permissions" __fs (dList decEmbedPermission) ([]) |> Result.bind (fun permissions ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dReq "title" __fs decTextSource |> Result.bind (fun title ->
    Ok { AspectRatio = aspectRatio; Permissions = permissions; Src = src; Title = title })))))

and private decErrorBoundarySpec (j: JVal) : Result<ErrorBoundarySpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "child" __fs decNode |> Result.bind (fun child ->
    dReq "fallback" __fs decNode |> Result.bind (fun fallback ->
    Ok { Child = child; Fallback = fallback })))

and private decFactSpec (j: JVal) : Result<FactSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs dBool (false) |> Result.bind (fun emphasis ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dReq "value" __fs decTextSource |> Result.bind (fun value ->
    Ok { Emphasis = emphasis; Help = help; Icon = icon; Label = label; Tone = tone; Value = value })))))))

and private decFileUploadSpec (j: JVal) : Result<FileUploadSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "accept" __fs (dList dStr) |> Result.bind (fun accept ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "multiple" __fs dBool |> Result.bind (fun multiple ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: Fuaran.UI.HostPrelude.FileSelection list) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    dDef "acceptPaste" __fs dBool (false) |> Result.bind (fun acceptPaste ->
    dDef "dropTarget" __fs dBool (false) |> Result.bind (fun dropTarget ->
    dOpt "capture" __fs decCaptureSource |> Result.bind (fun capture ->
    Ok { Accept = accept; Label = label; Multiple = multiple; OnSelect = onSelect; Disabled = disabled; AcceptPaste = acceptPaste; DropTarget = dropTarget; Capture = capture })))))))))

and private decFiltersSpec (j: JVal) : Result<FiltersSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "items" __fs (dList decFilterSpec) |> Result.bind (fun items ->
    Ok { Items = items }))

and private decFormSpec (j: JVal) : Result<FormSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "fields" __fs (dList decFormField) |> Result.bind (fun fields ->
    dReq "onSubmit" __fs decAction |> Result.bind (fun onSubmit ->
    dReq "submitLabel" __fs decTextSource |> Result.bind (fun submitLabel ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    Ok { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = disabled })))))

and private decFragmentDeclSpec (j: JVal) : Result<FragmentDeclSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "body" __fs decNode |> Result.bind (fun body ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dOpt "holes" __fs (dList decHoleDecl) |> Result.bind (fun holes ->
    dOpt "effect" __fs decEffectClass |> Result.bind (fun effect ->
    Ok { Body = body; Name = name; Holes = holes; Effect = effect })))))

and private decFragmentRefSpec (j: JVal) : Result<FragmentRefSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dOpt "args" __fs (dMap decFragmentArg) |> Result.bind (fun args ->
    Ok { Name = name; Args = args })))

and private decHeadingSpec (j: JVal) : Result<HeadingSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "level" __fs dInt |> Result.bind (fun level ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    dReq "variant" __fs decHeadingVariant |> Result.bind (fun variant ->
    Ok { Level = level; Text = text; Variant = variant }))))

// Phase 821 — Icon display kind.
and private decIconSpec (j: JVal) : Result<IconSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "icon" __fs dStr |> Result.bind (fun icon ->
    dDef "size" __fs decIconSize (IconSize.Medium) |> Result.bind (fun size ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "label" __fs dStr |> Result.bind (fun label ->
    Ok { Icon = icon; Size = size; Tone = tone; Label = label })))))

and private decImageSpec (j: JVal) : Result<ImageSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "alt" __fs decTextSource |> Result.bind (fun alt ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dReq "variant" __fs decImageVariant |> Result.bind (fun variant ->
    dDef "fit" __fs decImageFit (ImageFit.Natural) |> Result.bind (fun fit ->
    dDef "aspectRatio" __fs decImageAspect (ImageAspect.Natural) |> Result.bind (fun aspectRatio ->
    dDef "loading" __fs decImageLoading (ImageLoading.Eager) |> Result.bind (fun loading ->
    dDef "srcSet" __fs (dList decSrcSetEntry) ([]) |> Result.bind (fun srcSet ->
    dDef "expandable" __fs dBool (false) |> Result.bind (fun expandable ->
    dOpt "caption" __fs decTextSource |> Result.bind (fun caption ->
    Ok { Alt = alt; Src = src; Variant = variant; Fit = fit; AspectRatio = aspectRatio; Loading = loading; SrcSet = srcSet; Expandable = expandable; Caption = caption }))))))))))

and private decLabelValueRowSpec (j: JVal) : Result<LabelValueRowSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "emphasis" __fs dBool (false) |> Result.bind (fun emphasis ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
    dOpt "help" __fs decTextSource |> Result.bind (fun help ->
    Ok { Emphasis = emphasis; Format = format; Label = label; Value = value; Help = help }))))))

and private decLinkSpec (j: JVal) : Result<LinkSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "href" __fs (decBinding dStr) |> Result.bind (fun href ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "download" __fs dBool |> Result.bind (fun download ->
    dOpt "rel" __fs dStr |> Result.bind (fun rel ->
    dOpt "target" __fs dStr |> Result.bind (fun target ->
    dOpt "protection" __fs decLinkProtection |> Result.bind (fun protection ->
    Ok { Href = href; Label = label; Download = download; Rel = rel; Target = target; Protection = protection })))))))

and private decListSpec (j: JVal) : Result<ListSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "items" __fs (dList decTextSource) |> Result.bind (fun items ->
    dReq "ordered" __fs dBool |> Result.bind (fun ordered ->
    Ok { Items = items; Ordered = ordered })))

and private decMapSpec (j: JVal) : Result<MapSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "centreLatitude" __fs dFloat |> Result.bind (fun centreLatitude ->
    dReq "centreLongitude" __fs dFloat |> Result.bind (fun centreLongitude ->
    dReq "source" __fs (decBinding (dList decMapMarker)) |> Result.bind (fun source ->
    dReq "zoom" __fs dInt |> Result.bind (fun zoom ->
    (dPresent "onMarkerClick" __fs |> Result.map (Option.map (fun () -> (fun (_: MapMarker) -> Action.Chain [])))) |> Result.bind (fun onMarkerClick ->
    Ok { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = onMarkerClick }))))))

and private decMarkdownSpec (j: JVal) : Result<MarkdownSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "text" __fs decTextSource |> Result.bind (fun text ->
    Ok { Text = text }))

and private decMathSpec (j: JVal) : Result<MathSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "source" __fs dStr |> Result.bind (fun source ->
    dReq "display" __fs decMathDisplay |> Result.bind (fun display ->
    Ok { Source = source; Display = display })))

and private decMediaSpec (j: JVal) : Result<MediaSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "controls" __fs dBool (true) |> Result.bind (fun controls ->
    dReq "kind" __fs decMediaKind |> Result.bind (fun kind ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dDef "loop" __fs dBool (false) |> Result.bind (fun loop ->
    dReq "src" __fs (decBinding dStr) |> Result.bind (fun src ->
    dDef "tracks" __fs (dList decTrackEntry) ([]) |> Result.bind (fun tracks ->
    dOpt "transcript" __fs decTextSource |> Result.bind (fun transcript ->
    Ok { Controls = controls; Kind = kind; Label = label; Loop = loop; Src = src; Tracks = tracks; Transcript = transcript }))))))))

and private decMetricSpec (j: JVal) : Result<MetricSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    dReq "value" __fs (decBinding dFloat) |> Result.bind (fun value ->
    dDef "format" __fs decCellFormat (CellFormat.None) |> Result.bind (fun format ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dDef "weight" __fs decStyleWeight (StyleWeight.Standard) |> Result.bind (fun weight ->
    dDef "emphasis" __fs decEmphasis (Emphasis.Normal) |> Result.bind (fun emphasis ->
    dOpt "trend" __fs (decBinding dFloat) |> Result.bind (fun trend ->
    dOpt "trendFormat" __fs decCellFormat |> Result.bind (fun trendFormat ->
    dDef "trendPolarity" __fs decTrendPolarity (TrendPolarity.HigherIsBetter) |> Result.bind (fun trendPolarity ->
    dOpt "icon" __fs dStr |> Result.bind (fun icon ->
    dOpt "subtext" __fs decTextSource |> Result.bind (fun subtext ->
    Ok { Label = label; Value = value; Format = format; Tone = tone; Weight = weight; Emphasis = emphasis; Trend = trend; TrendFormat = trendFormat; TrendPolarity = trendPolarity; Icon = icon; Subtext = subtext }))))))))))))

and private decModalSpec (j: JVal) : Result<ModalSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "dismissable" __fs dBool |> Result.bind (fun dismissable ->
    dOpt "onDismiss" __fs decAction |> Result.bind (fun onDismiss ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    dDef "modality" __fs decModalityKind (ModalityKind.Modal) |> Result.bind (fun modality ->
    dOpt "anchor" __fs dStr |> Result.bind (fun anchor ->
    Ok { Children = children; Dismissable = dismissable; OnDismiss = onDismiss; Open = ``open``; Heading = heading; Modality = modality; Anchor = anchor }))))))))

and private decMountSpec (j: JVal) : Result<MountSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "capabilities" __fs (dList dStr) |> Result.bind (fun capabilities ->
    dReq "channel" __fs decGuestChannel |> Result.bind (fun channel ->
    dOpt "inputs" __fs (dMap decFragmentArg) |> Result.bind (fun inputs ->
    (dPresent "onBubble" __fs |> Result.map (Option.map (fun () -> (fun (_: obj) -> Action.Chain [])))) |> Result.bind (fun onBubble ->
    dReq "scopeId" __fs dStr |> Result.bind (fun scopeId ->
    Ok { Capabilities = capabilities; Channel = channel; Inputs = inputs; OnBubble = onBubble; ScopeId = scopeId }))))))

and private decProgressSpec (j: JVal) : Result<ProgressSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "fraction" __fs (decBinding dFloat) |> Result.bind (fun fraction ->
    dDef "indeterminate" __fs dBool (false) |> Result.bind (fun indeterminate ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    dOpt "label" __fs decTextSource |> Result.bind (fun label ->
    dOpt "caveat" __fs decTextSource |> Result.bind (fun caveat ->
    Ok { Fraction = fraction; Indeterminate = indeterminate; Tone = tone; Label = label; Caveat = caveat }))))))

and private decScrollAreaSpec (j: JVal) : Result<ScrollAreaSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "orientation" __fs decScrollOrientation |> Result.bind (fun orientation ->
    dOpt "maxHeight" __fs dInt |> Result.bind (fun maxHeight ->
    dOpt "maxWidth" __fs dInt |> Result.bind (fun maxWidth ->
    Ok { Children = children; Orientation = orientation; MaxHeight = maxHeight; MaxWidth = maxWidth })))))

and private decSelectSpec (j: JVal) : Result<SelectSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs decTextSource |> Result.bind (fun label ->
    (dPresent "onChange" __fs |> Result.map (Option.map (fun () -> (fun (_: string option) -> Action.Chain [])))) |> Result.bind (fun onChange ->
    (dPresent "onChangeMulti" __fs |> Result.map (Option.map (fun () -> (fun (_: string list) -> Action.Chain [])))) |> Result.bind (fun onChangeMulti ->
    dReq "source" __fs (decBinding (dList decSelectOption)) |> Result.bind (fun source ->
    dReq "value" __fs (decBinding dStr) |> Result.bind (fun value ->
    dOpt "placeholder" __fs decTextSource |> Result.bind (fun placeholder ->
    dOpt "disabled" __fs (decBinding dBool) |> Result.bind (fun disabled ->
    dOpt "multiple" __fs dBool |> Result.bind (fun multiple ->
    dOpt "values" __fs (decBinding (dList dStr)) |> Result.bind (fun values ->
    Ok { Label = label; OnChange = onChange; OnChangeMulti = onChangeMulti; Source = source; Value = value; Placeholder = placeholder; Disabled = disabled; Multiple = multiple; Values = values }))))))))))

and private decSkeletonSpec (j: JVal) : Result<SkeletonSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "rows" __fs dInt |> Result.bind (fun rows ->
    Ok { Rows = rows }))

and private decSparklineSpec (j: JVal) : Result<SparklineSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "source" __fs (decBinding (dList dFloat)) |> Result.bind (fun source ->
    Ok { Source = source }))

and private decSplitPanelSpec (j: JVal) : Result<SplitPanelSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dReq "weight" __fs dFloat |> Result.bind (fun weight ->
    Ok { Children = children; Weight = weight })))

and private decStepperSpec (j: JVal) : Result<StepperSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "activeStep" __fs (decBinding dInt) |> Result.bind (fun activeStep ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: int) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    Ok { ActiveStep = activeStep; Children = children; OnSelect = onSelect }))))

and private decSummaryListSpec (j: JVal) : Result<SummaryListSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "heading" __fs decTextSource |> Result.bind (fun heading ->
    Ok { Children = children; Heading = heading })))

and private decSwitchSpec (j: JVal) : Result<SwitchSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "cases" __fs (dList decSwitchCase) |> Result.bind (fun cases ->
    dReq "default" __fs decNode |> Result.bind (fun ``default`` ->
    // Phase 768 — `on` (any Binding) or the compact `stateKey` (State form).
    // When both are absent the stateKey requirement carries the MISSING_FIELD,
    // keeping the existing reject fixture's error byte-identical.
    (match dOpt "on" __fs (decBinding dStr) with
     | Ok (Some on) -> Ok on
     | Ok None -> dReq "stateKey" __fs dStr |> Result.map (fun key -> Binding.State(key, None))
     | Error e -> Error e) |> Result.bind (fun on ->
    // Phase 1122 — `autoAdvanceMs` is optional, and a PRESENT value must be a
    // positive integer. `0` and negatives are refused rather than read as
    // "off", on the `Masonry.cols` ruling: absence is already the spelling of
    // off, so a rewrite would make two spellings mean one thing and hide the
    // emitter's misunderstanding of the slot.
    (match dOpt "autoAdvanceMs" __fs dInt with
     | Ok (Some ms) when ms > 0 -> Ok (Some ms)
     | Ok (Some _) -> Error "autoAdvanceMs must be a positive integer"
     | Ok None -> Ok None
     | Error e -> Error e) |> Result.bind (fun autoAdvanceMs ->
    Ok { Cases = cases; Default = ``default``; On = on; AutoAdvanceMs = autoAdvanceMs })))))

and private decTabsSpec (j: JVal) : Result<TabsSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dReq "activeIndex" __fs (decBinding dInt) |> Result.bind (fun activeIndex ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dDef "orientation" __fs decOrientation (Orientation.Horizontal) |> Result.bind (fun orientation ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: int) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    (dPresent "onSelectTag" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> Action.Chain [])))) |> Result.bind (fun onSelectTag ->
    dOpt "tabHeaders" __fs (dList decTabHeader) |> Result.bind (fun tabHeaders ->
    dOpt "tabTags" __fs (dList dStr) |> Result.bind (fun tabTags ->
    dOpt "activeTag" __fs (decBinding dStr) |> Result.bind (fun activeTag ->
    Ok { ActiveIndex = activeIndex; Children = children; Orientation = orientation; OnSelect = onSelect; OnSelectTag = onSelectTag; TabHeaders = tabHeaders; TabTags = tabTags; ActiveTag = activeTag })))))))))

and private decToastSpec (j: JVal) : Result<ToastSpec, string> =
    dObj j |> Result.bind (fun __fs ->
    dDef "dismissable" __fs dBool (true) |> Result.bind (fun dismissable ->
    dReq "message" __fs decTextSource |> Result.bind (fun message ->
    dReq "open" __fs (decBinding dBool) |> Result.bind (fun ``open`` ->
    dDef "tone" __fs decToneVariant (ToneVariant.Default) |> Result.bind (fun tone ->
    Ok { Dismissable = dismissable; Message = message; Open = ``open``; Tone = tone })))))

and private decTreeSpec (j: JVal) : Result<TreeSpec<obj>, string> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "expandedStateKey" __fs dStr |> Result.bind (fun expandedStateKey ->
    dReq "items" __fs (dList decTreeItem) |> Result.bind (fun items ->
    (dPresent "onSelect" __fs |> Result.map (Option.map (fun () -> (fun (_: string) -> Action.Chain [])))) |> Result.bind (fun onSelect ->
    dOpt "selectionStateKey" __fs dStr |> Result.bind (fun selectionStateKey ->
    Ok { ExpandedStateKey = expandedStateKey; Items = items; OnSelect = onSelect; SelectionStateKey = selectionStateKey })))))

// Phase 818 — the Transform source slot. A `$type` of State / Selection / Query preserves the binding as
// `TransformSource.Live` with the initial snapshot derived from its carried
// default data (`Fuaran.UI.HostPrelude.TransformLive`). Every other shape
// decodes through Core's columnar codec unchanged.
//
// Phase 1085 — a State source carrying NO data decodes to a live source over
// the EMPTY initial snapshot, exactly as a Selection / Query source already
// did. It used to be refused through the columnar codec (the Phase-815
// posture, correct when nothing could fill the slot), which made the most
// direct way to say "I read this key and carry no data of my own"
// unspellable: under the Phase-1075 seeding rule a SIBLING reader's
// declaration fills the slot, and FUARAN106's own remedy text tells an author
// to write precisely this shape. `"defaultValue": []` remains legal and means
// the same thing; the bare form is no longer a second answer to one question.
and private decTransformSource (j: JVal) : Result<TransformSource, string> =
    let asData (v: JVal) : Result<TransformSource, string> =
        Fuaran.Core.ColumnCodec.decodeJson v |> Result.map TransformSource.Data |> Result.mapError string

    match j with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = "$type") with
        | Some(_, JStr(("State" | "Selection" | "Query") as tag)) ->
            decBinding dJson j |> Result.bind (fun b ->
                let carried =
                    match b with
                    | Binding.State(_, dv) -> dv
                    | Binding.Selection(_, _, dv, _) -> dv
                    | _ -> None

                match carried, tag with
                | Some data, "State" ->
                    // A State source's carried data IS the initial snapshot —
                    // it must decode as a table (the Phase-815 posture).
                    Fuaran.UI.HostPrelude.TransformLive.initialSource data
                    |> Result.map (fun initial -> TransformSource.Live(b, initial))
                    |> Result.mapError Fuaran.Core.ColumnCodec.errorString
                | Some data, _ ->
                    // A Selection default may legitimately be a scalar / row
                    // shape rather than a table; fall back to the empty
                    // initial (the runtime evaluation stays loud on mismatch).
                    match Fuaran.UI.HostPrelude.TransformLive.initialSource data with
                    | Ok initial -> Ok(TransformSource.Live(b, initial))
                    | Error _ -> Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource))
                // Phase 1085 — no carried data, on ANY of the three tags: the
                // binding is preserved live over the empty initial snapshot.
                // The State arm used to fall through to `asData` here.
                | None, _ -> Ok(TransformSource.Live(b, Fuaran.UI.HostPrelude.TransformLive.emptySource)))
        | _ -> asData j
    | _ -> asData j

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
let decodeNode (s: string) : Result<Node<obj>, string> =
    Json.parse s |> Result.bind decNode

let private witnessKindTag (n: Node<'Msg>) : string =
    match n.Kind with
    | NodeKind.Badge _ -> "Badge"
    | NodeKind.Box _ -> "Box"
    | NodeKind.Button _ -> "Button"
    | NodeKind.Callout _ -> "Callout"
    | NodeKind.Chart _ -> "Chart"
    | NodeKind.CodeBlock _ -> "CodeBlock"
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.DataGrid _ -> "DataGrid"
    | NodeKind.Disclosure _ -> "Disclosure"
    | NodeKind.Drawing _ -> "Drawing"
    | NodeKind.Embed _ -> "Embed"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Fact _ -> "Fact"
    | NodeKind.FileUpload _ -> "FileUpload"
    | NodeKind.Filters _ -> "Filters"
    | NodeKind.Form _ -> "Form"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Heading _ -> "Heading"
    | NodeKind.Icon _ -> "Icon"
    | NodeKind.Image _ -> "Image"
    | NodeKind.LabelValueRow _ -> "LabelValueRow"
    | NodeKind.Link _ -> "Link"
    | NodeKind.List _ -> "List"
    | NodeKind.Map _ -> "Map"
    | NodeKind.Markdown _ -> "Markdown"
    | NodeKind.Math _ -> "Math"
    | NodeKind.Media _ -> "Media"
    | NodeKind.Metric _ -> "Metric"
    | NodeKind.Modal _ -> "Modal"
    | NodeKind.Mount _ -> "Mount"
    | NodeKind.Progress _ -> "Progress"
    | NodeKind.ScrollArea _ -> "ScrollArea"
    | NodeKind.Select _ -> "Select"
    | NodeKind.Skeleton _ -> "Skeleton"
    | NodeKind.Sparkline _ -> "Sparkline"
    | NodeKind.SplitPanel _ -> "SplitPanel"
    | NodeKind.Stepper _ -> "Stepper"
    | NodeKind.SummaryList _ -> "SummaryList"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.Tabs _ -> "Tabs"
    | NodeKind.Toast _ -> "Toast"
    | NodeKind.Tree _ -> "Tree"

let private witnessChildren (n: Node<'Msg>) : Node<'Msg> list =
    match n.Kind with
    | NodeKind.Box s -> s.Children
    | NodeKind.Disclosure s -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child ] @ [ s.Fallback ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | NodeKind.Modal s -> s.Children
    | NodeKind.ScrollArea s -> s.Children
    | NodeKind.SplitPanel s -> s.Children
    | NodeKind.Stepper s -> s.Children
    | NodeKind.SummaryList s -> s.Children
    | NodeKind.Switch s -> [ s.Default ]
    | NodeKind.Tabs s -> s.Children
    | _ -> []

let private witnessReplaceChildren (n: Node<'Msg>) (kids: Node<'Msg> list) : Node<'Msg> =
    match n.Kind with
    | NodeKind.Box s -> { n with Kind = NodeKind.Box { s with Children = kids } }
    | NodeKind.Disclosure s -> { n with Kind = NodeKind.Disclosure { s with Children = kids } }
    | NodeKind.ErrorBoundary s -> { n with Kind = NodeKind.ErrorBoundary { s with Child = List.item 0 kids; Fallback = List.item 1 kids } }
    | NodeKind.FragmentDecl s -> { n with Kind = NodeKind.FragmentDecl { s with Body = List.head kids } }
    | NodeKind.Modal s -> { n with Kind = NodeKind.Modal { s with Children = kids } }
    | NodeKind.ScrollArea s -> { n with Kind = NodeKind.ScrollArea { s with Children = kids } }
    | NodeKind.SplitPanel s -> { n with Kind = NodeKind.SplitPanel { s with Children = kids } }
    | NodeKind.Stepper s -> { n with Kind = NodeKind.Stepper { s with Children = kids } }
    | NodeKind.SummaryList s -> { n with Kind = NodeKind.SummaryList { s with Children = kids } }
    | NodeKind.Switch s -> { n with Kind = NodeKind.Switch { s with Default = List.head kids } }
    | NodeKind.Tabs s -> { n with Kind = NodeKind.Tabs { s with Children = kids } }
    | _ -> n

let nodeWitness: NodeWitness<Node<'Msg>, string> =
    { Id = fun n -> n.Id
      KindTag = witnessKindTag
      Children = witnessChildren
      ReplaceChildren = witnessReplaceChildren }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node<'Msg>, string>) (root: Node<'Msg>) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkBadge (id: string) (label: TextSource) (variant: BadgeVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Badge { Label = label; Variant = variant }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkBox (id: string) (children: Node<'Msg> list) (layout: LayoutMode) (role: BoxRole) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Box { Children = children; Heading = None; Layout = layout; Role = role; KeepTogether = false; BreakBefore = false }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkButton (id: string) (label: TextSource) (onClick: Action<'Msg>) (variant: ButtonVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Button { Label = label; OnClick = onClick; Variant = variant; Icon = None; Tooltip = None; Disabled = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkCallout (id: string) (body: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Callout { Body = body; Dismissable = false; Tone = ToneVariant.Default; Heading = None; Icon = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkChart (id: string) (kind: ChartKind) (source: Binding<Fuaran.Core.Row seq>) (stacked: bool) (xField: string) (yFields: string list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Chart { Kind = kind; Source = source; Stacked = stacked; XField = xField; YFields = yFields; Title = None; ValueFormat = None; XTitle = None; YTitle = None; Subtitle = None; LegendPosition = None; DataLabels = None; XScale = None; OnPointClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkCodeBlock (id: string) (code: string) (copyable: bool) (highlightLines: int list) (language: string) (lineNumbers: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.CodeBlock { Code = code; Copyable = copyable; HighlightLines = highlightLines; Language = language; LineNumbers = lineNumbers }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkCustom (id: string) (moduleId: string) (componentId: string) (props: Map<string, JVal>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Custom { ModuleId = moduleId; ComponentId = componentId; Props = props; ContentHash = None; ExposedNodeIds = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkDataGrid (id: string) (columns: ColumnErased<'Msg> list) (source: Binding<Fuaran.Core.Row seq>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.DataGrid { Columns = columns; Editable = false; RowKey = None; RowKeyField = None; SortStateKey = None; PageSize = None; PageStateKey = None; DefaultSort = None; EditStateKey = None; Reorderable = false; TransferInKey = None; TransferOutKey = None; KeepRowsTogether = false; RepeatHeader = false; Exportable = false; Source = source; StaticRows = None; OnRowClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkDisclosure (id: string) (children: Node<'Msg> list) (defaultOpen: bool) (heading: TextSource) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Disclosure { Children = children; DefaultOpen = defaultOpen; Heading = heading; OnToggle = None; Open = ``open`` }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkDrawing (id: string) (shapes: Shape list) (style: DrawStyle) (viewBox: ViewBox) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Drawing { Description = None; Shapes = shapes; Style = style; Title = None; ViewBox = viewBox }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkEmbed (id: string) (src: Binding<string>) (title: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Embed { AspectRatio = ImageAspect.Natural; Permissions = []; Src = src; Title = title }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkErrorBoundary (id: string) (child: Node<'Msg>) (fallback: Node<'Msg>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.ErrorBoundary { Child = child; Fallback = fallback }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkFact (id: string) (label: TextSource) (value: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Fact { Emphasis = false; Help = None; Icon = None; Label = label; Tone = ToneVariant.Default; Value = value }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkFileUpload (id: string) (accept: string list) (label: TextSource) (multiple: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FileUpload { Accept = accept; Label = label; Multiple = multiple; OnSelect = None; Disabled = None; AcceptPaste = false; DropTarget = false; Capture = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkFilters (id: string) (items: FilterSpec<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Filters { Items = items }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkForm (id: string) (fields: FormField<'Msg> list) (onSubmit: Action<'Msg>) (submitLabel: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Form { Fields = fields; OnSubmit = onSubmit; SubmitLabel = submitLabel; Disabled = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkFragmentDecl (id: string) (body: Node<'Msg>) (name: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FragmentDecl { Body = body; Name = name; Holes = None; Effect = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkFragmentRef (id: string) (name: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.FragmentRef { Name = name; Args = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkHeading (id: string) (level: int) (text: TextSource) (variant: HeadingVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Heading { Level = level; Text = text; Variant = variant }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkIcon (id: string) (icon: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Icon { Icon = icon; Size = IconSize.Medium; Tone = ToneVariant.Default; Label = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkImage (id: string) (alt: TextSource) (src: Binding<string>) (variant: ImageVariant) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Image { Alt = alt; Src = src; Variant = variant; Fit = ImageFit.Natural; AspectRatio = ImageAspect.Natural; Loading = ImageLoading.Eager; SrcSet = []; Expandable = false; Caption = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkLabelValueRow (id: string) (label: TextSource) (value: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.LabelValueRow { Emphasis = false; Format = CellFormat.None; Label = label; Value = value; Help = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkLink (id: string) (href: Binding<string>) (label: TextSource) (download: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Link { Href = href; Label = label; Download = download; Rel = None; Target = None; Protection = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkList (id: string) (items: TextSource list) (ordered: bool) : Node<'Msg> =
    { Id = id; Kind = NodeKind.List { Items = items; Ordered = ordered }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMap (id: string) (centreLatitude: float) (centreLongitude: float) (source: Binding<MapMarker list>) (zoom: int) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Map { CentreLatitude = centreLatitude; CentreLongitude = centreLongitude; Source = source; Zoom = zoom; OnMarkerClick = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMarkdown (id: string) (text: TextSource) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Markdown { Text = text }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMath (id: string) (source: string) (display: MathDisplay) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Math { Source = source; Display = display }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMedia (id: string) (kind: MediaKind) (label: TextSource) (src: Binding<string>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Media { Controls = true; Kind = kind; Label = label; Loop = false; Src = src; Tracks = []; Transcript = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMetric (id: string) (label: TextSource) (value: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Metric { Label = label; Value = value; Format = CellFormat.None; Tone = ToneVariant.Default; Weight = StyleWeight.Standard; Emphasis = Emphasis.Normal; Trend = None; TrendFormat = None; TrendPolarity = TrendPolarity.HigherIsBetter; Icon = None; Subtext = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkModal (id: string) (children: Node<'Msg> list) (dismissable: bool) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Modal { Children = children; Dismissable = dismissable; OnDismiss = None; Open = ``open``; Heading = None; Modality = ModalityKind.Modal; Anchor = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkMount (id: string) (capabilities: string list) (channel: GuestChannel) (scopeId: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Mount { Capabilities = capabilities; Channel = channel; Inputs = None; OnBubble = None; ScopeId = scopeId }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkProgress (id: string) (fraction: Binding<float>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Progress { Fraction = fraction; Indeterminate = false; Tone = ToneVariant.Default; Label = None; Caveat = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkScrollArea (id: string) (children: Node<'Msg> list) (orientation: ScrollOrientation) : Node<'Msg> =
    { Id = id; Kind = NodeKind.ScrollArea { Children = children; Orientation = orientation; MaxHeight = None; MaxWidth = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSelect (id: string) (label: TextSource) (source: Binding<SelectOption list>) (value: Binding<string>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Select { Label = label; OnChange = None; OnChangeMulti = None; Source = source; Value = value; Placeholder = None; Disabled = None; Multiple = None; Values = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSkeleton (id: string) (rows: int) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Skeleton { Rows = rows }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSparkline (id: string) (source: Binding<float list>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Sparkline { Source = source }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSplitPanel (id: string) (children: Node<'Msg> list) (weight: float) : Node<'Msg> =
    { Id = id; Kind = NodeKind.SplitPanel { Children = children; Weight = weight }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkStepper (id: string) (activeStep: Binding<int>) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Stepper { ActiveStep = activeStep; Children = children; OnSelect = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSummaryList (id: string) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.SummaryList { Children = children; Heading = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkSwitch (id: string) (cases: SwitchCase<'Msg> list) (``default``: Node<'Msg>) (stateKey: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Switch { Cases = cases; Default = ``default``; On = Binding.State(stateKey, None); AutoAdvanceMs = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkTabs (id: string) (activeIndex: Binding<int>) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Tabs { ActiveIndex = activeIndex; Children = children; Orientation = Orientation.Horizontal; OnSelect = None; OnSelectTag = None; TabHeaders = None; TabTags = None; ActiveTag = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkToast (id: string) (message: TextSource) (``open``: Binding<bool>) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Toast { Dismissable = true; Message = message; Open = ``open``; Tone = ToneVariant.Default }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }

let mkTree (id: string) (items: TreeItem list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Tree { ExpandedStateKey = None; Items = items; OnSelect = None; SelectionStateKey = None }; Accessibility = None; ExtraAttributes = None; Motion = None; State = None; Style = None; Tooltip = None }